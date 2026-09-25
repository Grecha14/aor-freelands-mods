using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ItemForge
{
    /// <summary>
    /// Lets a smith choose to make a thing magical, and pay for the choice.
    ///
    /// Цвет кованой вещи в игре — лотерея: `EnhanceEquipment` бросает кость, и умение кузнеца
    /// лишь подкручивает шансы. Ковать нарочно нельзя ни обычное, ни магическое, и оттого
    /// ремесло не отвечает ни за что: вышло фиолетовым — повезло.
    ///
    /// Здесь выбор делает человек, а не кость. Кнопка в окне ковки перебирает семь положений:
    /// обычная вещь и шесть камней. Обычная кроется бесплатно и выходит белой всегда.
    /// Магическая просит магической пыли и камня, и камень решает, к чему будет прибавка —
    /// по цвету, а не по названию:
    ///
    ///     рубин     красный     сила
    ///     алмаз     белый       выносливость
    ///     изумруд   зелёный     ловкость
    ///     топаз     жёлтый      точность
    ///     сапфир    синий       интеллект
    ///     аметист   фиолетовый  воля
    ///
    /// Все шесть камней в игре редкие, третьего яруса, по шестьсот монет — ровня друг другу.
    /// Значит выбирают их по нужде, а не по цене, и это единственный правильный способ: камень
    /// дороже прочих превратил бы выбор в подсчёт.
    /// </summary>
    internal static class Charm
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Dust;
        internal static ConfigEntry<string> Gems;
        internal static ConfigEntry<string> Kindred;
        internal static ConfigEntry<string> Extras;
        internal static ConfigEntry<string> Reagents;
        internal static ConfigEntry<string> Brew;
        internal static ConfigEntry<string> Cost;
        internal static ConfigEntry<float> RareAt;
        internal static ConfigEntry<float> EpicAt;
        internal static ConfigEntry<float> Ceiling;
        internal static ConfigEntry<string> Power;
        internal static ConfigEntry<string> Label;
        internal static ConfigEntry<string> Plain;
        internal static ConfigEntry<string> MakeLabel;
        internal static ConfigEntry<float> ShiftX;
        internal static ConfigEntry<float> ShiftY;

        /// <summary>Which of the seven the smith has chosen. Zero is plain.</summary>
        internal static int Chosen;

        /// <summary>True from the moment materials are paid until the thing is made.</summary>
        internal static bool Making;

        /// <summary>True while a thing is being forged at all, chosen plain or chosen magical.</summary>
        internal static bool Forging;

        /// <summary>The craft of whoever is at the anvil, caught when the materials are paid.</summary>
        internal static int Hand;

        private static GameObject button;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Charm", "Enabled", true,
                "Let a smith choose whether he is making an ordinary thing or a magical one. "
                + "The game rolls for it instead, which makes the craft answerable for nothing.");

            Dust = config.Bind("Charm", "Dust", "items_Materials_MonsterMaterials_MagicDust",
                "What the magic dust is called in the item database.");

            Gems = config.Bind("Charm", "Gems",
                "items_Materials_Gems_Ruby=Strength,items_Materials_Gems_Diamond=Endurance,"
                + "items_Materials_Gems_Emerald=Agility,items_Materials_Gems_Topaz=Precision,"
                + "items_Materials_Gems_Sapphire=Intelligence,items_Materials_Gems_Amethyst=Willpower",
                "Which stone grants which attribute, written as item name = AddonAttribute "
                + "name. Laid out by colour rather than by lore: red for strength, green for "
                + "agility, blue for the mind, yellow for the eye, white for endurance, purple "
                + "for the will.");

            Kindred = config.Bind("Charm", "Kindred",
                "Strength=MeleeDamage,WeaponForce,DamageIncrease,BlockBreak;"
                + "Endurance=HP,HPPercent,PDR,DamageReduce,EPsave;"
                + "Agility=AttackSpeed,MoveSpeed,Dodge,EP;"
                + "Precision=Crit,CritMultiple,Attack,AttackRange,RangeDamage;"
                + "Intelligence=MagicDamage,MP,MPrestore,CooldownReduce;"
                + "Willpower=MDR,Defence,Threat,HealMD",
                "What else a stone brings with it, besides the attribute it is named for. A "
                + "ruby that gives strength and nothing else is a number; a ruby that gives "
                + "strength, a heavier blow and the weight to break a guard is a ruby. Each "
                + "stone keeps to its own kind — nothing from the sapphire ever turns up in "
                + "the ruby — so what a thing does can be read off its colour. Written as "
                + "attribute=list, separated by semicolons.");

            Extras = config.Bind("Charm", "Extras", "0,0,1,1,2,2",
                "How many of those kindred bonuses a thing of each tier carries, over and "
                + "above the stone's own attribute. Six numbers, T0 to T5.");

            Reagents = config.Bind("Charm", "Reagents", "Light:T2=Materials_MonsterMaterials_Chameleon tongue;Light:T3=Materials_MonsterMaterials_Harpy feathers;Light:T4=Materials_MonsterMaterials_HighLevel_GiantBeetleWings;Light:T5=Materials_MonsterMaterials_HighLevel_GiantBeetleWings;Medium:T2=Materials_MonsterMaterials_Spider gland;Medium:T3=Materials_MonsterMaterials_Chitinous plating;Medium:T4=Materials_MonsterMaterials_HighLevel_ChimeraScale;Medium:T5=Materials_MonsterMaterials_HighLevel_ChimeraScale;Heavy:T2=Materials_MonsterMaterials_BoneMeal;Heavy:T3=Materials_MonsterMaterials_Giant leg bone;Heavy:T4=Materials_MonsterMaterials_Rhino horn;Heavy:T5=Materials_MonsterMaterials_Rhino horn;Weapon:T2=Materials_MonsterMaterials_Tusks;Weapon:T3=Materials_MonsterMaterials_Troll's teeth;Weapon:T4=Materials_MonsterMaterials_GriffinClaw;Weapon:T5=Materials_MonsterMaterials_GriffinClaw;Trinket:T2=Materials_MonsterMaterials_Giant goblins brain;Trinket:T3=Materials_MonsterMaterials_GhoulBrain;Trinket:T4=Materials_MonsterMaterials_WitchEyeball;Trinket:T5=Materials_MonsterMaterials_WitchEyeball",
                "What each kind of thing asks for besides the dust and the stone, written as "
                + "kind:tier=item. The game drops fifty-nine different pieces of monster and "
                + "finds a use for almost none of them, so a cellar fills with harpy feathers "
                + "and chameleon tongues that nothing will ever want. Here each kind of work "
                + "takes what suits it: light armour the supple and quick things, medium the "
                + "hide and the scale, heavy the bone and the horn, weapons the teeth and the "
                + "claws, charms the eyes and the brains. Kinds are Light, Medium, Heavy, "
                + "Weapon and Trinket.");

            Brew = config.Bind("Charm", "Brew", "0,0,1,2,4,6",
                "How many of that reagent each tier takes. Fewer than the dust: it is the "
                + "thing that gives the work its character, not the thing that fuels it.");

            Cost = config.Bind("Charm", "Cost", "0,0,3,7,14,20",
                "How much dust and how many stones a magical thing of each tier asks for, from "
                + "T0 to T5 — the same number of each. The first two tiers cannot be enchanted "
                + "at all: there is nothing in them worth the dust. The climb is steep on "
                + "purpose, but it stops where it stops: twenty is a hoard a player can "
                + "actually gather, and a cost nobody can gather is not a cost, it is a wall.");

            EpicAt = config.Bind("Charm", "EpicAt", 70f,
                new ConfigDescription(
                    "Chance of a magical piece coming out purple, in per cent, at full mastery. "
                    + "It falls off by the square of the craft, not straight, so a half-taught "
                    + "hand gets a quarter of it and a novice next to none. Purple is not a "
                    + "better roll — it is what a master makes and nobody else.",
                    new AcceptableValueRange<float>(0f, 100f)));

            RareAt = config.Bind("Charm", "RareAt", 30f,
                new ConfigDescription(
                    "And blue, at full mastery. The two together say how much of a master's "
                    + "work is better than green — at seventy and thirty, all of it. Below full "
                    + "mastery that share shrinks straight with the craft, and purple shrinks "
                    + "faster still, so blue is what fills the middle of a smith's life.",
                    new AcceptableValueRange<float>(0f, 100f)));

            Ceiling = config.Bind("Charm", "Ceiling", 100f,
                new ConfigDescription(
                    "The mastery at which those two chances are reached in full.",
                    new AcceptableValueRange<float>(1f, 1000f)));

            Power = config.Bind("Charm", "Power", "0,0,2,3,5,8",
                "How many points of the stone's attribute the thing ends up carrying, by tier. "
                + "Six numbers, separated by commas.");

            Label = config.Bind("Charm", "Label", "Создание магических предметов",
                "What the choice button is called. The state follows after a colon: the stone "
                + "chosen, or «нет» when the work is to be plain. Before, the button simply "
                + "said «Обычная», which reads as the name of the button rather than as the "
                + "answer it is currently giving.");

            Plain = config.Bind("Charm", "Plain", "нет",
                "And what it says when no stone is chosen.");

            MakeLabel = config.Bind("Charm", "MakeLabel", "Ковать",
                "What the game's own craft button says. It says «Скорость ремесла» — «craft "
                + "speed» — which is neither what it does nor a phrase anybody would write on "
                + "a button. The translation tables cannot reach it: the game writes that text "
                + "straight onto the label, never asking its own localisation, so it is set "
                + "here instead. Empty leaves it alone.");

            ShiftX = config.Bind("Charm", "ShiftX", 0f,
                new ConfigDescription(
                    "Where the choice button sits, sideways from the forge button.",
                    new AcceptableValueRange<float>(-2000f, 2000f)));

            ShiftY = config.Bind("Charm", "ShiftY", 90f,
                new ConfigDescription(
                    "And how far above it. The repair button sits at 45, so this one goes above "
                    + "that; the window is not mine to know the shape of, so both of these are "
                    + "here to be nudged until it sits where it should.",
                    new AcceptableValueRange<float>(-2000f, 2000f)));
        }

        // ------------------------------------------------------------------ камни

        internal sealed class Stone
        {
            internal string item;
            internal AddonAttribute gives;
            internal string said;
        }

        private static List<Stone> stones;
        private static string stonesRead;

        internal static List<Stone> Stones()
        {
            string written = Gems.Value ?? "";
            if (stones != null && written == stonesRead) return stones;

            stones = new List<Stone>();

            foreach (string row in written.Split(','))
            {
                int split = row.IndexOf('=');
                if (split <= 0) continue;

                try
                {
                    Stone one = new Stone();
                    one.item = row.Substring(0, split).Trim();
                    one.gives = (AddonAttribute)Enum.Parse(
                        typeof(AddonAttribute), row.Substring(split + 1).Trim(), true);
                    one.said = Word(one.item) + ": " + Word(one.gives);
                    stones.Add(one);
                }
                catch
                {
                }
            }

            stonesRead = written;
            return stones;
        }

        private static string Word(string item)
        {
            switch (item)
            {
                case "items_Materials_Gems_Ruby": return "рубин";
                case "items_Materials_Gems_Diamond": return "алмаз";
                case "items_Materials_Gems_Emerald": return "изумруд";
                case "items_Materials_Gems_Topaz": return "топаз";
                case "items_Materials_Gems_Sapphire": return "сапфир";
                case "items_Materials_Gems_Amethyst": return "аметист";
                default: return item;
            }
        }

        private static string Word(AddonAttribute what)
        {
            switch (what)
            {
                case AddonAttribute.Strength: return "сила";
                case AddonAttribute.Endurance: return "выносливость";
                case AddonAttribute.Agility: return "ловкость";
                case AddonAttribute.Precision: return "точность";
                case AddonAttribute.Intelligence: return "интеллект";
                case AddonAttribute.Willpower: return "воля";
                default: return what.ToString();
            }
        }

        /// <summary>The stone the smith has chosen, or nothing when he is making plain work.</summary>
        internal static Stone Picked()
        {
            List<Stone> all = Stones();
            if (Chosen <= 0 || Chosen > all.Count) return null;
            return all[Chosen - 1];
        }

        // ------------------------------------------------------------------ припас

        private static int[] Numbers(ConfigEntry<string> from)
        {
            List<int> got = new List<int>();

            foreach (string one in (from.Value ?? "").Split(','))
            {
                int much;
                if (int.TryParse(one.Trim(), out much)) got.Add(Mathf.Max(0, much));
            }

            while (got.Count < 6) got.Add(0);
            return got.ToArray();
        }

        internal static int Needed(ItemTier tier)
        {
            int[] scale = Numbers(Cost);
            int step = (int)tier;
            return (step >= 0 && step < scale.Length) ? scale[step] : 0;
        }

        internal static int Points(ItemTier tier)
        {
            int[] scale = Numbers(Power);
            int step = (int)tier;
            return (step >= 0 && step < scale.Length) ? scale[step] : 0;
        }

        private static int Have(string name)
        {
            try
            {
                UIItemInfo what = UIItemDatabase.Instance.GetByName(name);
                if (what == null || PartyManager.instance == null) return 0;

                int much = 0;
                foreach (Inventory bit in PartyManager.instance.FindItemsInParty(what))
                {
                    if (bit != null) much += Mathf.Max(1, bit.stackNum);
                }
                return much;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>Takes the dust and the stone, or says why it cannot.</summary>
        internal static bool Pay(UIItemInfo made)
        {
            Stone stone = Picked();
            if (stone == null) return false;

            int need = Needed(made.tier);

            if (need <= 0)
            {
                GameController.ShowMessage("Этот ярус не зачаровать", 2f);
                return false;
            }

            if (Have(Dust.Value) < need || Have(stone.item) < need)
            {
                GameController.ShowMessage(
                    $"Нужно {need} пыли и {need} камня «{Word(stone.item)}»", 3f);
                return false;
            }

            try
            {
                UIItemInfo dust = UIItemDatabase.Instance.GetByName(Dust.Value);
                UIItemInfo gem = UIItemDatabase.Instance.GetByName(stone.item);

                PartyManager.instance.RemoveItemInParty(dust, need);
                PartyManager.instance.RemoveItemInParty(gem, need);

                ItemForgePlugin.Log.LogInfo($"Зачарование: снято {need} пыли и {need} "
                    + $"«{Word(stone.item)}» на {made.Name} T{(int)made.tier}.");

                return true;
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог взять припас на зачарование: " + e);
                return false;
            }
        }

        /// <summary>Puts the stone's virtue into the thing that was just forged.</summary>
        internal static void Bless(Inventory made)
        {
            Stone stone = Picked();
            if (stone == null || made == null || made.itemInfo == null) return;

            try
            {
                int points = Points(made.itemInfo.tier);
                if (points <= 0) return;

                // Что было на вещи до камня — уже по мерке своего цвета; что вложит камень —
                // голое, и доводится до мерки вместе со всем.
                Dictionary<AddonAttribute, float> before = Grade.Sums(made);
                float beforeAt = Grade.Graded(made);

                UIItemQuality colour = Colour();

                if (made.quality < colour) made.quality = colour;

                made.addAttrs.Add(new AddonAttributes(stone.gives, points));

                // Родня камня: то, что идёт вместе с его статом и никогда не идёт с чужим.
                // Рубин, дающий одну силу, — это число; рубин, дающий силу, тяжесть удара и
                // умение ломать защиту, — это рубин.
                List<AddonAttribute> kin = Kin(stone.gives);
                int many = Mathf.Min(Many(made.itemInfo.tier), kin.Count);

                for (int i = 0; i < many; i++)
                {
                    AddonAttribute what = kin[UnityEngine.Random.Range(0, kin.Count)];
                    kin.Remove(what);

                    made.addAttrs.Add(new AddonAttributes(what, Share(what, points)));
                }

                made.addAttrs = AddonAttributes.MergeAttributes(made.addAttrs);

                // Кованая вещь — такая же цветная, как выпавшая: её приписки множатся по той же
                // мерке. Прежде кузнец вешал их мимо этой мерки, и фиолетовое кольцо с камнем
                // выходило слабее выпавшего фиолетового.
                Grade.Settle(made, before, beforeAt);

                ItemForgePlugin.Log.LogInfo($"«{made.itemInfo.Name}» вышла {colour}, "
                    + $"{Word(stone.gives)} +{points}, кузнец {Hand}.");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог вложить камень: " + e);
            }
        }

        private static readonly Dictionary<AddonAttribute, List<AddonAttribute>> kindred =
            new Dictionary<AddonAttribute, List<AddonAttribute>>();
        private static string kindredRead;

        /// <summary>Everything that belongs to this attribute's family.</summary>
        private static List<AddonAttribute> Kin(AddonAttribute stat)
        {
            string written = Kindred.Value ?? "";

            if (written != kindredRead)
            {
                kindred.Clear();

                foreach (string row in written.Split(';'))
                {
                    int split = row.IndexOf('=');
                    if (split <= 0) continue;

                    try
                    {
                        AddonAttribute head = (AddonAttribute)Enum.Parse(
                            typeof(AddonAttribute), row.Substring(0, split).Trim(), true);

                        List<AddonAttribute> family = new List<AddonAttribute>();

                        foreach (string one in row.Substring(split + 1).Split(','))
                        {
                            try
                            {
                                family.Add((AddonAttribute)Enum.Parse(
                                    typeof(AddonAttribute), one.Trim(), true));
                            }
                            catch
                            {
                            }
                        }

                        kindred[head] = family;
                    }
                    catch
                    {
                    }
                }

                kindredRead = written;
            }

            List<AddonAttribute> got;
            return kindred.TryGetValue(stat, out got)
                ? new List<AddonAttribute>(got) : new List<AddonAttribute>();
        }

        private static int Many(ItemTier tier)
        {
            int[] scale = Numbers(Extras);
            int step = (int)tier;
            return (step >= 0 && step < scale.Length) ? scale[step] : 0;
        }

        /// <summary>
        /// How much of a bonus this kind of thing is worth.
        ///
        /// Очки статов и проценты — разные величины, и одним числом их не мерить: пять к силе
        /// это подарок, пять процентов к скорости атаки — тоже, а пять процентов к здоровью
        /// почти ничто. Поэтому доли идут по роду прибавки, а не поровну.
        /// </summary>
        private static float Share(AddonAttribute what, int points)
        {
            switch (what)
            {
                case AddonAttribute.HP:
                case AddonAttribute.EP:
                case AddonAttribute.MP:
                case AddonAttribute.Threat:
                    return points * 5f;

                case AddonAttribute.HPPercent:
                case AddonAttribute.DamageIncrease:
                case AddonAttribute.DamageReduce:
                case AddonAttribute.CritMultiple:
                    return points;

                case AddonAttribute.AttackSpeed:
                case AddonAttribute.MoveSpeed:
                case AddonAttribute.CooldownReduce:
                case AddonAttribute.EPsave:
                    return points * 0.5f;

                default:
                    return points;
            }
        }

        /// <summary>
        /// What colour this piece comes out, by the hand that made it.
        ///
        /// Зачарованная вещь не бывает белой — вложенное в неё никуда не денется, и зелёное
        /// она берёт даром. А синее и фиолетовое ковать нельзя, их можно только заслужить:
        /// шанс на них растёт с ремеслом кузнеца, и растёт по-разному. Синее — прямо, так что
        /// умелый берёт его каждый второй раз. Фиолетовое — квадратом, так что подмастерье не
        /// возьмёт его почти никогда, а мастер — изредка.
        ///
        /// В этом весь смысл: цвет перестаёт быть удачей и становится тем, чего кузнец стоит.
        /// </summary>
        private static UIItemQuality Colour()
        {
            float able = Mathf.Clamp01(Hand / Mathf.Max(1f, Ceiling.Value));

            // Сколько работы вообще выходит лучше зелёной — растёт прямо с ремеслом. Внутри
            // этой доли фиолетовое отъедает своё квадратом, а синее забирает остаток. Оттого
            // у подмастерья почти всё зелёное, у середняка правит синее, и только у мастера
            // фиолетовое обгоняет: он не просто чаще удачлив, он делает другую работу.
            float better = (EpicAt.Value + RareAt.Value) * able;

            float epic = EpicAt.Value * able * able;
            float rare = Mathf.Max(0f, better - epic);

            float roll = UnityEngine.Random.Range(0f, 100f);

            if (roll < epic) return UIItemQuality.Epic;
            if (roll < epic + rare) return UIItemQuality.Rare;

            return UIItemQuality.Uncommon;
        }

        // ------------------------------------------------------------------ в самом рецепте

        // Куда мы вписали своё и в каком рецепте: чтобы убрать ровно это и ничего лишнего.
        private static CraftRecipe dressed;
        private static readonly List<int> ours = new List<int>();

        /// <summary>
        /// Puts the dust and the stone into the recipe itself, so the window shows them.
        ///
        /// Рисовать свои гнёзда рядом с игровыми было бы вернее по чистоте, но хуже по делу:
        /// игра сама проверяет рецепт, сама гасит кнопку при нехватке и сама списывает. Стоит
        /// положить припас туда же, куда смотрит она, — и всё это достаётся даром, а окно
        /// показывает пыль с камнем наравне со слитками.
        ///
        /// Рецепт общий на всю игру, поэтому вписанное надо снимать так же тщательно, как
        /// кладём: забытая в рецепте пыль стала бы требоваться и от обычной ковки.
        /// </summary>
        internal static void Dress(CraftRecipe recipe)
        {
            Undress();

            if (!Enabled.Value || recipe == null || recipe.materials == null) return;

            dressed = recipe;

            if (!(recipe.product is UIEquipmentInfo))
            {
                Note(recipe, "не снаряжение");
                return;
            }

            Stone stone = Picked();
            if (stone == null) { Note(recipe, "камень не выбран"); return; }

            int need = Needed(recipe.product.tier);
            if (need <= 0) { Note(recipe, $"ярус {recipe.product.tier} не зачаровывается"); return; }

            try
            {
                UIItemInfo dust = Thing(Dust.Value);
                UIItemInfo gem = Thing(stone.item);

                if (dust == null || gem == null)
                {
                    Note(recipe, $"не нашлось: {(dust == null ? Dust.Value : "")} "
                        + $"{(gem == null ? stone.item : "")}");
                    return;
                }

                Put(recipe, dust, need);
                Put(recipe, gem, need);

                // И то, что идёт к этому роду работы: пёрышко к лёгкому, кость к тяжёлому,
                // клык к оружию. Без этого вся алхимия сводилась бы к одной пыли, а полсотни
                // трофеев так и лежали бы в погребе без дела.
                UIItemInfo brew = Reagent(recipe.product);
                if (brew != null) Put(recipe, brew, Sip(recipe.product.tier));

                Note(recipe, $"вписано: пыль x{need}, {Word(stone.item)} x{need}"
                    + (brew != null ? $", {brew.LocalizedName} x{Sip(recipe.product.tier)}" : ", трофея нет")
                    + $" — гнёзд занято {ours.Count}, всего {recipe.materials.Length}");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог вписать припас в рецепт: " + e);
            }
        }

        /// <summary>What kind of work this is, for the purpose of alchemy.</summary>
        private static string Kind(UIItemInfo made)
        {
            if (made is UIWeaponInfo) return "Weapon";

            UIArmorInfo coat = made as UIArmorInfo;
            if (coat != null) return coat.armourType.ToString();

            return "Trinket";
        }

        /// <summary>The reagent this kind and tier asks for.</summary>
        private static UIItemInfo Reagent(UIItemInfo made)
        {
            if (made == null) return null;

            string want = Kind(made) + ":" + made.tier;

            foreach (string row in (Reagents.Value ?? "").Split(';'))
            {
                int split = row.IndexOf('=');
                if (split <= 0) continue;

                if (!string.Equals(row.Substring(0, split).Trim(), want,
                        StringComparison.OrdinalIgnoreCase)) continue;

                return Thing("items_" + row.Substring(split + 1).Trim());
            }

            return null;
        }

        private static int Sip(ItemTier tier)
        {
            List<int> got = new List<int>();

            foreach (string one in (Brew.Value ?? "").Split(','))
            {
                int much;
                if (int.TryParse(one.Trim(), out much)) got.Add(Mathf.Max(0, much));
            }

            while (got.Count < 6) got.Add(0);

            int step = (int)tier;
            return (step >= 0 && step < got.Count) ? got[step] : 0;
        }

        /// <summary>
        /// Finds a free slot, making one if the recipe has none.
        ///
        /// Массив припаса у рецепта объявлен на девять гнёзд, но у каждого рецепта он свой и
        /// бывает короче — ровно по числу занятого. Оттого из трёх добавок влезала одна, а
        /// две молча пропадали: свободного места не было, а я его не проверял, а искал.
        /// </summary>
        private static int Free(CraftRecipe recipe)
        {
            for (int i = 0; i < recipe.materials.Length; i++)
            {
                if (recipe.materials[i] == null || recipe.materials[i].itemInfo == null) return i;
            }

            Inventory[] wider = new Inventory[recipe.materials.Length + 1];
            Array.Copy(recipe.materials, wider, recipe.materials.Length);
            recipe.materials = wider;

            return wider.Length - 1;
        }

        private static void Put(CraftRecipe recipe, UIItemInfo what, int many)
        {
            if (many <= 0 || what == null) return;

            int slot = Free(recipe);

            recipe.materials[slot] = new Inventory(what, many);
            ours.Add(slot);
        }

        /// <summary>
        /// Takes back out of the recipe exactly what we put in, and nothing else.
        ///
        /// Убирать «по содержимому» — то есть выметать из рецепта всякую пыль и всякий
        /// самоцвет — было бы проще и оказалось бы разрушительно: боевой шест просит рубин,
        /// изумруд и сапфир сам по себе, это его собственный состав. Такая уборка вынесла бы
        /// их из рецепта насовсем, и вещь стало бы невозможно сковать вовсе.
        ///
        /// Поэтому только по записи: что положили, то и снимаем, и ни гнездом больше.
        /// </summary>
        internal static void Undress()
        {
            CraftRecipe recipe = dressed;

            dressed = null;

            if (recipe == null || recipe.materials == null) { ours.Clear(); return; }

            try
            {
                foreach (int slot in ours)
                {
                    if (slot >= 0 && slot < recipe.materials.Length) recipe.materials[slot] = null;
                }
            }
            catch
            {
            }

            ours.Clear();
        }

        /// <summary>Says out loud what happened, so the next guess is not needed.</summary>
        private static void Note(CraftRecipe recipe, string said)
        {
            string what = (recipe != null && recipe.product != null)
                ? recipe.product.LocalizedName : "?";

            ItemForgePlugin.Log.LogInfo($"Зачарование «{what}»: {said}.");
        }

        /// <summary>Makes the window redraw the material slots it has already built.</summary>
        private static void Redraw()
        {
            try
            {
                CraftManager craft = CraftManager.Instance;
                if (craft == null) return;

                CraftRecipe now = AccessTools.Field(typeof(CraftManager), "currentRecipe")
                    .GetValue(craft) as CraftRecipe;

                // Панель собирается один раз при выборе рецепта и сама себя не пересобирает.
                // Просим игру выбрать тот же рецепт заново: она и перерисует.
                if (now != null) craft.OnRecipeSelect(now);
            }
            catch
            {
            }
        }

        /// <summary>The thing itself, never the recipe for it.</summary>
        private static UIItemInfo Thing(string name)
        {
            string said = (name ?? "").Trim();
            if (said.Length == 0) return null;

            try
            {
                UIItemDatabase db = UIItemDatabase.Instance;
                if (db == null || db.items == null) return null;

                foreach (UIItemInfo thing in db.items)
                {
                    if (thing == null || thing is CraftRecipe) continue;
                    if (string.Equals(thing.Name, said, StringComparison.OrdinalIgnoreCase)) return thing;
                }
            }
            catch
            {
            }

            return null;
        }

        // ------------------------------------------------------------------ кнопка

        /// <summary>Puts the choice button in the forge window, beside the repair one.</summary>
        internal static void Ready(CraftType type)
        {
            if (!Enabled.Value) return;

            try
            {
                CraftManager craft = CraftManager.Instance;
                if (craft == null || craft.makeBtn == null) return;

                bool forge = type == CraftType.blacksmith;

                // Рецепты правятся при первом обращении, а оно случалось только на починке:
                // уголь и подорожание не доходили до ковки вовсе. Спрашиваем здесь.
                if (forge) Smithing.Recipe(null);

                if (button == null && forge) Build(craft);
                if (button != null) button.SetActive(forge);

                Rename(craft);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог поставить кнопку выбора: " + e.Message);
            }
        }

        /// <summary>Puts a sane word on the game's own craft button.</summary>
        private static void Rename(CraftManager craft)
        {
            string said = (MakeLabel.Value ?? "").Trim();
            if (said.Length == 0 || craft.makeBtn == null) return;

            try
            {
                foreach (Text word in craft.makeBtn.GetComponentsInChildren<Text>(true))
                {
                    if (word.text != said) word.text = said;
                }
            }
            catch
            {
            }
        }

        private static void Build(CraftManager craft)
        {
            Button made = craft.makeBtn;

            GameObject copy = UnityEngine.Object.Instantiate(made.gameObject, made.transform.parent);
            copy.name = "ItemForgeCharmButton";

            RectTransform from = made.GetComponent<RectTransform>();
            RectTransform to = copy.GetComponent<RectTransform>();

            if (from != null && to != null)
            {
                to.anchoredPosition = from.anchoredPosition
                    + new Vector2(ShiftX.Value, ShiftY.Value);
            }

            Button press = copy.GetComponent<Button>();
            if (press != null)
            {
                // Целиком новое событие, а не «RemoveAllListeners». Тот снимает только то,
                // что навешено на ходу, а у кнопки ковки обработчик прописан в разметке —
                // и переживает снятие. Оттого нажатие на наш переключатель запускало ковку:
                // сперва чужое дело, потом наше.
                press.onClick = new Button.ButtonClickedEvent();
                press.onClick.AddListener(Next);
                press.interactable = true;
            }

            button = copy;
            Say();

            ItemForgePlugin.Log.LogInfo("Кнопка выбора ковки поставлена"
                + (to != null ? $": место {to.anchoredPosition.x:0}, {to.anchoredPosition.y:0}" : ""));
        }

        private static void Next()
        {
            Chosen++;
            if (Chosen > Stones().Count) Chosen = 0;

            Say();

            // Рецепт закрытый, спрашиваем отражением: перебирать окно ковки ради одного
            // поля дешевле, чем держать своё представление о том, что в нём выбрано.
            try
            {
                if (CraftManager.Instance == null) return;

                CraftRecipe now = AccessTools.Field(typeof(CraftManager), "currentRecipe")
                    .GetValue(CraftManager.Instance) as CraftRecipe;

                Dress(now);
                Redraw();
            }
            catch
            {
            }
        }

        private static void Say()
        {
            if (button == null) return;

            Stone stone = Picked();

            // Сперва имя кнопки, потом её ответ. Одно «Обычная» читалось как название, и
            // было непонятно, что это створка, которую можно повернуть.
            string said = Label.Value + ": " + (stone == null ? Plain.Value : stone.said);

            foreach (Text word in button.GetComponentsInChildren<Text>(true))
            {
                word.text = said;
            }
        }
    }

    // Выбрали рецепт — вписали в него пыль с камнем, если выбран камень.
    [HarmonyPatch(typeof(CraftManager), "OnRecipeSelect")]
    internal static class OnRecipeSelect_Charm_Patch
    {
        private static void Postfix(CraftRecipe recipe)
        {
            if (Charm.Enabled.Value) Charm.Dress(recipe);
        }
    }

    // Окно ковки закрылось — вынимаем своё обратно, иначе оно осталось бы в общем рецепте.
    [HarmonyPatch(typeof(CraftManager), "OnDestroy")]
    internal static class OnDestroy_Charm_Patch
    {
        private static void Postfix() { Charm.Undress(); }
    }

    // Окно ковки открылось — ставим кнопку рядом с той, что ставит Camp.
    [HarmonyPatch(typeof(CraftManager), "OpenWithType")]
    internal static class OpenWithType_Charm_Patch
    {
        private static void Postfix(CraftType type)
        {
            Charm.Ready(type);
        }
    }

    // Припас списывается здесь, вплотную перед тем, как вещь берётся из ниоткуда. Не хватило
    // пыли или камня — куём обычное, а не отказываем: человек уже нажал.
    [HarmonyPatch(typeof(CraftManager), "Cost")]
    internal static class Cost_Charm_Patch
    {
        private static void Postfix(CraftRecipe recipe)
        {
            Charm.Making = false;
            Charm.Forging = false;

            if (!Charm.Enabled.Value) return;
            if (recipe == null || !(recipe.product is UIEquipmentInfo)) return;

            Charm.Forging = true;

            // Ремесло того, кто сейчас у наковальни. Ловим здесь: к моменту, когда вещь
            // ложится в сумку, окно ковки может быть уже закрыто.
            try { Charm.Hand = CraftManager.Instance != null ? CraftManager.Instance.SkillLevel : 0; }
            catch { Charm.Hand = 0; }

            // Пыль и камень лежат в самом рецепте, и «Cost» уже списал их вместе со сталью.
            // Своего списания больше нет: два счёта на один припас — это двойная цена.
            Charm.Making = Charm.Picked() != null;

            // Списано — значит своё из рецепта пора вынимать. Окно ковки метода закрытия не
            // имеет, и это единственная минута, когда мы точно знаем, что оно отработало.
            Charm.Undress();
        }
    }

    // Вещь готова. Выбранное вкладываем здесь: цвет по вложенному, прибавка по камню.
    [HarmonyPatch(typeof(InventoryManager), "PutItemInbag", new[] { typeof(Inventory), typeof(bool) })]
    internal static class PutItemInbag_Charm_Patch
    {
        private static void Prefix(Inventory inventory)
        {
            if (!Charm.Enabled.Value) return;

            bool blessed = Charm.Making;

            Charm.Making = false;
            Charm.Forging = false;

            if (blessed) Charm.Bless(inventory);
        }
    }
}
