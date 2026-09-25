using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using spell;
using UnityEngine;

namespace DemonLook
{
    /// <summary>
    /// The watch by night: guards sleep without their armour, and after a body is found they
    /// walk the streets and stop whoever walks them.
    ///
    /// Ложась, стражник снимает шлем, доспех и поножи — они ждут его в оружейной казармы, —
    /// оружие остаётся при нём. Встал — через двадцать секунд снова в броне; если за эти секунды
    /// на него напали, бьётся без неё, пока бой не кончится. В сохранении он всегда записан
    /// одетым: броня не пропадёт ни при сохранении во сне, ни если мод снять. Убит во сне —
    /// так и лежит без брони: она осталась в оружейной.
    ///
    /// Нашли утром мёртвого — неделю город ходит облавой: из стражи спит лишь каждый третий,
    /// остальные ходят по улицам. Кто идёт ночью по городу, того останавливают: героя — на
    /// допрос, не чаще раза за ночь, горожанина — на несколько слов. Стражу не спрашивают.
    /// </summary>
    internal static class Nightwatch
    {
        internal static ConfigEntry<bool> Undressed;
        internal static ConfigEntry<float> DressDelay;
        internal static ConfigEntry<int> RaidNights;
        internal static ConfigEntry<float> RaidOffDuty;
        internal static ConfigEntry<float> RaidReach;

        internal static void Bind(ConfigFile config)
        {
            Undressed = config.Bind("Nightwatch", "Undressed", true,
                "Let a guard take off his helmet, armour and greaves when he goes to bed, and put them "
                + "back on when he gets up.");

            DressDelay = config.Bind("Nightwatch", "DressDelay", 20f,
                new ConfigDescription("How many seconds a guard needs to put his armour back on.",
                    new AcceptableValueRange<float>(0f, 300f)));

            RaidNights = config.Bind("Nightwatch", "RaidNights", 7,
                new ConfigDescription("For how many nights after a body is found the town walks a raid.",
                    new AcceptableValueRange<int>(0, 60)));

            RaidOffDuty = config.Bind("Nightwatch", "RaidOffDuty", 0.3f,
                new ConfigDescription("The share of guards who still sleep during a raid.",
                    new AcceptableValueRange<float>(0f, 1f)));

            RaidReach = config.Bind("Nightwatch", "RaidReach", 10f,
                new ConfigDescription("How close, in metres, a guard on a raid stops whoever walks at night.",
                    new AcceptableValueRange<float>(1f, 40f)));
        }

        // ------------------------------------------------------------------ броня во сне

        private static readonly int[] armour = { 2, 4, 7 };

        private class Kit
        {
            internal HumaniodUnit man;
            internal readonly Inventory[] items = new Inventory[10];
            internal float dressAt = -1f;
        }

        private static readonly Dictionary<UnitAttribute, Kit> kits = new Dictionary<UnitAttribute, Kit>();
        internal static bool Muted;

        internal static void Undress(UnitAttribute u)
        {
            if (Undressed == null || !Undressed.Value || u == null || u.inParty || !Crime.Guard(u)) return;

            Kit kit;
            if (kits.TryGetValue(u, out kit))
            {
                kit.dressAt = -1f;
                return;
            }

            HumaniodUnit man = u as HumaniodUnit;
            EquipmentManager gear = man != null ? man.equipmentmanger : null;
            if (gear == null || !gear.isInited || gear.equipInfos == null) return;

            kit = new Kit { man = man };
            bool any = false;

            Muted = true;
            try
            {
                foreach (int s in armour)
                {
                    if (s >= gear.equipInfos.Length) continue;
                    EquipInfo slot = gear.equipInfos[s];
                    if (slot == null || !slot.IsEquiped()) continue;

                    kit.items[s] = slot.inventory;
                    gear.UnequipItem(s);
                    any = true;
                }
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Стража: броня не снялась: " + e.Message);
            }
            finally
            {
                Muted = false;
            }

            if (any) kits[u] = kit;
        }

        internal static void Rose(UnitAttribute u)
        {
            Kit kit;
            if (u != null && kits.TryGetValue(u, out kit) && kit.dressAt < 0f) kit.dressAt = Time.time + DressDelay.Value;
        }

