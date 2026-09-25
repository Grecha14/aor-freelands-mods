using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using DuloGames.UI;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// A rack of every weapon the world has, for when the point is to try them.
    ///
    /// Разведка по замахам требует бить всем подряд — мечом, топором, молотом, копьём, луком,
    /// арбалетом, — а собирать это по лавкам и сундукам дольше, чем потом читать лог. Одна
    /// клавиша кладёт в сумку по одному образцу каждого вида.
    ///
    /// Берётся не лучшее и не редкое: обычная вещь средней ступени, какую носит всякий второй.
    /// Уникальные пропускаются нарочно — единственный в мире клинок, размноженный по нажатию,
    /// перестаёт быть единственным, и это тот сорт мелочи, который потом всплывает в сохранении
    /// через полгода.
    ///
    /// Колчаны кладутся вместе с луками: без них стрелять нечем, и разведка по стрелковому
    /// молчала бы ровно так же, как молчала по всему.
    /// </summary>
    internal static class Armoury
    {
        internal static ConfigEntry<KeyCode> Key;
        internal static ConfigEntry<string> Tier;
        internal static ConfigEntry<bool> Unique;
        internal static ConfigEntry<string> Only;
        internal static ConfigEntry<string> Grade;

        internal static void Bind(ConfigFile config)
        {
            Key = config.Bind("Armoury", "Key", KeyCode.None,
                "Press this in game to put one of every kind of weapon into the bag. Meant for "
                + "trying them, not for playing with.");

            Tier = config.Bind("Armoury", "Tier", "T3",
                "Which tier to prefer. The nearest available is taken when a kind has nothing at "
                + "this one.");

            Grade = config.Bind("Armoury", "Grade", "Common",
                "Which quality to hand out. The nearest available is taken when a kind has "
                + "nothing of it. Legendary for testing: those carry the bonuses written by "
                + "hand, so what the swing does and what the tooltip claims can be told apart.");

            Unique = config.Bind("Armoury", "Unique", false,
                "Whether one-of-a-kind weapons may be handed out too. Off: a unique blade copied "
                + "by a keypress stops being unique, and that is the sort of thing that surfaces "
                + "in a save six months later.");

            Only = config.Bind("Armoury", "Only",
                "GreatSword,Katana,Rapier,Sword,Spear,Dagger,RoundShield,Staff",
                "Kinds to hand out, by the game's own class names, separated by commas. Empty "
                + "means one of every kind there is, which is forty-odd and mostly pointless: "
                + "swings belong to a branch of animation, not to a kind of weapon, and a dozen "
                + "kinds share one branch. What stands here is only what is still unmeasured — "
                + "the two-hander and the katana, which have no standing swings recorded at all; "
                + "the rapier, which has half of them; and a shield with the one-handers to go "
                + "beside it, since a shield in the off hand makes every swing its own.");
        }

        internal static void Rack()
        {
            try
            {
                HumaniodUnit who = gameManager.currentplayUnit as HumaniodUnit;
                if (who == null)
                {
                    ItemForgePlugin.Log.LogWarning("Оружейная: некому выдавать, игра ещё не началась.");
                    return;
                }

                UIItemDatabase book = UIItemDatabase.Instance;
                if (book == null || book.items == null)
                {
                    ItemForgePlugin.Log.LogWarning("Оружейная: база предметов ещё не поднята.");
                    return;
                }

                HashSet<string> asked = Asked();
                int want = Wanted();
                int grade = Graded();

                // По одному образцу на вид: ближайший к нужной ступени, обычный, не уникальный.
                Dictionary<WeaponClass, UIWeaponInfo> picked =
                    new Dictionary<WeaponClass, UIWeaponInfo>();

                foreach (UIItemInfo thing in book.items)
                {
                    UIWeaponInfo blade = thing as UIWeaponInfo;
                    if (blade == null) continue;
                    if (blade.isUnique && !Unique.Value) continue;
                    if (blade.weaponClass == WeaponClass.Empty) continue;

                    // Колчан — это и есть стрелы: каждый выстрел снимает с него единицу
                    // прочности, и сотня прочности означает сотню стрел. Бить им нельзя, и по
                    // общему правилу «не выдаём то, чем не бьют» он бы отсеялся — а без него
                    // лук в сумке бесполезен.
                    bool ammo = blade.WeaponType == WeaponType.quiver;

                    if (blade.disableAttack && !ammo) continue;

                    if (asked.Count > 0
                        && !asked.Contains(blade.weaponClass.ToString().ToLowerInvariant())) continue;

                    UIWeaponInfo had;
                    if (!picked.TryGetValue(blade.weaponClass, out had))
                    {
                        picked[blade.weaponClass] = blade;
                        continue;
                    }

                    if (Nearer(blade, had, want, grade)) picked[blade.weaponClass] = blade;
                }

                if (picked.Count == 0)
                {
                    ItemForgePlugin.Log.LogWarning("Оружейная: ничего не подошло под запрос.");
                    return;
                }

                StringBuilder given = new StringBuilder();
                int count = 0;

                foreach (KeyValuePair<WeaponClass, UIWeaponInfo> one in picked)
                {
                    if (!Hand(who, one.Value)) continue;

                    if (given.Length > 0) given.Append(", ");
                    given.Append(one.Key).Append(" (").Append(one.Value.Quality)
                        .Append(" ").Append(one.Value.tier);

                    if (one.Value.WeaponType == WeaponType.quiver)
                    {
                        given.Append(", стрел ").Append(one.Value.durability.ToString("0"));
                    }

                    given.Append(")");
                    count++;
                }

                ItemForgePlugin.Log.LogInfo("Оружейная выдала " + count + " видов: " + given + ".");

                GameController.ShowMessage("Оружие в сумке: " + count + " видов", 3f);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Оружейная не сработала: " + e);
            }
        }

        private static bool Nearer(UIWeaponInfo one, UIWeaponInfo than, int want, int grade)
        {
            // Качество решает первым: просили легендарное — значит легендарное, даже если оно
            // на ступень ниже заказанной.
            int mineGrade = Mathf.Abs((int)one.Quality - grade);
            int theirsGrade = Mathf.Abs((int)than.Quality - grade);

            if (mineGrade != theirsGrade) return mineGrade < theirsGrade;

            int mine = Mathf.Abs((int)one.tier - want);
            int theirs = Mathf.Abs((int)than.tier - want);

            return mine < theirs;
        }

        private static int Graded()
        {
            string written = (Grade.Value ?? "").Trim();

            try
            {
                return (int)(UIItemQuality)Enum.Parse(typeof(UIItemQuality), written, true);
            }
            catch
            {
                return (int)UIItemQuality.Legendary;
            }
        }

        private static bool Hand(HumaniodUnit who, UIWeaponInfo blade)
        {
            try
            {
                Inventory bit = new Inventory(blade, 1);

                if (InventoryManager.instance != null
                    && (object)InventoryManager.instance.currentTarget == (object)who)
                {
                    InventoryManager.instance.PutItemInbag(bit);
                }
                else
                {
                    who.items.AddInventory(bit);
                }

                return true;
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог выдать «" + blade.Name + "»: " + e.Message);
                return false;
            }
        }

        private static int Wanted()
        {
            string written = (Tier.Value ?? "").Trim();

            try
            {
                return (int)(ItemTier)Enum.Parse(typeof(ItemTier), written, true);
            }
            catch
            {
                return (int)ItemTier.T3;
            }
        }

        private static HashSet<string> Asked()
        {
            HashSet<string> named = new HashSet<string>();

            foreach (string one in (Only.Value ?? "").Split(','))
            {
                string name = one.Trim().ToLowerInvariant();
                if (name.Length > 0) named.Add(name);
            }

            return named;
        }

        // ------------------------------------------------------------------ общий поиск

        private static readonly Dictionary<string, UIEquipmentInfo> shelf =
            new Dictionary<string, UIEquipmentInfo>(StringComparer.Ordinal);

        /// <summary>
        /// One place that answers «what piece of this kind exists at this tier».
        ///
        /// Один и тот же вопрос задавали трое: страже надо переодеть в полулаты, походу —
        /// вождя в латы, кругу — магов в ткань. Три копии одного перебора по базе расходятся
        /// на первой же правке, и расходились: один искал по классу, другой по типу гнезда,
        /// третий забывал про уникальные.
        /// </summary>
        internal static UIArmorInfo Find(ArmourClass klass, EquipSlotType where, ItemTier tier)
        {
            return Pick("a" + klass + where + tier, delegate (UIEquipmentInfo gear)
            {
                UIArmorInfo coat = gear as UIArmorInfo;
                return coat != null && coat.armourClass == klass
                    && coat.EquipType == where && coat.tier == tier;
            }) as UIArmorInfo;
        }

        internal static UIWeaponInfo Blade(WeaponType hand, ItemTier tier)
        {
            return Pick("w" + hand + tier, delegate (UIEquipmentInfo gear)
            {
                UIWeaponInfo arm = gear as UIWeaponInfo;
                return arm != null && arm.WeaponType == hand && arm.tier == tier;
            }) as UIWeaponInfo;
        }

        internal static UIEquipmentInfo Trinket(EquipSlotType where, ItemTier tier)
        {
            return Pick("t" + where + tier, delegate (UIEquipmentInfo gear)
            {
                return gear.EquipType == where && gear.tier == tier;
            });
        }

        private static UIEquipmentInfo Pick(string key, Predicate<UIEquipmentInfo> fits)
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
                        if (gear == null || gear.isUnique || gear.noRandomQuality) continue;
                        if (gear.EquipType == EquipSlotType.relic) continue;
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
