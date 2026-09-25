using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// The tally the set keeps of what has been killed while wearing it.
    ///
    /// Two things are paid out of the same tally. The full set turns kills into strength at a
    /// flat rate. The boots turn them into damage at a rate that gets worse the further it goes:
    /// cheap up to the first tier, five times dearer up to the second, ten times dearer after
    /// that. Nothing is capped — the price simply keeps rising, which is what makes an open
    /// ended bonus survivable.
    ///
    /// The count has to outlive the session and cannot go into the save, which stores items as
    /// plain ids and has no room for a field of ours, so it lives in a file beside the plugin.
    /// That makes it one tally for the character being played, not one per save slot.
    /// </summary>
    internal static class SetKills
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> CountParts;

        internal static ConfigEntry<int> StrengthParts;
        internal static ConfigEntry<int> KillsPerStep;
        internal static ConfigEntry<float> StrengthPerStep;

        internal static ConfigEntry<bool> DamageFromBoots;
        internal static ConfigEntry<bool> DamageForDemons;
        internal static ConfigEntry<float> DamagePerStep;
        internal static ConfigEntry<int> DamageStep1;
        internal static ConfigEntry<int> DamageTier1;
        internal static ConfigEntry<int> DamageStep2;
        internal static ConfigEntry<int> DamageTier2;
        internal static ConfigEntry<int> DamageStep3;

        private static int count = -1;      // -1 значит «ещё не читали с диска»
        private static string path;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("SetKills", "Enabled", true,
                "Count kills made while the set is worn and turn them into strength and damage.");

            CountParts = config.Bind("SetKills", "CountParts", 1,
                new ConfigDescription(
                    "Pieces that must be worn for a kill to be counted at all.",
                    new AcceptableValueRange<int>(1, 9)));

            StrengthParts = config.Bind("SetKills", "StrengthParts", 9,
                new ConfigDescription(
                    "Pieces that must be worn for the strength the tally has earned to apply.",
                    new AcceptableValueRange<int>(1, 9)));

            KillsPerStep = config.Bind("SetKills", "KillsPerStep", 100,
                new ConfigDescription(
                    "Kills needed for one step of strength.",
                    new AcceptableValueRange<int>(1, 10000)));

            StrengthPerStep = config.Bind("SetKills", "StrengthPerStep", 0.01f,
                new ConfigDescription(
                    "Strength added per step, as a share. 0.01 is one percent, and there is no cap.",
                    new AcceptableValueRange<float>(0f, 1f)));

            DamageFromBoots = config.Bind("SetKills", "DamageFromBoots", false,
                "Let the boots carry the kill-fed damage instead of the demon's own blood. Off, "
                + "since it has moved to the racial trait.");

            DamageForDemons = config.Bind("SetKills", "DamageForDemons", true,
                "Give the kill-fed damage to every demon by blood, rather than to whoever happens "
                + "to be wearing the boots. It then belongs to the race and cannot be taken off "
                + "with a piece of armour.");

            DamagePerStep = config.Bind("SetKills", "DamagePerStep", 0.01f,
                new ConfigDescription(
                    "Damage added per step, as a share. 0.01 is one percent.",
                    new AcceptableValueRange<float>(0f, 1f)));

            DamageStep1 = config.Bind("SetKills", "DamageStep1", 100,
                new ConfigDescription(
                    "Kills per step until the first tier is reached.",
                    new AcceptableValueRange<int>(1, 100000)));

            DamageTier1 = config.Bind("SetKills", "DamageTier1", 25,
                new ConfigDescription(
                    "Steps after which the price rises to the second rate.",
                    new AcceptableValueRange<int>(1, 1000)));

            DamageStep2 = config.Bind("SetKills", "DamageStep2", 500,
                new ConfigDescription(
                    "Kills per step between the first tier and the second.",
                    new AcceptableValueRange<int>(1, 100000)));

            DamageTier2 = config.Bind("SetKills", "DamageTier2", 50,
                new ConfigDescription(
                    "Steps after which the price rises to the third rate and stays there.",
                    new AcceptableValueRange<int>(1, 1000)));

            DamageStep3 = config.Bind("SetKills", "DamageStep3", 1000,
                new ConfigDescription(
                    "Kills per step beyond the second tier. This rate never changes again.",
                    new AcceptableValueRange<int>(1, 100000)));
        }

        private static string File
        {
            get
            {
                if (path == null)
                {
                    string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    path = Path.Combine(dir, "kills.txt");
                }
                return path;
            }
        }

        internal static int Count
        {
            get
            {
                if (count < 0)
                {
                    count = 0;
                    try
                    {
                        if (System.IO.File.Exists(File))
                        {
                            int read;
                            if (int.TryParse(System.IO.File.ReadAllText(File).Trim(),
                                    NumberStyles.Integer, CultureInfo.InvariantCulture, out read)
                                && read > 0)
                            {
                                count = read;
                            }
                        }
                        ItemForgePlugin.Log.LogInfo($"Счёт убийств комплекта: {count}.");
                    }
                    catch (Exception e)
                    {
                        ItemForgePlugin.Log.LogWarning("Не смог прочитать счёт убийств: " + e.Message);
                    }
                }
                return count;
            }
        }

        internal static void Add(int killed)
        {
            count = Count + killed;
            try { System.IO.File.WriteAllText(File, count.ToString(CultureInfo.InvariantCulture)); }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог сохранить счёт убийств: " + e.Message);
            }
        }

        /// <summary>Strength earned so far, as a share. Flat rate, no cap.</summary>
        internal static float StrengthBonus()
        {
            if (!Enabled.Value || KillsPerStep.Value < 1) return 0f;
            return (Count / KillsPerStep.Value) * StrengthPerStep.Value;
        }

        /// <summary>
        /// Damage earned so far, as a share.
        ///
        /// Counted in whole steps rather than smoothly, so the bonus moves in visible notches and
        /// the tier boundaries land exactly where the rates say they do. Each tier is priced by
        /// how many kills one step costs inside it, and the tally is spent tier by tier.
        /// </summary>
        internal static float DamageBonus()
        {
            if (!Enabled.Value) return 0f;
            if (!DamageFromBoots.Value && !DamageForDemons.Value) return 0f;

            int step1 = Mathf.Max(1, DamageStep1.Value);
            int step2 = Mathf.Max(1, DamageStep2.Value);
            int step3 = Mathf.Max(1, DamageStep3.Value);
            int tier1 = Mathf.Max(0, DamageTier1.Value);
            int tier2 = Mathf.Max(tier1, DamageTier2.Value);

            int kills = Count;
            int firstCost = tier1 * step1;
            if (kills <= firstCost) return (kills / step1) * DamagePerStep.Value;

            int secondCost = (tier2 - tier1) * step2;
            if (kills <= firstCost + secondCost)
            {
                return (tier1 + (kills - firstCost) / step2) * DamagePerStep.Value;
            }

            return (tier2 + (kills - firstCost - secondCost) / step3) * DamagePerStep.Value;
        }

        internal static int WornCount(UnitAttribute unit)
        {
            if (!Enabled.Value) return 0;
            return Forge.WornCount(unit);
        }

        /// <summary>True when a forged piece for that slot is on.</summary>
        internal static bool WearingSlot(UnitAttribute unit, EquipSlotType slot)
        {
            if (!Enabled.Value) return false;
            return Forge.WearingSlot(unit, slot);
        }
    }

    // Считаем в Die, а не в TakeDamage: сюда игра приходит один раз на смерть и приносит
    // убийцу, так что добивание ядом или союзником не запишется на носителя комплекта.
    [HarmonyPatch(typeof(UnitAttribute), "Die", new[] { typeof(UnitAttribute), typeof(bool) })]
    internal static class Die_Patch
    {
        private static void Prefix(UnitAttribute __instance, UnitAttribute killer)
        {
            try
            {
                if (killer == null || __instance == null || killer == __instance) return;

                // Die зовут и по уже мёртвому — второй раз считать нечего.
                if (__instance.Data != null && __instance.Data.isdead) return;
                // Считаем убийства демона независимо от надетого, раз прибавка расовая.
                bool counts = SetKills.DamageForDemons.Value
                    ? (killer.Data != null && killer.Data.race == UnitRace.demon)
                    : SetKills.WornCount(killer) >= SetKills.CountParts.Value;

                if (!counts) return;

                SetKills.Add(1);

                int step = SetKills.KillsPerStep.Value;
                if (step > 0 && SetKills.Count % step == 0)
                {
                    ItemForgePlugin.Log.LogInfo($"Комплект насчитал {SetKills.Count} убийств: "
                        + $"сила +{SetKills.StrengthBonus() * 100f:0.#}%, "
                        + $"урон +{SetKills.DamageBonus() * 100f:0.#}%.");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Счёт убийств сорвался: " + e);
            }
        }
    }

    // Прибавка к силе читается из счёта в момент запроса, поэтому снятый комплект её
    // мгновенно убирает, а надетый возвращает — в сохранении не остаётся ничего.
    [HarmonyPatch(typeof(HumaniodUnit), "Strength", MethodType.Getter)]
    internal static class StrengthFromKills_Patch
    {
        [ThreadStatic] private static bool inside;

        private static void Postfix(HumaniodUnit __instance, ref int __result)
        {
            if (inside) return;
            try
            {
                inside = true;

                float bonus = SetKills.StrengthBonus();
                if (bonus <= 0f) return;
                if (SetKills.WornCount(__instance) < SetKills.StrengthParts.Value) return;

                __result = Mathf.RoundToInt(__result * (1f + bonus));
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Прибавка силы за убийства сорвалась: " + e);
            }
            finally { inside = false; }
        }
    }

    // Урон домножается там же, где это делает сама игра со своим мастерством оружия:
    // после базового пересчёта, который каждый раз собирает урон заново из BSdamage.
    // Поэтому множитель не накапливается от вызова к вызову.
    [HarmonyPatch(typeof(HumaniodUnit), "WriteUnitAttribute")]
    internal static class DamageFromKills_Patch
    {
        private static void Postfix(HumaniodUnit __instance)
        {
            try
            {
                float bonus = SetKills.DamageBonus();
                if (bonus <= 0f) return;

                // По крови, а не по сапогам: прибавка принадлежит демону и не снимается
                // вместе с бронёй. Прежнее условие оставлено настройкой на случай возврата.
                bool deserves = SetKills.DamageForDemons.Value
                    ? (__instance.Data != null && __instance.Data.race == UnitRace.demon)
                    : SetKills.WearingSlot(__instance, EquipSlotType.pants);

                if (!deserves) return;
                if (__instance.weapons == null) return;

                float factor = 1f + bonus;
                foreach (Weapon weapon in __instance.weapons)
                {
                    if (weapon == null || weapon.damage == null) continue;

                    // Урон оружия перечисляется парами «тип — значение», как это делает
                    // и сама игра, домножая свой множитель мастерства.
                    foreach (System.Collections.Generic.KeyValuePair<DamageType, Damage> part in weapon.damage)
                    {
                        if (part.Value == null) continue;
                        part.Value.minDamage *= factor;
                        part.Value.maxDamage *= factor;
                        part.Value.currentDamage *= factor;
                    }
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Прибавка урона за убийства сорвалась: " + e);
            }
        }
    }
}