        private static void Dress(Kit kit)
        {
            EquipmentManager gear = kit.man.equipmentmanger;
            if (gear == null || gear.equipInfos == null) return;

            Muted = true;
            try
            {
                foreach (int s in armour)
                {
                    Inventory item = kit.items[s];
                    if (item == null || s >= gear.equipInfos.Length) continue;
                    if (gear.equipInfos[s] != null && gear.equipInfos[s].IsEquiped()) continue;
                    gear.EquipItem(item, s);
                }
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Стража: броня не наделась: " + e.Message);
            }
            finally
            {
                Muted = false;
            }
        }

        /// <summary>What he took off: written as worn, so the save never loses it.</summary>
        internal static void Worn(UnitAttribute u, NPCSaveData data)
        {
            Kit kit;
            if (u == null || data == null || data.equips == null || !kits.TryGetValue(u, out kit)) return;

            foreach (int s in armour)
            {
                if (s < data.equips.Length && data.equips[s] == null && kit.items[s] != null) data.equips[s] = kit.items[s];
            }
        }

        /// <summary>Killed asleep: he lies as he slept, the armour stays in the armoury.</summary>
        internal static void Fell(UnitAttribute u)
        {
            if (u != null) kits.Remove(u);
        }

        private static readonly List<UnitAttribute> dressing = new List<UnitAttribute>();

        private static void Kits()
        {
            if (kits.Count == 0) return;

            dressing.Clear();
            foreach (KeyValuePair<UnitAttribute, Kit> one in kits)
            {
                Kit kit = one.Value;
                if (one.Key == null || kit.man == null || kit.man.Data == null || kit.man.Data.isdead)
                {
                    dressing.Add(one.Key);
                    continue;
                }

                if (kit.dressAt < 0f) continue;
                if (kit.man.isSleeping) { kit.dressAt = -1f; continue; }
                if (Time.time < kit.dressAt || kit.man.isEngaged) continue;

                Dress(kit);
                dressing.Add(one.Key);
            }

            foreach (UnitAttribute u in dressing) kits.Remove(u);
        }

        // ------------------------------------------------------------------ облава

        internal static bool Raid(Faction team)
        {
            return RaidNights != null && RaidNights.Value > 0 && Souls.RaidTonight(team, RaidNights.Value);
        }

        internal static float OffDutyTonight(Faction team)
        {
            return Raid(team) ? RaidOffDuty.Value : Crime.OffDuty.Value;
        }

        private static bool Night()
        {
            try
            {
                int hour = TimeManager.Hour;
                return hour >= 21 || hour < 6;
            }
            catch
            {
                return false;
            }
        }

        private class Stop
        {
            internal UnitAttribute guard;
            internal UnitAttribute who;
            internal float at;
            internal bool answered;
        }

        private static readonly List<Stop> stops = new List<Stop>();
        private static readonly Dictionary<int, int> asked = new Dictionary<int, int>();

        private static float nextWalk;
        private static float nextLook;

        internal static void Tick()
        {
            try { Kits(); } catch { }
            try { Stops(); } catch { }

            if (!Night()) return;
            CityTownManager town = CityTownManager.instance;
            if (town == null || town.barrack == null || town.barrack.guards == null) return;

            Faction team = town.faction;
            if (!Raid(team)) return;

            float now = Time.time;

            if (now >= nextWalk)
            {
                nextWalk = now + 20f;
                try { Walk(town.barrack.guards); } catch { }
            }

            if (now >= nextLook)
            {
                nextLook = now + 0.5f;
                try { Look(town.barrack.guards, team); } catch { }
            }
        }

        private static bool Free(UnitAttribute g)
        {
            if (g == null || g.Data == null || g.Data.isdead || g.isSleeping || g.isEngaged || g.inParty) return false;
            if (g.stateMachine == null || g.stateMachine.unitcstate == BehaviorState.stopCrime) return false;

            foreach (Stop s in stops)
            {
                if (ReferenceEquals(s.guard, g)) return false;
            }
            return true;
        }

        /// <summary>Those who are up walk the streets instead of standing at their posts.</summary>
        private static void Walk(List<UnitAttribute> guards)
        {
            if (AreaManager.Instance == null) return;

            foreach (UnitAttribute g in guards)
            {
                if (!Free(g) || g.aiController == null) continue;

                AIBehaviorBase now = g.aiController.CurrentBehavior;
                if (now != null && (now.type == UncombatAIType.patrol || now.type == UncombatAIType.rest)) continue;

                Street street = AreaManager.Instance.FindRandomStreet();
                if (street == null) continue;

                AIBehaviorBase walk = new AIBehaviorBase
                {
                    type = UncombatAIType.patrol,
                    patrolStreet = street,
                    canLoop = true,
                    isTemp = true,
                    during = new RandomValue(120f, 240f),
                };

                try { g.aiController.ChangeBehavior(walk, true); } catch { }
            }
        }

