using BepInEx.Configuration;
using HarmonyLib;

namespace DemonLook
{
    /// <summary>
    /// Shops shut for the night.
    ///
    /// С девяти вечера до шести утра лавки закрыты: ни у прилавка в городе, ни из меню города на
    /// карте мира не поторгуешь. Трактир не лавка — трактирщик наливает и ночью.
    /// </summary>
    internal static class Shutters
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Shut;
        internal static ConfigEntry<int> Open;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Shutters", "Enabled", true,
                "Close the shops for the night. The tavern keeper still serves.");

            Shut = config.Bind("Shutters", "Shut", 21,
                new ConfigDescription("The hour the shops close.", new AcceptableValueRange<int>(0, 23)));

            Open = config.Bind("Shutters", "Open", 6,
                new ConfigDescription("The hour the shops open.", new AcceptableValueRange<int>(0, 23)));
        }

        internal static bool Closed()
        {
            if (Enabled == null || !Enabled.Value) return false;

            int hour;
            try { hour = TimeManager.Hour; }
            catch { return false; }

            int shut = Shut.Value, open = Open.Value;
            return shut > open ? hour >= shut || hour < open : hour >= shut && hour < open;
        }

        internal static void Say()
        {
            Souls.Say($"Лавка закрыта до утра: торгуют с {Open.Value}:00 до {Shut.Value}:00.");
        }
    }

    [HarmonyPatch(typeof(ShopVender), "Interact")]
    internal static class Vender_Shutters_Patch
    {
        private static bool Prefix(ShopVender __instance, UnitAttribute otherunit)
        {
            if (!Shutters.Closed()) return true;

            try
            {
                NPCSaveData npc = __instance.unit != null ? __instance.unit.Data as NPCSaveData : null;
                if (npc != null && npc.career == CareerType.Bartender) return true;

                Shutters.Say();
                if (otherunit != null && otherunit.stateMachine != null)
                    otherunit.stateMachine.HandleCommand(new UnitCommand(commandsName.stop));
                return false;
            }
            catch
            {
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(VenderManager), "StartShoping", new[] { typeof(Shop), typeof(bool) })]
    internal static class TownShop_Shutters_Patch
    {
        private static bool Prefix(Shop shop)
        {
            if (shop == null || !shop.isCommonTownShop || !Shutters.Closed()) return true;

            Shutters.Say();
            return false;
        }
    }
}
