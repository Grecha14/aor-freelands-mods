using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using spell;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Set bonuses the game's data model cannot express.
    ///
    /// A bonus is a list of stat modifiers, so anything conditional — on a critical hit, on
    /// damage dealt, on a particular damage type — has no place to live in the item itself
    /// and has to be written here. Each rule checks how many pieces are worn at the moment
    /// it fires, so nothing is stored and taking the set off ends it.
    /// </summary>
    internal static class SetCombat
    {
        internal static ConfigEntry<int> KnockoutParts;
        internal static ConfigEntry<int> KnockoutChance;
        internal static ConfigEntry<float> KnockoutSeconds;

        internal static ConfigEntry<int> FinalParts;
        internal static ConfigEntry<float> LightVulnerability;
        internal static ConfigEntry<float> Lifesteal;

        internal static void Bind(ConfigFile config)
        {
            KnockoutParts = config.Bind("SetCombat", "KnockoutParts", 7,
                new ConfigDescription("Pieces needed before critical hits can knock the target out.",
                    new AcceptableValueRange<int>(1, 9)));

            KnockoutChance = config.Bind("SetCombat", "KnockoutChance", 50,
                new ConfigDescription("Chance in percent that a critical hit knocks the target out.",
                    new AcceptableValueRange<int>(0, 100)));

            KnockoutSeconds = config.Bind("SetCombat", "KnockoutSeconds", 8f,
                new ConfigDescription("How long the knockout lasts. Eight is what the game itself uses.",
                    new AcceptableValueRange<float>(1f, 60f)));

            FinalParts = config.Bind("SetCombat", "FinalParts", 9,
                new ConfigDescription("Pieces needed for the final bargain: lifesteal and the "
                    + "vulnerability to light that pays for it.",
                    new AcceptableValueRange<int>(1, 9)));

            LightVulnerability = config.Bind("SetCombat", "LightVulnerability", 1.0f,
                new ConfigDescription("Extra light damage the wearer suffers, as a share. 1.0 doubles it. "
                    + "Light resistance cannot go below zero in this game, so the vulnerability has to "
                    + "be applied here rather than as a negative resistance.",
                    new AcceptableValueRange<float>(0f, 5f)));

            Lifesteal = config.Bind("SetCombat", "Lifesteal", 0.1f,
                new ConfigDescription(
                    "Share of the damage dealt that heals the wearer, on top of what the pieces "
                    + "themselves give. Zero by default: the boots carry the game's own lifesteal "
                    + "talent, and a second helping here would quietly double it.",
                    new AcceptableValueRange<float>(0f, 1f)));
        }

        internal static int WornCount(UnitAttribute unit)
        {
            return Forge.WornCount(unit);
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "TakeDamage")]
    internal static class TakeDamage_Patch
    {
        // Snapshot of the victim's health before the blow, so the damage that actually landed
        // can be measured afterwards. Reading the attack's own numbers would count damage that
        // resistances and shields went on to absorb.
        private static void Prefix(UnitAttribute __instance, out float __state)
        {
            __state = __instance != null && __instance.Data != null ? __instance.Data.currenthp : 0f;
        }

        private static void Postfix(UnitAttribute __instance, Attack attack, float __state)
        {
            try
            {
                if (attack == null || attack.attacker == null || __instance == null) return;
                if (attack.attacker == __instance) return;

                int worn = SetCombat.WornCount(attack.attacker);
                if (worn == 0) return;

                float dealt = __state - __instance.Data.currenthp;
                if (dealt <= 0f) return;

                if (attack.isCrit
                    && worn >= SetCombat.KnockoutParts.Value
                    && SetCombat.KnockoutChance.Value > 0
                    && UnityEngine.Random.Range(0, 100) < SetCombat.KnockoutChance.Value)
                {
                    Knockout(__instance, attack.attacker);
                }

                if (worn >= SetCombat.FinalParts.Value && SetCombat.Lifesteal.Value > 0f)
                {
                    attack.attacker.Heal(dealt * SetCombat.Lifesteal.Value, attack.attacker);
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Боевой эффект комплекта сорвался: " + e);
            }
        }

        private static void Knockout(UnitAttribute victim, UnitAttribute attacker)
        {
            if (victim.buffmanger == null || victim.Data == null) return;
            if (victim.Data.isdead || victim.buffmanger.ContainBuff("Knockout")) return;

            UIBuffInfo info = UIBuffDatabase.Instance != null ? UIBuffDatabase.Instance.GetByID("Knockout") : null;
            if (info == null) return;

            victim.buffmanger.AddBuff(new BuffBase(info, attacker, SetCombat.KnockoutSeconds.Value));
        }
    }

    // Светлый урон по носителю удваивается. Отдельным патчем, а не отрицательным
    // сопротивлением: игра зажимает сопротивления снизу нулём, ниже опустить нельзя.
    [HarmonyPatch(typeof(UnitAttribute), "DamageReduce")]
    internal static class DamageReduce_Patch
    {
        private static void Postfix(UnitAttribute __instance, ref DamageBase __result)
        {
            try
            {
                if (__result == null) return;
                if (SetCombat.LightVulnerability.Value <= 0f) return;
                if (SetCombat.WornCount(__instance) < SetCombat.FinalParts.Value) return;

                Damage light;
                if (!__result.TryGetValue(DamageType.positive, out light) || light == null) return;

                light.currentDamage *= 1f + SetCombat.LightVulnerability.Value;
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Уязвимость к свету сорвалась: " + e);
            }
        }
    }
}
