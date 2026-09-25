using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using UnityEngine;

namespace DemonLook
{
    /// <summary>
    /// Townsfolk buy clothes and change into them.
    ///
    /// Раз в месяц всякий горожанин покупает себе что-нибудь из одежды в лавке доспехов и тканей
    /// своего города — самое дешёвое, что ему по карману: рубаху, штаны, шапку. Покупку он тут
    /// же надевает, а старую вещь сдаёт лавке: одежда ходит по кругу, из мира не пропадает.
    /// Латы горожанин не покупает — только ткань да кожу. Пока героя в городе нет, покупки
    /// идут без него: с полки просто пропадает купленное и в кассе прибавляется.
    /// </summary>
    internal static class Wardrobe
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> PerDay;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Wardrobe", "Enabled", true,
                "Let the townsfolk buy clothes once a month and wear what they bought.");

            PerDay = config.Bind("Wardrobe", "PerDay", 3,
                new ConfigDescription("How many townsfolk change their clothes in a day while the hero is in town. Each change "
                    + "rebuilds a body, so the number is kept small.",
                    new AcceptableValueRange<int>(0, 50)));
        }

        private static readonly int[] slots = { 4, 7, 2 };
        private static int doneDay = -1;
        private static int doneCount;
        private static float next;

        private static bool Light(UIItemInfo info)
        {
            UIArmorInfo a = info as UIArmorInfo;
            if (a == null || a.isUnique || (int)a.tier > (int)ItemTier.T2) return false;
            if (a.goodsType == GoodsType.Clothing) return true;
            string c = a.armourClass.ToString();
            return c == "Cloth" || c == "PaddingArmor" || c == "LightLeatherArmor" || c == "hat" || c == "headband";
        }

        internal static void Tick()
        {
            if (Enabled == null || !Enabled.Value || (bool)WorldTravelManager.instance) return;

            CityTownManager city = CityTownManager.instance;
            if (city == null || city.shops == null || city.shops.Count < 2) return;

            float now = Time.unscaledTime;
            if (now < next) return;
            next = now + 15f;

            int today = Souls.Today();
            if (doneDay != today) { doneDay = today; doneCount = 0; }
            if (doneCount >= PerDay.Value || Shutters.Closed()) return;

            Shop shop = city.shops[1];
            if (shop == null || shop.itemstock == null) return;

            try { Change(city.faction, shop, today); }
            catch (Exception e) { DemonLookPlugin.Log.LogWarning("Гардероб: не переоделся: " + e.Message); }
        }

        private static void Change(Faction town, Shop shop, int today)
        {
            foreach (AIBehaviorController brain in UnityEngine.Object.FindObjectsOfType<AIBehaviorController>())
            {
                HumaniodUnit man = brain != null ? brain.GetComponent<UnitAttribute>() as HumaniodUnit : null;
                if (man == null || man.Data == null || man.Data.isdead || man.inParty || man.Data.team != town) continue;
                if (man.isSleeping || man.isEngaged || man.isTalking || man.items == null) continue;
                if (Crime.Guard(man) || Crime.QuestPerson(man) || Kids.Is(man)) continue;
                if (man.info != null && man.info.utype.ToString() == "keyNPC") continue;

                NPCSaveData npc = man.Data;
                if (npc == null || npc.heroCareer != null) continue;
                if (Mathf.Abs(today + npc.id) % 30 != 0) continue;

                EquipmentManager gear = man.equipmentmanger;
                if (gear == null || !gear.isInited || gear.equipInfos == null) continue;

                // Меняет то, что на нём дешевле всего: из трёх мест одежды.
                int slot = -1;
                int worst = int.MaxValue;
                foreach (int s in slots)
                {
                    if (s >= gear.equipInfos.Length) continue;
                    EquipInfo e = gear.equipInfos[s];
                    int v = e != null && e.IsEquiped() ? e.inventory.Value : 0;
                    if (v < worst) { worst = v; slot = s; }
                }
                if (slot < 0) continue;

                Inventory pick = null;
                foreach (Inventory inv in shop.itemstock.items)
                {
                    UIEquipmentInfo info = inv != null ? inv.itemInfo as UIEquipmentInfo : null;
                    if (info == null || !Light(info) || info.EquipType != gear.equipInfos[slot].slotType) continue;
                    if (inv.Value > man.items.money || inv.Value <= worst) continue;
                    if (pick == null || inv.Value < pick.Value) pick = inv;
                }
                if (pick == null) continue;

                // Купил: деньги лавке, вещь на себя, старое — лавке за четверть цены.
                man.items.money -= pick.Value;
                shop.itemstock.money += pick.Value;
                shop.itemstock.items.Remove(pick);

                Inventory old = gear.equipInfos[slot].IsEquiped() ? gear.equipInfos[slot].inventory : null;
                Nightwatch.Muted = true;
                try
                {
                    if (old != null) gear.UnequipItem(slot);
                    gear.EquipItem(pick, slot);
                }
                finally
                {
                    Nightwatch.Muted = false;
                }

                if (old != null)
                {
                    int back = Mathf.Max(1, old.Value / 4);
                    shop.itemstock.AddInventory(old);
                    if (shop.itemstock.money >= back) { shop.itemstock.money -= back; man.items.money += back; }
                }

                Economy.Place p = Census.Here();
                if (p != null)
                {
                    float had;
                    if (p.demand.TryGetValue(1, out had) && had >= 1f) p.demand[1] = had - 1f;
                    Economy.Touch();
                }

                doneCount++;
                return;
            }
        }
    }
}
