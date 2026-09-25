using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;

namespace EncounterScale
{
    /// <summary>
    /// Gives the world's fighters a trade to follow instead of a grab-bag of skills.
    ///
    /// The game already knows how to grow a coherent fighter. When a hero spends experience it
    /// finds the weapon he is best with, keeps that mastery level with his level, and then
    /// draws new skills at random from a pool — but the pool is not the whole game:
    ///
    ///     List&lt;SkillSet&gt; list = new List&lt;SkillSet&gt;(nsd.skillSet);
    ///     SkillSet item = (SkillSet)(200 + num);            // the branch of his best weapon
    ///     if (num2 &gt; 5 &amp;&amp; num &gt; 0) list.AddCheckContains(item);
    ///
    /// Everything he ever learns comes from his own list of schools plus the branch of the
    /// weapon he favours. So an archer who learns swordplay is not the growth code failing —
    /// it is a hero who was handed swordplay among his schools on the day he was made, and
    /// then grew exactly as told.
    ///
    /// The fix therefore belongs at birth rather than at every level: hand out a trade whose
    /// schools agree with each other, and seed the weapon that trade lives by. From then on
    /// the game's own growth keeps him in his lane, because his lane is all he has.
    /// </summary>
    internal static class Professions
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> VerboseLog;

        // Индексы владения оружием ровно те, которыми пользуется сама игра: школа ветки
        // считается как 200 + индекс, отчего 4 и означает дальний бой.
        private const int Unarmed = 0;
        private const int OneHand = 1;
        private const int TwoHand = 2;
        private const int Shield = 3;
        private const int Range = 4;
        private const int Dual = 5;

        private sealed class Trade
        {
            internal readonly string Name;
            internal readonly int Weapon;
            internal readonly SkillSet[] Schools;

            internal Trade(string name, int weapon, params SkillSet[] schools)
            {
                Name = name;
                Weapon = weapon;
                Schools = schools;
            }
        }

        // Ни одно ремесло не делит школы с другим: именно пересечения и превращали лучника
        // в человека, который заодно фехтует.
        private static readonly Trade[] Trades =
        {
            new Trade("лучник", Range, SkillSet.ranger, SkillSet.marksman),
            new Trade("дуэлист", Dual, SkillSet.duelist, SkillSet.rogue),
            new Trade("латник", Shield, SkillSet.defender, SkillSet.commander),
            new Trade("берсерк", TwoHand, SkillSet.berserker),
            new Trade("мечник", OneHand, SkillSet.fighter, SkillSet.yanguanSword),
            new Trade("монах", Unarmed, SkillSet.battlemonk, SkillSet.lingshePalm)
        };

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Professions", "Enabled", true,
                "Give each new fighter a single trade and the weapon that goes with it.");

            VerboseLog = config.Bind("Professions", "VerboseLog", false,
                "Write a line for every fighter given a trade.");
        }

        internal static void Assign(NPCSaveData who)
        {
            if (!Enabled.Value || who == null) return;

            try
            {
                if (who.weaponMastery == null || who.weaponMastery.Length == 0) return;

                Trade trade = Trades[UnityEngine.Random.Range(0, Trades.Length)];

                int index = trade.Weapon;
                if (index >= who.weaponMastery.Length) index = who.weaponMastery.Length - 1;

                who.skillSet = new List<SkillSet>(trade.Schools);

                // Владение своим оружием ставим выше прочих: игра выбирает лучшее из семи и
                // от него пляшет, так что этим числом и задаётся, кем он вырастет.
                for (int i = 0; i < who.weaponMastery.Length; i++)
                {
                    who.weaponMastery[i] = i == index
                        ? Math.Max(who.weaponMastery[i], 10 + who.level * 2)
                        : Math.Min(who.weaponMastery[i], 3);
                }

                if (VerboseLog.Value)
                {
                    EncounterScalePlugin.Log.LogInfo($"«{who.unitname}» — {trade.Name} "
                        + $"({string.Join(", ", Array.ConvertAll(trade.Schools, x => x.ToString()))}).");
                }
            }
            catch (Exception e)
            {
                EncounterScalePlugin.Log.LogError("Не смог дать ремесло: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(HeroUnitMaker), "Create")]
    internal static class HeroCreate_Patch
    {
        private static void Postfix(NPCSaveData __result, bool forPlayer)
        {
            // Тех, кого игра делает для игрока, не трогаем: выбор ремесла там ваш, не наш.
            if (!forPlayer) Professions.Assign(__result);
        }
    }
}
