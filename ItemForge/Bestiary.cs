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
    /// Makes the rare beasts of this world worth meeting.
    ///
    /// Химера, горгона, минотавр, циклоп, грифон, мантикора — двадцать три создания, каждое
    /// названо и сделано отдельно, и каждое умирает так же буднично, как разбойник у дороги.
    /// Тролли того же рода: четверо на всю игру, и все четверо — обычные орки по числам.
    ///
    /// Здесь они перестают быть проходными. Правится не живое существо, а его шаблон, и
    /// правится один раз за запуск: запас сил и урон растут во столько раз, во сколько сказано,
    /// а вместе с ними растёт и «power» — число, из которого игра считает опыт за убийство и в
    /// одиночку, и на весь отряд. Так награда идёт следом за опасностью сама, без отдельного
    /// правила.
    ///
    /// Шансы — попадания, блока, уклонения, крита — и сопротивления остаются нетронутыми
    /// нарочно. Это доли, а не запасы: утроенное уклонение делает существо не втрое опаснее, а
    /// неуязвимым, и бой превращается в ожидание.
    /// </summary>
    internal static class Bestiary
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Races;
        internal static ConfigEntry<string> Names;
        internal static ConfigEntry<string> Might;
        internal static ConfigEntry<bool> ExpFollows;
        internal static ConfigEntry<string> Schools;
        internal static ConfigEntry<int> Rank;
        internal static ConfigEntry<float> Mana;

        private static bool done;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Bestiary", "Enabled", true,
                "Let the world's rare beasts be worth meeting. They are hand-made, one at a "
                + "time, and they die like roadside bandits.");

            Races = config.Bind("Bestiary", "Races", "mythological=3",
                "Whole races and what they are multiplied by, separated by commas. The "
                + "mythological are the twenty-three named creatures — chimera, gorgon, "
                + "minotaur, cyclops, griffon, manticore, the centaurs, the were-beasts and "
                + "the rest. Names are the game's own.");

            Names = config.Bind("Bestiary", "Names",
                "Troll=2,Troll_Trub=2,Troll_Armored=2,MountainTroll=2",
                "And single creatures by name, for those a race cannot pick out. The four "
                + "trolls are ordinary orcs as far as the game is concerned, and there is no "
                + "way to raise them without raising every orc with them. A name here wins "
                + "over a race above.");

            Might = config.Bind("Bestiary", "Might",
                "mythological=100,Troll=50,Troll_Trub=50,Troll_Armored=50,MountainTroll=50",
                "The least strength a creature may have, written as «race or name = strength». "
                + "Beasts in this game have no attributes at all — their blueprints hold health, "
                + "stamina and defence, and not one of the six — so the game reads their strength "
                + "as one. A troll could therefore carry three kilograms, and its own club "
                + "outweighed it threefold; being crushed under its own weapon, it never swung "
                + "again, though it went on casting. This is the floor that makes a giant as "
                + "strong as it looks. Creatures named here are also left out of the reckoning of "
                + "load altogether: they get neither the penalty of a heavy pack nor the "
                + "advantage of travelling light, because a troll with one club would otherwise "
                + "count as unburdened and move like a dancer.");

            ExpFollows = config.Bind("Bestiary", "ExpFollows", true,
                "Let the reward follow the danger. Experience for a kill is worked out from a "
                + "creature's «power», so raising that by the same amount gives three times the "
                + "experience for a thing three times as hard, with no second rule to keep.");

            Schools = config.Bind("Bestiary", "Schools", "",
                "Creatures that know a school of magic, by name. Empty by default: dragons were "
                + "given schools here for a while, and it turned out to be the wrong door. Their "
                + "fire is not a spell — it is written into the bite itself, half the damage of "
                + "it, and weapon damage never passes through the magic multiplier. A school "
                + "would have added a second, separate fire beside the one they already breathe.");

            Rank = config.Bind("Bestiary", "Rank", 10,
                new ConfigDescription(
                    "The rank the school is known at — both the spells themselves and the "
                    + "mastery that opens them. A spell's rank decides what it does, and these "
                    + "are creatures of the fifty-fifth level; they should not cast like "
                    + "apprentices.",
                    new AcceptableValueRange<int>(1, 15)));

            Mana = config.Bind("Bestiary", "Mana", 10f,
                new ConfigDescription(
                    "How much the mana of a schooled creature is multiplied by. A dragon is "
                    + "given a hundred points of it, which is one spell and then silence — the "
                    + "school would be a decoration. Only creatures in the list above are "
                    + "touched.",
                    new AcceptableValueRange<float>(1f, 100f)));
        }

        // ------------------------------------------------------------------ школы

        private static readonly Dictionary<string, SkillSet> schooled =
            new Dictionary<string, SkillSet>();

        private static string schoolRead;
        private static readonly HashSet<int> taught = new HashSet<int>();

        /// <summary>
        /// Gives a creature the spells of the school it was always supposed to have.
        ///
        /// Заклинания живут не в шаблоне существа, а на нём самом, поэтому выдаются не разом при
        /// загрузке, а каждому появившемуся — один раз. Вместе с ними снимаются два замка, без
        /// которых выданное так и осталось бы лежать: игра по умолчанию позволяет существу
        /// пускать в ход только то, что стоит у него на панели, и не даёт колдовать само.
        /// </summary>
        /// <summary>Which creatures know which school, by name.</summary>
        internal static Dictionary<string, SkillSet> Schooled()
        {
            string written = Schools.Value ?? "";

            if (written != schoolRead)
            {
                schoolRead = written;
                schooled.Clear();

                foreach (string one in written.Split(','))
                {
                    string[] halves = one.Split('=');
                    if (halves.Length != 2) continue;

                    string key = halves[0].Trim();
                    string name = halves[1].Trim();
                    if (key.Length == 0 || name.Length == 0) continue;

                    try { schooled[key] = (SkillSet)Enum.Parse(typeof(SkillSet), name, true); }
                    catch { ItemForgePlugin.Log.LogWarning($"Школы «{name}» в игре нет."); }
                }
            }

            return schooled;
        }

        // Кому мы выставили силу — тех груз не касается вовсе.
        private static readonly HashSet<int> shaped = new HashSet<int>();

        /// <summary>The floor of strength this creature is owed, or nought.</summary>
        internal static float Floor(UnitAttribute who)
        {
            if (!Enabled.Value || who == null || who.info == null || who.Data == null) return 0f;

            try
            {
                Dictionary<string, float> byMight = Read(Might.Value);
                if (byMight.Count == 0) return 0f;

                float much;

                if (who.info.name != null && byMight.TryGetValue(who.info.name, out much)) return much;
                if (byMight.TryGetValue(who.Data.race.ToString(), out much)) return much;
            }
            catch
            {
            }

            return 0f;
        }

        /// <summary>Whether load should be reckoned for this creature at all.</summary>
        internal static bool Unburdened(UnitAttribute who)
        {
            return Floor(who) > 0f;
        }

        /// <summary>Gives a beast the strength its bulk implies.</summary>
        internal static void Steel(UnitAttribute who)
        {
            if (who == null || who.Data == null) return;

            NPCSaveData mind = who.Data as NPCSaveData;
            if (mind == null || mind.humanAttribute == null) return;

            float floor = Floor(who);
            if (floor <= 0f) return;

            if (!shaped.Add(who.GetInstanceID())) return;

            try
            {
                int want = Mathf.Clamp(Mathf.RoundToInt(floor), 1, 100);
                if (mind.humanAttribute.BSstrength >= want) return;

                mind.humanAttribute[0] = want;

                if (mind.humanAttribute.potential < mind.humanAttribute.Sum)
                {
                    mind.humanAttribute.potential = mind.humanAttribute.Sum;
                }

                ItemForgePlugin.Log.LogInfo("«" + who.info.name + "»: сила поднята до " + want
                    + " — своей у зверья нет вовсе, и предел переноски выходил в три килограмма.");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог дать зверю силы: " + e.Message);
            }
        }

        internal static void Teach(UnitAttribute who)
        {
            if (!Enabled.Value || who == null || who.info == null) return;
            if (who.spellmanger == null) return;

            Dictionary<string, SkillSet> list = Schooled();
            if (list.Count == 0) return;

            SkillSet school;
            if (!list.TryGetValue(who.info.name ?? "", out school)) return;

            if (!taught.Add(who.GetInstanceID())) return;

            try
            {
                // Знак магии ставится первым. Без него школа складывается в данные и не
                // показывается, а заклинания лежат нетронутыми: игра не считает такого
                // способным колдовать вовсе.
                if (who.Data != null && !who.Data.isMagician) who.Data.isMagician = true;

                // Мастерство школы — это и есть открытая ветка. Одни заклинания без него лежат
                // мёртвым грузом: их некому пустить в ход.
                bool opened = false;

                UITalentDatabase lore = UITalentDatabase.Instance;

                if (lore != null && who.talentmanger != null)
                {
                    UITalentInfo mark = lore.GetMasteryTalent(school);

                    if (mark != null && !who.talentmanger.ContainTalent(mark))
                    {
                        who.talentmanger.AddTalent(mark, Rank.Value);
                        opened = true;
                    }
                }

                UISpellDatabase book = UISpellDatabase.Instance;
                if (book == null || book.spells == null) return;

                int given = 0;

                foreach (UISpellInfo spell in book.spells)
                {
                    if (spell == null || spell.SkillSet != school) continue;
                    if (who.spellmanger.ContainSpell(spell)) continue;

                    who.spellmanger.AddSpell(spell, Rank.Value);
                    given++;
                }

                if (given > 0 || opened)
                {
                    who.spellmanger.onlyActionBar = false;
                    who.spellmanger.useAutoCast = true;

                    ItemForgePlugin.Log.LogInfo($"«{who.info.name}» владеет школой {school}: "
                        + $"мастерство {(opened ? "открыто" : "уже было")}, "
                        + $"заклинаний {given} на ступени {Rank.Value}, "
                        + $"мана {who.maxmp:0}.");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог обучить зверя: " + e.Message);
            }
        }

        /// <summary>Raises the named creatures in the catalogue, once.</summary>
        internal static void Raise()
        {
            if (done || !Enabled.Value) return;

            try
            {
                UIUnitDatabase db;
                try { db = UIUnitDatabase.Instance; }
                catch { return; }

                if (db == null || db.indexes == null) return;

                done = true;

                Dictionary<string, float> byRace = Read(Races.Value);
                Dictionary<string, float> byName = Read(Names.Value);

                if (byRace.Count == 0 && byName.Count == 0) return;

                int touched = 0;
                Dictionary<string, int> counted = new Dictionary<string, int>();

                foreach (UnitInfo who in db.indexes)
                {
                    if (who == null) continue;

                    float much = 1f;
                    string why = null;

                    // Имя решает раньше расы: тролля иначе не отделить от орка.
                    if (who.name != null && byName.TryGetValue(who.name, out much)) why = who.name;
                    else if (byRace.TryGetValue(who.race.ToString(), out much)) why = who.race.ToString();
                    else continue;

                    if (Mathf.Approximately(much, 1f)) continue;

                    who.BShp *= much;
                    who.BSsp *= much;
                    who.BSmp *= much;

                    // Урон существа лежит не в шаблоне, а на его модели, поэтому поднимается
                    // не он сам, а общая прибавка к урону по каждому виду: игра считает её как
                    // «единица плюс прибавка», так что двойка даёт втрое.
                    if (who.BSdamageTypeBonus != null)
                    {
                        for (int i = 0; i < who.BSdamageTypeBonus.Length; i++)
                        {
                            who.BSdamageTypeBonus[i] += much - 1f;
                        }
                    }

                    if (ExpFollows.Value) who.power = Mathf.RoundToInt(who.power * much);

                    touched++;

                    int had;
                    counted.TryGetValue(why, out had);
                    counted[why] = had + 1;
                }

                // Мана тем, кому выдана школа. Дракону положено сто единиц — этого хватает на
                // одно заклинание и молчание до конца боя, и школа была бы украшением.
                Dictionary<string, SkillSet> list = Schooled();

                if (list.Count > 0 && Mana.Value > 1f)
                {
                    int filled = 0;

                    foreach (UnitInfo who in db.indexes)
                    {
                        if (who == null || who.name == null) continue;
                        if (!list.ContainsKey(who.name)) continue;

                        who.BSmp *= Mana.Value;
                        filled++;
                    }

                    if (filled > 0)
                    {
                        ItemForgePlugin.Log.LogInfo($"Маны налито: {filled} существам, "
                            + $"в {Mana.Value:0.#} раза.");
                    }
                }

                if (touched > 0)
                {
                    List<string> said = new List<string>();
                    foreach (KeyValuePair<string, int> one in counted)
                    {
                        said.Add($"{one.Key} — {one.Value}");
                    }

                    ItemForgePlugin.Log.LogInfo("Зверьё подросло: " + string.Join(", ", said.ToArray()) + ".");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог поднять зверьё: " + e);
            }
        }

        private static Dictionary<string, float> Read(string written)
        {
            Dictionary<string, float> got = new Dictionary<string, float>();

            foreach (string one in (written ?? "").Split(','))
            {
                string[] halves = one.Split('=');
                if (halves.Length != 2) continue;

                string key = halves[0].Trim();
                if (key.Length == 0) continue;

                float much;
                if (!float.TryParse(halves[1].Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out much)) continue;

                got[key] = Mathf.Clamp(much, 0.1f, 50f);
            }

            return got;
        }
    }

    // Заклинания выдаются живому существу, а не шаблону, поэтому цепляемся за пересчёт — место,
    // через которое проходит всякий, кто появился в мире. Дальше отбор идёт по имени, и каждому
    // достаётся ровно один раз.
    [HarmonyPatch(typeof(UnitAttribute), "WriteUnitAttribute")]
    internal static class WriteUnitAttribute_Bestiary_Patch
    {
        private static void Postfix(UnitAttribute __instance)
        {
            try { Bestiary.Steel(__instance); }
            catch { }

            try { Bestiary.Teach(__instance); }
            catch { }
        }
    }
}
