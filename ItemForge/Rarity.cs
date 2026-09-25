using System;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Makes rarity mean something about the thing itself, not just about its margins.
    ///
    /// В игре качество вещи не меняет ни защиты, ни урона, ни прочности. Оно добавляет
    /// случайные приписки — «+3 к ловкости», «+2 % к скорости» — через «EnhanceEquipmentToQuality»,
    /// и на этом всё: белая и фиолетовая кираса держат удар одинаково, а фиолетовая просто
    /// обвешана строчками. Отсюда и ощущение, что цвет рамки — украшение, а не обещание.
    ///
    /// Здесь цвет становится обещанием. Защита, урон, доля блока и прочность растут вместе с
    /// редкостью по ряду Фибоначчи: первые ступени почти незаметны, последние решают. Приписки
    /// игры при этом остаются на месте — они про то, что у вещи сверх её сути, а мы правим саму
    /// суть.
    ///
    /// Легендарные стоят особняком и намеренно: это уникальные вещи, их в игре считанные штуки,
    /// и втрое против белой — не перекос, а причина, по которой за ними идут.
    /// </summary>
    internal static class Rarity
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Ladder;
        internal static ConfigEntry<bool> TouchArmour;
        internal static ConfigEntry<bool> TouchWeapons;
        internal static ConfigEntry<bool> TouchDurability;
        internal static ConfigEntry<string> Price;

        private static bool done;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Rarity", "Enabled", true,
                "Let the colour of a thing say something about the thing. The game gives "
                + "quality nothing but extra affixes, so a purple breastplate stops the same "
                + "blow as a white one.");

            Ladder = config.Bind("Rarity", "Ladder", "0.92,1,1.2,1.5,2,3",
                "What each grade is worth against a white one, from poor to legendary. Laid "
                + "out by Fibonacci, so the climb accelerates: green is barely better than "
                + "white, blue is noticeably better, purple is half again. Legendary stands "
                + "apart at three — those are the unique things, there are a handful of them "
                + "in the whole game, and they are the reason anybody goes looking.");

            TouchArmour = config.Bind("Rarity", "TouchArmour", true,
                "Scale what armour keeps out of cutting, crushing and thrusting. Resistances to "
                + "magic stay as the game wrote them, and rings, amulets and belts are never "
                + "touched: they are ornaments, not armour.");

            TouchWeapons = config.Bind("Rarity", "TouchWeapons", true,
                "Scale what weapons do, and what shields turn aside.");

            TouchDurability = config.Bind("Rarity", "TouchDurability", true,
                "Scale how long a thing lasts. A legendary blade that notches like a common "
                + "one is a legendary blade only until the second fight.");

            Price = config.Bind("Rarity", "Price", "0.6,0.8,1,1.4,2,3",
                "What each grade costs against a plain one, from poor to legendary. The game "
                + "doubles at every step — white one, green two, blue four, purple eight, "
                + "legendary sixteen — which turns a colour into a price tag and nothing else. "
                + "Here green is the standard and legendary is three times it, with the steps "
                + "between laid out by Fibonacci: the gaps run 1, 1, 2, 3, 5. Empty leaves the "
                + "game's own doubling alone.");
        }

        // ------------------------------------------------------------------ цена

        private static float[] worths;
        private static string worthRead;

        /// <summary>What a thing of this colour is worth against a plain one.</summary>
        internal static float Worth(UIItemQuality grade)
        {
            string written = Price.Value ?? "";
            if (written.Trim().Length == 0) return -1f;

            if (worths == null || written != worthRead)
            {
                System.Collections.Generic.List<float> got =
                    new System.Collections.Generic.List<float>();

                foreach (string one in written.Split(','))
                {
                    float much;
                    if (float.TryParse(one.Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out much))
                    {
                        got.Add(Mathf.Clamp(much, 0.05f, 100f));
                    }
                }

                while (got.Count < 6) got.Add(1f);

                worthRead = written;
                worths = got.ToArray();
            }

            int step = (int)grade;
            if (step < 0 || step >= worths.Length) return 1f;

            return worths[step];
        }

        /// <summary>Raises everything in the catalogue to what its colour promises, once.</summary>
        internal static void Raise()
        {
            if (done || !Enabled.Value) return;

            try
            {
                UIItemDatabase db;
                try { db = UIItemDatabase.Instance; }
                catch { return; }

                if (db == null || db.items == null) return;

                done = true;

                float[] steps = Steps();
                int touched = 0;

                foreach (UIItemInfo thing in db.items)
                {
                    UIEquipmentInfo worn = thing as UIEquipmentInfo;
                    if (worn == null) continue;

                    int grade = (int)thing.Quality;
                    if (grade < 0 || grade >= steps.Length) continue;

                    float much = steps[grade];
                    if (Mathf.Approximately(much, 1f)) continue;

                    UIArmorInfo armour = worn as UIArmorInfo;
                    UIWeaponInfo weapon = worn as UIWeaponInfo;

                    // Цвет растит то, от чего доспех и есть доспех, — удержание железа. Стихии
                    // остаются такими, какими их вписал автор вещи, а у колец, амулетов и
                    // поясов цвет не трогает сопротивлений вовсе: это не броня.
                    EquipSlotType where = worn.EquipType;
                    bool ornament = where == EquipSlotType.finger || where == EquipSlotType.neck
                        || where == EquipSlotType.belt;

                    if (armour != null && TouchArmour.Value && armour.damageDR != null && !ornament)
                    {
                        for (int i = 0; i < armour.damageDR.Length && i <= 2; i++)
                        {
                            armour.damageDR[i] *= much;
                        }
                    }

                    if (weapon != null && TouchWeapons.Value)
                    {
                        if (weapon.damage != null)
                        {
                            foreach (System.Collections.Generic.KeyValuePair<DamageType, Damage> one
                                in weapon.damage)
                            {
                                if (one.Value == null) continue;

                                one.Value.minDamage *= much;
                                one.Value.maxDamage *= much;
                                one.Value.currentDamage *= much;
                            }
                        }

                        weapon.BlockRate *= much;
                    }

                    if (TouchDurability.Value && thing.durability > 0f)
                    {
                        thing.durability *= much;
                    }

                    touched++;
                }

                ItemForgePlugin.Log.LogInfo($"Редкость теперь что-то значит: {touched} вещей.");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог поднять вещи по редкости: " + e);
            }
        }

        private static float[] steps;
        private static string read;

        private static float[] Steps()
        {
            string written = Ladder.Value ?? "";
            if (steps != null && written == read) return steps;

            System.Collections.Generic.List<float> got = new System.Collections.Generic.List<float>();

            foreach (string one in written.Split(','))
            {
                float much;
                if (float.TryParse(one.Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out much))
                {
                    got.Add(Mathf.Clamp(much, 0.1f, 20f));
                }
            }

            while (got.Count < 6) got.Add(1f);

            read = written;
            steps = got.ToArray();
            return steps;
        }
    }

    // Цена вещи считается в одном месте, и оттуда её берут и витрина, и кошелёк. Игра
    // удваивает на каждой ступени цвета — от белой до легендарной выходит шестнадцатикратно,
    // и цвет превращается в ценник. Здесь лестница другая: зелёное — мера, легендарное втрое,
    // а промежутки идут по Фибоначчи. Прочее в расчёте оставлено слово в слово, включая то,
    // как игра округляет по износу: разойтись с ней хотя бы на единицу значило бы получить
    // две разные цены в двух окнах.
    [HarmonyPatch(typeof(Inventory), "Value", MethodType.Getter)]
    internal static class Value_Rarity_Patch
    {
        private static bool Prefix(Inventory __instance, ref int __result)
        {
            if (!Rarity.Enabled.Value) return true;

            try
            {
                UIItemInfo thing = __instance.itemInfo;
                if (thing == null) return true;

                float much = Rarity.Worth(__instance.quality);
                if (much < 0f) return true;          // лестница пуста — считает игра

                float worth = thing.value;

                // Ярус магического считается здесь же, а не своим патчем: два префикса на
                // одном свойстве, оба возвращающие ложь, дерутся молча — второй не исполнится
                // вовсе, и заметить это можно только по странным числам.
                if (__instance.Equipment != null)
                {
                    worth *= much * Enchant.Tier(__instance) * Enchant.Plate(__instance);
                }

                worth = (__instance.DurState < 3)
                    ? Mathf.RoundToInt(worth * __instance.durability / thing.durability)
                    : 0f;

                __result = (int)worth;
                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}
