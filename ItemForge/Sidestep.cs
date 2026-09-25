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
    /// A step aside, back or in, the moment the enemy starts his swing.
    ///
    /// Кто видит замах, тот успевает сделать шаг. В сторону — уходит с линии укола или рубки;
    /// назад — из-под длины клинка; вперёд, под длинное древко или широкий мах, — принимает
    /// удар ближе к рукояти, где он слабее. Ушёл совсем — удар проходит мимо: игра в миг
    /// удара сама перепроверяет, кто ещё в досягаемости и в дуге. Не успел уйти совсем —
    /// удар всё равно смягчён.
    ///
    /// Шагнуть успевает не всякий: ловкость прибавляет, тяжёлый доспех отнимает, и никто не
    /// шагает, пока сам рубит, колдует, лежит, прикрывается щитом или не видит, откуда бьют.
    /// </summary>
    internal static class Sidestep
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Chance;
        internal static ConfigEntry<float> PerAgility;
        internal static ConfigEntry<float> Most;
        internal static ConfigEntry<string> Armour;
        internal static ConfigEntry<float> Soften;
        internal static ConfigEntry<bool> Hero;
        internal static ConfigEntry<bool> Tags;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Sidestep", "Enabled", true,
                "Let fighters step aside, back or in when an enemy starts his swing at them.");

            Chance = config.Bind("Sidestep", "Chance", 0.15f,
                new ConfigDescription("The chance to step at ten agility, as a share.",
                    new AcceptableValueRange<float>(0f, 1f)));

            PerAgility = config.Bind("Sidestep", "PerAgility", 0.005f,
                new ConfigDescription("How much each point of agility above ten adds to the chance.",
                    new AcceptableValueRange<float>(0f, 0.05f)));

            Most = config.Bind("Sidestep", "Most", 0.45f,
                new ConfigDescription("The chance never goes above this.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Armour = config.Bind("Sidestep", "Armour", "1.1,1,0.7,0.35",
                "How the chance is multiplied in no armour, light, medium and heavy armour.");

            Soften = config.Bind("Sidestep", "Soften", 0.4f,
                new ConfigDescription("How much of the blow a step takes off when it still lands.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Hero = config.Bind("Sidestep", "Hero", true,
                "Let the hero under the player hand step too, while he stands his ground.");

            Tags = config.Bind("Sidestep", "Tags", true,
                "Say over the one who stepped which way he went, where the player people fight.");
        }

        private static string armourRead;
        private static readonly float[] byArmour = { 1.1f, 1f, 0.7f, 0.35f };

        private static float ArmourShare(ArmourType kind)
        {
            if (armourRead != Armour.Value)
            {
                armourRead = Armour.Value;
                string[] parts = (Armour.Value ?? "").Split(',');
                for (int i = 0; i < byArmour.Length && i < parts.Length; i++)
                {
                    float much;
                    if (float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out much))
                        byArmour[i] = Mathf.Clamp(much, 0f, 3f);
                }
            }

            return byArmour[Mathf.Clamp((int)kind, 0, byArmour.Length - 1)];
        }

        internal static float ChanceOf(UnitAttribute u)
        {
            float chance = Chance.Value;

            HumaniodUnit man = u as HumaniodUnit;
            if (man != null)
            {
                chance += (man.Agility - 10) * PerAgility.Value;
                chance *= ArmourShare(u.armourtype);
            }

            return Mathf.Clamp(chance, 0f, Most.Value);
        }

        internal static bool Able(UnitAttribute u, UnitAttribute attacker, Attack attack)
        {
            // Шагать учатся люди: у зверя и чудовища нужного движения может не быть вовсе.
            if (!(u is HumaniodUnit)) return false;
            if (u == null || u.Data == null || u.Data.isdead || !u.Data.allowdodge || !u.isEngaged) return false;
            if (u.isAttacking || u.isBlocking || u.isDodgeing || u.isEating || u.isCarrying || u.isCrouching) return false;
            if (u.isSleeping || u.isKnockdown || u.isRagdolled || u.isGrabed || u.isMovementRestricted || u.isHit) return false;
            if (u.inTurnBaseMode || u.isSealed || u.isCaged || u.isFlying || u.isPracticeDummy) return false;
            if (u.ani == null || u.nav == null || !u.nav.enabled) return false;
            if (u.spellmanger != null && u.spellmanger.isCasting) return false;
            if (u.stateMachine != null && (u.stateMachine.simpleSstate == SimpleState.disabled || u.stateMachine.IsClimbing(u.stateMachine.TState))) return false;
            if (u.buffmanger != null && (u.buffmanger.ContainBuffType(bufftype.loseControl) || u.buffmanger.ContainBuff("Knockdown") || u.buffmanger.ContainBuff("Stunned"))) return false;

            if ((object)u == (object)gameManager.currentplayUnit && (!Hero.Value || u.isMoving)) return false;

            // Не видит, откуда бьют, — и шагнуть не догадается.
            if (Shade.Unaware(u, attacker, Shade.LaunchOf(attack))) return false;

            return true;
        }

        private sealed class Took
        {
            internal Attack attack;
            internal float at;
        }

        private static readonly ConditionalWeakTable<UnitAttribute, Took> took = new ConditionalWeakTable<UnitAttribute, Took>();

        private sealed class Push
        {
            internal UnitAttribute who;
            internal Vector3 way;
            internal float left;
        }

        private static readonly List<Push> pushes = new List<Push>();

        /// <summary>Everyone the swing is aimed at gets his chance to step.</summary>
        internal static void Answer(UnitAttribute attacker, Attack attack)
        {
            if (Enabled == null || !Enabled.Value || attacker == null || attack == null || attack.weapon == null) return;
            if (attack.attackType != AttackType.melee || attack.weapon.hitType == WeaponHitType.collider) return;

            List<UnitAttribute> near = attacker.GetEnemyUnitsInAttackRange(attack);
            if (near == null) return;

            for (int i = 0; i < near.Count; i++)
            {
                UnitAttribute victim = near[i];
                if (victim == null || attack.dodger.Contains(victim) || attack.blocker.Contains(victim)) continue;
                if (!attacker.Anglecheck(victim, attack, setYZero: true)) continue;
                if (!Able(victim, attacker, attack)) continue;
                if (UnityEngine.Random.value >= ChanceOf(victim)) continue;

                Go(victim, attacker, attack);
            }
        }

        private static void Go(UnitAttribute victim, UnitAttribute attacker, Attack attack)
        {
            Vector3 here = victim.transform.position;
            Vector3 there = attacker.transform.position;
            float distance = Vector3.Distance(new Vector3(here.x, 0f, here.z), new Vector3(there.x, 0f, there.z));

            bool behind = UnitAttribute.GetDirection(victim, attacker) == AttackDirection.behind;
            bool wide = attack.weapon.hitType == WeaponHitType.aroundNode || attack.angle >= 150f;
            bool clears = distance + 1.5f > attack.range + 0.3f;
            bool longArm = attacker.weapontype == WeaponType.twohand || attacker.weapontype == WeaponType.polearms;

            // Куда шагнуть: из-под короткого — назад; с линии узкого удара — в сторону; под
            // широкий мах длинного древка, откуда назад не успеть, — вперёд, к рукояти.
            int way;
            if (!behind && clears && (wide || UnityEngine.Random.value < 0.5f)) way = 2;
            else if (!wide || behind) way = 1;
            else if (longArm) way = 3;
            else way = clears && !behind ? 2 : 1;

            string said;
            if (way == 1)
            {
                bool right = Vector3.Dot(victim.transform.right, there - here) > 0f;
                victim.ani.SetFloat("dodgeset", right ? 0f : 1f);
                victim.ani.SetTrigger("dodge");
                said = "Шаг в сторону";
            }
            else if (way == 2)
            {
                victim.ani.SetFloat("dodgeset", 2f);
                victim.ani.SetTrigger("dodge");
                said = "Отшаг";
            }
            else
            {
                Vector3 toward = there - here;
                toward.y = 0f;
                if (toward.sqrMagnitude > 0.0001f)
                {
                    pushes.Add(new Push { who = victim, way = toward.normalized * Mathf.Min(0.6f, Mathf.Max(0f, distance - 0.8f)), left = 0.15f });
                }
                said = "Шаг под удар";
            }

            Took mine;
            if (!took.TryGetValue(victim, out mine))
            {
                mine = new Took();
                took.Add(victim, mine);
            }
            mine.attack = attack;
            mine.at = Time.time;

            if (Tags.Value && victim.lifebar != null && Blows.Ours(victim, attacker))
            {
                try { victim.lifebar.ShowTextTag("<color=#7FD67F>" + said + "</color>", 1.2f); } catch { }
            }
        }

        /// <summary>What is left of a blow that lands on one who stepped from it: 1 if he did not.</summary>
        internal static float Softened(UnitAttribute victim, Attack attack)
        {
            if (Enabled == null || !Enabled.Value || victim == null || attack == null) return 1f;

            Took mine;
            if (!took.TryGetValue(victim, out mine) || (object)mine.attack != (object)attack) return 1f;

            float since = Time.time - mine.at;
            if (since < 0f || since > 2f) return 1f;

            return 1f - Soften.Value;
        }

        internal static void Tick()
        {
            if (pushes.Count == 0) return;

            float dt = Time.deltaTime;

            for (int i = pushes.Count - 1; i >= 0; i--)
            {
                Push p = pushes[i];

                if (p.who == null || p.who.Data == null || p.who.Data.isdead || p.who.nav == null || !p.who.nav.enabled || p.left <= 0f)
                {
                    pushes.RemoveAt(i);
                    continue;
                }

                float share = Mathf.Min(dt, p.left) / 0.15f;
                p.left -= dt;

                try { p.who.nav.Move(p.way * share); } catch { pushes.RemoveAt(i); }
            }
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "AttackStart")]
    internal static class AttackStart_Sidestep_Patch
    {
        private static void Postfix(UnitAttribute __instance, int weaponIndex)
        {
            if (Sidestep.Enabled == null || !Sidestep.Enabled.Value || (bool)WorldTravelManager.instance) return;

            try
            {
                Weapon weapon = null;
                foreach (Weapon one in __instance.weapons)
                {
                    if (one != null && one.index == weaponIndex) { weapon = one; break; }
                }
                if (weapon == null || weapon.attack == null) return;

                Sidestep.Answer(__instance, weapon.attack);
            }
            catch
            {
            }
        }
    }
}
