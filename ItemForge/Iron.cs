using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// What playing without a second chance is worth.
    ///
    /// Железный человек — это одно сохранение и никаких перезагрузок: проиграл бой, значит
    /// проиграл. Игра за это не даёт ничего. Между тем это не прихоть настройки, а другая
    /// игра: каждый бой берётся всерьёз, из каждой драки выходят, а не переигрывают её.
    ///
    /// Здесь за это платят сундуками. Не опытом — опыт ускорял бы и без того быструю игру, —
    /// а тем, что лежит внутри: цветные вещи в сундуке железного человека попадаются чаще.
    /// Кто не может переиграть неудачу, тому хотя бы находка достаётся щедрее.
    ///
    /// И учится он быстрее: характеристики от дела — ходьбы, боя, заклинаний, ран — растут у
    /// своих на пятнадцать процентов скорее. Опыт уровня и покупка очков за него не трогаются:
    /// речь о том, чему человек учится сам, делая.
    /// </summary>
    internal static class Iron
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Chests;
        internal static ConfigEntry<float> Growth;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Iron", "Enabled", true,
                "Pay for playing without a second chance. Ironman finds better things in "
                + "chests: the game asks more of him and gave him nothing for it.");

            Chests = config.Bind("Iron", "Chests", 0.15f,
                new ConfigDescription(
                    "How much likelier a coloured thing is in an ironman's chest, as a share. "
                    + "Every step of colour — green, blue, purple — becomes this much likelier "
                    + "at once, so the loot as a whole comes out about this much better.\n\n"
                    + "Прежде здесь был опыт за убитых — на треть больше. Но опыт только "
                    + "ускоряет игру, которая и так идёт быстро; а находка в сундуке — это то, "
                    + "ради чего в него лезут.",
                    new AcceptableValueRange<float>(0f, 2f)));

            Growth = config.Bind("Iron", "Growth", 0.15f,
                new ConfigDescription(
                    "How much faster an ironman's own people grow their attributes by doing — "
                    + "walking, fighting, casting, taking wounds — as a share. The experience of "
                    + "levels and the buying of points with it are left alone.",
                    new AcceptableValueRange<float>(0f, 2f)));
        }

        /// <summary>Идёт ли игра железным человеком.</summary>
        internal static bool Is()
        {
            try
            {
                SaveLoadManager keeper = SaveLoadManager.Instance;

                return keeper != null && keeper.archiveProfile != null
                    && keeper.archiveProfile.isIronMan;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Во сколько раз щедрее сундук прямо сейчас.</summary>
        internal static float Luck()
        {
            if (Enabled == null || !Enabled.Value || !Enchant.Opening) return 1f;
            if (Chests == null || Chests.Value <= 0f) return 1f;
            if (!Is()) return 1f;

            return 1f + Chests.Value;
        }
    }

    // Опыт характеристик от дела — у своих железного человека он скорее.
    [HarmonyPatch(typeof(HumaniodUnit), "GainAttributeExp")]
    internal static class GainAttributeExp_Iron_Patch
    {
        private static void Prefix(HumaniodUnit __instance, ref float exp)
        {
            try
            {
                if (Iron.Enabled == null || !Iron.Enabled.Value || Iron.Growth.Value <= 0f) return;
                if (__instance == null || __instance.Data == null) return;
                if (!__instance.inParty && __instance.Data.team != Faction.player) return;
                if (!Iron.Is()) return;

                exp *= 1f + Iron.Growth.Value;
            }
            catch
            {
            }
        }
    }
}
