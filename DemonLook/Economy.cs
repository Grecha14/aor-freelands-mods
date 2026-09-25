using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using spell;
using UnityEngine;

namespace DemonLook
{
    /// <summary>
    /// The towns and villages keep house: stores, harvests, wages and craftsmen.
    ///
    /// У всякого города и всякой деревни свой склад: металл, дерево, ткань, кожа, камни, травы,
    /// серебро, золото — и еда. Каждый день место добывает своё: что у него в промысле — по пять,
    /// прочее — по два, чего ему не хватает — ничего. Еду растят деревни, по двадцать в день, и
    /// урожай у каждой свой и меняется раз в неделю: от неурожая, когда не родится ничего, до
    /// изобилия вдвое против обычного.
    ///
    /// Горожанин съедает единицу еды в день. Жалованье — каждый день в кошелёк: простому люду
    /// пятьдесят медных, мастерам и торговцам сто, страже сто пятьдесят; еда стоит тридцать. Кто
    /// набил кошелёк, того и обчистить есть на что. Мало еды на складе — дорожает еда и пустеет
    /// продуктовая лавка; много — дешевеет.
    ///
    /// Мастера города каждый день делают из склада по рецептам игры два оружия или доспеха, щит,
    /// три зелья и пояс, амулет или кольцо — если хватает материалов. Сделанное ложится в лавки
    /// этого города. Проданное вами сырьё идёт на склад и тоже в дело.
    ///
    /// Раз в день бродячие торговцы везут излишки к трём ближним по дороге соседям, у кого этого
    /// не хватает: до десяти единиц всякого товара на дорогу. Еда так и идёт — из деревень в
    /// города. Караван, что дошёл до места на карте мира, разгружается: сырьё и еда — на склад,
    /// готовые вещи — в лавки. Разбитый караван не довёз ничего.
    /// </summary>
    internal static class Economy
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Specialty;
        internal static ConfigEntry<int> Common;
        internal static ConfigEntry<int> Harvest;
        internal static ConfigEntry<int> VillageMouths;
        internal static ConfigEntry<int> TownMouths;
        internal static ConfigEntry<string> Wages;
        internal static ConfigEntry<int> Meal;

