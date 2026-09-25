using System;
using BepInEx.Configuration;
using DuloGames.UI;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// What a thing asks of the hand that holds it.
    ///
    /// Требования вещей писаны под человека, каким он был до того, как у пород появились свои
    /// дары. Орк родится с прибавкой в пятнадцать силы, мужчина с десятью, и латы, которые
    /// прежде надевал не всякий, теперь надевает всякий: порог остался там же, а шагнули к
    /// нему с подставки.
    ///
    /// Оттого пороги поднимаются ровно на то, чем вознаграждена порода. Поднимаются только
    /// там, где уже что-то стояло: вещь, которой всё равно, в чьей она руке, такой и остаётся.
    /// И только те три, которыми вещь и держится, — сила, выносливость, ловкость. Восприятие,
    /// разум и воля к тяжести доспеха отношения не имеют, и трогать их незачем.
    /// </summary>
    internal static class Demand
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Raise;
        internal static ConfigEntry<string> Which;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Demand", "Enabled", true,
                "Raise what equipment asks of the body. The gifts of kind and sex handed "
                + "everybody ten to fifteen points they did not earn, and every threshold in "
                + "the game was written before those gifts existed.");

            Raise = config.Bind("Demand", "Raise", 10,
                new ConfigDescription(
                    "By how much. Ten is what a man gets for being a man and a woman for being "
                    + "a woman; an orc gets half again more, and an orc in plate is the point.",
                    new AcceptableValueRange<int>(0, 100)));

            Which = config.Bind("Demand", "Which", "strength,endurance,agility",
                "Which of the six are raised, by the game's own names: strength, endurance, "
                + "agility, precision, intelligence, willpower. The other three decide nothing "
                + "about whether a thing can be carried.");

            Telling = config.Bind("Demand", "Telling", true,
                "Say in the log how many things were raised.");
        }

        private static bool raised;

        private static bool Counts(string name)
        {
            foreach (string one in (Which.Value ?? "").Split(','))
            {
                if (string.Equals(one.Trim(), name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Raises every threshold that was already there.</summary>
        internal static void Lift()
        {
            if (raised || Enabled == null || !Enabled.Value || Raise.Value <= 0) return;

            try
            {
                UIItemDatabase db;
                try { db = UIItemDatabase.Instance; }
                catch { return; }

                if (db == null || db.items == null) return;

                raised = true;

                bool str = Counts("strength");
                bool end = Counts("endurance");
                bool agi = Counts("agility");
                bool pre = Counts("precision");
                bool int_ = Counts("intelligence");
                bool wil = Counts("willpower");

                int much = Raise.Value;
                int things = 0, thresholds = 0;

                foreach (UIItemInfo thing in db.items)
                {
                    UIEquipmentInfo gear = thing as UIEquipmentInfo;
                    if (gear == null || gear.attributeRquire == null) continue;

                    HumanAttribute asks = gear.attributeRquire;
                    int was = 0;

                    if (str && asks.BSstrength > 0) { asks.BSstrength += much; was++; }
                    if (end && asks.BSendurance > 0) { asks.BSendurance += much; was++; }
                    if (agi && asks.BSagility > 0) { asks.BSagility += much; was++; }
                    if (pre && asks.BSprecision > 0) { asks.BSprecision += much; was++; }
                    if (int_ && asks.BSintelligence > 0) { asks.BSintelligence += much; was++; }
                    if (wil && asks.BSwillpower > 0) { asks.BSwillpower += much; was++; }

                    if (was <= 0) continue;

                    things++;
                    thresholds += was;
                }

                if (Telling.Value)
                {
                    ItemForgePlugin.Log.LogInfo($"Требования подняты на {much}: вещей {things}, "
                        + $"порогов {thresholds}.");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог поднять требования: " + e.Message);
            }
        }
    }
}
