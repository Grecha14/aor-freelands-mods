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
    /// Where the blow landed, and what was standing there.
    ///
    /// Броня в этой игре складывается в одно число. Шлем, нагрудник, штаны и пояс суммируют
    /// свои сопротивления, и сумма работает против каждого удара разом, куда бы он ни пришёлся.
    /// Полный доспех хорош не тем, что закрывает тело, а тем, что предметов в нём четыре.
    ///
    /// Место попадания у игры на самом деле есть — она разыгрывает его при каждом ударе с
    /// весами «грудь сорок пять, ноги двадцать пять, голова двадцать, пояс десять», — но
    /// использует только чтобы снять с одной вещи прочность.
    ///
    /// Здесь этот розыгрыш решает и урон: держит удар та вещь, в которую попало, одна.
    ///
    /// И держит она не по своей авторской цифре, а по прочности. Авторские цифры оказались
    /// негодными: латы Т4 держат рубящее 41.6, дробящее 41.6, колющее 42.5 — то есть разницы
    /// между мечом, молотом и копьём для лат не существует, — а средняя чешуя держит 45 против
    /// тяжёлых лат с их 17. Прочность же честна: от стёганки в сто одиннадцать очков до латного
    /// нагрудника в три тысячи.
    ///
    /// Отсюда два числа на вещь. Стойкость — сколько она снимает с удара, с поправкой на то,
    /// чем бьют: рубящее латы держат прекрасно, дробящее плохо. И порог — та пробивная сила,
    /// ниже которой удар не проходит вовсе: меч по латам скользит, болт и молот проходят.
    /// </summary>
    internal static class Anatomy
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Spots;
        internal static ConfigEntry<float> Sideways;
        internal static ConfigEntry<float> Behind;
        internal static ConfigEntry<string> Height;
        internal static ConfigEntry<float> Hit;
        internal static ConfigEntry<float> Rest;
        internal static ConfigEntry<bool> FromDurability;
        internal static ConfigEntry<float> Solid;
        internal static ConfigEntry<string> Against;
        internal static ConfigEntry<bool> Gates;
        internal static ConfigEntry<float> Bite;
        internal static ConfigEntry<float> Blunted;
        internal static ConfigEntry<bool> Grinds;

        // Места тела. Голова, грудь и ноги совпадают с игровыми слотами один в один.
        internal const int Head = 2;
        internal const int Chest = 4;
        internal const int Pants = 7;

        // Пояс местом быть перестал: он ушёл к кольцам и амулетам — украшение со статами, а
        // не доспех. Ремень на поясе никогда и не был тем, во что метят, а десятую долю всех
        // ударов на себя брал. Номер оставлен, чтобы прочие модули узнавали пояс и обходили
        // его стороной.
        internal const int Belt = 6;

        // Руки. Своего слота у них в игре нет — наручи отдельной вещью не носят, — поэтому
        // номер берём за пределом игровых. Железо на них считается нагрудное и вполсилы:
        // рукав кольчуги и крыло наплечника прикрывают руку, но кирасой не становятся.
        internal const int Arms = 20;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Anatomy", "Enabled", true,
                "Let the blow land somewhere. Armour then holds by the piece that was struck "
                + "rather than by everything worn at once.");

            Height = config.Bind("Anatomy", "Height",
                "Small=pants,Medium=pants|arms|chest,Large=pants|arms|chest|head,"
                + "Giant=pants|arms|chest|head,Titanic=pants|arms|chest|head",
                "How high a creature can reach, by its size. A rat goes for the legs because a "
                + "rat cannot reach anything else; a wolf takes the legs and the belly; only "
                + "something man-high or taller gets at the throat. "
                + "Anything that flies ignores this and strikes where it likes — that is what "
                + "wings are for. Men are not bound by it either: they carry weapons, and a "
                + "weapon has its own reach.");

            Spots = config.Bind("Anatomy", "Spots", "chest=44,pants=24,head=18,arms=14",
                "Where blows land and how often. Every place is drawn whether or not anything "
                + "is worn there: a man without a helmet has a head all the same. "
                + "The belt is gone from the list — it is jewellery now, not armour — and arms "
                + "stand in its stead: they are what a man puts between himself and the blade.");

            Sideways = config.Bind("Anatomy", "Sideways", 2.5f,
                new ConfigDescription(
                    "How much likelier a blow from the flank is to find an arm. Facing a man "
                    + "you strike past his guard; from his side you strike through it, and what "
                    + "is in the way is his arm. The game reckons the quarter a blow comes from "
                    + "itself, and this reads that.",
                    new AcceptableValueRange<float>(1f, 10f)));

            Behind = config.Bind("Anatomy", "Behind", 0.4f,
                new ConfigDescription(
                    "And how much likelier from behind, where the arms are not between anything "
                    + "and anything. Less than even: a man struck in the back is struck in the "
                    + "back.",
                    new AcceptableValueRange<float>(0f, 2f)));

            FromDurability = config.Bind("Anatomy", "FromDurability", true,
                "Work out what a piece is worth from how much of it there is — its durability — "
                + "rather than from the resistance written on it. The written figures do not "
                + "survive reading: plate stops a hammer exactly as well as it stops a sword, and "
                + "a scale shirt outdoes a plate cuirass. Durability is honest: a hundred and "
                + "eleven points of padding against three thousand of plate.");

            Solid = config.Bind("Anatomy", "Solid", 0.3f,
                new ConfigDescription(
                    "Damage a piece takes off a blow, per square root of its durability. The root "
                    + "is what keeps a thirtyfold spread in the ledger from becoming a thirtyfold "
                    + "spread in the fight. Three tenths puts a plate cuirass at seventeen, a "
                    + "leather jerkin at six, a padded coat at three.",
                    new AcceptableValueRange<float>(0f, 3f)));

            Against = config.Bind("Anatomy", "Against", "1.4,0.5,1.3",
                "What the piece is worth against cutting, crushing and thrusting, in that order. "
                + "Heavy armour is very nearly proof against an edge and a point alike — that is "
                + "what it was for — and does badly against a hammer, which is the whole reason "
                + "hammers were carried. A bolt gets through the same armour by a different "
                + "road: not by beating the plate but by going through it, which is reckoned "
                + "separately.");

            Gates = config.Bind("Anatomy", "Gates", true,
                "Let armour refuse a blow outright. Without this everything gets through, only "
                + "less of it, and a knight in full harness is merely inconvenienced by arrows "
                + "rather than untroubled by them.");

            Bite = config.Bind("Anatomy", "Bite", 0.45f,
                new ConfigDescription(
                    "The piercing a blow needs to get through at all, per square root of the "
                    + "piece's durability. At forty-five hundredths a crossbow, a war hammer and "
                    + "an estoc go through plate; swords, axes and arrows do not.",
                    new AcceptableValueRange<float>(0f, 3f)));

            Blunted = config.Bind("Anatomy", "Blunted", 0.15f,
                new ConfigDescription(
                    "What still gets through when a blow fails to pierce, as a share. Not nothing: "
                    + "a man in plate hit by a mace is unhurt in the skin and shaken all the same.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Grinds = config.Bind("Anatomy", "Grinds", false,
                "Let armour also grind down the blows it fails to stop, taking a share off each "
                + "one. Off, and armour does one thing only: it says whether the blow arrived. "
                + "Doing both made it do neither — a two-hander delivered five points of a "
                + "hundred and sixty to a harnessed man, a duel ran to a hundred blows, and the "
                + "whole of mastery was worth twelve percent. What a blow costs is now the place "
                + "it landed, which is reckoned under Lethal.");

            Hit = config.Bind("Anatomy", "Hit", 1f,
                new ConfigDescription(
                    "What the struck piece is worth, as a multiple of itself. Raise it to give a "
                    + "full harness back some of what it loses by no longer counting four times "
                    + "over.",
                    new AcceptableValueRange<float>(0f, 5f)));

            Rest = config.Bind("Anatomy", "Rest", 0f,
                new ConfigDescription(
                    "What the other three pieces are still worth against this blow, as a share. "
                    + "Zero: they are covering somewhere else.",
                    new AcceptableValueRange<float>(0f, 1f)));
        }

        // Место разыгрывается один раз на удар и на цель — и для урона, и для износа, иначе
        // болт пробивал бы нагрудник, а тупился бы о шлем.
        private static Attack lastAttack;
        private static UnitAttribute lastTarget;
        private static int lastSpot;
        private static int lastFrame = -1;

        /// <summary>
        /// Удар, который разбирается прямо сейчас для этого человека, и место, куда он
        /// пришёлся. Нужно там, где места уже не спрашивают, — в самом списании жизни.
        /// </summary>
        internal static bool Fresh(UnitAttribute target, out Attack attack, out int spot)
        {
            attack = lastAttack;
            spot = lastSpot;

            // И именно свежим: разыгранное место живёт один кадр. Иначе всякая потеря жизни
            // помимо боя — голод, яд, падение, наш же перелив — подбирала последний удар,
            // случившийся когда угодно раньше, и считалась его продолжением. Отсюда и брались
            // переломы у того, кто в этом бою не дрался вовсе.
            if (lastFrame != Time.frameCount) return false;

            return lastAttack != null && (object)target == (object)lastTarget;
        }

        /// <summary>Where this blow landed on this man.</summary>
        internal static int Spot(Attack attack, UnitAttribute target)
        {
            if (attack != null && (object)attack == (object)lastAttack
                && (object)target == (object)lastTarget)
            {
                return lastSpot;
            }

            // Сперва спрашиваем замах: он знает, куда метили, и это не жребий. Жребий
            // остаётся на случай, когда ветка не размечена или бьёт не человек.
            int spot = Aim.Land(attack != null ? attack.attacker : null, target);
            if (spot < 0) spot = Roll(attack != null ? attack.attacker : null, target);

            // Выше своего роста не ударишь. Если выпало недосягаемое, бьём по тому, до чего
            // бьющий дотягивается, — по самому верхнему из доступного ему.
            List<int> высоко = Reach(attack != null ? attack.attacker : null);

            if (высоко != null && высоко.Count > 0 && !высоко.Contains(spot))
            {
                spot = высоко[высоко.Count - 1];
            }

            lastAttack = attack;
            lastTarget = target;
            lastSpot = spot;
            lastFrame = Time.frameCount;

            return spot;
        }

        private static readonly Dictionary<string, List<int>> reach =
            new Dictionary<string, List<int>>();
        private static string reachRead;

        /// <summary>
        /// Докуда достаёт бьющий. Крыса метит в ноги не по злобе, а потому что выше не
        /// дотягивается; волк берёт ноги и брюхо; до горла добирается только то, что ростом
        /// с человека и выше.
        ///
        /// Летающие правилу не подчиняются — на то и крылья. Люди тоже: у них оружие, и
        /// дотягивается оно само.
        /// </summary>
        private static List<int> Reach(UnitAttribute who)
        {
            if (who == null || who is HumaniodUnit) return null;

            try
            {
                if (who.info != null && who.info.canFly) return null;

                string written = Height.Value ?? "";

                if (written != reachRead)
                {
                    reachRead = written;
                    reach.Clear();

                    foreach (string one in written.Split(','))
                    {
                        string[] halves = one.Split('=');
                        if (halves.Length != 2) continue;

                        List<int> places = new List<int>();

                        foreach (string where in halves[1].Split('|'))
                        {
                            int slot = Slot(where.Trim());
                            if (slot >= 0) places.Add(slot);
                        }

                        if (places.Count > 0) reach[halves[0].Trim()] = places;
                    }
                }

                List<int> got;
                return reach.TryGetValue(who.size.ToString(), out got) ? got : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Насколько это место вероятнее прочих при ударе с этой стороны.
        ///
        /// Рука — единственное, для чего сторона что-то значит. Спереди человек держит её при
        /// себе и прикрывает ею то, что дороже; сбоку она оказывается первой на пути; со спины
        /// она не прикрывает ничего, и бьют мимо неё.
        ///
        /// Четверть, из которой пришёл удар, игра считает сама — тем же способом, каким решает,
        /// бить ли в спину. Её и спрашиваем, а не меряем углы заново.
        /// </summary>
        private static float Tilt(int place, UnitAttribute who, UnitAttribute target)
        {
            if (place != Arms || who == null || target == null) return 1f;

            try
            {
                AttackDirection from = UnitAttribute.GetDirection(target, who);

                if (from == AttackDirection.left || from == AttackDirection.right
                    || from == AttackDirection.side)
                {
                    return Sideways.Value;
                }

                if (from == AttackDirection.behind) return Behind.Value;
            }
            catch
            {
            }

            return 1f;
        }

        private static int Roll(UnitAttribute who, UnitAttribute target)
        {
            List<KeyValuePair<int, int>> places = Places();
            if (places.Count == 0) return Chest;

            float all = 0f;
            foreach (KeyValuePair<int, int> one in places)
            {
                all += one.Value * Tilt(one.Key, who, target);
            }

            if (all <= 0f) return Chest;

            float drawn = UnityEngine.Random.value * all;

            foreach (KeyValuePair<int, int> one in places)
            {
                drawn -= one.Value * Tilt(one.Key, who, target);
                if (drawn < 0f) return one.Key;
            }

            return Chest;
        }

        private static List<KeyValuePair<int, int>> places;
        private static string placesRead;

        /// <summary>Места и их веса — наружу, чтобы считать по ним среднее по человеку.</summary>
        internal static List<KeyValuePair<int, int>> Table()
        {
            return Places();
        }

        private static List<KeyValuePair<int, int>> Places()
        {
            string written = Spots.Value ?? "";

            if (written == placesRead && places != null) return places;

            placesRead = written;
            places = new List<KeyValuePair<int, int>>();

            foreach (string one in written.Split(','))
            {
                string[] halves = one.Split('=');
                if (halves.Length != 2) continue;

                int weight;
                if (!int.TryParse(halves[1].Trim(), out weight) || weight <= 0) continue;

                int slot = Slot(halves[0].Trim());
                if (slot < 0) continue;

                places.Add(new KeyValuePair<int, int>(slot, weight));
            }

            return places;
        }

        /// <summary>Номер места по его имени. Наружу — чтобы прочие модули не заводили свой.</summary>
        internal static int Where(string name)
        {
            return Slot(name);
        }

        private static int Slot(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "head": return Head;
                case "chest": return Chest;
                case "arms":
                case "hands": return Arms;
                case "pants":
                case "legs": return Pants;
                default: return -1;
            }
        }

        /// <summary>
        /// В каком слоте лежит то, что прикрывает это место.
        ///
        /// Для головы, груди и ног это тот же номер. Для рук — нагрудник: отдельной вещи на
        /// руку в игре нет, а рукав есть у той самой кирасы, и рвётся он вместе с ней.
        /// </summary>
        internal static int Piece(int slot)
        {
            return slot == Arms ? Chest : slot;
        }

        /// <summary>The very piece worn at a place, not its pattern in the book.</summary>
        internal static Inventory WornKit(UnitAttribute who, int slot)
        {
            try
            {
                slot = Piece(slot);

                HumaniodUnit man = who as HumaniodUnit;
                EquipmentManager kit = man != null ? man.equipmentmanger : null;
                if (kit == null || kit.equipInfos == null) return null;
                if (slot < 0 || slot >= kit.equipInfos.Length) return null;
                if (!kit.equipInfos[slot].IsEquiped()) return null;

                return kit.equipInfos[slot].inventory;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>The armour at a place, if anything is worn there.</summary>
        internal static UIArmorInfo Worn(UnitAttribute who, int slot, out float wear)
        {
            wear = 0f;

            try
            {
                slot = Piece(slot);

                // Броня есть только у людей. У зверя всё сопротивление — его собственная шкура,
                // и место попадания ей безразлично.
                HumaniodUnit man = who as HumaniodUnit;
                EquipmentManager kit = man != null ? man.equipmentmanger : null;
                if (kit == null || kit.equipInfos == null) return null;
                if (slot < 0 || slot >= kit.equipInfos.Length) return null;
                if (!kit.equipInfos[slot].IsEquiped()) return null;

                Inventory bit = kit.equipInfos[slot].inventory;
                UIArmorInfo coat = bit.itemInfo as UIArmorInfo;
                if (coat == null) return null;

                // Потрёпанность: до семидесяти процентов прочности вещь держит в полную силу,
                // ниже — на процент меньше за каждый потерянный процент, и разбитая в ноль
                // держит ещё треть. Игровые ступени по четвертям здесь больше не действуют.
                wear = Tatter.Holds(bit);

                return coat;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>What a single piece takes off a blow, per damage type.</summary>
        private static float Guard(UIArmorInfo coat, float wear, int kind)
        {
            if (coat == null) return 0f;

            if (!FromDurability.Value)
            {
                return coat.damageDR != null && kind < coat.damageDR.Length
                    ? coat.damageDR[kind] * wear
                    : 0f;
            }

            float much = coat.durability;
            if (much <= 0f) return 0f;

            return Solid.Value * Mathf.Sqrt(much) * wear * Facing(kind);
        }

        /// <summary>The piercing a blow must carry to get through this piece at all.</summary>
        internal static float Gate(UIArmorInfo coat, float wear)
        {
            if (coat == null || !Gates.Value) return 0f;

            float much = coat.durability;
            if (much <= 0f) return 0f;

            return Bite.Value * Mathf.Sqrt(much) * wear;
        }

        private static float[] facings;
        private static string facingsRead;

        private static float Facing(int kind)
        {
            string written = Against.Value ?? "";

            if (written != facingsRead)
            {
                facingsRead = written;

                List<float> got = new List<float>();
                foreach (string one in written.Split(','))
                {
                    float much;
                    if (float.TryParse(one.Trim(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out much))
                    {
                        got.Add(much);
                    }
                }

                facings = got.ToArray();
            }

            if (facings == null || kind < 0 || kind >= facings.Length) return 1f;

            return facings[kind];
        }

        /// <summary>
        /// Turns what stands at the struck place into the percentages the game reckons with.
        /// Null when nothing can be worked out and the game should be left alone.
        /// </summary>
        internal static float[] Coat(UnitAttribute who, int slot, Attack attack)
        {
            int kinds = UnitAttribute.DAMAGE_TYPE_COUNT;

            HumaniodUnit man = who as HumaniodUnit;
            EquipmentManager kit = man != null ? man.equipmentmanger : null;
            if (kit == null || kit.equipInfos == null) return null;
            if (who.damageResist == null || attack == null || attack.damage == null) return null;

            float[] mine = new float[kinds];

            // То, что не от брони — шкура, зачарования, буфы, бонусы комплектов — к месту не
            // привязано и работает везде. Берём его как то, что игра насчитала сверх надетого.
            float[] loose = new float[kinds];
            for (int j = 0; j < kinds && j < who.damageResist.Length; j++) loose[j] = who.damageResist[j];

            for (int i = 0; i < kit.equipInfos.Length; i++)
            {
                float wear;
                UIArmorInfo coat = Worn(who, i, out wear);
                if (coat == null || coat.damageDR == null) continue;

                for (int j = 0; j < kinds && j < coat.damageDR.Length; j++)
                {
                    loose[j] -= coat.damageDR[j] * wear;
                }
            }

            float there;
            UIArmorInfo struck = Worn(who, slot, out there);

            float through = Volley.Pierce(attack, struck);

            for (int j = 0; j < kinds; j++)
            {
                // Стойкость вещи больше не отнимает от удара: она только решает, дошёл ли он.
                // Перемалывание включается отдельной настройкой и по умолчанию выключено.
                float held = Grinds.Value
                    ? Guard(struck, there, j) * Hit.Value * (1f - through)
                    : 0f;

                // Остальные три места, если им оставлена доля.
                if (Grinds.Value && Rest.Value > 0f)
                {
                    for (int i = 0; i < kit.equipInfos.Length; i++)
                    {
                        if (i == slot) continue;

                        float wear;
                        UIArmorInfo other = Worn(who, i, out wear);
                        held += Guard(other, wear, j) * Rest.Value;
                    }
                }

                // Стойкость снимает с удара столько-то урона, а игра считает долями. Переводим
                // одно в другое по тому, чем именно бьют: удар в десять очков латы съедают
                // целиком, удар в сто — наполовину.
                float coming = Coming(attack, j);
                float part = coming > 0.01f ? 100f * held / coming : 0f;

                mine[j] = Mathf.Clamp(Mathf.Max(0f, loose[j]) + part, 0f,
                    UnitAttribute.DAMAGE_RESISTANCE_CAP);
            }

            return mine;
        }

        private static float Coming(Attack attack, int kind)
        {
            try
            {
                foreach (KeyValuePair<DamageType, Damage> one in attack.damage)
                {
                    if ((int)one.Key == kind && one.Value != null) return one.Value.currentDamage;
                }
            }
            catch
            {
            }

            return 0f;
        }

        /// <summary>Whether this blow got through the piece at the struck place at all.</summary>
        internal static bool Pierced(UnitAttribute who, int slot, Attack attack)
        {
            if (!Gates.Value) return true;

            float wear;
            UIArmorInfo struck = Worn(who, slot, out wear);
            if (struck == null) return true;

            float gate = Gate(struck, wear);
            if (gate <= 0f) return true;

            return Volley.Punch(attack) >= gate;
        }
    }

    // Единственное место, где сопротивление превращается в снятый урон. Подменяем массив на
    // время расчёта: всё прочее — пробивание оружия, предел, порядок типов — игра сделает сама
    // и по-своему, и это как раз то, что нужно.
    [HarmonyPatch(typeof(UnitAttribute), "DamageReduce")]
    internal static class DamageReduce_Anatomy_Patch
    {
        private sealed class Held
        {
            internal float[] was;
            internal bool stopped;
            internal int spot;
            internal float share = -1f;   // -1: Breach не вмешивался
        }

        private static void Prefix(UnitAttribute __instance, Attack attack, ref object __state)
        {
            __state = null;

            try
            {
                if (!Anatomy.Enabled.Value || __instance == null || attack == null) return;
                if (__instance.damageResist == null) return;

                int spot = Anatomy.Spot(attack, __instance);

                Held held = new Held();
                held.spot = spot;
                held.stopped = !Anatomy.Pierced(__instance, spot, attack);

                // Клинок садится о то, во что попал. Молот не садится вовсе: он для того и
                // сделан, чтобы бить по железу.
                float wear;
                UIArmorInfo coat = Anatomy.Worn(__instance, spot, out wear);
                if (coat != null) Lethal.Blunt(attack, coat, wear);

                // Наша стена считает только рубящее, дробящее и колющее. Чисто
                // огненному или ядовитому удару ей сказать нечего, и вмешиваться туда
                // нельзя: у доспеха на это свои сопротивления, и они должны работать.
                //
                // И только по тому, на ком доспех вообще бывает. Наш вычет заменяет надетое,
                // а у зверя надето ничего — у него шкура, и шкуру мы не считаем. Отнимая у
                // него игровое сопротивление, мы отнимали бы её вовсе: минотавр в броне на
                // модели держит полсотни процентов, дух волка шестьдесят пять, и всё это
                // обнулялось. Человек без шлема — другое дело: там голое место и должно
                // биться в полный урон.
                bool worn = __instance is HumaniodUnit;

                // Зверю доспеха не надеть, но шкура у него есть, и считается она той же
                // стеной: Beastly выводит её из роста и уровня. Оттого пробитие оружия
                // наконец что-то значит и против зверья.
                if (!worn && Breach.Enabled.Value && Breach.Physical(attack)
                    && Beastly.Enabled != null && Beastly.Enabled.Value)
                {
                    float hide = Beastly.Wall(__instance);

                    if (hide > 0f)
                    {
                        held.share = Breach.ShareAt(attack, hide, Beastly.Through.Value, __instance);
                        held.stopped = held.share <= Breach.Spared.Value + 1e-4f;

                        held.was = __instance.damageResist;

                        float[] onlyMine = new float[UnitAttribute.DAMAGE_TYPE_COUNT];
                        for (int i = 3; i < onlyMine.Length && i < held.was.Length; i++)
                        {
                            onlyMine[i] = held.was[i];
                        }

                        __instance.damageResist = onlyMine;
                        __state = held;
                        return;
                    }
                }

                if (Breach.Enabled.Value && Breach.Physical(attack) && worn)
                {
                    // Броня считается воротами, а не процентом. Игре на время расчёта
                    // отдаём пустое сопротивление — она ничего не срежет, — а свою долю
                    // накладываем после, потому что её потолок в восемьдесят процентов
                    // нам тесен: латы должны уметь не пустить вовсе.
                    // Вычет — это прочность на килограмм, а у каждой вещи она своя: одна
                    // вышла из кузни ладной, другая тяжёлой и хрупкой.
                    float worth = Temper.Worth(Anatomy.WornKit(__instance, spot));

                    held.share = Breach.Share(attack, coat, wear, spot, worth, __instance);
                    held.stopped = held.share <= Breach.Spared.Value + 1e-4f;

                    held.was = __instance.damageResist;

                    // Обнуляем только рубящее, дробящее и колющее — то, что считаем сами.
                    // Стойкость к огню, холоду и яду остаётся игровой: она приходит с
                    // амулетами и кольцами, и отнимать её у игрока не за что.
                    float[] mineOnly = new float[UnitAttribute.DAMAGE_TYPE_COUNT];

                    for (int i = 3; i < mineOnly.Length && i < held.was.Length; i++)
                    {
                        mineOnly[i] = held.was[i];
                    }

                    __instance.damageResist = mineOnly;
                }
                else
                {
                    float[] mine = Anatomy.Coat(__instance, spot, attack);

                    if (mine != null)
                    {
                        held.was = __instance.damageResist;
                        __instance.damageResist = mine;
                    }
                }

                __state = held;
            }
            catch
            {
                __state = null;
            }
        }

        /// <summary>
        /// Наложить долю только на то, что встречало стену.
        ///
        /// Удар бывает смешанным: у лавового дракона половина укуса — огонь, у лесного треть —
        /// яд. Стена держит железо, а не пламя, и множить долей весь удар разом значит дважды
        /// считать одно и то же: сперва доспех съедает огонь, которого не касался, а потом его
        /// же съедает огнестойкость.
        ///
        /// Оттого доля ложится на рубящее, дробящее и колющее, а стихии идут своей дорогой —
        /// к сопротивлениям, которые игрок носит на шее и на пальцах.
        /// </summary>
        private static void Bite(DamageBase all, float share)
        {
            if (all == null) return;

            try
            {
                foreach (KeyValuePair<DamageType, Damage> one in all)
                {
                    if (one.Value == null || (int)one.Key > 2) continue;

                    one.Value.minDamage *= share;
                    one.Value.maxDamage *= share;
                    one.Value.currentDamage *= share;
                }
            }
            catch
            {
            }
        }

        private static void Postfix(UnitAttribute __instance, object __state, ref DamageBase __result)
        {
            Held held = __state as Held;
            if (held == null) return;

            // Возвращаем всегда и при любом исходе: оставленный подменённым массив — это
            // сломанная броня до конца боя.
            if (held.was != null && __instance != null) __instance.damageResist = held.was;

            if (__result == null) return;

            // Непробившее проходит малой долей. Режем здесь, а не сопротивлением, потому что
            // сопротивление у игры упирается в восемьдесят процентов, а тут нужно больше.
            // Доля, посчитанная воротами. Ноль означает, что доспех удержал целиком.
            if (held.share >= 0f)
            {
                Bite(__result, held.share);
            }
            else if (held.stopped)
            {
                __result *= Lethal.Enabled.Value ? Lethal.Spared.Value : Anatomy.Blunted.Value;
                return;
            }

            if (!Lethal.Enabled.Value) return;

            // Дошедший удар стоит столько, сколько стоит место: голова вдвое с половиной
            // дороже корпуса, ноги вдвое дешевле.
            __result *= Lethal.Worth(held.spot);

            // И ногами человека не убить. Двадцать ударов по голеням оставят его калекой на
            // ногах, но не мертвецом: чтобы убить, надо бить туда, где убивают.
            if (!Lethal.Kills(held.spot) && __instance != null)
            {
                float floor = __instance.maxhp * Lethal.Legs.Value;
                float most = (__instance.Data != null ? __instance.Data.currenthp : 0f) - floor;

                if (most <= 0f) __result *= 0f;
                else if (__result.Damage() > most) __result *= most / __result.Damage();
            }
        }
    }

    // Износ идёт туда же, куда пришёлся удар. Игра разыгрывала своё место отдельно и только
    // среди надетого; теперь место одно на оба счёта, и пустое тоже выпадает — просто рвать
    // там нечего.
    [HarmonyPatch(typeof(EquipmentManager), "OnGetHit")]
    internal static class OnGetHit_Anatomy_Patch
    {
        private static bool Prefix()
        {
            // Износ брони теперь считается в одном месте — там, где известно, что именно
            // прошло сквозь железо (Pierce.Land). Прежде здесь снималось ещё и за каждый
            // удержанный удар, вдвое против пробившего: латы, которых никто не пробил,
            // стирались от одних попыток, а потрёпанные латы пропускали урон.
            return true;
        }
    }
}
