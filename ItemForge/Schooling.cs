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
    /// Gives a grown man a school to his name.
    ///
    /// Школа в этой игре достаётся из книги, и потому её почти ни у кого нет: книги редки, а
    /// раздаёт их игра только тем, кого сама и придумала как особенных. Оттого ветеран
    /// сорокового уровня дерётся ровно тем же, чем и новобранец, — числами, а не умением, и
    /// уровень означает только запас здоровья.
    ///
    /// Здесь уровень означает выучку. К двадцатому человек знает школу, к сороковому две, и так
    /// до пяти. Какие именно — решает не жребий, а то, чем он живёт: маг идёт по магическим,
    /// двуручник по берсерку и бойцу, щитоносец по защитнику, стрелок по меткому. Список на
    /// каждый склад лежит в настройках по порядку, и первым берётся то, что человеку ближе всего.
    ///
    /// Школа здесь — это право и панель, а не готовые приёмы: она открывает ветку, а не
    /// наполняет её. Чем её наполнить, чтобы неигровые ею действительно дрались, — отдельная
    /// работа, и она впереди.
    /// </summary>
    internal static class Schooling
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Steps;
        internal static ConfigEntry<string> Paths;
        internal static ConfigEntry<bool> Mastery;
        internal static ConfigEntry<int> PerRank;
        internal static ConfigEntry<bool> TouchPlayer;
        internal static ConfigEntry<bool> GiveSpells;
        internal static ConfigEntry<bool> SpellsForPlayer;
        internal static ConfigEntry<int> MagicianSchools;
        internal static ConfigEntry<float> WitsPerStep;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Schooling", "Enabled", true,
                "Let a man's level give him schools. The game hands them out in books and almost "
                + "nobody gets one, so a veteran of the fortieth level fights exactly like a "
                + "recruit and his level is nothing but a pile of health.");

            Steps = config.Bind("Schooling", "Steps", "20,40,60,80,100",
                "The levels at which the first, second, third and further schools arrive.");

            Paths = config.Bind("Schooling", "Paths",
                "magician=black,white,fire,ice,lightning;"
                + "twohand=berserker,fighter,commander,defender,duelist;"
                + "polearms=defender,fighter,commander,berserker,duelist;"
                + "shield=defender,fighter,commander,berserker,duelist;"
                + "onehand=duelist,fighter,rogue,commander,defender;"
                + "daul=duelist,rogue,ronin,fighter,berserker;"
                + "range=marksman,ranger,rogue,fighter,duelist;"
                + "unarmed=battlemonk,berserker,fighter,duelist,commander",
                "What each kind of fighter learns, and in what order. Groups are separated by "
                + "semicolons, schools inside a group by commas. A magician goes down the magic "
                + "list whatever he holds; everyone else is sorted by the weapon the game says "
                + "he fights with. Names are the game's own, from both the fighting schools and "
                + "the magical ones.");

            Mastery = config.Bind("Schooling", "Mastery", true,
                "Grant the school's mastery talent along with the school itself, so the branch "
                + "is opened rather than merely listed.");

            PerRank = config.Bind("Schooling", "PerRank", 10,
                new ConfigDescription(
                    "How many levels are worth one rank of that mastery. At ten, a man of fifty "
                    + "holds his schools at the fifth rank. The game's own ceiling is fifteen.",
                    new AcceptableValueRange<int>(1, 100)));

            TouchPlayer = config.Bind("Schooling", "TouchPlayer", true,
                "Include your own character and your companions. They are characters too, and "
                + "the rule was asked for all of them.");

            GiveSpells = config.Bind("Schooling", "GiveSpells", true,
                "Hand out the school's own skills along with the school, and let the bearer use "
                + "them unasked. This is what makes a schooled man fight differently instead of "
                + "merely owning a panel: the game's fighters are given almost nothing to choose "
                + "from, so they swing and nothing else. Weapon skills are spells here too, so "
                + "this covers the blade as well as the flame.");

            SpellsForPlayer = config.Bind("Schooling", "SpellsForPlayer", false,
                "And the same for your own character and companions. Off: a school opened for "
                + "you is a road to walk, and handing you every skill on it at once would walk "
                + "it for you. The enemy has no such objection.");

            MagicianSchools = config.Bind("Schooling", "MagicianSchools", 1,
                new ConfigDescription(
                    "How many schools a magician is given, however high he rises. One: a mage "
                    + "goes deep rather than wide, and five schools at once would make him a "
                    + "collection of tricks instead of a master of one art.",
                    new AcceptableValueRange<int>(1, 5)));

            WitsPerStep = config.Bind("Schooling", "WitsPerStep", 1.0f,
                new ConfigDescription(
                    "What a magician gets instead of every school he was not given: this much "
                    + "again of the experience that goes into his intelligence. At one, the "
                    + "fortieth level doubles it, the sixtieth trebles it, and so on — the depth "
                    + "he does not spend sideways he spends downward.",
                    new AcceptableValueRange<float>(0f, 10f)));
        }

        // Кому что уже роздано. Ключ — существо, значение — сколько школ ему досталось: пока
        // уровень не перешагнул следующую ступень, делать нечего и спрашивать нечего.
        private static readonly Dictionary<int, int> served = new Dictionary<int, int>();

        /// <summary>Gives a character the schools his standing has earned.</summary>
        internal static void Teach(HumaniodUnit who)
        {
            if (!Enabled.Value || who == null) return;

            NPCSaveData mind = who.Data;
            if (mind == null || mind.humanAttribute == null) return;

            if (!TouchPlayer.Value && Own(who)) return;

            int[] rungs = Rungs();
            if (rungs.Length == 0) return;

            int want = 0;
            foreach (int rung in rungs)
            {
                if (mind.level >= rung) want++;
            }

            if (want <= 0) return;

            // Маг идёт вглубь, а не вширь: школа у него одна, сколько бы ступеней он ни прошёл.
            // То, что не ушло в новые школы, уходит в разум — это считается ниже, при начислении
            // опыта, и потому здесь просто обрезается.
            if (mind.isMagician) want = Mathf.Min(want, Mathf.Max(1, MagicianSchools.Value));

            int had;
            int key = who.GetInstanceID();
            if (served.TryGetValue(key, out had) && had >= want) return;

            try
            {
                List<SkillSet> path = Path(who, mind);
                if (path == null || path.Count == 0) { served[key] = want; return; }

                if (mind.skillSet == null) mind.skillSet = new List<SkillSet>();

                int given = 0;
                int reach = Mathf.Clamp(mind.level / Mathf.Max(1, PerRank.Value), 1, 15);

                for (int i = 0; i < want && i < path.Count; i++)
                {
                    SkillSet school = path[i];

                    if (!mind.skillSet.Contains(school))
                    {
                        mind.skillSet.Add(school);
                        given++;
                    }

                    if (Mastery.Value) Open(who, school, reach);
                    if (GiveSpells.Value && (SpellsForPlayer.Value || !Own(who))) Arm(who, school, reach);
                }

                served[key] = want;

                if (given > 0)
                {
                    ItemForgePlugin.Log.LogInfo($"«{mind.unitname}» ({mind.level}-й уровень) "
                        + $"получил школ: {given}, всего {Mathf.Min(want, path.Count)} "
                        + $"на ступени {reach}.");
                }
            }
            catch (Exception e)
            {
                served[who.GetInstanceID()] = want;
                ItemForgePlugin.Log.LogWarning("Не смог дать школу: " + e.Message);
            }
        }

        // Талант мастерства школы — это и есть открытая ветка. Без него школа значится в данных
        // и не значит ничего: панель строится, а взять из неё нечего.
        private static void Open(HumaniodUnit who, SkillSet school, int rank)
        {
            if (who.talentmanger == null) return;

            UITalentDatabase book = UITalentDatabase.Instance;
            if (book == null) return;

            UITalentInfo mark = book.GetMasteryTalent(school);
            if (mark == null || who.talentmanger.ContainTalent(mark)) return;

            who.talentmanger.AddTalent(mark, rank);
        }

        /// <summary>
        /// Gives the school's own skills to the one who holds it, and lets him use them.
        ///
        /// Открытая ветка сама по себе ничего не меняет: неигровому персонажу выбирать всё
        /// равно не из чего, потому что заклинаний и приёмов ему никто не выдавал. Оттого
        /// разбойник со школой дуэлянта дерётся точно так же, как разбойник без неё.
        ///
        /// Приёмы оружия в этой игре — те же заклинания, просто с набором от двухсот, поэтому
        /// одна раздача покрывает и клинок, и пламя. И два замка снимаются вместе с ней: игра
        /// по умолчанию позволяет пускать в ход только то, что стоит на панели, и не даёт
        /// колдовать без приказа.
        /// </summary>
        private static void Arm(HumaniodUnit who, SkillSet school, int rank)
        {
            if (who.spellmanger == null) return;

            // Знак магии — отдельный замок перед магической школой: без него заклинания лежат
            // выданными и нетронутыми, потому что игра не считает такого способным колдовать.
            // Боевых школ это не касается, их приёмы идут от оружия.
            if (Magic(school) && who.Data != null && !who.Data.isMagician)
            {
                who.Data.isMagician = true;
            }

            UISpellDatabase book = UISpellDatabase.Instance;
            if (book == null || book.spells == null) return;

            int given = 0;

            foreach (UISpellInfo spell in book.spells)
            {
                if (spell == null || spell.SkillSet != school) continue;
                if (who.spellmanger.ContainSpell(spell)) continue;

                who.spellmanger.AddSpell(spell, rank);
                given++;
            }

            if (given <= 0) return;

            who.spellmanger.onlyActionBar = false;
            who.spellmanger.useAutoCast = true;
        }

        /// <summary>Which list of schools this one walks down.</summary>
        private static List<SkillSet> Path(HumaniodUnit who, NPCSaveData mind)
        {
            Dictionary<string, List<SkillSet>> ways = Ways();

            List<SkillSet> found;

            // Маг идёт по магическим, что бы ни держал в руках: школа ему по складу ума, а не
            // по хвату. Прочие — по тому, чем игра считает их вооружёнными.
            if (mind.isMagician && ways.TryGetValue("magician", out found)) return found;

            string kind = who.weapontype.ToString();
            if (ways.TryGetValue(kind, out found)) return found;

            // Хвата в списке нет — берём первый боевой, какой есть, чтобы человек не остался
            // ни с чем из-за колчана или лютни в руках.
            foreach (KeyValuePair<string, List<SkillSet>> way in ways)
            {
                if (way.Key != "magician") return way.Value;
            }

            return null;
        }

        /// <summary>
        /// How much more of his experience a magician pours into his mind.
        ///
        /// Вширь ему расти некуда — школа одна, — и каждая пройденная ступень, за которую
        /// боец получил бы новую школу, у мага обращается в глубину: опыт, идущий в разум,
        /// множится. На сороковом вдвое, на шестидесятом втрое, и так далее.
        /// </summary>
        internal static float Wits(HumaniodUnit who)
        {
            if (!Enabled.Value || WitsPerStep.Value <= 0f || who == null) return 1f;

            NPCSaveData mind = who.Data;
            if (mind == null || !mind.isMagician) return 1f;

            if (!TouchPlayer.Value && Own(who)) return 1f;

            int[] rungs = Rungs();
            if (rungs.Length == 0) return 1f;

            int passed = 0;
            foreach (int rung in rungs)
            {
                if (mind.level >= rung) passed++;
            }

            int spare = passed - Mathf.Max(1, MagicianSchools.Value);
            if (spare <= 0) return 1f;

            return 1f + WitsPerStep.Value * spare;
        }

        // Школы магии в игре пронумерованы от сотни, боевые — до неё, оружейные — от двухсот.
        private static bool Magic(SkillSet school)
        {
            int number = (int)school;
            return number >= 101 && number < 200;
        }

        private static bool Own(UnitAttribute who)
        {
            if ((object)who == (object)gameManager.currentplayUnit) return true;
            if (who.inParty || who.isTempFollower) return true;

            return who.Data != null && who.Data.team == Faction.player;
        }

        // ------------------------------------------------------------------ разбор настроек

        private static int[] rungs;
        private static string rungRead;

        private static int[] Rungs()
        {
            string written = Steps.Value ?? "";
            if (rungs != null && written == rungRead) return rungs;

            List<int> got = new List<int>();

            foreach (string one in written.Split(','))
            {
                int step;
                if (int.TryParse(one.Trim(), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out step) && step > 0)
                {
                    got.Add(step);
                }
            }

            got.Sort();

            rungRead = written;
            rungs = got.ToArray();
            return rungs;
        }

        private static Dictionary<string, List<SkillSet>> ways;
        private static string wayRead;

        private static Dictionary<string, List<SkillSet>> Ways()
        {
            string written = Paths.Value ?? "";
            if (ways != null && written == wayRead) return ways;

            Dictionary<string, List<SkillSet>> got = new Dictionary<string, List<SkillSet>>();

            foreach (string group in written.Split(';'))
            {
                string[] halves = group.Split('=');
                if (halves.Length != 2) continue;

                string kind = halves[0].Trim();
                if (kind.Length == 0) continue;

                List<SkillSet> list = new List<SkillSet>();

                foreach (string one in halves[1].Split(','))
                {
                    string name = one.Trim();
                    if (name.Length == 0) continue;

                    try { list.Add((SkillSet)Enum.Parse(typeof(SkillSet), name, true)); }
                    catch { ItemForgePlugin.Log.LogWarning($"Школы «{name}» в игре нет."); }
                }

                if (list.Count > 0) got[kind] = list;
            }

            wayRead = written;
            ways = got;
            return ways;
        }
    }

    // Школы раздаются на пересчёте — месте, через которое проходит всякий, кто появился и
    // всякий, кто вырос. Проверка стоит одно обращение к словарю, пока уровень не перешагнул
    // следующую ступень.
    [HarmonyPatch(typeof(HumaniodUnit), "WriteUnitAttribute")]
    internal static class WriteUnitAttribute_School_Patch
    {
        private static void Postfix(HumaniodUnit __instance)
        {
            try { Schooling.Teach(__instance); }
            catch { }
        }
    }

    // Опыт в разум магу идёт быстрее ровно настолько, насколько ему не досталось школ. Правится
    // вход, а не начисленное: дальше игра сама решает, хватило ли на ступень.
    [HarmonyPatch(typeof(HumaniodUnit), "GainAttributeExp")]
    internal static class GainAttributeExp_School_Patch
    {
        private const int Intelligence = 4;

        private static void Prefix(HumaniodUnit __instance, int index, ref float exp)
        {
            if (index != Intelligence || exp <= 0f) return;

            try
            {
                float much = Schooling.Wits(__instance);
                if (much > 1f) exp *= much;
            }
            catch
            {
            }
        }
    }
}
