using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;

namespace DemonLook
{
    /// <summary>
    /// Locks a demon to a single starting background. The creator picks a background by
    /// index into its own list, so restricting the race is a matter of pinning that index
    /// and refusing to move it.
    /// </summary>
    internal static class StartRestriction
    {
        internal static ConfigEntry<bool> RestrictStart;
        internal static ConfigEntry<string> DemonStartSet;
        internal static ConfigEntry<bool> ListStartSets;

        private static bool listed;
        private static int cachedIndex = -2;

        internal static void Bind(ConfigFile config)
        {
            RestrictStart = config.Bind("Race", "RestrictStart", true,
                "Lock demons to a single starting background. Turn off to let a demon take any of them.");

            DemonStartSet = config.Bind("Race", "DemonStartSet", "",
                "Name of the background to lock demons to, matched against the game's own set name. "
                + "Leave empty to pick automatically: the hardest background that carries no "
                + "profession, and among equals the poorest.");

            ListStartSets = config.Bind("Race", "ListStartSets", true,
                "Write the available starting backgrounds to the log, once, with their difficulty, "
                + "level, money and professions.");
        }

        internal static bool IsDemonCharacter(CharacterCreaterController creator)
        {
            return creator != null
                && creator.theCharacter != null
                && creator.theCharacter.Data != null
                && creator.theCharacter.Data.race == UnitRace.demon;
        }

        internal static void ReportSets(CharacterCreaterController creator)
        {
            if (!ListStartSets.Value || listed) return;
            if (creator == null || creator.startSets == null) return;

            listed = true;
            DemonLookPlugin.Log.LogInfo($"--- starting backgrounds: {creator.startSets.Count} ---");

            for (int i = 0; i < creator.startSets.Count; i++)
            {
                StartSetting set = creator.startSets[i];
                if (set == null) continue;

                DemonLookPlugin.Log.LogInfo(
                    $"[{i}] {set.setName}: difficulty {set.startDifficulty}, "
                    + $"level {set.startLevel}, money {set.startMoney}, mage {set.isMage}, "
                    + $"mercenary {set.startMercenaryLevel}, gladiator {set.startGladiatorLevel}, "
                    + $"merchant {set.startMerchantLevel}");
            }
        }

        private static bool HasNoProfession(StartSetting set)
        {
            return set.startMercenaryLevel == MercenaryLevel.None
                && set.startGladiatorLevel == HeroRankTitle.None
                && set.startMerchantLevel == MerchantLevel.None;
        }

        /// <summary>Index of the background a demon is allowed, or -1 when unrestricted.</summary>
        internal static int AllowedIndex(CharacterCreaterController creator)
        {
            if (!RestrictStart.Value) return -1;
            if (creator == null || creator.startSets == null || creator.startSets.Count == 0) return -1;
            if (cachedIndex != -2) return cachedIndex;

            string wanted = DemonStartSet.Value;
            if (!string.IsNullOrEmpty(wanted))
            {
                for (int i = 0; i < creator.startSets.Count; i++)
                {
                    StartSetting named = creator.startSets[i];
                    if (named != null && named.setName == wanted)
                    {
                        cachedIndex = i;
                        return cachedIndex;
                    }
                }

                DemonLookPlugin.Log.LogWarning(
                    $"No starting background called {wanted}, falling back to picking one automatically.");
            }

            // The pauper start, expressed as rules rather than as a name: no profession and
            // therefore none of its bonuses, the hardest difficulty on offer, and among
            // equals the one that starts with the least money and the lowest level.
            int best = -1;
            StartSetting bestSet = null;

            for (int i = 0; i < creator.startSets.Count; i++)
            {
                StartSetting set = creator.startSets[i];
                if (set == null || !HasNoProfession(set)) continue;

                if (bestSet == null
                    || set.startDifficulty > bestSet.startDifficulty
                    || (set.startDifficulty == bestSet.startDifficulty && set.startMoney < bestSet.startMoney)
                    || (set.startDifficulty == bestSet.startDifficulty && set.startMoney == bestSet.startMoney
                        && set.startLevel < bestSet.startLevel))
                {
                    best = i;
                    bestSet = set;
                }
            }

            if (bestSet == null)
            {
                DemonLookPlugin.Log.LogWarning("No profession-free starting background found, leaving the choice open.");
                cachedIndex = -1;
                return cachedIndex;
            }

            DemonLookPlugin.Log.LogInfo(
                $"Demon start chosen automatically: [{best}] {bestSet.setName}, "
                + $"difficulty {bestSet.startDifficulty}, level {bestSet.startLevel}, money {bestSet.startMoney}.");

            cachedIndex = best;
            return cachedIndex;
        }

        /// <summary>
        /// Returns true when the caller should be stopped, having already pinned the index.
        /// </summary>
        internal static bool Pin(CharacterCreaterController creator)
        {
            try
            {
                if (!IsDemonCharacter(creator)) return false;

                int allowed = AllowedIndex(creator);
                if (allowed < 0) return false;

                if (creator.setIndex == allowed) return true;

                creator.setIndex = allowed;
                creator.OnChangeStartSet();
                DemonLookPlugin.Log.LogInfo($"Demon locked to starting background {creator.startSets[allowed].setName}.");
                return true;
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogError("Could not lock the demon starting background: " + e);
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(CharacterCreaterController), "PrevStartSet")]
    internal static class PrevStartSet_Patch
    {
        private static bool Prefix(CharacterCreaterController __instance)
        {
            StartRestriction.ReportSets(__instance);
            return !StartRestriction.Pin(__instance);
        }
    }

    [HarmonyPatch(typeof(CharacterCreaterController), "NextStartSet")]
    internal static class NextStartSet_Patch
    {
        private static bool Prefix(CharacterCreaterController __instance)
        {
            StartRestriction.ReportSets(__instance);
            return !StartRestriction.Pin(__instance);
        }
    }

    // Switching to demon while standing on some other background has to move the choice too,
    // otherwise the restriction would only take effect on the next click.
    [HarmonyPatch(typeof(CharacterCreaterController), "ChangeStartSet")]
    internal static class ChangeStartSet_Patch
    {
        private static void Postfix(CharacterCreaterController __instance)
        {
            StartRestriction.ReportSets(__instance);
        }
    }
}
