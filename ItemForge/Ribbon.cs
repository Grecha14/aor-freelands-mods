using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;
using LitJson;
using TroopManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ItemForge
{
    /// <summary>
    /// Several bouts open at one arena at once, and the player pages through them.
    ///
    /// Игра держит у арены один бой и один вызов, и окно показывает только их. Здесь у арены
    /// своя лента открытых боёв разных ступеней: раз в неделю бой случайной ступени с первой
    /// по третью, раз в месяц четвёртой, раз в сезон пятой. Листается она кнопками «◄ ►» в
    /// окне арены: какой бой показан, на тот и записываешься. Последняя страница — пустая: на
    /// ней открывается вызов, который игра прячет, пока показан бой.
    ///
    /// Ступень боя пишется в его собственное поле «ранг»: у обычных боёв оно ни на что не
    /// влияет и уходит в сохранение само. Бои, ждущие в ленте, сохраняются в свой файл рядом
    /// с настройками — при каждом сохранении игры.
    /// </summary>
    internal static class Ribbon
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> WeekDays;
        internal static ConfigEntry<int> MonthDays;
        internal static ConfigEntry<int> SeasonDays;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Ribbon", "Enabled", true,
                "Keep several bouts of different rungs open at every arena and let the player "
                + "page through them.");

            WeekDays = config.Bind("Ribbon", "WeekDays", 7,
                new ConfigDescription("Every how many days a bout of the first to third rung opens.",
                    new AcceptableValueRange<int>(1, 365)));

            MonthDays = config.Bind("Ribbon", "MonthDays", 30,
                new ConfigDescription("Every how many days a bout of the fourth rung opens.",
                    new AcceptableValueRange<int>(1, 365)));

            SeasonDays = config.Bind("Ribbon", "SeasonDays", 90,
                new ConfigDescription("Every how many days a bout of the fifth rung opens.",
                    new AcceptableValueRange<int>(1, 1000)));

            Telling = config.Bind("Ribbon", "Telling", true,
                "Say in the log what opened, what was paged to and what was kept.");
        }

        // ----------------------------------------------------------------- лента одной арены

        private sealed class Book
        {
            internal readonly List<ArenaMatch> waiting = new List<ArenaMatch>();
            internal int week = int.MinValue, month = int.MinValue, season = int.MinValue;
        }

        private static readonly Dictionary<string, Book> books = new Dictionary<string, Book>();

        private static Book Of(Arena arena)
        {
            string key = arena.arenaName ?? "";
            Book got;
            if (!books.TryGetValue(key, out got))
            {
                got = new Book();
                books[key] = got;
            }
            return got;
        }

        internal static bool Serves(Arena arena)
        {
            return Enabled != null && Enabled.Value && arena != null && arena.isActive
                && !arena.isPlayerArena && !arena.nomad && !arena.isGortusShrine;
        }

        /// <summary>The rung of a bout of ours, 1 to 5; nought for the game own ones.</summary>
        internal static int RungOf(ArenaMatch m)
        {
            if (m == null || m.isStoryMatch || m.isStoryChallenge) return 0;
            int r = (int)m.rank;
            return r >= 1 && r <= 5 ? r : 0;
        }

        // Пока открывается бой нашей ступени: по ней подбираются бойцы и звери.
        internal static int opening;

        // ----------------------------------------------------------------- открыть бой

        private static int Today()
        {
            try { return TimeManager.TotalDay; } catch { return 0; }
        }

        /// <summary>Opens a bout of this rung, the way the game would open one of its own.</summary>
        internal static ArenaMatch Open(Arena arena, int rung)
        {
            opening = rung;

            try
            {
                int count = arena.GetAllAvailableGladiators().Count;
                int max = Math.Max(1, Math.Min(count, 4));
                int teams = 2;
                int scale;
                ArenaMatchMode mode;

                int dice = UnityEngine.Random.Range(0, 100);

                if (arena.canHoldVenationes && dice < 30)
                {
                    mode = ArenaMatchMode.venationes;
                    teams = 1;
                    scale = UnityEngine.Random.Range(1, 5);
                }
                else if (count > 1 && dice < 60)
                {
                    mode = ArenaMatchMode.tagMatch;
                    scale = Mathf.Clamp(UnityEngine.Random.Range(2, 5), 2, Mathf.Clamp(count, 2, 3));
                }
                else
                {
                    mode = ArenaMatchMode.show;
                    scale = Mathf.Clamp(UnityEngine.Random.Range(1, 6), 1, max);
                }

                if (arena.isWuxia) scale = 1;

                ArenaMatch made = arena.SpawnNewMatch(mode, teams, scale);
                if (made == null) return null;

                made.site = arena;
                made.rank = (HeroRankTitle)rung;

                Lift(made, rung);

                if (Telling.Value)
                {
                    ItemForgePlugin.Log.LogInfo($"Арена «{arena.arenaName}»: открыт бой ступени {rung} "
                        + $"({mode}, уровень {made.level}, награда {made.prize}, дней до начала {made.dayForEntry}).");
                }

                return made;
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог открыть бой арены: " + e.Message);
                return null;
            }
            finally
            {
                opening = 0;
            }
        }

        /// <summary>Brings a bout up to its rung: fighters by their attributes, the purse by the rung.</summary>
        internal static void Lift(ArenaMatch m, int rung)
        {
            int least, most;
            Ladder.Range(rung, out least, out most);

            int sum = 0, count = 0;

            if (m.teams != null)
            {
                foreach (GladiatorTeam team in m.teams)
                {
                    if (team == null || team.members == null) continue;

                    foreach (NPCSaveData one in team.members)
                    {
                        if (one == null) continue;

                        if (one.level < least)
                        {
                            Ladder.Raise(one, UnityEngine.Random.Range(least, most + 1));
                            try { one.CalculatePower(); } catch { }
                            try { HeroUnitMaker.UpgradeEquips(one); } catch { }
                        }

                        sum += one.level;
                        count++;
                    }
                }
            }

            if (m.matchMode == ArenaMatchMode.venationes) m.level = UnityEngine.Random.Range(least, most + 1);
            else if (count > 0) m.level = sum / count;

            // Деньги — по ступени, слава — как дала игра.
            m.prize = Mathf.RoundToInt(m.prize * Ladder.PrizeTimes(rung));
        }

        // ----------------------------------------------------------------- день

        /// <summary>A day has passed: open what is due, run what is waiting, show something.</summary>
        internal static void Day(int days)
        {
            if (Enabled == null || !Enabled.Value || ArenaMatchManager.instance == null) return;

            int today = Today();

            foreach (Arena arena in ArenaMatchManager.instance.arenas)
            {
                if (!Serves(arena)) continue;

                try
                {
                    Book book = Of(arena);

                    // Идут своим ходом те, что ждут в ленте: в окне игра ведёт только показанный.
                    for (int d = 0; d < Math.Max(1, days); d++) Run(arena, book);

                    if (book.week == int.MinValue)
                    {
                        // Первое знакомство: неделя, месяц и сезон отсчитываются вразнобой, чтобы
                        // старшие ступени не открывались в первый же день все разом.
                        book.week = today - WeekDays.Value;
                        book.month = today - UnityEngine.Random.Range(0, Math.Max(1, MonthDays.Value));
                        book.season = today - UnityEngine.Random.Range(0, Math.Max(1, SeasonDays.Value));
                    }

                    if (today - book.week >= WeekDays.Value)
                    {
                        book.week = today;
                        ArenaMatch m = Open(arena, UnityEngine.Random.Range(1, 4));
                        if (m != null) book.waiting.Add(m);
                    }

                    if (today - book.month >= MonthDays.Value)
                    {
                        book.month = today;
                        ArenaMatch m = Open(arena, 4);
                        if (m != null) book.waiting.Add(m);
                    }

                    if (today - book.season >= SeasonDays.Value)
                    {
                        book.season = today;
                        ArenaMatch m = Open(arena, 5);
                        if (m != null) book.waiting.Add(m);
                    }

                    // Окно пусто, а вызова нет — показываем первый бой ленты.
                    if (arena.match == null && arena.challenge == null && book.waiting.Count > 0)
                    {
                        arena.match = book.waiting[0];
                        book.waiting.RemoveAt(0);
                    }
                }
                catch (Exception e)
                {
                    ItemForgePlugin.Log.LogWarning($"Лента арены «{arena.arenaName}» сбилась: {e.Message}");
                }
            }
        }

        /// <summary>One day for the bouts waiting in the ribbon, each put in the window for the moment.</summary>
        private static void Run(Arena arena, Book book)
        {
            foreach (ArenaMatch m in book.waiting.ToArray())
            {
                if (m == null || m.playerIn || (object)m == (object)ArenaMatchManager.currentMatch) continue;

                ArenaMatch shown = arena.match;
                arena.match = m;
                m.site = arena;

                try
                {
                    arena.CalculateCurrentMatch();
                }
                catch
                {
                }

                ArenaMatch after = arena.match;
                arena.match = shown;

                // Кончился или отменён — из ленты вон.
                if (after == null) book.waiting.Remove(m);
            }
        }

        /// <summary>The arena champion standing in this challenge, if it is his.</summary>
        internal static NPCSaveData Champion(Arena arena, ArenaMatch m)
        {
            if (arena == null || m == null || m.teams == null) return null;

            foreach (GladiatorTeam team in m.teams)
            {
                if (team == null || team.members == null) continue;
                foreach (NPCSaveData one in team.members)
                {
                    if (one == null) continue;
                    if ((object)one == (object)arena.champion || (object)one == (object)arena.champion2) return one;
                }
            }

            return null;
        }

        /// <summary>A bout has ended or been called off: it leaves the ribbon.</summary>
        internal static void Forget(ArenaMatch m)
        {
            if (m == null) return;
            foreach (Book book in books.Values) book.waiting.Remove(m);
        }

        // ----------------------------------------------------------------- листать

        internal static bool Locked(Arena arena)
        {
            ArenaMatch shown = arena.match;
            if (shown == null) return false;
            if (shown.playerIn || shown.isStarted || (object)shown == (object)ArenaMatchManager.currentMatch) return true;

            // Чужой бой — сюжетный или открытый самой игрой — держит окно, пока не кончится.
            return RungOf(shown) == 0;
        }

        /// <summary>All pages of an arena in order: bouts, then the empty page for the challenge.</summary>
        private static List<ArenaMatch> Pages(Arena arena, out int at)
        {
            Book book = Of(arena);
            List<ArenaMatch> pages = new List<ArenaMatch>();

            if (arena.match != null) pages.Add(arena.match);
            pages.AddRange(book.waiting);

            bool blank = arena.challenge != null || pages.Count == 0;
            if (blank) pages.Add(null);

            at = arena.match != null ? 0 : pages.Count - 1;
            return pages;
        }

        internal static void Page(Arena arena, int step)
        {
            if (arena == null || !Serves(arena)) return;

            if (Locked(arena))
            {
                try { GameController.ShowMessage("Этот бой держит окно, пока не кончится", 2f); } catch { }
                return;
            }

            int at;
            List<ArenaMatch> pages = Pages(arena, out at);
            if (pages.Count <= 1) return;

            int next = ((at + step) % pages.Count + pages.Count) % pages.Count;
            ArenaMatch chosen = pages[next];

            Book book = Of(arena);
            book.waiting.Clear();
            foreach (ArenaMatch one in pages)
            {
                if (one != null && (object)one != (object)chosen) book.waiting.Add(one);
            }

            arena.match = chosen;
            if (chosen != null) chosen.site = arena;

            try { ArenaDetailMenu.Instance.ShowArenaInfo(arena); } catch { }
        }

        /// <summary>What the window writes over the bout: its rung and its place in the ribbon.</summary>
        internal static string Caption(Arena arena)
        {
            if (!Serves(arena)) return null;

            int at;
            List<ArenaMatch> pages = Pages(arena, out at);

            string where = pages.Count > 1 ? $"  ·  {at + 1} из {pages.Count}" : "";
            int rung = RungOf(arena.match);

            if (arena.match == null) return pages.Count > 1 ? "Вызов" + where : null;
            return (rung > 0 ? $"ступень {rung}" : "") + where;
        }

        // ----------------------------------------------------------------- сохранить и поднять

        private static System.Reflection.FieldInfo archiveField;

        private static string FilePath()
        {
            string archive = "";
            try
            {
                if (archiveField == null) archiveField = AccessTools.Field(typeof(SaveLoadManager), "currentArchive");
                archive = (archiveField != null && SaveLoadManager.Instance != null
                    ? archiveField.GetValue(SaveLoadManager.Instance) as string : null) ?? "";
            }
            catch
            {
            }

            foreach (char bad in Path.GetInvalidFileNameChars()) archive = archive.Replace(bad, '_');
            return Path.Combine(BepInEx.Paths.ConfigPath, "aor.ribbon." + archive + ".txt");
        }

        internal static void Save()
        {
            if (Enabled == null || !Enabled.Value) return;

            try
            {
                StringBuilder sb = new StringBuilder();

                foreach (KeyValuePair<string, Book> one in books)
                {
                    Book book = one.Value;
                    sb.Append("arena\t").Append(one.Key).Append('\t').Append(book.week).Append('\t')
                      .Append(book.month).Append('\t').Append(book.season).Append('\n');

                    foreach (ArenaMatch m in book.waiting)
                    {
                        if (m == null) continue;
                        string json = JsonMapper.ToJson(new ArenaMatchSaveData(m)).Replace("\n", " ").Replace("\r", " ");
                        sb.Append("bout\t").Append(one.Key).Append('\t').Append(RungOf(m)).Append('\t')
                          .Append(m.day).Append('\t').Append(json).Append('\n');
                    }
                }

                File.WriteAllText(FilePath(), sb.ToString(), Encoding.UTF8);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не записал ленту арены: " + e.Message);
            }
        }

        internal static void Load()
        {
            books.Clear();
            if (Enabled == null || !Enabled.Value || ArenaMatchManager.instance == null) return;

            try
            {
                string path = FilePath();
                if (!File.Exists(path)) return;

                Dictionary<string, Arena> byName = new Dictionary<string, Arena>();
                foreach (Arena a in ArenaMatchManager.instance.arenas)
                {
                    if (a != null && a.arenaName != null) byName[a.arenaName] = a;
                }

                int kept = 0, lost = 0;

                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string[] p = line.Split(new[] { '\t' }, 5);
                    if (p.Length < 2) continue;

                    Arena arena;
                    if (!byName.TryGetValue(p[1], out arena)) continue;

                    Book book = Of(arena);

                    if (p[0] == "arena" && p.Length >= 5)
                    {
                        int.TryParse(p[2], out book.week);
                        int.TryParse(p[3], out book.month);
                        int.TryParse(p[4], out book.season);
                        continue;
                    }

                    if (p[0] != "bout" || p.Length < 5) continue;

                    int rung, day;
                    int.TryParse(p[2], out rung);
                    string[] rest = p[4].Split(new[] { '\t' }, 2);
                    int.TryParse(p[3], out day);
                    string json = p[4];

                    try
                    {
                        ArenaMatchSaveData data = JsonMapper.ToObject<ArenaMatchSaveData>(json);
                        ArenaMatch m = data;
                        if (m == null || m.teams == null) { lost++; continue; }

                        bool whole = true;
                        foreach (GladiatorTeam team in m.teams)
                        {
                            if (team == null || team.members == null) { whole = false; break; }
                            foreach (NPCSaveData one in team.members)
                            {
                                if (one == null || one.heroCareer == null) { whole = false; break; }
                            }
                        }

                        if (!whole && m.matchMode != ArenaMatchMode.venationes) { lost++; continue; }

                        m.site = arena;
                        m.day = day;
                        m.rank = (HeroRankTitle)Mathf.Clamp(rung, 1, 5);

                        foreach (GladiatorTeam team in m.teams)
                        {
                            if (team == null || team.members == null) continue;
                            team.arena = arena;
                            foreach (NPCSaveData one in team.members)
                            {
                                if (one != null && one.heroCareer != null) one.heroCareer.gladiatorState = AgentState.match;
                            }
                        }

                        book.waiting.Add(m);
                        kept++;
                    }
                    catch
                    {
                        lost++;
                    }
                }

                if (Telling.Value)
                {
                    ItemForgePlugin.Log.LogInfo($"Лента арен поднята: боёв {kept}, не восстановлено {lost}.");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не прочёл ленту арены: " + e.Message);
            }
        }

        // ----------------------------------------------------------------- звери ступени

        private static readonly ConditionalWeakTable<UnitAttribute, object> raised =
            new ConditionalWeakTable<UnitAttribute, object>();

        internal static bool Beast(UnitAttribute unit)
        {
            try
            {
                return ArenaManager.instance != null && ArenaManager.instance.beasts != null
                    && ArenaManager.instance.beasts.Contains(unit);
            }
            catch
            {
                return false;
            }
        }

        internal static void Level(UnitAttribute beast)
        {
            object seen;
            if (raised.TryGetValue(beast, out seen)) return;

            int rung = RungOf(ArenaMatchManager.currentMatch);
            if (rung <= 0) return;

            raised.Add(beast, null);

            int target = Ladder.Roll(rung);
            if (beast.Data.level < target) beast.Data.level = target;

            if (Telling.Value)
            {
                ItemForgePlugin.Log.LogInfo($"Арена: зверь «{beast.Data.unitname}» выходит {beast.Data.level}-м уровнем "
                    + $"(ступень {rung}).");
            }
        }

        private sealed class Passes { internal int left = 3; }

        private static readonly ConditionalWeakTable<UnitAttribute, Passes> healed =
            new ConditionalWeakTable<UnitAttribute, Passes>();

        /// <summary>
        /// Fills the raised beast to its new whole, over its first few reckonings.
        ///
        /// Прибавка за уровень ложится эффектом, а предел здоровья игра выводит из эффекта
        /// только на следующем пересчёте. Налить один раз — значит налить до прежнего предела.
        /// </summary>
        internal static void Heal(UnitAttribute beast)
        {
            object seen;
            if (!raised.TryGetValue(beast, out seen)) return;
            if (beast.Data == null || beast.maxhp <= 0f) return;

            Passes mine;
            if (!healed.TryGetValue(beast, out mine))
            {
                mine = new Passes();
                healed.Add(beast, mine);
            }

            if (mine.left <= 0) return;
            mine.left--;

            beast.Data.currenthp = beast.maxhp;
        }
    }

    // --------------------------------------------------------------------- игра

    [HarmonyPatch(typeof(ArenaMatchManager), "OnDayPassed")]
    internal static class ArenaDay_Ribbon_Patch
    {
        private static void Postfix(int dayPassed)
        {
            try
            {
                if (gameManager.GM != null && gameManager.GM.isCatchingTime) return;
                Ribbon.Day(dayPassed);
            }
            catch
            {
            }
        }
    }

    // Свои бои арена больше не открывает: их открывает лента, по ступеням и по сроку.
    [HarmonyPatch(typeof(Arena), "UpdateMatches")]
    internal static class UpdateMatches_Ribbon_Patch
    {
        private static bool Prefix(Arena __instance)
        {
            if (!Ribbon.Serves(__instance)) return true;
            return __instance.match != null;
        }
    }

    [HarmonyPatch(typeof(ArenaMatch), "EndMatch")]
    internal static class EndMatch_Ribbon_Patch
    {
        private static void Postfix(ArenaMatch __instance) { Ribbon.Forget(__instance); }
    }

    [HarmonyPatch(typeof(ArenaMatch), "CancelMatch")]
    internal static class CancelMatch_Ribbon_Patch
    {
        private static void Postfix(ArenaMatch __instance) { Ribbon.Forget(__instance); }
    }

    // Пока открывается бой ступени, в него зовут тех, кто ей по уровню.
    [HarmonyPatch(typeof(Arena), "GetAllAvailableGladiators")]
    internal static class Available_Ribbon_Patch
    {
        private static void Postfix(ref List<NPCSaveData> __result)
        {
            if (Ribbon.opening <= 0 || __result == null) return;

            int least, most;
            Ladder.Range(Ribbon.opening, out least, out most);

            // Сильнее ступени — не зовём; слабее — поднимем после, когда бой уже собран.
            __result = __result.FindAll(one => one != null && one.level <= most);
        }
    }

    // Звери ступени: пул — по ступени, от слабейшего к сильнейшему.
    [HarmonyPatch(typeof(Arena), "GetMonsterPoolInLevelRange")]
    internal static class MonsterPool_Ribbon_Patch
    {
        private static void Prefix(Arena __instance, ref int maxLv, ref int minLv)
        {
            if (Ribbon.opening <= 0 || __instance == null || __instance.monsterPoolIndexs == null) return;
            if (ArenaMatchManager.instance == null || ArenaMatchManager.instance.monsterPool == null) return;

            try
            {
                List<int> levels = new List<int>();
                foreach (int index in __instance.monsterPoolIndexs)
                {
                    if (index < 0 || index >= ArenaMatchManager.instance.monsterPool.Length) continue;
                    MonsterPool pool = ArenaMatchManager.instance.monsterPool[index];
                    if (pool != null) levels.Add(pool.Level);
                }

                if (levels.Count == 0) return;
                levels.Sort();

                // Ступень выбирает место в ряду: первая — слабейшие, пятая — сильнейшие.
                int at = Mathf.Clamp(Mathf.RoundToInt((Ribbon.opening - 1) / 4f * (levels.Count - 1)), 0, levels.Count - 1);
                int level = levels[at];

                minLv = level;
                maxLv = level + 1;
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "ApplyUnitLevelBonus")]
    internal static class ApplyUnitLevelBonus_Ribbon_Patch
    {
        private static void Prefix(UnitAttribute __instance)
        {
            try
            {
                if (__instance != null && __instance.Data != null && Ribbon.Beast(__instance)) Ribbon.Level(__instance);
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "DoUpdateAttribute")]
    internal static class DoUpdateAttribute_Ribbon_Patch
    {
        private static void Postfix(UnitAttribute __instance)
        {
            try
            {
                if (__instance != null && Ribbon.Beast(__instance)) Ribbon.Heal(__instance);
            }
            catch
            {
            }
        }
    }

    // Вызов — та же лестница: ступень растёт с каждой победой игрока над вызовом.
    [HarmonyPatch(typeof(Arena), "SpawnChallenge")]
    internal static class SpawnChallenge_Ribbon_Patch
    {
        private static void Postfix(Arena __instance, ArenaMatch __result)
        {
            if (!Ribbon.Serves(__instance) || __result == null || __result.isStoryChallenge) return;

            try
            {
                // Чемпион арены — герой в легендарном: у него своя высота, от трёхсот до
                // пятисот, у каждого своя и одна и та же, сколько бы раз он ни выходил.
                NPCSaveData hero = Ribbon.Champion(__instance, __result);
                if (hero != null)
                {
                    int least, most;
                    Ladder.Range(6, out least, out most);
                    int level = new System.Random(hero.id * 7919 + 17).Next(least, most + 1);

                    Ladder.Raise(hero, level);
                    try { hero.CalculatePower(); } catch { }
                    try { HeroUnitMaker.UpgradeEquips(hero); } catch { }

                    __result.level = hero.level;

                    if (Ribbon.Telling.Value)
                    {
                        ItemForgePlugin.Log.LogInfo($"Арена «{__instance.arenaName}»: чемпион «{hero.unitname}» "
                            + $"выходит {hero.level}-м уровнем, награда {__result.prize}.");
                    }

                    return;
                }

                int rung = Mathf.Clamp(__instance.beatenByPlayer + 1, 1, 5);
                Ribbon.Lift(__result, rung);

                if (Ribbon.Telling.Value)
                {
                    ItemForgePlugin.Log.LogInfo($"Арена «{__instance.arenaName}»: вызов ступени {rung}, "
                        + $"уровень {__result.level}, награда {__result.prize}.");
                }
            }
            catch
            {
            }
        }
    }

    // Окно арены: подпись ступени и места в ленте, и кнопки листания.
    [HarmonyPatch(typeof(ArenaDetailMenu), "ShowArenaInfo")]
    internal static class ShowArenaInfo_Ribbon_Patch
    {
        private static Button back, forth;

        private static void Postfix(ArenaDetailMenu __instance, Arena arena)
        {
            if (!Ribbon.Serves(arena) || __instance == null) return;

            try
            {
                string caption = Ribbon.Caption(arena);
                if (!string.IsNullOrEmpty(caption) && __instance.gameType != null && arena.match != null)
                {
                    __instance.gameType.text = __instance.gameType.text + "  ·  " + caption;
                }

                if (__instance.enrollBtn == null) return;

                if (back == null) back = Make(__instance.enrollBtn, "◄", -1);
                if (forth == null) forth = Make(__instance.enrollBtn, "►", 1);

                bool can = !Ribbon.Locked(arena);
                back.gameObject.SetActive(true);
                forth.gameObject.SetActive(true);
                back.interactable = can;
                forth.interactable = can;
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог дописать окно арены: " + e.Message);
            }
        }

        private static readonly AccessTools.FieldRef<ArenaDetailMenu, Arena> shown =
            AccessTools.FieldRefAccess<ArenaDetailMenu, Arena>("currentArena");

        private static Button Make(Button like, string label, int step)
        {
            Button made = UnityEngine.Object.Instantiate(like, like.transform.parent);
            made.name = "RibbonPage" + (step < 0 ? "Back" : "Forth");
            made.onClick = new Button.ButtonClickedEvent();
            made.onClick.AddListener(() =>
            {
                try
                {
                    ArenaDetailMenu menu = ArenaDetailMenu.Instance;
                    if (menu != null) Ribbon.Page(shown(menu), step);
                }
                catch
                {
                }
            });

            foreach (Text text in made.GetComponentsInChildren<Text>(true)) text.text = label;

            RectTransform box = made.transform as RectTransform;
            RectTransform from = like.transform as RectTransform;
            if (box != null && from != null)
            {
                float w = from.rect.width > 1f ? from.rect.width : 120f;
                box.sizeDelta = new Vector2(Mathf.Max(40f, from.rect.height), from.sizeDelta.y);
                box.anchoredPosition = from.anchoredPosition + new Vector2(step * (w * 0.5f + 34f), 0f);
            }

            return made;
        }
    }

    // Сохраняет игра — сохраняем и мы; поднимает игра — поднимаем и мы.
    [HarmonyPatch(typeof(SaveLoadManager), "WriteDataToSaveFile")]
    internal static class WriteSave_Ribbon_Patch
    {
        private static void Postfix() { Ribbon.Save(); }
    }

    [HarmonyPatch(typeof(ArenaMatchManager), "LoadInstance")]
    internal static class LoadInstance_Ribbon_Patch
    {
        private static void Postfix() { Ribbon.Load(); }
    }

    // Недостающих бойцов игра набирает сама — пусть набирает их на уровень ступени, а не
    // вслепую: иначе в бой первой ступени приходит новичок сорокового уровня.
    [HarmonyPatch(typeof(HeroUnitMaker), "Create")]
    internal static class HeroCreate_Ribbon_Patch
    {
        private static void Prefix(ref HeroRankTitle rank)
        {
            if (Ribbon.opening <= 0) return;

            int least, most;
            Ladder.Range(Ribbon.opening, out least, out most);

            // Игра даёт рангу уровни «(ранг−1)·10 + 1..10»; выше пятого у неё нет.
            int want = Mathf.Clamp(least / 10 + 1, 1, 5);
            rank = (HeroRankTitle)want;
        }
    }
}
