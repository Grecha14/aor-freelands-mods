using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using spell;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// A potion weighs what its water weighs, and the injury killer no longer kills injuries.
    ///
    /// Склянка весила в игре килограмм, полтора и два — больше доброго кинжала. Здесь она весит
    /// свою воду: большая полкило, средняя триста граммов, малая сто. Мази лечения и бывший
    /// «Убийца травм» — по той же шкале.
    ///
    /// Травмы теперь лечит только медицина: лекарь, полевой лекарь в лагере, лазарет. Бывший
    /// «Убийца травм» травму не снимает, он глушит боль. «Отвар ивовой коры» четыре часа вдвое
    /// ослабляет всё, что травмы отнимают; «Маковая настойка» на шесть часов и «Маковое молоко»
    /// на двенадцать гасят их вовсе. Сама травма никуда не девается, и счёт до гибели тот же.
    /// Кончилось действие — приходит ломота: два часа без десяти очков морали.
    /// </summary>
    internal static class Anodyne
    {
        internal static ConfigEntry<bool> Weights;
        internal static ConfigEntry<string> Sizes;
        internal static ConfigEntry<bool> Painkiller;
        internal static ConfigEntry<int> Ache;
        internal static ConfigEntry<float> AcheHours;

        internal static void Bind(ConfigFile config)
        {
            Weights = config.Bind("Anodyne", "Weights", true,
                "Let a potion weigh what its water weighs, by its size.");

            Sizes = config.Bind("Anodyne", "Sizes", "0.5,0.3,0.1",
                "Kilograms for a large, a medium and a small potion. Healing salves and the old "
                + "injury killer (its first, second and third tier) go by the same scale.");

            Painkiller = config.Bind("Anodyne", "Painkiller", true,
                "Turn the injury killer into a painkiller: it no longer cures injuries, it only dulls "
                + "them for a while. Injuries are cured by medicine alone.");

            Ache = config.Bind("Anodyne", "Ache", 10,
                new ConfigDescription("Morale lost while the ache lasts after a painkiller wears off.",
                    new AcceptableValueRange<int>(0, 100)));

            AcheHours = config.Bind("Anodyne", "AcheHours", 2f,
                new ConfigDescription("Game hours the ache lasts.",
                    new AcceptableValueRange<float>(0f, 48f)));
        }

        private static readonly string[] killers =
        {
            "items_Consumables_Potions_Useless_InjuryKiller",
            "items_Consumables_Potions_Useless_InjuryKiller_T2",
            "items_Consumables_Potions_Useless_InjuryKiller_T3",
        };

        private static readonly string[] titles = { "Отвар ивовой коры", "Маковая настойка", "Маковое молоко" };
        private static readonly float[] hours = { 4f, 6f, 12f };
        private static readonly float[] relief = { 0.5f, 1f, 1f };

        private static string CardId(int tier) { return "ItemForgePainkiller" + (tier + 1); }
        private const string AcheId = "ItemForgeAche";

        private static string Telling(int tier)
        {
            string how = relief[tier] >= 0.999f
                ? "боль от травм не чувствуется вовсе"
                : "всё, что отнимают травмы, вдвое слабее";
            return $"Глушит боль, но не лечит: {hours[tier]:0} ч {how}. Сама травма остаётся, "
                + "вылечит её только лекарь. Когда действие пройдёт, начнётся ломота: "
                + $"мораль −{Ache.Value} на {AcheHours.Value:0.#} ч.";
        }

        // ------------------------------------------------------------------ имена

        private static readonly Dictionary<string, string> words = new Dictionary<string, string>(StringComparer.Ordinal);

        internal static bool TryWord(string key, out string text)
        {
            text = null;
            return key != null && words.Count > 0 && words.TryGetValue(key, out text);
        }

        // ------------------------------------------------------------------ вес

        private static bool weighed;

        private static float[] ReadSizes()
        {
            float[] got = { 0.5f, 0.3f, 0.1f };
            string[] parts = (Sizes.Value ?? "").Split(',');
            for (int i = 0; i < got.Length && i < parts.Length; i++)
            {
                float v;
                if (float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= 0f) got[i] = v;
            }
            return got;
        }

        private static int Killer(string name)
        {
            for (int i = 0; i < killers.Length; i++)
            {
                if (string.Equals(name, killers[i], StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        private static int Rung(string name)
        {
            if (name.EndsWith("_Large", StringComparison.Ordinal)) return 0;
            if (name.EndsWith("_Medium", StringComparison.Ordinal)) return 1;
            if (name.EndsWith("_Small", StringComparison.Ordinal)) return 2;
            return -1;
        }

        /// <summary>Weighs the potions by size and renames the old injury killer, once.</summary>
        internal static void Weigh()
        {
            if (weighed) return;

            UIItemDatabase db;
            try { db = UIItemDatabase.Instance; }
            catch { return; }
            if (db == null || db.items == null || db.items.Length == 0) return;

            weighed = true;

            try
            {
                float[] size = ReadSizes();
                int changed = 0;

                foreach (UIItemInfo thing in db.items)
                {
                    UIConsumableInfo drink = thing as UIConsumableInfo;
                    if (drink == null || drink.goodsType != GoodsType.Potion || string.IsNullOrEmpty(drink.Name)) continue;

                    int tier = Killer(drink.Name);
                    if (tier >= 0 && Painkiller.Value)
                    {
                        words[drink.Name] = titles[tier];
                        if (!string.IsNullOrEmpty(drink.Description)) words[drink.Description] = Telling(tier);
                    }

                    int rung = tier >= 0 ? tier : Rung(drink.Name);
                    if (!Weights.Value || rung < 0) continue;

                    if (!Mathf.Approximately(drink.weight, size[rung]))
                    {
                        drink.weight = size[rung];
                        changed++;
                    }
                }

                ItemForgePlugin.Log.LogInfo($"Зелья по весу воды: {changed} склянок.");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Зелья: не смог перевесить: " + e);
            }
        }

        // ------------------------------------------------------------------ обезболивающее

        private static readonly UIBuffInfo[] cards = new UIBuffInfo[3];
        private static UIBuffInfo ache;
        private static bool carded;

        private static UIBuffInfo Card(string id, string name, string description, bool good, float lasts, UIBuffInfo look)
        {
            UIBuffDatabase db = UIBuffDatabase.Instance;
            UIBuffInfo card = db.GetByID(id);

            if (card == null)
            {
                UIBuffInfo pattern = db.GetByID("WeightDebuff");
                if (pattern == null) return null;

                card = UnityEngine.Object.Instantiate(pattern);
                card.name = id;
                card.id = id;

                List<UIBuffInfo> all = new List<UIBuffInfo>(db.buffs);
                all.Add(card);
                db.buffs = all.ToArray();
            }

            card.buffname = name;
            card.description = description;
            card.type = good ? bufftype.positive : bufftype.negative;
            card.isVisible = true;
            card.showDuration = true;
            card.canForceRemove = false;
            card.immuneDispel = true;
            card.durationIsHour = true;
            card.duration = lasts;
            if (card.addAttrs != null) card.addAttrs.Clear();
            if (look != null && look.icon != null) card.icon = look.icon;
            return card;
        }

        /// <summary>Makes the cards as early as the buff book exists, so a save finds them on load.</summary>
        internal static void Register()
        {
            if (carded || Painkiller == null || !Painkiller.Value) return;

            UIBuffDatabase db;
            try { db = UIBuffDatabase.Instance; }
            catch { return; }
            if (db == null || db.buffs == null || db.buffs.Length == 0) return;

            carded = true;

            try
            {
                UIBuffInfo look = db.GetByID("InjuryKillerBuff_T1");
                for (int t = 0; t < 3; t++)
                    cards[t] = Card(CardId(t), titles[t], Telling(t), true, hours[t], look);

                ache = Card(AcheId, "Ломота",
                    $"Обезболивающее прошло, и тело ломит: мораль −{Ache.Value}.", false, AcheHours.Value, null);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Обезболивающее: не смог завести знаки: " + e);
            }
        }

        private static readonly HashSet<UnitAttribute> numbed = new HashSet<UnitAttribute>();
        private static readonly Dictionary<UnitAttribute, float> aching = new Dictionary<UnitAttribute, float>();

        private static bool Has(UnitAttribute u, string id)
        {
            try { return u != null && u.buffmanger != null && u.buffmanger.ContainBuff(id); }
            catch { return false; }
        }

        /// <summary>How much of what the injuries take is dulled right now, from 0 to 1.</summary>
        internal static float Relief(UnitAttribute u)
        {
            if (numbed.Count == 0 || u == null || !numbed.Contains(u)) return 0f;

            float most = 0f;
            for (int t = 0; t < 3; t++)
            {
                if (relief[t] > most && Has(u, CardId(t))) most = relief[t];
            }
            return most;
        }

        /// <summary>Drunk: the pain goes, the injury stays.</summary>
        internal static void Numb(UnitAttribute u, int tier)
        {
            Register();
            if (u == null || u.buffmanger == null || cards[tier] == null) return;

            try
            {
                for (int t = 0; t < 3; t++)
                {
                    if (Has(u, CardId(t))) u.buffmanger.RemoveBuff(CardId(t));
                }

                BuffBase dose = new BuffBase(cards[tier], u);
                dose.duration = hours[tier];
                u.buffmanger.AddBuff(dose);
                numbed.Add(u);
                u.UpdateAttribute();

                if (u.inParty && u.lifebar != null) u.lifebar.ShowTextTag("<color=#C8E6C9>Боль отступает</color>", 2f);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Обезболивающее: не легло: " + e.Message);
            }
        }

        private static float next;
        private static readonly List<UnitAttribute> gone = new List<UnitAttribute>();

        internal static void Tick()
        {
            if (Painkiller == null || !Painkiller.Value) return;
            Register();

            float now = Time.unscaledTime;
            if (now < next) return;
            next = now + 2f;

            // После загрузки: на ком знак лежит, тех и считаем.
            try
            {
                if (PartyManager.instance != null && PartyManager.instance.partyMembers != null)
                {
                    foreach (HumaniodUnit m in PartyManager.instance.partyMembers)
                    {
                        if (m == null) continue;
                        if (!numbed.Contains(m))
                        {
                            for (int t = 0; t < 3; t++)
                            {
                                if (Has(m, CardId(t))) { numbed.Add(m); m.UpdateAttribute(); break; }
                            }
                        }
                        if (!aching.ContainsKey(m) && Has(m, AcheId)) aching[m] = Ache.Value;
                    }
                }
            }
            catch
            {
            }

            gone.Clear();
            foreach (UnitAttribute u in numbed)
            {
                if (u == null || u.Data == null || u.Data.isdead) { gone.Add(u); continue; }

                bool still = false;
                for (int t = 0; t < 3; t++) still |= Has(u, CardId(t));
                if (still) continue;

                gone.Add(u);
                try { u.UpdateAttribute(); } catch { }
                Hurt(u);
            }
            foreach (UnitAttribute u in gone) numbed.Remove(u);

            gone.Clear();
            foreach (KeyValuePair<UnitAttribute, float> one in aching)
            {
                UnitAttribute u = one.Key;
                if (u == null || u.Data == null || u.Data.isdead) { gone.Add(u); continue; }
                if (Has(u, AcheId)) continue;

                gone.Add(u);
                try { NPCSaveData npc = u.Data as NPCSaveData; if (npc != null) npc.AddMorale(one.Value); } catch { }
            }
            foreach (UnitAttribute u in gone) aching.Remove(u);
        }

        private static void Hurt(UnitAttribute u)
        {
            NPCSaveData npc = u.Data as NPCSaveData;
            if (ache == null || Ache.Value <= 0 || AcheHours.Value <= 0f || npc == null) return;

            try
            {
                float before = npc.morale;
                npc.AddMorale(-Ache.Value);
                float lost = Mathf.Max(0f, before - npc.morale);

                if (Has(u, AcheId)) u.buffmanger.RemoveBuff(AcheId);
                BuffBase b = new BuffBase(ache, u);
                b.duration = AcheHours.Value;
                u.buffmanger.AddBuff(b);

                float had;
                aching[u] = (aching.TryGetValue(u, out had) ? had : 0f) + lost;

                if (u.inParty && u.lifebar != null) u.lifebar.ShowTextTag("<color=#E0B0B0>Ломота</color>", 2f);
            }
            catch
            {
            }
        }
    }

    // Бывший «Убийца травм» травму не снимает — глушит боль.
    [HarmonyPatch(typeof(BuffManager), "OnNewBuff")]
    internal static class NewBuff_Anodyne_Patch
    {
        private static bool Prefix(BuffBase buff, UnitAttribute unit)
        {
            if (Anodyne.Painkiller == null || !Anodyne.Painkiller.Value || buff == null || buff.id == null) return true;
            if (buff.id.IndexOf("InjuryKillerBuff", StringComparison.Ordinal) < 0) return true;

            int tier = buff.id.EndsWith("T3", StringComparison.Ordinal) ? 2 : buff.id.EndsWith("T2", StringComparison.Ordinal) ? 1 : 0;
            try { Anodyne.Numb(unit, tier); } catch { }
            return false;
        }
    }

    // Пока боль заглушена, травмы отнимают меньше — или ничего.
    [HarmonyPatch(typeof(UnitAttribute), "CountBuffsBonus")]
    internal static class BuffsBonus_Anodyne_Patch
    {
        private static bool Prefix(UnitAttribute __instance)
        {
            float dulled = Anodyne.Relief(__instance);
            if (dulled <= 0f) return true;

            try
            {
                if (!__instance.Data.buffsInited) return false;

                foreach (BuffBase buff in __instance.buffmanger.buffs)
                {
                    if (buff == null) continue;
                    if (!buff.buffInfo.canStack) buff.stackNum = 1;

                    float factor = buff.stackNum;
                    if (buff.buffInfo.type == bufftype.injury) factor *= 1f - dulled;
                    if (factor != 0f) __instance.CountBonus(buff.addAttrs, factor);
                }
                return false;
            }
            catch
            {
                return true;
            }
        }
    }

    // Новые имена и описания склянок.
    [HarmonyPatch(typeof(gameManager), "LocalizedItemString")]
    internal static class ItemString_Anodyne_Patch
    {
        private static void Postfix(string s, ref string __result)
        {
            string text;
            if (Anodyne.TryWord(s, out text)) __result = text;
        }
    }
}
