using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;

namespace DemonLook
{
    /// <summary>
    /// Keeps decent people out of a demon's company.
    ///
    /// The game has no axis of good and evil — no alignment, no morality score. What it has
    /// instead are two traits that behave exactly like one. When somebody dies at the party's
    /// hands, a companion carrying Cruel gains morale for it and one carrying Kind loses it:
    ///
    ///     if (talentmanger.ContainTrait("Cruel"))     Data.AddMorale(1f);
    ///     else if (talentmanger.ContainTrait("Kind")) Data.AddMorale(-1f);
    ///
    /// So a kind companion following a demon is already being worn down by the company he
    /// keeps, in the game's own reckoning. This only makes the refusal explicit and moves it
    /// to the moment of recruitment, where it can be stated, rather than letting it show up
    /// weeks later as an unexplained collapse in morale.
    ///
    /// Blocking the kind is the default rather than demanding the cruel. Most people in this
    /// world carry neither trait, and requiring Cruel would rule out nearly everybody alive —
    /// which reads less like a demon lord who repels the decent than like a demon lord who
    /// cannot hire anyone. Requiring it outright is one setting away for those who want that.
    /// </summary>
    internal static class Company
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> DemonOnly;
        internal static ConfigEntry<bool> RequireCruel;
        internal static ConfigEntry<string> GoodTraits;
        internal static ConfigEntry<string> EvilTraits;
        internal static ConfigEntry<bool> MercenariesOnly;
        internal static ConfigEntry<bool> BlockMercenaries;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Company", "Enabled", true,
                "Refuse to take the good-hearted into the party.");

            DemonOnly = config.Bind("Company", "DemonOnly", true,
                "Apply this only while the party is led by a demon. Off makes it a rule of the "
                + "world rather than a consequence of who you are.");

            RequireCruel = config.Bind("Company", "RequireCruel", false,
                "Demand cruelty rather than merely refusing kindness. Off by default: most "
                + "people carry neither trait, so requiring it leaves almost nobody recruitable.");

            GoodTraits = config.Bind("Company", "GoodTraits", "Kind",
                "Traits that make somebody unfit for a demon's company, separated by commas. "
                + "Kind is the game's own: it is the trait that loses morale when the party "
                + "kills, and the exact counterpart of Cruel, which gains it.");

            MercenariesOnly = config.Bind("Company", "MercenariesOnly", false,
                "Take none but hired swords: refuse anyone whose career is not Mercenary, which "
                + "is every quest character and every townsman there is. Off by default, because "
                + "the rule above already turns away the good-hearted among them, and this one "
                + "turns away the wicked ones too — it is a rule about station rather than "
                + "character, and it shuts a door the demon might want open.");

            BlockMercenaries = config.Bind("Company", "BlockMercenaries", true,
                "Refuse hired swords outright. Hiring runs through the same door as any other "
                + "recruitment — the game clears a mercenary off its spawner from inside "
                + "AddCompanion — so shutting that door shuts hiring with it. Note what this "
                + "leaves: with the kind turned away as well, the only company a demon can keep "
                + "is the wicked who follow for their own reasons, and there are few of those. "
                + "Walking alone is the likely outcome, which is the point.");

            EvilTraits = config.Bind("Company", "EvilTraits", "Cruel",
                "Traits that count as wicked enough to serve, used when cruelty is demanded.");
        }

        private static List<int> Ids(string names)
        {
            List<int> ids = new List<int>();

            UITalentDatabase db = UITalentDatabase.Instance;
            if (db == null) return ids;

            foreach (string entry in (names ?? "").Split(','))
            {
                string wanted = entry.Trim();
                if (wanted.Length == 0) continue;

                UITalentInfo trait = db.GetTraitByName(wanted);
                if (trait != null) ids.Add(trait.ID);
                else DemonLookPlugin.Log.LogWarning($"Черты «{wanted}» в игре нет, пропускаю.");
            }
            return ids;
        }

        private static bool Carries(NPCSaveData who, string names)
        {
            // У живого спутника черты лежат в менеджере талантов, а у ещё не созданного —
            // только номерами в сохранении. Смотрим там, где они на самом деле есть.
            if (who.Unit != null && who.Unit.talentmanger != null)
            {
                foreach (string entry in (names ?? "").Split(','))
                {
                    string wanted = entry.Trim();
                    if (wanted.Length > 0 && who.Unit.talentmanger.ContainTrait(wanted)) return true;
                }
                return false;
            }

            if (who.traits == null) return false;

            foreach (int id in Ids(names))
            {
                if (who.traits.Contains(id)) return true;
            }
            return false;
        }

        /// <returns>True when this one may not join.</returns>
        internal static bool Refuse(NPCSaveData who)
        {
            if (!Enabled.Value || who == null) return false;

            try
            {
                if (DemonOnly.Value)
                {
                    PartyManager party = PartyManager.instance;
                    HumaniodUnit leader = party != null ? party.leader : null;
                    if (leader == null || !Racial.IsDemon(leader)) return false;
                }

                if (Carries(who, GoodTraits.Value))
                {
                    DemonLookPlugin.Log.LogInfo($"«{who.unitname}» не встанет под руку демона: "
                        + "слишком добр.");
                    return true;
                }

                if (BlockMercenaries.Value && who.career == CareerType.Mercenary)
                {
                    DemonLookPlugin.Log.LogInfo($"«{who.unitname}» не нанимается: наёмников "
                        + "демон не берёт.");
                    return true;
                }

                if (MercenariesOnly.Value && who.career != CareerType.Mercenary)
                {
                    DemonLookPlugin.Log.LogInfo($"«{who.unitname}» отвергнут: не наёмник "
                        + $"({who.career}).");
                    return true;
                }

                if (RequireCruel.Value && !Carries(who, EvilTraits.Value))
                {
                    DemonLookPlugin.Log.LogInfo($"«{who.unitname}» отвергнут: недостаточно жесток.");
                    return true;
                }

                return false;
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogError("Проверка спутника сорвалась, беру как есть: " + e);
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(PartyManager), "AddCompanion")]
    internal static class AddCompanion_Patch
    {
        private static bool Prefix(NPCSaveData psd)
        {
            return !Company.Refuse(psd);
        }
    }
}
