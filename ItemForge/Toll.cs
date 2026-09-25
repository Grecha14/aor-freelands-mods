using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Makes a kill worth what the kill was worth.
    ///
    /// Игра делит опыт по одной мерке: мощь убитого, помноженная на размер отряда и на него
    /// же поделённая. Кого убили — не в счёт. Оттого отряд сорокового уровня, вырезающий
    /// деревенских крыс, растёт ровно так же, как если бы он дрался с равными, только
    /// быстрее: крыс больше.
    ///
    /// Здесь добавлены две вещи. Общая доля — просто меньше опыта за всё, потому что расти
    /// вчетверо за одну зачистку не дело. И разница уровней: за того, кто выше тебя, платят
    /// больше, за того, кто ниже — меньше, вплоть до крох.
    ///
    /// Цена характеристик при этом не тронута: покупка статов остаётся ровно той, что была.
    /// </summary>
    internal static class Toll
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Share;
        internal static ConfigEntry<float> Step;
        internal static ConfigEntry<float> Floor;
        internal static ConfigEntry<float> Ceiling;
        internal static ConfigEntry<bool> Beasts;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Toll", "Enabled", true,
                "Weigh a kill by who was killed. Without this the game pays the same for a rat "
                + "as for a knight of one's own standing, so long as their power is written "
                + "the same.");

            Share = config.Bind("Toll", "Share", 0.6f,
                new ConfigDescription(
                    "What part of the game's own reward is paid at all. Three fifths: growing "
                    + "four levels in one clearing is not a fight, it is a harvest.",
                    new AcceptableValueRange<float>(0.05f, 2f)));

            Step = config.Bind("Toll", "Step", 0.05f,
                new ConfigDescription(
                    "What one level of difference is worth, as a share. A twentieth: a foe ten "
                    + "levels above pays half again, ten below pays half.",
                    new AcceptableValueRange<float>(0f, 0.5f)));

            Floor = config.Bind("Toll", "Floor", 0.1f,
                new ConfigDescription(
                    "The least that is ever paid, however far beneath you the dead man stood. "
                    + "Not nothing: killing is still killing, and a tenth keeps the training "
                    + "yard worth something without making it a living.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Ceiling = config.Bind("Toll", "Ceiling", 2f,
                new ConfigDescription(
                    "The most that is ever paid, however far above you the dead man stood. "
                    + "Twice: a giant-killer earns well, but one lucky blow must not carry a "
                    + "man through half the game.",
                    new AcceptableValueRange<float>(1f, 10f)));

            Beasts = config.Bind("Toll", "Beasts", true,
                "Count the party's beasts among those the pot is divided between. A tamed beast "
                + "is a fighter of the company and eats from where the company eats: taking a "
                + "wolf along means everyone else moves up a little. Off, and beasts are fed "
                + "separately, by what they themselves spent in the fight.");

            Telling = config.Bind("Toll", "Telling", false,
                "Write out each division of a kill's worth.");
        }

        /// <summary>What this kill is worth to a man of that level.</summary>
        internal static float Worth(int dead, int mine)
        {
            float much = 1f + (dead - mine) * Step.Value;
            return Mathf.Clamp(much, Floor.Value, Ceiling.Value);
        }
    }

    // Делёж опыта за убитого. Считаем сами и игровой счёт не пускаем: он не знает ни про
    // разницу уровней, ни про общую долю.
    [HarmonyPatch(typeof(PartyManager), "ShareExpGain")]
    internal static class Toll_Share_Patch
    {
        private static bool Prefix(PartyManager __instance, UnitAttribute dead)
        {
            if (Toll.Enabled == null || !Toll.Enabled.Value) return true;

            try
            {
                if (dead == null || dead.inParty) return false;

                var party = __instance.partyMembers;
                if (party == null || party.Count == 0) return true;

                HumaniodUnit man = dead as HumaniodUnit;

                int power = man != null && man.Data != null
                    ? man.Data.power
                    : (dead.info != null ? dead.info.power : 0);

                int level = man != null && man.Data != null
                    ? man.Data.level
                    : (dead.info != null ? dead.info.level : 1);

                // Звери отряда — такие же его бойцы и едят из того же котла. Оттого они
                // входят и в число едоков: взять волка значит подвинуться.
                var pack = Toll.Beasts.Value
                    ? Taming.Pack()
                    : new System.Collections.Generic.List<UnitAttribute>();

                int mouths = party.Count + pack.Count;
                if (mouths <= 0) return true;

                // Игровая мерка: чем больше отряд, тем больше общий котёл, но на едока
                // меньше. Оставляем её как есть — она про делёж, а не про цену головы.
                float pot = power * (0.5f + 0.25f * mouths) / mouths;
                pot *= Toll.Share.Value;

                foreach (UnitAttribute beast in pack)
                {
                    if (beast == null || beast.Data == null) continue;

                    float mine = pot * Toll.Worth(level, beast.Data.level);
                    Taming.Fed(beast, Mathf.Max(1f, mine));

                    if (Toll.Telling.Value)
                    {
                        ItemForgePlugin.Log.LogInfo($"Опыт: за «{dead.Data?.unitname}» "
                            + $"ур. {level} зверю ур. {beast.Data.level} — {mine:0}.");
                    }
                }

                foreach (HumaniodUnit mine in party)
                {
                    if (mine == null || mine.Data == null) continue;

                    float much = pot * Toll.Worth(level, mine.Data.level);

                    if (mine.talentmanger != null && mine.talentmanger.ContainTrait("Bellicose"))
                    {
                        much *= 1.1f;
                    }

                    int paid = Mathf.Max(1, Mathf.RoundToInt(much));
                    mine.GainExp(paid);

                    if (Toll.Telling.Value)
                    {
                        ItemForgePlugin.Log.LogInfo($"Опыт: за «{dead.Data?.unitname}» "
                            + $"ур. {level} бойцу ур. {mine.Data.level} — {paid} "
                            + $"(мощь {power}, доля {Toll.Worth(level, mine.Data.level):0.00}).");
                    }
                }

                return false;
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог разделить опыт: " + e.Message);
                return true;
            }
        }
    }
}
