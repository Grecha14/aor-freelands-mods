using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using spell;
using UnityEngine;

namespace DemonLook
{
    /// <summary>
    /// The night is the demon side: he is harder to see in it, and the Veil hides him almost wholly.
    ///
    /// Ночью демона видят хуже, чем прочих: с семи десятых того, откуда увидели бы человека.
    /// «Вуаль тьмы» — его собственное заклинание на месте «Щита тьмы»: кастуется отдельно и
    /// держится до первого врага, который его заметит. Пока она на нём, ночью в режиме
    /// скрытности его видно на девяносто процентов хуже.
    ///
    /// И город ночью не весь спит: пятнадцать из сотни горожан бродят до утра — у каждого своя
    /// ночь без сна, по жребию на эту ночь. Это те, кто может увидеть демона над спящим.
    ///
    /// Всё это — только когда отряд ведёт демон. Остальным «Щит тьмы» остаётся щитом.
    /// </summary>
    internal static class Veil
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Hide;
        internal static ConfigEntry<float> Night;
        internal static ConfigEntry<float> Walkers;
        internal static ConfigEntry<int> Step;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Veil", "Enabled", true,
                "Let the night hide the demon, give him the Veil of Darkness in place of the Dark "
                + "Shield, and keep some townsfolk awake at night.");

            Hide = config.Bind("Veil", "Hide", 0.9f,
                new ConfigDescription("How much harder the veiled demon is seen at night while sneaking, as a share.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Night = config.Bind("Veil", "Night", 0.7f,
                new ConfigDescription("How far the demon is seen at night, against anyone else, as a share.",
                    new AcceptableValueRange<float>(0.05f, 1f)));

            Walkers = config.Bind("Veil", "Walkers", 0.15f,
                new ConfigDescription("The share of townsfolk who stay awake and wander at night.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Step = config.Bind("Veil", "Step", 1,
                new ConfigDescription("The step of the demon dark school at which the veil comes to him: "
                    + "the first, at the tenth soul, making it his second spell after the Soul Feast.",
                    new AcceptableValueRange<int>(1, 5)));
        }

        internal const int VeilId = 907102;
        private const string ShieldName = "DarkShield";

        private static UISpellInfo veil;
        private static UISpellInfo shield;

        internal static UISpellInfo VeilSpell() { Feast.Register(); return veil; }
        internal static UISpellInfo ShieldSpell() { Feast.Register(); return shield; }

        internal static bool IsVeil(UISpellInfo s)
        {
            return s != null && (s.ID == VeilId || (veil != null && (object)s == (object)veil));
        }

        internal static bool IsShieldOriginal(UISpellInfo s)
        {
            return s != null && s.ID != VeilId && s.name == ShieldName;
        }

        /// <summary>The school step the veil opens at: the first, so it is his second spell.</summary>
        internal static int Needs()
        {
            return Step != null ? Mathf.Clamp(Step.Value, 1, 5) : 1;
        }

        /// <summary>Makes the demon copy of the Dark Shield, beside the Soul Feast.</summary>
        internal static void Register(UISpellDatabase db, List<UISpellInfo> all)
        {
            UISpellInfo original = db.GetByName(ShieldName);
            if (original == null)
            {
                DemonLookPlugin.Log.LogWarning("Вуаль тьмы: «Щита тьмы» нет в книге.");
                return;
            }

            shield = original;

            UISpellInfo copy = db.GetByID(VeilId);
            if (copy == null)
            {
                copy = UnityEngine.Object.Instantiate(original);
                copy.name = "DemonVeil";
                copy.ID = VeilId;
                all.Add(copy);
            }

            copy.Name = "Вуаль тьмы";
            copy.description = "Ночью в режиме скрытности делает демона на 90% менее заметным. "
                + "Держится до первого врага, который его заметит.";
            copy.DescriptionParam = new List<DecriptionParam>();

            veil = copy;
        }

        // ------------------------------------------------------------------ вуаль

        private const string CardId = "DemonLookVeil";

        private static UIBuffInfo Card()
        {
            return Souls.Card(CardId, "Вуаль тьмы",
                $"Ночью в режиме скрытности демона видно на {Mathf.RoundToInt(Hide.Value * 100f)}% хуже. "
                + "Спадёт, как только его заметит враг.",
                veil != null ? veil.Icon : null, true);
        }

        /// <summary>After a load the veil is still on him: its sign goes back on too.</summary>
        internal static void Keep(HumaniodUnit demon)
        {
            if (demon == null || demon.buffmanger == null) return;

            try
            {
                if (Souls.Veiled)
                {
                    if (!demon.buffmanger.ContainBuff(CardId))
                    {
                        UIBuffInfo card = Card();
                        if (card != null) demon.buffmanger.AddBuff(new BuffBase(card, demon));
                    }
                }
                else if (demon.buffmanger.ContainBuff(CardId))
                {
                    demon.buffmanger.RemoveBuff(CardId);
                }
            }
            catch
            {
            }
        }

        internal static bool Dark()
        {
            try { return TimeManager.Instance != null && TimeManager.Instance.IsNight && !(bool)WorldTravelManager.instance; }
            catch { return false; }
        }

        internal static void Cast(UnitAttribute unit, SpellBase cast)
        {
            Feast.EndLater(unit);

            if (!Racial.IsDemon(unit)) return;

            float cost = cast.theSpell.EPCost(unit);
            if (!unit.CostMP(cost))
            {
                Souls.Say("Не хватает маны на вуаль.");
                return;
            }

            Souls.Veiled = true;
            Keep(unit as HumaniodUnit);

            try { cast.StartCD(); } catch { }
            try { if (unit.lifebar != null) unit.lifebar.ShowTextTag("<color=#6A5A8C>Вуаль тьмы</color>", 2f); } catch { }

            Souls.Say("Вуаль тьмы: ночью в скрытности вас почти не видно — до первого врага, что заметит.");
        }

        internal static void Sensed(UnitAttribute who, UnitAttribute whom)
        {
            if (whom == null || who == null || !Racial.IsDemon(whom) || !Souls.Veiled) return;

            bool enemy;
            try { enemy = FactionManager.Instance.IsEnemy(who, whom); }
            catch { enemy = false; }
            if (!enemy) return;

            Souls.Veiled = false;
            try { if (whom.buffmanger != null && whom.buffmanger.ContainBuff(CardId)) whom.buffmanger.RemoveBuff(CardId); } catch { }
            Souls.Say("Вуаль сорвана: вас заметили.");
        }

        // ------------------------------------------------------------------ бодрствующие ночью

        /// <summary>Whether this townsman stays awake tonight: his own lot, drawn anew each night.</summary>
        internal static bool Walker(int id)
        {
            float share = Walkers.Value;
            if (share <= 0f) return false;
            if (share >= 1f) return true;

            int night = TimeManager.TotalDay - (TimeManager.Hour < 12 ? 1 : 0);

            unchecked
            {
                int h = id * 73856093 ^ night * 19349663;
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return (h & 0x7fffffff) % 1000 < Mathf.RoundToInt(share * 1000f);
            }
        }
    }

    // Ночью демона видно хуже, под вуалью в скрытности — почти не видно.
    [HarmonyPatch(typeof(UnitAttribute), "SeeAvoidanceMD", MethodType.Getter)]
    internal static class SeeAvoidance_Veil_Patch
    {
        private static void Postfix(UnitAttribute __instance, ref float __result)
        {
            if (Veil.Enabled == null || !Veil.Enabled.Value || __instance == null) return;
            if (!Racial.IsDemon(__instance) || !Veil.Dark()) return;

            __result *= Veil.Night.Value;
            if (__instance.isCrouching && Souls.Veiled) __result *= Mathf.Clamp01(1f - Veil.Hide.Value);
        }
    }

}
