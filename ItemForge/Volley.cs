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
    /// Bows and crossbows, told apart at last.
    ///
    /// В игре четыре вида стрелкового оружия и одна повадка на всех: разница между коротким
    /// луком и тяжёлым арбалетом сводится к числам в карточке, а ведут они себя одинаково.
    ///
    /// Здесь у каждого своя правда. Длинный лук бьёт дальше и тяжелее короткого, но тетива у
    /// него тугая и выстрел редкий. Арбалет стреляет не чаще, зато болт идёт туда, куда стрела
    /// не идёт вовсе: в латы. Против лёгкой брони и голого тела он ничем не примечателен, а
    /// против тяжёлой снимает её вклад — до Т3 весь, у Т4 половину, у Т5 четверть — и
    /// оставляет рваную рану, которая ещё какое-то время течёт.
    ///
    /// Снимается при этом сопротивление именно той вещи, в которую попало: болт, вошедший в
    /// наплечник, ничего не знает о поножах.
    /// </summary>
    internal static class Volley
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> LongDamage;
        internal static ConfigEntry<float> LongSpeed;
        internal static ConfigEntry<float> LongRange;
        internal static ConfigEntry<float> HeavyDamage;
        internal static ConfigEntry<float> HeavyReload;
        internal static ConfigEntry<bool> Punches;
        internal static ConfigEntry<string> Tiers;
        internal static ConfigEntry<string> Armours;
        internal static ConfigEntry<string> Force;
        internal static ConfigEntry<string> Named;
        internal static ConfigEntry<string> Bleed;
        internal static ConfigEntry<float> BleedChance;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Volley", "Enabled", true,
                "Tell the ranged weapons apart: a longbow from a shortbow, a crossbow from both.");

            LongDamage = config.Bind("Volley", "LongDamage", 1.5f,
                new ConfigDescription(
                    "What a longbow does to its own damage. Half again.",
                    new AcceptableValueRange<float>(0.1f, 5f)));

            LongSpeed = config.Bind("Volley", "LongSpeed", 0.7f,
                new ConfigDescription(
                    "What a longbow does to its own rate of shooting. Three tenths slower: the "
                    + "stave is heavy and the draw is long.",
                    new AcceptableValueRange<float>(0.1f, 5f)));

            LongRange = config.Bind("Volley", "LongRange", 1.5f,
                new ConfigDescription(
                    "What a longbow does to its own reach.",
                    new AcceptableValueRange<float>(0.1f, 5f)));

            HeavyDamage = config.Bind("Volley", "HeavyDamage", 1.3f,
                new ConfigDescription(
                    "What a heavy crossbow does to its damage, over an ordinary one.",
                    new AcceptableValueRange<float>(0.1f, 5f)));

            HeavyReload = config.Bind("Volley", "HeavyReload", 1.8f,
                new ConfigDescription(
                    "How much longer a heavy crossbow takes to wind.\n\n"
                    + "Восемь десятых сверх — то есть болт уходит раз в семь секунд с лишним "
                    + "вместо пяти. Ворот крутят всем телом, упираясь ногой в стремя, и всё "
                    + "это время стрелок не боец вовсе. Прежде стояла пятая часть, и арбалет "
                    + "выходил оружием без изъяна: самый сильный удар в игре и вполне сносный "
                    + "темп. Плата за такой удар — стоять и крутить.",
                    new AcceptableValueRange<float>(0.1f, 5f)));

            Punches = config.Bind("Volley", "Punches", true,
                "Let a crossbow defeat heavy armour. This is the whole reason anyone carried one.");

            Tiers = config.Bind("Volley", "Tiers", "1,1,1,1,0.5,0.25",
                "How much of the struck armour a bolt takes out of the reckoning, by that "
                + "armour's tier from T0 to T5. All of it up to T3, half at T4, a quarter at T5: "
                + "the finest plate still turns a bolt, the common sort does not.");

            Armours = config.Bind("Volley", "Armours", "Heavy,Medium",
                "Kinds of armour this counts against, by the game's own names. Against cloth and "
                + "leather a bolt is only a bolt, and has nothing to defeat.");

            Force = config.Bind("Volley", "Force", "0.6,1.6,1.2",
                "What a blow carries against armour, by what kind of blow it is: cutting, "
                + "crushing, thrusting. An edge is the thing plate was made against and does "
                + "worst of the three; a hammer does not care what it is hitting; a thrust goes "
                + "where the plates meet.");

            Named = config.Bind("Volley", "Named", "Crossbow=2.0,HeavyCrossbow=2.0,Longbow=0.5,Shortbow=0.5",
                "Weapons whose piercing is not what their damage type would suggest, by the "
                + "game's own class names. A bolt and an arrow are both counted as thrusting "
                + "damage and are nothing alike against plate — the one was made to go through "
                + "it, the other was not, and no damage type in this game can tell them apart.");

            Bleed = config.Bind("Volley", "Bleed", "",
                "Name of the wound a bolt leaves. Empty, and the first bleeding effect the game "
                + "has is used; the log says which one was found.");

            BleedChance = config.Bind("Volley", "BleedChance", 1f,
                new ConfigDescription(
                    "How often a bolt that lands leaves that wound.",
                    new AcceptableValueRange<float>(0f, 1f)));
        }

        // ------------------------------------------------------------------ лук и ворот

        /// <summary>Gives each stave what belongs to it. Rebuilt every pass, so never twice.</summary>
        internal static void Draw(UnitAttribute who)
        {
            if (!Enabled.Value || who == null || who.weapons == null) return;

            try
            {
                bool touched = false;

                foreach (Weapon arm in who.weapons)
                {
                    if (arm == null) continue;

                    if (arm.weaponClass == WeaponClass.Longbow)
                    {
                        arm.damage *= LongDamage.Value;
                        arm.attackSpeed *= LongSpeed.Value;
                        arm.attackDistance *= LongRange.Value;
                        touched = true;
                    }
                    else if (arm.weaponClass == WeaponClass.HeavyCrossbow)
                    {
                        arm.damage *= HeavyDamage.Value;
                        arm.attackSpeed /= Mathf.Max(0.01f, HeavyReload.Value);
                        touched = true;
                    }
                }

                if (!touched) return;

                // Отряд и враги целятся по числам самого бойца, а не оружия, и игра сложила их
                // до нас. Складываем заново, её же правилом: дальность по самому короткому из
                // того, чем можно бить, скорость средняя.
                float speed = 0f;
                float reach = 0f;
                bool first = true;

                foreach (Weapon arm in who.weapons)
                {
                    if (arm == null) continue;

                    speed += arm.attackSpeed;

                    if (!arm.canAttack) continue;
                    if (first || arm.attackDistance < reach) reach = arm.attackDistance;
                    first = false;
                }

                if (who.weapons.Count > 0) who.attackspeed = speed / who.weapons.Count;
                if (!first) who.attackDistance = reach;
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог развести луки и арбалеты: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ болт и латы

        internal static bool IsCrossbow(WeaponClass kind)
        {
            return kind == WeaponClass.Crossbow || kind == WeaponClass.HeavyCrossbow;
        }

        /// <summary>
        /// How much of the struck piece a bolt takes out of the reckoning, as a share. Zero
        /// when this is not a bolt, or when there is nothing there worth defeating.
        /// </summary>
        internal static float Pierce(Attack attack, UIArmorInfo coat)
        {
            if (!Enabled.Value || !Punches.Value) return 0f;
            if (attack == null || attack.weapon == null || coat == null) return 0f;

            try
            {
                if (!IsCrossbow(attack.weapon.weaponClass)) return 0f;
                if (!Kinds().Contains((int)coat.armourType)) return 0f;

                return Mathf.Clamp01(Share((int)coat.tier));
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>
        /// What a blow carries for getting through armour at all: its bodily damage weighed by
        /// what kind of blow it is. A hammer of forty carries more than a sword of forty,
        /// because the sword has an edge and the edge is what plate was made against.
        /// </summary>
        internal static float Punch(Attack attack)
        {
            if (attack == null || attack.damage == null) return 0f;

            try
            {
                float body = 0f;
                float most = 0f;
                int leading = -1;

                foreach (KeyValuePair<DamageType, Damage> one in attack.damage)
                {
                    int kind = (int)one.Key;
                    if (kind > 2 || one.Value == null) continue;

                    body += one.Value.currentDamage;

                    if (one.Value.currentDamage > most)
                    {
                        most = one.Value.currentDamage;
                        leading = kind;
                    }
                }

                // Чистая магия брони не знает вовсе: ей нечего пробивать.
                if (body <= 0f) return float.MaxValue;

                float factor = Heave(leading);

                // Классу оружия слово последнее. Болт и стрела — оба колющие, а против лат это
                // разные вещи, и по типу урона их не различить ничем.
                if (attack.weapon != null)
                {
                    float said;
                    if (Told().TryGetValue((int)attack.weapon.weaponClass, out said)) factor = said;
                }

                return body * factor;
            }
            catch
            {
                return float.MaxValue;
            }
        }

        private static float[] heaves;
        private static string heavesRead;

        private static float Heave(int kind)
        {
            string written = Force.Value ?? "";

            if (written != heavesRead)
            {
                heavesRead = written;

                List<float> got = new List<float>();
                foreach (string one in written.Split(','))
                {
                    float much;
                    if (float.TryParse(one.Trim(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out much))
                    {
                        got.Add(much);
                    }
                }

                heaves = got.ToArray();
            }

            if (heaves == null || kind < 0 || kind >= heaves.Length) return 1f;

            return heaves[kind];
        }

        private static readonly Dictionary<int, float> told = new Dictionary<int, float>();
        private static string toldRead;

        private static Dictionary<int, float> Told()
        {
            string written = Named.Value ?? "";
            if (written == toldRead) return told;

            toldRead = written;
            told.Clear();

            foreach (string one in written.Split(','))
            {
                string[] halves = one.Split('=');
                if (halves.Length != 2) continue;

                float much;
                if (!float.TryParse(halves[1].Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out much)) continue;

                try
                {
                    told.Add((int)(WeaponClass)Enum.Parse(
                        typeof(WeaponClass), halves[0].Trim(), true), much);
                }
                catch
                {
                    ItemForgePlugin.Log.LogWarning("Такого оружия игра не знает: " + halves[0]);
                }
            }

            return told;
        }

        private static float[] shares;
        private static string sharesRead;

        private static float Share(int tier)
        {
            string written = Tiers.Value ?? "";

            if (written != sharesRead)
            {
                sharesRead = written;

                List<float> got = new List<float>();
                foreach (string one in written.Split(','))
                {
                    float much;
                    if (float.TryParse(one.Trim(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out much))
                    {
                        got.Add(much);
                    }
                }

                shares = got.ToArray();
            }

            if (shares == null || shares.Length == 0) return 0f;
            if (tier < 0) tier = 0;
            if (tier >= shares.Length) tier = shares.Length - 1;

            return shares[tier];
        }

        private static readonly HashSet<int> kinds = new HashSet<int>();
        private static string kindsRead;

        private static HashSet<int> Kinds()
        {
            string written = Armours.Value ?? "";
            if (written == kindsRead) return kinds;

            kindsRead = written;
            kinds.Clear();

            foreach (string one in written.Split(','))
            {
                string name = one.Trim();
                if (name.Length == 0) continue;

                try
                {
                    kinds.Add((int)(ArmourType)Enum.Parse(typeof(ArmourType), name, true));
                }
                catch
                {
                    ItemForgePlugin.Log.LogWarning("Такого рода брони игра не знает: " + name);
                }
            }

            return kinds;
        }

        // ------------------------------------------------------------------ рана

        private static UIBuffInfo wound;
        private static bool sought;

        private static UIBuffInfo Wound()
        {
            if (sought) return wound;
            sought = true;

            try
            {
                UIBuffDatabase book = UIBuffDatabase.Instance;
                if (book == null || book.buffs == null) return null;

                string asked = (Bleed.Value ?? "").Trim();

                if (asked.Length > 0)
                {
                    wound = book.GetByID(asked);

                    if (wound == null)
                    {
                        ItemForgePlugin.Log.LogWarning("Раны с именем «" + asked
                            + "» в игре нет — кровотечения от болта не будет.");
                    }

                    return wound;
                }

                foreach (UIBuffInfo one in book.buffs)
                {
                    if (one == null || one.type != bufftype.bleeding) continue;

                    wound = one;

                    ItemForgePlugin.Log.LogInfo("Кровотечение от болта: беру «" + one.id
                        + "». Если это не та рана, впишите нужную в настройку Volley.Bleed.");

                    return wound;
                }

                ItemForgePlugin.Log.LogWarning("Кровотечений в игре не нашлось вовсе.");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог найти кровотечение: " + e.Message);
            }

            return wound;
        }

        /// <summary>Leaves the wound a bolt leaves.</summary>
        internal static void Tear(UnitAttribute target, Attack attack)
        {
            if (!Enabled.Value || target == null || attack == null || attack.weapon == null) return;

            try
            {
                if (!IsCrossbow(attack.weapon.weaponClass)) return;
                if (target.buffmanger == null) return;
                if (UnityEngine.Random.value >= BleedChance.Value) return;

                UIBuffInfo mark = Wound();
                if (mark == null) return;

                target.buffmanger.AddBuff(new BuffBase(mark, target), attack.attacker);
            }
            catch
            {
            }
        }
    }

    // Числа оружия игра пересобирает целиком при каждом пересчёте, поэтому множители здесь не
    // накапливаются: что ни проход, то заново от исходных.
    [HarmonyPatch(typeof(UnitAttribute), "WriteUnitAttribute")]
    internal static class WriteUnitAttribute_Volley_Patch
    {
        private static void Postfix(UnitAttribute __instance)
        {
            try { Volley.Draw(__instance); }
            catch { }
        }
    }

    // Сюда удар приходит уже состоявшимся: цель известна, промах и блок позади.
    [HarmonyPatch(typeof(UnitAttribute), "UnderAttack")]
    internal static class UnderAttack_Volley_Patch
    {
        private static void Postfix(UnitAttribute __instance, Attack t_attack)
        {
            try { Volley.Tear(__instance, t_attack); }
            catch { }
        }
    }
}
