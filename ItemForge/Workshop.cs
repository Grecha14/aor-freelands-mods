using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Work that takes time: a thing left with a smith, and the hours he needs for it.
    ///
    /// В игре ремонт мгновенен. Платишь — и вещь целая, сколько бы от неё ни осталось. Кузнец
    /// при этом не получает ни опыта, ни времени: его ремесло ни на что не влияет, потому что
    /// влиять негде.
    ///
    /// Здесь работа занимает часы. Вещь ложится на городской склад и чинится там: за игровой
    /// час кузнец возвращает ей столько прочности, сколько позволяет его уровень. Забрать её
    /// можно когда угодно — недочиненной.
    ///
    /// Про хранение. В сохранение игры мод не пишет, поэтому список заказов живёт своим
    /// файлом, а найти его надо так, чтобы находился он у того же мира, даже если игра
    /// переименовала или скопировала слот. Якорем взят сам персонаж: его номер и имя при
    /// копировании не меняются. Но якорь только ищет список — верить списку нельзя: каждая
    /// запись обязана найти свою вещь на складе названного города, с прочностью в ожидаемых
    /// пределах. Не нашлась — запись вычёркивается молча. Так заказ не может ни починить
    /// чужое, ни приписать работу, которой не было.
    ///
    /// И про догон. Часы идут и когда вас в городе нет, а склад чужого города в это время не
    /// загружен. Поэтому прочность не начисляется по тику, а досчитывается при встрече: вошли
    /// в город — заказ добирает всё, что причиталось ему за прошедшее время.
    /// </summary>
    internal static class Workshop
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> PerLevel;
        internal static ConfigEntry<float> Floor;
        internal static ConfigEntry<float> Toll;

        private static readonly List<Order> orders = new List<Order>();
        private static bool read;
        private static string anchor;

        internal sealed class Order
        {
            internal string town;
            internal int item;
            internal float was;      // прочность на момент приёма — и примета, и счёт к оплате
            internal float rate;     // прочности за игровой час
            internal double placed;  // час мира, когда заказ принят
            internal double seen;    // час мира, до которого уже досчитано
            internal bool told;      // о готовности уже сказано
        }

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Workshop", "Enabled", true,
                "Let repairs take time. The game hands a whole thing back the moment the money "
                + "moves, which is why nobody has ever wondered how good their smith is.");

            PerLevel = config.Bind("Workshop", "PerLevel", 1f,
                new ConfigDescription(
                    "Points of durability a smith restores in a game hour, per level of his "
                    + "craft. One means a first-level smith mends a point an hour and a "
                    + "fifteenth-level one fifteen — so a ruined legendary blade is days of "
                    + "work, not minutes.",
                    new AcceptableValueRange<float>(0.05f, 100f)));

            Toll = config.Bind("Workshop", "Toll", 1f,
                new ConfigDescription(
                    "What a full repair costs against what the thing is worth. One means "
                    + "bringing a ruined thing back to whole costs its market price, and a "
                    + "quarter of its durability costs a quarter of it. Lower this if smiths "
                    + "should be cheaper than merchants.",
                    new AcceptableValueRange<float>(0.05f, 5f)));

            Floor = config.Bind("Workshop", "Floor", 10f,
                new ConfigDescription(
                    "The craft to credit a smith with when the game gives no figure for him — "
                    + "which is every time the town is visited from the world map, where the "
                    + "smith is not a person in a scene but a line in a menu. Ten is what the "
                    + "game itself gives a town doctor. Nobody works slower than this.",
                    new AcceptableValueRange<float>(0.1f, 50f)));
        }

        /// <summary>The name this world answers to, and the same one after any copy.</summary>
        private static string Anchor()
        {
            if (anchor != null) return anchor;

            try
            {
                HumaniodUnit who = (PartyManager.instance != null)
                    ? PartyManager.instance.leader : null;

                if (who == null || who.Data == null) return null;

                anchor = who.Data.id + "/" + (who.Data.unitName ?? "");
            }
            catch
            {
                return null;
            }

            return anchor;
        }

        private static string File()
        {
            return Path.Combine(BepInEx.Paths.ConfigPath, "aor.orders.tsv");
        }

        /// <summary>Reads the ledger once, keeping only this world's rows.</summary>
        private static void Read()
        {
            if (read) return;

            string mine = Anchor();
            if (mine == null) return;

            read = true;

            try
            {
                if (!System.IO.File.Exists(File())) return;

                foreach (string line in System.IO.File.ReadAllLines(File(), Encoding.UTF8))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length < 7) continue;
                    if (parts[0] != mine) continue;

                    Order one = new Order();
                    one.town = parts[1];

                    if (!int.TryParse(parts[2], out one.item)) continue;
                    if (!Num(parts[3], out one.was)) continue;
                    if (!Num(parts[4], out one.rate)) continue;

                    double placed, seen;
                    if (!double.TryParse(parts[5], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out placed)) continue;
                    if (!double.TryParse(parts[6], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out seen)) continue;

                    one.placed = placed;
                    one.seen = seen;

                    orders.Add(one);
                }

                if (orders.Count > 0)
                {
                    ItemForgePlugin.Log.LogInfo($"Заказов у кузнецов: {orders.Count}.");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог прочесть список заказов: " + e.Message);
            }
        }

        private static bool Num(string what, out float got)
        {
            return float.TryParse(what, NumberStyles.Float, CultureInfo.InvariantCulture, out got);
        }

        /// <summary>Writes the ledger back, keeping other worlds' rows untouched.</summary>
        private static void Save()
        {
            string mine = Anchor();
            if (mine == null) return;

            try
            {
                List<string> rows = new List<string>();

                // Чужие миры переписывать нельзя: файл один на всех, и у соседа там свои
                // заказы. Сохраняем их как есть и дописываем свои.
                if (System.IO.File.Exists(File()))
                {
                    foreach (string line in System.IO.File.ReadAllLines(File(), Encoding.UTF8))
                    {
                        if (!line.StartsWith(mine + "\t", StringComparison.Ordinal))
                        {
                            if (line.Length > 0) rows.Add(line);
                        }
                    }
                }

                foreach (Order one in orders)
                {
                    rows.Add(string.Join("\t", new string[]
                    {
                        mine,
                        one.town,
                        one.item.ToString(CultureInfo.InvariantCulture),
                        one.was.ToString("0.###", CultureInfo.InvariantCulture),
                        one.rate.ToString("0.###", CultureInfo.InvariantCulture),
                        one.placed.ToString("0.####", CultureInfo.InvariantCulture),
                        one.seen.ToString("0.####", CultureInfo.InvariantCulture)
                    }));
                }

                System.IO.File.WriteAllLines(File(), rows.ToArray(), Encoding.UTF8);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог записать список заказов: " + e.Message);
            }
        }

        /// <summary>The hour the world is at, counted from its beginning.</summary>
        private static double Now()
        {
            return (TimeManager.Instance != null) ? TimeManager.Instance.currentTimeInHours : 0.0;
        }

        /// <summary>How fast this smith works, in durability an hour.</summary>
        internal static float Speed(int level)
        {
            float lv = Mathf.Max(Floor.Value, level);

            // Очко прочности в час на каждую ступень ремесла. Кузнец пятнадцатого уровня
            // возвращает пятнадцать в час — полные легендарные латы у него займут четверо
            // суток, и это та цена времени, ради которой ремонт вообще стал занимать время.
            return lv * PerLevel.Value;
        }

        /// <summary>
        /// The craft of whoever stands behind this counter.
        ///
        /// Кузнец у панели ремонта лежит в закрытом поле, и добраться до него можно только
        /// отражением. Это не изящно, но честнее выдумывания: уровень берётся у того самого
        /// человека, к которому вы пришли, а не назначается числом из настроек.
        /// </summary>
        internal static UnitAttribute SmithUnit(RepairManager panel)
        {
            try
            {
                return AccessTools.Field(typeof(RepairManager), "blacksmith")
                    ?.GetValue(panel) as UnitAttribute;
            }
            catch
            {
                return null;
            }
        }

        internal static int Smith(RepairManager panel)
        {
            try
            {
                UnitAttribute who = SmithUnit(panel);

                NPCSaveData mind = (who != null) ? who.Data as NPCSaveData : null;

                if (mind != null && mind.humanTalent != null)
                {
                    return Mathf.Max(1, mind.humanTalent.BSSmithing);
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог спросить кузнеца о его уровне: "
                    + e.Message);
            }

            return Mathf.Max(1, Mathf.RoundToInt(Floor.Value));
        }

        /// <summary>
        /// The town's storeroom, and the name it answers to.
        ///
        /// Сперва я искал «CityTownManager» — объект города в сцене. Это работает, только
        /// пока город загружен, то есть когда вы внутри него ходите. А из меню поселения на
        /// карте мира сцены города нет вовсе, и кузнец отвечал «город не опознан», хотя склад
        /// рядом и открывается.
        ///
        /// У игры для этого есть своё: «ResolveTownWarehouseStock» отдаёт склад загруженного
        /// города, а если такого нет — находит город по карте мира и берёт его кладовую.
        /// Берём то же самое, и вопрос закрывается обоими способами сразу.
        /// </summary>
        internal static ItemStock Shelf(out string town)
        {
            town = null;

            try
            {
                if (CityTownManager.instance != null
                    && CityTownManager.instance.warehouseStock != null)
                {
                    town = CityTownManager.instance.name;
                    return CityTownManager.instance.warehouseStock;
                }

                if (WorldPlacesManager.instance != null
                    && WorldPlacesManager.instance.currentTown != null)
                {
                    town = WorldPlacesManager.instance.currentTown.name;
                }

                ItemStock shelf = WarehouseManager.ResolveTownWarehouseStock();

                if (shelf == null || string.IsNullOrEmpty(town))
                {
                    town = null;
                    return null;
                }

                return shelf;
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог найти городской склад: " + e.Message);

                town = null;
                return null;
            }
        }

        // Поднимается, когда вещь ушла на склад: тогда переждать часы уже не надо.
        internal static bool took;

        /// <summary>Strikes a slot out of the panel's own two lists.</summary>
        internal static void Forget(RepairManager panel, UIItemSlot slot)
        {
            if (panel == null || slot == null) return;

            foreach (string name in new string[] { "slots_equip", "slots_inventory" })
            {
                try
                {
                    List<UIItemSlot> list = AccessTools.Field(typeof(RepairManager), name)
                        ?.GetValue(panel) as List<UIItemSlot>;

                    if (list != null) list.Remove(slot);
                }
                catch (Exception e)
                {
                    ItemForgePlugin.Log.LogWarning($"Не смог вычеркнуть ячейку из {name}: "
                        + e.Message);
                }
            }
        }

        // Город спрашивается не на каждую ячейку, а раз в полсекунды: ячеек на складе
        // полсотни, и каждая перерисовка гоняла бы поиск склада заново.
        private static string lastTown;
        private static float asked;

        private static string Where()
        {
            float now = Time.realtimeSinceStartup;

            if (now - asked < 0.5f) return lastTown;

            asked = now;

            string town;
            Shelf(out town);

            lastTown = town;
            return town;
        }

        /// <summary>True when this very thing is lying with a smith right now.</summary>
        internal static bool InWork(Inventory thing)
        {
            if (!Enabled.Value || thing == null || thing.itemInfo == null) return false;

            string town = Where();
            if (string.IsNullOrEmpty(town)) return false;

            Read();

            foreach (Order one in orders)
            {
                if (one.town != town || one.item != thing.itemInfo.ID) continue;
                if (thing.durability < one.was - 0.01f) continue;

                return true;
            }

            return false;
        }

        /// <summary>
        /// What to say about a thing lying with a smith, if anything.
        ///
        /// Вещь на складе чинилась молча: лежит как всякая другая, прочность тихо ползёт
        /// вверх, и узнать об этом можно было только из журнала. Теперь подсказка говорит
        /// сама — сколько осталось работы и во что она обойдётся при выдаче.
        /// </summary>
        internal static string Word(Inventory thing)
        {
            if (!Enabled.Value || thing == null || thing.itemInfo == null) return null;

            try
            {
                string town;
                if (Shelf(out town) == null) return null;

                Read();

                foreach (Order one in orders)
                {
                    if (one.town != town || one.item != thing.itemInfo.ID) continue;
                    if (thing.durability < one.was - 0.01f) continue;

                    float whole = thing.itemInfo.durability;
                    float left = whole - thing.durability;
                    int due = Cost(thing, one);

                    // Коротко: места в строке прочности мало, и длинная фраза обрезалась на
                    // середине — «у кузнеца, ещё» и всё. Здесь важны два числа, а слова можно
                    // и опустить.
                    if (left <= 0.01f)
                    {
                        return "<color=#5FA83C>готово</color> · " + due;
                    }

                    float hours = left / Mathf.Max(0.01f, one.rate);

                    string when = (hours >= 1f)
                        ? hours.ToString("0") + " ч"
                        : (hours * 60f).ToString("0") + " мин";

                    return "<color=#E2B35D>" + when + "</color> · " + due;
                }
            }
            catch
            {
            }

            return null;
        }

        /// <summary>Says why nothing was taken in, and leaves the thing as it was.</summary>
        internal static void Refuse(string reason)
        {
            ItemForgePlugin.Log.LogInfo("Кузнец не взял вещь в работу: " + reason + ".");
            GameController.ShowMessage(reason);
        }

        private static string excuse;

        /// <summary>Says once why a repair went the old, instant way.</summary>
        internal static void Why(string reason)
        {
            if (reason == excuse) return;

            excuse = reason;
            ItemForgePlugin.Log.LogInfo("Ремонт прошёл по-старому, мгновенно: " + reason + ".");
        }

        /// <summary>
        /// Takes a thing away from whoever holds it, wherever that is.
        ///
        /// Вещь в окне ремонта приходит либо со слота — тогда её надо снять, — либо из
        /// сумки, тогда вынуть. Третьего не дано, и если не нашлось ни того, ни другого,
        /// значит мы чего-то не понимаем про эту вещь и трогать её нельзя.
        /// </summary>
        private static bool Detach(Inventory thing)
        {
            if (thing == null) return false;

            try
            {
                if (PartyManager.instance == null || PartyManager.instance.partyMembers == null)
                {
                    return false;
                }

                foreach (HumaniodUnit one in PartyManager.instance.partyMembers)
                {
                    if (one == null) continue;

                    if (one.equipmentmanger != null)
                    {
                        if (Strip(one.equipmentmanger.equipInfos, thing)) return true;
                        if (Strip(one.equipmentmanger.standByWeaponInfos, thing)) return true;
                    }

                    if (Pull(one.items, thing)) return true;
                }

                // Из меню поселения отряда как существ в сцене может не быть вовсе, а вещь
                // всё равно откуда-то взялась: из общей сумки отряда или из повозки. Спросим
                // и у них, прежде чем сдаваться.
                if (InventoryManager.instance != null
                    && Pull(InventoryManager.instance.inventories, thing))
                {
                    return true;
                }

                if (CaravanUIManager.Instance != null
                    && Pull(CaravanUIManager.Instance.caravanStock, thing))
                {
                    return true;
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог забрать вещь у владельца: " + e.Message);
            }

            return false;
        }

        /// <summary>Takes a thing out of one stock, if it is lying there.</summary>
        private static bool Pull(ItemStock stock, Inventory thing)
        {
            if (stock == null || stock.items == null) return false;
            if (!stock.items.Contains(thing)) return false;

            stock.RemoveInventory(thing, 0);
            return true;
        }

        private static bool Strip(EquipInfo[] slots, Inventory thing)
        {
            if (slots == null) return false;

            foreach (EquipInfo slot in slots)
            {
                if (slot == null || !slot.IsEquiped()) continue;
                if (!ReferenceEquals(slot.inventory, thing)) continue;

                // Владельца найдём через сам слот: у EquipmentManager он свой.
                HumaniodUnit who = null;

                foreach (HumaniodUnit one in PartyManager.instance.partyMembers)
                {
                    if (one != null && one.equipmentmanger != null
                        && (Array.IndexOf(one.equipmentmanger.equipInfos, slot) >= 0
                            || Array.IndexOf(one.equipmentmanger.standByWeaponInfos, slot) >= 0))
                    {
                        who = one;
                        break;
                    }
                }

                if (who == null) return false;

                who.equipmentmanger.UnequipItem(slot);
                return true;
            }

            return false;
        }

        /// <summary>The best hand at the forge the party has.</summary>
        internal static UnitAttribute Handy()
        {
            UnitAttribute best = null;
            int most = -1;

            try
            {
                if (PartyManager.instance == null || PartyManager.instance.partyMembers == null)
                {
                    return null;
                }

                foreach (HumaniodUnit one in PartyManager.instance.partyMembers)
                {
                    if (one == null || one.Data == null || one.Data.isdead) continue;

                    int level = Craftsman((UnitAttribute)(object)one);
                    if (level <= most) continue;

                    most = level;
                    best = (UnitAttribute)(object)one;
                }
            }
            catch
            {
            }

            return best;
        }

        /// <summary>What this one knows of the forge.</summary>
        internal static int Craftsman(UnitAttribute who)
        {
            try
            {
                NPCSaveData mind = (who != null) ? who.Data as NPCSaveData : null;

                if (mind != null && mind.humanTalent != null)
                {
                    return Mathf.Max(1, mind.humanTalent.BSSmithing);
                }
            }
            catch
            {
            }

            return Mathf.Max(1, Mathf.RoundToInt(Floor.Value));
        }

        // Недоработанные часы. Перемотка умеет только целые, а работа целыми не бывает.
        private static float owed;

        /// <summary>
        /// Winds the world forward by the hours the work took.
        ///
        /// Сначала я округлял вверх до целого часа, и починка на семь секунд съедала час —
        /// а на соседней вещи выходило то же самое, отчего казалось, что время идёт как
        /// попало. Теперь целые часы перематываются, а остаток запоминается и ждёт
        /// следующей работы: десять мелких починок в сумме всё равно станут часом, а одна
        /// мелкая не станет.
        /// </summary>
        internal static void Skip(float hours)
        {
            if (TimeManager.Instance == null || hours <= 0f) return;

            owed += hours;

            int whole = Mathf.FloorToInt(owed);
            if (whole <= 0) return;

            owed -= whole;

            // Тем же способом, каким игра пережидает ковку у верстака: экран гаснет, часы
            // уходят вперёд, и мир вокруг живёт эти часы как обычно.
            TimeManager.Instance.StartCoroutine(
                TimeManager.Instance.SkipTime(whole, true));
        }

        /// <summary>Takes a thing in for work, and says how long it will be.</summary>
        internal static bool Take(Inventory thing, string town, ItemStock shelf, int level)
        {
            if (!Enabled.Value || thing == null || thing.itemInfo == null) return false;
            if (string.IsNullOrEmpty(town) || shelf == null) return false;

            Read();

            float whole = thing.itemInfo.durability;
            float missing = whole - thing.durability;
            if (missing <= 0.01f) return false;

            Order one = new Order();
            one.town = town;
            one.item = thing.itemInfo.ID;
            one.was = thing.durability;
            one.rate = Speed(level);
            one.placed = Now();
            one.seen = one.placed;

            // Сперва запись, потом всё остальное. Если что-то оборвётся между двумя
            // действиями, пусть это будет заказ без вещи, а не вещь без заказа.
            // Сперва отнять у прежнего владельца, потом отдать складу. У вещи одно место
            // жительства: иначе она числится и надетой, и лежащей в кладовой, и при обыске
            // получается два предмета из одного.
            if (!Detach(thing))
            {
                ItemForgePlugin.Log.LogWarning($"«{thing.itemInfo.Name}»: не нашёл, у кого "
                    + "она была — в работу не беру, иначе задвоится.");

                orders.Remove(one);
                return false;
            }

            orders.Add(one);
            Save();

            shelf.AddInventoryNoEvent(thing);

            float hours = missing / Mathf.Max(0.01f, one.rate);

            ItemForgePlugin.Log.LogInfo($"«{thing.itemInfo.Name}» принята в ремонт "
                + $"в «{town}»: {thing.durability:0.#} из {whole:0.#}, "
                + $"кузнец {level}-го уровня, сроку {hours:0.#} ч.");

            return true;
        }

        /// <summary>
        /// Brings every order in this town up to the hour it is now.
        ///
        /// Вызывается при встрече, а не по тику: склад чужого города не загружен, и начислять
        /// там нечего. Зато при входе видно сразу всё, что натекло за время отсутствия.
        /// </summary>
        internal static void Catch(string town, ItemStock shelf)
        {
            if (!Enabled.Value || string.IsNullOrEmpty(town)) return;

            Read();
            if (orders.Count == 0) return;

            if (shelf == null || shelf.items == null) return;

            double now = Now();
            bool moved = false;

            for (int i = orders.Count - 1; i >= 0; i--)
            {
                Order one = orders[i];
                if (one.town != town) continue;

                Inventory thing = Find(shelf, one);

                // Сверка: вещи нет или она не та — заказ вычёркиваем. Забрали со склада,
                // продали, загрузились в другой мир — всё это сюда.
                if (thing == null)
                {
                    orders.RemoveAt(i);
                    moved = true;
                    continue;
                }

                double hours = now - one.seen;
                if (hours <= 0.0) continue;

                float whole = thing.itemInfo.durability;
                float add = (float)(hours * one.rate);

                thing.SetDurability(Mathf.Min(whole, thing.durability + add));

                one.seen = now;
                moved = true;

                // Готовую вещь заказ не отпускает: плата берётся при выдаче, а взять её
                // можно только с того, кто помнит, сколько было сделано. Вычеркнем, когда
                // вещь заберут, и не раньше.
                if (thing.durability >= whole - 0.01f && !one.told)
                {
                    one.told = true;

                    ItemForgePlugin.Log.LogInfo($"«{thing.itemInfo.Name}» починена "
                        + $"и ждёт вас на складе в «{town}»: к оплате "
                        + $"{Cost(thing, one)} при выдаче.");

                    GameController.ShowMessage(thing.itemInfo.Name);
                }
            }

            if (moved) Save();
        }

        /// <summary>
        /// What the work done on this thing comes to.
        ///
        /// Игра считала ремонт по недостающей прочности и ярусу: кожаный ремень T4 и
        /// легендарные латы T4 стоили чинить одинаково, потому что ярус у них один. Но
        /// починка стоит столько, сколько стоит вещь, — а не сколько стоит её ярус.
        ///
        /// Здесь цена берётся от рыночной стоимости целой вещи и доли восстановленного.
        /// Вернули четверть прочности — заплатили четверть цены. Из этого само собой
        /// выходит правило, которого в игре не было: разбитое в хлам дешевле бросить.
        /// </summary>
        internal static int Cost(Inventory thing, Order one)
        {
            if (thing == null || thing.itemInfo == null) return 0;

            float whole = thing.itemInfo.durability;
            if (whole <= 0f) return 0;

            float done = thing.durability - one.was;
            if (done <= 0f) return 0;

            // Цена вещи как она есть в каталоге, без торговой наценки за качество. Игровой
            // расчёт стоимости домножает её на два в степени качества — у легендарной выходит
            // шестнадцатикратно, — но это цена купли-продажи, а не работы. Кузнец берёт за
            // труд над этой вещью, а не за то, сколько она стоит у барышника.
            //
            // Считаем по прочности: сколько стоит одно её очко, столько и берём за каждое
            // восстановленное.
            float perPoint = thing.itemInfo.value / whole;

            return Mathf.CeilToInt(perPoint * done * Toll.Value);
        }

        /// <summary>
        /// Settles up when a thing is taken off the shelf.
        ///
        /// Платит человек за сделанное, а не за обещанное: забрал недочиненное — заплатил за
        /// то, что успели. Поэтому счёт и хранится в самом заказе одной приметой: прочность
        /// на момент приёма. Всё, что выше неё, — работа кузнеца.
        /// </summary>
        internal static bool Withdraw(Inventory thing, string town)
        {
            if (!Enabled.Value || thing == null || thing.itemInfo == null) return true;
            if (string.IsNullOrEmpty(town)) return true;

            Read();

            for (int i = 0; i < orders.Count; i++)
            {
                Order one = orders[i];
                if (one.town != town || one.item != thing.itemInfo.ID) continue;
                if (thing.durability < one.was - 0.01f) continue;

                int due = Cost(thing, one);

                if (due > 0)
                {
                    // Не хватило — берём сколько есть, остаток прощаем. Отказать в выдаче
                    // было бы честнее по счёту, но вещь уже вынута из ячейки, и попытка
                    // вернуть её назад — это способ её потерять. Долг кузнеца дешевле.
                    int paid = Mathf.Min(due, Mathf.Max(0, ManagementModeCore.Money));

                    if (paid > 0) ManagementModeCore.TakeMoney(0 - paid);

                    ItemForgePlugin.Log.LogInfo($"За «{thing.itemInfo.Name}» уплачено {paid}"
                        + ((paid < due) ? $" из {due}, остальное прощено" : "")
                        + $": восстановлено {thing.durability - one.was:0.#} прочности.");

                    GameController.ShowMessage($"{thing.itemInfo.Name}: {paid}");
                }

                orders.RemoveAt(i);
                Save();

                return true;
            }

            return true;
        }

        /// <summary>The thing this order speaks of, if it is still lying where it was left.</summary>
        private static Inventory Find(ItemStock shelf, Order one)
        {
            float whole = -1f;

            foreach (Inventory thing in shelf.items)
            {
                if (thing == null || thing.itemInfo == null) continue;
                if (thing.itemInfo.ID != one.item) continue;

                whole = thing.itemInfo.durability;

                // Примета: прочность не меньше той, что была при приёме, и не больше целой.
                // Чинить её мог только наш кузнец, а он лишнего не добавит.
                if (thing.durability >= one.was - 0.01f && thing.durability <= whole + 0.01f)
                {
                    return thing;
                }
            }

            return null;
        }
    }

    // Встреча с городом: досчитываем всё, что натекло по его заказам, пока нас не было.
    [HarmonyPatch(typeof(WarehouseManager), "LoadInventories")]
    internal static class LoadInventories_Catch_Patch
    {
        private static void Prefix()
        {
            try
            {
                string town;
                ItemStock shelf = Workshop.Shelf(out town);

                if (shelf != null) Workshop.Catch(town, shelf);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог досчитать заказы: " + e.Message);
            }
        }
    }

    // Вещь ушла со склада — значит работа кончена, сколько бы её ни сделали. Здесь и
    // берётся плата: за восстановленное, а не за обещанное.
    [HarmonyPatch(typeof(WarehouseManager), "OnUnassign")]
    internal static class OnUnassign_Pay_Patch
    {
        private static void Postfix(Inventory inventory)
        {
            try
            {
                string town;
                Workshop.Shelf(out town);

                if (town != null) Workshop.Withdraw(inventory, town);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог взять плату за ремонт: " + e.Message);
            }
        }
    }

    // «Взять всё» уносит вещи мимо выдачи: оно перекладывает их в сумку само и очищает
    // список, не трогая OnUnassign. То есть починенное ушло бы даром. Берём плату здесь же,
    // до того как склад опустеет.
    [HarmonyPatch(typeof(WarehouseManager), "OnTakeAll")]
    internal static class OnTakeAll_Pay_Patch
    {
        private static void Prefix(WarehouseManager __instance)
        {
            try
            {
                string town;
                ItemStock shelf = Workshop.Shelf(out town);

                if (shelf == null || shelf.items == null) return;

                Workshop.Catch(town, shelf);

                foreach (Inventory thing in new List<Inventory>(shelf.items))
                {
                    Workshop.Withdraw(thing, town);
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог рассчитаться за «взять всё»: "
                    + e.Message);
            }
        }
    }

    // Ремонт ремкомплектом в поле. Кузни нет и склада нет, но работа есть, и делает её тот
    // из отряда, кто лучше всех в кузнечном деле. Часы не ждут — их перематывают, как игра
    // перематывает ковку: экран гаснет, время уходит вперёд, вещь готова.
    [HarmonyPatch(typeof(RepairManager), "RepairItem")]
    internal static class RepairItem_Field_Patch
    {
        // Ячейку родной метод уничтожает сам: на успехе он делает «slot.Unassign()» и
        // сносит объект. Значит после него у слота нет ни вещи, ни её прочности, и смотреть
        // туда бесполезно — ровно на этом постфикс и умолкал, а ремонт оставался мгновенным.
        // Поэтому и вещь, и её прежнюю прочность держим у себя, а не в слоте.
        private sealed class Before
        {
            internal Inventory thing;
            internal float was;
        }

        private static void Prefix(RepairManager __instance, UIItemSlot slot,
            out Before __state)
        {
            __state = null;
            Workshop.took = false;

            if (!Workshop.Enabled.Value || __instance == null) return;
            if (slot == null || slot.inventory == null || slot.inventory.itemInfo == null) return;

            __state = new Before { thing = slot.inventory, was = slot.inventory.durability };
        }

        private static void Postfix(bool __result, Before __state)
        {
            if (!Workshop.Enabled.Value || !__result) return;
            if (__state == null || Workshop.took) return;
            if (__state.thing == null || __state.thing.itemInfo == null) return;

            try
            {
                float done = __state.thing.durability - __state.was;
                if (done <= 0.01f) return;

                UnitAttribute hands = Workshop.Handy();
                int level = Workshop.Craftsman(hands);

                // У наковальни работа идёт быстрее, чем на колене в поле, и считается по
                // родной формуле игры: столько за уровень повозки плюс столько за ремесло.
                bool forge = Camp.AtForge;
                float rate = forge ? Camp.Speed(hands) : Workshop.Speed(level);

                float hours = done / Mathf.Max(0.01f, rate);

                Craft.Paid(hands, hours, __state.thing.itemInfo.tier);

                ItemForgePlugin.Log.LogInfo($"«{__state.thing.itemInfo.Name}» починена "
                    + (forge
                        ? $"в кузне каравана: {done:0.#} прочности, повозка {Camp.Forge()}-го "
                          + $"уровня, кузнец {level}-го, "
                        : $"в поле: {done:0.#} прочности, кузнец {level}-го уровня, ")
                    + (hours >= 1f ? $"{hours:0.#} ч работы." : $"{hours * 60f:0.#} мин работы."));

                Workshop.Skip(hours);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог отсчитать время полевого ремонта: "
                    + e.Message);
            }
        }
    }

    // Ремонт в лавке перестаёт быть мгновенным. Вместо того чтобы вернуть целую вещь за
    // деньги, кузнец забирает её в работу: она ложится на городской склад и чинится там по
    // часам. Плата — при выдаче, и потому здесь не берётся ничего.
    [HarmonyPatch(typeof(RepairManager), "RepairItem")]
    internal static class RepairItem_Order_Patch
    {
        private static bool Prefix(RepairManager __instance, UIItemSlot slot, ref bool __result)
        {
            if (!Workshop.Enabled.Value) return true;
            if (__instance == null) return true;

            // Одна строка на каждый отказ: без неё «в лавке починилось мгновенно» — это
            // загадка, а с ней — ответ.
            if (!__instance.isInShop)
            {
                Workshop.Why("это не лавка, а ремкомплект");
                return true;
            }

            // Кузня каравана — не лавка: заказ там оставить негде, склада в дороге нет.
            // Работа делается на месте, а время за неё снимет полевой постфикс.
            if (Camp.AtForge)
            {
                Workshop.Why("это кузня каравана — чиним на месте, со временем");
                return true;
            }

            if (slot == null || slot.inventory == null || slot.inventory.itemInfo == null)
            {
                return true;
            }

            string town;
            ItemStock shelf = Workshop.Shelf(out town);

            if (shelf == null)
            {
                // Склада нет — работать негде. Чинить мгновенно взамен нельзя: это ровно та
                // дыра, ради закрытия которой всё и делалось.
                Workshop.Refuse("здесь негде оставить вещь в работу");
                __result = false;
                return false;
            }

            try
            {
                Inventory thing = slot.inventory;

                int level = Workshop.Smith(__instance);
                float missing = thing.itemInfo.durability - thing.durability;

                if (!Workshop.Take(thing, town, shelf, level))
                {
                    Workshop.Refuse("вещь не удалось взять в работу");
                    __result = false;
                    return false;
                }

                // Кузнец получает за часы, которые на эту работу уйдут. Начисляем при
                // приёме, а не при выдаче: к тому времени его самого рядом может не быть —
                // город другой, сцена не загружена, спросить не у кого.
                Craft.Paid(Workshop.SmithUnit(__instance),
                    missing / Mathf.Max(0.01f, Workshop.Speed(level)),
                    thing.itemInfo.tier);

                // Вещь уехала на склад — убираем её из окна ремонта тем же порядком, каким
                // это делает сама игра, когда работа закончена. Вычеркнуть из обоих списков
                // обязательно: иначе в них останется ссылка на уничтоженный объект, и
                // «починить всё» споткнётся об неё на следующем нажатии.
                Workshop.Forget(__instance, slot);

                slot.Unassign();
                UnityEngine.Object.Destroy(slot.gameObject);

                Workshop.took = true;
                __result = true;
                return false;
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог принять вещь в ремонт: " + e);
                return true;
            }
        }
    }
    // Подсказка предмета: если вещь в работе, она должна об этом сказать. Дописываем к той
    // же строке прочности, где оберег показывает свой запас, — другого места у подсказки нет.
    [HarmonyPatch(typeof(UIItemTip), "SetValue")]
    internal static class ItemTip_Work_Patch
    {
        private static void Postfix(UIItemTip __instance, UISlotBase slot)
        {
            if (!Workshop.Enabled.Value) return;

            try
            {
                UnityEngine.UI.Text line = __instance.durabilityText;
                if (line == null || !line.gameObject.activeSelf) return;

                Inventory shown = null;

                // По ссылке, а не оператором Unity: ячейка второй панели сравнения создана
                // через «new» и родным сравнением читается как пустая.
                if ((object)slot != null)
                {
                    System.Reflection.FieldInfo held = AccessTools
                        .Field(slot.GetType(), "inventory");

                    if (held != null) shown = held.GetValue(slot) as Inventory;
                }

                string word = Workshop.Word(shown);
                if (word == null) return;

                // Поле прочности узкое и режет всё, что не влезло. Разрешаем вылезать за
                // край: строка короткая, соседей у неё справа нет.
                line.horizontalOverflow = UnityEngine.HorizontalWrapMode.Overflow;

                line.text = line.text + "  <size=11>" + word + "</size>";
            }
            catch
            {
            }
        }
    }
    // Ячейка вещи, лежащей у кузнеца, красится красной подложкой — той самой, что игра
    // держит у каждой ячейки для своих нужд. Так на складе сразу видно, что своё, а что в
    // работе, не наводя мышь на каждую.
    [HarmonyPatch(typeof(UIItemSlot), "SetSlot", new Type[] { typeof(Inventory) })]
    internal static class SetSlot_Work_Patch
    {
        private static void Postfix(UIItemSlot __instance, Inventory inv)
        {
            if (!Workshop.Enabled.Value || __instance == null) return;
            if (__instance.redBG == null) return;

            try
            {
                __instance.redBG.gameObject.SetActive(Workshop.InWork(inv));
            }
            catch
            {
            }
        }
    }
}
