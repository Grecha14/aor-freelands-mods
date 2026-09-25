using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// How an enemy is dressed for his level.
    ///
    /// Игра одевает человека по одной строке: ступень снаряжения — это уровень, делённый на
    /// десять, и ещё одна сверху всякому врагу с планом роста. То есть почти всякому. Выходит
    /// лестница, которая бежит вдвое быстрее игрока: к двадцатому уровню враг в третьей
    /// ступени, к тридцатому в четвёртой, к сороковому в пятой — в той, что делается на заказ
    /// у лучших кузнецов мира. Латы при этом достаются всякому, кто командует или обороняет,
    /// — десятнику при обозе наравне с королевским рыцарем.
    ///
    /// Здесь лестница вдвое длиннее, и каждая ступень в ней смешанная. До двадцатого уровня
    /// враги в первой и второй, до сорокового во второй и третьей, до шестидесятого в третьей
    /// и четвёртой, дальше в четвёртой. Внутри полосы доля старшей ступени растёт: в начале её
    /// носит каждый четвёртый, к концу трое из четырёх. Так соседние уровни не отличаются
    /// скачком, а высокие не становятся одинаковыми.
    ///
    /// Полосы выбраны под требования вещей. Вторая ступень просит двадцать силы и двадцать
    /// мастерства, третья тридцать и сорок, четвёртая сорок и семьдесят пять, пятая пятьдесят
    /// и сотню. Человек набирает это примерно к десятому, двадцать пятому и сорок пятому
    /// уровню — туда полосы и легли.
    ///
    /// Латы и пятая ступень — для особых: чемпиона, босса и того, кто перерос шестидесятый.
    /// </summary>
    internal static class Muster
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Band;
        internal static ConfigEntry<string> Mix;
        internal static ConfigEntry<int> Top;
        internal static ConfigEntry<int> TopFrom;
        internal static ConfigEntry<int> Special;
        internal static ConfigEntry<int> PlateFrom;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Muster", "Enabled", true,
                "Dress enemies for their level by a slower, mixed ladder, and keep plate for "
                + "the ones who have earned it.");

            Band = config.Bind("Muster", "Band", 20,
                new ConfigDescription(
                    "How many levels one band of the ladder spans. Twenty: levels 1-19 wear the "
                    + "first and second tier, 20-39 the second and third, 40-59 the third and "
                    + "fourth. The game used ten, and ran twice as fast as the player.",
                    new AcceptableValueRange<int>(5, 50)));

            Mix = config.Bind("Muster", "Mix", "0.25,0.75",
                "The chance of wearing the higher tier of a band, at its first level and at its "
                + "last. A quarter to three quarters: at the start of a band one enemy in four "
                + "has the better gear, at the end three in four.");

            Top = config.Bind("Muster", "Top", 4,
                new ConfigDescription(
                    "The highest tier an ordinary enemy ever wears. The fifth is left to the "
                    + "special ones.",
                    new AcceptableValueRange<int>(1, 5)));

            TopFrom = config.Bind("Muster", "TopFrom", 60,
                new ConfigDescription(
                    "From which level an ordinary enemy wears the top tier and nothing below it.",
                    new AcceptableValueRange<int>(1, 200)));

            Special = config.Bind("Muster", "Special", 1,
                new ConfigDescription(
                    "How many tiers above his level a champion or a boss is dressed.",
                    new AcceptableValueRange<int>(0, 4)));

            PlateFrom = config.Bind("Muster", "PlateFrom", 60,
                new ConfigDescription(
                    "From which level an ordinary enemy may wear plate. Below it plate goes to "
                    + "champions and bosses only: a sergeant with the baggage no longer walks "
                    + "about dressed like a royal knight.",
                    new AcceptableValueRange<int>(1, 200)));

            Telling = config.Bind("Muster", "Telling", true,
                "Say in the log, for the first few enemies, what tier they were dressed in.");
        }

        private static int told;

        /// <summary>Особый ли это противник: чемпион или босс.</summary>
        internal static bool Marked(NPCSaveData psd)
        {
            try
            {
                if (psd == null) return false;

                if (psd.heroCareer != null && psd.heroCareer.isChampion) return true;

                UnitAttribute unit = psd.Unit;
                if (unit != null && unit.info != null && unit.info.isBoss) return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Ступень, в которую одет этот противник.</summary>
        internal static ItemTier Tier(NPCSaveData psd)
        {
            return TierAt(psd.level, psd.id * 7919 + Mathf.Max(1, psd.level), Marked(psd),
                psd.unitname);
        }

        /// <summary>
        /// Ступень по уровню — одна и та же для героя и для рядового из шаблона.
        ///
        /// Кость своя у каждого человека: из двух соседних ступеней полосы старшая достаётся не
        /// всем, и тот же человек на том же уровне одевается всегда одинаково.
        /// </summary>
        internal static ItemTier TierAt(int level, int seed, bool marked, string who)
        {
            level = Mathf.Max(1, level);
            int top = Mathf.Clamp(Top.Value, 1, 5);
            int tier;

            if (level >= TopFrom.Value)
            {
                tier = top;
            }
            else
            {
                int band = Mathf.Max(1, Band.Value);
                int low = 1 + level / band;

                float at = band > 1 ? (float)(level % band) / (band - 1) : 0f;

                float from = 0.25f, to = 0.75f;
                string[] two = (Mix.Value ?? "").Split(',');
                if (two.Length >= 2)
                {
                    float.TryParse(two[0].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out from);
                    float.TryParse(two[1].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out to);
                }

                float chance = Mathf.Clamp01(from + (to - from) * at);

                System.Random dice = new System.Random(seed);

                tier = low + (dice.NextDouble() < chance ? 1 : 0);
                tier = Mathf.Clamp(tier, 1, top);
            }

            if (marked) tier = Mathf.Min(5, tier + Mathf.Max(0, Special.Value));

            if (Telling.Value && told < 25)
            {
                told++;

                ItemForgePlugin.Log.LogInfo($"Снаряжение «{who}»: уровень {level}, "
                    + $"ступень T{tier}" + (marked ? " (особый)" : "") + ".");
            }

            return (ItemTier)tier;
        }

        /// <summary>Можно ли этому противнику латы.</summary>
        internal static bool MayPlate(NPCSaveData psd)
        {
            if (psd == null) return true;

            return MayPlateAt(psd.level, Marked(psd));
        }

        internal static bool MayPlateAt(int level, bool marked)
        {
            return marked || level >= PlateFrom.Value;
        }
    }

    // Одевание по уровню. Повторяет игровое слово в слово, кроме самой ступени.
    [HarmonyPatch(typeof(HeroUnitMaker), "UpgradeEquips")]
    internal static class Equips_Muster_Patch
    {
        private static bool Prefix(NPCSaveData psd)
        {
            try
            {
                if (Muster.Enabled == null || !Muster.Enabled.Value || psd == null) return true;
                if (psd.heroCareer == null) return true;

                // Своих игра не переодевает — пусть скажет это сама.
                PartyManager party = PartyManager.instance;
                if (party != null && (party.companions.Contains(psd)
                        || party.caravanMembers.Contains(psd) || party.prisoners.Contains(psd)))
                {
                    return true;
                }

                ItemTier tier = Muster.Tier(psd);

                if (psd.heroCareer.heroType != HeroType.Slave)
                {
                    HeroUnitMaker.UpgradeWeapons(psd, tier);
                    if (psd.level >= 5) HeroUnitMaker.UpgradeOrnaments(psd, tier);
                }
                else
                {
                    tier = ItemTier.T0;
                }

                HeroUnitMaker.UpgradeWearings(psd, tier);

                return false;
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог одеть противника: " + e.Message);
                return true;
            }
        }
    }

    // Латы — особым. Из того, что игра готова надеть, убираем комплекты с латами, если
    // противник не особый и есть чем их заменить.
    [HarmonyPatch(typeof(HeroUnitMaker), "GetOutfits")]
    internal static class Outfits_Muster_Patch
    {
        private static void Postfix(NPCSaveData psd, ref List<EquipmentOutfit> __result)
        {
            try
            {
                if (Muster.Enabled == null || !Muster.Enabled.Value) return;
                if (__result == null || __result.Count == 0) return;
                if (Muster.MayPlate(psd)) return;

                List<EquipmentOutfit> kept = new List<EquipmentOutfit>();
                bool heavyWas = false, heavyLeft = false;

                foreach (EquipmentOutfit one in __result)
                {
                    if (one == null) continue;
                    if (one.type == ArmourType.Heavy) heavyWas = true;

                    bool plated = false;

                    if (one.outfit != null)
                    {
                        foreach (UIEquipmentInfo piece in one.outfit)
                        {
                            UIArmorInfo coat = piece as UIArmorInfo;
                            if (coat != null && coat.armourClass == ArmourClass.PlateArmor)
                            {
                                plated = true;
                                break;
                            }
                        }
                    }

                    if (plated) continue;

                    kept.Add(one);
                    if (one.type == ArmourType.Heavy) heavyLeft = true;
                }

                // Если без лат выбирать не из чего — оставляем как было: тяжёлый воин без
                // тяжёлого доспеха хуже, чем воин в латах не по чину.
                if (kept.Count == 0) return;
                if (heavyWas && !heavyLeft) return;

                __result = kept;
            }
            catch
            {
            }
        }
    }
}
