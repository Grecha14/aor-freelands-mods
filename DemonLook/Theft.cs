using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using spell;
using UnityEngine;

namespace DemonLook
{
    /// <summary>
    /// What a thief can lift from a pocket, and what the one who catches him does about it.
    ///
    /// Из кармана можно вытащить деньги, кольца и амулеты — и из сумки, и надетые, — а прочее
    /// только лёгкое: вещь до полукилограмма. Тяжёлое в окне кражи просто не показывается.
    ///
    /// Поймал впервые — предупреждает: разговор игры, украденное отдаётся назад, и больше ничего;
    /// ни очков преступления, ни стражи. Эту попытку поймавший помнит неделю, и память у каждого
    /// своя. Поймал снова за эту неделю — разговора нет: кричит «Вор!» и бежит к ближайшему
    /// стражнику. Добежал — стражник идёт к вору, и дальше как при всяком увиденном
    /// преступлении. Не добежал — упал без памяти, убит — преступления нет. Стражи в этих местах
    /// нет вовсе — убегает с криком «Грабят!», и город всё равно узнаёт.
    /// </summary>
    internal static class Theft
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> LightWeight;
        internal static ConfigEntry<int> MemoryDays;
        internal static ConfigEntry<float> RunFor;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Theft", "Enabled", true,
                "Pockets give only money, rings, amulets and light things; the first one caught is "
                + "warned, the second time he is reported to the guard.");

            LightWeight = config.Bind("Theft", "LightWeight", 0.5f,
                new ConfigDescription("The heaviest thing, in kilograms, a pocket can give besides money, rings and amulets.",
                    new AcceptableValueRange<float>(0f, 100f)));

            MemoryDays = config.Bind("Theft", "MemoryDays", 7,
                new ConfigDescription("How many days the one who caught a thief remembers him.",
                    new AcceptableValueRange<int>(1, 365)));

            RunFor = config.Bind("Theft", "RunFor", 90f,
                new ConfigDescription("How many seconds a witness runs for the guard before he gives up and is heard anyway.",
                    new AcceptableValueRange<float>(10f, 600f)));
        }

        internal static bool On()
        {
            return Enabled != null && Enabled.Value && Crime.Game();
        }

        // ------------------------------------------------------------------ карманы

        private static readonly AccessTools.FieldRef<LootManager, bool> coding =
            AccessTools.FieldRefAccess<LootManager, bool>("isCodeSetting");

        private struct Worn
        {
            internal HumaniodUnit owner;
            internal int slot;
            internal Inventory item;
            internal ItemStock stock;
        }

        private static readonly List<Worn> worn = new List<Worn>();

        internal static bool Jewel(UIItemInfo info)
        {
            UIEquipmentInfo gear = info as UIEquipmentInfo;
            return gear != null && (gear.EquipType == EquipSlotType.neck || gear.EquipType == EquipSlotType.finger);
        }

        internal static bool Liftable(Inventory item)
        {
            if (item == null || item.itemInfo == null) return false;
            if (Jewel(item.itemInfo)) return true;
            return item.itemInfo.weight <= LightWeight.Value + 0.0001f;
        }

        /// <summary>Lays out the rings and the amulet he wears among what can be lifted.</summary>
        internal static void ShowWorn(ItemStock stock, UnitAttribute target)
        {
            Restore();

            HumaniodUnit man = target as HumaniodUnit;
            if (stock == null || man == null || man.equipmentmanger == null || man.equipmentmanger.equipInfos == null) return;

            EquipInfo[] slots = man.equipmentmanger.equipInfos;
            for (int i = 0; i < slots.Length; i++)
            {
                EquipInfo one = slots[i];
                if (one == null || !one.IsEquiped() || !Jewel(one.inventory.itemInfo)) continue;
                if (stock.items.Contains(one.inventory)) continue;

                stock.AddInventoryNoEvent(one.inventory);
                worn.Add(new Worn { owner = man, slot = i, item = one.inventory, stock = stock });
            }
        }

        /// <summary>Hides from the window what a pocket cannot give.</summary>
        internal static void Hide(LootManager loot)
        {
            if (loot == null || loot.slots == null) return;

            bool was = coding(loot);
            coding(loot) = true;
            try
            {
                foreach (UIItemSlot slot in loot.slots)
                {
                    if (slot == null || !slot.IsAssigned()) continue;
                    if (!Liftable(slot.inventory)) slot.ClearSlot();
                }
            }
            finally
            {
                coding(loot) = was;
            }
        }

        /// <summary>A worn thing was taken: it comes off him.</summary>
        internal static void Taken(Inventory item)
        {
            for (int i = worn.Count - 1; i >= 0; i--)
            {
                Worn w = worn[i];
                if (!ReferenceEquals(w.item, item)) continue;

                try
                {
                    if (w.owner != null && w.owner.equipmentmanger != null
                        && ReferenceEquals(w.owner.equipmentmanger.equipInfos[w.slot].inventory, item))
                        w.owner.equipmentmanger.UnequipItem(w.slot);
                }
                catch
                {
                }

                worn.RemoveAt(i);
            }
        }

        /// <summary>The window closed: what was not taken stays on him.</summary>
        internal static void Restore()
        {
            foreach (Worn w in worn)
            {
                try
                {
                    if (w.stock != null && w.stock.items != null) w.stock.items.Remove(w.item);
                }
                catch
                {
                }
            }
            worn.Clear();
        }

        // ------------------------------------------------------------------ память и поимка

        private static readonly Dictionary<int, int> warned = new Dictionary<int, int>();

        private static int decidedFrame = -1;
        private static UnitAttribute decidedFor;
        private static bool decidedWarn;
        private static int quietFrame = -1;

        /// <summary>Whether this frame the game must not count the theft it is recording.</summary>
        internal static bool Quiet()
        {
            return quietFrame == Time.frameCount;
        }

        /// <summary>Warn, or report: the first catch in a week is a warning, the second a report.</summary>
        internal static bool Warn(UnitAttribute owner)
        {
            if (owner == null || owner.Data == null) return true;
            if (decidedFrame == Time.frameCount && ReferenceEquals(decidedFor, owner)) return decidedWarn;

            Sync();
            int today = Souls.Today();
            int id = owner.Data.id;

            // Неделя считается от предупреждения: всё, что он застанет за эту неделю, — к страже.
            int day;
            bool fresh = warned.TryGetValue(id, out day) && today >= day && today - day < MemoryDays.Value;
            bool warn = !fresh;

            if (warn)
            {
                warned[id] = today;
                dirty = true;
            }

            decidedFrame = Time.frameCount;
            decidedFor = owner;
            decidedWarn = warn;
            quietFrame = Time.frameCount;
            return warn;
        }

        private class Runner
        {
            internal HumaniodUnit who;
            internal UnitAttribute guard;
            internal Faction team;
            internal float until;
            internal float next;
        }

        private static readonly List<Runner> runners = new List<Runner>();

        private static UnitAttribute NearestGuard(UnitAttribute from, Faction team)
        {
            UnitAttribute best = null;
            float far = float.MaxValue;

            foreach (UnitAttribute u in UnitAttribute.GetUnitsInRangeNoCollider(from, 400f, from.transform.position, TargetAllow.all))
            {
                if (u == null || u.Data == null || u.Data.isdead || u.isSleeping || u.inParty) continue;
                if (!Crime.Guard(u) || u.Data.team != team || u.isEngaged) continue;

                float d = (u.transform.position - from.transform.position).sqrMagnitude;
                if (d < far) { far = d; best = u; }
            }
            return best;
        }

        /// <summary>Caught a second time: off he runs for the guard, or away if there is none.</summary>
        internal static void Report(HumaniodUnit owner, UnitAttribute thief)
        {
            if (owner == null || owner.Data == null) return;

            Faction team = owner.Data.team;
            UnitAttribute guard = NearestGuard(owner, team);

            if (guard != null)
            {
                Crime.Bark(owner, "Вор! Стража!");
                runners.Add(new Runner { who = owner, guard = guard, team = team, until = Time.time + RunFor.Value });
                try { owner.stateMachine.HandleCommand(new UnitCommand(commandsName.move, guard.gameObject, 1f)); } catch { }
                Souls.Say($"«{owner.Data.unitname}» ловит вас второй раз — и бежит за стражей.");
                return;
            }

            Crime.Bark(owner, "Грабят! Помогите!");
            try
            {
                UIBuffInfo fear = UIBuffDatabase.Instance.GetByID("Fleeing");
                if (fear != null && owner.buffmanger != null)
                {
                    BuffBase run = new BuffBase(fear, thief);
                    run.duration = 15f;
                    owner.buffmanger.AddBuff(run);
                }
            }
            catch
            {
            }

            Record(team);
            Souls.Say($"«{owner.Data.unitname}» ловит вас второй раз и с криком убегает. Город узнает.");
        }

        /// <summary>The town learns of the theft: a crime at once above the arrest line.</summary>
        private static void Record(Faction team)
        {
            try
            {
                if (!team.FactionHasCrime()) return;
                int much = Mathf.Max(Crime.RawFactor(CrimeDataType.Steal), Crime.RawFactor(CrimeDataType.GuardArrestThreshold));
                FactionManager.Instance.AddCrimeToPlayer(team, much);
            }
            catch
            {
            }
        }

        private static float nextRun;

        internal static void Tick()
        {
            if (runners.Count == 0 || Time.time < nextRun) return;
            nextRun = Time.time + 0.5f;

            HumaniodUnit thief = Crime.Leader();

            for (int i = runners.Count - 1; i >= 0; i--)
            {
                Runner r = runners[i];

                // Не добежал — упал без памяти или убит: преступления нет.
                if (r.who == null || r.who.Data == null || r.who.Data.isdead || Down(r.who))
                {
                    runners.RemoveAt(i);
                    continue;
                }

                if (r.guard == null || r.guard.Data == null || r.guard.Data.isdead)
                {
                    r.guard = NearestGuard(r.who, r.team);
                    if (r.guard == null)
                    {
                        runners.RemoveAt(i);
                        Record(r.team);
                        continue;
                    }
                }

                bool there = Vector3.Distance(r.who.transform.position, r.guard.transform.position) < 3f;
                if (!there && Time.time < r.until)
                {
                    if (Time.time >= r.next)
                    {
                        r.next = Time.time + 1.5f;
                        try { r.who.stateMachine.HandleCommand(new UnitCommand(commandsName.move, r.guard.gameObject, 1f)); } catch { }
                    }
                    continue;
                }

                runners.RemoveAt(i);
                Crime.Bark(r.who, "Там! Он меня обокрал!");
                Record(r.team);

                try
                {
                    if (thief != null && r.guard.stateMachine != null)
                        r.guard.stateMachine.HandleCommand(new UnitCommand(commandsName.move, thief.transform.position));
                }
                catch
                {
                }
            }
        }

        private static bool Down(UnitAttribute u)
        {
            try
            {
                return u.buffmanger != null && (u.buffmanger.ContainBuff("Knockout") || u.buffmanger.ContainBuff("knockoutBuff"));
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------------ файл

        private static string loadedFor;
        private static bool dirty;
        private static float synced = -100f;
        private static System.Reflection.FieldInfo archiveField;

        private static string FilePath()
        {
            string archive = "";
            try
            {
                if (archiveField == null) archiveField = AccessTools.Field(typeof(SaveLoadManager), "currentArchive");
                archive = (archiveField != null && SaveLoadManager.Instance != null
                    ? archiveField.GetValue(SaveLoadManager.Instance) as string : null) ?? "";
            }
            catch
            {
            }

            foreach (char bad in Path.GetInvalidFileNameChars()) archive = archive.Replace(bad, '_');
            return Path.Combine(BepInEx.Paths.ConfigPath, "aor.crime." + archive + ".txt");
        }

        private static void Sync()
        {
            float now = Time.unscaledTime;
            if (loadedFor != null && now - synced < 5f && now >= synced) return;
            synced = now;

            string path = FilePath();
            if (!string.Equals(path, loadedFor, StringComparison.OrdinalIgnoreCase))
            {
                Load(path);
                dirty = false;
            }
        }

        private static void Load(string path)
        {
            warned.Clear();
            loadedFor = path;

            try
            {
                if (!File.Exists(path)) return;

                foreach (string line in File.ReadAllLines(path))
                {
                    int split = line.IndexOf('=');
                    if (split <= 0) continue;

                    string key = line.Substring(0, split).Trim();
                    string[] parts = line.Substring(split + 1).Trim().Split(';');
                    int a, b;
                    if (key == "warned" && parts.Length >= 2 && int.TryParse(parts[0], out a) && int.TryParse(parts[1], out b))
                        warned[a] = b;
                }
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Кража: не смог прочесть память: " + e.Message);
            }
        }

        internal static void Flush()
        {
            if (Enabled == null || !Enabled.Value) return;
            Sync();
            if (!dirty) return;

            try
            {
                int today = Souls.Today();
                List<string> lines = new List<string>();
                foreach (KeyValuePair<int, int> one in warned)
                {
                    if (today - one.Value < MemoryDays.Value) lines.Add("warned=" + one.Key + ";" + one.Value);
                }
                File.WriteAllLines(loadedFor ?? FilePath(), lines.ToArray());
                dirty = false;
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Кража: не смог записать память: " + e.Message);
            }
        }

        internal static void Reload()
        {
            if (Enabled == null || !Enabled.Value) return;
            synced = Time.unscaledTime;
            Load(FilePath());
            dirty = false;
            runners.Clear();
            Restore();
        }
    }

    // Перед тем как разложить карманы — надетые кольца и амулет к остальному.
    [HarmonyPatch(typeof(LootManager), "StartStealing")]
    internal static class StartStealing_Theft_Patch
    {
        private static void Prefix(ItemStock stock, UnitAttribute _stealTarget)
        {
            if (!Theft.On()) return;
            try { Theft.ShowWorn(stock, _stealTarget); } catch { }
        }

        private static void Postfix(LootManager __instance)
        {
            if (!Theft.On()) return;
            try { Theft.Hide(__instance); } catch { }
        }
    }

    [HarmonyPatch(typeof(LootManager), "OnLoseInventory")]
    internal static class LoseInventory_Theft_Patch
    {
        private static void Postfix(Inventory inventory)
        {
            try { Theft.Taken(inventory); } catch { }
        }
    }

    [HarmonyPatch(typeof(LootManager), "OnStealLootWindowClose")]
    internal static class StealClose_Theft_Patch
    {
        private static void Postfix()
        {
            try { Theft.Restore(); } catch { }
        }
    }

    // Пойман в чужом кармане: решить сразу, до того как игра запишет кражу.
    [HarmonyPatch(typeof(UnitAttribute), "StealingGetCaught")]
    internal static class UnitCaught_Theft_Patch
    {
        private static void Prefix(UnitAttribute __instance)
        {
            if (!Theft.On() || !__instance.inParty || __instance.Target == null) return;

            try
            {
                UnitAttribute victim = __instance.Target.GetComponent<UnitAttribute>();
                if (victim is HumaniodUnit) Theft.Warn(victim);
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(StealingState), "StealingGetCaught")]
    internal static class StateCaught_Theft_Patch
    {
        private static readonly AccessTools.FieldRef<StealingState, UnitAttribute> target =
            AccessTools.FieldRefAccess<StealingState, UnitAttribute>("targetUnit");

        private static void Prefix(StealingState __instance)
        {
            if (!Theft.On() || __instance.unit == null || !__instance.unit.inParty) return;

            try
            {
                UnitAttribute victim = target(__instance);
                if (victim is HumaniodUnit) Theft.Warn(victim);
            }
            catch
            {
            }
        }
    }

    // Хозяин застал вора: первый раз — разговор игры и назад украденное; второй — за стражей.
    [HarmonyPatch(typeof(HumaniodUnit), "ToActCaughtTheft")]
    internal static class CaughtTheft_Theft_Patch
    {
        private static bool Prefix(HumaniodUnit __instance, UnitAttribute thief)
        {
            if (!Theft.On() || thief == null || !thief.inParty) return true;

            try
            {
                if (Theft.Warn(__instance))
                {
                    Souls.Say($"«{__instance.Data.unitname}» поймал вас и требует вернуть украденное. "
                        + "Попадётесь ему ещё раз на этой неделе — побежит за стражей.");
                    return true;
                }

                Theft.Report(__instance, thief);
                return false;
            }
            catch
            {
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(SaveLoadManager), "WriteDataToSaveFile")]
    internal static class WriteSave_Theft_Patch
    {
        private static void Postfix() { try { Theft.Flush(); } catch { } }
    }

    [HarmonyPatch(typeof(TroopManagement.ArenaMatchManager), "LoadInstance")]
    internal static class LoadInstance_Theft_Patch
    {
        private static void Postfix() { try { Theft.Reload(); } catch { } }
    }
}
