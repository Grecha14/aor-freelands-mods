using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using PixelCrushers.DialogueSystem;
using spell;
using UnityEngine;

namespace DemonLook
{
    /// <summary>
    /// A town that keeps watch at night: who wakes, who is called, and what a caught thief pays.
    ///
    /// Кражу, взлом и чужой порог, которых никто не видел, город не замечает вовсе: счёт
    /// преступлений от них не растёт. Кого видели — того сразу ведут к ответу: одного такого
    /// раза довольно, чтобы стража пришла с допросом, штрафом и тюрьмой; за вторжение в дом —
    /// вдвое против игрового. Сидят по дню за каждые десять очков преступления.
    ///
    /// Кто видел или проснулся, тот кричит; ближний стражник идёт на место, а горожане бегут к
    /// нему. Спящего будит шум шагов и свет рядом с кроватью — стражника вдвое быстрее.
    ///
    /// Ночью ложатся все, кроме стражи на посту: половина стражи по своему жребию на эту ночь
    /// спит у себя дома, и квестовые люди — тоже, если только они не дают больших поручений:
    /// повышений у гладиаторов, наёмников, купцов. Лавочники живут по игре. Кому лечь негде,
    /// тот не ложится посреди улицы, а бродит.
    ///
    /// Сундуки в чужих домах теперь полнее: вещей вдвое, золота втрое.
    ///
    /// Всё это — при любом герое. Демону сверх того остаётся своё: утренние тела, подозрение
    /// и обыск.
    /// </summary>
    internal static class Crime
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Trespass;
        internal static ConfigEntry<int> JailPer;
        internal static ConfigEntry<int> Loot;
        internal static ConfigEntry<int> Gold;
        internal static ConfigEntry<float> WakeRate;
        internal static ConfigEntry<float> WakeFloor;
        internal static ConfigEntry<float> GuardWake;
        internal static ConfigEntry<float> OffDuty;
        internal static ConfigEntry<bool> QuestSleep;
        internal static ConfigEntry<bool> Locked;
        internal static ConfigEntry<float> LockNoise;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Crime", "Enabled", true,
                "Let the towns keep watch: unseen crimes go unnoticed, seen ones bring the guard at "
                + "once, sleepers wake to steps and light, and houses hold more.");

            Trespass = config.Bind("Crime", "Trespass", 2f,
                new ConfigDescription("How many times the game crime for being seen in a house.",
                    new AcceptableValueRange<float>(1f, 10f)));

            JailPer = config.Bind("Crime", "JailPer", 10,
                new ConfigDescription("Points of crime for one day in jail; at least one day.",
                    new AcceptableValueRange<int>(1, 100)));

            Loot = config.Bind("Crime", "Loot", 2,
                new ConfigDescription("How many times more things a chest in somebody house holds.",
                    new AcceptableValueRange<int>(1, 10)));

            Gold = config.Bind("Crime", "Gold", 3,
                new ConfigDescription("How many times more gold a chest in somebody house holds.",
                    new AcceptableValueRange<int>(1, 20)));

            WakeRate = config.Bind("Crime", "WakeRate", 60f,
                new ConfigDescription("How fast steps right by the bed wake a sleeper, per second, against "
                    + "a hundred to wake.",
                    new AcceptableValueRange<float>(0f, 1000f)));

            WakeFloor = config.Bind("Crime", "WakeFloor", 20f,
                new ConfigDescription("How fast steps at the very edge of hearing wake him, per second.",
                    new AcceptableValueRange<float>(0f, 1000f)));

            GuardWake = config.Bind("Crime", "GuardWake", 2f,
                new ConfigDescription("How many times faster a sleeping guard wakes.",
                    new AcceptableValueRange<float>(1f, 10f)));

            OffDuty = config.Bind("Crime", "OffDuty", 0.5f,
                new ConfigDescription("The share of town guards who sleep at home at night.",
                    new AcceptableValueRange<float>(0f, 1f)));

            QuestSleep = config.Bind("Crime", "QuestSleep", true,
                "Let the people who give quests sleep at night like everyone, except those who give "
                + "the great errands: the promotions of gladiators, mercenaries and merchants.");

            Locked = config.Bind("Crime", "Locked", true,
                "Keep the houses of everyone but quest people locked by day as well as by night. "
                + "The owners pass their own doors as always.");

            LockNoise = config.Bind("Crime", "LockNoise", 3f,
                new ConfigDescription("How many times louder a thief is while picking a lock.",
                    new AcceptableValueRange<float>(1f, 20f)));
        }

        /// <summary>The one the party is played through: whoever he is, the town watches him.</summary>
        internal static HumaniodUnit Leader()
        {
            HumaniodUnit you = gameManager.currentplayUnit;
            if (you != null) return you;

            PartyManager party = PartyManager.instance;
            return party != null ? party.leader : null;
        }

        internal static bool Game()
        {
            return Enabled != null && Enabled.Value && Leader() != null;
        }

        // ------------------------------------------------------------------ счёт преступлений

        private static bool raw;
        internal static bool Raw => raw;

        /// <summary>The game own figure for this crime, untouched by the rules here.</summary>
        internal static int RawFactor(CrimeDataType type)
        {
            raw = true;
            try { return UICrimeDatabase.Instance != null ? UICrimeDatabase.Instance.GetCrimeValueFactor(type) : 0; }
            catch { return 0; }
            finally { raw = false; }
        }

        internal static int seenFrame = -1;
        internal static bool ours;

        internal static bool Guard(UnitAttribute u)
        {
            NPCSaveData npc = u != null ? u.Data as NPCSaveData : null;
            return npc != null && npc.career == CareerType.Guard;
        }

        private static bool Awake(UnitAttribute u)
        {
            return u != null && u.Data != null && !u.Data.isdead && !u.isSleeping && !u.Data.isPaused
                && !u.inParty && u.Data.team != Faction.player && u is HumaniodUnit;
        }

        /// <summary>A seen crime: the nearest guard comes to the spot, and the townsfolk run to him.</summary>
        internal static void Alarm(Faction team)
        {
            HumaniodUnit demon = Leader();
            if (demon == null) return;

            try
            {
                Vector3 at = demon.transform.position;
                UnitAttribute guard = null;
                float best = float.MaxValue;

                foreach (UnitAttribute u in UnitAttribute.GetUnitsInRangeNoCollider(demon, 60f, at, TargetAllow.all))
                {
                    if (!Awake(u) || !Guard(u) || u.Data.team != team || u.isEngaged) continue;
                    float d = (u.transform.position - at).sqrMagnitude;
                    if (d < best) { best = d; guard = u; }
                }

                if (guard != null && guard.stateMachine != null)
                    guard.stateMachine.HandleCommand(new UnitCommand(commandsName.move, at));

                bool shouted = false;
                foreach (UnitAttribute u in UnitAttribute.GetUnitsInRangeNoCollider(demon, 15f, at, TargetAllow.all))
                {
                    if (!Awake(u) || Guard(u) || u.Data.team != team || u.stateMachine == null) continue;

                    if (!shouted)
                    {
                        shouted = true;
                        Bark(u, "Стража! Сюда!");
                    }

                    if (guard != null) u.stateMachine.HandleCommand(new UnitCommand(commandsName.move, guard.transform.position));
                }
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Тревога не поднялась: " + e.Message);
            }
        }

        internal static void Bark(UnitAttribute u, string said)
        {
            try
            {
                HumaniodUnit man = u as HumaniodUnit;
                if (man != null) man.StartBark(said, 3f);
                else if (u.lifebar != null) u.lifebar.ShowTextTag(said, 3f);
            }
            catch
            {
            }
        }

        // ------------------------------------------------------------------ сон

        private static readonly Dictionary<int, float> unrest = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> awakeUntil = new Dictionary<int, float>();

        internal static bool KeptAwake(UnitAttribute u)
        {
            float until;
            return u != null && awakeUntil.TryGetValue(u.GetInstanceID(), out until) && Time.time < until;
        }

        internal static int Night()
        {
            return TimeManager.TotalDay - (TimeManager.Hour < 12 ? 1 : 0);
        }

        /// <summary>A lot drawn for this one for this night, from nought to one.</summary>
        internal static float Lot(int id, int salt)
        {
            unchecked
            {
                int h = id * 73856093 ^ Night() * 19349663 ^ salt * 83492791;
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return ((h & 0x7fffffff) % 10000) / 10000f;
            }
        }

        private static readonly List<Light> lamps = new List<Light>();
        private static float surveyed = -100f;

        private static bool Lit(UnitAttribute u)
        {
            float now = Time.realtimeSinceStartup;
            if (now - surveyed > 8f || now < surveyed)
            {
                surveyed = now;
                lamps.Clear();
                try
                {
                    foreach (Light one in UnityEngine.Object.FindObjectsOfType<Light>())
                    {
                        if (one == null || !one.enabled || one.type == LightType.Directional) continue;
                        if (one.intensity <= 0f || one.range < 0.5f || one.range > 25f) continue;
                        lamps.Add(one);
                    }
                }
                catch
                {
                }
            }

            Vector3 at = u.transform.position + Vector3.up;
            foreach (Light one in lamps)
            {
                if (one == null || !one.enabled) continue;
                if ((one.transform.position - at).sqrMagnitude < one.range * one.range * 0.5f) return true;
            }
            return false;
        }

        private static bool InsideHome(UnitAttribute sleeper)
        {
            try
            {
                AIBehaviorController ai = sleeper.aiController as AIBehaviorController;
                BuildingController home = ai != null ? ai.home : null;
                return home != null && home.isPrivateBuilding && home.playerInside;
            }
            catch
            {
                return false;
            }
        }

        private static float nextWake;
        private static float nextSearch;

        internal static void Tick()
        {
            if (!Game()) return;
            if ((bool)WorldTravelManager.instance) return;

            float now = Time.time;

            if (now >= nextWake)
            {
                float dt = now - nextWake + 0.25f;
                nextWake = now + 0.25f;
                try { Wake(Mathf.Clamp(dt, 0.05f, 1f)); } catch { }
            }

            if (now >= nextSearch)
            {
                nextSearch = now + 1f;
                if (Souls.Demon() != null)
                {
                    try { Search.Look(); } catch { }
                }
            }

            try { Locks(); } catch { }
            try { Theft.Tick(); } catch { }
            try { Nightwatch.Tick(); } catch { }
        }

        private static void Wake(float dt)
        {
            HumaniodUnit demon = Leader();
            if (demon == null || demon.Data == null || demon.Data.isdead) return;

            Vector3 at = demon.transform.position;
            bool lit = Lit(demon);

            foreach (UnitAttribute u in UnitAttribute.GetUnitsInRangeNoCollider(demon, 12f, at, TargetAllow.all))
            {
                if (u == null || u.Data == null || !u.isSleeping || u.Data.isPaused || u.Data.isdead) continue;
                if (u.inParty || u.Data.team == Faction.player || !(u is HumaniodUnit)) continue;

                int key = u.GetInstanceID();
                float d = Vector3.Distance(u.transform.position, at);
                float reach = u.hearingRange * demon.HearingAvoidanceMD;

                float rate = 0f;
                if (d < reach) rate = WakeFloor.Value + WakeRate.Value * (1f - d / reach);
                if (lit && d < 6f) rate = Mathf.Max(rate, WakeFloor.Value) * 2f;
                if (Guard(u)) rate *= GuardWake.Value;

                float have;
                unrest.TryGetValue(key, out have);
                have = rate > 0f ? have + rate * dt : Mathf.Max(0f, have - 20f * dt);

                if (have < 100f)
                {
                    if (have > 0f) unrest[key] = have;
                    else unrest.Remove(key);
                    continue;
                }

                unrest.Remove(key);
                awakeUntil[key] = Time.time + 150f;

                try { if (u.stateMachine != null) u.stateMachine.HandleStop(); } catch { }
                Bark(u, Guard(u) ? "Кто здесь?! Стоять!" : "Кто здесь?!");

                // Проснулся у себя дома и видит чужого — это уже вторжение, и его видели.
                if (InsideHome(u))
                {
                    try { FactionManager.Instance.AddCrimeToPlayer(u.Data.team, UICrimeDatabase.Instance.GetCrimeValueFactor(CrimeDataType.Occupy)); }
                    catch { }
                }
            }

            if (unrest.Count > 200) unrest.Clear();
            if (awakeUntil.Count > 200) awakeUntil.Clear();
        }

        // ------------------------------------------------------------------ замки

        /// <summary>Whether he is picking a lock right now.</summary>
        internal static bool Picking(UnitAttribute u)
        {
            try
            {
                LockedInteractable l = u != null ? u.interactObject as LockedInteractable : null;
                return l != null && l.interacting && l.isLocked;
            }
            catch
            {
                return false;
            }
        }

        private static readonly Dictionary<int, bool> lockedHouse = new Dictionary<int, bool>();

        /// <summary>A private house with owners, none of whom gives quests: kept locked.</summary>
        internal static bool KeptLocked(BuildingController house)
        {
            if (house == null || !house.isPrivateBuilding || house.owners == null || house.owners.Count == 0) return false;

            int key = house.GetInstanceID();
            bool keep;
            if (lockedHouse.TryGetValue(key, out keep)) return keep;

            keep = true;
            foreach (UnitAttribute owner in house.owners)
            {
                if (owner != null && QuestPerson(owner)) { keep = false; break; }
            }

            lockedHouse[key] = keep;
            return keep;
        }

        private static float nextLocks;
        private static float nextHouses;
        private static float nextPick;
        private static BuildingController[] houses = new BuildingController[0];
        private static readonly Dictionary<int, Faction> jailNow = new Dictionary<int, Faction>();

        internal static bool JailNow(Faction team)
        {
            return jailNow.ContainsKey((int)team);
        }

        internal static void Jailed(Faction team)
        {
            jailNow.Remove((int)team);
        }

        /// <summary>Keeps the houses locked, and watches the thief at a lock.</summary>
        private static void Locks()
        {
            HumaniodUnit demon = Leader();
            if (demon == null) return;

            if (Locked.Value && Time.time >= nextLocks)
            {
                nextLocks = Time.time + 5f;
                try
                {
                    if (Time.time >= nextHouses || houses.Length == 0)
                    {
                        nextHouses = Time.time + 30f;
                        houses = UnityEngine.Object.FindObjectsOfType<BuildingController>();
                        lockedHouse.Clear();
                    }

                    foreach (BuildingController house in houses)
                    {
                        if (house == null || house.playerInside || !KeptLocked(house) || house.mainDoors == null) continue;
                        foreach (DoorController door in house.mainDoors)
                        {
                            if (door == null || door.interacting) continue;
                            if (!door.isLocked)
                            {
                                if (door.isOpen) door.CloseDoor();
                                door.isLocked = true;
                            }

                            // Жилец прошёл — дверь закрылась запертой и вырезала проход из
                            // навигации: вернуть его, чужих остановит сама дверь.
                            Homes.Uncarve(door);
                        }
                    }
                }
                catch
                {
                }
            }

            // Кто увидел демона за взломом — видел попытку: это уже преступление при свидетелях.
            if (!Picking(demon) || Time.time < nextPick) return;
            nextPick = Time.time + 0.25f;

            LockedInteractable target = demon.interactObject as LockedInteractable;
            Faction team = Faction.none;
            try
            {
                if (target != null && target.owners != null)
                {
                    foreach (UnitAttribute owner in target.owners)
                    {
                        if (owner != null && owner.Data != null) { team = owner.Data.team; break; }
                    }
                }
            }
            catch
            {
            }

            foreach (UnitAttribute u in UnitAttribute.GetUnitsInRangeNoCollider(demon, 25f, demon.transform.position, TargetAllow.all))
            {
                if (!Awake(u)) continue;
                if (team != Faction.none && u.Data.team != team && !Guard(u)) continue;
                if (!u.InSenseRange(demon)) continue;

                Faction whose = team != Faction.none ? team : u.Data.team;
                bool town = false;
                try { town = whose.FactionHasCrime(); } catch { }
                if (!town) continue;

                try { if (demon.stateMachine != null) demon.stateMachine.HandleStop(); } catch { }
                Bark(u, Guard(u) ? "Стоять! Взлом!" : "Вор! Он ломает замок!");

                jailNow[(int)whose] = whose;
                try { FactionManager.Instance.AddCrimeToPlayer(whose, UICrimeDatabase.Instance.GetCrimeValueFactor(CrimeDataType.PickLock)); } catch { }

                Souls.Say("Вас увидели за взломом. Стража уже идёт — за попытку взлома полагается тюрьма.");
                return;
            }
        }

        // ------------------------------------------------------------------ кто ложится

        private static readonly Dictionary<string, bool> greatTitles = new Dictionary<string, bool>();

        /// <summary>Whether this one gives the great errands: the rank of gladiator, mercenary or merchant.</summary>
        internal static bool GreatGiver(UnitAttribute u)
        {
            try
            {
                UnitDialogueManager talk = u.GetComponent<UnitDialogueManager>();
                if (talk == null || talk.dialogueNames == null || DialogueManager.instance == null) return false;

                DialogueDatabase book = DialogueManager.instance.masterDatabase;
                if (book == null) return false;

                foreach (string title in talk.dialogueNames)
                {
                    if (string.IsNullOrEmpty(title)) continue;

                    bool great;
                    if (!greatTitles.TryGetValue(title, out great))
                    {
                        great = false;
                        Conversation conv = book.GetConversation(title);
                        if (conv != null && conv.dialogueEntries != null)
                        {
                            foreach (DialogueEntry step in conv.dialogueEntries)
                            {
                                if (step == null) continue;
                                string seq = ((step.Sequence ?? "") + (step.userScript ?? "")).Replace(" ", "");
                                if (seq.IndexOf("Gladiator(Promote", StringComparison.OrdinalIgnoreCase) >= 0
                                    || seq.IndexOf("Mercenary(Promote", StringComparison.OrdinalIgnoreCase) >= 0
                                    || seq.IndexOf("Merchant(Promote", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    great = true;
                                    break;
                                }
                            }
                        }
                        greatTitles[title] = great;
                    }

                    if (great) return true;
                }
            }
            catch
            {
            }

            return false;
        }

        internal static bool Shopkeeper(UnitAttribute u)
        {
            if (Shutters.Enabled == null || !Shutters.Enabled.Value) return false;
            NPCSaveData npc = u != null ? u.Data as NPCSaveData : null;
            if (npc == null || npc.heroCareer != null) return false;
            return npc.career == CareerType.Merchant || npc.career == CareerType.Blacksmith || npc.career == CareerType.Doctor;
        }

        internal static bool QuestPerson(UnitAttribute u)
        {
            NPCSaveData npc = u.Data as NPCSaveData;
            if (npc != null)
            {
                CareerType c = npc.career;
                if (c == CareerType.Guard || c == CareerType.Merchant || c == CareerType.Blacksmith
                    || c == CareerType.Doctor || c == CareerType.Bartender) return false;
            }

            UnitDialogueManager talk = u.GetComponent<UnitDialogueManager>();
            return talk != null && talk.dialogueNames != null && talk.dialogueNames.Length > 0;
        }

        private static bool HasBed(AIBehaviorControllerBase ai)
        {
            AIBehaviorController full = ai as AIBehaviorController;
            return full != null && full.home != null && full.home.beds != null && full.home.beds.Length > 0;
        }

        // Своя постель у стражника — в своём доме или в казарме.
        private static bool GuardHome(AIBehaviorControllerBase ai)
        {
            try
            {
                AIBehaviorController full = ai as AIBehaviorController;
                Barrack barrack = CityTownManager.instance != null ? CityTownManager.instance.barrack : null;
                return full != null && full.home != null
                    && (full.home.isPrivateBuilding || (barrack != null && full.home == barrack.buildingController));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Who lies down tonight and who stays up: guards off duty, quest people, walkers, the woken.</summary>
        internal static void Rest(AIBehaviorControllerBase ai, ref RestStyle result)
        {
            UnitAttribute u = ai.GetComponent<UnitAttribute>();
            if (u == null || u.Data == null || u.inParty || u.Data.team == Faction.player) return;
            if (!(u is HumaniodUnit) || u.isCaged) return;

            if (KeptAwake(u) || Tavern.Visiting(u))
            {
                if (result != RestStyle.none) result = RestStyle.none;
                return;
            }

            int hour = TimeManager.Hour;
            bool night = hour < ai.wakeTime || hour >= ai.sleepTime;
            bool guard = Guard(u);

            // Дети ночью спят дома, без исключений.
            if (Kids.Is(u))
            {
                result = night && Sleep.On() && Sleep.CanSleep(ai) ? RestStyle.sleep : RestStyle.none;
                return;
            }

            // Стража ночью: кому выпал жребий — спит у себя, остальные на посту. Решает жребий,
            // а не раздача постов игры, которая к ночи оставляет без дела кого попало.
            if (guard && night)
            {
                result = GuardHome(ai) && HasBed(ai) && Sleep.CanSleep(ai) && Lot(u.Data.id, 1) < Nightwatch.OffDutyTonight(u.Data.team)
                    ? RestStyle.sleep : RestStyle.none;
                return;
            }

            // Отстоял ночь в карауле — отсыпается утром у себя, до двух часов пополудни. Кто стоял,
            // решает тот же ночной жребий, так что это видно и тому, кто пришёл в город только утром.
            if (guard && !night && hour < 12 && Weariness.On() && Sleep.On() && GuardHome(ai) && HasBed(ai) && Sleep.CanSleep(ai)
                && Lot(u.Data.id, 1) >= Nightwatch.OffDutyTonight(u.Data.team))
            {
                result = RestStyle.sleep;
                Sleep.ByDay(u, TimeManager.TotalDay * 24f + ai.wakeTime + 8f);
                return;
            }

            if (result == RestStyle.none && night && !ai.canRest && HasBed(ai) && !guard
                && QuestSleep.Value && QuestPerson(u) && !GreatGiver(u))
            {
                result = RestStyle.sleep;
            }

            // Лавка ночью закрыта — лавочник, кузнец и лекарь спят дома. Кто в пути, тот идёт;
            // трактирщик стоит за стойкой.
            if (night && Shopkeeper(u) && !u.isCrossSceneUnit)
            {
                result = Sleep.On() && Sleep.CanSleep(ai) ? RestStyle.sleep : RestStyle.none;
                return;
            }

            // Две ночи без сна — ложится, где застала ночь: и бродяга, и тот, кому лечь негде.
            if (night && !guard && Weariness.On() && Sleep.On() && Sleep.Spent(u))
            {
                result = RestStyle.sleep;
                return;
            }

            if (result == RestStyle.sleep && !guard)
            {
                // Лечь негде — посреди улицы не ложится, бродит до утра.
                if (Sleep.On() && !Sleep.CanSleep(ai)) result = RestStyle.none;
                else if (Veil.Walker(u.Data.id)) result = RestStyle.none;
            }
        }
    }

    // Незамеченное не считается, замеченное — сразу к ответу.
    [HarmonyPatch(typeof(UICrimeDatabase), "GetCrimeValueFactor")]
    internal static class CrimeValue_Crime_Patch
    {
        private static void Postfix(CrimeDataType type, ref int __result)
        {
            if (Crime.Raw || !Crime.Game()) return;

            try
            {
                switch (type)
                {
                    case CrimeDataType.StealUndetected:
                    case CrimeDataType.PickLockUndetected:
                    case CrimeDataType.OccupyUndetected:
                        __result = 0;
                        break;

                    case CrimeDataType.Steal:
                    case CrimeDataType.PickLock:
                    case CrimeDataType.Occupy:
                        {
                            // Пойман впервые — только предупреждение; второй раз — счёт придёт,
                            // когда свидетель добежит до стражи.
                            if (type == CrimeDataType.Steal && Theft.Quiet())
                            {
                                __result = 0;
                                break;
                            }

                            int arrest = Crime.RawFactor(CrimeDataType.GuardArrestThreshold);
                            int much = type == CrimeDataType.Occupy ? Mathf.RoundToInt(__result * Crime.Trespass.Value) : __result;
                            __result = Mathf.Max(much, arrest);
                            Crime.seenFrame = Time.frameCount;
                            break;
                        }
                }
            }
            catch
            {
            }
        }
    }

    // Нулевое преступление — не новость: игра на него объявляет «преступления сняты». Такие
    // записи у нас на каждом шагу — незамеченная кража, предупреждение, — и молчат.
    [HarmonyPatch(typeof(FactionManager), "AddCrimeToPlayer")]
    internal static class ZeroCrime_Crime_Patch
    {
        private static bool Prefix(FactionManager __instance, Faction faction, int crimeValue)
        {
            if (crimeValue != 0 || !Crime.Game()) return true;

            try
            {
                if (!faction.FactionHasCrime()) return false;
                if (!__instance.factionCrimeToPlayer.ContainsKey(faction)) __instance.factionCrimeToPlayer[faction] = 0;
                if (!__instance.factionFavorToPlayer.ContainsKey(faction)) __instance.factionFavorToPlayer[faction] = 0;
                return false;
            }
            catch
            {
                return true;
            }
        }
    }

    // Преступление записано, и его видели: поднимается тревога.
    [HarmonyPatch(typeof(FactionManager), "AddCrimeToPlayer")]
    internal static class AddCrime_Crime_Patch
    {
        private static void Postfix(Faction faction, int crimeValue)
        {
            if (crimeValue <= 0 || Crime.seenFrame != Time.frameCount || !Crime.Game()) return;
            Crime.seenFrame = -1;
            Crime.Alarm(faction);
        }
    }

    // Тюрьма — по дню за каждые десять очков, не меньше дня.
    [HarmonyPatch(typeof(FactionManager), "GetJailTime")]
    internal static class JailTime_Crime_Patch
    {
        private static void Postfix(FactionManager __instance, Faction faction, ref int __result)
        {
            if (!Crime.Game()) return;

            try
            {
                int crime;
                if (!__instance.factionCrimeToPlayer.TryGetValue(faction, out crime)) return;
                int days = Mathf.Max(1, Mathf.CeilToInt(crime / (float)Mathf.Max(1, Crime.JailPer.Value)));
                __result = days * 24;
            }
            catch
            {
            }
        }
    }

    // Сундуки в чужих домах полнее.
    [HarmonyPatch(typeof(ContainerContent), "AddContentToContainer")]
    internal static class ContainerContent_Crime_Patch
    {
        private static void Postfix(ContainerContent __instance, Container container)
        {
            if (container == null || container.isPlayerStorage || !Crime.Game()) return;

            try
            {
                bool owned = (container.owners != null && container.owners.Count > 0)
                    || (container.ownerIds != null && container.ownerIds.Count > 0);
                if (!owned) return;

                for (int i = 1; i < Crime.Loot.Value; i++) __instance.AddContentToStock(container.items);

                int extra = Crime.Gold.Value - Mathf.Max(1, Crime.Loot.Value);
                for (int i = 0; i < extra; i++) container.items.money += (int)__instance.moneyRegain.Value;
            }
            catch
            {
            }
        }
    }

    // Дома, запертые навсегда, днём не отпираются: игра отпирает дверь как обычно — и снимает
    // с прохода препятствие, чтобы хозяева вышли, — а замок тут же возвращается на место.
    // Отпереть по-настоящему может только тот, кто взломал.
    [HarmonyPatch(typeof(DoorController), "Unlock")]
    internal static class DoorUnlock_Crime_Patch
    {
        private static void Postfix(DoorController __instance)
        {
            if (Crime.Locked == null || !Crime.Locked.Value || !Crime.Game()) return;

            try
            {
                HumaniodUnit demon = Crime.Leader();
                if (demon != null && (object)demon.interactObject == (object)__instance) return;

                BuildingController house = __instance.GetComponentInParent<BuildingController>();
                if (house == null || house.playerInside || !Crime.KeptLocked(house)) return;

                __instance.isLocked = true;
            }
            catch
            {
            }
        }
    }

    // Взлом шумит.
    [HarmonyPatch(typeof(UnitAttribute), "HearingAvoidanceMD", MethodType.Getter)]
    internal static class Hearing_Crime_Patch
    {
        private static void Postfix(UnitAttribute __instance, ref float __result)
        {
            if (Crime.Enabled == null || !Crime.Enabled.Value || !__instance.inParty) return;
            if (Crime.Picking(__instance)) __result *= Crime.LockNoise.Value;
        }
    }

    // Кто ложится ночью и кто бодрствует.
    [HarmonyPatch(typeof(AIBehaviorControllerBase), "CheckCanOrNeedRest")]
    internal static class CheckRest_Crime_Patch
    {
        private static void Postfix(AIBehaviorControllerBase __instance, ref RestStyle __result)
        {
            if (!Crime.Game()) return;
            try { Crime.Rest(__instance, ref __result); } catch { }
        }
    }

    // Подозрение от тел стража разбирает обыском, а не арестом.
    [HarmonyPatch(typeof(AIBehaviorController), "ToActInterrogate")]
    internal static class Interrogate_Crime_Patch
    {
        private static bool Prefix(AIBehaviorController __instance, UnitAttribute target)
        {
            if (!Crime.Game()) return true;

            try
            {
                UnitAttribute guard = __instance.GetComponent<UnitAttribute>();
                if (guard == null || guard.Data == null) return true;

                // Попался на взломе — сразу в тюрьму, без штрафа.
                if (Crime.JailNow(guard.Data.team) && CityTownManager.instance != null && CityTownManager.instance.faction == guard.Data.team)
                {
                    Crime.Jailed(guard.Data.team);
                    CityTownManager.instance.SummaryCrimes();
                    CityTownManager.instance.Jail();
                    return false;
                }

                // Только у демона и только когда весь счёт города — подозрение. За настоящее —
                // настоящий арест.
                if (Souls.Demon() == null) return true;
                int suspicion = Souls.Suspicion(guard.Data.team);
                if (suspicion <= 0) return true;

                int crime;
                if (!FactionManager.Instance.factionCrimeToPlayer.TryGetValue(guard.Data.team, out crime)) return true;
                if (crime > suspicion) return true;

                Search.Open(guard);
                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}
