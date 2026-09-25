using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Wounds that take time, and a surgeon worth feeding.
    ///
    /// Здоровье в этой игре — не очки жизни, а та полоса от нуля до ста, которую лечит лекарь и
    /// которая держит всё прочее. Затягивалась она мгновенно: поел — и цел. Оттого ни лазарет,
    /// ни аптечка, ни навык медицины не значили ничего: рана переставала быть событием.
    ///
    /// Здесь она затягивается вчетверо медленнее, а еда лечит не с укуса, а со дня: съеденное
    /// ложится в запас, и запас выплачивается по часам, с потолком за сутки по лучшему блюду.
    ///
    /// Аптечка при этом сохраняет полную силу — она единственная осталась быстрым способом, —
    /// и только она учит медицине: игра засчитывала опыт назначенному лекарю и не засчитывала
    /// герою, перевязавшему свой же отряд. Обозный хирург лечит сам, каждый день и где угодно,
    /// по уровню повозки и по знанию лучшего врача в отряде.
    /// </summary>
    internal static class Mending
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Heal;
        internal static ConfigEntry<float> Doctor;
        internal static ConfigEntry<float> Kit;
        internal static ConfigEntry<bool> FromFood;
        internal static ConfigEntry<string> DayCap;
        internal static ConfigEntry<string> Wagon;
        internal static ConfigEntry<float> PerMedic;
        internal static ConfigEntry<float> KitExp;

        /// <summary>Правится ли прямо сейчас чьё-то здоровье руками — из лазарета или аптечкой.</summary>
        internal static bool Working;

        /// <summary>И если руками, то в городском лазарете, а не в поле.</summary>
        internal static bool Infirmary;

        /// <summary>Наша собственная выплата: её замедлять второй раз нельзя.</summary>
        internal static bool Paying;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Mending", "Enabled", true,
                "Make wounds take time. The health bar the doctor treats - not hit points - "
                + "comes back four times slower, and food no longer mends anything on the spot.");

            Heal = config.Bind("Mending", "Heal", 0.25f,
                new ConfigDescription(
                    "Everything that raises health is multiplied by this: the half point an hour "
                    + "the body finds on its own, the camp surgeon, the tavern. Harm to health is "
                    + "never touched, only mending.",
                    new AcceptableValueRange<float>(0.01f, 5f)));

            Doctor = config.Bind("Mending", "Doctor", 0.25f,
                new ConfigDescription(
                    "The same, for the town infirmary alone. Kept apart so a paid bed can be "
                    + "given its worth back without making every scratch close by itself.",
                    new AcceptableValueRange<float>(0.01f, 5f)));

            Kit = config.Bind("Mending", "Kit", 1f,
                new ConfigDescription(
                    "The same, for a medical kit used by hand. One, and the kit keeps its full "
                    + "worth: with everything else cut to a quarter, bandaging a man yourself "
                    + "becomes the fast way to mend him - and the only way the medicine skill "
                    + "ever gets practice.",
                    new AcceptableValueRange<float>(0.01f, 5f)));

            Wagon = config.Bind("Mending", "Wagon", "5,10,15",
                "Health the caravan surgeon mends per day, by the level of his wagon. It works "
                + "of itself, wherever the company happens to be, rather than only when Tend is "
                + "picked at a camp: once a day is too rare to feel.");

            PerMedic = config.Bind("Mending", "PerMedic", 1f,
                new ConfigDescription(
                    "Health mended per day for each point of medicine the best physician in the "
                    + "company has. Needs the wagon: a surgeon without a table is a man with "
                    + "opinions.",
                    new AcceptableValueRange<float>(0f, 10f)));

            KitExp = config.Bind("Mending", "KitExp", 0.5f,
                new ConfigDescription(
                    "Medicine learned per point of health mended by hand. The game only credits "
                    + "an appointed doctor, so a hero treating his own company learned nothing.",
                    new AcceptableValueRange<float>(0f, 10f)));

            FromFood = config.Bind("Mending", "FromFood", true,
                "Let food mend by the day rather than by the bite. What a dish would have healed "
                + "goes into a reserve instead, and the reserve pays out slowly.");

            DayCap = config.Bind("Mending", "DayCap", "10,10,10,20,30,40",
                "The most health food can mend in one day, by the tier of the best dish eaten "
                + "that day, T0 through T5. Eating more only fills the reserve; it does not "
                + "raise the ceiling. Better food does.");
        }

        /// <summary>How much of a mending actually lands: by hand in full, by itself slowly.</summary>
        internal static float Temper(float value)
        {
            if (!Enabled.Value || value <= 0f || Paying) return value;

            if (!Working) return value * Heal.Value;

            return value * (Infirmary ? Doctor.Value : Kit.Value);
        }

        /// <summary>Что рука выучила, перевязав. Лазарет не учит: там работает не она.</summary>
        internal static void Learned(float much)
        {
            if (!Enabled.Value || much <= 0f || KitExp.Value <= 0f || !Working || Infirmary) return;

            try
            {
                HumaniodUnit hand = Medic();
                if (hand == null) return;

                // Двенадцатое ремесло — медицина.
                hand.GainProfessionExp(12, much * KitExp.Value);
            }
            catch
            {
            }
        }

        /// <summary>The best physician the company has.</summary>
        internal static HumaniodUnit Medic()
        {
            HumaniodUnit best = null;

            try
            {
                PartyManager party = PartyManager.instance;
                if (party == null || party.partyMembers == null) return null;

                foreach (HumaniodUnit one in party.partyMembers)
                {
                    if (one == null || one.Data == null) continue;

                    if (best == null || one.Data.medical > best.Data.medical) best = one;
                }
            }
            catch
            {
            }

            return best;
        }

        /// <summary>Обозный хирург за прошедший час. Работает сам, где бы отряд ни был.</summary>
        internal static void Tend(HumaniodUnit who, float hourPassed)
        {
            if (!Enabled.Value || hourPassed <= 0f || who == null || who.Data == null
                || who.Data.health >= 100f) return;

            try
            {
                CaravanUIManager caravan = CaravanUIManager.Instance;
                if (caravan == null || !caravan.HasCaravan || caravan.saveData == null) return;

                int level = caravan.saveData.caravanMedicalLevel;
                if (level <= 0) return;

                float much = Rung(level);

                HumaniodUnit hand = Medic();
                if (hand != null) much += hand.Data.medical * PerMedic.Value;

                if (much <= 0f) return;

                // Своя выплата замедлению не подлежит: она уже посчитана за сутки.
                Paying = true;

                try
                {
                    who.Data.AddHealth(much / 24f * hourPassed);
                }
                finally
                {
                    Paying = false;
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог полечить обозом: " + e.Message);
            }
        }

        private static float[] rungs;
        private static string rungsRead;

        private static float Rung(int level)
        {
            string written = Wagon.Value ?? "";

            if (written != rungsRead)
            {
                rungsRead = written;

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

                rungs = got.ToArray();
            }

            if (rungs == null || rungs.Length == 0) return 0f;
            if (level < 1) return 0f;
            if (level > rungs.Length) level = rungs.Length;

            return rungs[level - 1];
        }

        // ----------------------------------------------------------------- запас от еды

        private sealed class Reserve
        {
            internal float left;
            internal float paid;
            internal float cap;
            internal int day;
        }

        private static readonly Dictionary<int, Reserve> kept = new Dictionary<int, Reserve>();
        private static bool read;
        private static bool dirty;
        private static float next;

        private static string Ledger
        {
            get { return Path.Combine(Paths.ConfigPath, "aor.mending.tsv"); }
        }

        private static Reserve Of(int who)
        {
            Reserve mine;
            if (!kept.TryGetValue(who, out mine))
            {
                mine = new Reserve();
                kept[who] = mine;
            }

            int today = TimeManager.TotalDay;

            if (mine.day != today)
            {
                mine.day = today;
                mine.paid = 0f;
                mine.cap = Ceiling(0);
            }

            return mine;
        }

        /// <summary>Съеденное идёт не в рану, а в запас: заживает не обед, а человек.</summary>
        internal static void Store(UnitAttribute who, UIItemInfo dish, float much)
        {
            if (!Enabled.Value || !FromFood.Value || much <= 0f || who == null
                || who.Data == null) return;

            try
            {
                Load();

                Reserve mine = Of(who.Data.id);
                mine.left += much;

                // Потолок за сутки держит лучшее из съеденного: похлёбка не станет лекарством,
                // сколько её ни съешь.
                float roof = Ceiling(dish != null ? (int)dish.tier : 0);
                if (roof > mine.cap) mine.cap = roof;

                dirty = true;

                ItemForgePlugin.Log.LogInfo($"«{who.Data.unitname}» отложил на поправку "
                    + $"{much:0.#}, в запасе {mine.left:0.#}, потолок на сутки {mine.cap:0.#}.");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог отложить на поправку: " + e.Message);
            }
        }

        /// <summary>И выплачивается по часам, покуда есть из чего.</summary>
        internal static void Drip(HumaniodUnit who, float hourPassed)
        {
            if (!Enabled.Value || !FromFood.Value || hourPassed <= 0f || who == null
                || who.Data == null) return;

            try
            {
                Load();

                Reserve mine;
                if (!kept.TryGetValue(who.Data.id, out mine)) return;

                mine = Of(who.Data.id);
                if (mine.left <= 0f) return;

                float roof = mine.cap > 0f ? mine.cap : Ceiling(0);

                float much = Mathf.Min(roof / 24f * hourPassed, roof - mine.paid);
                much = Mathf.Min(much, mine.left);

                if (much <= 0f) return;

                mine.left -= much;
                mine.paid += much;
                dirty = true;

                Paying = true;

                try
                {
                    who.Data.AddHealth(much);
                }
                finally
                {
                    Paying = false;
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог выплатить из запаса: " + e.Message);
            }
        }

        private static float[] roofs;
        private static string roofsRead;

        private static float Ceiling(int tier)
        {
            string written = DayCap.Value ?? "";

            if (written != roofsRead)
            {
                roofsRead = written;

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

                roofs = got.ToArray();
            }

            if (roofs == null || roofs.Length == 0) return 10f;
            if (tier < 0) tier = 0;
            if (tier >= roofs.Length) tier = roofs.Length - 1;

            return roofs[tier];
        }

        /// <summary>Запас переживает выход из игры: он про дни, а не про бой.</summary>
        internal static void Settle()
        {
            if (!dirty || Time.time < next) return;

            next = Time.time + 20f;
            dirty = false;

            try
            {
                StringBuilder said = new StringBuilder();

                foreach (KeyValuePair<int, Reserve> one in kept)
                {
                    said.Append(one.Key).Append('\t')
                        .Append(one.Value.left.ToString("0.###", CultureInfo.InvariantCulture))
                        .Append('\t')
                        .Append(one.Value.paid.ToString("0.###", CultureInfo.InvariantCulture))
                        .Append('\t')
                        .Append(one.Value.cap.ToString("0.###", CultureInfo.InvariantCulture))
                        .Append('\t')
                        .Append(one.Value.day)
                        .AppendLine();
                }

                File.WriteAllText(Ledger, said.ToString());
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог записать запасы на поправку: " + e.Message);
            }
        }

        private static void Load()
        {
            if (read) return;
            read = true;

            try
            {
                if (!File.Exists(Ledger)) return;

                foreach (string line in File.ReadAllLines(Ledger))
                {
                    string[] cells = line.Split('\t');
                    if (cells.Length < 5) continue;

                    int who;
                    float left, paid, cap;
                    int day;

                    if (int.TryParse(cells[0], out who)
                        && float.TryParse(cells[1], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out left)
                        && float.TryParse(cells[2], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out paid)
                        && float.TryParse(cells[3], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out cap)
                        && int.TryParse(cells[4], out day))
                    {
                        kept[who] = new Reserve
                        {
                            left = left,
                            paid = paid,
                            cap = cap,
                            day = day
                        };
                    }
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог прочесть запасы на поправку: " + e.Message);
            }
        }
    }

    // Всё, что поднимает здоровье, проходит здесь.
    [HarmonyPatch(typeof(NPCSaveData), "AddHealth")]
    internal static class AddHealth_Mending_Patch
    {
        private static void Prefix(ref float value)
        {
            try
            {
                value = Mending.Temper(value);
                Mending.Learned(value);
            }
            catch
            {
            }
        }
    }

    // Лазарет это или поле — узнаём по тому, как открыли окно.
    [HarmonyPatch(typeof(HealingManager), "OpenHealingWindow")]
    internal static class OpenHealing_Mending_Patch
    {
        private static void Prefix(bool isClinic)
        {
            Mending.Infirmary = isClinic;
        }
    }

    // Час прошёл: выплатить из запаса и дать поработать обозному хирургу.
    [HarmonyPatch(typeof(HumaniodUnit), "TickHealthAndMorale")]
    internal static class Tick_Mending_Patch
    {
        private static void Postfix(HumaniodUnit __instance, float hourPassed)
        {
            try
            {
                Mending.Drip(__instance, hourPassed);
            }
            catch
            {
            }

            try
            {
                Mending.Tend(__instance, hourPassed);
            }
            catch
            {
            }
        }
    }
}
