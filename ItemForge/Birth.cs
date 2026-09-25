using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Dresses a man once, at birth, in what he is meant to wear.
    ///
    /// Игра одевает человека по шаблону ровно в одном месте — «InitEquips». У шаблона есть
    /// список вещей и, если он того хочет, случайные комплекты брони и пары оружия. Игра
    /// берёт каждую строку списка, делает из неё вещь, бросает на неё качество, снимает
    /// износ и кладёт прямо в слот. В сумку не попадает ничего: у надетого одно место.
    ///
    /// Прежде мы одевали иначе: давали игре одеть человека как придётся, а потом снимали
    /// надетое и надевали своё, положив его перед этим ещё и в сумку. Так у каждого
    /// стражника выходило по второму комплекту в сумке, и каждый снятый доспех заставлял игру
    /// пересобирать модель человека.
    ///
    /// Здесь правится сам список, до того как игра по нему оденет. Каждая строка меняется на
    /// ту же вещь нужной ступени — той же линейки, если она есть на этой ступени, иначе того
    /// же класса в том же гнезде. Дальше игра одевает сама, своим обычным путём.
    ///
    /// Ступень берётся по лестнице «Muster»: до двадцатого уровня первая и вторая, до
    /// сорокового вторая и третья, до шестидесятого третья и четвёртая, дальше четвёртая.
    /// Латы и пятая ступень — боссам. У стражи и у круга в пещере свои правила, они
    /// записаны при них («Watch», «Ritual»), и здесь только исполняются.
    /// </summary>
    internal static class Birth
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> Ladder;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Birth", "Enabled", true,
                "Dress people once, at birth, by rewriting the list of things their template "
                + "is given, before the game makes anything from it. Nothing is taken off "
                + "afterwards and nothing lands in the bag twice.");

            Ladder = config.Bind("Birth", "Ladder", true,
                "Dress ordinary troops — bandits, soldiers, mercenaries, the people of the "
                + "roads — by the level ladder written in Muster. Off, and they keep the tier "
                + "their template was authored with.");

            Telling = config.Bind("Birth", "Telling", true,
                "Say in the log, for the first few, what each one was dressed in.");
        }

        private static int told;

        /// <summary>Какое качество должно быть у вещи не меньше — для тех, кому его назначили.</summary>
        private sealed class Least
        {
            internal UIEquipmentInfo what;
            internal UIItemQuality grade;
        }

        private static readonly Dictionary<EquipmentManager, List<Least>> graded =
            new Dictionary<EquipmentManager, List<Least>>();

        // ------------------------------------------------------------------ до одевания

        /// <summary>Rewrites the list a man is about to be dressed from.</summary>
        internal static void Before(EquipmentManager kit)
        {
            if (Enabled == null || !Enabled.Value || kit == null) return;

            HumaniodUnit man = kit.unit;
            if (man == null || man.Data == null || man.info == null) return;

            // Своих не трогаем: у отряда вещи его собственные, а не шаблонные.
            if (man.inParty || man.Data.team == Faction.player) return;

            UnitType kind = man.info.utype;
            if (kind == UnitType.keyNPC || kind == UnitType.companion || kind == UnitType.player)
            {
                return;
            }

            // Героев игра одевает сама, из готовых комплектов по ступени, и там стоит «Muster».
            // Узнаём их по тому, что их уже одевали так: не по «heroCareer» — карьера есть и у
            // горожан, и у стражи, и тогда мимо проходили все.
            NPCSaveData mind = man.Data as NPCSaveData;
            bool hero = mind != null && mind.heroCareer != null && mind.heroCareer.lastUpdateEquipLevel > 0;

            try
            {
                // Случайные комплекты шаблона разыгрываем тем же игровым броском, что и игра, —
                // но прежде нашей правки, чтобы править уже выпавшее. Второй раз игра их не
                // бросит.
                if (kit.randomizeEquips && Application.isPlaying)
                {
                    kit.RandomizeUnitEquipments();
                    kit.randomizeEquips = false;
                }

                List<Least> want = new List<Least>();
                string how;
                int changed, total;

                if (Watch.Enabled.Value && Watch.Watchman((UnitAttribute)(object)man))
                {
                    how = "страж";
                    Guard(kit, man, want, out changed, out total);
                }
                else if (Ritual.Enabled.Value && Ritual.Here())
                {
                    bool mage = Ritual.Mage(kit.equips);
                    how = mage ? "круг, маг" : "круг, рыцарь";
                    Circle(kit, mage, want, out changed, out total);
                }
                else if (hero)
                {
                    return;
                }
                else if (Muster.Enabled.Value && Ladder.Value)
                {
                    how = "по лестнице";
                    Rungs(kit, man, out changed, out total);
                }
                else
                {
                    return;
                }

                if (want.Count > 0) graded[kit] = want;

                if (Telling.Value && told < 30 && total > 0)
                {
                    told++;

                    ItemForgePlugin.Log.LogInfo($"Одет при появлении «{man.Data.unitname}» "
                        + $"(ур. {man.Data.level}, шаблон {man.info.name}, {how}): "
                        + $"заменено {changed} из {total}.");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог одеть при появлении: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ после одевания

        /// <summary>Raises what was given a colour to that colour, in the slot it lies in.</summary>
        internal static void After(EquipmentManager kit)
        {
            if (kit == null) return;

            List<Least> want;
            if (!graded.TryGetValue(kit, out want)) return;
            graded.Remove(kit);

            try
            {
                foreach (Least one in want)
                {
                    foreach (EquipInfo slot in kit.equipInfos)
                    {
                        if (slot == null || !slot.IsEquiped() || slot.inventory == null) continue;
                        if ((object)slot.inventory.itemInfo != (object)one.what) continue;

                        if (slot.inventory.quality < one.grade)
                        {
                            EquipmentMaker.EnhanceEquipmentToQuality(slot.inventory, one.grade);
                        }

                        break;
                    }
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог дать цвет надетому: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ стража

        private static void Guard(EquipmentManager kit, HumaniodUnit man, List<Least> want,
            out int changed, out int total)
        {
            changed = 0;
            total = 0;

            ItemTier tier;
            try { tier = (ItemTier)Enum.Parse(typeof(ItemTier), Watch.Wear.Value.Trim(), true); }
            catch { return; }

            bool captain = Watch.Captain((UnitAttribute)(object)man);

            kit.equips = Map(kit.equips, ref changed, ref total, delegate (UIEquipmentInfo gear)
            {
                UIArmorInfo coat = gear as UIArmorInfo;
                if (coat != null)
                {
                    ArmourClass klass = Watch.Heavier(coat.armourClass, captain);
                    if (coat.tier >= tier && klass == coat.armourClass) return gear;

                    return Same(coat, tier, klass) ?? gear;
                }

                UIWeaponInfo blade = gear as UIWeaponInfo;
                if (blade != null)
                {
                    if (!Watch.Arms.Value || blade.tier >= tier) return gear;
                    return Same(blade, tier, null) ?? gear;
                }

                return gear;
            });

            kit.standByWeapons = Map(kit.standByWeapons, ref changed, ref total,
                delegate (UIEquipmentInfo gear)
                {
                    UIWeaponInfo blade = gear as UIWeaponInfo;
                    if (blade == null || !Watch.Arms.Value || blade.tier >= tier) return gear;
                    return Same(blade, tier, null) ?? gear;
                });

            // Оберег и кольцо — в пустые гнёзда, как и прежде: что у человека есть своё, то
            // его. Цвет им назначен, и поднимется он уже в слоте.
            ItemTier charmTier;
            try { charmTier = (ItemTier)Enum.Parse(typeof(ItemTier), Watch.TrinketTier.Value.Trim(), true); }
            catch { return; }

            foreach (string one in (Watch.Trinkets.Value ?? "").Split(','))
            {
                EquipSlotType where;
                try { where = (EquipSlotType)Enum.Parse(typeof(EquipSlotType), one.Trim(), true); }
                catch { continue; }

                if (Holds(kit.equips, where)) continue;

                UIEquipmentInfo charm = Watch.Trinket(where, charmTier);
                if (charm == null) continue;

                kit.equips = Add(kit.equips, charm);
                want.Add(new Least { what = charm, grade = UIItemQuality.Uncommon });

                changed++;
                total++;
            }
        }

        // ------------------------------------------------------------------ круг в пещере

        private static void Circle(EquipmentManager kit, bool mage, List<Least> want,
            out int changed, out int total)
        {
            changed = 0;
            total = 0;

            ArmourClass? klass;
            ItemTier tier;
            UIItemQuality grade;

            if (!Ritual.Clad(mage, out klass, out tier, out grade)) return;

            List<Least> local = want;

            kit.equips = Map(kit.equips, ref changed, ref total, delegate (UIEquipmentInfo gear)
            {
                UIArmorInfo coat = gear as UIArmorInfo;
                if (coat == null) return gear;

                UIArmorInfo better = Armoury.Find(klass ?? coat.armourClass, coat.EquipType, tier);
                if (better == null) return gear;

                if (grade > UIItemQuality.Common) local.Add(new Least { what = better, grade = grade });
                return better;
            });

            // Рыцарю — оружие из череды: пятеро с одинаковым мечом — это один человек,
            // нарисованный пять раз.
            if (!mage)
            {
                UIWeaponInfo arm = Ritual.NextArm(tier);

                if (arm != null)
                {
                    bool placed = false;
                    UIEquipmentInfo[] list = kit.equips ?? new UIEquipmentInfo[0];

                    for (int i = 0; i < list.Length; i++)
                    {
                        UIWeaponInfo held = list[i] as UIWeaponInfo;
                        if (held == null || held.EquipType != EquipSlotType.mainhand) continue;

                        list[i] = arm;
                        placed = true;
                        changed++;
                        break;
                    }

                    if (!placed)
                    {
                        list = Add(list, arm);
                        changed++;
                        total++;
                    }

                    // С двуручным щит не держат: убираем его из списка, иначе игра повесит оба.
                    if (arm.WeaponType == WeaponType.twohand || arm.WeaponType == WeaponType.polearms)
                    {
                        List<UIEquipmentInfo> kept = new List<UIEquipmentInfo>();
                        foreach (UIEquipmentInfo one in list)
                        {
                            UIWeaponInfo other = one as UIWeaponInfo;
                            if (other != null && other.WeaponType == WeaponType.shield) continue;
                            kept.Add(one);
                        }
                        list = kept.ToArray();
                    }

                    kit.equips = list;
                }
            }

            ItemTier charmTier;
            UIItemQuality charmGrade;

            if (Ritual.Charms(mage, out charmTier, out charmGrade))
            {
                foreach (EquipSlotType where in new[] { EquipSlotType.neck, EquipSlotType.finger })
                {
                    UIEquipmentInfo charm = Armoury.Trinket(where, charmTier);
                    if (charm == null) continue;

                    kit.equips = Replace(kit.equips, where, charm);
                    want.Add(new Least { what = charm, grade = charmGrade });

                    changed++;
                }
            }
        }

        // ------------------------------------------------------------------ лестница

        private static void Rungs(EquipmentManager kit, HumaniodUnit man,
            out int changed, out int total)
        {
            changed = 0;
            total = 0;

            int level = Mathf.Max(1, man.Data.level);
            bool marked = man.info != null && man.info.isBoss;

            // Своя кость у каждого человека: вторая ступень в полосе достаётся не всем.
            int seed = man.Data.id != 0 ? man.Data.id : man.GetInstanceID();
            ItemTier tier = Muster.TierAt(level, seed, marked, man.Data.unitname);
            bool plate = Muster.MayPlateAt(level, marked);

            UIEquipmentInfo[] map(UIEquipmentInfo[] list, ref int c, ref int t)
            {
                return Map(list, ref c, ref t, delegate (UIEquipmentInfo gear)
                {
                    // Нулевая ступень — это одежда, а не снаряжение: селянину незачем полулаты.
                    if (gear.tier == ItemTier.T0) return gear;

                    UIArmorInfo coat = gear as UIArmorInfo;
                    if (coat != null)
                    {
                        ArmourClass klass = coat.armourClass;
                        if (klass == ArmourClass.PlateArmor && !plate) klass = ArmourClass.HalfPlate;

                        if (coat.tier == tier && klass == coat.armourClass) return gear;
                        return Same(coat, tier, klass) ?? gear;
                    }

                    UIWeaponInfo blade = gear as UIWeaponInfo;
                    if (blade != null)
                    {
                        if (blade.tier == tier) return gear;
                        return Same(blade, tier, null) ?? gear;
                    }

                    return gear;
                });
            }

            kit.equips = map(kit.equips, ref changed, ref total);
            kit.standByWeapons = map(kit.standByWeapons, ref changed, ref total);
        }

        // ------------------------------------------------------------------ подбор

        /// <summary>Walks the list and lets the rule replace each thing it is allowed to.</summary>
        private static UIEquipmentInfo[] Map(UIEquipmentInfo[] list, ref int changed, ref int total,
            Func<UIEquipmentInfo, UIEquipmentInfo> rule)
        {
            if (list == null) return null;

            UIEquipmentInfo[] made = (UIEquipmentInfo[])list.Clone();

            for (int i = 0; i < made.Length; i++)
            {
                UIEquipmentInfo gear = made[i];
                if (gear == null) continue;

                total++;

                // Единственное, квестовое и сделанное руками не трогаем: у них своя судьба.
                if (gear.isUnique || gear.noRandomQuality || gear.itemType == ItemType.Quest) continue;
                if (gear.EquipType == EquipSlotType.relic) continue;

                UIEquipmentInfo instead = rule(gear);

                if (instead != null && (object)instead != (object)gear)
                {
                    made[i] = instead;
                    changed++;
                }
            }

            return made;
        }

        private static bool Holds(UIEquipmentInfo[] list, EquipSlotType where)
        {
            if (list == null) return false;

            foreach (UIEquipmentInfo gear in list)
            {
                if (gear != null && gear.EquipType == where) return true;
            }

            return false;
        }

        private static UIEquipmentInfo[] Add(UIEquipmentInfo[] list, UIEquipmentInfo gear)
        {
            List<UIEquipmentInfo> made = list != null
                ? new List<UIEquipmentInfo>(list) : new List<UIEquipmentInfo>();

            made.Add(gear);
            return made.ToArray();
        }

        /// <summary>Puts this thing in place of whatever the list had for the slot, or adds it.</summary>
        private static UIEquipmentInfo[] Replace(UIEquipmentInfo[] list, EquipSlotType where,
            UIEquipmentInfo gear)
        {
            if (list != null)
            {
                for (int i = 0; i < list.Length; i++)
                {
                    if (list[i] != null && list[i].EquipType == where)
                    {
                        UIEquipmentInfo[] made = (UIEquipmentInfo[])list.Clone();
                        made[i] = gear;
                        return made;
                    }
                }
            }

            return Add(list, gear);
        }

        // ------------------------------------------------------------------ та же вещь

        // Ступень в имени вещи пишется «_T3»: «KnightSword_T3_01», «HalfPlate_Helmet_T3».
        // Без неё остаётся линейка — то, что делает рыцарский меч рыцарским на любой ступени.
        private static readonly Regex Rung = new Regex(@"_T[0-5](?=_|$)", RegexOptions.Compiled);
        private static readonly Regex Variant = new Regex(@"_\d{1,2}$", RegexOptions.Compiled);

        private static Dictionary<string, List<UIEquipmentInfo>> lines;

        private static void Index()
        {
            if (lines != null) return;

            UIItemDatabase db = UIItemDatabase.Instance;
            if (db == null || db.items == null) return;

            lines = new Dictionary<string, List<UIEquipmentInfo>>(StringComparer.Ordinal);

            foreach (UIItemInfo thing in db.items)
            {
                UIEquipmentInfo gear = thing as UIEquipmentInfo;
                if (gear == null || gear.isUnique || gear.noRandomQuality) continue;
                if (gear.EquipType == EquipSlotType.relic || gear.itemType == ItemType.Quest) continue;
                if (string.IsNullOrEmpty(gear.Name)) continue;

                foreach (string key in Keys(gear.Name))
                {
                    List<UIEquipmentInfo> row;
                    if (!lines.TryGetValue(key, out row))
                    {
                        row = new List<UIEquipmentInfo>();
                        lines[key] = row;
                    }

                    row.Add(gear);
                }
            }
        }

        /// <summary>The line this thing belongs to, narrow first: with its variant, then without.</summary>
        private static IEnumerable<string> Keys(string name)
        {
            if (!Rung.IsMatch(name)) yield break;

            string line = Rung.Replace(name, "");
            yield return line;

            string wide = Variant.Replace(line, "");
            if (wide != line) yield return wide + "#";
        }

        /// <summary>
        /// The same thing at another tier: same line if the line goes that high, otherwise the
        /// same class in the same slot. Null when there is nothing honest to give.
        /// </summary>
        internal static UIEquipmentInfo Same(UIEquipmentInfo gear, ItemTier tier, ArmourClass? klass)
        {
            if (gear == null) return null;

            Index();

            UIArmorInfo coat = gear as UIArmorInfo;
            UIWeaponInfo blade = gear as UIWeaponInfo;

            bool fits(UIEquipmentInfo other)
            {
                if (other == null || other.tier != tier || other.EquipType != gear.EquipType) return false;

                if (coat != null)
                {
                    UIArmorInfo their = other as UIArmorInfo;
                    return their != null && their.armourClass == (klass ?? coat.armourClass);
                }

                if (blade != null)
                {
                    UIWeaponInfo their = other as UIWeaponInfo;
                    return their != null && their.WeaponType == blade.WeaponType
                        && their.weaponClass == blade.weaponClass;
                }

                return other.GetType() == gear.GetType();
            }

            // Линейку спрашиваем только тогда, когда класс не меняется: полулаты из линейки
            // лат — это уже другие латы, их ищут по классу.
            bool sameClass = coat == null || !klass.HasValue || klass.Value == coat.armourClass;

            if (sameClass && lines != null && !string.IsNullOrEmpty(gear.Name))
            {
                foreach (string key in Keys(gear.Name))
                {
                    List<UIEquipmentInfo> row;
                    if (!lines.TryGetValue(key, out row)) continue;

                    foreach (UIEquipmentInfo other in row)
                    {
                        if (fits(other)) return other;
                    }
                }
            }

            if (coat != null) return Armoury.Find(klass ?? coat.armourClass, coat.EquipType, tier);

            if (blade != null)
            {
                UIItemDatabase db = UIItemDatabase.Instance;
                if (db == null || db.items == null) return null;

                foreach (UIItemInfo thing in db.items)
                {
                    UIWeaponInfo other = thing as UIWeaponInfo;
                    if (other == null || other.isUnique || other.noRandomQuality) continue;
                    if (fits(other)) return other;
                }
            }

            return null;
        }
    }

    // Список правится до одевания. Раньше всех наших: остальным — износу, силе под груз —
    // нужно уже то, что надето по правленому списку.
    [HarmonyPatch(typeof(EquipmentManager), "InitEquips")]
    internal static class InitEquips_Birth_Patch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(EquipmentManager __instance)
        {
            Birth.Before(__instance);
        }

        [HarmonyPriority(Priority.First)]
        private static void Postfix(EquipmentManager __instance)
        {
            Birth.After(__instance);
        }
    }
}
