using System;
using System.Collections.Generic;
using System.Globalization;
using DuloGames.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DemonLook
{
    /// <summary>
    /// Who lives in a place, counted when the hero walks in; what they get out of the land.
    ///
    /// Добыча места считается не общим числом, а по тем, кто в нём живёт. Свинья даёт свинину,
    /// корова — говядину и кожу, коза — баранину и кожу, куры у всякого двора — яйца и
    /// курятину. Селянин и фермер растят овощи и зерно, охотник приносит дичь, рыбак — рыбу,
    /// шахтёр — руду, гном-шахтёр — руду и камни, а мясник разделывает так, что мяса выходит
    /// на пятую часть больше. Перепись обновляется при каждом приходе: убили свиней — нет и
    /// свинины. Пока место не видели, оно живёт по общим числам своего вида.
    /// </summary>
    internal class Census
    {
        internal int folk, farmers, hunters, fishers, miners, dwarves, butchers, pigs, cattle, goats, houses, guards;

        private static readonly int[] PigIds = { 1011 };
        private static readonly int[] CattleIds = { 1140 };
        private static readonly int[] GoatIds = { 1429 };
        private static readonly int[] FarmerIds = { 1129 };
        private static readonly int[] HunterIds = { 1255, 1794, 1795, 1618 };
        private static readonly int[] FisherIds = { 1420 };
        private static readonly int[] MinerIds = { 1782 };
        private static readonly int[] DwarfMinerIds = { 1887 };
        private static readonly int[] ButcherIds = { 1468 };

        internal int Mouths => Mathf.Max(1, folk + farmers + hunters + fishers + miners + dwarves + butchers + guards);

        /// <summary>What they get out of the land in a day.</summary>
        internal void Yield(Economy.Place p)
        {
            float crops = (p.village ? folk * 2f : 0f) + farmers * 4f;
            p.Add(GoodsType.Produce, crops * p.harvest);

            float meat = pigs + cattle + goats * 0.6f + (p.village ? houses * 0.5f : 0f) + hunters * 1.5f + fishers * 2f;
            if (butchers > 0) meat *= 1.2f;
            p.Add(GoodsType.Meat, meat);

            Gain(p, GoodsType.Leather, cattle * 0.3f + goats * 0.2f + hunters * 0.3f);
            Gain(p, GoodsType.Metal, miners * 3f + dwarves * 4f);
            Gain(p, GoodsType.Gem, dwarves * 0.2f);
        }

        private static void Gain(Economy.Place p, GoodsType t, float n)
        {
            Economy.Gain(p, t, n);
        }

        internal string Write()
        {
            return string.Join(",", new[]
            {
                folk, farmers, hunters, fishers, miners, dwarves, butchers, pigs, cattle, goats, houses, guards,
            }.Select(x => x.ToString(CultureInfo.InvariantCulture)));
        }

        internal static Census Parse(string s)
        {
            string[] v = (s ?? "").Split(',');
            int[] n = new int[12];
            for (int i = 0; i < n.Length && i < v.Length; i++) int.TryParse(v[i], out n[i]);
            return new Census
            {
                folk = n[0], farmers = n[1], hunters = n[2], fishers = n[3], miners = n[4], dwarves = n[5],
                butchers = n[6], pigs = n[7], cattle = n[8], goats = n[9], houses = n[10], guards = n[11],
            };
        }

        // ------------------------------------------------------------------ где мы

        // Имя сцены — имя места: карта мира знает, какая сцена за каким местом стоит.
        internal static readonly Dictionary<string, string> scenes = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>On the world map: which scene belongs to which place.</summary>
        internal static void Map()
        {
            WorldTravelManager wtm = WorldTravelManager.instance;
            if (wtm == null || wtm.worldPlaces == null) return;

            foreach (WorldPlace wp in wtm.worldPlaces)
            {
                if (wp == null || string.IsNullOrEmpty(wp.linkedScene)) continue;
                string had;
                if (scenes.TryGetValue(wp.linkedScene, out had) && had == wp.name) continue;
                scenes[wp.linkedScene] = wp.name;
                Economy.Touch();
            }
        }

        /// <summary>The place the hero is standing in, if it is a town or a village of ours.</summary>
        internal static Economy.Place Here()
        {
            if ((bool)WorldTravelManager.instance || VillageManager.instance == null) return null;

            try
            {
                WorldPlacesManager wpm = WorldPlacesManager.instance;
                if (CityTownManager.instance != null && wpm != null && wpm.currentTown != null)
                {
                    Economy.Place town = Economy.Get(wpm.currentTown.name);
                    if (town != null) return town;
                }

                string scene = SceneManager.GetActiveScene().name;
                string name;
                if (!string.IsNullOrEmpty(scene) && scenes.TryGetValue(scene, out name)) return Economy.Get(name);

                // Сцена «001-FariniVillage» стоит за местом «FariniVillage».
                int dash = scene != null ? scene.IndexOf('-') : -1;
                if (dash >= 0) return Economy.Get(scene.Substring(dash + 1));
            }
            catch
            {
            }
            return null;
        }

        // ------------------------------------------------------------------ перепись

        private static string countedScene;
        private static float countAt = -1f;

        internal static void Tick()
        {
            if ((bool)WorldTravelManager.instance)
            {
                Map();
                countedScene = null;
                return;
            }

            VillageManager village = VillageManager.instance;
            if (village == null) return;

            string scene = SceneManager.GetActiveScene().name;
            float now = Time.unscaledTime;

            // Новая сцена — сперва дать ей населиться, потом считать; дальше раз в пять минут.
            if (scene != countedScene)
            {
                countedScene = scene;
                countAt = now + 20f;
                return;
            }
            if (now < countAt) return;
            countAt = now + 300f;

            Economy.Place p = Here();
            if (p == null) return;

            try
            {
                p.census = Count(village.faction);
                p.mouths = p.census.Mouths;
                Economy.Touch();
                DemonLookPlugin.Log.LogInfo($"Перепись «{p.name}»: людей {p.census.Mouths}, свиней {p.census.pigs}, "
                    + $"коров {p.census.cattle}, коз {p.census.goats}, дворов {p.census.houses}.");
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Перепись не удалась: " + e.Message);
            }
        }

        private static bool Is(int id, int[] set)
        {
            return Array.IndexOf(set, id) >= 0;
        }

        private static Census Count(Faction team)
        {
            Census c = new Census();

            foreach (UnitAttribute u in UnityEngine.Object.FindObjectsOfType<UnitAttribute>())
            {
                if (u == null || u.Data == null || u.Data.isdead || u.inParty) continue;
                int id = u.info != null ? u.info.unitId : -1;

                if (Is(id, PigIds)) { c.pigs++; continue; }
                if (Is(id, CattleIds)) { c.cattle++; continue; }
                if (Is(id, GoatIds)) { c.goats++; continue; }

                if (!(u is HumaniodUnit) || u.Data.team != team || Kids.Is(u)) continue;
                if (Crime.Guard(u)) { c.guards++; continue; }

                if (Is(id, FarmerIds)) c.farmers++;
                else if (Is(id, HunterIds)) c.hunters++;
                else if (Is(id, FisherIds)) c.fishers++;
                else if (Is(id, MinerIds)) c.miners++;
                else if (Is(id, DwarfMinerIds)) c.dwarves++;
                else if (Is(id, ButcherIds)) c.butchers++;
                else c.folk++;
            }

            foreach (BuildingController b in UnityEngine.Object.FindObjectsOfType<BuildingController>())
            {
                if (b != null && b.isPrivateBuilding && !b.isPlayerHouse && b.owners != null && b.owners.Count > 0) c.houses++;
            }

            return c;
        }
    }

    internal static class CensusLinq
    {
        internal static IEnumerable<string> Select(this int[] values, Func<int, string> f)
        {
            foreach (int v in values) yield return f(v);
        }
    }
}
