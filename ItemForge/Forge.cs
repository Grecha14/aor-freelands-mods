using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Builds the King of Hell set by cloning existing items and overriding their data.
    ///
    /// Cloning rather than building from scratch is what makes this cheap: Instantiate copies
    /// every serialised field, including the reference to the 3D model and the icon, so the new
    /// item looks like its source without a single asset being touched. Only the numbers, the
    /// text and the classification change.
    /// </summary>
    internal static class Forge
    {
        // Far above anything the game ships, so a modded id cannot collide with a real one
        // or with one a future update introduces.
        private const int IdBase = 907100;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> WeightMultiplier;
        internal static ConfigEntry<float> BladeWeight;
        internal static ConfigEntry<float> ShieldWeight;
        internal static ConfigEntry<float> DurabilityMultiplier;
        internal static ConfigEntry<bool> DemonOnly;
        internal static ConfigEntry<float> DarkDamageMin;
        internal static ConfigEntry<float> DarkDamageMax;
        internal static ConfigEntry<float> WeaponScale;
        internal static ConfigEntry<int> Price;
        internal static ConfigEntry<float> Penetration;
        internal static ConfigEntry<string> PenetrationTypes;
        internal static ConfigEntry<float> OrnamentResistance;
        internal static ConfigEntry<float> OrnamentLightResistance;
        internal static ConfigEntry<bool> OrnamentKeepDonorResistance;
        internal static ConfigEntry<bool> ExplainMechanics;
        internal static ConfigEntry<KeyCode> GiveKey;

        private static bool built;
        internal static readonly List<UIEquipmentInfo> Built = new List<UIEquipmentInfo>();

        /// <summary>True for an id this mod hands out, whichever forge produced it.</summary>
        internal static bool IsOurId(int id)
        {
            return id >= IdBase && id < IdBase + 100;
        }

        /// <summary>
        /// How many forged pieces a unit is carrying on its person.
        ///
        /// Both weapon sets are counted, not just the worn one. The game keeps the second set in
        /// an array of its own, and while it is the one in hand the first array holds nothing in
        /// the weapon slots at all — so a check that looked only there saw a character in full
        /// armour and a forged blade as wearing none of it.
        /// </summary>
        /// <summary>
        /// How many forged pieces a unit is carrying on its person.
        ///
        /// Four places have to be looked at, not one. Equipment lives in a plain array of items
        /// and also in an array of slot records, each holding its own inventory entry, and both
        /// exist twice over because a character carries two weapon sets. Which of them answers
        /// depends on how the piece got there, so all four are read and the same object is only
        /// counted once.
        /// </summary>
        internal static int WornCount(UnitAttribute unit)
        {
            HashSet<UIEquipmentInfo> found = Gather(unit);
            return found == null ? 0 : found.Count;
        }

        /// <summary>True when a forged piece for that slot is on.</summary>
        internal static bool WearingSlot(UnitAttribute unit, EquipSlotType slot)
        {
            HashSet<UIEquipmentInfo> found = Gather(unit);
            if (found == null) return false;

            foreach (UIEquipmentInfo piece in found)
            {
                if (piece.EquipType == slot) return true;
            }
            return false;
        }

        /// <summary>Every forged piece the unit has on, gathered from all four holders.</summary>
        internal static HashSet<UIEquipmentInfo> Gather(UnitAttribute unit)
        {
            if (unit == null || Built.Count == 0) return null;

            HumaniodUnit human = unit as HumaniodUnit;
            if (human == null || human.equipmentmanger == null) return null;

            HashSet<UIEquipmentInfo> found = new HashSet<UIEquipmentInfo>();
            EquipmentManager gear = human.equipmentmanger;

            Collect(found, gear.equips);
            Collect(found, gear.standByWeapons);
            Collect(found, gear.equipInfos);
            Collect(found, gear.standByWeaponInfos);

            return found;
        }

        private static void Collect(HashSet<UIEquipmentInfo> found, UIEquipmentInfo[] slots)
        {
            if (slots == null) return;

            foreach (UIEquipmentInfo equipped in slots)
            {
                if (equipped != null && Built.Contains(equipped)) found.Add(equipped);
            }
        }

        private static void Collect(HashSet<UIEquipmentInfo> found, EquipInfo[] slots)
        {
            if (slots == null) return;

            foreach (EquipInfo slot in slots)
            {
                if (slot == null || slot.inventory == null) continue;

                UIEquipmentInfo equipped = slot.inventory.itemInfo as UIEquipmentInfo;
                if (equipped != null && Built.Contains(equipped)) found.Add(equipped);
            }
        }

        /// <summary>The forged blade, for anything that needs to look at it from outside.</summary>
        internal static UIWeaponInfo Blade()
        {
            foreach (UIEquipmentInfo piece in Built)
            {
                UIWeaponInfo weapon = piece as UIWeaponInfo;
                if (weapon != null && weapon.WeaponType != WeaponType.shield) return weapon;
            }
            return null;
        }

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Set", "Enabled", true,
                "Create the King of Hell set when a save is loaded.");

            BladeWeight = config.Bind("Set", "BladeWeight", 9f,
                new ConfigDescription(
                    "What the King's blade weighs, in kilograms, whatever its donor weighed. "
                    + "Nine: a blade the length of a two-handed sword, held in one hand by one "
                    + "who is not a man.",
                    new AcceptableValueRange<float>(0.5f, 40f)));

            ShieldWeight = config.Bind("Set", "ShieldWeight", 1.5f,
                new ConfigDescription(
                    "How much heavier the King's shield is than the rest of the set's rule "
                    + "makes it, as a multiple. Half again: nobody ever broke it, and it was "
                    + "never light.",
                    new AcceptableValueRange<float>(0.1f, 5f)));

            WeightMultiplier = config.Bind("Set", "WeightMultiplier", 0.9f,
                new ConfigDescription(
                    "Weight of every piece, relative to the item it was cloned from. The nine donors "
                    + "weigh 59.1 together, carrying capacity is 35 plus strength, and the penalty "
                    + "starts above 65 per cent of it — so at 50 strength the budget is 55.25 and the "
                    + "set has to come in under that. At 0.9 it weighs 53.2. The set no longer adds "
                    + "strength of its own, so nothing here is paid back by wearing it: raise this "
                    + "above 0.93 and 50 strength is no longer enough.",
                    new AcceptableValueRange<float>(0.1f, 10f)));

            DurabilityMultiplier = config.Bind("Set", "DurabilityMultiplier", 3.0f,
                new ConfigDescription(
                    "Durability of every piece, relative to its source.",
                    new AcceptableValueRange<float>(0.1f, 10f)));

            DarkDamageMin = config.Bind("Set", "DarkDamageMin", 12f,
                new ConfigDescription(
                    "Lowest dark damage the blade adds, on top of the physical damage it already deals.",
                    new AcceptableValueRange<float>(0f, 200f)));

            DarkDamageMax = config.Bind("Set", "DarkDamageMax", 18f,
                new ConfigDescription(
                    "Highest dark damage the blade adds. Set to 0 to add none.",
                    new AcceptableValueRange<float>(0f, 200f)));

            WeaponScale = config.Bind("Set", "WeaponScale", 1.147f,
                new ConfigDescription(
                    "How much larger the blade is drawn. The model is a single mesh, so grip and "
                    + "blade grow together — separating them would mean editing geometry. 1.147 is "
                    + "1.72 over 1.5, the club's length against the blade's, so the sword ends up "
                    + "the size of the weapon the glow was cut for. The glow hangs inside the model "
                    + "and grows with it, so this figure does not change how the two line up.",
                    new AcceptableValueRange<float>(0.1f, 10f)));

            Price = config.Bind("Set", "Price", 666,
                new ConfigDescription(
                    "Price the tooltip shows for every piece, in gold. The stored field is not "
                    + "what is displayed: the game multiplies it by two to the power of the quality "
                    + "step, which is sixteen for legendary, then splits the result into gold, "
                    + "silver and copper at ten thousand and a hundred. The stored number is worked "
                    + "back from this one so the tooltip reads exactly what is set here.",
                    new AcceptableValueRange<int>(1, 100000)));

            Penetration = config.Bind("Set", "Penetration", 20f,
                new ConfigDescription(
                    "How much of the target's resistance the blade ignores, in percent. This lives "
                    + "in the weapon's own resistPen array rather than in a bonus, which is where "
                    + "the penetration the tooltip already showed came from — a bonus on top of it "
                    + "would have been counted twice.",
                    new AcceptableValueRange<float>(0f, 100f)));

            PenetrationTypes = config.Bind("Set", "PenetrationTypes", "sharp,stab,negative",
                "Damage types the penetration above applies to, separated by commas. Every other "
                + "type is set to zero so the tooltip lists only what the blade actually deals. "
                + "Names are the game's own: sharp, blunt, stab, flame, cold, electric, poison, "
                + "positive, negative.");

            OrnamentResistance = config.Bind("Set", "OrnamentResistance", 0f,
                new ConfigDescription(
                    "Resistance every ring, amulet and belt of the set gives against each damage "
                    + "type except light. The donors carry five per cent here and it is not a "
                    + "bonus — it lives in the armour's own damageDR array, which is why "
                    + "re-forging never touched it. Zero strips it. Body armour is never touched: "
                    + "there the same array is what armour is for.",
                    new AcceptableValueRange<float>(-80f, 80f)));

            OrnamentLightResistance = config.Bind("Set", "OrnamentLightResistance", -20f,
                new ConfigDescription(
                    "Resistance against light alone, kept separate so the demon can be made frail "
                    + "to it without touching the rest. Negative subtracts from whatever the "
                    + "wearer has, and no further: the game sums base and modifiers and clamps at "
                    + "zero, so this can strip light resistance but not turn it into extra damage "
                    + "taken. That part is done by the damage patch at nine pieces. Four ornaments "
                    + "are worn, so this is applied four times over.",
                    new AcceptableValueRange<float>(-80f, 80f)));

            OrnamentKeepDonorResistance = config.Bind("Set", "OrnamentKeepDonorResistance", true,
                "Leave every resistance on rings, amulets and belts exactly as the item they were "
                + "cloned from had it, and change light alone. Turn this off to flatten the rest to "
                + "OrnamentResistance instead.");

            GiveKey = config.Bind("Set", "GiveKey", KeyCode.F10,
                "Press this in game to put the whole King of Hell set into the bag of whoever "
                + "is selected: every piece, as it is made.");

            ExplainMechanics = config.Bind("Set", "ExplainMechanics", true,
                "Write what the set does beyond its bonuses into every piece's description. The "
                + "bonus block of a tooltip is built strictly from modifiers — each line takes its "
                + "number and its label from one — so an effect written in code has nowhere to "
                + "appear there, and the description is the only free text an item has.");

            DemonOnly = config.Bind("Set", "DemonOnly", true,
                "Restrict every piece to the demon race.");
        }

        private sealed class Spec
        {
            internal int Source;          // item to clone: model, icon and everything else
            internal string Name;
            internal string Description;
            internal AddonAttributes[] Bonuses;
            internal int TalentFrom;      // borrow the bound talent from this item instead, 0 to keep its own
            internal int IconFrom;        // borrow just the icon from this item, 0 to keep its own
            internal Action<UIEquipmentInfo> Extra;
            internal float PhysicalResist;   // защита от режущего, тупого и колющего; 0 — не трогать
        }

        /// <summary>
        /// What the set does beyond its modifiers, written out for the description.
        ///
        /// The bonus block of a tooltip cannot carry this: every line there is built from one
        /// modifier, taking both its number and its label from it, so an effect that lives in
        /// code has no line to occupy — which is why the seven-piece row shows as empty. The
        /// description is the only free text an item has, so the explanation goes there.
        /// </summary>
        private static string Mechanics()
        {
            if (!ExplainMechanics.Value) return "";

            // Перевод строки строится из кода символа, а не пишется в литерале: этот файл
            // не раз проходил через инструменты, которые ломали экранирование.
            string breakLine = new string((char)10, 2);

            return breakLine + "Сверх бонусов комплекта. Семь частей: критический удар с вероятностью "
                 + "30% отправляет врага в накаут на 8 секунд. Девять частей: получаемый урон "
                 + "светом удваивается. Девять частей: каждые 100 убийств дают +1% силы, без "
                 + "предела. Поножи обращают те же убийства в урон — +1% за каждые 100 убийств "
                 + "до 25%, дальше за каждые 500, а после 50% за каждую тысячу, и тоже без предела.";
        }

        /// <summary>
        /// Rewrites the physical protection of a piece of armour.
        ///
        /// Armour keeps it in the same damageDR array ornaments use for their resistances, but
        /// here the array is not a bonus on top of the item — it is what the armour is for, so
        /// it is set only where a piece asks for it and left alone everywhere else.
        /// </summary>
        private static void SetPhysicalResist(UIEquipmentInfo eq, float value)
        {
            UIArmorInfo armor = eq as UIArmorInfo;
            if (armor == null || armor.damageDR == null || value <= 0f) return;

            armor.damageDR = (float[])armor.damageDR.Clone();
            foreach (DamageType type in new[] { DamageType.sharp, DamageType.blunt, DamageType.stab })
            {
                int index = (int)type;
                if (index < armor.damageDR.Length) armor.damageDR[index] = value;
            }
        }

        private static AddonAttributes A(AddonAttribute type, float value)
        {
            return new AddonAttributes(type, value);
        }

        /// <summary>
        /// Sets the weapon's resistance penetration for the listed damage types and clears it
        /// everywhere else. The array is indexed by DamageType and the clone arrives holding
        /// whatever the donor had, so the types left out are zeroed rather than merely skipped:
        /// otherwise the donor's own values would still show up in the tooltip.
        /// </summary>
        private static void SetPenetration(UIWeaponInfo w)
        {
            int length = Enum.GetValues(typeof(DamageType)).Length;

            // Игра читает массив по индексу типа урона, так что укорачивать его нельзя.
            if (w.resistPen == null || w.resistPen.Length < length)
            {
                float[] grown = new float[length];
                if (w.resistPen != null) Array.Copy(w.resistPen, grown, w.resistPen.Length);
                w.resistPen = grown;
            }
            else
            {
                w.resistPen = (float[])w.resistPen.Clone();
            }

            ItemForgePlugin.Log.LogInfo("Проникновение донора: [" + string.Join(", ",
                Array.ConvertAll(w.resistPen, v => v.ToString())) + "]");

            for (int i = 0; i < w.resistPen.Length; i++) w.resistPen[i] = 0f;

            foreach (string raw in (PenetrationTypes.Value ?? "").Split(','))
            {
                string name = raw.Trim();
                if (name.Length == 0) continue;

                try
                {
                    int index = (int)(DamageType)Enum.Parse(typeof(DamageType), name, true);
                    if (index >= 0 && index < w.resistPen.Length) w.resistPen[index] = Penetration.Value;
                }
                catch { ItemForgePlugin.Log.LogWarning($"Не знаю тип урона '{name}', пропускаю."); }
            }
        }

        /// <summary>
        /// Rewrites the resistances an ornament carries.
        ///
        /// These do not live in the bonus list, which is why forging never changed them: armour
        /// keeps them in a damageDR array of its own, one entry per damage type, and rings and
        /// amulets are armour as far as the data model is concerned. Body armour is left alone —
        /// there the same array is what makes armour armour, not a bonus on top.
        /// </summary>
        private static void SetOrnamentResistance(UIEquipmentInfo eq)
        {
            UIArmorInfo armor = eq as UIArmorInfo;
            if (armor == null || armor.damageDR == null) return;

            if (eq.EquipType != EquipSlotType.finger
                && eq.EquipType != EquipSlotType.neck
                && eq.EquipType != EquipSlotType.belt) return;

            ItemForgePlugin.Log.LogInfo($"Сопротивления донора «{eq.Name}»: [" + string.Join(", ",
                Array.ConvertAll(armor.damageDR, v => v.ToString())) + "]");

            armor.damageDR = (float[])armor.damageDR.Clone();

            if (!OrnamentKeepDonorResistance.Value)
            {
                for (int i = 0; i < armor.damageDR.Length; i++) armor.damageDR[i] = OrnamentResistance.Value;
            }

            int light = (int)DamageType.positive;
            if (light < armor.damageDR.Length) armor.damageDR[light] = OrnamentLightResistance.Value;
        }

        private static Spec[] BuildSpecs()
        {
            return new[]
            {
                new Spec
                {
                    Source = 1896,
                    TalentFrom = 829,   // «Бедствие» с двуручной палицы: единственное в игре
                                        // снижение воли противника
                    PhysicalResist = 15f,
                    Name = "Венец Короля Ада",
                    Description =
                        "Корону сковали из светильников преисподней, что горели, пока их не погасил "
                        + "взгляд их господина. Он носил её не ради власти — власть у него была и так, — "
                        + "а чтобы подданным было куда смотреть, когда смотреть в лицо они не смели.",
                    Bonuses = new[]
                    {
                        A(AddonAttribute.Threat, 0.5f), A(AddonAttribute.Crit, 5),
                        A(AddonAttribute.CritMultiple, 0.15f),
                    },
                },
                new Spec
                {
                    Source = 1895,
                    TalentFrom = 2305, // DragonArmor — снижение всего получаемого урона на 15%
                    PhysicalResist = 35f,
                    Name = "Доспех Короля Ада",
                    Description =
                        "Кирасу ковали девять кузнецов девять лет, и каждый умер, закончив свою часть. "
                        + "Говорят, последний успел сказать, что металл остывал неохотно — будто "
                        + "понимал, на чьи плечи ляжет, и не спешил расставаться с жаром.",
                    Bonuses = new[]
                    {
                        A(AddonAttribute.Threat, 1f), A(AddonAttribute.CritMultiple, 0.25f),
                        A(AddonAttribute.DamageIncrease, 0.1f),
                    },
                },
                new Spec
                {
                    Source = 1894,
                    TalentFrom = 1931, // вампиризм с доспехов «Красный лотос»
                    Name = "Поножи Короля Ада",
                    Description =
                        "Под этими сапогами камень преисподней плавился и застывал снова, оставляя "
                        + "дорогу там, где её не было. В мире смертных они ступают тише — земля здесь "
                        + "мягче и не запоминает, кто по ней прошёл.",
                    Bonuses = new[]
                    {
                        A(AddonAttribute.MoveSpeed, 0.15f), A(AddonAttribute.AttackSpeed, 0.1f),
                    },
                },
                new Spec
                {
                    Source = 1245,      // Бедствие рыбаков: и модель, и собственный навык
                    Name = "Клинок Короля Ада",
                    Description =
                        "Клинок длиной в двуручный меч, но Король держит его одной рукой — не потому, "
                        + "что тот лёгок, а потому, что вторая рука ему нужна для щита. Смертные, "
                        + "пробовавшие повторить, роняли его себе на ноги.",
                    Bonuses = new[]
                    {
                        A(AddonAttribute.NegativeSpellDamageModifier, 0.3f),
                        A(AddonAttribute.AttackSpeed, 0.2f), A(AddonAttribute.CritMultiple, 0.2f),
                        A(AddonAttribute.WeaponForce, 0.35f),
                    },
                    Extra = item =>
                    {
                        UIWeaponInfo w = item as UIWeaponInfo;
                        if (w == null) return;

                        // Одноручный по хвату, двуручный по родству: игра штатно засчитывает
                        // оба типа и в требованиях умений, и в начислении мастерства.
                        w.WeaponType = WeaponType.onehand;
                        w.WeaponType_Secondary = WeaponType.twohand;
                        w.AnimationType = AnimationType.onehand;
                        w.AnimationSubType = AnimationSubType.Sword;

                        w.spCost = 30f;
                        w.weight = BladeWeight.Value;
                        w.AttackAngle = Mathf.Min(w.AttackAngle * 1.2f, 360f);
                        w.AttackSpeed = Mathf.Min(w.AttackSpeed * 1.2f, 2f);

                        // Урон оружия — словарь по типам, поэтому тьма добавляется третьей
                        // строкой рядом с режущим и колющим, а не вместо них.
                        if (w.damage != null && DarkDamageMax.Value > 0f)
                        {
                            w.damage = w.damage + new Damage(DamageType.negative,
                                DarkDamageMin.Value, DarkDamageMax.Value);
                        }

                        SetPenetration(w);
                    },
                },
                new Spec
                {
                    Source = 2354,   // модель гигантского крабового щита
                    TalentFrom = 1255, // а иммунитет к опрокидыванию — от корня Осителя
                    Name = "Эгида Короля Ада",
                    Description =
                        "Щит, о который разбилось восстание. Его не пробили ни разу, и потому никто "
                        + "не знает, что под слоем панциря — металл, кость или вовсе ничего, кроме "
                        + "упрямства владельца.",
                    Bonuses = new[]
                    {
                        A(AddonAttribute.Threat, 1f), A(AddonAttribute.DamageIncrease, 0.1f),
                        A(AddonAttribute.BlockAngle, 0.2f),
                    },
                    Extra = item =>
                    {
                        UIWeaponInfo w = item as UIWeaponInfo;
                        if (w == null) return;

                        w.weaponClass = WeaponClass.Buckler;
                        w.weight *= ShieldWeight.Value;

                        // Угол блока нигде не зажимается: игра сравнивает угол до атакующего
                        // с половиной этого числа, так что 180 закрывает переднюю полусферу,
                        // а 360 закрыло бы и спину. Бонус выше умножает это значение.
                        w.BlockAngle = 180f;
                        w.BlockRate = 35f;
                    },
                },
                new Spec
                {
                    Source = 1117,
                    TalentFrom = 1241, // заморозка с топора «Морозный клык»
                    IconFrom = 663,    // а картинка — от амулета «Песня тени»
                    Name = "Амулет Короля Ада",
                    Description =
                        "Внутри амулета заперт огонь, которым Король однажды сжёг город, забывший его "
                        + "имя. Огонь всё ещё горит и всё ещё помнит название города. Больше его не "
                        + "помнит никто.",
                    Bonuses = new[]
                    {
                        A(AddonAttribute.WeaponForce, 0.35f), A(AddonAttribute.MeleeDamage, 0.12f),
                        A(AddonAttribute.DamageIncrease, 0.1f),
                    },
                },
                new Spec
                {
                    Source = 2035,
                    TalentFrom = 693,   // эффект сабли «Командир судьбы» переехал сюда с клинка
                    PhysicalResist = 10f,
                    Name = "Пояс Короля Ада",
                    Description =
                        "Пояс сплетён из жил тех, кто пытался свергнуть его владельца. Их было немного: "
                        + "пояс узкий. Зато каждый, кто видел его вблизи, понимал, что узким он остался "
                        + "не от недостатка желающих.",
                    Bonuses = new[]
                    {
                        A(AddonAttribute.DamageIncrease, 0.1f), A(AddonAttribute.AttackSpeed, 0.1f),
                        A(AddonAttribute.Threat, 0.5f),
                    },
                },
                new Spec
                {
                    Source = 2506,   // модель Кольца Потенциала
                    TalentFrom = 1264, // «Праведность» с двуручного молота: пробой брони
                    Name = "Печать Короля Ада",
                    Description =
                        "Этой печатью Король скреплял договоры, и ни один из них не был нарушен — не "
                        + "из честности сторон, а потому что нарушивших не оставалось. Печать до сих "
                        + "пор холодна на ощупь, хотя лежала в огне.",
                    Bonuses = new[]
                    {
                        A(AddonAttribute.Crit, 8), A(AddonAttribute.CritMultiple, 0.15f),
                        A(AddonAttribute.WeaponForce, 0.25f),
                    },
                },
                new Spec
                {
                    Source = 2527,
                    TalentFrom = 1248, // «Перелом» с молота «Костолом»: замедление цели
                    Name = "Перстень Короля Ада",
                    Description =
                        "Перстень шепчет. Раньше он шептал советы, от которых дрожали легионы; теперь, "
                        + "в мире смертных, он советует то же самое, но слушать стало некому, и Король "
                        + "впервые за вечность слышит собственные мысли.",
                    Bonuses = new[]
                    {
                        A(AddonAttribute.Crit, 5), A(AddonAttribute.DamageIncrease, 0.1f),
                        A(AddonAttribute.WeaponForce, 0.25f),
                    },
                },
            };
        }
        internal static void Build()
        {
            if (built || !Enabled.Value) return;

            try
            {
                // Само обращение к базе может бросить, пока она не поднята: это не поломка,
                // а «ещё рано», и шуметь об этом в лог незачем.
                UIItemDatabase db;
                try { db = UIItemDatabase.Instance; }
                catch { return; }

                if (db == null || db.items == null) return;

                Spec[] specs = BuildSpecs();
                List<UIItemInfo> created = new List<UIItemInfo>();
                Built.Clear();

                int id = IdBase;
                foreach (Spec spec in specs)
                {
                    UIItemInfo source = db.GetByID(spec.Source);
                    if (source == null)
                    {
                        ItemForgePlugin.Log.LogError($"Не нашёл предмет-основу {spec.Source} для «{spec.Name}», пропускаю.");
                        continue;
                    }

                    // Instantiate копирует все сериализованные поля, включая ссылку на модель
                    // и иконку. Поэтому копия выглядит как оригинал, не трогая ни одного ассета.
                    UIItemInfo clone = UnityEngine.Object.Instantiate(source);
                    UnityEngine.Object.DontDestroyOnLoad(clone);

                    clone.name = "KingOfHell_" + spec.Source;
                    clone.ID = id++;
                    clone.Name = spec.Name;
                    clone.Description = spec.Description + Mechanics();
                    clone.isUnique = true;

                    // Подсказка показывает не поле value, а Inventory.Value: цену, умноженную
                    // на ступень цвета и урезанную износом. Считаем обратно, чтобы в подсказке
                    // стояло заданное число, и в золоте: монеты показываются тройкой
                    // золото-серебро-медь, где золото это десять тысяч.
                    //
                    // Множитель спрашиваем у того, кто его на самом деле накладывает. Игра
                    // удваивает на каждой ступени — для легендарного это ×16, — но если своя
                    // лестница цен включена, считает уже она, и втрое вместо шестнадцати.
                    // Раньше здесь стояло игровое ×16 всегда, и сет показывал сто двадцать
                    // пять золотых вместо шестисот шестидесяти шести.
                    float step = Rarity.Enabled.Value ? Rarity.Worth(clone.Quality) : -1f;
                    if (step <= 0f) step = Mathf.Pow(2f, Mathf.Max(0, (int)clone.Quality - 1));

                    // И своя доля легендарного, если она задана: она накладывается там же, при
                    // чтении цены, и без этого деления сет показывал бы половину назначенного.
                    if (Enchant.Enabled != null && Enchant.Enabled.Value
                        && Enchant.LegendPrice != null && Enchant.LegendPrice.Value > 0.01f)
                    {
                        step *= Enchant.LegendPrice.Value;
                    }

                    clone.value = Mathf.RoundToInt(Price.Value * 10000f / step);
                    clone.weight = source.weight * WeightMultiplier.Value;
                    clone.durability = source.durability * DurabilityMultiplier.Value;

                    UIEquipmentInfo eq = clone as UIEquipmentInfo;
                    if (eq != null)
                    {
                        if (DemonOnly.Value) eq.race = UnitRace.demon;

                        eq.addAttrs = new List<AddonAttributes>();
                        if (spec.Bonuses != null)
                        {
                            foreach (AddonAttributes bonus in spec.Bonuses) eq.addAttrs.Add(bonus);
                        }

                        // Иконка берётся отдельно от модели: в сумке вещь узнают по картинке,
                        // а клон наследует её от донора и в списке сливается с оригиналом.
                        if (spec.IconFrom != 0)
                        {
                            UIItemInfo art = db.GetByID(spec.IconFrom);
                            if (art != null && art.Icon != null)
                            {
                                clone.Icon = art.Icon;
                                ItemForgePlugin.Log.LogInfo($"«{spec.Name}»: иконка от [{spec.IconFrom}] «{art.Name}».");
                            }
                            else ItemForgePlugin.Log.LogWarning($"Не нашёл иконку {spec.IconFrom} для «{spec.Name}».");
                        }

                        if (spec.TalentFrom != 0)
                        {
                            UIEquipmentInfo donor = db.GetByID(spec.TalentFrom) as UIEquipmentInfo;
                            if (donor != null)
                            {
                                // Уникальный эффект вещи складывается из трёх полей, а не одного.
                                // Перенести только талант мало: клон сохранял заклинание своей
                                // модели и показывал чужой эффект вместо нужного.
                                eq.bindTalent = donor.bindTalent;
                                eq.spell = donor.spell;
                                eq.learnTrait = donor.learnTrait;

                                // Текст уникального навыка подсказка берёт вообще не из
                                // таланта, а из readContent — поля, которое выглядит как
                                // содержимое книги и им же является для книг. Без него
                                // вещь несла чужое описание при верном эффекте.
                                eq.readContent = donor.readContent;

                                ItemForgePlugin.Log.LogInfo($"«{spec.Name}»: эффект от [{spec.TalentFrom}] "
                                    + $"«{donor.Name}» — талант {(donor.bindTalent != null ? donor.bindTalent.name : "нет")}, "
                                    + $"заклинание {(donor.spell != null ? donor.spell.name : "нет")}, "
                                    + $"черта {(donor.learnTrait != null ? donor.learnTrait.name : "нет")}.");
                            }
                            else ItemForgePlugin.Log.LogWarning($"Не нашёл предмет {spec.TalentFrom} для «{spec.Name}».");
                        }

                        // Свечение вшивается в префаб модели, а не вешается на готовую вещь:
                        // только так оно попадает и в окно персонажа, как у игровых образцов.
                        UIWeaponInfo blade = eq as UIWeaponInfo;
                        if (blade != null && blade.WeaponType != WeaponType.shield) WeaponVfx.InjectInto(eq);

                        SetOrnamentResistance(eq);
                        SetPhysicalResist(eq, spec.PhysicalResist);

                        if (spec.Extra != null) spec.Extra(eq);
                        Built.Add(eq);
                    }

                    created.Add(clone);
                }

                if (created.Count == 0) return;

                Append(db, created);
                MakeSet(db);

                built = true;
                ItemForgePlugin.Log.LogInfo($"Комплект «Король Ада» создан: {created.Count} предметов, id с {IdBase}.");

                // Выдачи больше нет: комплект добывается в мире, а не появляется в сумке.
                // Обновление остаётся — вещи, уже лежащие в сохранении, надо переподвязать
                // к заново созданным описаниям, иначе после перезапуска они осиротеют.
                Refresh(created);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не удалось создать комплект: " + e);
            }
        }

        private static UIItemInfo Find(UIItemDatabase db, string token)
        {
            int id;
            if (int.TryParse(token, out id)) return db.GetByID(id);

            foreach (UIItemInfo item in db.items)
            {
                if (item == null) continue;
                if (item.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) return item;
                if (item.Name != null && item.Name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) return item;
            }
            return null;
        }

        /// <summary>
        /// Names the consumables that look like what was asked for, when nothing matched.
        ///
        /// A fragment that finds nothing is useless on its own — the next guess would be just as
        /// blind. Listing the potions the game actually has turns one failed attempt into the
        /// answer, since the right name is somewhere in that list.
        /// </summary>
        private static void Suggest(UIItemDatabase db, string token)
        {
            List<string> potions = new List<string>();
            List<string> others = new List<string>();

            foreach (UIItemInfo item in db.items)
            {
                if (item == null || item.itemType != ItemType.Consumable) continue;

                string line = $"[{item.ID}] {item.name}";

                if (item.name.IndexOf("potion", StringComparison.OrdinalIgnoreCase) >= 0
                    || item.name.IndexOf("exp", StringComparison.OrdinalIgnoreCase) >= 0
                    || item.name.IndexOf("scroll", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (potions.Count < 40) potions.Add(line);
                }
                else if (others.Count < 10) others.Add(line);
            }

            if (potions.Count > 0)
            {
                ItemForgePlugin.Log.LogInfo($"Зелья и свитки ({potions.Count}): "
                    + string.Join(", ", potions.ToArray()));
            }
            else if (others.Count > 0)
            {
                ItemForgePlugin.Log.LogInfo("Расходники: " + string.Join(", ", others.ToArray()));
            }
        }
        /// <summary>
        /// Points anything already worn or carried at the freshly built pieces.
        ///
        /// A piece keeps its id across sessions, but the object behind that id is rebuilt every
        /// time the game starts. Whatever the save restored is therefore an older build's work,
        /// with that build's numbers and that build's unique effect, and forging again does not
        /// reach it — it only adds new copies to the bag. So each holder is walked and rehung on
        /// the current object, and what was found is written down: the name of the effect on the
        /// old piece says at a glance which build it came from.
        /// </summary>
        private static HashSet<int> Refresh(List<UIItemInfo> created)
        {
            HashSet<int> present = new HashSet<int>();

            try
            {
                PartyManager party = PartyManager.instance;
                if (party == null || party.partyMembers == null) return present;

                Dictionary<int, UIItemInfo> fresh = new Dictionary<int, UIItemInfo>();
                foreach (UIItemInfo item in created) fresh[item.ID] = item;

                int fixedUp = 0;

                foreach (HumaniodUnit unit in party.partyMembers)
                {
                    if (unit == null || unit.equipmentmanger == null) continue;

                    UIEquipmentInfo[] equips = unit.equipmentmanger.equips;
                    if (equips != null)
                    {
                        for (int i = 0; i < equips.Length; i++)
                        {
                            Note(present, fresh, equips[i]);

                            UIItemInfo replacement = Stale(fresh, equips[i], "надето");
                            if (replacement == null) continue;
                            equips[i] = replacement as UIEquipmentInfo;
                            fixedUp++;
                        }
                    }

                    // У каждой надетой вещи есть своя запись инвентаря, и подсказка читает
                    // именно её, а не предмет из базы.
                    EquipInfo[] slots = unit.equipmentmanger.equipInfos;
                    if (slots != null)
                    {
                        foreach (EquipInfo slot in slots)
                        {
                            if (slot == null) continue;
                            if (slot.inventory != null) Note(present, fresh, slot.inventory.itemInfo);
                            if (Rehang(fresh, slot.inventory, "надето")) fixedUp++;
                        }
                    }

                    ItemStock stock = unit.equipmentmanger.inventory;
                    if (stock == null || stock.items == null) continue;

                    foreach (Inventory slot in stock.items)
                    {
                        if (slot != null) Note(present, fresh, slot.itemInfo);
                        if (Rehang(fresh, slot, "в сумке")) fixedUp++;
                    }
                }

                if (fixedUp > 0) ItemForgePlugin.Log.LogInfo($"Обновлено старых экземпляров: {fixedUp}.");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог обновить старые вещи: " + e);
            }

            return present;
        }

        /// <summary>Records that the party already holds this piece, so it is not handed out twice.</summary>
        private static void Note(HashSet<int> present, Dictionary<int, UIItemInfo> fresh, UIItemInfo held)
        {
            if (held != null && fresh.ContainsKey(held.ID)) present.Add(held.ID);
        }

        /// <summary>
        /// Puts one carried copy back in step with the freshly built piece.
        ///
        /// An inventory entry does not read the item's numbers as it needs them: it copies the
        /// bonus list once, when the item enters the bag, and keeps that copy for good. So
        /// pointing the entry at the new object is only half the job — the copied bonuses have
        /// to be taken again as well, or the tooltip goes on showing what an older build of this
        /// mod wrote there, however many times the set is forged anew.
        /// </summary>
        private static bool Rehang(Dictionary<int, UIItemInfo> fresh, Inventory slot, string where)
        {
            if (slot == null) return false;

            UIItemInfo replacement = Stale(fresh, slot.itemInfo, where);
            if (replacement == null) return false;

            slot.itemInfo = replacement;
            slot.quality = replacement.Quality;

            UIEquipmentInfo eq = replacement as UIEquipmentInfo;
            slot.addAttrs = eq != null && eq.addAttrs != null
                ? new List<AddonAttributes>(eq.addAttrs)
                : new List<AddonAttributes>();

            return true;
        }

        /// <summary>The fresh piece an old one should become, or null when it is already current.</summary>
        private static UIItemInfo Stale(Dictionary<int, UIItemInfo> fresh, UIItemInfo held, string where)
        {
            if (held == null) return null;

            UIItemInfo replacement;
            if (!fresh.TryGetValue(held.ID, out replacement)) return null;
            if (ReferenceEquals(held, replacement)) return null;

            UIEquipmentInfo old = held as UIEquipmentInfo;
            string talent = old != null && old.bindTalent != null ? old.bindTalent.name : "нет";
            ItemForgePlugin.Log.LogWarning($"Старое «{held.Name}» ({where}, id {held.ID}) несёт талант "
                + $"{talent} — заменяю на свежее.");
            return replacement;
        }

        private static void Append(UIItemDatabase db, List<UIItemInfo> created)
        {
            // Записи прошлой ковки выбрасываются: иначе база копит поколение за поколением,
            // и поиск по номеру начинает отдавать давно замещённую вещь.
            List<UIItemInfo> kept = new List<UIItemInfo>(db.items.Length + created.Count);
            foreach (UIItemInfo item in db.items)
            {
                if (item != null && IsOurId(item.ID)) continue;
                kept.Add(item);
            }

            kept.AddRange(created);
            db.items = kept.ToArray();
        }

        private static void MakeSet(UIItemDatabase db)
        {
            EquipmentSet set = ScriptableObject.CreateInstance<EquipmentSet>();
            UnityEngine.Object.DontDestroyOnLoad(set);

            set.name = "KingOfHellSet";
            set.Name = "Король Ада";
            set.quality = UIItemQuality.Legendary;
            set.parts = new List<UIEquipmentInfo>(Built);
            set.bonus = new List<SetBonus>
            {
                // Три части. Прежде здесь стояло восстановление здоровья и маны в секунду, но
                // лечение теперь идёт часами и через лекаря, и пять в секунду почти ничего не
                // стоили. На их месте — дыхание: сет тяжёл, и махать им надо уметь.
                new SetBonus(3, new List<AddonAttributes>
                {
                    A(AddonAttribute.NegativeSpellDamageModifier, 0.25f),
                    A(AddonAttribute.EPsave, 0.2f),
                }),
                new SetBonus(5, new List<AddonAttributes>
                {
                    A(AddonAttribute.DamageIncrease, 0.25f),
                    A(AddonAttribute.CritMultiple, 0.25f),
                }),
                // За семь частей — нокаут по критическому удару. Это не прибавка, а поведение,
                // поэтому порог держится пустым, а работу делает патч.
                // Порога в семь частей в данных нет намеренно. Накаут на критах есть и
                // работает, но он сделан кодом, а строка комплекта умеет показать только
                // модификатор — пустой порог рисовался пустой строкой и выглядел поломкой.
                new SetBonus(9, new List<AddonAttributes>
                {
                    A(AddonAttribute.NegativeSpellDamageModifier, 0.75f),
                    // Доспех уже несёт драконьи 15% через талант — вместе выходит сорок пять.
                    A(AddonAttribute.DamageReduce, 0.30f),
                    // И ещё дыхания: вместе с тремя частями — две пятых от расхода.
                    A(AddonAttribute.EPsave, 0.2f),
                }),
            };

            foreach (UIEquipmentInfo part in Built) part.set = set;

            if (db.sets == null) db.sets = new EquipmentSet[0];
            EquipmentSet[] extended = new EquipmentSet[db.sets.Length + 1];
            Array.Copy(db.sets, extended, db.sets.Length);
            extended[db.sets.Length] = set;
            db.sets = extended;

            ItemForgePlugin.Log.LogInfo($"Комплект зарегистрирован: {set.Name}, частей {set.parts.Count}, порогов бонусов {set.bonus.Count}.");
        }
    }

    /// <summary>Hands the whole King of Hell set over, by a key.</summary>
    internal static class SetGift
    {
        internal static void Hand()
        {
            try
            {
                HumaniodUnit who = gameManager.currentplayUnit as HumaniodUnit;
                if (who == null || who.items == null)
                {
                    ItemForgePlugin.Log.LogWarning("Комплект Короля Ада: некому выдавать, игра ещё не началась.");
                    return;
                }

                if (Forge.Built.Count == 0)
                {
                    ItemForgePlugin.Log.LogWarning("Комплект Короля Ада ещё не собран: мир не загружен.");
                    return;
                }

                int count = 0;

                foreach (UIEquipmentInfo piece in Forge.Built)
                {
                    if (piece == null) continue;

                    Inventory bit = new Inventory(piece, 1);

                    if (InventoryManager.instance != null
                        && (object)InventoryManager.instance.currentTarget == (object)who)
                    {
                        InventoryManager.instance.PutItemInbag(bit);
                    }
                    else
                    {
                        who.items.AddInventory(bit);
                    }

                    count++;
                }

                ItemForgePlugin.Log.LogInfo("Комплект Короля Ада выдан: вещей " + count + ".");
                GameController.ShowMessage("Комплект Короля Ада в сумке: " + count + " вещей", 3f);
            }
            catch (System.Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог выдать комплект Короля Ада: " + e);
            }
        }
    }
}
