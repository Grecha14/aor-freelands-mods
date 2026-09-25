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
    /// The mage: how his mana comes back, what his shield costs, what his staff holds.
    ///
    /// Мана возвращается так же, как здоровье: своё восстановление растянуто на час, а в бою
    /// почти стоит. Прежде здоровье поправлялось по-новому, а мана — по-игровому, секундами, и
    /// маг, отдышавшись за один бой, снова бил в полную силу.
    ///
    /// Магический щит принимает удар целиком и платит за него маной: две единицы маны за
    /// единицу удержанного урона. Кончилась мана — кончился щит.
    ///
    /// Посох и жезл — это ещё и запас: сверху к мане по ярусу, сто на первом и девятьсот на
    /// пятом. Цвет вещи этот запас не умножает: он от яруса, а не от приписок.
    /// </summary>
    internal static class Arcane
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> Breath;
        internal static ConfigEntry<float> ShieldTakes;
        internal static ConfigEntry<float> ShieldCost;
        internal static ConfigEntry<string> StaffMana;
        internal static ConfigEntry<string> StaffShares;
        internal static ConfigEntry<float> StaffSpread;
        internal static ConfigEntry<string> StaffNamed;
        internal static ConfigEntry<float> StaffPower;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Arcane", "Enabled", true,
                "Mana comes back the way health does, a mage shield takes the whole blow and pays "
                + "for it in mana, and staves and wands carry a store of mana by their tier.");

            Breath = config.Bind("Arcane", "Breath", true,
                "Cut the mage own mana regeneration exactly as health regeneration is cut: spread "
                + "over the hour, and all but stopped in a fight.");

            ShieldTakes = config.Bind("Arcane", "ShieldTakes", 1f,
                new ConfigDescription(
                    "What share of every blow a mage shield takes. One: all of it.",
                    new AcceptableValueRange<float>(0f, 1f)));

            ShieldCost = config.Bind("Arcane", "ShieldCost", 2f,
                new ConfigDescription(
                    "How much mana a mage shield spends on every point of damage it holds.",
                    new AcceptableValueRange<float>(0f, 20f)));

            StaffMana = config.Bind("Arcane", "StaffMana", "0,100,200,300,500,900",
                "How much mana a purple staff or wand adds, by tier from T0 to T5. The other "
                + "colours take their share of it below. Flat: the colour does not multiply the "
                + "affixes on top of this.");

            StaffShares = config.Bind("Arcane", "StaffShares", "0.1,0.1,0.2,0.5,1,1",
                "What share of that each colour gets, from poor to legendary: white a tenth, "
                + "green a fifth, blue a half, purple the whole.");

            StaffSpread = config.Bind("Arcane", "StaffSpread", 0.2f,
                new ConfigDescription(
                    "How far one staff may stray from its class, either way, as a share. A fifth: "
                    + "a purple staff of the first tier holds between 80 and 120. Drawn once for "
                    + "each staff and kept with it.",
                    new AcceptableValueRange<float>(0f, 0.9f)));

            StaffNamed = config.Bind("Arcane", "StaffNamed", "20000=1500,10000=1200,5000=900,0=700",
                "Named legendary staves and wands, by what they are worth: from this price up, "
                + "this much mana. The Messenger of the Night costs twenty thousand.");

            StaffPower = config.Bind("Arcane", "StaffPower", 0.001f,
                new ConfigDescription(
                    "How much stronger every spell grows for every point of mana the staff holds, "
                    + "as a share: a thousandth, so a hundred mana is a tenth more power.",
                    new AcceptableValueRange<float>(0f, 0.01f)));
        }

        // ----------------------------------------------------------------- щит

        private static bool done;

        /// <summary>Rewrites every mage shield, once.</summary>
        internal static void Catalogue()
        {
            if (done || Enabled == null || !Enabled.Value) return;

            try
            {
                UIBuffDatabase book;
                try { book = UIBuffDatabase.Instance; }
                catch { return; }

                if (book == null || book.buffs == null) return;

                done = true;
                int shields = 0;

                foreach (UIBuffInfo card in book.buffs)
                {
                    if (card == null || card.type != bufftype.magicShield || card.costStamina) continue;

                    card.absorbRate = Mathf.Clamp01(ShieldTakes.Value);
                    card.absorbRateLevelAlter = 0f;
                    card.manaCostPD = Mathf.Max(0f, ShieldCost.Value);
                    card.manaCostLevelAlter = 0f;
                    shields++;
                }

                ItemForgePlugin.Log.LogInfo($"Магические щиты: {shields}, принимают "
                    + $"{ShieldTakes.Value * 100f:0}% удара за {ShieldCost.Value:0.#} маны на единицу.");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог переписать магические щиты: " + e.Message);
            }
        }

        // ----------------------------------------------------------------- посох

        private static float[] Row(string written, ref float[] kept, ref string read)
        {
            written = written ?? "";
            if (kept != null && written == read) return kept;

            List<float> got = new List<float>();
            foreach (string one in written.Split(','))
            {
                float much;
                if (float.TryParse(one.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out much))
                {
                    got.Add(Mathf.Max(0f, much));
                }
            }

            read = written;
            kept = got.ToArray();
            return kept;
        }

        private static float[] tops, shares;
        private static string topsRead, sharesRead;

        /// <summary>Метка на посохе: во сколько раз этот посох отступил от своего класса.</summary>
        internal const AddonAttribute MarkSpread = (AddonAttribute)9104;

        /// <summary>True for the named legendaries, which carry a written figure instead of a ladder.</summary>
        private static bool Named(UIWeaponInfo blade)
        {
            return blade.isUnique || ((UIItemInfo)blade).Quality >= UIItemQuality.Legendary;
        }

        private static float NamedMana(UIWeaponInfo blade)
        {
            int price = ((UIItemInfo)blade).value;
            float best = 0f;
            int bestFrom = -1;

            foreach (string one in (StaffNamed.Value ?? "").Split(','))
            {
                int split = one.IndexOf('=');
                if (split <= 0) continue;

                int from;
                float much;
                if (!int.TryParse(one.Substring(0, split).Trim(), out from)) continue;
                if (!float.TryParse(one.Substring(split + 1).Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out much)) continue;

                if (price >= from && from > bestFrom)
                {
                    bestFrom = from;
                    best = much;
                }
            }

            return best;
        }

        /// <summary>This staff own draw within its class, made once and kept on it.</summary>
        private static float Drawn(Inventory thing)
        {
            if (thing.addAttrs == null) return 1f;

            foreach (AddonAttributes one in thing.addAttrs)
            {
                if (one != null && one.type == MarkSpread && one.value > 0f) return one.value;
            }

            float spread = Mathf.Clamp(StaffSpread.Value, 0f, 0.9f);
            float drawn = UnityEngine.Random.Range(1f - spread, 1f + spread);
            drawn = Mathf.Round(drawn * 100f) / 100f;

            thing.addAttrs.Add(new AddonAttributes(MarkSpread, drawn));
            return drawn;
        }

        /// <summary>How much mana this staff or wand adds, or nought for anything else.</summary>
        internal static float Store(Inventory thing)
        {
            if (Enabled == null || !Enabled.Value || thing == null) return 0f;

            UIWeaponInfo blade = thing.itemInfo as UIWeaponInfo;
            if (blade == null || !Wield.Wand(blade)) return 0f;

            if (Named(blade)) return NamedMana(blade);

            float[] top = Row(StaffMana.Value, ref tops, ref topsRead);
            float[] share = Row(StaffShares.Value, ref shares, ref sharesRead);

            int tier = (int)blade.tier;
            int colour = (int)thing.quality;
            if (tier < 0 || tier >= top.Length || colour < 0 || colour >= share.Length) return 0f;

            float much = top[tier] * share[colour];
            if (much <= 0f) return 0f;

            return Mathf.Round(much * Drawn(thing));
        }

        /// <summary>How much stronger spells grow with this staff in hand, as a share.</summary>
        internal static float Power(Inventory thing)
        {
            float mana = Store(thing);
            return mana > 0f ? mana * StaffPower.Value : 0f;
        }

        // Показ в подсказке: на время отрисовки к припискам вещи прибавляется строка маны.
        private static List<AddonAttributes> shownIn;
        private static AddonAttributes shown;
        private static AddonAttributes shownPower;

        internal static void Dress(UISlotBase slot)
        {
            Undress();

            try
            {
                if ((object)slot == null) return;

                Inventory thing = null;

                UIItemSlot bag = slot as UIItemSlot;
                if ((object)bag != null) thing = bag.inventory;
                else
                {
                    UIEquipSlot worn = slot as UIEquipSlot;
                    if ((object)worn != null) thing = worn.inventory;
                }

                float much = Store(thing);
                if (much <= 0f || thing.addAttrs == null) return;

                shown = new AddonAttributes(AddonAttribute.MP, much);
                shownPower = new AddonAttributes(AddonAttribute.MagicDamage, Power(thing));
                shownIn = thing.addAttrs;
                shownIn.Add(shown);
                if (shownPower.value > 0f) shownIn.Add(shownPower);
            }
            catch
            {
                Undress();
            }
        }

        internal static void Undress()
        {
            if (shownIn != null)
            {
                try
                {
                    if (shown != null) shownIn.Remove(shown);
                    if (shownPower != null) shownIn.Remove(shownPower);
                }
                catch
                {
                }
            }

            shownIn = null;
            shown = null;
            shownPower = null;
        }
    }

    // Своя мана в секунду идёт там же, где своё здоровье, и срезается тем же правилом.
    [HarmonyPatch(typeof(UnitAttribute), "RestoreMP")]
    internal static class RestoreMP_Arcane_Patch
    {
        private static void Prefix(UnitAttribute __instance, ref float restoreAmount)
        {
            if (Arcane.Enabled == null || !Arcane.Enabled.Value || !Arcane.Breath.Value) return;
            if (restoreAmount <= 0f) return;
            if ((object)Knit.Ticking != (object)__instance) return;

            try
            {
                restoreAmount *= Knit.Flowing(__instance);
            }
            catch
            {
            }
        }
    }

    // Запас посоха ложится в ту же ручку, из которой игра складывает предел маны.
    [HarmonyPatch(typeof(HumaniodUnit), "CountEquipmentsBonus")]
    internal static class CountEquipmentsBonus_Arcane_Patch
    {
        private static readonly AccessTools.FieldRef<UnitAttribute, float> mpmd =
            AccessTools.FieldRefAccess<UnitAttribute, float>("MPMD");

        private static void Postfix(HumaniodUnit __instance)
        {
            if (Arcane.Enabled == null || !Arcane.Enabled.Value || __instance == null) return;

            try
            {
                if (__instance.Data == null || !__instance.Data.equipsInited) return;

                EquipmentManager gear = __instance.equipmentmanger;
                if (gear == null || !gear.isInited || gear.equipInfos == null) return;

                float more = 0f;
                float power = 0f;

                for (int i = 0; i < 2 && i < gear.equipInfos.Length; i++)
                {
                    EquipInfo slot = gear.equipInfos[i];
                    if (slot == null || !slot.IsEquiped()) continue;

                    more += Arcane.Store(slot.inventory);
                    power += Arcane.Power(slot.inventory);
                }

                if (more > 0f) mpmd((UnitAttribute)(object)__instance) += more;
                if (power > 0f) __instance.MagicDamageMD += power;
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(UIItemTip), "SetValue")]
    [HarmonyPriority(Priority.Low)]
    internal static class ItemTip_Arcane_Patch
    {
        private static void Prefix(UISlotBase slot)
        {
            try { Arcane.Dress(slot); } catch { Arcane.Undress(); }
        }

        private static void Postfix()
        {
            Arcane.Undress();
        }
    }
}
