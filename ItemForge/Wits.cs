using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// What a wizard's mind is worth to his spell.
    ///
    /// Игра даёт за очко интеллекта сотую долю к силе заклинания — ровно столько же, сколько
    /// сила даёт удару. Но удару сила даёт не только это: она входит в требование оружия, в
    /// переносимый вес, в мышцы. У интеллекта же всего и есть, что перезарядка да пол-очка
    /// уклонения, и оттого сотая доля выходит скупой платой за стат, на который маг обязан
    /// тратить весь свой рост.
    ///
    /// Здесь она удвоена: очко ума — два процента силы заклинания. Сотня даёт двойной урон,
    /// и маг наконец растёт от того, чем он маг.
    /// </summary>
    internal static class Wits
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> PerPoint;
        internal static ConfigEntry<float> Learning;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Wits", "Enabled", true,
                "Let the mind tell on the spell as much as the arm tells on the blow.");

            PerPoint = config.Bind("Wits", "PerPoint", 0.02f,
                new ConfigDescription(
                    "What one point of intelligence is worth to a spell, as a share. Two "
                    + "hundredths, where the game gives one: at a hundred a spell strikes twice "
                    + "as hard. Set it back to a hundredth to have the game's own reckoning.",
                    new AcceptableValueRange<float>(0f, 0.1f)));

            Learning = config.Bind("Wits", "Learning", 0.0025f,
                new ConfigDescription(
                    "How much more experience every point of intelligence brings, as a share. "
                    + "A quarter of a percent: a hundred of it gives a quarter more for "
                    + "everything learned, from a kill to a finished quest.",
                    new AcceptableValueRange<float>(0f, 0.05f)));
        }

        /// <summary>
        /// Ничего. И это правильное «ничего».
        ///
        /// Прежде здесь доплачивалась разница: игровая сотая вычиталась, наша двухсотая
        /// прибавлялась сверх уже посчитанного. Счёт поверх счёта, в котором старое правило
        /// поминалось на каждом шагу, — а само правило при этом оставалось игровым и в любую
        /// минуту могло разойтись с нашим представлением о нём.
        ///
        /// Теперь цена стоит прямо в строке пересчёта: «MagicDamageMD += Intelligence × наш
        /// спрос». Доплачивать нечего.
        /// </summary>
        internal static void Sharpen(HumaniodUnit who)
        {
        }
    }

    // После того как игра свела свои множители, но до того, как ими кто-то воспользовался.
    [HarmonyPatch(typeof(HumaniodUnit), "WriteUnitAttribute")]
    internal static class Write_Wits_Patch
    {
        private static void Postfix(HumaniodUnit __instance)
        {
            try
            {
                Wits.Sharpen(__instance);
            }
            catch
            {
            }
        }
    }

    // Умный берёт от всякого опыта больше: за каждое очко ума своя доля сверху.
    [HarmonyPatch(typeof(HumaniodUnit), "GainExp")]
    internal static class GainExp_Wits_Patch
    {
        private static void Prefix(HumaniodUnit __instance, ref int exp)
        {
            try
            {
                if (!Wits.Enabled.Value || Wits.Learning.Value <= 0f || exp <= 0) return;
                if (__instance == null || __instance.Data == null) return;

                float more = Mathf.Max(0, __instance.Data.intelligence) * Wits.Learning.Value;
                if (more > 0f) exp = Mathf.RoundToInt(exp * (1f + more));
            }
            catch
            {
            }
        }
    }
}
