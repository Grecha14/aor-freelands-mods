using System.Collections.Generic;
using DuloGames.UI;
using HarmonyLib;
using spell;

namespace ItemForge
{
    /// <summary>
    /// A block that breaks costs no health the armour did not let through.
    ///
    /// Когда у блокирующего кончается выносливость, игра снимает с него здоровье отдельно от
    /// самого удара: «сколько выносливости не хватило, но не меньше единицы». Урон к этому
    /// мигу уже прошёл через броню, и если она удержала всё, игра всё равно берёт единицу —
    /// мимо брони, за каждый удар. Латник с пустой выносливостью под ножом грабителя терял
    /// по очку с каждого тычка, который не пробил бы и его рубаху.
    ///
    /// Здесь правило то же, что у всего остального: не пробило — ничего не прошло.
    /// </summary>
    internal static class Parry
    {
        internal static readonly HashSet<UnitAttribute> blocking = new HashSet<UnitAttribute>();
        internal static readonly Dictionary<UnitAttribute, float> left = new Dictionary<UnitAttribute, float>();
    }

    [HarmonyPatch(typeof(UnitAttribute), "Doblock")]
    internal static class Doblock_Parry_Patch
    {
        private static void Prefix(UnitAttribute __instance)
        {
            if (__instance == null) return;

            Parry.blocking.Add(__instance);
            Parry.left.Remove(__instance);
        }

        private static void Finalizer(UnitAttribute __instance)
        {
            if (__instance == null) return;

            Parry.blocking.Remove(__instance);
            Parry.left.Remove(__instance);
        }
    }

    // Сколько удара осталось после брони — последним словом, когда все прочие расчёты уже
    // сказали своё. Пишется только пока идёт блок.
    [HarmonyPatch(typeof(UnitAttribute), "DamageReduce")]
    [HarmonyPriority(Priority.Last)]
    internal static class DamageReduce_Parry_Patch
    {
        private static void Postfix(UnitAttribute __instance, DamageBase __result)
        {
            if (__instance == null || !Parry.blocking.Contains(__instance)) return;

            Parry.left[__instance] = __result != null ? __result.Damage() : 0f;
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "CostHP")]
    [HarmonyPriority(Priority.First)]
    internal static class CostHP_Parry_Patch
    {
        private static bool Prefix(UnitAttribute __instance, ref bool __result)
        {
            if (__instance == null || !Parry.blocking.Contains(__instance)) return true;

            float got;
            if (!Parry.left.TryGetValue(__instance, out got)) return true;
            if (got > 0.001f) return true;

            // Броня удержала всё: снимать нечего, человек жив.
            __result = true;
            return false;
        }
    }
}
