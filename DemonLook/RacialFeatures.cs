using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace DemonLook
{
    /// <summary>
    /// The demon racial package. Everything here is recomputed at runtime and nothing is
    /// written into the save, so removing the mod simply removes the bonuses instead of
    /// leaving dangling references behind.
    /// </summary>
    internal static class Racial
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> ThreatMultiplier;
        internal static ConfigEntry<float> DarkSpellBonus;
        internal static ConfigEntry<float> LightSpellPenalty;
        internal static ConfigEntry<float> ExpMultiplier;

        internal static ConfigEntry<float> HpPerPoint;
        internal static ConfigEntry<float> BonusPerPoint;
        internal static ConfigEntry<float> FreeHp;
        internal static ConfigEntry<float> BonusCap;

        internal static ConfigEntry<int> Intimidate;
        internal static ConfigEntry<bool> Tribute;
        internal static ConfigEntry<int> TributeStep;
        internal static ConfigEntry<int> TributeTier;
        internal static ConfigEntry<string> TributeLadder;
        internal static ConfigEntry<int> TributeCap;
        internal static ConfigEntry<string> TributeRaces;
        internal static ConfigEntry<bool> Diagnostics;

        // Indices into UnitAttribute.DamageTypeMD, which is addressed as (AddonAttribute - 400).
        private const int PositiveDamageIndex = 7; // light
        private const int NegativeDamageIndex = 8; // dark

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Racial", "Enabled", true,
                "Apply the demon racial bonuses. Turn off to keep only the appearance.");

            ThreatMultiplier = config.Bind("Racial", "ThreatMultiplier", 3.0f,
                "Threat is multiplied by this. 3.0 is the plus two hundred percent aggression.");

            DarkSpellBonus = config.Bind("Racial", "DarkSpellBonus", 1.0f,
                "Added to negative (dark) spell damage. 1.0 doubles it.");

            LightSpellPenalty = config.Bind("Racial", "LightSpellPenalty", 1.0f,
                "Subtracted from positive (light) spell damage. 1.0 cancels it entirely.");

            ExpMultiplier = config.Bind("Racial", "ExpMultiplier", 1f,
                "Experience is multiplied by this. One, and the demon learns at the same pace "
                + "as anyone else. It was seven tenths once, to pay for what he takes from the "
                + "dead; the price turned out to be felt everywhere and understood nowhere, so "
                + "it was dropped. Lower it here to bring the toll back.");

            HpPerPoint = config.Bind("HealthToStrength", "HpPerPoint", 5f,
                "How many health points count as one step. Five is what one point of endurance grants.");

            BonusPerPoint = config.Bind("HealthToStrength", "BonusPerPoint", 0.005f,
                "Strength share granted per step. 0.005 is the half a percent per five health.");

            FreeHp = config.Bind("HealthToStrength", "FreeHp", 150f,
                "Health below this does not count, so the bonus rewards invested endurance and gear "
                + "rather than base body and character level. Set to 0 to count all health.");

            BonusCap = config.Bind("HealthToStrength", "BonusCap", 0.60f,
                "Upper limit on the health bonus. 0.60 caps it at plus sixty percent.");

            Tribute = config.Bind("Tribute", "Enabled", true,
                "Let the demon take strength from the men he kills. Only men: beasts and "
                + "monsters are meat, and a demon grows on what it takes from people.");

            TributeStep = config.Bind("Tribute", "Step", 100,
                new ConfigDescription(
                    "Men killed for the first point of strength. Each further tier of ten "
                    + "points costs this much again on top: a hundred a point for the first "
                    + "ten, two hundred for the next ten, three hundred for the ten after.",
                    new AcceptableValueRange<int>(1, 100000)));

            TributeLadder = config.Bind("Tribute", "Ladder",
                "10,21,34,55,89,144,233,377,610,987",
                "What one point of strength costs in souls, by the tier of points already "
                + "taken. Fibonacci, from ten to a thousand: the first ten points come at ten "
                + "souls apiece and the last ten at nine hundred and eighty seven, so a "
                + "hundred points is five and twenty thousand dead. This is not the reward for "
                + "a campaign, it is the sum of a life.");

            TributeTier = config.Bind("Tribute", "Tier", 10,
                new ConfigDescription(
                    "How many points make up one tier before the price rises again.",
                    new AcceptableValueRange<int>(1, 1000)));

            TributeRaces = config.Bind("Tribute", "Races", "human,elf,dwarf,orc",
                "Whose death counts, by the game's own race names. Speaking peoples only: a "
                + "beast under his hand is a dead beast and nothing more. The rest of the "
                + "list, should you want it: bruteman, lizard, fairy, undead, mythological, "
                + "animal, insect, demon.");

            TributeCap = config.Bind("Tribute", "Cap", 100,
                new ConfigDescription(
                    "The most strength this can ever grant.",
                    new AcceptableValueRange<int>(1, 1000)));

            Intimidate = config.Bind("Racial", "Intimidate", 10,
                "Floor for the demon's intimidation. The skill is raised to this if it sits lower, "
                + "and left alone if the character has already grown past it. Set to 0 to disable.");

            Diagnostics = config.Bind("Debug", "Diagnostics", true,
                "Log the numbers needed to calibrate the health bonus, once per character.");
        }

        private static readonly System.Collections.Generic.HashSet<UnitRace> counted =
            new System.Collections.Generic.HashSet<UnitRace>();

        private static string listed;

        /// <summary>True when a death of this race is worth anything to him.</summary>
        internal static bool Counts(UnitRace race)
        {
            string written = TributeRaces.Value ?? "";

            if (written != listed)
            {
                listed = written;
                counted.Clear();

                foreach (string one in written.Split(','))
                {
                    string name = one.Trim();
                    if (name.Length == 0) continue;

                    try
                    {
                        counted.Add((UnitRace)Enum.Parse(typeof(UnitRace), name, true));
                    }
                    catch
                    {
                        DemonLookPlugin.Log.LogWarning($"Дань: расы «{name}» в игре нет.");
                    }
                }

                DemonLookPlugin.Log.LogInfo($"Дань берётся с: {written}.");
            }

            return counted.Contains(race);
        }

        private static int taken = -1;
        private static string ledger;

        private static string File
        {
            get
            {
                if (ledger == null)
                {
                    ledger = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "aor.tribute.txt");
                }

                return ledger;
            }
        }

        /// <summary>How many men this demon has killed.</summary>
        internal static int Taken
        {
            get
            {
                if (taken < 0)
                {
                    taken = 0;

                    try
                    {
                        if (System.IO.File.Exists(File))
                        {
                            int read;
                            if (int.TryParse(System.IO.File.ReadAllText(File).Trim(), out read))
                            {
                                taken = Mathf.Max(0, read);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        DemonLookPlugin.Log.LogWarning("Не смог прочесть счёт дани: " + e.Message);
                    }
                }

                return taken;
            }
        }

        /// <summary>Writes one more down.</summary>
        internal static void Took()
        {
            taken = Taken + 1;

            Save();
        }

        private static int innocent = -1;

        /// <summary>
        /// Сколько среди них было мирных.
        ///
        /// Дань берётся со всякого убитого разумного — воин он или нет, сила от него одна и
        /// та же. А вот вражду мира демон наживает не боем: убитый в честной драке наёмник
        /// никого не пугает, это его ремесло. Пугает вырезанный караван и поножовщина в
        /// городе, и считаются они отдельно.
        ///
        /// Мирным считается тот, кто на тебя не шёл: не был врагом игроку и не стоял в чужой
        /// воюющей стороне. Гвардия, если она уже подняла оружие, сюда не попадает.
        /// </summary>
        internal static int Innocent
        {
            get
            {
                if (innocent < 0)
                {
                    innocent = 0;

                    try
                    {
                        string file = File + ".peace";

                        if (System.IO.File.Exists(file))
                        {
                            int read;
                            if (int.TryParse(System.IO.File.ReadAllText(file).Trim(), out read))
                            {
                                innocent = Mathf.Max(0, read);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        DemonLookPlugin.Log.LogWarning("Не смог прочесть счёт мирных: "
                            + e.Message);
                    }
                }

                return innocent;
            }
        }

        /// <summary>Записывает ещё одного мирного.</summary>
        internal static void Slew()
        {
            innocent = Innocent + 1;

            try { System.IO.File.WriteAllText(File + ".peace", innocent.ToString()); }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Не смог записать счёт мирных: " + e.Message);
            }
        }

        /// <summary>Был ли этот убитый мирным — то есть не шёл ли он на тебя сам.</summary>
        internal static bool Peaceful(UnitAttribute who)
        {
            try
            {
                if (who == null || who.Data == null) return false;

                // Тот, кто уже дрался с отрядом, мирным не был.
                if (who.Data.isPlayerEnemy) return false;
                if (who.isEngaged) return false;

                // И тот, чья сторона с отрядом воюет.
                if (FactionManager.Instance != null
                    && FactionManager.Instance.IsNativePlayerEnemy(who)) return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void Save()
        {
            try { System.IO.File.WriteAllText(File, taken.ToString()); }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Не смог записать счёт дани: " + e.Message);
            }
        }

        /// <summary>
        /// What that tribute is worth, in strength.
        ///
        /// Лестница простая и всё более крутая: первые десять очков — по сотне человек за
        /// каждое, следующие десять — по две сотни, потом по три, и так далее. Сотое очко
        /// стоит тысячи, а вся дорога до него — пятидесяти пяти тысяч убитых.
        ///
        /// Считается прямым перебором, а не формулой: перебор читается, а формула с целыми
        /// делениями и остатками — нет, и ошибиться в ней куда легче, чем в десяти строках.
        /// </summary>
        private static float[] ladder;
        private static string ladderRead;

        /// <summary>
        /// Лестница цены: сколько душ стоит одно очко на каждом десятке.
        ///
        /// По Фибоначчи, от десяти до тысячи. Первый десяток очков достаётся по десять душ за
        /// очко, последний — по девятьсот восемьдесят семь: всего до сотни очков выходит
        /// двадцать пять с половиной тысяч убитых, то есть не награда за поход, а итог жизни.
        /// </summary>
        private static float[] Ladder()
        {
            string written = TributeLadder != null ? (TributeLadder.Value ?? "") : "";

            if (written != ladderRead || ladder == null)
            {
                ladderRead = written;

                System.Collections.Generic.List<float> got =
                    new System.Collections.Generic.List<float>();

                foreach (string one in written.Split(','))
                {
                    float much;
                    if (float.TryParse(one.Trim(),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out much)
                        && much > 0f)
                    {
                        got.Add(much);
                    }
                }

                ladder = got.Count > 0 ? got.ToArray() : new float[] { 10f };
            }

            return ladder;
        }

        /// <summary>Во что обходится очко под таким-то счётом уже взятых.</summary>
        private static long Price(int points)
        {
            float[] rungs = Ladder();

            int per = Mathf.Max(1, TributeTier.Value);
            int step = Mathf.Clamp(points / per, 0, rungs.Length - 1);

            return (long)Mathf.Max(1f, rungs[step]);
        }

        internal static int Strength()
        {
            // Дани за убитых больше нет: сила приходит только с выпитыми душами.
            if (!Enabled.Value) return 0;
            return Souls.Points();
        }

        /// <summary>How many more men the next point costs.</summary>
        internal static int Until()
        {
            int points = Strength();
            if (points >= Mathf.Max(1, TributeCap.Value)) return 0;

            long spent = 0;
            for (int i = 0; i < points; i++) spent += Price(i);

            return (int)(spent + Price(points) - Taken);
        }

        internal static bool IsDemon(UnitAttribute unit)
        {
            return Enabled.Value && unit != null && unit.Data != null && unit.Data.race == UnitRace.demon;
        }

        /// <summary>
        /// The health share of the strength bonus. Uses current health, so it falls as the
        /// demon is worn down and recovers as it heals.
        /// </summary>
        internal static float HealthBonus(UnitAttribute unit)
        {
            if (unit == null || unit.Data == null) return 0f;

            float step = HpPerPoint.Value;
            if (step <= 0f) return 0f;

            float counted = unit.Data.currenthp - FreeHp.Value;
            if (counted <= 0f) return 0f;

            float bonus = counted / step * BonusPerPoint.Value;
            return Mathf.Min(bonus, BonusCap.Value);
        }
    }

    // WriteUnitAttribute is where the game recomputes derived stats, so the remaining
    // racial modifiers are stamped on right after it finishes.
    [HarmonyPatch(typeof(UnitAttribute), "WriteUnitAttribute")]
    internal static class WriteUnitAttribute_Patch
    {
        private static void Postfix(UnitAttribute __instance)
        {
            try
            {
                if (!Racial.IsDemon(__instance)) return;

                __instance.threat *= Racial.ThreatMultiplier.Value;

                // Дань за убитых людей больше не пишется в саму характеристику: она висит
                // прибавкой поверх неё. Так надо по двум причинам, и обе тяжёлые.
                //
                // Первая: у характеристик в игре потолок в девяносто девять, и записанная дань
                // в него упиралась — дальше убивать становилось незачем.
                //
                // Вторая: зелье сброса характеристик обнуляет записанное, и вместе с ним
                // сгорала вся дань, собранная за игру. Прибавка же считается заново из числа
                // убитых и пережила бы и сброс, и перезапись.

                float[] byType = __instance.DamageTypeMD;
                if (byType != null && byType.Length > 8)
                {
                    byType[8] += Racial.DarkSpellBonus.Value;   // negative damage, the dark school
                    byType[7] -= Racial.LightSpellPenalty.Value; // positive damage, the light school
                }

                Diagnose(__instance);
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogError("Demon racial modifiers failed: " + e);
            }
        }

        private static readonly HashSet<int> Reported = new HashSet<int>();

        // One line per demon, carrying the numbers needed to pick FreeHp and BonusCap
        // against real values rather than guesses.
        private static void Diagnose(UnitAttribute unit)
        {
            if (!Racial.Diagnostics.Value) return;

            HumaniodUnit human = unit as HumaniodUnit;
            if (human == null) return;

            int id = unit.Data.id;
            if (!Reported.Add(id)) return;

            float bonus = Racial.HealthBonus(unit);

            // Сила теперь ничем не усилена и читается как есть: расовая прибавка к ней снята,
            // потому что от силы зависит грузоподъёмность, и множить её значит втихую выдавать
            // демону лишние килограммы.
            int shown = human.Strength;

            DemonLookPlugin.Log.LogInfo(
                $"Demon stats: level {unit.Data.level}, endurance {human.Endurance}, "
                + $"maxhp {unit.maxhp:0}, currenthp {unit.Data.currenthp:0}, "
                + $"health bonus {bonus * 100f:0.0}%, strength now {shown}, "
                + $"threat {unit.threat:0.0}, intimidate {human.Data.intimidate}, "
                + $"dark {unit.DamageTypeMD[8]:0.00}, light {unit.DamageTypeMD[7]:0.00}");
        }
    }

    // Сила демона: своя, записанная, плюс дань — и дань именно прибавкой.
    //
    // Прибавка входит в само число, которое видит и лист персонажа, и всякий счёт, зависящий
    // от силы. Потолок в девяносто девять её не держит, и зелье сброса ей ничего не делает:
    // она считается из числа убитых всякий раз заново.
    [HarmonyPatch(typeof(HumaniodUnit), "get_Strength")]
    internal static class Strength_Tribute_Patch
    {
        private static void Postfix(HumaniodUnit __instance, ref int __result)
        {
            try
            {
                if (!Racial.IsDemon((UnitAttribute)(object)__instance)) return;

                __result += Racial.Strength();
            }
            catch
            {
            }
        }
    }

    // Living skills are recomputed here, with intimidation coming out as
    // base plus modifiers. The racial floor is applied once that sum is known.
    [HarmonyPatch(typeof(HumaniodUnit), "CalculateLivingSkills")]
    internal static class CalculateLivingSkills_Patch
    {
        private static void Postfix(HumaniodUnit __instance)
        {
            try
            {
                if (!Racial.IsDemon(__instance)) return;

                int floor = Racial.Intimidate.Value;
                if (floor <= 0) return;
                if (__instance.Data.intimidate >= floor) return;

                __instance.Data.intimidate = floor;
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogError("Demon intimidation floor failed: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(HumaniodUnit), "GainExp")]
    internal static class GainExp_Patch
    {
        private static void Prefix(HumaniodUnit __instance, ref int exp)
        {
            try
            {
                if (!Racial.IsDemon(__instance)) return;
                exp = Mathf.RoundToInt(exp * RacialSchool.Rate(__instance));
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogError("Demon experience penalty failed: " + e);
            }
        }
    }
    // Считаем в «Die»: сюда игра приходит один раз на смерть и приносит убийцу, так что
    // добивание ядом или союзником на демона не запишется.
    [HarmonyPatch(typeof(UnitAttribute), "Die", new[] { typeof(UnitAttribute), typeof(bool) })]
    internal static class Die_Tribute_Patch
    {
        private static void Prefix(UnitAttribute __instance, UnitAttribute killer)
        {
            try
            {
                if (!Racial.Tribute.Value || !Racial.Enabled.Value) return;
                if (killer == null || __instance == null || killer == __instance) return;

                // По уже мёртвому «Die» зовут ещё раз — второй раз считать нечего.
                if (__instance.Data == null || __instance.Data.isdead) return;

                if (killer.Data == null || killer.Data.race != UnitRace.demon) return;

                // Только те, кто ходит на двух ногах и говорит. Зверь под рукой — просто
                // мёртвый зверь; дань берётся с народов, а не с мяса.
                if (!Racial.Counts(__instance.Data.race)) return;

                // Силы убитый больше не даёт — её дают только выпитые души. Счёт убитых
                // остаётся для «Похода»: мир замечает того, кто много убивает.
                Racial.Took();

                // И отдельно — был ли он мирным: вражду мира наживают не боем.
                if (Racial.Peaceful(__instance)) Racial.Slew();
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Счёт дани сорвался: " + e.Message);
            }
        }
    }
}
