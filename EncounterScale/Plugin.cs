using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace EncounterScale
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class EncounterScalePlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "aor.encounterscale";
        public const string PluginName = "Encounter Scale";
        public const string PluginVersion = "1.2.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> MinMultiplier;
        internal static ConfigEntry<float> MaxMultiplier;
        internal static ConfigEntry<int> MaxGroupSize;
        internal static ConfigEntry<bool> AffectGuards;
        internal static ConfigEntry<bool> AffectCaravans;
        internal static ConfigEntry<bool> AffectVillagers;
        internal static ConfigEntry<bool> VerboseLog;

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind("General", "Enabled", true,
                "Master switch. Turn off to leave group sizes untouched without uninstalling the mod.");

            MinMultiplier = Config.Bind("General", "MinMultiplier", 1.0f,
                new ConfigDescription(
                    "Smallest a spawned group can be multiplied by. 1 leaves it as the game made it.",
                    new AcceptableValueRange<float>(1f, 50f)));

            MaxMultiplier = Config.Bind("General", "MaxMultiplier", 2.0f,
                new ConfigDescription(
                    "Largest a spawned group can be multiplied by. Each group rolls its own figure "
                    + "between the two, so encounters vary instead of every one being the same "
                    + "size — a fixed tenfold made every fight look identical and cost the same "
                    + "framerate whatever it was.",
                    new AcceptableValueRange<float>(1f, 50f)));

            MaxGroupSize = Config.Bind("General", "MaxGroupSize", 60,
                new ConfigDescription(
                    "Hard cap on the resulting group size, applied after the multiplier. " +
                    "Guards against unplayable framerates on large groups. Lower this first if the game stutters.",
                    new AcceptableValueRange<int>(1, 500)));

            AffectGuards = Config.Bind("Targets", "AffectGuards", false,
                "Also scale town and settlement guard patrols.");

            AffectCaravans = Config.Bind("Targets", "AffectCaravans", false,
                "Also scale trade caravan escorts.");

            AffectVillagers = Config.Bind("Targets", "AffectVillagers", false,
                "Also scale villager groups.");

            VerboseLog = Config.Bind("Debug", "VerboseLog", false,
                "Write a log line for every group that gets scaled.");

            try
            {
                Difficulty.Bind(Config);
                Abilities.Bind(Config);
                Tutelage.Bind(Config);
                Professions.Bind(Config);
                new Harmony(PluginGuid).PatchAll();
                Log.LogInfo($"Encounter Scale v{PluginVersion} loaded. Multiplier x{MinMultiplier.Value}-x{MaxMultiplier.Value}, cap {MaxGroupSize.Value}. "
                    + $"Сила по уровню с {Difficulty.FromLevel.Value}: здоровье +{Difficulty.HealthPerLevel.Value * 100f:0}%/ур, урон +{Difficulty.DamagePerLevel.Value * 100f:0}%/ур; "
                    + $"боссы x{Difficulty.BossHealth.Value}/x{Difficulty.BossDamage.Value}, драконы x{Difficulty.DragonPower.Value}.");
            }
            catch (Exception e)
            {
                Log.LogError("Encounter Scale failed to patch, group sizes stay vanilla: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(TravelGroupSpawner), nameof(TravelGroupSpawner.Spawn),
        new[] { typeof(TravelGroupInfo), typeof(int), typeof(List<Transform>) })]
    internal static class TravelGroupSpawner_Spawn_Patch
    {
        // Vanilla decides the group size as:
        //     num = count > 0 ? count : (spawnNum != null ? (int)spawnNum : 0)
        // and then adds num-1 members on top of the leader, before SaveCSD hands out
        // ids and registers everyone with the save system. Overriding count here means
        // the enlarged group still walks the whole vanilla path: level bonuses, starting
        // gear, ids, save registration.
        private static void Prefix(TravelGroupSpawner __instance, ref int count)
        {
            try
            {
                if (!EncounterScalePlugin.Enabled.Value) return;
                if (EncounterScalePlugin.MaxMultiplier.Value <= 1f) return;

                // AddOneGroupMember picks from unitInfos; with none there is nobody to clone.
                if (__instance.unitInfos == null || __instance.unitInfos.Length == 0) return;

                if (__instance.isCarriageGroup) return;
                if (__instance.isGuardGroup && !EncounterScalePlugin.AffectGuards.Value) return;
                if (__instance.isCaravanGroup && !EncounterScalePlugin.AffectCaravans.Value) return;
                if (__instance.isVillageGroup && !EncounterScalePlugin.AffectVillagers.Value) return;

                int baseSize = count > 0
                    ? count
                    : (__instance.spawnNum != null ? __instance.spawnNum.Value : 0);

                // A lone leader still counts as a group of one.
                if (baseSize < 1) baseSize = 1;

                // Каждая встреча тянет свой множитель: одинаковые толпы утомляют глаз
                // и одинаково просаживают кадры, а разброс делает стычки непохожими.
                float rolled = UnityEngine.Random.Range(
                    EncounterScalePlugin.MinMultiplier.Value,
                    Mathf.Max(EncounterScalePlugin.MinMultiplier.Value, EncounterScalePlugin.MaxMultiplier.Value));

                int scaled = Mathf.RoundToInt(baseSize * rolled);
                if (scaled > EncounterScalePlugin.MaxGroupSize.Value)
                    scaled = EncounterScalePlugin.MaxGroupSize.Value;

                if (scaled <= baseSize) return;

                count = scaled;

                if (EncounterScalePlugin.VerboseLog.Value)
                    EncounterScalePlugin.Log.LogInfo($"Scaled group '{__instance.groupName}' from {baseSize} to {scaled}.");
            }
            catch (Exception e)
            {
                EncounterScalePlugin.Log.LogError("Encounter Scale prefix failed, group left at vanilla size: " + e);
            }
        }
    }
}
