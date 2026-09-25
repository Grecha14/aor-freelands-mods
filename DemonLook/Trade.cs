using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace DemonLook
{
    /// <summary>
    /// Traders on the roads and the gangs that wait for them.
    ///
    /// У каждого города свои торговцы: трое возят что выгоднее, двое — только еду из деревень в
    /// города. Торговец покупает там, где товара в избытке, и везёт туда, где его не хватает, в
    /// любой город; в пути он столько дней, сколько даёт дорога.
    ///
    /// На дорогах сидят шайки. Проходящий караван они грабят тем вернее, чем их больше против его
    /// охраны: груз уходит в их добычу, торговец поворачивает назад, а город, куда он шёл, не
    /// получает ничего — если это была еда, в городе голод, и еда дорожает втрое. Тогда этот
    /// город даёт задание разбить шайку, и тот, кто её разобьёт, найдёт награбленное при её
    /// вожаке. Кого не трогают, тех каждую неделю становится на одного больше.
    ///
    /// Ночует торговец в пути под крышей: в таверне того города или деревни, что стоит ближе
    /// всего к его дороге, — двадцать медных с человека трактиру и еда с его склада. Под крышей
    /// ночью не грабят; нет жилья по дороге — ночь в поле, и напасть на него в полтора раза
    /// вернее. Последнюю ночь он проводит в таверне города, куда шёл.
    /// </summary>
    internal static class Trade
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Traders;
        internal static ConfigEntry<int> FoodTraders;
        internal static ConfigEntry<int> Load;
        internal static ConfigEntry<int> Guards;
        internal static ConfigEntry<float> GangsPerTown;
        internal static ConfigEntry<int> GangStart;
        internal static ConfigEntry<int> GangCap;
        internal static ConfigEntry<float> PacksPerTown;
        internal static ConfigEntry<int> PackStart;
        internal static ConfigEntry<int> PackCap;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Trade", "Enabled", true,
                "Let traders travel between all towns with their loads, and gangs rob them on the roads.");

            Traders = config.Bind("Trade", "Traders", 3,
                new ConfigDescription("Traders of all goods a town keeps.", new AcceptableValueRange<int>(0, 20)));

            FoodTraders = config.Bind("Trade", "FoodTraders", 2,
                new ConfigDescription("Traders who carry only food from the villages.", new AcceptableValueRange<int>(0, 20)));

            Load = config.Bind("Trade", "Load", 100,
                new ConfigDescription("What a trader carries at most.", new AcceptableValueRange<int>(1, 10000)));

            Guards = config.Bind("Trade", "Guards", 4,
                new ConfigDescription("How many guards ride with a trader.", new AcceptableValueRange<int>(0, 50)));

            GangsPerTown = config.Bind("Trade", "GangsPerTown", 0.5f,
                new ConfigDescription("How many gangs the roads hold for each town.", new AcceptableValueRange<float>(0f, 5f)));

            GangStart = config.Bind("Trade", "GangStart", 6,
                new ConfigDescription("How many men a new gang has.", new AcceptableValueRange<int>(1, 50)));

            GangCap = config.Bind("Trade", "GangCap", 30,
                new ConfigDescription("How many men a gang left alone can grow to.", new AcceptableValueRange<int>(1, 200)));

            PacksPerTown = config.Bind("Trade", "PacksPerTown", 0.5f,
                new ConfigDescription("How many packs of beasts the roads hold for each town.", new AcceptableValueRange<float>(0f, 5f)));

            PackStart = config.Bind("Trade", "PackStart", 4,
                new ConfigDescription("How many beasts a new pack has.", new AcceptableValueRange<int>(1, 50)));

            PackCap = config.Bind("Trade", "PackCap", 20,
                new ConfigDescription("How many beasts a pack left alone can grow to.", new AcceptableValueRange<int>(1, 200)));
        }

        internal static bool On()
        {
            return Enabled != null && Enabled.Value;
        }

        internal class Trader
        {
            internal int id;
            internal string home;
            internal bool food;
            internal string at;
            internal string from;
            internal string to;
            internal int arrive = -1;
            internal int guards;
            internal string stop;
            internal readonly Dictionary<GoodsType, float> cargo = new Dictionary<GoodsType, float>();

            internal float Carried()
            {
                float n = 0f;
                foreach (float v in cargo.Values) n += v;
                return n;
            }
        }

        internal class Gang
        {
            internal int id;
            internal bool beasts;
            internal string a;
            internal string b;
            internal int men;
            internal int grown;
            internal int quest = -1;
            internal readonly Dictionary<GoodsType, float> loot = new Dictionary<GoodsType, float>();
        }

        internal static readonly List<Trader> traders = new List<Trader>();
        internal static readonly List<Gang> gangs = new List<Gang>();
        private static int nextId = 1;

        // ------------------------------------------------------------------ дороги

        private static float scale = -1f;

        /// <summary>Days on the road between two places: one for the distance to a place's nearest neighbour.</summary>
        internal static int Days(string a, string b)
        {
            if (scale <= 0f)
            {
                List<float> nearest = new List<float>();
                foreach (Economy.Place p in Economy.Places)
                {
                    float best = float.MaxValue;
                    foreach (Economy.Place o in Economy.Places)
                    {
                        if (ReferenceEquals(o, p)) continue;
                        float d = Economy.Distance(p.name, o.name);
                        if (d > 0f && d < best) best = d;
                    }
                    if (best < float.MaxValue) nearest.Add(best);
                }
                nearest.Sort();
                scale = nearest.Count > 0 ? nearest[nearest.Count / 2] : 1f;
            }

            float far = Economy.Distance(a, b);
            if (far <= 0f) return 3;
            return Mathf.Clamp(Mathf.RoundToInt(far / scale), 1, 14);
        }

        // ------------------------------------------------------------------ день

        /// <summary>A day of the roads: traders move, gangs rob and grow.</summary>
        internal static void Day(int d, System.Random dice)
        {
            Muster(d, dice);

            // Задание, которое так никто и не взял, сошло с доски: при новом грабеже город объявит его снова.
            if (RandomQuestManager.instance != null)
            {
                foreach (Gang g in gangs)
                {
                    if (g.quest >= 0 && RandomQuestManager.instance.quests.Find(q => q.id == g.quest) == null) g.quest = -1;
                }
            }

            foreach (Trader t in traders)
            {
                if (t.to != null)
                {
                    if (d >= t.arrive) Arrive(t, d);
                    else
                    {
                        // Под крышей ночью не грабят: риск только дорожный; в поле — в полтора раза выше.
                        Economy.Place inn = Economy.Get(t.stop);
                        if (inn != null) Lodge(t, inn);
                        Rob(t, d, dice, inn != null ? 1f : 1.5f);
                    }
                    continue;
                }

                if (t.food) PlanFood(t, d);
                else PlanGoods(t, d);
            }

            foreach (Gang g in gangs)
            {
                if (d - g.grown >= 7 && g.men < (g.beasts ? PackCap.Value : GangCap.Value))
                {
                    g.men++;
                    g.grown = d;
                }
            }
        }

        /// <summary>Makes the traders and the gangs the world is short of.</summary>
        private static void Muster(int d, System.Random dice)
        {
            List<Economy.Place> towns = new List<Economy.Place>();
            List<Economy.Place> all = new List<Economy.Place>(Economy.Places);
            foreach (Economy.Place p in all) if (!p.village) towns.Add(p);
            if (towns.Count == 0) return;

            foreach (Economy.Place town in towns)
            {
                int general = 0, foodies = 0;
                foreach (Trader t in traders)
                {
                    if (t.home != town.name) continue;
                    if (t.food) foodies++; else general++;
                }
                for (; general < Traders.Value; general++) traders.Add(new Trader { id = nextId++, home = town.name, at = town.name, guards = Guards.Value });
                for (; foodies < FoodTraders.Value; foodies++) traders.Add(new Trader { id = nextId++, home = town.name, at = town.name, guards = Guards.Value, food = true });
            }

            foreach (bool beasts in new[] { false, true })
            {
                int want = Mathf.RoundToInt(towns.Count * (beasts ? PacksPerTown.Value : GangsPerTown.Value));
                int have = gangs.FindAll(x => x.beasts == beasts).Count;
                for (int tries = 0; have < want && all.Count > 1 && tries < 50; tries++)
                {
                    Economy.Place a = all[dice.Next(all.Count)];
                    Economy.Place b = null;
                    float best = float.MaxValue;
                    foreach (Economy.Place o in all)
                    {
                        if (ReferenceEquals(o, a)) continue;
                        float dist = Economy.Distance(a.name, o.name);
                        if (dist > 0f && dist < best) { best = dist; b = o; }
                    }
                    if (b == null) break;
                    gangs.Add(new Gang { id = nextId++, beasts = beasts, a = a.name, b = b.name, men = beasts ? PackStart.Value : GangStart.Value, grown = d });
                    have++;
                    DemonLookPlugin.Log.LogInfo($"Дороги: на пути «{a.name}» — «{b.name}» завелась {(beasts ? "стая зверей" : "шайка")}.");
                }
            }
        }

        private static void Go(Trader t, string to, int d)
        {
            t.from = t.at;
            t.to = to;
            t.arrive = d + Days(t.at, to);
            t.at = null;
            t.stop = t.arrive - d > 1 ? Waypoint(t.from, t.to) : null;
        }

        /// <summary>The town or village nearest the road between two places, if any is on the way at all.</summary>
        private static string Waypoint(string from, string to)
        {
            float direct = Economy.Distance(from, to);
            if (direct <= 0f) return null;

            string best = null;
            float least = direct * 1.5f;
            foreach (Economy.Place p in Economy.Places)
            {
                if (p.name == from || p.name == to) continue;
                float a = Economy.Distance(from, p.name), b = Economy.Distance(p.name, to);
                if (a <= 0f || b <= 0f) continue;
                if (a + b < least) { least = a + b; best = p.name; }
            }
            return best;
        }

        /// <summary>A night under a roof: the inn is paid, and the pantry feeds the trader and his guards.</summary>
        private static void Lodge(Trader t, Economy.Place inn)
        {
            int men = 1 + t.guards;
            inn.tavernCoin += 20f * men;
            inn.Eat(men);
            Economy.Touch();
        }

        private static void Arrive(Trader t, int d)
        {
            Economy.Place p = Economy.Get(t.to);
            if (p != null)
            {
                foreach (KeyValuePair<GoodsType, float> c in t.cargo) p.Add(c.Key, c.Value);
                Lodge(t, p);
            }
            t.stop = null;
            t.cargo.Clear();
            t.at = t.to;
            t.from = null;
            t.to = null;
            t.arrive = -1;
            if (t.guards < Guards.Value && d % 7 == 0) t.guards++;
        }

        private static void Rob(Trader t, int d, System.Random dice, float risk)
        {
            foreach (Gang g in gangs)
            {
                bool onRoad = (g.a == t.from && g.b == t.to) || (g.a == t.to && g.b == t.from);
                if (!onRoad) continue;

                float chance = risk * (g.beasts ? 0.35f : 0.5f) * g.men / (g.men + t.guards * 2f);
                if (dice.NextDouble() >= chance) continue;

                // Шайка забирает груз себе; звери его рвут и растаскивают — не достаётся никому.
                if (!g.beasts)
                {
                    foreach (KeyValuePair<GoodsType, float> c in t.cargo)
                    {
                        float had;
                        g.loot.TryGetValue(c.Key, out had);
                        g.loot[c.Key] = had + c.Value;
                    }
                }

                string target = t.to;
                t.cargo.Clear();
                t.guards = Mathf.Max(0, t.guards - 1);
                t.at = t.from;
                t.from = null;
                t.to = null;
                t.arrive = -1;
                t.stop = null;

                DemonLookPlugin.Log.LogInfo($"Дороги: {(g.beasts ? "стая" : "шайка")} ({g.men}) разорила караван в «{target}».");
                Bounty(g, target);
                return;
            }
        }

        private static float Keep(Economy.Place p, GoodsType t)
        {
            if (!Economy.Food(t)) return Economy.RawKeep.Value;
            float days = p.village ? 3f : 7f;
            float share = t == GoodsType.Produce ? 0.6f : 0.4f;
            return days * p.mouths * share;
        }

        private static void Buy(Trader t, Economy.Place p, GoodsType g, float n)
        {
            if (n <= 0f) return;
            p.Add(g, -n);
            float had;
            t.cargo.TryGetValue(g, out had);
            t.cargo[g] = had + n;
        }

        /// <summary>The food trader: from the village richest in food to the town that lacks it most.</summary>
        private static void PlanFood(Trader t, int d)
        {
            Economy.Place here = Economy.Get(t.at);
            if (here == null) return;

            if (t.Carried() <= 0f)
            {
                if (here.village && here.FoodStock() > Keep(here, GoodsType.Produce) + Keep(here, GoodsType.Meat) + 5f)
                {
                    float room = Load.Value;
                    foreach (GoodsType g in new[] { GoodsType.Produce, GoodsType.Meat })
                    {
                        float spare = Mathf.Min(room, here.Has(g) - Keep(here, g));
                        if (spare <= 0f) continue;
                        Buy(t, here, g, spare);
                        room -= spare;
                    }
                }

                if (t.Carried() <= 0f)
                {
                    Economy.Place richest = null;
                    float most = 5f;
                    foreach (Economy.Place v in Economy.Places)
                    {
                        if (!v.village) continue;
                        float spare = v.FoodStock() - Keep(v, GoodsType.Produce) - Keep(v, GoodsType.Meat);
                        float worth = spare / (1f + Days(t.at, v.name));
                        if (worth > most) { most = worth; richest = v; }
                    }
                    if (richest != null && richest.name != t.at) Go(t, richest.name, d);
                    return;
                }
            }

            Economy.Place hungriest = null;
            float least = float.MaxValue;
            foreach (Economy.Place town in Economy.Places)
            {
                if (town.village || town.name == t.at) continue;
                float days = Economy.FoodDays(town) + Days(t.at, town.name) * 0.5f;
                if (days < least) { least = days; hungriest = town; }
            }
            if (hungriest != null) Go(t, hungriest.name, d);
        }

        /// <summary>The trader of all goods: the good and the road that pay best, anywhere.</summary>
        private static void PlanGoods(Trader t, int d)
        {
            Economy.Place here = Economy.Get(t.at);
            if (here == null) return;

            GoodsType bestGood = GoodsType.None;
            Economy.Place bestTo = null;
            float bestWorth = 3f;

            List<GoodsType> goods = new List<GoodsType>(Economy.Raw);
            foreach (GoodsType g in goods)
            {
                float spare = here.Has(g) - Keep(here, g);
                if (spare <= 0f) continue;

                foreach (Economy.Place o in Economy.Places)
                {
                    if (o.name == t.at) continue;
                    float want = Keep(o, g) - o.Has(g);
                    if (want <= 0f) continue;
                    float worth = Mathf.Min(spare, want) / (1f + Days(t.at, o.name));
                    if (worth > bestWorth) { bestWorth = worth; bestGood = g; bestTo = o; }
                }
            }

            if (bestTo == null)
            {
                // Нечего везти отсюда — домой, если он не дома.
                if (t.at != t.home && d % 3 == 0) Go(t, t.home, d);
                return;
            }

            float spareBest = here.Has(bestGood) - Keep(here, bestGood);
            float wantBest = Keep(bestTo, bestGood) - bestTo.Has(bestGood);
            Buy(t, here, bestGood, Mathf.Min(Load.Value, Mathf.Min(spareBest, wantBest)));
            Go(t, bestTo.name, d);
        }

        // ------------------------------------------------------------------ задание на шайку

        /// <summary>The town the robbed caravan was going to puts a price on the gang or the pack.</summary>
        private static void Bounty(Gang g, string town)
        {
            if (g.quest >= 0) return;
            RandomQuest quest = PostHunt(town, !g.beasts, g.men);
            if (quest == null) return;
            g.quest = quest.id;
            Economy.Touch();
        }

        /// <summary>A kill errand of a town against bandits or beasts, as many as they are.</summary>
        internal static RandomQuest PostHunt(string town, bool bandits, int men)
        {
            try
            {
                WorldTownInfo info = WorldPlacesManager.instance != null ? WorldPlacesManager.instance.GetTown(town) : null;
                if (info == null || !FactionManager.IsTownFaction(info.faction) || RandomQuestManager.instance == null) return null;

                RandomQuestSpawner kill = null;
                foreach (FactionRandomQuestSpawnerSet set in RandomQuestManager.instance.spawnerSets)
                {
                    if (set == null || set.spawnPlace != info.faction || set.mercenaryQuestSpawners == null) continue;
                    foreach (RandomQuestSpawner s in set.mercenaryQuestSpawners)
                    {
                        if (s is RQKillSpawner) { kill = s; break; }
                    }
                    if (kill != null) break;
                }
                if (kill == null) return null;

                RandomQuest quest = Errands.SpawnAgainst(kill, bandits, Mathf.Max(1, men));
                if (quest == null) return null;

                Souls.Say(bandits
                    ? $"В «{town}» ищут тех, кто разобьёт шайку с дороги: задание на доске."
                    : $"В «{town}» ищут охотников на стаю, что рвёт караваны: задание на доске.");
                return quest;
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Дороги: задание не вышло: " + e.Message);
                return null;
            }
        }

        /// <summary>The gang is beaten: gone from the roads.</summary>
        internal static void Beaten(RandomQuest quest)
        {
            if (quest == null) return;
            int n = gangs.RemoveAll(g => g.quest == quest.id);
            if (n > 0) { Economy.Touch(); DemonLookPlugin.Log.LogInfo("Дороги: шайка разбита."); }
        }

        /// <summary>The loot of the gang rides with its leader, for whoever beats it.</summary>
        internal static void Arm(RandomQuest quest, TravelGroup group)
        {
            if (quest == null || group == null || group.leader == null || group.leader.items == null) return;

            Gang g = gangs.Find(x => x.quest == quest.id);
            if (g == null || g.beasts || g.loot.Count == 0) return;

            System.Random dice = new System.Random(quest.id);
            int laid = 0;
            foreach (KeyValuePair<GoodsType, float> l in g.loot)
            {
                int n = Mathf.Min(20, Mathf.FloorToInt(l.Value));
                if (n <= 0) continue;
                UIItemInfo what = Economy.AnyWare(l.Key, dice);
                if (what == null) continue;
                group.leader.items.AddInventory(new Inventory(what, n));
                laid += n;
            }
            g.loot.Clear();
            if (laid > 0) Economy.Touch();
        }

        // ------------------------------------------------------------------ файл

        internal static void Write(List<string> lines)
        {
            lines.Add("traderid=" + nextId);
            foreach (Trader t in traders)
            {
                string cargo = "";
                foreach (KeyValuePair<GoodsType, float> c in t.cargo) cargo += (int)c.Key + ":" + c.Value.ToString("0.#", CultureInfo.InvariantCulture) + ",";
                lines.Add("trader=" + t.id + ";" + t.home + ";" + (t.food ? 1 : 0) + ";" + (t.at ?? "") + ";" + (t.from ?? "") + ";"
                    + (t.to ?? "") + ";" + t.arrive + ";" + t.guards + ";" + cargo + ";" + (t.stop ?? ""));
            }
            foreach (Gang g in gangs)
            {
                string loot = "";
                foreach (KeyValuePair<GoodsType, float> c in g.loot) loot += (int)c.Key + ":" + c.Value.ToString("0.#", CultureInfo.InvariantCulture) + ",";
                lines.Add("gang=" + g.id + ";" + g.a + ";" + g.b + ";" + g.men + ";" + g.grown + ";" + g.quest + ";" + loot + ";" + (g.beasts ? 1 : 0));
            }
        }

        internal static void Clear()
        {
            traders.Clear();
            gangs.Clear();
            nextId = 1;
            scale = -1f;
        }

        private static void Cargo(string s, Dictionary<GoodsType, float> into)
        {
            foreach (string part in (s ?? "").Split(','))
            {
                int colon = part.IndexOf(':');
                if (colon <= 0) continue;
                int t;
                float n;
                if (int.TryParse(part.Substring(0, colon), out t)
                    && float.TryParse(part.Substring(colon + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out n))
                    into[(GoodsType)t] = n;
            }
        }

        internal static bool Read(string key, string[] v)
        {
            switch (key)
            {
                case "traderid":
                    int.TryParse(v[0], out nextId);
                    return true;
                case "trader":
                    if (v.Length < 9) return true;
                    Trader t = new Trader { home = v[1], food = v[2] == "1" };
                    int.TryParse(v[0], out t.id);
                    t.at = v[3].Length > 0 ? v[3] : null;
                    t.from = v[4].Length > 0 ? v[4] : null;
                    t.to = v[5].Length > 0 ? v[5] : null;
                    int.TryParse(v[6], out t.arrive);
                    int.TryParse(v[7], out t.guards);
                    Cargo(v[8], t.cargo);
                    t.stop = v.Length >= 10 && v[9].Length > 0 ? v[9] : null;
                    traders.Add(t);
                    return true;
                case "gang":
                    if (v.Length < 7) return true;
                    Gang g = new Gang { a = v[1], b = v[2] };
                    int.TryParse(v[0], out g.id);
                    int.TryParse(v[3], out g.men);
                    int.TryParse(v[4], out g.grown);
                    int.TryParse(v[5], out g.quest);
                    Cargo(v[6], g.loot);
                    g.beasts = v.Length >= 8 && v[7] == "1";
                    gangs.Add(g);
                    return true;
            }
            return false;
        }
    }

    // Разбили шайку по заданию города — её больше нет на дорогах.
    [HarmonyPatch(typeof(RandomQuestManager), "OnQuestSuccess")]
    internal static class QuestSuccess_Trade_Patch
    {
        private static void Postfix(RandomQuest quest)
        {
            try { Trade.Beaten(quest); } catch { }
        }
    }

    // Отряд шайки вышел на карту — награбленное при его вожаке.
    [HarmonyPatch(typeof(RQKillSpawner), "OnQuestStart")]
    internal static class KillStart_Trade_Patch
    {
        private static void Postfix(RandomQuest quest)
        {
            try
            {
                if (quest == null || quest.travelGroupIds == null || WorldTravelManager.instance == null) return;
                foreach (int id in quest.travelGroupIds)
                {
                    TravelGroup group = WorldTravelManager.instance.FindGroupById(id);
                    if (group != null) Trade.Arm(quest, group);
                }
            }
            catch
            {
            }
        }
    }
}
