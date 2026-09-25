using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using spell;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Makes food go bad, and a cook worth feeding.
    ///
    /// Еда в этой игре не портится вовсе. Сваренный суп пролежит в телеге год и утолит голод
    /// так же, как в день, когда его сняли с огня, — поэтому запасы делаются раз и навсегда, а
    /// повар в отряде не нужен никому.
    ///
    /// И вот что выяснилось, прежде чем придумывать числа: **сроки в игре уже проставлены**.
    /// У каждой съедобной вещи заполнено поле прочности, и заполнено рукой человека — вяленая
    /// говядина восемьсот, копчёности двести, горячее блюдо сто, а вино и мёд минус единица,
    /// то есть не портятся. Поле при этом не делает ничего: у еды прочность не тратится.
    /// Значит придумывать шкалу на сотню с лишним блюд не нужно — она написана, её надо только
    /// завести.
    ///
    /// Авторская шкала сохраняется по порядку, но растягивается: у них вяленое живёт в восемь
    /// раз дольше горячего, а нужно в пятнадцать. Поэтому каждому их числу отвечает свой срок в
    /// часах, и обе крайние точки — двое суток и тридцать дней — стоят там, где заказаны.
    ///
    /// Повар делает два дела разом: у него запасы держатся дольше, и его еда полезнее. Второе —
    /// по лестнице, где сотня навыка стоит пятикратной пользы.
    /// </summary>
    internal static class Larder
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Shelf;
        internal static ConfigEntry<string> Ladder;
        internal static ConfigEntry<bool> Keeps;
        internal static ConfigEntry<bool> Enriches;
        internal static ConfigEntry<float> RottenFeed;
        internal static ConfigEntry<float> RottenHealth;
        internal static ConfigEntry<float> RottenMorale;
        internal static ConfigEntry<float> RawPlant;
        internal static ConfigEntry<float> RawMeat;
        internal static ConfigEntry<bool> Shops;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Larder", "Enabled", true,
                "Let food go bad. The game never spoils anything, so a pot of soup keeps for a "
                + "year in the wagon and nobody has ever needed a cook.");

            Shelf = config.Bind("Larder", "Shelf",
                "100=48,150=96,200=168,250=240,300=336,500=480,700=600,800=720",
                "How long each kind of food keeps, in game hours, written as «the number the "
                + "game wrote in its durability field = hours». Those numbers already exist and "
                + "already make sense — dried beef 800, smoked meat 200, a hot dish 100, wine "
                + "and mead -1 for never — so this only says what they mean in time. A hot dish "
                + "keeps two days and dried meat thirty, with the rest laid out between; the "
                + "game's own ordering is kept exactly.");

            Ladder = config.Bind("Larder", "Ladder", "10=1.5,20=2,50=3,100=5",
                "What the party's best cook is worth, as «skill=times». Read straight between "
                + "the named points, and no more than the last of them. Nothing below the first "
                + "is worth anything.");

            Keeps = config.Bind("Larder", "Keeps", true,
                "Let the cook's hand keep food longer by the same ladder — five times at a "
                + "hundred. A cook who salts and smokes properly is what a larder is.");

            Enriches = config.Bind("Larder", "Enriches", true,
                "And let his hand make food worth more: hunger, vigour, health and morale all "
                + "by the same ladder.");

            RottenFeed = config.Bind("Larder", "RottenFeed", 0.2f,
                new ConfigDescription(
                    "What share of its nourishment spoiled food still gives. A fifth: it is "
                    + "food, it goes down, and it barely counts.",
                    new AcceptableValueRange<float>(0f, 1f)));

            RottenHealth = config.Bind("Larder", "RottenHealth", 0.15f,
                new ConfigDescription(
                    "And what share of the eater's whole health it takes. Taken from the bar "
                    + "itself rather than from what the dish would have healed: the dish is not "
                    + "healing anybody now.",
                    new AcceptableValueRange<float>(0f, 1f)));

            RawPlant = config.Bind("Larder", "RawPlant", 168f,
                new ConfigDescription("Game hours raw fruit, vegetables, grain and bread keep: a week. "
                    + "The same for the party, the shops and everybody.",
                    new AcceptableValueRange<float>(1f, 10000f)));

            RawMeat = config.Bind("Larder", "RawMeat", 72f,
                new ConfigDescription("Game hours raw meat, fish and eggs keep: three days. Cooked, smoked and "
                    + "dried food keeps by the table above.",
                    new AcceptableValueRange<float>(1f, 10000f)));

            Shops = config.Bind("Larder", "Shops", true,
                "Let food go bad on the shop shelves too; what has gone off is thrown out when the shop opens.");

            RottenMorale = config.Bind("Larder", "RottenMorale", 30f,
                new ConfigDescription(
                    "And how much heart it takes out of him. Applied directly: the game gives "
                    + "morale only when a dish heals nothing, so a spoiled one would otherwise "
                    + "cost nothing at all.",
                    new AcceptableValueRange<float>(0f, 200f)));
        }

        // ------------------------------------------------------------------ что считается едой

        /// <summary>True for something that goes bad: it feeds, and it was given a shelf life.</summary>
        internal static bool Perishes(UIItemInfo thing)
        {
            UIConsumableInfo food = thing as UIConsumableInfo;

            if (food == null || food.hungryRestore <= 0f) return false;
            if (thing.noDurability || thing.durability <= 0f) return false;

            return Hours(thing) > 0f;
        }

        /// <summary>How long this kind of food keeps, in game hours.</summary>
        /// <summary>Whether the helping now going into a mouth has gone off.</summary>
        internal static bool Spoiled(UIItemInfo food)
        {
            // Порция та, что сейчас съедается. Если её не назвали — считаем свежей: лучше не
            // испортить обед, чем отравить наугад.
            if (!Enabled.Value || food == null || !Perishes(food)) return false;

            Inventory bit = Meal.Portion;

            return bit != null
                && (object)bit.itemInfo == (object)food
                && bit.durability <= 0f;
        }

        /// <summary>
        /// Raw food as it comes from the field, the pen and the hunt: fruit, vegetables, grain,
        /// meat, fish, eggs. Told by the game's own name for ingredients and by a short written
        /// shelf life — cheese, flour, ham and tea are ingredients too, but they were written to
        /// keep, and keep they do.
        /// </summary>
        internal static bool Raw(UIItemInfo thing)
        {
            if (thing == null || string.IsNullOrEmpty(thing.Name)) return false;
            if (thing.Name.IndexOf("FoodIngredients_", StringComparison.Ordinal) < 0) return false;
            return thing.durability > 0f && thing.durability <= 150f;
        }

        internal static float Hours(UIItemInfo thing)
        {
            if (Raw(thing))
                return thing.goodsType == GoodsType.Produce ? RawPlant.Value : RawMeat.Value;

            Dictionary<int, float> table = Table();
            if (table.Count == 0) return 0f;

            int written = Mathf.RoundToInt(thing.durability);

            float hours;
            if (table.TryGetValue(written, out hours)) return hours;

            // Числа, которого нет в таблице, в игре не встречалось — но настройки правятся
            // руками, а добавить блюдо может и другой мод. Считаем по тому же наклону, что и
            // у сотни: она в этом мире мера всего свежего.
            float basis;
            if (!table.TryGetValue(100, out basis) || basis <= 0f) return 0f;

            return written * (basis / 100f);
        }

        // ------------------------------------------------------------------ повар

        /// <summary>The best cook the party has.</summary>
        internal static int Cook()
        {
            int best = 0;

            try
            {
                PartyManager party = PartyManager.instance;
                if (party == null || party.partyMembers == null) return 0;

                foreach (HumaniodUnit one in party.partyMembers)
                {
                    if (one == null || one.Data == null) continue;
                    if (one.Data.cooking > best) best = one.Data.cooking;
                }
            }
            catch
            {
            }

            return best;
        }

        /// <summary>What a cook of that standing is worth, read off the ladder.</summary>
        internal static float Worth(int skill)
        {
            List<KeyValuePair<int, float>> steps = Steps();
            if (steps.Count == 0 || skill < steps[0].Key) return 1f;

            for (int i = 0; i < steps.Count; i++)
            {
                if (skill == steps[i].Key) return steps[i].Value;

                if (skill < steps[i].Key)
                {
                    // Между названными точками — ровно, без ступеней: повар сорока девяти не
                    // должен стоить столько же, сколько повар двадцати.
                    KeyValuePair<int, float> low = steps[i - 1];
                    KeyValuePair<int, float> high = steps[i];

                    float share = (float)(skill - low.Key) / (high.Key - low.Key);
                    return low.Value + (high.Value - low.Value) * share;
                }
            }

            return steps[steps.Count - 1].Value;
        }

        // ------------------------------------------------------------------ порча

        private static readonly Dictionary<string, double> seen = new Dictionary<string, double>();
        private static bool read;
        private static bool dirty;
        private static float next;

        private static string Ledger
        {
            get { return Path.Combine(BepInEx.Paths.ConfigPath, "aor.larder.tsv"); }
        }

        private static double Now()
        {
            return (TimeManager.Instance != null) ? TimeManager.Instance.currentTimeInHours : 0.0;
        }

        /// <summary>
        /// Lets time do to food what time does.
        ///
        /// Каждому месту хранения — свой счёт прошедших часов. Сумки и повозка всегда рядом,
        /// поэтому их час идёт непрерывно; склад чужого города не загружен, пока вас там нет,
        /// и его часы досчитываются при входе — тем же приёмом, каким догоняются заказы у
        /// кузнеца. Иначе запас в дальнем городе оставался бы свежим вечно.
        /// </summary>
        internal static void Tick()
        {
            if (!Enabled.Value) return;
            if (Time.unscaledTime < next) return;

            // Пока открыт инвентарь — не считаем. Запись новой прочности поднимает у игры
            // событие «вещь изменилась», ячейка перерисовывается, и открытая подсказка
            // закрывается вместе с ней. Раз в три секунды это выглядело как мигание описаний,
            // и это было оно.
            //
            // Пропуск ничего не портит: порча считается по часам игрового мира, а не по числу
            // заходов сюда. Что накопилось, пока вы разбирали сумку, будет учтено следующим же
            // разом, целиком.
            try
            {
                if (InventoryManager.instance != null && InventoryManager.instance.IsShow) return;
            }
            catch
            {
            }

            next = Time.unscaledTime + 15f;

            try
            {
                if (PartyManager.instance == null || PartyManager.instance.leader == null) return;

                Load();

                double now = Now();
                if (now <= 0.0) return;

                float slower = Keeps.Value ? Mathf.Max(0.1f, Worth(Cook())) : 1f;

                Age("party", now, slower, Bags());

                string town;
                ItemStock shelf = Workshop.Shelf(out town);

                if (shelf != null && !string.IsNullOrEmpty(town))
                {
                    Age("town/" + town, now, slower, new List<ItemStock> { shelf });
                }

                Save();
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог состарить припасы: " + e.Message);
            }
        }

        /// <summary>A shop's shelf ages from its last opening to this one; what went off is thrown out.</summary>
        internal static void ShopShelf(Shop shop)
        {
            if (!Enabled.Value || !Shops.Value || shop == null || shop.itemstock == null) return;

            try
            {
                Load();
                double now = Now();
                if (now <= 0.0) return;

                string key = shop.isCommonTownShop
                    ? "shop/" + shop.shopFaction + "/" + shop.shopType
                    : "shop/" + shop.id;
                Age(key, now, 1f, new List<ItemStock> { shop.itemstock });

                int thrown = 0;
                for (int i = shop.itemstock.items.Count - 1; i >= 0; i--)
                {
                    Inventory thing = shop.itemstock.items[i];
                    if (thing == null || thing.itemInfo == null || !Perishes(thing.itemInfo)) continue;
                    if (thing.durability > 0f) continue;
                    shop.itemstock.items.RemoveAt(i);
                    thrown++;
                }

                if (thrown > 0) ItemForgePlugin.Log.LogInfo($"Лавка выбросила испорченного: {thrown}.");
                Save();
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог состарить полку лавки: " + e.Message);
            }
        }

        private static List<ItemStock> Bags()
        {
            List<ItemStock> found = new List<ItemStock>();

            try
            {
                PartyManager party = PartyManager.instance;

                if (party != null && party.partyMembers != null)
                {
                    foreach (HumaniodUnit one in party.partyMembers)
                    {
                        if (one != null && one.items != null) found.Add(one.items);
                    }
                }

                if (InventoryManager.instance != null && InventoryManager.instance.inventories != null)
                {
                    found.Add(InventoryManager.instance.inventories);
                }

                if (CaravanUIManager.Instance != null && CaravanUIManager.Instance.caravanStock != null)
                {
                    found.Add(CaravanUIManager.Instance.caravanStock);
                }
            }
            catch
            {
            }

            return found;
        }

        private static void Age(string place, double now, float slower, List<ItemStock> stocks)
        {
            double was;

            if (!seen.TryGetValue(place, out was))
            {
                // Первое знакомство: ничего не портим задним числом — неизвестно, сколько эта
                // еда там пролежала до того, как за ней стали следить.
                seen[place] = now;
                dirty = true;
                return;
            }

            double hours = now - was;
            if (hours <= 0.0) return;

            seen[place] = now;
            dirty = true;

            int spoiled = 0;

            foreach (ItemStock stock in stocks)
            {
                if (stock == null || stock.items == null) continue;

                foreach (Inventory thing in stock.items)
                {
                    if (thing == null || thing.itemInfo == null) continue;
                    if (!Perishes(thing.itemInfo)) continue;
                    if (thing.durability <= 0f) continue;

                    float life = Hours(thing.itemInfo) * slower;
                    if (life <= 0f) continue;

                    float lost = (float)(hours * thing.itemInfo.durability / life);
                    if (lost <= 0f) continue;

                    float left = Mathf.Max(0f, thing.durability - lost);
                    thing.SetDurability(left);

                    if (left <= 0f) spoiled++;
                }
            }

            if (spoiled > 0)
            {
                ItemForgePlugin.Log.LogInfo($"Испортилось припасов: {spoiled} ({place}).");
            }
        }

        private static void Load()
        {
            if (read) return;
            read = true;

            try
            {
                if (!File.Exists(Ledger)) return;

                foreach (string line in File.ReadAllLines(Ledger, Encoding.UTF8))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length != 2) continue;

                    double when;
                    if (!double.TryParse(parts[1], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out when)) continue;

                    seen[parts[0]] = when;
                }
            }
            catch
            {
            }
        }

        private static void Save()
        {
            if (!dirty) return;
            dirty = false;

            try
            {
                StringBuilder text = new StringBuilder();

                foreach (KeyValuePair<string, double> one in seen)
                {
                    text.AppendLine(one.Key + "\t"
                        + one.Value.ToString("R", CultureInfo.InvariantCulture));
                }

                File.WriteAllText(Ledger, text.ToString(), Encoding.UTF8);
            }
            catch
            {
            }
        }

        // ------------------------------------------------------------------ разбор настроек

        private static Dictionary<int, float> table;
        private static string tableRead;

        private static Dictionary<int, float> Table()
        {
            string written = Shelf.Value ?? "";
            if (table != null && written == tableRead) return table;

            Dictionary<int, float> got = new Dictionary<int, float>();

            foreach (string one in written.Split(','))
            {
                string[] halves = one.Split('=');
                if (halves.Length != 2) continue;

                int key;
                float hours;

                if (!int.TryParse(halves[0].Trim(), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out key)) continue;
                if (!float.TryParse(halves[1].Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out hours)) continue;

                got[key] = Mathf.Max(0f, hours);
            }

            tableRead = written;
            table = got;
            return table;
        }

        private static List<KeyValuePair<int, float>> steps;
        private static string stepRead;

        private static List<KeyValuePair<int, float>> Steps()
        {
            string written = Ladder.Value ?? "";
            if (steps != null && written == stepRead) return steps;

            List<KeyValuePair<int, float>> got = new List<KeyValuePair<int, float>>();

            foreach (string one in written.Split(','))
            {
                string[] halves = one.Split('=');
                if (halves.Length != 2) continue;

                int skill;
                float much;

                if (!int.TryParse(halves[0].Trim(), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out skill)) continue;
                if (!float.TryParse(halves[1].Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out much)) continue;

                got.Add(new KeyValuePair<int, float>(skill, much));
            }

            got.Sort(delegate (KeyValuePair<int, float> a, KeyValuePair<int, float> b)
            {
                return a.Key.CompareTo(b.Key);
            });

            stepRead = written;
            steps = got;
            return steps;
        }
    }

    // Съедаемая порция известна только здесь: дальше игра передаёт одну карточку блюда, а
    // свежесть у каждой порции своя. Запоминаем на время глотка.
    [HarmonyPatch(typeof(ItemSlotSelector), "ConsumeInventoryObject")]
    internal static class Consume_Larder_Patch
    {
        private static void Prefix(Inventory inv)
        {
            Meal.Portion = inv;
        }
    }

    /// <summary>What is being eaten right now, and what the cook's hand does to it.</summary>
    internal static class Meal
    {
        internal static Inventory Portion;

        // Что подменено в карточке блюда на время еды, и что там было.
        private static UIConsumableInfo dish;
        private static float hungry, vigor, health, morale;
        private static UIBuffInfo blessing;
        private static bool rotten;
        private static bool stowed;
        private static float stow;

        internal static void Serve(SpellCaster caster)
        {
            Clear();

            if (!Larder.Enabled.Value || caster == null) return;

            try
            {
                UIConsumableInfo food = caster.item as UIConsumableInfo;
                if (food == null || food.hungryRestore <= 0f) return;

                bool bad = Larder.Spoiled(food);

                float much = Larder.Enriches.Value ? Larder.Worth(Larder.Cook()) : 1f;

                // Лечение здоровья от еды больше не мгновенное: оно ложится в запас и капает
                // оттуда по часу. Значит вмешиваться надо и тогда, когда повар ничего не
                // прибавил, — иначе миска супа снова вылечит всё разом.
                bool away = Mending.Enabled.Value && Mending.FromFood.Value
                    && food.healthRestore > 0f;

                if (!bad && !away && much <= 1.0001f) return;

                dish = food;
                hungry = food.hungryRestore;
                vigor = food.vigorRestore;
                health = food.healthRestore;
                morale = food.moraleRestore;
                blessing = food.buff;
                rotten = bad;

                if (bad)
                {
                    // Съедается и травит: сытость почти не идёт, лечения нет вовсе, и благодати
                    // блюда тоже — а урон и упавшее сердце добавим сами, после.
                    food.hungryRestore = hungry * Larder.RottenFeed.Value;
                    food.vigorRestore = 0f;
                    food.healthRestore = 0f;
                    food.moraleRestore = 0f;
                    food.buff = null;
                }
                else
                {
                    food.hungryRestore = hungry * much;
                    food.vigorRestore = vigor * much;
                    food.healthRestore = away ? 0f : health * much;
                    food.moraleRestore = morale * much;

                    stowed = away;
                    stow = away ? health * much : 0f;
                }
            }
            catch
            {
                Restore();
            }
        }

        /// <summary>Puts the dish back as it was, and makes the spoiled one tell.</summary>
        internal static void Eaten(SpellCaster caster)
        {
            bool bad = rotten;
            bool away = stowed;
            float much = stow;
            UIConsumableInfo what = dish;

            Restore();

            if (caster == null) return;

            try
            {
                UnitAttribute who = caster.targetUnit != null ? caster.targetUnit : caster.user;

                // Отложенное считается и тогда, когда всё прочее в порядке: это не наказание,
                // а другой ход времени.
                if (away && much > 0f) Mending.Store(who, what, much);

                if (!bad) return;

                HumaniodUnit man = who as HumaniodUnit;
                if (man == null || man.Data == null) return;

                // Здоровье отнимается от самой шкалы, а не от того, что блюдо могло бы вылечить:
                // лечить тут нечему. Мораль — прямо, потому что игра отдаёт её только блюдам,
                // которые ничего не лечат, и испорченное иначе не стоило бы ничего.
                int hurt = Mathf.Max(1, Mathf.RoundToInt(man.maxhp * Larder.RottenHealth.Value));

                man.Data.AddHealth(-hurt);
                man.Data.AddMorale(-Mathf.RoundToInt(Larder.RottenMorale.Value));

                ItemForgePlugin.Log.LogInfo($"«{man.Data.unitname}» съел испорченное: "
                    + $"здоровья −{hurt}, морали −{Larder.RottenMorale.Value:0}.");

                GameController.ShowMessage("Испорченная еда", 2f);
            }
            catch
            {
            }
        }

        private static void Restore()
        {
            if (dish != null)
            {
                dish.hungryRestore = hungry;
                dish.vigorRestore = vigor;
                dish.healthRestore = health;
                dish.moraleRestore = morale;
                dish.buff = blessing;
            }

            Clear();
        }

        private static void Clear()
        {
            dish = null;
            blessing = null;
            rotten = false;
            stowed = false;
            stow = 0f;
        }
    }

    // Подмена живёт ровно один глоток: карточка блюда общая на все порции, и оставить её
    // правленой значило бы испортить всю еду этого вида навсегда.
    [HarmonyPatch(typeof(BehavUseItem), "ExecuteBehav")]
    internal static class UseItem_Larder_Patch
    {
        private static void Prefix(BehavUseItem __instance)
        {
            try { Meal.Serve(__instance == null ? null : __instance.caster); }
            catch { }
        }

        private static void Postfix(BehavUseItem __instance)
        {
            try { Meal.Eaten(__instance == null ? null : __instance.caster); }
            catch { }
        }
    }

    // Лавка открывается — её полка стареет за то время, что её никто не видел.
    [HarmonyPatch(typeof(VenderManager), "StartShoping", new[] { typeof(Shop), typeof(bool) })]
    internal static class Shop_Larder_Patch
    {
        [HarmonyPriority(Priority.High)]
        private static void Prefix(Shop shop)
        {
            try { Larder.ShopShelf(shop); } catch { }
        }
    }
}
