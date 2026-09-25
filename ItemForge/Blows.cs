using System;
using System.Runtime.CompilerServices;
using DuloGames.UI;
using BepInEx.Configuration;
using HarmonyLib;
using spell;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Every swing lands a little differently, and a blow in the back is a different blow.
    ///
    /// Один и тот же меч в одной и той же руке бьёт не одинаково. Из десяти замахов один-два
    /// приходятся вскользь — плашмя, по касательной, — и берут половину; один-два ложатся
    /// точно, всем весом, и берут в полтора раза больше; остальные — как обычно. Считается от
    /// обычного урона оружия, до всякой брони, и одинаково для всех, кого задел один замах.
    ///
    /// И куда пришёлся удар. В лицо человек встречает его щитом, клинком, плечом; в бок —
    /// уже не весь; в спину — не встречает ничем. Удар в спину вдвое против того, что вышло бы
    /// спереди, в бок — в полтора раза, и множится это уже на итог, после всех прибавок и
    /// всех вычетов. Тот, кто прорывается мимо строя к магам, подставляет строю бока и спину.
    /// </summary>
    internal static class Blows
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> WeakChance;
        internal static ConfigEntry<float> StrongChance;
        internal static ConfigEntry<float> Weak;
        internal static ConfigEntry<float> Strong;
        internal static ConfigEntry<float> Back;
        internal static ConfigEntry<float> Side;
        internal static ConfigEntry<bool> Tags;
        internal static ConfigEntry<float> Knockout;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Blows", "Enabled", true,
                "Let each swing land weak, plain or strong, and let blows from behind and from "
                + "the side cost more.");

            WeakChance = config.Bind("Blows", "WeakChance", 0.15f,
                new ConfigDescription("How often a swing glances off, as a share.",
                    new AcceptableValueRange<float>(0f, 1f)));

            StrongChance = config.Bind("Blows", "StrongChance", 0.15f,
                new ConfigDescription("How often a swing lands with all its weight, as a share.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Weak = config.Bind("Blows", "Weak", 0.5f,
                new ConfigDescription("What a glancing swing takes of the weapon ordinary damage.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Strong = config.Bind("Blows", "Strong", 1.5f,
                new ConfigDescription("What a full swing takes of the weapon ordinary damage.",
                    new AcceptableValueRange<float>(1f, 5f)));

            Back = config.Bind("Blows", "Back", 2f,
                new ConfigDescription("How many times the final damage of a blow in the back.",
                    new AcceptableValueRange<float>(1f, 10f)));

            Side = config.Bind("Blows", "Side", 1.5f,
                new ConfigDescription("How many times the final damage of a blow in the side.",
                    new AcceptableValueRange<float>(1f, 10f)));

            Knockout = config.Bind("Blows", "Knockout", 180f,
                new ConfigDescription("Seconds a man stays senseless after a crushing crit that did not "
                    + "kill him; nought turns it off. A weak club is how one takes a man alive.",
                    new AcceptableValueRange<float>(0f, 3600f)));

            Tags = config.Bind("Blows", "Tags", true,
                "Write over the one struck how the blow landed — out of the dark, in the back, "
                + "full or glancing — where the player people strike or are struck.");
        }

        private sealed class Roll { internal float times = 1f; }

        // Ключ — сам урон замаха. Объект атаки у оружия один на все его удары, а урон игра на
        // каждый замах заново снимает с оружия копией: эта копия и есть замах.
        private static readonly ConditionalWeakTable<DamageBase, Roll> rolled = new ConditionalWeakTable<DamageBase, Roll>();

        /// <summary>How this swing landed: drawn once per swing, however many it strikes.</summary>
        internal static float Swing(DamageBase swing)
        {
            if (swing == null) return 1f;

            Roll mine;
            if (rolled.TryGetValue(swing, out mine)) return mine.times;

            mine = new Roll();
            float dice = UnityEngine.Random.value;

            if (dice < WeakChance.Value) mine.times = Weak.Value;
            else if (dice < WeakChance.Value + StrongChance.Value) mine.times = Strong.Value;

            rolled.Add(swing, mine);
            return mine.times;
        }

        internal static bool Weapon(Attack attack)
        {
            return attack != null && attack.isweaponAttack && attack.weapon != null
                && attack.attacker != null && attack.damage != null;
        }

        /// <summary>How much more a blow costs by where it came from.</summary>
        internal static float Facing(UnitAttribute victim, Attack attack)
        {
            if (victim == null || attack == null || attack.attacker == null) return 1f;
            if ((object)attack.attacker == (object)victim) return 1f;
            if (Pierce.Spell(attack) || Pierce.Inner(attack, victim)) return 1f;

            AttackDirection from = UnitAttribute.GetDirection(victim, attack.attacker);

            if (from == AttackDirection.behind) return Back.Value;
            if (from == AttackDirection.left || from == AttackDirection.right) return Side.Value;
            return 1f;
        }

        internal static bool Ours(UnitAttribute a, UnitAttribute b)
        {
            return (a != null && a.inParty) || (b != null && b.inParty);
        }

        internal static void Tag(UnitAttribute over, string said, string colour)
        {
            if (!Tags.Value || over == null || over.lifebar == null || string.IsNullOrEmpty(said)) return;
            try { over.lifebar.ShowTextTag("<color=" + colour + ">" + said + "</color>", 1.5f); } catch { }
        }

        internal sealed class Blow
        {
            internal DamageBase was;
            internal bool crit;
            internal float swing = 1f;
            internal float sneak = 1f;
            internal float soft = 1f;
        }
    }

    // Как лёг замах и видел ли его тот, кому он достался: до всякой брони, по обычному урону
    // оружия. Урон подменяется копией на время одного приёма удара — следующему, кого задел
    // тот же замах, достаётся нетронутый.
    [HarmonyPatch(typeof(UnitAttribute), "TakeDamage")]
    [HarmonyPriority(Priority.First)]
    internal static class TakeDamage_Blows_Patch
    {
        private static void Prefix(UnitAttribute __instance, Attack attack, bool directDamage, out Blows.Blow __state)
        {
            __state = null;
            if (directDamage || Blows.Enabled == null || !Blows.Enabled.Value) return;

            try
            {
                if (!Blows.Weapon(attack) || __instance == null || __instance.Data == null || __instance.Data.isdead) return;

                Blows.Blow blow = new Blows.Blow { was = attack.damage, crit = attack.isCrit };
                blow.swing = Blows.Swing(attack.damage);

                bool forced;
                blow.sneak = Shade.Sneak(__instance, attack, out forced);

                blow.soft = Sidestep.Softened(__instance, attack);

                float times = blow.swing * blow.sneak * blow.soft;
                if (Mathf.Approximately(times, 1f) && !forced)
                {
                    if (!Mathf.Approximately(blow.swing, 1f) || blow.sneak > 1.0001f || blow.soft < 0.9999f) __state = blow;
                    return;
                }

                attack.damage = attack.damage.Clone() * times;
                if (forced) attack.isCrit = true;
                __state = blow;
            }
            catch
            {
                __state = null;
            }
        }

        private static void Postfix(UnitAttribute __instance, Attack attack, bool __result, Blows.Blow __state)
        {
            if (!__result || Blows.Enabled == null || !Blows.Enabled.Value) return;

            // Кому досталось — тот знает, откуда. Игра сама раненого к обидчику не поворачивает,
            // и без этого из темноты можно было бы бить одного и того же раз за разом.
            try
            {
                if (Blows.Weapon(attack) && __instance != null && __instance.Data != null && !__instance.Data.isdead
                    && (object)attack.attacker != (object)__instance && !__instance.sensedUnit.Contains(attack.attacker)
                    && !GameController.IsAlly(__instance, attack.attacker))
                {
                    __instance.AddSense(attack.attacker);
                }
            }
            catch
            {
            }

            // Крит дробящим, не убивший, — человек без памяти на несколько минут.
            try
            {
                if (Blows.Knockout.Value > 0f && attack.isCrit && Blows.Weapon(attack) && __instance is HumaniodUnit
                    && __instance.Data != null && !__instance.Data.isdead && Breach.Crushing(attack)
                    && __instance.buffmanger != null && !__instance.buffmanger.ContainBuff("Knockout"))
                {
                    UIBuffInfo knock = UIBuffDatabase.Instance != null ? UIBuffDatabase.Instance.GetByID("Knockout") : null;
                    if (knock != null)
                    {
                        __instance.buffmanger.AddBuff(new BuffBase(knock, attack.attacker, Blows.Knockout.Value));
                        if (Blows.Ours(__instance, attack.attacker)) Blows.Tag(__instance, "Без сознания", "#C8B070");
                    }
                }
            }
            catch
            {
            }

            if (!Blows.Tags.Value) return;

            try
            {
                if (!Blows.Weapon(attack) || !Blows.Ours(__instance, attack.attacker)) return;

                float facing = Blows.Facing(__instance, attack);
                float swing = __state != null ? __state.swing : 1f;
                bool sneak = __state != null && __state.sneak > 1.0001f;

                string said = "";
                if (sneak) said = "Из тени";
                if (facing >= Blows.Back.Value - 0.001f && facing > 1.0001f) said = Join(said, "в спину");
                if (swing > 1.0001f) said = Join(said, "всем весом");
                else if (swing < 0.9999f) said = Join(said, "вскользь");
                if (__state != null && __state.soft < 0.9999f) said = Join(said, "смягчён шагом");

                if (said.Length == 0) return;

                string colour = sneak ? "#FF3C3C" : facing > 1.0001f ? "#FF7A3C" : swing > 1.0001f ? "#FFB43C" : "#A0A0A0";
                Blows.Tag(__instance, char.ToUpper(said[0]) + said.Substring(1), colour);
            }
            catch
            {
            }
        }

        private static string Join(string said, string more)
        {
            return said.Length == 0 ? more : said + ", " + more;
        }

        private static void Finalizer(Attack attack, Blows.Blow __state)
        {
            if (__state == null || attack == null) return;
            attack.damage = __state.was;
            attack.isCrit = __state.crit;
        }
    }

    // Откуда пришёл удар — на итог. Всё, что игра ещё множит после брони, — таланты, раса,
    // казнь, — множит и это: от перестановки итог не меняется.
    [HarmonyPatch(typeof(UnitAttribute), "DamageReduce")]
    [HarmonyPriority(Priority.VeryLow)]
    internal static class DamageReduce_Facing_Patch
    {
        private static void Postfix(UnitAttribute __instance, Attack attack, ref DamageBase __result)
        {
            if (Blows.Enabled == null || !Blows.Enabled.Value || __result == null || attack == null) return;

            try
            {
                float times = Blows.Facing(__instance, attack);
                if (times > 1.0001f) __result *= times;
            }
            catch
            {
            }
        }
    }
}
