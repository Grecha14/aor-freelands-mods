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
    /// A mage among the mercenaries of every tavern, at five times the wage.
    ///
    /// Раз в неделю таверна набирает своих наёмников заново — четверых. Теперь один из них маг:
    /// со своей школой магии, одной, и в одежде мага. Стоит он в найме как всякий — по своему
    /// снаряжению и силе, — а в содержании впятеро дороже простого наёмника: пятьдесят медных в
    /// день за каждый его уровень у простого, двести пятьдесят у мага. Маги в этой игре не
    /// бывают новичками: самый младший из них — третьего ранга.
    /// </summary>
    internal static class Magi
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> PerTavern;
        internal static ConfigEntry<int> Wage;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Magi", "Enabled", true,
                "Let a mage stand among the mercenaries of every tavern, at a higher wage.");

            PerTavern = config.Bind("Magi", "PerTavern", 1,
                new ConfigDescription("How many of a tavern's mercenaries of the week are mages.", new AcceptableValueRange<int>(0, 10)));

            Wage = config.Bind("Magi", "Wage", 5,
                new ConfigDescription("How many times an ordinary mercenary's wage a mage asks.", new AcceptableValueRange<int>(1, 20)));
        }

        internal static bool On()
        {
            return Enabled != null && Enabled.Value;
        }

        internal static bool Magic(SkillSet s)
        {
            int n = (int)s;
            return n >= 101 && n < 200;
        }

        /// <summary>The tavern has just taken on its mercenaries for the week: the last of them makes way for a mage.</summary>
        internal static void Hire(MercenarySpawner spawner)
        {
            if (!On() || spawner == null || spawner.mercenarySpawned == null) return;

            List<NPCSaveData> hired = spawner.mercenarySpawned;
            int want = Mathf.Min(PerTavern.Value, hired.Count);
            int have = hired.FindAll(n => n != null && n.isMagician).Count;

            for (; have < want; have++)
            {
                int slot = hired.FindLastIndex(n => n != null && !n.isMagician);
                if (slot < 0) break;

                NPCSaveData mage = HeroUnitMaker.Create((HeroRankTitle)UnityEngine.Random.Range(1, 4), spawner.mercernaryFaction, HeroType.Mage);
                if (mage == null) break;

                OneSchool(mage);
                if (mage.equips == null || mage.equips.Length < 5 || mage.equips[4] == null) HeroUnitMaker.UpgradeEquips(mage);
                if (mage.career != CareerType.Mercenary) HeroUnitMaker.StartCareer(mage, CareerType.Mercenary);

                // Кого маг сменил, тот уходит к прочим героям мира — как всякий, кого за неделю не наняли.
                NPCSaveData freeman = hired[slot];
                hired[slot] = mage;
                HeroUnitMaker.RetireHero(freeman);

                DemonLookPlugin.Log.LogInfo($"Таверна: среди наёмников маг «{mage.unitname}», уровень {mage.level}, "
                    + $"содержание {HeroUnitMaker.GetMercSalary(mage)} медных в день.");
            }
        }

        /// <summary>Keeps a character to the first school of magic he has: the rest go, with their spells and their mastery.</summary>
        internal static int OneSchool(NPCSaveData nsd)
        {
            if (nsd == null || nsd.skillSet == null) return 0;

            bool first = true;
            List<SkillSet> dropped = new List<SkillSet>();
            foreach (SkillSet s in nsd.skillSet)
            {
                if (!Magic(s)) continue;
                if (first) { first = false; continue; }
                dropped.Add(s);
            }
            if (dropped.Count == 0) return 0;

            nsd.skillSet.RemoveAll(s => dropped.Contains(s));

            if (nsd.spells != null && UISpellDatabase.Instance != null)
            {
                nsd.spells.RemoveAll(sp =>
                {
                    UISpellInfo info = sp != null ? UISpellDatabase.Instance.GetByID(sp.id) : null;
                    return info != null && dropped.Contains(info.SkillSet);
                });
            }

            if (nsd.talents != null && UITalentDatabase.Instance != null)
            {
                nsd.talents.RemoveAll(t =>
                {
                    UITalentInfo info = t != null ? UITalentDatabase.Instance.GetByID(t.id) : null;
                    return info != null && dropped.Contains(info.talentClass);
                });
            }

            try { nsd.CalculatePower(); } catch { }
            return dropped.Count;
        }
    }

    [HarmonyPatch(typeof(MercenarySpawner), "Spawn")]
    internal static class Spawn_Magi_Patch
    {
        private static void Postfix(MercenarySpawner __instance)
        {
            try { Magi.Hire(__instance); }
            catch (Exception e) { DemonLookPlugin.Log.LogWarning("Таверна: мага не нашлось: " + e.Message); }
        }
    }

    // Маг в найме впятеро дороже: и в окне найма, и в дневном жалованье отряда.
    [HarmonyPatch(typeof(HeroUnitMaker), "GetMercSalary")]
    internal static class Salary_Magi_Patch
    {
        private static void Postfix(NPCSaveData merc, ref int __result)
        {
            if (!Magi.On() || merc == null || !merc.isMagician) return;
            __result *= Magi.Wage.Value;
        }
    }
}
