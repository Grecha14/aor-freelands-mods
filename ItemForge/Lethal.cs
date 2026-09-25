using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// What a blow does once it is through, and what it costs the blade.
    ///
    /// Броня у нас перемалывала каждый удар, отнимая долю. Модель на настоящих числах
    /// показала, куда это ведёт: двуручник доносил до тела закованного человека пять очков из
    /// ста шестидесяти, бой между двумя латниками растягивался на сотню ударов, а мастерство
    /// от первой ступени до девяносто девятой стоило двенадцать процентов — потому что решало
    /// только, куда попасть, а попадание никуда не вело.
    ///
    /// Здесь иначе, и проще. Броня решает **дошёл ли удар**: не дотянулся — не дошёл вовсе,
    /// дотянулся — прошёл почти целиком. А чем он кончится, решает место. Голова убивает
    /// вдвое с половиной быстрее корпуса, ноги не убивают никогда — они калечат.
    ///
    /// Отсюда и смысл всего, что было раньше. Мастерство покупает голову, голова покупает
    /// смерть с двух ударов. Доспех не отнимает по чуть-чуть, а не пускает. Молот хорош не
    /// потому, что бьёт сильнее, а потому что железо ему не помеха — и сам он о железо не
    /// тупится, тогда как клинок о латы садится за десяток ударов.
    /// </summary>
    internal static class Lethal
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Zones;
        internal static ConfigEntry<float> Spared;
        internal static ConfigEntry<float> Legs;
        internal static ConfigEntry<float> Grip;
        internal static ConfigEntry<float> Shock;
        internal static ConfigEntry<bool> Wears;
        internal static ConfigEntry<float> PerArmour;
        internal static ConfigEntry<float> Blunts;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Lethal", "Enabled", true,
                "Let the place a blow lands decide what it does. Armour then says whether the "
                + "blow arrives at all and the place says what it costs; the two used to be one "
                + "muddle in which neither mattered.");

            Zones = config.Bind("Lethal", "Zones", "head=2.5,chest=1,arms=0.5,pants=0.5",
                "What a blow is worth by where it lands, as a multiple. A head is worth two and "
                + "a half of a chest: with a two-hander that is death in two blows where the "
                + "body takes five. Legs and arms are worth half, and neither kills — a limb "
                + "holds no organ, and what it costs a man is reckoned below instead.");

            Grip = config.Bind("Lethal", "Grip", 1.5f,
                new ConfigDescription(
                    "What a blow to the arm costs in stamina, per point of harm it did. This is "
                    + "what arms are for in a fight: the blade that finds one takes little "
                    + "blood and much strength. A man whose stamina is spent cannot swing at "
                    + "all, and his shield breaks at the first blow that meets it.",
                    new AcceptableValueRange<float>(0f, 10f)));

            Shock = config.Bind("Lethal", "Shock", 0.05f,
                new ConfigDescription(
                    "And the share of a man's whole strength a blow to the arm must carry to "
                    + "knock the swing out of it altogether. A twentieth — half of what the "
                    + "game asks of a blow to the body, because it is the hand that is holding "
                    + "the weapon. This is the whole reason to strike at arms.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Legs = config.Bind("Lethal", "Legs", 0.05f,
                new ConfigDescription(
                    "The share of a man's whole strength below which a blow to the legs cannot "
                    + "take him. Twenty blows to the shins will leave him crippled and standing; "
                    + "they will not leave him dead. To kill a man you have to go for the parts "
                    + "that kill.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Spared = config.Bind("Lethal", "Spared", 0.10f,
                new ConfigDescription(
                    "What still gets through armour that held. A tenth: plate that stops a sword "
                    + "does not stop the man behind it feeling it.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Wears = config.Bind("Lethal", "Wears", true,
                "Let armour blunt the weapons that strike it. An edge against plate is an edge "
                + "spent; this is why nobody fought harnessed men with a sabre.");

            PerArmour = config.Bind("Lethal", "PerArmour", 0.004f,
                new ConfigDescription(
                    "Durability an edge loses per point of the struck armour's own durability. "
                    + "Four thousandths: a plate cuirass of three thousand costs a blade twelve "
                    + "points a blow, a leather jerkin of four hundred costs it under two.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Blunts = config.Bind("Lethal", "Blunts", 0f,
                new ConfigDescription(
                    "The same for crushing weapons. Nought: a hammer against plate is a hammer "
                    + "doing what it was made for, and nothing is spent but the other man. It "
                    + "wears only when met by a weapon, which the game already reckons.",
                    new AcceptableValueRange<float>(0f, 1f)));
        }

        /// <summary>What a blow to this place is worth.</summary>
        internal static float Worth(int slot)
        {
            Dictionary<int, float> table = Read();

            float much;
            return table.TryGetValue(slot, out much) ? much : 1f;
        }

        /// <summary>Whether a blow to this place may take a man's life at all.</summary>
        internal static bool Kills(int slot)
        {
            return slot != Anatomy.Pants && slot != Anatomy.Arms;
        }

        /// <summary>
        /// Что удар по руке делает с рукой.
        ///
        /// Крови в руке мало, и убить через неё нельзя — это считается выше. Но держат ею
        /// оружие, и потому удар туда стоит не жизни, а силы: рука, принявшая клинок, роняет
        /// замах и не держит щит. Отсюда две цены. Запас сил уходит по полтора очка за очко
        /// вреда, а сильный удар сбивает начатое вовсе — тем же зовом, каким игра сбивает
        /// заклинание, только порог вдвое ниже: то, что тело стерпит, кисть не держит.
        /// </summary>
        internal static void Wrist(UnitAttribute who, Attack attack, int spot, float dealt)
        {
            if (!Enabled.Value || who == null || spot != Anatomy.Arms || dealt <= 0f) return;

            try
            {
                if (who.Data == null || who.Data.isdead) return;

                if (Grip.Value > 0f) who.CostSP(dealt * Grip.Value);

                if (Shock.Value > 0f && who.maxhp > 0f && dealt >= who.maxhp * Shock.Value)
                {
                    who.Interrupt(attack);
                }
            }
            catch
            {
            }
        }

        /// <summary>Blunts the edge that struck this armour.</summary>
        internal static void Blunt(Attack attack, UIArmorInfo coat, float wear)
        {
            if (!Enabled.Value || !Wears.Value) return;
            if (attack == null || attack.weapon == null || coat == null) return;

            try
            {
                float rate = Crushing(attack) ? Blunts.Value : PerArmour.Value;
                if (rate <= 0f) return;

                float much = coat.durability * wear * rate;
                if (much <= 0f) return;

                UnitAttribute who = attack.attacker;
                HumaniodUnit man = who as HumaniodUnit;
                if (man == null || man.equipmentmanger == null) return;

                int index = attack.weapon.index;
                if (index < 0 || index >= man.equipmentmanger.equipInfos.Length) return;

                EquipInfo slot = man.equipmentmanger.equipInfos[index];
                if (slot == null || !slot.IsEquiped()) return;

                slot.inventory.CostDurability(much);
            }
            catch
            {
            }
        }

        private static bool Crushing(Attack attack)
        {
            try
            {
                float most = 0f;
                int lead = -1;

                foreach (KeyValuePair<DamageType, Damage> one in attack.damage)
                {
                    int kind = (int)one.Key;
                    if (kind > 2 || one.Value == null) continue;

                    if (one.Value.currentDamage > most)
                    {
                        most = one.Value.currentDamage;
                        lead = kind;
                    }
                }

                return lead == 1;
            }
            catch
            {
                return false;
            }
        }

        private static readonly Dictionary<int, float> zones = new Dictionary<int, float>();
        private static string read;

        private static Dictionary<int, float> Read()
        {
            string written = Zones.Value ?? "";
            if (written == read) return zones;

            read = written;
            zones.Clear();

            foreach (string one in written.Split(','))
            {
                string[] halves = one.Split('=');
                if (halves.Length != 2) continue;

                float much;
                if (!float.TryParse(halves[1].Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out much)) continue;

                switch (halves[0].Trim().ToLowerInvariant())
                {
                    case "head": zones[Anatomy.Head] = much; break;
                    case "chest": zones[Anatomy.Chest] = much; break;
                    case "arms":
                    case "hands": zones[Anatomy.Arms] = much; break;
                    case "pants":
                    case "legs": zones[Anatomy.Pants] = much; break;
                }
            }

            return zones;
        }
    }

    // Зов удара по руке живёт вместе с разносом урона по частям, в «Limb»: счёт там один
    // и тот же — сколько жизни ушло, — и делать его дважды незачем.
}
