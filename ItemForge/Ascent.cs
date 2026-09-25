using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Takes the lid off the level.
    ///
    /// Уровень в этой игре не начисляется, а выводится: «SetLevel» проходит по шести
    /// характеристикам, за каждую ступень выше пятой добавляет от одного до трёх очков и делит
    /// сумму на пять. Числом его записать нельзя — сотрётся при первом же пересчёте. И вот
    /// у этого вывода в конце стоит зажим: не выше девяносто девятого, что бы ни было вложено.
    ///
    /// Зажим не бережёт ничего. Он просто обрывает лестницу там, где до её конца ещё далеко:
    /// при девяноста девяти в каждой из шести характеристик формула даёт триста двадцать
    /// четвёртый уровень, и весь путь от сотого до него не существует. А от уровня зависит не
    /// строчка в окне: «ApplyUnitLevelBonus» выдаёт существу и запас здоровья, и урон
    /// пропорционально тому, насколько оно переросло свой шаблон.
    ///
    /// Здесь потолок поднимается, и лестница идёт до конца. Сами характеристики и мастерство
    /// остаются с прежним пределом в девяносто девять: их полосы нарисованы под сотню и выше
    /// неё поедут, а уровню ширины хватает и так.
    /// </summary>
    internal static class Ascent
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Ceiling;

        private static bool raised;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Ascent", "Enabled", true,
                "Take the ninety-ninth level off as a ceiling. The level is worked out from the "
                + "six attributes rather than handed out, and the formula runs to three hundred "
                + "and twenty-four before it is clamped back down to ninety-nine.");

            Ceiling = config.Bind("Ascent", "Ceiling", 999,
                new ConfigDescription(
                    "The new ceiling: nine hundred and ninety-nine. The arena ladder runs its "
                    + "heroes up to five hundred, and nothing is meant to stand above the "
                    + "thousandth.",
                    new AcceptableValueRange<int>(1, 99999)));
        }

        /// <summary>Raises the game's own idea of the highest level there is, once.</summary>
        internal static void Lift()
        {
            if (raised || !Enabled.Value) return;

            try
            {
                // Число живёт в статическом поле и читается в паре мест: при отборе бойцов на
                // арену и при рождении героя. Поднимаем его вместе с зажимом, иначе эти двое
                // продолжат считать, что выше девяносто девятого никого не бывает.
                if (UnitAttribute.MAX_LEVEL >= Ceiling.Value) { raised = true; return; }

                int was = UnitAttribute.MAX_LEVEL;
                UnitAttribute.MAX_LEVEL = Ceiling.Value;
                raised = true;

                ItemForgePlugin.Log.LogInfo($"Потолок уровня снят: было {was}, "
                    + $"стало {UnitAttribute.MAX_LEVEL}.");
            }
            catch (Exception e)
            {
                raised = true;
                ItemForgePlugin.Log.LogWarning("Не смог снять потолок уровня: " + e.Message);
            }
        }

        /// <summary>The game's own sum, without the lid at the end of it.</summary>
        internal static int Reach(HumanAttribute mind)
        {
            int total = 0;

            for (int i = 0; i < 6; i++)
            {
                for (int step = mind[i] - 1; step >= 0; step--)
                {
                    total += Mathf.Min(step / 5, 3);
                }
            }

            return Mathf.Clamp(total / 5, 1, Mathf.Max(1, Ceiling.Value));
        }
    }

    // Вывод уровня повторяется слово в слово — та же лестница по пять ступеней и тот же
    // делитель, — и отличается ровно одним: верхней границей. Считать заново дешевле и надёжнее,
    // чем разбирать чужой результат: из зажатых девяноста девяти уже не узнать, сколько было.
    [HarmonyPatch(typeof(NPCSaveData), "SetLevel")]
    internal static class SetLevel_Ascent_Patch
    {
        private static void Postfix(NPCSaveData __instance)
        {
            if (!Ascent.Enabled.Value) return;

            try
            {
                if (__instance == null || __instance.humanAttribute == null) return;
                if (__instance.level < 99) return;          // до потолка дело не дошло

                __instance.level = Ascent.Reach(__instance.humanAttribute);
            }
            catch
            {
            }
        }
    }
}
