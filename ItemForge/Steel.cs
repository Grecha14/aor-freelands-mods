using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using spell;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Against cutting, crushing and thrusting only the armour answers.
    ///
    /// Удар по телу решает вычет брони: сколько железа пробито и сколько прошло сквозь него.
    /// Сопротивления «рубящему», «дробящему» и «колющему» — проценты старого счёта, которых в
    /// бою больше нет, — всё ещё лежали на вещах, в заклинаниях и талантах и честно писались в
    /// подсказках: «+2% сопротивление режущему урону», «снижает сопротивление резанию на 30%».
    /// Ничего не делая, они обещали. Здесь они снимаются отовсюду.
    /// </summary>
    internal static class Steel
    {
        internal static ConfigEntry<bool> Enabled;

        private const int CorruptingTouch = 237;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Steel", "Enabled", true,
                "Take resistance to cutting, crushing and thrusting off every item, effect, spell "
                + "and talent. Against those only the armour deduction answers now.");
        }

        internal static bool Physical(AddonAttribute type)
        {
            int n = (int)type;
            return n >= 100 && n <= 102;
        }

        /// <summary>Takes the three physical resistances out of a list; how many went.</summary>
        internal static int Strip(List<AddonAttributes> list)
        {
            if (Enabled == null || !Enabled.Value || list == null) return 0;
            return list.RemoveAll(a => a != null && Physical(a.type));
        }

        private static bool done;

        /// <summary>Strips the catalogue, once, and whatever already walks the world.</summary>
        internal static void Catalogue()
        {
            if (done || Enabled == null || !Enabled.Value) return;

            try
            {
                UIItemDatabase items;
                UIBuffDatabase buffs;
                UITalentDatabase talents;

                try
                {
                    items = UIItemDatabase.Instance;
                    buffs = UIBuffDatabase.Instance;
                    talents = UITalentDatabase.Instance;
                }
                catch
                {
                    return;
                }

                if (items == null || items.items == null || buffs == null || talents == null) return;

                done = true;

                int things = 0, cards = 0, gifts = 0;

                foreach (UIItemInfo any in items.items)
                {
                    UIEquipmentInfo gear = any as UIEquipmentInfo;
                    if (gear != null && Strip(gear.addAttrs) > 0) things++;
                }

                foreach (UIBuffInfo[] set in new UIBuffInfo[][] { buffs.buffs,
                    buffs.injuryBuffs != null ? buffs.injuryBuffs.ToArray() : null,
                    buffs.damageBuffs != null ? buffs.damageBuffs.ToArray() : null })
                {
                    if (set == null) continue;
                    foreach (UIBuffInfo card in set)
                    {
                        if (card != null && Strip(card.addAttrs) > 0) cards++;
                    }
                }

                foreach (UITalentInfo[] set in new UITalentInfo[][] { talents.talents,
                    talents.masteryTalents, talents.traits, talents.inbornTraits })
                {
                    if (set == null) continue;
                    foreach (UITalentInfo gift in set)
                    {
                        if (gift != null && Strip(gift.addAttrs) > 0) gifts++;
                    }
                }

                // Заклинания держат свои карточки и мимо общего списка.
                UISpellDatabase book = null;
                try { book = UISpellDatabase.Instance; } catch { }

                if (book != null && book.spells != null)
                {
                    foreach (UISpellInfo spell in book.spells)
                    {
                        if (spell == null) continue;

                        foreach (UIBuffInfo card in Spells.CardsOf(spell))
                        {
                            if (card != null && Strip(card.addAttrs) > 0) cards++;
                        }

                        if (spell.ID == CorruptingTouch) Reword(spell);
                    }
                }

                ItemForgePlugin.Log.LogInfo($"Сопротивления рубящему, дробящему и колющему сняты: "
                    + $"вещей {things}, эффектов {cards}, талантов {gifts}.");

                Sweep();
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог снять сопротивления железу: " + e);
            }
        }

        /// <summary>What is already laid on the living was laid before the catalogue was stripped.</summary>
        private static void Sweep()
        {
            try
            {
                foreach (UnitAttribute unit in UnityEngine.Object.FindObjectsOfType<UnitAttribute>())
                {
                    if (unit == null) continue;

                    int gone = 0;

                    if (unit.talentmanger != null && unit.talentmanger.talents != null)
                    {
                        foreach (TalentBase one in unit.talentmanger.talents)
                        {
                            if (one != null) gone += Strip(one.addAttrs);
                        }
                    }

                    if (unit.buffmanger != null && unit.buffmanger.buffs != null)
                    {
                        foreach (BuffBase one in unit.buffmanger.buffs)
                        {
                            if (one != null) gone += Strip(one.addAttrs);
                        }
                    }

                    if (gone > 0) unit.UpdateAttribute();
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Corrupting Touch no longer promises a cut in resistances it cannot make.
        ///
        /// В подсказке три числа: насколько режется урон, насколько сопротивления, и сколько
        /// длится. Второго больше нет — остаются первое и третье, в той же записи, в какой их
        /// писала игра.
        /// </summary>
        private static void Reword(UISpellInfo spell)
        {
            try
            {
                string key = spell.description;
                string was = Tongue.Get(key);
                if (string.IsNullOrEmpty(was)) return;

                MatchCollection marks = Regex.Matches(was, @"\{\[?\d+\]?\}");
                if (marks.Count < 3)
                {
                    ItemForgePlugin.Log.LogInfo("«Губительное прикосновение»: подсказку не узнал, оставил: «" + was + "».");
                    return;
                }

                string hurt = marks[0].Value;
                string lasts = marks[marks.Count - 1].Value;

                Tongue.Put(key, "Используйте тёмную силу, чтобы разрушить оружие и броню врага, "
                    + "снижая их урон в ближнем, дальнем бою и магический урон на " + hurt
                    + "% в течение " + lasts + " секунд.");

                ItemForgePlugin.Log.LogInfo("«Губительное прикосновение»: из подсказки убрано "
                    + "сопротивление железу. Было: «" + was + "».");
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(TalentBase), "CalculateTalentLevel")]
    internal static class CalculateTalentLevel_Steel_Patch
    {
        private static void Postfix(TalentBase __instance)
        {
            try { if (__instance != null) Steel.Strip(__instance.addAttrs); } catch { }
        }
    }

    [HarmonyPatch(typeof(BuffBase), "CalculateBuffLevel")]
    internal static class CalculateBuffLevel_Steel_Patch
    {
        private static void Postfix(BuffBase __instance)
        {
            try { if (__instance != null) Steel.Strip(__instance.addAttrs); } catch { }
        }
    }

    [HarmonyPatch(typeof(BuffManager), "AddBuff", new Type[] { typeof(BuffBase) })]
    internal static class AddBuff_Steel_Patch
    {
        private static void Prefix(BuffBase buff)
        {
            try { if (buff != null) Steel.Strip(buff.addAttrs); } catch { }
        }
    }
}
