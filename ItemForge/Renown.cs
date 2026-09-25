using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Lets a long war against one kind of enemy leave a mark on the one who fought it.
    ///
    /// Черты «Убийца орков», «Убийца нежити» и восемь подобных в игре есть, написаны и дают по
    /// пятнадцать процентов урона по своей расе. Выдаются они только на арене, за бои по
    /// расписанию, — а тот, кто вырезал в поле тысячу орков, не получает ничего и остаётся ровно
    /// тем же, кем был до первого.
    ///
    /// Здесь счёт ведётся честно: за каждым, кто ходит с вами, записывается, кого он убил и
    /// сколько. Дошло до тысячи — черта его, навсегда. Мифических в этом мире всего двадцать три
    /// вида и встречаются они поодиночке, поэтому им свой порог.
    ///
    /// Счёт хранится рядом с настройками и переживает выход из игры: тысяча убитых — это не
    /// один вечер, и требовать их за один заход значило бы не требовать вовсе.
    /// </summary>
    internal static class Renown
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Needed;
        internal static ConfigEntry<string> Rare;
        internal static ConfigEntry<string> Marks;
        internal static ConfigEntry<bool> PartyOnly;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Renown", "Enabled", true,
                "Let a long war against one kind of enemy be worth something. The game has the "
                + "traits for it already and hands them out only in the arena.");

            Needed = config.Bind("Renown", "Needed", 1000,
                new ConfigDescription(
                    "How many of a race must fall to one man before that race holds no more "
                    + "surprises for him.",
                    new AcceptableValueRange<int>(1, 100000)));

            Rare = config.Bind("Renown", "Rare", "mythological=200",
                "Races that ask for fewer, because there are fewer of them. Twenty-three kinds "
                + "of mythological creature exist in the whole world and they are met one at a "
                + "time; a thousand of them is not a feat, it is an impossibility.");

            Marks = config.Bind("Renown", "Marks",
                "human=SlayerOfHuman,elf=SlayerOfElf,dwarf=SlayerOfDwarf,orc=SlayerOfOrc,"
                + "undead=SlayerOfUndead,bruteman=SlayerOfBruteman,animal=SlayerOfBeast,"
                + "insect=SlayerOfInsect,lizard=SlayerOfLizard,"
                + "mythological=SlayerOfMythological",
                "Which trait answers for which race. Names are the game's own, straight out of "
                + "its own list. Fairies and demons have no such trait written for them, so "
                + "killing them counts towards nothing.");

            PartyOnly = config.Bind("Renown", "PartyOnly", true,
                "Keep the tally only for you and those who travel with you. Every bandit in the "
                + "world could be given one too, but nobody would ever see it and the ledger "
                + "would grow without end.");
        }

        // Счёт: «кто» — «раса» — сколько. Ключ человека собирается из номера и имени: один
        // номер в разных сохранениях достаётся разным людям, имя разводит их почти всегда.
        private static readonly Dictionary<string, Dictionary<UnitRace, int>> tally =
            new Dictionary<string, Dictionary<UnitRace, int>>();

        private static bool loaded;
        private static bool dirty;
        private static float saveAt;

        private static string Ledger
        {
            get { return Path.Combine(BepInEx.Paths.ConfigPath, "aor.kills.tsv"); }
        }

        /// <summary>Writes down one kill, and hands out the trait when the tally is full.</summary>
        internal static void Fell(UnitAttribute dead, UnitAttribute killer)
        {
            if (!Enabled.Value || dead == null || killer == null) return;
            if ((object)dead == (object)killer) return;

            HumaniodUnit hand = killer as HumaniodUnit;
            if (hand == null || hand.Data == null) return;

            if (PartyOnly.Value && !Ours(hand)) return;

            UnitRace race = dead.Data != null ? dead.Data.race : UnitRace.none;
            if (race == UnitRace.none) return;

            string mark;
            if (!Named().TryGetValue(race, out mark)) return;

            Load();

            string who = Who(hand);

            Dictionary<UnitRace, int> book;
            if (!tally.TryGetValue(who, out book))
            {
                book = new Dictionary<UnitRace, int>();
                tally[who] = book;
            }

            int had;
            book.TryGetValue(race, out had);

            int now = had + 1;
            book[race] = now;

            dirty = true;

            int want = Threshold(race);
            if (now != want) return;                 // ровно на пороге, не раньше и не дважды

            Award(hand, mark, race, now);
        }

        private static void Award(HumaniodUnit hand, string mark, UnitRace race, int count)
        {
            try
            {
                UITalentDatabase lore = UITalentDatabase.Instance;
                if (lore == null) return;

                UITalentInfo trait = Find(lore, mark);

                if (trait == null)
                {
                    ItemForgePlugin.Log.LogWarning($"Черты «{mark}» в игре нет.");
                    return;
                }

                if (hand.talentmanger == null || hand.talentmanger.ContainTrait(trait)) return;

                bool taken = hand.talentmanger.AddTrait(trait, false);

                ItemForgePlugin.Log.LogInfo($"«{hand.Data.unitname}» убил {count} — раса {race}. "
                    + $"Черта «{trait.Name}»: {(taken ? "принята" : "не принята")}.");

                if (taken && Ours(hand))
                {
                    GameController.ShowMessage($"{hand.Data.unitname}: {trait.Name}", 3f);
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог выдать черту: " + e.Message);
            }
        }

        private static UITalentInfo Find(UITalentDatabase lore, string mark)
        {
            UITalentInfo[][] shelves = new UITalentInfo[][] { lore.traits, lore.inbornTraits, lore.talents };

            foreach (UITalentInfo[] shelf in shelves)
            {
                if (shelf == null) continue;

                foreach (UITalentInfo one in shelf)
                {
                    if (one != null && string.Equals(one.name, mark, StringComparison.OrdinalIgnoreCase))
                    {
                        return one;
                    }
                }
            }

            return null;
        }

        private static bool Ours(UnitAttribute who)
        {
            if ((object)who == (object)gameManager.currentplayUnit) return true;
            if (who.inParty || who.isTempFollower) return true;

            return who.Data != null && who.Data.team == Faction.player;
        }

        private static string Who(HumaniodUnit hand)
        {
            return hand.Data.id + "/" + (hand.Data.unitname ?? "?");
        }

        private static int Threshold(UnitRace race)
        {
            int few;
            if (Few().TryGetValue(race, out few)) return few;

            return Mathf.Max(1, Needed.Value);
        }

        // ------------------------------------------------------------------ книга счёта

        internal static void Settle()
        {
            if (!dirty || !loaded) return;
            if (Time.unscaledTime < saveAt) return;

            saveAt = Time.unscaledTime + 10f;
            dirty = false;

            try
            {
                StringBuilder text = new StringBuilder();
                text.AppendLine("кто\tраса\tубито");

                foreach (KeyValuePair<string, Dictionary<UnitRace, int>> one in tally)
                {
                    foreach (KeyValuePair<UnitRace, int> row in one.Value)
                    {
                        text.AppendLine(one.Key + "\t" + row.Key + "\t" + row.Value);
                    }
                }

                File.WriteAllText(Ledger, text.ToString(), new UTF8Encoding(true));
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог записать счёт убитых: " + e.Message);
            }
        }

        private static void Load()
        {
            if (loaded) return;
            loaded = true;

            try
            {
                if (!File.Exists(Ledger)) return;

                int rows = 0;

                foreach (string line in File.ReadAllLines(Ledger))
                {
                    string[] cells = line.Split('\t');
                    if (cells.Length != 3) continue;

                    UnitRace race;
                    int count;

                    try { race = (UnitRace)Enum.Parse(typeof(UnitRace), cells[1].Trim(), true); }
                    catch { continue; }

                    if (!int.TryParse(cells[2].Trim(), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out count)) continue;

                    Dictionary<UnitRace, int> book;
                    if (!tally.TryGetValue(cells[0], out book))
                    {
                        book = new Dictionary<UnitRace, int>();
                        tally[cells[0]] = book;
                    }

                    book[race] = count;
                    rows++;
                }

                if (rows > 0) ItemForgePlugin.Log.LogInfo($"Счёт убитых поднят: {rows} строк.");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог поднять счёт убитых: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ разбор настроек

        private static Dictionary<UnitRace, string> named;
        private static string nameRead;

        private static Dictionary<UnitRace, string> Named()
        {
            string written = Marks.Value ?? "";
            if (named != null && written == nameRead) return named;

            Dictionary<UnitRace, string> got = new Dictionary<UnitRace, string>();

            foreach (string one in written.Split(','))
            {
                string[] halves = one.Split('=');
                if (halves.Length != 2) continue;

                try
                {
                    UnitRace race = (UnitRace)Enum.Parse(typeof(UnitRace), halves[0].Trim(), true);
                    got[race] = halves[1].Trim();
                }
                catch
                {
                    ItemForgePlugin.Log.LogWarning($"Расы «{halves[0].Trim()}» в игре нет.");
                }
            }

            nameRead = written;
            named = got;
            return named;
        }

        private static Dictionary<UnitRace, int> few;
        private static string fewRead;

        private static Dictionary<UnitRace, int> Few()
        {
            string written = Rare.Value ?? "";
            if (few != null && written == fewRead) return few;

            Dictionary<UnitRace, int> got = new Dictionary<UnitRace, int>();

            foreach (string one in written.Split(','))
            {
                string[] halves = one.Split('=');
                if (halves.Length != 2) continue;

                int count;
                if (!int.TryParse(halves[1].Trim(), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out count)) continue;

                try
                {
                    UnitRace race = (UnitRace)Enum.Parse(typeof(UnitRace), halves[0].Trim(), true);
                    got[race] = Mathf.Max(1, count);
                }
                catch
                {
                }
            }

            fewRead = written;
            few = got;
            return few;
        }
    }

    // Смерть — единственное место, где известны и павший, и тот, кто его свалил.
    [HarmonyPatch(typeof(UnitAttribute), "Die")]
    internal static class Die_Renown_Patch
    {
        private static void Prefix(UnitAttribute __instance, UnitAttribute killer)
        {
            // До, а не после: родной метод первым делом ставит признак смерти, и по нему уже
            // не отличить только что павшего от давно лежащего.
            try
            {
                if (__instance != null && __instance.Data != null && __instance.Data.isdead) return;

                Renown.Fell(__instance, killer);
            }
            catch
            {
            }
        }
    }
}
