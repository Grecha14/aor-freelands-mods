using System;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// The force of a blow, reckoned from what is actually swinging.
    ///
    /// Сила удара в этой игре — авторская цифра при оружии, и цифры эти не про вес и не про
    /// того, кто бьёт: у кинжала тридцать, у боевого молота двести, и так у всякого молота во
    /// всяком тире, хоть в руках у мальчишки, хоть у латника в двести кило.
    ///
    /// Между тем толчок — это масса. Не оружия и не человека порознь, а их вместе: замах несёт
    /// вес железа, разогнанный весом тела, и упереться в него надо всем, что есть. Оттого сила
    /// здесь считается так: вес оружия на вес того, кто им машет, вместе с доспехом на руках, и
    /// всё это на один общий коэффициент.
    ///
    /// Коэффициент подобран так, чтобы новичок с одноручным мечом выдавал ровно ту силу, какая
    /// стояла у него в игре, — около сотни. Дальше числа растут сами, и растут круто: и оружие
    /// тяжелеет со ступенью, и человек. Двуручный молот пятого тира в руках латника толкает
    /// вдесятеро против своего же первого, и это ровно то, чем он и должен быть.
    ///
    /// На пробитие брони сила не влияет уже нигде. Она решает три вещи: сшибает ли с ног,
    /// ломает ли кость и как быстро выводит из строя руку или ногу.
    /// </summary>
    internal static class Might
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Coupling;
        internal static ConfigEntry<float> Sleeves;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Might", "Enabled", true,
                "Reckon a weapon's force from what is swinging it — the weight of the thing "
                + "times the weight of the man and the harness on his arms — instead of reading "
                + "a number written beside the weapon by hand.");

            Coupling = config.Bind("Might", "Coupling", 0.4f,
                new ConfigDescription(
                    "The one coefficient the whole reckoning hangs on: force is the weapon's "
                    + "kilograms times the swinger's kilograms times this. Four tenths, chosen "
                    + "so that a plain man with a plain one-handed sword comes out at about the "
                    + "hundred the game gave him — everything else then follows from weight.",
                    new AcceptableValueRange<float>(0.01f, 5f)));

            Sleeves = config.Bind("Might", "Sleeves", 0.6f,
                new ConfigDescription(
                    "What share of the body armour counts as being on the arms. Six tenths of it "
                    + "for a two-handed grip, half of that for one hand: the sleeve travels with "
                    + "the blow and adds its own weight to it.",
                    new AcceptableValueRange<float>(0f, 2f)));

            Telling = config.Bind("Might", "Telling", false,
                "Write out the first few weapons and what their force came to.");
        }

        private static int told;

        // Игровой множитель силы удара закрыт от нас — читаем его отражением, один раз найдя
        // поле. Он собирает всё, что наговорено и надето поверх самой вещи.
        private static System.Reflection.FieldInfo boost;

        private static float Boost(HumaniodUnit who)
        {
            try
            {
                if (boost == null)
                {
                    boost = AccessTools.Field(typeof(UnitAttribute), "WeaponForceMD");
                }

                if (boost == null) return 1f;

                object got = boost.GetValue(who);
                return got is float ? Mathf.Max(0.01f, (float)got) : 1f;
            }
            catch
            {
                return 1f;
            }
        }

        /// <summary>The armour riding on the arms, in kilograms.</summary>
        private static float Sleeve(HumaniodUnit who, bool bothHands)
        {
            try
            {
                float wear;
                UIArmorInfo coat = Anatomy.Worn(who, Anatomy.Chest, out wear);
                if (coat == null) return 0f;

                return coat.weight * Sleeves.Value * (bothHands ? 1f : 0.5f);
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>The weapon behind a Weapon in hand, for its weight.</summary>
        private static UIWeaponInfo Item(HumaniodUnit who, int slot)
        {
            try
            {
                EquipInfo[] kit = who.equipmentmanger != null
                    ? who.equipmentmanger.equipInfos : null;

                if (kit == null || slot < 0 || slot >= kit.Length) return null;
                if (kit[slot] == null || !kit[slot].IsEquiped()) return null;
                if (kit[slot].inventory == null) return null;

                return kit[slot].inventory.itemInfo as UIWeaponInfo;
            }
            catch
            {
                return null;
            }
        }

        internal static void Swing(HumaniodUnit who)
        {
            if (Enabled == null || !Enabled.Value || who == null || who.weapons == null) return;

            try
            {
                // Тело без всего надетого: в замахе участвует человек и рукава, а не
                // весь доспех. Полный вес идёт в другую сторону счёта — тому, кого бьют.
                float body = Mass.Bare(who);

                for (int i = 0; i < who.weapons.Count; i++)
                {
                    Weapon arm = who.weapons[i];
                    if (arm == null) continue;

                    UIWeaponInfo blade = Item(who, i);

                    // Своего веса у когтя и клыка нет, и такие руки мы не трогаем: у зверя
                    // сила удара своя, и считает её «Beastly».
                    if (blade == null || blade.weight <= 0f) continue;

                    bool bothHands = arm.weaponType == WeaponType.twohand
                        || arm.weaponType == WeaponType.polearms
                        || arm.weaponType == WeaponType.range;

                    float moving = body + Sleeve(who, bothHands);

                    // Игровые прибавки к силе оружия — множитель, им и остаются: игра успела
                    // его собрать со всего надетого и наговорённого.
                    float mine = blade.weight * moving * Coupling.Value
                        * Boost(who);

                    arm.weaponForce = mine;

                    if (Telling.Value && told < 12)
                    {
                        told++;
                        ItemForgePlugin.Log.LogInfo($"Сила удара «{blade.Name}» у "
                            + $"«{who.Data.unitname}»: {blade.weight:0.#} кг × {moving:0} кг "
                            + $"× {Coupling.Value:0.##} = {mine:0}.");
                    }
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог посчитать силу удара: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(HumaniodUnit), "WriteUnitAttribute")]
    internal static class Write_Might_Patch
    {
        private static void Postfix(HumaniodUnit __instance)
        {
            try
            {
                Might.Swing(__instance);
            }
            catch
            {
            }
        }
    }
}
