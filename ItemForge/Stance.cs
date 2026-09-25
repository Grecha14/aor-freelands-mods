using System;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Makes the aggressive stance mean what it says.
    ///
    /// В игре у каждого бойца есть стойка: наступать, отвечать, держать место, бежать. Но для
    /// того, кем вы играете, она ничего не решает. Решает другое — общий «автобой» в
    /// настройках. Проверка устроена так:
    ///
    ///     if ((unit != currentplayUnit || AutoCombat) | loseControl) ...
    ///
    /// То есть ваш персонаж сам врага не ищет никогда, пока не включён автобой на весь отряд
    /// разом. До стойки дело просто не доходит: её спрашивают уже внутри, за этой дверью.
    /// Отсюда и то, что вы видели, — стоит столбом посреди драки, а после приказа вернуться
    /// на место так же молча стоит.
    ///
    /// Здесь дверь открывается той же стойкой, ради которой она и заведена: выбрал
    /// «наступать» — ищет и бьёт ближайшего, сколько бы раз его ни отзывали назад. Выбрал
    /// «держать место» или «отвечать» — всё как было. Общий автобой при этом не нужен и не
    /// трогается: он остаётся отдельным выключателем на весь отряд.
    /// </summary>
    internal static class Stance
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> Party;

        private static MethodInfo engage;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Stance", "Enabled", true,
                "Let the aggressive stance send a man at the nearest enemy by itself. The game "
                + "asks about the stance only after it has already decided that your own "
                + "character never looks for a fight, so the stance never gets a say.");

            Party = config.Bind("Stance", "Party", true,
                "Apply this to companions too, not only to the one you are steering. Without "
                + "it a companion set to attack still waits until the whole party is engaged.");
        }

        /// <summary>True when this one should go looking for a fight on its own.</summary>
        internal static bool Eager(UnitAttribute who)
        {
            if (!Enabled.Value || who == null || who.Data == null) return false;
            if (who.Data.isdead || !who.Data.allowattack) return false;

            if (who.Data.engageStyle != EngageStyle.initiative) return false;

            bool mine = (object)who == (object)gameManager.currentplayUnit;

            if (!mine && !Party.Value) return false;
            if (!mine && !who.inParty && !who.isTempFollower) return false;

            return true;
        }

        /// <summary>Asks the game's own engage routine, which is hidden behind protection.</summary>
        internal static bool Engage(ConsistentState state)
        {
            try
            {
                if (engage == null)
                {
                    engage = AccessTools.Method(typeof(ConsistentState), "AutoFindTargetAndEngage");
                }

                if (engage == null) return false;

                return (bool)engage.Invoke(state, null);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог послать в бой: " + e.Message);
                return false;
            }
        }
    }

    // Та самая дверь. Пропускаем в неё по стойке, а не по общему выключателю; всем прочим
    // оставляем родное решение нетронутым.
    [HarmonyPatch(typeof(ConsistentState), "AutoFindTargetCheckPlayer")]
    internal static class AutoFindTargetCheckPlayer_Patch
    {
        private static bool Prefix(ConsistentState __instance, ref bool __result)
        {
            if (__instance == null || !Stance.Eager(__instance.unit)) return true;

            __result = Stance.Engage(__instance);
            return false;
        }
    }
}
