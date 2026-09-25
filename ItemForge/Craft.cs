using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// A trade a man can spend a life on, and be paid for the hours he spends.
    ///
    /// В игре пятнадцать ремёсел и у каждого потолок в пятнадцать ступеней. Опыт до
    /// следующей считается формулой «100 × 1,15^ур + 10 × ур» — не таблицей, а значит выше
    /// пятнадцати она работает сама, без правок. Потолок стоит в двух местах кода
    /// одинаковым числом, и только он и держит.
    ///
    /// Ремонт же не даёт кузнецу ничего: во всём методе починки нет ни одного начисления
    /// опыта. Кузнечное дело влияет на ковку и не влияет на то, чем кузнец занят каждый
    /// день. Здесь за работу платят: пять опыта за игровой час у наковальни.
    ///
    /// Про крутизну кривой. При множителе 1,15 все пятьдесят ступеней стоят 734 тысячи
    /// опыта — это сто сорок шесть тысяч игровых часов ремонта, шесть тысяч игровых суток.
    /// Потолок в пятьдесят при такой кривой недостижим и потому бессмыслен. Множитель
    /// вынесен в настройку, чтобы это можно было решить числом, а не разговором: при 1,075
    /// те же пятьдесят ступеней стоят 60 тысяч. По умолчанию оставлено как у игры — менять
    /// чужой баланс молча неправильно.
    /// </summary>
    internal static class Craft
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Ceiling;
        internal static ConfigEntry<int> Doubling;
        internal static ConfigEntry<float> Steepness;
        internal static ConfigEntry<float> PerHour;
        internal static ConfigEntry<string> Steps;
        internal static ConfigEntry<bool> ByTier;

        private static bool set;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Craft", "Enabled", true,
                "Raise what a trade can grow to, and pay a smith for the hours he works.");

            Ceiling = config.Bind("Craft", "Ceiling", 100,
                new ConfigDescription(
                    "How high a trade can go. The game stops every one of them at fifteen, "
                    + "which is a ladder a man finishes by midgame. A hundred is where a smith "
                    + "reaches his flawless hand, and nothing above it would mean anything.",
                    new AcceptableValueRange<int>(15, 9999)));

            Doubling = config.Bind("Craft", "Doubling", 500,
                new ConfigDescription(
                    "Every this many steps, the price of a step doubles on top of the usual "
                    + "climb. This was the brake back when there was no ceiling at all; with a "
                    + "hundred to stop a man there is nothing left for it to do, so it is set "
                    + "past any reachable step. Lower it to bring the bite back.",
                    new AcceptableValueRange<int>(1, 500)));

            Steepness = config.Bind("Craft", "Steepness", 1.065f,
                new ConfigDescription(
                    "How much dearer each step of a trade is than the one before it. The "
                    + "game's own number is 1.15, which puts the fiftieth step at seven "
                    + "hundred thousand — a ceiling nobody would ever reach. 1.075 puts the "
                    + "whole fifty at sixty thousand: long, and finite.",
                    new AcceptableValueRange<float>(1.01f, 1.5f)));

            Steps = config.Bind("Craft", "Steps", "1,2,6,24,120",
                "What each tier pays in experience against the first, T1 through T5. The game "
                + "doubles every step flat; here the step itself grows — the second tier is "
                + "twice the first, the third three times the second, the fourth four times the "
                + "third, the fifth five times the fourth. That is a factorial, and it says "
                + "plainly that a man learns his trade on hard work, not on nails.");

            PerHour = config.Bind("Craft", "PerHour", 5f,
                new ConfigDescription(
                    "Experience a smith earns for an hour of game time at the anvil.",
                    new AcceptableValueRange<float>(0f, 500f)));

            ByTier = config.Bind("Craft", "ByTier", true,
                "Multiply that experience by the tier of the thing worked on. Without this a "
                + "smith grows fastest on the cheapest jobs, because a good smith finishes "
                + "them sooner and is paid by the hour.");
        }

        /// <summary>
        /// Стоит на месте Mathf.Pow в игровой награде за ковку, с той же подписью.
        ///
        /// Игра платит два в степени тира. Здесь основание первой ступени оставлено игровым, а
        /// дальше каждый тир втрое дороже предыдущего: не подмастерье учится на гвоздях, а
        /// работа над сложным и учит всерьёз.
        /// </summary>
        internal static float Bench(float basis, float tier)
        {
            if (!Enabled.Value) return Mathf.Pow(basis, tier);

            float[] rungs = Rungs();
            if (rungs.Length == 0) return Mathf.Pow(basis, tier);

            int t = Mathf.RoundToInt(tier);
            if (t < 1) t = 1;

            int i = t - 1;
            if (i >= rungs.Length) i = rungs.Length - 1;

            // Первая ступень остаётся игровой: у неё множитель единица, и основание проходит
            // насквозь ровно тем, чем игра его и задумала.
            float much = basis * rungs[i];

            // Испорченная заготовка тоже учит, только меньше. Жребий уже брошен — он падает
            // на кладку вещи в сумку, а плата идёт следом.
            if (Chancery.Enabled != null && Chancery.Enabled.Value && Chancery.Missed)
            {
                much *= Mathf.Clamp01(Chancery.Spoiled.Value);
            }

            return much;
        }

        private static float[] rungs;
        private static string rungsRead;

        private static float[] Rungs()
        {
            string written = Steps.Value ?? "";

            if (written != rungsRead || rungs == null)
            {
                rungsRead = written;

                List<float> got = new List<float>();
                foreach (string one in written.Split(','))
                {
                    float much;
                    if (float.TryParse(one.Trim(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out much)) got.Add(much);
                }

                rungs = got.ToArray();
            }

            return rungs;
        }

        /// <summary>The ceiling, as the patched methods ask for it.</summary>
        internal static int Top()
        {
            return (Enabled.Value) ? Ceiling.Value : 15;
        }

        /// <summary>
        /// How much dearer this step is than the plain curve would make it.
        ///
        /// Ровная кривая рано или поздно упирается в то, что следующая ступень стоит
        /// примерно как предыдущая, и мастерство перестаёт что-либо значить. Потолок это
        /// лечил грубо — стеной. Здесь вместо стены цена: первые двадцать пять ступеней
        /// идут как шли, а дальше каждые двадцать пять цена шага удваивается. Предела нет,
        /// но и дойти до сотой ступени можно только жизнью, потраченной на ремесло.
        /// </summary>
        internal static float Steep(int level)
        {
            if (!Enabled.Value) return 1f;

            int each = Mathf.Max(1, Doubling.Value);
            int times = Mathf.Max(0, level / each);

            return Mathf.Pow(2f, Mathf.Min(times, 20));
        }

        /// <summary>Sets how dear each step is. Once.</summary>
        internal static void Shape()
        {
            if (set || !Enabled.Value) return;

            set = true;

            HumaniodUnit.PROFESSION_EXP_FACTOR = Steepness.Value;

            ItemForgePlugin.Log.LogInfo($"Ремёсла растут до {Ceiling.Value}, "
                + $"каждая ступень дороже предыдущей в {Steepness.Value:0.###} раза.");
        }

        /// <summary>Pays a smith for the hours he has just spent.</summary>
        internal static void Paid(UnitAttribute smith, float hours, ItemTier tier)
        {
            if (!Enabled.Value || smith == null || hours <= 0f) return;
            if (PerHour.Value <= 0f) return;

            try
            {
                HumaniodUnit hands = smith as HumaniodUnit;
                if (hands == null) return;

                float exp = hours * PerHour.Value;
                if (ByTier.Value) exp *= Mathf.Max(1, (int)tier);

                // Девятое ремесло — кузнечное. Порядок задан перечислением HumanUtilityType.
                hands.GainProfessionExp(9, exp);

                ItemForgePlugin.Log.LogInfo($"«{smith.Data.unitname}» получил "
                    + $"{exp:0.#} опыта кузнеца за {hours:0.#} ч работы.");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог заплатить кузнецу опытом: " + e.Message);
            }
        }
    }

    // Потолок ремесла стоит в коде числом. Правим не метод целиком, а само число: всё
    // прочее — начисление, сообщение, достижение — остаётся игровым и работает как было.
    [HarmonyPatch(typeof(HumaniodUnit), "GainProfessionExp")]
    internal static class GainProfessionExp_Unit_Patch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            return Ceilings.Lift(code);
        }
    }

    [HarmonyPatch(typeof(LivingSkill), "GainProfessionExp")]
    internal static class GainProfessionExp_Talent_Patch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            return Ceilings.Lift(code);
        }
    }

    // Цена следующей ступени. Игра считает её ровной кривой; мы домножаем на то, во
    // сколько раз ремесло стало дороже за пройденные четверть сотни ступеней.
    [HarmonyPatch(typeof(LivingSkill), "GetNextLVExp")]
    internal static class GetNextLVExp_Patch
    {
        private static void Postfix(LivingSkill __instance, int index, ref int __result)
        {
            try
            {
                if (!Craft.Enabled.Value) return;

                float much = Craft.Steep(__instance[index]);
                if (much <= 1f) return;

                __result = Mathf.RoundToInt(__result * much);
            }
            catch
            {
            }
        }
    }

    internal static class Ceilings
    {
        /// <summary>Replaces every literal fifteen with whatever the ceiling now is.</summary>
        internal static IEnumerable<CodeInstruction> Lift(IEnumerable<CodeInstruction> code)
        {
            foreach (CodeInstruction one in code)
            {
                if (one.opcode == OpCodes.Ldc_I4_S && one.operand is sbyte && (sbyte)one.operand == 15)
                {
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(Craft), "Top"));
                    continue;
                }

                if (one.opcode == OpCodes.Ldc_I4 && one.operand is int && (int)one.operand == 15)
                {
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(Craft), "Top"));
                    continue;
                }

                yield return one;
            }
        }
    }

    /// <summary>
    /// Опыт за ковку у верстака. Сама ковка живёт в сопрограмме, оттого правится её MoveNext,
    /// и правится бережно: не метод целиком, а один адрес вызова. Подпись у Bench та же, что у
    /// Mathf.Pow, и стек остаётся нетронутым.
    /// </summary>
    [HarmonyPatch]
    internal static class BenchExp_Craft_Patch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.EnumeratorMoveNext(
                AccessTools.Method(typeof(CraftManager), "DoCraft"));
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            MethodInfo was = AccessTools.Method(typeof(Mathf), "Pow",
                new[] { typeof(float), typeof(float) });
            MethodInfo now = AccessTools.Method(typeof(Craft), "Bench");

            int swapped = 0;

            foreach (CodeInstruction one in code)
            {
                if ((one.opcode == OpCodes.Call || one.opcode == OpCodes.Callvirt)
                    && one.operand as MethodInfo == was)
                {
                    swapped++;
                    yield return new CodeInstruction(OpCodes.Call, now);
                    continue;
                }

                yield return one;
            }

            if (swapped == 0)
            {
                ItemForgePlugin.Log.LogWarning("Опыт за тир остался игровым: степень в ковке "
                    + "не нашлась.");
            }
        }
    }
}
