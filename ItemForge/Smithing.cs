using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// What it costs to mend a thing with your own hands.
    ///
    /// Чинить своими руками и чинить за деньги — разное занятие, и стоить они должны разного.
    /// У городского кузнеца платят монетой и уходят; в своей кузне платят тем, из чего вещь
    /// сделана, и временем. Игра этого не различает вовсе: ремкомплект годится и на рубаху, и
    /// на драконьи латы, и в обоих случаях расходуется одинаково.
    ///
    /// Из чего сделана вещь, спрашиваем не у себя, а у игры: у всякой кованой вещи есть
    /// рецепт, а в рецепте — список припаса. Это и есть ответ, причём точный: если вещь куют
    /// из стали и толстой кожи, то и чинят её сталью и толстой кожей, и никакая моя таблица
    /// не скажет этого вернее.
    ///
    /// Сколько припаса — по доле восстановленного. Починить четверть прочности стоит четверти
    /// рецепта, округляя вверх: меньше одной единицы не бывает, и мелкий ремонт оттого
    /// невыгоден. Это правильно — вещи чинят, когда они побиты, а не после каждой стычки.
    /// </summary>
    internal static class Smithing
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Share;
        internal static ConfigEntry<string> KitName;
        internal static ConfigEntry<float> PerKit;
        internal static ConfigEntry<string> Crown;
        internal static ConfigEntry<string> Costlier;
        internal static ConfigEntry<string> Coal;
        internal static ConfigEntry<string> Coals;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Smithing", "Enabled", true,
                "Make mending by your own hand cost what the thing is made of. The game asks "
                + "for repair kits and nothing else, so a linen shirt and a dragon-scale "
                + "breastplate are mended out of the same sack.");

            Share = config.Bind("Smithing", "Share", 1f,
                new ConfigDescription(
                    "How much of the recipe a full mend costs, against forging the thing anew. "
                    + "One means a ruined piece costs what it cost to make — which is right: "
                    + "there is nothing left of it but the shape. Half would mean the old steel "
                    + "is worth reusing.",
                    new AcceptableValueRange<float>(0.05f, 2f)));

            KitName = config.Bind("Smithing", "KitName", "RepairKit",
                "What the repair kit is called in the item database. It is needed whatever the "
                + "thing is made of: the file, the hammer and the whetstone do not care.");

            PerKit = config.Bind("Smithing", "PerKit", 300f,
                new ConfigDescription(
                    "How much durability one repair kit is good for. Above this a second is "
                    + "needed, and so on.",
                    new AcceptableValueRange<float>(1f, 10000f)));

            Crown = config.Bind("Smithing", "Crown", "Gold",
                "What a legendary piece asks for over and above its recipe — one of these per "
                + "mend, whatever the damage. The dragon scale a dragon armour was made of "
                + "cannot be found twice; gold is what a smith puts in its place, and it is "
                + "meant to hurt. Empty asks for nothing extra.");

            Costlier = config.Bind("Smithing", "Costlier", "1,1,2,3,5,5",
                "How much more of its recipe a thing of each tier costs to make, from T0 to "
                + "T5. The game asks about the same handful of ingots whether the blade is a "
                + "peasant's or a champion's, so the difference between tiers lives entirely "
                + "in where the recipe is found rather than in what it takes to use it. Here "
                + "the fourth tier costs five times the materials of the first, and a smith "
                + "who wants to work at that height has to keep a cart behind him. "
                + "Repair reads the same recipes, so it grows with them — which is right: a "
                + "thing that was expensive to make is expensive to mend.");

            Coal = config.Bind("Smithing", "Coal", "items_Materials_Coal",
                "What the coal is called in the item database. Empty asks for none.");

            Coals = config.Bind("Smithing", "Coals", "0,1,2,4,8,12",
                "How much coal a thing of each tier takes to forge, from T0 to T5. Steel is not "
                + "shaped cold: the forge has to be lit, and keeping it lit is half of what a "
                + "smith spends. The game never asked for fuel at all, which is why a mithril "
                + "blade could be made in a field.");

            Telling = config.Bind("Smithing", "Telling", true,
                "Write down what each mend cost and what was missing.");
        }

        // ------------------------------------------------------------------ рецепт вещи

        private static readonly Dictionary<int, CraftRecipe> known =
            new Dictionary<int, CraftRecipe>();
        private static bool read;

        /// <summary>The recipe this thing is made by, if the game has one.</summary>
        internal static CraftRecipe Recipe(UIItemInfo made)
        {
            if (!read)
            {
                read = true;

                try
                {
                    UIItemDatabase db = UIItemDatabase.Instance;
                    if (db != null && db.items != null)
                    {
                        foreach (UIItemInfo thing in db.items)
                        {
                            CraftRecipe recipe = thing as CraftRecipe;
                            if (recipe == null || recipe.product == null) continue;

                            known[recipe.product.ID] = recipe;
                        }
                    }

                    int raised = Dearer();

                    ItemForgePlugin.Log.LogInfo($"Рецептов собрано: {known.Count}, "
                        + $"подорожало составов: {raised}.");
                }
                catch (Exception e)
                {
                    ItemForgePlugin.Log.LogError("Не смог собрать рецепты: " + e);
                }
            }

            if (made == null) return null;

            CraftRecipe found;
            return known.TryGetValue(made.ID, out found) ? found : null;
        }

        /// <summary>
        /// Makes the higher tiers cost what they look like they should.
        ///
        /// Игра просит примерно одну и ту же горсть слитков и на крестьянский клинок, и на
        /// чемпионский: разница между ярусами живёт в том, где найден рецепт, а не в том, чего
        /// он стоит. Оттого дойдя до четвёртого яруса кузнец кует его так же дёшево, как
        /// первый, и весь смысл ярусов держится на одной редкости бумажки.
        ///
        /// Правится один раз при чтении базы. Рецепт — общий на всю игру, и множить его
        /// дважды нельзя, оттого и отметка.
        /// </summary>
        private static int Dearer()
        {
            int[] scale = Numbers();
            int raised = 0;

            UIItemInfo fuel = Named(Coal.Value);

            foreach (CraftRecipe recipe in known.Values)
            {
                if (recipe == null || recipe.materials == null || recipe.product == null) continue;

                int tier = (int)recipe.product.tier;
                if (tier < 0 || tier >= scale.Length) continue;

                bool touched = false;

                int much = scale[tier];

                if (much > 1)
                {
                    foreach (Inventory need in recipe.materials)
                    {
                        if (need == null || need.itemInfo == null) continue;

                        need.stackNum = Mathf.Max(1, need.stackNum) * much;
                        touched = true;
                    }
                }

                // И уголь — во всё, что куют. Сталь не гнут на холодную, и топливо кузнец
                // тратит не меньше, чем железо; игра же не спрашивала его вовсе, оттого
                // мифриловый клинок выходил в чистом поле.
                if (Fuel(recipe, fuel, Burn(tier))) touched = true;

                if (touched) raised++;
            }

            return raised;
        }

        /// <summary>How much coal a thing of this tier is worth.</summary>
        private static int Burn(int tier)
        {
            List<int> got = new List<int>();

            foreach (string one in (Coals.Value ?? "").Split(','))
            {
                int much;
                if (int.TryParse(one.Trim(), out much)) got.Add(Mathf.Max(0, much));
            }

            while (got.Count < 6) got.Add(0);
            return (tier >= 0 && tier < got.Count) ? got[tier] : 0;
        }

        /// <summary>Puts coal into a free slot of the recipe, once.</summary>
        private static bool Fuel(CraftRecipe recipe, UIItemInfo fuel, int many)
        {
            if (fuel == null || many <= 0) return false;

            // Уже вписан — второй раз не кладём: рецепт общий, и чтение базы бывает не одно.
            foreach (Inventory need in recipe.materials)
            {
                if (need != null && need.itemInfo == fuel) return false;
            }

            // Гнёзд может не быть вовсе: массив у каждого рецепта свой и бывает ровно по
            // числу занятого. Расширяем, а не отступаем.
            int slot = -1;

            for (int i = 0; i < recipe.materials.Length; i++)
            {
                if (recipe.materials[i] == null || recipe.materials[i].itemInfo == null)
                {
                    slot = i;
                    break;
                }
            }

            if (slot < 0)
            {
                Inventory[] wider = new Inventory[recipe.materials.Length + 1];
                Array.Copy(recipe.materials, wider, recipe.materials.Length);
                recipe.materials = wider;
                slot = wider.Length - 1;
            }

            recipe.materials[slot] = new Inventory(fuel, many);
            return true;
        }

        private static int[] Numbers()
        {
            List<int> got = new List<int>();

            foreach (string one in (Costlier.Value ?? "").Split(','))
            {
                int much;
                if (int.TryParse(one.Trim(), out much)) got.Add(Mathf.Max(1, much));
            }

            while (got.Count < 6) got.Add(1);
            return got.ToArray();
        }

        // ------------------------------------------------------------------ счёт припаса

        internal sealed class Bill
        {
            internal readonly Dictionary<UIItemInfo, int> want =
                new Dictionary<UIItemInfo, int>();

            internal readonly List<string> missing = new List<string>();

            internal bool Enough() { return missing.Count == 0; }
        }

        /// <summary>Adds up what mending this much of this thing would take.</summary>
        internal static void Reckon(Bill bill, Inventory thing, float points)
        {
            if (!Enabled.Value || bill == null || thing == null || points <= 0f) return;
            if (thing.itemInfo == null) return;

            try
            {
                float whole = Mathf.Max(1f, thing.itemInfo.durability);
                float part = Mathf.Clamp01(points / whole) * Share.Value;

                CraftRecipe recipe = Recipe(thing.itemInfo);

                if (recipe != null && recipe.materials != null)
                {
                    foreach (Inventory need in recipe.materials)
                    {
                        if (need == null || need.itemInfo == null) continue;

                        int stack = Mathf.Max(1, need.stackNum);
                        int asked = Mathf.CeilToInt(stack * part);
                        if (asked <= 0) continue;

                        Add(bill, need.itemInfo, asked);
                    }
                }

                // Легендарке сверх рецепта — золото. Чешуи того дракона больше нет на свете,
                // и кузнец кладёт вместо неё то, что кладут вместо невосполнимого.
                if (thing.itemInfo.tier >= ItemTier.T5)
                {
                    UIItemInfo gold = Named(Crown.Value);
                    if (gold != null) Add(bill, gold, 1);
                }

                // Ремкомплект нужен всегда: напильнику и точилу всё равно, из чего вещь.
                UIItemInfo kit = Named(KitName.Value);
                if (kit != null)
                {
                    Add(bill, kit, Mathf.Max(1, Mathf.CeilToInt(points / PerKit.Value)));
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог посчитать припас на починку: " + e);
            }
        }

        private static void Add(Bill bill, UIItemInfo what, int many)
        {
            int had;
            bill.want.TryGetValue(what, out had);
            bill.want[what] = had + many;
        }

        private static readonly Dictionary<string, UIItemInfo> plain =
            new Dictionary<string, UIItemInfo>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The thing itself, never the recipe for it.
        ///
        /// Рецепты лежат в той же базе такими же вещами, и зовутся так же. «GetByName» отдаёт
        /// первое совпадение — и в счёт на починку попадали «Рецепт: Золотой слиток» и
        /// «Рецепт: Ремонтные инструменты» вместо слитка и инструментов.
        /// </summary>
        private static UIItemInfo Named(string name)
        {
            string said = (name ?? "").Trim();
            if (said.Length == 0) return null;

            UIItemInfo kept;
            if (plain.TryGetValue(said, out kept)) return kept;

            kept = null;

            try
            {
                UIItemDatabase db = UIItemDatabase.Instance;
                if (db != null && db.items != null)
                {
                    foreach (UIItemInfo thing in db.items)
                    {
                        if (thing == null || thing is CraftRecipe) continue;
                        if (!string.Equals(thing.Name, said, StringComparison.OrdinalIgnoreCase)) continue;

                        kept = thing;
                        break;
                    }
                }

                if (kept == null) kept = db != null ? db.GetByName(said) : null;
            }
            catch
            {
            }

            plain[said] = kept;
            return kept;
        }

        /// <summary>How much of this the party actually has.</summary>
        internal static int Have(UIItemInfo what)
        {
            try
            {
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

        /// <summary>Marks up everything the bill asks for that is not to hand.</summary>
        internal static void Check(Bill bill)
        {
            if (bill == null) return;

            bill.missing.Clear();

            foreach (KeyValuePair<UIItemInfo, int> one in bill.want)
            {
                int have = Have(one.Key);
                if (have >= one.Value) continue;

                bill.missing.Add($"{one.Key.LocalizedName} {have}/{one.Value}");
            }
        }

        /// <summary>Pays the bill, or says it cannot.</summary>
        internal static bool Pay(Bill bill)
        {
            if (!Enabled.Value || bill == null) return true;

            Check(bill);

            if (!bill.Enough())
            {
                GameController.ShowMessage(
                    "Не хватает: " + string.Join(", ", bill.missing.ToArray()), 4f);

                if (Telling.Value)
                {
                    ItemForgePlugin.Log.LogInfo("Починка не вышла, не хватает: "
                        + string.Join(", ", bill.missing.ToArray()));
                }

                return false;
            }

            try
            {
                List<string> spent = new List<string>();

                foreach (KeyValuePair<UIItemInfo, int> one in bill.want)
                {
                    PartyManager.instance.RemoveItemInParty(one.Key, one.Value);
                    spent.Add($"{one.Key.LocalizedName} x{one.Value}");
                }

                if (Telling.Value && spent.Count > 0)
                {
                    ItemForgePlugin.Log.LogInfo("На починку ушло: "
                        + string.Join(", ", spent.ToArray()));
                }

                return true;
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог списать припас: " + e);
                return false;
            }
        }

        /// <summary>The bill, said in one line for the waiting window.</summary>
        internal static string Word(Bill bill)
        {
            if (bill == null || bill.want.Count == 0) return null;

            Check(bill);

            List<string> said = new List<string>();

            foreach (KeyValuePair<UIItemInfo, int> one in bill.want)
            {
                int have = Have(one.Key);

                said.Add(have >= one.Value
                    ? $"{one.Key.LocalizedName} x{one.Value}"
                    : $"{one.Key.LocalizedName} x{one.Value} (есть {have})");
            }

            return string.Join(", ", said.ToArray());
        }
    }
}
