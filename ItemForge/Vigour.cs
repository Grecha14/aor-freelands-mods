using System;
using BepInEx.Configuration;
using HarmonyLib;
using spell;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// A strong body throws off what a weak one carries: poison, fire, bleeding, hurt.
    ///
    /// Яд в этой игре не встречает сопротивления ни от чего, кроме колец: сопротивление ядам —
    /// это вещь на пальце, а не крепость тела, и здоровяк травится ровно как чахоточный. Срок
    /// отравы тоже один для всех.
    ///
    /// Между тем весь смысл выносливости в том и есть, чтобы перетерпеть. Здесь она даёт своё
    /// сопротивление яду — и укорачивает всё, что тело перемогает само: отраву, кровотечение,
    /// огонь на одежде, увечье.
    ///
    /// Заметим: часть этой работы у выносливости уже была и до нас, только спрятанная. Каждое
    /// её очко даёт очко стойкости, а стойкость укорачивает всякое телесное проклятие на свой
    /// процент. Здесь добавлено то, чего не было: сама сопротивляемость и особый срок для
    /// того, что грызёт изнутри.
    /// </summary>
    internal static class Vigour
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Poison;
        internal static ConfigEntry<float> Shorter;
        internal static ConfigEntry<string> Kinds;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Vigour", "Enabled", true,
                "Let endurance stand against poison and cut short what the body throws off by "
                + "itself. Poison resistance in this game comes off rings and nothing else, so "
                + "the hale are poisoned exactly as the sickly are.");

            Poison = config.Bind("Vigour", "Poison", 0.5f,
                new ConfigDescription(
                    "Poison resistance per point of endurance, in the game's own percent. Half a "
                    + "point: at fifty endurance a quarter of the venom does nothing, at a "
                    + "hundred a half. The game's ceiling of eighty stands.",
                    new AcceptableValueRange<float>(0f, 2f)));

            Shorter = config.Bind("Vigour", "Shorter", 0.005f,
                new ConfigDescription(
                    "How much shorter one point of endurance makes the things a body fights off "
                    + "by itself, as a share of their time. A two-hundredth: at fifty endurance "
                    + "a poison runs three quarters of its course, at a hundred a half. Never "
                    + "less than a fifth, however hale the man.",
                    new AcceptableValueRange<float>(0f, 0.02f)));

            Kinds = config.Bind("Vigour", "Kinds", "poisoned,bleeding,burning,injury",
                "Which kinds of affliction endurance shortens. What the body outlasts, not what "
                + "is done to it: a curse is not waited out by being hale.");
        }

        private const int PoisonAt = 6;

        /// <summary>Endurance as poison resistance, written where the game reads it.</summary>
        internal static void Steel(HumaniodUnit who)
        {
            if (Enabled == null || !Enabled.Value || Poison.Value <= 0f) return;

            try
            {
                if (who == null || who.damageResist == null) return;
                if (PoisonAt >= who.damageResist.Length) return;

                who.damageResist[PoisonAt] = Mathf.Clamp(
                    who.damageResist[PoisonAt] + who.Endurance * Poison.Value,
                    0f, UnitAttribute.DAMAGE_RESISTANCE_CAP);
            }
            catch
            {
            }
        }

        private static string[] kinds;
        private static string kindsRead;

        private static bool Outlasts(bufftype what)
        {
            string written = Kinds.Value ?? "";

            if (written != kindsRead || kinds == null)
            {
                kindsRead = written;
                kinds = written.Split(',');

                for (int i = 0; i < kinds.Length; i++) kinds[i] = kinds[i].Trim().ToLowerInvariant();
            }

            string name = what.ToString().ToLowerInvariant();

            foreach (string one in kinds)
            {
                if (one == name) return true;
            }

            return false;
        }

        /// <summary>How much of its course this affliction will actually run.</summary>
        internal static float Course(UnitAttribute who, bufftype what)
        {
            if (Enabled == null || !Enabled.Value || Shorter.Value <= 0f) return 1f;

            try
            {
                HumaniodUnit man = who as HumaniodUnit;
                if (man == null || !Outlasts(what)) return 1f;

                return Mathf.Clamp(1f - man.Endurance * Shorter.Value, 0.2f, 1f);
            }
            catch
            {
                return 1f;
            }
        }
    }

    [HarmonyPatch(typeof(HumaniodUnit), "WriteUnitAttribute")]
    internal static class Write_Vigour_Patch
    {
        private static void Postfix(HumaniodUnit __instance)
        {
            try
            {
                Vigour.Steel(__instance);
            }
            catch
            {
            }
        }
    }

    // Срок проклятия ставится при наложении — здесь его и укорачиваем.
    [HarmonyPatch(typeof(BuffManager), "AddBuff", new[] { typeof(BuffBase) })]
    internal static class AddBuff_Vigour_Patch
    {
        private static void Prefix(BuffManager __instance, BuffBase buff)
        {
            try
            {
                if (__instance == null || buff == null || buff.buffInfo == null) return;
                if (buff.duration <= 0f) return;

                float part = Vigour.Course(__instance.unit, buff.buffInfo.type);
                if (part >= 0.999f) return;

                buff.duration *= part;
            }
            catch
            {
            }
        }
    }
}
