using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using spell;
using UnityEngine;
using UnityEngine.UI;

namespace ItemForge
{
    /// <summary>
    /// What the six attributes do now, told where a player looks for it.
    ///
    /// Подсказки у характеристик в игре есть и написаны честно — для той игры, какой она была.
    /// Про ловкость там сказано про скорость атаки, поворота и уклонение, и ни слова про то,
    /// что теперь она же ведёт стрелу и решает, куда именно ляжет удар. Про силу — ничего про
    /// дыхание и мышцы, про выносливость — ничего про кости и яд.
    ///
    /// Своих подсказок мы не рисуем: игровая уже висит на строке, со своей рамкой и своим
    /// переносом. Мы дописываем в неё наши строки, один раз за окно, и помечаем написанное,
    /// чтобы не приписать дважды.
    /// </summary>
    internal static class Lore
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Might;
        internal static ConfigEntry<string> Grit;
        internal static ConfigEntry<string> Quick;
        internal static ConfigEntry<string> Eye;
        internal static ConfigEntry<string> Wit;
        internal static ConfigEntry<string> Will;
        internal static ConfigEntry<string> Paints;
        internal static ConfigEntry<string> Fixes;
        internal static ConfigEntry<string> Drops;
        internal static ConfigEntry<bool> Telling;

