using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Gives good amulets something to be good at: standing between their wearer and magic.
    ///
    /// Броня против заклинаний бесполезна — и по замыслу, и теперь буквально: доля доспеха
    /// для магии равна нулю. Значит против неё нужно что-то другое, и место для этого в игре
    /// уже есть. Амулет — единственная вещь, которая не закрывает тело и не держит удар; она
    /// носится ради того, чем является, а не ради того, из чего сделана.
    ///
    /// Стат не выдуман: игра сама считает девять сопротивлений по родам урона, шесть из них
    /// магические, и показывает их в подсказке. Мы лишь дописываем их хорошим амулетам —
    /// синим и выше, от второго яруса, — так что дальше всё идёт её обычным путём: подсказка,
    /// расчёт, общий потолок сопротивлений.
    /// </summary>
    internal static class Ward
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> PerTier;
        internal static ConfigEntry<float> PerQuality;
        internal static ConfigEntry<string> LeastTier;
        internal static ConfigEntry<string> LeastQuality;

        internal static ConfigEntry<float> Hold;
        internal static ConfigEntry<int> Rest;
        internal static ConfigEntry<float> Shatter;
        internal static ConfigEntry<float> WitHaste;

        private static bool done;

        // Когда каждый оберег в последний раз считался, в секундах живого времени: по разнице
        // и набегает восполнение, ровно как у самой маны.
        private static readonly Dictionary<Inventory, float> counted = new Dictionary<Inventory, float>();

        // Шесть родов урона, против которых доспех не работает.
        private static readonly AddonAttribute[] schools = new AddonAttribute[]
        {
            AddonAttribute.FlameDamageResistance,
            AddonAttribute.ColdDamageResistance,
            AddonAttribute.ElectricDamageResistance,
            AddonAttribute.PoisonDamageResistance,
            AddonAttribute.PositiveDamageResistance,
            AddonAttribute.NegativeDamageResistance,
        };

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Ward", "Enabled", true,
                "Let a good amulet absorb magic. Armour is no answer to a spell and is not "
                + "meant to be; without something that is, magic is simply a thing that "
                + "happens to you. An amulet covers nothing and stops no blow — which is "
                + "exactly why it can be the thing that does this instead.");

            PerTier = config.Bind("Ward", "PerTier", 4f,
                new ConfigDescription(
                    "How much resistance each tier above the first is worth, in points against "
                    + "every magic school.",
                    new AcceptableValueRange<float>(0f, 25f)));

            PerQuality = config.Bind("Ward", "PerQuality", 3f,
                new ConfigDescription(
                    "How much each step of quality above uncommon is worth, on top of the tier.",
                    new AcceptableValueRange<float>(0f, 25f)));

            LeastTier = config.Bind("Ward", "LeastTier", "T2",
                "The lowest tier that carries the ward: T0 through T5.");

            LeastQuality = config.Bind("Ward", "LeastQuality", "Rare",
                "The lowest quality that carries it. Rare is the blue one — below that an "
                + "amulet is a trinket, and a trinket that turns aside fire is not a trinket.");

            Hold = config.Bind("Ward", "Hold", 1f,
                new ConfigDescription(
                    "How much magic one point of the amulet drinks. The ward is a vessel, not "
                    + "a wall: it takes the spell into itself and empties as it does, exactly "
                    + "the way a breastplate is used up by being hit. What it holds is its own "
                    + "durability, and the bar over it is the honest measure of how much is "
                    + "left before the next fireball is the wearer’s problem.",
                    new AcceptableValueRange<float>(0.1f, 10f)));

            Rest = config.Bind("Ward", "Rest", 1,
                new ConfigDescription(
                    "How many days an emptied amulet takes to fill itself again. It fills "
                    + "steadily rather than all at once, so a ward half spent this morning is "
                    + "half back by evening. Nothing needs doing about it: a smith cannot mend "
                    + "what is not broken, and what refills it is time.",
                    new AcceptableValueRange<int>(0, 30)));

            WitHaste = config.Bind("Ward", "WitHaste", 2.4f,
                new ConfigDescription(
                    "How many points of absorption the ward gains each hour of the world, for "
                    + "every point of the wearer's mana regeneration. An amulet is not a "
                    + "waterskin filled from a stream — it fills from the person wearing it, "
                    + "out of the same well his spells come from, and it fills by the world's "
                    + "clock rather than by ours: a night at an inn is worth a night.",
                    new AcceptableValueRange<float>(0.01f, 1000f)));

            Shatter = config.Bind("Ward", "Shatter", 0f,
                new ConfigDescription(
                    "The chance that a blow finds the amulet, the ring or the belt itself "
                    + "rather than the man wearing it, and ends it. Small things worn openly "
                    + "can be destroyed, and an enemy who knows what the amulet is doing has "
                    + "every reason to aim at it.",
                    new AcceptableValueRange<float>(0f, 0.5f)));
        }

        /// <summary>
        /// Once wrote resistances into every good amulet. No longer does.
        ///
        /// Сопротивления магии теперь такие, как в самой игре: у кого из амулетов они есть от
        /// автора вещи — у того и есть, остальным мы их больше не дописываем. Оберег — то, что
        /// принимает заклинание в себя, — остаётся только у именных амулетов, которым игра
        /// сама дала защиту от магии (см. <see cref="Keeps"/>).
        /// </summary>
        internal static void Enchant()
        {
            done = true;
        }

        /// <summary>The old enchanting, kept for reference and never called.</summary>
        private static void EnchantOld()
        {
            if (done || !Enabled.Value) return;

            try
            {
                UIItemDatabase db;
                try { db = UIItemDatabase.Instance; }
                catch { return; }

                if (db == null || db.items == null) return;

                ItemTier leastTier;
                UIItemQuality leastQuality;

                try
                {
                    leastTier = (ItemTier)Enum.Parse(typeof(ItemTier), LeastTier.Value.Trim(), true);
                    leastQuality = (UIItemQuality)Enum.Parse(typeof(UIItemQuality),
                        LeastQuality.Value.Trim(), true);
                }
                catch
                {
                    ItemForgePlugin.Log.LogWarning("Не понял ярус или качество оберега.");
                    return;
                }

                done = true;
                int given = 0;

                foreach (UIItemInfo any in db.items)
                {
                    UIEquipmentInfo neck = any as UIEquipmentInfo;
                    if (neck == null || neck.EquipType != EquipSlotType.neck) continue;
                    if (((UIItemInfo)neck).tier < leastTier) continue;
                    if (((UIItemInfo)neck).Quality < leastQuality) continue;

                    float much = Worth(neck);
                    if (much <= 0f) continue;

                    if (neck.addAttrs == null) neck.addAttrs = new List<AddonAttributes>();

                    // Дописываем только недостающие школы. У части амулетов сопротивление
                    // магии стоит и без нас — родное, от автора вещи; его не трогаем и не
                    // задваиваем, а доводим набор до полного. Так и легендарки, и собственные
                    // обереги игры оказываются под одним правилом.
                    int added = 0;

                    foreach (AddonAttribute school in schools)
                    {
                        if (Carries(neck, school)) continue;

                        AddonAttributes ward = new AddonAttributes();
                        ward.type = school;
                        ward.value = much;
                        ward.levelAlter = 0f;

                        neck.addAttrs.Add(ward);
                        added++;
                    }

                    if (added > 0) given++;
                }

                if (given > 0)
                {
                    ItemForgePlugin.Log.LogInfo($"Оберег вписан в амулеты: {given} "
                        + $"(от {leastTier}, от {leastQuality}).");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог вписать оберег: " + e);
            }
        }

        /// <summary>How deep this amulet is, by tier and by rarity together.</summary>
        internal static float Grade(UIEquipmentInfo neck)
        {
            if (neck == null) return 1f;

            ItemTier leastTier;
            UIItemQuality leastQuality;

            try
            {
                leastTier = (ItemTier)Enum.Parse(typeof(ItemTier), LeastTier.Value.Trim(), true);
                leastQuality = (UIItemQuality)Enum.Parse(typeof(UIItemQuality),
                    LeastQuality.Value.Trim(), true);
            }
            catch { return 1f; }

            int tier = Mathf.Max(1, (int)((UIItemInfo)neck).tier - (int)leastTier + 1);
            int fine = Mathf.Max(1, (int)((UIItemInfo)neck).Quality - (int)leastQuality + 1);

            // Перемножаем, а не складываем: хороший амулет хорош дважды, и разрыв между
            // синей двойкой и легендарной пятёркой должен быть виден без таблицы.
            return tier * fine;
        }

        /// <summary>How much this amulet turns aside, by what it is.</summary>
        private static float Worth(UIEquipmentInfo neck)
        {
            int tier = Mathf.Max(0, (int)((UIItemInfo)neck).tier - 1);
            int fine = Mathf.Max(0, (int)((UIItemInfo)neck).Quality - (int)UIItemQuality.Uncommon);

            return Mathf.Round(tier * PerTier.Value + fine * PerQuality.Value);
        }

        /// <summary>
        /// Leaves a drop in an empty ward the moment a living hand takes it.
        ///
        /// Выпитый досуха оберег нельзя надеть: игра прямо запрещает надевать вещь на
        /// нулевой прочности. Для сапог это правильно, а для амулета выходит ловушка:
        /// он опустел, потому что делал свою работу, и за это его больше нельзя носить,
        /// а значит и наполнить — восполняется только надетое. Единица разрывает этот круг.
        ///
        /// Только в руках у живого. Пока амулет лежит на трупе — ноль, и в описи ноль:
        /// выпитый оберег остаётся выпитым.
        /// </summary>
        internal static void Revive(ItemStock where, Inventory inv)
        {
            if (!Enabled.Value || where == null || inv == null) return;
            if (inv.itemInfo == null || inv.durability > 0f) return;

            try
            {
                UnitAttribute taker = where.unit;
                if (taker == null || taker.Data == null) return;
                if (taker.Data.isdead) return;
                if (!taker.inParty && taker.Data.team != Faction.player) return;

                UIEquipmentInfo neck = inv.itemInfo as UIEquipmentInfo;
                if (neck == null || neck.EquipType != EquipSlotType.neck) return;
                if (!Has(neck)) return;

                inv.SetDurability(1f);

                ItemForgePlugin.Log.LogInfo($"Оберег «{inv.itemInfo.Name}» поднят пустым — "
                    + "оставляю каплю, чтобы его можно было надеть.");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог оживить пустой оберег: " + e.Message);
            }
        }

        /// <summary>True when this amulet stands against magic at all.</summary>
        internal static bool Has(UIEquipmentInfo neck)
        {
            return Keeps(neck);
        }

        /// <summary>
        /// True for the named amulets that the game itself set against magic.
        ///
        /// Таких немного: от второго яруса, синие и выше, и с сопротивлением магии, вписанным
        /// самим автором вещи, — Гортус, Магнолия, лунный камень, мать-земля и им подобные.
        /// Медные и серебряные подвески с камнем — украшения: прочности у них нет, поглощать
        /// им нечем.
        /// </summary>
        internal static bool Keeps(UIEquipmentInfo neck)
        {
            if (Enabled == null || !Enabled.Value || neck == null) return false;
            if (neck.EquipType != EquipSlotType.neck) return false;

            try
            {
                ItemTier leastTier = (ItemTier)Enum.Parse(typeof(ItemTier), LeastTier.Value.Trim(), true);
                UIItemQuality leastQuality = (UIItemQuality)Enum.Parse(typeof(UIItemQuality),
                    LeastQuality.Value.Trim(), true);

                if (((UIItemInfo)neck).tier < leastTier) return false;
                if (((UIItemInfo)neck).Quality < leastQuality) return false;
            }
            catch
            {
                return false;
            }

            UIArmorInfo coat = neck as UIArmorInfo;
            if (coat == null || coat.damageDR == null) return false;

            // Огонь, холод, молния, яд, свет, тьма — в массиве сопротивлений вещи.
            for (int i = 3; i < coat.damageDR.Length; i++)
            {
                if (coat.damageDR[i] > 0f) return true;
            }

            return false;
        }

        /// <summary>True when this amulet already stands against that one school.</summary>
        private static bool Carries(UIEquipmentInfo neck, AddonAttribute school)
        {
            if (neck.addAttrs == null) return false;

            foreach (AddonAttributes had in neck.addAttrs)
            {
                if (had != null && had.type == school && had.value > 0f) return true;
            }

            return false;
        }

        /// <summary>How much magic this amulet can still drink before it is empty.</summary>
        internal static float Left(Inventory neck)
        {
            return Left(neck, true);
        }

        /// <summary>The same, but able to look without touching.</summary>
        internal static float Left(Inventory neck, bool mayRefill)
        {
            if (!Enabled.Value || neck == null || neck.itemInfo == null) return -1f;

            UIEquipmentInfo made = neck.itemInfo as UIEquipmentInfo;
            if (made == null || made.EquipType != EquipSlotType.neck || !Has(made)) return -1f;

            if (mayRefill)
            {
                Refill(neck, PartyManager.instance != null ? PartyManager.instance.leader : null);
            }

            return Mathf.Max(0f, neck.durability) * Hold.Value * Grade(made);
        }

        /// <summary>How much magic this amulet holds when it is full.</summary>
        internal static float Whole(Inventory neck)
        {
            if (neck == null || neck.itemInfo == null) return 0f;

            UIEquipmentInfo made = neck.itemInfo as UIEquipmentInfo;
            if (made == null) return 0f;

            return neck.itemInfo.durability * Hold.Value * Grade(made);
        }

        /// <summary>Drinks what it can of a spell, and empties as it does.</summary>
        internal static float Drink(HumaniodUnit body, float magic)
        {
            if (!Enabled.Value || body == null || magic <= 0f) return magic;

            Inventory neck = Pierce.Worn(body, 3);
            if (neck == null || neck.itemInfo == null) return magic;

            UIEquipmentInfo made = neck.itemInfo as UIEquipmentInfo;
            if (made == null || !Has(made)) return magic;

            Refill(neck, body);

            if (neck.durability <= 0f) return magic;

            // Сколько магии берёт одно очко оберега — зависит от того, что это за оберег.
            // Ярус и редкость решают, сколько он выпьет всего: дешёвая бирюлька гасит одно
            // заклинание, легендарка — десяток.
            float sip = Hold.Value * Grade(made);

            // Пока в обереге есть хоть что-то, он берёт удар целиком. Не «сколько успел» —
            // весь: это не броня, которая ослабляет, а сосуд, который принимает. Последнее
            // заклинание может быть больше остатка, и тогда оно всё равно не доходит, а
            // оберег после него пуст. В этом и договор: он спасает полностью, но однажды.
            neck.durability = Mathf.Max(0f, neck.durability - magic / sip);

            if (neck.durability <= 0f)
            {
                ItemForgePlugin.Log.LogInfo($"Оберег «{neck.itemInfo.Name}» выпит досуха "
                    + $"(принял {magic:0.#}): полон снова через {Rest.Value} сут.");
            }

            return 0f;
        }

        /// <summary>Sometimes a blow finds the trinket itself and ends it.</summary>
        internal static void Aim(HumaniodUnit body, UnitAttribute victim)
        {
            // Украшения — не броня, и прочности у них нет: разбивать нечего.
            if (Tatter.Trinkets != null && Tatter.Trinkets.Value) return;

            if (!Enabled.Value || body == null || Shatter.Value <= 0f) return;
            if (UnityEngine.Random.value >= Shatter.Value) return;

            // Амулет, кольцо, пояс — то, что носят поверх и не прикрывают ничем.
            int[] small = new int[] { 3, 8, 6 };
            int slot = small[UnityEngine.Random.Range(0, small.Length)];

            Inventory thing = Pierce.Worn(body, slot);
            if (thing == null || thing.itemInfo == null) return;
            if (thing.durability < 0f || thing.itemInfo.noDurability) return;
            if (thing.durability <= 0f) return;

            thing.durability = 0f;
            counted.Remove(thing);

            ItemForgePlugin.Log.LogInfo($"Удар пришёлся в «{thing.itemInfo.Name}» "
                + $"на {Pierce.Named(victim)} — вещь разбита.");
        }

        /// <summary>Gives back what time has returned to this amulet since we last looked.</summary>
        private static void Refill(Inventory neck, HumaniodUnit wearer)
        {
            if (neck == null || neck.itemInfo == null) return;

            // Наполняет только живой и только то, что на нём. Оберег в сумке, в сундуке или
            // на трупе не наполняется ничем: это не сосуд, стоящий под дождём, а часть
            // человека, пока он его носит.
            if (wearer == null || wearer.Data == null || wearer.Data.isdead) return;
            if (Pierce.Worn(wearer, 3) != neck) return;

            float whole = neck.itemInfo.durability;
            if (whole <= 0f) return;

            // Часы мира, а не наши: ночёвка в таверне стоит ночи, а бой длиной в минуту не
            // наполняет ничего. Оберег живёт по календарю игры.
            float now = Clock();

            float before;
            if (!counted.TryGetValue(neck, out before))
            {
                counted[neck] = now;
                return;
            }

            float passed = now - before;
            if (passed <= 0f) return;

            counted[neck] = now;

            if (neck.durability >= whole) return;

            // Скорость берём из восстановления маны владельца: чем полнее его собственный
            // источник, тем быстрее наполняется то, что от него питается.
            float perHour = ((UnitAttribute)(object)wearer).MPrestore * WitHaste.Value;
            if (perHour <= 0f) return;

            // Считаем в единицах поглощения, а переводим в прочность: у глубокого амулета одно
            // очко прочности стоит нескольких единиц запаса.
            float sip = Hold.Value * Grade(neck.itemInfo as UIEquipmentInfo);
            if (sip <= 0f) return;

            bool wasEmpty = neck.durability <= 0f;
            neck.durability = Mathf.Min(whole, neck.durability + perHour * passed / sip);

            if (wasEmpty && neck.durability > 0f)
            {
                ItemForgePlugin.Log.LogInfo($"Оберег «{neck.itemInfo.Name}» снова держит: "
                    + $"{neck.durability * sip:0}.");
            }
        }

        /// <summary>The hour the world is on.</summary>
        private static float Clock()
        {
            try
            {
                if (TimeManager.Instance == null) return 0f;
                return (TimeManager.Year * 360 + TimeManager.Day) * 24f + TimeManager.Hour;
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>The day the world is on.</summary>
        private static int Today()
        {
            try
            {
                TimeManager clock = TimeManager.Instance;
                if (clock == null) return 0;

                return TimeManager.Year * 360 + TimeManager.Day;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>Forgets every drained amulet, for a new game.</summary>
        internal static void Forget()
        {
            counted.Clear();
        }
    }

    // Выпитый досуха оберег нельзя надеть: игра прямо запрещает надевать вещь на нулевой
    // прочности — «снаряжение сломано». Для сапог это правильно, а для амулета выходит
    // ловушка: он опустел, потому что делал свою работу, и за это его больше нельзя носить,
    // а значит и наполнить — восполняется только надетое. Единица разрывает этот круг: вещь
    // снова можно надеть, а запас у неё при этом почти нулевой и набирается обычным путём.
    //
    // Единица ставится только в руках у живого. Пока амулет лежит на трупе — ноль, и в описи
    // ноль: выпитый оберег остаётся выпитым.
    //
    // Патчить надо оба входа. Сначала здесь был только «AddInventory», и правило не
    // срабатывало ни разу: окно обыска перекладывает вещи через «AddInventoryNoEvent»,
    // а это другой метод. Амулет приходил в сумку на нуле и надеть его было нельзя.
    [HarmonyLib.HarmonyPatch(typeof(ItemStock), "AddInventory")]
    internal static class AddInventory_Revive_Patch
    {
        private static void Postfix(ItemStock __instance, Inventory inv)
        {
            Ward.Revive(__instance, inv);
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ItemStock), "AddInventoryNoEvent")]
    internal static class AddInventoryNoEvent_Revive_Patch
    {
        private static void Postfix(ItemStock __instance, Inventory inv)
        {
            Ward.Revive(__instance, inv);
        }
    }

    // Полоска прочности у амулета говорит о нём то же, что у сапог, — сколько он выдержит
    // ударов. Но амулет ударов не держит, он держит заклинания, и знать надо другое число.
    // Дописываем его туда же, где игра показывает прочность: одна строка вместо загадки.
    [HarmonyLib.HarmonyPatch(typeof(UIItemTip), "SetValue")]
    internal static class ItemTip_Ward_Patch
    {
        private static void Postfix(UIItemTip __instance, UISlotBase slot)
        {
            if (!Ward.Enabled.Value) return;

            try
            {
                UnityEngine.UI.Text line = __instance.durabilityText;
                if (line == null || !line.gameObject.activeSelf) return;

                // Вещь берём из слота, а не у подсказки: у подсказки её нет — «inventory» там
                // локальная переменная метода, а не поле. На этом первая попытка и молчала.
                Inventory shown = null;

                if (slot != null)
                {
                    System.Reflection.FieldInfo held = HarmonyLib.AccessTools
                        .Field(slot.GetType(), "inventory");

                    if (held != null) shown = held.GetValue(slot) as Inventory;
                }

                // Пока открыто окно обыска, подсказка ничего не наполняет: вещь перед нами
                // чужая и мёртвая, а владельцем восполнение считало бы игрока — живого и с
                // высоким разумом. Так амулет покойника наливался от одного наведения мыши.
                bool looting = LootManager.instance != null && LootManager.instance.isLooting;

                float left = Ward.Left(shown, !looting);
                if (left < 0f) return;

                // Полоску прочности у амулета подменяем целиком, а не дописываем к ней. У
                // амулета нет прочности в том смысле, в каком она есть у сапог: он не
                // протирается и не чинится. Число там одно и то же, но означает другое —
                // сколько заклинаний он ещё примет, — и называться должно так же.
                float whole = Ward.Whole(shown);

                string colour = "<color=#9B6BFF>";
                if (left <= 0f) colour = "<color=#C30000>";
                else if (left < whole * 0.34f) colour = "<color=#E2B35D>";

                line.text = colour + left.ToString("0") + "</color>/" + whole.ToString("0")
                    + " <size=11>поглощение</size>";
            }
            catch
            {
                // Подсказка — украшение. Если поле переименовали, молчим и живём дальше.
            }
        }
    }
}
