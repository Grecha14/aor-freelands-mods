using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// A slow man still walks: his legs move, only slower.
    ///
    /// Шаг в игре рисуется смесью: на нуле скорости — стойка, на скорости шага — ходьба, между
    /// ними одно переходит в другое. Когда перебитая нога, переломы или неподъёмный груз
    /// отнимают девять десятых хода, человек движется со скоростью в четверть метра в секунду,
    /// и смесь почти целиком оказывается стойкой: он плывёт по земле с неподвижными ногами.
    ///
    /// Здесь смеси дают ходьбу, а медленность переносят в сам шаг: ноги переставляются реже,
    /// но переставляются. Так выглядит тяжёлый, хромой, изнемогающий человек, а не призрак.
    /// </summary>
    internal static class Gait
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Walk;
        internal static ConfigEntry<float> Slowest;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Gait", "Enabled", true,
                "Let a very slow man walk instead of gliding: the walking is drawn at walking "
                + "pace and slowed down, instead of being drawn as standing still.");

            Walk = config.Bind("Gait", "Walk", 1.5f,
                new ConfigDescription(
                    "Below what speed the walk is drawn as a walk anyway, in the game's own "
                    + "measure, where four is a run.",
                    new AcceptableValueRange<float>(0.2f, 4f)));

            Slowest = config.Bind("Gait", "Slowest", 0.35f,
                new ConfigDescription(
                    "The slowest the steps are drawn, as a share of their ordinary pace. Slower "
                    + "still and it reads as slow motion rather than as a heavy step.",
                    new AcceptableValueRange<float>(0.05f, 1f)));
        }

        /// <summary>Draws the walk of a slow mover as a walk, at a slower step.</summary>
        internal static void Draw(UnitAttribute who)
        {
            if (Enabled == null || !Enabled.Value || who == null || who.ani == null) return;
            if (!who.isMoving || who.isCrouching || who.isFlying) return;

            float speed = who.movementSpeed;
            float walk = Mathf.Max(0.2f, Walk.Value);

            if (speed <= 0.01f || speed >= walk) return;

            float pace = 0.5f + who.maxSpeed * 0.1f;
            float step = Mathf.Max(Slowest.Value, speed / walk) * Mathf.Max(0.5f, pace);

            who.ani.SetFloat("speed", walk);
            who.ani.SetFloat("movementspeed", step);
        }

        /// <summary>Puts the ordinary step back once he stands.</summary>
        internal static void Stand(UnitAttribute who)
        {
            if (Enabled == null || !Enabled.Value || who == null || who.ani == null) return;

            who.ani.SetFloat("movementspeed", 0.5f + who.maxSpeed * 0.1f);
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "SetMoveSpeed")]
    internal static class SetMoveSpeed_Gait_Patch
    {
        private static void Postfix(UnitAttribute __instance)
        {
            try { Gait.Draw(__instance); } catch { }
        }
    }

    // Пересчёт характеристик заново пишет шаг обычным — для медленного переписываем.
    [HarmonyPatch(typeof(UnitAttribute), "WriteUnitAttribute")]
    [HarmonyPriority(Priority.Last)]
    internal static class WriteUnitAttribute_Gait_Patch
    {
        private static void Postfix(UnitAttribute __instance)
        {
            try { Gait.Draw(__instance); } catch { }
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "Stop")]
    internal static class Stop_Gait_Patch
    {
        private static void Postfix(UnitAttribute __instance)
        {
            try { Gait.Stand(__instance); } catch { }
        }
    }
}