        private static bool grumbled;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Lore", "Enabled", true,
                "Add what the attributes do now to the game's own tooltips for them. The game's "
                + "read true for the game as it was: agility said nothing about arrows, "
                + "endurance nothing about bone.");

            Might = config.Bind("Lore", "Might",
                "+1% урона в ближнем бою|"
                + "+1% силы оружия|"
                + "+1 нагрузки на снаряжение|"
                + "+3 грузоподъёмности|"
                + "-0,75% расхода сил на удар, рывок и блок|"
                + "+0,5% собственного веса: тяжелее сбить с ног|"
                + "мышцы держат удар: природная броня",
                "The whole list under the strength tooltip, split by a vertical bar. Это и есть "
                + "перечень, а не приписка к нему: игровой заменяется целиком, от него остаются "
                + "только слова о том, что эта характеристика такое.");

            Grit = config.Bind("Lore", "Grit",
                "+5 очков здоровья, разложенного по частям тела|"
                + "+5 очков выносливости|"
                + "+0,1 регенерации выносливости|"
                + "+1 упорства|"
                + "кости держат удар: природная броня|"
                + "травмы заживают на 1% быстрее|"
                + "негативные эффекты проходят на 1% быстрее (до 80%)",
                "The same for endurance. Сопротивление яду, срок увечий и скорость исцеления "
                + "выносливость даёт по-прежнему — просто в подсказке их нет. Подсказка не "
                + "опись, а напоминание: шесть строк подряд читают хуже двух.");

            Fixes = config.Bind("Lore", "Fixes",
                "скорости атаки=swing|скорости передвижения=pace|шанса крита=crit|"
                + "магического урона=spell|получаемого опыта=learn",
                "Numbers in the game's own tooltips that we have changed, and which of ours to "
                + "put in their place. Written as «слово из подсказки=наша ручка», divided by a "
                + "vertical bar. Знает две: «swing» — цена ловкости в замахе, «pace» — она же "
                + "в шаге. Подсказка есть обещание: если в ней написан процент, а платится две "
                + "трети, то врёт не подсказка.");

            Drops = config.Bind("Lore", "Drops",
                "регенерация здоровья",
                "Lines of the game's own tooltips to strike out, by a word in them, divided by "
                + "a vertical bar. Про реген здоровья игра обещает сотые доли в секунду, а "
                + "лечение у нас идёт часами и через лекаря: обещание стало неправдой, и "
                + "честнее его убрать, чем поправить число.");

            Quick = config.Bind("Lore", "Quick",
                "+1% скорости атаки|"
                + "+1% скорости передвижения|"
                + "+1% скорости поворота|"
                + "+1 уклонения|"
                + "+1% урона в дальнем бою|"
                + "бьёт в то место, где у врага меньше осталось",
                "The same for agility. Попадание отсюда убрано: попадёт удар или нет, решает "
                + "восприятие, а ловкость решает куда. Две строки про одно только путали.");

            Eye = config.Bind("Lore", "Eye",
                "+1 меткости|"
                + "+1 защиты|"
                + "+1 шанса крита|"
                + "решает, попадёт ли удар вообще|"
                + "выше 50 мастерства ведёт руку, ниже - мешает ей",
                "The same for precision.");

            Wit = config.Bind("Lore", "Wit",
                "+1% магического урона|"
                + "+1% сокращения времени восстановления|"
                + "+1% множителя критического урона|"
                + "+2% урона призыва|"
                + "+0,5 уклонения|"
                + "+0,25% получаемого опыта",
                "The same for intelligence. Магический урон здесь писан игровым числом и "
                + "поправляется из «Fixes» той же ручкой, из которой платится, — оттого два "
                + "числа про одно разойтись больше не могут.");

            Will = config.Bind("Lore", "Will",
                "положительные эффекты длятся на 2% дольше",
                "The same for willpower, which the game now calls wisdom.");

            Paints = config.Bind("Lore", "Paints",
                "ItemForgeStrain_strength=E06A18,ItemForgeStrain_agility=F0A828,"
                + "ItemForgeStrain_mastery=C8842C",
                "What colour the plate under each of our own marks is painted, by the mark's "
                + "id and a colour in the usual six digits. The picture on the mark is borrowed "
                + "from one of the game's own buffs and stays as it is; the plate beneath it is "
                + "ours, and it is what tells the borrowed picture apart from the original. "
                + "Three warm shades, near enough to read as one warning and far enough apart "
                + "to tell the three of them from each other.");

            Telling = config.Bind("Lore", "Telling", false,
                "Say in the log which tooltips were added to, and complain when one could not "
                + "be found.");
        }

        // Метка, по которой узнаём уже дописанное.
        private const string Mark = "\n\n— ";

        /// <summary>
        /// Наш перечень для этой характеристики — в том же виде, в каком его пишет игра.
        ///
        /// Без тире, без пустой строки, без второго списка под первым. Прежде мы дописывали
        /// своё отдельным блоком, и выходило, что про урон в ближнем бою сказано дважды, а про
        /// грузоподъёмность — дважды и по-разному: игра обещала два, мы три. Подсказка должна
        /// читаться как одно, а не как спор двух источников.
        /// </summary>
        private static string Lines(ConfigEntry<string> from)
        {
            string written = from != null ? (from.Value ?? "") : "";
            if (written.Length == 0) return null;

            string said = "";

            foreach (string one in written.Split('|'))
            {
                string bit = one.Trim();
                if (bit.Length == 0) continue;

                said += (said.Length == 0 ? "" : "\n") + bit;
            }

            return said.Length > 0 ? said : null;
        }

        /// <summary>
        /// Оставляет от игровой подсказки только вступление.
        ///
        /// Перечисление у неё начинается там, где строка начинается со знака: «+1% урона…»,
        /// «-0,5%…». Всё до первой такой строки — слова о том, что эта характеристика такое, и
        /// они остаются: они верны и написаны лучше, чем написали бы мы.
        /// </summary>
        private static string Head(string said)
        {
            if (said == null) return "";

            string kept = "";

            foreach (string line in said.Split('\n'))
            {
                string bit = line.Trim();

                if (bit.Length > 0 && (bit[0] == '+' || bit[0] == '-' || bit[0] == '−')) break;

                kept += (kept.Length == 0 ? "" : "\n") + line;
            }

            return kept;
        }

        // Ключи подсказок, в которые мы уже вписали своё: второй раз незачем.
        private static readonly HashSet<string> laid = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Вписывает наше в игровую подсказку — туда, откуда игра её берёт.
        ///
        /// Прежде мы дописывали прямо в ячейку окна, и это не работало по причине, которую
        /// стоило узнать раньше: в ячейке лежит не текст, а ключ — «Tooltip_нечто», — а текст
        /// достаётся по нему в тот миг, когда на ячейку наводят. Приписка к ключу давала ключ,
        /// которого нет, и от беды нас спасла только проверка на длину: ключи короткие, и
        /// дописывание молча не происходило вовсе.
        ///
        /// Теперь пишем по ключу в саму таблицу. Заодно поправляются числа, которые мы у игры
        /// изменили: в её подсказке про ловкость по-прежнему стояли целый процент замаха и
        /// половина процента шага, а платит она теперь меньше.
        /// </summary>
        private static bool Add(Text value, ConfigEntry<string> from, string what)
        {
            if (value == null || value.transform.parent == null) return false;

            string mine = Amend(Lines(from));

            string key = Key(value.transform.parent)
                ?? (value.transform.parent.parent != null
                    ? Key(value.transform.parent.parent) : null);

            if (key == null) return false;
            if (laid.Contains(key)) return true;

            string said = Tongue.Get(key);
            if (string.IsNullOrEmpty(said) || said == key) return false;

            // Свой перечень есть — он и становится перечнем: от игрового остаются слова о том,
            // что эта характеристика такое. Своего нет — оставляем игровой как был, поправив
            // числа и вычеркнув обещания, которых мы больше не держим.
            string better = mine != null
                ? Head(said) + "\n" + mine
                : Strike(Amend(said));

            if (!Tongue.Put(key, better)) return false;

            laid.Add(key);

            if (Telling != null && Telling.Value)
            {
                ItemForgePlugin.Log.LogInfo($"Подсказка «{what}» переписана по ключу «{key}».");
            }

            return true;
        }

        /// <summary>Ключ описательной подсказки в этой строке окна.</summary>
        private static string Key(Transform where)
        {
            foreach (UIShowToolTip show in where.GetComponentsInChildren<UIShowToolTip>(true))
            {
                if (show == null || string.IsNullOrEmpty(show.tip)) continue;

                // Подсказка со счётом «12 (+ 3)» ключом не является: игра пишет в неё готовое
                // число и переписывает его при каждом открытии окна. Ключ — это имя, и пробелов
                // с круглыми скобками в нём не бывает.
                if (show.tip.IndexOf(' ') >= 0 || show.tip.IndexOf('(') >= 0) continue;

                return show.tip;
            }

            return null;
        }

        private static string[] drops;
        private static string dropsRead;

        /// <summary>Вычёркивает строки, которые игра обещает, а мы не держим.</summary>
        private static string Strike(string said)
        {
            string written = Drops != null ? (Drops.Value ?? "") : "";

            if (written != dropsRead)
            {
                dropsRead = written;
                drops = written.Split('|');

                for (int i = 0; i < drops.Length; i++) drops[i] = drops[i].Trim();
            }

            if (drops == null || drops.Length == 0 || said == null) return said;

            string[] lines = said.Split('\n');
            string kept = "";

            foreach (string line in lines)
            {
                bool out_ = false;

                foreach (string word in drops)
                {
                    if (word.Length == 0) continue;

                    if (line.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        out_ = true;
                        break;
                    }
                }

                if (out_) continue;

                kept += (kept.Length == 0 ? "" : "\n") + line;
            }

            return kept;
        }

        private static readonly Dictionary<string, string> amends =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private static string amendsRead;

        /// <summary>
        /// Поправляет числа, которые мы у игры изменили, — и в её строках, и в своих.
        ///
        /// Подсказка есть обещание. Если в ней написан процент, а платится две трети, то врёт
        /// не подсказка. Оттого число в тексте не пишется руками, а спрашивается у той же
        /// ручки, из которой платят: повернули ручку — поправилось и обещание.
        /// </summary>
        private static string Amend(string said)
        {
            if (said == null) return null;

            string written = Fixes != null ? (Fixes.Value ?? "") : "";

            if (written != amendsRead)
            {
                amendsRead = written;
                amends.Clear();

                foreach (string one in written.Split('|'))
                {
                    int split = one.IndexOf('=');
                    if (split <= 0) continue;

                    amends[one.Substring(0, split).Trim()] = one.Substring(split + 1).Trim();
                }
            }

            if (amends.Count == 0) return said;

            foreach (KeyValuePair<string, string> one in amends)
            {
                float much;
                bool share;

                switch (one.Value)
                {
                    case "swing":
                        much = Nimble.Swing != null ? Nimble.Swing.Value : 0.01f;
                        share = true;
                        break;

                    case "pace":
                        much = Pace.Nimble != null ? Pace.Nimble.Value : 0.005f;
                        share = true;
                        break;

                    case "spell":
                        much = Wits.PerPoint != null ? Wits.PerPoint.Value : 0.01f;
                        share = true;
                        break;

                    case "crit":
                        much = Nimble.Crit != null ? Nimble.Crit.Value : 1f;
                        share = false;
                        break;

                    case "learn":
                        much = Wits.Learning != null ? Wits.Learning.Value : 0.0025f;
                        share = true;
                        break;

                    default:
                        continue;
                }

                string number = (share ? much * 100f : much).ToString("0.##",
                    System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));

                try
                {
                    said = System.Text.RegularExpressions.Regex.Replace(
                        said,
                        @"[+\-−]?\s*\d+(?:[.,]\d+)?\s*(%?)\s*(?="
                            + System.Text.RegularExpressions.Regex.Escape(one.Key) + ")",
                        delegate (System.Text.RegularExpressions.Match got)
                        {
                            return "+" + number + got.Groups[1].Value + " ";
                        });
                }
                catch
                {
                }
            }

            return said;
        }

        private static readonly Dictionary<string, Color> paints =
            new Dictionary<string, Color>(StringComparer.Ordinal);
        private static string paintsRead;

        /// <summary>The colour the plate under this mark takes, if it is one of ours.</summary>
        internal static bool Paint(string id, out Color mine)
        {
            mine = Color.white;

            string written = Paints != null ? (Paints.Value ?? "") : "";

            if (written != paintsRead)
            {
                paintsRead = written;
                paints.Clear();

                foreach (string one in written.Split(','))
                {
                    int split = one.IndexOf('=');
                    if (split <= 0) continue;

                    Color got;
                    if (ColorUtility.TryParseHtmlString("#" + one.Substring(split + 1).Trim(),
                            out got))
                    {
                        paints[one.Substring(0, split).Trim()] = got;
                    }
                }
            }

            return paints.TryGetValue(id, out mine);
        }

        internal static void Tell(UIApplyUnitAttribute tip)
        {
            if (Enabled == null || !Enabled.Value || tip == null) return;

            try
            {
                bool all = true;

                all &= Add(tip.strength, Might, "сила");
                all &= Add(tip.endurance, Grit, "выносливость");
                all &= Add(tip.agility, Quick, "ловкость");
                all &= Add(tip.precision, Eye, "восприятие");
                all &= Add(tip.intelligence, Wit, "интеллект");
                all &= Add(tip.willpower, Will, "воля");

                if (!all && Telling != null && Telling.Value && !grumbled)
                {
                    grumbled = true;
                    ItemForgePlugin.Log.LogWarning("Описательной подсказки у какой-то из шести "
                        + "характеристик не нашлось — дописывать некуда.");
                }
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(UIApplyUnitAttribute), "UpdateInfoWindow")]
    internal static class UnitTip_Lore_Patch
    {
        private static void Postfix(UIApplyUnitAttribute __instance)
        {
            try
            {
                Lore.Tell(__instance);
            }
            catch
            {
            }
        }
    }

    // Знаки недобора — «не по силам», «не по руке», «не по умению» — красим особо. Они не
    // проклятие и не болезнь: это предупреждение о том, что вещь взята не по себе, и читаться
    // оно должно с первого взгляда.
    //
    // Красим подложку, а не сам значок: рисунок на значке одолжен у чужого эффекта и должен
    // остаться узнаваемым, а вот то, на чём он лежит, — наше, и по нему знак и отличается от
    // того, у кого картинка взята. Каждому из трёх свой оттенок, чтобы и между собой они не
    // путались.
    [HarmonyPatch(typeof(BuffSlot), "SetBuffInfo")]
    internal static class BuffSlot_Lore_Patch
    {
        // Какой была подложка до нас — по ячейке. Ячейки в игре переиспользуются, и вернуть
        // чужому знаку его собственный вид надо точно, а не «примерно белым».
        private static readonly Dictionary<int, Color> plain = new Dictionary<int, Color>();

        /// <summary>Подложка этой ячейки: то, на чём лежит значок.</summary>
        private static Image Back(BuffSlot slot)
        {
            Image mine = slot.GetComponent<Image>();
            if (mine != null && mine != slot.targetImage) return mine;

            foreach (Image one in slot.GetComponentsInChildren<Image>(true))
            {
                if (one != null && one != slot.targetImage) return one;
            }

            return null;
        }

        private static void Postfix(BuffSlot __instance, BuffBase t_buff)
        {
            try
            {
                if (__instance == null || __instance.targetImage == null) return;

                // Значок всегда свой: рисунок мы больше не трогаем.
                __instance.targetImage.color = Color.white;

                Image back = Back(__instance);
                if (back == null) return;

                int id = __instance.GetInstanceID();

                if (!plain.ContainsKey(id)) plain[id] = back.color;

                string kind = t_buff != null && t_buff.buffInfo != null
                    ? t_buff.buffInfo.id : null;

                Color mine;

                if (kind != null && Lore.Paint(kind, out mine)) back.color = mine;
                else back.color = plain[id];
            }
            catch
            {
            }
        }
    }
}
