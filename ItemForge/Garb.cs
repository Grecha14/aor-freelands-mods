using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Makes the weight of armour tell on the man inside it.
    ///
    /// До сих пор выбор доспеха выбором не был. Вычет у лат в двадцать пять раз больше, чем у
    /// ткани, а платят за них одним весом — который снимается силой: три килограмма за очко
    /// при силе «4 + 0.75 × уровень» дают к сороковому уровню сто килограммов вместимости при
    /// полном наборе лат в сорок. Перегруза нет, значит нет и платы, и латы носят все.
    ///
    /// Здесь у доспеха появляется вторая сторона. Лёгкий даёт ход и уворот, средний — точность
    /// удара, тяжёлый отнимает и то, и другое, и замах. Всё считается от веса куска, так что
    /// ступень тира входит в счёт сама: латы пятой ступени тяжелее вторых и штрафуют сильнее.
    ///
    /// Заодно чинится ярлык. Игра делит доспехи на лёгкие, средние и тяжёлые по своему полю, и
    /// делит скверно: мягкая кожа у неё в тяжёлых, полулаты в лёгких, а один и тот же
    /// металлический шлем числится то средним, то тяжёлым. Мы это поле переписываем по весу —
    /// и карточка перестаёт спорить сама с собой, и людские таланты на лёгкий доспех начинают
    /// применяться к тому, что и правда лёгкое.
    /// </summary>
    internal static class Garb
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Share;
        internal static ConfigEntry<string> Bands;
        internal static ConfigEntry<string> Speed;
        internal static ConfigEntry<string> Dodging;
        internal static ConfigEntry<string> Aiming;
        internal static ConfigEntry<string> Swing;
        internal static ConfigEntry<bool> Relabel;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Garb", "Enabled", true,
                "Let the weight of armour tell on the one wearing it: light gives speed and "
                + "evasion, middling gives a steadier hand, heavy takes all three away. Without "
                + "this the only thing armour costs is weight, and weight is bought off with "
                + "strength — so plate is simply better than everything and there is nothing "
                + "to choose.");

            Share = config.Bind("Garb", "Share", "head=0.2,chest=0.5,pants=0.3",
                "What share of a full suit each piece is. The same division «Burden» uses for "
                + "weight, and for the same reason: a helmet is a fifth of the iron, a cuirass "
                + "a half. A full suit therefore gives exactly what the ladder below says, and "
                + "a man who mixes pieces gets what he put on.");

            Speed = config.Bind("Garb", "Speed",
                "3.6=0,5.3=0,7.1=0,9.8=0,14.2=0,17.8=0,19.6=-6,23.1=-13,26.7=-20,40=-25",
                "What a full suit of this weight does to walking, as a percentage, by the "
                + "weight of the suit in kilograms. Read between the written points, held flat "
                + "beyond the ends.\n\n"
                + "Sideways only: armour can slow a man and cannot hurry him. Light cloth used "
                + "to be worth a fifth again of his pace, which made the first hour of the game "
                + "the fastest he would ever move and every suit of iron afterwards feel like a "
                + "punishment. Nakedness is not a gift; it is merely the absence of a cost. "
                + "Evasion and a steady hand still reward light gear, because there the "
                + "advantage is real.");

            Dodging = config.Bind("Garb", "Dodging",
                "3.6=20,5.3=18,7.1=15,9.8=10,14.2=4,16=3,17.8=2,19.6=-7,23.1=-13,26.7=-20,40=-25",
                "The same, for evasion, in points. A point is worth four tenths of a percent "
                + "of the enemy's chance to land a blow, by «Aim.PerDodge», so twenty points "
                + "is eight of his hundred.");

            Aiming = config.Bind("Garb", "Aiming",
                "3.6=0,5.3=2,7.1=4,9.8=15,14.2=12,16=10,17.8=8,19.6=-6,23.1=-13,26.7=-20,40=-25",
                "The same, for the steadiness of one's own hand, in points. This is what "
                + "middling armour is for: iron light enough to swing in, heavy enough to lean "
                + "on. Counted by «Aim.PerArmour».");

            Swing = config.Bind("Garb", "Swing",
                "3.6=5,5.3=4,7.1=3,9.8=2,14.2=1,16=0,17.8=0,19.6=-6,23.1=-13,26.7=-20,40=-25",
                "The same, for the speed of one's own blows, as a percentage. Plate costs a "
                + "fifth of everything a man does in a minute, which is the whole of what it "
                + "asks in exchange for its wall.");

            Bands = config.Bind("Garb", "Bands", "Light=8,Medium=18",
                "Where the names part, by the weight of a full suit in kilograms: up to the "
                + "first it is light, up to the second middling, above it heavy. Names only — "
                + "everything above is counted from the weight itself, not from the band.");

            Relabel = config.Bind("Garb", "Relabel", true,
                "Write our own name into the item, so the card, the talents for light armour "
                + "and the mana a mage loses all follow the same reckoning. Turn this off to "
                + "leave the game's own word alone and keep the numbers.");

            Telling = config.Bind("Garb", "Telling", true,
                "Write out what was sorted and what changed name.");
        }

        // ---------------------------------------------------------------- лестницы

        private sealed class Rung
        {
            internal float kilos;
            internal float value;
        }

        private static readonly Dictionary<string, List<Rung>> ladders =
            new Dictionary<string, List<Rung>>();

        private static readonly Dictionary<string, string> readAs =
            new Dictionary<string, string>();

        /// <summary>Reads one of those ladders, and only when its writing has changed.</summary>
        private static List<Rung> Ladder(string name, ConfigEntry<string> from)
        {
            string written = from != null ? (from.Value ?? "") : "";
            string was;

            if (readAs.TryGetValue(name, out was) && was == written && ladders.ContainsKey(name))
            {
                return ladders[name];
            }

            List<Rung> steps = new List<Rung>();

            foreach (string piece in written.Split(','))
            {
                int split = piece.IndexOf('=');
                if (split <= 0) continue;

                float kilos, value;

                if (!float.TryParse(piece.Substring(0, split).Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out kilos)) continue;

                if (!float.TryParse(piece.Substring(split + 1).Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out value)) continue;

                steps.Add(new Rung { kilos = kilos, value = value });
            }

            steps.Sort(delegate (Rung a, Rung b) { return a.kilos.CompareTo(b.kilos); });

            ladders[name] = steps;
            readAs[name] = written;

            return steps;
        }

        /// <summary>What the ladder says at this weight: read between the written points.</summary>
        private static float At(List<Rung> steps, float kilos)
        {
            if (steps == null || steps.Count == 0) return 0f;

            if (kilos <= steps[0].kilos) return steps[0].value;
            if (kilos >= steps[steps.Count - 1].kilos) return steps[steps.Count - 1].value;

            for (int i = 1; i < steps.Count; i++)
            {
                if (kilos > steps[i].kilos) continue;

                Rung low = steps[i - 1];
                Rung high = steps[i];

                float span = high.kilos - low.kilos;
                if (span <= 0f) return high.value;

                float part = (kilos - low.kilos) / span;
                return low.value + (high.value - low.value) * part;
            }

            return steps[steps.Count - 1].value;
        }

        // ---------------------------------------------------------------- доли кусков

        private static readonly Dictionary<EquipSlotType, float> shares =
            new Dictionary<EquipSlotType, float>();
        private static string sharesRead;

        private static float Part(EquipSlotType slot)
        {
            string written = Share != null ? (Share.Value ?? "") : "";

            if (written != sharesRead)
            {
                sharesRead = written;
                shares.Clear();

                foreach (string piece in written.Split(','))
                {
                    int split = piece.IndexOf('=');
                    if (split <= 0) continue;

                    string name = piece.Substring(0, split).Trim();
                    float much;

                    if (!float.TryParse(piece.Substring(split + 1).Trim(),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out much)) continue;

                    try
                    {
                        shares[(EquipSlotType)Enum.Parse(typeof(EquipSlotType), name, true)] = much;
                    }
                    catch
                    {
                    }
                }
            }

            float got;
            return shares.TryGetValue(slot, out got) ? got : 0f;
        }

        // ---------------------------------------------------------------- счёт

        /// <summary>The weight a full suit of this piece's making would come to.</summary>
        private static float Suit(UIArmorInfo coat, out float part)
        {
            part = Part(coat.EquipType);
            return part > 0f ? coat.weight / part : 0f;
        }

        /// <summary>What this one piece is worth on the given ladder.</summary>
        private static float Worth(UIArmorInfo coat, ConfigEntry<string> from, string name)
        {
            float part;
            float suit = Suit(coat, out part);

            if (suit <= 0f) return 0f;

            return At(Ladder(name, from), suit) * part;
        }

        // Что весит полный набор каждого материала на нижнем тире. Читаем ту же таблицу,
        // по которой «Burden» раздаёт вес: имя должно стоять на материале, а не на вещи.
        private static readonly Dictionary<string, float> stuff = new Dictionary<string, float>();
        private static string stuffRead;

        private static float Plain(ArmourClass kind)
        {
            string written = Burden.Materials != null ? (Burden.Materials.Value ?? "") : "";

            if (written != stuffRead)
            {
                stuffRead = written;
                stuff.Clear();

                foreach (string entry in written.Split(','))
                {
                    int split = entry.IndexOf('=');
                    if (split <= 0) continue;

                    float much;
                    if (!float.TryParse(entry.Substring(split + 1).Trim(),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out much)) continue;

                    stuff[entry.Substring(0, split).Trim()] = much;
                }
            }

            float got;
            return stuff.TryGetValue(kind.ToString(), out got) ? got : 0f;
        }

        /// <summary>Which of the three names this weight answers to.</summary>
        private static ArmourType Named(float suit)
        {
            float light = 8f, middling = 18f;

            try
            {
                foreach (string piece in (Bands.Value ?? "").Split(','))
                {
                    int split = piece.IndexOf('=');
                    if (split <= 0) continue;

                    string name = piece.Substring(0, split).Trim();
                    float much;

                    if (!float.TryParse(piece.Substring(split + 1).Trim(),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out much)) continue;

                    if (name.Equals("Light", StringComparison.OrdinalIgnoreCase)) light = much;
                    else if (name.Equals("Medium", StringComparison.OrdinalIgnoreCase)) middling = much;
                }
            }
            catch
            {
            }

            if (suit <= light) return ArmourType.Light;
            return suit <= middling ? ArmourType.Medium : ArmourType.Heavy;
        }

        // ---------------------------------------------------------------- запись

        private static bool sorted;

        /// <summary>
        /// Writes the four numbers into every piece of armour, and its name with them.
        ///
        /// Пишем в саму вещь, а не поверх окна: игра сама применит прибавки к тому, кто её
        /// наденет, и сама покажет их в карточке — тем же путём, каким посох носит свою силу
        /// заклинаний. Наше дело сосчитать.
        /// </summary>
        internal static void Sort()
        {
            if (sorted || Enabled == null || !Enabled.Value) return;

            try
            {
                UIItemDatabase db;
                try { db = UIItemDatabase.Instance; }
                catch { return; }

                if (db == null || db.items == null) return;

                sorted = true;

                int dressed = 0, renamed = 0;

                foreach (UIItemInfo thing in db.items)
                {
                    UIArmorInfo coat = thing as UIArmorInfo;
                    if (coat == null || coat.weight <= 0f) continue;

                    float part;
                    float suit = Suit(coat, out part);

                    // Пояса, плащи и прочее в дележе набора не участвуют: их вес ни к чему
                    // не отнести, и трогать их мы не беремся.
                    if (suit <= 0f) continue;

                    float walk = Worth(coat, Speed, "speed");
                    float duck = Worth(coat, Dodging, "dodge");
                    float hand = Worth(coat, Aiming, "aim");
                    float swing = Worth(coat, Swing, "swing");

                    // Вещь, которую мы уже одевали, не трогаем второй раз: прибавки
                    // сложились бы, а чужие, писанные самой игрой, пришлось бы отличать от
                    // своих — отличить их нечем.
                    if (mine.Contains(thing.ID)) continue;

                    if (coat.addAttrs == null) coat.addAttrs = new List<AddonAttributes>();

                    if (Mathf.Abs(walk) >= 0.5f)
                    {
                        coat.addAttrs.Add(new AddonAttributes(AddonAttribute.MoveSpeed, walk / 100f));
                    }

                    if (Mathf.Abs(duck) >= 0.5f)
                    {
                        coat.addAttrs.Add(new AddonAttributes(AddonAttribute.Dodge, duck));
                    }

                    if (Mathf.Abs(swing) >= 0.5f)
                    {
                        coat.addAttrs.Add(new AddonAttributes(AddonAttribute.AttackSpeed, swing / 100f));
                    }

                    // Меткость пишем ради карточки и ради игрового поля; в нашем расчёте
                    // попадания она считается отдельно — «Aim» игровое поле не читает.
                    if (Mathf.Abs(hand) >= 0.5f)
                    {
                        coat.addAttrs.Add(new AddonAttributes(AddonAttribute.Attack, hand));
                    }

                    mine.Add(thing.ID);
                    dressed++;

                    if (Relabel.Value)
                    {
                        // Имя ставим по материалу, а не по весу этой вещи. Вес растёт с
                        // тиром в полтора раза, и по нему кольчуга третьей ступени звалась бы
                        // тяжёлой, а лёгкая кожа средней — вместе с талантом на лёгкий доспех,
                        // который снялся бы заодно с именем. Полоса у класса одна и та же на
                        // всех ступенях; числа при этом считаются от настоящего веса, так что
                        // латы Т5 всё равно тяжелее и наказывают сильнее лат Т2.
                        float plain = Plain(coat.armourClass);
                        ArmourType want = Named(plain > 0f ? plain : suit);

                        if (coat.armourType != want)
                        {
                            coat.armourType = want;
                            renamed++;
                        }
                    }
                }

                if (Telling.Value)
                {
                    ItemForgePlugin.Log.LogInfo($"Доспехи разобраны по весу: одето {dressed}, "
                        + $"переименовано {renamed}.");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог разобрать доспехи по весу: " + e);
            }
        }

        private static readonly HashSet<int> mine = new HashSet<int>();

        // Вещи, которым мы уже вписали своё. Считаем по самим предметам, а не по их образцу:
        // у каждого предмета список прибавок свой.
        private static readonly HashSet<Inventory> worn = new HashSet<Inventory>();

        /// <summary>
        /// Writes our four numbers into the thing itself, not only into its pattern.
        ///
        /// Образец и вещь — разное. Игра списывает прибавки с образца в предмет один раз, в
        /// час его рождения («addAttrs = new List(uIEquipmentInfo.addAttrs)»), и дальше живёт
        /// со списком предмета. Всё, что лежало в мире до нашего разбора, нашего не получило
        /// вовсе: ни в карточке, ни в бою.
        ///
        /// Потому дописываем и в предмет. Помечать нам нечем, и чтобы не удвоить при втором
        /// заходе, каждый предмет запоминается.
        /// </summary>
        internal static void Dress(Inventory bit)
        {
            if (Enabled == null || !Enabled.Value || bit == null) return;
            if (worn.Contains(bit)) return;

            try
            {
                worn.Add(bit);

                UIArmorInfo coat = bit.itemInfo as UIArmorInfo;
                if (coat == null || coat.weight <= 0f) return;

                float part;
                float suit = Suit(coat, out part);
                if (suit <= 0f) return;

                if (bit.addAttrs == null) bit.addAttrs = new List<AddonAttributes>();

                float walk = Worth(coat, Speed, "speed");
                float duck = Worth(coat, Dodging, "dodge");
                float hand = Worth(coat, Aiming, "aim");
                float swing = Worth(coat, Swing, "swing");

                if (Mathf.Abs(walk) >= 0.5f)
                {
                    bit.addAttrs.Add(new AddonAttributes(AddonAttribute.MoveSpeed, walk / 100f));
                }

                if (Mathf.Abs(duck) >= 0.5f)
                {
                    bit.addAttrs.Add(new AddonAttributes(AddonAttribute.Dodge, duck));
                }

                if (Mathf.Abs(swing) >= 0.5f)
                {
                    bit.addAttrs.Add(new AddonAttributes(AddonAttribute.AttackSpeed, swing / 100f));
                }

                if (Mathf.Abs(hand) >= 0.5f)
                {
                    bit.addAttrs.Add(new AddonAttributes(AddonAttribute.Attack, hand));
                }
            }
            catch
            {
            }
        }

        /// <summary>Goes over what the party wears and dresses what has not been dressed.</summary>
        internal static void Round()
        {
            if (Enabled == null || !Enabled.Value) return;

            try
            {
                PartyManager party = PartyManager.instance;
                if (party == null || party.partyMembers == null) return;

                foreach (HumaniodUnit man in party.partyMembers)
                {
                    if (man == null || man.equipmentmanger == null) continue;

                    EquipInfo[] kit = man.equipmentmanger.equipInfos;
                    if (kit == null) continue;

                    foreach (EquipInfo one in kit)
                    {
                        if (one == null || one.inventory == null) continue;
                        if (!one.IsEquiped()) continue;

                        Dress(one.inventory);
                    }
                }
            }
            catch
            {
            }
        }

        // ---------------------------------------------------------------- рука

        /// <summary>
        /// How much steadier this man's hand is for what he wears.
        ///
        /// Считается заново по надетому, а не читается из игрового поля меткости: наш расчёт
        /// попадания того поля не касается вовсе, а в поле, кроме доспеха, входит ещё многое.
        /// Здесь нужно ровно то, что дал доспех, и ничего сверх.
        /// </summary>
        internal static float Hand(HumaniodUnit who)
        {
            if (Enabled == null || !Enabled.Value || who == null) return 0f;

            try
            {
                HashSet<UIEquipmentInfo> worn = Forge.Gather(who);
                if (worn == null) return 0f;

                float sum = 0f;

                foreach (UIEquipmentInfo piece in worn)
                {
                    UIArmorInfo coat = piece as UIArmorInfo;
                    if (coat == null || coat.weight <= 0f) continue;

                    sum += Worth(coat, Aiming, "aim");
                }

                return sum;
            }
            catch
            {
                return 0f;
            }
        }
    }
}
