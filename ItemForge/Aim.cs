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
    /// Where a blow was meant to go, and where it actually went.
    ///
    /// Место попадания игра разыгрывала жребием — и жребий этот ничего не знал о том, как
    /// человек замахнулся. Между тем знает сама анимация: у каждого оружия шесть движений, и
    /// они идут в разные места. Рапира колет в лицо, булава обрушивается к земле, у двуручного
    /// меча два замаха через голову и один рубящий до самых ног.
    ///
    /// Раскладка ниже не выдумана — она снята с игры. Тысяча ударов, каждый записан в кадре
    /// своего попадания: где оказался бьющий конец оружия относительно роста бойца. Замахи
    /// сравниваются внутри своего же оружия, а не по общей мерке: у кинжала и у алебарды
    /// «высоко» — это разные метры, но одинаковая доля.
    ///
    /// Так замах становится прицелом. А попадёт ли удар туда, куда метил, решает рука: мастер
    /// кладёт удар в задуманное место почти всегда, новичок мажет на соседнее. Промахнувшийся
    /// вверх уходит выше головы, вниз — в землю.
    /// </summary>
    internal static class Aim
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Table;
        internal static ConfigEntry<float> Base;
        internal static ConfigEntry<float> PerMastery;
        internal static ConfigEntry<float> PerPrecision;
        internal static ConfigEntry<float> PerArmour;
        internal static ConfigEntry<float> PerDodge;
        internal static ConfigEntry<float> Steadiest;
        internal static ConfigEntry<bool> Chooses;
        internal static ConfigEntry<float> Cunning;
        internal static ConfigEntry<float> Nimble;
        internal static ConfigEntry<float> Wisest;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Aim", "Enabled", true,
                "Let the swing decide where the blow is aimed, instead of drawing the place out "
                + "of a hat. The game rolls for it and the roll knows nothing of how the man "
                + "swung; the animation knows exactly.");

            Table = config.Bind("Aim", "Table",
                "1H_Sword=2|0,3|1;1H_Dagger=0|2|1;1H_Mace=1|2|0;1H_Rapier=4|1,5,3|2;"
                + "1H_Spear=2|5,1,0|3;Katana=4|3|0;2HAxe=2,4|0,5|1,3;2HSpear=1,5|3,0|4,2;"
                + "2HSword_Estoc=2|0,1|3;2HSword_Greatsword=3,5|4,1|2,0;Stick=0|1,5,4|2;"
                + "Shield_Sword=0|2|1;Shield_Mace=0|2|1;Shield_Dagger=2|1|0;"
                + "Dual_Sword=2|1|0;Dual_Dagger=1|2|0;Dual_Rapier=1|0|2;Dual_Spear=1|2|0;"
                + "Dual_Mace=2|1|0;Unarmed=2|0|1",
                "Which swing of which weapon goes where, written as «branch = legs | body | "
                + "head», the numbers being the game's own swing numbers. Measured rather than "
                + "invented: a thousand blows, each recorded in the frame it landed, by where the "
                + "striking end of the weapon stood against the height of the man. A branch "
                + "missing from this list falls back to the roll.");

            Base = config.Bind("Aim", "Base", 0.40f,
                new ConfigDescription(
                    "How often a man with no skill at all puts the blow where he meant to. Four "
                    + "times in ten; the rest land somewhere next to it.",
                    new AcceptableValueRange<float>(0f, 1f)));

            PerMastery = config.Bind("Aim", "PerMastery", 0.005f,
                new ConfigDescription(
                    "What each point of mastery in that weapon adds to it. Half a percent, so a "
                    + "full hundred is worth fifty — the difference between a man who has held "
                    + "the thing and a man who has lived with it.",
                    new AcceptableValueRange<float>(0f, 0.05f)));

            PerPrecision = config.Bind("Aim", "PerPrecision", 0f,
                new ConfigDescription(
                    "And what each point of precision adds.",
                    new AcceptableValueRange<float>(0f, 0.05f)));

            PerArmour = config.Bind("Aim", "PerArmour", 0.002f,
                "What a point of the steadiness armour grants is worth to the chance of "
                + "landing a blow. The same weight a point of precision carries, since it is "
                + "the same thing by another road: middling iron to lean on instead of a "
                + "steadier eye.");

            PerDodge = config.Bind("Aim", "PerDodge", 0.004f,
                new ConfigDescription(
                    "What each point of the target's dodging takes away. A nimble man spoils the "
                    + "aim by moving, not by being hard to hurt.",
                    new AcceptableValueRange<float>(0f, 0.05f)));

            Steadiest = config.Bind("Aim", "Steadiest", 0.95f,
                new ConfigDescription(
                    "And the most anyone can ever be sure of. Nobody places every blow.",
                    new AcceptableValueRange<float>(0.1f, 1f)));

            Chooses = config.Bind("Aim", "Chooses", true,
                "Let a fighter pick where to strike instead of swinging wherever the animation "
                + "happens to go. He looks at what stands between him and the man — a bare head, "
                + "a thin skirt of leather, a cuirass of three thousand — and chooses the swing "
                + "that goes to the worst of it. The picture follows the choice, because the "
                + "choice is made by picking the swing itself, not by bending anything.");

            Cunning = config.Bind("Aim", "Cunning", 0.008f,
                new ConfigDescription(
                    "What each point of mastery adds to the chance of finding the gap. At eight "
                    + "thousandths a full hundred is worth four fifths: a master reads armour "
                    + "almost every time, a farmhand swings where the arm takes him.",
                    new AcceptableValueRange<float>(0f, 0.05f)));

            Nimble = config.Bind("Aim", "Nimble", 0.01f,
                new ConfigDescription(
                    "What one point of agility adds to the chance of putting the blow where it "
                    + "was aimed. Skill says which gap to go for; the hand decides whether it "
                    + "gets there. A hundredth a point, so a quick man lands his choice where "
                    + "a clumsy one swings at what he meant and hits what he did not.",
                    new AcceptableValueRange<float>(0f, 0.05f)));

            Wisest = config.Bind("Aim", "Wisest", 0.9f,
                new ConfigDescription(
                    "And the most anyone ever reads it. Even a master swings blind sometimes.",
                    new AcceptableValueRange<float>(0f, 1f)));
        }

        // Места сверху вниз. Соседство считается по этому порядку: сбитый удар в голову уходит
        // в грудь, а не в стопу.
        private static readonly int[] Down =
        {
            Anatomy.Head, Anatomy.Chest, Anatomy.Pants
        };

        // Что было задумано этим замахом, и кем.
        private static UnitAttribute meant;
        private static int aimed = -1;

        /// <summary>Remembers what the swing now beginning is aimed at.</summary>
        internal static void Intend(UnitAttribute who)
        {
            meant = null;
            aimed = -1;

            if (!Enabled.Value || who == null || who.ani == null) return;

            try
            {
                string branch = Branch(who);
                if (branch == null) return;

                int set = Mathf.RoundToInt(who.ani.GetFloat("attackset"));

                // Сперва даём бойцу выбрать. Выбор делается подменой номера замаха, поэтому
                // картинка сходится с намерением сама: он и правда бьёт тем движением, которое
                // идёт туда, куда он метит.
                int chosen = Pick(who, branch);

                if (chosen >= 0 && chosen != set)
                {
                    set = chosen;
                    who.ani.SetFloat("attackset", set);
                }

                int slot;
                if (!Read().TryGetValue(branch + "#" + set, out slot)) return;

                meant = who;
                aimed = slot;
            }
            catch
            {
            }
        }

        /// <summary>Where this blow actually lands, or minus one when it is not ours to say.</summary>
        internal static int Land(UnitAttribute who, UnitAttribute target)
        {
            if (!Enabled.Value || aimed < 0) return -1;
            if (who == null || (object)who != (object)meant) return -1;

            try
            {
                float sure = Steady(who as HumaniodUnit, target);

                if (UnityEngine.Random.value <= sure) return aimed;

                // Сбитый удар уходит на соседнее место, вверх или вниз. Дальше соседнего не
                // уходит: замах, шедший в голову, может лечь в грудь, но не в стопу.
                int at = System.Array.IndexOf(Down, aimed);
                if (at < 0) return aimed;

                int step = UnityEngine.Random.value < 0.5f ? -1 : 1;
                int next = at + step;

                if (next < 0 || next >= Down.Length) return aimed;

                return Down[next];
            }
            catch
            {
                return aimed;
            }
        }

        /// <summary>
        /// Куда бить: где у противника меньше осталось.
        ///
        /// Драка добивает то, что уже повреждено, — так дерётся всякий, кто видит, за какую
        /// руку враг держится. А у целого человека меньше всего в голове, и туда бьёт тот, кто
        /// умеет: стрелок — всегда, потому что стрелку ничто не мешает выбрать.
        ///
        /// Сколько железа стоит на этом месте, не спрашиваем: добить надрубленную ногу вернее,
        /// чем искать голое место.
        /// </summary>
        private static int Choose(UnitAttribute who)
        {
            if (!Chooses.Value) return -1;

            try
            {
                UnitAttribute mark = who.Target;
                if (mark == null) return -1;

                HumaniodUnit man = who as HumaniodUnit;
                if (man == null) return -1;

                if (UnityEngine.Random.value > Wit(man)) return -1;

                int best = -1;
                float worst = float.MaxValue;

                foreach (int slot in Down)
                {
                    float much = Left(mark, slot);
                    if (much < worst) { worst = much; best = slot; }
                }

                return best;
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>Which swing this fighter chooses, or minus one when he does not choose.</summary>
        private static int Pick(UnitAttribute who, string branch)
        {
            if (!Chooses.Value) return -1;

            try
            {
                UnitAttribute mark = who.Target;
                if (mark == null) return -1;

                HumaniodUnit man = who as HumaniodUnit;
                if (man == null) return -1;

                int best = Choose(who);
                if (best < 0) return -1;

                // И замах, который туда идёт. Если у оружия такого нет — бьём как придётся.
                List<int> able;
                if (!Swings().TryGetValue(branch + "@" + best, out able) || able.Count == 0)
                {
                    return -1;
                }

                return able[UnityEngine.Random.Range(0, able.Count)];
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>How well this hand reads what the other man is wearing.</summary>
        private static float Wit(HumaniodUnit who)
        {
            try
            {
                if (who.Data == null) return 0f;

                float much = 0f;

                if (who.Data.weaponMastery != null)
                {
                    int branch = (int)who.weapontype;

                    if (branch >= 0 && branch < who.Data.weaponMastery.Length)
                    {
                        much += who.Data.weaponMastery[branch] * Cunning.Value;
                    }
                }

                // Замысел — дело умения: увидеть, за какую руку враг держится, и ударить
                // туда. Исполнение — дело руки, и оно считается отдельно, когда удар уже
                // пошёл.
                return Mathf.Clamp(much, 0f, Wisest.Value);
            }
            catch
            {
                return 0f;
            }
        }

        private static readonly Dictionary<string, List<int>> swings =
            new Dictionary<string, List<int>>();

        /// <summary>Which swings of a branch go to which place.</summary>
        private static Dictionary<string, List<int>> Swings()
        {
            Read();
            return swings;
        }

        /// <summary>
        /// Сколько жизни осталось у этого места. Пока части считаются, берём их; если счёт
        /// по частям выключен, ориентируемся по железу, как прежде.
        /// </summary>
        private static float Left(UnitAttribute mark, int slot)
        {
            try
            {
                if (Limb.Counts(mark))
                {
                    Limb.Body body = Limb.Of(mark);

                    if (body.known)
                    {
                        if (slot == Anatomy.Head) return body.now[Limb.Head];
                        if (slot == Anatomy.Chest) return body.now[Limb.Chest];

                        if (slot == Anatomy.Pants)
                        {
                            return Mathf.Min(body.now[Limb.LegL], body.now[Limb.LegR]);
                        }

                        if (slot == Anatomy.Arms)
                        {
                            return Mathf.Min(body.now[Limb.ArmL], body.now[Limb.ArmR]);
                        }
                    }
                }

                float wear;
                UIArmorInfo coat = Anatomy.Worn(mark, slot, out wear);

                return coat != null ? coat.durability * wear : 0f;
            }
            catch
            {
                return float.MaxValue;
            }
        }

        /// <summary>How sure this hand is of putting the blow where it meant to.</summary>
        private static float Steady(HumaniodUnit who, UnitAttribute target)
        {
            float sure = Base.Value;

            try
            {
                if (who != null && who.Data != null)
                {
                    int branch = (int)who.weapontype;

                    if (who.Data.weaponMastery != null
                        && branch >= 0 && branch < who.Data.weaponMastery.Length)
                    {
                        // От половины: ниже пятидесяти рука ещё не своя и штрафует,
                        // выше — ведёт. Мастерство из пробития ушло сюда целиком.
                        sure += (who.Data.weaponMastery[branch] - 50) * PerMastery.Value;
                    }

                    // Попадёт ли туда, куда метил, решает рука. Глаз решает другое — попадёт
                    // ли вообще, — и это игра считает своей меткостью, куда восприятие и идёт.
                    sure += who.Data.agility * Nimble.Value;
                    sure += who.Data.precision * PerPrecision.Value;

                    // Доспех среднего веса ведёт руку, тяжёлый ей мешает. Считаем по
                    // надетому, а не по игровому полю меткости: этого поля наш расчёт
                    // не касается вовсе, а в него входит ещё многое помимо доспеха.
                    sure += Garb.Hand(who) * PerArmour.Value;
                }

                if (target != null) sure -= target.dodge * PerDodge.Value;
            }
            catch
            {
            }

            return Mathf.Clamp(sure, 0.05f, Steadiest.Value);
        }

        /// <summary>The animator branch this fighter is swinging in.</summary>
        private static string Branch(UnitAttribute who)
        {
            HumaniodUnit man = who as HumaniodUnit;
            if (man == null) return null;

            UIWeaponInfo blade = man.equipmentmanger != null
                ? man.equipmentmanger.Weapon_mainhand : null;

            string sub = blade != null ? blade.AnimationSubType.ToString() : "none";

            switch (man.weapontype)
            {
                case WeaponType.unarmed: return "Unarmed";
                case WeaponType.daul: return "Dual_" + Kind(sub);
                case WeaponType.twohand:
                    if (sub == "Estoc") return "2HSword_Estoc";
                    if (sub == "GreatSwrod") return "2HSword_Greatsword";
                    if (sub == "Staff" || sub == "Stick") return "Stick";
                    return "2HAxe";
                case WeaponType.polearms: return "2HSpear";
                case WeaponType.onehand:
                    if (man.weapontype2 == WeaponType.shield) return "Shield_" + Kind(sub);
                    return "1H_" + Kind(sub);
                default: return null;
            }
        }

        // Имена веток в аниматоре и имена подтипов у игры сходятся почти всюду, кроме катаны:
        // она стоит отдельным семейством, а не разновидностью одноручного.
        private static string Kind(string sub)
        {
            switch (sub)
            {
                case "Sword": return "Sword";
                case "Rapier": return "Rapier";
                case "Dagger": return "Dagger";
                case "Mace": return "Mace";
                case "Spear": return "Spear";
                default: return "Sword";
            }
        }

        private static readonly Dictionary<string, int> table = new Dictionary<string, int>();
        private static string read;

        private static Dictionary<string, int> Read()
        {
            string written = Table.Value ?? "";
            if (written == read) return table;

            read = written;
            table.Clear();
            swings.Clear();

            foreach (string one in written.Split(';'))
            {
                string[] halves = one.Split('=');
                if (halves.Length != 2) continue;

                string branch = halves[0].Trim();
                string[] zones = halves[1].Split('|');
                if (branch.Length == 0 || zones.Length < 3) continue;

                int[] places = { Anatomy.Pants, Anatomy.Chest, Anatomy.Head };

                for (int i = 0; i < 3; i++)
                {
                    foreach (string set in zones[i].Split(','))
                    {
                        int number;
                        if (!int.TryParse(set.Trim(), NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out number)) continue;

                        table[branch + "#" + number] = places[i];

                        string where = branch + "@" + places[i];

                        List<int> had;
                        if (!swings.TryGetValue(where, out had))
                        {
                            had = new List<int>();
                            swings[where] = had;
                        }

                        had.Add(number);
                    }
                }
            }

            return table;
        }
    }

    // Замах только что выбран — самое время узнать, куда он метит.
    [HarmonyPatch(typeof(HumaniodUnit), "BasicAttack")]
    internal static class BasicAttack_Aim_Patch
    {
        private static void Postfix(HumaniodUnit __instance)
        {
            try { Aim.Intend(__instance); }
            catch { }
        }
    }
}
