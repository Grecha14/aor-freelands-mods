using System;
using System.Reflection;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// A working day, and the end of it.
    ///
    /// Ремесло в игре не знает усталости. Нажал «ковать» десять раз — часы прокрутились на
    /// шестьдесят вперёд, и всё это время человек не ел, не спал и не отдыхал. Раньше это
    /// ничего не стоило: сытость возвращалась миской супа, здоровье само собой. Теперь стоит,
    /// и стоит дорого — за одну ночную смену можно уехать в такую яму, из которой отряд будет
    /// выбираться неделю.
    ///
    /// Поэтому работа идёт сменами. За один раз человек делает столько, сколько успевает за
    /// смену, и не больше; остальное — после отдыха, второй смены, третьей. А если он уже
    /// голоден, вымотан или подавлен ниже половины, он не берётся за дело вовсе и говорит об
    /// этом. Это не запрет, а порядок: сначала о себе, потом о наковальне.
    /// </summary>
    internal static class Shift
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Hours;
        internal static ConfigEntry<float> Floor;
        internal static ConfigEntry<bool> Guards;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Shift", "Enabled", true,
                "Break long work into shifts, and let a man refuse work he is in no state for.");

            Hours = config.Bind("Shift", "Hours", 6,
                new ConfigDescription(
                    "Hours of work in one stretch. What does not fit waits for the next one, "
                    + "after food and sleep.",
                    new AcceptableValueRange<int>(1, 24)));

            Floor = config.Bind("Shift", "Floor", 0.5f,
                new ConfigDescription(
                    "The share of morale, satiety and vigour below which nobody takes up work. "
                    + "A half.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Guards = config.Bind("Shift", "Guards", true,
                "Let a man refuse. Without this the shift still applies, but a starving hero "
                + "will still stand at the anvil.");
        }

        /// <summary>Whether a man is in any state to work, and says so if not.</summary>
        internal static bool Ready(HumaniodUnit who, string job)
        {
            if (!Enabled.Value || !Guards.Value || who == null || who.Data == null) return true;

            try
            {
                float line = Floor.Value;

                string missing = null;

                if (who.Data.morale < 100f * line) missing = "не в духе";
                else if (Share(who.Data.satiety, Most(who, true)) < line) missing = "голоден";
                else if (Share(who.Data.vigor, Most(who, false)) < line) missing = "вымотан";

                if (missing == null) return true;

                string word = who.Data.unitname + " " + missing + ", работа подождёт";

                GameController.ShowMessage(word, 2.5f);
                ItemForgePlugin.Log.LogInfo(word + " (" + job + ").");

                return false;
            }
            catch
            {
                return true;
            }
        }

        private static float Share(float had, float most)
        {
            if (most <= 0f) return 1f;
            return had / most;
        }

        private static float Most(HumaniodUnit who, bool food)
        {
            try
            {
                if (who.info == null) return 100f;
                return food ? who.info.maxSatiety : who.info.maxVigor;
            }
            catch
            {
                return 100f;
            }
        }

        /// <summary>How many of a thing fit in one shift, given what one costs.</summary>
        internal static int Fits(int each, int asked)
        {
            if (!Enabled.Value || each <= 0 || asked <= 1) return asked;

            int room = Hours.Value / each;
            if (room < 1) room = 1;

            return asked <= room ? asked : room;
        }
    }

    // Ковка, алхимия и стряпня идут через одно окно, и кнопка у него одна. Здесь и решается,
    // возьмётся ли человек за дело и сколько успеет за смену.
    [HarmonyPatch(typeof(CraftManager), "OnMakeButtonDown")]
    internal static class OnMakeButtonDown_Shift_Patch
    {
        private static FieldInfo whoField;
        private static FieldInfo howManyField;
        private static FieldInfo recipeField;

        private static bool Prefix(CraftManager __instance)
        {
            if (!Shift.Enabled.Value || __instance == null) return true;

            try
            {
                if (whoField == null) whoField = AccessTools.Field(typeof(CraftManager), "creator");
                if (howManyField == null) howManyField = AccessTools.Field(typeof(CraftManager), "count");
                if (recipeField == null) recipeField = AccessTools.Field(typeof(CraftManager), "currentRecipe");

                if (whoField == null || howManyField == null || recipeField == null) return true;

                HumaniodUnit hand = whoField.GetValue(__instance) as HumaniodUnit;
                if (hand == null) return true;

                if (!Shift.Ready(hand, "ремесло")) return false;

                CraftRecipe recipe = recipeField.GetValue(__instance) as CraftRecipe;
                if (recipe == null || recipe.product == null) return true;

                int asked = (int)howManyField.GetValue(__instance);
                int each = __instance.GetCraftTimeCost(recipe.product);

                int fits = Shift.Fits(each, asked);
                if (fits >= asked) return true;

                howManyField.SetValue(__instance, fits);

                string word = "За смену успеет " + fits + " из " + asked
                    + ", остальное после отдыха";

                GameController.ShowMessage(word, 2.5f);
                ItemForgePlugin.Log.LogInfo(word + " (" + recipe.product.Name + ", "
                    + each + " ч за штуку).");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог разложить работу по сменам: " + e.Message);
            }

            return true;
        }
    }

    // Лечение руками — такая же работа. В городской лечебнице трудится не герой, и там его
    // состояние никого не касается.
    [HarmonyPatch(typeof(HealingManager), "OnPatientSlotClicked")]
    internal static class OnPatientSlotClicked_Shift_Patch
    {
        private static bool Prefix()
        {
            try
            {
                if (Mending.Infirmary) return true;

                HumaniodUnit hand = Mending.Medic();
                if (hand == null) return true;

                return Shift.Ready(hand, "лечение");
            }
            catch
            {
                return true;
            }
        }
    }

    // Починка снаряжения. Сюда приходит и наковальня в лагере, и походное действие «Ремонт».
    [HarmonyPatch(typeof(HumaniodUnit), "RepairEquipments")]
    internal static class RepairEquipments_Shift_Patch
    {
        private static bool Prefix(HumaniodUnit __instance)
        {
            try { return Shift.Ready(__instance, "ремонт"); }
            catch { return true; }
        }
    }
}
