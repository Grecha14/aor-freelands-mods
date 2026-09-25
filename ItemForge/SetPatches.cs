using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// What the King of Hell set does beyond its numbers.
    ///
    /// Everything here is decided at the moment it is asked, from what the character is
    /// wearing. Nothing is written into the save: take the set off and every effect below
    /// simply stops applying, which is why mastery requirements are waived rather than the
    /// stored mastery levels being raised.
    /// </summary>
    internal static class SetRules
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> PartsRequired;
        internal static ConfigEntry<string> Branches;
        internal static ConfigEntry<bool> WaiveMastery;
        internal static ConfigEntry<bool> ShareMasteryExp;
        internal static ConfigEntry<float> ShareRate;
        internal static ConfigEntry<int> CounterattackBonus;

        private static WeaponType[] branchCache;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("SetRules", "Enabled", true,
                "Apply the King of Hell rules below while the set is worn.");

            PartsRequired = config.Bind("SetRules", "PartsRequired", 9,
                new ConfigDescription(
                    "How many pieces must be worn for the rules to take effect.",
                    new AcceptableValueRange<int>(1, 9)));

            Branches = config.Bind("SetRules", "Branches", "unarmed,onehand,twohand,shield,daul,polearms",
                "Weapon branches the set counts as, separated by commas. Names are the game's own: "
                + "unarmed, onehand, twohand, shield, range, daul, polearms.");

            WaiveMastery = config.Bind("SetRules", "WaiveMastery", false,
                "Waive the mastery level a passive skill demands, so passives from every listed branch "
                + "become available without earning them. Off by default: the branches are meant to be "
                + "raised by carrying the matching weapon. The stored mastery levels are never touched.");

            ShareMasteryExp = config.Bind("SetRules", "ShareMasteryExp", true,
                "Give weapon mastery experience to every listed branch, so all of them can be raised "
                + "while the set is worn.");

            ShareRate = config.Bind("SetRules", "ShareRate", 1.0f,
                new ConfigDescription(
                    "Share of the experience each extra branch receives. 1.0 is the ordinary rate, "
                    + "the same a branch would earn as the main one. The game itself gives a second "
                    + "branch 0.7, so set that to keep its discount.",
                    new AcceptableValueRange<float>(0.1f, 1f)));

            CounterattackBonus = config.Bind("SetRules", "CounterattackBonus", 25,
                new ConfigDescription(
                    "Added to the chance of a shield counterattack, in percent, on top of whatever "
                    + "the character already has. Works even without the talent.",
                    new AcceptableValueRange<int>(0, 100)));
        }

        internal static WeaponType[] GetBranches()
        {
            if (branchCache != null) return branchCache;

            List<WeaponType> list = new List<WeaponType>();
            foreach (string raw in (Branches.Value ?? "").Split(','))
            {
                string name = raw.Trim();
                if (name.Length == 0) continue;

                try { list.Add((WeaponType)Enum.Parse(typeof(WeaponType), name, true)); }
                catch { ItemForgePlugin.Log.LogWarning($"Не знаю ветку '{name}', пропускаю."); }
            }

            branchCache = list.ToArray();
            return branchCache;
        }

        /// <summary>True when the unit wears enough pieces of the forged set.</summary>
        internal static bool Wearing(UnitAttribute unit)
        {
            if (!Enabled.Value || unit == null) return false;

            return Forge.WornCount(unit) >= PartsRequired.Value;
        }

        internal static bool IsListedBranch(WeaponType type)
        {
            foreach (WeaponType t in GetBranches())
            {
                if (t == type) return true;
            }
            return false;
        }
    }

    // Пассивка чужой ветки молча не применяется, потому что игра сверяет требуемый тип
    // оружия только с двумя — основным и запасным. В полном комплекте сверяем со списком.
    [HarmonyPatch(typeof(UITalentInfo), "WeaponTypeFit")]
    internal static class WeaponTypeFit_Patch
    {
        private static void Postfix(UITalentInfo __instance, UnitAttribute unit, ref bool __result)
        {
            try
            {
                if (__result) return;
                if (__instance.RequireWeapon == null || __instance.RequireWeapon.Length == 0) return;
                if (!SetRules.Wearing(unit)) return;

                foreach (WeaponType required in __instance.RequireWeapon)
                {
                    if (SetRules.IsListedBranch(required)) { __result = true; return; }
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Проверка типа оружия сорвалась: " + e);
            }
        }
    }

    // Пассивки заперты уровнем мастерства своей ветки. В полном комплекте требование
    // снимается — но сохранённые уровни при этом не трогаются.
    [HarmonyPatch(typeof(UITalentInfo), "CheckRequirement")]
    internal static class CheckRequirement_Patch
    {
        private static void Postfix(UITalentInfo __instance, CharacterSaveData csd, ref bool __result)
        {
            try
            {
                if (__result) return;
                if (!SetRules.WaiveMastery.Value) return;
                if (__instance.type != TalentType.PassiveSkill) return;
                if (csd == null || csd.Unit == null) return;
                if (!SetRules.Wearing(csd.Unit)) return;

                // Ветки оружия занимают значения от 200; всё остальное не наше дело.
                int branch = (int)__instance.talentClass - 200;
                if (branch < 0 || branch > 6) return;
                if (!SetRules.IsListedBranch((WeaponType)branch)) return;

                // Требование по характеристикам оставляем: снимаем только запрет по мастерству.
                NPCSaveData npc = csd as NPCSaveData;
                if (npc != null && !(npc.humanAttribute >= __instance.attributeRquire)) return;

                __result = true;
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Проверка требований сорвалась: " + e);
            }
        }
    }

    // Опыт мастерства штатно достаётся двум веткам. В полном комплекте — всем перечисленным,
    // чтобы полоски действительно заполнялись, а не стояли на нуле.
    [HarmonyPatch(typeof(HumaniodUnit), "GainWeaponMasteryExp", new[] { typeof(int) })]
    internal static class GainWeaponMasteryExp_Patch
    {
        private static bool inside;

        private static void Postfix(HumaniodUnit __instance, int exp)
        {
            try
            {
                if (inside) return;
                if (!SetRules.ShareMasteryExp.Value) return;
                if (!SetRules.Wearing(__instance)) return;

                inside = true;
                try
                {
                    foreach (WeaponType branch in SetRules.GetBranches())
                    {
                        if (branch == __instance.weapontype || branch == __instance.weapontype2) continue;
                        __instance.GainWeaponMasteryExp(branch, Mathf.RoundToInt(exp * SetRules.ShareRate.Value));
                    }
                }
                finally { inside = false; }
            }
            catch (Exception e)
            {
                inside = false;
                ItemForgePlugin.Log.LogError("Начисление мастерства сорвалось: " + e);
            }
        }
    }

    // Модель оружия появляется не в SetWeapon — тот переносит одни только числа, — а здесь,
    // когда игра подвешивает предмет в руку и собирает его рендереры. Прежний патч писал
    // масштаб в узел руки до того, как модель туда попадала, и не менял ровно ничего.
    [HarmonyPatch(typeof(EquipmentManager), "EquipWeaponModel")]
    internal static class EquipWeaponModel_Patch
    {
        // Исходный масштаб модели, по её же идентификатору. Хранить сам Transform нельзя:
        // уничтоженный объект в Unity сравнивается с null особым образом и ломает словарь.
        private static readonly Dictionary<int, Vector3> Original = new Dictionary<int, Vector3>();

        // Что уже сообщено в лог по каждой модели, чтобы не повторяться каждые полсекунды.
        private static readonly Dictionary<int, bool> Reported = new Dictionary<int, bool>();

        internal static void Refresh(EquipmentManager gear)
        {
            if (gear == null) return;

            Dress(gear.weapon_First, gear, 0);
            Dress(gear.weapon_Second, gear, 1);

            // Пустая рука — отдельный случай, и раньше он был пропущен. Свечение снималось
            // только когда в руку брали чужое оружие; если оружие убирали совсем, обходить
            // становилось нечего, и копия оставалась висеть в воздухе сама по себе.
            Sweep(gear);
        }

        /// <summary>Убирает свечение, оставшееся от оружия, которого больше нет в руках.</summary>
        private static void Sweep(EquipmentManager gear)
        {
            try
            {
                if (gear.unit == null) return;

                // Опознаём теми же двумя путями, что и при навешивании: по самой модели и по
                // записи слота. Раньше уборка знала только первый, а он в этот момент ещё пуст —
                // и она стирала свечение, повешенное строкой выше.
                bool armed = Ours(gear.weapon_First, gear, 0) || Ours(gear.weapon_Second, gear, 1);
                if (armed) return;

                foreach (Transform node in gear.unit.GetComponentsInChildren<Transform>(true))
                {
                    if (node.name != "KingOfHellAura") continue;

                    UnityEngine.Object.Destroy(node.gameObject);
                    ItemForgePlugin.Log.LogInfo($"Снял осиротевшее свечение с «{gear.unit.name}».");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Уборка свечения сорвалась: " + e);
            }
        }

        private static bool Ours(PickableItem item, EquipmentManager gear, int slot)
        {
            if (item == null) return false;

            UIWeaponInfo info = item.itemInfo as UIWeaponInfo;
            if (info == null || !Forge.Built.Contains(info)) info = FromSlot(gear, slot) ?? info;

            return info != null && info.WeaponType != WeaponType.shield && Forge.Built.Contains(info);
        }

        private static void Postfix(EquipmentManager __instance)
        {
            try
            {
                Refresh(__instance);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог оформить модель оружия: " + e);
            }
        }

        private static void Dress(PickableItem item, EquipmentManager gear, int slot)
        {
            if (item == null) return;

            Transform node = item.transform;
            int key = node.GetInstanceID();

            Vector3 baseScale;
            if (!Original.TryGetValue(key, out baseScale))
            {
                baseScale = node.localScale;
                Original[key] = baseScale;
            }

            // Предмет берём и у самой модели, и из записи слота: в момент, когда модель
            // только подвешена, её собственная ссылка ещё не проставлена, и проверка по
            // ней одна давала «не наше» на нашем же клинке.
            UIWeaponInfo info = item.itemInfo as UIWeaponInfo;
            if (info == null || !Forge.Built.Contains(info)) info = FromSlot(gear, slot) ?? info;

            bool ours = info != null && Forge.Built.Contains(info);

            // Щит светится наравне с клинком, но не растёт: его размер задан углом блока,
            // и раздутый он перекрывал бы обзор.
            bool grows = ours && info.WeaponType != WeaponType.shield;

            node.localScale = grows ? baseScale * Forge.WeaponScale.Value : baseScale;

            // Эмиссию на оружие больше не пишем: свечение даёт позаимствованный эффект, а
            // каждое обращение к materials создаёт новые копии материалов — на опросе раз в
            // полсекунды это утекало тысячами.
            //
            // Свечение идёт только на клинок: эффект донора вытянут вдоль древка палицы, и
            // на круглом щите он торчит в сторону, а не обнимает его.
            WeaponVfx.Apply(node, grows);

            // И дым «Губительного прикосновения» — на клинок и на щит комплекта.
            Taint.OnWeapon(node, ours);

            // Пишем только при смене, иначе опрос раз в полсекунды забивает лог.
            int key2 = node.GetInstanceID();
            bool was;
            if (ours && (!Reported.TryGetValue(key2, out was) || !was))
            {
                Reported[key2] = true;
                ItemForgePlugin.Log.LogInfo($"Модель «{info.Name}»: масштаб {node.localScale.x:0.##}, "
                    + $"исходный {baseScale.x:0.##}.");
            }
            else if (!ours) Reported[key2] = false;
        }

        /// <summary>The weapon the slot record says is in that hand, whichever set is active.</summary>
        private static UIWeaponInfo FromSlot(EquipmentManager gear, int slot)
        {
            if (gear == null) return null;

            UIWeaponInfo found = Pick(gear.equipInfos, slot);
            return found != null ? found : Pick(gear.standByWeaponInfos, slot);
        }

        private static UIWeaponInfo Pick(EquipInfo[] slots, int slot)
        {
            if (slots == null || slot < 0 || slot >= slots.Length) return null;
            if (slots[slot] == null || slots[slot].inventory == null) return null;

            return slots[slot].inventory.itemInfo as UIWeaponInfo;
        }
    }
}
