using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// What wear does to a thing, what wears it, and what it costs to undo.
    ///
    /// Игра знала о прочности три ступени: выше половины вещь цела, ниже половины теряет
    /// четверть, ниже четверти — половину, в нуле не работает вовсе. Поверх этого лежали два
    /// наших правила, писанные в разное время и друг о друге не знавшие: одно пропускало
    /// сквозь латы столько процентов урона, сколько процентов прочности они потеряли, другое
    /// тупило клинок от первой же царапины. Три счёта на одно и то же.
    ///
    /// Теперь счёт один. До семидесяти процентов прочности вещь делает своё дело целиком: латы
    /// держат, клинок режет. Ниже — за каждый потерянный процент теряется процент дела, и
    /// разбитая в ноль вещь делает ещё треть. Кираса, державшая удар в 248, на половине
    /// прочности держит 198, в нуле — 74.
    ///
    /// Изнашивается вещь тоже по одному правилу на каждую: доспех — только когда сквозь него
    /// прошёл удар, на десятую долю прошедшего; клинок — на десятую долю пробитой им брони;
    /// щит — на десятую долю веса оружия, которое он принял. Кольца, амулеты и пояса — не
    /// броня, а украшения: прочности у них нет вовсе.
    /// </summary>
    internal static class Tatter
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Keeps;
        internal static ConfigEntry<float> Per;
        internal static ConfigEntry<float> RepairFree;
        internal static ConfigEntry<bool> Trinkets;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Tatter", "Enabled", true,
                "One rule for what wear does and what wears a thing: armour and weapons work in "
                + "full down to a share of their durability, and lose a percent of their work "
                + "for every percent below it; armour wears only by what got through it, a blade "
                + "by the armour it pierced, a shield by the weight of what it caught.");

            Keeps = config.Bind("Tatter", "Keeps", 0.7f,
                new ConfigDescription(
                    "Down to what share of its durability a thing works in full. Below it, every "
                    + "percent lost takes a percent of the work: at seven tenths a breastplate "
                    + "holding 248 still holds 248, at a half it holds 198, at nothing it holds "
                    + "74. The same for the edge of a blade.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Per = config.Bind("Tatter", "Per", 10f,
                new ConfigDescription(
                    "What each wearing is divided by. Armour loses the damage that got through "
                    + "it divided by this; a blade loses the armour it pierced divided by this; "
                    + "a shield loses the weight of the weapon it caught divided by this. A hammer "
                    + "wears plate faster than a dagger, because its share goes through the "
                    + "steel and a dagger goes past it.",
                    new AcceptableValueRange<float>(1f, 1000f)));

            RepairFree = config.Bind("Tatter", "RepairFree", 0.8f,
                new ConfigDescription(
                    "Down to what share of durability mending goes at its ordinary pace. Below "
                    + "it every tenth lost makes each point one more time as slow: under eight "
                    + "tenths twice, under seven three times, and a thing worn to nothing nine "
                    + "times as long.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Trinkets = config.Bind("Tatter", "Trinkets", true,
                "Rings, amulets and belts are ornaments, not armour: they have no durability, no "
                + "bar and nothing to mend. The one exception are the amulets that stand against "
                + "magic in the game itself — their bar is what they have left to absorb.");

            Telling = config.Bind("Tatter", "Telling", true,
                "Say in the log, for the first few, what wore what and by how much.");
        }

        // ----------------------------------------------------------------- что вещь ещё может

        /// <summary>How much of its work a thing still does, from what is left of it.</summary>
        internal static float Holds(Inventory thing)
        {
            if (Enabled == null || !Enabled.Value) return GameStep(thing);
            if (thing == null || thing.itemInfo == null) return 1f;
            if (thing.durability < 0f || thing.itemInfo.noDurability) return 1f;
            if (thing.itemInfo.durability <= 0f) return 1f;

            float left = thing.DurPercent;
            float keeps = Mathf.Clamp01(Keeps.Value);

            if (left >= keeps) return 1f;

            return Mathf.Clamp01(1f - (keeps - left));
        }

        /// <summary>The step the game itself takes off, so that it can be put back.</summary>
        internal static float GameStep(Inventory thing)
        {
            if (thing == null || thing.itemInfo == null) return 1f;

            int state = thing.DurState;
            if (state <= 0) return 1f;
            if (state >= 3) return 0f;

            return 1f - 0.25f * state;
        }

        // ----------------------------------------------------------------- износ

        private static int told;

        /// <summary>Takes this much durability off, as it is, with no ladder on top.</summary>
        internal static void Take(Inventory thing, float much, string why)
        {
            if (thing == null || thing.itemInfo == null || much <= 0f) return;
            if (thing.durability < 0f || thing.itemInfo.noDurability) return;

            int was = thing.DurState;
            float before = thing.durability;

            thing.durability = Mathf.Max(0f, thing.durability - much);

            try
            {
                thing.onDurabilityChange.Invoke(thing);
                if (thing.DurState != was) thing.onDurStateChange.Invoke(thing);
            }
            catch
            {
            }

            if (Telling.Value && told < 40)
            {
                told++;
                ItemForgePlugin.Log.LogInfo($"Износ ({why}): «{thing.itemInfo.Name}» "
                    + $"{before:0.#} → {thing.durability:0.#} (−{much:0.##}).");
            }
        }

        /// <summary>
        /// Wears what a blow went through and what struck it — after the blow, once.
        ///
        /// Сколько прошло сквозь железо и сколько железа пробито, записал расчёт брони, когда
        /// считал сам удар. Здесь эта запись забирается и тратится.
        /// </summary>
        internal static void Struck(UnitAttribute victim, Attack attack, Inventory worn)
        {
            if (Enabled == null || !Enabled.Value || victim == null || attack == null) return;

            Breach.Blow blow = Breach.Took(victim);
            if (blow == null) return;

            float per = Mathf.Max(1f, Per.Value);

            // Доспех — только тем, что прошло сквозь него самого.
            if (worn != null && blow.iron > 0f && blow.coming > 0f)
            {
                Take(worn, blow.coming * blow.iron / per, "доспех пробит");
            }

            // Клинок — той бронёй, что он пробил. Голое тело клинка не тупит.
            if (blow.pierced > 0f)
            {
                Inventory blade = Pierce.Held(attack.weapon);
                if (blade != null) Take(blade, blow.pierced / per, "клинок о броню");
            }
        }

        /// <summary>Wears a shield by the weight of the weapon it caught.</summary>
        internal static void Caught(Inventory shield, Attack attack)
        {
            if (Enabled == null || !Enabled.Value || shield == null || attack == null) return;

            Inventory blade = Pierce.Held(attack.weapon);
            if (blade == null || blade.itemInfo == null) return;

            float heft = blade.itemInfo.weight;
            if (heft <= 0f) return;

            Take(shield, heft / Mathf.Max(1f, Per.Value), "щит принял удар");
        }

        // ----------------------------------------------------------------- починка

        /// <summary>How many times as slow each point of mending goes at this wear.</summary>
        internal static int RepairTimes(Inventory thing)
        {
            if (Enabled == null || !Enabled.Value || thing == null || thing.itemInfo == null) return 1;

            int left = Mathf.FloorToInt(thing.DurPercent * 100f + 0.0001f);
            int free = Mathf.RoundToInt(Mathf.Clamp01(RepairFree.Value) * 100f);

            if (left >= free) return 1;

            // Под восемью десятыми — вдвое, под семью — втрое, и так до девяти раз в нуле.
            return 1 + Mathf.CeilToInt((free - left) / 10f);
        }

        /// <summary>
        /// Mends what is worn by this much work, worst first, each point at its own pace.
        ///
        /// Игра раздаёт свои очки починки по одному, начиная с самой изношенной вещи. Здесь
        /// то же, только очко на изношенной вещи стоит дороже: вещь в нуле чинится вдевятеро
        /// медленнее, и чем ближе к целой, тем быстрее идёт дело.
        /// </summary>
        internal static void Mend(HumaniodUnit who, float amount)
        {
            if (who == null || who.equipmentmanger == null || amount <= 0f) return;
            if (who.equipmentmanger.equipInfos == null) return;

            List<Inventory> list = new List<Inventory>();

            foreach (EquipInfo slot in who.equipmentmanger.equipInfos)
            {
                if (slot == null || !slot.IsEquiped() || slot.inventory == null) continue;

                UIEquipmentInfo made = slot.inventory.itemInfo as UIEquipmentInfo;
                if (made == null || made.noDurability || made.cannotRepair) continue;
                if (slot.inventory.durability >= made.durability) continue;

                list.Add(slot.inventory);
            }

            list = list.OrderBy(x => x.DurPercent).ToList();

            int guard = 0;
            while (amount > 0.0001f && list.Count > 0 && guard++ < 200000)
            {
                Inventory thing = list[0];
                int times = RepairTimes(thing);

                float step = Mathf.Min(1f, amount / times);
                bool whole = thing.RestoreDurability(step);
                amount -= step * times;

                if (whole || thing.DurPercent >= 1f) list.RemoveAt(0);
            }
        }

        // ----------------------------------------------------------------- украшения

        private static bool swept;

        /// <summary>
        /// Takes durability away from rings, amulets and belts, once, in the catalogue.
        ///
        /// У амулета, который в самой игре стоит против магии, полоска остаётся: это не
        /// прочность, а то, сколько он ещё может принять в себя, прежде чем опустеть.
        /// </summary>
        internal static void Catalogue()
        {
            if (swept || Enabled == null || !Enabled.Value || !Trinkets.Value) return;

            try
            {
                UIItemDatabase db;
                try { db = UIItemDatabase.Instance; }
                catch { return; }

                if (db == null || db.items == null) return;

                swept = true;
                int ornaments = 0, wards = 0;

                foreach (UIItemInfo any in db.items)
                {
                    UIEquipmentInfo worn = any as UIEquipmentInfo;
                    if (worn == null) continue;

                    EquipSlotType where = worn.EquipType;
                    if (where != EquipSlotType.finger && where != EquipSlotType.neck
                        && where != EquipSlotType.belt) continue;

                    if (where == EquipSlotType.neck && Ward.Keeps(worn))
                    {
                        wards++;
                        continue;
                    }

                    worn.noDurability = true;
                    ornaments++;
                }

                ItemForgePlugin.Log.LogInfo($"Украшения без прочности: {ornaments}; "
                    + $"амулетов с поглощением магии: {wards}.");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог снять прочность с украшений: " + e.Message);
            }
        }
    }

    // Защиту доспеха от стихий и то, что показывает лист персонажа, игра собирает здесь — по
    // своим ступеням и без разбитых вещей вовсе. Правим разницу: сколько она положила и
    // сколько положено по нашему правилу. Приписки разбитой вещи она не считает, и мы вслед
    // за ней: разбитая держит треть, но свойств своих не даёт.
    [HarmonyPatch(typeof(HumaniodUnit), "CountEquipmentsBonus")]
    internal static class CountEquipmentsBonus_Tatter_Patch
    {
        private static readonly AccessTools.FieldRef<UnitAttribute, float[]> drmd =
            AccessTools.FieldRefAccess<UnitAttribute, float[]>("damageDRMD");

        private static void Postfix(HumaniodUnit __instance)
        {
            if (Tatter.Enabled == null || !Tatter.Enabled.Value || __instance == null) return;

            try
            {
                if (__instance.Data == null || !__instance.Data.equipsInited) return;

                EquipmentManager gear = __instance.equipmentmanger;
                if (gear == null || !gear.isInited || gear.equipInfos == null) return;

                float[] sum = drmd((UnitAttribute)(object)__instance);
                if (sum == null) return;

                foreach (EquipInfo slot in gear.equipInfos)
                {
                    if (slot == null || !slot.IsEquiped() || slot.inventory == null) continue;

                    UIArmorInfo coat = slot.inventory.itemInfo as UIArmorInfo;
                    if (coat == null || coat.damageDR == null) continue;

                    float mine = Tatter.Holds(slot.inventory);
                    float game = Tatter.GameStep(slot.inventory);

                    float more = mine - game;
                    if (Mathf.Abs(more) < 0.0001f) continue;

                    for (int j = 0; j < sum.Length && j < coat.damageDR.Length; j++)
                    {
                        sum[j] += coat.damageDR[j] * more;
                    }
                }
            }
            catch
            {
            }
        }
    }

    // Клинок игра тупит ступенями ещё при надевании. Возвращаем ему полный урон: насколько он
    // затупился, решается в ударе, по нашему правилу.
    [HarmonyPatch(typeof(Weapon), "SetWeapon")]
    internal static class SetWeapon_Tatter_Patch
    {
        private static void Postfix(Weapon __instance, Inventory weapon)
        {
            if (Tatter.Enabled == null || !Tatter.Enabled.Value) return;
            if (__instance == null || weapon == null) return;

            try
            {
                UIWeaponInfo made = weapon.itemInfo as UIWeaponInfo;
                if (made == null || made.WeaponType == WeaponType.quiver) return;

                int state = weapon.DurState;
                if (state < 1) return;

                float game = 1f - 0.25f * state;
                if (game <= 0.0001f) return;

                float back = 1f / game;

                if (__instance.damage != null) __instance.damage *= back;
                if (__instance.BSdamage != null) __instance.BSdamage *= back;
            }
            catch
            {
            }
        }
    }

    // Починка снаряжения идёт через одну дверь: и наковальня в лагере, и походное действие
    // «Ремонт». Смена решает, возьмётся ли человек за дело вовсе; дальше — наша лесенка.
    [HarmonyPatch(typeof(HumaniodUnit), "RepairEquipments")]
    [HarmonyPriority(Priority.Low)]
    internal static class RepairEquipments_Tatter_Patch
    {
        private static bool Prefix(HumaniodUnit __instance, float amount, bool __runOriginal)
        {
            // Смена уже отказала — работы нет.
            if (!__runOriginal) return false;
            if (Tatter.Enabled == null || !Tatter.Enabled.Value) return true;

            try
            {
                Tatter.Mend(__instance, amount);
                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}
