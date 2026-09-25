using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using spell;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Mending a body, and the days it takes.
    ///
    /// Городской доктор в игре — это строка в разговоре. Выбрал спутника, и все его травмы
    /// сняты тут же, разом, бесплатно по времени. Перелом ноги лечится ровно столько же,
    /// сколько ссадина, то есть нисколько, и вся ветка медицины ни на что не влияет.
    ///
    /// А в лазарете отряда у игры уже сделано правильно: у врача есть уровень, у лечения —
    /// скорость, «сто процентов плюс тридцать три за ступень». Числа готовые, и незачем
    /// выдумывать свои — берём их и для городского доктора.
    ///
    /// Себя лечишь — время перематывается: экран гаснет, часы уходят вперёд, встаёшь
    /// здоровым. Спутника — остаётся у доктора, и травмы сойдут в свой час.
    /// </summary>
    internal static class Healing
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> PerInjury;
        internal static ConfigEntry<int> Doctor;

        // Пока это правда, лечение идёт своим ходом и перехватывать его не надо.
        private static bool letting;

        private sealed class Care
        {
            internal int who;
            internal double due;
        }

        private static readonly List<Care> beds = new List<Care>();

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Healing", "Enabled", true,
                "Let treatment take time. The game clears every injury the instant the "
                + "conversation ends, which is why nobody has ever asked how good the doctor is.");

            PerInjury = config.Bind("Healing", "PerInjury", 6f,
                new ConfigDescription(
                    "Game hours a doctor of no skill needs for one injury. His skill divides "
                    + "this by the game's own cure speed — a hundred percent, and thirty-three "
                    + "more for every step of his craft.",
                    new AcceptableValueRange<float>(0.1f, 240f)));

            Doctor = config.Bind("Healing", "Doctor", 10,
                new ConfigDescription(
                    "How skilled to count a town doctor. Ten is the figure the game itself "
                    + "gives its towns, so a town doctor works four times faster than a man "
                    + "with no training at all.",
                    new AcceptableValueRange<int>(0, 50)));
        }

        /// <summary>How long this one's hurts will take to mend.</summary>
        internal static float Hours(UnitAttribute who)
        {
            if (who == null || who.buffmanger == null) return 0f;

            List<BuffBase> hurts = who.buffmanger.FindBuffsofType(bufftype.injury);
            if (hurts == null || hurts.Count == 0) return 0f;

            int skill = Mathf.Max(0, Doctor.Value);

            // Скорость лечения игры: сто процентов и по тридцать три за ступень.
            float speed = (TroopClinicManager.CureSpeedBase
                + skill * TroopClinicManager.CureSpeedLvAdd) / 100f;

            return hurts.Count * PerInjury.Value / Mathf.Max(0.1f, speed);
        }

        /// <summary>Lays a companion up until his hour comes.</summary>
        private static void Lay(UnitAttribute who, float hours)
        {
            if (who == null || who.Data == null) return;

            double now = (TimeManager.Instance != null)
                ? TimeManager.Instance.currentTimeInHours : 0.0;

            foreach (Care bed in beds)
            {
                if (bed.who == who.Data.id) return;
            }

            beds.Add(new Care { who = who.Data.id, due = now + hours });

            ItemForgePlugin.Log.LogInfo($"«{who.Data.unitname}» оставлен у доктора: "
                + $"{hours:0.#} ч. Забирать после.");

            GameController.ShowMessage(who.Data.unitname);
        }

        /// <summary>
        /// Lets go whoever has lain long enough.
        ///
        /// Проверяется по часам мира, а не по тику: часы идут и в пути, и во сне, и при
        /// перемотке, и лежащий у доктора выздоравливает всё это время.
        /// </summary>
        internal static void Round()
        {
            if (!Enabled.Value || beds.Count == 0) return;

            double now = (TimeManager.Instance != null)
                ? TimeManager.Instance.currentTimeInHours : 0.0;

            for (int i = beds.Count - 1; i >= 0; i--)
            {
                if (now < beds[i].due) continue;

                UnitAttribute who = Whom(beds[i].who);
                beds.RemoveAt(i);

                if (who == null) continue;

                letting = true;
                try { who.CureInjuries(-1); }
                finally { letting = false; }

                ItemForgePlugin.Log.LogInfo($"«{who.Data.unitname}» вылечен у доктора.");
                GameController.ShowMessage(who.Data.unitname);
            }
        }

        private static UnitAttribute Whom(int id)
        {
            try
            {
                if (PartyManager.instance == null || PartyManager.instance.partyMembers == null)
                {
                    return null;
                }

                foreach (HumaniodUnit one in PartyManager.instance.partyMembers)
                {
                    if (one != null && one.Data != null && one.Data.id == id)
                    {
                        return (UnitAttribute)(object)one;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        /// <summary>True when this is the one you are playing.</summary>
        private static bool Yours(UnitAttribute who)
        {
            return who != null && (object)who == (object)gameManager.currentplayUnit;
        }

        /// <summary>Decides whether this cure happens now, later, or after a fade.</summary>
        internal static bool Intercept(UnitAttribute who)
        {
            if (!Enabled.Value || letting || who == null) return true;

            float hours = Hours(who);
            if (hours <= 0f) return true;

            if (Yours(who))
            {
                // Сам себя не оставишь — значит пережидаешь. Тем же способом, каким игра
                // пережидает ковку: экран гаснет, время уходит вперёд.
                ItemForgePlugin.Log.LogInfo($"«{who.Data.unitname}» лечится сам: "
                    + $"{hours:0.#} ч, время перематывается.");

                Workshop.Skip(hours);
                return true;
            }

            Lay(who, hours);
            return false;
        }
    }

    // Единственное место, где травмы снимаются: и разговор с доктором, и лазарет, и всё
    // прочее приходит сюда. Значит и решать, сколько это займёт, надо здесь.
    [HarmonyPatch(typeof(UnitAttribute), "CureInjuries")]
    internal static class CureInjuries_Patch
    {
        private static bool Prefix(UnitAttribute __instance)
        {
            try
            {
                return Healing.Intercept(__instance);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог отмерить время лечения: " + e.Message);
                return true;
            }
        }
    }

    // Час прошёл — значит кому-то из лежащих пора вставать.
    [HarmonyPatch(typeof(HumaniodUnit), "OnHourPass")]
    internal static class OnHourPass_Healing_Patch
    {
        private static void Postfix()
        {
            Healing.Round();
        }
    }
}
