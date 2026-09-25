using System;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using spell;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Makes what a man carries decide what he can do, and makes strength the thing that
    /// decides how much he may carry.
    ///
    /// В игре две грузоподъёмности, и они живут порознь. Надетое считается против «35 + Сила»
    /// и выше 65 % даёт умеренный штраф. Сумка считается против «50 + Сила × 2», и её перегруз
    /// не стоит ничего, кроме скорости переходов по карте мира: можно унести с поля боя сто
    /// шестьдесят единиц добычи и драться так же бодро. Сила при этом прибавляет к пределу
    /// единицу за очко — то есть не решает ничего.
    ///
    /// Здесь всё сведено в одно число. Груз — это надетое плюс сумка, предел — «20 + Сила × 3»,
    /// и каждый процент груза сверх половины предела стоит двух процентов боеспособности:
    /// медленнее бьёшь, хуже блокируешь, парируешь и уворачиваешься, быстрее выдыхаешься. На
    /// самом пределе не можешь ничего, за пределом не идёшь вовсе.
    ///
    /// Отсюда и смысл для тяжёлого бойца: латы весят столько, что без силы в них нельзя жить.
    /// Сила перестаёт быть характеристикой для галочки и становится тем, чем латник платит за
    /// право носить латы.
    /// </summary>
    internal static class Burden
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Base;
        internal static ConfigEntry<float> PerMight;
        internal static ConfigEntry<float> Free;
        internal static ConfigEntry<float> Gift;
        internal static ConfigEntry<float> Bite;
        internal static ConfigEntry<float> Most;
        internal static ConfigEntry<bool> Rooted;
        internal static ConfigEntry<float> Crawl;
        internal static ConfigEntry<bool> Refuse;
        internal static ConfigEntry<bool> Breakdown;
        internal static ConfigEntry<KeyCode> BreakdownKey;
        internal static ConfigEntry<float> BreakdownRange;

        internal static ConfigEntry<bool> FitNPC;
        internal static ConfigEntry<float> GreenFill;
        internal static ConfigEntry<float> VeteranFill;
        internal static ConfigEntry<int> Veteran;
        internal static ConfigEntry<float> MightBase;
        internal static ConfigEntry<float> MightPerLevel;

        internal static ConfigEntry<string> Materials;
        internal static ConfigEntry<string> TierLadder;
        internal static ConfigEntry<string> SetShare;
        internal static ConfigEntry<float> Spread;
        internal static ConfigEntry<bool> GrowMages;
        internal static ConfigEntry<bool> TrinketsFree;
        internal static ConfigEntry<bool> Ledger;

        private static bool weighed;
        private static bool listed;

        // Пока это правда, мы уже внутри расчёта груза. Без такой отметки выходит петля:
        // пересчёт веса зовёт нас, мы дорастим силу, рост зовёт пересчёт характеристик, тот
        // снова считает вес — и так до переполнения стека или до одного кадра в секунду.
        private static bool inside;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Burden", "Enabled", true,
                "Count everything a man carries against one capacity, and let the weight decide "
                + "what he can still do. Off, and the game keeps its two separate numbers, of "
                + "which the larger one costs nothing outside the world map.");

            Base = config.Bind("Burden", "Base", 0f,
                new ConfigDescription(
                    "What anybody can carry before strength is counted at all. Kept at "
                    + "nothing: the whole capacity belongs to strength, so a weakling carries "
                    + "nothing and there is no free allowance to hide behind. Nothing here is "
                    + "safe against a creature whose attributes have not been rolled yet — "
                    + "that is what the readiness check is for, and without it everyone in the "
                    + "cave once froze where they stood.",
                    new AcceptableValueRange<float>(0f, 200f)));

            PerMight = config.Bind("Burden", "PerMight", 1f,
                new ConfigDescription(
                    "How many kilos a point of strength is worth. Three: the dragon suit at "
                    + "forty then asks twenty-four of its wearer, and thirty once he has a "
                    + "sword, a shield and a belt on him as well. The game gives one kilo per "
                    + "point, which is why nobody ever took strength for it.",
                    new AcceptableValueRange<float>(0.1f, 20f)));

            Free = config.Bind("Burden", "Free", 0.5f,
                new ConfigDescription(
                    "The share of capacity a man carries for nothing. Below it he moves as if "
                    + "empty-handed.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Gift = config.Bind("Burden", "Gift", 0.3f,
                new ConfigDescription(
                    "The most that going light is ever worth, as a share.\n\n"
                    + "Пустые руки прежде стоили вдвое больше полных: правило работало в обе "
                    + "стороны по одной цене и упиралось только в единицу, так что налегке "
                    + "человек получал до целой сотни процентов ко всему разом — к шагу, к "
                    + "замаху, к увороту, к дыханию. Это не преимущество лёгкого бойца, это "
                    + "другой человек.\n\n"
                    + "Помеха от перегруза своего потолка не теряет: она считается отдельно, "
                    + "по «Most».",
                    new AcceptableValueRange<float>(0f, 1f)));

            Bite = config.Bind("Burden", "Bite", 2f,
                new ConfigDescription(
                    "How many points of penalty each point of load above the free share costs. "
                    + "At two, a man at three quarters of his capacity has lost half of "
                    + "everything, and at full capacity he has lost all of it.",
                    new AcceptableValueRange<float>(0f, 10f)));

            Most = config.Bind("Burden", "Most", 0.6f,
                new ConfigDescription(
                    "The heaviest the load is ever allowed to weigh on a man, as a share. Six "
                    + "tenths, and not because the arithmetic asks for it: the game works out a "
                    + "man's rate of striking as one plus this penalty, so at a full one the rate "
                    + "becomes nought, the readiness of his weapon never counts down, and he "
                    + "stands there swinging at nothing for the rest of his life. Spells still "
                    + "work, which is exactly how it looked — a troll casting and never once "
                    + "raising its club. A load must slow a man, never stop him.",
                    new AcceptableValueRange<float>(0.05f, 0.95f)));

            Rooted = config.Bind("Burden", "Rooted", true,
                "Take movement away entirely past the capacity. Not slowed — stopped. He can "
                + "still swing and block where he stands, but he goes nowhere until something "
                + "comes off his back.");

            Crawl = config.Bind("Burden", "Crawl", 0.99f,
                new ConfigDescription(
                    "What the load takes of a man's speed once he is past his limit, as a "
                    + "share. Taking movement away outright works on everyone the game moves "
                    + "itself, and on nobody else: the player's own group has its permission "
                    + "written back to true by the travel manager, and the two rules spent six "
                    + "hundred turns handing the flag to each other while the man walked on "
                    + "with three hundred kilos. Speed nobody writes back. At ninety-nine "
                    + "hundredths he barely stirs, and it goes to his arms as well as his "
                    + "legs: a man under a load he cannot lift neither walks nor swings. "
                    + "It reads as «too heavy» without a word of explanation and cannot "
                    + "be argued with.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Refuse = config.Bind("Burden", "Refuse", true,
                "Refuse to stow what the party cannot carry. The thing is not lost and not "
                + "quietly swallowed: it goes on the ground at his feet, the same way anything "
                + "dropped does, and a line says why. Quest items and writings pass regardless "
                + "— a story must never be locked behind a full bag. Taking something off "
                + "yourself passes too: that is moving weight from one shoulder to the other, "
                + "not picking it up.");

            Breakdown = config.Bind("Burden", "Breakdown", true,
                "Write out what a man's load is actually made of the first time the weight "
                + "stops him: every worn piece with its weight, everything in his bag with "
                + "its weight, and the line that settles the argument - anything counted in "
                + "both places at once.");

            BreakdownKey = config.Bind("Burden", "BreakdownKey", KeyCode.F7,
                "Press this in game to write the same breakdown for everybody standing near "
                + "you, whether the weight has stopped them or not.");

            BreakdownRange = config.Bind("Burden", "BreakdownRange", 40f,
                new ConfigDescription(
                    "How far around you the key reaches, in metres.",
                    new AcceptableValueRange<float>(5f, 300f)));

            FitNPC = config.Bind("Burden", "FitNPC", true,
                "Give everyone else strength enough for what they were dressed in. Without "
                + "this the rule would fall hardest on the people it was not aimed at: every "
                + "guardsman in plate would become a statue, because the game never asked "
                + "whether he could carry it.");

            GreenFill = config.Bind("Burden", "GreenFill", 0.65f,
                new ConfigDescription(
                    "How loaded a raw recruit is left: enough strength to bear his kit at this "
                    + "share of his capacity, and no more. Above the free share, so he is "
                    + "always a little encumbered.",
                    new AcceptableValueRange<float>(0.1f, 1f)));

            VeteranFill = config.Bind("Burden", "VeteranFill", 0.5f,
                new ConfigDescription(
                    "How loaded a veteran is left. At the free share exactly: he carries his "
                    + "own armour as though it were nothing, which is what the years bought him.",
                    new AcceptableValueRange<float>(0.1f, 1f)));

            Materials = config.Bind("Burden", "Materials",
                "Cloth=3.6,PaddingArmor=5.3,LightLeatherArmor=7.1,HardLeaterArmor=9.8,"
                + "ScaleMail=14.2,ChainArmor=16,SplintArmor=17.8,LamellarArmor=19.6,"
                + "HalfPlate=23.1,PlateArmor=26.7",
                "What a full suit of each material weighs at the lowest tier — helmet, chest "
                + "and legs together. What a thing is made of is what decides its weight, and "
                + "the game does not think so: it sorts armour into light, medium and heavy and "
                + "then puts soft leather in the heavy pile and half-plate in the light one. "
                + "Mail is lighter than plate whatever anybody calls it.");

            TierLadder = config.Bind("Burden", "TierLadder", "1,1.042,1.083,1.167,1.292,1.5",
                "How much heavier each tier of the same material is, from T0 to T5. Half again "
                + "from end to end, laid out by Fibonacci, so the early steps are barely felt "
                + "and the last one is a third of the whole climb — the move to legendary is "
                + "meant to be an event.");

            Spread = config.Bind("Burden", "Spread", 0.1f,
                new ConfigDescription(
                    "How far a single thing may sit from what its material and tier call for. "
                    + "A tenth: one breastplate is a little heavier than another of the same "
                    + "steel and the same age, and that is all the difference there should be. "
                    + "Without a limit the authors’ own odd numbers come through "
                    + "multiplied — a piece that was six times its neighbours stays six times "
                    + "theirs, only now at a heavier scale.",
                    new AcceptableValueRange<float>(0f, 1f)));

            SetShare = config.Bind("Burden", "SetShare", "0.2,0.5,0.3",
                "How the suit’s weight is divided between helmet, chest and legs.");

            MightBase = config.Bind("Burden", "MightBase", 4f,
                new ConfigDescription(
                    "The strength a creature of the first level is born with.",
                    new AcceptableValueRange<float>(0f, 50f)));

            MightPerLevel = config.Bind("Burden", "MightPerLevel", 0.75f,
                new ConfigDescription(
                    "How much strength each level of experience is worth. Three quarters of a "
                    + "point: a town guard of the sixth level comes to nine and carries mail, a "
                    + "knight of the twenty-fourth comes to twenty-two and carries plate, and "
                    + "nobody carries plate before they have earned it. The game itself has a "
                    + "«power» rating for every creature, but it ranks a twenty-seventh-level "
                    + "swordsman below a seventh-level ranger, so it is no use for this.",
                    new AcceptableValueRange<float>(0f, 5f)));

            GrowMages = config.Bind("Burden", "GrowMages", false,
                "Let a magician's years go into his arms as well. Strength grows with level "
                + "here, and level is worked out from the six attributes, so the two feed each "
                + "other: a sorcerer of the hundred and thirtieth came out with a hundred and "
                + "fifty of strength — more than the knight who had done nothing else with his "
                + "life. Off, and a caster keeps the body he was written with; he can still "
                + "carry his robes, because what he carries is settled by weight rather than "
                + "by years.");

            TrinketsFree = config.Bind("Burden", "TrinketsFree", true,
                "Let rings and amulets weigh nothing. They are the one thing a man wears that "
                + "is not a burden — and counting them is how a game ends up telling somebody "
                + "he cannot walk because of jewellery.");

            Ledger = config.Bind("Burden", "Ledger", true,
                "Write every wearable thing in the game out to a file next to this one, with "
                + "its kind, class, tier, weight before and after. Not for the game to read — "
                + "for us: numbers are easier to argue about when they are all on one page.");

            Veteran = config.Bind("Burden", "Veteran", 30,
                new ConfigDescription(
                    "The level at which somebody counts as a veteran for the above.",
                    new AcceptableValueRange<int>(1, 100)));
        }

        /// <summary>Moves armour to the weight its kind and tier call for, keeping its own.</summary>
        internal static void Weigh()
        {
            if (weighed || !Enabled.Value) return;

            try
            {
                UIItemDatabase db;
                try { db = UIItemDatabase.Instance; }
                catch { return; }

                if (db == null || db.items == null) return;

                weighed = true;

                Write(db, "до");

                if (TrinketsFree.Value)
                {
                    int free = 0;

                    foreach (UIItemInfo thing in db.items)
                    {
                        UIEquipmentInfo worn = thing as UIEquipmentInfo;
                        if (worn == null) continue;
                        if (worn.EquipType != EquipSlotType.neck
                            && worn.EquipType != EquipSlotType.finger) continue;

                        if (thing.weight <= 0f) continue;

                        thing.weight = 0f;
                        free++;
                    }

                    if (free > 0)
                    {
                        ItemForgePlugin.Log.LogInfo($"Кольца и амулеты обезвешены: {free}.");
                    }
                }

                System.Collections.Generic.Dictionary<string, float> stuff = Stuff();
                float[] steps = Ladder(TierLadder.Value);
                float[] share = Ladder(SetShare.Value);

                // Сперва смотрим, что в игре уже есть: средний вес по каждой тройке «класс,
                // ярус, часть». Свои веса у брони есть, и стирать их нельзя — вещь, которая
                // была тяжелее своих сверстниц, должна остаться тяжелее. Двигаем не каждую
                // по отдельности, а всю группу разом, одним множителем к плановому числу.
                System.Collections.Generic.Dictionary<int, float> sum =
                    new System.Collections.Generic.Dictionary<int, float>();
                System.Collections.Generic.Dictionary<int, int> count =
                    new System.Collections.Generic.Dictionary<int, int>();

                foreach (UIItemInfo thing in db.items)
                {
                    int key;
                    if (!Kind(thing, out key)) continue;
                    if (thing.weight <= 0f) continue;

                    if (!sum.ContainsKey(key)) { sum[key] = 0f; count[key] = 0; }

                    sum[key] += thing.weight;
                    count[key]++;
                }

                int changed = 0;

                foreach (UIItemInfo thing in db.items)
                {
                    int key;
                    if (!Kind(thing, out key)) continue;
                    if (thing.weight <= 0f) continue;

                    UIArmorInfo made = thing as UIArmorInfo;
                    int tier = (int)thing.tier;
                    int slot = (key % 10);

                    float heavy;
                    if (!stuff.TryGetValue(made.armourClass.ToString(), out heavy)) continue;
                    if (tier < 0 || tier >= steps.Length) continue;

                    float want = heavy * steps[tier] * Share(share, slot);
                    float have = (count[key] > 0) ? sum[key] / count[key] : 0f;
                    if (have <= 0f || want <= 0f) continue;

                    thing.weight *= want / have;

                    // Дальше десятой доли от своего места вещь не уходит. Разброс внутри
                    // материала нужен — одна кираса чуть тяжелее другой, — но он должен
                    // оставаться разбросом, а не наследоваться от авторских странностей.
                    if (Spread.Value > 0f)
                    {
                        thing.weight = Mathf.Clamp(thing.weight,
                            want * (1f - Spread.Value), want * (1f + Spread.Value));
                    }

                    changed++;
                }

                ItemForgePlugin.Log.LogInfo($"Вес брони пересчитан по классу и ярусу: "
                    + $"{changed} вещей, {sum.Count} групп.");

                Write(db, "после");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог пересчитать вес брони: " + e);
            }
        }

        /// <summary>Writes every creature template out with every field it has.</summary>
        internal static void Roster()
        {
            // Однократно. Без этого заслона опись писалась при каждой попытке сборки — за один
            // заход восемьсот шестьдесят пять раз, по проходу через тысячу шаблонов и запись
            // файла на четверть мегабайта каждый. Игра шла на семи кадрах, и виноват был не
            // расчёт груза, а вот эта строка, которой не было.
            if (listed || !Ledger.Value) return;
            listed = true;

            try
            {
                UIUnitDatabase db;
                try { db = UIUnitDatabase.Instance; }
                catch { return; }

                if (db == null || db.indexes == null) return;

                // Те же правила, что и для вещей: столбцы не выбираем, берём все поля. В
                // шаблоне существа нет ни характеристик, ни снаряжения — они на префабе, —
                // но есть уровень, раса, базовое здоровье, сопротивления и трофеи, и этого
                // хватит, чтобы разложить очки по классам.
                System.Collections.Generic.List<string> columns =
                    new System.Collections.Generic.List<string>();
                System.Collections.Generic.HashSet<string> seen =
                    new System.Collections.Generic.HashSet<string>();

                foreach (UnitInfo who in db.indexes)
                {
                    if (who == null) continue;

                    foreach (System.Reflection.FieldInfo field in who.GetType().GetFields(
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    {
                        if (seen.Add(field.Name)) columns.Add(field.Name);
                    }
                }

                System.Collections.Generic.List<string> rows =
                    new System.Collections.Generic.List<string>();

                rows.Add("имя	" + string.Join("	", columns.ToArray()));

                foreach (UnitInfo who in db.indexes)
                {
                    if (who == null) continue;

                    string[] cells = new string[columns.Count + 1];
                    cells[0] = who.name;

                    for (int i = 0; i < columns.Count; i++)
                    {
                        System.Reflection.FieldInfo field = who.GetType().GetField(columns[i],
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                        cells[i + 1] = (field == null) ? "" : Show(field.GetValue(who));
                    }

                    rows.Add(string.Join("	", cells));
                }

                string file = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "aor.units.tsv");
                System.IO.File.WriteAllLines(file, rows.ToArray());

                ItemForgePlugin.Log.LogInfo($"Опись существ: {rows.Count - 1} шаблонов, "
                    + $"{columns.Count} столбцов, «{file}».");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог выписать опись существ: " + e);
            }
        }

        /// <summary>Writes every item in the game out with every field it has.</summary>
        private static void Write(UIItemDatabase db, string when)
        {
            if (!Ledger.Value) return;

            try
            {
                // Столбцы не выбираем руками — берём все поля, какие у вещей есть. Что бы
                // авторы туда ни положили, оно окажется в таблице: иначе разговор о числах
                // всегда упирается в то, чего я не догадался выписать.
                System.Collections.Generic.List<string> columns =
                    new System.Collections.Generic.List<string>();
                System.Collections.Generic.HashSet<string> seen =
                    new System.Collections.Generic.HashSet<string>();
                System.Collections.Generic.List<Type> kinds =
                    new System.Collections.Generic.List<Type>();

                foreach (UIItemInfo thing in db.items)
                {
                    if (thing == null || !kinds.Contains(thing.GetType())) continue;
                }

                foreach (UIItemInfo thing in db.items)
                {
                    if (thing == null) continue;

                    Type sort = thing.GetType();
                    if (kinds.Contains(sort)) continue;
                    kinds.Add(sort);

                    foreach (System.Reflection.FieldInfo field in sort.GetFields(
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    {
                        if (seen.Add(field.Name)) columns.Add(field.Name);
                    }
                }

                System.Collections.Generic.List<string> rows =
                    new System.Collections.Generic.List<string>();

                rows.Add("тип	" + string.Join("	", columns.ToArray()));

                foreach (UIItemInfo thing in db.items)
                {
                    if (thing == null) continue;

                    Type sort = thing.GetType();
                    string[] cells = new string[columns.Count + 1];
                    cells[0] = sort.Name;

                    for (int i = 0; i < columns.Count; i++)
                    {
                        System.Reflection.FieldInfo field = sort.GetField(columns[i],
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                        cells[i + 1] = (field == null) ? "" : Show(field.GetValue(thing));
                    }

                    rows.Add(string.Join("	", cells));
                }

                string file = System.IO.Path.Combine(BepInEx.Paths.ConfigPath,
                    "aor.items." + when + ".tsv");

                System.IO.File.WriteAllLines(file, rows.ToArray());

                ItemForgePlugin.Log.LogInfo($"Опись снаряжения «{when}»: {rows.Count - 1} вещей, "
                    + $"{columns.Count} столбцов, «{file}».");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог выписать опись снаряжения: " + e);
            }
        }

        /// <summary>Puts a field of any kind into one cell of a table.</summary>
        private static string Show(object what)
        {
            if (what == null) return "";

            // Числа пишем без запятых: таблица разбирается разделителем, и запятая в числе
            // ломает её молча.
            if (what is float) return ((float)what).ToString("0.###",
                System.Globalization.CultureInfo.InvariantCulture);

            if (what is double) return ((double)what).ToString("0.###",
                System.Globalization.CultureInfo.InvariantCulture);

            if (what is UnityEngine.Object)
            {
                UnityEngine.Object made = (UnityEngine.Object)what;
                return (made == null) ? "" : made.name;
            }

            if (what is float[])
            {
                float[] row = (float[])what;
                string[] parts = new string[row.Length];
                for (int i = 0; i < row.Length; i++)
                {
                    parts[i] = row[i].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                }
                return string.Join("|", parts);
            }

            if (what is System.Collections.IEnumerable && !(what is string))
            {
                System.Collections.Generic.List<string> parts =
                    new System.Collections.Generic.List<string>();

                foreach (object one in (System.Collections.IEnumerable)what)
                {
                    if (parts.Count >= 12) { parts.Add("…"); break; }
                    parts.Add(Show(one));
                }

                return string.Join("|", parts.ToArray());
            }

            return what.ToString().Replace((char)9, (char)32).Replace((char)10, (char)32);
        }

        /// <summary>Which class, tier and part of a suit this is, if it is one at all.</summary>
        private static bool Kind(UIItemInfo thing, out int key)
        {
            key = 0;

            UIArmorInfo armour = thing as UIArmorInfo;
            if (armour == null) return false;

            int slot;
            if (armour.EquipType == EquipSlotType.head) slot = 0;
            else if (armour.EquipType == EquipSlotType.chest) slot = 1;
            else if (armour.EquipType == EquipSlotType.pants) slot = 2;
            else return false;

            // Ключ по материалу, а не по игровому классу: игра записала мягкую кожу в
            // тяжёлые, а полулаты в лёгкие, и по её классу кожа получила бы вес лат.
            int kind = (int)armour.armourClass;
            if (kind < 0) return false;

            int tier = (int)thing.tier;
            if (tier < 0 || tier > 5) return false;

            key = kind * 100 + tier * 10 + slot;
            return true;
        }

        private static float Share(float[] parts, int which)
        {
            return (parts != null && which >= 0 && which < parts.Length) ? parts[which] : 0.33f;
        }

        /// <summary>Reads the table of materials and what a suit of each weighs.</summary>
        private static System.Collections.Generic.Dictionary<string, float> Stuff()
        {
            System.Collections.Generic.Dictionary<string, float> table =
                new System.Collections.Generic.Dictionary<string, float>();

            foreach (string entry in (Materials.Value ?? "").Split(','))
            {
                int split = entry.IndexOf('=');
                if (split <= 0) continue;

                string name = entry.Substring(0, split).Trim();

                float much;
                if (!float.TryParse(entry.Substring(split + 1).Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out much)) continue;

                table[name] = Mathf.Max(0.01f, much);
            }

            return table;
        }

        /// <summary>Reads a row of numbers written with commas.</summary>
        private static float[] Ladder(string written)
        {
            System.Collections.Generic.List<float> got = new System.Collections.Generic.List<float>();

            foreach (string one in (written ?? "").Split(','))
            {
                float much;
                if (float.TryParse(one.Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out much))
                {
                    got.Add(Mathf.Max(0f, much));
                }
            }

            // Пусто — значит этот класс не трогаем вовсе: у авторов там уже сложилась своя
            // лестница, и переписывать её незачем.
            if (got.Count == 0) return new float[0];

            while (got.Count < 6) got.Add(got[got.Count - 1]);

            return got.ToArray();
        }

        /// <summary>What this man can carry before it costs him anything he cannot pay.</summary>
        internal static float Capacity(HumaniodUnit who)
        {
            if (who == null) return 1f;

            return Mathf.Max(1f, Base.Value + who.Strength * PerMight.Value);
        }

        /// <summary>Everything he is carrying: worn and stowed alike.</summary>
        internal static float Load(HumaniodUnit who)
        {
            if (who == null) return 0f;

            return Mathf.Max(0f, who.currentWeight) + Mathf.Max(0f, who.inventoryWeight);
        }

        /// <summary>How much of him the load has taken, from none to all of it.</summary>
        /// <summary>True when this creature has been given its attributes yet.</summary>
        internal static bool Ready(HumaniodUnit who)
        {
            // Игра расставляет характеристики позже, чем впервые считает вес. До этого сила
            // читается нулём, предел выходит нулевым, и любой груз оказывается за ним — а
            // значит всем подряд запрещается двигаться. Ровно это и случилось: и демон, и
            // весь круг встали намертво ещё до первой реплики. Пока силы нет, груза нет.
            return who != null && who.Data != null && who.Strength > 0;
        }

        internal static float Toll(HumaniodUnit who)
        {
            if (!Enabled.Value || who == null || !Ready(who)) return 0f;

            // Кому мы сами выставили силу — тем груз не считается ни в одну сторону. Тролль с
            // одной дубиной иначе оказался бы «налегке» и получил премию к скорости, а он не
            // налегке: он просто зверь, и его ноша — это он сам.
            if (Bestiary.Unburdened((UnitAttribute)(object)who)) return 0f;

            float fill = Load(who) / Capacity(who);

            // Правило работает в обе стороны и по одной цене. Выше половины каждый процент
            // груза стоит двух процентов боеспособности; ниже — столько же возвращает. Идти
            // налегке становится не отсутствием беды, а преимуществом, и лёгкий боец получает
            // ту роль, которой у него не было: он быстрее не по характеристике, а потому что
            // на нём меньше железа.
            // Премия за лёгкость своего потолка не перерастает: идти пустым — преимущество,
            // а не превращение в другое существо.
            float gift = Gift != null ? Gift.Value : 0.3f;

            return Mathf.Clamp((fill - Free.Value) * Bite.Value, -gift, 1f);
        }

        /// <summary>The game's own card we lay the weight on, and take it off by.</summary>
        private const string Mark = "WeightDebuff";

        /// <summary>Lays the weight on a man, or takes it off him.</summary>
        internal static void Settle(HumaniodUnit who)
        {
            if (!Enabled.Value || who == null || who.buffmanger == null) return;

            if (inside) return;

            // Мёртвого груз не касается. Игра при смерти снимает с человека все эффекты, а
            // этот пересчёт возвращал на труп штраф за груз, растил ему силу и запрещал ходить.
            if (who.Data == null || who.Data.isdead) return;

            try
            {
                if (!Ready(who)) return;

                inside = true;

                // Сперва дорастим, если игра одела существо не по силам: здесь вес уже
                // посчитан, а на самом надевании он ещё нулевой, и подгонка уходила ни с чем.
                Grow((UnitAttribute)(object)who);
                Fit((UnitAttribute)(object)who);

                // И под то, что просят сами вещи: оружие, щит, доспех.
                Meet.Fit((UnitAttribute)(object)who);

                float toll = Mathf.Min(Toll(who), Most.Value);

                // Снимаем по тому имени, под которым игра его и держит. Прежде здесь стояло
                // «ItemForgeBurden» — имя, которого у карточки нет: карточка берётся готовая,
                // игровая, и зовётся «WeightDebuff». Оттого снять своё же наложение не
                // удавалось никогда: первое село намертво, а все последующие пересчёты уходили
                // в пустоту. Человек шёл с тройным перегрузом и штрафом в семь процентов,
                // выписанным когда-то в начале пути.
                if (Mathf.Abs(toll) < 0.005f)
                {
                    who.buffmanger.RemoveBuff(Mark);
                    Walk(who, true);
                    return;
                }

                UIBuffInfo shape = UIBuffDatabase.Instance != null
                    ? UIBuffDatabase.Instance.GetByID(Mark) : null;

                if (shape != null)
                {
                    who.buffmanger.RemoveBuff(Mark);

                    BuffBase weight = new BuffBase(shape, (UnitAttribute)(object)who);
                    weight.addAttrs.Clear();

                    // За пределом отнимается не постепенно, а начисто: и ход, и удар. Пока
                    // человек под неподъёмным, он не идёт и не машет — в этом весь смысл
                    // предела, иначе его носят и не замечают.
                    float slow = (Load(who) > Capacity(who))
                        ? Mathf.Max(toll, Crawl.Value)
                        : toll;

                    // Школа «Защитник» учит ходить и бить в латах: со штрафа к скорости она снимает
                    // свою долю — в той мере, в какой груз состоит из тяжёлой брони.
                    float pace = slow > 0f ? slow * Bulwark.Ease(who) : slow;

                    // Всё, чем человек делает своё дело в бою, и всё, чем он за это платит.
                    weight.addAttrs.Add(new AddonAttributes(AddonAttribute.AttackSpeed, -pace));
                    weight.addAttrs.Add(new AddonAttributes(AddonAttribute.MoveSpeed, -pace));
                    weight.addAttrs.Add(new AddonAttributes(AddonAttribute.Dodge, -100f * toll));

                    // «Defence» у этой игры — не доспех, а умение принять удар на щит:
                    // «block = BSblock + DefMD». Гружёный принимает хуже, и это честно.
                    weight.addAttrs.Add(new AddonAttributes(AddonAttribute.Defence, -100f * toll));

                    // А вот угол блока груз не трогает: он у щита свой, от доски и руки, и от
                    // мешка за спиной не сужается. Прежде срезался и он — за компанию.

                    // Отрицательная экономия — это и есть возросший расход: каждый удар, рывок
                    // и блок стоят дороже ровно на ту же долю.
                    weight.addAttrs.Add(new AddonAttributes(AddonAttribute.EPsave, -toll));
                    weight.addAttrs.Add(new AddonAttributes(AddonAttribute.BlockEPsave, -toll));

                    who.buffmanger.AddBuff(weight);
                }

                // За пределом — не медленно, а никак.
                Walk(who, !(Rooted.Value && Load(who) > Capacity(who)));
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог разложить груз: " + e);
            }
            finally
            {
                inside = false;
            }
        }

        /// <summary>Lets a man walk, or does not.</summary>
        private static void Walk(HumaniodUnit who, bool may)
        {
            if (who == null || who.Data == null) return;

            CharacterSaveData data = (CharacterSaveData)(object)who.Data;

            // Груз — не единственное, что может человека остановить. Оружие не по силам
            // держит его так же, и снимать этот запрет грузу не положено: иначе два правила
            // переписывали бы друг друга по очереди, и кто победил, зависело бы от того, что
            // пересчиталось последним.
            if (may && Strain.Enabled != null && Strain.Enabled.Value && Strain.Rooted.Value
                && Strain.Lacking(who).strength)
            {
                return;
            }

            if (data.allowmove == may) return;

            data.allowmove = may;

            // Об одном и том же человеке — один раз. Прежде запись выходила при каждой
            // перемене флага, а флаг у отряда игрока перекидывается туда-обратно чужой рукой:
            // в одном бою набегало шестьсот строк об одном и том же.
            if (!may && stopped.Add(who.GetInstanceID()))
            {
                ItemForgePlugin.Log.LogInfo($"«{who.Data.unitname}» встал под грузом: "
                    + $"{Load(who):0.#} из {Capacity(who):0.#}.");

                Explain(who, again: false);
            }
        }

        private static readonly System.Collections.Generic.HashSet<int> stopped =
            new System.Collections.Generic.HashSet<int>();

        private static readonly System.Collections.Generic.HashSet<int> explained =
            new System.Collections.Generic.HashSet<int>();

        /// <summary>
        /// Writes out what a man's load is made of, piece by piece.
        ///
        /// Спор о том, откуда у мага в робе двадцать четыре килограмма, нельзя выиграть
        /// чтением кода: я уже однажды его прочёл и сказал, что удвоения нет, а оно было.
        /// Поэтому здесь ничего не рассуждается, а печатается — надетое, сумка и то, что
        /// сосчиталось в обоих местах сразу. Последняя строка либо пуста, либо называет
        /// виноватых поимённо.
        /// </summary>
        internal static void Nearby()
        {
            try
            {
                HumaniodUnit near = (PartyManager.instance != null)
                    ? PartyManager.instance.leader : null;

                if (near == null)
                {
                    ItemForgePlugin.Log.LogWarning("Некого спросить о грузе: отряда ещё нет.");
                    return;
                }

                Vector3 here = near.transform.position;
                float reach = BreakdownRange.Value;
                int shown = 0;

                ItemForgePlugin.Log.LogInfo($"--- разбор груза, вокруг на {reach:0} м ---");

                foreach (HumaniodUnit who in
                    UnityEngine.Object.FindObjectsOfType<HumaniodUnit>())
                {
                    if (who == null || who.Data == null) continue;
                    if (Vector3.Distance(who.transform.position, here) > reach) continue;

                    Explain(who, again: true);
                    shown++;
                }

                ItemForgePlugin.Log.LogInfo($"--- разобрано: {shown} ---");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог разобрать груз вокруг: " + e);
            }
        }

        /// <summary>The same for one man. Once each, unless asked again.</summary>
        internal static void Explain(HumaniodUnit who, bool again)
        {
            if (!Breakdown.Value || who == null || who.Data == null) return;
            if (!again && !explained.Add(who.GetInstanceID())) return;

            try
            {
                ItemForgePlugin.Log.LogInfo($"«{who.Data.unitname}» "
                    + $"(ур. {((CharacterSaveData)(object)who.Data).level}): "
                    + $"груз {Load(who):0.#} из {Capacity(who):0.#}, сила {who.Strength} "
                    + $"| надето {who.currentWeight:0.#}, сумка {who.inventoryWeight:0.#}");

                System.Collections.Generic.List<Inventory> worn =
                    new System.Collections.Generic.List<Inventory>();

                if (who.equipmentmanger != null)
                {
                    Slots(who.equipmentmanger.equipInfos, worn);
                    Slots(who.equipmentmanger.standByWeaponInfos, worn);
                }

                System.Collections.Generic.List<Inventory> bag =
                    new System.Collections.Generic.List<Inventory>();

                if (who.items != null && who.items.items != null)
                {
                    foreach (Inventory thing in who.items.items)
                    {
                        if (thing != null && thing.itemInfo != null) bag.Add(thing);
                    }
                }

                ItemForgePlugin.Log.LogInfo("    надето: " + Line(worn));
                ItemForgePlugin.Log.LogInfo("    в сумке: " + Line(bag));

                // Один и тот же объект в слоте и в сумке: игра сосчитает его дважды, потому
                // что надетое и сумка считаются порознь и друг о друге не знают.
                System.Collections.Generic.List<Inventory> twice =
                    new System.Collections.Generic.List<Inventory>();

                foreach (Inventory thing in worn)
                {
                    if (bag.Contains(thing)) twice.Add(thing);
                }

                ItemForgePlugin.Log.LogInfo(twice.Count == 0
                    ? "    в обоих местах сразу: ничего"
                    : "    В ОБОИХ МЕСТАХ СРАЗУ: " + Line(twice));

                // И отдельно — разные объекты одного вида. Так выглядел задвоенный амулет:
                // объекты не равны, а вещь на человеке одна и та же.
                System.Collections.Generic.List<Inventory> pairs =
                    new System.Collections.Generic.List<Inventory>();

                foreach (Inventory thing in worn)
                {
                    if (twice.Contains(thing)) continue;

                    foreach (Inventory other in bag)
                    {
                        if (!ReferenceEquals(other, thing) && other.itemInfo == thing.itemInfo)
                        {
                            pairs.Add(other);
                            break;
                        }
                    }
                }

                if (pairs.Count > 0)
                {
                    ItemForgePlugin.Log.LogInfo("    того же вида и надето, и в сумке: "
                        + Line(pairs));
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог разобрать груз: " + e);
            }
        }

        /// <summary>Collects what is actually sitting in these slots.</summary>
        private static void Slots(EquipInfo[] slots,
            System.Collections.Generic.List<Inventory> into)
        {
            if (slots == null) return;

            foreach (EquipInfo slot in slots)
            {
                if (slot == null || !slot.IsEquiped()) continue;
                if (slot.inventory == null || slot.inventory.itemInfo == null) continue;

                into.Add(slot.inventory);
            }
        }

        /// <summary>One line: the sum, then every piece with what it weighs.</summary>
        private static string Line(System.Collections.Generic.List<Inventory> things)
        {
            if (things == null || things.Count == 0) return "пусто";

            System.Collections.Generic.List<string> parts =
                new System.Collections.Generic.List<string>();

            float sum = 0f;

            foreach (Inventory thing in things)
            {
                int stack = Mathf.Max(1, thing.stackNum);
                float mass = thing.itemInfo.weight * stack;

                sum += mass;

                parts.Add($"{thing.itemInfo.Name} {mass:0.##}"
                    + (stack > 1 ? $" (x{stack})" : ""));
            }

            return $"{sum:0.#} кг — " + string.Join(", ", parts.ToArray());
        }

        /// <summary>Gives a creature the strength its years have earned it.</summary>
        internal static void Grow(UnitAttribute who)
        {
            if (!Enabled.Value || who == null || who.Data == null) return;
            if (who.Data.isdead) return;
            if (who.Data.team == Faction.player || who.inParty) return;

            // Годы мага уходят в голову, а не в руки. Без этой оговорки правило делало из
            // всякого старого чародея силача: уровень растит силу, сила считается в уровень,
            // и колдун сто тридцатого выходил с полутора сотнями силы — больше, чем у рыцаря,
            // который ничем другим и не занимался. Унести своё он всё равно сможет: вес
            // разбирается ниже, по грузу, а не по годам.
            if (!GrowMages.Value && who.Data.isMagician) return;

            try
            {
                NPCSaveData mind = who.Data as NPCSaveData;
                if (mind == null) return;

                CharacterSaveData data = (CharacterSaveData)(object)who.Data;

                // Сила берётся из того, кто существо есть, а не из того, во что его одели.
                // Тогда латы достаются тем, кто дожил до них, а не всякому, кого игра решила
                // нарядить.
                int want = Mathf.CeilToInt(MightBase.Value + data.level * MightPerLevel.Value);
                if (want <= mind.strength) return;

                mind.strength = want;
                if (mind.humanAttribute != null) mind.humanAttribute.BSstrength = want;

                who.DoUpdateAttribute();
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог вырастить силу по уровню: " + e.Message);
            }
        }

        private static readonly AccessTools.FieldRef<HumaniodUnit, int> strMD =
            AccessTools.FieldRefAccess<HumaniodUnit, int>("StrMD");

        /// <summary>What gear and effects add to strength, as the game itself last counted it.</summary>
        private static int MightBonus(HumaniodUnit who)
        {
            try { return strMD(who); }
            catch { return 0; }
        }

        private static readonly System.Collections.Generic.HashSet<int> trimmed =
            new System.Collections.Generic.HashSet<int>();
        private static int trimTold;

        /// <summary>
        /// Takes back strength the old fitting heaped on by mistake.
        ///
        /// Сбой прежней подгонки складывал силу заново на каждом заходе, и у кого заходов было
        /// много, у того она выросла в разы: торговец пятого уровня с силой под двести пятьдесят.
        /// Такого не даёт ни уровень, ни ноша, ни шаблон — это ошибка, и её возвращаем. Порог
        /// нарочно высокий, «уровень × 5 + 60»: настоящего силача из шаблона он не заденет.
        /// </summary>
        private static void Trim(HumaniodUnit body, NPCSaveData mind, int level, int baseWant)
        {
            if (body == null || mind == null || mind.humanAttribute == null) return;
            if (!trimmed.Add(body.GetInstanceID())) return;

            UnitAttribute who = (UnitAttribute)(object)body;
            if (Watch.Watchman(who) || Bestiary.Unburdened(who)) return;

            int cap = Mathf.Max(level * 5 + 60, baseWant);
            int now = mind.humanAttribute.BSstrength;
            if (now <= cap) return;

            int floor = Mathf.CeilToInt(MightBase.Value + level * MightPerLevel.Value);
            int back = Mathf.Max(baseWant, floor, 1);

            mind.humanAttribute.BSstrength = back;
            mind.strength = Mathf.Max(1, back + MightBonus(body));

            if (trimTold < 20)
            {
                trimTold++;
                ItemForgePlugin.Log.LogInfo($"Сила «{mind.unitname}» (ур. {level}) урезана: {now} → {back}, "
                    + "лишнее набрала сбойная подгонка.");
            }
        }

        /// <summary>Gives somebody the strength the kit they were handed calls for.</summary>
        internal static void Fit(UnitAttribute who)
        {
            if (!Enabled.Value || !FitNPC.Value || who == null || who.Data == null) return;
            if (who.Data.isdead) return;
            if (who.Data.team == Faction.player || who.inParty) return;

            HumaniodUnit body = who as HumaniodUnit;
            if (body == null) return;

            try
            {
                NPCSaveData mind = who.Data as NPCSaveData;
                if (mind == null) return;

                float load = Load(body);
                if (load <= 0f) return;

                // Доля, в которую надо уложиться, зависит от опыта: новобранец всегда немного
                // скован, ветеран носит своё как пустой.
                CharacterSaveData data = (CharacterSaveData)(object)who.Data;
                float grown = Mathf.Clamp01((float)data.level / Mathf.Max(1, Veteran.Value));
                float fill = Mathf.Lerp(GreenFill.Value, VeteranFill.Value, grown);

                float need = (load / Mathf.Max(0.05f, fill) - Base.Value) / Mathf.Max(0.1f, PerMight.Value);
                int want = Mathf.CeilToInt(need);

                if (mind.humanAttribute == null) return;

                // Писать надо в базовую силу — ту, что игра хранит. Итоговую, «strength», игра
                // при каждом пересчёте заново складывает: «strength = BSstrength + StrMD».
                // Прибавку берём из самого «StrMD», а не как разность итоговой и базовой: пока
                // человек рождается, итоговая ещё не пересчитана, и разность выходила
                // отрицательной — каждый заход подгонки добавлял к базовой заново, и у
                // торговцев сила раздувалась вчетверо.
                int bonus = body != null ? MightBonus(body) : 0;
                int baseWant = want - bonus;

                Trim(body, mind, data.level, baseWant);

                if (baseWant <= mind.humanAttribute.BSstrength) return;

                int had = mind.strength;
                mind.humanAttribute.BSstrength = baseWant;

                // И итоговую сразу: во время рождения игра её не пересчитывает, а предел груза
                // спрашивают у неё.
                mind.strength = Mathf.Max(1, baseWant + bonus);

                who.DoUpdateAttribute();

                if (Pierce.told < Pierce.Explain.Value)
                {
                    Pierce.told++;
                    ItemForgePlugin.Log.LogInfo($"«{who.Data.unitname}» (ур. {data.level}): "
                        + $"груз {load:0.#}, сила {had} → {want}, "
                        + $"загрузка {load / Capacity(body) * 100f:0} %.");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог подогнать силу под снаряжение: " + e.Message);
            }
        }
    }

    // Строка грузоподъёмности в окне персонажа говорила неправду, и это была моя неправда.
    // Игра пишет туда своё: надетое против «35 + Сила», то есть 2,7 из 42. А правило теперь
    // другое — груз это надетое и сумка вместе против «Сила × 3», и по нему выходило 26 из 21.
    // Человек видел зелёную полоску в одну десятую и при этом «−100 % к скорости», и нигде
    // во всей игре не было числа, по которому можно понять почему. Теперь там то же число,
    // по которому начисляется штраф.
    /// <summary>
    /// Walking teaches what the walking is actually like.
    ///
    /// Игра тренирует ходьбой так: налегке растёт гибкость, под тяжестью — сила. Мысль
    /// верная, а мерка чужая: она смотрит только на надетое и считает его против «35 + Сила».
    /// Сумку не видит вовсе. Отсюда и выходило, что человек идёт с полной поклажей, еле
    /// переставляя ноги, а учится при этом лёгкости движений.
    ///
    /// Здесь та же мысль по нашей мерке: груз — это всё, что на человеке и при нём, а доли
    /// остались игровые, треть и две трети.
    [HarmonyPatch(typeof(HumaniodUnit), "OnMove")]
    internal static class OnMove_Patch
    {
        private static bool Prefix(HumaniodUnit __instance, float time)
        {
            if (!Burden.Enabled.Value || __instance == null) return true;
            if (!Burden.Ready(__instance)) return true;

            // На карте мира ходьба не учит ничему — это правило игры, и оно остаётся.
            if (WorldTravelManager.instance != null) return false;

            try
            {
                float fill = Burden.Load(__instance) / Burden.Capacity(__instance);

                if (fill < 0.35f)
                {
                    __instance.GainAttributeExp(2, time);
                }
                else if (fill < 0.65f)
                {
                    __instance.GainAttributeExp(0, 0.5f * time);
                    __instance.GainAttributeExp(2, 0.5f * time);
                }
                else
                {
                    __instance.GainAttributeExp(0, time);
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог зачесть ходьбу: " + e.Message);
                return true;
            }

            return false;
        }
    }

    // То же самое в окне сумки. Игра пишет там свой отдельный счёт — вес сумки против
    // «50 + Сила × 2» — и подписывает «без штрафов», потому что по её правилам перегруз
    // сумки стоит только скорости переходов по карте мира. У нас груз один на всё, и два
    // разных числа в двух окнах — это просто способ запутать.
    [HarmonyPatch(typeof(InventoryManager), "SetWeightUI")]
    internal static class SetWeightUI_Patch
    {
        private static void Postfix(InventoryManager __instance, UnitAttribute target)
        {
            if (!Burden.Enabled.Value || __instance == null || target == null) return;

            HumaniodUnit who = target as HumaniodUnit;
            if (who == null || !Burden.Ready(who)) return;

            try
            {
                float load = Burden.Load(who);
                float room = Burden.Capacity(who);
                float fill = (room > 0f) ? load / room : 0f;

                if (__instance.weightBearText != null)
                {
                    __instance.weightBearText.text =
                        load.ToString("0.0") + "/" + room.ToString("0");
                }

                if (__instance.weightBearBar != null)
                {
                    __instance.weightBearBar.fillAmount = Mathf.Clamp01(fill);

                    string hex = (fill < Burden.Free.Value) ? "#537D22"
                        : (fill < 1f) ? "#8C3E13" : "#B02418";

                    Color paint;
                    if (ColorUtility.TryParseHtmlString(hex, out paint))
                    {
                        __instance.weightBearBar.color = paint;
                    }
                }

                // И подпись при наведении: не «без штрафов», а во что груз обходится на
                // самом деле — то же число, по которому начисляется бафф.
                if (__instance.weightTooltip != null)
                {
                    float toll = Burden.Toll(who);

                    __instance.weightTooltip.tip = (toll <= 0.005f)
                        ? ((toll < -0.005f)
                            ? $"налегке: +{-toll * 100f:0}% ко всему"
                            : "без штрафов")
                        : $"под грузом: -{toll * 100f:0}% ко всему"
                            + ((load > room) ? ", идти нельзя" : "");

                    // И сколько снимает школа «Защитник».
                    float ease = Bulwark.Ease(who);
                    if (toll > 0.005f && ease < 0.999f)
                    {
                        __instance.weightTooltip.tip += $"\nЗащитник: скорость -{toll * ease * 100f:0}% вместо -{toll * 100f:0}%";
                    }

                    // И во что обходится каждый шаг.
                    if (Stride.Enabled != null && Stride.Enabled.Value && Stride.PerKilo.Value > 0f)
                    {
                        __instance.weightTooltip.tip += $"\nшаг: {load * Stride.PerKilo.Value:0.##} "
                            + $"выносливости за метр, бегом ×{Stride.Running.Value:0.#}";
                    }
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог показать груз в сумке: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(UIApplyUnitAttribute), "OnWeightChange")]
    internal static class OnWeightChange_Patch
    {
        private static void Postfix(UIApplyUnitAttribute __instance, HumaniodUnit unit)
        {
            if (!Burden.Enabled.Value || __instance == null || unit == null) return;
            if (!Burden.Ready(unit)) return;

            try
            {
                float load = Burden.Load(unit);
                float room = Burden.Capacity(unit);
                float fill = (room > 0f) ? load / room : 0f;

                if (__instance.weightbearText != null)
                {
                    __instance.weightbearText.text =
                        load.ToString("0.0") + "/" + room.ToString("0");
                }

                if (__instance.weightBearBar != null)
                {
                    __instance.weightBearBar.fillAmount = Mathf.Clamp01(fill);

                    // Зелёное — налегке, до половины: там прибавка. Бурое — выше половины,
                    // там плата. Красное — за пределом, там уже никуда не идёшь.
                    string hex = (fill < Burden.Free.Value) ? "#537D22"
                        : (fill < 1f) ? "#8C3E13" : "#B02418";

                    Color paint;
                    if (ColorUtility.TryParseHtmlString(hex, out paint))
                    {
                        __instance.weightBearBar.color = paint;
                    }
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог показать груз: " + e.Message);
            }
        }
    }

    // Родной расчёт штрафа за надетое считает только надетое и только против своего предела.
    // Заменяем его целиком: груз один, предел один, и цена у него другая.
    [HarmonyPatch(typeof(HumaniodUnit), "CountCurrentWeight")]
    internal static class CountCurrentWeight_Patch
    {
        private static void Postfix(HumaniodUnit __instance)
        {
            if (!Burden.Enabled.Value) return;

            __instance.buffmanger?.RemoveBuff("WeightBuff");
            Burden.Settle(__instance);
        }
    }

    // Вес сумки теперь тоже часть груза, значит и пересчитывать надо при каждом его изменении.
    //
    // А вот замедление переходов по карте мира остаётся игровым, и намеренно: там считается
    // вес сумки против «50 + Сила × 2», и это про другое. Наш груз — про то, каково человеку
    // драться и ходить ногами; дорога между городами меряется вьюком, а не боеспособностью.
    [HarmonyPatch(typeof(HumaniodUnit), "CountInventoryWeight")]
    internal static class CountInventoryWeight_Patch
    {
        private static void Postfix(HumaniodUnit __instance)
        {
            if (!Burden.Enabled.Value) return;

            Burden.Settle(__instance);
        }
    }

    // Силу подгоняем после того, как существо оделось: раньше считать нечего.
    [HarmonyPatch(typeof(EquipmentManager), "InitEquips")]
    internal static class InitEquips_Might_Patch
    {
        private static void Postfix(EquipmentManager __instance)
        {
            if (__instance == null || __instance.unit == null) return;

            Burden.Fit((UnitAttribute)(object)__instance.unit);
        }
    }

    // И ещё раз при каждом надевании: моя выдача приходит на бойца позже родного одевания, а
    // вслед за ней приходит и вес. Иначе рыцарь получает силу под своё шаблонное тряпьё, потом
    // на него вешают сорок три единицы латов, и спросить об этом уже некому.
    [HarmonyPatch(typeof(EquipmentManager), "EquipItem", new Type[] { typeof(Inventory), typeof(int) })]
    internal static class EquipItem_Might_Patch
    {
        private static void Postfix(EquipmentManager __instance)
        {
            if (__instance == null || __instance.unit == null) return;

            Burden.Fit((UnitAttribute)(object)__instance.unit);
        }
    }

    // Единственная дверь, через которую вещь попадает в сумку отряда: сундук, земля, лавка,
    // награда — всё идёт сюда. Здесь и спрашиваем, поднимет ли он это.
    //
    // Отказ — не отмена. Берущий вынимает вещь из сундука прежде, чем кладёт в сумку, и
    // простое «нет» уничтожило бы её. Поэтому кладём под ноги родным же способом: вещь на
    // земле, её видно, её можно взять, сбросив другое.
    [HarmonyPatch(typeof(InventoryManager), "PutItemInbag",
        new Type[] { typeof(Inventory), typeof(bool) })]
    internal static class PutItemInbag_Burden_Patch
    {
        private static bool Prefix(InventoryManager __instance, Inventory inventory)
        {
            if (Burden.Enabled == null || !Burden.Enabled.Value) return true;
            if (Burden.Refuse == null || !Burden.Refuse.Value) return true;
            if (__instance == null || inventory == null || inventory.itemInfo == null) return true;

            try
            {
                // Сюжет не запираем.
                ItemType kind = inventory.itemInfo.itemType;
                if (kind == ItemType.Quest || kind == ItemType.Readable) return true;

                HumaniodUnit who = gameManager.currentplayUnit;
                if (who == null || !Burden.Ready(who)) return true;

                // Снятое с себя — перекладывание, а не прибавка: этот вес уже посчитан.
                if (Wearing(who, inventory)) return true;

                float mass = Mathf.Max(0f, inventory.itemInfo.weight)
                    * Mathf.Max(1, inventory.stackNum);

                if (mass <= 0f) return true;

                float load = Burden.Load(who);
                float room = Burden.Capacity(who);

                if (load + mass <= room) return true;

                __instance.DropItemOnTheFloor(inventory, (UnitAttribute)(object)who);

                GameController.ShowMessage($"Не унесёшь: {load:0.#} из {room:0.#}, "
                    + $"а в этом ещё {mass:0.#}. Лежит под ногами.", 3f);

                return false;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>True when this very thing is already on him, and only changing hands.</summary>
        private static bool Wearing(HumaniodUnit who, Inventory thing)
        {
            if (who == null || who.equipmentmanger == null) return false;
            if (who.equipmentmanger.equipInfos == null) return false;

            foreach (EquipInfo slot in who.equipmentmanger.equipInfos)
            {
                if (slot != null && slot.inventory == thing) return true;
            }

            return false;
        }
    }
}
