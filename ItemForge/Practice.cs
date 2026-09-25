using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Mastery bought with practice instead of with gear.
    ///
    /// Опыт владения оружием игра начисляет **равным нанесённому урону**: ударил на полторы
    /// тысячи — получил полторы тысячи опыта. Пока удары были на тридцать, это читалось как
    /// «бей и научишься». Стоило урону вырасти — а мы подняли его со всех сторон разом, — и
    /// один удар стал перескакивать через пять ступеней сразу.
    ///
    /// Беда не в скорости, а в том, на что мастерство оказалось завязано. Оно росло от
    /// снаряжения: надел легендарку — стал мастером, снял — остался им же. Меч учил владению
    /// мечом ровно настолько, насколько был дорог.
    ///
    /// Здесь ступень стоит ударов, а не урона. Первые даются за пять, восемь, тринадцать —
    /// дальше по двадцать, и так до сотой. Ряд начинается по Фибоначчи и упирается в потолок:
    /// без потолка сороковая ступень просила бы миллионы ударов, что не «долго», а «никогда».
    ///
    /// Удар по тому, кто сильнее, считается за нескольких, по слабому — за долю, но в пределах
    /// трети в обе стороны: тролль учит лучше крысы, однако ни один противник не заменяет
    /// собой практику.
    /// </summary>
    internal static class Practice
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Ladder;
        internal static ConfigEntry<int> Ceiling;
        internal static ConfigEntry<float> Spread;
        internal static ConfigEntry<float> Least;
        internal static ConfigEntry<int> Dummies;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Practice", "Enabled", true,
                "Buy weapon mastery with blows struck rather than with damage dealt. The game "
                + "grants experience equal to the damage, so a legendary blade teaches its "
                + "owner faster than a plain one — and once damage climbs into the thousands, a "
                + "single blow carries five ranks at once.");

            Ladder = config.Bind("Practice", "Ladder", "5,8,13",
                "Blows the first ranks cost, one number per rank. Fibonacci, as the ladders in "
                + "this mod usually are; past the last of them every rank costs the ceiling "
                + "below.");

            Ceiling = config.Bind("Practice", "Ceiling", 20,
                new ConfigDescription(
                    "Blows every rank past the ladder costs. Twenty: about two hundred enemies "
                    + "to the fortieth rank, which is what a third-tier weapon asks for, and "
                    + "about five hundred to the hundredth. Without a ceiling the sequence "
                    + "reaches millions by the fortieth rank, which is not «long» but «never».",
                    new AcceptableValueRange<int>(1, 500)));

            Spread = config.Bind("Practice", "Spread", 0.3f,
                new ConfigDescription(
                    "How far a foe's level may move the worth of a blow, as a share. Three "
                    + "tenths: against someone far above you a blow counts for a third more. Narrow on "
                    + "purpose — practice is practice, and no single opponent should replace it.",
                    new AcceptableValueRange<float>(0f, 3f)));

            Least = config.Bind("Practice", "Least", 1f,
                new ConfigDescription(
                    "The least a blow may be worth, however far beneath you the foe stands. One: "
                    + "a blow is a blow, and beating a rat teaches no worse than beating a man — "
                    + "it simply teaches no better. Lower it to make weak enemies a waste of "
                    + "practice as well as of time.",
                    new AcceptableValueRange<float>(0.1f, 1f)));

            Dummies = config.Bind("Practice", "Dummies", 10,
                new ConfigDescription(
                    "The rank up to which a straw man still teaches. The game forbids it "
                    + "altogether, and rightly for the most part: mastery earned against "
                    + "something that does not strike back is not mastery. But the first ten "
                    + "ranks are not mastery either — they are learning which end to hold, and "
                    + "that is exactly what a practice post is for. Past this rank the post "
                    + "teaches nothing and a living opponent is the only teacher.",
                    new AcceptableValueRange<int>(0, 99)));
        }

        // По кому пришёлся удар. Начисление опыта об этом не знает — его зовут уже после, — а
        // поправка за противника без этого не считается.
        internal static UnitAttribute Struck;

        /// <summary>What one blow is worth in experience, for a hand of this standing.</summary>
        internal static int Worth(HumaniodUnit who, int rank)
        {
            try
            {
                int hits = Hits(rank);
                if (hits <= 0) return 1;

                // Цена ступени берётся у самой игры: свою кривую выдумывать незачем, надо лишь
                // решить, во сколько ударов она обходится.
                int cost = HumaniodUnit.GetNextWeaponMasteryExpCap(rank);
                if (cost <= 0) return 1;

                float worth = (float)cost / hits * Weigh(who);

                return Mathf.Max(1, Mathf.RoundToInt(worth));
            }
            catch
            {
                return 1;
            }
        }

        /// <summary>How much this particular foe is worth against this particular hand.</summary>
        private static float Weigh(HumaniodUnit who)
        {
            if (Spread.Value <= 0f) return 1f;

            try
            {
                if (Struck == null || Struck.Data == null || who == null || who.Data == null) return 1f;

                int mine = Mathf.Max(1, who.Data.level);
                int theirs = Mathf.Max(1, Struck.Data.level);

                // Вниз не режем. Слабый противник учит не хуже — он просто не учит лучше:
                // удар остаётся ударом, кого бы вы ни били. Прибавка идёт только вверх, за
                // тех, кто вам не по зубам.
                return Mathf.Clamp((float)theirs / mine, Least.Value, 1f + Spread.Value);
            }
            catch
            {
                return 1f;
            }
        }

        /// <summary>Lets a straw man teach the first ranks, and no more than the first.</summary>
        internal static void Post(UnitAttribute who, UnitAttribute target)
        {
            if (!Enabled.Value || Dummies.Value <= 0) return;
            if (who == null || target == null || !target.isPracticeDummy) return;

            HumaniodUnit hand = who as HumaniodUnit;
            if (hand == null || hand.Data == null || hand.Data.weaponMastery == null) return;

            WeaponType branch = hand.weapontype;
            if (branch == WeaponType.none || branch > WeaponType.polearms) return;

            int index = (int)branch;
            if (index < 0 || index >= hand.Data.weaponMastery.Length) return;

            int rank = hand.Data.weaponMastery[index];
            if (rank >= Dummies.Value) return;

            // Игра начисление на чучеле запрещает, поэтому добавляем своё — и только до той
            // ступени, после которой чучело и правда перестаёт чему-либо учить.
            hand.GainWeaponMasteryExp(branch, Worth(hand, rank));
        }

        private static int[] steps;
        private static string read;

        private static int Hits(int rank)
        {
            string written = Ladder.Value ?? "";

            if (written != read)
            {
                read = written;

                List<int> got = new List<int>();
                foreach (string one in written.Split(','))
                {
                    int much;
                    if (int.TryParse(one.Trim(), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out much) && much > 0)
                    {
                        got.Add(much);
                    }
                }

                steps = got.ToArray();
            }

            if (steps == null || steps.Length == 0) return Ceiling.Value;
            if (rank < 0) rank = 0;

            return rank < steps.Length ? steps[rank] : Ceiling.Value;
        }
    }

    // Единственная воронка, через которую проходит состоявшийся удар: здесь известны и бьющий,
    // и тот, по кому пришлось. Начисление опыта случится дальше и цели уже не увидит.
    [HarmonyPatch(typeof(UnitAttribute), "ApplyAttackEffect")]
    internal static class ApplyAttackEffect_Practice_Patch
    {
        private static void Prefix(UnitAttribute __instance, UnitAttribute target)
        {
            Practice.Struck = target;

            try { Practice.Post(__instance, target); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(HumaniodUnit), "GainWeaponMasteryExp",
        new Type[] { typeof(WeaponType), typeof(int) })]
    internal static class GainWeaponMasteryExp_Practice_Patch
    {
        private static void Prefix(HumaniodUnit __instance, WeaponType t_weapontype, ref int exp)
        {
            try
            {
                if (!Practice.Enabled.Value || __instance == null || __instance.Data == null) return;
                if (exp <= 0) return;

                int branch = (int)t_weapontype;
                if (__instance.Data.weaponMastery == null
                    || branch < 0 || branch >= __instance.Data.weaponMastery.Length) return;

                exp = Practice.Worth(__instance, __instance.Data.weaponMastery[branch]);
            }
            catch
            {
            }
        }
    }
}
