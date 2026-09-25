using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Decides in what state a man's gear comes into the world, and where it goes when he
    /// leaves it.
    ///
    /// Две вещи, которые оказались одной. Игра создаёт снаряжение существа прямо в слот —
    /// «new Inventory(item)», затем «EquipItem» — и в сумку его не кладёт: надетое существует
    /// лишь потому, что на него смотрит слот. Отсюда обе беды сразу.
    ///
    /// Первая: снять вещь значит обнулить единственную ссылку на неё, то есть уничтожить. Моё
    /// раздевание тела при обыске делало именно это со всеми, кого одевал не я, — добыча
    /// исчезала, и заметно это не было, потому что у своих охотников вещи лежали ещё и в
    /// сумке. Вторая: положенная мною вещь числилась и в сумке, и в слоте, а вытесненная
    /// оставалась ничьей — отсюда второй амулет с нетронутой прочностью.
    ///
    /// Лечится одним понятием: у вещи одно место жительства. При жизни — слот, после смерти —
    /// сумка, и переезд означает перенос того же объекта, а не создание нового.
    /// </summary>
    internal static class Keeping
    {
        internal static ConfigEntry<bool> Handover;
        internal static ConfigEntry<bool> ByLevel;
        internal static ConfigEntry<float> Worst;
        internal static ConfigEntry<int> Seasoned;
        internal static ConfigEntry<bool> KeepAtDeath;

        // Пока это правда, износ проходит мимо вещей: идёт родная порча за факт смерти.
        internal static bool dying;

        internal static void Bind(ConfigFile config)
        {
            Handover = config.Bind("Keeping", "Handover", true,
                "Move what a man was wearing into his bag at the moment he dies, rather than "
                + "when somebody opens him. The same object, with the durability it had when he "
                + "fell — nothing made, nothing lost, nothing left in two places at once.");

            ByLevel = config.Bind("Keeping", "ByLevel", true,
                "Let the state of an enemy's gear follow his standing. The game gives every "
                + "unit outside the player's faction the same worn sixty to eighty percent, so "
                + "a road bandit and a king's champion are equally shabby, and the loot off "
                + "either is equally middling.");

            Worst = config.Bind("Keeping", "Worst", 0.6f,
                new ConfigDescription(
                    "The worst state gear can be in, for a creature of no standing at all.",
                    new AcceptableValueRange<float>(0.1f, 1f)));

            KeepAtDeath = config.Bind("Keeping", "KeepAtDeath", true,
                "Let a dead man's gear keep exactly the durability it had when he fell. The "
                + "game takes another twenty to seventy percent off every worn piece at the "
                + "moment of death, after the last blow has already landed — a toll for dying "
                + "rather than a consequence of anything. What the fight did to a thing is what "
                + "the thing shows.");

            Seasoned = config.Bind("Keeping", "Seasoned", 30,
                new ConfigDescription(
                    "The level from which gear is always whole. Somebody who has lived to this "
                    + "has either the means to keep his kit or the sense to take better from "
                    + "the people he outlived.",
                    new AcceptableValueRange<int>(1, 100)));
        }

        /// <summary>What share of whole this creature's gear starts at.</summary>
        internal static float Fresh(UnitAttribute owner)
        {
            if (owner == null || owner.Data == null) return 1f;

            CharacterSaveData data = (CharacterSaveData)(object)owner.Data;

            float grown = Mathf.Clamp01((float)data.level / Mathf.Max(1, Seasoned.Value));
            float least = Mathf.Lerp(Worst.Value, 1f, grown);

            return UnityEngine.Random.Range(least, 1f);
        }

        /// <summary>Hands a body's gear over to its own bag, once, at death.</summary>
        internal static void Hand(UnitAttribute dead)
        {
            if (!Handover.Value || dead == null) return;

            HumaniodUnit body = dead as HumaniodUnit;
            if (body == null || body.equipmentmanger == null || body.items == null) return;

            try
            {
                int moved = 0;

                moved += Move(body, body.equipmentmanger.equipInfos);
                moved += Move(body, body.equipmentmanger.standByWeaponInfos);

                if (moved > 0)
                {
                    ItemForgePlugin.Log.LogInfo($"«{dead.Data.unitname}» отдал телу надетое: "
                        + $"{moved} вещей.");
                }

                Inventory(body, dead);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог передать надетое телу: " + e);
            }
        }

        /// <summary>Writes out what is actually lying in the body, with numbers.</summary>
        private static void Inventory(HumaniodUnit body, UnitAttribute dead)
        {
            if (body.items == null || body.items.items == null) return;

            // Та же таблица, что игрок видит в окне обыска, только с числами. По ней видно
            // без рассуждений: несёт ли вещь на трупе тот износ, который на ней был в бою.
            ItemForgePlugin.Log.LogInfo($"Опись тела «{dead.Data.unitname}»:");

            foreach (Inventory thing in body.items.items)
            {
                if (thing == null || thing.itemInfo == null) continue;

                string state = (thing.durability < 0f || thing.itemInfo.noDurability)
                    ? "без прочности"
                    : $"{thing.durability:0.#} / {thing.itemInfo.durability:0.#} "
                        + $"({Pierce.Share(thing) * 100f:0} %)";

                string ward = "";
                float left = Ward.Left(thing, false);
                if (left >= 0f) ward = $", оберег {left:0}";

                ItemForgePlugin.Log.LogInfo($"    {thing.itemInfo.Name}: {state}{ward}");
            }
        }

        /// <summary>Moves the very things themselves out of the slots and into the bag.</summary>
        private static int Move(HumaniodUnit body, EquipInfo[] slots)
        {
            if (slots == null) return 0;

            int moved = 0;

            foreach (EquipInfo slot in slots)
            {
                if (slot == null || !slot.IsEquiped()) continue;
                if (slot.inventory == null || slot.inventory.itemInfo == null) continue;

                Inventory thing = slot.inventory;

                // Сперва в сумку, потом из слота. Наоборот нельзя: между двумя строками вещь
                // осталась бы без единой ссылки, и это тот самый способ её потерять.
                body.items.AddInventoryNoEvent(thing);
                body.equipmentmanger.UnequipItem(slot);

                moved++;
            }

            return moved;
        }

        /// <summary>Takes out of the bag whatever our own dressing pushed out of a slot.</summary>
        internal static void Displaced(HumaniodUnit body, Inventory pushedOut)
        {
            if (body == null || body.items == null || pushedOut == null) return;

            try
            {
                body.items.RemoveInventory(pushedOut, pushedOut.stackNum);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог убрать вытесненное: " + e.Message);
            }
        }
    }

    // Состояние снаряжения при рождении: по уровню владельца, а не одно на всех.
    [HarmonyPatch(typeof(EquipmentManager), "InitEquips")]
    internal static class InitEquips_Wear_Patch
    {
        private static void Postfix(EquipmentManager __instance)
        {
            if (!Keeping.ByLevel.Value || __instance == null || __instance.unit == null) return;

            try
            {
                UnitAttribute owner = (UnitAttribute)(object)__instance.unit;
                if (owner.Data == null || owner.Data.team == Faction.player) return;

                int touched = 0;

                touched += Season(__instance.equipInfos, owner);
                touched += Season(__instance.standByWeaponInfos, owner);

                if (touched > 0 && Pierce.told < Pierce.Explain.Value)
                {
                    Pierce.told++;
                    ItemForgePlugin.Log.LogInfo($"«{owner.Data.unitname}» "
                        + $"(ур. {((CharacterSaveData)(object)owner.Data).level}) одет: "
                        + $"{touched} вещей по своему званию.");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог состарить снаряжение по уровню: " + e);
            }
        }

        private static int Season(EquipInfo[] slots, UnitAttribute owner)
        {
            if (slots == null) return 0;

            int touched = 0;

            foreach (EquipInfo slot in slots)
            {
                if (slot == null || !slot.IsEquiped()) continue;

                Inventory thing = slot.inventory;
                if (thing == null || thing.itemInfo == null) continue;
                if (thing.durability < 0f || thing.itemInfo.noDurability) continue;

                float whole = thing.itemInfo.durability;
                if (whole <= 0f) continue;

                // Перезаписываем, а не домножаем: игра уже сбила прочность до своих
                // шестидесяти-восьмидесяти, и умножать одно на другое значило бы обобрать
                // существо дважды.
                thing.SetDurability(whole * Keeping.Fresh(owner));
                touched++;
            }

            return touched;
        }
    }

    // Надетое, выданное со стороны, рождается новым и мимо родного одевания проходит. Ловим
    // его здесь: всякая целая вещь, впервые попадающая на чужого бойца, получает состояние по
    // его званию — как если бы он в ней жил, а не получил её только что из воздуха.
    [HarmonyPatch(typeof(EquipmentManager), "EquipItem", new Type[] { typeof(Inventory), typeof(int) })]
    internal static class EquipItem_Season_Patch
    {
        private static void Prefix(EquipmentManager __instance, Inventory inv)
        {
            if (!Keeping.ByLevel.Value || __instance == null || __instance.unit == null) return;
            if (inv == null || inv.itemInfo == null) return;
            if (inv.durability < 0f || inv.itemInfo.noDurability) return;

            try
            {
                UnitAttribute owner = (UnitAttribute)(object)__instance.unit;
                if (owner.Data == null || owner.Data.team == Faction.player) return;

                // Только нетронутые: у ношеной вещи своя история, и переписывать её нельзя.
                float whole = inv.itemInfo.durability;
                if (whole <= 0f || inv.durability < whole) return;

                inv.SetDurability(whole * Keeping.Fresh(owner));
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог состарить выданное: " + e.Message);
            }
        }
    }

    // Смерть: надетое переезжает в сумку только у тех, кто не оставляет тела. Остальные
    // лежат одетыми до обыска — иначе поле боя устлано голыми, а раздевание и есть та самая
    // передача владения, просто она должна случиться позже.
    [HarmonyPatch(typeof(UnitAttribute), "Die")]
    internal static class Die_Handover_Patch
    {
        private static void Prefix(UnitAttribute __instance)
        {
            if (__instance == null || __instance.info == null) return;
            if (!__instance.info.leftTrophyBag) return;

            Keeping.Hand(__instance);
        }
    }

    // Обыск: вот теперь надетое становится добычей.
    [HarmonyPatch(typeof(LootManager), "StartLooting")]
    internal static class StartLooting_Handover_Patch
    {
        private static void Prefix(ItemStock stock, Container cont)
        {
            if (!Keeping.Handover.Value) return;

            try
            {
                UnitAttribute body = null;

                if (cont != null) body = cont.GetComponent<UnitAttribute>();
                if (body == null && stock != null) body = stock.GetComponent<UnitAttribute>();

                if (body != null && body.Data != null && body.Data.isdead) Keeping.Hand(body);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог передать надетое при обыске: " + e);
            }
        }
    }

    // Родная порча за факт смерти: двадцать-семьдесят процентов сверх того, что сделал бой.
    // Отменяем не метод целиком — в нём ещё выпадение оружия и разбор арены, — а только сам
    // съём прочности, поднимая флаг на время его работы.
    [HarmonyPatch(typeof(EquipmentManager), "OnUnitDead")]
    internal static class OnUnitDead_Keep_Patch
    {
        private static void Prefix() { if (Keeping.KeepAtDeath.Value) Keeping.dying = true; }

        private static void Postfix() { Keeping.dying = false; }
    }
}
