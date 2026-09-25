using System;
using BepInEx.Configuration;
using HarmonyLib;
using spell;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// What endurance and wisdom do to the time things take.
    ///
    /// Выносливый заживает быстрее: каждое очко выносливости ускоряет заживление травм — сломанной
    /// кости, выбитого сустава — на процент, и без всякого предела: у кого её триста, тот
    /// срастается вчетверо быстрее. Всё прочее дурное — яд, оглушение, страх, проклятие —
    /// проходит с него на процент быстрее за очко, но не больше чем на восемьдесят процентов.
    ///
    /// Мудрый держит доброе дольше: каждое очко мудрости продлевает положительное действие на
    /// него на два процента. Мудрость — это то, что в игре звалось силой воли: сама черта та же,
    /// другое у неё только имя.
    /// </summary>
    internal static class Vigil
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Heal;
        internal static ConfigEntry<float> Shed;
        internal static ConfigEntry<float> ShedMost;
        internal static ConfigEntry<float> Wisdom;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Vigil", "Enabled", true,
                "Let endurance make injuries heal and ill effects wear off sooner, and wisdom make "
                + "good effects last longer.");

            Heal = config.Bind("Vigil", "Heal", 0.01f,
                new ConfigDescription(
                    "How much faster injuries heal for every point of endurance, as a share. No "
                    + "ceiling: three hundred endurance heals four times as fast.",
                    new AcceptableValueRange<float>(0f, 0.1f)));

            Shed = config.Bind("Vigil", "Shed", 0.01f,
                new ConfigDescription(
                    "How much faster every other ill effect wears off for every point of "
                    + "endurance, as a share.",
                    new AcceptableValueRange<float>(0f, 0.1f)));

            ShedMost = config.Bind("Vigil", "ShedMost", 0.8f,
                new ConfigDescription(
                    "The most endurance can speed an ill effect up by, as a share. Eight tenths: "
                    + "never more than one and eight tenths as fast.",
                    new AcceptableValueRange<float>(0f, 5f)));

            Wisdom = config.Bind("Vigil", "Wisdom", 0.02f,
                new ConfigDescription(
                    "How much longer a good effect lasts for every point of wisdom, as a share.",
                    new AcceptableValueRange<float>(0f, 0.2f)));

            Telling = config.Bind("Vigil", "Telling", true,
                "Say in the log, for the first few, how long an effect was made to last and why.");
        }

        /// <summary>How many times as fast an injury heals on this man.</summary>
        internal static float Healing(UnitAttribute who)
        {
            if (Enabled == null || !Enabled.Value) return 1f;

            HumaniodUnit man = who as HumaniodUnit;
            if (man == null) return 1f;

            return 1f + Mathf.Max(0, man.Endurance) * Heal.Value;
        }

        private static int told;

        /// <summary>Stretches or shortens an effect by what the man bearing it is made of.</summary>
        internal static void Time(BuffManager keeper, BuffBase buff)
        {
            if (Enabled == null || !Enabled.Value || keeper == null || buff == null) return;
            if (buff.buffInfo == null || buff.duration <= 0f) return;

            HumaniodUnit man = keeper.unit as HumaniodUnit;
            if (man == null || man.Data == null) return;

            // Наши собственные знаки — груз, недобор сил — не действие, а положение дел: они
            // лежат, пока лежит причина, и сроком не живут.
            string id = buff.buffInfo.id ?? "";
            if (id.StartsWith("ItemForge", StringComparison.Ordinal)) return;
            if (id == "WeightDebuff" || id == "WeightBuff") return;

            float was = buff.duration;
            string why;

            if (buff.buffInfo.type == bufftype.injury)
            {
                buff.duration = was / Healing(man);
                why = "травма, выносливость " + man.Endurance;
            }
            else if (buff.buffInfo.IsDebuff)
            {
                float faster = Mathf.Min(Mathf.Max(0f, ShedMost.Value), Mathf.Max(0, man.Endurance) * Shed.Value);
                buff.duration = was / (1f + faster);
                why = "дурное, выносливость " + man.Endurance;
            }
            else if (buff.buffInfo.IsBuff)
            {
                buff.duration = was * (1f + Mathf.Max(0, man.Willpower) * Wisdom.Value);
                why = "доброе, мудрость " + man.Willpower;
            }
            else
            {
                return;
            }

            if (Telling.Value && told < 20 && Mathf.Abs(buff.duration - was) > 0.01f)
            {
                told++;
                ItemForgePlugin.Log.LogInfo($"«{man.Data.unitname}»: «{id}» {was:0.#} → "
                    + $"{buff.duration:0.#} ({why}).");
            }
        }
    }

    [HarmonyPatch(typeof(BuffManager), "AddBuff", new Type[] { typeof(BuffBase) })]
    internal static class AddBuff_Vigil_Patch
    {
        private static void Prefix(BuffManager __instance, BuffBase buff)
        {
            try { Vigil.Time(__instance, buff); } catch { }
        }
    }
}
