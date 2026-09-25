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
    /// Дикое: уровень особи и её прозвище.
    ///
    /// В игре у каждого вида один уровень на всех: волк всегда пятый, медведь всегда
    /// четырнадцатый. Оттого лес одинаков на всём протяжении игры — что в первый час, что в
    /// сотый, волк тот же самый.
    ///
    /// Здесь у вида есть вилка, и особь берёт из неё своё: волк идёт от первого до
    /// пятнадцатого. Игра к такому готова — `Data.level` хранится у особи отдельно от чертежа,
    /// и когда он выше, она сама доращивает здоровье и урон через `ApplyUnitLevelBonus`.
    /// Оставалось только назначить.
    ///
    /// Имя показывает, где особь стоит в своей вилке. Матёрая крыса вдвое злее молодой, и
    /// узнать об этом лучше до того, как она вцепится.
    ///
    /// Род прилагательного угадывается по окончанию: «крыса» женского, «волк» мужского,
    /// «чудище» среднего. Для зверья это правило работает почти без осечек.
    /// </summary>
    internal static class Wild
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Spread;
        internal static ConfigEntry<string> Fixed;
        internal static ConfigEntry<string> ByHand;
        internal static ConfigEntry<bool> Naming;
        internal static ConfigEntry<string> Grown;
        internal static ConfigEntry<string> Ancient;
        internal static ConfigEntry<float> Ripe;
        internal static ConfigEntry<string> Hurt;
        internal static ConfigEntry<float> Vary;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Wild", "Enabled", true,
                "Let a creature take its own level out of a range instead of every one of its "
                + "kind standing at the same number. A wood where every wolf is the fifth level "
                + "is the same wood in the first hour and the hundredth.");

            Spread = config.Bind("Wild", "Spread", 3,
                new ConfigDescription(
                    "How far above its blueprint a creature may be born, as a multiple. Three: a "
                    + "fifth-level wolf runs from the fifth to the fifteenth.",
                    new AcceptableValueRange<int>(1, 10)));

            Fixed = config.Bind("Wild", "Fixed",
                "DesertDragon=300,ForestDragon=300,LavaDragon=300,Mountain Dragon=300,"
                + "TheKingOfRot=300",
                "Creatures whose level is set by hand and has no range at all. These are the "
                + "peaks of the ladder, and a peak is not what the arithmetic happened to give — "
                + "it is what was meant.");

            ByHand = config.Bind("Wild", "ByHand",
                "Wolf=1-15,Wolf King=15-25,White Wolf=11-15,Werewolf=55-75",
                "Ranges written by hand, which override the common rule. Matched against the "
                + "creature's own name without whatever stands in brackets.");

            Hurt = config.Bind("Wild", "Hurt", "Small=5,Medium=18,Large=45,Giant=60,Titanic=90",
                "What a creature's blow is worth before anything multiplies it, by size. The "
                + "game writes this by hand for every beast and writes it badly: among "
                + "middling creatures it runs from three to sixty, a twentyfold spread between "
                + "animals of one height. Size is the honest measure, and strength and skill "
                + "multiply it afterwards exactly as they do for a man.");

            Vary = config.Bind("Wild", "Vary", 0.15f,
                new ConfigDescription(
                    "How far one beast's blow may stray from its kind, either way. Two wolves of "
                    + "a pack do not bite alike, no more than two swords of a smithy weigh alike. "
                    + "Thrown at birth and kept.",
                    new AcceptableValueRange<float>(0f, 0.5f)));

            Naming = config.Bind("Wild", "Naming", true,
                "Give a creature standing high in its range a word before its name, so that what "
                + "it is can be read before it reaches you.");

            Ripe = config.Bind("Wild", "Ripe", 0.5f,
                new ConfigDescription(
                    "From what part of its range a creature counts as grown. A half: the upper "
                    + "half of the range earns the word.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Grown = config.Bind("Wild", "Grown",
                "матёрый|матёрая|матёрое,злой|злая|злое,свирепый|свирепая|свирепое,"
                + "грозный|грозная|грозное,лютый|лютая|лютое",
                "Words for a creature high in its range: masculine, feminine and neuter forms "
                + "through a bar, several sets through commas. One is drawn at birth and stays "
                + "with the beast. Gender is guessed from how the name ends, which for animals "
                + "is nearly always right.");

            Ancient = config.Bind("Wild", "Ancient",
                "древний|древняя|древнее,ужасный|ужасная|ужасное,первый|первая|первое",
                "The same for a creature whose level is fixed — the peaks. These are met once.");
        }

        // ------------------------------------------------------------ вилки

        private static readonly Dictionary<string, int> fixedAt = new Dictionary<string, int>();
        private static readonly Dictionary<string, int[]> byHand = new Dictionary<string, int[]>();
        private static string fixedRead, handRead;

        private static void Learn()
        {
            string written = Fixed.Value ?? "";

            if (written != fixedRead)
            {
                fixedRead = written;
                fixedAt.Clear();

                foreach (string one in written.Split(','))
                {
                    string[] halves = one.Split('=');
                    if (halves.Length != 2) continue;

                    int level;
                    if (int.TryParse(halves[1].Trim(), out level))
                    {
                        fixedAt[halves[0].Trim().ToLowerInvariant()] = level;
                    }
                }
            }

            written = ByHand.Value ?? "";

            if (written != handRead)
            {
                handRead = written;
                byHand.Clear();

                foreach (string one in written.Split(','))
                {
                    string[] halves = one.Split('=');
                    if (halves.Length != 2) continue;

                    string[] span = halves[1].Split('-');
                    if (span.Length != 2) continue;

                    int low, high;
                    if (int.TryParse(span[0].Trim(), out low)
                        && int.TryParse(span[1].Trim(), out high))
                    {
                        byHand[halves[0].Trim().ToLowerInvariant()] = new[] { low, high };
                    }
                }
            }
        }

        /// <summary>
        /// How well this beast handles its own claw: fifty in a cub, a hundred in a grown one.
        ///
        /// У человека это выученная ступень оружия, и решает она не силу удара, а его точность:
        /// мастер находит стык. Зверю такой ступени игра не пишет, а нужна она ему не меньше —
        /// без неё коготь взрослого волка отличается от щенячьего одной лишь силой, и пробитие
        /// выходит скупым: до кольчуги зверь не достаёт вовсе.
        ///
        /// Мерка та же, по какой зверю дают прозвище: место его уровня в вилке своего вида.
        /// Молодой — пятьдесят, матёрый — сто, и множится это тем же счётом, что у человека.
        /// </summary>
        internal static int Honed(UnitAttribute who)
        {
            try
            {
                if (who == null || who is HumaniodUnit || who.Data == null) return 0;

                // Имя берём database'ное: к тому, что в записи, уже могло прирасти прозвище,
                // а по нему вилку в списке писаных руками не найти.
                string name = who.info != null ? who.info.unitName : null;
                if (string.IsNullOrEmpty(name)) name = who.Data.unitname;

                int born = who.info != null ? who.info.level : who.Data.level;

                int low, high;
                bool still;
                Span(name, born, out low, out high, out still);

                if (high <= low) return 100;

                float part = Mathf.Clamp01((who.Data.level - low) / (float)(high - low));
                return Mathf.RoundToInt(50f + 50f * part);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>Вилка этого вида: низ, верх и стоит ли уровень намертво.</summary>
        internal static void Span(string name, int born, out int low, out int high, out bool still)
        {
            Learn();

            low = high = born;
            still = false;

            string plain = (name ?? "").Trim().ToLowerInvariant();

            foreach (KeyValuePair<string, int> one in fixedAt)
            {
                if (plain.Contains(one.Key))
                {
                    low = high = one.Value;
                    still = true;
                    return;
                }
            }

            // Имя в игре идёт с внутренним в скобках; сверяем по видимой части.
            int bracket = plain.IndexOf('(');
            string bare = bracket > 0 ? plain.Substring(0, bracket).Trim() : plain;

            int[] span;
            if (byHand.TryGetValue(bare, out span))
            {
                low = span[0];
                high = span[1];
                return;
            }

            if (born > 0) high = born * Mathf.Max(1, Spread.Value);
        }

        // ------------------------------------------------------------ удар

        private static readonly Dictionary<string, float> hurt = new Dictionary<string, float>();
        private static string hurtRead;

        /// <summary>Чего стоит удар этого роста, до всяких множителей.</summary>
        internal static float Blow(UnitSize size)
        {
            string written = Hurt.Value ?? "";

            if (written != hurtRead)
            {
                hurtRead = written;
                hurt.Clear();

                foreach (string one in written.Split(','))
                {
                    string[] halves = one.Split('=');
                    if (halves.Length != 2) continue;

                    float much;
                    if (float.TryParse(halves[1].Trim(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out much))
                    {
                        hurt[halves[0].Trim()] = much;
                    }
                }
            }

            float got;
            return hurt.TryGetValue(size.ToString(), out got) ? got : 0f;
        }

        /// <summary>
        /// Поставить зверю удар по росту, с разбросом на особь.
        ///
        /// Игровые числа здесь негодны: у средних они гуляют от трёх до шестидесяти, то есть в
        /// двадцать раз между зверями одной высоты. Рост — мерка честная, а сила с мастерством
        /// домножат потом, ровно как человеку.
        /// </summary>
        internal static void Arm(UnitAttribute who)
        {
            try
            {
                float mid = Blow(who.size);
                if (mid <= 0f || who.weapons == null) return;

                float stray = 1f;
                if (Vary.Value > 0f)
                {
                    stray = 1f + UnityEngine.Random.Range(-Vary.Value, Vary.Value);
                }

                foreach (Weapon arm in who.weapons)
                {
                    if (arm == null || arm.BSdamage == null) continue;

                    // Вилку оставляем ту же по ширине, что была, а середину двигаем на свою.
                    foreach (KeyValuePair<DamageType, Damage> one in arm.BSdamage)
                    {
                        if (one.Value == null) continue;

                        float was = (one.Value.minDamage + one.Value.maxDamage) * 0.5f;
                        if (was <= 0f) continue;

                        float spread = (one.Value.maxDamage - one.Value.minDamage) * 0.5f;
                        float want = mid * stray;

                        one.Value.minDamage = Mathf.Max(1f, want - spread * want / was);
                        one.Value.maxDamage = want + spread * want / was;
                    }
                }
            }
            catch
            {
            }
        }

        // ------------------------------------------------------------ прозвище

        private static readonly char[] Ends = { 'а', 'я' };
        /// <summary>Имя так, как его увидит игрок: по нему и судим о роде.</summary>
        private static string Said(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;

            try
            {
                string said = gameManager.LocalizedNameString(key);
                return string.IsNullOrEmpty(said) ? key : said;
            }
            catch
            {
                return key;
            }
        }



        /// <summary>
        /// Какого рода имя. Для зверья окончание решает почти всегда: «крыса» женского,
        /// «чудище» среднего, всё прочее мужского.
        /// </summary>
        private static int Gender(string name)
        {
            string plain = (name ?? "").Trim();
            if (plain.Length == 0) return 0;

            int bracket = plain.IndexOf('(');
            if (bracket > 0) plain = plain.Substring(0, bracket).Trim();

            // Берём последнее слово: у «Короля червей» род задаёт «король».
            string[] words = plain.Split(' ');
            string last = words.Length > 0 ? words[0] : plain;
            if (last.Length == 0) return 0;

            char tail = char.ToLowerInvariant(last[last.Length - 1]);

            if (tail == 'а' || tail == 'я') return 1;
            if (tail == 'о' || tail == 'е') return 2;

            return 0;
        }

        private static string Word(string written, int gender, int drawn)
        {
            string[] sets = (written ?? "").Split(',');
            if (sets.Length == 0) return null;

            string set = sets[Mathf.Abs(drawn) % sets.Length].Trim();
            if (set.Length == 0) return null;

            string[] forms = set.Split('|');
            if (forms.Length == 0) return null;

            return forms[Mathf.Min(gender, forms.Length - 1)].Trim();
        }

        /// <summary>Прозвище по месту в вилке. Пусто — значит молодняк, ему прозвища нет.</summary>
        internal static string Rank(string name, int level, int low, int high, bool still)
        {
            if (!Naming.Value) return null;

            // Род берём у русского имени, а не у ключа. Ключ английский — «Horse», «Dog», —
            // и окончание в нём ничего не значит: выходило «грозный Лошадь».
            int gender = Gender(Said(name));
            int drawn = UnityEngine.Random.Range(0, 1000);

            if (still) return Word(Ancient.Value, gender, drawn);

            if (high <= low) return null;

            float part = (level - low) / (float)(high - low);
            if (part < Ripe.Value) return null;

            return Word(Grown.Value, gender, drawn);
        }
    }

    /// <summary>
    /// Здесь особь берёт свой уровень. Игра ставит его из чертежа один на всех; мы тянем из
    /// вилки и тут же вешаем прозвище, чтобы оно ушло в сохранение вместе с ней.
    /// </summary>
    [HarmonyPatch(typeof(UnitAttribute), "InitializeUnit")]
    internal static class Born_Wild_Patch
    {
        private static void Postfix(UnitAttribute __instance)
        {
            if (Wild.Enabled == null || !Wild.Enabled.Value) return;

            try
            {
                if (__instance == null || __instance.Data == null || __instance.info == null) return;

                // Людей не трогаем: у них свои уровни, своя раздача очков и свои имена.
                if (__instance is HumaniodUnit) return;

                // Призванному уровень ставит призвавший — не наше дело.
                if (__instance.Data.level != __instance.info.level) return;

                string name = __instance.Data.unitname;
                if (string.IsNullOrEmpty(name)) name = __instance.info.unitName;

                int low, high;
                bool still;
                Wild.Span(name, __instance.info.level, out low, out high, out still);

                int level = still ? low : UnityEngine.Random.Range(low, high + 1);
                if (level < 1) level = 1;

                __instance.Data.level = level;

                Wild.Arm(__instance);

                string rank = Wild.Rank(name, level, low, high, still);

                if (!string.IsNullOrEmpty(rank) && !name.StartsWith(rank))
                {
                    __instance.Data.unitname = rank + " " + name;
                }
            }
            catch
            {
            }
        }
    }
}
