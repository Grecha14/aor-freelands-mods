using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Every step under a load costs breath, and a running step twice.
    ///
    /// Игра берёт выносливость за удары, блоки и рывки, а ходьба ей ничего не стоит, сколько
    /// бы на человеке ни было железа. Здесь каждый пройденный метр стоит сотую долю очка
    /// выносливости за каждый килограмм, что человек несёт, — надетое и сумка вместе, — а бегом
    /// вдвое больше. Латник в шестьдесят килограммов проходит шагом метр за шесть десятых
    /// очка, бегом — за одно и две десятых; налегке почти не устаёт вовсе.
    ///
    /// Считается по пройденному, а не по времени: и в живом бою, и в пошаговом, где каждый ход
    /// — свой путь.
    /// </summary>
    internal static class Stride
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> PerKilo;
        internal static ConfigEntry<float> Running;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Stride", "Enabled", true,
                "Let every step cost stamina by what is carried, and a running step twice.");

            PerKilo = config.Bind("Stride", "PerKilo", 0.01f,
                new ConfigDescription(
                    "How much stamina one metre walked costs for every kilogram carried, worn and "
                    + "stowed together.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Running = config.Bind("Stride", "Running", 2f,
                new ConfigDescription(
                    "How many times dearer a running step is than a walking one.",
                    new AcceptableValueRange<float>(1f, 10f)));
        }

        private sealed class Last
        {
            internal Vector3 at;
            internal bool known;
        }

        private static readonly ConditionalWeakTable<UnitAttribute, Last> seen =
            new ConditionalWeakTable<UnitAttribute, Last>();

        /// <summary>What a metre costs this man right now, walking or running.</summary>
        internal static float Metre(HumaniodUnit man)
        {
            if (Enabled == null || !Enabled.Value || man == null || !Burden.Ready(man)) return 0f;

            float load = Burden.Load(man);
            if (load <= 0f) return 0f;

            float cost = load * PerKilo.Value;
            if (man.moveSpeedMD > 0.75f) cost *= Mathf.Max(1f, Running.Value);

            return cost;
        }

        /// <summary>Charges for the ground covered since the last look.</summary>
        internal static void Step(UnitAttribute who)
        {
            if (Enabled == null || !Enabled.Value || who == null || who.Data == null) return;

            HumaniodUnit man = who as HumaniodUnit;
            if (man == null) return;

            Last mine;
            if (!seen.TryGetValue(who, out mine))
            {
                mine = new Last();
                seen.Add(who, mine);
            }

            Vector3 now = who.transform.position;

            if (!mine.known || who.Data.isdead)
            {
                mine.at = now;
                mine.known = true;
                return;
            }

            Vector3 moved = now - mine.at;
            moved.y = 0f;
            mine.at = now;

            float metres = moved.magnitude;

            // Скачок — это перенос, а не шаг: смена местности, телепорт, загрузка.
            if (metres < 0.05f || metres > 30f) return;

            float cost = metres * Metre(man);
            if (cost <= 0f) return;

            CharacterSaveData data = (CharacterSaveData)(object)who.Data;
            float was = data.currentsp;
            if (was <= 0f) return;

            data.currentsp = Mathf.Max(0f, was - cost);
            who.onSPChange.Invoke(data.currentsp - was, who);
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "TickHPSPMP")]
    internal static class Tick_Stride_Patch
    {
        private static void Postfix(UnitAttribute __instance)
        {
            try { Stride.Step(__instance); } catch { }
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "EndTurn")]
    internal static class Turn_Stride_Patch
    {
        private static void Postfix(UnitAttribute __instance)
        {
            try { Stride.Step(__instance); } catch { }
        }
    }
}
