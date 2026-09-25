using System;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Makes mending a thing cost what the thing is worth.
    ///
    /// The game prices repair by wear and tier alone:
    ///
    ///     ceil(missing durability) * 2^tier
    ///
    /// which has nothing to do with what is being mended. A legendary plate and a rusty tier-4
    /// helm cost the same to bring back from ruin, and both cost a fraction of what either is
    /// worth. Once every body can be stripped, that is the whole economy: fight, collect,
    /// repair for coppers, sell for gold.
    ///
    /// Here the price follows the item instead. A thing worn to nothing costs its full worth to
    /// make whole again, and half-worn costs half — so repairing is a real decision, and the
    /// broken plate you hauled out of a cave may be worth less than the trouble.
    /// </summary>
    internal static class Repair
    {
        internal static ConfigEntry<bool> Dear;
        internal static ConfigEntry<float> Share;
        internal static ConfigEntry<float> Hard;
        internal static ConfigEntry<float> Floor;
        internal static ConfigEntry<bool> KitsUnsellable;
        internal static ConfigEntry<string> KitName;

        private static bool sealed_;

        internal static void Bind(ConfigFile config)
        {
            Dear = config.Bind("Repair", "Dear", true,
                "Price a repair against the item rather than against its tier.");

            Hard = config.Bind("Repair", "Hard", 1.5f,
                new ConfigDescription(
                    "How steeply the price of mending rises with what the armour holds. At one "
                    + "and a half, plate costs about a hundred times what cloth costs: a full "
                    + "harness is ruinous to keep, and that is the price of being "
                    + "unpierceable. Set to zero to charge by the thing's worth alone.",
                    new AcceptableValueRange<float>(0f, 4f)));

            Floor = config.Bind("Repair", "Floor", 8f,
                new ConfigDescription(
                    "What counts as an ordinary wall for that reckoning: armour holding this "
                    + "much costs what its worth alone would say.",
                    new AcceptableValueRange<float>(1f, 100f)));

            Share = config.Bind("Repair", "Share", 1.0f,
                new ConfigDescription(
                    "What a full repair costs, as a share of the item's own worth. At one, "
                    + "bringing a ruined thing back to new costs exactly what the thing is "
                    + "worth; half-worn costs half of that.",
                    new AcceptableValueRange<float>(0.1f, 3f)));

            KitsUnsellable = config.Bind("Repair", "KitsUnsellable", false,
                "Meddle with how shops carry repair kits. Off, and they are carried the way the "
                + "game intends. This once claimed to take kits out of the shops and did the "
                + "opposite: the flag it sets means «this never sells out», so kits stood on "
                + "the shelf in unlimited supply. Left here only so the flag can be found again.");

            KitName = config.Bind("Repair", "KitName", "RepairKit",
                "Part of the name that marks an item as a repair kit.");
        }

        /// <summary>What the item is actually worth, the way the game shows it.</summary>
        internal static int Worth(UIItemInfo item)
        {
            if (item == null) return 0;

            // Цена в подсказке — не поле value, а оно же, умноженное на качество: каждая
            // ступень удваивает. Считаем так же, иначе «как сама вещь» разойдётся с тем,
            // что игрок видит на ярлыке.
            int step = Mathf.Max(0, (int)item.Quality - 1);
            return Mathf.CeilToInt(item.value * Mathf.Pow(2f, step));
        }

        /// <summary>Closes the shops to repair kits, once, when the database is up.</summary>
        internal static void SealKits()
        {
            if (sealed_ || !KitsUnsellable.Value) return;

            try
            {
                UIItemDatabase db;
                try { db = UIItemDatabase.Instance; }
                catch { return; }

                if (db == null || db.items == null) return;

                string wanted = (KitName.Value ?? "").Trim();
                if (wanted.Length == 0) return;

                sealed_ = true;
                int closed = 0;

                foreach (UIItemInfo item in db.items)
                {
                    if (item == null || item.name == null) continue;
                    if (item.name.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    item.canSellup = false;
                    closed++;
                }

                if (closed > 0)
                {
                    ItemForgePlugin.Log.LogInfo($"Наборы для ремонта сняты с продажи: {closed}.");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог закрыть продажу наборов: " + e);
            }
        }
    }

    // Постфикс на сам расчёт цены: игра спрашивает её в одном месте и для витрины, и для
    // списания денег, так что подменять что-то ещё не нужно.
    [HarmonyPatch]
    internal static class RepairCost_Patch
    {
        private static System.Reflection.MethodBase TargetMethod()
        {
            // Тип известен: окно ремонта. Раньше здесь шёл перебор по всей сборке игры —
            // метод закрытый, и я не хотел зависеть от имени класса. Цена вышла заметной:
            // «AccessTools.DeclaredMethod» пишет предупреждение на каждый промах, и два
            // десятка строк про SkillSet и GortusVenderManager падали в лог при каждом
            // запуске. Спрашиваем прямо.
            System.Reflection.MethodInfo found = AccessTools.DeclaredMethod(
                typeof(RepairManager), "GetShopRepairCost");

            if (found != null) return found;

            // Если игра однажды переименует класс — ищем по-старому, но молча: обычное
            // отражение промахов не логирует.
            Type[] mine;
            try { mine = typeof(Inventory).Assembly.GetTypes(); }
            catch (System.Reflection.ReflectionTypeLoadException partial) { mine = partial.Types; }

            const System.Reflection.BindingFlags any =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.DeclaredOnly;

            foreach (Type type in mine)
            {
                if (type == null) continue;

                System.Reflection.MethodInfo one = type.GetMethod("GetShopRepairCost", any);
                if (one != null) return one;
            }

            ItemForgePlugin.Log.LogWarning("Не нашёл расчёт цены ремонта — цены остаются игровыми.");
            return null;
        }

        private static void Postfix(Inventory inv, ref int __result)
        {
            if (!Repair.Dear.Value || inv == null || inv.itemInfo == null) return;

            try
            {
                float whole = inv.itemInfo.durability;
                if (whole <= 0f) return;

                float missing = Mathf.Clamp(whole - inv.durability, 0f, whole);
                if (missing <= 0f) return;

                float much = Repair.Worth(inv.itemInfo) * Repair.Share.Value
                    * (missing / whole);

                // Чем крепче стена, тем разорительнее её держать. Латник платит за свою
                // неуязвимость не только весом: полный доспех съедает кошель.
                UIArmorInfo coat = inv.itemInfo as UIArmorInfo;

                // Пояс сюда не идёт: держать ему нечего, и дорожать за стену, которой у
                // него нет, ему не за что.
                if (coat != null && Repair.Hard.Value > 0f
                    && coat.EquipType != EquipSlotType.belt)
                {
                    int spot = Anatomy.Chest;
                    switch (coat.EquipType)
                    {
                        case EquipSlotType.head: spot = Anatomy.Head; break;
                        case EquipSlotType.pants: spot = Anatomy.Pants; break;
                    }

                    float wall = Breach.Must(coat, 1f, spot, 0);

                    if (wall > 0f)
                    {
                        much *= Mathf.Pow(wall / Mathf.Max(1f, Repair.Floor.Value),
                            Repair.Hard.Value);
                    }
                }

                int asked = Mathf.CeilToInt(much);

                // Родную цену оставляем полом: дешевле, чем в игре, чинить не станет никто.
                if (asked > __result) __result = asked;
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог пересчитать цену ремонта: " + e);
            }
        }
    }
}
