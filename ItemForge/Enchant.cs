using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Makes a purple thing a find rather than a background colour.
    ///
    /// Три правила об одном. Игра разыгрывает цвет в единственном месте —
    /// `EquipmentMaker.EnhanceEquipment`, — и через эту дверь идут разом лавки, добыча,
    /// снаряжение врагов и кузня. Оттого и правится одна дверь, а не четыре пути.
    ///
    /// Первое: фиолетовое встречается реже, и на разных ярусах по-разному. Игра считает
    /// целыми процентами — её порог целый, — и полпроцента ей не выговорить, поэтому розыгрыш
    /// переписан целиком: тот же порядок, те же ступени, дробные пороги.
    ///
    /// Второе: цена. Лестница цвета живёт в `Rarity` и отвечает на вопрос «чего стоит цвет»;
    /// здесь ответ на другой — «чего стоит цвет на этом ярусе». Легендарное и уникальное не
    /// трогаем: у них цена поставлена руками, и сет короля демонов должен остаться при своих
    /// шестистах шестидесяти шести.
    ///
    /// Третье: где это лежит. Город в игре — это фракция, и `factionFavorToPlayer` хранит её
    /// расположение к отряду. Значит «хорошая репутация в городе» читается прямо: у чужих на
    /// прилавке белое, у союзников фиолетовое.
    /// </summary>
    internal static class Enchant
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> EpicRate;
        internal static ConfigEntry<string> Chances;
        internal static ConfigEntry<string> Affixes;
        internal static ConfigEntry<string> MagicPrice;
        internal static ConfigEntry<float> LegendPrice;
        internal static ConfigEntry<string> MagicFavor;
        internal static ConfigEntry<string> ArmourPrice;
        internal static ConfigEntry<float> DropRate;
        internal static ConfigEntry<string> BossFloor;
        internal static ConfigEntry<string> PlateRate;
        internal static ConfigEntry<string> BeastSpoil;
        internal static ConfigEntry<string> SpoilAt;
        internal static ConfigEntry<bool> Telling;

        // Пока лавка набивает прилавок, здесь стоит её потолок цвета. Вне этого — ничей.
        internal static UIItemQuality Ceiling = UIItemQuality.Legendary;
        private static bool trading;

        // Пока набивается сундук. Железному человеку содержимое сундука щедрее, и отличить
        // сундук от лавки и от добычи можно только по тому, кто сейчас набивает.
        internal static bool Opening;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Enchant", "Enabled", true,
                "Let a purple thing be a find. The game makes better tiers likelier to be "
                + "purple, so by the fourth tier one thing in seven is — which turns the "
                + "colour into a background rather than an event.");

            EpicRate = config.Bind("Enchant", "EpicRate", "1,1,0.1,0.02,1,1",
                "How much likelier or rarer purple is on each tier than the game makes it, "
                + "from T0 to T5. One leaves the tier alone, a tenth makes it ten times rarer. "
                + "The game's own chances are 0, 0, 5, 10, 15 and 20 per cent, so the second "
                + "tier falls to half a per cent and the third to a fifth of one — which puts "
                + "a dip in the middle of the ladder, deliberately: those are the tiers a "
                + "party lives on longest, and a purple found there should be remembered. Six "
                + "numbers, separated by commas.");

            Chances = config.Bind("Enchant", "Chances",
                "5|1|0,8|2|0,12|4|0.5,16|6|1,20|9|2,25|12|4",
                "The chance, in per cent, that a thing comes out green or better, blue or "
                + "better, and purple, on each tier from T0 to T5 — three numbers per tier, "
                + "divided by a bar, tiers divided by commas.\n\n"
                + "Цветная вещь — находка, а не фон. Прежде магической выходила треть всего, "
                + "а на четвёртой ступени фиолетовой — каждая седьмая. Теперь втрое реже, и "
                + "растёт плавно со ступенью, а ступень у врага идёт от уровня: чем выше "
                + "уровень, тем чаще цвет. Прежний «EpicRate» этим заменён и больше не "
                + "читается.");

            Affixes = config.Bind("Enchant", "Affixes", "",
                "How much stronger the random bonuses on a coloured thing are than the game "
                + "makes them, by colour. Legendary is left alone: it is made by hand.\n\n"
                + "Цветная вещь стала реже втрое — и должна того стоить. Прежде зелёная "
                + "отличалась от белой на глаз, а не в бою.");

            MagicPrice = config.Bind("Enchant", "MagicPrice", "1,1,10,25,75,250",
                "What anything green or better is worth on each tier, from T0 to T5, over what "
                + "the colour alone already makes it worth. A magical thing is not a better "
                + "version of an ordinary one — it is a different kind of object, and the "
                + "higher the tier the wider that gap. Legendary and unique things are left "
                + "alone: their prices are set by hand.");

            LegendPrice = config.Bind("Enchant", "LegendPrice", 0.5f,
                new ConfigDescription(
                    "What a legendary or unique thing is worth against the price written into "
                    + "it by hand. The tier ladder above passes these by — their prices are not "
                    + "worked out, they are chosen — and so they stayed where they were while "
                    + "everything else came down by half. This is the one knob that reaches "
                    + "them. The demon king's set is exempt: its price is named in gold, and it "
                    + "is counted back through this as well, so six hundred and sixty-six stays "
                    + "six hundred and sixty-six.",
                    new AcceptableValueRange<float>(0.05f, 10f)));

            MagicFavor = config.Bind("Enchant", "MagicFavor", "50=Uncommon,70=Rare,90=Epic",
                "What a town will put on the counter, by how it feels about the party. The "
                + "scale runs from minus a hundred to a hundred. Below the lowest step named "
                + "here nothing but plain goods is shown: strangers are sold what strangers "
                + "are sold. Written as favour=colour, separated by commas.");

            ArmourPrice = config.Bind("Enchant", "ArmourPrice",
                "Cloth=1,PaddingArmor=1.2,LightLeatherArmor=1.5,HardLeaterArmor=2,"
                + "SplintArmor=3,ChainArmor=4,ScaleMail=5,LamellarArmor=6,HalfPlate=8,"
                + "PlateArmor=10",
                "What each kind of armour costs against a shirt of cloth. The game prices "
                + "armour by its tier and quality and barely by what it is made of, so a "
                + "breastplate and a padded coat of the same tier cost about the same — which "
                + "is why everyone ends up in plate. Plate is a season of a smith's work and a "
                + "cartload of steel; it should cost like one. Written as class=multiplier, "
                + "separated by commas.");

            DropRate = config.Bind("Enchant", "DropRate", 0.1f,
                "How much of the usual chance a creature's own gear has of being anything "
                + "better than plain. A tenth: what falls off the dead is now mostly what the "
                + "dead could afford, and a coloured piece taken off a body is a story rather "
                + "than a Tuesday. Chests and counters are not touched by this — a chest is "
                + "somebody's hoard and was put there on purpose.");

            BossFloor = config.Bind("Enchant", "BossFloor", "1000=Epic,0=Uncommon",
                "The colour a boss's gear is never worse than, by how much power he has. Bosses "
                + "were made harder and the reward has to answer for it: the great ones carry "
                + "purple, the ones met on the road carry blue, and the least of them still "
                + "carry green — nobody who is called a boss is dressed in nothing. Written as "
                + "power=colour, "
                + "separated by commas; the highest threshold a creature clears is the one that "
                + "counts.");

            BeastSpoil = config.Bind("Enchant", "BeastSpoil",
                "MonsterEarthSpirit,MonsterForestSpirit,MonsterSkySpirit,"
                + "MonsterFragmentOfTheSoulCrystal",
                "What a beast that is called a boss leaves behind, by item name. Materials and "
                + "nothing else: a dragon does not carry a sword, and a sword found in a "
                + "dragon is a sword the dragon ate. These are the legendary essences the "
                + "fifth-tier draughts are brewed from, and they are the reason to hunt "
                + "something that can kill five men.");

            SpoilAt = config.Bind("Enchant", "SpoilAt", "",
                "How many of them, by how much power the beast has, written as power=count. "
                + "Empty by default, and deliberately: the game already gives every boss beast "
                + "its own trophies, and gives them better than any list of mine could. A "
                + "desert dragon leaves an earth spirit with its bones, scales and hide; a "
                + "sandworm leaves an earth spirit with its skin; a griffon leaves a claw and "
                + "an egg. Each carries its own essence and its own recognisable parts, and a "
                + "generic list would hand a troll a forest spirit for no reason. Fill this in "
                + "only to add something over and above what the creature already leaves.");

            PlateRate = config.Bind("Enchant", "PlateRate", "HalfPlate=0.3,PlateArmor=0.15",
                "What share of the heavy armour a counter would have stocked actually turns up "
                + "on it. A breastplate is a season of a smith's work and a cartload of steel; "
                + "no market town has three of them lying about. Anything not named here is "
                + "stocked as usual. Written as class=share, separated by commas.");

            Telling = config.Bind("Enchant", "Telling", false,
                "Write down every colour rolled and every counter filled.");
        }

        // ------------------------------------------------------------------ розыгрыш цвета

        /// <summary>Rolls a colour the way the game does, only in fractions.</summary>
        internal static UIItemQuality Roll(UIEquipmentInfo made, int extra)
        {
            int tier = Mathf.Clamp((int)made.tier, 0, 5);

            float fine, rare, epic;
            Chance(tier, out fine, out rare, out epic);

            // Прибавка звавшего — кузнечная удача, шаг снаряжения врага — ложится долей, а не
            // процентами сверху: иначе она съела бы всю редкость таблицы.
            if (extra != 0)
            {
                float more = Mathf.Max(0f, 1f + extra / 100f);
                fine *= more;
                rare *= more;
                epic *= more;
            }

            // Сундук железного человека: каждая ступень цвета разом вероятнее.
            float luck = Iron.Luck();
            if (luck != 1f)
            {
                epic *= luck;
                rare *= luck;
                fine *= luck;
            }

            float num = UnityEngine.Random.Range(0f, 100f);

            if (num < epic) return UIItemQuality.Epic;
            if (num < rare) return UIItemQuality.Rare;
            if (num < fine) return UIItemQuality.Uncommon;

            return UIItemQuality.Common;
        }

        private static float[][] chances;
        private static string chancesRead;

        /// <summary>Шансы цвета на этой ступени: зелёное и выше, синее и выше, фиолетовое.</summary>
        private static void Chance(int tier, out float fine, out float rare, out float epic)
        {
            string written = Chances != null ? (Chances.Value ?? "") : "";

            if (chances == null || written != chancesRead)
            {
                chancesRead = written;

                List<float[]> got = new List<float[]>();

                foreach (string one in written.Split(','))
                {
                    string[] three = one.Split('|');
                    float[] row = new float[3];

                    for (int i = 0; i < 3 && i < three.Length; i++)
                    {
                        float.TryParse(three[i].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out row[i]);
                    }

                    got.Add(row);
                }

                while (got.Count < 6) got.Add(got.Count > 0 ? got[got.Count - 1] : new float[3]);

                chances = got.ToArray();
            }

            float[] mine = chances[Mathf.Clamp(tier, 0, chances.Length - 1)];
            fine = mine[0];
            rare = mine[1];
            epic = mine[2];
        }

        private static float[] rates;
        private static string ratesRead;

        private static readonly Dictionary<string, float> affixes =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private static string affixesRead;

        /// <summary>Во сколько раз сильнее бафы вещи этого цвета.</summary>
        internal static float Affix(UIItemQuality quality)
        {
            string written = Affixes != null ? (Affixes.Value ?? "") : "";

            if (written != affixesRead)
            {
                affixesRead = written;
                affixes.Clear();

                foreach (string one in written.Split(','))
                {
                    int split = one.IndexOf('=');
                    if (split <= 0) continue;

                    float much;
                    if (float.TryParse(one.Substring(split + 1).Trim(),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out much))
                    {
                        affixes[one.Substring(0, split).Trim()] = much;
                    }
                }
            }

            float got;
            return affixes.TryGetValue(quality.ToString(), out got) ? got : 1f;
        }

        /// <summary>How much rarer purple is on this tier.</summary>
        private static float Rate(int tier)
        {
            string written = EpicRate.Value ?? "";

            if (rates == null || written != ratesRead)
            {
                List<float> got = new List<float>();

                foreach (string one in written.Split(','))
                {
                    float much;
                    if (float.TryParse(one.Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out much))
                    {
                        got.Add(Mathf.Clamp(much, 0f, 100f));
                    }
                }

                while (got.Count < 6) got.Add(1f);

                ratesRead = written;
                rates = got.ToArray();
            }

            if (tier < 0 || tier >= rates.Length) return 1f;
            return rates[tier];
        }

        // ------------------------------------------------------------------ цена

        /// <summary>
        /// What this thing is worth for being magical at all, on its tier.
        ///
        /// Зовётся из «Rarity», а не своим патчем на «Inventory.Value»: два префикса на одном
        /// свойстве, оба возвращающие ложь, дерутся молча — первый выигрывает, второй не
        /// исполняется вовсе, и узнать об этом можно только по странным числам.
        /// </summary>
        internal static float Tier(Inventory thing)
        {
            if (!Enabled.Value || thing == null || thing.itemInfo == null) return 1f;

            // Белое и хуже магией не было.
            if (thing.quality < UIItemQuality.Uncommon) return 1f;

            // Легендарное и уникальное оценено руками, и лестница ярусов его не касается:
            // цена такой вещи не выведена, а выбрана. Своя доля у неё одна на всех.
            if (thing.quality >= UIItemQuality.Legendary || thing.itemInfo.isUnique)
            {
                return Mathf.Max(0.01f, LegendPrice.Value);
            }

            float[] scale = Prices();
            int tier = (int)thing.itemInfo.tier;

            if (tier < 0 || tier >= scale.Length) return 1f;
            return scale[tier];
        }

        private static readonly Dictionary<string, float> plates =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private static string platesRead;

        /// <summary>
        /// What this piece is worth for being made of what it is made of.
        ///
        /// Игра считает броню по ярусу и качеству и почти не смотрит, из чего она. Оттого
        /// кираса и стёганка одного яруса стоят примерно одинаково, и всякий рано или поздно
        /// оказывается в латах — не потому, что заслужил, а потому, что дешевле было некуда.
        /// Латы — это сезон работы кузнеца и воз стали, и стоить они должны как сезон работы
        /// кузнеца и воз стали.
        /// </summary>
        internal static float Plate(Inventory thing)
        {
            if (!Enabled.Value || thing == null || thing.itemInfo == null) return 1f;

            UIArmorInfo coat = thing.itemInfo as UIArmorInfo;
            if (coat == null) return 1f;

            string written = ArmourPrice.Value ?? "";

            if (written != platesRead)
            {
                plates.Clear();

                foreach (string row in written.Split(','))
                {
                    int split = row.IndexOf('=');
                    if (split <= 0) continue;

                    float much;
                    if (!float.TryParse(row.Substring(split + 1).Trim(),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out much)) continue;

                    plates[row.Substring(0, split).Trim()] = Mathf.Clamp(much, 0.01f, 1000f);
                }

                platesRead = written;
            }

            float worth;
            return plates.TryGetValue(coat.armourClass.ToString(), out worth) ? worth : 1f;
        }

        private static float[] prices;
        private static string pricesRead;

        private static float[] Prices()
        {
            string written = MagicPrice.Value ?? "";
            if (prices != null && written == pricesRead) return prices;

            List<float> got = new List<float>();

            foreach (string one in written.Split(','))
            {
                float much;
                if (float.TryParse(one.Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out much))
                {
                    got.Add(Mathf.Clamp(much, 0.01f, 10000f));
                }
            }

            while (got.Count < 6) got.Add(1f);

            pricesRead = written;
            prices = got.ToArray();
            return prices;
        }

        // ------------------------------------------------------------------ прилавок

        /// <summary>Opens a counter, with the ceiling this town's standing allows.</summary>
        internal static void Counter(Shop shop)
        {
            trading = true;
            Ceiling = Allowed(Standing(shop));

            if (Telling.Value)
            {
                ItemForgePlugin.Log.LogInfo($"Прилавок «{(shop != null ? shop.shopName : "?")}»: "
                    + $"расположение {Standing(shop)}, потолок {Ceiling}.");
            }
        }

        internal static void Shut()
        {
            trading = false;
            Ceiling = UIItemQuality.Legendary;
        }

        /// <summary>How the town this counter belongs to feels about the party.</summary>
        private static int Standing(Shop shop)
        {
            try
            {
                Faction whose = Faction.none;

                if (shop != null && shop.shopFaction != Faction.none) whose = shop.shopFaction;
                else if (shop != null && shop.vender != null && shop.vender.unit != null
                         && shop.vender.unit.Data != null)
                {
                    whose = shop.vender.unit.Data.team;
                }

                if (whose == Faction.none) return 100;   // ничьё — не наше дело

                return FactionManager.Instance.GetFavorToPlayer(whose);
            }
            catch
            {
                return 100;
            }
        }

        /// <summary>The best colour a counter at this standing is allowed to show.</summary>
        private static UIItemQuality Allowed(int favour)
        {
            UIItemQuality best = UIItemQuality.Common;

            foreach (string row in (MagicFavor.Value ?? "").Split(','))
            {
                int split = row.IndexOf('=');
                if (split <= 0) continue;

                int needed;
                if (!int.TryParse(row.Substring(0, split).Trim(), out needed)) continue;
                if (favour < needed) continue;

                try
                {
                    UIItemQuality asked = (UIItemQuality)Enum.Parse(
                        typeof(UIItemQuality), row.Substring(split + 1).Trim(), true);

                    if (asked > best) best = asked;
                }
                catch
                {
                }
            }

            return best;
        }

        /// <summary>The ceiling in force right now, for whoever is rolling.</summary>
        internal static UIItemQuality Cap()
        {
            return trading ? Ceiling : UIItemQuality.Legendary;
        }

        /// <summary>
        /// How much of the usual chance this roll gets at all.
        ///
        /// Прилавок и сундук не трогаем: там вещь положена нарочно, купцом или тем, кто прятал
        /// клад. А снаряжение существа — это то, что оно смогло себе позволить, и цветного в
        /// нём должно быть мало, иначе всякий труп у дороги одевает отряд лучше кузни.
        /// </summary>
        internal static float Share()
        {
            if (trading) return 1f;
            if (Charm.Enabled != null && Charm.Enabled.Value && Charm.Forging) return 1f;

            return Mathf.Clamp01(DropRate.Value);
        }

        private static readonly Dictionary<string, float> scarce =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private static string scarceRead;

        /// <summary>
        /// Takes the heavy armour off a counter that should not have had so much of it.
        ///
        /// Дорогая вещь и редкая вещь — разное. Латы подорожали вдесятеро, но пока их три
        /// штуки на прилавке в каждом городке, они остаются товаром, а не событием. Здесь они
        /// становятся вторым: большую часть того, что игра выложила, снимаем обратно.
        /// </summary>
        internal static void Thin(ItemStock stock)
        {
            if (!Enabled.Value || stock == null || stock.items == null) return;

            string written = PlateRate.Value ?? "";

            if (written != scarceRead)
            {
                scarce.Clear();
                foreach (string row in written.Split(','))
                {
                    int split = row.IndexOf('=');
                    if (split <= 0) continue;

                    float share;
                    if (!float.TryParse(row.Substring(split + 1).Trim(),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out share)) continue;

                    scarce[row.Substring(0, split).Trim()] = Mathf.Clamp01(share);
                }
                scarceRead = written;
            }

            if (scarce.Count == 0) return;

            try
            {
                List<Inventory> gone = new List<Inventory>();

                foreach (Inventory bit in stock.items)
                {
                    UIArmorInfo coat = bit != null ? bit.itemInfo as UIArmorInfo : null;
                    if (coat == null) continue;

                    float share;
                    if (!scarce.TryGetValue(coat.armourClass.ToString(), out share)) continue;
                    if (UnityEngine.Random.value < share) continue;

                    gone.Add(bit);
                }

                foreach (Inventory bit in gone) stock.items.Remove(bit);

                if (gone.Count > 0 && Telling.Value)
                {
                    ItemForgePlugin.Log.LogInfo($"С прилавка снято тяжёлой брони: {gone.Count}.");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог проредить прилавок: " + e);
            }
        }

        /// <summary>The colour a creature of this standing never falls below.</summary>
        internal static UIItemQuality Floor(UnitAttribute who)
        {
            if (who == null || who.info == null || !who.info.isBoss) return UIItemQuality.Poor;

            UIItemQuality best = UIItemQuality.Poor;
            float cleared = -1f;

            foreach (string row in (BossFloor.Value ?? "").Split(','))
            {
                int split = row.IndexOf('=');
                if (split <= 0) continue;

                float need;
                if (!float.TryParse(row.Substring(0, split).Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out need)) continue;

                if (who.info.power < need || need <= cleared) continue;

                try
                {
                    best = (UIItemQuality)Enum.Parse(
                        typeof(UIItemQuality), row.Substring(split + 1).Trim(), true);
                    cleared = need;
                }
                catch
                {
                }
            }

            return best;
        }

        /// <summary>Raises everything a boss carries to what his standing promises.</summary>
        internal static void Crown(UnitAttribute who)
        {
            if (!Enabled.Value || who == null) return;

            UIItemQuality floor = Floor(who);
            if (floor <= UIItemQuality.Poor) return;

            try
            {
                int raised = 0;

                HumaniodUnit man = who as HumaniodUnit;

                if (man != null && man.equipmentmanger != null
                    && man.equipmentmanger.equipInfos != null)
                {
                    foreach (EquipInfo slot in man.equipmentmanger.equipInfos)
                    {
                        if (slot == null || !slot.IsEquiped()) continue;
                        if (Lift(slot.inventory, floor)) raised++;
                    }
                }

                if (who.items != null && who.items.items != null)
                {
                    foreach (Inventory bit in who.items.items)
                    {
                        if (Lift(bit, floor)) raised++;
                    }
                }

                // И одеть мало — надо, чтобы он это унёс. Стат под надетое добираем той же
                // рукой, что и страже: иначе босс в поднятых латах встанет столбом от Strain,
                // и весь бой кончится, не начавшись.
                if (man != null && Watch.Fit(man))
                {
                    man.Data.SetLevel();
                    who.UpdateAttribute();
                }

                if (raised > 0 && Telling.Value)
                {
                    ItemForgePlugin.Log.LogInfo($"«{who.info.name}» (сила {who.info.power}): "
                        + $"поднято до {floor} вещей {raised}.");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог одеть босса по чину: " + e);
            }
        }

        /// <summary>
        /// Gives a beast that is called a boss something worth killing it for.
        ///
        /// Снаряжения зверю не кладём, и это не придирка: меч, найденный в драконе, — это меч,
        /// который дракон съел. Зверь оставляет то, из чего он состоял, и у по-настоящему
        /// крупного это легендарная суть — дух земли, леса, неба, осколок кристалла души. Из
        /// них варят зелья пятого яруса, и ради них на такого и охотятся.
        ///
        /// Трофеи существа при этом остаются нетронутыми: шкура и клык — про то, чем зверь
        /// был, а суть — про то, чего стоила охота.
        /// </summary>
        internal static void Hoard(UnitAttribute who)
        {
            if (!Enabled.Value || who == null || who.info == null) return;
            if (!who.info.isBoss || who.items == null) return;

            int many = Many(who.info.power);
            if (many <= 0) return;

            // Один раз на особь: «InitializeUnit» зовётся и при загрузке области.
            if (!hoarded.Add(who.GetInstanceID())) return;

            try
            {
                string[] names = (BeastSpoil.Value ?? "").Split(',');
                if (names.Length == 0) return;

                for (int i = 0; i < many; i++)
                {
                    string name = names[UnityEngine.Random.Range(0, names.Length)].Trim();
                    if (name.Length == 0) continue;

                    UIItemInfo what = UIItemDatabase.Instance.GetByName(name);
                    if (what == null) continue;

                    who.items.AddNewInventory(what);

                    if (Telling.Value)
                    {
                        ItemForgePlugin.Log.LogInfo($"«{who.info.name}» "
                            + $"(сила {who.info.power}) понесёт «{name}».");
                    }
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог дать зверю награду: " + e);
            }
        }

        private static readonly HashSet<int> hoarded = new HashSet<int>();

        /// <summary>How many essences a beast of this standing is worth.</summary>
        private static int Many(float power)
        {
            int most = 0;
            float cleared = -1f;

            foreach (string row in (SpoilAt.Value ?? "").Split(','))
            {
                int split = row.IndexOf('=');
                if (split <= 0) continue;

                float need; int many;
                if (!float.TryParse(row.Substring(0, split).Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out need)) continue;
                if (!int.TryParse(row.Substring(split + 1).Trim(), out many)) continue;

                if (power < need || need <= cleared) continue;

                most = many;
                cleared = need;
            }

            return most;
        }

        private static bool Lift(Inventory bit, UIItemQuality floor)
        {
            if (bit == null || bit.itemInfo == null) return false;

            UIEquipmentInfo gear = bit.itemInfo as UIEquipmentInfo;
            if (gear == null || gear.isUnique || gear.noRandomQuality) return false;
            if (gear.EquipType == EquipSlotType.relic) return false;
            if (bit.quality >= floor) return false;

            EquipmentMaker.EnhanceEquipmentToQuality(bit, floor);
            return true;
        }
    }

    // Единственная дверь, через которую в игре появляется цвет. Заменяем её целиком: игра
    // считает целыми процентами, а полпроцента её порогом не выговорить.
    [HarmonyPatch(typeof(EquipmentMaker), "EnhanceEquipment")]
    internal static class EnhanceEquipment_Enchant_Patch
    {
        private static bool Prefix(Inventory inv, int extraRate, UIItemQuality maxQuality)
        {
            if (!Enchant.Enabled.Value) return true;

            try
            {
                UIEquipmentInfo made = inv != null ? inv.itemInfo as UIEquipmentInfo : null;
                if (made == null) return false;

                if (made.isUnique || made.noRandomQuality
                    || made.EquipType == EquipSlotType.relic) return false;

                // Кузнец выбрал обычную работу — значит обычная и выходит. Бросок здесь ни
                // при чём: в том и смысл выбора, что за него отвечает человек, а не кость.
                if (Charm.Enabled.Value && Charm.Forging && Charm.Picked() == null)
                {
                    return false;
                }

                // Гному везёт у наковальни: его доля прибавляется к самому броску, а не
                // ложится вторым броском поверх.
                if (Charm.Enabled.Value && Charm.Forging && Blood.Bench != null)
                {
                    float luck = Blood.Of(Blood.Bench, "forge");
                    if (luck > 0f) extraRate += Mathf.RoundToInt(luck * 100f);
                }

                UIItemQuality rolled = Enchant.Roll(made, extraRate);

                // Доля броска: у трупа она десятая, у прилавка и сундука полная.
                if (rolled > UIItemQuality.Common
                    && UnityEngine.Random.value > Enchant.Share())
                {
                    rolled = UIItemQuality.Common;
                }

                // Потолков два, и оба обязательны: тот, что просил звавший, и тот, что
                // позволяет город. Берём меньший.
                if (rolled > maxQuality) rolled = maxQuality;

                UIItemQuality town = Enchant.Cap();
                if (rolled > town) rolled = town;

                if (inv.quality < rolled) EquipmentMaker.EnhanceEquipmentToQuality(inv, rolled);

                if (Enchant.Telling.Value && rolled > UIItemQuality.Common)
                {
                    ItemForgePlugin.Log.LogInfo(
                        $"Цвет: «{made.Name}» T{(int)made.tier} → {rolled}.");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог разыграть цвет: " + e);
                return true;
            }

            return false;
        }
    }

    // У зверя нет слотов снаряжения, и поднимать ему нечего. Но дракон, с которого падает
    // одна шкура, — плохая награда за дракона. Кладём вещь в тело: не вместо трофеев, а сверх
    // них, и ровно того цвета, которого стоит убитый.
    [HarmonyPatch(typeof(UnitAttribute), "InitializeUnit")]
    internal static class InitializeUnit_Beastly_Enchant_Patch
    {
        private static void Postfix(UnitAttribute __instance)
        {
            if (__instance is HumaniodUnit) return;      // людьми занят другой патч

            Enchant.Hoard(__instance);
        }
    }

    // Босса надо одеть по чину, и делается это после того, как игра его одела сама.
    [HarmonyPatch(typeof(HumaniodUnit), "InitializeUnit")]
    internal static class InitializeUnit_Enchant_Patch
    {
        private static void Postfix(HumaniodUnit __instance)
        {
            Enchant.Crown((UnitAttribute)(object)__instance);
        }
    }

    // Лавка набивает прилавок здесь. На время этой работы над ней стоит потолок её города.
    [HarmonyPatch(typeof(ShopGoodsList), "AddGoodsToStock")]
    internal static class AddGoodsToStock_Enchant_Patch
    {
        private static void Prefix(Shop shop)
        {
            if (Enchant.Enabled.Value) Enchant.Counter(shop);
        }

        private static void Postfix(ItemStock unitItems)
        {
            if (!Enchant.Enabled.Value) return;

            Enchant.Thin(unitItems);
            Enchant.Shut();
        }
    }

    // Сундуки и мешки набиваются другим путём, но по тому же правилу: что лежит в городе,
    // тем город и торгует.
    [HarmonyPatch(typeof(ContainerContent), "AddContentToStock")]
    internal static class AddContentToStock_Enchant_Patch
    {
        private static void Prefix()
        {
            Enchant.Opening = true;
            if (Enchant.Enabled.Value) Enchant.Counter(null);
        }

        private static void Postfix()
        {
            Enchant.Opening = false;
            if (Enchant.Enabled.Value) Enchant.Shut();
        }
    }
}
