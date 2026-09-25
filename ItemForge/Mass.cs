using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using HarmonyLib;
using spell;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// What a man himself weighs, and why that is the whole of standing your ground.
    ///
    /// Вес в этой игре есть только у вещей. Самого человека не весит никто, и оттого щуплый
    /// лучник опрокидывается от удара ровно так же, как двухметровый бугай в латах, — а
    /// опрокидывание тут не считается вовсе, оно просто накладывается, как насморк.
    ///
    /// Здесь у человека появляется свой вес. За основу взяты живые числа: мужчина ростом
    /// сто семьдесят пять сантиметров весит около семидесяти пяти килограммов, женщина ростом
    /// сто шестьдесят пять — около шестидесяти двух. Дальше поправка на породу (гном тяжелее
    /// человека при меньшем росте, фея легче ребёнка) и на рост существа, а сила прибавляет
    /// по полпроцента за очко: это мышцы, и они весят.
    ///
    /// На переносимый груз этот вес не влияет никак — своё тело человек носит бесплатно.
    /// Влияет он на одно: устоит ли он, когда его толкают. Тяжёлого сдвинуть труднее.
    /// </summary>
    internal static class Mass
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Races;
        internal static ConfigEntry<float> Woman;
        internal static ConfigEntry<string> Sizes;
        internal static ConfigEntry<float> PerMight;
        internal static ConfigEntry<float> Worn;
        internal static ConfigEntry<float> Push;
        internal static ConfigEntry<float> Share;
        internal static ConfigEntry<string> Falls;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Mass", "Enabled", true,
                "Give a man a weight of his own, so that knocking him off his feet depends on "
                + "who he is. The game weighs only what he carries.");

            Races = config.Bind("Mass", "Races",
                "human=75,elf=65,dwarf=72,bruteman=105,lizard=85,fairy=20,demon=110,"
                + "undead=65,orc=100,mythological=140",
                "What a grown male of each kind weighs, in kilograms, at the middling height of "
                + "his kind. Man is the measure: a hundred and seventy five centimetres and "
                + "seventy five kilograms, which is a real man of real build and not a heroic "
                + "one. A dwarf is shorter and heavier for it, a fairy weighs what a cat does.");

            Woman = config.Bind("Mass", "Woman", 0.83f,
                new ConfigDescription(
                    "What a woman weighs against a man of her kind. Five sixths: a hundred and "
                    + "sixty five centimetres and sixty two kilograms against his seventy five.",
                    new AcceptableValueRange<float>(0.3f, 1.5f)));

            Sizes = config.Bind("Mass", "Sizes", "Small=0.35,Medium=1,Large=2.2,Giant=6,"
                + "Titanic=20",
                "And what the size of the creature does to that. Weight goes by the cube of "
                + "height, so a thing half again as tall is not half again as heavy but three "
                + "times so.");

            PerMight = config.Bind("Mass", "PerMight", 0.005f,
                new ConfigDescription(
                    "What one point of strength adds to a man's own weight, as a share. Half a "
                    + "hundredth: at fifty strength a man is a quarter heavier than his own "
                    + "frame, and that is muscle.",
                    new AcceptableValueRange<float>(0f, 0.02f)));

            Worn = config.Bind("Mass", "Worn", 1f,
                "How much of what a man wears and carries counts toward the weight that has to "
                + "be shifted to put him down. In full: forty kilograms of harness are forty "
                + "kilograms to shove, and that is half the reason to wear them.");

            Push = config.Bind("Mass", "Push", 0.08f,
                new ConfigDescription(
                    "How many kilograms of shove one point of a weapon's force is worth. "
                    + "Force is no longer a number written beside a weapon but the weight "
                    + "swinging it, so this is what turns one into the other: at twelve "
                    + "hundredths a plain sword shoves like eight kilograms and a two-handed maul of "
                    + "the fifth tier like a hundred and thirty — so only a heavy man in heavy "
                    + "harness has any chance of standing under the second, and almost anyone "
                    + "stands under the first.",
                    new AcceptableValueRange<float>(0f, 2f)));

            Share = config.Bind("Mass", "Share", 0.75f,
                new ConfigDescription(
                    "The most of his weight advantage a man may ever turn into staying upright. "
                    + "Three quarters: however heavy, he can be put down.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Falls = config.Bind("Mass", "Falls", "Knockdown",
                "Which afflictions weight lets a man shrug off, by their name in the game.");

            Telling = config.Bind("Mass", "Telling", false,
                "Write down every shrug and every fall.");
        }

        private static readonly Dictionary<string, float> races = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> sizes = new Dictionary<string, float>();
        private static string racesRead;
        private static string sizesRead;

        private static float Table(Dictionary<string, float> into, ref string was,
            ConfigEntry<string> from, string key, float plain)
        {
            string written = from != null ? (from.Value ?? "") : "";

            if (written != was)
            {
                was = written;
                into.Clear();

                foreach (string one in written.Split(','))
                {
                    int split = one.IndexOf('=');
                    if (split <= 0) continue;

                    float much;
                    if (float.TryParse(one.Substring(split + 1).Trim(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out much))
                    {
                        into[one.Substring(0, split).Trim().ToLowerInvariant()] = much;
                    }
                }
            }

            float got;
            return into.TryGetValue(key.ToLowerInvariant(), out got) ? got : plain;
        }

        /// <summary>
        /// What this one weighs altogether: himself and everything he has on.
        ///
        /// Сдвинуть надо не человека, а человека в доспехе: латник весит на треть больше
        /// себя самого, и упереться ему есть чем. Оттого в счёт устойчивости идёт полный вес,
        /// а в счёт замаха — только тело и рукава, потому что остальное железо в замахе не
        /// участвует.
        /// </summary>
        internal static float Of(UnitAttribute who)
        {
            float much = Bare(who);

            if (Worn == null || Worn.Value <= 0f) return much;

            try
            {
                HumaniodUnit man = who as HumaniodUnit;
                EquipInfo[] kit = man != null && man.equipmentmanger != null
                    ? man.equipmentmanger.equipInfos : null;

                if (kit == null) return much;

                foreach (EquipInfo one in kit)
                {
                    if (one == null || !one.IsEquiped() || one.inventory == null) continue;
                    if (one.inventory.itemInfo == null) continue;

                    much += one.inventory.itemInfo.weight * Worn.Value;
                }
            }
            catch
            {
            }

            return much;
        }

        /// <summary>What this one weighs, himself, in kilograms.</summary>
        internal static float Bare(UnitAttribute who)
        {
            if (Enabled == null || !Enabled.Value || who == null) return 75f;

            try
            {
                float much = 75f;

                if (who.info != null)
                {
                    much = Table(races, ref racesRead, Races, who.info.race.ToString(), 75f);
                }

                much *= Table(sizes, ref sizesRead, Sizes, who.size.ToString(), 1f);

                HumaniodUnit man = who as HumaniodUnit;

                if (man != null && man.Data != null)
                {
                    if (man.Data.gender == UnitGender.female) much *= Woman.Value;

                    much *= 1f + man.Strength * PerMight.Value;
                }

                return Mathf.Max(1f, much);
            }
            catch
            {
                return 75f;
            }
        }

        /// <summary>How hard this one shoves, in the same kilograms.</summary>
        internal static float Shove(UnitAttribute who)
        {
            if (who == null) return 50f;

            try
            {
                float force = 0f;

                if (who.weapons != null)
                {
                    foreach (Weapon arm in who.weapons)
                    {
                        if (arm != null && arm.weaponForce > force) force = arm.weaponForce;
                    }
                }

                // Безоружный толкает собой: половиной своего веса.
                if (force <= 0f) return Of(who) * 0.5f;

                return force * Push.Value;
            }
            catch
            {
                return 50f;
            }
        }

        private static string[] falls;
        private static string fallsRead;

        internal static bool Topples(string id)
        {
            string written = Falls.Value ?? "";

            if (written != fallsRead || falls == null)
            {
                fallsRead = written;
                falls = written.Split(',');

                for (int i = 0; i < falls.Length; i++) falls[i] = falls[i].Trim();
            }

            foreach (string one in falls)
            {
                if (one.Length > 0 && string.Equals(one, id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether this one keeps his feet under that shove.</summary>
        internal static bool Stands(UnitAttribute who, UnitAttribute from)
        {
            if (Enabled == null || !Enabled.Value || who == null) return false;

            try
            {
                float mine = Of(who);
                float theirs = Shove(from);

                if (mine <= theirs) return false;

                float sure = Mathf.Clamp01((mine - theirs) / mine) * Share.Value;

                bool stood = UnityEngine.Random.value < sure;

                if (Telling.Value && who.Data != null)
                {
                    ItemForgePlugin.Log.LogInfo($"«{who.Data.unitname}» {mine:0} кг против "
                        + $"толчка в {theirs:0}: {(stood ? "устоял" : "сбит")} "
                        + $"(шанс устоять {sure * 100f:0}%).");
                }

                return stood;
            }
            catch
            {
                return false;
            }
        }
    }

    // Опрокидывание в этой игре накладывается, а не считается: это порча, которую вешают на
    // бойца. Значит и стряхивается она там же, где вешается.
    [HarmonyPatch(typeof(BuffManager), "AddBuff", new[] { typeof(BuffBase) })]
    internal static class AddBuff_Mass_Patch
    {
        private static bool Prefix(BuffManager __instance, BuffBase buff)
        {
            try
            {
                if (Mass.Enabled == null || !Mass.Enabled.Value) return true;
                if (__instance == null || buff == null || __instance.unit == null) return true;
                if (!Mass.Topples(buff.id)) return true;

                if (!Mass.Stands(__instance.unit, buff.creater)) return true;

                try
                {
                    if (__instance.unit.lifebar != null)
                    {
                        __instance.unit.lifebar.ShowTextTag("<color=#C0C0C0FF>устоял</color>", 1f);
                    }
                }
                catch
                {
                }

                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}
