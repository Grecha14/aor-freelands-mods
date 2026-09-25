using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using spell;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Makes a good potion worth carrying.
    ///
    /// Зелье с бафом в этой игре почти не зависит от того, какого оно яруса: пятый даёт немногим
    /// больше первого, и разница между дешёвой склянкой и редкой настойкой — в цене, а не в том,
    /// что происходит после глотка. Оттого их и не пьют: проще выпить что попало.
    ///
    /// Здесь ярус решает. Первый удваивает написанное, второй утраивает, и так до пятого. Правится
    /// при этом не зелье и не шаблон бафа, а тот один-единственный баф, который сейчас
    /// накладывается: шаблон в игре общий, один и тот же баф стоит на склянках разных ярусов, и
    /// правка шаблона сделала бы слабое зелье сильным задним числом.
    ///
    /// И правится не значение внутри чужого объекта, а сам объект подменяется на новый с новым
    /// числом. Разница важная: «BuffBase» копирует список приписок, но не сами приписки — они
    /// остаются общими с шаблоном, и умножить их на месте значило бы испортить баф навсегда.
    ///
    /// Длительность не трогаем: сильнее — не значит дольше.
    /// </summary>
    internal static class Draught
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Ladder;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Draught", "Enabled", true,
                "Let the tier of a potion decide how much its blessing is worth. Only potions "
                + "that carry a buff are touched; what merely restores health or hunger is left "
                + "as it is.");

            Ladder = config.Bind("Draught", "Ladder", "1,2,3,4,5,6",
                "What each tier multiplies the buff by, from T0 to T5. A first-tier draught is "
                + "worth double what is written on it, a fourth-tier one five times. T5 carries "
                + "the same step onward — nobody asked for it, so it is a guess and meant to be "
                + "tuned.");
        }

        // Что сейчас пьётся. Держится ровно от начала глотка до конца, и снимается в любом случае.
        private static UIBuffInfo awaited;
        private static float much = 1f;

        /// <summary>Remembers what is being drunk, so the blessing can be measured against it.</summary>
        internal static void Sip(SpellCaster caster)
        {
            Done();

            if (!Enabled.Value || caster == null) return;

            try
            {
                UIConsumableInfo drink = caster.item as UIConsumableInfo;
                if (drink == null || drink.buff == null) return;

                float[] steps = Steps();
                int step = (int)drink.tier;
                if (step < 0 || step >= steps.Length) return;

                if (Mathf.Approximately(steps[step], 1f)) return;

                awaited = drink.buff;
                much = steps[step];
            }
            catch
            {
                Done();
            }
        }

        /// <summary>Pours the tier into the blessing, if this is the one the draught promised.</summary>
        internal static void Pour(BuffBase buff)
        {
            if (awaited == null || buff == null) return;

            try
            {
                if (buff.buffInfo != awaited) return;
                if (buff.addAttrs == null || buff.addAttrs.Count == 0) { Done(); return; }

                for (int i = 0; i < buff.addAttrs.Count; i++)
                {
                    AddonAttributes one = buff.addAttrs[i];
                    if (one == null) continue;

                    // Новый объект, а не правка старого: этот живёт в шаблоне и общий для всех.
                    buff.addAttrs[i] = new AddonAttributes(
                        one.type, one.value * much, one.levelAlter * much);
                }
            }
            catch
            {
            }
            finally
            {
                Done();
            }
        }

        internal static void Done()
        {
            awaited = null;
            much = 1f;
        }

        private static float[] steps;
        private static string read;

        private static float[] Steps()
        {
            string written = Ladder.Value ?? "";
            if (steps != null && written == read) return steps;

            List<float> got = new List<float>();

            foreach (string one in written.Split(','))
            {
                float value;
                if (float.TryParse(one.Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out value))
                {
                    got.Add(Mathf.Clamp(value, 0.1f, 50f));
                }
            }

            while (got.Count < 6) got.Add(1f);

            read = written;
            steps = got.ToArray();
            return steps;
        }
    }

    // Глоток. Здесь известно, что именно пьют; дальше баф накладывается уже без этого знания,
    // поэтому ярус запоминается на время одного действия.
    [HarmonyPatch(typeof(BehavUseItem), "ExecuteBehav")]
    internal static class UseItem_Draught_Patch
    {
        private static void Prefix(BehavUseItem __instance)
        {
            try { Draught.Sip(__instance == null ? null : __instance.caster); }
            catch { Draught.Done(); }
        }

        private static void Postfix()
        {
            Draught.Done();
        }
    }

    // Наложение бафа. Сюда приходит всё подряд — от талантов до заклинаний, — поэтому сверяемся
    // с тем самым бафом, который обещало выпитое, и только его и трогаем.
    [HarmonyPatch(typeof(BuffManager), "AddBuff", new Type[] { typeof(BuffBase) })]
    internal static class AddBuff_Draught_Patch
    {
        private static void Prefix(BuffBase buff)
        {
            Draught.Pour(buff);
        }
    }
}
