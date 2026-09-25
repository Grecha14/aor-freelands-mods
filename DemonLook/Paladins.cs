using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace DemonLook
{
    /// <summary>
    /// The order that does not wait for a reason.
    ///
    /// Мир поднимается на демона не сразу: покуда он не вырезал сотню мирных, никому нет до
    /// него дела, и это верно — стража не бросается на прохожего за то, что он родился не тем.
    ///
    /// Но один орден в этом мире устроен иначе. Белый Конь — не городская стража и не наёмники,
    /// он существует ровно за тем, чтобы находить зло и убивать его, и зло для него — не
    /// поступок, а порода. Ему довольно знать, кто перед ним.
    ///
    /// Здесь орден получает своё имя — Паладины, — своё описание и своё право: он враждебен
    /// демону с первого дня и остаётся враждебен, сколько бы тот ни жил праведно. Единственное
    /// исключение из общего правила.
    /// </summary>
    internal static class Paladins
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Sworn;
        internal static ConfigEntry<bool> Rename;
        internal static ConfigEntry<string> Name;
        internal static ConfigEntry<string> About;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Paladins", "Enabled", true,
                "Let one order hunt the demon from the first day, whatever the tally says. The "
                + "rest of the world still needs a reason; these need only to know what he is.");

            Sworn = config.Bind("Paladins", "Sworn", "WhiteSteed",
                "Which factions are sworn against him from the start, by the game's own names. "
                + "WhiteSteed is the knightly order — the Paladins.");

            Rename = config.Bind("Paladins", "Rename", true,
                "Give the order its proper name and words. The game calls it the White Steed "
                + "and says almost nothing about it.");

            Name = config.Bind("Paladins", "Name", "Паладины",
                "What the order is called.");

            About = config.Bind("Paladins", "About",
                "Орден, который не ждёт повода. Его рыцари приносят обет не городу, не королю "
                + "и не собственной славе, а одному простому обещанию: покуда в мире есть зло, "
                + "оно не будет спать спокойно.\n\n"
                + "Зло они понимают буквально и не берут в расчёт поступков. Разбойник, "
                + "которого можно повесить, для них дело стражи; их дело — то, что рождается "
                + "злом и остаётся им, что бы ни делало. Оттого с ними невозможно договориться "
                + "и незачем оправдываться: они пришли не судить, а исполнять.\n\n"
                + "Башня ордена стоит в стороне от городов и живёт своим уставом. Говорят, в "
                + "её подвалах держат то, что даже они не решились уничтожить.",
                "And what is written about it.");
        }

        /// <summary>Тот ли это орден, что клялся на нас.</summary>
        internal static bool Is(Faction faction)
        {
            if (Enabled == null || !Enabled.Value) return false;

            foreach (string one in (Sworn.Value ?? "").Split(','))
            {
                if (string.Equals(one.Trim(), faction.ToString(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>И он же — по самому существу.</summary>
        internal static bool Is(UnitAttribute who)
        {
            try
            {
                return who != null && who.Data != null && Is(who.Data.team);
            }
            catch
            {
                return false;
            }
        }

        private static bool named;

        /// <summary>Даёт ордену имя и слова — однажды за игру.</summary>
        internal static void Call()
        {
            if (Enabled == null || !Enabled.Value || !Rename.Value || named) return;

            try
            {
                FactionManager book = FactionManager.Instance;
                if (book == null || book.factions == null) return;

                foreach (Team one in book.factions)
                {
                    if (one == null || !Is(one.faction)) continue;

                    one.factionName = Name.Value;
                    one.description = (About.Value ?? "").Replace("\\n", "\n");

                    named = true;

                    DemonLookPlugin.Log.LogInfo($"Орден «{one.faction}» назван «{Name.Value}».");
                }
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Не смог назвать орден: " + e.Message);
            }
        }
    }

    // Имя ордену даём, когда стороны уже собраны.
    [HarmonyPatch(typeof(FactionManager), "Awake")]
    internal static class Awake_Paladins_Patch
    {
        private static void Postfix()
        {
            try { Paladins.Call(); } catch { }
        }
    }
}
