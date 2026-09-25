using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using DuloGames.UI;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Перековка: вес и прочность вещей приводятся к тому, что они означают в бою.
    ///
    /// Расчёт удара живёт в Breach и берёт вычет из своей таблицы по классу и тиру. Сами же
    /// вещи об этом ничего не знают: латы весят те же двадцать семь килограммов, что и до
    /// нас, а прочность у тряпичной рубахи стоит шестьдесят — столько же, сколько у иной
    /// кольчуги. Оттого в подсказке одно, а в бою другое, и тяжёлый доспех не требует силы,
    /// хотя должен требовать.
    ///
    /// Здесь это сводится. Вес берётся по классу: комплект от четырёх килограммов у ткани до
    /// сорока у лат, и делится по кускам — грудь вдвое головы, ноги в полтора раза. Прочность
    /// же не задаётся, а выводится: сколько вещь держит, помноженное на то, сколько в ней
    /// металла. Латная кираса выходит под две с половиной тысячи, тряпичная рубаха под
    /// двадцать, и это честно: первой хватит на сотню боёв, второй на десяток.
    ///
    /// Оружию правится только вес, по той же лестнице Фибоначчи, по какой Breach считает
    /// пробитие — чтобы кинжал пятой ступени не пробивал как двухкилограммовый, оставаясь
    /// в сумке килограммовым.
    /// </summary>
    internal static class Smithy
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> Armours;
        internal static ConfigEntry<bool> Blades;
        internal static ConfigEntry<string> Suits;
        internal static ConfigEntry<float> Fatter;
        internal static ConfigEntry<string> Pieces;
        internal static ConfigEntry<bool> Crown;

        /// <summary>Правился ли вес оружия — от этого зависит, кто несёт лестницу тира.</summary>
        internal static bool Bladed;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Smithy", "Enabled", true,
                "Reforge what the game says a thing weighs and how much punishment it takes, so "
                + "that both agree with what it does in a fight. Off, and the numbers on the "
                + "tooltip go on describing a game we no longer play.");

            Armours = config.Bind("Smithy", "Armours", true,
                "Rewrite armour. Weight decides what a man can walk and run in; durability "
                + "decides how long the harness lasts before it stops being one.");

            Blades = config.Bind("Smithy", "Blades", true,
                "Rewrite weapon weight along the same ladder the reckoning uses, so what the "
                + "bag says agrees with what the arm feels.");

            Suits = config.Bind("Smithy", "Suits",
                "Cloth=4.2,PaddingArmor=5.0,LightLeatherArmor=6.5,HardLeaterArmor=9.0,"
                + "SplintArmor=15.0,ChainArmor=17.4,ScaleMail=22.5,LamellarArmor=22.5,"
                + "HalfPlate=29.5,PlateArmor=40.0",
                "What a full suit of each class weighs in kilograms, at the lowest tier the "
                + "class has. Forty for plate is a tourney harness and deliberately so: a man "
                + "wants five and forty of Strength to walk in it and five and sixty to run.");

            Fatter = config.Bind("Smithy", "Fatter", 1.07f,
                new ConfigDescription(
                    "How much heavier a suit grows per step of tier. Denser alloys and thicker "
                    + "plate: a seventh again by the fifth step, not more, or the numbers stop "
                    + "being believable.",
                    new AcceptableValueRange<float>(1f, 2f)));

            Crown = config.Bind("Smithy", "Crown", true,
                "Set every legendary thing to the fifth tier, which is what it already is in all "
                + "but the field. The game names them T5 and tags most of them T4, so seventy "
                + "seven legendary harnesses have been protecting like common work of their "
                + "tier. Note that repair cost in this game doubles per tier, so mending a "
                + "legendary becomes twice as dear.");

            Pieces = config.Bind("Smithy", "Pieces", "head=1,chest=2,pants=1.5",
                "How a suit's weight divides among its pieces. The breastplate is twice the "
                + "helm and the legs half again. Three pieces, not four: the belt is jewellery "
                + "now, and the iron of a suit is all in the three that hold a blow.");
        }

        private static bool forged;
        private static readonly HashSet<int> done = new HashSet<int>();

        /// <summary>Called once the item database is up.</summary>
        internal static void Forge()
        {
            if (!Enabled.Value || forged) return;

            try
            {
                UIItemDatabase book = UIItemDatabase.Instance;
                if (book == null || book.items == null) return;

                forged = true;
                Bladed = Blades.Value;

                int armours = 0, blades = 0, crowned = 0;

                foreach (UIItemInfo thing in book.items)
                {
                    if (thing == null) continue;

                    // Легендарное и есть пятая ступень: имя говорит T5, а поле тира стоит
                    // четвёртым, и имя честнее. Ставится до перековки, чтобы лестница веса
                    // и вычета считалась уже по новому.
                    if (Crown.Value
                        && (int)thing.Quality >= (int)UIItemQuality.Legendary
                        && thing.tier != ItemTier.T5)
                    {
                        thing.tier = ItemTier.T5;
                        crowned++;
                    }

                    UIArmorInfo coat = thing as UIArmorInfo;
                    if (coat != null)
                    {
                        if (Armours.Value && done.Add(coat.ID) && Reforge(coat)) armours++;
                        continue;
                    }

                    UIWeaponInfo blade = thing as UIWeaponInfo;
                    if (blade != null && Blades.Value && done.Add(blade.ID) && Rehaft(blade))
                    {
                        blades++;
                    }
                }

                ItemForgePlugin.Log.LogInfo("Перековано: брони " + armours
                    + ", оружия " + blades + ", возведено в пятую ступень " + crowned + ".");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Перековка сорвалась: " + e);
            }
        }

        /// <summary>Weight and durability for one piece of armour.</summary>
        private static bool Reforge(UIArmorInfo coat)
        {
            try
            {
                float suit;
                if (!Table().TryGetValue(coat.armourClass, out suit)) return false;
                if (suit <= 0f) return false;

                string slot = Where(coat);
                if (slot == null) return false;

                float share, all;
                if (!Split(out all).TryGetValue(slot, out share)) return false;
                if (all <= 0f) return false;

                // Комплект тяжелеет со ступенью, и куски делят его в своей доле.
                int first = First(coat.armourClass);
                int t = (int)coat.tier;
                int steps = t > first ? t - first : 0;

                float kilos = suit * Mathf.Pow(Fatter.Value, steps) * share / all;
                if (kilos <= 0.01f) return false;

                coat.weight = kilos;

                // Прочность не задаётся, а выводится: держит вещь столько-то, и металла в ней
                // столько-то. Одно помноженное на другое и есть запас, которым она платит.
                float guard = Breach.Wall(coat, 1f, Slot(slot));
                if (guard > 0f) coat.durability = Mathf.Round(guard * kilos);

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Weight for one weapon, along the same ladder the reckoning uses.</summary>
        private static bool Rehaft(UIWeaponInfo blade)
        {
            try
            {
                if (blade.weight <= 0.01f) return false;

                float rung = Breach.Rung(blade.tier, blade.Quality);
                if (rung <= 0f) return false;

                blade.weight = blade.weight * rung;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string Where(UIArmorInfo coat)
        {
            // Игровые номера мест совпадают с нашими один в один: голова два, грудь четыре,
            // пояс шесть, ноги семь.
            switch (coat.EquipType)
            {
                case EquipSlotType.head: return "head";
                case EquipSlotType.chest: return "chest";
                case EquipSlotType.pants: return "pants";
                default: return null;
            }
        }

        private static int Slot(string name)
        {
            switch (name)
            {
                case "head": return Anatomy.Head;
                case "chest": return Anatomy.Chest;
                case "pants": return Anatomy.Pants;
                default: return Anatomy.Chest;
            }
        }

        private static int First(ArmourClass which)
        {
            switch (which)
            {
                case ArmourClass.ChainArmor:
                case ArmourClass.ScaleMail:
                case ArmourClass.LamellarArmor:
                case ArmourClass.HalfPlate:
                case ArmourClass.PlateArmor:
                    return 2;
                default:
                    return 1;
            }
        }

        private static readonly Dictionary<ArmourClass, float> table =
            new Dictionary<ArmourClass, float>();
        private static string read;

        private static Dictionary<ArmourClass, float> Table()
        {
            string written = Suits.Value ?? "";
            if (written == read) return table;

            read = written;
            table.Clear();

            foreach (string one in written.Split(','))
            {
                string[] halves = one.Split('=');
                if (halves.Length != 2) continue;

                float much;
                if (!float.TryParse(halves[1].Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out much)) continue;

                try
                {
                    table[(ArmourClass)Enum.Parse(typeof(ArmourClass), halves[0].Trim(), true)] = much;
                }
                catch
                {
                }
            }

            return table;
        }

        private static readonly Dictionary<string, float> split = new Dictionary<string, float>();
        private static string splitRead;
        private static float splitAll;

        private static Dictionary<string, float> Split(out float all)
        {
            string written = Pieces.Value ?? "";

            if (written != splitRead)
            {
                splitRead = written;
                split.Clear();
                splitAll = 0f;

                foreach (string one in written.Split(','))
                {
                    string[] halves = one.Split('=');
                    if (halves.Length != 2) continue;

                    float much;
                    if (!float.TryParse(halves[1].Trim(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out much)) continue;

                    split[halves[0].Trim().ToLowerInvariant()] = much;
                    splitAll += much;
                }
            }

            all = splitAll;
            return split;
        }
    }
}
