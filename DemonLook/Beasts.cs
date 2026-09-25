using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace DemonLook
{
    /// <summary>
    /// Beasts on the roads: they keep to their ground, see less than men, hunt harder by night,
    /// never walk into a town, and grow with every kill.
    ///
    /// Стая на карте мира не стоит на месте, а обходит свою округу. Замечает она на четыре десятых
    /// ближе, чем человек, — зато ночью в полтора раза дальше и бегает на пятую часть быстрее. В
    /// города и деревни звери не заходят: подошли к околице — поворачивают к себе.
    ///
    /// Зверь растёт, как и всякий, кто убивает: за каждые три убийства — уровень, но не больше чем
    /// на десять сверх своего вида. Стая, что одолела отряд на дороге, становится на уровень
    /// сильнее. Разорила караван — город, куда он шёл, даёт задание перебить её.
    /// </summary>
    internal static class Beasts
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> DaySight;
        internal static ConfigEntry<float> NightSight;
        internal static ConfigEntry<float> NightSpeed;
        internal static ConfigEntry<float> Roam;
        internal static ConfigEntry<int> KillsPerLevel;
        internal static ConfigEntry<int> LevelCap;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Beasts", "Enabled", true,
                "Let the beasts of the world map keep to their ground, hunt harder at night, stay out of towns and grow with their kills.");

            DaySight = config.Bind("Beasts", "DaySight", 0.6f,
                new ConfigDescription("How far a beast sees by day, against a man.", new AcceptableValueRange<float>(0.1f, 3f)));

            NightSight = config.Bind("Beasts", "NightSight", 0.9f,
                new ConfigDescription("How far a beast sees by night, against a man.", new AcceptableValueRange<float>(0.1f, 3f)));

            NightSpeed = config.Bind("Beasts", "NightSpeed", 1.2f,
                new ConfigDescription("How much faster a beast runs by night.", new AcceptableValueRange<float>(0.5f, 3f)));

            Roam = config.Bind("Beasts", "Roam", 20f,
                new ConfigDescription("How far from its ground a pack wanders on the world map.", new AcceptableValueRange<float>(1f, 200f)));

            KillsPerLevel = config.Bind("Beasts", "KillsPerLevel", 3,
                new ConfigDescription("Kills a beast needs for a level.", new AcceptableValueRange<int>(1, 100)));

            LevelCap = config.Bind("Beasts", "LevelCap", 10,
                new ConfigDescription("How many levels a beast can grow over its kind.", new AcceptableValueRange<int>(0, 100)));
        }

        internal static bool On()
        {
            return Enabled != null && Enabled.Value;
        }

        internal static bool Animal(UnitAttribute u)
        {
            return u != null && u.info != null && u.info.utype == UnitType.animal;
        }

        private static bool Night()
        {
            try
            {
                int hour = TimeManager.Hour;
                return hour >= 21 || hour < 6;
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------------ стаи на карте

        private class Pack
        {
            internal Vector3 home;
            internal float sight;
            internal float speed;
            internal float idleSince;
        }

        private static readonly Dictionary<int, Pack> packs = new Dictionary<int, Pack>();
        private static float next;

        internal static void Tick()
        {
            if (!On()) return;
            WorldTravelManager wtm = WorldTravelManager.instance;
            if (wtm == null || wtm.travelGroups == null)
            {
                if (packs.Count > 0) packs.Clear();
                return;
            }

            float now = Time.time;
            if (now < next) return;
            next = now + 2f;

            bool night = Night();

            foreach (TravelGroup g in wtm.travelGroups)
            {
                if (g == null || g.isPlayer || g.leader == null || !Animal(g.leader)) continue;
                UnitAttribute lead = g.leader;
                if (lead.Data == null || lead.Data.isdead) continue;

                Pack p;
                if (!packs.TryGetValue(g.id, out p))
                {
                    p = new Pack { home = lead.transform.position, sight = lead.senseRange, speed = g.travelSpeedBonus, idleSince = now };
                    packs[g.id] = p;
                }

                // Ночью видит дальше и бегает быстрее; днём — хуже человека.
                lead.senseRange = p.sight * (night ? NightSight.Value : DaySight.Value);
                g.travelSpeedBonus = p.speed * (night ? NightSpeed.Value : 1f);

                if (lead.isEngaged) { p.idleSince = now; continue; }

                // У околицы — назад к себе: в города звери не заходят.
                if (NearSettlement(wtm, lead.transform.position))
                {
                    Move(lead, p.home);
                    p.idleSince = now;
                    continue;
                }

                // Постоял — пошёл обходить свою округу.
                if (lead.isMoving) { p.idleSince = now; continue; }
                if (now - p.idleSince < 20f) continue;

                Vector2 step = UnityEngine.Random.insideUnitCircle * Roam.Value;
                Move(lead, p.home + new Vector3(step.x, 0f, step.y));
                p.idleSince = now;
            }

            if (packs.Count > 300) packs.Clear();
        }

        private static bool NearSettlement(WorldTravelManager wtm, Vector3 at)
        {
            if (wtm.worldPlaces == null) return false;
            foreach (WorldPlace wp in wtm.worldPlaces)
            {
                if (wp == null || (wp.areaType != WorldPlaceType.city && wp.areaType != WorldPlaceType.village)) continue;
                float reach = Mathf.Max(wp.clearRange, 5f) + 8f;
                if ((wp.transform.position - at).sqrMagnitude < reach * reach) return true;
            }
            return false;
        }

        private static void Move(UnitAttribute lead, Vector3 to)
        {
            try { lead.stateMachine.HandleCommand(new UnitCommand(commandsName.move, to, Night() ? 1f : 0.6f)); } catch { }
        }

        // ------------------------------------------------------------------ рост

        private static readonly Dictionary<int, int> kills = new Dictionary<int, int>();

        private static void Grow(UnitAttribute u, int levels)
        {
            if (u == null || u.Data == null || u.info == null || levels <= 0) return;
            int top = u.info.level + LevelCap.Value;
            if (u.Data.level >= top) return;
            u.Data.level = Mathf.Min(top, u.Data.level + levels);
            try { u.UpdateAttribute(); } catch { }
        }

        /// <summary>A beast killed someone: every few kills it grows a level.</summary>
        internal static void Killed(UnitAttribute killer)
        {
            if (!On() || !Animal(killer) || killer.inParty || killer.Data == null) return;

            int id = killer.GetInstanceID();
            int n;
            kills.TryGetValue(id, out n);
            n++;
            if (n >= KillsPerLevel.Value)
            {
                n = 0;
                Grow(killer, 1);
            }
            kills[id] = n;
            if (kills.Count > 500) kills.Clear();
        }

        /// <summary>A fight on the world map is over: beasts that won grow; a caravan they broke brings a hunt.</summary>
        internal static void Fought(WorldMapFight fight)
        {
            if (fight == null || fight.winGroup == null) return;

            bool attackerWon = fight.winGroup == fight.attackerGroup;
            List<TravelGroup> won = new List<TravelGroup>();
            List<TravelGroup> lost = new List<TravelGroup>();
            (attackerWon ? won : lost).Add(fight.attackerGroup);
            (attackerWon ? won : lost).AddRange(fight.attackerReinforcements);
            (attackerWon ? lost : won).Add(fight.defenderGroup);
            (attackerWon ? lost : won).AddRange(fight.defenderReinforcements);

            bool beasts = false, bandits = false;
            int men = 0;
            foreach (TravelGroup g in won)
            {
                if (g == null || g.leader == null) continue;
                if (Animal(g.leader))
                {
                    beasts = true;
                    if (On()) Grow(g.leader, 1);
                }
                else if (g.leader.Data != null && g.leader.Data.team == Faction.outlaw) bandits = true;
                men += 1 + (g.members != null ? g.members.Count : 0);
            }

            foreach (TravelGroup g in lost)
            {
                if (g == null || !g.isCaravan || string.IsNullOrEmpty(g.targetPlace)) continue;
                if (beasts && On()) Trade.PostHunt(g.targetPlace, false, men);
                else if (bandits && Trade.On()) Trade.PostHunt(g.targetPlace, true, men);
            }
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "Die")]
    internal static class Die_Beasts_Patch
    {
        private static void Postfix(UnitAttribute killer)
        {
            try { Beasts.Killed(killer); } catch { }
        }
    }

    [HarmonyPatch(typeof(GlobalEncounterManager), "CalculateBattleResult")]
    internal static class Battle_Beasts_Patch
    {
        private static void Prefix(WorldMapFight fight)
        {
            try { Beasts.Fought(fight); } catch { }
        }
    }
}
