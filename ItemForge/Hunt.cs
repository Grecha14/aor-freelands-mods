using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using spell;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Who goes for whom: archers and thieves for the mages first, two-handers break through.
    ///
    /// Игра выбирает цель просто — ближайшего. Теперь лучник и вор сперва ищут глазами мага,
    /// за ним — лучника, а из остальных — того, на ком меньше железа. Воин с двуручником ищет
    /// одного мага и идёт к нему сквозь строй — а проходя мимо чужих клинков, подставляет им
    /// бок и спину. Остальные, как и прежде, бьют ближайшего.
    ///
    /// Гоняются не за краем света: лучник смотрит на тех, до кого достанет стрела, вор и
    /// двуручник — на тех, кто рядом. Дальних выбирают, только если ближе никого нет. И кто
    /// уже выбрал себе цель своего разряда, тот её не бросает ради такой же.
    /// </summary>
    internal static class Hunt
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Reach;
        internal static ConfigEntry<float> Close;
        internal static ConfigEntry<bool> Breakers;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Hunt", "Enabled", true,
                "Let archers and thieves go for mages first, then archers, then whoever wears "
                + "the least armour; let two-handed warriors break through to the mages.");

            Reach = config.Bind("Hunt", "Reach", 30f,
                new ConfigDescription("How far, in metres, an archer looks for the one he wants.",
                    new AcceptableValueRange<float>(5f, 80f)));

            Close = config.Bind("Hunt", "Close", 18f,
                new ConfigDescription("How far, in metres, a thief or a two-hander looks for the one "
                    + "he wants.",
                    new AcceptableValueRange<float>(3f, 60f)));

            Breakers = config.Bind("Hunt", "Breakers", true,
                "Let warriors with two-handed weapons push through to the mages.");
        }

        internal enum Kind { Plain, Archer, Thief, Breaker }

        private sealed class Sort
        {
            internal float at = -100f;
            internal bool mage;
            internal bool archer;
        }

        private static readonly ConditionalWeakTable<UnitAttribute, Sort> sorts = new ConditionalWeakTable<UnitAttribute, Sort>();

        private static Sort Look(UnitAttribute u)
        {
            Sort s;
            if (!sorts.TryGetValue(u, out s))
            {
                s = new Sort();
                sorts.Add(u, s);
            }

            float now = Time.time;
            if (now - s.at < 5f && now >= s.at) return s;
            s.at = now;

            s.mage = false;
            s.archer = u.weapontype == WeaponType.range;

            try
            {
                if (u.weapons != null)
                {
                    foreach (Weapon w in u.weapons)
                    {
                        if (w == null) continue;
                        if (w.isMagicAttack || w.weaponClass == WeaponClass.Staff || w.weaponClass == WeaponClass.Wand)
                        {
                            s.mage = true;
                            break;
                        }
                    }
                }

                if (!s.mage && u.spellmanger != null && u.spellmanger.spells != null)
                {
                    foreach (SpellBase spell in u.spellmanger.spells)
                    {
                        if (Spells.Magic(spell)) { s.mage = true; break; }
                    }
                }
            }
            catch
            {
            }

            return s;
        }

        internal static bool Mage(UnitAttribute u) { return u != null && Look(u).mage; }

        internal static bool Archer(UnitAttribute u) { return u != null && Look(u).archer; }

        private static bool Thief(UnitAttribute u)
        {
            if (u.talentmanger != null && u.talentmanger.ContainTalent("Rogue")) return true;
            if (u.armourtype == ArmourType.Medium || u.armourtype == ArmourType.Heavy) return false;
            if (u.weapontype == WeaponType.daul) return true;

            if (u.weapons != null)
            {
                foreach (Weapon w in u.weapons)
                {
                    if (w == null) continue;
                    WeaponClass c = w.weaponClass;
                    if (c == WeaponClass.Dagger || c == WeaponClass.Katar || c == WeaponClass.Wakizashi) return true;
                }
            }

            return false;
        }

        internal static Kind KindOf(UnitAttribute u)
        {
            if (!(u is HumaniodUnit) || Mage(u)) return Kind.Plain;
            if (Archer(u)) return Kind.Archer;
            if (Thief(u)) return Kind.Thief;
            if (Breakers.Value && u.weapontype == WeaponType.twohand) return Kind.Breaker;
            return Kind.Plain;
        }

        private static int Tier(Kind kind, UnitAttribute one)
        {
            if (Mage(one)) return 0;
            if (kind == Kind.Breaker) return 1;
            if (Archer(one)) return 1;
            return 2;
        }

        private static bool Fits(UnitAttribute chooser, UnitAttribute one)
        {
            return one != null && one.Data != null && !one.Data.isdead && !one.isPracticeDummy
                && (one.buffmanger == null || !one.buffmanger.ContainBuff("Sleeping"))
                && !chooser.ignoredEnemy.ContainsKey(one)
                && FactionManager.Instance.IsEnemy(chooser, one);
        }

        /// <summary>The one this chooser goes for, or null to leave it to the game.</summary>
        internal static UnitAttribute Pick(UnitAttribute chooser, List<UnitAttribute> group)
        {
            Kind kind = KindOf(chooser);
            if (kind == Kind.Plain) return null;

            float reach = kind == Kind.Archer ? Reach.Value : Close.Value;
            Vector3 here = chooser.transform.position;

            UnitAttribute best = null;
            int bestTier = int.MaxValue;
            float bestArmour = float.MaxValue;
            float bestDistance = float.MaxValue;

            UnitAttribute held = chooser.Target;
            int heldTier = int.MaxValue;

            foreach (UnitAttribute one in group)
            {
                if (!Fits(chooser, one)) continue;

                float distance = Vector3.Distance(here, one.transform.position);

                // Дальше, чем достаёт взгляд охотника, — последний разряд: туда, только если
                // ближе совсем никого.
                int tier = distance <= reach ? Tier(kind, one) : 9;

                // Из одного разряда — тот, на ком меньше железа; двуручнику всё равно.
                float armour = kind == Kind.Breaker || tier == 9 ? 0f : (int)one.armourtype * 1000f + one.PDR;

                if ((object)one == (object)held) heldTier = tier;

                bool better = tier < bestTier
                    || (tier == bestTier && armour < bestArmour - 0.01f)
                    || (tier == bestTier && Mathf.Abs(armour - bestArmour) <= 0.01f && distance < bestDistance);

                if (better)
                {
                    best = one;
                    bestTier = tier;
                    bestArmour = armour;
                    bestDistance = distance;
                }
            }

            // Кто уже держит цель своего разряда, тот её и держит.
            if (held != null && heldTier == bestTier && bestTier < 9) return held;

            return best;
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "PickTargetInGroup")]
    internal static class PickTargetInGroup_Hunt_Patch
    {
        private static bool Prefix(UnitAttribute __instance, List<UnitAttribute> group, ref UnitAttribute __result)
        {
            if (Hunt.Enabled == null || !Hunt.Enabled.Value || group == null || group.Count == 0) return true;
            if ((object)__instance == (object)gameManager.currentplayUnit) return true;

            try
            {
                UnitAttribute chosen = Hunt.Pick(__instance, group);
                if (chosen == null) return true;

                __result = chosen;
                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}
