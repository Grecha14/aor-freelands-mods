using System;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Takes the reload out of shopping.
    ///
    /// Лавка в игре набивается случайным броском, а решает, набивать ли, вот это:
    ///
    ///     public virtual void Init()
    ///     {
    ///         if (inited) return;
    ///         if (refreshCD &lt;= 0) { refreshCD = refreshDays; RefreshShop(); }
    ///         inited = true;
    ///     }
    ///
    /// `inited` в сохранение не попадает — в `ShopSaveData` его нет. Значит при каждой загрузке
    /// сцены `Init` проходит заново, и товар может перекатиться. Отсюда и известный приём:
    /// не понравилось на прилавке — перезагрузился.
    ///
    /// Гоняться за порядком вызовов здесь не стоит: он зависит от того, посещали ли область,
    /// успело ли отработать восстановление, и от десятка мелочей, которые я проверить не могу.
    /// Вместо этого делаем бросок повторяемым. Зерно случайности на время набивки подменяется
    /// на «эта лавка, это окно времени» — и сколько бы раз `RefreshShop` ни сработал за одно
    /// окно, он выложит ровно то же самое. Перезагрузка перестаёт что-либо значить сама собой,
    /// а когда окно честно сменится, сменится и товар.
    /// </summary>
    internal static class Counter
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Counter", "Enabled", true,
                "Make a shop's stock the same however many times it is rolled inside one "
                + "refresh period. The game re-runs that roll on every scene load, so reloading "
                + "until the counter looks better is free — and a shop nobody has opened yet "
                + "can be re-rolled by walking out of town and back.");

            Telling = config.Bind("Counter", "Telling", false,
                "Write down every counter fixed and the seed it was fixed to.");
        }

        private static UnityEngine.Random.State kept;
        private static bool holding;

        /// <summary>The number this shop's stock is decided by, for this window of time.</summary>
        private static int Seed(Shop shop)
        {
            string who = shop.instanceId;
            if (string.IsNullOrEmpty(who)) who = shop.shopName ?? "shop";

            int window = 0;
            try
            {
                int days = Mathf.Max(1, shop.refreshDays);
                window = TimeManager.Day / days;
            }
            catch
            {
            }

            // Имя лавки и окно времени — всё, от чего зависит её товар. Ни числа загрузок, ни
            // числа заходов в город здесь нет, и в этом весь смысл.
            unchecked
            {
                int hash = 17;
                foreach (char c in who) hash = hash * 31 + c;
                return hash * 31 + window;
            }
        }

        internal static void Hold(Shop shop)
        {
            if (!Enabled.Value || shop == null || holding) return;

            try
            {
                kept = UnityEngine.Random.state;
                holding = true;

                int seed = Seed(shop);
                UnityEngine.Random.InitState(seed);

                if (Telling.Value)
                {
                    ItemForgePlugin.Log.LogInfo($"Прилавок «{shop.shopName}» закреплён "
                        + $"зерном {seed} (день {TimeManager.Day}, срок {shop.refreshDays}).");
                }
            }
            catch (Exception e)
            {
                holding = false;
                ItemForgePlugin.Log.LogError("Не смог закрепить прилавок: " + e);
            }
        }

        internal static void Free()
        {
            if (!holding) return;

            holding = false;
            UnityEngine.Random.state = kept;
        }
    }

    // Набивка прилавка целиком: зерно ставим до неё и возвращаем после, чтобы всё прочее в
    // игре продолжало случаться как ни в чём не бывало.
    [HarmonyPatch(typeof(Shop), "RefreshShop")]
    internal static class RefreshShop_Counter_Patch
    {
        private static void Prefix(Shop __instance) { Counter.Hold(__instance); }

        private static void Postfix() { Counter.Free(); }
    }
}
