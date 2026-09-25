using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Lets the weapon decide what a critical blow is worth.
    ///
    /// У игры множитель крита один на бойца — полтора, плюс сотая за очко ума, — и оттого
    /// крит молотом и крит двуручным мечом стоят одинаково. Между тем это разные вещи.
    /// Крит — найденная щель; что в неё войдёт, решает то, чем бьют. Клинок в подмышку
    /// уходит целиком, и удвоение тут скромная оценка. Молот в ту же щель не входит вовсе:
    /// он и так бьёт сквозь железо своей долей, и добавлять ему сверху нечего.
    ///
    /// Смешанный урон — тоже сотня: вещь, бьющая и лезвием, и обухом, не делает ни того, ни
    /// другого чисто, и щель ей достаётся хуже.
    /// </summary>
    internal static class Edge
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Table;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Edge", "Enabled", true,
                "Let the weapon decide what a critical blow multiplies by. The game gives every "
                + "fighter one figure for all of them, so a hammer's critical hit and a "
                + "greatsword's are worth the same.");

            Table = config.Bind("Edge", "Table", "blunt=1.0,mixed=1.0,onehand=1.5,twohand=2.0",
                "What a critical blow multiplies damage by, by what is held: «blunt» for "
                + "anything crushing, «mixed» for a weapon that deals two kinds of damage at "
                + "once, «twohand» for a blade held in both hands or on a shaft, «onehand» for "
                + "everything else. Whatever a man's own bonuses add above the game's own one "
                + "and a half is kept on top of these.");

            Telling = config.Bind("Edge", "Telling", false,
                "Write out the first few blows and what each was counted as.");
        }

        private static readonly Dictionary<string, float> table =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private static string read;

        private static float Worth(string name, float fallback)
        {
            string written = Table.Value ?? "";

            if (written != read)
            {
                read = written;
                table.Clear();

                foreach (string one in written.Split(','))
                {
                    int split = one.IndexOf('=');
                    if (split <= 0) continue;

                    float much;
                    if (!float.TryParse(one.Substring(split + 1).Trim(),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out much)) continue;

                    table[one.Substring(0, split).Trim()] = much;
                }
            }

            float got;
            return table.TryGetValue(name, out got) ? got : fallback;
        }

        /// <summary>Which of the four this weapon answers to.</summary>
        internal static string Kind(Weapon arm)
        {
            if (arm == null || arm.damage == null) return "onehand";

            int kinds = 0;
            float most = 0f;
            DamageType lead = DamageType.sharp;

            foreach (KeyValuePair<DamageType, Damage> one in arm.damage)
            {
                if (one.Value == null || one.Value.maxDamage <= 0f) continue;

                kinds++;

                if (one.Value.maxDamage > most)
                {
                    most = one.Value.maxDamage;
                    lead = one.Key;
                }
            }

            // Две руки, два лезвия: вещь, которая бьёт и так, и этак, ни одного удара не
            // кладёт чисто, и щель ей достаётся хуже.
            if (kinds > 1) return "mixed";

            if (lead == DamageType.blunt) return "blunt";

            return arm.weaponType == WeaponType.twohand || arm.weaponType == WeaponType.polearms
                ? "twohand"
                : "onehand";
        }

        // Что у бойца стояло до нашей подмены: на время удара множитель свой, после — его же.
        private static float kept;
        private static bool swapped;
        private static int told;

        internal static void Take(UnitAttribute who, int index)
        {
            swapped = false;

            if (!Enabled.Value || who == null || who.weapons == null) return;

            try
            {
                Weapon arm = null;

                foreach (Weapon one in who.weapons)
                {
                    if (one != null && one.index == index) { arm = one; break; }
                }

                if (arm == null) return;

                string kind = Kind(arm);

                // Своё у бойца сверх игровых полутора — ум, зелья, черты — остаётся при нём.
                float mine = Worth(kind, 1.5f) + (who.critMultiple - 1.5f);

                kept = who.critMultiple;
                who.critMultiple = Mathf.Max(0.1f, mine);
                swapped = true;

                if (Telling.Value && told < 12)
                {
                    told++;
                    ItemForgePlugin.Log.LogInfo($"Крит: «{arm.weaponType}» это {kind}, "
                        + $"множитель {kept:0.00} → {who.critMultiple:0.00}.");
                }
            }
            catch
            {
            }
        }

        internal static void Give(UnitAttribute who)
        {
            if (!swapped || who == null) return;

            who.critMultiple = kept;
            swapped = false;
        }
    }

    // Множитель крита берётся внутри удара, и внутри же домножается урон. Подменяем на время
    // одного замаха и тут же возвращаем: поле общее, и оставить его чужим нельзя.
    [HarmonyPatch(typeof(UnitAttribute), "AttackStart", new Type[] { typeof(int) })]
    internal static class Edge_Attack_Patch
    {
        private static void Prefix(UnitAttribute __instance, int weaponIndex)
        {
            if (Edge.Enabled == null || !Edge.Enabled.Value) return;

            try
            {
                Edge.Take(__instance, weaponIndex);
            }
            catch
            {
            }
        }

        private static void Postfix(UnitAttribute __instance)
        {
            if (Edge.Enabled == null || !Edge.Enabled.Value) return;

            try
            {
                Edge.Give(__instance);
            }
            catch
            {
            }
        }
    }
}
