using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Light, shadow and noise, and the blow that comes out of nowhere.
    ///
    /// Видят тем хуже, чем темнее: днём на полном свету — как в игре, в тени днём — на
    /// треть ближе, в сумерках ещё ближе, ночью — меньше чем на половину. Факел, костёр,
    /// фонарь высвечивают стоящего рядом, как днём. Слышат шаги: стоящего на месте почти не
    /// слышно, идущего — как в игре, бегущего — вдвое дальше; лёгкий и средний доспех шумят
    /// меньше, тяжёлый гремит в полтора раза громче обычного.
    ///
    /// Кто не знает, что ты рядом, — не увернётся и не прикроется. Удар вплотную в лёгком или
    /// среднем доспехе по такому — всегда крит оружия и ещё втрое; выстрел по такому —
    /// всегда крит и ещё в полтора раза. «Не знает» — не видит сейчас и не терял из виду
    /// последние несколько секунд: присесть посреди драки перед самым носом — не спрятаться.
    /// </summary>
    internal static class Shade
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Night;
        internal static ConfigEntry<float> Shadow;
        internal static ConfigEntry<bool> Lamps;
        internal static ConfigEntry<string> Noise;
        internal static ConfigEntry<float> Still;
        internal static ConfigEntry<float> Run;
        internal static ConfigEntry<float> Melee;
        internal static ConfigEntry<float> Ranged;
        internal static ConfigEntry<float> Forget;
        internal static ConfigEntry<bool> Tags;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Shade", "Enabled", true,
                "Let light, shadow and the noise of steps decide how far a man is seen and heard, "
                + "and let a blow on one who does not know you are there land as a sneak attack.");

            Night = config.Bind("Shade", "Night", 0.4f,
                new ConfigDescription("How far a man is seen at night, as a share of the day.",
                    new AcceptableValueRange<float>(0.1f, 1f)));

            Shadow = config.Bind("Shade", "Shadow", 0.7f,
                new ConfigDescription("How far a man standing in shade is seen by day, as a share.",
                    new AcceptableValueRange<float>(0.1f, 1f)));

            Lamps = config.Bind("Shade", "Lamps", true,
                "Let torches, fires and lamps light up whoever stands near them.");

            Noise = config.Bind("Shade", "Noise", "0.6,0.7,0.9,1.5",
                "How loud the steps are in no armour, light, medium and heavy armour, against "
                + "the game hearing.");

            Still = config.Bind("Shade", "Still", 0.35f,
                new ConfigDescription("How loud one is who stands still, against one walking.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Run = config.Bind("Shade", "Run", 2f,
                new ConfigDescription("How many times louder running is than walking.",
                    new AcceptableValueRange<float>(1f, 5f)));

            Melee = config.Bind("Shade", "Melee", 3f,
                new ConfigDescription("How many times more than a weapon crit a melee blow deals to "
                    + "one who does not know you are there, in light or medium armour.",
                    new AcceptableValueRange<float>(1f, 10f)));

            Ranged = config.Bind("Shade", "Ranged", 1.5f,
                new ConfigDescription("How many times more than a weapon crit a shot deals to one "
                    + "who does not know you are there.",
                    new AcceptableValueRange<float>(1f, 10f)));

            Forget = config.Bind("Shade", "Forget", 8f,
                new ConfigDescription("Seconds one still keeps in mind an enemy he has lost sight of.",
                    new AcceptableValueRange<float>(0f, 60f)));

            Tags = config.Bind("Shade", "Tags", true,
                "Say over the hero, while he sneaks, whether he stands in shadow or in light.");
        }

        // ------------------------------------------------------------------ свет

        private sealed class Mark
        {
            internal float lightAt = -100f;
            internal float noiseAt = -100f;
            internal float light = 1f;
            internal float noise = 1f;
        }

        private static readonly ConditionalWeakTable<UnitAttribute, Mark> marks = new ConditionalWeakTable<UnitAttribute, Mark>();

        private static Mark Of(UnitAttribute u)
        {
            Mark m;
            if (!marks.TryGetValue(u, out m))
            {
                m = new Mark();
                marks.Add(u, m);
            }
            return m;
        }

        private static bool Abroad()
        {
            return (bool)WorldTravelManager.instance;
        }

        /// <summary>How light it is by the clock: full by day, the night share at night.</summary>
        private static float Ambient()
        {
            TimeManager time = TimeManager.Instance;
            if (time == null || time.GameTime == null) return 1f;

            float h = time.GameTime.Hours + time.GameTime.Minutes / 60f;
            float dark = Night.Value;

            if (h >= 7f && h < 18f) return 1f;
            if (h >= 5f && h < 7f) return Mathf.Lerp(dark, 1f, (h - 5f) / 2f);
            if (h >= 18f && h < 20f) return Mathf.Lerp(1f, dark, (h - 18f) / 2f);
            return dark;
        }

        private static readonly List<Light> lamps = new List<Light>();
        private static Light sun;
        private static float surveyed = -100f;

        private static void Survey()
        {
            float now = Time.realtimeSinceStartup;
            if (now - surveyed < 8f && now >= surveyed) return;
            surveyed = now;

            lamps.Clear();
            sun = null;
            float bright = 0f;

            try
            {
                foreach (Light one in UnityEngine.Object.FindObjectsOfType<Light>())
                {
                    if (one == null || !one.enabled || !one.gameObject.activeInHierarchy) continue;

                    if (one.type == LightType.Directional)
                    {
                        if (one.intensity > bright) { bright = one.intensity; sun = one; }
                        continue;
                    }

                    // Заливающий свет на полсцены — это освещение сцены, а не факел.
                    if (one.intensity <= 0f || one.range < 0.5f || one.range > 25f) continue;
                    lamps.Add(one);
                }

                if (RenderSettings.sun != null && RenderSettings.sun.enabled) sun = RenderSettings.sun;
            }
            catch
            {
            }
        }

        private static float Lamp(Vector3 at)
        {
            float best = 0f;

            for (int i = 0; i < lamps.Count; i++)
            {
                Light one = lamps[i];
                if (one == null || !one.enabled) continue;

                float r = one.range;
                float d2 = (one.transform.position - at).sqrMagnitude;
                if (d2 >= r * r) continue;

                float lit = 1f - d2 / (r * r);
                if (lit > best) best = lit;
            }

            return best;
        }

        private static bool Shaded(Vector3 at)
        {
            if (sun == null) return false;

            Vector3 toward = -sun.transform.forward;
            if (toward.y < 0.05f) return true;

            return Physics.Raycast(at, toward, 80f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Who is looking right now, while the game asks whether he sees.</summary>
        internal static UnitAttribute watcher;

        /// <summary>True in the dark hours, when the light is at its night share.</summary>
        internal static bool Dark()
        {
            if (Enabled == null || !Enabled.Value || Abroad()) return false;
            return Ambient() <= Night.Value + 0.05f;
        }

        /// <summary>How far this one is seen, as a share of how far he would be seen by day.</summary>
        internal static float Seen(UnitAttribute u)
        {
            if (Enabled == null || !Enabled.Value || u == null || Abroad()) return 1f;

            // Зверь, нежить и чудовище видят в темноте, как днём.
            if (watcher != null && Dread.SeesInDark(watcher)) return 1f;

            Mark m = Of(u);
            float now = Time.time;
            if (now - m.lightAt < 0.5f && now >= m.lightAt) return m.light;
            m.lightAt = now;

            try
            {
                Vector3 at = u.transform.position + Vector3.up * 1.2f;
                float ambient = Ambient();

                if (ambient > Night.Value + 0.01f && Shaded(at)) ambient = Mathf.Max(Night.Value, ambient * Shadow.Value);

                float lamp = Lamps.Value ? Lamp(at) : 0f;
                m.light = Mathf.Clamp(Mathf.Max(ambient, lamp), 0.1f, 1f);
            }
            catch
            {
                m.light = 1f;
            }

            return m.light;
        }

        // ------------------------------------------------------------------ шум

        private static string noiseRead;
        private static readonly float[] byArmour = { 0.6f, 0.7f, 0.9f, 1.5f };

        private static float Armour(ArmourType kind)
        {
            if (noiseRead != Noise.Value)
            {
                noiseRead = Noise.Value;
                string[] parts = (Noise.Value ?? "").Split(',');
                for (int i = 0; i < byArmour.Length && i < parts.Length; i++)
                {
                    float much;
                    if (float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out much))
                        byArmour[i] = Mathf.Clamp(much, 0.05f, 5f);
                }
            }

            int at = Mathf.Clamp((int)kind, 0, byArmour.Length - 1);
            return byArmour[at];
        }

        /// <summary>How far this one is heard, as a share of how far the game would hear him.</summary>
        internal static float Heard(UnitAttribute u)
        {
            if (Enabled == null || !Enabled.Value || u == null || Abroad()) return 1f;

            Mark m = Of(u);
            float now = Time.time;
            if (now - m.noiseAt < 0.25f && now >= m.noiseAt) return m.noise;
            m.noiseAt = now;

            float body = u is HumaniodUnit ? Armour(u.armourtype) : 1f;

            float gait;
            if (!u.isMoving) gait = Still.Value;
            else if (!u.isCrouching && u.moveSpeedMD > 0.75f) gait = Run.Value;
            else gait = 1f;

            m.noise = Mathf.Max(0.05f, body * gait);
            return m.noise;
        }

        // ------------------------------------------------------------------ кто кого знает

        private sealed class Mind
        {
            internal readonly Dictionary<UnitAttribute, float> found = new Dictionary<UnitAttribute, float>();
            internal readonly Dictionary<UnitAttribute, float> lost = new Dictionary<UnitAttribute, float>();
        }

        private static readonly ConditionalWeakTable<UnitAttribute, Mind> minds = new ConditionalWeakTable<UnitAttribute, Mind>();

        private static Mind MindOf(UnitAttribute u)
        {
            Mind m;
            if (!minds.TryGetValue(u, out m))
            {
                m = new Mind();
                minds.Add(u, m);
            }
            return m;
        }

        private static readonly List<UnitAttribute> stale = new List<UnitAttribute>();

        private static void Tidy(Dictionary<UnitAttribute, float> book, float now)
        {
            if (book.Count < 48) return;

            stale.Clear();
            foreach (KeyValuePair<UnitAttribute, float> one in book)
            {
                if (one.Key == null || now - one.Value > 120f) stale.Add(one.Key);
            }
            for (int i = 0; i < stale.Count; i++) book.Remove(stale[i]);
        }

        internal static void Found(UnitAttribute who, UnitAttribute whom)
        {
            if (who == null || whom == null) return;
            Mind m = MindOf(who);
            float now = Time.time;
            m.found[whom] = now;
            Tidy(m.found, now);
        }

        internal static void Lost(UnitAttribute who, UnitAttribute whom)
        {
            if (who == null || whom == null) return;
            Mind m = MindOf(who);
            float now = Time.time;
            m.lost[whom] = now;
            Tidy(m.lost, now);
        }

        private sealed class Launch { internal float at; }

        private static readonly ConditionalWeakTable<Attack, Launch> launches = new ConditionalWeakTable<Attack, Launch>();

        /// <summary>A swing has begun or a shot has left: the moment the blow was sent.</summary>
        internal static void Launched(Attack attack)
        {
            if (attack == null) return;

            Launch mine;
            if (!launches.TryGetValue(attack, out mine))
            {
                mine = new Launch();
                launches.Add(attack, mine);
            }
            mine.at = Time.time;
        }

        internal static float LaunchOf(Attack attack)
        {
            Launch mine;
            if (attack != null && launches.TryGetValue(attack, out mine) && mine.at <= Time.time) return mine.at;
            return Time.time;
        }

        /// <summary>
        /// Whether the victim has no idea the attacker is there: does not sense him, and has not
        /// lost him from sight lately. Found out only after the blow had left — still unaware.
        /// </summary>
        internal static bool Unaware(UnitAttribute victim, UnitAttribute attacker, float since)
        {
            if (victim == null || attacker == null || (object)victim == (object)attacker) return false;
            if (victim.Data == null || victim.Data.isdead) return false;
            if (GameController.IsAlly(victim, attacker)) return false;
            if (victim.isSleeping) return true;

            Mind m;
            minds.TryGetValue(victim, out m);

            bool knows = victim.sensedUnit.Contains(attacker)
                || victim.engagedEnemy.Contains(attacker)
                || (object)victim.Target == (object)attacker;

            if (knows)
            {
                float at;
                bool late = m != null && m.found.TryGetValue(attacker, out at) && at >= since - 0.05f;
                if (!late) return false;
            }

            float gone;
            if (m != null && m.lost.TryGetValue(attacker, out gone) && since - gone < Forget.Value) return false;

            return true;
        }

        /// <summary>
        /// How many times more this blow deals for coming out of nowhere; 1 if it does not.
        /// A blow that was not a crit becomes one, which the caller must mark.
        /// </summary>
        internal static float Sneak(UnitAttribute victim, Attack attack, out bool forced)
        {
            forced = false;
            if (Enabled == null || !Enabled.Value || attack == null || attack.attacker == null) return 1f;

            bool ranged = attack.attackType == AttackType.ranged;
            if (!ranged && attack.attacker.armourtype == ArmourType.Heavy) return 1f;

            if (!Unaware(victim, attack.attacker, LaunchOf(attack))) return 1f;

            float times = ranged ? Ranged.Value : Melee.Value;
            if (!attack.isCrit)
            {
                times *= Mathf.Max(1f, attack.critMultiple);
                forced = true;
            }

            return times;
        }

        // ------------------------------------------------------------------ подсказка герою

        private static float ticked;
        private static bool wasHidden;
        private static bool wasLit;

        internal static void Tick()
        {
            if (Enabled == null || !Enabled.Value) return;

            // Светильники переписываются здесь, в общем такте, а не посреди проверки зрения:
            // поиск по сцене стоит дорого, и в горячем месте он давал бы рывок.
            if (!Abroad()) Survey();

            if (!Tags.Value) return;

            float now = Time.unscaledTime;
            if (now - ticked < 0.5f && now >= ticked) return;
            ticked = now;

            try
            {
                UnitAttribute hero = gameManager.currentplayUnit;
                if (hero == null || hero.Data == null || hero.Data.isdead || !hero.isCrouching || Abroad())
                {
                    wasHidden = false;
                    return;
                }

                bool lit = Seen(hero) >= 0.7f;

                if (!wasHidden || lit != wasLit)
                {
                    if (hero.lifebar != null)
                    {
                        hero.lifebar.ShowTextTag(lit ? "<color=#FFD27A>На свету</color>" : "<color=#8A93A6>В тени</color>", 1.5f);
                    }
                }

                wasHidden = true;
                wasLit = lit;
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "HearingAvoidanceMD", MethodType.Getter)]
    internal static class HearingAvoidance_Shade_Patch
    {
        private static void Postfix(UnitAttribute __instance, ref float __result)
        {
            __result *= Shade.Heard(__instance);
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "SeeAvoidanceMD", MethodType.Getter)]
    internal static class SeeAvoidance_Shade_Patch
    {
        private static void Postfix(UnitAttribute __instance, ref float __result)
        {
            __result *= Shade.Seen(__instance);
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "AddSense")]
    internal static class AddSense_Shade_Patch
    {
        private static void Prefix(UnitAttribute __instance, UnitAttribute enemyUnit)
        {
            if (enemyUnit == null || __instance.sensedUnit.Contains(enemyUnit)) return;
            Shade.Found(__instance, enemyUnit);
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "AddEngaged")]
    internal static class AddEngaged_Shade_Patch
    {
        private static void Prefix(UnitAttribute __instance, UnitAttribute enemyUnit)
        {
            if (enemyUnit == null || __instance.sensedUnit.Contains(enemyUnit)) return;
            Shade.Found(__instance, enemyUnit);
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "RemoveSense")]
    internal static class RemoveSense_Shade_Patch
    {
        private static void Prefix(UnitAttribute __instance, UnitAttribute enemyUnit)
        {
            if (enemyUnit == null || !__instance.sensedUnit.Contains(enemyUnit)) return;
            Shade.Lost(__instance, enemyUnit);
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "CalculateAttackInfo")]
    internal static class CalculateAttackInfo_Shade_Patch
    {
        private static void Postfix(Attack attack)
        {
            Shade.Launched(attack);
        }
    }

    // Кто не знает, что ты рядом, — не увернётся.
    [HarmonyPatch(typeof(UnitAttribute), "Dodgecheck")]
    internal static class Dodgecheck_Shade_Patch
    {
        private static bool Prefix(UnitAttribute __instance, Attack t_attack, ref bool __result)
        {
            if (Shade.Enabled == null || !Shade.Enabled.Value) return true;
            if (__instance.Data == null || !__instance.Data.hitable || !Blows.Weapon(t_attack)) return true;
            if (!Shade.Unaware(__instance, t_attack.attacker, Shade.LaunchOf(t_attack))) return true;

            __result = true;
            return false;
        }
    }

    // И не прикроется.
    [HarmonyPatch(typeof(UnitAttribute), "Blockcheck")]
    internal static class Blockcheck_Shade_Patch
    {
        private static bool Prefix(UnitAttribute __instance, Attack t_attack, ref bool __result)
        {
            if (Shade.Enabled == null || !Shade.Enabled.Value) return true;
            if (__instance.Data == null || !__instance.Data.hitable || !Blows.Weapon(t_attack)) return true;
            if (!Shade.Unaware(__instance, t_attack.attacker, Shade.LaunchOf(t_attack))) return true;

            __result = true;
            return false;
        }
    }
}
