using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using spell;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Makes armour wear where it was struck, and lets a blow through where it has failed.
    ///
    /// Игра уже разыгрывает зону попадания, но лотереей: при каждом ударе она тянет одну вещь
    /// из четырёх по весам — кираса 45, поножи 25, шлем 20, пояс 10 — и снимает с неё немного
    /// прочности. Куда пришёлся удар на самом деле, не имеет значения: стрела в голову может
    /// износить пояс.
    ///
    /// Здесь зона выводится из самого удара — откуда он пришёл, — и дальше всё следует из неё.
    /// Изнашивается та вещь, что стояла на пути. А когда она сломана или её нет вовсе, удар
    /// доходит до тела: сопротивление доспеха для этого удара не считается, и тяжёлый удар в
    /// открытое место оставляет травму.
    ///
    /// Сопротивление при этом остаётся общим, как в игре, — по зонам его не разбираем. Слом в
    /// одном месте не делает бойца голым везде; он делает голым одно место, и туда бьют.
    /// </summary>
    internal static class Pierce
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> WearRate;
        internal static ConfigEntry<bool> Injuries;
        internal static ConfigEntry<float> InjuryShare;
        internal static ConfigEntry<string> Breaks;
        internal static ConfigEntry<bool> WeaponDecay;
        internal static ConfigEntry<string> TierWear;
        internal static ConfigEntry<float> TierBlock;
        internal static ConfigEntry<float> TierPierce;
        internal static ConfigEntry<string> ClassBlock;
        internal static ConfigEntry<float> CritFinds;
        internal static ConfigEntry<float> CritGap;
        internal static ConfigEntry<string> TypeWear;
        internal static ConfigEntry<float> BluntSwing;
        internal static ConfigEntry<float> MagicWear;
        internal static ConfigEntry<float> MagicRank;
        internal static ConfigEntry<float> WitPower;
        internal static ConfigEntry<int> PlainWit;
        internal static ConfigEntry<string> EdgeWear;
        internal static ConfigEntry<float> GuardBite;
        internal static ConfigEntry<float> GuardCurve;
        internal static ConfigEntry<float> ShieldShare;
        internal static ConfigEntry<float> ArmourShare;
        internal static ConfigEntry<int> Explain;

        internal static int told;
        internal static ConfigEntry<float> SameTier;
        internal static ConfigEntry<string> TypeCarries;

        // Слоты игры: голова 2, грудь 4, пояс 6, ноги 7.
        private const int Head = 2;
        private const int Chest = 4;
        private const int Belt = 6;
        private const int Pants = 7;

        // Зона последнего удара по этому бойцу. Живёт от расчёта износа до расчёта урона —
        // это один и тот же удар, и решать про него дважды нельзя: разойдётся.
        private static readonly Dictionary<UnitAttribute, int> struck =
            new Dictionary<UnitAttribute, int>();

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Pierce", "Enabled", true,
                "Wear the piece that was actually in the way, and let a blow through where that "
                + "piece has failed. Off, and the game's own lottery decides which of the four "
                + "worn things takes the scratch, whatever happened on screen.");

            WearRate = config.Bind("Pierce", "WearRate", 3f,
                new ConfigDescription(
                    "How much faster the struck piece wears than the game wears a random one. "
                    + "The game takes a tenth of a point plus a hundredth of the damage, which "
                    + "on a five-hundred-point plate is four hundred blows. Three makes armour "
                    + "something that is kept up rather than something that outlives its owner.",
                    new AcceptableValueRange<float>(0.5f, 20f)));

            Injuries = config.Bind("Pierce", "Injuries", true,
                "Leave a wound when a heavy blow lands where the armour has failed. The game "
                + "has injuries and a table of them, but calls for one only at death, and only "
                + "for the player's own people — a consolation for having been knocked down "
                + "rather than a record of what the fight did.");

            TierWear = config.Bind("Pierce", "TierWear", "5.06,3.38,2.25,1.5,1,0.67",
                "How fast each tier wears, from T0 to T5, as multipliers of the ordinary rate. "
                + "The game gives every tier the same rule and lets the maker set a bigger "
                + "number of points on better things — so a tier-one sword and a tier-five one "
                + "lose a point at the same speed, and only the size of the bar differs. Here "
                + "the speed differs too, a tier and a half apart at every step: each tier "
                + "gives way half again as fast as the one above it, so a peasant's blade "
                + "notches five times faster than a legendary one holds. Six numbers from T0 "
                + "to T5, separated by commas.");

            TierBlock = config.Bind("Pierce", "TierBlock", 0.33f,
                new ConfigDescription(
                    "How much of a blow good armour turns aside for every tier the weapon is "
                    + "beneath it. At three tenths, a tier-one blade against whole tier-four "
                    + "plate loses nine tenths of what it would otherwise have done — it rings "
                    + "off. Against the same plate broken, or against a gap in it, the same "
                    + "blade does everything it has: the way in is the hole, not the steel.",
                    new AcceptableValueRange<float>(0f, 0.45f)));

            ClassBlock = config.Bind("Pierce", "ClassBlock", "0.35,0.7,1",
                "How much of that answer each kind of armour is actually able to give, for "
                + "light, medium and heavy. Tier alone is not the whole story: a fine leather "
                + "coat and a fine breastplate are the same tier and are not the same problem "
                + "for a knife. Plate answers in full, mail most of the way, leather barely — "
                + "what leather has instead is the game's own resistances, which stay as they "
                + "are. Three numbers, separated by commas.");

            SameTier = config.Bind("Pierce", "SameTier", 0.5f,
                new ConfigDescription(
                    "How much armour still answers a weapon of its own tier, in steps. Half a "
                    + "step: a tier-one sword does get through tier-one plate, just not well. "
                    + "Armour is armour even when it is nobody's pride — what the plate is "
                    + "worse at than its betters is holding, not existing.",
                    new AcceptableValueRange<float>(0f, 2f)));

            TypeCarries = config.Bind("Pierce", "TypeCarries", "0,0.75,0.2",
                "How much of the armour's answer each kind of blow goes around, for cutting, "
                + "crushing and thrusting. An edge is what plate was invented against and it "
                + "gets nothing. A mace does not need to open the steel — the blow arrives "
                + "through it, and what it lands on is a man in a bell. A point finds a little: "
                + "gaps, straps, the eye of a visor. Three numbers, separated by commas.");

            TypeWear = config.Bind("Pierce", "TypeWear", "1,2.5,0.7",
                "How hard each kind of blow uses up the armour it lands on, for cutting, "
                + "crushing and thrusting. A mace ruins plate whether or not it hurts the man "
                + "inside it — that is what it is for; an edge scores it; a point makes a small "
                + "hole and leaves the rest. Three numbers, separated by commas.");

            Explain = config.Bind("Pierce", "Explain", 100000,
                new ConfigDescription(
                    "How many blows to write out in full, once per game: who struck whom, "
                    + "where it landed, what the armour there was worth and what finally "
                    + "arrived. Zero keeps the log quiet. Set it when a fight goes a way that "
                    + "needs explaining — it is the difference between knowing who won and "
                    + "knowing why.",
                    new AcceptableValueRange<int>(0, 100000)));

            EdgeWear = config.Bind("Pierce", "EdgeWear", "5,3,1.6,0.85,0.45,0.24",
                "How fast weapons and shields of each tier give way, from T0 to T5. Armour has "
                + "its own ladder in TierWear, and it should: armour is the thing a man is "
                + "meant to keep up. What is held in the hands is the same steel asked to do "
                + "the opposite job, so it gets a ladder of its own, and a steeper one — each "
                + "tier gives way about twice as slowly as the one under it. A better tier "
                + "also carries more points to spend, so the two together make a thing last "
                + "some three times longer for every step up. Six numbers, separated by "
                + "commas.");

            GuardBite = config.Bind("Pierce", "GuardBite", 100f,
                new ConfigDescription(
                    "The armour reckoning at which a weapon wears at its ordinary speed — "
                    + "about a tier-three mail. Steel answers steel: a blade that spends its "
                    + "life on men in leather stays keen, and the same blade on plate is "
                    + "notched by the third fight. The game never charged a sword anything for "
                    + "cutting, so one that had killed forty men was as sharp as the day it "
                    + "left the forge.",
                    new AcceptableValueRange<float>(1f, 1000f)));

            GuardCurve = config.Bind("Pierce", "GuardCurve", 0.5f,
                new ConfigDescription(
                    "How steeply that answer grows with the armour. A half is the square root. "
                    + "The reckoning runs from a dozen on a shirt to seven hundred on dragon "
                    + "plate — fifty-eight times over — and taken straight that would make a "
                    + "sword good for a single fight. The root folds it into under three, "
                    + "which is a difference a man can feel without it ruling his life.",
                    new AcceptableValueRange<float>(0f, 1f)));

            ShieldShare = config.Bind("Pierce", "ShieldShare", 0.04f,
                new ConfigDescription(
                    "What share of a caught blow a shield pays for catching it. Straight off "
                    + "the damage, with no floor under it and no ceiling over it: the shield "
                    + "took the whole blow, so it pays for the whole blow. The game capped this "
                    + "at ten points, which made a blow of three hundred and a blow of a "
                    + "thousand cost a shield exactly the same.",
                    new AcceptableValueRange<float>(0f, 1f)));

            ArmourShare = config.Bind("Pierce", "ArmourShare", 0.04f,
                new ConfigDescription(
                    "What share of the damage that got through the armour comes off that "
                    + "armour. The same share a shield pays, and for the same reason: a shield "
                    + "pays for what it caught, armour pays for what it failed to catch. What "
                    + "it did catch costs it nothing at all — a shirt nobody put a hole in is "
                    + "still a shirt, and that is what armour is worn for.",
                    new AcceptableValueRange<float>(0f, 1f)));

            MagicRank = config.Bind("Pierce", "MagicRank", 0.2857f,
                new ConfigDescription(
                    "How much every rank of a school above the first adds to what its spells "
                    + "do. The game gives a studied spell a wider list of effects and leaves "
                    + "its damage where it was, so a master and a novice throw the same "
                    + "fireball — which makes studying a school a matter of paperwork. At "
                    + "this figure a school learned to its fifteenth and last rank hits five "
                    + "times as hard as a first lesson — and the road there is priced to match, "
                    + "in the Mastery section.",
                    new AcceptableValueRange<float>(0f, 1f)));

            WitPower = config.Bind("Pierce", "WitPower", 0f,
                new ConfigDescription(
                    "How much every point of the caster's wit above an ordinary head adds to "
                    + "what a spell does, ON TOP of what the game already gives. Kept at zero, "
                    + "and it should stay there: the game does this itself, one percent per "
                    + "point of intelligence, multiplied into the spell at the moment it lands. "
                    + "I read the code wrongly once and nearly paid intelligence twice. It is "
                    + "here only in case that link is ever wanted steeper.",
                    new AcceptableValueRange<float>(0f, 0.1f)));

            PlainWit = config.Bind("Pierce", "PlainWit", 10,
                new ConfigDescription(
                    "The wit of an ordinary head, from which the bonus is counted. Below it "
                    + "nothing is taken away: a fool with a wand is merely unremarkable, not "
                    + "harmless.",
                    new AcceptableValueRange<int>(1, 100)));

            MagicWear = config.Bind("Pierce", "MagicWear", 1f,
                new ConfigDescription(
                    "How hard magic uses up what it touches, against an ordinary blow. A spell "
                    + "that covers ground takes its share out of everything the man is wearing "
                    + "at once, because it did not have to find a way in — it was everywhere. "
                    + "A spell aimed at one man goes through his armour to him: steel is no "
                    + "answer to fire, and the fire ruins the steel on the way.",
                    new AcceptableValueRange<float>(0f, 10f)));

            BluntSwing = config.Bind("Pierce", "BluntSwing", 0.7f,
                new ConfigDescription(
                    "How fast a crushing weapon swings, against its own listed speed. Seven "
                    + "tenths: a mace that goes through plate, ruins it, and swings as quickly "
                    + "as a sword would be the only weapon anyone carried. The weight that "
                    + "makes it work is the weight that makes it slow, and a slow swing is one "
                    + "that can be stepped out of or caught on a shield — which in this game "
                    + "any blow can be.",
                    new AcceptableValueRange<float>(0.2f, 1f)));

            CritGap = config.Bind("Pierce", "CritGap", 0.8f,
                new ConfigDescription(
                    "What share of a critical blow lands where the armour is not: the gap at "
                    + "the elbow, the strap under the arm, the slit of the visor. A crit is "
                    + "not a harder swing, it is a found opening — so most of it arrives on "
                    + "skin no matter how good the plate is, and the rest goes through the "
                    + "steel as usual. Without this a dagger could never kill a knight, which "
                    + "is the one thing daggers are for.",
                    new AcceptableValueRange<float>(0f, 1f)));

            CritFinds = config.Bind("Pierce", "CritFinds", 1f,
                new ConfigDescription(
                    "How much of that answer a critical blow goes around. At one, a crit "
                    + "ignores it entirely: against plate a poor weapon has no honest way "
                    + "through, and what is left to it is the lucky blow — the visor, the "
                    + "armpit, the strap. That is the whole difference between grinding at a "
                    + "knight with a rusty sword and actually killing him.",
                    new AcceptableValueRange<float>(0f, 1f)));

            TierPierce = config.Bind("Pierce", "TierPierce", 0.2f,
                new ConfigDescription(
                    "How much better a blow bites for every tier the weapon is above the armour "
                    + "it meets. A legendary blade against a peasant's coat is not merely "
                    + "sharper — the coat stops being an answer to it.",
                    new AcceptableValueRange<float>(0f, 1f)));

            WeaponDecay = config.Bind("Pierce", "WeaponDecay", true,
                "Let a worn weapon strike for less, point for point, the way worn armour keeps "
                + "out less. A blade at three fifths of its durability lands three fifths of "
                + "its damage. The game itself only notices at a quarter and at nothing; "
                + "between those it treats a notched sword as a new one.");

            Breaks = config.Bind("Pierce", "Breaks",
                "head=SkullFracture|Knockout;"
                + "chest=RibFracture|ArmFractureLeft|ArmFractureRight;"
                + "pants=LegFractureLeft|LegFractureRight",
                "Which wounds each place can leave. A blow to the head cracks the skull or puts "
                + "a man out; one to the body breaks ribs, or the arm he raised to catch it; one "
                + "to the legs breaks a leg. "
                + "Before this the wound was drawn at random from everything the man did not "
                + "already have, so a blow to the shin broke his skull. The place was known all "
                + "along — it decides the armour and the damage — and there was no reason not "
                + "to ask it. "
                + "A name the game does not know is skipped; if a place has nothing left to "
                + "give, no wound is left.");

            InjuryShare = config.Bind("Pierce", "InjuryShare", 0.15f,
                new ConfigDescription(
                    "How big a blow has to be to leave a wound, as a share of the victim's whole "
                    + "health. At fifteen hundredths, a wound is the mark of a bad blow rather "
                    + "than of any blow at all.",
                    new AcceptableValueRange<float>(0.01f, 1f)));
        }

        /// <summary>Which worn piece stood in the way of this blow.</summary>
        internal static int Zone(Attack attack, UnitAttribute victim)
        {
            // Откуда пришёл удар. У ближнего боя это оружие в замахе, у стрелы и заклинания —
            // точка, из которой они вышли.
            Vector3 from;
            if (!Blow(attack, victim, out from)) return Chest;

            // Игра знает, где у бойца голова, грудь, пояс и ноги: это настоящие узлы модели
            // со своими коллайдерами, она собирает их из костей и держит списком. В бою она
            // ими не пользуется — удар считается по бойцу целиком, — но спросить их можно.
            // Спрашиваем: какая часть ближе всего к тому месту, откуда пришёл удар, ту он и
            // нашёл. Это уже не прикидка по высоте, а геометрия.
            int nearest = Chest;
            float best = float.MaxValue;

            Look(victim, BodyPart.head, Head, from, ref nearest, ref best);
            Look(victim, BodyPart.chest, Chest, from, ref nearest, ref best);
            // Пояс доспехом быть перестал, а место под ним осталось: удар в живот изводит
            // то, что там и надето, — юбку кирасы или верх штанов.
            Look(victim, BodyPart.waist, Pants, from, ref nearest, ref best);
            Look(victim, BodyPart.footL, Pants, from, ref nearest, ref best);
            Look(victim, BodyPart.footR, Pants, from, ref nearest, ref best);

            // Руки прикрыты тем же, чем корпус: отдельного наплечника в слотах нет.
            Look(victim, BodyPart.handL, Chest, from, ref nearest, ref best);
            Look(victim, BodyPart.handR, Chest, from, ref nearest, ref best);

            return nearest;
        }

        /// <summary>Keeps the nearest body part seen so far.</summary>
        private static void Look(UnitAttribute victim, BodyPart part, int slot, Vector3 from,
            ref int nearest, ref float best)
        {
            Transform node = victim.GetBodyPart(part);
            if (node == null) return;

            float apart = Vector3.Distance(node.position, from);
            if (apart >= best) return;

            best = apart;
            nearest = slot;
        }

        /// <summary>Where the blow came from, as well as the game knows it.</summary>
        private static bool Blow(Attack attack, UnitAttribute victim, out Vector3 from)
        {
            from = victim.transform.position;
            if (attack == null) return false;

            if (!float.IsInfinity(attack.damagerPosition.x))
            {
                from = attack.damagerPosition;
                return true;
            }

            if (attack.damagerTrans != null)
            {
                from = attack.damagerTrans.position;
                return true;
            }

            if (attack.attacker != null)
            {
                from = attack.attacker.transform.position + Vector3.up * 1.2f;
                return true;
            }

            return false;
        }

        /// <summary>How much of the piece covering that zone is left, from none to all.</summary>
        internal static float Cover(HumaniodUnit body, int slot)
        {
            Inventory worn = Worn(body, slot);
            if (worn == null) return 0f;

            return Share(worn);
        }

        /// <summary>What share of a thing is left, by its own durability.</summary>
        internal static float Share(Inventory thing)
        {
            if (thing == null || thing.itemInfo == null) return 1f;
            if (thing.durability < 0f || thing.itemInfo.noDurability) return 1f;

            float whole = thing.itemInfo.durability;
            if (whole <= 0f) return 1f;

            return Mathf.Clamp01(thing.durability / whole);
        }

        /// <summary>The thing worn in that slot, if anything is.</summary>
        internal static Inventory Worn(HumaniodUnit body, int slot)
        {
            if (body == null || body.equipmentmanger == null) return null;

            EquipInfo[] slots = body.equipmentmanger.equipInfos;
            if (slots == null || slot < 0 || slot >= slots.Length) return null;

            EquipInfo worn = slots[slot];
            if (worn == null || !worn.IsEquiped()) return null;
            if (worn.inventory == null || worn.inventory.itemInfo == null) return null;

            return worn.inventory;
        }

        /// <summary>Remembers where this blow landed, and wears what was there.</summary>
        internal static void Land(HumaniodUnit body, Attack attack)
        {
            if (body == null || attack == null) return;

            UnitAttribute victim = (UnitAttribute)(object)body;

            int slot = Zone(attack, victim);
            struck[victim] = slot;

            EquipInfo[] slots = body.equipmentmanger != null
                ? body.equipmentmanger.equipInfos : null;

            EquipInfo worn = (slots != null && slot >= 0 && slot < slots.Length)
                ? slots[slot] : null;

            bool covered = worn != null && worn.IsEquiped() && worn.inventory != null
                && worn.inventory.itemInfo != null && !worn.inventory.itemInfo.noDurability;

            // Заклинание не пробивает доспех — оно его минует. Доспеху от него ничего.
            if (Spell(attack)) return;

            // Износ по одному правилу: доспех — тем, что прошло сквозь само его железо,
            // клинок — той бронёй, что он пробил. Сколько чего было, записал расчёт брони,
            // пока считал этот же удар. Щель в латах доспеха не портит, дробящее портит.
            Inventory plate = Anatomy.WornKit(victim, Anatomy.Spot(attack, victim));

            Tatter.Struck(victim, attack, plate);

            if (told < Explain.Value && covered)
            {
                Chronicle.Say($"  износ: {Where(slot)} {Thing(worn.inventory)}");
            }
        }

        // Сколько урона дошло до тела. Пишется в конце расчёта брони, читается здесь: игра
        // зовёт TakeDamage прежде onGetHit, так что к этой минуте ответ уже известен.
        private static readonly Dictionary<UnitAttribute, float> arrived =
            new Dictionary<UnitAttribute, float>();

        internal static void Arrived(UnitAttribute who, float much)
        {
            if (who != null) arrived[who] = much;
        }

        private static float Arrived(UnitAttribute who)
        {
            float much;
            if (who == null || !arrived.TryGetValue(who, out much)) return 0f;

            // Один удар — один ответ. Иначе следующий, пришедший мимо расчёта, взял бы
            // прошлое число.
            arrived.Remove(who);
            return much;
        }

        /// <summary>How much harder a weapon is used up by the armour it landed on.</summary>
        private static float Guarded(Inventory worn, int slot)
        {
            if (worn == null) return 1f;

            try
            {
                UIArmorInfo coat = worn.itemInfo as UIArmorInfo;
                if (coat == null) return 1f;

                int state = worn.DurState;
                float wear = state >= 3 ? 0f : (state >= 1 ? 1f - 0.25f * state : 1f);

                float guard = Breach.Wall(coat, wear, slot, Temper.Worth(worn));
                if (guard <= 0f) return 1f;

                return Mathf.Pow(1f + guard / Mathf.Max(1f, GuardBite.Value), GuardCurve.Value);
            }
            catch
            {
                return 1f;
            }
        }

        /// <summary>A unit by the name the player sees.</summary>
        internal static string Named(UnitAttribute who)
        {
            if (who == null || who.Data == null) return "никто";
            return "«" + who.Data.unitname + "»";
        }

        /// <summary>A thing by its name, tier, kind and how much of it is left.</summary>
        internal static string Thing(Inventory what)
        {
            if (what == null || what.itemInfo == null) return "нет";

            UIArmorInfo armour = what.itemInfo as UIArmorInfo;
            string kind = armour != null ? " " + armour.armourType : "";

            return $"«{what.itemInfo.Name}» {what.itemInfo.tier}{kind} "
                + $"{Share(what) * 100f:0}%";
        }

        /// <summary>The name of a slot, as a person would say it.</summary>
        internal static string Where(int slot)
        {
            if (slot == Head) return "голова";
            if (slot == Chest) return "корпус";
            if (slot == Belt) return "пояс";
            if (slot == Pants) return "ноги";
            return "слот " + slot;
        }

        /// <summary>A blow broken into the kinds of harm it carries.</summary>
        internal static string Split(DamageBase blow)
        {
            if (blow == null) return "0";

            string[] called = new string[]
            {
                "реж", "дроб", "кол", "огонь", "холод", "ток", "яд", "свет", "тьма"
            };

            List<string> parts = new List<string>();
            float all = 0f;

            foreach (KeyValuePair<DamageType, Damage> one in blow)
            {
                float much = one.Value.currentDamage;
                if (much <= 0f) continue;

                all += much;

                int type = (int)one.Key;
                string name = (type >= 0 && type < called.Length) ? called[type] : one.Key.ToString();
                parts.Add($"{name} {much:0.#}");
            }

            if (parts.Count == 0) return "0";
            return $"{all:0.#} ({string.Join(", ", parts.ToArray())})";
        }

        /// <summary>
        /// Uses up the weapon that landed the blow.
        ///
        /// Оружие изнашивается всегда, пробило оно или нет: оно било по стали, и сталь
        /// ответила. Чем толще то, во что бьёшь, тем дороже обходится удар.
        /// </summary>
        internal static void Bite(Attack attack, float guarded)
        {
            if (attack == null || attack.damage == null) return;

            Inventory blade = Held(attack.weapon);
            if (blade == null) return;

            float much = (0.1f + attack.damage.Damage() * 0.01f) * guarded * Tears(attack);

            Hands(blade, much);

            if (told < Explain.Value)
            {
                Chronicle.Say($"  износ оружия: {Thing(blade)}"
                    + $" (снято {much * Edge(blade):0.#}, броня ×{guarded:0.00})");
            }
        }

        /// <summary>Uses up whatever caught the blow.</summary>
        internal static void Caught(HumaniodUnit body, Attack attack)
        {
            if (body == null || attack == null || ShieldShare.Value <= 0f) return;
            if (body.equipmentmanger == null || attack.damage == null) return;

            EquipInfo[] slots = body.equipmentmanger.equipInfos;
            if (slots == null || slots.Length < 2) return;

            // Щитом, если он есть, иначе тем, чем отбили — клинком в правой руке.
            EquipInfo held = (slots[1] != null && slots[1].IsEquiped()) ? slots[1] : slots[0];
            if (held == null || !held.IsEquiped() || held.inventory == null) return;

            // Щит платит весом того, что в него ударило: палица сминает его быстрее ножа.
            Tatter.Caught(held.inventory, attack);

            if (told < Explain.Value)
            {
                Chronicle.Say($"  износ щита: {Thing(held.inventory)}");
            }
        }

        /// <summary>Wears what a spell touched, since the game never asks us about spells.</summary>
        internal static int Sorcery(HumaniodUnit body, UnitAttribute victim, Attack attack)
        {
            // Обработчик попадания игра зовёт только для оружия. Заклинания проходят мимо
            // него целиком — оттого после чужой магии ни одна вещь не теряла ни очка, сколько
            // бы раз по ней ни ударило. Здесь мы в расчёте урона, и сюда магия приходит.
            int slot = Zone(attack, victim);
            struck[victim] = slot;

            // Доспех заклинанием не пробит — ему от него ничего не делается.
            return slot;
        }

        /// <summary>How much this spell gains from the head that cast it.</summary>
        internal static float Wit(Attack attack)
        {
            if (attack == null || attack.attacker == null) return 1f;
            if (WitPower.Value <= 0f) return 1f;

            NPCSaveData mind = attack.attacker.Data as NPCSaveData;
            if (mind == null) return 1f;

            int over = mind.intelligence - PlainWit.Value;
            if (over <= 0) return 1f;

            return 1f + WitPower.Value * over;
        }

        /// <summary>How much this spell gains from the study behind it.</summary>
        internal static float Study(Attack attack)
        {
            if (attack == null || attack.bindSpell == null) return 1f;
            if (MagicRank.Value <= 0f) return 1f;

            int rank = Mathf.Max(1, attack.bindSpell.level);
            return 1f + MagicRank.Value * (rank - 1);
        }

        /// <summary>True when this came from a spell rather than from a hand.</summary>
        internal static bool Magic(Attack attack)
        {
            return Spell(attack);
        }

        /// <summary>
        /// True for a spell and nothing else.
        ///
        /// У игры всякий удар без оружия по умолчанию записан магическим: капкан, шипы,
        /// кровотечение, падение. Прежде мы верили этой записи, и капкан бил в латы как
        /// заклинание — мимо брони, в полную силу. Заклинание — это то, за чем стоит само
        /// заклинание.
        /// </summary>
        internal static bool Spell(Attack attack)
        {
            if (attack == null || attack.attackType != AttackType.magic) return false;
            return attack.bindSpell != null && attack.bindSpell.theSpell != null;
        }

        /// <summary>
        /// True for what comes from inside: bleeding, poison, burning in the blood.
        ///
        /// Такой удар игра наносит «от самого раненого» — место, откуда он пришёл, это сам
        /// раненый. Доспеху тут отвечать нечем: рана уже есть.
        /// </summary>
        internal static bool Inner(Attack attack, UnitAttribute victim)
        {
            if (attack == null || victim == null || attack.weapon != null) return false;
            return attack.damagerTrans != null && attack.damagerTrans == victim.transform;
        }

        /// <summary>Takes its share out of everything the man is wearing.</summary>
        private static void Spread(HumaniodUnit body, float much)
        {
            if (body == null || body.equipmentmanger == null) return;

            // Голова, корпус, пояс, ноги, оружие, вторая рука, шея. Кольца и реликвия — нет:
            // это не то, что стоит между человеком и заклинанием, и портиться им не от чего.
            // Прежде здесь были только четыре слота брони, и сгоревший в чёрном тумане выходил
            // с пробитой кирасой и нетронутым мечом.
            int[] hit = new int[] { Head, Chest, Belt, Pants, 0, 1, 3 };

            foreach (int which in hit)
            {
                Inventory worn = Worn(body, which);
                if (worn == null || worn.itemInfo == null || worn.itemInfo.noDurability) continue;

                Wear(worn, much);
            }
        }

        /// <summary>Takes durability off, going around the easy-mode door.</summary>
        internal static void Wear(Inventory worn, float much)
        {
            if (worn == null || worn.itemInfo == null) return;
            if (worn.durability < 0f || worn.itemInfo.noDurability) return;

            worn.durability = Mathf.Max(0f, worn.durability - much * Rate(worn));
        }

        /// <summary>Takes durability off a thing held in the hands, by its own ladder.</summary>
        internal static void Hands(Inventory thing, float much)
        {
            if (thing == null || thing.itemInfo == null) return;
            if (thing.durability < 0f || thing.itemInfo.noDurability) return;

            thing.durability = Mathf.Max(0f, thing.durability - much * Edge(thing));
        }

        /// <summary>How fast a weapon or shield of this tier gives way.</summary>
        internal static float Edge(Inventory thing)
        {
            if (thing == null || thing.itemInfo == null) return 1f;

            float[] scale = Edges();
            int tier = (int)thing.itemInfo.tier;

            if (tier < 0 || tier >= scale.Length) return 1f;
            return scale[tier];
        }

        private static float[] edges;
        private static string edgesRead;

        private static float[] Edges()
        {
            string written = EdgeWear.Value ?? "";
            if (edges != null && written == edgesRead) return edges;

            List<float> got = new List<float>();

            foreach (string one in written.Split(','))
            {
                float much;
                if (float.TryParse(one.Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out much))
                {
                    got.Add(Mathf.Clamp(much, 0.01f, 50f));
                }
            }

            while (got.Count < 6) got.Add(1f);

            edgesRead = written;
            edges = got.ToArray();
            return edges;
        }

        /// <summary>How fast a thing of this tier gives way.</summary>
        internal static float Rate(Inventory thing)
        {
            if (thing == null || thing.itemInfo == null) return 1f;

            float[] scale = Scale();
            int tier = (int)thing.itemInfo.tier;

            if (tier < 0 || tier >= scale.Length) return 1f;
            return scale[tier];
        }

        private static float[] rates;
        private static string read;

        /// <summary>Reads the six numbers once, and again when they change.</summary>
        private static float[] Scale()
        {
            string written = TierWear.Value ?? "";
            if (rates != null && written == read) return rates;

            List<float> got = new List<float>();

            foreach (string one in written.Split(','))
            {
                float much;
                if (float.TryParse(one.Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out much))
                {
                    got.Add(Mathf.Clamp(much, 0.01f, 50f));
                }
            }

            while (got.Count < 6) got.Add(1f);

            read = written;
            rates = got.ToArray();
            return rates;
        }

        /// <summary>The thing a weapon in somebody's hand was made from.</summary>
        internal static Inventory Held(Weapon held)
        {
            if (held == null || held.owner == null) return null;

            HumaniodUnit body = held.owner as HumaniodUnit;
            if (body == null || body.equipmentmanger == null) return null;

            EquipInfo[] slots = body.equipmentmanger.equipInfos;
            if (slots == null || held.index < 0 || held.index >= slots.Length) return null;

            EquipInfo hand = slots[held.index];
            if (hand == null || !hand.IsEquiped() || hand.inventory == null) return null;

            return hand.inventory;
        }

        /// <summary>How much of a blow the covered part of the armour turns aside.</summary>
        internal static float Answer(Inventory blade, Inventory plate, bool crit)
        {
            if (plate == null || plate.itemInfo == null) return 0f;

            // Оружия нет — заклинание, когти, кулак. Ярусами их не меряем.
            if (blade == null || blade.itemInfo == null) return 0f;

            int step = (int)plate.itemInfo.tier - (int)blade.itemInfo.tier;

            // Оружие ниже доспеха: сталь отвечает. Каждая ступень вниз — своя доля удара,
            // которая просто не входит. Отсюда и весь смысл ярусов: T1 по целой T4 звенит,
            // и добраться до тела можно только через дыру или через сломанное.
            //
            // Но отвечает не всякий доспех одинаково. Ярус говорит, насколько вещь хороша,
            // и молчит о том, из чего она: тонкая кожа и кираса бывают одного яруса и не
            // бывают одной задачей для ножа. Латы отвечают в полную силу, кольчуга почти,
            // кожа едва — ей взамен остаются родные сопротивления игры.
            // Даже своему ярусу доспех отвечает: полшага по умолчанию. Т1 меч по Т1 латам
            // проходит, но плохо — латы остаются латами, просто не лучшими из них.
            float steps = (float)step + SameTier.Value;

            if (steps > 0f)
            {
                float held = Mathf.Clamp(TierBlock.Value * steps * Kind(plate), 0f, 0.98f);

                // Удачный удар обходит сталь там, где честного пути нет: забрало, подмышка,
                // ремень. Ржавым мечом рыцаря в латах можно убить — но только так, и это и
                // есть разница между «долблю и ничего» и «попал».
                if (crit) held *= 1f - CritFinds.Value;

                return held;
            }

            // Оружие выше доспеха: он перестаёт быть ответом. Отрицательная доля — прибавка.
            if (step < 0) return -Mathf.Clamp(TierPierce.Value * -step, 0f, 3f);

            return 0f;
        }

        /// <summary>How hard this blow uses up whatever it lands on.</summary>
        internal static float Tears(Attack attack)
        {
            if (attack == null || attack.damage == null) return 1f;

            float[] scale = Wears();
            float most = 0f;
            int kind = 0;

            // Род удара берём по тому, чего в нём больше: булава с примесью огня остаётся
            // булавой, а доспеху от неё достаётся именно как от булавы.
            for (int type = 0; type <= 2; type++)
            {
                DamageType which = (DamageType)type;
                if (!attack.damage.ContainsKey(which)) continue;

                float much = attack.damage[which].currentDamage;
                if (much > most) { most = much; kind = type; }
            }

            if (most <= 0f || kind >= scale.Length) return 1f;
            return scale[kind];
        }

        private static float[] wears;
        private static string wearsRead;

        private static float[] Wears()
        {
            string written = TypeWear.Value ?? "";
            if (wears != null && written == wearsRead) return wears;

            List<float> got = new List<float>();

            foreach (string one in written.Split(','))
            {
                float much;
                if (float.TryParse(one.Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out much))
                {
                    got.Add(Mathf.Clamp(much, 0f, 20f));
                }
            }

            while (got.Count < 3) got.Add(1f);

            wearsRead = written;
            wears = got.ToArray();
            return wears;
        }

        /// <summary>How much of the armour's answer a blow of this kind goes around.</summary>
        internal static float Carries(int type)
        {
            float[] scale = Types();
            if (type < 0 || type >= scale.Length) return 0f;
            return scale[type];
        }

        private static float[] types;
        private static string typesRead;

        private static float[] Types()
        {
            string written = TypeCarries.Value ?? "";
            if (types != null && written == typesRead) return types;

            List<float> got = new List<float>();

            foreach (string one in written.Split(','))
            {
                float much;
                if (float.TryParse(one.Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out much))
                {
                    got.Add(Mathf.Clamp01(much));
                }
            }

            while (got.Count < 3) got.Add(0f);

            typesRead = written;
            types = got.ToArray();
            return types;
        }

        /// <summary>How much of the tier answer this kind of armour can actually give.</summary>
        internal static float Kind(Inventory plate)
        {
            UIArmorInfo made = plate != null ? plate.itemInfo as UIArmorInfo : null;
            if (made == null) return 1f;

            float[] scale = Kinds();
            int which = (int)made.armourType - 1;

            if (which < 0 || which >= scale.Length) return 1f;
            return scale[which];
        }

        private static float[] kinds;
        private static string kindsRead;

        private static float[] Kinds()
        {
            string written = ClassBlock.Value ?? "";
            if (kinds != null && written == kindsRead) return kinds;

            List<float> got = new List<float>();

            foreach (string one in written.Split(','))
            {
                float much;
                if (float.TryParse(one.Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out much))
                {
                    got.Add(Mathf.Clamp01(much));
                }
            }

            while (got.Count < 3) got.Add(1f);

            kindsRead = written;
            kinds = got.ToArray();
            return kinds;
        }

        /// <summary>The zone the last blow on this unit found, or the chest if none recorded.</summary>
        internal static int Last(UnitAttribute victim)
        {
            int slot;
            return struck.TryGetValue(victim, out slot) ? slot : Chest;
        }

        private static readonly Dictionary<int, List<string>> breaks =
            new Dictionary<int, List<string>>();
        private static string breaksRead;

        /// <summary>
        /// Что ломается в этом месте.
        ///
        /// Место удара известно всегда — по нему считается и броня, и урон, — и выбирать
        /// перелом наугад при этом было нелепо: удар в голень крошил череп.
        /// </summary>
        private static List<string> Breaking(int spot)
        {
            string written = Breaks.Value ?? "";

            if (written != breaksRead)
            {
                breaksRead = written;
                breaks.Clear();

                foreach (string one in written.Split(';'))
                {
                    string[] halves = one.Split('=');
                    if (halves.Length != 2) continue;

                    int where = Anatomy.Where(halves[0].Trim());
                    if (where < 0) continue;

                    List<string> kinds = new List<string>();
                    foreach (string name in halves[1].Split('|'))
                    {
                        string bare = name.Trim();
                        if (bare.Length > 0) kinds.Add(bare);
                    }

                    if (kinds.Count > 0) breaks[where] = kinds;
                }
            }

            List<string> got;
            return breaks.TryGetValue(spot, out got) ? got : null;
        }

        /// <summary>Leaves a wound of the kind the game already keeps a table of.</summary>
        internal static void Wound(UnitAttribute victim, UnitAttribute by)
        {
            if (!Injuries.Value || victim == null || victim.buffmanger == null) return;

            // Кого игра держит неуязвимым, того и ранить нельзя: замок на здоровье ставят
            // там, где герой обязан дожить до конца сцены, а сцена с переломом черепа посреди
            // неё — это та же смерть, только медленнее.
            if (victim.Data != null && ((CharacterSaveData)(object)victim.Data).hpLock) return;

            try
            {
                UIBuffDatabase db = UIBuffDatabase.Instance;
                if (db == null || db.injuryBuffs == null || db.injuryBuffs.Count == 0) return;

                // Что может сломаться в том месте, куда пришлось.
                List<string> may = Breaking(Last(victim));

                List<UIBuffInfo> fresh = new List<UIBuffInfo>();

                foreach (UIBuffInfo hurt in db.injuryBuffs)
                {
                    if (hurt == null || victim.buffmanger.ContainBuff(hurt.id)) continue;
                    if (may != null && may.Count > 0 && !may.Contains(hurt.id)) continue;
                    fresh.Add(hurt);
                }

                if (fresh.Count == 0) return;

                UIBuffInfo got = fresh[UnityEngine.Random.Range(0, fresh.Count)];
                victim.buffmanger.AddBuff(new BuffBase(got, by ?? victim));

                ItemForgePlugin.Log.LogInfo($"«{victim.Data.unitname}» получает травму: {got.id}.");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог оставить травму: " + e);
            }
        }

        /// <summary>Forgets the dead, so the table does not grow for ever.</summary>
        internal static void Forget(UnitAttribute gone)
        {
            if (gone != null) struck.Remove(gone);
        }
    }

    // Уворот и блок решаются до всякого урона, и без них разбор боя неполон: «дошло ноль»
    // и «не дошло, потому что увернулся» — разные вещи, а в логе они выглядят одинаково.
    [HarmonyPatch(typeof(UnitAttribute), "Dodgecheck")]
    internal static class Dodge_Explain_Patch
    {
        private static void Postfix(UnitAttribute __instance, Attack t_attack, bool __result)
        {
            if (!__result || Pierce.told >= Pierce.Explain.Value) return;

            Pierce.told++;
            Chronicle.Say("Уворот: " + Pierce.Named(__instance)
                + " ушёл от " + Pierce.Named(t_attack != null ? t_attack.attacker : null));
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "Blockcheck")]
    internal static class Block_Explain_Patch
    {
        private static void Postfix(UnitAttribute __instance, Attack t_attack, bool __result)
        {
            if (!__result) return;

            // Пойманный удар изнашивает то, чем его поймали. Родной путь игры сюда тоже ведёт,
            // но он проходит через CostDurability, а тот, судя по щитам на полной шкале после
            // боя, до вещей не доходит. Пишем сами, как и во всём остальном.
            Pierce.Caught(__instance as HumaniodUnit, t_attack);

            if (Pierce.told >= Pierce.Explain.Value) return;

            Pierce.told++;
            Chronicle.Say("Блок: " + Pierce.Named(__instance)
                + " принял удар " + Pierce.Named(t_attack != null ? t_attack.attacker : null));
        }
    }

    // Вес, которым дробящее берёт сталь, — тот же вес, которым оно медленно. Иначе булава
    // выходит оружием без недостатка: и сквозь латы бьёт, и доспех рушит, и машет как меч.
    [HarmonyPatch(typeof(Weapon), "SetWeapon")]
    internal static class SetWeapon_Swing_Patch
    {
        private static void Postfix(Weapon __instance, Inventory weapon)
        {
            if (!Pierce.Enabled.Value || Pierce.BluntSwing.Value >= 1f) return;

            try
            {
                UIWeaponInfo made = weapon != null ? weapon.itemInfo as UIWeaponInfo : null;
                if (made == null || made.damage == null) return;

                float cut = made.damage.ContainsKey(DamageType.sharp)
                    ? made.damage[DamageType.sharp].currentDamage : 0f;
                float crush = made.damage.ContainsKey(DamageType.blunt)
                    ? made.damage[DamageType.blunt].currentDamage : 0f;
                float stab = made.damage.ContainsKey(DamageType.stab)
                    ? made.damage[DamageType.stab].currentDamage : 0f;

                if (crush <= cut || crush <= stab) return;

                __instance.BSattackSpeed *= Pierce.BluntSwing.Value;
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог утяжелить замах: " + e);
            }
        }
    }

    // Блок щитом, выстрел из лука, любой другой износ внутри игры — через эту дверь. Правим
    // не сам расчёт, а величину: пусть игра делает своё, но ярус вещи решает, во что ей
    // обходится удар.
    [HarmonyPatch(typeof(Inventory), "CostDurability")]
    internal static class CostDurability_Tier_Patch
    {
        private static bool Prefix(Inventory __instance, ref float value)
        {
            // Идёт родная порча за факт смерти — пропускаем её мимо вещи целиком.
            if (Keeping.dying) return false;

            if (!Pierce.Enabled.Value) return true;

            // Лук на выстреле и колчан на стреле — тоже руки, и лестница у них своя.
            bool hands = __instance != null && __instance.itemInfo is UIWeaponInfo;

            value *= hands ? Pierce.Edge(__instance) : Pierce.Rate(__instance);
            return true;
        }
    }

    // Родной износ оружия и щитов выключаем целиком. Он не заменён нашим, а лежал под ним:
    // удар снимал дважды — раз по нашему счёту, раз по игровому, вслепую и мимо всякой брони.
    [HarmonyPatch(typeof(EquipmentManager), "OnHitTarget")]
    internal static class OnHitTarget_Silence_Patch
    {
        private static bool Prefix() { return !Pierce.Enabled.Value; }
    }

    [HarmonyPatch(typeof(EquipmentManager), "OnBeBlocked")]
    internal static class OnBeBlocked_Silence_Patch
    {
        private static bool Prefix() { return !Pierce.Enabled.Value; }
    }

    [HarmonyPatch(typeof(EquipmentManager), "OnBlock")]
    internal static class OnBlock_Silence_Patch
    {
        private static bool Prefix() { return !Pierce.Enabled.Value; }
    }

    // Зверю бить некуда мимо: брони на нём нет, а шкура — не вещь и не изнашивается. Но
    // клинок об него тупится, как о голое тело человека, и охота должна доводить до кузни
    // не реже войны. Людей здесь не трогаем: у них этим занят Land.
    [HarmonyPatch(typeof(UnitAttribute), "TakeDamage")]
    internal static class TakeDamage_Bite_Patch
    {
        private static void Postfix(UnitAttribute __instance, Attack attack, bool __result)
        {
            if (!Pierce.Enabled.Value || !__result) return;
            if (__instance == null || __instance is HumaniodUnit) return;

            try
            {
                // Клинок о зверя тупится шкурой, которую пробил: на десятую её долю.
                if (!Pierce.Magic(attack)) Tatter.Struck(__instance, attack, null);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог ступить оружие о зверя: " + e);
            }
        }
    }

    // Последним в очереди, когда все прочие постфиксы уже сказали своё: запоминаем, сколько
    // урона дошло до тела. Износ брони считается ровно по этому числу.
    [HarmonyPatch(typeof(UnitAttribute), "DamageReduce")]
    [HarmonyPriority(Priority.Last)]
    internal static class DamageReduce_Arrived_Patch
    {
        private static void Postfix(UnitAttribute __instance, DamageBase __result)
        {
            if (!Pierce.Enabled.Value) return;

            Pierce.Arrived(__instance, __result != null ? __result.Damage() : 0f);
        }
    }

    // Родной розыгрыш зоны заменяем своим целиком: смысл в том, чтобы изнашивалась та вещь,
    // что стояла на пути, а не та, которую вытянул жребий.
    [HarmonyPatch(typeof(EquipmentManager), "OnGetHit")]
    internal static class OnGetHit_Zone_Patch
    {
        private static bool Prefix(EquipmentManager __instance, Attack attack)
        {
            if (!Pierce.Enabled.Value) return true;

            try
            {
                Pierce.Land(__instance.unit, attack);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог разнести удар по зонам: " + e);
                return true;
            }

            return false;
        }
    }

    // Урон: что делает с ударом сталь, по которой бьют, и сталь, которой бьют.
    //
    // Прежде здесь жило ещё одно правило брони, старше нынешнего: сколько процентов
    // прочности доспех потерял, столько процентов урона проходило сквозь него чистыми, мимо
    // всякого пробития, — и крит открывал ещё четыре пятых поверх того, что уже открыл расчёт
    // брони. Латы на половине прочности пропускали половину каждого удара ножом. Теперь
    // броню считает один расчёт (Breach), а потрёпанность входит в него самой стеной.
    //
    // Заклинанием здесь считается только то, за чем стоит заклинание: капкан и шипы игра
    // пишет «магией» по умолчанию, и прежде они били в латы мимо брони.
    [HarmonyPatch(typeof(UnitAttribute), "DamageReduce")]
    [HarmonyPriority(Priority.Low)]
    internal static class DamageReduce_Pierce_Patch
    {
        private static void Postfix(UnitAttribute __instance, Attack attack, ref DamageBase __result)
        {
            if (!Pierce.Enabled.Value || attack == null || __result == null) return;

            try
            {
                HumaniodUnit body = __instance as HumaniodUnit;

                // Клинок бьёт на столько, на сколько он цел: до семидесяти процентов прочности
                // в полную силу, ниже — на процент меньше за каждый потерянный процент.
                if (Pierce.WeaponDecay.Value && attack.weapon != null)
                {
                    float sharp = Tatter.Holds(Pierce.Held(attack.weapon));
                    if (sharp < 1f) __result *= sharp;
                }

                if (body == null) return;

                bool spell = Pierce.Spell(attack);
                bool inner = !spell && Pierce.Inner(attack, __instance);

                int slot = spell
                    ? Pierce.Sorcery(body, __instance, attack)
                    : Pierce.Last(__instance);

                Inventory plate = Pierce.Worn(body, slot);
                Inventory blade = Pierce.Held(attack.weapon);

                // Ответ стали за разницу в ярусах — только оружию по доспеху.
                float answer = (spell || inner) ? 0f : Pierce.Answer(blade, plate, attack.isCrit);

                if (!spell && !inner && answer == 0f) return;

                float through = 0f;
                int last = (spell || inner) ? 8 : 2;

                for (int type = 0; type <= last; type++)
                {
                    DamageType which = (DamageType)type;
                    if (!attack.damage.ContainsKey(which)) continue;

                    float whole = attack.damage[which].currentDamage;
                    if (whole <= 0f) continue;

                    // Что дошло после брони и сопротивлений — как посчитали до нас.
                    float kept = __result.ContainsKey(which) ? __result[which].currentDamage : 0f;
                    float final;

                    if (spell)
                    {
                        // Изученная школа бьёт сильнее. Сталь заклинанию не ответ: телесная
                        // часть идёт как есть; стихию режут сопротивления, как в самой игре.
                        float study = Pierce.Study(attack) * Pierce.Wit(attack);
                        final = (type <= 2 ? whole : kept) * study;

                        // Магию сперва пьёт оберег: сколько в нём осталось, столько и возьмёт.
                        if (type >= 3) final = Ward.Drink(body, final);
                    }
                    else if (inner)
                    {
                        // Рана уже есть, и кровь из неё доспех не держит. Яд и жар в крови
                        // встречают сопротивления, как в игре.
                        final = type <= 2 ? whole : kept;
                    }
                    else
                    {
                        // Ответ стали за ярусы. Отрицательный — прибавка: оружие выше доспеха.
                        float mine = answer > 0f ? answer * (1f - Pierce.Carries(type)) : answer;
                        final = kept * (1f - mine);
                        if (final < 0f) final = 0f;
                    }

                    __result[which] = new Damage(which, final);
                    if (final > kept) through += final - kept;
                }

                if (Pierce.told < Pierce.Explain.Value)
                {
                    Pierce.told++;

                    Chronicle.Say("Удар: "
                        + Pierce.Named(attack.attacker) + " → " + Pierce.Named(__instance)
                        + $" | {Pierce.Where(slot)} {Pierce.Thing(plate)}"
                        + $" | оружие {Pierce.Thing(blade)}"
                        + $" | ответ {answer:0.00}"
                        + (attack.isCrit ? ", КРИТ" : "")
                        + (spell ? (attack.attackArea ? ", магия по площади" : ", магия точечная") : "")
                        + (inner ? ", изнутри" : "")
                        + $" | урон {Pierce.Split(attack.damage)}"
                        + $" → {__result.Damage():0.#}"
                        + $" | здоровья было {((CharacterSaveData)(object)__instance.Data).currenthp:0}"
                        + $" из {__instance.maxhp:0}");
                }

                if (through <= 0f) return;

                // Травма — за то, что дошло до тела сверх расчёта брони.
                float ceiling = __instance.maxhp > 0f ? __instance.maxhp : 100f;
                if (through >= ceiling * Pierce.InjuryShare.Value)
                {
                    Pierce.Wound(__instance, attack.attacker);
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог провести удар в пробитое место: " + e);
            }
        }
    }
}
