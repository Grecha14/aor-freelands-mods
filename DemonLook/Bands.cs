using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace DemonLook
{
    /// <summary>
    /// Hero bands of the world: they take the towns' errands, bleed for them, and spend what they earn.
    ///
    /// У всякого города есть свой отряд вольных героев. Раз в день, если он свободен, он берёт одно
    /// поручение своего города — зачистить его заражённую шахту или пещеру, разбить шайку или
    /// перебить стаю на его дорогах — и не берёт другого, пока не вернётся. Исход решает сила
    /// отряда против силы врага: удача — место зачищено или дорога чиста, деньги и опыт; неудача
    /// — раненые и убитые.
    ///
    /// Вернувшись, отряд живёт в городе: платит за ночлег и выпивку в таверне, лечит раненых у
    /// лекаря — по одному за два дня, — чинит у кузнеца то, что износилось, докупает зелья и еду,
    /// а если людей меньше четырёх, нанимает новых в таверне, пока есть на что.
    /// </summary>
    internal static class Bands
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> PerTown;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Bands", "Enabled", true,
                "Let each town keep a band of heroes that takes its errands, bleeds for them and spends its pay in town.");

            PerTown = config.Bind("Bands", "PerTown", 1,
                new ConfigDescription("Hero bands a town keeps.", new AcceptableValueRange<int>(0, 5)));
        }

        internal static bool On()
        {
            return Enabled != null && Enabled.Value && Economy.On();
        }

        internal class Band
        {
            internal int id;
            internal string home;
            internal string job;
            internal string target;
            internal int back = -1;
            internal int members = 4;
            internal float level = 5f;
            internal int money = 500;
            internal int injured;
            internal float wear;
            internal int done;
        }

        internal static readonly List<Band> bands = new List<Band>();
        private static int nextId = 1;

        private static float Power(Band b)
        {
            float fit = b.members > 0 ? 1f - 0.5f * b.injured / b.members : 0f;
            return b.members * (10f + b.level) * fit * (1f - b.wear / 200f);
        }

        /// <summary>A day of the bands: jobs taken, fought and paid for, and the town life between them.</summary>
        internal static void Day(int d, System.Random dice)
        {
            if (!On()) return;

            foreach (Economy.Place town in Economy.Places)
            {
                if (town.village) continue;
                int have = bands.FindAll(b => b.home == town.name).Count;
                for (; have < PerTown.Value; have++) bands.Add(new Band { id = nextId++, home = town.name });
            }

            foreach (Band b in bands)
            {
                if (b.job != null)
                {
                    if (d >= b.back) Resolve(b, dice);
                    continue;
                }

                Economy.Place town = Economy.Get(b.home);
                if (town == null) continue;
                Live(b, town, d);
                Pick(b, d);
            }
        }

        /// <summary>A day in town: the inn, the doctor, the smith, the shops, and new men if needed.</summary>
        private static void Live(Band b, Economy.Place town, int d)
        {
            // Отряд полёг весь — раз в две недели в городе набираются новые охотники до славы.
            if (b.members == 0)
            {
                if (d % 14 != 0) return;
                b.members = 3;
                b.level = 5f;
                b.money = 300;
                b.injured = 0;
                b.wear = 0f;
            }

            int lodging = 20 * b.members;
            if (b.money >= lodging) { b.money -= lodging; town.tavernCoin += lodging; }

            if (b.injured > 0 && d % 2 == 0)
            {
                int fee = 50;
                if (b.money >= fee) { b.money -= fee; town.smithCoin += fee; b.injured--; }
            }

            if (b.wear > 50f)
            {
                int cost = Mathf.RoundToInt(b.wear * 5f);
                if (b.money >= cost) { b.money -= cost; town.smithCoin += cost; b.wear = 0f; }
            }

            town.AddDemand(3, 0.3f * b.members);
            town.AddDemand(5, 0.5f * b.members);
            town.Eat(b.members);

            if (b.members < 4 && b.money >= 300)
            {
                b.money -= 300;
                town.tavernCoin += 300;
                b.members++;
            }
            Economy.Touch();
        }

        /// <summary>One errand a day, and only when the last one is done.</summary>
        private static void Pick(Band b, int d)
        {
            if (b.members < 3 || b.injured * 2 > b.members) return;

            float power = Power(b);
            string job = null, target = null;
            float easiest = float.MaxValue;
            int days = 1;

            foreach (Sites.Site s in Sites.sites)
            {
                if (s.owner != b.home || s.Clear(d) || s.kind == Sites.Kind.None) continue;
                float hard = 40f + 10f * s.Growth(d);
                if (hard > power * 1.5f || hard >= easiest) continue;
                easiest = hard;
                job = "site";
                target = s.scene;
                days = 2;
            }

            foreach (Trade.Gang g in Trade.gangs)
            {
                if (g.a != b.home && g.b != b.home) continue;
                float hard = g.men * (g.beasts ? 8f : 12f);
                if (hard > power * 1.5f || hard >= easiest) continue;
                easiest = hard;
                job = "gang";
                target = g.id.ToString(CultureInfo.InvariantCulture);
                days = 1 + Trade.Days(g.a, g.b) / 2;
            }

            if (job == null) return;
            b.job = job;
            b.target = target;
            b.back = d + Mathf.Max(1, days);
            Economy.Touch();
        }

        private static void Resolve(Band b, System.Random dice)
        {
            float hard = 40f;
            string what = null;
            Sites.Site site = null;
            Trade.Gang gang = null;

            if (b.job == "site")
            {
                site = Sites.sites.Find(s => s.scene == b.target);
                if (site != null) { hard = 40f + 10f * site.Growth(Souls.Today()); what = site.name; }
            }
            else
            {
                int id;
                int.TryParse(b.target, out id);
                gang = Trade.gangs.Find(g => g.id == id);
                if (gang != null) { hard = gang.men * (gang.beasts ? 8f : 12f); what = gang.beasts ? "стаю зверей" : "шайку"; }
            }

            b.job = null;
            b.target = null;
            b.back = -1;
            Economy.Touch();
            if (what == null) return;

            float power = Power(b);
            bool won = dice.NextDouble() < power / (power + hard);
            b.wear = Mathf.Min(100f, b.wear + 15f + (float)dice.NextDouble() * 15f);

            if (won)
            {
                int pay = 200 + Mathf.RoundToInt(hard * 5f);
                b.money += pay;
                b.level = Mathf.Min(60f, b.level + 0.3f);
                b.done++;
                if (dice.NextDouble() < 0.3) b.injured = Mathf.Min(b.members, b.injured + 1);

                if (site != null) Sites.Cleared(site.scene);
                if (gang != null) Trade.gangs.Remove(gang);

                Souls.Say($"Отряд героев из «{b.home}» разделался с {(site != null ? "врагом в «" + what + "»" : what + " на дорогах")}.");
                return;
            }

            int dead = dice.Next(0, 3);
            b.members = Mathf.Max(0, b.members - dead);
            b.injured = Mathf.Min(b.members, b.injured + 1 + dice.Next(0, 2));
            if (b.members == 0)
            {
                DemonLookPlugin.Log.LogInfo($"Отряд героев из «{b.home}» погиб весь.");
                b.members = 0;
                b.level = 5f;
                b.money = 0;
            }
        }

        // ------------------------------------------------------------------ файл

        internal static void Write(List<string> lines)
        {
            lines.Add("bandid=" + nextId);
            foreach (Band b in bands)
            {
                lines.Add("band=" + b.id + ";" + b.home + ";" + (b.job ?? "") + ";" + (b.target ?? "") + ";" + b.back + ";" + b.members + ";"
                    + b.level.ToString("0.##", CultureInfo.InvariantCulture) + ";" + b.money + ";" + b.injured + ";"
                    + b.wear.ToString("0.#", CultureInfo.InvariantCulture) + ";" + b.done);
            }
        }

        internal static void Clear()
        {
            bands.Clear();
            nextId = 1;
        }

        internal static bool Read(string key, string[] v)
        {
            if (key == "bandid") { int.TryParse(v[0], out nextId); return true; }
            if (key != "band") return false;
            if (v.Length < 11) return true;

            Band b = new Band { home = v[1], job = v[2].Length > 0 ? v[2] : null, target = v[3].Length > 0 ? v[3] : null };
            int.TryParse(v[0], out b.id);
            int.TryParse(v[4], out b.back);
            int.TryParse(v[5], out b.members);
            float.TryParse(v[6], NumberStyles.Float, CultureInfo.InvariantCulture, out b.level);
            int.TryParse(v[7], out b.money);
            int.TryParse(v[8], out b.injured);
            float.TryParse(v[9], NumberStyles.Float, CultureInfo.InvariantCulture, out b.wear);
            int.TryParse(v[10], out b.done);
            bands.Add(b);
            return true;
        }
    }
}