        internal static ConfigEntry<int> Haul;
        internal static ConfigEntry<int> Neighbours;
        internal static ConfigEntry<int> RawKeep;
        internal static ConfigEntry<int> PlantDays;
        internal static ConfigEntry<int> MeatDays;
        internal static ConfigEntry<int> StoreCap;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Economy", "Enabled", true,
                "Let the towns and villages keep stores, grow food, pay wages and make goods.");

            Haul = config.Bind("Economy", "Haul", 10,
                new ConfigDescription("How much of each good the traders carry along one road a day.", new AcceptableValueRange<int>(0, 500)));

            Neighbours = config.Bind("Economy", "Neighbours", 3,
                new ConfigDescription("With how many of the nearest places by road each place trades.", new AcceptableValueRange<int>(0, 10)));

            PlantDays = config.Bind("Economy", "PlantDays", 7,
                new ConfigDescription("Days vegetables, fruit and grain keep in a store.", new AcceptableValueRange<int>(1, 365)));

            MeatDays = config.Bind("Economy", "MeatDays", 3,
                new ConfigDescription("Days meat and fish keep in a store.", new AcceptableValueRange<int>(1, 365)));

            StoreCap = config.Bind("Economy", "StoreCap", 200,
                new ConfigDescription("Beyond this much of a material a place stops getting more: the hands idle, nothing is thrown away.",
                    new AcceptableValueRange<int>(10, 100000)));

            RawKeep = config.Bind("Economy", "RawKeep", 30,
                new ConfigDescription("How much of each material a place keeps before it sells the rest on.", new AcceptableValueRange<int>(0, 500)));

            Specialty = config.Bind("Economy", "Specialty", 5,
                new ConfigDescription("What a place yields a day of what it is known for.", new AcceptableValueRange<int>(0, 100)));

            Common = config.Bind("Economy", "Common", 2,
                new ConfigDescription("What a place yields a day of anything else it does not lack.", new AcceptableValueRange<int>(0, 100)));

            Harvest = config.Bind("Economy", "Harvest", 20,
                new ConfigDescription("Food a village grows a day in an ordinary week; each week it runs from none to twice this.",
                    new AcceptableValueRange<int>(0, 500)));

            VillageMouths = config.Bind("Economy", "VillageMouths", 8,
                new ConfigDescription("How many a village feeds itself.", new AcceptableValueRange<int>(0, 500)));

            TownMouths = config.Bind("Economy", "TownMouths", 40,
                new ConfigDescription("How many a town is reckoned to feed until the hero has counted them himself.",
                    new AcceptableValueRange<int>(1, 2000)));

            Wages = config.Bind("Economy", "Wages", "50,100,150",
                "Copper a day for common folk, for craftsmen and traders, and for the guard.");

            Meal = config.Bind("Economy", "Meal", 30,
                new ConfigDescription("Copper a townsman pays a day for his food.", new AcceptableValueRange<int>(0, 1000)));
        }

        internal static bool On()
        {
            return Enabled != null && Enabled.Value;
        }

        internal static readonly GoodsType[] Raw =
        {
            GoodsType.Metal, GoodsType.Wood, GoodsType.Cloth, GoodsType.Leather,
            GoodsType.Gem, GoodsType.Alchemy, GoodsType.Silver, GoodsType.Gold,
        };

        internal static bool Food(GoodsType t)
        {
            return t == GoodsType.Meat || t == GoodsType.Produce;
        }

        internal static bool Tracked(GoodsType t)
        {
            return Food(t) || Array.IndexOf(Raw, t) >= 0;
        }

        // ------------------------------------------------------------------ места

        /// <summary>Food of one day's getting: it keeps as long as its kind keeps.</summary>
        internal class Batch
        {
            internal GoodsType t;
            internal float n;
            internal int day;
        }

        internal class Place
        {
            internal string name;
            internal bool village;
            internal readonly Dictionary<GoodsType, float> store = new Dictionary<GoodsType, float>();
            internal readonly List<Batch> food = new List<Batch>();
            internal float harvest = 1f;
            internal int harvestWeek = -1;
            internal int mouths;
            internal int paid = -1;
            internal readonly Dictionary<int, List<int>> queue = new Dictionary<int, List<int>>();

            // Перепись: кто здесь живёт, по последнему приходу героя. Пока её нет — считается по виду.
            internal Census census;

            // Что раскупили без героя: наёмники, отряды, горожане — по лавкам; и выручка трактира.
            internal readonly Dictionary<int, float> demand = new Dictionary<int, float>();
            internal float tavernCoin;
            internal float smithCoin;

            internal float Has(GoodsType t)
            {
                if (Food(t))
                {
                    float sum = 0f;
                    foreach (Batch b in food) if (b.t == t) sum += b.n;
                    return sum;
                }

                float v;
                return store.TryGetValue(t, out v) ? v : 0f;
            }

            internal void Add(GoodsType t, float v)
            {
                if (!Food(t))
                {
                    store[t] = Mathf.Max(0f, Has(t) + v);
                    return;
                }

                if (v > 0f)
                {
                    int today = Souls.Today();
                    Batch last = food.Count > 0 ? food[food.Count - 1] : null;
                    foreach (Batch b in food)
                    {
                        if (b.t == t && b.day == today) { last = b; break; }
                    }
                    if (last != null && last.t == t && last.day == today) last.n += v;
                    else food.Add(new Batch { t = t, n = v, day = today });
                    return;
                }

                Take(t, -v);
            }

            /// <summary>Takes from the oldest food first.</summary>
            internal float Take(GoodsType t, float need)
            {
                float taken = 0f;
                food.Sort((a, b) => a.day.CompareTo(b.day));
                for (int i = 0; i < food.Count && taken < need; i++)
                {
                    Batch b = food[i];
                    if (b.t != t) continue;
                    float bite = Mathf.Min(need - taken, b.n);
                    b.n -= bite;
                    taken += bite;
                }
                food.RemoveAll(b => b.n <= 0.0001f);
                return taken;
            }

            internal float FoodStock()
            {
                float sum = 0f;
                foreach (Batch b in food) sum += b.n;
                return sum;
            }

            /// <summary>Eats from the food store, the oldest first: meat and produce alike.</summary>
            internal float Eat(float need)
            {
                float eaten = 0f;
                food.Sort((a, b) => a.day.CompareTo(b.day));
                for (int i = 0; i < food.Count && eaten < need; i++)
                {
                    float bite = Mathf.Min(need - eaten, food[i].n);
                    food[i].n -= bite;
                    eaten += bite;
                }
                food.RemoveAll(b => b.n <= 0.0001f);
                return eaten;
            }

            /// <summary>What has kept too long goes bad: vegetables in a week, meat in three days.</summary>
            internal float Spoil(int today)
            {
                float gone = 0f;
                for (int i = food.Count - 1; i >= 0; i--)
                {
                    Batch b = food[i];
                    int keeps = b.t == GoodsType.Produce ? PlantDays.Value : MeatDays.Value;
                    if (today - b.day < keeps) continue;
                    gone += b.n;
                    food.RemoveAt(i);
                }
                return gone;
            }

            internal void AddDemand(int shop, float n)
            {
                float had;
                demand.TryGetValue(shop, out had);
                demand[shop] = Mathf.Min(had + n, 60f);
            }
        }

        private static readonly Dictionary<string, Place> places = new Dictionary<string, Place>(StringComparer.Ordinal);
        internal static IEnumerable<Place> Places => places.Values;

        internal static Place Get(string name)
        {
            Place p;
            if (name == null) return null;
            if (places.TryGetValue(name, out p)) return p;
            string other = Alias(name);
            return other != null && places.TryGetValue(other, out p) ? p : null;
        }

        // Игра на ходу переименовывает Золотую Гавань; в её же данных живут оба имени.
        private static string Alias(string name)
        {
            if (name == "GoldenHarbor") return "GoldenHabor";
            if (name == "GoldenHabor") return "GoldenHarbor";
            return null;
        }

        private static int day = -1;

        /// <summary>The places of the world, from the game's own list of them.</summary>
        private static void Survey()
        {
            WorldPlacesManager wpm = WorldPlacesManager.instance;
            if (wpm == null) return;

            // Города — под тем именем, под которым их знают цены и лавки: часть мест игра
            // переименовывает на ходу, и имя из общего списка мест с ним расходится.
            if (wpm.worldTowns != null)
            {
                foreach (WorldTownInfo town in wpm.worldTowns)
                {
                    if (town == null || string.IsNullOrEmpty(town.name)) continue;
                    if (town.type != WorldPlaceType.city && town.type != WorldPlaceType.village) continue;
                    Found(town.name, town.type == WorldPlaceType.village);
                }
            }

            if (wpm.worldPlaces != null)
            {
                foreach (WorldPlaceInfo info in wpm.worldPlaces)
                {
                    if (info == null || string.IsNullOrEmpty(info.name) || info.type != WorldPlaceType.village) continue;
                    Found(info.name, true);
                }
            }
        }

        private static void Found(string name, bool village)
        {
            if (places.ContainsKey(name)) return;

            Place p = new Place { name = name, village = village };
            p.mouths = village ? VillageMouths.Value : TownMouths.Value;

            // С чего начинают: немного сырья и неделя еды, будто её только что собрали.
            foreach (GoodsType t in Raw) p.store[t] = village ? 5f : 20f;
            p.Add(GoodsType.Produce, p.mouths * 4f);
            p.Add(GoodsType.Meat, p.mouths * 2f);
            places[name] = p;
            dirty = true;
        }

        private static WorldTownInfo Town(string name)
        {
            try { return WorldPlacesManager.instance != null ? WorldPlacesManager.instance.GetTown(name) : null; }
            catch { return null; }
        }

        // ------------------------------------------------------------------ день

        private static float next;

        internal static void Tick()
        {
            if (!On()) return;

            float now = Time.unscaledTime;
            if (now < next) return;
            next = now + 2f;

            if (WorldPlacesManager.instance == null || TimeManager.Instance == null) return;

            try
            {
                Sync();
                Survey();

                int today = Souls.Today();
                if (today < 0) return;
                if (day < 0) day = today;

                int days = Mathf.Clamp(today - day, 0, 30);
                for (int i = 0; i < days; i++) Day(day + i + 1);
                if (days > 0) { day = today; dirty = true; }

                Census.Tick();
                if ((bool)WorldTravelManager.instance) Sites.Survey();
                Pay(today);
                Morning(today);
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Хозяйство: день не прошёл: " + e.Message);
            }
        }

        private static void Day(int d)
        {
            System.Random dice = new System.Random(unchecked(d * 7919 + 17));

            foreach (Place p in places.Values)
            {
                WorldTownInfo info = Town(p.name);
                List<GoodsType> known = info != null ? info.specialties : null;
                List<GoodsType> lacks = info != null ? info.shortages : null;

                // Полный склад не пухнет: руки просто стоят без дела, ничего не выбрасывается.
                foreach (GoodsType t in Raw)
                {
                    int yield = (known != null && known.Contains(t)) ? Specialty.Value
                        : (lacks != null && lacks.Contains(t)) ? 0 : Common.Value;
                    if (p.village && (known == null || !known.Contains(t))) yield = Mathf.Min(yield, 1);
                    if (p.Has(t) < StoreCap.Value) p.Add(t, yield);
                }

                int week = d / 7;
                if (p.harvestWeek != week)
                {
                    p.harvestWeek = week;
                    p.harvest = 1f + (float)(new System.Random(unchecked(week * 104729 + p.name.GetHashCode())).NextDouble() * 2.0 - 1.0);
                }

                if (p.census != null) p.census.Yield(p);
                else if (p.village)
                {
                    float grown = Harvest.Value * p.harvest;
                    p.Add(GoodsType.Produce, grown * 0.6f);
                    p.Add(GoodsType.Meat, grown * 0.4f);
                }

                float spoiled = p.Spoil(d);
                p.Eat(p.mouths);

                // Раз в месяц всякий горожанин покупает себе что-то из одежды.
                if (!p.village) p.AddDemand(1, p.mouths / 30f);

                if (!p.village) Craft(p, dice);
            }

            Sites.Day(d);
            if (DemonLook.Trade.On()) DemonLook.Trade.Day(d, dice);
            else Trade();
            Bands.Day(d, dice);
            Prices();
        }

        /// <summary>A place gets more of a material, unless its store is full.</summary>
        internal static void Gain(Place p, GoodsType t, float n)
        {
            if (p != null && n > 0f && (Food(t) || p.Has(t) < StoreCap.Value)) p.Add(t, n);
        }

        /// <summary>Some ware of a kind, for loot: a material or a raw food.</summary>
        internal static UIItemInfo AnyWare(GoodsType t, System.Random dice)
        {
            List<UIItemInfo> list = Wares("*", null, t, Food(t));
            return list.Count > 0 ? list[dice.Next(list.Count)] : null;
        }

        // ------------------------------------------------------------------ торговцы

        private static readonly Dictionary<Place, List<Place>> roads = new Dictionary<Place, List<Place>>();
        private static int roadsFor = -1;

        internal static float Distance(string a, string b)
        {
            WorldPlacesManager wpm = WorldPlacesManager.instance;
            if (wpm == null) return 0f;

            float d = wpm.GetDistance(a, b);
            if (d > 0f) return d;
            string a2 = Alias(a), b2 = Alias(b);
            if (a2 != null) { d = wpm.GetDistance(a2, b); if (d > 0f) return d; }
            if (b2 != null) { d = wpm.GetDistance(a, b2); if (d > 0f) return d; }
            if (a2 != null && b2 != null) d = wpm.GetDistance(a2, b2);
            return d;
        }

        private static void Roads()
        {
            if (roadsFor == places.Count) return;
            roadsFor = places.Count;
            roads.Clear();

            List<Place> all = new List<Place>(places.Values);
            foreach (Place p in all)
            {
                List<KeyValuePair<float, Place>> near = new List<KeyValuePair<float, Place>>();
                foreach (Place o in all)
                {
                    if (ReferenceEquals(o, p)) continue;
                    float d = Distance(p.name, o.name);
                    if (d > 0f) near.Add(new KeyValuePair<float, Place>(d, o));
                }
                near.Sort((x, y) => x.Key.CompareTo(y.Key));

                List<Place> mine = new List<Place>();
                for (int i = 0; i < near.Count && i < Neighbours.Value; i++) mine.Add(near[i].Value);
                roads[p] = mine;
            }
        }

        /// <summary>How much of a good a place keeps for itself.</summary>
        private static float Keep(Place p, GoodsType t)
        {
            if (!Food(t)) return RawKeep.Value;
            float days = p.village ? 3f : 7f;
            float share = t == GoodsType.Produce ? 0.6f : 0.4f;
            return days * p.mouths * share;
        }

        /// <summary>The traders: whatever is left over goes down the road to whoever lacks it.</summary>
        private static void Trade()
        {
            if (Haul.Value <= 0) return;
            Roads();

            List<Place> order = new List<Place>(places.Values);
            order.Sort((x, y) => string.CompareOrdinal(x.name, y.name));

            List<GoodsType> goods = new List<GoodsType>(Raw) { GoodsType.Produce, GoodsType.Meat };

            foreach (Place from in order)
            {
                List<Place> near;
                if (!roads.TryGetValue(from, out near)) continue;

                foreach (Place to in near)
                {
                    foreach (GoodsType t in goods)
                    {
                        float spare = from.Has(t) - Keep(from, t);
                        float want = Keep(to, t) - to.Has(t);
                        float load = Mathf.Min(Haul.Value, Mathf.Min(spare, want));
                        if (load <= 0f) continue;

                        from.Add(t, -load);
                        to.Add(t, load);
                    }
                }
            }
        }

        /// <summary>A caravan reached a place: its load is unloaded there.</summary>
        internal static void Unload(TravelGroup group, WorldPlace place)
        {
            if (!On() || group == null || place == null || !group.isCaravan || group.leader == null) return;

            Sync();
            Survey();
            Place p = Get(place.name);
            ItemStock load = group.leader.items;
            if (p == null || load == null || load.items == null) return;

            int raw = 0, made = 0;
            foreach (Inventory inv in load.items)
            {
                if (inv == null || inv.itemInfo == null) continue;
                UIItemInfo info = inv.itemInfo;
                int n = Mathf.Max(1, inv.stackNum);

                if (Tracked(info.goodsType))
                {
                    p.Add(info.goodsType, n);
                    raw += n;
                    continue;
                }

                if (p.village) continue;

                int shop = -1;
                UIEquipmentInfo gear = info as UIEquipmentInfo;
                UIConsumableInfo drink = info as UIConsumableInfo;
                if (info is UIWeaponInfo) shop = 0;
                else if (info is UIArmorInfo) shop = 1;
                else if (gear != null && (gear.EquipType == EquipSlotType.neck || gear.EquipType == EquipSlotType.finger
                    || gear.EquipType == EquipSlotType.belt)) shop = 2;
                else if (drink != null && drink.consumableType == consumableType.potion) shop = 3;
                if (shop < 0) continue;

                List<int> q;
                if (!p.queue.TryGetValue(shop, out q)) p.queue[shop] = q = new List<int>();
                for (int c = 0; c < n && c < 5; c++) q.Add(info.ID);
                while (q.Count > 30) q.RemoveAt(0);
                made++;
            }

            if (raw + made > 0)
            {
                dirty = true;
                DemonLookPlugin.Log.LogInfo($"Хозяйство: караван разгрузился в «{p.name}»: сырья и еды {raw}, вещей {made}.");
            }
        }

        /// <summary>Food on hand in days: what it does to the price of food in a town.</summary>
        internal static float FoodDays(Place p)
        {
            return p == null ? 7f : p.FoodStock() / Mathf.Max(1, p.mouths);
        }

        private static void Prices()
        {
            foreach (Place p in places.Values)
            {
                if (p.village) continue;
                WorldTownInfo info = Town(p.name);
                if (info == null || info.goodsScarcity == null) continue;

                // Пять дней еды — цена как есть; пусто — втрое дороже; избыток — до сорока процентов дешевле.
                float s = Mathf.Clamp((5f - FoodDays(p)) * 0.4f, -0.4f, 2f);
                foreach (GoodsType t in new[] { GoodsType.Meat, GoodsType.Produce })
                {
                    if (info.goodsScarcity.ContainsKey(t)) info.goodsScarcity[t] = s;
                }
            }
        }

        // ------------------------------------------------------------------ ремесло

        private enum Kind { Weapon, Shield, Armour, Jewel, Potion }

        private static readonly Dictionary<Kind, List<CraftRecipe>> book = new Dictionary<Kind, List<CraftRecipe>>();
        private static bool booked;

        private static void Book()
        {
            if (booked) return;
            UIItemDatabase db;
            try { db = UIItemDatabase.Instance; }
            catch { return; }
            if (db == null || db.recipes == null || db.recipes.Length == 0) return;
            booked = true;

            foreach (Kind k in Enum.GetValues(typeof(Kind))) book[k] = new List<CraftRecipe>();

            foreach (CraftRecipe r in db.recipes)
            {
                if (r == null || r.product == null || r.product.isUnique) continue;
                if ((int)r.product.tier > (int)ItemTier.T4) continue;

                Kind k;
                UIWeaponInfo weapon = r.product as UIWeaponInfo;
                UIEquipmentInfo gear = r.product as UIEquipmentInfo;
                UIConsumableInfo drink = r.product as UIConsumableInfo;

                if (weapon != null) k = weapon.WeaponType == WeaponType.shield ? Kind.Shield : Kind.Weapon;
                else if (r.product is UIArmorInfo) k = Kind.Armour;
                else if (gear != null && (gear.EquipType == EquipSlotType.belt || gear.EquipType == EquipSlotType.neck
                    || gear.EquipType == EquipSlotType.finger)) k = Kind.Jewel;
                else if (drink != null && drink.consumableType == consumableType.potion) k = Kind.Potion;
                else continue;

                book[k].Add(r);
            }
        }

        private static readonly Dictionary<Kind, int> perDay = new Dictionary<Kind, int>
        {
            { Kind.Weapon, 1 }, { Kind.Armour, 1 }, { Kind.Shield, 1 }, { Kind.Potion, 3 }, { Kind.Jewel, 1 },
        };

        private static int ShopOf(Kind k)
        {
            switch (k)
            {
                case Kind.Weapon:
                case Kind.Shield: return 0;
                case Kind.Armour: return 1;
                case Kind.Jewel: return 2;
                default: return 3;
            }
        }

        private static bool Afford(Place p, CraftRecipe r)
        {
            Dictionary<GoodsType, float> need = Needs(r);
            foreach (KeyValuePair<GoodsType, float> n in need)
            {
                if (p.Has(n.Key) < n.Value) return false;
            }
            return true;
        }

        private static Dictionary<GoodsType, float> Needs(CraftRecipe r)
        {
            Dictionary<GoodsType, float> need = new Dictionary<GoodsType, float>();
            if (r.materials == null) return need;

            foreach (Inventory m in r.materials)
            {
                if (m == null || m.itemInfo == null) continue;
                GoodsType t = m.itemInfo.goodsType;
                if (!Tracked(t) || Food(t)) continue;

                float had;
                need.TryGetValue(t, out had);
                need[t] = had + Mathf.Max(1, m.stackNum);
            }
            return need;
        }

        private static void Craft(Place p, System.Random dice)
        {
            Book();
            if (!booked) return;

            foreach (KeyValuePair<Kind, int> plan in perDay)
            {
                List<CraftRecipe> all = book[plan.Key];
                if (all.Count == 0) continue;

                for (int n = 0; n < plan.Value; n++)
                {
                    // Чем проще вещь, тем чаще её делают: первый ярус вчетверо чаще третьего.
                    List<CraftRecipe> can = all.FindAll(r => Afford(p, r));
                    if (can.Count == 0) break;

                    int total = 0;
                    foreach (CraftRecipe r in can) total += Weight(r);
                    int roll = dice.Next(Mathf.Max(1, total));
                    CraftRecipe pick = can[0];
                    foreach (CraftRecipe r in can)
                    {
                        roll -= Weight(r);
                        if (roll < 0) { pick = r; break; }
                    }

                    foreach (KeyValuePair<GoodsType, float> need in Needs(pick)) p.Add(need.Key, -need.Value);

                    int shop = ShopOf(plan.Key);
                    List<int> q;
                    if (!p.queue.TryGetValue(shop, out q)) p.queue[shop] = q = new List<int>();
                    for (int c = 0; c < Mathf.Max(1, pick.productNum); c++) q.Add(pick.product.ID);
                    while (q.Count > 30) q.RemoveAt(0);
                }
            }
        }

        private static int Weight(CraftRecipe r)
        {
            int tier = Mathf.Clamp((int)r.product.tier, 0, 5);
            int w = Mathf.Max(1, 5 - tier);
            return w * w;
        }

        // ------------------------------------------------------------------ лавки

        private static int ShopIndex(Shop shop)
        {
            switch (shop.shopType)
            {
                case ShopType.weaponShop: return 0;
                case ShopType.armorShop: return 1;
                case ShopType.ornamentShop: return 2;
                case ShopType.alchemyShop: return 3;
                case ShopType.materialShop: return 4;
                case ShopType.foodShop:
                case ShopType.bakeryShop:
                case ShopType.fruitShop:
                case ShopType.meatShop:
                case ShopType.fishShop: return 5;
            }

            try
            {
                if (CityTownManager.instance != null && CityTownManager.instance.shops != null)
                {
                    int i = CityTownManager.instance.shops.IndexOf(shop);
                    if (i >= 0) return i;
                }
            }
            catch
            {
            }
            return -1;
        }

        /// <summary>The town a town shop belongs to.</summary>
        internal static Place PlaceOf(Shop shop)
        {
            if (shop == null) return null;

            try
            {
                WorldPlacesManager wpm = WorldPlacesManager.instance;
                if (wpm == null) return null;

                if (wpm.currentTown != null)
                {
                    Place here = Get(wpm.currentTown.name);
                    if (here != null && (shop.shopFaction == Faction.none || wpm.currentTown.faction == shop.shopFaction)) return here;
                }

                if (shop.shopFaction != Faction.none && wpm.worldTowns != null)
                {
                    foreach (WorldTownInfo t in wpm.worldTowns)
                    {
                        if (t != null && t.faction == shop.shopFaction && t.type == WorldPlaceType.city) return Get(t.name);
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        /// <summary>What the craftsmen made lands on the shelf when the shop opens.</summary>
        internal static void Deliver(Shop shop)
        {
            if (!On() || shop == null || shop.itemstock == null || !Market.TownShop(shop)) return;

            Sync();
            Place p = PlaceOf(shop);
            int index = ShopIndex(shop);
            List<int> q;
            if (p == null || index < 0 || !p.queue.TryGetValue(index, out q) || q.Count == 0) return;

            UIItemDatabase db = UIItemDatabase.Instance;
            int laid = 0;
            foreach (int id in q)
            {
                UIItemInfo info = db != null ? db.GetByID(id) : null;
                if (info == null) continue;

                Inventory made = new Inventory(info, 1);
                if (info is UIEquipmentInfo) EquipmentMaker.EnhanceEquipment(made);
                shop.itemstock.AddInventory(made);
                laid++;
            }
            q.Clear();
            dirty = true;

            if (laid > 0) DemonLookPlugin.Log.LogInfo($"Хозяйство: мастера «{p.name}» выложили {laid} вещей.");
        }

        // ------------------------------------------------------------------ полки со склада

        private static readonly Dictionary<string, List<UIItemInfo>> wares = new Dictionary<string, List<UIItemInfo>>();

        /// <summary>What a town sells of a kind: from its own goods lists, or failing that from the whole book.</summary>
        private static List<UIItemInfo> Wares(string town, ShopGoodsList list, GoodsType t, bool food)
        {
            string key = town + "/" + (int)t;
            List<UIItemInfo> got;
            if (wares.TryGetValue(key, out got)) return got;

            got = new List<UIItemInfo>();
            if (list != null)
            {
                foreach (List<GoodsSet> sets in new[] { list.goodsList, list.goodsList_Material, list.goodsList_Consumable })
                {
                    if (sets == null) continue;
                    foreach (GoodsSet s in sets)
                    {
                        if (s == null || s.item == null) continue;
                        foreach (UIItemInfo i in s.item)
                        {
                            if (i != null && i.goodsType == t && !i.isUnique && !got.Contains(i) && Fits(i, food)) got.Add(i);
                        }
                    }
                }
            }

            if (got.Count == 0)
            {
                try
                {
                    foreach (UIItemInfo i in UIItemDatabase.Instance.items)
                    {
                        if (i != null && i.goodsType == t && !i.isUnique && Fits(i, food) && (int)i.tier <= (int)ItemTier.T2) got.Add(i);
                    }
                }
                catch
                {
                }
            }

            wares[key] = got;
            return got;
        }

        private static bool Fits(UIItemInfo i, bool food)
        {
            if (food) return i.Name != null && i.Name.IndexOf("FoodIngredients_", StringComparison.Ordinal) >= 0;
            return i.itemType == ItemType.Material;
        }

        private static int OnShelf(Shop shop, Func<UIItemInfo, bool> kind)
        {
            int n = 0;
            foreach (Inventory inv in shop.itemstock.items)
            {
                if (inv != null && inv.itemInfo != null && kind(inv.itemInfo)) n += Mathf.Max(1, inv.stackNum);
            }
            return n;
        }

        private static void Lay(Shop shop, UIItemInfo info, int n)
        {
            if (info == null || n <= 0) return;
            if (info.stackable) shop.itemstock.AddInventory(new Inventory(info, n));
            else for (int i = 0; i < n; i++) shop.itemstock.AddInventory(new Inventory(info, 1));
        }

        private static UIItemInfo Pick(List<UIItemInfo> from, System.Random dice)
        {
            if (from == null || from.Count == 0) return null;
            return from[dice.Next(from.Count)];
        }

        /// <summary>The material and food stalls are filled from the town's stores, and from nowhere else.</summary>
        internal static void Supply(Shop shop)
        {
            if (!On() || shop == null || shop.itemstock == null || !Market.Closed(shop)) return;

            Sync();
            Place p = PlaceOf(shop);
            int index = ShopIndex(shop);
            if (p == null || (index != 4 && index != 5)) return;

            WorldTownInfo info = Town(p.name);
            System.Random dice = new System.Random(unchecked(Souls.Today() * 131 + index));
            int laid = 0;

            if (index == 4)
            {
                foreach (GoodsType t in Raw)
                {
                    int have = OnShelf(shop, i => i.goodsType == t);
                    int spare = Mathf.FloorToInt(p.Has(t) - RawKeep.Value);
                    int n = Mathf.Min(10 - have, spare);
                    if (n <= 0) continue;

                    UIItemInfo what = Pick(Wares(p.name, info != null ? info.meterialGoods : null, t, false), dice);
                    if (what == null) continue;
                    Lay(shop, what, n);
                    p.Add(t, -n);
                    laid += n;
                }
            }
            else
            {
                int have = OnShelf(shop, i => Food(i.goodsType));
                int spare = Mathf.FloorToInt(p.FoodStock() - 2f * p.mouths);
                int n = Mathf.Min(30 - have, spare);
                if (n > 0)
                {
                    float plants = p.FoodStock() > 0f ? p.Has(GoodsType.Produce) / p.FoodStock() : 0.5f;
                    // Мясная и рыбная лавка — мясо, овощная и пекарня — растительное, прочие — что есть.
                    if (shop.shopType == ShopType.meatShop || shop.shopType == ShopType.fishShop) plants = 0f;
                    else if (shop.shopType == ShopType.fruitShop || shop.shopType == ShopType.bakeryShop) plants = 1f;
                    int greens = Mathf.RoundToInt(n * plants);
                    foreach (KeyValuePair<GoodsType, int> part in new[]
                    {
                        new KeyValuePair<GoodsType, int>(GoodsType.Produce, greens),
                        new KeyValuePair<GoodsType, int>(GoodsType.Meat, n - greens),
                    })
                    {
                        List<UIItemInfo> list = Wares(p.name, info != null ? info.foodsGoods : null, part.Key, true);
                        for (int k = 0; k < part.Value; k++)
                        {
                            UIItemInfo what = Pick(list, dice);
                            if (what == null) break;
                            if (p.Take(part.Key, 1f) < 1f) break;
                            Lay(shop, what, 1);
                            laid++;
                        }
                    }
                }
            }

            if (laid > 0) { dirty = true; DemonLookPlugin.Log.LogInfo($"Хозяйство: со склада «{p.name}» на полку {laid}."); }
        }

        private static bool Wanted(int index, UIItemInfo i)
        {
            UIEquipmentInfo gear = i as UIEquipmentInfo;
            UIConsumableInfo drink = i as UIConsumableInfo;
            switch (index)
            {
                case 0: return i is UIWeaponInfo;
                case 1: return i is UIArmorInfo;
                case 2: return gear != null && (gear.EquipType == EquipSlotType.neck || gear.EquipType == EquipSlotType.finger || gear.EquipType == EquipSlotType.belt);
                case 3: return drink != null && drink.consumableType == consumableType.potion;
                case 5: return Food(i.goodsType);
            }
            return false;
        }

        /// <summary>
        /// What was bought while the hero was away is gone from the shelf, and its price is in the
        /// till: townsfolk for their clothes, heroes and mercenaries for their gear, food and potions.
        /// </summary>
        internal static void Buyers(Shop shop)
        {
            if (!On() || shop == null || shop.itemstock == null) return;

            Sync();
            Place p = PlaceOf(shop);
            if (p == null) return;

            if (shop is CitytownBarManager)
            {
                if (p.tavernCoin >= 1f)
                {
                    shop.itemstock.money += Mathf.FloorToInt(p.tavernCoin);
                    p.tavernCoin -= Mathf.Floor(p.tavernCoin);
                    dirty = true;
                }
                return;
            }

            int index = ShopIndex(shop);

            // Что отряды заплатили кузнецу и лекарю — в кассе оружейной лавки.
            if ((index == 0 || index == 1) && p.smithCoin >= 1f)
            {
                shop.itemstock.money += Mathf.FloorToInt(p.smithCoin);
                p.smithCoin -= Mathf.Floor(p.smithCoin);
                dirty = true;
            }

            float want;
            if (index < 0 || !p.demand.TryGetValue(index, out want) || want < 1f) return;

            int sold = 0;
            for (int k = 0; k < Mathf.FloorToInt(want); k++)
            {
                Inventory pick = null;
                foreach (Inventory inv in shop.itemstock.items)
                {
                    if (inv == null || inv.itemInfo == null || !Wanted(index, inv.itemInfo)) continue;
                    if (pick == null || inv.Value < pick.Value) pick = inv;
                }
                if (pick == null) break;

                shop.itemstock.money += Mathf.Max(1, pick.itemInfo.value);
                if (pick.itemInfo.stackable && pick.stackNum > 1) pick.stackNum--;
                else shop.itemstock.items.Remove(pick);
                sold++;
            }

            p.demand[index] = Mathf.Max(0f, want - Mathf.FloorToInt(want));
            dirty = true;
            if (sold > 0) DemonLookPlugin.Log.LogInfo($"Хозяйство: в «{p.name}» без вас раскупили {sold} вещей из лавки {index}.");
        }

        /// <summary>A hero band puts up in a town: it pays the inn, eats, and buys what it lacks.</summary>
        internal static void Lodge(TravelGroup group, WorldPlace place)
        {
            if (!On() || group == null || place == null || !group.isHeroAdventurer) return;

            Sync();
            Survey();
            Place p = Get(place.name);
            if (p == null || p.village) return;

            int men = 1 + (group.members != null ? group.members.Count : 0);
            p.AddDemand(0, 0.3f * men);
            p.AddDemand(1, 0.3f * men);
            p.AddDemand(3, 1f * men);
            p.AddDemand(5, 2f * men);
            p.tavernCoin += 40f * men;
            p.Eat(men);
            dirty = true;

            // На ночлеге отряд делится снаряжением.
            try { Growth.Share(group); } catch { }
        }

        /// <summary>What the hero sold to a town goes into its stores.</summary>
        internal static void Sold(UIItemInfo item, int count)
        {
            if (!On() || item == null || !Tracked(item.goodsType)) return;

            try
            {
                Shop shop = VenderManager.instance != null ? VenderManager.instance.shop : null;
                Place p = PlaceOf(shop);
                if (p == null) return;
                p.Add(item.goodsType, Mathf.Max(1, count));
                dirty = true;
            }
            catch
            {
            }
        }

        /// <summary>How full the food shop of a town is, by the food it has in store.</summary>
        internal static float FoodShelf(Shop shop)
        {
            if (!On() || shop == null || shop.shopType != ShopType.foodShop) return 1f;

            Sync();
            Place p = PlaceOf(shop);
            if (p == null) return 1f;

            float days = FoodDays(p);
            if (days < 2f) return 0.3f;
            if (days > 10f) return 1.5f;
            return 1f;
        }

        // ------------------------------------------------------------------ жалованье

        private static int[] wages;
        private static string wagesRead;

        private static int Wage(NPCSaveData npc)
        {
            if (wages == null || wagesRead != Wages.Value)
            {
                wagesRead = Wages.Value;
                wages = new[] { 50, 100, 150 };
                string[] parts = (Wages.Value ?? "").Split(',');
                for (int i = 0; i < wages.Length && i < parts.Length; i++)
                {
                    int v;
                    if (int.TryParse(parts[i].Trim(), out v) && v >= 0) wages[i] = v;
                }
            }

            switch (npc.career)
            {
                case CareerType.Guard: return wages[2];
                case CareerType.Merchant:
                case CareerType.Blacksmith:
                case CareerType.Doctor:
                case CareerType.Bartender: return wages[1];
                default: return wages[0];
            }
        }

        /// <summary>The townsfolk here get their wages and pay for their food, for each day since the last payday.</summary>
        private static void Pay(int today)
        {
            if ((bool)WorldTravelManager.instance) return;

            VillageManager village = VillageManager.instance;
            Place p = Census.Here();
            if (village == null || p == null) return;

            if (p.paid < 0) { p.paid = today; dirty = true; return; }
            int days = Mathf.Clamp(today - p.paid, 0, 14);
            if (days == 0) return;

            int folk = 0;
            bool fed = FoodDays(p) >= 0.5f;
            UIBuffInfo hungry = fed ? null : NeedCard(HungryId, "Hungry", "Голод",
                "Дома нечего есть: в городе не хватает еды. Слабость и тоска.", 24f);

            foreach (AIBehaviorController brain in UnityEngine.Object.FindObjectsOfType<AIBehaviorController>())
            {
                UnitAttribute u = brain != null ? brain.GetComponent<UnitAttribute>() : null;
                if (u == null || u.Data == null || u.Data.isdead || u.inParty || !(u is HumaniodUnit)) continue;
                if (u.Data.team != village.faction || u.items == null || Kids.Is(u)) continue;

                NPCSaveData npc = u.Data as NPCSaveData;
                if (npc == null || npc.heroCareer != null) continue;

                folk++;
                int net = Wage(npc) - (fed ? Meal.Value : 0);
                u.items.money = Mathf.Clamp(u.items.money + net * days, 0, 20000);

                // Нечего есть — голод и тоска: по десять морали за голодный день.
                if (hungry != null)
                {
                    Mark(u, hungry, 24f);
                    try { npc.AddMorale(-10f * days); } catch { }
                }
            }

            if (folk > 0) p.mouths = Mathf.Max(p.mouths, folk);
            p.paid = today;
            dirty = true;
        }

        private const string HungryId = "DemonLookHungry";
        internal const string TiredId = "DemonLookTired";

        /// <summary>The weariness a townsman or a traveller carries, of the same three levels as a hero's.</summary>
        internal static UIBuffInfo TiredCard()
        {
            return NeedCard(TiredId, "Fatigue", "Усталость", "Давно не спал: хуже меткость, ум и воля, быстрее выдыхается.", 12f);
        }

        /// <summary>
        /// The game keeps hunger and weariness for the party alone and wipes its own marks off
        /// everybody else each tick; these are the same marks under names of our own.
        /// </summary>
        internal static UIBuffInfo NeedCard(string id, string from, string name, string description, float hours)
        {
            UIBuffDatabase db;
            try { db = UIBuffDatabase.Instance; }
            catch { return null; }
            if (db == null || db.buffs == null) return null;

            UIBuffInfo card = db.GetByID(id);
            if (card == null)
            {
                UIBuffInfo pattern = db.GetByID(from) ?? db.GetByID("WeightDebuff");
                if (pattern == null) return null;
                card = UnityEngine.Object.Instantiate(pattern);
                card.name = id;
                card.id = id;
                List<UIBuffInfo> all = new List<UIBuffInfo>(db.buffs);
                all.Add(card);
                db.buffs = all.ToArray();
            }

            card.buffname = name;
            card.description = description;
            card.type = bufftype.negative;
            card.isVisible = true;
            card.durationIsHour = true;
            card.duration = hours;
            card.showDuration = true;
            return card;
        }

        internal static void Mark(UnitAttribute u, UIBuffInfo card, float hours, int level = 1)
        {
            try
            {
                if (u.buffmanger == null || card == null) return;
                if (u.buffmanger.ContainBuff(card.id)) u.buffmanger.RemoveBuff(card.id);
                BuffBase b = new BuffBase(card, u, hours, Mathf.Clamp(level, 1, 3));
                u.buffmanger.AddBuff(b);
            }
            catch
            {
            }
        }

        private static int tiredDay = -1;

        /// <summary>At eight in the morning: whoever did not sleep last night is weary, the heavier the more nights in a row.</summary>
        private static void Morning(int today)
        {
            if (tiredDay == today || (bool)WorldTravelManager.instance) return;

            int hour;
            try { hour = TimeManager.Hour; }
            catch { return; }
            if (hour < 8 || hour >= 12) return;
            tiredDay = today;

            VillageManager village = VillageManager.instance;
            if (village == null || !Sleep.On()) return;

            UIBuffInfo tired = TiredCard();
            if (tired == null) return;

            // Кто спал, а кто нет, видно, только если герой провёл эту ночь здесь; пришёл утром — не судить.
            int night = Crime.Night();
            if (!Sleep.Watched(night)) return;

            foreach (AIBehaviorController brain in UnityEngine.Object.FindObjectsOfType<AIBehaviorController>())
            {
                UnitAttribute u = brain != null ? brain.GetComponent<UnitAttribute>() : null;
                if (u == null || u.Data == null || u.Data.isdead || u.inParty || !(u is HumaniodUnit)) continue;
                if (u.Data.team != village.faction || Crime.Guard(u) || Kids.Is(u)) continue;

                NPCSaveData npc = u.Data as NPCSaveData;
                if (npc == null || npc.heroCareer != null) continue;

                // Ночь без сна — первая степень усталости, две подряд — вторая, три — третья.
                bool rested = Sleep.Slept(u, night);
                int missed = Weariness.On() ? Sleep.Count(u, rested) : (rested ? 0 : 1);
                if (!rested) Mark(u, tired, 12f * Mathf.Clamp(missed, 1, 2), Mathf.Clamp(missed, 1, 3));
            }
        }

        // ------------------------------------------------------------------ файл

        private static string loadedFor;
        private static bool dirty;
        private static float synced = -100f;
        private static System.Reflection.FieldInfo archiveField;

        private static string FilePath()
        {
            string archive = "";
            try
            {
                if (archiveField == null) archiveField = AccessTools.Field(typeof(SaveLoadManager), "currentArchive");
                archive = (archiveField != null && SaveLoadManager.Instance != null
                    ? archiveField.GetValue(SaveLoadManager.Instance) as string : null) ?? "";
            }
            catch
            {
            }

            foreach (char bad in Path.GetInvalidFileNameChars()) archive = archive.Replace(bad, '_');
            return Path.Combine(BepInEx.Paths.ConfigPath, "aor.economy." + archive + ".txt");
        }

        internal static void Sync()
        {
            float now = Time.unscaledTime;
            if (loadedFor != null && now - synced < 5f && now >= synced) return;
            synced = now;

            string path = FilePath();
            if (!string.Equals(path, loadedFor, StringComparison.OrdinalIgnoreCase))
            {
                Load(path);
                dirty = false;
            }
        }

        private static void Load(string path)
        {
            places.Clear();
            DemonLook.Trade.Clear();
            Sites.Clear();
            Bands.Clear();
            day = -1;
            loadedFor = path;

            try
            {
                if (!File.Exists(path)) return;

                foreach (string line in File.ReadAllLines(path))
                {
                    int split = line.IndexOf('=');
                    if (split <= 0) continue;

                    string key = line.Substring(0, split).Trim();
                    string[] v = line.Substring(split + 1).Split(';');

                    if (key == "day") { int.TryParse(v[0], out day); continue; }
                    if (DemonLook.Trade.Read(key, v)) continue;
                    if (Sites.Read(key, v)) continue;
                    if (Bands.Read(key, v)) continue;
                    if (key == "scene" && v.Length >= 2) { Census.scenes[v[0]] = v[1]; continue; }
                    if (v.Length < 2) continue;

                    Place p = Get(v[0]);
                    if (p == null && key == "place" && v.Length >= 5)
                    {
                        p = new Place { name = v[0], village = v[1] == "1" };
                        int.TryParse(v[2], out p.mouths);
                        int.TryParse(v[3], out p.paid);
                        int.TryParse(v[4], out p.harvestWeek);
                        if (v.Length >= 6) float.TryParse(v[5], NumberStyles.Float, CultureInfo.InvariantCulture, out p.harvest);
                        places[p.name] = p;
                        continue;
                    }
                    if (p == null) continue;

                    if (key == "store" && v.Length >= 3)
                    {
                        int t;
                        float amount;
                        if (int.TryParse(v[1], out t) && float.TryParse(v[2], NumberStyles.Float, CultureInfo.InvariantCulture, out amount))
                        {
                            if (Food((GoodsType)t)) p.food.Add(new Batch { t = (GoodsType)t, n = amount, day = day });
                            else p.store[(GoodsType)t] = amount;
                        }
                    }
                    else if (key == "food" && v.Length >= 4)
                    {
                        int t, when;
                        float amount;
                        if (int.TryParse(v[1], out t) && float.TryParse(v[2], NumberStyles.Float, CultureInfo.InvariantCulture, out amount)
                            && int.TryParse(v[3], out when))
                            p.food.Add(new Batch { t = (GoodsType)t, n = amount, day = when });
                    }
                    else if (key == "census" && v.Length >= 2)
                    {
                        p.census = Census.Parse(v[1]);
                    }
                    else if (key == "demand" && v.Length >= 3)
                    {
                        int shop;
                        float n;
                        if (int.TryParse(v[1], out shop) && float.TryParse(v[2], NumberStyles.Float, CultureInfo.InvariantCulture, out n))
                            p.demand[shop] = n;
                    }
                    else if (key == "tavern" && v.Length >= 2)
                    {
                        float.TryParse(v[1], NumberStyles.Float, CultureInfo.InvariantCulture, out p.tavernCoin);
                    }
                    else if (key == "smith" && v.Length >= 2)
                    {
                        float.TryParse(v[1], NumberStyles.Float, CultureInfo.InvariantCulture, out p.smithCoin);
                    }
                    else if (key == "queue" && v.Length >= 3)
                    {
                        int shop;
                        if (!int.TryParse(v[1], out shop)) continue;
                        List<int> q = new List<int>();
                        foreach (string id in v[2].Split(','))
                        {
                            int n;
                            if (int.TryParse(id, out n)) q.Add(n);
                        }
                        p.queue[shop] = q;
                    }
                }
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Хозяйство: не смог прочесть: " + e.Message);
            }
        }

        internal static void Flush()
        {
            if (!On()) return;
            Sync();
            if (!dirty) return;

            try
            {
                List<string> lines = new List<string> { "day=" + day };
                foreach (KeyValuePair<string, string> s in Census.scenes) lines.Add("scene=" + s.Key + ";" + s.Value);
                DemonLook.Trade.Write(lines);
                Sites.Write(lines);
                Bands.Write(lines);
                foreach (Place p in places.Values)
                {
                    lines.Add("place=" + p.name + ";" + (p.village ? "1" : "0") + ";" + p.mouths + ";" + p.paid + ";"
                        + p.harvestWeek + ";" + p.harvest.ToString("0.###", CultureInfo.InvariantCulture));
                    foreach (KeyValuePair<GoodsType, float> s in p.store)
                        lines.Add("store=" + p.name + ";" + (int)s.Key + ";" + s.Value.ToString("0.##", CultureInfo.InvariantCulture));
                    foreach (Batch b in p.food)
                        lines.Add("food=" + p.name + ";" + (int)b.t + ";" + b.n.ToString("0.##", CultureInfo.InvariantCulture) + ";" + b.day);
                    if (p.census != null) lines.Add("census=" + p.name + ";" + p.census.Write());
                    foreach (KeyValuePair<int, float> dm in p.demand)
                    {
                        if (dm.Value > 0.01f) lines.Add("demand=" + p.name + ";" + dm.Key + ";" + dm.Value.ToString("0.##", CultureInfo.InvariantCulture));
                    }
                    if (p.tavernCoin > 0.5f) lines.Add("tavern=" + p.name + ";" + p.tavernCoin.ToString("0", CultureInfo.InvariantCulture));
                    if (p.smithCoin > 0.5f) lines.Add("smith=" + p.name + ";" + p.smithCoin.ToString("0", CultureInfo.InvariantCulture));
                    foreach (KeyValuePair<int, List<int>> q in p.queue)
                    {
                        if (q.Value.Count > 0) lines.Add("queue=" + p.name + ";" + q.Key + ";" + string.Join(",", q.Value));
                    }
                }
                File.WriteAllLines(loadedFor ?? FilePath(), lines.ToArray());
                dirty = false;
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Хозяйство: не смог записать: " + e.Message);
            }
        }

        internal static void Reload()
        {
            if (!On()) return;
            synced = Time.unscaledTime;
            Load(FilePath());
            dirty = false;
        }

        internal static void Touch()
        {
            dirty = true;
        }
    }

    [HarmonyPatch(typeof(VenderManager), "StartShoping", new[] { typeof(Shop), typeof(bool) })]
    internal static class Deliver_Economy_Patch
    {
        private static void Prefix(Shop shop)
        {
            if (Shutters.Closed() && shop != null && shop.isCommonTownShop) return;
            try { Economy.Buyers(shop); } catch { }
            try { Economy.Deliver(shop); } catch { }
            try { Economy.Supply(shop); } catch { }
        }
    }

    // Караван дошёл — груз не пропадает, а ложится на склад и в лавки.
    [HarmonyPatch(typeof(WorldTravelManager), "GroupArrivePlace")]
    internal static class Arrive_Economy_Patch
    {
        private static void Prefix(TravelGroup group, WorldPlace place, TravelStyle travelStyle)
        {
            if (travelStyle == TravelStyle.supply)
            {
                try { Economy.Lodge(group, place); } catch { }
                return;
            }
            if (travelStyle != TravelStyle.enter) return;
            try { Economy.Unload(group, place); } catch { }
        }
    }

    [HarmonyPatch(typeof(VenderManager), "OnPlayerSellItem")]
    internal static class Sold_Economy_Patch
    {
        private static void Postfix(UIItemInfo item, int count)
        {
            try { Economy.Sold(item, count); } catch { }
        }
    }

    [HarmonyPatch(typeof(SaveLoadManager), "WriteDataToSaveFile")]
    internal static class WriteSave_Economy_Patch
    {
        private static void Postfix() { try { Economy.Flush(); } catch { } }
    }

    [HarmonyPatch(typeof(TroopManagement.ArenaMatchManager), "LoadInstance")]
    internal static class LoadInstance_Economy_Patch
    {
        private static void Postfix() { try { Economy.Reload(); } catch { } }
    }
}
