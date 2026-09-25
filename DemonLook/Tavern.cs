using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace DemonLook
{
    /// <summary>
    /// The tavern in the evening: townsfolk and shopkeepers drop in, sit a while, pay and go.
    ///
    /// С шести вечера до одиннадцати в таверну заходят горожане, а после закрытия лавок — и
    /// торговцы. Мест столько, сколько стульев: кому не хватило, тот не пришёл. Сидят час-два,
    /// платят за еду и выпивку от пятнадцати до сорока медных — деньги трактирщику, еда со склада
    /// города, — и уходят домой. Весь город в таверну не набивается: за вечер заглядывает
    /// каждый четвёртый.
    /// </summary>
    internal static class Tavern
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Opens;
        internal static ConfigEntry<int> Closes;
        internal static ConfigEntry<float> Share;
        internal static ConfigEntry<int> SpendLow;
        internal static ConfigEntry<int> SpendHigh;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Tavern", "Enabled", true,
                "Let the townsfolk and the shopkeepers spend their evenings in the tavern, as many as there are seats.");

            Opens = config.Bind("Tavern", "Opens", 18,
                new ConfigDescription("The hour the evening crowd starts to come.", new AcceptableValueRange<int>(0, 23)));

            Closes = config.Bind("Tavern", "Closes", 23,
                new ConfigDescription("The hour the last of them go home.", new AcceptableValueRange<int>(0, 23)));

            Share = config.Bind("Tavern", "Share", 0.25f,
                new ConfigDescription("What share of the townsfolk drops in on an evening.", new AcceptableValueRange<float>(0f, 1f)));

            SpendLow = config.Bind("Tavern", "SpendLow", 15,
                new ConfigDescription("The least copper a guest spends.", new AcceptableValueRange<int>(0, 10000)));

            SpendHigh = config.Bind("Tavern", "SpendHigh", 40,
                new ConfigDescription("The most copper a guest spends.", new AcceptableValueRange<int>(0, 10000)));
        }

        private class Guest
        {
            internal UnitAttribute u;
            internal Furniture seat;
            internal float leaveAt;
        }

        private static readonly List<Guest> guests = new List<Guest>();
        private static readonly Dictionary<int, int> came = new Dictionary<int, int>();
        private static float next;

        internal static bool Visiting(UnitAttribute u)
        {
            foreach (Guest g in guests)
            {
                if (ReferenceEquals(g.u, u)) return true;
            }
            return false;
        }

        private static bool Evening()
        {
            try
            {
                int hour = TimeManager.Hour;
                return hour >= Opens.Value && hour < Closes.Value;
            }
            catch
            {
                return false;
            }
        }

        internal static void Tick()
        {
            if (Enabled == null || !Enabled.Value) return;

            float now = Time.time;
            Leave(now);

            if ((bool)WorldTravelManager.instance || CityTownManager.instance == null || !Evening()) return;
            if (now < next) return;
            next = now + 20f;

            try { Come(now); } catch (Exception e) { DemonLookPlugin.Log.LogWarning("Таверна: гости не пришли: " + e.Message); }
        }

        private static void Leave(float now)
        {
            bool late = !Evening();
            for (int i = guests.Count - 1; i >= 0; i--)
            {
                Guest g = guests[i];
                bool gone = g.u == null || g.u.Data == null || g.u.Data.isdead;
                if (!gone && !late && now < g.leaveAt) continue;

                guests.RemoveAt(i);
                if (gone) continue;
                try { if (!g.u.isEngaged) g.u.stateMachine.HandleStop(); } catch { }
            }
        }

        private static void Come(float now)
        {
            BuildingController inn = Homes.Tavern();
            if (inn == null || inn.chiars == null || inn.chiars.Length == 0) return;

            List<Furniture> free = new List<Furniture>();
            foreach (Furniture chair in inn.chiars)
            {
                if (chair == null || chair.interacting) continue;
                bool taken = false;
                foreach (Guest g in guests) taken |= ReferenceEquals(g.seat, chair);
                if (!taken) free.Add(chair);
            }
            if (free.Count == 0) return;

            Faction town = CityTownManager.instance.faction;
            int tonight = Crime.Night();
            bool shopsShut = Shutters.Closed();

            foreach (AIBehaviorController brain in UnityEngine.Object.FindObjectsOfType<AIBehaviorController>())
            {
                UnitAttribute u = brain != null ? brain.GetComponent<UnitAttribute>() : null;
                if (u == null || u.Data == null || u.Data.isdead || u.inParty || !(u is HumaniodUnit)) continue;
                if (u.Data.team != town || u.isSleeping || u.isEngaged || u.isTalking || u.items == null) continue;
                if (Crime.Guard(u) || Visiting(u) || Kids.Is(u)) continue;

                NPCSaveData npc = u.Data as NPCSaveData;
                if (npc == null || npc.heroCareer != null || npc.career == CareerType.Bartender) continue;
                if (Crime.Shopkeeper(u) && !shopsShut) continue;

                int was;
                if (came.TryGetValue(npc.id, out was) && was == tonight) continue;
                if (Crime.Lot(npc.id, 11) >= Share.Value) continue;

                came[npc.id] = tonight;
                Seat(u, npc, free[0], now);
                return;
            }
        }

        private static void Seat(UnitAttribute u, NPCSaveData npc, Furniture chair, float now)
        {
            try
            {
                u.stateMachine.HandleStop();
                u.stateMachine.HandleCommand(new UnitCommand(commandsName.interact, chair.gameObject));
            }
            catch
            {
                return;
            }

            // Час-два игрового времени: у нас он идёт по полторы сотни секунд на час.
            float hours = 1f + Crime.Lot(npc.id, 12);
            guests.Add(new Guest { u = u, seat = chair, leaveAt = now + hours * TimeManager.LOCAL_MAP_HOUR_IN_SECOND });

            int spend = Mathf.RoundToInt(Mathf.Lerp(SpendLow.Value, SpendHigh.Value, Crime.Lot(npc.id, 13)));
            spend = Mathf.Min(spend, u.items.money);
            if (spend <= 0) return;

            u.items.money -= spend;
            try
            {
                CitytownBarManager bar = CitytownBarManager.instance;
                if (bar != null && bar.itemstock != null) bar.itemstock.money += spend;
            }
            catch
            {
            }

            Economy.Place p = Census.Here();
            if (p != null)
            {
                p.Eat(0.5f);
                Economy.Touch();
            }
        }
    }
}
