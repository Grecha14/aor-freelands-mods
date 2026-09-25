using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace DemonLook
{
    /// <summary>
    /// The one man at the head of the crusade, and the books his mages carry.
    ///
    /// Поход из восьми одинаково снаряжённых — это не поход, а восемь противников. У войска
    /// есть тот, кого видно с первого взгляда: он в полных латах, при двуручном мече, и на нём
    /// то, что остальным не досталось. Убивают его первым или последним, но не путают ни с кем.
    ///
    /// Маги остаются при своём: белая школа и посох — это их дело, и трогать его незачем. Но
    /// маг, к которому подошли вплотную, беспомощен до неприличия, и это делает поход задачей
    /// на выносливость, а не на бой. Поэтому каждому кладётся по книге ближнего боя — своей,
    /// не одинаковой: один умеет держать строй, другой встречать грудью, третий бить в ответ.
    /// Восемь человек, из которых каждый умеет что-то одно, опаснее восьми одинаковых.
    /// </summary>
    internal static class Captain
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Clad;
        internal static ConfigEntry<string> Tier;
        internal static ConfigEntry<string> Quality;
        internal static ConfigEntry<string> Blade;
        internal static ConfigEntry<int> Rings;
        internal static ConfigEntry<string> Books;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Captain", "Enabled", true,
                "Put one man at the head of the crusade and dress him so nobody mistakes him "
                + "for the rest. Eight identically equipped men are not a crusade, they are "
                + "eight opponents.");

            Clad = config.Bind("Captain", "Clad", "PlateArmor",
                "What he wears, by armour class — every piece of it, head to legs.");

            Tier = config.Bind("Captain", "Tier", "T4",
                "And of what tier.");

            Quality = config.Bind("Captain", "Quality", "Epic",
                "And of what colour. This covers the amulet and the rings as well: they are the "
                + "part of him that the rest of the band did not get.");

            Blade = config.Bind("Captain", "Blade", "twohand",
                "What he holds, by the game's WeaponType name. A two-handed sword says what he "
                + "is without a word being spoken.");

            Rings = config.Bind("Captain", "Rings", 2,
                new ConfigDescription(
                    "How many rings. The game gives a man one finger slot and the rest are "
                    + "closed; what cannot be worn is put in his bag, and what he leaves behind "
                    + "is worth taking.",
                    new AcceptableValueRange<int>(0, 4)));

            Books = config.Bind("Captain", "Books",
                "fighter,defender,commander,berserker,duelist",
                "One melee discipline each, handed out in order so no two share — and handed "
                + "out once. When the list runs out the rest of the band gets none: five books "
                + "mean five men who can hold a line, not eight men with three of them reading "
                + "the same page. A mage nobody can reach is a puzzle; a mage who can stand for "
                + "three seconds is a fight.");
        }

        // ------------------------------------------------------------------ вождь

        /// <summary>Dresses the man at the head of the band.</summary>
        internal static void Lead(UnitAttribute who)
        {
            if (!Enabled.Value || who == null) return;

            HumaniodUnit man = who as HumaniodUnit;
            if (man == null || man.items == null || man.equipmentmanger == null) return;

            try
            {
                ItemTier tier = (ItemTier)Enum.Parse(typeof(ItemTier), Tier.Value.Trim(), true);
                UIItemQuality grade = (UIItemQuality)Enum.Parse(
                    typeof(UIItemQuality), Quality.Value.Trim(), true);
                ArmourClass klass = (ArmourClass)Enum.Parse(
                    typeof(ArmourClass), Clad.Value.Trim(), true);
                WeaponType hand = (WeaponType)Enum.Parse(
                    typeof(WeaponType), Blade.Value.Trim(), true);

                int put = 0;

                // Латы на все три места разом: голова, корпус, ноги.
                foreach (EquipSlotType where in new[]
                    { EquipSlotType.head, EquipSlotType.chest, EquipSlotType.pants })
                {
                    UIArmorInfo coat = Plate(klass, where, tier);
                    if (coat != null && Wear(man, coat, grade)) put++;
                }

                UIWeaponInfo sword = Sword(hand, tier);
                if (sword != null && Wear(man, sword, grade)) put++;

                UIEquipmentInfo charm = Trinket(EquipSlotType.neck, tier);
                if (charm != null && Wear(man, charm, grade)) put++;

                // Гнездо под кольцо у человека одно, и второе кольцо просто не наденется.
                // Кладём в сумку: с павшего вождя его и снимут.
                UIEquipmentInfo ring = Trinket(EquipSlotType.finger, tier);

                for (int i = 0; i < Rings.Value && ring != null; i++)
                {
                    if (Wear(man, ring, grade)) put++;
                }

                DemonLookPlugin.Log.LogInfo($"Во главе похода «{man.Data.unitname}»: "
                    + $"{Clad.Value} {Tier.Value} {Quality.Value}, вещей {put}.");
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Не смог одеть вождя похода: " + e.Message);
            }
        }

        /// <summary>Makes the thing, colours it, and puts it on if there is room.</summary>
        private static bool Wear(HumaniodUnit man, UIEquipmentInfo what, UIItemQuality grade)
        {
            try
            {
                Inventory made = new Inventory(what);
                EquipmentMaker.EnhanceEquipmentToQuality(made, grade);

                // У вещи одно место: надетая живёт в слоте, в сумку идёт только то, что не
                // наделось, потому что гнездо занято, — второе кольцо. Прежде в сумку клалось
                // всё подряд, и надетое числилось дважды: на вожде и у него в мешке.
                man.equipmentmanger.EquipItem(made);

                if (!Worn(man.equipmentmanger, made)) man.items.AddInventoryNoEvent(made);

                return true;
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------------ книги магам

        private static int handed;

        /// <summary>Resets the round of books, so a new crusade starts from the first.</summary>
        internal static void Fresh() { handed = 0; }

        /// <summary>True when this very thing ended up in a slot.</summary>
        private static bool Worn(EquipmentManager kit, Inventory thing)
        {
            if (kit == null || thing == null || kit.equipInfos == null) return false;

            foreach (EquipInfo slot in kit.equipInfos)
            {
                if (slot != null && slot.IsEquiped() && ReferenceEquals(slot.inventory, thing)) return true;
            }

            return false;
        }

        /// <summary>
        /// Gives this one its own discipline, different from the last.
        ///
        /// По кругу, а не жребием: жребий на восьмерых дважды выдал бы одно и то же, и вместо
        /// восьми разных вышло бы пять с повторами.
        /// </summary>
        internal static void Read(UnitAttribute who)
        {
            if (!Enabled.Value || who == null) return;

            HumaniodUnit man = who as HumaniodUnit;
            if (man == null || man.Data == null) return;

            string[] all = (Books.Value ?? "").Split(',');

            // По кругу не ходим: список кончился — значит кончились и книги. Иначе шестой
            // получил бы то же, что первый, и «разные» перестало бы быть правдой.
            if (handed >= all.Length) return;

            string said = all[handed].Trim();
            handed++;

            if (said.Length == 0) return;

            try
            {
                SkillSet what = (SkillSet)Enum.Parse(typeof(SkillSet), said, true);

                if (man.Data.skillSet == null) man.Data.skillSet = new List<SkillSet>();
                if (man.Data.skillSet.Contains(what)) return;

                man.Data.skillSet.Add(what);

                DemonLookPlugin.Log.LogInfo($"«{man.Data.unitname}» прочёл книгу: {said}.");
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning($"Не смог выдать книгу «{said}»: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ поиск вещей

        private static readonly Dictionary<string, UIEquipmentInfo> shelf =
            new Dictionary<string, UIEquipmentInfo>(StringComparer.Ordinal);

        private static UIArmorInfo Plate(ArmourClass klass, EquipSlotType where, ItemTier tier)
        {
            return Find("a" + klass + where + tier, delegate (UIEquipmentInfo gear)
            {
                UIArmorInfo coat = gear as UIArmorInfo;
                return coat != null && coat.armourClass == klass
                    && coat.EquipType == where && coat.tier == tier;
            }) as UIArmorInfo;
        }

        private static UIWeaponInfo Sword(WeaponType hand, ItemTier tier)
        {
            return Find("w" + hand + tier, delegate (UIEquipmentInfo gear)
            {
                UIWeaponInfo arm = gear as UIWeaponInfo;
                return arm != null && arm.WeaponType == hand && arm.tier == tier;
            }) as UIWeaponInfo;
        }

        private static UIEquipmentInfo Trinket(EquipSlotType where, ItemTier tier)
        {
            return Find("t" + where + tier, delegate (UIEquipmentInfo gear)
            {
                return gear.EquipType == where && gear.tier == tier;
            });
        }

        private static UIEquipmentInfo Find(string key, Predicate<UIEquipmentInfo> fits)
        {
            UIEquipmentInfo kept;
            if (shelf.TryGetValue(key, out kept)) return kept;

            kept = null;

            try
            {
                UIItemDatabase db = UIItemDatabase.Instance;
                if (db != null && db.items != null)
                {
                    foreach (UIItemInfo thing in db.items)
                    {
                        UIEquipmentInfo gear = thing as UIEquipmentInfo;
                        if (gear == null || gear.isUnique) continue;
                        if (!fits(gear)) continue;

                        kept = gear;
                        break;
                    }
                }
            }
            catch
            {
            }

            shelf[key] = kept;
            return kept;
        }
    }
}
