using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace DemonLook
{
    /// <summary>
    /// The world written down, to reckon its balance outside the game.
    ///
    /// Раз за сеанс, как только сохранение загружено, мод выписывает в aor.world.txt всё, на
    /// чём стоит хозяйство: города и деревни с их промыслами и нехватками, расстояния между
    /// ними, рецепты мастеров с материалами, все вещи с видом товара, ценой и весом, чем
    /// торгуют лавки каждого города, каким будет урожай каждого места на год вперёд, и что
    /// хозяйство мода знает сейчас: переписи, склады, торговцев, шайки, подземелья, отряды. На
    /// карте мира к этому добавляется сама карта: где что стоит и какие дороги куда ведут.
    /// Игры выписка не меняет — только читает её и пишет файл.
    /// </summary>
    internal static class Almanac
    {
        internal static ConfigEntry<bool> Enabled;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Almanac", "Enabled", true,
                "Once a session, write the towns, roads, recipes, goods and shops of the world to aor.world.txt, to reckon the balance outside the game.");
        }

        private static bool written;
        private static bool mapped;
        private static float due = -1f;
        private static readonly List<string> map = new List<string>();

        internal static void Tick()
        {
            if (Enabled == null || !Enabled.Value || mapped) return;
            if (PartyManager.instance == null || PartyManager.instance.leader == null) return;
            if (WorldPlacesManager.instance == null || TimeManager.Instance == null || Souls.Today() < 0) return;

            bool onMap = (bool)WorldTravelManager.instance;
            if (written && !onMap) return;

            // Дать месту устояться: сцене — населиться, карте — расставить свои места.
            float now = Time.unscaledTime;
            if (due < 0f) { due = now + (onMap ? 10f : 30f); return; }
            if (now < due) return;
            due = -1f;

            try
            {
                if (onMap) Map();
                Write();
                written = true;
                mapped = onMap;
            }
            catch (Exception e)
            {
                written = true;
                mapped = true;
                DemonLookPlugin.Log.LogWarning("Выписка мира не удалась: " + e);
            }
        }

        // ------------------------------------------------------------------ карта

        private static void Map()
        {
            WorldTravelManager wtm = WorldTravelManager.instance;
            map.Clear();

            List<WorldPlace> seen = new List<WorldPlace>();
            Places(wtm.worldTowns != null ? wtm.worldTowns.ConvertAll<WorldPlace>(t => t) : null, "town", seen);
            Places(wtm.dungeons, "dungeon", seen);
            Places(wtm.worldArenas, "arena", seen);
            Places(wtm.wildPlaces, "wild", seen);
            Places(wtm.worldPlaces, "place", seen);

            HashSet<string> roads = new HashSet<string>(StringComparer.Ordinal);
            foreach (WorldPlace p in seen)
            {
                if (p.roads == null) continue;
                foreach (WorldRoad r in p.roads)
                {
                    if (r == null || r.connectedA == null || r.connectedB == null) continue;
                    string a = r.connectedA.name, b = r.connectedB.name;
                    string key = string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;
                    if (!roads.Add(key)) continue;
                    map.Add("road=" + Clean(a) + ";" + Clean(b) + ";" + F(r.distance));
                }
            }
        }

        private static void Places(List<WorldPlace> list, string kind, List<WorldPlace> seen)
        {
            if (list == null) return;
            foreach (WorldPlace p in list)
            {
                if (p == null || seen.Contains(p)) continue;
                seen.Add(p);
                Vector3 at = p.transform.position;
                map.Add("map=" + kind + ";" + Clean(p.name) + ";" + p.areaType + ";" + p.faction + ";" + Clean(p.linkedScene) + ";"
                    + F(at.x) + ";" + F(at.z) + ";" + F(p.clearRange));
            }
        }

        // ------------------------------------------------------------------ выписка

        private static void Write()
        {
            WorldPlacesManager wpm = WorldPlacesManager.instance;
            UIItemDatabase db = UIItemDatabase.Instance;
            List<string> lines = new List<string>
            {
                "# DemonLook: выписка мира " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                "today=" + Souls.Today(),
                "hour=" + TimeManager.Hour,
                "archive=" + Archive(),
            };

            // Места и города — как их знает игра, с промыслами, нехватками и товаром лавок.
            int towns = 0;
            if (wpm.worldPlaces != null)
            {
                foreach (WorldPlaceInfo p in wpm.worldPlaces)
                {
                    if (p != null) lines.Add("place=" + Clean(p.name) + ";" + p.type + ";" + p.faction);
                }
            }
            if (wpm.worldTowns != null)
            {
                foreach (WorldTownInfo t in wpm.worldTowns)
                {
                    if (t == null) continue;
                    towns++;
                    lines.Add("town=" + Clean(t.name) + ";" + t.type + ";" + t.faction + ";" + F(t.security) + ";"
                        + Types(t.specialties) + ";" + Types(t.shortages));

                    if (t.goodsScarcity != null)
                    {
                        List<string> s = new List<string>();
                        foreach (KeyValuePair<GoodsType, float> g in t.goodsScarcity) s.Add(g.Key + ":" + F(g.Value));
                        lines.Add("scarcity=" + Clean(t.name) + ";" + string.Join(",", s.ToArray()));
                    }

                    Shop(lines, t.name, "weapon", t.weaponGoods);
                    Shop(lines, t.name, "armour", t.armourGoods);
                    Shop(lines, t.name, "accessories", t.accessoriesGoods);
                    Shop(lines, t.name, "alchemy", t.alchemyGoods);
                    Shop(lines, t.name, "material", t.meterialGoods);
                    Shop(lines, t.name, "food", t.foodsGoods);
                }
            }

            // Общий товар бродячих торговцев и караванов.
            if (db != null)
            {
                Shop(lines, "*", "weapon", db.commonWeaponGoods);
                Shop(lines, "*", "armour", db.commonArmorGoods);
                Shop(lines, "*", "ornament", db.commonOrnamentGoods);
                Shop(lines, "*", "material", db.commonMaterialGoods);
                Shop(lines, "*", "alchemy", db.commonAlchemyGoods);
                Shop(lines, "*", "food", db.commonFoodGoods);
                Shop(lines, "*", "magic", db.commonMagicGoods);
                Shop(lines, "*", "dirty", db.commonDirtyGoods);
                Shop(lines, "*", "book", db.commonBookGoods);
                Shop(lines, "*", "drink", db.commonDrinkGoods);
                Shop(lines, "*", "bakery", db.commonBakeryGoods);
                Shop(lines, "*", "fruit", db.commonFruitGoods);
                Shop(lines, "*", "meat", db.commonMeatGoods);
                Shop(lines, "*", "fish", db.commonFishGoods);
            }

            int dists = 0;
            if (wpm.distanceMap != null)
            {
                foreach (KeyValuePair<string, float> d in wpm.distanceMap)
                {
                    lines.Add("dist=" + Clean(d.Key) + ";" + F(d.Value));
                    dists++;
                }
            }

            // Рецепты — с материалами поштучно; вещи — все, и те, что есть только в рецептах.
            int recipes = 0, items = 0;
            HashSet<int> listed = new HashSet<int>();
            List<UIItemInfo> refs = new List<UIItemInfo>();
            if (db != null && db.recipes != null)
            {
                foreach (CraftRecipe r in db.recipes)
                {
                    if (r == null) continue;
                    List<string> mats = new List<string>();
                    if (r.materials != null)
                    {
                        foreach (Inventory m in r.materials)
                        {
                            if (m == null || m.itemInfo == null) continue;
                            mats.Add(m.itemInfo.ID + ":" + m.stackNum);
                            refs.Add(m.itemInfo);
                        }
                    }
                    if (r.product != null) refs.Add(r.product);
                    lines.Add("recipe=" + r.id + ";" + r.ID + ";" + Clean(r.name) + ";" + (r.product != null ? r.product.ID : -1) + ";"
                        + r.productNum + ";" + r.requireLv + ";" + r.cost + ";" + string.Join(",", mats.ToArray()));
                    recipes++;
                }
            }
            if (db != null && db.items != null)
            {
                foreach (UIItemInfo i in db.items)
                {
                    if (i == null || !listed.Add(i.ID)) continue;
                    lines.Add(Item(i));
                    items++;
                }
            }
            foreach (UIItemInfo i in refs)
            {
                if (i == null || !listed.Add(i.ID)) continue;
                lines.Add(Item(i));
                items++;
            }

            // Урожай каждого места на год вперёд — тем же жребием, что бросает хозяйство.
            int week = Souls.Today() / 7;
            foreach (string name in EconomyNames(wpm))
            {
                List<string> w = new List<string>();
                for (int k = 0; k < 53; k++)
                {
                    int wk = week + k;
                    float h = 1f + (float)(new System.Random(unchecked(wk * 104729 + name.GetHashCode())).NextDouble() * 2.0 - 1.0);
                    w.Add(h.ToString("0.###", CultureInfo.InvariantCulture));
                }
                lines.Add("harvest=" + Clean(name) + ";" + week + ";" + string.Join(",", w.ToArray()));
            }

            // Что хозяйство знает сейчас, ещё не сохранённое.
            foreach (KeyValuePair<string, string> s in Census.scenes) lines.Add("scene=" + s.Key + ";" + s.Value);
            foreach (Economy.Place p in Economy.Places)
            {
                List<string> store = new List<string>();
                foreach (GoodsType t in Economy.Raw) store.Add(t + ":" + F(p.Has(t)));
                lines.Add("state=" + Clean(p.name) + ";" + (p.village ? 1 : 0) + ";" + p.mouths + ";" + (p.census != null ? p.census.Write() : "") + ";"
                    + F(p.Has(GoodsType.Produce)) + ";" + F(p.Has(GoodsType.Meat)) + ";" + string.Join(",", store.ToArray()) + ";"
                    + F(p.harvest) + ";" + F(p.tavernCoin) + ";" + F(p.smithCoin));
            }
            DemonLook.Trade.Write(lines);
            Sites.Write(lines);
            Bands.Write(lines);

            lines.AddRange(map);

            string path = Path.Combine(BepInEx.Paths.ConfigPath, "aor.world.txt");
            File.WriteAllLines(path, lines.ToArray());

            DemonLookPlugin.Log.LogInfo($"Выписка мира: {path}: городов {towns}, расстояний {dists}, рецептов {recipes}, вещей {items}"
                + (map.Count > 0 ? $", карта {map.Count} строк." : ", карты ещё нет — она допишется на карте мира."));
            if (map.Count > 0) Souls.Say("Мир выписан для расчёта баланса: aor.world.txt.");
        }

        /// <summary>The places the economy keeps, under the names it keeps them by.</summary>
        private static List<string> EconomyNames(WorldPlacesManager wpm)
        {
            List<string> names = new List<string>();
            if (wpm.worldTowns != null)
            {
                foreach (WorldTownInfo t in wpm.worldTowns)
                {
                    if (t == null || string.IsNullOrEmpty(t.name)) continue;
                    if (t.type != WorldPlaceType.city && t.type != WorldPlaceType.village) continue;
                    if (!names.Contains(t.name)) names.Add(t.name);
                }
            }
            if (wpm.worldPlaces != null)
            {
                foreach (WorldPlaceInfo p in wpm.worldPlaces)
                {
                    if (p == null || string.IsNullOrEmpty(p.name) || p.type != WorldPlaceType.village) continue;
                    if (!names.Contains(p.name)) names.Add(p.name);
                }
            }
            return names;
        }

        private static void Shop(List<string> lines, string town, string kind, ShopGoodsList list)
        {
            if (list == null) return;
            lines.Add("shop=" + Clean(town) + ";" + kind + ";" + list.refreshNum + ";" + list.refreshNum_Material + ";"
                + list.refreshNum_Consumable + ";" + list.refreshNum_Recipe);
            Sets(lines, town, kind, "goods", list.goodsList);
            Sets(lines, town, kind, "material", list.goodsList_Material);
            Sets(lines, town, kind, "consumable", list.goodsList_Consumable);
            Sets(lines, town, kind, "recipe", list.goodsList_Recipe);
        }

        private static void Sets(List<string> lines, string town, string kind, string part, List<GoodsSet> sets)
        {
            if (sets == null) return;
            foreach (GoodsSet s in sets)
            {
                if (s == null) continue;
                List<string> ids = new List<string>();
                if (s.item != null) foreach (UIItemInfo i in s.item) if (i != null) ids.Add(i.ID.ToString(CultureInfo.InvariantCulture));
                int lo = 1, hi = 1;
                if (s.stackNum != null)
                {
                    if (s.stackNum.Type == 0) lo = hi = s.stackNum.Base;
                    else { lo = s.stackNum.Min; hi = s.stackNum.Max; }
                }
                lines.Add("set=" + Clean(town) + ";" + kind + ";" + part + ";" + Clean(s.setName) + ";" + F(s.randomWeight) + ";"
                    + lo + ";" + hi + ";" + (s.mustOnce ? 1 : 0) + ";" + (s.onlyOnce ? 1 : 0) + ";" + string.Join(",", ids.ToArray()));
            }
        }

        private static string Item(UIItemInfo i)
        {
            string local;
            try { local = i.LocalizedName; }
            catch { local = ""; }

            string sub = "";
            UIWeaponInfo weapon = i as UIWeaponInfo;
            UIEquipmentInfo gear = i as UIEquipmentInfo;
            UIConsumableInfo drink = i as UIConsumableInfo;
            if (weapon != null) sub = "weapon:" + weapon.WeaponType;
            else if (i is UIArmorInfo && gear != null) sub = "armour:" + gear.EquipType;
            else if (gear != null) sub = "gear:" + gear.EquipType;
            else if (drink != null) sub = "use:" + drink.consumableType;

            return "item=" + i.ID + ";" + Clean(i.Name) + ";" + Clean(local) + ";" + i.GetType().Name + ";" + i.itemType + ";" + i.goodsType + ";"
                + Types(i.goodsTypes) + ";" + i.tier + ";" + i.value + ";" + F(i.weight) + ";" + F(i.durability) + ";"
                + (i.isUnique ? 1 : 0) + ";" + (i.stackable ? 1 : 0) + ";" + sub;
        }

        private static System.Reflection.FieldInfo archiveField;

        private static string Archive()
        {
            try
            {
                if (archiveField == null) archiveField = AccessTools.Field(typeof(SaveLoadManager), "currentArchive");
                return (archiveField != null && SaveLoadManager.Instance != null ? archiveField.GetValue(SaveLoadManager.Instance) as string : null) ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static string Types(IEnumerable<GoodsType> list)
        {
            if (list == null) return "";
            List<string> s = new List<string>();
            foreach (GoodsType t in list) s.Add(t.ToString());
            return string.Join(",", s.ToArray());
        }

        private static string F(float v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Clean(string s)
        {
            return (s ?? "").Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
