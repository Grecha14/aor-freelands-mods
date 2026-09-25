using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using spell;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// No wound, no bleeding.
    ///
    /// Удар в игре несёт с собой эффекты — кровотечение, яд, горение, травму, — и игра вешает
    /// их на цель после урона, не спрашивая, прошёл ли урон вообще. Латы удержали клинок
    /// целиком, а кровь всё равно течёт. У ловушек ещё хуже: капкан вешает свои эффекты раньше,
    /// чем посчитан урон, и делает это каждые четверть секунды, пока человек в нём стоит.
    ///
    /// Здесь правило одно: нет раны — нет и того, что приходит через рану. Если удар не прошёл
    /// сквозь доспех, кровотечение, яд, ожог, травма и урон со временем на цель не ложатся.
    /// Всё, что приходит силой удара, а не раной, — сбить с ног, оглушить, схватить ногу, — как
    /// было: железный капкан держит и латную ногу, просто не режет её.
    /// </summary>
    internal static class Wounds
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Kinds;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Wounds", "Enabled", true,
                "Let the effects that come through a wound — bleeding, poison, burning, injury, "
                + "damage over time — land only when the blow actually got through. Armour that "
                + "stopped the steel stops the bleeding too.");

            Kinds = config.Bind("Wounds", "Kinds", "bleeding,poisoned,burning,injury,damage",
                "Which kinds of effect need a wound to land, by the game's buff types, separated "
                + "by commas. Control — knockdown, stun, being held — is left out on purpose: it "
                + "comes from the force of the blow, not the cut.");

            Telling = config.Bind("Wounds", "Telling", true,
                "Say in the log, for the first few, what was kept off because nothing got through.");
        }

        private static HashSet<bufftype> kinds;
        private static string kindsRead;
        private static int told;

        internal static bool Needs(BuffBase buff)
        {
            if (Enabled == null || !Enabled.Value || buff == null || buff.buffInfo == null) return false;

            string written = Kinds.Value ?? "";
            if (kinds == null || written != kindsRead)
            {
                kinds = new HashSet<bufftype>();
                foreach (string one in written.Split(','))
                {
                    try { kinds.Add((bufftype)Enum.Parse(typeof(bufftype), one.Trim(), true)); }
                    catch { }
                }
                kindsRead = written;
            }

            return kinds.Contains(buff.buffInfo.type);
        }

        internal static void Kept(UnitAttribute who, BuffBase buff, string from)
        {
            if (Telling == null || !Telling.Value || told >= 15 || who == null || who.Data == null) return;

            told++;
            ItemForgePlugin.Log.LogInfo($"«{who.Data.unitname}»: {from} не ранил — «{buff.buffInfo.id}» "
                + "не наложен.");
        }

        // Удары, что сейчас раздают свои эффекты, и по кому. Стопкой: вложенный удар (заслон
        // «Страж» бьёт по заслонившему) — свой. Цель держим сами: у удара по площади игра
        // запоминает только первую.
        internal static readonly List<KeyValuePair<Attack, UnitAttribute>> dealing =
            new List<KeyValuePair<Attack, UnitAttribute>>();
    }

    // Удар: сначала урон, потом эффекты. Пока идут эффекты, помним, чей это удар.
    [HarmonyPatch(typeof(UnitAttribute), "ApplyAttackEffect")]
    internal static class ApplyAttackEffect_Wounds_Patch
    {
        private static void Prefix(UnitAttribute target, Attack attack)
        {
            if (attack != null) Wounds.dealing.Add(new KeyValuePair<Attack, UnitAttribute>(attack, target));
        }

        private static void Finalizer(Attack attack)
        {
            if (attack == null) return;

            for (int i = Wounds.dealing.Count - 1; i >= 0; i--)
            {
                if ((object)Wounds.dealing[i].Key != (object)attack) continue;

                Wounds.dealing.RemoveAt(i);
                break;
            }
        }
    }

    // Эффект из удара, который никого не ранил, не ложится.
    [HarmonyPatch(typeof(BuffManager), "AddBuff", new Type[] { typeof(BuffBase) })]
    internal static class AddBuff_Wounds_Patch
    {
        private static bool Prefix(BuffManager __instance, BuffBase buff)
        {
            try
            {
                if (Wounds.dealing.Count == 0 || !Wounds.Needs(buff)) return true;

                KeyValuePair<Attack, UnitAttribute> now = Wounds.dealing[Wounds.dealing.Count - 1];
                Attack attack = now.Key;
                if (attack == null || __instance.unit == null) return true;
                if ((object)now.Value != (object)__instance.unit) return true;
                if (attack.realDamage > 0f) return true;

                Wounds.Kept(__instance.unit, buff, "удар");
                return false;
            }
            catch
            {
                return true;
            }
        }
    }

    // Капкан: игра вешала эффекты раньше урона. Тот же порядок, что у удара, — сперва урон,
    // потом эффекты, и те, что идут через рану, только если рана есть.
    [HarmonyPatch(typeof(Trap), "ApplyHitEffect")]
    internal static class TrapHit_Wounds_Patch
    {
        private static bool Prefix(Trap __instance, UnitAttribute unit)
        {
            if (Wounds.Enabled == null || !Wounds.Enabled.Value) return true;
            if (__instance == null || unit == null || __instance.attack == null) return true;

            try
            {
                Attack attack = __instance.attack;

                if (attack.force != 0f && unit.rig != null)
                {
                    Vector3 way = ((attack.force > 0f)
                        ? (unit.transform.position - __instance.transform.position)
                        : (__instance.transform.position - unit.transform.position)).normalized;

                    float force = Mathf.Abs(attack.force) * unit.rig.drag / 10f;
                    unit.ApplyForceEffect(force, attack.forceType, way);
                }

                attack.realDamage = 9999f;
                unit.TakeDamage(attack);

                bool hurt = attack.realDamage > 0f && attack.realDamage < 9999f;

                // Капкан, пробивший доспех, портит его так же, как клинок: на десятую долю
                // прошедшего сквозь железо. Не пробивший — не портит.
                try
                {
                    Inventory plate = Anatomy.WornKit(unit, Anatomy.Spot(attack, unit));
                    Tatter.Struck(unit, attack, plate);
                }
                catch
                {
                }

                if (attack.buff.Count != 0 && unit.buffmanger != null && !unit.Data.isdead)
                {
                    foreach (BuffBase item in attack.buff)
                    {
                        if (item == null) continue;

                        if (!hurt && Wounds.Needs(item))
                        {
                            Wounds.Kept(unit, item, "капкан");
                            continue;
                        }

                        BuffBase made = new BuffBase(item.buffInfo);
                        made.Clone(item);
                        unit.buffmanger.AddBuff(made);
                    }
                }

                return false;
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Капкан сорвался: " + e.Message);
                return true;
            }
        }
    }
}
