using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DemonLook
{
    /// <summary>
    /// Mines, forests and caves that feed a town, and the monsters that sit in them.
    ///
    /// За всяким городом числятся свои места: шахта даёт руду и камни, лесоповал — дерево,
    /// пещеры и болота — травы. Место, где засели монстры — гоблины в шахте, твари в пещере, —
    /// даёт втрое меньше, а деревня, что стоит к нему ближе всего, — вдвое меньше еды: чудища
    /// топчут её поля. Тогда город даёт задание зачистить место, и когда там перебиты все, кем
    /// бы ни было, добыча снова полная.
    ///
    /// Зачищенное место пустует тридцать–шестьдесят дней, у каждого свой срок, — потом монстры
    /// возвращаются. Кого не выбивают, те крепнут: каждую неделю следующее заселение на уровень
    /// сильнее, до десяти.
    /// </summary>
    internal static class Sites
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Infested;
        internal static ConfigEntry<float> VillageLoss;
        internal static ConfigEntry<int> ClearLow;
        internal static ConfigEntry<int> ClearHigh;
        internal static ConfigEntry<int> GrowthCap;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Sites", "Enabled", true,
                "Let the mines, forests and caves feed their towns, cut down by the monsters in them.");

            Infested = config.Bind("Sites", "Infested", 0.3f,
                new ConfigDescription("What share of its yield an infested site still gives.", new AcceptableValueRange<float>(0f, 1f)));

            VillageLoss = config.Bind("Sites", "VillageLoss", 0.5f,
                new ConfigDescription("What share of its food the village nearest an infested site loses.", new AcceptableValueRange<float>(0f, 1f)));

            ClearLow = config.Bind("Sites", "ClearLow", 30,
                new ConfigDescription("Fewest days a cleared site stays clear.", new AcceptableValueRange<int>(1, 365)));

            ClearHigh = config.Bind("Sites", "ClearHigh", 60,
                new ConfigDescription("Most days a cleared site stays clear.", new AcceptableValueRange<int>(1, 365)));

            GrowthCap = config.Bind("Sites", "GrowthCap", 10,
                new ConfigDescription("How many levels stronger the monsters of a site left alone can grow.", new AcceptableValueRange<int>(0, 50)));
        }

        internal static bool On()
        {
            return Enabled != null && Enabled.Value;
        }

        internal enum Kind { None, Ore, Wood, Herbs }

        internal class Site
        {
            internal string scene;
            internal string name;
            internal string owner;
            internal string village;
            internal Kind kind;
            internal int clearUntil = -1;
            internal int infestedSince;
            internal int quest = -1;

            internal bool Clear(int today)
            {
                return today < clearUntil;
            }

            internal int Growth(int today)
            {
                if (Clear(today)) return 0;
                return Mathf.Clamp((today - infestedSince) / 7, 0, GrowthCap.Value);
            }
        }

        internal static readonly List<Site> sites = new List<Site>();

        private static Kind KindOf(string s)
        {
            s = (s ?? "").ToLowerInvariant();
            if (s.Contains("mine") || s.Contains("goblincave") || s.Contains("dwarf") || s.Contains("quarry")) return Kind.Ore;
            if (s.Contains("logging") || s.Contains("lumber")) return Kind.Wood;
            if (s.Contains("tomb") || s.Contains("crypt") || s.Contains("prison") || s.Contains("ruin") || s.Contains("temple")
                || s.Contains("maze") || s.Contains("tower") || s.Contains("arena") || s.Contains("manor") || s.Contains("palace")) return Kind.None;
            if (s.Contains("cave") || s.Contains("swamp") || s.Contains("forest") || s.Contains("dungeon")) return Kind.Herbs;
            return Kind.None;
        }

        internal static Site Find(string scene)
        {
            return scene == null ? null : sites.Find(x => x.scene == scene);
        }

        // ------------------------------------------------------------------ перечень мест

        /// <summary>On the world map: every dungeon, whose town it feeds and which village it troubles.</summary>
        internal static void Survey()
        {
            WorldTravelManager wtm = WorldTravelManager.instance;
            if (!On() || wtm == null || wtm.dungeons == null || wtm.worldPlaces == null) return;

            Dictionary<string, string> owners = BoardOwners();
            int today = Souls.Today();

            foreach (WorldPlace d in wtm.dungeons)
            {
                if (d == null || string.IsNullOrEmpty(d.linkedScene) || Find(d.linkedScene) != null) continue;

                WorldPlace town = null, village = null;
                float townFar = float.MaxValue, villageFar = float.MaxValue;
                foreach (WorldPlace p in wtm.worldPlaces)
                {
                    if (p == null || p == d) continue;
                    float far = (p.transform.position - d.transform.position).sqrMagnitude;
                    if (p.areaType == WorldPlaceType.city && far < townFar) { townFar = far; town = p; }
                    if (p.areaType == WorldPlaceType.village && far < villageFar) { villageFar = far; village = p; }
                }

                string owner;
                if (!owners.TryGetValue(d.linkedScene, out owner)) owner = town != null ? town.name : null;

                Site s = new Site
                {
                    scene = d.linkedScene,
                    name = d.name,
                    owner = owner,
                    village = village != null && villageFar < townFar ? village.name : null,
                    kind = KindOf(d.linkedScene + " " + d.name),
                    infestedSince = today,
                };
                sites.Add(s);
                Economy.Touch();
            }
        }

        /// <summary>The dungeons each town already posts its own purge errands for.</summary>
        private static Dictionary<string, string> BoardOwners()
        {
            Dictionary<string, string> owners = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                if (RandomQuestManager.instance == null || WorldPlacesManager.instance == null) return owners;
                foreach (FactionRandomQuestSpawnerSet set in RandomQuestManager.instance.spawnerSets)
                {
                    if (set == null || set.mercenaryQuestSpawners == null) continue;
                    WorldTownInfo town = WorldPlacesManager.instance.worldTowns.Find(t => t != null && t.faction == set.spawnPlace && t.type == WorldPlaceType.city);
                    if (town == null) continue;
                    foreach (RandomQuestSpawner s in set.mercenaryQuestSpawners)
                    {
                        RQDungeonSpawner dungeon = s as RQDungeonSpawner;
                        if (dungeon == null || dungeon.locationSet == null) continue;
                        foreach (TargetLocationTemplate t in dungeon.locationSet)
                        {
                            if (t != null && !string.IsNullOrEmpty(t.targetLocation) && !owners.ContainsKey(t.targetLocation))
                                owners[t.targetLocation] = town.name;
                        }
                    }
                }
            }
            catch
            {
            }
            return owners;
        }

        // ------------------------------------------------------------------ день

        /// <summary>A day of the sites: what they yield their towns, what they cost their villages.</summary>
        internal static void Day(int d)
        {
            if (!On()) return;

            foreach (Site s in sites)
            {
                bool clear = s.Clear(d);
                if (!clear && s.clearUntil >= 0 && d == s.clearUntil) s.infestedSince = d;

                Economy.Place owner = Economy.Get(s.owner);
                if (owner != null && s.kind != Kind.None)
                {
                    float share = clear ? 1f : Infested.Value;
                    switch (s.kind)
                    {
                        case Kind.Ore:
                            Economy.Gain(owner, GoodsType.Metal, 10f * share);
                            Economy.Gain(owner, GoodsType.Gem, 0.5f * share);
                            break;
                        case Kind.Wood:
                            Economy.Gain(owner, GoodsType.Wood, 10f * share);
                            break;
                        case Kind.Herbs:
                            Economy.Gain(owner, GoodsType.Alchemy, 6f * share);
                            break;
                    }
                }

                // Чудища у околицы топчут поля ближней деревни.
                if (!clear && s.village != null)
                {
                    Economy.Place v = Economy.Get(s.village);
                    if (v != null) v.Take(GoodsType.Produce, Economy.Harvest.Value * 0.6f * v.harvest * VillageLoss.Value);
                }

                if (!clear && s.kind != Kind.None) Post(s);
            }
        }

        // ------------------------------------------------------------------ зачистка

        /// <summary>Everything in the site is dead: it stays clear for a month or two, each by its own reckoning.</summary>
        internal static void Cleared(string scene)
        {
            Site s = Find(scene);
            if (s == null) return;

            int today = Souls.Today();
            if (s.Clear(today)) return;

            int span = ClearLow.Value + Mathf.RoundToInt(Crime.Lot(s.scene.GetHashCode(), 21) * Mathf.Max(0, ClearHigh.Value - ClearLow.Value));
            s.clearUntil = today + span;
            s.quest = -1;
            Economy.Touch();
            Souls.Say($"«{s.name}» зачищено: {(s.kind == Kind.Ore ? "руда" : s.kind == Kind.Wood ? "дерево" : s.kind == Kind.Herbs ? "травы" : "дорога")} "
                + $"снова идёт в «{s.owner}». Монстры вернутся не раньше чем через {span} дней.");
        }

        /// <summary>Whether the site's monsters must stay away for now.</summary>
        internal static bool KeepEmpty(string scene)
        {
            Site s = Find(scene);
            return s != null && On() && s.Clear(Souls.Today());
        }

        internal static int GrowthOf(string scene)
        {
            Site s = Find(scene);
            return s != null && On() ? s.Growth(Souls.Today()) : 0;
        }

        /// <summary>The owner town posts a purge errand on its infested site, one at a time.</summary>
        private static void Post(Site s)
        {
            if (s.quest >= 0 && RandomQuestManager.instance != null && RandomQuestManager.instance.quests.Find(q => q.id == s.quest) != null) return;
            s.quest = -1;

            try
            {
                WorldTownInfo info = WorldPlacesManager.instance != null ? WorldPlacesManager.instance.GetTown(s.owner) : null;
                if (info == null || RandomQuestManager.instance == null) return;

                // Больше двух зачисток разом город не объявляет: доска не для одних подземелий.
                int open = 0;
                foreach (RandomQuest q in RandomQuestManager.instance.quests)
                {
                    if (q != null && q.questType == RandomQuestType.minor_quest_dungeon && q.spawnPlace == info.faction) open++;
                }
                if (open >= 2) return;

                RQDungeonSpawner board = null;
                foreach (FactionRandomQuestSpawnerSet set in RandomQuestManager.instance.spawnerSets)
                {
                    if (set == null || set.spawnPlace != info.faction || set.mercenaryQuestSpawners == null) continue;
                    foreach (RandomQuestSpawner sp in set.mercenaryQuestSpawners)
                    {
                        if (sp is RQDungeonSpawner) { board = (RQDungeonSpawner)sp; break; }
                    }
                    if (board != null) break;
                }
                if (board == null) return;

                RandomQuest quest = Errands.SpawnAtSite(board, s.scene, s.name);
                if (quest == null) return;
                s.quest = quest.id;
                Economy.Touch();
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Места: задание на зачистку не вышло: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ файл

        internal static void Write(List<string> lines)
        {
            foreach (Site s in sites)
            {
                lines.Add("site=" + s.scene + ";" + s.name + ";" + (s.owner ?? "") + ";" + (s.village ?? "") + ";" + (int)s.kind + ";"
                    + s.clearUntil + ";" + s.infestedSince + ";" + s.quest);
            }
        }

        internal static void Clear()
        {
            sites.Clear();
        }

        internal static bool Read(string key, string[] v)
        {
            if (key != "site") return false;
            if (v.Length < 8) return true;
            Site s = new Site { scene = v[0], name = v[1], owner = v[2].Length > 0 ? v[2] : null, village = v[3].Length > 0 ? v[3] : null };
            int k;
            int.TryParse(v[4], out k);
            s.kind = (Kind)k;
            int.TryParse(v[5], out s.clearUntil);
            int.TryParse(v[6], out s.infestedSince);
            int.TryParse(v[7], out s.quest);
            sites.Add(s);
            return true;
        }
    }

    // Все в подземелье мертвы — место зачищено, кто бы ни убивал.
    [HarmonyPatch(typeof(DungeonManager), "OnEnemyDead")]
    internal static class DungeonDead_Sites_Patch
    {
        private static void Postfix(DungeonManager __instance)
        {
            if (!Sites.On()) return;
            try
            {
                foreach (UnitAttribute u in __instance.unitSpawned)
                {
                    if (u != null && u.Data != null && !u.Data.isdead) return;
                }
                Sites.Cleared(SceneManager.GetActiveScene().name);
            }
            catch
            {
            }
        }
    }

    // Зачищенное место пустует свой срок: заселять его раньше нельзя. Незачищенное — крепнет.
    [HarmonyPatch(typeof(DungeonManager), "Spawn")]
    internal static class DungeonSpawn_Sites_Patch
    {
        private static bool Prefix(DungeonManager __instance)
        {
            if (!Sites.On()) return true;
            try
            {
                string scene = SceneManager.GetActiveScene().name;
                if (!Sites.KeepEmpty(scene)) return true;
                if (RandomQuestManager.instance != null && RandomQuestManager.instance.GetQuestByLocationName(scene) != null) return true;
                __instance.refreshCD = __instance.refreshGap;
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static void Postfix(DungeonManager __instance)
        {
            if (!Sites.On()) return;
            try
            {
                int growth = Sites.GrowthOf(SceneManager.GetActiveScene().name);
                if (growth <= 0) return;
                foreach (UnitAttribute u in __instance.unitSpawned)
                {
                    if (u == null || u.Data == null || u.Data.isdead || u is HumaniodUnit) continue;
                    u.Data.level += growth;
                    u.UpdateAttribute();
                }
            }
            catch
            {
            }
        }
    }
}
