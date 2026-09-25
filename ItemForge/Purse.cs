using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Coin, since colour no longer pays for everything.
    ///
    /// Цветная вещь стала втрое реже, и добыча перестала кормить: прежде отряд жил продажей
    /// зелёного и синего, снятого с каждого второго, а теперь таких находок единицы. Монеты
    /// в игре лежат в двух местах — в сундуках («moneyRegain» у содержимого сундука) и в
    /// кошельках людей: шаблон кладёт свои, карьера — свои («commonCargo»), вожаку отряда —
    /// свои. Здесь каждое такое место получает больше: во сколько раз — бросается заново для
    /// каждого сундука и каждого кошелька, от двух до пяти.
    ///
    /// Лавки, деньги отряда и наши собственные сцены не трогаются: там число назначено, а не
    /// найдено.
    /// </summary>
    internal static class Purse
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Least;
        internal static ConfigEntry<float> Most;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Purse", "Enabled", true,
                "Put more coin into chests and into the purses of people you can fight. Coloured "
                + "things are three times rarer than they were, and the party used to live off "
                + "selling them.");

            Least = config.Bind("Purse", "Least", 2f,
                new ConfigDescription(
                    "The smallest multiple a chest or a purse is filled by.",
                    new AcceptableValueRange<float>(1f, 20f)));

            Most = config.Bind("Purse", "Most", 5f,
                new ConfigDescription(
                    "The largest. Each chest and each purse rolls its own multiple between the two.",
                    new AcceptableValueRange<float>(1f, 20f)));

            Telling = config.Bind("Purse", "Telling", true,
                "Say in the log, for the first twenty, what a chest or purse held and what it "
                + "holds now.");
        }

        private static int told;

        // Кому кошелёк уже наполнен: вожака игра может снабдить деньгами и после рождения.
        private static readonly HashSet<int> filled = new HashSet<int>();

        /// <summary>The multiple for this one chest or purse.</summary>
        internal static float Roll()
        {
            float least = Mathf.Max(1f, Least.Value);
            float most = Mathf.Max(least, Most.Value);

            return UnityEngine.Random.Range(least, most);
        }

        /// <summary>Whether this man's purse is one of those found rather than set.</summary>
        internal static bool Counts(UnitAttribute who)
        {
            if (Enabled == null || !Enabled.Value) return false;

            HumaniodUnit man = who as HumaniodUnit;
            if (man == null || man.Data == null || man.info == null || man.items == null) return false;

            if (man.inParty || man.Data.team == Faction.player) return false;

            UnitType kind = man.info.utype;
            if (kind == UnitType.keyNPC || kind == UnitType.companion || kind == UnitType.player)
            {
                return false;
            }

            // Кто торгует или служит, держит не кошелёк, а кассу.
            if (man.vender != null) return false;

            NPCSaveData mind = man.Data as NPCSaveData;
            if (mind != null)
            {
                switch (mind.career)
                {
                    case CareerType.Merchant:
                    case CareerType.Bartender:
                    case CareerType.Blacksmith:
                    case CareerType.Doctor:
                        return false;
                }
            }

            // Круг в пещере одет и оплачен по сцене: там число назначено.
            if (Ritual.Enabled.Value && Ritual.Here()) return false;

            return true;
        }

        /// <summary>Fills one man's purse, once.</summary>
        internal static void Fill(UnitAttribute who, string why)
        {
            if (!Counts(who)) return;
            if (!filled.Add(who.GetInstanceID())) return;

            try
            {
                int had = who.items.money;
                if (had <= 0) return;

                float much = Roll();
                who.items.money = Mathf.RoundToInt(had * much);

                if (Telling.Value && told < 20)
                {
                    told++;
                    ItemForgePlugin.Log.LogInfo($"Кошелёк «{who.Data.unitname}» ({why}): "
                        + $"{had} → {who.items.money} (×{much:0.0}).");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог наполнить кошелёк: " + e.Message);
            }
        }

        /// <summary>Fills a leader's purse again, after the game has written it anew.</summary>
        internal static void Refill(UnitAttribute who)
        {
            if (!Counts(who)) return;

            filled.Add(who.GetInstanceID());

            try
            {
                int had = who.items.money;
                if (had <= 0) return;

                float much = Roll();
                who.items.money = Mathf.RoundToInt(had * much);

                if (Telling.Value && told < 20)
                {
                    told++;
                    ItemForgePlugin.Log.LogInfo($"Кошелёк «{who.Data.unitname}» (вожак): "
                        + $"{had} → {who.items.money} (×{much:0.0}).");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог наполнить кошелёк вожака: " + e.Message);
            }
        }

        /// <summary>Adds to a chest what its own filling put into it, times the roll.</summary>
        internal static void Chest(Container box, int before)
        {
            if (Enabled == null || !Enabled.Value || box == null || box.items == null) return;

            try
            {
                int gained = box.items.money - before;
                if (gained <= 0) return;

                float much = Roll();
                int extra = Mathf.RoundToInt(gained * (much - 1f));
                box.items.money += extra;

                if (Telling.Value && told < 20)
                {
                    told++;
                    ItemForgePlugin.Log.LogInfo($"Сундук «{box.name}»: монет {gained} → "
                        + $"{gained + extra} (×{much:0.0}).");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог добавить монет в сундук: " + e.Message);
            }
        }
    }

    // Сундук наполняется здесь, и только сундук: лавки и мешки наполняются мимо этого метода.
    [HarmonyPatch(typeof(ContainerContent), "AddContentToContainer")]
    internal static class AddContentToContainer_Purse_Patch
    {
        private static void Prefix(Container container, out int __state)
        {
            __state = 0;

            try
            {
                if (container != null && container.items != null) __state = container.items.money;
            }
            catch
            {
            }
        }

        private static void Postfix(Container container, int __state)
        {
            Purse.Chest(container, __state);
        }
    }

    // Кошелёк человека складывается при рождении: сперва шаблон, потом карьера, которая
    // может его переписать. Берём то, что осталось в конце, — один раз.
    [HarmonyPatch(typeof(HumaniodUnit), "InitializeUnit")]
    internal static class InitializeUnit_Purse_Patch
    {
        private static void Prefix(HumaniodUnit __instance, out bool __state)
        {
            __state = false;

            try
            {
                __state = __instance != null && __instance.Data != null && !__instance.Data.unitInited;
            }
            catch
            {
            }
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(HumaniodUnit __instance, bool __state)
        {
            if (!__state) return;

            Purse.Fill(__instance, "при рождении");
        }
    }

    // Вожаку отряда игра кладёт деньги отдельно. Если он к тому времени уже родился, его
    // кошелёк наполнен выше и переписан заново — наполняем ещё раз, уже этот.
    [HarmonyPatch(typeof(TravelGroupSpawner), "SetLeaderReady")]
    internal static class SetLeaderReady_Purse_Patch
    {
        private static void Postfix(UnitAttribute leader)
        {
            try
            {
                if (leader == null || leader.Data == null || !leader.Data.unitInited) return;

                Purse.Refill(leader);
            }
            catch
            {
            }
        }
    }
}
