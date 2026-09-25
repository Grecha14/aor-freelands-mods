using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Lets a weapon answer to the hand it was made for.
    ///
    /// У каждой вещи в игре записано, чем её держат: «сила/ловкость 0,7/0,3» у топора,
    /// 0,3/0,7 у рапиры. Поля эти лежат в `strFactor` и `agiFactor` и не делают ничего —
    /// игра множит урон одной силой и только в ближнем бою: «MeleeDamageMD += Strength ×
    /// 0.01». Дальний бой не растёт вовсе ни от чего, и лучник со ста ловкости бьёт ровно
    /// так же, как с десятью.
    ///
    /// Оттого ловкачу и незачем было качать ловкость: урон ей не двигался. Здесь это
    /// исправлено — вещь растёт от той руки, для которой сделана, в той доле, которая в ней
    /// и записана.
    /// </summary>
    internal static class Blend
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> PerPoint;
        internal static ConfigEntry<float> Narrow;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Blend", "Enabled", true,
                "Let each weapon grow by the attribute it was made for, in the share written "
                + "into it. Without this the game grows melee damage by strength alone and "
                + "ranged damage by nothing at all, so a bow of a hundred agility hits exactly "
                + "as hard as one of ten.");

            PerPoint = config.Bind("Blend", "PerPoint", 0.01f,
                new ConfigDescription(
                    "What one point of the weapon's own attribute adds, as a share. A "
                    + "hundredth — the same the game gives strength, so nothing gets louder, "
                    + "it only starts counting the right attribute.",
                    new AcceptableValueRange<float>(0f, 0.1f)));

            Narrow = config.Bind("Blend", "Narrow", 0.01f,
                new ConfigDescription(
                    "How much one rank of mastery draws the two ends of a weapon's damage "
                    + "together, as a share of the spread. A hundredth a rank: at nothing the "
                    + "spread is what the thing was made with, at a hundred it is gone "
                    + "altogether and every blow lands the same. The middle never moves. "
                    + "This is what skill actually is. A novice and a master hit equally hard "
                    + "on their best blow; the difference is the worst one. A bow that swings "
                    + "from sixteen to twenty-seven in raw hands runs from nineteen to "
                    + "twenty-four at half mastery and strikes a flat twenty-two in a hand "
                    + "that has nothing left to learn.",
                    new AcceptableValueRange<float>(0f, 0.02f)));

            Telling = config.Bind("Blend", "Telling", false,
                "Write out the first few weapons and what their mix came to.");
        }

        private static int told;

        /// <summary>The share this weapon's own mix of attributes is worth to its damage.</summary>
        private static float Mix(HumaniodUnit man, UIWeaponInfo blade, bool far)
        {
            if (man == null) return 0f;

            // Дальний бой растёт ловкостью и только ею, в полную долю. Не потому, что сила
            // луку не нужна — она нужна, чтобы его натянуть, и спрашивается при надевании, —
            // а потому, что попасть и попасть верно решает рука. Доли, записанные в самом
            // луке, тут не при чём: по ним длинный лук рос бы силой на две трети, и лучник
            // качал бы силу.
            if (far) return man.Agility * PerPoint.Value;

            float str = blade != null ? blade.strFactor : 1f;
            float agi = blade != null ? blade.agiFactor : 0f;

            // Если в вещи не записано ничего, считаем её силовой: так делает и игра.
            if (str <= 0f && agi <= 0f) { str = 1f; agi = 0f; }

            return (man.Strength * str + man.Agility * agi) * PerPoint.Value;
        }

        /// <summary>The item behind a weapon in hand, for the mix written into it.</summary>
        private static UIWeaponInfo Item(HumaniodUnit man, Weapon arm)
        {
            try
            {
                if (man == null || man.equipmentmanger == null || arm == null) return null;

                EquipInfo[] kit = man.equipmentmanger.equipInfos;
                if (kit == null) return null;

                foreach (EquipInfo one in kit)
                {
                    if (one == null || one.inventory == null || !one.IsEquiped()) continue;

                    UIWeaponInfo blade = one.inventory.itemInfo as UIWeaponInfo;
                    if (blade == null) continue;

                    if (blade.WeaponType == arm.weaponType) return blade;
                }
            }
            catch
            {
            }

            return null;
        }

        internal static void Even(HumaniodUnit man)
        {
            if (Enabled == null || !Enabled.Value || man == null || man.weapons == null) return;

            try
            {
                foreach (Weapon arm in man.weapons)
                {
                    if (arm == null || arm.damage == null) continue;

                    UIWeaponInfo blade = Item(man, arm);

                    bool far = arm.weaponType == WeaponType.range
                        || arm.weaponType == WeaponType.quiver;

                    float mine = 1f + Mix(man, blade, far);

                    // Игра уже домножила ближний бой на силу целиком. Вычитаем её долю и
                    // ставим свою: у топора выйдет почти то же, у рапиры — вдвое больше от
                    // ловкости и вдвое меньше от силы. Дальний бой игра не множит ни на что.
                    float theirs = far ? 1f : Mathf.Max(0.01f, man.MeleeDamageMD);

                    float much = mine / theirs;

                    // Сколько эта рука знает об этом оружии: разброс сжимается умением, а
                    // не силой. Ветку берём ту же, которой бьют.
                    int skill = 0;

                    try
                    {
                        int branch = (int)arm.weaponType;

                        if (man.Data != null && man.Data.weaponMastery != null
                            && branch >= 0 && branch < man.Data.weaponMastery.Length)
                        {
                            skill = Mathf.Max(0, man.Data.weaponMastery[branch]);
                        }
                    }
                    catch
                    {
                    }

                    // Очко умения снимает сотую долю разброса. На сотне его не остаётся вовсе:
                    // рука, которой нечему учиться, кладёт удар в удар.
                    float tight = Mathf.Clamp01(1f - Narrow.Value * skill);

                    // Ни смесь не сдвинулась, ни разброс не сжался — трогать нечего.
                    if (Mathf.Abs(much - 1f) < 0.001f && Mathf.Abs(tight - 1f) < 0.001f) continue;

                    foreach (KeyValuePair<DamageType, Damage> one in arm.damage)
                    {
                        if (one.Value == null) continue;

                        float low = one.Value.minDamage * much;
                        float high = one.Value.maxDamage * much;

                        // Середина остаётся на месте, сходятся концы: у мастера худший удар
                        // подтягивается к лучшему, а не лучший становится ещё лучше.
                        float mid = (low + high) * 0.5f;
                        float half = (high - low) * 0.5f * tight;

                        // До десятой доли: числа идут в карточку, и «17,3849» там ни к чему.
                        one.Value.minDamage = Mathf.Max(1f, Mathf.Round((mid - half) * 10f) / 10f);
                        one.Value.maxDamage = Mathf.Round((mid + half) * 10f) / 10f;
                    }

                    if (Telling.Value && told < 10)
                    {
                        told++;
                        ItemForgePlugin.Log.LogInfo($"Смесь «{arm.weaponType}» у "
                            + $"«{man.Data.unitname}»: "
                            + (far
                                ? $"ловкость {man.Agility} в полную долю"
                                : $"сила {man.Strength} × "
                                    + $"{(blade != null ? blade.strFactor : 1f):0.##} + ловкость "
                                    + $"{man.Agility} × {(blade != null ? blade.agiFactor : 0f):0.##}")
                            + $" → ×{mine:0.###}, вместо игрового ×{theirs:0.###}.");
                    }
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог свести смесь оружия: " + e.Message);
            }
        }
    }

    // После того как игра посчитала урон оружия, но до того, как его кто-то прочитал.
    [HarmonyPatch(typeof(HumaniodUnit), "WriteUnitAttribute")]
    internal static class Blend_Write_Patch
    {
        private static void Postfix(HumaniodUnit __instance)
        {
            if (Blend.Enabled == null || !Blend.Enabled.Value) return;

            try
            {
                Blend.Even(__instance);
            }
            catch
            {
            }
        }
    }
}
