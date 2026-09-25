using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Lets a heavy blow bleed like a heavy blow.
    ///
    /// Кровь в игре есть, и сделана она хорошо: три размера брызг на каждый тип удара и на
    /// каждый цвет крови, лужи, подсыхающие следы. Выбирается размер долей от здоровья —
    /// меньше пятнадцати сотых мелкие, меньше трёх десятых средние, дальше крупные
    /// («EffectManager.GetSlashBlood»).
    ///
    /// Беда в том, что доля считается от полного здоровья, а здоровье к концу игры за четыре
    /// сотни. Удар в полсотни — это восемь сотых, то есть мелкие брызги: рубящий по горлу
    /// выглядит как царапина. И крит не отличается ничем: нашёл щель, прошёл мимо железа, а
    /// крови столько же, сколько от скользнувшего по наручу.
    ///
    /// Здесь доля, с которой игра выбирает размер, поднимается: криту вдвое, тяжёлому удару на
    /// ступень. Самой крови мы не рисуем и своих брызг не заводим — игровые и так хороши,
    /// им только нужно сказать правду о том, насколько сильно ударили.
    /// </summary>
    internal static class Gore
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Crit;
        internal static ConfigEntry<float> HeavyAt;
        internal static ConfigEntry<float> Heavy;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Gore", "Enabled", true,
                "Let a hard blow bleed like one. The game picks the size of the spray from the "
                + "share of full health taken, and by the end of the game health is four "
                + "hundred and more — so a blow of fifty counts as a scratch and a critical "
                + "one looks no different from a graze.");

            Crit = config.Bind("Gore", "Crit", 2.0f,
                new ConfigDescription(
                    "How much of a critical blow counts towards the size of the spray. Twice: a "
                    + "critical hit is a found seam, and what goes through a seam goes into "
                    + "flesh with nothing in the way.",
                    new AcceptableValueRange<float>(1f, 10f)));

            HeavyAt = config.Bind("Gore", "HeavyAt", 0.08f,
                new ConfigDescription(
                    "From what share of a man's full health a blow counts as heavy.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Heavy = config.Bind("Gore", "Heavy", 1.8f,
                new ConfigDescription(
                    "How much such a blow counts towards the size of the spray. At near two a "
                    + "heavy blow steps up a size, which is exactly what it should look like.",
                    new AcceptableValueRange<float>(1f, 10f)));

            Telling = config.Bind("Gore", "Telling", false,
                "Write out the first few blows and what they were counted as.");
        }

        // Что известно о том ударе, который прямо сейчас рисуется. Игра зовёт отрисовку крови
        // изнутри «OnHurt», куда «Attack» приходит целиком, а до самой отрисовки уже не
        // доходит — потому и запоминаем на один вызов.
        private static bool crit;
        private static float share;
        private static bool fresh;
        private static int told;

        internal static void Saw(Attack attack, UnitAttribute hurt)
        {
            crit = false;
            share = 0f;
            fresh = false;

            if (!Enabled.Value || attack == null || hurt == null) return;

            try
            {
                crit = attack.isCrit;
                share = hurt.maxhp > 0f ? attack.realDamage / hurt.maxhp : 0f;
                fresh = true;
            }
            catch
            {
            }
        }

        /// <summary>What share the spray should be drawn by, given what the blow was.</summary>
        internal static float Told(float was)
        {
            if (!Enabled.Value || !fresh) return was;

            float much = was;

            if (crit) much *= Crit.Value;
            else if (share >= HeavyAt.Value) much *= Heavy.Value;

            if (Telling.Value && told < 12)
            {
                told++;
                ItemForgePlugin.Log.LogInfo($"Кровь: доля {was:0.###} → {much:0.###}"
                    + (crit ? ", крит" : (share >= HeavyAt.Value ? ", тяжёлый" : "")));
            }

            return much;
        }
    }

    // Удар приходит сюда целиком, и здесь же игра зовёт отрисовку крови. Запоминаем, чем он
    // был, пока это ещё известно.
    [HarmonyPatch(typeof(UnitAudioManager), "OnHurt")]
    internal static class Gore_Saw_Patch
    {
        private static void Prefix(Attack attack, UnitAttribute hurted)
        {
            if (Gore.Enabled == null || !Gore.Enabled.Value) return;

            try
            {
                Gore.Saw(attack, hurted);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог разглядеть удар для крови: " + e.Message);
            }
        }
    }

    // А здесь размер брызг и выбирается — по доле, которую мы правим.
    [HarmonyPatch(typeof(EffectManager), "CreatBloodEffect")]
    internal static class Gore_Blood_Patch
    {
        private static void Prefix(ref float damagePercent)
        {
            if (Gore.Enabled == null || !Gore.Enabled.Value) return;

            try
            {
                damagePercent = Gore.Told(damagePercent);
            }
            catch
            {
            }
        }
    }
}
