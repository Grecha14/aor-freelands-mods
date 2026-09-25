using System;
using BepInEx.Configuration;
using HarmonyLib;

namespace ItemForge
{
    /// <summary>
    /// Makes every point of skill dearer than the one before it, and steeper than the game does.
    ///
    /// Игра просит за очко навыка «500 × 1,06^n», и n здесь — не ступень внутри школы, а
    /// число всех очков, уже вложенных во все школы и таланты разом. Прежде я думал иначе и
    /// поставил рост в треть за очко, считая по пятнадцать ступеней на школу. На общем счёте
    /// это взрывалось: к шестидесятому очку цена выходила за предел целого числа и
    /// становилась отрицательной — «требуемый опыт −2147483648», — а покупка по такой цене
    /// прибавляла два миллиарда вместо того, чтобы отнимать.
    ///
    /// Теперь рост мягче игрового всего вдвое — восемь сотых на очко вместо шести, — и цена
    /// считается с запасом и упирается в потолок числа, а не переваливает через него.
    /// </summary>
    internal static class Mastery
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Base;
        internal static ConfigEntry<float> Steepness;

        private static bool done;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Mastery", "Enabled", true,
                "Make a school of magic expensive to learn. The game asks five hundred "
                + "experience for the first of its fifteen ranks and eleven hundred for the "
                + "last, so mastery arrives by itself and means nothing.");

            Base = config.Bind("Mastery", "Base", 500,
                new ConfigDescription(
                    "What the first rank of a school costs. The game's own number, kept: the "
                    + "beginning should stay reachable, it is the end that should not.",
                    new AcceptableValueRange<int>(50, 20000)));

            Steepness = config.Bind("Mastery", "Steepness", 1.08f,
                new ConfigDescription(
                    "How much dearer each point of skill is than the one before it. The game "
                    + "counts every point already spent in every school and talent together, "
                    + "and asks base times this to that power: at the game's own one and six "
                    + "hundredths the eightieth point costs fifty-three thousand, at one and "
                    + "eight hundredths it costs two hundred and thirty-six thousand. The "
                    + "steepness compounds on the whole count, so a small change here is a "
                    + "large one at the top.",
                    new AcceptableValueRange<float>(1.01f, 1.2f)));
        }

        /// <summary>Steepens the cost of every rank, once, on the game's own numbers.</summary>
        internal static void Raise()
        {
            if (done || !Enabled.Value) return;

            try
            {
                done = true;

                HumaniodUnit.SKILL_EXP_BASE = Base.Value;
                HumaniodUnit.SKILL_EXP_LV_FACTOR = Steepness.Value;

                // Показываем, во что обошлась правка, на четырёх точках: начало, ступень
                // босса, ступень мага круга и цену последнего шага.
                ItemForgePlugin.Log.LogInfo("Школы подорожали: ступень 1 — "
                    + $"{Cost(0):n0}, ступень 8 — {Cost(7):n0}, ступень 12 — {Cost(11):n0}, "
                    + $"ступень 15 — {Cost(14):n0} опыта; вся школа — {Whole(15):n0}.");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог поднять цену школ: " + e);
            }
        }

        private static double Cost(int level)
        {
            return Base.Value * Math.Pow(Steepness.Value, level);
        }

        private static double Whole(int ranks)
        {
            double all = 0d;
            for (int i = 0; i < ranks; i++) all += Cost(i);
            return all;
        }
    }

    // Цена очка навыка считается в дробях и приводится к целому. Сверх предела целого числа
    // приведение даёт не предел, а самое отрицательное из чисел — и покупка по нему
    // прибавляет опыт. Считаем с запасом и упираемся в потолок.
    // Цена очка объявлена у общего предка всех персонажей, а не у НПС: правка, нацеленная
    // на НПС, не находила метода и не вставала вовсе.
    [HarmonyPatch(typeof(CharacterSaveData), "GetSkillLearnCost", new[] { typeof(int) })]
    internal static class SkillLearnCost_Mastery_Patch
    {
        private static bool Prefix(int level, ref int __result)
        {
            try
            {
                double cost = HumaniodUnit.SKILL_EXP_BASE
                    * Math.Pow(HumaniodUnit.SKILL_EXP_LV_FACTOR, Math.Max(0, level));

                if (double.IsNaN(cost) || cost < 1d) cost = 1d;
                if (cost > int.MaxValue - 1) cost = int.MaxValue - 1;

                __result = (int)cost;
                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}
