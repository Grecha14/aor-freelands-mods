using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Puts the city watch where a city watch belongs.
    ///
    /// Игра раздаёт стражникам уровни от шестого до двадцать шестого и снаряжение по чертежу:
    /// «Guard_T1_Knight» ходит в первом ярусе, «Guard_T2_Defender» во втором. Отряд к середине
    /// игры перерастает их вдвое, и городская стража перестаёт быть силой — она становится
    /// помехой, которую сносят мимоходом. Город, который нельзя взять нахрапом, держится на
    /// том, что стража в нём не хуже тех, кто в него входит.
    ///
    /// Уровень поднять прямо нельзя, и это выяснилось дорогой ценой. У человека он не
    /// хранится вовсе — он выводится из шести статов:
    ///
    ///     for (i = 0..5)
    ///         for (k = стат-1 .. 0) num += min(k / 5, 3);
    ///     level = clamp(num / 5, 1, 99);
    ///
    /// Записанное в `Data.level` живёт ровно до ближайшего пересчёта, и мой первый заход
    /// ставил пятьдесят, а получал двадцать семь — потому что двадцать семь и есть то, на что
    /// у стражника хватало статов. Значит растить надо статы, а уровень придёт сам. Ровно то
    /// же, к чему пришли со зверями.
    ///
    /// Растут они не поровну. Стражнику незачем ум мудреца — ему надо носить доспех вместе
    /// с оружием и бегать в нём, а это сила и выносливость. Поэтому у роста своя доля на
    /// каждый стат, и ум с волей прибавляют едва-едва: из вояки не делается книжник.
    ///
    /// Броня сложнее: надетое приходит с шаблона, а не разыгрывается. Прежде мы стражника
    /// переодевали уже одетым — снимали, что надето, и надевали своё, положив его ещё и в
    /// сумку. Выходило по второму комплекту в сумке у каждого, пятьдесят килограммов лишнего
    /// железа, и стража вставала под грузом. Теперь её одевают при рождении, по правленому
    /// списку шаблона («Birth»): тот же кусок того же класса нужного яруса, в слот, мимо
    /// сумки. Класс берётся у шаблонного: кто ходил в кольчуге, останется в кольчуге, только
    /// в лучшей.
    /// </summary>
    internal static class Watch
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> ByCareer;
        internal static ConfigEntry<string> Guards;
        internal static ConfigEntry<string> Shape;
        internal static ConfigEntry<int> Least;
        internal static ConfigEntry<string> Wear;
        internal static ConfigEntry<string> Clad;
        internal static ConfigEntry<string> Captains;
        internal static ConfigEntry<string> CaptainClad;
        internal static ConfigEntry<string> Ladder;
        internal static ConfigEntry<bool> Arms;
        internal static ConfigEntry<string> Trinkets;
        internal static ConfigEntry<string> TrinketTier;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Watch", "Enabled", true,
                "Let the city watch be worth the wall it stands on. The game gives them levels "
                + "in the single figures and gear of the first two tiers, so a party halfway "
                + "through the game walks past them.");

            ByCareer = config.Bind("Watch", "ByCareer", true,
                "Count anybody whose career is Guard, whatever his creature is called. The "
                + "game hands out careers separately from creature assets, so a town's watch is "
                + "made of ordinary townsfolk with the Guard career — and a list of asset names "
                + "misses every one of them.");

            Guards = config.Bind("Watch", "Guards",
                "Guard_T1_Archer,Guard_T1_Fighter,Guard_T1_Knight,Guard_T1_Spearman,"
                + "Guard_T2_Defender,Guard_T2_EliteSpearman,"
                + "Guard_T2_Marksman,TownGuard,TownGuardCaptain,PrisonGuard,"
                + "Guard_Female1,Guard_Female2",
                "Who counts as the city watch, by UnitInfo asset name, separated by commas. "
                + "Arena guards, trial guardians and the iron constructs are deliberately not "
                + "here: they are not a city's watch and answer to other rules. "
                + "Guard_T2_EliteKnight is left out on purpose as well, and for a sharper "
                + "reason: the holy crusade that hunts a demon is built out of them. Dressed as "
                + "city watch they would come in tier-three half-plate instead of the "
                + "tier-five the crusade hands out, and the fight the whole month was building "
                + "towards would be over in a minute.");

            Shape = config.Bind("Watch", "Shape", "3,3,1.5,1.5,0.25,1",
                "How much of the growth each of the six takes, in order: strength, endurance, "
                + "agility, precision, intelligence, will. A watchman has to carry armour and a "
                + "weapon in it and still run, and that is strength and endurance; wits he has "
                + "no use for. Grown evenly he would come out a scholar in plate.");

            Least = config.Bind("Watch", "Least", 50,
                new ConfigDescription(
                    "The level below which no watchman is found. Anyone the game made better "
                    + "than this keeps what he has.",
                    new AcceptableValueRange<int>(1, 300)));

            Wear = config.Bind("Watch", "Wear", "T3",
                "What tier the watch is armoured to. A piece already at this tier or above is "
                + "left alone; anything under it is swapped for the same piece of the same "
                + "class at this tier, so a man in mail stays a man in mail and a man in plate "
                + "stays in plate.");

            Clad = config.Bind("Watch", "Clad", "HalfPlate",
                "What a watchman stands in — the floor and the ceiling both. Lighter is raised "
                + "to it and heavier is brought down to it, so the watch looks like a watch "
                + "rather than like whoever the game dressed that morning. Empty, and the class "
                + "is left alone and only the tier is raised.");

            Captains = config.Bind("Watch", "Captains", "TownGuardCaptain",
                "Who outranks the rest, by UnitInfo asset name, separated by commas. A captain "
                + "wears what his men are not given.");

            CaptainClad = config.Bind("Watch", "CaptainClad", "PlateArmor",
                "And what he wears. Plate on one man in a gatehouse is a rank; plate on all of "
                + "them is a uniform, and then it is not worth anything to anybody.");

            Ladder = config.Bind("Watch", "Ladder",
                "Cloth,hat,headband,tiara,PaddingArmor,LightLeatherArmor,HardLeaterArmor,leatherHelemt,SplintArmor,ChainArmor,ScaleMail,LamellarArmor,metalHelmet,HalfPlate,PlateArmor",
                "The armour classes in order from lightest to heaviest. Clad is read against "
                + "this, and so is «already heavy enough». "
                + "Helmets carry their own classes — hat, headband, tiara, leatherHelemt, "
                + "metalHelmet — and they belong on the same ladder, otherwise a man in "
                + "half-plate keeps whatever hat he was born in. They are placed by what they "
                + "actually stop, read off Breach.Guards: a metal helmet holds seventy-eight, "
                + "exactly what half-plate holds, so it sits directly below it and is raised "
                + "to it. Nothing is lost by the swap — it is the same protection under a "
                + "name that matches the rest of the suit.");

            Arms = config.Bind("Watch", "Arms", true,
                "Raise what they hold to the same tier as what they wear. A man in half-plate "
                + "with a first-tier sword is not a watchman, he is a man who was given armour "
                + "and forgotten about.");

            Trinkets = config.Bind("Watch", "Trinkets", "neck,finger",
                "Which trinket slots the city fills for its own, by the game's EquipSlotType "
                + "names. An amulet and a ring: the city pays its watch in more than bread, and "
                + "it is the one thing worth taking off a guard you should not have fought.");

            TrinketTier = config.Bind("Watch", "TrinketTier", "T2",
                "And of what tier. Green at the second tier is a city's wage, not a hero's "
                + "find.");

            Telling = config.Bind("Watch", "Telling", true,
                "Write down every watchman raised and every piece swapped.");
        }

        private static HashSet<string> named;
        private static string namedRead;

        /// <summary>True when this creature is one of the city's own.</summary>
        internal static bool Watchman(UnitAttribute who)
        {
            if (!Enabled.Value || who == null || who.info == null) return false;

            string written = Guards.Value ?? "";

            if (named == null || written != namedRead)
            {
                named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string one in written.Split(','))
                {
                    string name = one.Trim();
                    if (name.Length > 0) named.Add(name);
                }
                namedRead = written;
            }

            if (named.Contains(who.info.name ?? "")) return true;

            // Карьера важнее имени: стража города — это обычные горожане, которым игра
            // выдала карьеру стражника, и по имени существа их не найти ни одного. Карьера
            // живёт на людской записи, не на общей: у зверя её нет и быть не может.
            HumaniodUnit man = who as HumaniodUnit;

            if (ByCareer.Value && man != null && man.Data != null
                && man.Data.career == CareerType.Guard)
            {
                return true;
            }

            return false;
        }

        /// <summary>The level these six attributes add up to, by the game's own reckoning.</summary>
        internal static int Level(HumanAttribute six)
        {
            int num = 0;

            for (int i = 0; i < 6; i++)
            {
                for (int k = six[i] - 1; k >= 0; k--) num += Mathf.Min(k / 5, 3);
            }

            return Mathf.Clamp(num / 5, 1, 99);
        }

        /// <summary>
        /// Grows a man until his attributes carry the standing he is meant to have.
        ///
        /// Соразмерно, а не до ровного числа: множитель на все шесть сразу, и подбирается он
        /// снизу вверх, пока уровень не дотянет. У кого сила была выше прочего, у того и
        /// останется выше — стражник растёт, а не подменяется другим человеком.
        /// </summary>
        private static bool Grow(HumaniodUnit man)
        {
            if (man == null || man.Data == null || man.Data.humanAttribute == null) return false;

            HumanAttribute six = man.Data.humanAttribute;
            if (Level(six) >= Least.Value) return false;

            int[] was = new int[6];
            for (int i = 0; i < 6; i++) was[i] = six[i];

            float[] share = Shares();

            // Шаг мелкий нарочно: доли роста втрое разные, и крупным шагом пятидесятый
            // перескакивался в шестидесятый — стражник выходил сильнее, чем просили.
            for (float much = 0.01f; much <= 10f; much += 0.01f)
            {
                for (int i = 0; i < 6; i++)
                {
                    float grown = was[i] * (1f + much * share[i]);
                    six[i] = Mathf.Max(was[i], Mathf.RoundToInt(grown));
                }

                if (Level(six) >= Least.Value) return true;
            }

            return true;
        }

        /// <summary>
        /// Makes sure the man can actually hold what he has been given.
        ///
        /// Соразмерный рост держит облик, но не обещает, что силы хватит именно на этот молот:
        /// у кого она была ниже прочего, у того и после роста может не дотянуть. А не дотянет —
        /// и `Strain` поставит стражника столбом посреди улицы, потому что правило одно на всех
        /// и на стражу тоже. Поэтому вторым заходом добираем ровно то, чего просит надетое.
        /// </summary>
        internal static bool Fit(HumaniodUnit man)
        {
            if (man == null || man.Data == null || man.Data.humanAttribute == null) return false;
            if (man.equipmentmanger == null || man.equipmentmanger.equipInfos == null) return false;

            HumanAttribute six = man.Data.humanAttribute;
            bool grown = false;

            for (int slot = 0; slot < 2 && slot < man.equipmentmanger.equipInfos.Length; slot++)
            {
                EquipInfo held = man.equipmentmanger.equipInfos[slot];
                if (held == null || !held.IsEquiped() || held.inventory == null) continue;

                UIWeaponInfo blade = held.inventory.itemInfo as UIWeaponInfo;
                if (blade == null) continue;

                Wield.Demand want = Wield.Ask(blade);
                if (want == null) continue;

                if (want.first >= 0 && want.first < 6 && six[want.first] < want.firstNeed)
                {
                    six[want.first] = want.firstNeed;
                    grown = true;
                }

                if (want.second >= 0 && want.second < 6 && six[want.second] < want.secondNeed)
                {
                    six[want.second] = want.secondNeed;
                    grown = true;
                }
            }

            return grown;
        }

        private static float[] shares;
        private static string sharesRead;

        private static float[] Shares()
        {
            string written = Shape.Value ?? "";
            if (shares != null && written == sharesRead) return shares;

            List<float> got = new List<float>();

            foreach (string one in written.Split(','))
            {
                float much;
                if (float.TryParse(one.Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out much))
                {
                    got.Add(Mathf.Clamp(much, 0f, 10f));
                }
            }

            while (got.Count < 6) got.Add(1f);

            sharesRead = written;
            shares = got.ToArray();
            return shares;
        }

        /// <summary>Raises a watchman to his standing. He is dressed at birth, by Birth.</summary>
        internal static void Post(UnitAttribute who)
        {
            if (!Watchman(who) || who.Data == null) return;

            try
            {
                HumaniodUnit man = who as HumaniodUnit;
                if (man == null || man.Data == null || man.Data.humanAttribute == null) return;

                int was = who.Data.level;

                // Каждый шаг отвечает за себя. Один общий перехват уже стоил нам целого
                // правила: шаг, упавший на существе без сумки, отменял и рост уровня.
                int swapped = 0;

                // Стражник, одетый до правки, носит в сумке второй комплект. Сперва убираем
                // его, потом подгоняем силу: иначе подгоним под лишние полсотни килограммов.
                try { swapped += Unpack(man); }
                catch (Exception e)
                { ItemForgePlugin.Log.LogWarning("Не смог разобрать сумку стражника: " + e.Message); }

                bool raised = Grow(man);

                if (swapped > 0) raised = true;

                try { if (Fit(man)) raised = true; }
                catch (Exception e)
                { ItemForgePlugin.Log.LogWarning("Не смог подогнать стражника: " + e.Message); }

                if (raised)
                {
                    // Уровень выводится из статов, и пересчитать его надо тем же способом,
                    // каким игра это делает сама, — иначе он останется прежним числом при
                    // новых статах, и это увидят все, кроме нас.
                    man.Data.SetLevel();
                    who.UpdateAttribute();
                }

                if (Telling.Value && (was != who.Data.level || swapped > 0))
                {
                    HumanAttribute six = man.Data.humanAttribute;

                    ItemForgePlugin.Log.LogInfo($"Стража «{who.info.name}»: уровень {was} → "
                        + $"{who.Data.level}, из сумки убрано копий {swapped}, статы "
                        + $"{six[0]}/{six[1]}/{six[2]}/{six[3]}/{six[4]}/{six[5]}.");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог поставить стражника на пост: " + e);
            }
        }

        /// <summary>
        /// Takes out of a watchman's bag the second copy of what he is wearing.
        ///
        /// Прежнее переодевание клало новую вещь в сумку и тут же надевало её, и у каждого
        /// стражника, одетого до этой правки, в сумке лежит второй комплект: у капитана это
        /// полсотни килограммов тех же лат. Убираем из сумки то, что надето на нём же, — ровно
        /// те вещи, которых там быть не должно. Остальное в сумке его.
        /// </summary>
        private static int Unpack(HumaniodUnit man)
        {
            if (man == null || man.items == null || man.items.items == null) return 0;
            if (man.equipmentmanger == null || man.equipmentmanger.equipInfos == null) return 0;

            HashSet<UIItemInfo> worn = new HashSet<UIItemInfo>();

            foreach (EquipInfo[] slots in new[]
                { man.equipmentmanger.equipInfos, man.equipmentmanger.standByWeaponInfos })
            {
                if (slots == null) continue;

                foreach (EquipInfo slot in slots)
                {
                    if (slot == null || !slot.IsEquiped() || slot.inventory == null) continue;
                    if (slot.inventory.itemInfo != null) worn.Add(slot.inventory.itemInfo);
                }
            }

            if (worn.Count == 0) return 0;

            int taken = 0;

            foreach (Inventory thing in new List<Inventory>(man.items.items))
            {
                if (thing == null || !(thing.itemInfo is UIEquipmentInfo)) continue;
                if (!worn.Contains(thing.itemInfo)) continue;

                // Ноль — вся запись целиком, а не одна штука из стопки.
                man.items.RemoveInventory(thing, 0);
                taken++;
            }

            if (taken > 0) man.CountInventoryWeight();

            return taken;
        }

        private static readonly Dictionary<string, UIEquipmentInfo> charms =
            new Dictionary<string, UIEquipmentInfo>(StringComparer.Ordinal);

        internal static UIEquipmentInfo Trinket(EquipSlotType where, ItemTier want)
        {
            string key = where + "/" + want;

            UIEquipmentInfo kept;
            if (charms.TryGetValue(key, out kept)) return kept;

            kept = null;

            try
            {
                UIItemDatabase db = UIItemDatabase.Instance;
                if (db != null && db.items != null)
                {
                    foreach (UIItemInfo thing in db.items)
                    {
                        UIEquipmentInfo gear = thing as UIEquipmentInfo;
                        if (gear == null || gear.isUnique) continue;
                        if (gear.tier != want || gear.EquipType != where) continue;

                        kept = gear;
                        break;
                    }
                }
            }
            catch
            {
            }

            charms[key] = kept;
            return kept;
        }

        private static string[] rungs;
        private static string rungsRead;

        /// <summary>True when this one commands the rest.</summary>
        internal static bool Captain(UnitAttribute who)
        {
            if (who == null || who.info == null) return false;

            foreach (string one in (Captains.Value ?? "").Split(','))
            {
                if (string.Equals(one.Trim(), who.info.name ?? "", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The armour class this piece should be in: exactly what his rank is dressed in.
        ///
        /// И снизу, и сверху. Лёгкое поднимаем, тяжёлое опускаем — иначе латы, случайно
        /// выданные игрой рядовому, обесценивают их у капитана, а стража перестаёт читаться
        /// как стража и выглядит тем, во что её одели утром.
        /// </summary>
        internal static ArmourClass Heavier(ArmourClass worn, bool captain)
        {
            string floor = ((captain ? CaptainClad.Value : Clad.Value) ?? "").Trim();
            if (floor.Length == 0) return worn;

            string written = Ladder.Value ?? "";
            if (rungs == null || written != rungsRead)
            {
                rungs = written.Split(',');
                for (int i = 0; i < rungs.Length; i++) rungs[i] = rungs[i].Trim();
                rungsRead = written;
            }

            int his = -1, least = -1;
            for (int i = 0; i < rungs.Length; i++)
            {
                if (string.Equals(rungs[i], worn.ToString(), StringComparison.OrdinalIgnoreCase)) his = i;
                if (string.Equals(rungs[i], floor, StringComparison.OrdinalIgnoreCase)) least = i;
            }

            if (his < 0 || least < 0 || his == least) return worn;

            try { return (ArmourClass)Enum.Parse(typeof(ArmourClass), rungs[least], true); }
            catch { return worn; }
        }
    }

    // Человек рождается здесь, и здесь у него уже есть и уровень, и надетое.
    //
    // Именно у «HumaniodUnit», а не у «UnitAttribute»: людской «InitializeUnit» — это
    // override, то есть отдельный метод со своим телом. Патч на базовый до стражника бы не
    // дошёл ни разу, и молча: ошибки нет, просто ничего не происходит.
    [HarmonyPatch(typeof(HumaniodUnit), "InitializeUnit")]
    internal static class InitializeUnit_Watch_Patch
    {
        private static void Postfix(UnitAttribute __instance)
        {
            if (!Watch.Enabled.Value) return;

            Watch.Post(__instance);
        }
    }
}
