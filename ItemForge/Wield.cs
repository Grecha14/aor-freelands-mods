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
    /// Makes a weapon ask something of the one holding it.
    ///
    /// В игре оружие не спрашивает ничего. Двуручный меч пятого яруса ложится в руки дохляка
    /// с восемью силы и бьёт ровно так же, как в руках того, кто десять лет его носил. Поле
    /// для требований в данных есть — «attributeRquire», — но почти нигде не заполнено, а там,
    /// где заполнено, оно просто запрещает надеть, и на этом разговор кончается.
    ///
    /// Здесь требование появляется у каждого оружия и выводится из его же яруса, а невыполнение
    /// не запрещает ничего: оно делает вещь плохой. Лестница требований идёт по Фибоначчи —
    /// 10, 15, 25, 40, 70, — потому что разрыв между ярусами должен расти, а не тянуться
    /// линейкой. Глубина же провала одинакова на всех ярусах: полный недобор стоит семидесяти
    /// процентов, и ниже трети от своего урона вещь не опускается никогда. Дубина первого яруса
    /// в руках слабака бьёт на треть — ровно как и меч пятого, потому что беспомощность
    /// беспомощна одинаково.
    ///
    /// Чего требует вещь, решает не её название, а её же данные: тип урона и тип хвата. Поэтому
    /// правило не промахивается мимо предмета, которого я никогда не видел, и полуторник, у
    /// которого вторичный тип «двуручное», спрашивает как двуручник — включая клинок Короля
    /// Ада, помеченный ровно так же.
    /// </summary>
    internal static class Wield
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> StatLadder;
        internal static ConfigEntry<string> MasteryLadder;
        internal static ConfigEntry<float> Half;
        internal static ConfigEntry<float> Depth;
        internal static ConfigEntry<float> Floor;
        internal static ConfigEntry<bool> Multiply;
        internal static ConfigEntry<int> Ceiling;
        internal static ConfigEntry<float> SpeedShare;
        internal static ConfigEntry<string> Staves;
        internal static ConfigEntry<string> StaffPower;
        internal static ConfigEntry<bool> TrainOwn;
        internal static ConfigEntry<string> Crossbows;
        internal static ConfigEntry<string> Aiming;
        internal static ConfigEntry<float> Spanning;
        internal static ConfigEntry<float> Shielding;
        internal static ConfigEntry<float> Heaving;
        internal static ConfigEntry<float> Bearing;
        internal static ConfigEntry<float> Bowing;
        internal static ConfigEntry<bool> ShowRequirement;
        internal static ConfigEntry<bool> Advisory;
        internal static ConfigEntry<string> FitFrom;
        internal static ConfigEntry<bool> FitLegendary;
        internal static ConfigEntry<bool> FitPlayer;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Wield", "Enabled", true,
                "Let a weapon ask something of the one holding it. Without this a greatsword of "
                + "the fifth tier works the same in the hands of a starveling and of a veteran.");

            StatLadder = config.Bind("Wield", "StatLadder", "10,20,30,40,50",
                "What a weapon of each tier demands of the body, from the first tier to the "
                + "fifth. An even ten a step: a greatsword of the fifth tier asks fifty "
                + "strength, one of the first asks ten, and the climb between is a straight "
                + "line anybody can hold in his head. Two-handed weapons and polearms ask the "
                + "whole of it, everything else asks the share set by Half — a blade in one "
                + "hand is a lighter question than the same steel in two.");

            MasteryLadder = config.Bind("Wield", "MasteryLadder", "10,20,40,75,100",
                "And what it demands of the hand: the mastery of its branch, first tier to "
                + "fifth. The game's mastery runs to a hundred, though it stops growing at "
                + "ninety-nine, so the last step is met at ninety-nine.");

            Half = config.Bind("Wield", "Half", 0.5f,
                new ConfigDescription(
                    "Share of the ladder a one-handed weapon asks for, and the share each of a "
                    + "bow's two attributes asks for. A one-hander is lighter than a two-hander "
                    + "and should cost less; a bow wants hand and eye both, so the demand is "
                    + "split between them rather than doubled.",
                    new AcceptableValueRange<float>(0.1f, 1f)));

            Depth = config.Bind("Wield", "Depth", 0.70f,
                new ConfigDescription(
                    "What a complete failure of one demand costs. Seven tenths: a man with no "
                    + "strength at all keeps three tenths of the weapon's damage. The same on "
                    + "every tier — helplessness is equally helpless with a club and with a "
                    + "greatsword.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Floor = config.Bind("Wield", "Floor", 0.30f,
                new ConfigDescription(
                    "And what no weapon ever falls below, however badly its bearer fails it. "
                    + "Failing strength and mastery both cannot take a weapon past this.",
                    new AcceptableValueRange<float>(0.05f, 1f)));

            Multiply = config.Bind("Wield", "Multiply", true,
                "Multiply the two shortfalls together instead of adding them. Multiplying keeps "
                + "each demand meaningful on its own: half the strength and half the mastery "
                + "leaves four tenths of the damage rather than landing straight on the floor.");

            Ceiling = config.Bind("Wield", "Ceiling", 99,
                new ConfigDescription(
                    "Mastery at or above this counts as a hundred. The game stops raising "
                    + "mastery at ninety-nine, so without this the fifth tier could never be "
                    + "satisfied by anyone.",
                    new AcceptableValueRange<int>(1, 100)));

            SpeedShare = config.Bind("Wield", "SpeedShare", 1.0f,
                new ConfigDescription(
                    "How much of the loss falls on the swing as well as on the blow. At one the "
                    + "weapon is as slow as it is weak; at zero a man too small for his sword "
                    + "still swings it briskly and merely does nothing with it.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Staves = config.Bind("Wield", "Staves", "Staff,Stick,Wand",
                "Weapon classes that answer to the mind rather than the body. These ask for "
                + "intelligence, and failing them costs magic power instead of weapon damage — "
                + "a wizard's stick was never meant to be swung.");

            StaffPower = config.Bind("Wield", "StaffPower", "",
                "What a staff adds to the spells cast with it, by tier, as a fraction. A plain "
                + "staff in this game carries nothing at any tier — the item dump says «no "
                + "bonuses» for every one of them — so a mage's own weapon was the only thing "
                + "in his kit that did not matter. Here it does: at the fourth tier the spell "
                + "hits eight tenths harder, and a wizard picking up a better staff feels it "
                + "the way a swordsman feels a better sword. Covers whatever Staves calls a "
                + "staff.");

            Aiming = config.Bind("Wield", "Aiming", "0.5,0.25",
                "What share of the ladder a bow or a crossbow asks of agility and of "
                + "perception. Half and a quarter: at the fifth tier that is twenty-five and "
                + "thirteen. They were a whole and a half, and with strength added on top a "
                + "fifth tier crossbow came to a hundred and twenty-five points in three "
                + "attributes — of a hundred and forty-nine a man has for all six. A demand "
                + "nobody can meet is not a demand, it is a wall.");

            Spanning = config.Bind("Wield", "Spanning", 0.3f,
                new ConfigDescription(
                    "What share of the ladder a crossbow asks of strength. Three tenths: "
                    + "fifteen at the fifth tier, so that the whole of a crossbowman's demand "
                    + "comes to about fifty — the same a greatsword asks of a strong man.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Bearing = config.Bind("Wield", "Bearing", 3f,
                new ConfigDescription(
                    "What one kilogram of shield costs in stamina to stop a blow with. Three, "
                    + "and the price of a block no longer follows the blow at all: it follows "
                    + "the shield. A buckler of two kilos costs six, a round shield of four "
                    + "costs twelve, a pavise of six costs eighteen — and they all stop the "
                    + "blow entirely, which is the whole of what a shield does. Nought puts the "
                    + "game's own reckoning back, where a block costs whatever it stopped.",
                    new AcceptableValueRange<float>(0f, 20f)));

            Heaving = config.Bind("Wield", "Heaving", 0.75f,
                new ConfigDescription(
                    "What a two-handed weapon asks of endurance, as a share of what it asks of "
                    + "strength.\n\n"
                    + "Силы двуручное просило и прежде — всю, сколько её положено на его "
                    + "ступени. Но поднять его мало: им ещё надо махать, и махать не один раз. "
                    + "Три четверти дыхания сверх силы: новичок такое не возьмёт, а взявший "
                    + "будет дышать ровно.",
                    new AcceptableValueRange<float>(0f, 2f)));

            Shielding = config.Bind("Wield", "Shielding", 0.5f,
                new ConfigDescription(
                    "What share of the ladder a shield asks of endurance. A half: a sword and "
                    + "shield together then cost about what a greatsword costs alone, which is "
                    + "what they should. It was the whole ladder, and a shielded swordsman "
                    + "paid half again more than anybody.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Crossbows = config.Bind("Wield", "Crossbows",
                "Crossbow,HeavyCrossbow,CrossbowQuiver",
                "What counts as spanned rather than drawn. A crossbow is cocked with the whole "
                + "body against a windlass or a goat's foot, and asks a man's full strength; a "
                + "bow is pulled with the back and shoulder and asks less.");

            Bowing = config.Bind("Wield", "Bowing", 0.7f,
                new ConfigDescription(
                    "What share of a crossbow's demand for strength a bow makes. Seven tenths: "
                    + "a third less, because a string is drawn and a windlass is cranked.",
                    new AcceptableValueRange<float>(0f, 1f)));

            TrainOwn = config.Bind("Wield", "TrainOwn", true,
                "Let each weapon in hand train its own branch. The game gives mastery by the "
                + "fighter's stance rather than by what he holds: a man with a shield pours "
                + "everything into the shield branch and never learns the sword in his other "
                + "hand, and a man with two blades feeds only the dual branch. Without this a "
                + "sword's own demand could never be earned while a shield was worn.");

            ShowRequirement = config.Bind("Wield", "ShowRequirement", true,
                "Write the demand into the item itself, so the game's own tooltip prints it and "
                + "paints it red when the character falls short. Nothing else about the item is "
                + "touched, and this lives only as long as the session does.");

            Advisory = config.Bind("Wield", "Advisory", true,
                "Let an attribute demand be advice rather than a lock: a thing too heavy for you "
                + "can still be worn, it simply works badly. Demands of name, sex and race keep "
                + "their veto — those are not about being strong enough.");

            FitFrom = config.Bind("Wield", "FitFrom", "T3",
                "From this tier upward, a non-player character is built to the weapon he carries: "
                + "his attribute and his mastery are raised to what it asks. An enemy with a good "
                + "axe should fight like a man who owns one. Below this tier nobody is helped.");

            FitLegendary = config.Bind("Wield", "FitLegendary", true,
                "And the same for anyone carrying a legendary, whatever its tier.");

            FitPlayer = config.Bind("Wield", "FitPlayer", false,
                "Include your own character, your companions and your followers in that. Off: "
                + "otherwise picking a greatsword off a corpse would hand you seventy strength "
                + "and a hundred mastery, and no demand would ever bite you again.");
        }

        // ----------------------------------------------------------------- что просит вещь

        /// <summary>What a weapon asks of the one holding it.</summary>
        internal sealed class Demand
        {
            internal int first = -1;
            internal int firstNeed;
            internal int second = -1;
            internal int secondNeed;
            internal int third = -1;
            internal int thirdNeed;
            internal WeaponType branch;
            internal int masteryNeed;
            internal bool magic;
            internal bool shield;
        }

        // Требование вещи не меняется никогда, а спрашивают его на каждом пересчёте — а тот
        // случается часто, от смены снаряжения до каждого наложенного оберега. Считаем один раз
        // на вещь; список сбрасывается, если кто-то поправил лестницы в настройках.
        private static readonly Dictionary<UIWeaponInfo, Demand> asked =
            new Dictionary<UIWeaponInfo, Demand>();

        private static string askedBy;

        /// <summary>
        /// Reads a weapon's demand out of the weapon itself.
        ///
        /// Ни одно правило здесь не смотрит на название. Хват берётся из «WeaponType», а если у
        /// вещи объявлен вторичный хват — из него: так полуторник спрашивает как двуручник, чего
        /// и заслуживает. Чем бьёт одноручное, решает его же ведущий тип урона, поэтому дубина
        /// идёт по силе, а клинок и колющее — по ловкости, и предмет, которого нет ни в одном
        /// моём списке, всё равно попадёт куда надо.
        /// </summary>
        internal static Demand Ask(UIWeaponInfo blade)
        {
            if (blade == null) return null;

            string by = StatLadder.Value + "|" + MasteryLadder.Value + "|"
                + Half.Value.ToString(CultureInfo.InvariantCulture) + "|" + Staves.Value;

            if (by != askedBy) { askedBy = by; asked.Clear(); }

            Demand kept;
            if (asked.TryGetValue(blade, out kept)) return kept;

            kept = Read(blade);
            asked[blade] = kept;
            return kept;
        }

        private static Demand Read(UIWeaponInfo blade)
        {
            int step = (int)blade.tier;
            if (step < 1) return null;

            float[] body = Steps(StatLadder, ref statRead, ref statSteps);
            float[] hand = Steps(MasteryLadder, ref mastRead, ref mastSteps);
            if (step > body.Length || step > hand.Length) return null;

            WeaponType branch = blade.WeaponType;
            if (blade.WeaponType_Secondary != WeaponType.none
                && blade.WeaponType_Secondary <= WeaponType.polearms)
            {
                branch = blade.WeaponType_Secondary;
            }

            int full = Mathf.Max(1, Mathf.RoundToInt(body[step - 1]));
            int part = Mathf.Max(1, Mathf.RoundToInt(body[step - 1] * Half.Value));

            Demand want = new Demand();
            want.branch = branch;
            want.masteryNeed = Mathf.Max(0, Mathf.RoundToInt(hand[step - 1]));

            if (Wand(blade))
            {
                want.magic = true;
                want.first = 4;              // интеллект
                want.firstNeed = full;
                return want;
            }

            switch (branch)
            {
                case WeaponType.shield:
                    want.shield = true;
                    want.first = 1;          // выносливость
                    want.firstNeed = Mathf.Max(1,
                        Mathf.RoundToInt(body[step - 1] * Shielding.Value));
                    return want;

                case WeaponType.range:
                    // Лук и арбалет держат натяжением и выцеливают глазом. Ловкость просят
                    // целиком, как двуручное просит силу: это их главный стат, а не приправа.
                    float[] eye = Steps(Aiming, ref aimRead, ref aimSteps);
                    float toHand = eye.Length > 0 ? eye[0] : 0.5f;
                    float toEye = eye.Length > 1 ? eye[1] : 0.25f;

                    want.first = 2;          // ловкость
                    want.firstNeed = Mathf.Max(1,
                        Mathf.RoundToInt(body[step - 1] * toHand));
                    want.second = 3;         // восприятие
                    want.secondNeed = Mathf.Max(1,
                        Mathf.RoundToInt(body[step - 1] * toEye));

                    // И силы: арбалет взводят усилием, а не пальцами, лук тянут спиной.
                    // Арбалету её нужно больше — ворот крутят всем телом, — луку на треть
                    // меньше.
                    float pull = Spanning.Value * (Crossbow(blade) ? 1f : Bowing.Value);

                    want.third = 0;          // сила
                    want.thirdNeed = Mathf.Max(1,
                        Mathf.RoundToInt(body[step - 1] * pull));

                    return want;

                case WeaponType.twohand:
                case WeaponType.polearms:
                    // Силу — всю: поднять двуручное нечем, кроме неё. И дыхание следом:
                    // поднять его мало, им ещё надо махать, и махать не один раз.
                    want.first = 0;          // сила
                    want.firstNeed = full;
                    want.second = 1;         // выносливость
                    want.secondNeed = Mathf.Max(1,
                        Mathf.RoundToInt(full * Heaving.Value));
                    return want;

                case WeaponType.daul:
                    // Парное — это ловкость и ничто другое, чем бы ни были сами клинки. Две
                    // руки, идущие врозь, стоят не силы, а того, чтобы они не мешали друг
                    // другу, и это ровно она.
                    want.first = 2;          // ловкость
                    want.firstNeed = full;
                    return want;

                case WeaponType.onehand:
                    // Два вопроса и оба заданы порознь. Какой стат — решает ведущий урон:
                    // остриё спрашивает ловкость, режущее и дробящее силу, ровно тот стат,
                    // которым Breach считает пробитие. Сколько его — решает хват: одна рука
                    // берёт половину лестницы, и берёт её всегда. Сталь та же, а рук вдвое
                    // меньше, и кинжал тут ничем не строже меча.
                    want.first = Breach.Lead(blade) == 2 ? 2 : 0;
                    want.firstNeed = part;
                    return want;

                default:
                    return null;             // кулаки, колчан, лютня
            }
        }

        /// <summary>True when the thing is spanned rather than drawn: a crossbow.</summary>
        private static bool Crossbow(UIWeaponInfo blade)
        {
            if (blade == null) return false;

            string kind = blade.weaponClass.ToString();

            foreach (string one in (Crossbows.Value ?? "").Split(','))
            {
                if (string.Equals(one.Trim(), kind, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when the weapon's leading damage is blunt, so it answers to strength.</summary>
        private static bool Blunt(UIWeaponInfo blade)
        {
            if (blade.damage == null) return false;

            float sharp = 0f, blunt = 0f, stab = 0f;

            foreach (KeyValuePair<DamageType, Damage> one in blade.damage)
            {
                if (one.Value == null) continue;

                if (one.Key == DamageType.sharp) sharp += one.Value.maxDamage;
                else if (one.Key == DamageType.blunt) blunt += one.Value.maxDamage;
                else if (one.Key == DamageType.stab) stab += one.Value.maxDamage;
            }

            return blunt > sharp && blunt > stab;
        }

        private static readonly HashSet<WeaponClass> sticks = new HashSet<WeaponClass>();
        private static string sticksRead;

        /// <summary>True for a wizard's stick, which answers to the mind.</summary>
        internal static bool Wand(UIWeaponInfo blade)
        {
            string written = Staves.Value ?? "";

            if (written != sticksRead)
            {
                sticksRead = written;
                sticks.Clear();

                foreach (string one in written.Split(','))
                {
                    string name = one.Trim();
                    if (name.Length == 0) continue;

                    try { sticks.Add((WeaponClass)Enum.Parse(typeof(WeaponClass), name, true)); }
                    catch { ItemForgePlugin.Log.LogWarning($"Класса оружия «{name}» в игре нет."); }
                }
            }

            return sticks.Contains(blade.weaponClass);
        }

        // ----------------------------------------------------------------- насколько дотянул

        /// <summary>How well the bearer answers the demand, from the floor to one.</summary>
        /// <summary>What this thing asks of one particular attribute, or nought.</summary>
        internal static int Asks(UIWeaponInfo blade, int stat)
        {
            Demand want = Ask(blade);
            if (want == null) return 0;

            if (want.first == stat) return want.firstNeed;
            if (want.second == stat) return want.secondNeed;
            if (want.third == stat) return want.thirdNeed;

            return 0;
        }

        internal static float Grip(HumaniodUnit who, Demand want)
        {
            NPCSaveData mind = who == null ? null : who.Data;
            if (mind == null || mind.humanAttribute == null) return 1f;

            float body = Lack(mind.humanAttribute[want.first], want.firstNeed);

            if (want.third >= 0)
            {
                body = (body + Lack(mind.humanAttribute[want.third], want.thirdNeed)) * 0.5f;
            }

            if (want.second >= 0)
            {
                body = (body + Lack(mind.humanAttribute[want.second], want.secondNeed)) * 0.5f;
            }

            float hand = 0f;

            int slot = (int)want.branch;

            if (want.masteryNeed > 0 && mind.weaponMastery != null
                && slot >= 0 && slot < mind.weaponMastery.Length)
            {
                // Каждая вещь спрашивает свою ветку и только свою. Соблазн зачесть вместо неё
                // ту, которой боец дерётся сейчас, велик — но тогда щитоносец со сточенной до
                // сотни веткой щита махал бы мечом пятого яруса в полную силу, не вложив в меч
                // ничего. Тупик, из-за которого этот соблазн возникал, снят в другом месте:
                // теперь каждое оружие в руках качает свою ветку само.
                int have = mind.weaponMastery[slot];

                if (have >= Ceiling.Value) have = want.masteryNeed;

                hand = Lack(have, want.masteryNeed);
            }

            float one = 1f - Depth.Value * body;
            float two = 1f - Depth.Value * hand;

            float able = Multiply.Value
                ? one * two
                : 1f - (Depth.Value * body + Depth.Value * hand);

            return Mathf.Clamp(able, Floor.Value, 1f);
        }

        private static float Lack(int have, int need)
        {
            if (need <= 0) return 0f;
            return Mathf.Clamp01(1f - (float)have / need);
        }

        /// <summary>
        /// Lets every weapon in hand train the branch it actually belongs to.
        ///
        /// Игра раздаёт опыт мастерства не по вещам, а по одному числу на бойца: взял щит — и
        /// весь опыт уходит в ветку «щит», а меч в правой руке не учит владению мечом ни часа.
        /// Парное оружие точно так же кормит одну ветку «парное». Оттого требование к мечу по
        /// его собственной ветке было бы невыполнимым — не потому, что оно строгое, а потому
        /// что заслужить его нечем.
        ///
        /// Здесь опыт идёт туда, где его заработали: меч учит мечу, щит учит щиту, а клинок
        /// Короля Ада — двуручному, потому что вторичным хватом объявлен именно он. Ветки,
        /// которые игра уже наградила сама, пропускаются, чтобы никому не досталось дважды.
        /// </summary>
        internal static void Train(HumaniodUnit who, int exp)
        {
            if (!Enabled.Value || !TrainOwn.Value || who == null || exp <= 0) return;

            // На тренировочном дворе игра мастерство не начисляет нарочно — чтобы им нельзя
            // было обзавестись, колотя чучело. Довесок подчиняется тому же запрету.
            if (TroopTrainingGround.instance != null) return;

            EquipmentManager gear = who.equipmentmanger;
            if (gear == null || gear.equipInfos == null) return;

            WeaponType given = WeaponType.none;

            for (int i = 0; i < 2 && i < gear.equipInfos.Length; i++)
            {
                EquipInfo slot = gear.equipInfos[i];
                if (slot == null || !slot.IsEquiped()) continue;

                UIWeaponInfo blade = slot.inventory.itemInfo as UIWeaponInfo;
                if (blade == null) continue;

                Demand want = Ask(blade);
                if (want == null) continue;

                WeaponType branch = want.branch;

                if (branch == WeaponType.none || branch > WeaponType.polearms) continue;
                if (branch == who.weapontype || branch == who.weapontype2) continue;
                if (branch == given) continue;

                given = branch;
                who.GainWeaponMasteryExp(branch, exp);
            }
        }

        // ----------------------------------------------------------------- применение

        /// <summary>
        /// Takes from a weapon what its bearer cannot give it.
        ///
        /// Стоит в самом конце пересчёта, когда игра уже свела воедино и урон, и скорость, и
        /// блок: всё это она собирает заново на каждом проходе из базовых чисел, поэтому
        /// множители здесь не накапливаются, сколько бы раз пересчёт ни случился.
        /// </summary>
        internal static void Weigh(HumaniodUnit who)
        {
            if (!Enabled.Value || who == null || who.weapons == null) return;

            EquipmentManager gear = who.equipmentmanger;
            if (gear == null || gear.equipInfos == null) return;

            bool swung = false;

            for (int i = 0; i < who.weapons.Count && i < 2 && i < gear.equipInfos.Length; i++)
            {
                EquipInfo slot = gear.equipInfos[i];
                if (slot == null || !slot.IsEquiped()) continue;

                UIWeaponInfo blade = slot.inventory.itemInfo as UIWeaponInfo;
                if (blade == null) continue;

                Demand want = Ask(blade);
                if (want == null) continue;

                float able = Grip(who, want);
                if (able >= 0.999f) continue;

                if (want.shield) { Shielded(who, able); continue; }

                if (want.magic) { who.MagicDamageMD *= able; continue; }

                Scale(who.weapons[i], able);
                who.weapons[i].attackSpeed *= 1f - (1f - able) * SpeedShare.Value;
                swung = true;
            }

            if (swung) Average(who);
        }

        /// <summary>The shield in hand, if there is one.</summary>
        internal static UIWeaponInfo Shield(UnitAttribute who)
        {
            try
            {
                HumaniodUnit man = who as HumaniodUnit;
                if (man == null || man.equipmentmanger == null) return null;

                EquipInfo[] kit = man.equipmentmanger.equipInfos;
                if (kit == null) return null;

                for (int i = 0; i < 2 && i < kit.Length; i++)
                {
                    EquipInfo slot = kit[i];
                    if (slot == null || !slot.IsEquiped() || slot.inventory == null) continue;

                    UIWeaponInfo shield = slot.inventory.itemInfo as UIWeaponInfo;
                    if (shield != null && shield.WeaponType == WeaponType.shield) return shield;
                }
            }
            catch
            {
            }

            return null;
        }

        /// <summary>
        /// What a shield costs the arm too weak to hold it.
        ///
        /// Стамина за блок считается как урон, умноженный на «1 − blockEPsave», поэтому
        /// подорожание задаётся не прибавкой, а обратной долей: при трети эффективности блок
        /// стоит втрое с лишним. Шанс и угол просто проседают — это и есть то, что со стороны
        /// выглядит как «не успел подставить щит».
        /// </summary>
        private static void Shielded(UnitAttribute who, float able)
        {
            who.block *= able;
            who.blockAngle *= able;

            float pays = (1f - who.blockEPsave) / Mathf.Max(0.05f, able);
            who.blockEPsave = 1f - pays;
        }

        internal static void Scale(Weapon arm, float much)
        {
            if (arm == null || arm.damage == null) return;

            foreach (KeyValuePair<DamageType, Damage> one in arm.damage)
            {
                if (one.Value == null) continue;

                one.Value.minDamage *= much;
                one.Value.maxDamage *= much;
                one.Value.currentDamage *= much;
            }
        }

        // Средняя скорость атаки — число, по которому игра считает ход в пошаговом режиме.
        // Она сводится до наших правок, поэтому после них её надо свести заново.
        private static void Average(UnitAttribute who)
        {
            if (who.weapons == null || who.weapons.Count == 0) return;

            float sum = 0f;
            for (int i = 0; i < who.weapons.Count; i++) sum += who.weapons[i].attackSpeed;

            who.attackspeed = sum / who.weapons.Count;
        }

        // ----------------------------------------------------------------- подгон неигровых

        /// <summary>
        /// Builds a character to the weapon he was given.
        ///
        /// Иначе выходит глупость: разбойник получает при рождении добротный топор и машет им
        /// как палкой, потому что ни силы, ни выучки под него ему никто не выдал. Здесь ему
        /// выдают — один раз, в тот момент, когда снаряжение уже разобрано.
        ///
        /// Уровень при этом не пишется, а пересчитывается: в этой игре он выводится из шести
        /// характеристик, и всякая попытка записать его числом стирается при первом же
        /// пересчёте. Растёт то, из чего он считается, — и уровень растёт следом сам.
        /// </summary>
        internal static void Fit(HumaniodUnit who, HashSet<int> done)
        {
            if (!Enabled.Value || who == null) return;

            NPCSaveData mind = who.Data;
            if (mind == null || mind.humanAttribute == null || !mind.equipsInited) return;

            EquipmentManager gear = who.equipmentmanger;
            if (gear == null || !gear.isInited || gear.equipInfos == null) return;

            if (!done.Add(who.GetInstanceID())) return;

            if (!FitPlayer.Value && Own(who)) return;

            int floor = Tier();
            bool moved = false;

            for (int i = 0; i < 2 && i < gear.equipInfos.Length; i++)
            {
                EquipInfo slot = gear.equipInfos[i];
                if (slot == null || !slot.IsEquiped()) continue;

                UIWeaponInfo blade = slot.inventory.itemInfo as UIWeaponInfo;
                if (blade == null) continue;

                bool worthy = (int)blade.tier >= floor
                    || (FitLegendary.Value && slot.inventory.quality >= UIItemQuality.Legendary);

                if (!worthy) continue;

                Demand want = Ask(blade);
                if (want == null) continue;

                moved |= Lift(mind, want.first, want.firstNeed);
                if (want.second >= 0) moved |= Lift(mind, want.second, want.secondNeed);
                if (want.third >= 0) moved |= Lift(mind, want.third, want.thirdNeed);

                int branch = (int)want.branch;

                if (want.masteryNeed > 0 && mind.weaponMastery != null
                    && branch >= 0 && branch < mind.weaponMastery.Length)
                {
                    int reach = Mathf.Min(want.masteryNeed, Ceiling.Value);

                    if (mind.weaponMastery[branch] < reach)
                    {
                        mind.weaponMastery[branch] = reach;
                        moved = true;
                    }
                }
            }

            if (!moved) return;

            // Потенциал — это потолок, до которого персонажу вообще позволено расти. Подняв
            // характеристики выше него, мы оставили бы существо, которое уже переросло само
            // себя; поэтому потолок поднимается следом.
            if (mind.humanAttribute.potential < mind.humanAttribute.Sum)
            {
                mind.humanAttribute.potential = mind.humanAttribute.Sum;
            }

            int was = mind.level;
            mind.SetLevel();

            ItemForgePlugin.Log.LogInfo($"«{mind.unitname}» подогнан под своё оружие: "
                + $"уровень {was} → {mind.level}.");
        }

        private static bool blessed;

        /// <summary>
        /// Writes the staff's worth into the staff, so the game shows it and counts it itself.
        ///
        /// Дописывать число к строке прочности было проще, но неправдой: получалась подпись,
        /// которой нет ни в одной другой вещи. У игры для этого есть своё место — список
        /// свойств, тот самый, где стоят «Проникающая режущая сила» и прибавки колец, — и
        /// своё правило: «MagicDamageMD += свойство». Кладём прибавку туда, в сам образец
        /// посоха, и дальше игра сама и рисует её, и считает. Вещи рождаются из образца
        /// копией списка, поэтому написанное здесь достаётся каждому посоху этого вида.
        /// </summary>
        internal static void Bless()
        {
            if (blessed || !Enabled.Value) return;

            try
            {
                UIItemDatabase db;
                try { db = UIItemDatabase.Instance; }
                catch { return; }

                if (db == null || db.items == null) return;

                blessed = true;

                float[] steps = Steps(StaffPower, ref staffRead, ref staffSteps);
                if (steps.Length == 0) return;

                int touched = 0;

                foreach (UIItemInfo thing in db.items)
                {
                    UIWeaponInfo blade = thing as UIWeaponInfo;
                    if (blade == null || !Wand(blade)) continue;

                    int step = (int)blade.tier;
                    if (step < 1 || step > steps.Length || steps[step - 1] <= 0f) continue;

                    if (blade.addAttrs == null)
                    {
                        blade.addAttrs = new List<AddonAttributes>();
                    }

                    // Своё прежнее слово перезаписываем, чужое не трогаем: у именных посохов
                    // прибавка к заклинаниям бывает и своя, и отнимать её незачем.
                    bool had = false;

                    foreach (AddonAttributes one in blade.addAttrs)
                    {
                        if (one != null && one.type == AddonAttribute.MagicDamage) { had = true; break; }
                    }

                    if (had) continue;

                    blade.addAttrs.Add(new AddonAttributes(AddonAttribute.MagicDamage, steps[step - 1]));
                    touched++;
                }

                ItemForgePlugin.Log.LogInfo($"Посохи теперь усиливают заклинания: {touched} вещей "
                    + $"(Т1 +{steps[0] * 100f:0}%, Т4 +{steps[Mathf.Min(3, steps.Length - 1)] * 100f:0}%).");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог наделить посохи силой: " + e);
            }
        }

        private static bool Lift(NPCSaveData mind, int index, int want)
        {
            if (index < 0 || want <= 0) return false;
            if (mind.humanAttribute[index] >= want) return false;

            mind.humanAttribute[index] = Mathf.Min(want, 100);
            return true;
        }

        /// <summary>True for your own character, your companions and those following you.</summary>
        private static bool Own(UnitAttribute who)
        {
            if ((object)who == (object)gameManager.currentplayUnit) return true;
            if (who.inParty || who.isTempFollower) return true;

            return who.Data != null && who.Data.team == Faction.player;
        }

        private static int tier = -1;
        private static string tierRead;

        private static int Tier()
        {
            string written = FitFrom.Value ?? "";
            if (tier >= 0 && written == tierRead) return tier;

            tierRead = written;
            tier = 3;

            try { tier = (int)(ItemTier)Enum.Parse(typeof(ItemTier), written.Trim(), true); }
            catch { ItemForgePlugin.Log.LogWarning($"Яруса «{written}» нет, беру T3."); }

            return tier;
        }

        // ----------------------------------------------------------------- показ требования

        private static bool marked;

        /// <summary>
        /// Writes each weapon's demand into the weapon, so the game's own tooltip shows it.
        ///
        /// Рисовать своё окно поверх чужого — последнее дело: у игры уже есть блок требований,
        /// он сам красит зелёным и красным по тому, кого вы сейчас смотрите. Ему просто нечего
        /// было показывать. Теперь есть. Существующие требования не перетираются — только
        /// поднимаются, если наше строже.
        /// </summary>
        internal static void Mark()
        {
            if (marked || !Enabled.Value || !ShowRequirement.Value) return;

            try
            {
                UIItemDatabase db;
                try { db = UIItemDatabase.Instance; }
                catch { return; }

                if (db == null || db.items == null) return;

                // Требование, записанное в вещь, для самой игры — запрет надеть. Снимает его
                // наш патч, и если он почему-то не встал, записывать было бы вредительством:
                // игрок остался бы без половины оружия. Сперва убеждаемся, что снимать есть кому.
                if (Advisory.Value && !Waived())
                {
                    marked = true;
                    ItemForgePlugin.Log.LogWarning("Требования не проставлены: патч, снимающий "
                        + "запрет на надевание, не встал, и вещи стали бы недоступны.");
                    return;
                }

                marked = true;
                int touched = 0;

                foreach (UIItemInfo thing in db.items)
                {
                    UIWeaponInfo blade = thing as UIWeaponInfo;
                    if (blade == null) continue;

                    Demand want = Ask(blade);
                    if (want == null) continue;

                    if (blade.attributeRquire == null)
                    {
                        HumanAttribute fresh = new HumanAttribute();
                        for (int i = 0; i < 6; i++) fresh[i] = 0;
                        blade.attributeRquire = fresh;
                    }

                    Raise(blade.attributeRquire, want.first, want.firstNeed);
                    Raise(blade.attributeRquire, want.second, want.secondNeed);
                    Raise(blade.attributeRquire, want.third, want.thirdNeed);
                    touched++;
                }

                ItemForgePlugin.Log.LogInfo($"Оружие теперь просит по себе: {touched} вещей.");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог проставить требования: " + e);
            }
        }

        private static void Raise(HumanAttribute need, int index, int value)
        {
            if (index < 0 || value <= 0) return;
            if (need[index] < value) need[index] = value;
        }

        /// <summary>True when the patch that turns an attribute demand into advice is in place.</summary>
        private static bool Waived()
        {
            try
            {
                System.Reflection.MethodBase gate =
                    AccessTools.Method(typeof(UIEquipmentInfo), "CheckReqirement");

                if (gate == null) return false;

                HarmonyLib.Patches on = HarmonyLib.Harmony.GetPatchInfo(gate);

                return on != null && on.Postfixes != null && on.Postfixes.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>How much mastery this weapon asks, or nothing when it asks none.</summary>
        internal static int Rank(UIWeaponInfo blade)
        {
            if (!Enabled.Value || !ShowRequirement.Value || blade == null) return 0;

            Demand want = Ask(blade);
            return want == null ? 0 : Mathf.Max(0, want.masteryNeed);
        }

        /// <summary>How much of that mastery the one looking at it actually has.</summary>
        internal static int Have(UIWeaponInfo blade)
        {
            try
            {
                if (blade == null) return 0;

                HumaniodUnit who = EquipSlotManager.instance != null
                    ? EquipSlotManager.instance.targetUnit
                    : gameManager.currentplayUnit;

                if (who == null || who.Data == null || who.Data.weaponMastery == null) return 0;

                Demand want = Ask(blade);
                if (want == null) return 0;

                int branch = (int)want.branch;

                return branch >= 0 && branch < who.Data.weaponMastery.Length
                    ? who.Data.weaponMastery[branch]
                    : 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>The mastery line for the tooltip, or null when the thing asks for none.</summary>
        internal static string Word(UIItemInfo thing)
        {
            if (!Enabled.Value || !ShowRequirement.Value) return null;

            UIWeaponInfo blade = thing as UIWeaponInfo;
            if (blade == null) return null;

            Demand want = Ask(blade);
            if (want == null || want.masteryNeed <= 0) return null;

            return "Мастерство " + want.masteryNeed;
        }

        // ----------------------------------------------------------------- разбор лестниц

        private static float[] statSteps, mastSteps, staffSteps, aimSteps;
        private static string statRead, mastRead, staffRead, aimRead;

        private static float[] Steps(ConfigEntry<string> from, ref string read, ref float[] kept)
        {
            string written = from.Value ?? "";
            if (kept != null && written == read) return kept;

            List<float> got = new List<float>();

            foreach (string one in written.Split(','))
            {
                float much;
                if (float.TryParse(one.Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out much))
                {
                    got.Add(Mathf.Max(0f, much));
                }
            }

            while (got.Count < 5) got.Add(got.Count > 0 ? got[got.Count - 1] : 0f);

            read = written;
            kept = got.ToArray();
            return kept;
        }
    }

    // Подгон стоит до пересчёта, а штраф — после: так поднятые характеристики учитываются тем
    // же проходом, а не следующим, и лишнего пересчёта не нужно вовсе.
    [HarmonyPatch(typeof(HumaniodUnit), "WriteUnitAttribute")]
    internal static class WriteUnitAttribute_Wield_Patch
    {
        private static readonly HashSet<int> done = new HashSet<int>();

        private static void Prefix(HumaniodUnit __instance)
        {
            try { Wield.Fit(__instance, done); }
            catch (Exception e) { ItemForgePlugin.Log.LogWarning("Подгон сорвался: " + e.Message); }
        }

        private static void Postfix(HumaniodUnit __instance)
        {
            try { Wield.Weigh(__instance); }
            catch (Exception e) { ItemForgePlugin.Log.LogWarning("Требования сорвались: " + e.Message); }
        }
    }

    // Опыт мастерства раздаётся игрой по стойке бойца, а не по тому, что у него в руках.
    // Досыпаем каждому оружию в его собственную ветку — ту самую, которую с него же и
    // спрашивают. Без этого требование к мечу под щитом было бы нечем заслужить.
    // Цена блока — вес щита, а не сила удара.
    //
    // Игра берёт за блок «урон × StaminaDamageMD × (1 − blockEPsave)» и берёт одинаково с
    // баклера и с павезы: щиты между собой не различались ничем, что стоило бы выбора. Теперь
    // держать удар стоит того, что держишь: три очка сил за килограмм.
    //
    // Сделано подменой той самой доли, а не перехватом расхода, и вот почему. Когда сил не
    // хватило, игра считает пробившийся остаток по ней же — «сколько сил было, столько урона и
    // съедено». Подмени расход мимо неё, и блок ломался бы неправильно: цена одна, остаток по
    // другой. Ставим долю так, чтобы оба счёта сошлись на весе.
    [HarmonyPatch(typeof(UnitAttribute), "Doblock")]
    internal static class Doblock_Bearing_Patch
    {
        internal static UnitAttribute holding;
        private static float was;

        private static void Prefix(UnitAttribute __instance)
        {
            holding = null;

            if (Wield.Enabled == null || !Wield.Enabled.Value) return;
            if (Wield.Bearing == null || Wield.Bearing.Value <= 0f || __instance == null) return;

            holding = __instance;
            was = __instance.blockEPsave;
        }

        private static void Postfix(UnitAttribute __instance)
        {
            // Возвращаем всегда: оставленная подменённой доля — это сломанный блок до конца боя.
            if ((object)holding == (object)__instance && __instance != null)
            {
                __instance.blockEPsave = was;
            }

            holding = null;
        }
    }

    // Внутри блока игра первым делом считает урон — этим самым вызовом. Здесь он и известен,
    // и здесь доля выставляется под вес щита, ровно перед тем, как её прочтут.
    [HarmonyPatch(typeof(UnitAttribute), "DamageReduce")]
    internal static class DamageReduce_Bearing_Patch
    {
        private static void Postfix(UnitAttribute __instance, Attack attack, DamageBase __result)
        {
            if ((object)Doblock_Bearing_Patch.holding != (object)__instance) return;
            if (__instance == null || attack == null || __result == null) return;

            try
            {
                UIWeaponInfo shield = Wield.Shield(__instance);
                if (shield == null || shield.weight <= 0f) return;

                float much = __result.Damage();
                float rate = attack.StaminaDamageMD;

                if (much <= 0.01f || rate <= 0f) return;

                __instance.blockEPsave = 1f - shield.weight * Wield.Bearing.Value / (much * rate);
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(HumaniodUnit), "GainWeaponMasteryExp", new[] { typeof(int) })]
    internal static class GainWeaponMasteryExp_Wield_Patch
    {
        private static bool inside;

        private static void Prefix(int exp, out int __state)
        {
            __state = exp;
        }

        private static void Postfix(HumaniodUnit __instance, int __state)
        {
            if (inside) return;

            inside = true;
            try { Wield.Train(__instance, __state); }
            catch (Exception e) { ItemForgePlugin.Log.LogWarning("Мастерство сорвалось: " + e.Message); }
            finally { inside = false; }
        }
    }

    // Нехватка силы больше не запрещает надеть вещь — она делает вещь плохой. Запрет по имени,
    // полу и расе остаётся: это не про то, дорос ли ты, а про то, твоё ли это вообще.
    [HarmonyPatch(typeof(UIEquipmentInfo), "CheckReqirement")]
    internal static class CheckReqirement_Wield_Patch
    {
        private static void Postfix(UIEquipmentInfo __instance, HumaniodUnit unit, ref bool __result)
        {
            if (__result) return;

            try
            {
                if (!Wield.Enabled.Value || !Wield.Advisory.Value) return;
                if (unit == null || unit.Data == null) return;

                // Послабление даётся только тому оружию, которому требование поставили мы.
                // Доспех, которому запрет по силе прописали авторы игры, своё право сохраняет:
                // мы туда не лезли, значит и отменять там нечего.
                UIWeaponInfo blade = __instance as UIWeaponInfo;
                if (blade == null || Wield.Ask(blade) == null) return;

                if (!string.IsNullOrEmpty(__instance.personRquire)
                    && unit.Data.unitname != __instance.personRquire) return;

                if (__instance.gender != UnitGender.none && __instance.gender != unit.Data.gender) return;
                if (__instance.race != UnitRace.none && __instance.race != unit.Data.race) return;

                __result = true;
            }
            catch
            {
            }
        }
    }

    // Требование по мастерству своего поля в окне подсказки не имеет, поэтому дописывается к
    // строке прочности — туда же, где живёт заметка о ремонте.
    [HarmonyPatch(typeof(UIItemTip), "SetValue")]
    internal static class ItemTip_Wield_Patch
    {
        private static System.Reflection.FieldInfo shown;

        private static void Postfix(UIItemTip __instance)
        {
            try
            {
                UnityEngine.UI.Text line = __instance.durabilityText;
                if (line == null || !line.gameObject.activeSelf) return;

                if (shown == null) shown = AccessTools.Field(typeof(UIItemTip), "current");
                if (shown == null) return;

                // Мастерство переехало в блок требований, к ловкости и восприятию. Здесь
                // оно больше не пишется — иначе стояло бы в двух местах разом.
                if (Naming.Enabled != null && Naming.Enabled.Value
                    && Naming.Mastery != null && Naming.Mastery.Value) return;

                string word = Wield.Word(shown.GetValue(__instance) as UIItemInfo);
                if (word == null) return;

                line.horizontalOverflow = UnityEngine.HorizontalWrapMode.Overflow;
                line.text = line.text + "  <size=11>" + word + "</size>";
            }
            catch
            {
            }
        }
    }

}
