using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace DemonLook
{
    /// <summary>
    /// Every town hands out errands the way the guild does, and all errands pay better.
    ///
    /// Своя доска заданий в игре есть не у всякого города. Здесь каждому городу, у которого её
    /// нет, заводится своя: те поручения гильдии, что не привязаны к месту, — шайки и звери
    /// рядом с городом, арест, возврат реликвии, спасение, сбор долга. Брать их можно и без
    /// ранга в гильдии.
    ///
    /// Чем больше город доверяет герою, тем больше и работа, и плата: каждое очко доверия — на
    /// сотую часть больше врагов и на сотую больше денег; к доверию 50 приходят поручения ранга
    /// B, к 75 — ранга A, к сотне — S. Сверх того всякое случайное задание, гильдейское и
    /// городское, платит вдвое. Уже взятые задания остаются как были: числа ставятся, когда
    /// задание рождается.
    /// </summary>
    internal static class Errands
    {
        internal static ConfigEntry<bool> TownBoards;
        internal static ConfigEntry<float> Pay;
        internal static ConfigEntry<float> PerTrust;
        internal static ConfigEntry<int> AtOnce;

        internal static void Bind(ConfigFile config)
        {
            TownBoards = config.Bind("Errands", "TownBoards", true,
                "Give every town without a guild board one of its own, with the guild errands that "
                + "are not tied to a place. Anyone may take them, with or without a guild rank.");

            Pay = config.Bind("Errands", "Pay", 2f,
                new ConfigDescription("What every random errand, guild or town, pays over the game.",
                    new AcceptableValueRange<float>(0.1f, 20f)));

            PerTrust = config.Bind("Errands", "PerTrust", 0.01f,
                new ConfigDescription("How much larger a town errand grows, in foes and in pay, for each point of the "
                    + "town's trust in the hero.",
                    new AcceptableValueRange<float>(0f, 0.1f)));

            AtOnce = config.Bind("Errands", "AtOnce", 3,
                new ConfigDescription("How many town errands a board holds at once.",
                    new AcceptableValueRange<int>(1, 10)));
        }

        internal const int FirstId = 7000;

        internal static bool Ours(RandomQuestSpawner s)
        {
            return s != null && s.spawnerId >= FirstId && s.spawnerId < FirstId + 1000;
        }

        private static bool Movable(RandomQuestSpawner s)
        {
            return s is RQKillSpawner || s is RQArrestSpawner || s is RQHeirloomSpawner
                || s is RQRescueSpawner || s is RQDebtSpawner || s is RQDungeonSpawner;
        }

        /// <summary>Boards for the towns that have none, made before the saved errands are read.</summary>
        internal static void Build(RandomQuestManager manager)
        {
            if (TownBoards == null || !TownBoards.Value || manager == null || manager.spawnerSets == null) return;

            FactionRandomQuestSpawnerSet guild = null;
            HashSet<Faction> have = new HashSet<Faction>();

            foreach (FactionRandomQuestSpawnerSet set in manager.spawnerSets)
            {
                if (set == null || set.faction != Faction.mercenaryGuild) continue;
                have.Add(set.spawnPlace);
                if (guild == null && set.mercenaryQuestSpawners != null && set.mercenaryQuestSpawners.Count > 0) guild = set;
            }

            if (guild == null) return;

            List<RandomQuestSpawner> sources = new List<RandomQuestSpawner>();
            foreach (RandomQuestSpawner s in guild.mercenaryQuestSpawners)
            {
                if (s != null && Movable(s) && !Ours(s)) sources.Add(s);
            }
            if (sources.Count == 0) return;

            int made = 0;
            foreach (Faction town in Enum.GetValues(typeof(Faction)))
            {
                if (!FactionManager.IsTownFaction(town) || have.Contains(town)) continue;

                FactionRandomQuestSpawnerSet board = new FactionRandomQuestSpawnerSet
                {
                    faction = Faction.mercenaryGuild,
                    spawnPlace = town,
                    useMercenaryLevelLimit = false,
                    questCountLimitBasic = 0,
                    questCountLimitMercenary = AtOnce.Value,
                    basicQuestSpawners = new List<RandomQuestSpawner>(),
                    mercenaryQuestSpawners = new List<RandomQuestSpawner>(),
                };

                for (int k = 0; k < sources.Count && k < 20; k++)
                {
                    RandomQuestSpawner clone = UnityEngine.Object.Instantiate(sources[k], manager.transform);
                    clone.name = "TownErrand_" + town + "_" + k;
                    clone.spawnerId = FirstId + (int)town * 20 + k;
                    clone.currentQuests = new List<RandomQuest>();

                    // Подземелья у каждого города свои: чужой список ему ни к чему, свои места ставит мод.
                    RQDungeonSpawner dungeon = clone as RQDungeonSpawner;
                    if (dungeon != null) dungeon.locationSet = new List<TargetLocationTemplate>();
                    board.mercenaryQuestSpawners.Add(clone);
                }

                board.Init();
                manager.spawnerSets.Add(board);
                made++;
            }

            if (made > 0) DemonLookPlugin.Log.LogInfo($"Задания: городских досок заведено {made}.");
        }

        /// <summary>The highest rank a town hands out at this trust.</summary>
        internal static MercenaryLevel Highest(int trust)
        {
            if (trust >= 100) return MercenaryLevel.S;
            if (trust >= 75) return MercenaryLevel.A;
            if (trust >= 50) return MercenaryLevel.B;
            return MercenaryLevel.C;
        }

        internal static int Trust(RandomQuestSpawner s)
        {
            try { return Mathf.Clamp(FactionManager.Instance.GetFavorToPlayer(s.SpawnPlace), 0, 100); }
            catch { return 0; }
        }

        /// <summary>The numbers of a fresh errand: pay for all, and for a town's own also its trust.</summary>
        internal static void Scale(RandomQuestSpawner s, RandomQuest quest)
        {
            if (s == null || quest == null) return;

            // Задание на шайку или стаю: врагов столько, сколько их есть на самом деле, и плата по ним.
            if (forcedMen > 0 && quest.randomValues != null && quest.randomValues.Count > 0)
            {
                int was = Mathf.Max(1, quest.randomValues[0]);
                quest.randomValues[0] = forcedMen;
                quest.reward = Mathf.RoundToInt(quest.reward * (float)forcedMen / was);
            }

            float grow = 1f;
            if (Ours(s) && forcedMen <= 0)
            {
                grow = 1f + PerTrust.Value * Trust(s);

                if (quest.randomValues != null && quest.randomValues.Count > 0 && quest.randomValues[0] > 0)
                    quest.randomValues[0] = Mathf.Max(1, Mathf.RoundToInt(quest.randomValues[0] * grow));
            }

            if (quest.reward > 0) quest.reward = Mathf.RoundToInt(quest.reward * Pay.Value * grow);
        }

        private static readonly Dictionary<RandomQuestSpawner, List<EnemyTravelGroupTemplate>> kept =
            new Dictionary<RandomQuestSpawner, List<EnemyTravelGroupTemplate>>();

        private static readonly Dictionary<Type, FieldInfo> setFields = new Dictionary<Type, FieldInfo>();

        private static FieldInfo SetField(Type t)
        {
            FieldInfo f;
            if (!setFields.TryGetValue(t, out f))
            {
                f = AccessTools.Field(t, "travelGroupSet");
                setFields[t] = f;
            }
            return f;
        }

        // Задание против определённого врага: шайки с дороги или стаи зверей.
        private static List<EnemyTravelGroupTemplate> forced;
        private static int forcedMen;

        private static bool Animal(UnitInfo u)
        {
            return u != null && u.utype.ToString() == "animal";
        }

        private static bool Match(EnemyTravelGroupTemplate t, bool bandits)
        {
            TravelGroupInfo g = t.travelGroupInfo;
            if (g == null) return false;
            if (bandits) return g.groupFaction == Faction.outlaw;
            if (Animal(g.leaderInfo)) return true;
            return g.unitInfos != null && g.unitInfos.Count > 0 && Animal(g.unitInfos[0]);
        }

        /// <summary>
        /// Puts a kill errand on the board against a particular foe, sized to how many they are.
        /// Null when the board has no such foe among its templates.
        /// </summary>
        internal static RandomQuest SpawnAgainst(RandomQuestSpawner s, bool bandits, int men)
        {
            FieldInfo field = SetField(s.GetType());
            List<EnemyTravelGroupTemplate> all = field != null ? field.GetValue(s) as List<EnemyTravelGroupTemplate> : null;
            if (all == null) return null;

            List<EnemyTravelGroupTemplate> pick = all.FindAll(t => t != null && !t.promote && Match(t, bandits));
            int taken = 0;
            foreach (RandomQuest q in s.CurrentQuests) if (q != null && !q.promote) taken++;
            if (pick.Count <= taken) return null;

            int before = s.CurrentQuests.Count;
            forced = pick;
            forcedMen = men;
            try { s.SpawnQuest(); }
            finally { forced = null; forcedMen = 0; }

            return s.CurrentQuests.Count > before ? s.CurrentQuests[s.CurrentQuests.Count - 1] : null;
        }

        /// <summary>A purge errand on one particular site of the town.</summary>
        internal static RandomQuest SpawnAtSite(RQDungeonSpawner s, string scene, string display)
        {
            if (s == null || string.IsNullOrEmpty(scene) || RandomQuestManager.instance == null) return null;
            if (RandomQuestManager.instance.FilterAreaUsed(RandomQuestType.minor_quest_dungeon).Contains(scene)) return null;

            TargetLocationTemplate site = new TargetLocationTemplate
            {
                targetLocation = scene,
                locationDisplayName = display ?? scene,
                mercenaryLevel = MercenaryLevel.C,
                spawnWeight = 100,
                additiveFavors = new SerializableDic<Faction, int>(),
                timeLimit = 72f,
            };

            List<TargetLocationTemplate> own = s.locationSet;
            int before = s.CurrentQuests.Count;
            s.locationSet = new List<TargetLocationTemplate> { site };
            try { s.SpawnQuest(); }
            finally { s.locationSet = own; }

            return s.CurrentQuests.Count > before ? s.CurrentQuests[s.CurrentQuests.Count - 1] : null;
        }

        /// <summary>
        /// Before a town board spawns: only the ranks its trust allows. False when none are left,
        /// so the game does not pick from an empty list.
        /// </summary>
        internal static bool Narrow(RandomQuestSpawner s)
        {
            if (forced != null)
            {
                FieldInfo f = SetField(s.GetType());
                List<EnemyTravelGroupTemplate> own = f != null ? f.GetValue(s) as List<EnemyTravelGroupTemplate> : null;
                if (own == null) return false;
                kept[s] = own;
                f.SetValue(s, forced);
                return true;
            }

            if (!Ours(s)) return true;

            FieldInfo field = SetField(s.GetType());
            List<EnemyTravelGroupTemplate> all = field != null ? field.GetValue(s) as List<EnemyTravelGroupTemplate> : null;
            if (all == null) return true;

            MercenaryLevel top = Highest(Trust(s));
            List<EnemyTravelGroupTemplate> allowed = all.FindAll(t => t != null && !t.promote && t.mercenaryLevel <= top);

            int taken = 0;
            foreach (RandomQuest q in s.CurrentQuests)
            {
                if (q != null && !q.promote) taken++;
            }
            if (allowed.Count <= taken) return false;

            kept[s] = all;
            field.SetValue(s, allowed);
            return true;
        }

        internal static void Widen(RandomQuestSpawner s)
        {
            List<EnemyTravelGroupTemplate> all;
            if (s == null || !kept.TryGetValue(s, out all)) return;

            kept.Remove(s);
            FieldInfo field = SetField(s.GetType());
            if (field != null) field.SetValue(s, all);
        }
    }

    [HarmonyPatch(typeof(RandomQuestManager), "Awake")]
    internal static class QuestAwake_Errands_Patch
    {
        private static void Postfix(RandomQuestManager __instance)
        {
            try { Errands.Build(__instance); }
            catch (Exception e) { DemonLookPlugin.Log.LogWarning("Задания: доски не завелись: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(RandomQuestSpawner), "SpawnQuest")]
    internal static class SpawnQuest_Errands_Patch
    {
        private static bool Prefix(RandomQuestSpawner __instance)
        {
            try { return Errands.Narrow(__instance); }
            catch { return true; }
        }

        private static Exception Finalizer(RandomQuestSpawner __instance, Exception __exception)
        {
            try { Errands.Widen(__instance); } catch { }
            return __exception;
        }
    }

    // Числа нового задания — сразу, как оно сложилось, до того как игра напишет его текст.
    [HarmonyPatch]
    internal static class RandomSet_Errands_Patch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (Type t in typeof(RandomQuestSpawner).Assembly.GetTypes())
            {
                if (t == null || t.IsAbstract || !typeof(RandomQuestSpawner).IsAssignableFrom(t)) continue;

                MethodInfo random = AccessTools.DeclaredMethod(t, "GetRandomSet", new[] { typeof(RandomQuest) });
                if (random != null) yield return random;

                MethodInfo promote = AccessTools.DeclaredMethod(t, "GetPromoteSet", new[] { typeof(RandomQuest), typeof(MercenaryLevel) });
                if (promote != null) yield return promote;
            }
        }

        private static void Postfix(RandomQuestSpawner __instance, RandomQuest quest)
        {
            try { Errands.Scale(__instance, quest); } catch { }
        }
    }

    // Кнопка заданий в меню города на карте — у всякого города, где героя не считают врагом.
    [HarmonyPatch(typeof(Assets.Data.Scripts.System.WorldTravel.WorldMapCityMenu), "UpdateButtons")]
    internal static class CityButtons_Errands_Patch
    {
        private static readonly AccessTools.FieldRef<Assets.Data.Scripts.System.WorldTravel.WorldMapCityMenu, WorldTown> town =
            AccessTools.FieldRefAccess<Assets.Data.Scripts.System.WorldTravel.WorldMapCityMenu, WorldTown>("city");

        private static void Postfix(Assets.Data.Scripts.System.WorldTravel.WorldMapCityMenu __instance)
        {
            try
            {
                WorldTown city = town(__instance);
                if (city == null) return;

                bool hostile = FactionManager.Instance.GetFavorDegree(city.faction) == FavorDegree.hostile;
                bool wanted = FactionManager.Instance.IsPlayerCriminal(city.faction);

                if (Errands.TownBoards != null && Errands.TownBoards.Value && FactionManager.IsTownFaction(city.faction)
                    && __instance.questBtn != null && !hostile && !wanted)
                    __instance.questBtn.gameObject.SetActive(true);

                if (Shutters.Closed() && __instance.venderBtn != null) __instance.venderBtn.gameObject.SetActive(false);
            }
            catch
            {
            }
        }
    }
}
