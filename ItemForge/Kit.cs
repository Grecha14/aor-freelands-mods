using System;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using spell;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// The medical kit, and the reason to carry one into the field.
    ///
    /// Медицина в этой игре — ремесло для лагеря: перевязать после боя, полежать в лазарете.
    /// В самом бою она не значила ничего, потому что и перевязывать было нечего — здоровье
    /// возвращалось зельем, а раны отменялись сами.
    ///
    /// Теперь есть что: сломанная рука не срастётся сама и не выпьется зельем, её надо взять в
    /// лубок, и сделать это может только тот, кто умеет, и только тем, что взял с собой. Отсюда
    /// и смысл таскать аптечку, и смысл качать медицину: чем лекарь умелее, тем быстрее срастётся.
    ///
    /// Аптечкой считается всё, что лечит саму рану, а не запас сил: в игре это вещи с возвратом
    /// здоровья — бинты, мази, снадобья лекаря. Еда сюда не входит, она кормит.
    /// </summary>
    internal static class Kit
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> Sets;
        internal static ConfigEntry<float> Mends;
        internal static ConfigEntry<float> Learns;
        internal static ConfigEntry<float> PerMedic;
        internal static ConfigEntry<float> PerHealed;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Kit", "Enabled", true,
                "Let a medical kit do in the field what only a surgeon could do before: set a "
                + "broken bone. This is what makes the medicine skill worth raising and the kit "
                + "worth carrying.");

            Sets = config.Bind("Kit", "Sets", true,
                "Let one kit set one bone — the worst one first. A set bone still hurts, but it "
                + "knits from then on, and it knits faster the better the hand that set it.");

            Mends = config.Bind("Kit", "Mends", 1.5f,
                new ConfigDescription(
                    "Health of the body restored per point the kit mends of the wound itself. "
                    + "Half again: bandaging closes what was opened, and what it closes is real.",
                    new AcceptableValueRange<float>(0f, 10f)));

            PerMedic = config.Bind("Kit", "PerMedic", 0.01f,
                new ConfigDescription(
                    "How much faster a kit pours in for each point of medicine the one using it "
                    + "has. A hundredth a point: at fifty the same bandage closes the wound in "
                    + "twenty seconds instead of thirty, at a hundred in fifteen. It pours in "
                    + "the same amount either way — the skill is in the speed, not in the "
                    + "quantity.",
                    new AcceptableValueRange<float>(0f, 0.05f)));

            PerHealed = config.Bind("Kit", "PerHealed", 0.5f,
                new ConfigDescription(
                    "Medicine learned for each point of health mended by hand. This is the "
                    + "ordinary way the skill grows: by treating people.",
                    new AcceptableValueRange<float>(0f, 10f)));

            Learns = config.Bind("Kit", "Learns", 3f,
                new ConfigDescription(
                    "Medicine learned for setting a bone. The game credits a little for every "
                    + "kit used; this is for the part of the work that is actually difficult.",
                    new AcceptableValueRange<float>(0f, 50f)));
        }

        /// <summary>Whether this is something a physician uses, rather than something eaten.</summary>
        internal static bool Medical(UIConsumableInfo what)
        {
            if (what == null) return false;
            if (what.consumableType == consumableType.food) return false;
            if (what.consumableType == consumableType.drink) return false;

            return what.healthRestore > 0f;
        }

        /// <summary>Someone has been treated in the field. See what it was worth.</summary>
        internal static void Used(UnitAttribute hand, UnitAttribute mark, UIConsumableInfo what)
        {
            if (Enabled == null || !Enabled.Value || !Medical(what)) return;
            if (mark == null) return;

            try
            {
                HumaniodUnit healer = hand as HumaniodUnit;

                int skill = 0;
                if (healer != null && healer.Data != null) skill = healer.Data.medical;

                // Перевязка возвращает и саму жизнь, не только полосу ран. Вливается она
                // так же, как всё прочее лечение, — не разом, а покуда идёт перевязка. Умение
                // лекаря здесь решает скорость: он делает то же самое, но быстрее.
                if (Mends.Value > 0f)
                {
                    float much = what.healthRestore * Mends.Value;

                    Knit.Pour(mark, much, 1f + skill * PerMedic.Value);

                    // И учится он на этом же: за каждое возвращённое очко.
                    if (healer != null && PerHealed.Value > 0f)
                    {
                        healer.GainProfessionExp(12, much * PerHealed.Value);
                    }
                }

                if (!Sets.Value) return;

                int worst = Break.Worst(mark);
                if (worst < 0) return;

                if (!Break.Treat(mark, worst, skill)) return;

                if (healer != null && Learns.Value > 0f)
                {
                    healer.GainProfessionExp(12, Learns.Value);
                }
            }
            catch
            {
            }
        }
    }

    // Аптечка в поле: игра уже раздала своё, а кости остались нам.
    [HarmonyPatch(typeof(BehavUseItem), "ExecuteBehav")]
    internal static class UseItem_Kit_Patch
    {
        private static void Postfix(BehavUseItem __instance)
        {
            try
            {
                if (__instance == null || __instance.caster == null) return;

                UIConsumableInfo what = __instance.caster.item as UIConsumableInfo;
                if (what == null) return;

                UnitAttribute mark = __instance.caster.targetUnit != null
                    ? __instance.caster.targetUnit
                    : __instance.caster.user;

                Kit.Used(__instance.caster.user, mark, what);
            }
            catch
            {
            }
        }
    }
}
