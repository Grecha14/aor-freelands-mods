using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Fear that spreads from the dead, grief for the fallen, and a night that belongs to beasts.
    ///
    /// Ужас расправы. Кому на глазах снесли голову, отсекли руку, раскололи грудь — тот уже не
    /// просто «пал»: рядом стоящие его товарищи надламываются. Игра умеет надлом — бегство на
    /// несколько секунд или ярость, — и зовёт его сама, когда боец ранен или падает кто-то из
    /// группы; здесь его зовёт и расправа, тем сильнее, чем она страшнее и чем ближе видевший.
    /// Что надломит, решает боевой дух: у бодрого есть шанс устоять, у сломленного — почти нет.
    ///
    /// Скорбь. Погибший в отряде — навсегда, и это ложится на всех: каждый теряет боевой дух,
    /// а близкий друг павшего — вдвое. Боевой дух ниже семидесяти пяти игра сама превращает в
    /// потерю меткости, разума и мудрости; возвращается он отдыхом, едой и временем.
    ///
    /// Ночь принадлежит зверю. Люди в темноте видят хуже — звери, нежить и чудовища видят
    /// как днём и бьют ночью на четверть сильнее.
    /// </summary>
    internal static class Dread
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Terror;
        internal static ConfigEntry<float> Radius;
        internal static ConfigEntry<float> Grief;
        internal static ConfigEntry<float> CloseGrief;
        internal static ConfigEntry<bool> NightEyes;
        internal static ConfigEntry<float> NightBite;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Dread", "Enabled", true,
                "Let gruesome deaths break the nerve of those who see them, let the party grieve "
                + "its dead, and let beasts own the night.");

            Terror = config.Bind("Dread", "Terror", 35f,
                new ConfigDescription("The break chance a beheading puts on a comrade standing right "
                    + "by, against the game own roll on morale. A severed limb or a burst chest "
                    + "gives two thirds of it; it falls off to nothing at the edge of the radius.",
                    new AcceptableValueRange<float>(0f, 100f)));

            Radius = config.Bind("Dread", "Radius", 12f,
                new ConfigDescription("How far, in metres, a slaughter is seen.",
                    new AcceptableValueRange<float>(2f, 40f)));

            Grief = config.Bind("Dread", "Grief", 15f,
                new ConfigDescription("Morale every party member loses when one of them dies.",
                    new AcceptableValueRange<float>(0f, 100f)));

            CloseGrief = config.Bind("Dread", "CloseGrief", 30f,
                new ConfigDescription("Morale a close friend of the fallen loses instead.",
                    new AcceptableValueRange<float>(0f, 100f)));

            NightEyes = config.Bind("Dread", "NightEyes", true,
                "Let beasts, undead and monsters see at night as by day.");

            NightBite = config.Bind("Dread", "NightBite", 1.25f,
                new ConfigDescription("How many times harder beasts, undead and monsters hit at night.",
                    new AcceptableValueRange<float>(1f, 3f)));

            Telling = config.Bind("Dread", "Telling", true,
                "Say in the log who broke at the sight of what, and who grieves whom.");
        }

        /// <summary>Beasts, the undead and monsters — not the player people.</summary>
        internal static bool Creature(UnitAttribute u)
        {
            if (u == null || u.Data == null || u.inParty || u.Data.team == Faction.player) return false;

            UnitRace race = u.Data.race;
            if (race == UnitRace.undead || race == UnitRace.mythological || race == UnitRace.animal || race == UnitRace.insect) return true;

            Faction team = u.Data.team;
            if (team == Faction.monster || team == Faction.wilding || team == Faction.neutralWilding) return true;

            return !(u is HumaniodUnit);
        }

        internal static bool SeesInDark(UnitAttribute watcher)
        {
            return Enabled != null && Enabled.Value && NightEyes.Value && Creature(watcher);
        }

        // ------------------------------------------------------------------ ужас расправы

        private static int toldTerror;

        /// <summary>
        /// A man has died hard: those of his side who saw it may break.
        /// grim — 3 голова, 2 рука или нога, грудь вдребезги.
        /// </summary>
        internal static void Slaughter(UnitAttribute victim, UnitAttribute killer, int grim)
        {
            if (Enabled == null || !Enabled.Value || victim == null || grim <= 0 || Terror.Value <= 0f) return;

            try
            {
                Vector3 at = victim.transform.position;
                float radius = Radius.Value;
                float full = Terror.Value * Mathf.Clamp01(grim / 3f);

                foreach (HumaniodUnit one in UnityEngine.Object.FindObjectsOfType<HumaniodUnit>())
                {
                    if (one == null || (object)one == (object)victim || (object)one == (object)killer) continue;
                    if (one.Data == null || one.Data.isdead || !one.gameObject.activeInHierarchy) continue;
                    if ((object)one == (object)gameManager.currentplayUnit) continue;
                    if (!GameController.IsAlly(one, victim)) continue;

                    float distance = Vector3.Distance(one.transform.position, at);
                    if (distance > radius) continue;

                    // Паладин со второй ступени не ломается, а рядом с ним ломаются реже.
                    if (Calling.Rank(one, Calling.Kind.Paladin) >= 2) continue;

                    float chance = full * (1f - distance / radius) * (1f - Calling.Courage(one));
                    if (chance <= 0.5f) continue;

                    bool was = one.stateMachine != null && one.stateMachine.simpleSstate == SimpleState.fleeing;
                    one.CheckBreak(killer, chance, 30f);

                    if (Telling.Value && toldTerror < 40 && !was && one.stateMachine != null
                        && one.stateMachine.simpleSstate == SimpleState.fleeing)
                    {
                        toldTerror++;
                        ItemForgePlugin.Log.LogInfo($"Ужас: «{one.Data.unitname}» дрогнул, увидев расправу "
                            + $"над «{victim.Data.unitname}» в {distance:0.#} м (шанс {chance:0}).");
                    }
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Ужас расправы сорвался: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ скорбь

        internal static void Mourn(UnitAttribute dead)
        {
            if (Enabled == null || !Enabled.Value || dead == null || dead.Data == null) return;
            if (!dead.Data.trueDead || !dead.inParty || (bool)dead.summonComponent) return;
            if (PartyManager.instance == null || PartyManager.instance.partyMembers == null) return;

            try
            {
                List<string> said = new List<string>();

                foreach (HumaniodUnit one in PartyManager.instance.partyMembers)
                {
                    if (one == null || (object)one == (object)dead || one.Data == null || one.Data.isdead) continue;

                    bool close = false;
                    try { close = one.GetRelation(dead.Data.id) >= 60; } catch { }

                    float loss = close ? CloseGrief.Value : Grief.Value;
                    if (loss <= 0f) continue;

                    one.Data.AddMorale(-loss);
                    said.Add(one.Data.unitname + (close ? " (друг, −" : " (−") + loss.ToString("0") + ")");
                }

                if (Telling.Value && said.Count > 0)
                {
                    ItemForgePlugin.Log.LogInfo($"Скорбь по «{dead.Data.unitname}»: " + string.Join(", ", said.ToArray()) + ".");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Скорбь сорвалась: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "Die")]
    internal static class Die_Dread_Patch
    {
        private static void Postfix(UnitAttribute __instance)
        {
            try { Dread.Mourn(__instance); } catch { }
        }
    }

    // Ночь — время зверя: зверь, нежить и чудовище бьют сильнее.
    [HarmonyPatch(typeof(UnitAttribute), "DamageReduce")]
    [HarmonyPriority(Priority.VeryLow)]
    internal static class DamageReduce_Dread_Patch
    {
        private static void Postfix(Attack attack, ref DamageBase __result)
        {
            if (Dread.Enabled == null || !Dread.Enabled.Value || __result == null || attack == null) return;
            if (Dread.NightBite.Value <= 1.0001f || !Dread.Creature(attack.attacker)) return;

            try
            {
                if (Shade.Dark()) __result *= Dread.NightBite.Value;
            }
            catch
            {
            }
        }
    }

    // Кто смотрит: зверю ночь не помеха.
    [HarmonyPatch(typeof(UnitAttribute), "InSenseRange")]
    internal static class InSenseRange_Dread_Patch
    {
        private static void Prefix(UnitAttribute __instance, out UnitAttribute __state)
        {
            __state = Shade.watcher;
            Shade.watcher = __instance;
        }

        private static void Finalizer(UnitAttribute __state)
        {
            Shade.watcher = __state;
        }
    }
}
