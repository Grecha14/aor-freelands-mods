using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace DemonLook
{
    /// <summary>
    /// Sends the town to bed at night and gets it up in the morning.
    ///
    /// В этой сборке игры ночь не наступает ни для кого. Распорядок шлёт горожанина спать, но у
    /// состояния «иду отдыхать» свой собственный выбор — спать, дремать или встать, — и его
    /// никто не ставит: в первую же секунду горожанин «просыпается» и идёт по своим делам до
    /// утра. А кто всё же лёг, того никто и не поднимет: утренней побудки в игре нет.
    ///
    /// Здесь выбор берётся у самого распорядка, кровать ищется без падения на пустом месте, а к
    /// утру каждый встаёт в своё время в пределах часа. Кому лечь негде — ни кровати, ни дома, —
    /// посреди улицы не ложится: бродит до утра. Разве что не спал уже две ночи подряд: такой
    /// валится там, где застала ночь.
    ///
    /// Стражник, отстоявший ночь в карауле, отсыпается утром у себя до двух часов пополудни:
    /// его не будят со всеми.
    /// </summary>
    internal static class Sleep
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> WakeSpread;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Sleep", "Enabled", true,
                "Let the townsfolk really go to bed at night and get up in the morning. The game "
                + "sends them to rest and wakes them the very next second.");

            WakeSpread = config.Bind("Sleep", "WakeSpread", 60,
                new ConfigDescription("Over how many game minutes after their waking hour the sleepers get up, "
                    + "each at his own minute.",
                    new AcceptableValueRange<int>(0, 240)));
        }

        internal static bool On()
        {
            return Enabled != null && Enabled.Value;
        }

        internal static readonly AccessTools.FieldRef<ToRestState, RestStyle> StateStyle =
            AccessTools.FieldRefAccess<ToRestState, RestStyle>("restStyle");

        internal static readonly AccessTools.FieldRef<ToRestState, AIBehaviorController> StateBrain =
            AccessTools.FieldRefAccess<ToRestState, AIBehaviorController>("abc");

        internal static readonly AccessTools.FieldRef<ToRestState, Furniture> StateBed =
            AccessTools.FieldRefAccess<ToRestState, Furniture>("bed");

        internal static readonly AccessTools.FieldRef<AIBehaviorControllerBase, RestStyle> BrainStyle =
            AccessTools.FieldRefAccess<AIBehaviorControllerBase, RestStyle>("restStyle");

        /// <summary>
        /// His bed: the one given to him in his own house, or else the first free bed there.
        /// A bed he was given somewhere else — a barracks cot of the night roster, when he lives
        /// in town — is not his tonight.
        /// </summary>
        internal static Furniture PickBed(AIBehaviorController brain)
        {
            if (brain == null) return null;

            BuildingController home = brain.home;
            Furniture mine = brain.bed;
            // Кровать героя — не для горожан: она открывает герою отдых, а не укладывает спать.
            if (mine != null && mine.f_type == FunitureType.bed && !mine.isPlayerBed
                && (home == null || home.beds == null || Array.IndexOf(home.beds, mine) >= 0)) return mine;

            if (home == null || home.beds == null) return null;

            foreach (Furniture one in home.beds)
            {
                if (one != null && one.f_type == FunitureType.bed && !one.isPlayerBed && !one.interacting) return one;
            }
            return null;
        }

        /// <summary>Whether he has anywhere to lie down but the street.</summary>
        internal static bool CanSleep(AIBehaviorControllerBase ai)
        {
            AIBehaviorController full = ai as AIBehaviorController;
            if (full == null || full.home == null) return false;
            if (full.bed != null) return true;
            if (full.home.spawnPoint != null) return true;

            if (full.home.beds != null)
            {
                foreach (Furniture one in full.home.beds)
                {
                    if (one != null && one.f_type == FunitureType.bed) return true;
                }
            }
            return false;
        }

        // Кто лёг ночью по распорядку: только их и будим утром. Кто спит днём по своей роли —
        // больной в лазарете, пьяный в таверне, — того не трогаем.
        private static readonly HashSet<UnitAttribute> night = new HashSet<UnitAttribute>();

        internal static void Lay(UnitAttribute u)
        {
            if (u == null || u.inParty || u.stateMachine == null) return;

            // По распорядку — или ночью вообще: после загрузки лёгший спит уже без распорядка.
            bool routine = u.stateMachine.CState is ToRestState;
            if (!routine)
            {
                int hour = 12;
                try { hour = TimeManager.Hour; } catch { }
                AIBehaviorControllerBase ai = u.aiController;
                int wake = ai != null ? ai.wakeTime : 6;
                int sleep = ai != null ? ai.sleepTime : 21;
                if (hour < sleep && hour >= wake) return;
            }

            night.Add(u);
            if (u.Data != null)
            {
                slept[u.Data.id] = Crime.Night();
                missed.Remove(u.Data.id);
            }

            watched = Crime.Night();
            watchedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        }

        // Какую ночь и где герой видел своими глазами: кто спал, а кто нет, утром судят только за неё.
        private static int watched = -1;
        private static string watchedScene;

        internal static bool Watched(int nightIndex)
        {
            return watched == nightIndex && watchedScene == UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        }

        // Кто спал в какую ночь: утром по этому решается, устал ли он.
        private static readonly Dictionary<int, int> slept = new Dictionary<int, int>();

        internal static bool Slept(UnitAttribute u, int nightIndex)
        {
            int n;
            return u != null && u.Data != null && slept.TryGetValue(u.Data.id, out n) && n == nightIndex;
        }

        internal static void Rose(UnitAttribute u)
        {
            if (u != null) night.Remove(u);
        }

        internal static bool NightSleeper(UnitAttribute u)
        {
            return u != null && night.Contains(u);
        }

        // Сколько ночей подряд он не спал: считают по утрам, пока герой в городе и видит это сам.
        private static readonly Dictionary<int, int> missed = new Dictionary<int, int>();

        /// <summary>Counts this morning in: nought if he slept, one more if he did not. Says how many nights in a row now.</summary>
        internal static int Count(UnitAttribute u, bool rested)
        {
            if (u == null || u.Data == null) return 0;
            int n = 0;
            if (!rested)
            {
                missed.TryGetValue(u.Data.id, out n);
                n++;
            }
            missed[u.Data.id] = n;
            return n;
        }

        /// <summary>Two nights and more without sleep: he lies down wherever the night finds him.</summary>
        internal static bool Spent(UnitAttribute u)
        {
            int n;
            return u != null && u.Data != null && missed.TryGetValue(u.Data.id, out n) && n >= 2;
        }

        // Кто спит днём после ночного караула — до какого часа от начала игры его не будить.
        private static readonly Dictionary<int, float> dayUntil = new Dictionary<int, float>();

        internal static void ByDay(UnitAttribute u, float until)
        {
            if (u != null && u.Data != null) dayUntil[u.Data.id] = until;
        }

        internal static float Hours()
        {
            try { return TimeManager.TotalDay * 24f + TimeManager.Hour + TimeManager.Instance.GameTime.Minutes / 60f; }
            catch { return 0f; }
        }

        private static float next;
        private static readonly List<UnitAttribute> rising = new List<UnitAttribute>();

        internal static void Tick()
        {
            if (!On() || night.Count == 0) return;
            if ((bool)WorldTravelManager.instance) return;

            float now = Time.unscaledTime;
            if (now < next) return;
            next = now + 3f;

            int hour, minute;
            try
            {
                hour = TimeManager.Hour;
                minute = TimeManager.Instance.GameTime.Minutes;
            }
            catch
            {
                return;
            }

            rising.Clear();
            foreach (UnitAttribute u in night)
            {
                if (u == null || u.Data == null || u.Data.isdead)
                {
                    rising.Add(u);
                    continue;
                }

                // Отсыпается после ночного караула — встаёт в свой час, не со всеми.
                float until;
                if (dayUntil.TryGetValue(u.Data.id, out until))
                {
                    if (Hours() < until) continue;
                    dayUntil.Remove(u.Data.id);
                    rising.Add(u);
                    continue;
                }

                AIBehaviorControllerBase ai = u.aiController;
                int wake = ai != null ? ai.wakeTime : 6;
                int sleep = ai != null ? ai.sleepTime : 21;
                if (hour >= sleep || hour < wake) continue;

                int late = (hour - wake) * 60 + minute;
                int mine = Mathf.RoundToInt(Crime.Lot(u.Data.id, 7) * Mathf.Max(0, WakeSpread.Value));
                if (late < mine && hour < 12) continue;

                rising.Add(u);
            }

            foreach (UnitAttribute u in rising)
            {
                night.Remove(u);
                if (u == null || u.Data == null || u.Data.isdead) continue;

                try
                {
                    if (u.aiController != null) BrainStyle(u.aiController) = RestStyle.none;
                    if (u.isSleeping && u.stateMachine != null) u.stateMachine.HandleStop();
                }
                catch
                {
                }
            }
        }
    }

    // Состояние «иду отдыхать» берёт выбор у распорядка: сказано спать — спит.
    [HarmonyPatch(typeof(ToRestState), "OnEnterState")]
    internal static class ToRestEnter_Sleep_Patch
    {
        private static void Postfix(ToRestState __instance)
        {
            if (!Sleep.On()) return;

            try
            {
                UnitAttribute u = __instance.unit;
                if (u == null || u.inParty || u.aiController == null) return;

                RestStyle wanted = Sleep.BrainStyle(u.aiController);
                if (wanted != RestStyle.none) Sleep.StateStyle(__instance) = wanted;
            }
            catch
            {
            }
        }
    }

    // К кровати — без падения на пустом месте; лечь негде — посреди улицы не ложится.
    [HarmonyPatch(typeof(ToRestState), "Rest")]
    internal static class ToRestRest_Sleep_Patch
    {
        private static bool Prefix(ToRestState __instance)
        {
            if (!Sleep.On()) return true;

            UnitAttribute unit = __instance.unit;
            if (unit == null || unit.inParty) return true;

            try
            {
                AIBehaviorController brain = Sleep.StateBrain(__instance) ?? unit.aiController as AIBehaviorController;
                Furniture bed = Sleep.PickBed(brain);

                if (bed != null)
                {
                    if (unit.interactObject == null || unit.interactObject.gameObject != bed.gameObject)
                        unit.stateMachine.HandleCommand(new UnitCommand(commandsName.interact, bed.gameObject));
                    return false;
                }

                BuildingController home = brain != null ? brain.home : null;
                if (home != null && home.spawnPoint != null)
                {
                    if (Vector3.Distance(unit.transform.position, home.spawnPoint.position) > 1f)
                    {
                        if (!unit.isMoving && !unit.isEngaged && unit.maxSpeed > 0f)
                            unit.stateMachine.HandleCommand(new UnitCommand(commandsName.move, home.spawnPoint.gameObject, 0.4f));
                    }
                    else
                    {
                        unit.stateMachine.HandleCommand(new UnitCommand(commandsName.rest, "laydown"));
                    }
                    return false;
                }

                // Две ночи без сна — ложится там, где стоит.
                if (Sleep.Spent(unit))
                {
                    unit.stateMachine.HandleCommand(new UnitCommand(commandsName.rest, "laydown"));
                    return false;
                }

                Sleep.StateStyle(__instance) = RestStyle.none;
                if (unit.aiController != null) Sleep.BrainStyle(unit.aiController) = RestStyle.none;
                return false;
            }
            catch
            {
                try { Sleep.StateStyle(__instance) = RestStyle.none; } catch { }
                return false;
            }
        }
    }

    // Лёг в кровать по ночному распорядку — запомнить, чтобы поднять утром.
    [HarmonyPatch(typeof(RestingState), "OnEnterState")]
    internal static class RestingEnter_Sleep_Patch
    {
        private static void Postfix(RestingState __instance)
        {
            try
            {
                UnitAttribute u = __instance.unit;
                if (u == null || !u.isSleeping) return;

                if (Sleep.On()) Sleep.Lay(u);
                Nightwatch.Undress(u);
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(RestingState), "OnExitState")]
    internal static class RestingExit_Sleep_Patch
    {
        private static void Postfix(RestingState __instance)
        {
            try
            {
                UnitAttribute u = __instance.unit;
                Sleep.Rose(u);
                Nightwatch.Rose(u);
            }
            catch
            {
            }
        }
    }
}
