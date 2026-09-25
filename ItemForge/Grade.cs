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
    /// Makes the colour of a thing worth the colour.
    ///
    /// Качество в этой игре добавляет вещи строчек, а не веса. Фиолетовая кираса получает три
    /// лишние приписки вместо одной — «+1 к силе», «+3 % урона по ящерицам», — и каждая из них
    /// ровно такая же, как у белой. Оттого цвет рамки читается как «чуть больше мелочи», а не
    /// как «другая вещь».
    ///
    /// Здесь множится сама величина приписки: синее вдвое, фиолетовое втрое, легендарное
    /// впятеро. Было «+1 сила» — стало три у фиолетового и два у синего; было «+3 % по
    /// ящерицам» — стало девять и шесть.
    ///
    /// Ничего не переписывается в самой вещи и ничего не попадает в сохранение. Игра уже
    /// посчитала приписки один раз в полную величину, поэтому здесь просто досчитывается
    /// недостающая доля поверх — и при следующей загрузке ничего не умножится второй раз.
    /// Части комплекта Короля Ада исключены нарочно: у них своя арифметика.
    /// </summary>
    internal static class Grade
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Ladder;
        internal static ConfigEntry<float> Forged;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Grade", "Enabled", true,
                "Let the colour of a thing multiply what the thing gives, not merely how many "
                + "lines it gives it on. The game grants extra affixes with quality and leaves "
                + "each of them the same size as a white item's.");

            Ladder = config.Bind("Grade", "Ladder", "1,1,1,2,3,5",
                "What each grade multiplies its affixes by, from poor to legendary: poor, "
                + "common and uncommon stand as they are, rare doubles, epic triples, legendary "
                + "gives five times. This reads off the thing in hand, not off its blueprint, so "
                + "a white sword that rolled purple counts as purple.");

            Trace = config.Bind("Grade", "Trace", true,
                "Write a line to the log for every tooltip drawn: which kind of slot it came "
                + "from, which thing was in it, its colour and the multiplier it got. For "
                + "finding out why one panel of a comparison disagrees with the other.");

            Engraves = config.Bind("Grade", "Engraves", true,
                "Strengthen the things whose bonuses were written by hand rather than rolled — "
                + "the unique blades and harnesses, and every piece of a set. Those never pass "
                + "through the rolling at all: they are born finished, so there is no moment of "
                + "making to write anything into. Their recipe is written instead, once at "
                + "startup. Recipes are not saved, so this one is reversible: switch the mod off "
                + "and they are as they were.");

            Bake = config.Bind("Grade", "Bake", true,
                "Write the strengthening into the thing itself, at the moment it is made, "
                + "instead of adding it up again at every reckoning. Then the figure in the "
                + "tooltip and the figure in the fight are the same figure, because they are one "
                + "number and not two. It only touches things made from now on — what already "
                + "lies in a chest keeps the numbers it was made with — and it goes into the "
                + "save, so turning the mod off afterwards leaves those things strengthened.");

            Truthful = config.Bind("Grade", "Truthful", false,
                "Show the strengthened figures in the tooltip rather than the ones written on "
                + "the item. It is done by swapping the list of bonuses for the moment the panel "
                + "reads it and swapping it back after, and that swap is the suspect in tooltips "
                + "that blink out and come back. Off, the tooltip understates a rare or "
                + "legendary item — the fight uses the full figures either way.");

            Forged = config.Bind("Grade", "Forged", 2f,
                new ConfigDescription(
                    "What the King of Hell set multiplies its affixes by, apart from the ladder "
                    + "above. Its pieces are legendary and would take five times over, which is "
                    + "too much on top of a recipe already written by hand; twice — a blue "
                    + "thing's share — is enough to matter and little enough to keep the set "
                    + "balanced against itself. Zero puts it back on the common ladder.",
                    new AcceptableValueRange<float>(0f, 20f)));
        }

        /// <summary>What this particular thing multiplies its affixes by.</summary>
        internal static float Factor(Inventory thing)
        {
            if (!Enabled.Value || thing == null || thing.itemInfo == null) return 1f;

            UIEquipmentInfo worn = thing.itemInfo as UIEquipmentInfo;
            if (worn == null) return 1f;

            // Комплект считается по своей мерке, а не по цвету. Он легендарный, и общая
            // лестница дала бы ему впятеро — поверх рецепта, написанного вручную, это перебор.
            if (Forged.Value > 0f && Forge.Built.Contains(worn)) return Forged.Value;

            float[] steps = Steps();
            int grade = (int)thing.quality;
            if (grade < 0 || grade >= steps.Length) return 1f;

            return steps[grade];
        }

        // ----------------------------------------------------------------- рукописные вещи

        private static readonly HashSet<int> engraved = new HashSet<int>();
        private static bool swept;

        /// <summary>Writes the strengthening into the recipes of hand-made things.</summary>
        internal static void Engrave()
        {
            if (!Enabled.Value || !Engraves.Value || swept) return;

            try
            {
                UIItemDatabase book = UIItemDatabase.Instance;
                if (book == null || book.items == null) return;

                swept = true;
                int done = 0;

                foreach (UIItemInfo thing in book.items)
                {
                    UIEquipmentInfo worn = thing as UIEquipmentInfo;
                    if (worn == null || worn.addAttrs == null || worn.addAttrs.Count == 0) continue;

                    if (!engraved.Add(worn.ID)) continue;

                    // По собственной редкости заготовки, а не по редкости выпавшего экземпляра.
                    // У обычного меча она обычная, и множитель равен единице — такие вещи не
                    // трогаются вовсе, их усилит раскатка при рождении. А у уникальной она и
                    // есть та самая, с которой вещь родится.
                    float much = Step((int)worn.Quality, worn);
                    if (Mathf.Approximately(much, 1f)) continue;

                    List<AddonAttributes> mine = new List<AddonAttributes>(worn.addAttrs.Count);

                    // Рецепт, каким его написал автор, — чтобы узнать в сохранении вещи,
                    // сделанные до усиления, и подтянуть их к усиленному.
                    List<AddonAttributes> first = new List<AddonAttributes>(worn.addAttrs.Count);
                    foreach (AddonAttributes one in worn.addAttrs)
                    {
                        if (one != null) first.Add(new AddonAttributes(one));
                    }
                    originals[worn.ID] = first;

                    foreach (AddonAttributes one in worn.addAttrs)
                    {
                        if (one == null) continue;

                        AddonAttributes copy = new AddonAttributes(one);

                        // Только достоинства. У вещей бывают собственные штрафы — минус к
                        // скорости атаки, минус к ходьбе, — и множитель редкости углублял их
                        // вместе с пользой: минус десять процентов превращался в минус
                        // пятьдесят. Две такие вещи, и скорость атаки уходит в ноль, а игра
                        // считает готовность оружия как единицу, делённую на неё. Оружие не
                        // готово никогда, обычный удар не начинается — а способности идут мимо
                        // этого счёта и работают. Ровно то, что мы и увидели.
                        // Сопротивления цвет не множит: они такие, как вписал автор.
                        if (one.value > 0f && !Resist(one.type)) copy.value = one.value * much;

                        mine.Add(copy);
                    }

                    worn.addAttrs = mine;
                    done++;
                }

                ItemForgePlugin.Log.LogInfo("Рукописные вещи усилены по редкости: " + done + ".");

                // То, что уже лежит у отряда, сделано по прежнему рецепту. Подтягиваем.
                Catch();
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог усилить рукописные вещи: " + e.Message);
            }
        }

        private static float Step(int grade, UIEquipmentInfo worn)
        {
            if (Forged.Value > 0f && Forge.Built.Contains(worn)) return Forged.Value;

            float[] steps = Steps();
            if (grade < 0 || grade >= steps.Length) return 1f;

            return steps[grade];
        }

        // ----------------------------------------------------------------- показ в подсказке

        // Подмена живёт ровно один вызов отрисовки. Хранится список, снятый с вещи, и сама
        // вещь, чтобы вернуть его на место; сметается и в постфиксе, и каждый кадр — на случай,
        // если отрисовка сорвётся посередине и постфикс не выполнится вовсе.
        private static Inventory dressed;
        private static List<AddonAttributes> bare;

        internal static ConfigEntry<bool> Trace;
        internal static ConfigEntry<bool> Truthful;
        internal static ConfigEntry<bool> Bake;
        internal static ConfigEntry<bool> Engraves;

        private static readonly Dictionary<string, int> told = new Dictionary<string, int>();

        /// <summary>Puts the multiplied numbers on the thing for as long as it is being drawn.</summary>
        internal static void Dress(UISlotBase slot)
        {
            Undress();

            if (Truthful != null && !Truthful.Value) return;

            // Сравнение с пустотой здесь только через ссылку. Unity перегружает «==» для
            // своих объектов, и ячейка второй панели — MonoBehaviour, созданный через «new»,
            // а не движком, — отвечает «я null», хотя жива и читается. Родной «==» молча
            // выбрасывал её отсюда, и панель сравнения оставалась с неумноженными числами.
            if (!Enabled.Value || (object)slot == null) return;

            try
            {
                // Оба вида ячеек держат вещь в открытом поле, и игра сама читает её точно так
                // же — в начале SetValue. Берём тем же способом, без отражения: имя поля не
                // может разойтись с тем, что скомпилировано.
                Inventory thing = null;

                UIItemSlot bag = slot as UIItemSlot;
                if ((object)bag != null) thing = bag.inventory;
                else
                {
                    UIEquipSlot worn = slot as UIEquipSlot;
                    if ((object)worn != null) thing = worn.inventory;
                }

                float much = thing == null ? 1f : Factor(thing);

                // Счёт ведётся по каждому виду ячейки отдельно, а не общим числом. Общее
                // съедала перерисовка сумки — два десятка ячеек подряд, — и панель сравнения,
                // ради которой всё и писалось, в лог уже не попадала.
                string kind = slot.GetType().Name;
                int said;
                told.TryGetValue(kind, out said);

                if (Trace != null && Trace.Value && said < 10)
                {
                    told[kind] = said + 1;
                    ItemForgePlugin.Log.LogInfo($"Подсказка: ячейка {slot.GetType().Name}, "
                        + (thing == null || thing.itemInfo == null
                            ? "вещи нет."
                            : $"«{thing.itemInfo.Name}», цвет {thing.quality}, "
                              + $"приписок {(thing.addAttrs == null ? 0 : thing.addAttrs.Count)}, "
                              + $"множитель {much:0.##}."));
                }

                if (thing == null || thing.addAttrs == null || thing.addAttrs.Count == 0) return;
                if (Mathf.Approximately(much, 1f)) return;

                List<AddonAttributes> grown = new List<AddonAttributes>(thing.addAttrs.Count);

                foreach (AddonAttributes one in thing.addAttrs)
                {
                    if (one == null) continue;

                    float by = Resist(one.type) ? 1f : much;
                    grown.Add(new AddonAttributes(one.type, one.value * by, one.levelAlter * by));
                }

                dressed = thing;
                bare = thing.addAttrs;
                thing.addAttrs = grown;
            }
            catch
            {
                Undress();
            }
        }

        /// <summary>True while this thing is wearing the multiplied numbers for the tooltip.</summary>
        internal static bool Dressed(Inventory thing)
        {
            return dressed != null && (object)dressed == (object)thing;
        }

        /// <summary>Gives the thing its own list back.</summary>
        internal static void Undress()
        {
            if (dressed == null) return;

            try { dressed.addAttrs = bare; }
            catch { }

            dressed = null;
            bare = null;
        }

        // ----------------------------------------------------------------- лестница

        private static float[] steps;
        private static string read;

        // ----------------------------------------------------------------- одна мерка на всё

        /// <summary>
        /// Метка на самой вещи: во сколько раз её приписки уже усилены.
        ///
        /// Без неё вещь, получившая цвет дважды — выпала зелёной, а потом дотянута до
        /// фиолетовой минимумом цвета у босса, — умножалась дважды: сперва на своё, потом на
        /// новое поверх. А кованая вещь не умножалась вовсе: кузнец вешает приписки сам, мимо
        /// того места, где вещь получает цвет. Метка лежит в списке приписок вещи и уходит в
        /// сохранение вместе с ней; игра её не видит.
        /// </summary>
        internal const AddonAttribute MarkGrade = (AddonAttribute)9101;

        /// <summary>Сопротивления — к рубящему, дробящему, колющему и шести стихиям.</summary>
        internal static bool Resist(AddonAttribute type)
        {
            int n = (int)type;
            return n >= 100 && n <= 108;
        }

        /// <summary>Метка на вещи: сопротивления, умноженные прежде цветом, возвращены.</summary>
        internal const AddonAttribute MarkResist = (AddonAttribute)9102;

        // Рецепты рукописных вещей такими, какими их написал автор, — до усиления.
        private static readonly Dictionary<int, List<AddonAttributes>> originals =
            new Dictionary<int, List<AddonAttributes>>();

        private static int synced;

        /// <summary>
        /// Puts right what an older build wrote into a thing, once, when the thing is read.
        ///
        /// Три поправки, и все про то, что вещь несёт в себе из сохранения. Сопротивления,
        /// умноженные прежде цветом, делятся обратно. Сопротивления магии, которые мы сами
        /// дописывали хорошим амулетам, — по одному числу на все шесть стихий, — снимаются: у
        /// амулетов защита от магии теперь такая, как в игре. И рукописная вещь, сделанная до
        /// того, как её рецепт усилили по редкости, подтягивается к усиленному.
        /// </summary>
        internal static void Heal(Inventory thing)
        {
            if (thing == null || thing.itemInfo == null || thing.addAttrs == null) return;

            UIEquipmentInfo worn = thing.itemInfo as UIEquipmentInfo;
            if (worn == null) return;

            try
            {
                bool marked = false;
                foreach (AddonAttributes one in thing.addAttrs)
                {
                    if (one != null && one.type == MarkResist) { marked = true; break; }
                }

                if (!marked)
                {
                    float at = Graded(thing);

                    // Наши шесть одинаковых снимаем. Родные у амулетов лежат в самой вещи, а
                    // не в приписках, и этого не касаются.
                    if (worn.EquipType == EquipSlotType.neck) Unward(thing);

                    if (at > 1.0001f)
                    {
                        foreach (AddonAttributes one in thing.addAttrs)
                        {
                            if (one == null || !Resist(one.type) || one.value <= 0f) continue;
                            one.value = Mathf.Round(one.value / at * 10f) / 10f;
                        }
                    }

                    thing.addAttrs.Add(new AddonAttributes(MarkResist, 1f));
                }

                Sync(thing);

                // Сопротивления железу больше ничего не значат: против удара отвечает броня.
                Steel.Strip(thing.addAttrs);

                // Прежняя прибавка посоха к заклинаниям, вписанная в образец, — снимаем: теперь
                // посох даёт её сам, по ярусу, цвету и своей доле, мимо приписок.
                Unstaff(thing);
            }
            catch
            {
            }
        }

        private static readonly float[] oldStaff = { 0.15f, 0.3f, 0.5f, 0.8f, 1.3f };

        /// <summary>Takes off the staff power an older build wrote into every plain staff.</summary>
        private static void Unstaff(Inventory thing)
        {
            UIWeaponInfo blade = thing.itemInfo as UIWeaponInfo;
            if (blade == null || blade.isUnique || !Wield.Wand(blade)) return;

            int tier = (int)blade.tier;
            if (tier < 1 || tier > oldStaff.Length) return;

            float was = oldStaff[tier - 1];
            float at = Graded(thing);

            thing.addAttrs.RemoveAll(one => one != null && one.type == AddonAttribute.MagicDamage
                && (Mathf.Abs(one.value - was) < 0.011f || Mathf.Abs(one.value - was * at) < 0.02f
                    || Mathf.Abs(one.value - Up(AddonAttribute.MagicDamage, was * at)) < 0.02f));
        }

        /// <summary>Removes the six equal magic resistances an older build wrote into amulets.</summary>
        private static void Unward(Inventory thing)
        {
            List<AddonAttributes> six = new List<AddonAttributes>();
            float first = -1f;

            foreach (AddonAttributes one in thing.addAttrs)
            {
                if (one == null) continue;

                int n = (int)one.type;
                if (n < 103 || n > 108) continue;

                if (first < 0f) first = one.value;
                if (Mathf.Abs(one.value - first) > 0.011f) return;

                six.Add(one);
            }

            if (six.Count != 6) return;

            foreach (AddonAttributes one in six) thing.addAttrs.Remove(one);
        }

        /// <summary>Brings a hand-made thing made before its recipe was strengthened up to it.</summary>
        private static void Sync(Inventory thing)
        {
            if (!Enabled.Value || !Engraves.Value || thing == null) return;

            UIEquipmentInfo worn = thing.itemInfo as UIEquipmentInfo;
            if (worn == null || worn.addAttrs == null || thing.addAttrs == null) return;

            List<AddonAttributes> was;
            if (!originals.TryGetValue(worn.ID, out was) || was == null) return;

            float much = Step((int)worn.Quality, worn);

            // Приписки вещи — по порядку, как в рецепте, не считая наших меток и снятых
            // сопротивлений железу: их нет ни в рецепте, ни в вещи.
            List<AddonAttributes> own = new List<AddonAttributes>();
            foreach (AddonAttributes one in thing.addAttrs)
            {
                if (one != null && (int)one.type < 9000 && !Steel.Physical(one.type)) own.Add(one);
            }

            List<AddonAttributes> plainWas = new List<AddonAttributes>();
            foreach (AddonAttributes one in was)
            {
                if (one != null && !Steel.Physical(one.type)) plainWas.Add(one);
            }
            was = plainWas;

            if (own.Count != was.Count) return;

            for (int i = 0; i < own.Count; i++)
            {
                if (own[i].type != was[i].type) return;
            }

            int raised = 0;

            for (int i = 0; i < own.Count; i++)
            {
                float plain = was[i].value;
                float want = (plain > 0f && !Resist(was[i].type)) ? plain * much : plain;

                // Своё, ни на рецепт, ни на усиленный рецепт не похожее, не трогаем: вещь
                // перековали, и это уже её собственные числа.
                bool asWritten = Mathf.Abs(own[i].value - plain) < 0.011f;
                bool asRaised = Mathf.Abs(own[i].value - plain * much) < 0.011f;
                if (!asWritten && !asRaised) continue;

                if (Mathf.Abs(own[i].value - want) > 0.011f)
                {
                    own[i].value = want;
                    raised++;
                }
            }

            if (raised > 0 && synced < 60)
            {
                synced++;
                ItemForgePlugin.Log.LogInfo("Вещь «" + worn.Name + "» подтянута к рецепту: "
                    + "приписок исправлено " + raised + ".");
            }
        }

        /// <summary>Puts right what the party already carries and wears.</summary>
        private static void Catch()
        {
            try
            {
                if (PartyManager.instance == null || PartyManager.instance.partyMembers == null) return;

                foreach (HumaniodUnit man in PartyManager.instance.partyMembers)
                {
                    if (man == null || man.equipmentmanger == null) continue;

                    if (man.equipmentmanger.equipInfos != null)
                    {
                        foreach (EquipInfo slot in man.equipmentmanger.equipInfos)
                        {
                            if (slot != null && slot.IsEquiped()) Sync(slot.inventory);
                        }
                    }

                    ItemStock bag = man.equipmentmanger.inventory;
                    if (bag != null && bag.items != null)
                    {
                        foreach (Inventory one in bag.items) Sync(one);
                    }

                    man.UpdateAttribute();
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог подтянуть вещи отряда: " + e.Message);
            }
        }

        /// <summary>Во сколько раз усилены приписки этой вещи сейчас.</summary>
        internal static float Graded(Inventory thing)
        {
            if (thing == null || thing.addAttrs == null) return 1f;

            foreach (AddonAttributes one in thing.addAttrs)
            {
                if (one != null && one.type == MarkGrade && one.value > 0f) return one.value;
            }

            return 1f;
        }

        /// <summary>Сколько сейчас каждой приписки — только достоинства, без наших меток.</summary>
        internal static Dictionary<AddonAttribute, float> Sums(Inventory thing)
        {
            Dictionary<AddonAttribute, float> got = new Dictionary<AddonAttribute, float>();
            if (thing == null || thing.addAttrs == null) return got;

            foreach (AddonAttributes one in thing.addAttrs)
            {
                if (one == null || (int)one.type >= 9000 || one.value <= 0f) continue;
                if (Resist(one.type)) continue;

                float had;
                got.TryGetValue(one.type, out had);
                got[one.type] = had + one.value;
            }

            return got;
        }

        // Эти игра показывает и считает целыми: «+1 к силе», а не «+1,4».
        private static readonly HashSet<AddonAttribute> whole = new HashSet<AddonAttribute>
        {
            AddonAttribute.Strength, AddonAttribute.Endurance, AddonAttribute.Agility,
            AddonAttribute.Precision, AddonAttribute.Intelligence, AddonAttribute.Willpower,
            AddonAttribute.Persuade, AddonAttribute.Bargain, AddonAttribute.Intimidate,
            AddonAttribute.Pathfind, AddonAttribute.Insight, AddonAttribute.Sneak,
            AddonAttribute.Mechanics, AddonAttribute.Theft, AddonAttribute.Scholarly,
            AddonAttribute.Smithing, AddonAttribute.Alchemy, AddonAttribute.Cooking,
            AddonAttribute.Attack, AddonAttribute.BlockBreak, AddonAttribute.Dodge,
            AddonAttribute.Defence, AddonAttribute.HP, AddonAttribute.EP, AddonAttribute.MP,
        };

        /// <summary>Округление вверх до того шага, каким число показывается.</summary>
        private static float Up(AddonAttribute type, float value)
        {
            if (value <= 0f) return value;

            if (whole.Contains(type)) return Mathf.Ceil(value - 0.0001f);

            // Доли показываются целыми процентами: 3,6 % — это 4 %.
            if (value < 1f) return Mathf.Ceil(value * 100f - 0.001f) / 100f;

            return Mathf.Ceil(value * 10f - 0.001f) / 10f;
        }

        /// <summary>
        /// Brings a thing's affixes to what its colour asks for.
        ///
        /// Что лежало на вещи до этого прохода — было уже усилено по прежнему цвету и
        /// пересчитывается с прежнего множителя на нынешний; что прибыло сейчас — пришло из
        /// игры голым и умножается целиком. Выходит ровно «как у игры, умноженное на цвет»,
        /// сколько бы раз вещь ни получала цвет и каким бы путём ни получала.
        /// </summary>
        internal static void Settle(Inventory thing, Dictionary<AddonAttribute, float> before, float beforeAt)
        {
            if (!Enabled.Value || thing == null || thing.addAttrs == null || thing.addAttrs.Count == 0) return;

            float want = Factor(thing);
            if (beforeAt <= 0f) beforeAt = 1f;

            Dictionary<AddonAttribute, float> now = Sums(thing);

            // Своя копия каждой приписки: список у новой вещи — мелкая копия заготовки, и
            // умножить на месте значило бы усилить заготовку, а через неё всё, что родится.
            List<AddonAttributes> mine = new List<AddonAttributes>(thing.addAttrs.Count + 1);

            foreach (AddonAttributes one in thing.addAttrs)
            {
                if (one == null || one.type == MarkGrade) continue;

                AddonAttributes copy = new AddonAttributes(one);

                // Только достоинства: штрафы вещи усиливать незачем. И не сопротивления: их
                // цвет не множит, они такие, как вписал автор или выпал жребий.
                if (one.value > 0f && (int)one.type < 9000 && !Resist(one.type))
                {
                    float total = now[one.type];

                    float had;
                    if (before == null || !before.TryGetValue(one.type, out had)) had = 0f;
                    had = Mathf.Min(had, total);

                    float came = total - had;
                    float goal = had * (want / beforeAt) + came * want;

                    if (total > 0f) copy.value = one.value * goal / total;
                    copy.value = Up(copy.type, copy.value);
                }

                mine.Add(copy);
            }

            if (!Mathf.Approximately(want, 1f)) mine.Add(new AddonAttributes(MarkGrade, want));

            Steel.Strip(mine);
            thing.addAttrs = mine;
        }

        private static float[] Steps()
        {
            string written = Ladder.Value ?? "";
            if (steps != null && written == read) return steps;

            List<float> got = new List<float>();

            foreach (string one in written.Split(','))
            {
                float much;
                if (float.TryParse(one.Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out much))
                {
                    got.Add(Mathf.Clamp(much, 0.1f, 20f));
                }
            }

            while (got.Count < 6) got.Add(1f);

            read = written;
            steps = got.ToArray();
            return steps;
        }
    }

    // Здесь вещь только что получила свои случайные свойства и уже знает свою редкость —
    // единственный момент, когда известно и то и другое. Дальше она уходит в мир и в
    // сохранение с теми числами, что мы ей тут запишем.
    [HarmonyPatch(typeof(EquipmentMaker), "EnhanceEquipmentToQuality")]
    internal static class EnhanceToQuality_Grade_Patch
    {
        private sealed class Before
        {
            internal Dictionary<AddonAttribute, float> sums;
            internal float at;
        }

        private static void Prefix(Inventory inv, out object __state)
        {
            __state = null;

            try
            {
                if (!Grade.Enabled.Value || !Grade.Bake.Value || inv == null) return;

                __state = new Before { sums = Grade.Sums(inv), at = Grade.Graded(inv) };
            }
            catch
            {
            }
        }

        private static void Postfix(Inventory inv, object __state)
        {
            Before was = __state as Before;
            if (was == null) return;

            try
            {
                Grade.Settle(inv, was.sums, was.at);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог вписать усиление в вещь: " + e.Message);
            }
        }
    }

    // Сколько приписок вещи ещё положено, игра считает по тем, что на ней уже висят: каждая
    // идёт за столько долей, во сколько раз она больше игровой. Наши усиленные казались ей
    // вдвое и втрое больше — и вещь, дотянутая до нового цвета, недополучала новых приписок.
    // На время этого счёта даём ей игровые величины.
    [HarmonyPatch(typeof(EquipmentMaker), "GetQualityBuyPoint")]
    internal static class BuyPoint_Grade_Patch
    {
        private static void Prefix(Inventory inv, out List<AddonAttributes> __state)
        {
            __state = null;

            try
            {
                if (inv == null || inv.addAttrs == null) return;

                float at = Grade.Graded(inv);
                if (Mathf.Approximately(at, 1f)) return;

                __state = inv.addAttrs;

                List<AddonAttributes> bare = new List<AddonAttributes>(__state.Count);
                foreach (AddonAttributes one in __state)
                {
                    if (one == null || (int)one.type >= 9000) continue;

                    AddonAttributes copy = new AddonAttributes(one);
                    if (copy.value > 0f) copy.value /= at;
                    bare.Add(copy);
                }

                inv.addAttrs = bare;
            }
            catch
            {
                __state = null;
            }
        }

        private static void Finalizer(Inventory inv, List<AddonAttributes> __state)
        {
            if (__state != null && inv != null) inv.addAttrs = __state;
        }
    }

    // Игра уже насчитала приписки в полную величину, поэтому здесь доливается недостающее:
    // фиолетовому ещё две доли, легендарному ещё четыре. Так ни один список не переписывается
    // и в сохранение не попадает ничего лишнего.
    [HarmonyPatch(typeof(HumaniodUnit), "CountEquipmentsBonus")]
    internal static class CountEquipmentsBonus_Grade_Patch
    {
        private static void Postfix(HumaniodUnit __instance)
        {
            if (!Grade.Enabled.Value) return;

            // Когда усиление вписано в саму вещь, доливать нечего: оно уже в её списке.
            if (Grade.Bake.Value) return;

            try
            {
                if (__instance == null || __instance.Data == null) return;
                if (!__instance.Data.equipsInited) return;

                EquipmentManager gear = __instance.equipmentmanger;
                if (gear == null || !gear.isInited || gear.equipInfos == null) return;

                foreach (EquipInfo slot in gear.equipInfos)
                {
                    if (slot == null || !slot.IsEquiped()) continue;

                    Inventory thing = slot.inventory;

                    // Сломанное игра не считает вовсе, и мы вслед за ней.
                    if (thing.DurState >= 3) continue;
                    if (thing.addAttrs == null || thing.addAttrs.Count == 0) continue;

                    // Если у вещи сейчас надет показной список — тот, что рисуется в подсказке, —
                    // он уже умножен, и доливать к нему значило бы умножить дважды.
                    if (Grade.Dressed(thing)) continue;

                    float much = Grade.Factor(thing);
                    if (Mathf.Approximately(much, 1f)) continue;

                    List<AddonAttributes> gives = new List<AddonAttributes>(thing.addAttrs.Count);
                    foreach (AddonAttributes one in thing.addAttrs)
                    {
                        if (one != null && !Grade.Resist(one.type)) gives.Add(one);
                    }

                    __instance.CountBonus(gives, much - 1f);
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог досчитать по редкости: " + e.Message);
            }
        }
    }

    // Вторая отметка, чтобы развилка была однозначной: сюда игра приходит, когда решает
    // показать подсказку — и для наведённой вещи, и для той, с которой её сравнивают. Если в
    // логе есть эта строка, а строки про SetValue нет, значит панель рисуется мимо подмены; а
    // если нет и этой — значит вторую панель зовут откуда-то ещё.
    [HarmonyPatch(typeof(UIItemTip), "Show")]
    internal static class Show_Grade_Patch
    {
        private static void Prefix(UISlotBase slotBase, int equipCount)
        {
            try
            {
                if (Grade.Trace == null || !Grade.Trace.Value) return;

                ItemForgePlugin.Log.LogInfo("Показ подсказки: ячейка "
                    + ((object)slotBase == null ? "нет" : slotBase.GetType().Name)
                    + ", счёт сравнения " + equipCount + ".");
            }
            catch
            {
            }
        }
    }

    // Подсказка должна показывать то, что персонаж на самом деле получит, иначе цифры на
    // экране и цифры в бою разойдутся, и верить будет нечему.
    [HarmonyPatch(typeof(UIItemTip), "SetValue")]
    internal static class ItemTip_Grade_Patch
    {
        private static void Prefix(UISlotBase slot)
        {
            try { Grade.Dress(slot); }
            catch { Grade.Undress(); }
        }

        private static void Postfix()
        {
            Grade.Undress();
        }
    }

    // Вещь, прочитанная из сохранения, — единственный миг, когда видно, что в ней записала
    // прежняя сборка.
    [HarmonyPatch(typeof(Inventory), MethodType.Constructor, new Type[] { typeof(ItemSaveData) })]
    internal static class InventoryLoad_Grade_Patch
    {
        private static void Postfix(Inventory __instance)
        {
            Grade.Heal(__instance);
        }
    }
}
