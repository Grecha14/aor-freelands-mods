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
    /// Зверь считается как человек: стат, вес того, чем бьёт, мастерство ветки.
    ///
    /// В игре у существ нет ни одного из шести статов — только здоровье, урон и сопротивления,
    /// проставленные поштучно и вразнобой: у средних урон гуляет от трёх до шестидесяти, а
    /// здоровья на уровень выходит от девятнадцати до пятидесяти девяти. Оттого лес не
    /// складывается в лестницу, а наша боевая модель зверя вовсе не видит: пробивать у него
    /// нечего, и оружие против него безразлично.
    ///
    /// Здесь всё выводится из двух вещей — **размера и уровня**.
    ///
    ///     статы     — по росту, с приростом за уровень
    ///     ведёт     — ловкость у мелкого и среднего, сила у крупного и выше
    ///     мастерство— полусотня внизу своей вилки, сотня наверху
    ///     пробитие  — стат × вес когтя × (1 + мастерство/100)
    ///     вычет     — шкура: ровная у мелочи, растущая у крупных
    ///     здоровье  — человеческой формулой, где выносливость помножена на тушу
    ///
    /// Вершины писаны руками: у дракона свои сила, коготь и стена, потому что вершина — это не
    /// то, что дала арифметика, а то, что задумано.
    /// </summary>
    internal static class Beastly
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Bearing;
        internal static ConfigEntry<string> Stats;
        internal static ConfigEntry<string> Growth;
        internal static ConfigEntry<string> Claw;
        internal static ConfigEntry<string> Bulk;
        internal static ConfigEntry<string> Walls;
        internal static ConfigEntry<float> WallGrowth;
        internal static ConfigEntry<string> Handed;
        internal static ConfigEntry<float> Scale;
        internal static ConfigEntry<float> Through;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Bearing = config.Bind("Beastly", "Bearing",
                "damage=0.01,aim=0.01,stamina=0.01,swing=0.01,speed=0.005,dodge=0.5,"
                + "crit=0.5,block=0.3,critdamage=0.01,cooldown=0.01,mind=0.5,body=0.2",
                "How a beast's six numbers reach the numbers anybody can read off it. Without "
                + "this the six were ours alone: we showed them, we counted our own damage by "
                + "them, and the game went on working from the template — so a bear with forty "
                + "of strength struck exactly as hard as one with twenty. Each entry is what a "
                + "point of an attribute is worth: strength to the blow itself, precision to "
                + "aim, endurance to stamina, agility to swing, speed and dodging, precision "
                + "also to critical chance and blocking, wit to critical damage and cooldown, "
                + "will to resisting the mind and the body. Strength went to aim until now, "
                + "which meant a beast struck the same at its first level and at its "
                + "hundredth. Health is settled apart, by its own line above.");
            Enabled = config.Bind("Beastly", "Enabled", true,
                "Reckon a creature the way a man is reckoned: by stats. The game gives beasts "
                + "none at all, so the whole of the combat model passes them by — there is "
                + "nothing to pierce and no reason to choose one weapon over another.");

            Stats = config.Bind("Beastly", "Stats",
                "Small=3,4,16,12,2,2;Medium=8,10,18,10,3,3;Large=30,26,8,8,4,4;"
                + "Giant=50,40,4,6,5,5;Titanic=70,55,3,5,6,6",
                "The six stats each size starts with, at level nothing: strength, endurance, "
                + "agility, precision, intelligence, will. Small and middling creatures lead "
                + "with agility — a wolf is quick, not strong; large ones lead with strength.");

            Growth = config.Bind("Beastly", "Growth",
                "Small=0.05,0.08,0.30,0.20,0.03,0.03;Medium=0.12,0.20,0.40,0.20,0.05,0.05;"
                + "Large=0.55,0.45,0.10,0.12,0.06,0.06;Giant=0.80,0.60,0.05,0.10,0.08,0.08;"
                + "Titanic=1.00,0.75,0.04,0.08,0.10,0.10",
                "What each stat gains per level, in the same order.");

            Claw = config.Bind("Beastly", "Claw",
                "Small=2.0,Medium=2.2,Large=2.0,Giant=3.0,Titanic=4.0",
                "What a claw or a jaw weighs, in kilograms — the same figure a weapon carries. "
                + "A bear's claw is no heavier than a wolf's; what makes a bear is the arm "
                + "behind it. A creature holding a real weapon uses the weapon's weight.");

            Bulk = config.Bind("Beastly", "Bulk",
                "Small=1,Medium=2,Large=6,Giant=25,Titanic=60",
                "How much meat there is, as a multiple on endurance. The health formula is the "
                + "one men use; what men do not have is bulk, and a minotaur has ten times a "
                + "man's at the same endurance.");

            Walls = config.Bind("Beastly", "Walls",
                "Small=5,Medium=15,Large=60,Giant=150,Titanic=300",
                "The deduction a hide is worth. Small and middling stand still — they are what "
                + "one levels on, and a rat should not turn a sword. Large and above climb with "
                + "level: that is the hunt.");

            WallGrowth = config.Bind("Beastly", "WallGrowth", 0.015f,
                new ConfigDescription(
                    "How much a large creature's hide thickens per level. Small and middling are "
                    + "not touched by this.",
                    new AcceptableValueRange<float>(0f, 0.2f)));

            Handed = config.Bind("Beastly", "Handed",
                "DesertDragon=80,3000,30,20,50,20,3.0,700;"
                + "ForestDragon=80,3000,30,20,50,20,3.0,700;"
                + "LavaDragon=80,3000,30,20,50,20,3.0,700;"
                + "Mountain Dragon=80,3000,30,20,50,20,3.0,700;"
                + "TheKingOfRot=80,1500,30,20,50,20,3.0,400",
                "Creatures written by hand: the six stats, then the claw in kilograms, then the "
                + "deduction. These are the peaks, met once, and a peak is not what the "
                + "arithmetic happened to give — it is what was meant. "
                + "Their level is not written here: it comes out of the stats, the other way "
                + "round from every other creature. Three thousand of endurance puts a dragon "
                + "near the six hundredth level, three times higher than anything else alive, "
                + "and that is the point of a dragon. "
                + "Seven hundred is exactly the wall of fifth-tier plate: a dragon is harnessed "
                + "like the best harness in the world, and what opens it is what opens plate.");

            Scale = config.Bind("Beastly", "Scale", 0.185f,
                new ConfigDescription(
                    "What a point of stat is worth in levels, for creatures whose stats are "
                    + "written by hand. A man at the fiftieth level carries about two hundred "
                    + "and seventy across the six, so a point is worth about a fifth of a level.",
                    new AcceptableValueRange<float>(0.01f, 5f)));

            Through = config.Bind("Beastly", "Through", 0.7f,
                new ConfigDescription(
                    "What share of a crushing blow goes through a hide whatever its thickness. "
                    + "Seven tenths, as a scale shirt: a hide is not plate, and a hammer carries "
                    + "through flesh better than through steel.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Telling = config.Bind("Beastly", "Telling", false,
                "Write each kind of creature to the log once with what it came out as.");
        }

        // ------------------------------------------------------------ разбор настроек

        private static readonly Dictionary<string, float[]> stats = new Dictionary<string, float[]>();
        private static readonly Dictionary<string, float[]> growth = new Dictionary<string, float[]>();
        private static readonly Dictionary<string, float> claw = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> bulk = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> walls = new Dictionary<string, float>();
        private static readonly Dictionary<string, float[]> handed = new Dictionary<string, float[]>();
        private static string read;

        private static void Learn()
        {
            string written = Stats.Value + "|" + Growth.Value + "|" + Claw.Value + "|"
                + Bulk.Value + "|" + Walls.Value + "|" + Handed.Value;

            if (written == read) return;
            read = written;

            Six(Stats.Value, stats);
            Six(Growth.Value, growth);
            One(Claw.Value, claw);
            One(Bulk.Value, bulk);
            One(Walls.Value, walls);

            handed.Clear();
            foreach (string one in (Handed.Value ?? "").Split(';'))
            {
                string[] halves = one.Split('=');
                if (halves.Length != 2) continue;

                string[] eight = halves[1].Split(',');
                if (eight.Length < 8) continue;

                float[] got = new float[8];
                bool fine = true;

                for (int i = 0; i < 8; i++)
                {
                    if (!float.TryParse(eight[i].Trim(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out got[i])) fine = false;
                }

                if (fine) handed[halves[0].Trim().ToLowerInvariant()] = got;
            }
        }

        private static void Six(string written, Dictionary<string, float[]> into)
        {
            into.Clear();

            foreach (string one in (written ?? "").Split(';'))
            {
                string[] halves = one.Split('=');
                if (halves.Length != 2) continue;

                string[] parts = halves[1].Split(',');
                if (parts.Length < 6) continue;

                float[] got = new float[6];
                bool fine = true;

                for (int i = 0; i < 6; i++)
                {
                    if (!float.TryParse(parts[i].Trim(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out got[i])) fine = false;
                }

                if (fine) into[halves[0].Trim()] = got;
            }
        }

        private static void One(string written, Dictionary<string, float> into)
        {
            into.Clear();

            foreach (string one in (written ?? "").Split(','))
            {
                string[] halves = one.Split('=');
                if (halves.Length != 2) continue;

                float much;
                if (float.TryParse(halves[1].Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out much))
                {
                    into[halves[0].Trim()] = much;
                }
            }
        }

        // ------------------------------------------------------------ счёт

        /// <summary>Шесть статов этого зверя: сила, выносливость, ловкость, точность, ум, воля.</summary>
        internal static float[] Six(UnitSize size, int level)
        {
            Learn();


            float[] first, step;
            if (!stats.TryGetValue(size.ToString(), out first)) return null;
            if (!growth.TryGetValue(size.ToString(), out step)) step = new float[6];

            float[] got = new float[6];
            for (int i = 0; i < 6; i++) got[i] = first[i] + step[i] * level;

            return got;
        }

        /// <summary>Шесть статов этого зверя с учётом того, что писано руками.</summary>
        /// <summary>Adds what a tamed beast has had spent on it, if anything.</summary>
        private static float[] Spent(UnitAttribute who, float[] six)
        {
            if (six == null) return null;

            try
            {
                int[] more = Taming.Bought(who);
                if (more == null) return six;

                for (int i = 0; i < 6 && i < more.Length; i++) six[i] += more[i];
            }
            catch
            {
            }

            return six;
        }

        internal static float[] Mine(UnitAttribute who)
        {
            if (who == null) return null;

            float[] hand = Hand(who.Data != null ? who.Data.unitname : null);

            if (hand != null)
            {
                float[] got = new float[6];
                Array.Copy(hand, got, 6);
                return Spent(who, got);
            }

            // Табличная основа считается от того уровня, каким зверя взяли, а не от нынешнего:
            // нынешний сам выводится из шести чисел, и считать одно из другого по кругу нельзя.
            int at = who.Data != null ? who.Data.level : 1;

            try
            {
                int born = Taming.Born(who);
                if (born > 0) at = born;
            }
            catch
            {
            }

            float[] table = Six(who.size, at);

            // И то, что в зверя вложили руками: у прирученных шесть чисел не только выводятся
            // из роста, но и растут тем, что в них вкладывает хозяин.
            return Spent(who, table);
        }

        /// <summary>
        /// Уровень, выведенный из статов. Только для тех, кому статы писаны руками: у прочих
        /// всё наоборот, статы выводятся из уровня.
        /// </summary>
        internal static int Ranked(string name)
        {
            float[] hand = Hand(name);
            if (hand == null) return 0;

            float sum = 0f;
            for (int i = 0; i < 6; i++) sum += hand[i];

            return Mathf.Max(1, Mathf.RoundToInt(sum * Scale.Value));
        }

        /// <summary>Каким статом зверь этого роста бьёт: ноль сила, два ловкость.</summary>
        internal static int Leads(UnitSize size)
        {
            return size >= UnitSize.Large ? 0 : 2;
        }

        private static float[] Hand(string name)
        {
            Learn();

            string plain = (name ?? "").ToLowerInvariant();

            foreach (KeyValuePair<string, float[]> one in handed)
            {
                if (plain.Contains(one.Key)) return one.Value;
            }

            return null;
        }

        /// <summary>Пробитие зверя: стат × вес того, чем бьёт × мастерство.</summary>
        internal static float Piercing(UnitAttribute who, float mastery)
        {
            try
            {
                if (who == null || who is HumaniodUnit) return 0f;

                string name = who.Data != null ? who.Data.unitname : null;
                float[] hand = Hand(name);

                float[] six = Six(who.size, who.Data != null ? who.Data.level : 1);
                if (six == null) return 0f;

                float stat = hand != null ? hand[0] : six[Leads(who.size)];
                float kilos = hand != null ? hand[6] : Kilos(who);

                return stat * kilos * (1f + mastery / 100f);
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>Чем зверь бьёт: настоящим оружием, если оно есть, иначе когтем по росту.</summary>
        internal static float Paw(UnitAttribute who)
        {
            return Kilos(who);
        }

        private static float Kilos(UnitAttribute who)
        {
            Learn();

            try
            {
                if (who.weapons != null)
                {
                    foreach (Weapon arm in who.weapons)
                    {
                        if (arm == null) continue;

                        // Оружие из базы — значит настоящее, с весом.
                        UIWeaponInfo blade = UIItemDatabase.Instance != null && arm.index > 0
                            ? UIItemDatabase.Instance.GetByID(arm.index) as UIWeaponInfo
                            : null;

                        if (blade != null && blade.weight > 0f) return blade.weight;
                    }
                }
            }
            catch
            {
            }

            float got;
            return claw.TryGetValue(who.size.ToString(), out got) ? got : 2.0f;
        }

        /// <summary>Вычет шкуры.</summary>
        internal static float Wall(UnitAttribute who)
        {
            try
            {
                if (who == null || who is HumaniodUnit) return 0f;

                float[] hand = Hand(who.Data != null ? who.Data.unitname : null);
                if (hand != null) return hand[7];

                Learn();

                float first;
                if (!walls.TryGetValue(who.size.ToString(), out first)) return 0f;

                // Мелкие и средние стоят ровно: на них учатся, и шкура им не защита.
                if (who.size < UnitSize.Large) return first;

                int level = who.Data != null ? who.Data.level : 1;
                return first * (1f + level * WallGrowth.Value);
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>Здоровье по человеческой формуле, где выносливость помножена на тушу.</summary>
        private static readonly Dictionary<string, float> bearing =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        private static string bearingRead;

        private static float Worth(string name)
        {
            string written = Bearing.Value ?? "";

            if (written != bearingRead)
            {
                bearingRead = written;
                bearing.Clear();

                foreach (string one in written.Split(','))
                {
                    string[] cut = one.Split('=');
                    if (cut.Length < 2) continue;

                    float much;
                    if (!float.TryParse(cut[1].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out much))
                    {
                        continue;
                    }

                    bearing[cut[0].Trim()] = much;
                }
            }

            float got;
            return bearing.TryGetValue(name, out got) ? got : 0f;
        }

        /// <summary>
        /// Lets a beast's six numbers reach the numbers it fights by.
        ///
        /// До сих пор они были наши и только наши: мы их показывали, по ним же считали свой
        /// урон когтя и свою переноску — а игра всё это время работала от шаблона. Оттого
        /// медведь с сорока силы бил ровно так же, как медведь с двадцатью, и вложенное
        /// хозяином не значило ничего.
        ///
        /// Здесь они доходят до конца. Правится готовое, уже посчитанное игрой: доля к удару,
        /// к замаху, к бегу, к уклонению — то же, что делает с человеком его собственная
        /// шестёрка, только применённое снаружи.
        /// </summary>
        internal static void Bear(UnitAttribute who)
        {
            if (who == null || who is HumaniodUnit) return;

            try
            {
                float[] six = Mine(who);
                if (six == null || six.Length < 6) return;

                // Сила множит удар — тем же счётом, каким множит его человеку: у того
                // «MeleeDamageMD += Strength * 0.01», и на это число игра домножает урон
                // оружия. Зверю через поле это не передать: урон игра считает внутри
                // «WriteUnitAttribute», а мы стоим после неё, и правка поля опоздала бы на
                // целый пересчёт. Потому множим уже посчитанное — число то же, рука другая.
                // Накопиться оно не может: урон всякий раз считается заново из «BSdamage».
                //
                // До этой правки сила уходила в меткость, и зверь бил одинаково что на
                // первом уровне, что на сотом: весь его рост не доходил до удара никак.
                float more = 1f + six[0] * Worth("damage");

                if (more > 1f && who.weapons != null)
                {
                    foreach (Weapon arm in who.weapons)
                    {
                        if (arm == null || arm.damage == null) continue;

                        foreach (KeyValuePair<DamageType, Damage> one in arm.damage)
                        {
                            if (one.Value == null) continue;

                            one.Value.minDamage *= more;
                            one.Value.maxDamage *= more;
                        }
                    }
                }

                // Меткость — от точности: попадает не тот, кто силён, а тот, кто верно бьёт.
                who.attack *= 1f + six[3] * Worth("aim");

                // Дыхание и замах считаются не здесь: у них есть свои ручки, и положенное в
                // них расходится куда надо — запас по своей мерке, время удара по каждой руке
                // отдельно. Домножать готовое значило менять подпись в окне, а не сам удар.

                who.dodge += six[2] * Worth("dodge");
                who.crit += six[3] * Worth("crit");
                who.block += six[3] * Worth("block");

                who.critMultiple += six[4] * Worth("critdamage");
                who.cooldownReduce += six[4] * Worth("cooldown");

                who.MDR += six[5] * Worth("mind");
                who.PDR += six[5] * Worth("body");
            }
            catch
            {
            }
        }

        internal static float Health(UnitAttribute who)
        {
            try
            {
                if (who == null || who is HumaniodUnit) return 0f;

                int level = who.Data != null ? who.Data.level : 1;

                // По настоящим шести, а не по одной таблице: вложенное хозяином должно
                // доходить до здоровья, иначе выносливость покупается впустую.
                float[] six = Mine(who);
                if (six == null) return 0f;

                Learn();

                float much;
                if (!bulk.TryGetValue(who.size.ToString(), out much)) much = 1f;

                return 100f + level * 6f + 6f * six[1] * much;
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>Кладёт запас зверя в ту ручку, из которой игра выводит предел.</summary>
        internal static void Vessel(UnitAttribute who)
        {
            if (Enabled == null || !Enabled.Value) return;

            try
            {
                if (who == null || who is HumaniodUnit) return;

                float want = Health(who);
                if (want <= 0f) return;

                Dials.Holds(who, want);
            }
            catch
            {
            }
        }

        /// <summary>И резвость зверя — в счёт шага, до того как игра его сочтёт.</summary>
        internal static void Hastens(UnitAttribute who)
        {
            if (Enabled == null || !Enabled.Value) return;

            try
            {
                if (who == null || who is HumaniodUnit) return;

                float[] six = Mine(who);
                if (six == null || six.Length < 3) return;

                Pace.Help(who, six[2] * Worth("speed"));

                Dials.Swings(who, 1f + six[2] * Worth("swing"));
                Dials.Breathes(who, 1f + six[1] * Worth("stamina"));
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// Здоровье зверя — по человеческой формуле, где выносливость помножена на тушу.
    ///
    /// Игровое проставлено поштучно: на уровень выходит от девятнадцати очков до пятидесяти
    /// девяти, и лестницы в этом нет. Своё кладём в ту же ручку, из которой игра выводит
    /// предел, — до того, как она возьмётся считать.
    ///
    /// Прежде мы переписывали её итог и следом делили жизнь в прежней доле. На бумаге это
    /// сходилось, на деле — нет: по итогу игра тут же правит и текущую жизнь, а итог к тому
    /// мигу был ещё её. Наш предел стоял ниже игрового, разницу она честно доливала в жизнь,
    /// мы делили обратно — и с каждым пересчётом зверь прибавлял. Неподвижной точкой у этой
    /// лестницы был полный запас, а пересчётов в бою десятки: всякий удар, всякая наложенная
    /// и спавшая метка. Отсюда и брался волк, который лечится на глазах и не умирает.
    /// </summary>
    [HarmonyPatch(typeof(UnitAttribute), "WriteUnitAttribute")]
    internal static class Health_Beastly_Patch
    {
        private static void Postfix(UnitAttribute __instance)
        {
            if (Beastly.Enabled == null || !Beastly.Enabled.Value) return;

            try
            {
                if (__instance == null || __instance is HumaniodUnit) return;

                // Всё прочее, что считается по тем же шести: урон, защита, ход.
                Beastly.Bear(__instance);
            }
            catch
            {
            }
        }
    }
}
