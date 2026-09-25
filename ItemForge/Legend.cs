using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// No two blades of a kind alike, and every legend exactly five times its humblest kin.
    ///
    /// Кузнецы не штампуют: два меча одного вида, из одной кузни, бьют по-разному — один
    /// чуть тяжелее и злее, другой легче и мягче. У каждого экземпляра свой замер, от минус
    /// двадцати до плюс двадцати процентов урона против образца; он вынимается один раз, при
    /// первой встрече с вещью, и остаётся на ней навсегда. Подсказка показывает урон этого
    /// экземпляра, а не образца.
    ///
    /// Легендарное оружие существует в одном экземпляре и одном виде: замера у него нет, цвет
    /// случайно не меняется, и урон у него — ровно впятеро против первой ступени своего рода.
    /// Легендарный длинный меч — пять простых длинных мечей первой ступени в одном клинке.
    /// Где первой ступени у рода нет, она выводится из самой низкой, что есть: ступень в этой
    /// игре прибавляет около восьми процентов.
    /// </summary>
    internal static class Legend
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Times;
        internal static ConfigEntry<float> Spread;
        internal static ConfigEntry<bool> Telling;

        private static bool done;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Legend", "Enabled", true,
                "Give every weapon its own draw of damage around its pattern, and set every "
                + "legendary weapon to a fixed multiple of the first tier of its kind.");

            Times = config.Bind("Legend", "Times", 5f,
                new ConfigDescription("How many times the first tier of its kind a legendary weapon deals.",
                    new AcceptableValueRange<float>(1f, 20f)));

            Spread = config.Bind("Legend", "Spread", 0.2f,
                new ConfigDescription("How far a single weapon may stray from its pattern, either way, "
                    + "as a share of its damage.",
                    new AcceptableValueRange<float>(0f, 0.9f)));

            Telling = config.Bind("Legend", "Telling", true,
                "Say in the log what each legendary weapon was set to.");
        }

        internal static bool Fixed(UIWeaponInfo blade)
        {
            return blade != null && (blade.isUnique || ((UIItemInfo)blade).Quality >= UIItemQuality.Legendary);
        }

        private static bool Arm(UIWeaponInfo blade)
        {
            return blade != null && blade.WeaponType != WeaponType.shield && blade.WeaponType != WeaponType.quiver
                && !Forge.IsOurId(((UIItemInfo)blade).ID);
        }

        /// <summary>True for a weapon whose every copy is drawn apart from its pattern.</summary>
        internal static bool Spreads(UIWeaponInfo blade)
        {
            return Enabled != null && Enabled.Value && Arm(blade) && !Fixed(blade) && Spread.Value > 0.0001f;
        }

        internal static float Average(DamageBase damage)
        {
            if (damage == null) return 0f;

            float sum = 0f;
            foreach (KeyValuePair<DamageType, Damage> one in damage)
            {
                if (one.Value == null) continue;
                sum += (one.Value.minDamage + one.Value.maxDamage) * 0.5f;
            }
            return sum;
        }

        /// <summary>This weapon own draw, made once and kept on it.</summary>
        internal static float Drawn(Inventory thing)
        {
            if (thing == null || thing.addAttrs == null) return 1f;

            foreach (AddonAttributes one in thing.addAttrs)
            {
                if (one != null && one.type == Arcane.MarkSpread && one.value > 0f) return one.value;
            }

            float spread = Mathf.Clamp(Spread.Value, 0f, 0.9f);
            float drawn = UnityEngine.Random.Range(1f - spread, 1f + spread);
            drawn = Mathf.Round(drawn * 100f) / 100f;

            thing.addAttrs.Add(new AddonAttributes(Arcane.MarkSpread, drawn));
            return drawn;
        }

        /// <summary>How much this copy deals against its pattern: 1 for legends and non-weapons.</summary>
        internal static float Of(Inventory thing)
        {
            if (thing == null) return 1f;
            UIWeaponInfo blade = thing.itemInfo as UIWeaponInfo;
            if (!Spreads(blade)) return 1f;
            return Drawn(thing);
        }

        // ------------------------------------------------------------------ легенды

        /// <summary>Sets every legendary weapon to its multiple of the first tier, once.</summary>
        internal static void Catalogue()
        {
            if (done || Enabled == null || !Enabled.Value) return;

            UIItemDatabase db;
            try { db = UIItemDatabase.Instance; }
            catch { return; }
            if (db == null || db.items == null) return;

            done = true;

            try
            {
                // Мера рода: средний урон первой ступени, а где её нет — самой низкой из
                // имеющихся, сведённой к первой.
                Dictionary<WeaponClass, float> firstSum = new Dictionary<WeaponClass, float>();
                Dictionary<WeaponClass, int> firstCount = new Dictionary<WeaponClass, int>();
                Dictionary<WeaponClass, int> lowTier = new Dictionary<WeaponClass, int>();
                Dictionary<WeaponClass, float> lowSum = new Dictionary<WeaponClass, float>();
                Dictionary<WeaponClass, int> lowCount = new Dictionary<WeaponClass, int>();

                foreach (UIItemInfo thing in db.items)
                {
                    UIWeaponInfo blade = thing as UIWeaponInfo;
                    if (!Arm(blade) || Fixed(blade)) continue;

                    // Факел числится дубиной, но он не мера дубине: у него ещё и огонь.
                    if (blade.isTorch) continue;

                    int tier = (int)thing.tier;
                    if (tier < 1) continue;

                    float avg = Average(blade.damage);
                    if (avg <= 0f) continue;

                    WeaponClass kind = blade.weaponClass;

                    if (tier == 1)
                    {
                        float s; int c;
                        firstSum.TryGetValue(kind, out s);
                        firstCount.TryGetValue(kind, out c);
                        firstSum[kind] = s + avg;
                        firstCount[kind] = c + 1;
                    }

                    int low;
                    if (!lowTier.TryGetValue(kind, out low) || tier < low)
                    {
                        lowTier[kind] = tier;
                        lowSum[kind] = avg;
                        lowCount[kind] = 1;
                    }
                    else if (tier == low)
                    {
                        lowSum[kind] += avg;
                        lowCount[kind] += 1;
                    }
                }

                int set = 0;
                List<string> told = new List<string>();

                foreach (UIItemInfo thing in db.items)
                {
                    UIWeaponInfo blade = thing as UIWeaponInfo;
                    if (!Arm(blade) || thing.Quality < UIItemQuality.Legendary) continue;

                    WeaponClass kind = blade.weaponClass;
                    float first = 0f;

                    int count;
                    if (firstCount.TryGetValue(kind, out count) && count > 0) first = firstSum[kind] / count;
                    else if (lowCount.TryGetValue(kind, out count) && count > 0)
                    {
                        first = lowSum[kind] / count / Mathf.Pow(1.08f, lowTier[kind] - 1);
                    }

                    if (first <= 0f) continue;

                    float was = Average(blade.damage);
                    if (was <= 0f) continue;

                    float target = first * Times.Value;
                    float scale = target / was;

                    string before = Written(blade.damage);

                    foreach (KeyValuePair<DamageType, Damage> one in blade.damage)
                    {
                        if (one.Value == null) continue;
                        one.Value.minDamage = Mathf.Max(1f, Mathf.Round(one.Value.minDamage * scale));
                        one.Value.maxDamage = Mathf.Max(one.Value.minDamage, Mathf.Round(one.Value.maxDamage * scale));
                        one.Value.currentDamage *= scale;
                    }

                    set++;
                    if (Telling.Value && told.Count < 120)
                    {
                        told.Add($"«{thing.LocalizedName}» ({kind}): {before} → {Written(blade.damage)} "
                            + $"(первая ступень рода {first:0.#}, ×{Times.Value:0.#})");
                    }
                }

                ItemForgePlugin.Log.LogInfo($"Легендарное оружие выставлено на ×{Times.Value:0.#} первой ступени: {set}.");
                foreach (string line in told) ItemForgePlugin.Log.LogInfo("  " + line);

                Refresh();
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог выставить легендарное оружие: " + e);
            }
        }

        private static string Written(DamageBase damage)
        {
            List<string> parts = new List<string>();
            foreach (KeyValuePair<DamageType, Damage> one in damage)
            {
                if (one.Value == null || one.Value.maxDamage <= 0f) continue;
                parts.Add($"{one.Key} {one.Value.minDamage:0}-{one.Value.maxDamage:0}");
            }
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>Those already armed before the change take it up at once.</summary>
        private static void Refresh()
        {
            try
            {
                foreach (HumaniodUnit man in UnityEngine.Object.FindObjectsOfType<HumaniodUnit>())
                {
                    if (man != null && man.equipmentmanger != null) man.equipmentmanger.UpdateUnitWeapons();
                }
            }
            catch
            {
            }
        }

        // ------------------------------------------------------------------ подсказка

        internal sealed class Shown
        {
            internal readonly List<KeyValuePair<Damage, Vector2>> kept = new List<KeyValuePair<Damage, Vector2>>();
        }

        internal static Shown Dress(UISlotBase slot)
        {
            if ((object)slot == null) return null;

            Inventory thing = null;

            UIItemSlot bag = slot as UIItemSlot;
            if ((object)bag != null) thing = bag.inventory;
            else
            {
                UIEquipSlot worn = slot as UIEquipSlot;
                if ((object)worn != null) thing = worn.inventory;
            }

            if (thing == null) return null;

            UIWeaponInfo blade = thing.itemInfo as UIWeaponInfo;
            if (!Spreads(blade) || blade.damage == null) return null;

            float drawn = Drawn(thing);
            if (Mathf.Approximately(drawn, 1f)) return null;

            Shown shown = new Shown();
            foreach (KeyValuePair<DamageType, Damage> one in blade.damage)
            {
                Damage d = one.Value;
                if (d == null) continue;

                shown.kept.Add(new KeyValuePair<Damage, Vector2>(d, new Vector2(d.minDamage, d.maxDamage)));
                d.minDamage = Mathf.Round(d.minDamage * drawn);
                d.maxDamage = Mathf.Max(d.minDamage, Mathf.Round(d.maxDamage * drawn));
            }

            return shown;
        }

        internal static void Undress(Shown shown)
        {
            if (shown == null) return;
            foreach (KeyValuePair<Damage, Vector2> one in shown.kept)
            {
                one.Key.minDamage = one.Value.x;
                one.Key.maxDamage = one.Value.y;
            }
            shown.kept.Clear();
        }
    }

    // Урон экземпляра: образец, умноженный на его замер.
    [HarmonyPatch(typeof(Weapon), "SetWeapon")]
    internal static class SetWeapon_Legend_Patch
    {
        private static void Postfix(Weapon __instance, Inventory weapon)
        {
            if (Legend.Enabled == null || !Legend.Enabled.Value || __instance == null || weapon == null) return;

            try
            {
                float drawn = Legend.Of(weapon);
                if (Mathf.Approximately(drawn, 1f)) return;

                if (__instance.damage != null) __instance.damage *= drawn;
                if (__instance.BSdamage != null) __instance.BSdamage *= drawn;
            }
            catch
            {
            }
        }
    }

    // Подсказка показывает урон этого экземпляра, а не образца.
    [HarmonyPatch(typeof(UIItemTip), "SetValue")]
    internal static class SetValue_Legend_Patch
    {
        private static void Prefix(UISlotBase slot, out Legend.Shown __state)
        {
            __state = null;
            try { __state = Legend.Dress(slot); } catch { __state = null; }
        }

        private static void Finalizer(Legend.Shown __state)
        {
            try { Legend.Undress(__state); } catch { }
        }
    }
}
