using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using I2.Loc;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// The Russian the game should have shipped with, put on without costing frames.
    ///
    /// Перевод у игры машинный и местами бессмысленный: «Эпопея» вместо «Эпический», «Свет»
    /// вместо «Лёгкая», «член парламента» — это «MP», которое переводчик принял за члена
    /// парламента. Таблицы с правкой у нас давно собраны: три с половиной тысячи исправлений
    /// текста, две с половиной тысячи имён вещей, полтысячи заклинаний и двадцать тысяч строк
    /// диалогов.
    ///
    /// Прежний мод эти таблицы прикладывал не туда. Он ловил саму запись текста на экран —
    /// сеттеры `TMP_Text.text` и `UI.Text.text`, `OnEnable`, `SetCharArray`, — а через них
    /// проходит каждое меняющееся число: здоровье, выносливость, счётчики. Отсюда и рывки, из-за
    /// которых мод выключили.
    ///
    /// Здесь те же таблицы висят на дверях, куда стучатся редко и по делу:
    ///
    ///     gameManager.LocalizedString      — когда интерфейс спрашивает термин
    ///     gameManager.LocalizedItemString  — когда показывают имя вещи
    ///     разовый проход по базе диалогов  — один раз за загрузку
    ///
    /// Ни одна из них не зовётся покадрово, и каждая стоит одного поиска по хешу. Цена за это
    /// честная: текст, который игра рисует мимо своей же локализации, останется как был. Ровный
    /// ход важнее последнего процента строк.
    /// </summary>
    internal static class Tongue
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Tables;
        internal static ConfigEntry<bool> Dialogues;
        internal static ConfigEntry<string> Titles;
        internal static ConfigEntry<bool> Labels;
        internal static ConfigEntry<bool> Census;
        internal static ConfigEntry<string> RecipeMark;
        internal static ConfigEntry<bool> Telling;

        // Термин по ключу: «Endurance» → «Телосложение».
        private static readonly Dictionary<string, string> byKey =
            new Dictionary<string, string>(StringComparer.Ordinal);

        // Готовый текст по готовому тексту: «Эпопея» → «Эпический».
        private static readonly Dictionary<string, string> byText =
            new Dictionary<string, string>(StringComparer.Ordinal);

        // Имя вещи или заклинания по внутреннему имени.
        private static readonly Dictionary<string, string> byName =
            new Dictionary<string, string>(StringComparer.Ordinal);

        // Строка разговора по паре «разговор, строка».
        private static readonly Dictionary<long, string> bySpeech =
            new Dictionary<long, string>();

        private static bool read;
        private static bool spoken;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Tongue", "Enabled", true,
                "Put the corrected Russian on. The tables are read once at start and looked up "
                + "by hash from then on — nothing runs per frame, which is the whole difference "
                + "between this and the mod that had to be turned off for stuttering.");

            Tables = config.Bind("Tongue", "Tables", "LocalizationPatch",
                "Folder the tables are read from, relative to the BepInEx plugins directory, or "
                + "an absolute path. It wants ui_ru.tsv, translations.txt, items_ru.tsv, "
                + "spells_ru.tsv and dialogue_ru.tsv; any that are missing are simply skipped.");

            Dialogues = config.Bind("Tongue", "Dialogues", true,
                "Rewrite the dialogue database as well. This is one pass over every line at "
                + "load and nothing afterwards, so it costs a moment once and nothing later.");

            Titles = config.Bind("Tongue", "Titles",
                "Сторожить=Стражник;Торговля=Торговец;Ковать=Кузнец;Лечить=Лекарь;"
                + "Приключение=Искатель;Наёмничать=Наёмник;Разбойничать=Разбойник;"
                + "Прислуживать=Слуга",
                "Wrong first words in people's names, and what they should be. The game builds "
                + "an unnamed person's name out of his career, and the translator turned the "
                + "careers into verbs: «Сторожить Мари» reads as «To guard Mary». Only the "
                + "first word of a name is looked at, so this cannot touch an ordinary "
                + "sentence. Written as wrong=right, separated by semicolons.");

            Labels = config.Bind("Tongue", "Labels", true,
                "Fix text the game writes straight onto a label without asking its own "
                + "localisation — city menus, buttons, tabs. Caught when the label appears on "
                + "screen and not on every write: that distinction is the whole reason the old "
                + "translation mod had to be turned off. A label is enabled once and then sits "
                + "there; the numbers that change every frame go through the setter, and the "
                + "setter is left alone.");

            Census = config.Bind("Tongue", "Census", false,
                "Write out every creature in the game with the name the player actually sees, "
                + "once, to aor.names.tsv beside the settings. There is no other honest way to "
                + "judge the Russian on people: the names live in the game's own table and "
                + "only the running game can be asked what comes out of it.");

            RecipeMark = config.Bind("Tongue", "RecipeMark", "Рецепт",
                "How a recipe's name begins in the tables. A recipe and the thing it makes "
                + "share one name key in this game — two rows with the same key — and the one "
                + "read last won, which is how a real sword came to be called «Recipe: a "
                + "soldier's bastard sword». With this set, the recipe row yields to the plain "
                + "one, and a recipe still reads as itself wherever no plain row exists. Empty, "
                + "and the last row wins as before.");

            Telling = config.Bind("Tongue", "Telling", true,
                "Write down how much of each table was read and how many lines were replaced.");
        }

        // ------------------------------------------------------------------ чтение таблиц

        private static string Folder()
        {
            string said = (Tables.Value ?? "").Trim();
            if (said.Length == 0) return null;

            if (Path.IsPathRooted(said)) return said;

            string plugins = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);

            // Наши таблицы лежат рядом с прежним модом, а тот — соседом по plugins.
            string near = Path.Combine(plugins, said);
            if (Directory.Exists(near)) return near;

            string up = Path.Combine(Path.GetDirectoryName(plugins) ?? plugins, said);
            if (Directory.Exists(up)) return up;

            // И в отставке он тоже может стоять: papка plugins_OFF рядом с plugins.
            string bep = Path.GetDirectoryName(Path.GetDirectoryName(plugins) ?? plugins);
            if (bep != null)
            {
                string off = Path.Combine(Path.Combine(bep, "plugins_OFF"), said);
                if (Directory.Exists(off)) return off;
            }

            return near;
        }

        internal static void Read()
        {
            if (read || !Enabled.Value) return;
            read = true;

            try
            {
                string where = Folder();

                if (where == null || !Directory.Exists(where))
                {
                    ItemForgePlugin.Log.LogWarning($"Таблиц перевода не нашлось: «{where}».");
                    return;
                }

                int keys = Rows(Path.Combine(where, "ui_ru.tsv"), byKey, 0, 1);
                int names = Rows(Path.Combine(where, "items_ru.tsv"), byName, 1, 2);
                names += Rows(Path.Combine(where, "spells_ru.tsv"), byName, 1, 2);
                int texts = Pairs(Path.Combine(where, "translations.txt"));
                int lines = Speech(Path.Combine(where, "dialogue_ru.tsv"));

                if (Telling.Value)
                {
                    ItemForgePlugin.Log.LogInfo($"Перевод прочитан из «{where}»: "
                        + $"терминов {keys}, имён {names}, правок текста {texts}, "
                        + $"строк диалога {lines}.");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог прочесть таблицы перевода: " + e);
            }
        }

        /// <summary>Reads a tab-separated table into a map, by the two columns named.</summary>
        private static int Rows(string file, Dictionary<string, string> into, int from, int to)
        {
            if (!File.Exists(file)) return 0;

            int got = 0;

            foreach (string line in File.ReadAllLines(file))
            {
                if (line.Length == 0 || line[0] == '#') continue;

                string[] cut = line.Split('\t');
                if (cut.Length <= to) continue;

                string key = cut[from].Trim();
                string said = cut[to].Trim();
                if (key.Length == 0 || said.Length == 0) continue;

                // Одно имя на двоих: у вещи и у рецепта, который её делает. В таблице это две
                // строки с одним ключом, и прежде побеждала последняя — оттого настоящий меч
                // в сумке звался «Рецепт: Полуторный меч солдата». Рецептная строка уступает
                // простой, а без простой остаётся сама.
                string had;
                if (into.TryGetValue(key, out had) && Recipe(said) && !Recipe(had)) continue;

                into[key] = said;
                got++;
            }

            return got;
        }

        /// <summary>True when this name is a recipe's rather than the thing's own.</summary>
        private static bool Recipe(string said)
        {
            string mark = (RecipeMark.Value ?? "").Trim();

            return mark.Length > 0
                && said.StartsWith(mark, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Reads the «было=стало» corrections.</summary>
        private static int Pairs(string file)
        {
            if (!File.Exists(file)) return 0;

            int got = 0;

            foreach (string line in File.ReadAllLines(file))
            {
                if (line.Length == 0 || line[0] == '#') continue;

                int split = line.IndexOf('=');
                if (split <= 0) continue;

                string was = line.Substring(0, split);
                string now = line.Substring(split + 1);
                if (was.Length == 0 || now.Length == 0) continue;

                byText[was] = now;
                got++;
            }

            return got;
        }

        private static int Speech(string file)
        {
            if (!File.Exists(file) || !Dialogues.Value) return 0;

            int got = 0;

            foreach (string line in File.ReadAllLines(file))
            {
                if (line.Length == 0 || line[0] == '#') continue;

                string[] cut = line.Split('\t');
                if (cut.Length < 3) continue;

                int talk, step;
                if (!int.TryParse(cut[0].Trim(), out talk)) continue;
                if (!int.TryParse(cut[1].Trim(), out step)) continue;

                string said = cut[2];
                if (said.Length == 0) continue;

                bySpeech[Pair(talk, step)] = said;
                got++;
            }

            return got;
        }

        private static long Pair(int talk, int step)
        {
            return ((long)talk << 32) | (uint)step;
        }

        // ------------------------------------------------------------------ спрос

        internal static bool Term(string key, out string said)
        {
            said = null;
            if (!Enabled.Value || string.IsNullOrEmpty(key)) return false;

            return byKey.TryGetValue(key, out said);
        }

        internal static bool Said(string text, out string better)
        {
            better = null;
            if (!Enabled.Value || string.IsNullOrEmpty(text)) return false;

            return byText.TryGetValue(text, out better);
        }

        internal static bool Named(string name, out string said)
        {
            said = null;
            if (!Enabled.Value || string.IsNullOrEmpty(name)) return false;

            return byName.TryGetValue(name, out said);
        }

        private static readonly Dictionary<string, string> titles =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private static string titlesRead;

        /// <summary>
        /// Fixes the word a nameless person is called by.
        ///
        /// Безымянному игра складывает имя из его занятия, а переводчик перевёл занятия
        /// глаголами: выходит «Сторожить Мари» — «сторожить Марию». Правим только первое слово
        /// имени: обычной фразы это тронуть не может, потому что сюда обычные фразы не ходят.
        /// </summary>
        private static string[] nicks;
        private static string nicksRead;

        /// <summary>
        /// Translates a beast whose name we prefixed ourselves.
        ///
        /// «Wild» приписывает зверю кличку — «грозный», «матёрый», — и пишет её прямо в имя.
        /// После этого таблица имён по нему уже не ищется: там есть «Horse», а спрашивают
        /// «грозный Horse». Отсюда и полурусская кличка на скриншоте.
        ///
        /// Снимаем приписку, переводим то, что осталось, и возвращаем приписку на место.
        /// </summary>
        internal static bool Nicked(string name, out string better)
        {
            better = null;
            if (!Enabled.Value || string.IsNullOrEmpty(name)) return false;

            int space = name.IndexOf(' ');
            if (space <= 0) return false;

            string first = name.Substring(0, space);
            string rest = name.Substring(space + 1);
            if (rest.Length == 0) return false;

            // Кличка — русское слово, а то, что за ней, должно быть ключом: если остаток уже
            // по-русски, переводить нечего.
            bool ours = false;
            foreach (char c in first)
            {
                if (c >= 'А' && c <= 'я') { ours = true; break; }
            }
            if (!ours) return false;

            try
            {
                if (gameManager.GM == null || gameManager.GM.unitNameLanguageSource == null) return false;

                string said;
                if (!gameManager.GM.unitNameLanguageSource.TryGetTranslation(rest, out said)) return false;
                if (string.IsNullOrEmpty(said) || said == rest) return false;

                better = first + " " + said;
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool Title(string name, out string better)
        {
            better = null;
            if (!Enabled.Value || string.IsNullOrEmpty(name)) return false;

            string written = Titles.Value ?? "";

            if (written != titlesRead)
            {
                titles.Clear();
                foreach (string row in written.Split(';'))
                {
                    int split = row.IndexOf('=');
                    if (split <= 0) continue;
                    titles[row.Substring(0, split).Trim()] = row.Substring(split + 1).Trim();
                }
                titlesRead = written;
            }

            if (titles.Count == 0) return false;

            int space = name.IndexOf(' ');
            string first = space > 0 ? name.Substring(0, space) : name;

            string right;
            if (!titles.TryGetValue(first, out right)) return false;

            better = space > 0 ? right + name.Substring(space) : right;
            return true;
        }


        /// <summary>
        /// Кладёт одну строку в игровую таблицу под её же ключом.
        ///
        /// Подсказки в этой игре хранятся не текстом, а ключом: в самой ячейке окна лежит
        /// «Tooltip_нечто», и текст достаётся по нему в тот миг, когда на ячейку наводят. Оттого
        /// дописать в ячейку нельзя — припишешь к ключу, и ключ перестанет находиться. Писать
        /// надо туда же, откуда берут.
        /// </summary>
        internal static bool Put(string key, string said)
        {
            if (string.IsNullOrEmpty(key) || said == null) return false;

            try
            {
                if (LocalizationManager.Sources != null)
                {
                    foreach (LanguageSourceData one in LocalizationManager.Sources)
                    {
                        if (Lay(one, key, said)) return true;
                    }
                }

                gameManager keeper = gameManager.GM;

                if (keeper != null)
                {
                    if (Lay(keeper.itemLanguageSource, key, said)) return true;
                    if (Lay(keeper.skillLanguageSource, key, said)) return true;
                    if (Lay(keeper.unitNameLanguageSource, key, said)) return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool Lay(LanguageSourceData source, string key, string said)
        {
            if (source == null) return false;

            int lang = source.GetLanguageIndex(LocalizationManager.CurrentLanguage, true, false);
            if (lang < 0) return false;

            TermData term = source.GetTermData(key);
            if (term == null || term.Languages == null || lang >= term.Languages.Length) return false;

            term.Languages[lang] = said;
            return true;
        }

        /// <summary>И что там сейчас написано.</summary>
        internal static string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            try
            {
                string said;
                return LocalizationManager.TryGetTranslation(key, out said, false) ? said : null;
            }
            catch
            {
                return null;
            }
        }

        // ------------------------------------------------------------------ в таблицу

        private static string engraved;

        /// <summary>
        /// Вписывает наши строки в игровую таблицу — туда, откуда игра их берёт.
        ///
        /// Прежде мы перехватывали её на выходе: она честно доставала свою строку, а мы её
        /// выбрасывали и подставляли свою. Всякий раз. На каждый спрос, на каждую надпись, на
        /// каждую строку разговора — а спрашивает она без счёта.
        ///
        /// Тексты у неё лежат в таблице I2: у каждой строки свой ключ и по столбцу на язык.
        /// Столбец пишется. Оттого своё кладём прямо в него — один раз при запуске, — и дальше
        /// игра отдаёт наше сама, ничего не зная о нас. Перехваты после этого не нужны и сняты.
        ///
        /// Правим на три лада, и все три идут одним проходом по таблице:
        ///     по ключу  — самый точный: ключ различает то, что русское слово путает;
        ///     по тексту — когда ключа мы не знаем, а знаем, что написано;
        ///     по титулу — когда менять надо первое слово имени.
        ///
        /// Таблиц в игре четыре: общая, вещей, имён и умений. Обходим все.
        /// </summary>
        internal static void Engrave(bool again = false)
        {
            if (Enabled == null || !Enabled.Value) return;

            try
            {
                string tongue = LocalizationManager.CurrentLanguage;

                if (!again && engraved == tongue) return;
                engraved = tongue;

                Read();

                gameManager keeper = gameManager.GM;

                // Таблиц четыре, и собираются они не разом. Пока хоть одной нет, запись не
                // считается сделанной: иначе первый же спрос о переводе — а он случается
                // раньше, чем игра дочитает свои полки, — закрыл бы дверь навсегда, и в
                // таблицах вещей, имён и умений осталось бы чужое.
                bool all = keeper != null
                    && keeper.itemLanguageSource != null
                    && keeper.unitNameLanguageSource != null
                    && keeper.skillLanguageSource != null;

                if (!all) engraved = null;

                int carved = 0, looked = 0;

                if (LocalizationManager.Sources != null)
                {
                    foreach (LanguageSourceData one in LocalizationManager.Sources)
                    {
                        int was = carved, seen = looked;
                        Cut(one, ref carved, ref looked);

                        Tell("общая", carved - was, looked - seen);
                    }
                }

                if (keeper != null)
                {
                    int was = carved, seen = looked;
                    Cut(keeper.itemLanguageSource, ref carved, ref looked);
                    Tell("вещи", carved - was, looked - seen);

                    was = carved; seen = looked;
                    Cut(keeper.unitNameLanguageSource, ref carved, ref looked);
                    Tell("имена", carved - was, looked - seen);

                    was = carved; seen = looked;
                    Cut(keeper.skillLanguageSource, ref carved, ref looked);
                    Tell("умения", carved - was, looked - seen);
                }

                ItemForgePlugin.Log.LogInfo($"Перевод вписан в таблицу «{tongue}»: "
                    + $"правок {carved} из {looked} строк"
                    + (all ? "" : " — не все полки готовы, повторю") + ".");

                if (all)
                {
                    settled = true;

                    ItemForgePlugin.Log.LogInfo($"Наших строк: по ключу {byKey.Count}, "
                        + $"по тексту {byText.Count}, вещей {byName.Count}; "
                        + $"пристроено {carved}.");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог вписать перевод: " + e.Message);
            }
        }

        private static void Tell(string shelf, int carved, int looked)
        {
            if (Telling == null || !Telling.Value || looked <= 0) return;

            ItemForgePlugin.Log.LogInfo($"    полка «{shelf}»: правок {carved} из {looked}.");
        }

        private static bool settled;
        private static float waited;

        /// <summary>
        /// Пробует вписать перевод, покуда все четыре полки не соберутся.
        ///
        /// Прежде это висело на чужом спросе — на первом же обращении игры за переводом. Но
        /// спрашивает она не всегда: надписи в меню идут мимо, своим путём, и второго спроса
        /// можно ждать до первого разговора. Оттого повтор стоит на своём такте.
        /// </summary>
        /// <summary>Сменили язык — таблицы собраны заново, и наше из них ушло.</summary>
        internal static void Afresh()
        {
            settled = false;
            engraved = null;
        }

        internal static void Nudge(float passed)
        {
            if (settled || Enabled == null || !Enabled.Value) return;

            waited += passed;
            if (waited < 1f) return;

            waited = 0f;

            Engrave();
        }

        /// <summary>Один столбец одной таблицы.</summary>
        private static void Cut(LanguageSourceData source, ref int carved, ref int looked)
        {
            if (source == null || source.mTerms == null) return;

            int lang = source.GetLanguageIndex(LocalizationManager.CurrentLanguage, true, false);
            if (lang < 0) return;

            foreach (TermData term in source.mTerms)
            {
                if (term == null || term.Languages == null) continue;
                if (lang >= term.Languages.Length) continue;

                looked++;

                string better;

                // По ключу — он точен: одно русское слово стоит за разными сущностями, и
                // «выносливостью» подписаны и характеристика, и запас сил. Ключ различает.
                if (byKey.TryGetValue(term.Term, out better)
                    || byName.TryGetValue(term.Term, out better))
                {
                    term.Languages[lang] = better;
                    carved++;
                    continue;
                }

                string now = term.Languages[lang];
                if (string.IsNullOrEmpty(now)) continue;

                if (byText.TryGetValue(now, out better))
                {
                    term.Languages[lang] = better;
                    carved++;
                    continue;
                }

                if (Title(now, out better))
                {
                    term.Languages[lang] = better;
                    carved++;
                }
            }
        }

        private static bool counted;

        /// <summary>Writes out every creature by the name the player sees.</summary>
        internal static void Names()
        {
            if (counted || !Enabled.Value || !Census.Value) return;

            try
            {
                UIUnitDatabase db = UIUnitDatabase.Instance;
                if (db == null || db.indexes == null) return;

                counted = true;

                List<string> said = new List<string>();
                said.Add("существо	ключ	как видит игрок");

                foreach (UnitInfo who in db.indexes)
                {
                    if (who == null) continue;

                    string raw = who.unitName ?? "";
                    string shown = gameManager.LocalizedNameString(raw);

                    string better;
                    if (Title(shown, out better)) shown = better;

                    said.Add(who.name + "	" + raw + "	" + shown);
                }

                string where = Path.Combine(BepInEx.Paths.ConfigPath, "aor.names.tsv");
                File.WriteAllLines(where, said.ToArray(), System.Text.Encoding.UTF8);

                ItemForgePlugin.Log.LogInfo($"Перепись имён написана: {said.Count - 1} существ "
                    + $"в «{where}».");
            }
            catch (Exception e)
            {
                counted = true;
                ItemForgePlugin.Log.LogError("Не смог переписать имена: " + e);
            }
        }

        // ------------------------------------------------------------------ диалоги

        /// <summary>
        /// Rewrites every line in the dialogue database, once.
        ///
        /// Разговоры лежат готовой базой и после загрузки не меняются, поэтому проход по ним
        /// делается один раз и стоит ровно один раз. Это и есть та работа, которую прежний мод
        /// делал правильно, — а рывки давал не он, а ловля записей текста.
        /// </summary>
        internal static void Speak()
        {
            if (spoken || !Enabled.Value || !Dialogues.Value) return;
            if (bySpeech.Count == 0) return;

            try
            {
                if (PixelCrushers.DialogueSystem.DialogueManager.instance == null) return;

                PixelCrushers.DialogueSystem.DialogueDatabase book =
                    PixelCrushers.DialogueSystem.DialogueManager.instance.masterDatabase;

                if (book == null || book.conversations == null) return;

                spoken = true;
                int put = 0;

                foreach (PixelCrushers.DialogueSystem.Conversation talk in book.conversations)
                {
                    if (talk == null || talk.dialogueEntries == null) continue;

                    foreach (PixelCrushers.DialogueSystem.DialogueEntry step in talk.dialogueEntries)
                    {
                        if (step == null) continue;

                        string said;
                        if (!bySpeech.TryGetValue(Pair(talk.id, step.id), out said)) continue;

                        step.DialogueText = said;
                        put++;
                    }
                }

                if (Telling.Value)
                {
                    ItemForgePlugin.Log.LogInfo($"Диалоги переписаны: строк {put} "
                        + $"из {bySpeech.Count} в таблице.");
                }
            }
            catch (Exception e)
            {
                spoken = true;   // второй раз пробовать нечего, ошибка та же
                ItemForgePlugin.Log.LogError("Не смог переписать диалоги: " + e);
            }
        }
    }

    // Термин интерфейса. Спрашивается, когда окно рисует подпись, а не каждый кадр.
    // Перевод вписан в таблицу при запуске, и перехватывать игру на выходе больше незачем:
    // она достаёт оттуда наше сама. Оставлено одно место — имена, и вот почему.
    //
    // Имя зверя игра складывает при встрече: прозвище русское, порода ключом — «злой Wolf».
    // Такой строки в таблице нет и быть не может, она рождается на ходу. Оттого и правится на
    // ходу: порода переводится по таблице, прозвище остаётся своим.
    [HarmonyPatch(typeof(gameManager), "LocalizedNameString")]
    [HarmonyPatch(typeof(gameManager), "LocalizedNameString")]
    internal static class LocalizedNameString_Tongue_Patch
    {
        private static void Postfix(string name, ref string __result)
        {
            if (!Tongue.Enabled.Value) return;

            Tongue.Read();

            string s = name;

            string better;

            if (Tongue.Title(__result, out better)) { __result = better; return; }

            // Имя не нашлось целиком — может, мы сами приписали к нему кличку.
            if (__result == s && Tongue.Nicked(__result, out better)) __result = better;
        }
    }

    // Имя безымянного игра складывает один раз и пишет в сохранение уже готовой строкой:
    // «Сторожить Линси». Вывод мы поправляем выше, но сама запись остаётся глаголом, и её
    // видно везде, где имя читают напрямую, — в журнале, над головой, в записях боя. Правим
    // саму запись: при рождении имени и у тех, кто родился с ним раньше.
    internal static class TitleFix
    {
        internal static void Mend(UnitAttribute who)
        {
            if (Tongue.Enabled == null || !Tongue.Enabled.Value) return;
            if (who == null || who.Data == null || string.IsNullOrEmpty(who.Data.unitname)) return;

            try
            {
                string better;
                if (Tongue.Title(who.Data.unitname, out better) && better != who.Data.unitname)
                {
                    who.Data.unitname = better;
                }
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(HumaniodUnit), "RandomHumanName")]
    internal static class RandomHumanName_Title_Patch
    {
        private static void Postfix(HumaniodUnit __instance) { TitleFix.Mend(__instance); }
    }

    [HarmonyPatch(typeof(HumaniodUnit), "InitializeUnit")]
    internal static class InitializeUnit_Title_Patch
    {
        private static void Postfix(HumaniodUnit __instance) { TitleFix.Mend(__instance); }
    }

    // Сменили язык — таблицу собрали заново, и наше из неё ушло. Вписываем снова.
    [HarmonyPatch(typeof(LocalizationManager), "LocalizeAll")]
    internal static class LocalizeAll_Tongue_Patch
    {
        private static void Postfix()
        {
            try { Tongue.Afresh(); } catch { }
        }
    }

    // Надпись, впечатанная в саму сцену.
    //
    // Такие в таблице не лежат вовсе: их набрали руками при сборке окна, и спросить о них
    // игру нельзя — ей самой о них ничего не известно. Оттого правится не ответ на спрос, а
    // сам ярлык, и ровно один раз: когда окно открывают. Сеттер текста мы не трогаем
    // намеренно — через него идут числа, меняющиеся каждый кадр, и прежний мод на этом и
    // споткнулся.
    [HarmonyPatch(typeof(UnityEngine.UI.Text), "OnEnable")]
    internal static class TextOnEnable_Tongue_Patch
    {
        private static void Postfix(UnityEngine.UI.Text __instance)
        {
            if (!Tongue.Enabled.Value || !Tongue.Labels.Value) return;

            try
            {
                string was = __instance.text;
                if (string.IsNullOrEmpty(was) || was.Length > 64) return;

                string better;
                if (Tongue.Said(was, out better)) __instance.text = better;
            }
            catch
            {
            }
        }
    }

    // Игру спросили о переводе — значит, таблицы собраны и в них можно писать.
    //
    // Это единственный перехват, который тут остался, и он ничего не подменяет: три вызова
    // за ним отрабатывают по одному разу и дальше выходят на первой же строке. Своего места
    // у такой работы в этой игре нет — она не объявляет «я готова», — а угадывать по кадрам
    // хуже, чем спросить у неё самой.
    [HarmonyPatch(typeof(gameManager), "LocalizedString", new[] { typeof(string), typeof(bool), typeof(bool) })]
    internal static class Speak_Tongue_Patch
    {
        private static void Postfix()
        {
            if (!Tongue.Enabled.Value) return;

            Tongue.Engrave();
            Tongue.Speak();
            Tongue.Names();
        }
    }

    // Имя у рецепта и у вещи, которую он делает, одно на двоих, и в таблице побеждает простое:
    // иначе настоящий меч звался бы «Рецепт: Полуторный меч солдата». Но рецепт от этого
    // перестаёт называть себя, а отличить его есть по чему — у вещи свой род. Приписку
    // возвращаем здесь, в месте показа, где видно, что перед нами.
    [HarmonyPatch(typeof(UIItemTip), "SetValue")]
    internal static class ItemTip_Tongue_Patch
    {
        private static System.Reflection.FieldInfo shown;

        private static void Postfix(UIItemTip __instance)
        {
            if (!Tongue.Enabled.Value || __instance == null || __instance.title == null) return;

            try
            {
                string mark = (Tongue.RecipeMark.Value ?? "").Trim();
                if (mark.Length == 0) return;

                if (shown == null) shown = AccessTools.Field(typeof(UIItemTip), "current");
                if (shown == null) return;

                UIItemInfo thing = shown.GetValue(__instance) as UIItemInfo;
                if (thing == null || thing.itemType != ItemType.Recipe) return;

                string said = __instance.title.text ?? "";
                if (said.Length == 0) return;
                if (said.StartsWith(mark, StringComparison.OrdinalIgnoreCase)) return;

                __instance.title.text = mark + ": " + said;
            }
            catch
            {
            }
        }
    }
}
