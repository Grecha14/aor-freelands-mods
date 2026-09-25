using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// The arena as a ladder of five rungs, and a man is as strong as his rung says.
    ///
    /// Ступени — пять, у каждой свой разброс уровней: первая от одного до десяти, вторая от
    /// двадцати до тридцати, третья от пятидесяти до восьмидесяти, четвёртая от сотни до полутора,
    /// пятая от двухсот до трёхсот. Герой с легендарным снаряжением — от трёхсот до пятисот.
    ///
    /// Уровень в этой игре не записывается, а выводится из шести характеристик: каждое очко
    /// сверх пятнадцатого в любой из них даёт шесть десятых уровня. Опытом до трёхсотого не
    /// дойти — цена очка растёт так, что число переполняется. Поэтому бойцу поднимаются сами
    /// характеристики, в тех же долях, в каких он их носил, — и уровень выходит из них честно:
    /// трёхсотый и по числам трёхсотый. Зверю — сам уровень: каждый сверх его образца даёт
    /// ему по пять процентов здоровья и урона.
    /// </summary>
    internal static class Ladder
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Rungs;
        internal static ConfigEntry<string> Heroes;
        internal static ConfigEntry<string> Prize;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Ladder", "Enabled", true,
                "Make the arena a ladder of five rungs, each with its own levels, and let the "
                + "levels come from the fighters own attributes.");

            Rungs = config.Bind("Ladder", "Rungs", "1-10,20-30,50-80,100-150,200-300",
                "The levels of each rung, from the first to the fifth.");

            Heroes = config.Bind("Ladder", "Heroes", "300-500",
                "The levels of the heroes who come out in legendary gear.");

            Prize = config.Bind("Ladder", "Prize", "1,2,3,4,5",
                "How many times the prize money of each rung is multiplied, from the first to "
                + "the fifth. The favour stays as the game gives it.");

            Telling = config.Bind("Ladder", "Telling", true,
                "Say in the log who was raised to what.");
        }

        private static int told;

        /// <summary>The levels of a rung, from 1 to 5; 6 stands for the legendary heroes.</summary>
        internal static void Range(int rung, out int least, out int most)
        {
            least = 1;
            most = 10;

            string written = rung >= 6 ? (Heroes.Value ?? "") : (Rungs.Value ?? "");
            string[] parts = written.Split(',');

            int at = rung >= 6 ? 0 : Mathf.Clamp(rung, 1, 5) - 1;
            if (at >= parts.Length) return;

            string[] ends = parts[at].Split('-');
            if (ends.Length != 2) return;

            int a, b;
            if (int.TryParse(ends[0].Trim(), out a) && int.TryParse(ends[1].Trim(), out b))
            {
                least = Mathf.Max(1, Mathf.Min(a, b));
                most = Mathf.Max(least, Mathf.Max(a, b));
            }
        }

        internal static int Roll(int rung)
        {
            int least, most;
            Range(rung, out least, out most);
            return UnityEngine.Random.Range(least, most + 1);
        }

        internal static float PrizeTimes(int rung)
        {
            string[] parts = (Prize.Value ?? "").Split(',');
            int at = Mathf.Clamp(rung, 1, 5) - 1;

            float much;
            if (at < parts.Length && float.TryParse(parts[at].Trim(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out much)) return Mathf.Max(0.01f, much);

            return 1f;
        }

        // ----------------------------------------------------------------- уровень по характеристикам

        /// <summary>The level these attributes make, by the game own reckoning, without its lid.</summary>
        internal static int LevelOf(HumanAttribute a)
        {
            int total = 0;
            for (int i = 0; i < 6; i++)
            {
                for (int k = a[i] - 1; k >= 0; k--) total += Mathf.Min(k / 5, 3);
            }
            return Mathf.Max(1, total / 5);
        }

        /// <summary>
        /// Raises a fighter to this level through his attributes, in the shares he already had.
        ///
        /// Очки кладутся по одному туда, где их у него больше всего относительно доли: воин
        /// останется воином, маг — магом, просто каждый станет тем, кем был, но выше.
        /// </summary>
        internal static bool Raise(NPCSaveData who, int target)
        {
            if (who == null || who.humanAttribute == null || target <= 0) return false;

            HumanAttribute a = who.humanAttribute;
            int was = LevelOf(a);
            if (was >= target) return false;

            float[] share = new float[6];
            float sum = 0f;
            for (int i = 0; i < 6; i++) { share[i] = Mathf.Max(1, a[i]); sum += share[i]; }
            for (int i = 0; i < 6; i++) share[i] /= sum;

            int guard = 0;
            while (LevelOf(a) < target && guard++ < 20000)
            {
                // Кому сейчас меньше всего против его доли — тому и очко.
                int best = 0;
                float lack = float.MinValue;
                int all = 0;
                for (int i = 0; i < 6; i++) all += a[i];

                for (int i = 0; i < 6; i++)
                {
                    float want = share[i] * (all + 1) - a[i];
                    if (want > lack) { lack = want; best = i; }
                }

                a[best] = a[best] + 1;
            }

            if (a.potential < a.Sum) a.potential = a.Sum;

            who.SetLevel();

            if (Telling.Value && told < 30)
            {
                told++;
                ItemForgePlugin.Log.LogInfo($"Арена: «{who.unitname}» поднят с {was} до {who.level} "
                    + $"(цель {target}) — сила {a[0]}, вын. {a[1]}, лов. {a[2]}, вос. {a[3]}, "
                    + $"разум {a[4]}, мудр. {a[5]}.");
            }

            return true;
        }

        /// <summary>A beast of the arena brought to the level of its rung.</summary>
        internal static void Beast(UnitAttribute beast, int target)
        {
            if (beast == null || beast.Data == null || beast.info == null || target <= 0) return;
            if (beast.Data.level >= target) return;

            int was = beast.Data.level;
            beast.Data.level = target;
            beast.ApplyUnitLevelBonus();
            beast.UpdateAttribute();

            if (Telling.Value && told < 30)
            {
                told++;
                ItemForgePlugin.Log.LogInfo($"Арена: зверь «{beast.Data.unitname}» с {was} до {target} уровня "
                    + $"(+{(target - beast.info.level) * beast.info.levelHealthBonus * 100f:0}% здоровья и урона).");
            }
        }
    }
}
