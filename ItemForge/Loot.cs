using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Puts the set into the world instead of into the player's hands.
    ///
    /// The game hands out loot in UnitAttribute.Die: a creature's template carries a list of
    /// trophies, each with a drop rate, and a common multiplier of one and a half on top.
    /// Anything marked unique skips the roll entirely — which is the mechanism behind the four
    /// pieces that are meant to be carried rather than hoped for.
    ///
    /// Trophy lists live on the UnitInfo asset, shared by every creature of that kind, so they
    /// are left alone here: editing one would change it for the whole session and for every
    /// copy of that creature at once. The items are added to the corpse instead, at the moment
    /// it becomes lootable, which reaches the same place by a route that cannot leak.
    ///
    /// What counts as a legendary chance is measured rather than invented: the rate is the
    /// average of every legendary the game ships, read out of its own item database on first
    /// use and written to the log.
    /// </summary>
    internal static class Loot
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> ApexDrops;
        internal static ConfigEntry<float> ChestRate;
        internal static ConfigEntry<float> ChestRange;
        internal static ConfigEntry<string> Found;
        internal static ConfigEntry<float> RandomRate;
        internal static ConfigEntry<bool> ReportArea;
        internal static ConfigEntry<bool> Scavenging;
        internal static ConfigEntry<string> Spare;
        internal static ConfigEntry<bool> DropWorn;
        internal static ConfigEntry<bool> TrueDeath;
        internal static ConfigEntry<bool> NoLastStand;


        // Кого уже раздели. Список, а не поле на юните: чужой класс не наш, чтобы в нём
        // заводить свои отметки.
        private static readonly HashSet<UnitAttribute> bared = new HashSet<UnitAttribute>();

        // Средний шанс легендарки по базе игры. Считается один раз: база не меняется.
        private static float legendary = -1f;

        private static readonly HashSet<int> looted = new HashSet<int>();

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Loot", "Enabled", true,
                "Let the set be found in the world.");

            ApexDrops = config.Bind("Loot", "ApexDrops", "",
                "Which apex creature carries which piece, written as UnitInfo asset name = item "
                + "name, separated by semicolons. Empty, and nobody carries anything: the set is "
                + "found rather than taken, and the only way to it is through chests. Left here "
                + "because the machinery still works — fill it in and those creatures go back to "
                + "carrying what they are given.");

            ChestRate = config.Bind("Loot", "ChestRate", 1f,
                new ConfigDescription(
                    "Chance for each piece of the set to be lying in any one chest, in per "
                    + "cent. One per cent is a named piece every hundred chests and the whole "
                    + "set in some two hundred — and since every piece exists once, that is the "
                    + "whole of it: found, it is gone from the world and will not turn up again.",
                    new AcceptableValueRange<float>(0f, 100f)));

            Found = config.Bind("Loot", "Found", "",
                "Which pieces have already been found, by name, separated by semicolons. Each "
                + "piece of the set exists once: this is where that once is written down, so it "
                + "survives a night and a restart. Clear it by hand to put the set back into the "
                + "world.");

            ChestRange = config.Bind("Loot", "ChestRange", 30f,
                new ConfigDescription(
                    "How far from a named boss a chest may stand and still count as his, in "
                    + "metres. A quest boss guards a room, and what he guards belongs in the "
                    + "chest of that room rather than in his pockets. Nothing within reach, "
                    + "and the piece goes on the body as before.",
                    new AcceptableValueRange<float>(0f, 200f)));

            RandomRate = config.Bind("Loot", "RandomRate", 0f,
                new ConfigDescription(
                    "Chance for each of the remaining pieces, in per cent before the game's own "
                    + "multiplier. Zero means use the measured average of the game's own "
                    + "legendaries, which is what «the same chance as other legendaries» asks "
                    + "for — and it adjusts itself if the game is ever rebalanced.",
                    new AcceptableValueRange<float>(0f, 100f)));

            Scavenging = config.Bind("Loot", "Scavenging", true,
                "Let whoever made the kill strip the body and wear what is better than his own. "
                + "The game equips its fighters by their level rather than from what they find, "
                + "so a bandit who kills a knight goes on wearing rags while the plate lies in "
                + "the grass. This closes that: gear that changes hands stays in the world, and "
                + "a dangerous-looking enemy is dangerous because of what he took from someone.");

            Spare = config.Bind("Loot", "Spare", "DarkMage_T3",
                "Creatures that never strip the fallen, by their UnitInfo asset name, "
                + "separated by commas. Five summoners standing in a circle are not there to "
                + "loot: the scene ends the moment the fight does, and a mage who has just "
                + "picked three amulets and a shield off the dead walks out of it carrying "
                + "sixty kilos he was never written to carry.");

            DropWorn = config.Bind("Loot", "DropWorn", true,
                "Let what a body was wearing be taken off it. The game hands out loot from a "
                + "creature's trophy list and nothing else, so the plate and the sword you just "
                + "fought through stay on the corpse and cannot be picked up — which reads as a "
                + "bug long before it reads as a rule. This moves the worn gear into the body, "
                + "where the game's own looting can reach it.");

            TrueDeath = config.Bind("Loot", "TrueDeath", true,
                "Let a killed enemy stay dead. The game decides death by a roll and mostly "
                + "knocks people out instead: they lie there with a «Knockout» buff, get up "
                + "eight seconds later, and their body holds nothing — which is why a fight "
                + "could be won and leave nothing to take. Party members and quest characters "
                + "keep the game's own rule.");

            NoLastStand = config.Bind("Loot", "NoLastStand", true,
                "Take the being-downed mechanic out of the game entirely. Dealt more damage "
                + "than a man has left, and he is dead — the player's own companions included, "
                + "and whoever the story still needed. The game normally rolls for it and "
                + "mostly lets people get back up after eight seconds; with this on, nobody "
                + "does, ever.");

            ReportArea = config.Bind("Loot", "ReportArea", true,
                "Write down which area an apex creature was found in. There is no table in the "
                + "game that answers this — where a creature stands is placed by hand in the "
                + "scene — so the only honest way to learn it is to be told when one dies.");
        }

        /// <summary>The average drop rate of everything the game calls legendary.</summary>
        private static float LegendaryRate()
        {
            if (legendary >= 0f) return legendary;

            legendary = 0f;

            try
            {
                UIItemDatabase db = UIItemDatabase.Instance;
                if (db == null || db.items == null) return legendary;

                float total = 0f;
                int count = 0;
                float low = float.MaxValue;
                float high = 0f;

                foreach (UIItemInfo item in db.items)
                {
                    if (item == null || item.Quality != UIItemQuality.Legendary) continue;
                    if (item.isUnique) continue;   // уникальные падают без броска, шанс у них ни при чём

                    total += item.dropRate;
                    count++;
                    if (item.dropRate < low) low = item.dropRate;
                    if (item.dropRate > high) high = item.dropRate;
                }

                if (count == 0)
                {
                    ItemForgePlugin.Log.LogWarning("Легендарок в базе не нашлось, шанс остаётся нулевым.");
                    return legendary;
                }

                legendary = total / count;

                ItemForgePlugin.Log.LogInfo($"Легендарок в базе: {count}. Шанс выпадения: средний "
                    + $"{legendary:0.##}%, от {low:0.##}% до {high:0.##}%. С общим множителем игры "
                    + $"({UnitAttribute.TROPHY_DROP_RATE}) это {legendary * UnitAttribute.TROPHY_DROP_RATE:0.##}% "
                    + "с каждого подходящего трупа.");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог измерить шанс легендарки: " + e);
            }

            return legendary;
        }

        private static UIEquipmentInfo ByName(string name)
        {
            foreach (UIEquipmentInfo piece in Forge.Built)
            {
                if (piece != null && string.Equals(piece.Name, name, StringComparison.Ordinal)) return piece;
            }
            return null;
        }

        /// <summary>The piece this creature is meant to be carrying, if any.</summary>
        private static UIEquipmentInfo Carried(string asset)
        {
            string table = (ApexDrops.Value ?? "").Trim();
            if (table.Length == 0 || string.IsNullOrEmpty(asset)) return null;

            foreach (string row in table.Split(';'))
            {
                int split = row.IndexOf('=');
                if (split <= 0) continue;

                string who = row.Substring(0, split).Trim();
                if (!string.Equals(who, asset, StringComparison.OrdinalIgnoreCase)) continue;

                return ByName(row.Substring(split + 1).Trim());
            }
            return null;
        }

        /// <summary>
        /// Decides whether this corpse is one legendaries would come from at all.
        ///
        /// Раздавать наши вещи с кого попало значило бы обесценить их вернее любого шанса:
        /// крыса у дороги выпадала бы наравне с чудовищем. Спрашиваем у самой игры — несёт ли
        /// это существо легендарку в своём списке добычи. Если да, то это как раз те места,
        /// где легендарки и водятся, и наши встают рядом на равных.
        /// </summary>
        private static bool CarriesLegendary(UnitAttribute unit)
        {
            UIItemInfo[] trophies = unit.info.trophies;
            if (trophies == null) return false;

            foreach (UIItemInfo item in trophies)
            {
                if (item != null && item.Quality == UIItemQuality.Legendary) return true;
            }
            return false;
        }

        internal static void Drop(UnitAttribute unit)
        {
            if (!Enabled.Value) return;
            if (unit == null || unit.info == null || unit.Data == null || unit.items == null) return;
            if (!unit.Data.trueDead || !unit.info.canLootCorpse) return;
            if (Forge.Built.Count == 0) return;

            // Die вызывается не однажды: добивание, посмертные эффекты и пересчёты проходят
            // через него же. Без этой отметки один труп выдавал бы комплект столько раз,
            // сколько его потревожили.
            if (!looted.Add(unit.GetInstanceID())) return;

            try
            {
                string asset = unit.info.name;
                UIEquipmentInfo carried = Carried(asset);

                if (carried != null)
                {
                    string where = "неизвестно где";
                    if (ReportArea.Value && AreaManager.Instance != null)
                    {
                        where = AreaManager.Instance.sceneName;
                    }

                    // Клад, а не карман. У квестового босса своя комната, и вещь его лежит
                    // в сундуке, если сундук рядом есть, — так это и читается: не отобрали
                    // с тела, а нашли там, где он это держал.
                    Container hoard = Hoard(unit);

                    if (hoard != null && hoard.items != null)
                    {
                        hoard.items.AddNewInventory(carried);

                        ItemForgePlugin.Log.LogInfo($"«{asset}» пал в области «{where}», "
                            + $"«{carried.Name}» лёг в его сундук.");
                        return;
                    }

                    unit.items.AddNewInventory(carried);

                    ItemForgePlugin.Log.LogInfo($"«{asset}» пал в области «{where}» и отдал "
                        + $"«{carried.Name}» (сундука рядом не нашлось).");
                    return;
                }

                if (!CarriesLegendary(unit)) return;

                float rate = RandomRate.Value > 0f ? RandomRate.Value : LegendaryRate();
                if (rate <= 0f) return;

                float chance = rate * UnitAttribute.TROPHY_DROP_RATE;

                foreach (UIEquipmentInfo piece in Forge.Built)
                {
                    if (piece == null || Carried(piece)) continue;
                    if (UnityEngine.Random.Range(0f, 100f) >= chance) continue;

                    unit.items.AddNewInventory(piece);
                    ItemForgePlugin.Log.LogInfo($"С «{asset}» выпал «{piece.Name}» "
                        + $"(шанс {chance:0.##}%).");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Раздача добычи сорвалась: " + e);
            }
        }

        /// <summary>
        /// Moves what a body wore into the body, so it can be looted.
        ///
        /// Игра раздаёт добычу только из списка трофеев шаблона: надетое в него не попадает
        /// и остаётся на трупе недосягаемым. Для рядового монстра это незаметно, а для
        /// отряда в фиолетовых латах выглядит поломкой — вещи видно, взять нельзя.
        /// </summary>
        internal static void Strip(UnitAttribute dead)
        {
            if (!DropWorn.Value || dead == null || dead.Data == null) return;
            if (!dead.Data.trueDead || dead.info == null || !dead.info.canLootCorpse) return;

            HumaniodUnit body = dead as HumaniodUnit;
            if (body == null || body.equipmentmanger == null) return;

            // Обыскать тело можно не раз: закрыл окно, открыл снова. Снимаем только однажды,
            // иначе на втором заходе износ ляжет поверх износа.
            if (bared.Contains(dead)) return;
            bared.Add(dead);

            try
            {
                int taken = 0;

                taken += Unwear(body, body.equipmentmanger.equipInfos);
                taken += Unwear(body, body.equipmentmanger.standByWeaponInfos);

                if (taken > 0)
                {
                    ItemForgePlugin.Log.LogInfo($"С павшего «{dead.Data.unitname}» снято "
                        + $"надетого: {taken}.");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог снять надетое с павшего: " + e);
            }
        }

        /// <summary>
        /// Takes the real worn things off, rather than adding copies of them.
        ///
        /// Прежде я добавлял в труп новые вещи по шаблонам надетого — и получал вдвое больше,
        /// чем было, причём нетронутых. Настоящие лежат в слотах снаряжения вместе со своей
        /// прочностью, а игра при смерти уже сбивает её на двадцать-семьдесят процентов:
        /// убитого и вправду пробили. Снимаем их, и добыча выходит ровно та, что была на нём,
        /// в том виде, в каком её оставил бой.
        /// </summary>
        private static int Unwear(HumaniodUnit body, EquipInfo[] slots)
        {
            if (slots == null) return 0;

            int taken = 0;

            foreach (EquipInfo slot in slots)
            {
                if (slot == null || !slot.IsEquiped()) continue;
                if (slot.inventory == null || slot.inventory.itemInfo == null) continue;

                body.equipmentmanger.UnequipItem(slot);
                taken++;
            }

            return taken;
        }

        /// <summary>True when this one is not to strip the fallen at all.</summary>
        private static bool Sparing(UnitAttribute who)
        {
            string written = (Spare.Value ?? "").Trim();
            if (written.Length == 0 || who == null || who.info == null) return false;

            string asset = who.info.name ?? "";

            foreach (string one in written.Split(','))
            {
                if (string.Equals(one.Trim(), asset, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Lets the winner take what the loser no longer needs.
        ///
        /// Игроку это не нужно и было бы за него сыграно, поэтому его отряд не трогаем.
        /// Сравниваем по цене: у одного слота она ровно та мера, по которой одна вещь лучше
        /// другой, и она уже учитывает и качество, и ярус, и износ.
        /// </summary>
        internal static void Scavenge(UnitAttribute victim, UnitAttribute killer)
        {
            if (!Scavenging.Value || victim == null || killer == null) return;

            HumaniodUnit looter = killer as HumaniodUnit;
            if (looter == null || looter.Data == null) return;
            if (looter.Data.team == Faction.player || looter.inParty) return;
            if (Sparing(looter)) return;
            if (looter.items == null || looter.equipmentmanger == null) return;
            if (victim.items == null || victim.items.items == null) return;

            try
            {
                UIEquipmentInfo[] worn = looter.equipmentmanger.equips;
                if (worn == null) return;

                List<Inventory> spoils = new List<Inventory>(victim.items.items);
                int taken = 0;

                foreach (Inventory lying in spoils)
                {
                    if (lying == null) continue;

                    UIEquipmentInfo gear = lying.itemInfo as UIEquipmentInfo;
                    if (gear == null) continue;

                    int slot = (int)gear.EquipType;
                    if (slot < 0 || slot >= worn.Length) continue;

                    UIEquipmentInfo already = worn[slot];
                    if (already != null && already.value >= gear.value) continue;

                    // Переносим ту же самую вещь, а не делаем новую по её образцу.
                    //
                    // Было «AddNewInventory(gear)» — это рождение вещи из шаблона, со свежей
                    // прочностью и полным оберегом. Старая при этом снималась с убитого, то есть
                    // на поле появлялся целый второй комплект из ниоткуда. Именно он и лежал
                    // в теле чемпиона вторым корпусом и вторым амулетом, все как новенькие.
                    //
                    // Добыча — это переход вещи из рук в руки, а не её изготовление. Сначала
                    // забираем у павшего, потом отдаём взявшему: наоборот нельзя, иначе
                    // между двумя строками она числится у обоих.
                    // Ноль значит «всю запись целиком». Единица срезала бы стопку на одну штуку,
                    // оставив тот же объект у павшего, — и вещь числилась бы у двоих сразу.
                    victim.items.RemoveInventory(lying, 0);
                    looter.items.AddInventoryNoEvent(lying);
                    looter.equipmentmanger.EquipItem(lying);
                    taken++;
                }

                if (taken > 0)
                {
                    ItemForgePlugin.Log.LogInfo($"«{looter.Data.unitname}» обобрал павшего: "
                        + $"вещей {taken}.");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Обыск павшего сорвался: " + e);
            }
        }

        private static readonly HashSet<string> gone =
            new HashSet<string>(StringComparer.Ordinal);
        private static string goneRead;

        /// <summary>True when this piece has already been found, once and for all.</summary>
        private static bool Gone(string name)
        {
            string written = Found.Value ?? "";

            if (written != goneRead)
            {
                gone.Clear();
                foreach (string one in written.Split(';'))
                {
                    string said = one.Trim();
                    if (said.Length > 0) gone.Add(said);
                }
                goneRead = written;
            }

            return gone.Contains(name);
        }

        /// <summary>Writes a piece out of the world for good.</summary>
        private static void Take(string name)
        {
            Gone(name);                        // на случай, если список ещё не читан
            if (!gone.Add(name)) return;

            string written = string.Join(";", new List<string>(gone).ToArray());

            Found.Value = written;
            goneRead = written;
        }

        /// <summary>The nearest chest this creature could call its own, if there is one.</summary>
        private static Container Hoard(UnitAttribute unit)
        {
            if (ChestRange.Value <= 0f) return null;

            try
            {
                Container[] all = UnityEngine.Object.FindObjectsOfType<Container>();
                if (all == null || all.Length == 0) return null;

                Container nearest = null;
                float best = ChestRange.Value * ChestRange.Value;

                foreach (Container box in all)
                {
                    // В обысканный класть нечего: игрок его уже открыл и больше не откроет.
                    if (box == null || box.searched || box.items == null) continue;

                    float far = (box.transform.position - unit.transform.position).sqrMagnitude;
                    if (far > best) continue;

                    best = far;
                    nearest = box;
                }

                return nearest;
            }
            catch
            {
                return null;
            }
        }

        // Сундук обыскивают один раз: игра набивает его при первом открытии. Отметка на
        // случай, если наполнение позовут дважды — второй заход не должен удваивать находку.
        private static readonly HashSet<int> opened = new HashSet<int>();

        /// <summary>
        /// Lets the set turn up in a chest, rarely.
        ///
        /// Четверо именных боссов носят четыре вещи, и это путь верный: пошёл, нашёл, убил.
        /// Но верный путь проходят один раз, а потом играть становится нечем. Сундук — путь
        /// другой: он ничего не обещает и оттого держит дольше. Шанс намеренно мал, потому
        /// что вещь, которая попадается часто, перестаёт быть находкой.
        /// </summary>
        internal static void Chest(Container cont)
        {
            if (!Enabled.Value || ChestRate.Value <= 0f) return;
            if (cont == null || cont.items == null || Forge.Built.Count == 0) return;

            if (!opened.Add(cont.GetInstanceID())) return;

            try
            {
                int put = 0;

                foreach (UIEquipmentInfo piece in Forge.Built)
                {
                    if (piece == null) continue;

                    // Каждая вещь сета существует однажды. Найденная уходит из мира совсем:
                    // второй такой же нет и не будет, и в этом вся её цена.
                    if (Gone(piece.Name)) continue;

                    if (UnityEngine.Random.Range(0f, 100f) >= ChestRate.Value) continue;

                    cont.items.AddNewInventory(piece);
                    Take(piece.Name);
                    put++;

                    ItemForgePlugin.Log.LogInfo($"В сундуке нашёлся «{piece.Name}» "
                        + $"(шанс {ChestRate.Value:0.##}%). Больше такого в мире нет.");
                }

                if (put > 0 && ReportArea.Value && AreaManager.Instance != null)
                {
                    ItemForgePlugin.Log.LogInfo($"  область: «{AreaManager.Instance.sceneName}».");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог положить вещь в сундук: " + e);
            }
        }

        /// <summary>True when this piece is carried by one of the apex creatures.</summary>
        private static bool Carried(UIEquipmentInfo piece)
        {
            string table = (ApexDrops.Value ?? "");

            foreach (string row in table.Split(';'))
            {
                int split = row.IndexOf('=');
                if (split <= 0) continue;
                if (string.Equals(row.Substring(split + 1).Trim(), piece.Name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }

    // Postfix, а не prefix: родная раздача добычи должна пройти первой, иначе наши вещи
    // легли бы в сумку до того, как игра решит, что труп вообще можно обыскивать.
    /// <summary>
    /// Makes a killed enemy actually die, so that what he wore can be taken.
    ///
    /// Смерть в этой игре — бросок: «Data.trueDead = canKill &amp;&amp; (forceKill || … ||
    /// Random &lt; dieChance)». Почти всех бой только сбивает с ног, и через восемь секунд они
    /// встают. Отсюда и «не могу убить и забрать вещи»: с оглушённого снимать нечего.
    /// Поднимаем не сам исход, а «forceKill» — родной флаг игры, который она же и спрашивает,
    /// так что дальше всё идёт её обычным путём: добыча, прочность, труп.
    /// </summary>
    /// <summary>
    /// Takes the gear off a body when somebody searches it, not when it falls.
    ///
    /// Раздевать в момент смерти было неверно с двух сторон. Поле боя оставалось устлано
    /// голыми телами — снаряжение уходило в сумку трупа, а модель тут же теряла его. И часть
    /// добычи пропадала вовсе: существа с «leftTrophyBag» не оставляют тела, они исчезают и
    /// роняют мешок, а мешок собирается раньше, чем отрабатывает наш обработчик смерти, —
    /// снятое ложилось в того, кого через секунду удаляли.
    ///
    /// Здесь снятие привязано к самому обыску: тела лежат одетыми, пока к ним не подойдут, а
    /// открытое окно показывает и надетое, и запасное оружие, и всё, что было в сумке.
    /// </summary>
    [HarmonyPatch(typeof(LootManager), "StartLooting")]
    internal static class StartLooting_Strip_Patch
    {
        private static void Prefix(ItemStock stock, Container cont)
        {
            try
            {
                // Раздевать здесь больше нечего и нельзя: надетое переезжает в сумку в
                // момент смерти, тем же объектом. Прежний порядок — снимать при открытии
                // окна — уничтожал снаряжение всех, кого одевал не я, потому что у таких
                // вещей не было места в сумке и обнуление слота стирало их насовсем.
                if (Keeping.Handover.Value) return;

                UnitAttribute body = null;

                if (cont != null) body = cont.GetComponent<UnitAttribute>();
                if (body == null && stock != null) body = stock.GetComponent<UnitAttribute>();

                if (body != null) Loot.Strip(body);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог раздеть тело при обыске: " + e);
            }
        }
    }

    /// <summary>
    /// Empties a body that is about to vanish, before the game packs its bag.
    ///
    /// Тёмные маги помечены «leftTrophyBag»: тела они не оставляют, а растворяются и роняют
    /// мешок — и мешок игра набивает из их сумки прямо здесь, в SetToDead. Кто уходит так,
    /// того надо раздеть заранее, иначе надетое исчезнет вместе с телом. Наготы никто не
    /// увидит: эти исчезают в тот же миг.
    /// </summary>
    [HarmonyPatch(typeof(UnitAttribute), "SetToDead")]
    internal static class SetToDead_Bag_Patch
    {
        private static void Prefix(UnitAttribute __instance)
        {
            try
            {
                if (Keeping.Handover.Value) return;

                if (__instance == null || __instance.info == null) return;
                if (!__instance.info.leftTrophyBag) return;

                Loot.Strip(__instance);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог собрать мешок павшего: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "Die")]
    internal static class Die_TrueDeath_Patch
    {
        private static void Prefix(UnitAttribute __instance)
        {
            if (!Loot.TrueDeath.Value || __instance == null || __instance.Data == null) return;
            if (__instance.info == null) return;

            // Без «присмерти» вовсе: нанесли больше урона, чем осталось здоровья, — значит
            // мёртв, и это одно правило на всех. Иначе свои встают через восемь секунд, а
            // чужие лежат оглушённые и потом тоже встают, и ни одна смерть в бою не
            // окончательна. Владыка Ада в мире, где не умирают, — половина замысла.
            if (!Loot.NoLastStand.Value)
            {
                if (__instance.inParty || __instance.Data.team == Faction.player) return;
                if (__instance.info.utype == UnitType.keyNPC) return;
                if (__instance.info.utype == UnitType.companion) return;
            }

            // «canKill» игра ставит тем, кого вообще позволено убить; без него принуждение
            // не сработает — смерть считается как «canKill && (forceKill || ...)».
            __instance.Data.canKill = true;
            __instance.Data.forceKill = true;
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "Die")]
    internal static class Die_Loot_Patch
    {
        private static void Postfix(UnitAttribute __instance, UnitAttribute killer)
        {
            Loot.Drop(__instance);
            Loot.Scavenge(__instance, killer);
        }
    }

    // Окно обыска умеет закрываться только через контейнер, а поклажу лошади мы открываем
    // без него. Здесь — тот же конец, только без вызова, которому не на чем исполниться.
    [HarmonyPatch(typeof(LootManager), "EndLooting")]
    internal static class EndLooting_Loot_Patch
    {
        private static bool Prefix(LootManager __instance)
        {
            try
            {
                Container box = AccessTools.Field(typeof(LootManager), "container")
                    .GetValue(__instance) as Container;

                if (box != null) return true;

                bool stealing = (bool)AccessTools.Field(typeof(LootManager), "isStealing")
                    .GetValue(__instance);

                if (stealing) return true;

                if (__instance.window != null && __instance.window.IsOpen) __instance.window.Hide();

                // Поклажу закрыли — значит в ней что-то поменялось, и лошадь надо взвесить.
                Steed.WeighAll();

                // Слушателей не снимаем: следующий «StartLooting» снимает их сам, прежде чем
                // навесить свои, а обнулять «stocks» на живом окне опаснее, чем оставить.
                AccessTools.Field(typeof(LootManager), "isLooting").SetValue(__instance, false);

                return false;
            }
            catch
            {
                return true;
            }
        }
    }

    // Сундук набивается здесь, и здесь же в него может лечь наше. Не в «AddContentToStock»:
    // через тот же метод наполняются и лавки, и мешки, а сет должен лежать в сундуке.
    [HarmonyPatch(typeof(ContainerContent), "AddContentToContainer")]
    internal static class AddContentToContainer_Loot_Patch
    {
        private static void Postfix(Container container)
        {
            Loot.Chest(container);
        }
    }
}