        private static void Look(List<UnitAttribute> guards, Faction team)
        {
            HumaniodUnit you = Crime.Leader();
            int tonight = Crime.Night();
            float reach = RaidReach.Value;

            foreach (UnitAttribute g in guards)
            {
                if (!Free(g)) continue;
                Vector3 at = g.transform.position;

                // Герой на ночной улице — на допрос, раз за ночь.
                if (you != null && !you.Data.isdead && !you.isEngaged && Search.CanStopAtNight(team)
                    && Vector3.Distance(at, you.transform.position) < reach && g.InSenseRange(you))
                {
                    Search.OpenNight(g);
                    return;
                }

                foreach (UnitAttribute u in UnitAttribute.GetUnitsInRangeNoCollider(g, reach, at, TargetAllow.all))
                {
                    if (u == null || ReferenceEquals(u, g) || u.Data == null || u.Data.isdead) continue;
                    if (!(u is HumaniodUnit) || u.inParty || u.isSleeping || u.isEngaged || Crime.Guard(u) || Kids.Is(u)) continue;
                    if (u.Data.team == Faction.player || !u.isMoving || u.stateMachine == null) continue;

                    int night;
                    if (asked.TryGetValue(u.Data.id, out night) && night == tonight) continue;
                    if (!g.InSenseRange(u)) continue;

                    asked[u.Data.id] = tonight;
                    Question(g, u);
                    break;
                }
            }

            if (asked.Count > 400) asked.Clear();
        }

        private static void Question(UnitAttribute guard, UnitAttribute who)
        {
            try
            {
                who.stateMachine.HandleStop();
                who.stateMachine.HandleCommand(new UnitCommand(commandsName.talk, guard.gameObject, "", 7));
                guard.stateMachine.HandleCommand(new UnitCommand(commandsName.chat, who.gameObject, "", 7));
            }
            catch
            {
            }

            Crime.Bark(guard, "Стой! Куда в такой час?");
            stops.Add(new Stop { guard = guard, who = who, at = Time.time });
        }

        private static readonly string[] answers =
        {
            "Домой, господин стражник, домой…",
            "К лекарю, жена занемогла.",
            "Из таверны я, из таверны. Иду уже.",
            "Не спится… Уже ухожу.",
        };

        private static void Stops()
        {
            for (int i = stops.Count - 1; i >= 0; i--)
            {
                Stop s = stops[i];
                bool gone = s.guard == null || s.who == null || s.guard.Data == null || s.who.Data == null
                    || s.guard.Data.isdead || s.who.Data.isdead;

                if (!gone && !s.answered && Time.time >= s.at + 2.5f)
                {
                    s.answered = true;
                    Crime.Bark(s.who, answers[Mathf.Abs(s.who.Data.id) % answers.Length]);
                }

                if (!gone && Time.time < s.at + 7f) continue;

                stops.RemoveAt(i);
                if (gone) continue;

                try { if (!s.who.isEngaged) s.who.stateMachine.HandleStop(); } catch { }
                try { if (!s.guard.isEngaged) s.guard.stateMachine.HandleStop(); } catch { }
            }
        }
    }

    // Снятая на ночь броня пишется в сохранение как надетая.
    [HarmonyPatch(typeof(NPCSaveData), "SaveStatus")]
    internal static class SaveStatus_Nightwatch_Patch
    {
        private static void Postfix(NPCSaveData __instance, UnitAttribute unit)
        {
            try { Nightwatch.Worn(unit, __instance); } catch { }
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "Die")]
    internal static class Die_Nightwatch_Patch
    {
        private static void Prefix(UnitAttribute __instance)
        {
            try { Nightwatch.Fell(__instance); } catch { }
        }
    }

    // Переодевание во сне — без звона на всю улицу.
    [HarmonyPatch(typeof(UnitAudioManager), "OnUnequipItem")]
    internal static class UnequipSound_Nightwatch_Patch
    {
        private static bool Prefix() { return !Nightwatch.Muted; }
    }

    [HarmonyPatch(typeof(UnitAudioManager), "OnEquipItem")]
    internal static class EquipSound_Nightwatch_Patch
    {
        private static bool Prefix() { return !Nightwatch.Muted; }
    }
}
