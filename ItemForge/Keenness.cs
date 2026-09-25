using System;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// The difference between a man who hits well and a man who merely hits.
    ///
    /// Урон оружия записан вилкой — от и до, — и игра берёт из неё число ровно, безразлично
    /// кто бьёт. Меч в руках мастера и тот же меч в руках кашевара выдают одну и ту же
    /// случайность, и восприятие, которое по названию должно отвечать как раз за это, не
    /// участвует в ударе вовсе.
    ///
    /// Здесь бросок перекашивается. При нулевом восприятии всё как было — что выпало, то и
    /// выпало. Чем оно выше, тем ближе результат к верхнему краю вилки: не потому, что урон
    /// вырос, а потому что меткий чаще попадает туда, куда бьёт, и реже задевает вскользь.
    /// При сотне человек почти всегда выбирает из своего оружия всё, что там есть.
    ///
    /// Заодно это чинит перекос, который мы внесли сами. Дробящему разброс сузили нарочно —
    /// палица передаёт вес и бьёт ровно, — и вышло, что она предсказуемее меча у кого угодно.
    /// С восприятием получается честнее: палица ровна сама по себе, а меч становится ровным в
    /// умелых руках.
    /// </summary>
    internal static class Keenness
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Sharpness;
        internal static ConfigEntry<int> Full;
        internal static ConfigEntry<float> Widen;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Keenness", "Enabled", true,
                "Let precision decide where in a weapon's range the blow lands. Without this the "
                + "same sword does the same spread of damage in any hands.");

            Sharpness = config.Bind("Keenness", "Sharpness", 2f,
                new ConfigDescription(
                    "How far a full measure of precision leans the roll towards the top of the "
                    + "range. At two, a man of no precision averages the middle of his weapon's "
                    + "range and a man of a hundred averages three quarters of the way up. "
                    + "Nought leaves the roll flat, as the game has it.",
                    new AcceptableValueRange<float>(0f, 10f)));

            Full = config.Bind("Keenness", "Full", 100,
                new ConfigDescription(
                    "The precision counted as a full measure. A hundred: the human ceiling.",
                    new AcceptableValueRange<int>(1, 1000)));

            Widen = config.Bind("Keenness", "Widen", 1f,
                new ConfigDescription(
                    "Widens the range of weapons that are not crushing ones, about their middle. "
                    + "One leaves them as they are. Raise it to make an edge a wilder thing than "
                    + "a hammer, which is the knob for that; crushing weapons are steadied "
                    + "separately, under Heft.",
                    new AcceptableValueRange<float>(0.1f, 3f)));
        }

        // Чей это удар. Ставится на время расчёта атаки и снимается сразу: бросок урона игра
        // делает и помимо ударов — у ловушек, заклинаний, ядов, — и там перекашивать нечего.
        internal static UnitAttribute Who;

        /// <summary>Takes a number out of the range, leaning it by how good the hand is.</summary>
        internal static bool Roll(Damage hit)
        {
            if (!Enabled.Value || Who == null || hit == null) return true;

            try
            {
                float low = hit.minDamage;
                float high = hit.maxDamage;

                if (high <= low)
                {
                    hit.currentDamage = low;
                    return false;
                }

                // Расширение считается от середины, чтобы средний урон не поехал вместе с
                // разбросом: шире становится вилка, а не оружие.
                if (Widen.Value > 1.0001f && !Blunt(hit))
                {
                    float middle = (low + high) * 0.5f;
                    float half = (high - low) * 0.5f * Widen.Value;

                    low = Mathf.Max(0f, middle - half);
                    high = middle + half;
                }

                // Восприятие живёт у людей; у зверя его нет, и бросок остаётся ровным.
                float keen = 0f;
                NPCSaveData mind = Who.Data as NPCSaveData;
                if (mind != null) keen = mind.precision;

                float lean = 1f + Mathf.Max(0f, keen) / Mathf.Max(1, Full.Value) * Sharpness.Value;

                // Ровный бросок, возведённый в степень меньше единицы, тянется к верхнему краю.
                // При нулевом восприятии степень равна единице и бросок остаётся ровным.
                float part = Mathf.Pow(UnityEngine.Random.value, 1f / lean);

                hit.currentDamage = low + (high - low) * part;

                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool Blunt(Damage hit)
        {
            return hit.damageType == DamageType.blunt;
        }
    }

    // Пока идёт сборка удара, известно чей он. Дальше бросок делается внутри, и там уже не
    // спросить.
    [HarmonyPatch(typeof(UnitAttribute), "CalculateAttackInfo")]
    internal static class CalculateAttackInfo_Keen_Patch
    {
        private static void Prefix(UnitAttribute __instance)
        {
            Keenness.Who = __instance;
        }

        // Снимаем и при сорвавшемся расчёте: оставленный хозяин перекосил бы всё, что игра
        // бросает потом, — от яда до горящей смолы.
        private static void Finalizer()
        {
            Keenness.Who = null;
        }
    }

    [HarmonyPatch(typeof(Damage), "RandomDamage")]
    internal static class RandomDamage_Keen_Patch
    {
        private static bool Prefix(Damage __instance, ref float __result)
        {
            if (Keenness.Roll(__instance)) return true;

            __result = __instance.currentDamage;
            return false;
        }
    }
}
