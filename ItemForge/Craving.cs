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
    /// The thing a man starts thinking about on the road.
    ///
    /// Сытость закрывается любой едой, и потому еда в этой игре безлика: мешок сухарей решает
    /// всё, что можно решить. Человеку же через какое-то время хочется не еды вообще, а
    /// чего-то определённого — и постели под крышей, а не под телегой.
    ///
    /// Раз в несколько дней у каждого появляется такое желание. Пока оно не исполнено,
    /// настроение убывает вдвое быстрее обычного; исполнилось — желание снято и сверх того
    /// человек рад.
    ///
    /// Хочется всегда посильного. Блюдо берётся только из тех рецептов, что в отряде выучены
    /// и что этому человеку по его собственной руке: нельзя тосковать по тому, чего ты и
    /// назвать не умеешь. Кто не умеет ничего, тому остаётся гостиница — и это само по себе
    /// довод завести повара.
    /// </summary>
    internal static class Craving
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Least;
        internal static ConfigEntry<int> Most;
        internal static ConfigEntry<float> InnChance;
        internal static ConfigEntry<float> Weight;
        internal static ConfigEntry<float> Reward;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Craving", "Enabled", true,
                "Let people want particular things: a named dish, or a night under a roof.");

            Least = config.Bind("Craving", "Least", 3,
                new ConfigDescription(
                    "Fewest days between one wish being met and the next appearing.",
                    new AcceptableValueRange<int>(1, 60)));

            Most = config.Bind("Craving", "Most", 7,
                new ConfigDescription(
                    "Most days between them. Drawn somewhere between the two, so the company "
                    + "does not want things all on the same morning.",
                    new AcceptableValueRange<int>(1, 120)));

            InnChance = config.Bind("Craving", "InnChance", 0.30f,
                new ConfigDescription(
                    "How often the wish is a bed at an inn rather than a dish.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Weight = config.Bind("Craving", "Weight", 2f,
                new ConfigDescription(
                    "What the daily loss of morale is multiplied by while a wish stands unmet. "
                    + "Twice, so ignoring the company costs twenty a day instead of ten.",
                    new AcceptableValueRange<float>(1f, 10f)));

            Reward = config.Bind("Craving", "Reward", 15f,
                new ConfigDescription(
                    "Morale given the moment a wish is met, over and above what the thing itself "
                    + "is worth.",
                    new AcceptableValueRange<float>(0f, 100f)));
        }

        private sealed class Wish
        {
            internal int dish;
            internal bool inn;
            internal int since;
            internal int next;
        }

        private static readonly Dictionary<int, Wish> wishes = new Dictionary<int, Wish>();
        private static bool read;
        private static bool dirty;
        private static float beat;

        private static string Ledger
        {
            get { return Path.Combine(BepInEx.Paths.ConfigPath, "aor.cravings.tsv"); }
        }

        /// <summary>How much faster morale leaves a man who is being ignored.</summary>
        internal static float Burden(UnitAttribute who)
        {
            if (!Enabled.Value || who == null || who.Data == null) return 1f;

            Wish one;
            if (!wishes.TryGetValue(who.Data.id, out one)) return 1f;

            return one.dish != 0 || one.inn ? Weight.Value : 1f;
        }

        /// <summary>Looks over the company now and then, and lets somebody start wanting.</summary>
        internal static void Tick()
        {
            if (!Enabled.Value || Time.time < beat) return;
            beat = Time.time + 5f;

            try
            {
                PartyManager party = PartyManager.instance;
                if (party == null || party.partyMembers == null) return;
                if (TimeManager.Instance == null) return;

                Load();

                int today = TimeManager.TotalDay;

                foreach (HumaniodUnit one in party.partyMembers)
                {
                    if (one == null || one.Data == null) continue;

                    Wish wish;
                    if (!wishes.TryGetValue(one.Data.id, out wish))
                    {
                        // Новичку дают освоиться: желание придёт не в день найма.
                        wish = new Wish { next = today + Span() };
                        wishes[one.Data.id] = wish;
                        dirty = true;
                        continue;
                    }

                    if (wish.dish != 0 || wish.inn) continue;
                    if (today < wish.next) continue;

                    Make(one, wish, today);
                }

                Save();
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог раздать желания: " + e.Message);
            }
        }

        private static int Span()
        {
            int least = Mathf.Min(Least.Value, Most.Value);
            int most = Mathf.Max(Least.Value, Most.Value);

            return UnityEngine.Random.Range(least, most + 1);
        }

        private static void Make(HumaniodUnit who, Wish wish, int today)
        {
            UIItemInfo dish = null;

            if (UnityEngine.Random.value >= InnChance.Value) dish = Dish(who);

            wish.since = today;

            if (dish != null)
            {
                wish.dish = dish.ID;
                wish.inn = false;

                Say(who.Data.unitname + " не отказался бы от такого блюда: " + dish.LocalizedName);
            }
            else
            {
                wish.dish = 0;
                wish.inn = true;

                Say(who.Data.unitname + " устал спать под телегой и хочет в гостиницу");
            }

            dirty = true;
        }

        /// <summary>Something the man could cook himself, if anyone taught the company.</summary>
        private static UIItemInfo Dish(HumaniodUnit who)
        {
            try
            {
                CraftManager craft = CraftManager.Instance;
                if (craft == null || craft.learnedRecipes == null) return null;

                List<UIItemInfo> able = new List<UIItemInfo>();

                foreach (CraftRecipe recipe in craft.learnedRecipes)
                {
                    if (recipe == null || recipe.product == null) continue;
                    if (recipe.requireLv > who.Data.cooking) continue;

                    UIConsumableInfo food = recipe.product as UIConsumableInfo;
                    if (food == null || food.hungryRestore <= 0f) continue;

                    able.Add(recipe.product);
                }

                if (able.Count == 0) return null;

                return able[UnityEngine.Random.Range(0, able.Count)];
            }
            catch
            {
                return null;
            }
        }

        /// <summary>A helping goes down, and may be the one that was wanted.</summary>
        internal static void Ate(UnitAttribute who, UIItemInfo dish)
        {
            if (!Enabled.Value || who == null || who.Data == null || dish == null) return;

            try
            {
                Wish one;
                if (!wishes.TryGetValue(who.Data.id, out one)) return;
                if (one.dish != dish.ID) return;

                Grant(who, one, "дождался своего блюда");
            }
            catch
            {
            }
        }

        /// <summary>The company has slept under a roof.</summary>
        internal static void Slept()
        {
            if (!Enabled.Value) return;

            try
            {
                PartyManager party = PartyManager.instance;
                if (party == null || party.partyMembers == null) return;

                foreach (HumaniodUnit one in party.partyMembers)
                {
                    if (one == null || one.Data == null) continue;

                    Wish wish;
                    if (!wishes.TryGetValue(one.Data.id, out wish)) continue;
                    if (!wish.inn) continue;

                    Grant(one, wish, "выспался под крышей");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог зачесть ночёвку: " + e.Message);
            }
        }

        private static void Grant(UnitAttribute who, Wish wish, string why)
        {
            wish.dish = 0;
            wish.inn = false;
            wish.next = TimeManager.TotalDay + Span();
            dirty = true;

            NPCSaveData mind = who.Data as NPCSaveData;
            if (mind != null && Reward.Value > 0f) mind.AddMorale(Reward.Value);

            ItemForgePlugin.Log.LogInfo(who.Data.unitname + " " + why + ": настроение +"
                + Reward.Value.ToString("0.#") + ".");

            Save();
        }

        private static void Say(string word)
        {
            ItemForgePlugin.Log.LogInfo(word + ".");

            try { GameController.ShowMessage(word, 3f); }
            catch { }
        }

        private static void Load()
        {
            if (read) return;
            read = true;

            try
            {
                if (!File.Exists(Ledger)) return;

                foreach (string line in File.ReadAllLines(Ledger))
                {
                    string[] cells = line.Split('\t');
                    if (cells.Length < 5) continue;

                    int who, dish, since, next;
                    bool inn;

                    if (!int.TryParse(cells[0], out who)) continue;
                    if (!int.TryParse(cells[1], out dish)) continue;
                    if (!bool.TryParse(cells[2], out inn)) continue;
                    if (!int.TryParse(cells[3], out since)) continue;
                    if (!int.TryParse(cells[4], out next)) continue;

                    wishes[who] = new Wish { dish = dish, inn = inn, since = since, next = next };
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог прочитать желания: " + e.Message);
            }
        }

        private static void Save()
        {
            if (!dirty) return;
            dirty = false;

            try
            {
                StringBuilder text = new StringBuilder();
                foreach (KeyValuePair<int, Wish> one in wishes)
                {
                    text.Append(one.Key).Append('\t')
                        .Append(one.Value.dish).Append('\t')
                        .Append(one.Value.inn).Append('\t')
                        .Append(one.Value.since).Append('\t')
                        .Append(one.Value.next).AppendLine();
                }

                File.WriteAllText(Ledger, text.ToString());
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог записать желания: " + e.Message);
            }
        }
    }

    // Комната в трактире раздаёт отряду сытость, бодрость и настроение разом. Значит здесь и
    // кончается тоска по крыше — не в момент оплаты, а когда все выспались.
    [HarmonyPatch(typeof(RoomSlot), "Restore")]
    internal static class RoomRestore_Craving_Patch
    {
        private static void Postfix()
        {
            try { Craving.Slept(); }
            catch { }

            try { Spirit.Rested(); }
            catch { }
        }
    }
}
