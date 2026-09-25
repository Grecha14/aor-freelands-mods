using System;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Explains the new reckoning where it is shown, not in a changelog nobody reads.
    ///
    /// Мы поменяли то, как считается бой: защита стала числом в той же мерке, что урон, удар
    /// проходит порогом, а не вычитанием, мастерство сжимает разброс вместо того чтобы
    /// множить урон. Ничего из этого не видно по самим числам — «Защита от режущего 52» не
    /// говорит, что делать с пятьюдесятью двумя.
    ///
    /// Поэтому к каждой новой строке привешено объяснение: навёл — прочитал. Тексты в
    /// настройках, чтобы их можно было править, не пересобирая мод.
    /// </summary>
    internal static class Hints
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Wall;
        internal static ConfigEntry<string> Blow;
        internal static ConfigEntry<string> Spread;
        internal static ConfigEntry<string> Mobility;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Hints", "Enabled", true,
                "Explain the new reckoning on the cards themselves, where the numbers are.");

            Wall = config.Bind("Hints", "Wall",
                "Сколько урона эта вещь держит против такого удара.\\n\\n"
                + "Удар слабее трёх четвертей этого числа не проходит вовсе. Сильнее на "
                + "четверть — проходит целиком. Между — частью.\\n\\n"
                + "Мимо брони всегда идут два: крит находит щель и уносит от трети до "
                + "половины удара, а дробящее проносит сквозь железо свою долю — тем большую, "
                + "чем мягче доспех.",
                "What the three protection lines on an armour card say when hovered.");

            Blow = config.Bind("Hints", "Blow",
                "Урон, когда всё посчитано: вещь, рука, прибавки снаряжения и штрафы.\\n\\n"
                + "Он же и пробивает броню. Сравнивайте это число с тем, что написано на "
                + "чужом доспехе: «держит 52» против вашего удара в 49 значит, что не "
                + "пройдёт ничего, кроме крита.\\n\\n"
                + "Болт, клевец и кинжал бьют в точку и продавливают больше, чем несут: их "
                + "удар считается с надбавкой.",
                "What the total damage line in the character window says when hovered.");

            Spread = config.Bind("Hints", "Spread",
                "Размах удара сжимается мастерством: каждое очко снимает сотую долю.\\n\\n"
                + "Середина при этом не двигается. У новичка и у мастера лучший удар одинаков "
                + "— разница в худшем: он подтягивается к лучшему.\\n\\n"
                + "Сам урон растёт от того стата, для которого вещь сделана: у топора это "
                + "сила, у рапиры и лука — ловкость, и доля каждого записана в самой вещи.",
                "What the damage line on a weapon card says when hovered.");

            Mobility = config.Bind("Hints", "Mobility",
                "Доспех платит подвижностью за то, что держит.\\n\\n"
                + "Лёгкий прибавляет ход и уворот, средний — верность руки, тяжёлый отнимает "
                + "и то, и другое, и замах. Считается от веса вещи, а вес растёт со "
                + "ступенью: латы пятого тира неповоротливее лат второго.",
                "What the mobility bonuses on an armour card say when hovered.");
        }

        /// <summary>Hangs an explanation on a thing, or replaces the one already there.</summary>
        internal static void Say(GameObject where, string what)
        {
            if (Enabled == null || !Enabled.Value || where == null
                || string.IsNullOrEmpty(what)) return;

            try
            {
                UIShowToolTip show = where.GetComponent<UIShowToolTip>();
                if (show == null) show = where.AddComponent<UIShowToolTip>();

                show.tip = what.Replace("\\n", "\n");
            }
            catch
            {
            }
        }
    }
}
