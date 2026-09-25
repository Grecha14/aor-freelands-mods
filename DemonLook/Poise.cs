using System;
using BepInEx.Configuration;
using HarmonyLib;

namespace DemonLook
{
    /// <summary>
    /// A demon's blow does not wait on his stance.
    ///
    /// Стойка в этой игре стоит времени. Осторожный бьёт на треть реже, оборонительный вдвое
    /// реже: игра делит промежуток между ударами на три четверти и на половину, и так
    /// человек платит за то, что бережётся.
    ///
    /// Демон за это не платит. Он может держаться осторожно или стоять в обороне — и всё
    /// равно бить в полную силу, с тем же промежутком, что и напористый. Прочее, что даёт
    /// стойка, остаётся при ней: обороняющийся уворачивается лучше, осторожный точнее бьёт.
    /// Демон получает бережность без её цены.
    /// </summary>
    internal static class Poise
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Poise", "Enabled", true,
                "Let a demon strike at his full rate in any stance. A man who fights "
                + "cautiously strikes a third less often and a defensive one half as often; a "
                + "demon keeps what the stance gives him and pays nothing for it.");

            Telling = config.Bind("Poise", "Telling", false,
                "Say in the log when a demon's stance delay is waived.");
        }

        private static AccessTools.FieldRef<Weapon, float> interval;
        private static bool looked;
        private static int told;

        /// <summary>Промежуток между ударами — поле закрытое, достаём его однажды.</summary>
        internal static bool Set(Weapon arm, float every)
        {
            if (!looked)
            {
                looked = true;

                try
                {
                    interval = AccessTools.FieldRefAccess<Weapon, float>("attackInterval");
                }
                catch (Exception e)
                {
                    DemonLookPlugin.Log.LogWarning("Не нашёл промежутка удара: " + e.Message);
                }
            }

            if (interval == null) return false;

            interval(arm) = every;
            arm.attackTimer = every;

            if (Telling.Value && told < 5)
            {
                told++;
                DemonLookPlugin.Log.LogInfo($"Демон бьёт без задержки стойки: промежуток "
                    + $"{every:0.00} с.");
            }

            return true;
        }
    }

    // Отсчёт до следующего удара. У человека его растягивает стойка, у демона — нет.
    [HarmonyPatch(typeof(Weapon), "ResetAttackTimer")]
    internal static class Timer_Poise_Patch
    {
        private static bool Prefix(Weapon __instance)
        {
            try
            {
                if (Poise.Enabled == null || !Poise.Enabled.Value) return true;
                if (__instance == null || __instance.attackSpeed <= 0f) return true;
                if (!Racial.IsDemon(__instance.owner)) return true;

                // Ровно то, что делает игра, — без двух строк про стойку.
                return !Poise.Set(__instance, 1f / __instance.attackSpeed);
            }
            catch
            {
                return true;
            }
        }
    }
}
