using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// A cleared lair stays cleared for a month or two, each by its own reckoning.
    ///
    /// Логово, откуда вычистили волков или нежить, игра заселяет заново через сутки-другие:
    /// так настроены её спавнеры. Здесь всякий спавнер зверей и чудовищ после заселения ждёт
    /// своего следующего раза от тридцати до шестидесяти суток — у каждого свой срок, и
    /// местности оживают вразнобой, а не все разом. Бандитов, путников и горожан это не
    /// касается.
    /// </summary>
    internal static class Lairs
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> LeastDays;
        internal static ConfigEntry<float> MostDays;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Lairs", "Enabled", true,
                "Let the places of beasts and monsters fill up again only after a month or two, "
                + "each place by its own draw.");

            LeastDays = config.Bind("Lairs", "LeastDays", 30f,
                new ConfigDescription("The soonest a lair fills again, in game days.",
                    new AcceptableValueRange<float>(1f, 365f)));

            MostDays = config.Bind("Lairs", "MostDays", 60f,
                new ConfigDescription("The latest, in game days.",
                    new AcceptableValueRange<float>(1f, 365f)));

            Telling = config.Bind("Lairs", "Telling", true,
                "Say in the log, for the first few, which lair was set to wait and how long.");
        }

        private const string Key = "ifLongWait";

        // Спавнеры, которым срок уже назначен нами: чтобы не удлинять его второй раз.
        internal static readonly HashSet<UnitSpawner> waiting = new HashSet<UnitSpawner>();
        private static int told;

        /// <summary>True when this spawner fills a place with beasts or monsters.</summary>
        internal static bool Monstrous(UnitSpawner spawner)
        {
            if (spawner == null || spawner.spawnsets == null || spawner.timeIsSecound) return false;
            if (spawner is CitytownNPCSpawner) return false;

            // Ночные — не логово, а ночь: они уходят с рассветом и возвращаются с темнотой.
            if (spawner.onlySpawnAtNight) return false;

            foreach (SpawnSet set in spawner.spawnsets)
            {
                if (set == null) continue;

                if (set.faction == Faction.monster || set.faction == Faction.wilding
                    || set.faction == Faction.neutralWilding) return true;

                UnitInfo who = set.spawnUnit;
                if (who == null) continue;

                if (who.race == UnitRace.undead || who.race == UnitRace.mythological
                    || who.race == UnitRace.animal || who.race == UnitRace.insect) return true;
            }

            return false;
        }

        /// <summary>Sets this lair to wait its own month or two.</summary>
        internal static void Wait(UnitSpawner spawner, string why)
        {
            float least = Mathf.Max(1f, LeastDays.Value);
            float most = Mathf.Max(least, MostDays.Value);

            float days = UnityEngine.Random.Range(least, most);
            spawner.spawnTimer = days * 24f;
            waiting.Add(spawner);

            if (Telling.Value && told < 15)
            {
                told++;
                ItemForgePlugin.Log.LogInfo($"Логово «{spawner.name}» ({why}): оживёт через {days:0} сут.");
            }
        }
    }

    // Заселили — назначаем следующий раз через месяц-два.
    [HarmonyPatch(typeof(UnitSpawner), "DoTrySpawn")]
    internal static class DoTrySpawn_Lairs_Patch
    {
        private static void Prefix(UnitSpawner __instance, out float __state)
        {
            __state = __instance != null ? __instance.spawnTimer : 0f;
        }

        private static void Postfix(UnitSpawner __instance, float __state)
        {
            if (Lairs.Enabled == null || !Lairs.Enabled.Value || __instance == null) return;

            try
            {
                // Игра только что заселила и поставила свой короткий срок.
                bool spawned = Mathf.Approximately(__instance.spawnTimer, __instance.spawnGap)
                    && !Mathf.Approximately(__state, __instance.spawnGap);
                if (!spawned || !Lairs.Monstrous(__instance)) return;

                Lairs.Wait(__instance, "заселено");
            }
            catch
            {
            }
        }
    }

    // Логово, которое ждёт по старому, короткому сроку, — переводим на долгий, однажды.
    [HarmonyPatch(typeof(UnitSpawner), "OnHourPassed")]
    internal static class OnHourPassed_Lairs_Patch
    {
        private static void Prefix(UnitSpawner __instance)
        {
            if (Lairs.Enabled == null || !Lairs.Enabled.Value || __instance == null) return;
            if (Lairs.waiting.Contains(__instance)) return;

            try
            {
                // Ещё ни разу не заселённое — ждёт своего первого раза, его не трогаем.
                if (!__instance.spawnedOnce) return;
                if (__instance.spawnTimer <= 0f || !Lairs.Monstrous(__instance)) return;
                Lairs.Wait(__instance, "ждало по-старому");
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(UnitSpawner), "SerializeData")]
    internal static class SerializeData_Lairs_Patch
    {
        private static void Postfix(UnitSpawner __instance, Dictionary<string, object> __result)
        {
            try
            {
                if (__result != null && Lairs.waiting.Contains(__instance)) __result["ifLongWait"] = true;
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(UnitSpawner), "DeserializeData")]
    internal static class DeserializeData_Lairs_Patch
    {
        private static void Postfix(UnitSpawner __instance, Dictionary<string, object> data)
        {
            try
            {
                object seen;
                if (data != null && data.TryGetValue("ifLongWait", out seen) && seen is bool && (bool)seen)
                {
                    Lairs.waiting.Add(__instance);
                }
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// In the arena nobody dies — unless the blow took his head.
    ///
    /// Гладиатор на арене падает и лежит: толпа решает, бой окончен, его уносят. Умирает
    /// только тот, кому отсекли голову, — это не решается ни жребием, ни толпой.
    /// </summary>
    internal static class Colosseum
    {
        internal static ConfigEntry<bool> Enabled;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Colosseum", "Enabled", true,
                "Nobody dies in the arena except the one whose head is cut off.");
        }

        internal static bool Fighting()
        {
            try
            {
                return ArenaManager.instance != null && ArenaManager.instance.isFighting;
            }
            catch
            {
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "Die")]
    [HarmonyPriority(Priority.First)]
    internal static class Die_Colosseum_Patch
    {
        private static void Prefix(UnitAttribute __instance, ref bool forceDie, out int __state)
        {
            __state = -1;

            try
            {
                if (Colosseum.Enabled == null || !Colosseum.Enabled.Value) return;
                if (__instance == null || __instance.Data == null || __instance.Data.isdead) return;
                if (!Colosseum.Fighting()) return;

                __state = __instance.Data.canKill ? 1 : 0;

                if (Sever.Beheads(__instance))
                {
                    // Голова с плеч — это смерть, чья бы она ни была.
                    __instance.Data.canKill = true;
                    forceDie = true;
                    return;
                }

                __instance.Data.canKill = false;
                forceDie = false;
            }
            catch
            {
                __state = -1;
            }
        }

        private static void Postfix(UnitAttribute __instance, int __state)
        {
            if (__state < 0 || __instance == null || __instance.Data == null) return;
            __instance.Data.canKill = __state == 1;
        }
    }
}
