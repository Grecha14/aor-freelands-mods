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
    /// Lets mass do what mass does.
    ///
    /// Урон оружия в этой игре записан вручную и с весом не связан ничем: боевой молот в шесть
    /// килограммов и кинжал в один бьют по числам, которые кто-то однажды проставил, и разница
    /// между ними — вопрос вкуса автора таблицы, а не рычага и инерции. То же и со зверьём:
    /// медведь и крыса отличаются здоровьем, а не тем, что медведь — это триста килограммов,
    /// идущих на вас.
    ///
    /// Здесь вес и размер снова что-то значат. Каждый килограмм оружия добавляет десятую часть
    /// урона: пять килограммов — в полтора раза, десять — вдвое. Никто при этом ничего не
    /// теряет, кинжал остаётся кинжалом; выигрывает тяжёлое, как и должно.
    ///
    /// У зверей оружия нет, и за вес отвечает их собственная туша: средний бьёт в два с
    /// половиной раза сильнее, крупный вчетверо. Размер зверя берётся не на глаз, а из самого
    /// существа — поле, проставленное в его заготовке, то же, что игра показывает в окне
    /// персонажа.
    /// </summary>
    internal static class Heft
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> PerKilo;
        internal static ConfigEntry<float> Toil;
        internal static ConfigEntry<float> Light;
        internal static ConfigEntry<float> Most;
        internal static ConfigEntry<string> Swings;
        internal static ConfigEntry<float> Steady;
        internal static ConfigEntry<float> Temper;
        internal static ConfigEntry<bool> Beasts;
        internal static ConfigEntry<string> Sizes;
        internal static ConfigEntry<string> Races;
        internal static ConfigEntry<bool> Report;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Heft", "Enabled", true,
                "Let the weight of a weapon and the bulk of a beast decide how hard they hit. "
                + "The game writes every damage figure by hand and never looks at either.");

            Toil = config.Bind("Heft", "Toil", 0.2f,
                new ConfigDescription(
                    "What one kilo of weapon above the light mark adds to the stamina a swing "
                    + "costs, as a share. A fifth: a two-handed sword of nine kilos costs twice "
                    + "and a half what a dagger does, where the game asked the same of both.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Light = config.Bind("Heft", "Light", 2f,
                new ConfigDescription(
                    "The weight below which a weapon costs no more than it did. Two kilos: a "
                    + "sword, a hatchet, an arming blade — what a man swings all day.",
                    new AcceptableValueRange<float>(0f, 20f)));

            PerKilo = config.Bind("Heft", "PerKilo", 0.10f,
                new ConfigDescription(
                    "Damage a weapon gains per kilogram, as a share. A tenth: five kilograms is "
                    + "half again, ten kilograms is double. Nothing loses by this — a dagger "
                    + "simply gains least.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Swings = config.Bind("Heft", "Swings", "0.6,1.0,0.4",
                "How much of its weight a weapon puts into the blow, by the kind of blow it "
                + "strikes: cutting, crushing, thrusting. A mace is its own weight and nothing "
                + "else, so it counts in full. A sword is heavy too but kills with the edge, so "
                + "it counts for rather less. A thrust is aim, not mass.");

            Steady = config.Bind("Heft", "Steady", 0.6f,
                new ConfigDescription(
                    "How much of a crushing weapon's spread is taken away. Six tenths: a mace "
                    + "lands about the same blow every time, where a sword swings between a "
                    + "graze and a killing cut depending on how the edge met the mark.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Temper = config.Bind("Heft", "Temper", 0.9f,
                new ConfigDescription(
                    "What a crushing weapon's damage is multiplied by. Slightly under a sword's, "
                    + "which is the trade: it does less and it does it through armour.",
                    new AcceptableValueRange<float>(0.1f, 2f)));

            Most = config.Bind("Heft", "Most", 3.0f,
                new ConfigDescription(
                    "And what no weapon's weight may multiply it past, in case something in the "
                    + "world turns out to weigh twenty kilograms.",
                    new AcceptableValueRange<float>(1f, 10f)));

            Beasts = config.Bind("Heft", "Beasts", true,
                "Let the bulk of a beast decide how hard it hits. A beast carries no weapon, so "
                + "its own mass is the weapon.");

            Sizes = config.Bind("Heft", "Sizes", "0,150,300,450,600",
                "What each size adds, in percent, from small to titanic: small, medium, large, "
                + "giant, titanic. A medium beast hits two and a half times as hard, a large one "
                + "four times. Giant and titanic continue the same step — nobody asked for those "
                + "two, so they are a guess and meant to be tuned.");

            Races = config.Bind("Heft", "Races", "animal,insect",
                "Which races count as beasts for this. Insects are in by default — a giant "
                + "spider is as much an animal as a bear. Men, elves, orcs, the undead and demons "
                + "are not, whatever their size.");

            Report = config.Bind("Heft", "Report", true,
                "Write every kind of beast that gets the bonus to the log once, with its size. "
                + "Size is set in each creature's blueprint, and a creature whose author left it "
                + "alone counts as small — this is how to find out which ones did.");
        }


        // ----------------------------------------------------------------- дыхание за вес

        private static bool toiled;

        /// <summary>
        /// Что вес оружия стоит дыханию.
        ///
        /// Расход сил за удар записан у каждой вещи своим числом и с весом не связан ничем:
        /// кинжал и двуручный молот просят у бойца одинаково. Между тем именно здесь вес и
        /// должен сказаться: поднять тяжёлое можно и разово, а махать им весь бой — нет.
        ///
        /// Оттого цена удара растёт от веса сверх лёгкого. Кинжал и меч не дорожают, двуручное
        /// дорожает вдвое с лишним: новичок его и не возьмёт — не по силе, — а взявший будет
        /// считать каждый замах.
        /// </summary>
        internal static void Toils()
        {
            if (toiled || Enabled == null || !Enabled.Value || Toil.Value <= 0f) return;

            try
            {
                UIItemDatabase db;
                try { db = UIItemDatabase.Instance; }
                catch { return; }

                if (db == null || db.items == null) return;

                toiled = true;

                int moved = 0;
                float most = 0f;
                string worst = null;

                foreach (UIItemInfo thing in db.items)
                {
                    UIWeaponInfo blade = thing as UIWeaponInfo;
                    if (blade == null || blade.weight <= 0f || blade.spCost <= 0f) continue;

                    // Щит и колчан своей ценой не растут: щитом не машут, колчан просто висит.
                    if (blade.WeaponType == WeaponType.shield) continue;
                    if (blade.WeaponType == WeaponType.quiver) continue;

                    float over = Mathf.Max(0f, blade.weight - Light.Value);
                    if (over <= 0f) continue;

                    float much = 1f + over * Toil.Value;

                    blade.spCost *= much;
                    moved++;

                    if (much > most) { most = much; worst = blade.name; }
                }

                if (Report.Value && moved > 0)
                {
                    ItemForgePlugin.Log.LogInfo($"Цена замаха по весу: правлено {moved} вещей, "
                        + $"дороже всех «{worst}» в {most:0.00} раза.");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог пересчитать цену замаха: " + e.Message);
            }
        }

        // ----------------------------------------------------------------- вес оружия

        /// <summary>Gives a weapon what its own weight is worth.</summary>
        internal static void Hands(HumaniodUnit who)
        {
            if (!Enabled.Value || PerKilo.Value <= 0f) return;
            if (who == null || who.weapons == null) return;

            EquipmentManager gear = who.equipmentmanger;
            if (gear == null || gear.equipInfos == null) return;

            for (int i = 0; i < who.weapons.Count && i < 2 && i < gear.equipInfos.Length; i++)
            {
                EquipInfo slot = gear.equipInfos[i];
                if (slot == null || !slot.IsEquiped()) continue;

                UIWeaponInfo blade = slot.inventory.itemInfo as UIWeaponInfo;
                if (blade == null || blade.weight <= 0f) continue;

                // Щит и колчан веса своего не прикладывают: один держит удар, другой лежит.
                if (blade.WeaponType == WeaponType.shield) continue;
                if (blade.WeaponType == WeaponType.quiver) continue;

                // Вес идёт в удар по-разному. Дубина и молот — это вес и есть: вся их работа в
                // том, чтобы донести его до цели, и каждый килограмм считается полностью.
                // Двуручный меч тяжёл тоже, но бьёт не весом, а кромкой, и лишний килограмм
                // добавляет ему меньше. Кинжалу же от веса нет вовсе ничего.
                float rate = PerKilo.Value * Swing(blade);

                float much = Mathf.Clamp(1f + blade.weight * rate, 1f, Most.Value);
                if (much > 1f) Wield.Scale(who.weapons[i], much);

                Steadily(who.weapons[i], blade);
            }
        }

        /// <summary>Takes the luck out of a hammer and a little of its bite with it.</summary>
        private static void Steadily(Weapon arm, UIWeaponInfo blade)
        {
            // Дубина бьёт ровно. У меча всё решает, куда пришлась кромка, — оттого у него и
            // разброс от «царапина» до «пополам»; палица же просто передаёт вес, и от раза к
            // разу это один и тот же вес. Ровность стоит остроты: полного маха меча ей не
            // достать никогда, но и пустого удара у неё не бывает.
            if (arm == null || arm.damage == null) return;
            if (Steady.Value <= 0f && Temper.Value >= 1f) return;
            if (Swing(blade) < 0.99f) return;

            try
            {
                foreach (KeyValuePair<DamageType, Damage> one in arm.damage)
                {
                    Damage hit = one.Value;
                    if (hit == null) continue;

                    float middle = (hit.minDamage + hit.maxDamage) * 0.5f * Temper.Value;
                    float half = (hit.maxDamage - hit.minDamage) * 0.5f
                                 * (1f - Steady.Value) * Temper.Value;

                    hit.minDamage = Mathf.Max(0f, middle - half);
                    hit.maxDamage = middle + half;
                    hit.currentDamage *= Temper.Value;
                }
            }
            catch
            {
            }
        }

        /// <summary>How much of its own weight a weapon actually puts into the blow.</summary>
        private static float Swing(UIWeaponInfo blade)
        {
            if (blade == null || blade.damage == null) return 1f;

            try
            {
                float most = 0f;
                int leading = -1;

                foreach (KeyValuePair<DamageType, Damage> one in blade.damage)
                {
                    int kind = (int)one.Key;
                    if (kind > 2 || one.Value == null) continue;

                    float much = one.Value.maxDamage;
                    if (much > most) { most = much; leading = kind; }
                }

                return Bearing(leading);
            }
            catch
            {
                return 1f;
            }
        }

        private static float[] bearings;
        private static string bearingsRead;

        private static float Bearing(int kind)
        {
            string written = Swings.Value ?? "";

            if (written != bearingsRead)
            {
                bearingsRead = written;

                List<float> got = new List<float>();
                foreach (string one in written.Split(','))
                {
                    float much;
                    if (float.TryParse(one.Trim(),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out much))
                    {
                        got.Add(much);
                    }
                }

                bearings = got.ToArray();
            }

            if (bearings == null || kind < 0 || kind >= bearings.Length) return 1f;

            return bearings[kind];
        }

        // ----------------------------------------------------------------- размер зверя

        /// <summary>Gives a beast what its own bulk is worth.</summary>
        internal static void Bulk(UnitAttribute who)
        {
            if (!Enabled.Value || !Beasts.Value) return;
            if (who == null || who.Data == null || who.weapons == null) return;
            if (!Counts(who.Data.race)) return;

            float[] steps = Steps();
            int size = (int)who.size;
            if (size < 0 || size >= steps.Length) return;

            float much = 1f + steps[size] * 0.01f;
            if (much <= 1f) return;

            for (int i = 0; i < who.weapons.Count; i++) Wield.Scale(who.weapons[i], much);

            Seen(who, much);
        }

        private static readonly HashSet<UnitRace> beasts = new HashSet<UnitRace>();
        private static string racesRead;

        private static bool Counts(UnitRace race)
        {
            string written = Races.Value ?? "";

            if (written != racesRead)
            {
                racesRead = written;
                beasts.Clear();

                foreach (string one in written.Split(','))
                {
                    string name = one.Trim();
                    if (name.Length == 0) continue;

                    try { beasts.Add((UnitRace)Enum.Parse(typeof(UnitRace), name, true)); }
                    catch { ItemForgePlugin.Log.LogWarning($"Расы «{name}» в игре нет."); }
                }
            }

            return beasts.Contains(race);
        }

        // Одна строка на каждый вид, а не на каждую тварь: иначе стая волков засыпет лог
        // одним и тем же.
        private static readonly HashSet<string> told = new HashSet<string>();

        private static void Seen(UnitAttribute who, float much)
        {
            if (!Report.Value) return;

            try
            {
                string name = who.Data.unitname ?? who.name;
                string key = name + "/" + who.size;

                if (!told.Add(key)) return;

                ItemForgePlugin.Log.LogInfo($"Зверь «{name}»: размер {who.size}, "
                    + $"урон ×{much:0.##}.");
            }
            catch
            {
            }
        }

        // ----------------------------------------------------------------- лестница

        private static float[] steps;
        private static string read;

        private static float[] Steps()
        {
            string written = Sizes.Value ?? "";
            if (steps != null && written == read) return steps;

            List<float> got = new List<float>();

            foreach (string one in written.Split(','))
            {
                float much;
                if (float.TryParse(one.Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out much))
                {
                    got.Add(Mathf.Max(0f, much));
                }
            }

            while (got.Count < 5) got.Add(got.Count > 0 ? got[got.Count - 1] : 0f);

            read = written;
            steps = got.ToArray();
            return steps;
        }
    }

    // Вес оружия считается там же, где требования, — в конце пересчёта, когда урон уже сведён.
    // Порядок с ними неважен: и то и другое умножает, а умножение не помнит очерёдности.
    [HarmonyPatch(typeof(HumaniodUnit), "WriteUnitAttribute")]
    internal static class WriteUnitAttribute_Heft_Patch
    {
        private static void Postfix(HumaniodUnit __instance)
        {
            try { Heft.Hands(__instance); }
            catch (Exception e) { ItemForgePlugin.Log.LogWarning("Вес оружия сорвался: " + e.Message); }
        }
    }

    // Зверьё своего класса в игре не имеет вовсе — это обычный UnitAttribute, — поэтому
    // размер считается на общем пересчёте. Людей он тоже задевает, но отсекается по расе.
    [HarmonyPatch(typeof(UnitAttribute), "WriteUnitAttribute")]
    internal static class WriteUnitAttribute_Bulk_Patch
    {
        private static void Postfix(UnitAttribute __instance)
        {
            try { Heft.Bulk(__instance); }
            catch (Exception e) { ItemForgePlugin.Log.LogWarning("Размер зверя сорвался: " + e.Message); }
        }
    }
}
