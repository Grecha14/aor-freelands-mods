using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Dresses the circle that pulled the demon through, and the knights who came for it.
    ///
    /// Сцена уже есть: круг догорает, те, кто его чертил, стоят на своих местах, и по ним
    /// идут рыцари. Строить её не надо — надо одеть тех, кто в ней стоит, потому что игра
    /// одевает их как придётся, и первая же картина мира выходит случайной.
    ///
    /// Кто есть кто, спрашиваем не по имени существа, а по тому, что у него в руках: посох
    /// или жезл — маг, всё прочее — рыцарь. Список имён пришлось бы выяснять и он устарел бы
    /// от первой же правки сцены; руки не врут.
    ///
    /// Счёт тут такой. Латы третьего яруса держат вычетом полторы-две сотни, но только
    /// режущее, дробящее и колющее — заклинание идёт стихией и в вычет не упирается вовсе.
    /// Мага же защищает ткань, вычет у неё около полусотни, а меч третьего яруса пробивает
    /// под пятьсот. То есть обе стороны бьют друг друга насквозь, и решает только число:
    /// пятеро против троих кончают дело за четыре размена, теряя двоих.
    /// </summary>
    internal static class Ritual
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Scene;
        internal static ConfigEntry<string> Staves;
        internal static ConfigEntry<string> MageWear;
        internal static ConfigEntry<string> MageTrinket;
        internal static ConfigEntry<string> KnightWear;
        internal static ConfigEntry<string> KnightTrinket;
        internal static ConfigEntry<string> KnightArms;
        internal static ConfigEntry<string> Books;
        internal static ConfigEntry<int> MageLevel;
        internal static ConfigEntry<int> KnightLevel;
        internal static ConfigEntry<int> MageCoin;
        internal static ConfigEntry<int> KnightCoin;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Ritual", "Enabled", true,
                "Dress the mages of the opening circle and the knights who come for them. The "
                + "game equips them at random, so the first thing a demon ever sees is a "
                + "lottery.");

            Scene = config.Bind("Ritual", "Scene", "003-Breawoods_Cave",
                "Which scene this applies to. Everywhere else is left alone.");

            Staves = config.Bind("Ritual", "Staves", "Staff,Stick,Wand",
                "What marks a man as one of the circle rather than one of the knights: the "
                + "thing in his hands. Weapon classes, separated by commas. Names would have "
                + "to be found out and would go stale with the first edit of the scene; hands "
                + "do not lie.");

            MageWear = config.Bind("Ritual", "MageWear", "T4:Common",
                "What the circle wears, as tier:quality. Fourth tier and plain: they are "
                + "scholars with means, not champions — nothing on them was won.");

            MageTrinket = config.Bind("Ritual", "MageTrinket", "T2:Uncommon",
                "And the amulet and ring each of them carries.");

            KnightWear = config.Bind("Ritual", "KnightWear", "PlateArmor:T3:Uncommon",
                "What the knights wear, as class:tier:quality. Plate of the third tier, green "
                + "at best — nobody here is a champion either.");

            KnightTrinket = config.Bind("Ritual", "KnightTrinket", "T3:Uncommon",
                "And their amulet and ring.");

            KnightArms = config.Bind("Ritual", "KnightArms",
                "twohand,onehand,onehand,twohand,onehand",
                "What they hold, handed round in order by the game's WeaponType names. Mixed "
                + "on purpose: five men with the same sword are one man drawn five times.");

            Books = config.Bind("Ritual", "Books", "fighter,defender,commander,berserker,duelist",
                "One discipline each for the circle, in order and without repeating. A mage "
                + "nobody can reach is a puzzle; a mage who can stand for three seconds is a "
                + "fight.");

            MageLevel = config.Bind("Ritual", "MageLevel", 45,
                new ConfigDescription(
                    "The level the circle is grown to. Five of them against three knights at "
                    + "this height finish it in four exchanges and lose two: they win, and it "
                    + "costs them, which is the only outcome worth watching.",
                    new AcceptableValueRange<int>(1, 99)));

            KnightLevel = config.Bind("Ritual", "KnightLevel", 34,
                new ConfigDescription(
                    "And the knights. Below the circle deliberately — they came to stop "
                    + "something they did not understand.",
                    new AcceptableValueRange<int>(1, 99)));

            MageCoin = config.Bind("Ritual", "MageCoin", 10000,
                new ConfigDescription(
                    "What a mage carries in coin. Ten thousand is one gold piece.",
                    new AcceptableValueRange<int>(0, 10000000)));

            KnightCoin = config.Bind("Ritual", "KnightCoin", 2000,
                new ConfigDescription(
                    "And a knight. Two thousand is twenty silver.",
                    new AcceptableValueRange<int>(0, 10000000)));

            Telling = config.Bind("Ritual", "Telling", true,
                "Write down everyone the circle scene dressed, and as what.");
        }

        private static int handed;
        private static int armed;

        /// <summary>True while we are standing in the scene this is about.</summary>
        internal static bool Here()
        {
            try
            {
                if (AreaManager.Instance == null) return false;

                return string.Equals(AreaManager.Instance.sceneName, (Scene.Value ?? "").Trim(),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Dresses this one as what his hands say he is.</summary>
        internal static void Stage(HumaniodUnit man)
        {
            if (!Enabled.Value || man == null || man.Data == null) return;
            if (!Here()) return;

            try
            {
                bool mage = Holds(man);

                Grow(man, mage ? MageLevel.Value : KnightLevel.Value);

                // Одеты они уже при рождении, по правленому списку шаблона («Birth»): здесь
                // только то, что к одежде не относится.
                if (mage)
                {
                    Teach(man);
                    man.items.money = MageCoin.Value;
                }
                else
                {
                    man.items.money = KnightCoin.Value;
                }

                // Одели — теперь подгоняем стат под надетое, иначе Strain поставит их столбами
                // и вся сцена кончится, не начавшись.
                if (Watch.Fit(man)) { man.Data.SetLevel(); man.UpdateAttribute(); }

                if (Telling.Value)
                {
                    ItemForgePlugin.Log.LogInfo($"Круг: «{man.Data.unitname}» — "
                        + (mage ? "маг" : "рыцарь") + $", уровень {man.Data.level}.");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог обрядить круг: " + e);
            }
        }

        /// <summary>A staff in the hands is what makes a man one of the circle.</summary>
        private static bool Holds(HumaniodUnit man)
        {
            if (man.equipmentmanger == null || man.equipmentmanger.equipInfos == null) return false;

            for (int slot = 0; slot < 2 && slot < man.equipmentmanger.equipInfos.Length; slot++)
            {
                EquipInfo held = man.equipmentmanger.equipInfos[slot];
                if (held == null || !held.IsEquiped() || held.inventory == null) continue;

                UIWeaponInfo blade = held.inventory.itemInfo as UIWeaponInfo;
                if (blade == null) continue;

                foreach (string one in (Staves.Value ?? "").Split(','))
                {
                    if (string.Equals(one.Trim(), blade.weaponClass.ToString(),
                            StringComparison.OrdinalIgnoreCase)) return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------ одеть при рождении

        /// <summary>True when the list a man is about to be dressed from puts a staff in his hands.</summary>
        internal static bool Mage(UIEquipmentInfo[] list)
        {
            if (list == null) return false;

            foreach (UIEquipmentInfo gear in list)
            {
                UIWeaponInfo blade = gear as UIWeaponInfo;
                if (blade == null) continue;

                foreach (string one in (Staves.Value ?? "").Split(','))
                {
                    if (string.Equals(one.Trim(), blade.weaponClass.ToString(),
                            StringComparison.OrdinalIgnoreCase)) return true;
                }
            }

            return false;
        }

        /// <summary>What this one wears: class (or his own), tier and colour.</summary>
        internal static bool Clad(bool mage, out ArmourClass? klass, out ItemTier tier,
            out UIItemQuality grade)
        {
            klass = null;
            tier = ItemTier.T1;
            grade = UIItemQuality.Common;

            string[] parts = ((mage ? MageWear.Value : KnightWear.Value) ?? "").Split(':');

            try
            {
                if (parts.Length == 3)
                {
                    klass = (ArmourClass)Enum.Parse(typeof(ArmourClass), parts[0].Trim(), true);
                    tier = (ItemTier)Enum.Parse(typeof(ItemTier), parts[1].Trim(), true);
                    grade = (UIItemQuality)Enum.Parse(typeof(UIItemQuality), parts[2].Trim(), true);
                    return true;
                }

                if (parts.Length == 2)
                {
                    tier = (ItemTier)Enum.Parse(typeof(ItemTier), parts[0].Trim(), true);
                    grade = (UIItemQuality)Enum.Parse(typeof(UIItemQuality), parts[1].Trim(), true);
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        /// <summary>The next knight's weapon, handed round in order.</summary>
        internal static UIWeaponInfo NextArm(ItemTier tier)
        {
            string[] all = (KnightArms.Value ?? "").Split(',');
            if (all.Length == 0) return null;

            string said = all[armed % all.Length].Trim();
            armed++;

            try
            {
                WeaponType hand = (WeaponType)Enum.Parse(typeof(WeaponType), said, true);
                return Armoury.Blade(hand, tier);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>The amulet and ring of this one: tier and colour.</summary>
        internal static bool Charms(bool mage, out ItemTier tier, out UIItemQuality grade)
        {
            tier = ItemTier.T1;
            grade = UIItemQuality.Common;

            string[] parts = ((mage ? MageTrinket.Value : KnightTrinket.Value) ?? "").Split(':');
            if (parts.Length < 2) return false;

            try
            {
                tier = (ItemTier)Enum.Parse(typeof(ItemTier), parts[0].Trim(), true);
                grade = (UIItemQuality)Enum.Parse(typeof(UIItemQuality), parts[1].Trim(), true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void Teach(HumaniodUnit man)
        {
            string[] all = (Books.Value ?? "").Split(',');
            if (handed >= all.Length) return;

            string said = all[handed].Trim();
            handed++;

            try
            {
                SkillSet what = (SkillSet)Enum.Parse(typeof(SkillSet), said, true);

                if (man.Data.skillSet == null) man.Data.skillSet = new List<SkillSet>();
                if (!man.Data.skillSet.Contains(what)) man.Data.skillSet.Add(what);
            }
            catch
            {
            }
        }

        /// <summary>Grows a man to the height this scene asks of him.</summary>
        private static void Grow(HumaniodUnit man, int want)
        {
            if (man.Data.humanAttribute == null) return;

            HumanAttribute six = man.Data.humanAttribute;

            int[] was = new int[6];
            for (int i = 0; i < 6; i++) was[i] = six[i];

            for (float much = 0.01f; much <= 10f; much += 0.01f)
            {
                for (int i = 0; i < 6; i++)
                {
                    six[i] = Mathf.Max(was[i], Mathf.RoundToInt(was[i] * (1f + much)));
                }

                if (Watch.Level(six) >= want) break;
            }

            man.Data.SetLevel();
            man.UpdateAttribute();
        }
    }

    // Человек родился в сцене круга: одет он уже при рождении, здесь — рост, книги и кошель.
    [HarmonyPatch(typeof(HumaniodUnit), "InitializeUnit")]
    internal static class InitializeUnit_Ritual_Patch
    {
        private static void Postfix(HumaniodUnit __instance)
        {
            Ritual.Stage(__instance);
        }
    }
}
