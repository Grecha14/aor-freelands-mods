using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Strength as economy of effort, not only as force.
    ///
    /// Запас сил в этой игре держит одна выносливость, и оттого боец с тяжёлым оружием обязан
    /// качать её и только её: сила ему даёт урон, но махать этим уроном нечем. Между тем в
    /// жизни ровно наоборот — чем крепче рука, тем дешевле ей тот же самый замах.
    ///
    /// Здесь каждое очко силы удешевляет всё, что стоит сил: удар, рывок, блок. Три четверти
    /// процента за очко — при полусотне силы всякое усилие обходится на треть дешевле, и оба
    /// стата становятся нужны один без другого: выносливость наливает мех, сила бережёт его.
    ///
    /// Работает поверх нашей же цены блока — она считается от веса щита, — и поверх штрафа за
    /// груз, который эту же экономию отнимает. Потолок игры в четыре пятых мы соблюдаем: выше
    /// него усилие бесплатным не становится.
    /// </summary>
    internal static class Brawn
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> PerPoint;
        internal static ConfigEntry<float> Most;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Brawn", "Enabled", true,
                "Let strength pay part of what effort costs. Stamina is held up by endurance "
                + "alone in this game, so a man with a heavy weapon must raise endurance and "
                + "nothing else: strength gives him a blow he has no breath to swing.");

            PerPoint = config.Bind("Brawn", "PerPoint", 0.0075f,
                new ConfigDescription(
                    "What one point of strength takes off the cost of every effort — a swing, a "
                    + "dash, a block — as a share. Three quarters of a hundredth: at fifty "
                    + "strength everything costs a third less.",
                    new AcceptableValueRange<float>(0f, 0.02f)));

            Most = config.Bind("Brawn", "Most", 0.8f,
                new ConfigDescription(
                    "And the most it can ever come to, which is the game's own ceiling on the "
                    + "same thing. Four fifths: a blow never costs nothing.",
                    new AcceptableValueRange<float>(0f, 0.95f)));
        }

        internal static void Spare(HumaniodUnit who)
        {
            if (Enabled == null || !Enabled.Value || PerPoint.Value <= 0f || who == null) return;

            try
            {
                float mine = who.Strength * PerPoint.Value;
                if (mine <= 0f) return;

                // Одного места довольно: всякий расход сил в игре проходит через «CostSP»,
                // а он и есть то, что умножается на «1 − EPsave». Блок в том числе — сперва
                // он берёт своё по весу щита, а потом платит здесь. Клади мы то же самое ещё
                // и в блочную экономию, парирование клинком дешевело бы дважды.
                //
                // Потолок игры соблюдаем сами: её собственное усечение прошло раньше нас.
                who.EPsave = Mathf.Min(who.EPsave + mine, Most.Value);
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(HumaniodUnit), "WriteUnitAttribute")]
    internal static class Write_Brawn_Patch
    {
        private static void Postfix(HumaniodUnit __instance)
        {
            try
            {
                Brawn.Spare(__instance);
            }
            catch
            {
            }
        }
    }
}
