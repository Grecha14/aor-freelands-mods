using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using spell;
using UnityEngine;

namespace EncounterScale
{
    /// <summary>
    /// What a man learns in a long life of fighting.
    ///
    /// Прежде школы раздавались по чину: всякий босс получал одну и ту же пару — бойца и
    /// огонь, — а драконы и Король Гнили свою. Главарь разбойников, некромант и орочий вождь
    /// дрались одними приёмами, а обычный человек сорокового уровня знал ровно то, с чем его
    /// родила заготовка.
    ///
    /// Здесь школы приходят с годами. На тридцатом уровне человек осваивает новую школу
    /// вполовину, к сорок пятому — целиком. Дальше новые на пятидесятом, семидесятом,
    /// девяностом и сто двадцатом, и каждая так же: вполовину на входе, целиком пятнадцатью
    /// уровнями позже. Чем старше противник, тем больше у него в запасе — не оттого, что он
    /// босс, а оттого, что он прожил дольше.
    ///
    /// Школа выбирается по складу человека: воину боевая, магу магическая. Выбор постоянный
    /// для каждого — встретив его снова, увидишь те же приёмы.
    /// </summary>
    internal static class Tutelage
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Levels;
        internal static ConfigEntry<int> Ramp;
        internal static ConfigEntry<float> Start;
        internal static ConfigEntry<string> Combat;
        internal static ConfigEntry<string> Magic;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Tutelage", "Enabled", true,
                "Let humanoid enemies learn new schools as they grow older, instead of bosses "
                + "all being handed the same two.");

            Levels = config.Bind("Tutelage", "Levels", "30,50,70,90,120",
                "The levels at which a new school is learned, separated by commas.");

            Ramp = config.Bind("Tutelage", "Ramp", 15,
                new ConfigDescription(
                    "How many levels it takes to master a school once begun: from half to "
                    + "whole. Fifteen: begun at thirty, whole at forty five.",
                    new AcceptableValueRange<int>(1, 100)));

            Start = config.Bind("Tutelage", "Start", 0.5f,
                new ConfigDescription(
                    "How much of a school is known on the level it is begun, as a share of its "
                    + "whole.",
                    new AcceptableValueRange<float>(0.05f, 1f)));

            Combat = config.Bind("Tutelage", "Combat",
                "fighter,commander,defender,duelist,rogue,ranger,berserker,marksman,battlemonk,ronin",
                "The schools a fighting man may learn, by the game's own names.");

            Magic = config.Bind("Tutelage", "Magic",
                "fire,ice,lightning,white,air,spirit,necromancy,blood,forest,earth,spatial",
                "The schools a magician may learn. The black school is left out: it is the "
                + "demon's, and belongs to his kind.");

            Telling = config.Bind("Tutelage", "Telling", true,
                "Say in the log, for the first few, what schools a man has learned.");
        }

        private static readonly HashSet<int> taught = new HashSet<int>();
        private static int told;

        /// <summary>Учит человека школам, положенным его уровню.</summary>
        internal static void Teach(UnitAttribute unit)
        {
            if (Enabled == null || !Enabled.Value) return;
            if (unit == null || unit.Data == null || unit.info == null) return;
            if (!(unit is HumaniodUnit)) return;

            if (!taught.Add(unit.GetInstanceID())) return;

            try
            {
                int level = unit.Data.level;
                List<int> starts = Starts();

                bool mage = unit.Data.isMagician;
                List<SkillSet> pool = Pool(mage ? Magic.Value : Combat.Value);
                if (pool.Count == 0) return;

                // Одна и та же последовательность выбора для одного и того же человека.
                System.Random dice = new System.Random(unit.Data.id * 104729 + 17);

                List<string> said = new List<string>();

                for (int k = 0; k < starts.Count; k++)
                {
                    int from = starts[k];
                    if (level < from) break;

                    SkillSet school = Pick(unit, pool, dice);
                    if (school == SkillSet.none) break;

                    float share = Mathf.Clamp01(Start.Value
                        + (1f - Start.Value) * (level - from) / Mathf.Max(1f, Ramp.Value));

                    int given = Learn(unit, school, share);

                    said.Add($"{school} {share * 100f:0}% ({given})");
                }

                if (Telling.Value && said.Count > 0 && told < 25)
                {
                    told++;

                    EncounterScalePlugin.Log.LogInfo($"«{unit.info.name}» уровня {level} знает: "
                        + string.Join(", ", said.ToArray()) + ".");
                }
            }
            catch (Exception e)
            {
                EncounterScalePlugin.Log.LogWarning("Не смог научить школе: " + e.Message);
            }
        }

        private static List<int> Starts()
        {
            List<int> got = new List<int>();

            foreach (string one in (Levels.Value ?? "").Split(','))
            {
                int much;
                if (int.TryParse(one.Trim(), out much) && much > 0) got.Add(much);
            }

            got.Sort();
            return got;
        }

        private static List<SkillSet> Pool(string written)
        {
            List<SkillSet> got = new List<SkillSet>();

            foreach (string one in (written ?? "").Split(','))
            {
                string name = one.Trim();
                if (name.Length == 0) continue;

                try { got.Add((SkillSet)Enum.Parse(typeof(SkillSet), name, true)); }
                catch { }
            }

            return got;
        }

        /// <summary>Школа, которой у человека ещё нет, — из тех, что ему по складу.</summary>
        private static SkillSet Pick(UnitAttribute unit, List<SkillSet> pool, System.Random dice)
        {
            List<SkillSet> free = new List<SkillSet>();

            foreach (SkillSet one in pool)
            {
                if (!Knows(unit, one)) free.Add(one);
            }

            if (free.Count == 0) return SkillSet.none;

            return free[dice.Next(free.Count)];
        }

        private static bool Knows(UnitAttribute unit, SkillSet school)
        {
            try
            {
                UITalentDatabase tdb = UITalentDatabase.Instance;
                if (tdb == null || unit.talentmanger == null) return false;

                UITalentInfo mastery = tdb.GetMasteryTalent(school);
                return mastery != null && unit.talentmanger.ContainTalent(mastery.Name);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Выдаёт школу в этой доле от целого. Возвращает, сколько приёмов вышло.</summary>
        private static int Learn(UnitAttribute unit, SkillSet school, float share)
        {
            int given = 0;

            UITalentDatabase tdb = UITalentDatabase.Instance;
            if (tdb != null && unit.talentmanger != null)
            {
                UITalentInfo mastery = tdb.GetMasteryTalent(school);
                if (mastery != null && !unit.talentmanger.ContainTalent(mastery.Name))
                {
                    int rank = Mathf.Max(1, Mathf.RoundToInt(mastery.maxPoints * share));
                    unit.talentmanger.AddTalent(mastery, rank);
                }
            }

            UISpellDatabase sdb = UISpellDatabase.Instance;
            if (sdb != null && sdb.spells != null && unit.spellmanger != null)
            {
                foreach (UISpellInfo spell in sdb.spells)
                {
                    if (spell == null || spell.SkillSet != school) continue;
                    if (unit.spellmanger.ContainSpell(spell)) continue;

                    int rank = Mathf.Max(1, Mathf.RoundToInt(spell.maxPoints * share));
                    unit.spellmanger.AddSpell(spell, rank);
                    given++;
                }
            }

            return given;
        }
    }
}
