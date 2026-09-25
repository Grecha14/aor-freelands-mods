using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace DemonLook
{
    /// <summary>
    /// Fuller shops that restock instead of wiping their shelves, and prices that follow the shelf.
    ///
    /// Городская лавка в первый раз выкладывает вдвое больше товара, и денег на закупку у неё
    /// вдвое больше. Дальше она не берёт ничего из воздуха: раз в три дня полка пополняется
    /// только тем, что сделали мастера города, привезли караваны и что лежит на складе, — а
    /// что лежало, лежит, и что вы продали, тоже. Ничего не выбрасывается, кроме испорченной еды.
    ///
    /// Цена идёт за полкой. Одинаковых вещей много — каждая следующая на три сотых дешевле, но не
    /// больше чем на три десятых; вещь последняя — на пятую часть дороже. И лавка, у которой
    /// такого нет, платит за него дороже, а у которой его завались — дешевле.
    /// </summary>
    internal static class Market
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Goods;
        internal static ConfigEntry<float> Purse;
        internal static ConfigEntry<int> RestockDays;
        internal static ConfigEntry<float> Shelf;
        internal static ConfigEntry<bool> Prices;
        internal static ConfigEntry<float> Glut;
        internal static ConfigEntry<float> GlutFloor;
        internal static ConfigEntry<float> Last;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Market", "Enabled", true,
                "Let the town shops carry more, restock instead of wiping the shelf, and keep what the hero sold.");

            Goods = config.Bind("Market", "Goods", 2f,
                new ConfigDescription("How many times more goods a town shop puts out.", new AcceptableValueRange<float>(1f, 10f)));

            Purse = config.Bind("Market", "Purse", 2f,
                new ConfigDescription("How many times more money a town shop has to buy with.", new AcceptableValueRange<float>(1f, 20f)));

            RestockDays = config.Bind("Market", "RestockDays", 3,
                new ConfigDescription("Every how many days a town shop restocks.", new AcceptableValueRange<int>(1, 30)));

            Shelf = config.Bind("Market", "Shelf", 1.5f,
                new ConfigDescription("How full a shelf may grow over one fresh stock before the old goods go.",
                    new AcceptableValueRange<float>(1f, 5f)));

            Prices = config.Bind("Market", "Prices", true, "Let the price follow how much of a thing is on the shelf.");

            Glut = config.Bind("Market", "Glut", 0.03f,
                new ConfigDescription("How much cheaper each further copy on the shelf makes it.", new AcceptableValueRange<float>(0f, 0.5f)));

            GlutFloor = config.Bind("Market", "GlutFloor", 0.7f,
                new ConfigDescription("The lowest the glut can bring a price down to.", new AcceptableValueRange<float>(0.1f, 1f)));

            Last = config.Bind("Market", "Last", 1.2f,
                new ConfigDescription("What the last copy on the shelf costs over the usual.", new AcceptableValueRange<float>(1f, 3f)));
        }

        /// <summary>A shop of a town: the town's own stalls and the merchants who live there.</summary>
        internal static bool TownShop(Shop shop)
        {
            if (shop == null || Enabled == null || !Enabled.Value) return false;
            if (shop.isCommonTownShop) return true;

            try
            {
                UnitAttribute keeper = shop.vender != null ? shop.vender.unit : null;
                return keeper != null && keeper.Data != null && FactionManager.IsTownFaction(keeper.Data.team);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// A shop that lives off the town's own economy: arms, armour, jewels, materials, potions
        /// and food of every kind. Books, magic, the black market, drink and the tavern are left
        /// to the game: the economy makes none of that, and closing them would empty them for good.
        /// </summary>
        internal static bool Closed(Shop shop)
        {
            if (!TownShop(shop) || shop is CitytownBarManager) return false;
            switch (shop.shopType)
            {
                case ShopType.weaponShop:
                case ShopType.armorShop:
                case ShopType.ornamentShop:
                case ShopType.materialShop:
                case ShopType.alchemyShop:
                case ShopType.foodShop:
                case ShopType.bakeryShop:
                case ShopType.fruitShop:
                case ShopType.meatShop:
                case ShopType.fishShop:
                    return true;
            }
            return shop.isCommonTownShop;
        }

        // ------------------------------------------------------------------ выкладка

        internal struct Counts { internal int main, material, consumable, recipe; }

        internal static Counts Widen(ShopGoodsList list, float extra = 1f)
        {
            Counts was = new Counts
            {
                main = list.refreshNum,
                material = list.refreshNum_Material,
                consumable = list.refreshNum_Consumable,
                recipe = list.refreshNum_Recipe,
            };

            float k = Goods.Value * extra;
            list.refreshNum = Mathf.RoundToInt(was.main * k);
            list.refreshNum_Material = Mathf.RoundToInt(was.material * k);
            list.refreshNum_Consumable = Mathf.RoundToInt(was.consumable * k);
            list.refreshNum_Recipe = Mathf.RoundToInt(was.recipe * k);
            return was;
        }

        internal static void Narrow(ShopGoodsList list, Counts was)
        {
            list.refreshNum = was.main;
            list.refreshNum_Material = was.material;
            list.refreshNum_Consumable = was.consumable;
            list.refreshNum_Recipe = was.recipe;
        }

        // Что продал герой в этот заход — на полке неприкосновенно.
        private static readonly Dictionary<Shop, HashSet<UIItemInfo>> sold = new Dictionary<Shop, HashSet<UIItemInfo>>();

        internal static void Sold(UIItemInfo item)
        {
            try
            {
                Shop shop = VenderManager.instance != null ? VenderManager.instance.shop : null;
                if (shop == null || item == null) return;

                HashSet<UIItemInfo> mine;
                if (!sold.TryGetValue(shop, out mine)) sold[shop] = mine = new HashSet<UIItemInfo>();
                mine.Add(item);
            }
            catch
            {
            }
        }

        internal class Before
        {
            internal List<Inventory> items;
            internal int money;
        }

        // Сейчас идёт первая выкладка этой лавки: только её игра и наполняет сама.
        internal static Shop firstFill;

        internal static Before Keep(Shop shop)
        {
            if (!TownShop(shop) || shop.itemstock == null) return null;

            shop.autoRefresh = true;
            shop.refreshDays = RestockDays.Value;
            if (shop.refreshCD > RestockDays.Value) shop.refreshCD = RestockDays.Value;

            if (!shop.firstTimeRefreshed)
            {
                firstFill = shop;
                return new Before { items = null, money = 0 };
            }
            firstFill = null;
            return new Before { items = new List<Inventory>(shop.itemstock.items), money = shop.itemstock.money };
        }

        /// <summary>After a fresh stock: the old goods back on the shelf, and the shelf kept within bounds.</summary>
        internal static void Restock(Shop shop, Before before)
        {
            if (before == null || shop.itemstock == null) return;

            ItemStock stock = shop.itemstock;
            int fresh = stock.items.Count;
            int regain = Mathf.RoundToInt((float)shop.moneyRegain * Purse.Value);

            if (before.items == null)
            {
                stock.money = Mathf.Max(stock.money, regain);
                return;
            }

            HashSet<UIItemInfo> special = new HashSet<UIItemInfo>();
            if (shop.specialGoods != null)
            {
                foreach (Inventory s in shop.specialGoods)
                {
                    if (s != null && s.itemInfo != null) special.Add(s.itemInfo);
                }
            }

            HashSet<UIItemInfo> mine;
            sold.TryGetValue(shop, out mine);

            // Что лежало на полке — лежит: из мира ничто не пропадает.
            foreach (Inventory inv in before.items)
            {
                if (inv == null || inv.itemInfo == null || special.Contains(inv.itemInfo)) continue;
                stock.AddInventory(inv);
            }

            stock.money = Mathf.Max(before.money, regain);
            if (mine != null) mine.Clear();

            // И пополняется полка только из хозяйства города.
            try { Economy.Buyers(shop); } catch { }
            try { Economy.Deliver(shop); } catch { }
            try { Economy.Supply(shop); } catch { }
        }

        // ------------------------------------------------------------------ цена от полки

        private static int countedFrame = -1;
        private static ItemStock countedStock;
        private static readonly Dictionary<UIItemInfo, int> counted = new Dictionary<UIItemInfo, int>();

        private static int OnShelf(UIItemInfo item)
        {
            VenderManager vm = VenderManager.instance;
            ItemStock stock = vm != null ? vm.stocks : null;
            if (stock == null || stock.items == null || item == null) return -1;

            if (countedFrame != Time.frameCount || !ReferenceEquals(countedStock, stock))
            {
                countedFrame = Time.frameCount;
                countedStock = stock;
                counted.Clear();
                foreach (Inventory inv in stock.items)
                {
                    if (inv == null || inv.itemInfo == null) continue;
                    int n;
                    counted.TryGetValue(inv.itemInfo, out n);
                    counted[inv.itemInfo] = n + Mathf.Max(1, inv.stackNum);
                }
            }

            int have;
            return counted.TryGetValue(item, out have) ? have : 0;
        }

        /// <summary>What n copies on the shelf make of the price: the last dearer, a glut cheaper.</summary>
        internal static float ByShelf(int n)
        {
            if (n <= 1) return Last.Value;
            return Mathf.Max(GlutFloor.Value, 1f - Glut.Value * (n - 1));
        }

        internal static bool Priced()
        {
            if (Enabled == null || !Enabled.Value || Prices == null || !Prices.Value) return false;
            VenderManager vm = VenderManager.instance;
            return vm != null && vm.stocks != null && (vm.shop == null || TownShop(vm.shop));
        }

        internal static float Buy(UIItemInfo item)
        {
            int n = OnShelf(item);
            return n < 0 ? 1f : ByShelf(Mathf.Max(1, n));
        }

        internal static float Sell(UIItemInfo item)
        {
            int n = OnShelf(item);
            return n < 0 ? 1f : ByShelf(n + 1);
        }
    }

    [HarmonyPatch(typeof(ShopGoodsList), "AddGoodsToStock")]
    internal static class Goods_Market_Patch
    {
        private static void Prefix(ShopGoodsList __instance, Shop shop, out Market.Counts? __state)
        {
            __state = null;
            try
            {
                // Из воздуха — только первая выкладка лавки; дальше всё с полки и со склада.
                if (Market.TownShop(shop))
                    __state = Market.Widen(__instance, !Market.Closed(shop) || ReferenceEquals(shop, Market.firstFill) ? Economy.FoodShelf(shop) : 0f);
            }
            catch
            {
            }
        }

        private static Exception Finalizer(ShopGoodsList __instance, Market.Counts? __state, Exception __exception)
        {
            try { if (__state.HasValue) Market.Narrow(__instance, __state.Value); } catch { }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Shop), "RefreshShop")]
    internal static class Refresh_Market_Patch
    {
        private static void Prefix(Shop __instance, out Market.Before __state)
        {
            __state = null;
            try { __state = Market.Keep(__instance); } catch { }
        }

        private static void Postfix(Shop __instance, Market.Before __state)
        {
            try { Market.Restock(__instance, __state); } catch (Exception e) { DemonLookPlugin.Log.LogWarning("Лавка: не доложилась: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(Shop), "OnDayPassed")]
    internal static class Day_Market_Patch
    {
        private static void Prefix(Shop __instance)
        {
            try
            {
                if (!Market.TownShop(__instance)) return;
                __instance.autoRefresh = true;
                __instance.refreshDays = Market.RestockDays.Value;
                if (__instance.refreshCD > Market.RestockDays.Value) __instance.refreshCD = Market.RestockDays.Value;
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(VenderManager), "OnPlayerSellItem")]
    internal static class PlayerSell_Market_Patch
    {
        private static void Postfix(UIItemInfo item)
        {
            Market.Sold(item);
        }
    }

    [HarmonyPatch(typeof(VenderManager), "GetBuyPriceMD")]
    internal static class BuyPrice_Market_Patch
    {
        private static void Postfix(UIItemInfo item, ref float __result)
        {
            try { if (Market.Priced()) __result *= Market.Buy(item); } catch { }
        }
    }

    [HarmonyPatch(typeof(VenderManager), "GetSellPriceMD")]
    internal static class SellPrice_Market_Patch
    {
        private static void Postfix(UIItemInfo item, ref float __result)
        {
            try { if (Market.Priced()) __result *= Market.Sell(item); } catch { }
        }
    }
}
