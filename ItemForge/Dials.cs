using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// The game's own dials, turned from the inside instead of pushed from the outside.
    ///
    /// Всякую свою величину эта игра собирает по одному правилу: берётся то, с чем существо
    /// родилось, к нему прибавляется набранное, и произведение кладётся в поле, которым
    /// дальше пользуются все. Здоровье — «(BShp + HPMD) × hpPercentMD». Шаг — «BSms ×
    /// MovementSpeedMD × TimeSpeedMD».
    ///
    /// Прежде мы ждали, покуда она сочтёт, и переписывали итог. Это дурно по двум причинам.
    ///
    /// Первая: по итогу она тут же правит и текущее. Предел здоровья вырос — доливает разницу
    /// в жизнь, упал — срезает. К мигу, когда нас пускают, эта правка уже случилась, и
    /// случилась по её числу, а не по нашему. Распутывать её приходилось обратным счётом, в
    /// котором старое правило поминалось на каждом шагу, — а всякий такой счёт рано или
    /// поздно сходится не туда. У зверя он сходился к полному запасу: волк лечился сам собой
    /// и не умирал.
    ///
    /// Вторая: наш итог жил ровно до её следующего пересчёта, а пересчитывает она часто.
    ///
    /// Оттого итога мы больше не трогаем. Своё число кладётся в ту самую ручку, из которой
    /// итог считается, — и дальше она считает наше сама, своим порядком и со всеми своими
    /// последствиями. Старое правило при этом нигде не поминается: оно просто получает на
    /// вход другое.
    /// </summary>
    internal static class Dials
    {
        private static FieldInfo hpmd;
        private static FieldInfo percent;
        private static FieldInfo pace;
        private static bool looked;

        private static void Look()
        {
            if (looked) return;
            looked = true;

            try
            {
                hpmd = AccessTools.Field(typeof(UnitAttribute), "HPMD");
                percent = AccessTools.Field(typeof(UnitAttribute), "hpPercentMD");
                pace = AccessTools.Field(typeof(UnitAttribute), "MovementSpeedMD");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не нашёл игровых ручек: " + e.Message);
            }
        }

        private static float Read(FieldInfo what, UnitAttribute who, float ifNot)
        {
            try
            {
                if (what == null || who == null) return ifNot;

                object got = what.GetValue(who);
                return got is float ? (float)got : ifNot;
            }
            catch
            {
                return ifNot;
            }
        }

        // ----------------------------------------------------------------- здоровье

        /// <summary>Сколько игра собиралась положить в предел здоровья.</summary>
        internal static float Meant(UnitAttribute who)
        {
            Look();

            if (who == null || who.info == null) return 0f;

            float share = Read(percent, who, 1f);
            if (share <= 0f) share = 1f;

            return (who.info.BShp + Read(hpmd, who, 0f)) * share;
        }

        /// <summary>
        /// Кладёт наш предел здоровья в игровую ручку — так, чтобы она сама вывела его.
        ///
        /// Зовётся до того, как она сочтёт: тогда и предел выйдет наш, и правка текущей жизни
        /// сделается по нашему числу. Долю от зелий и чар не трогаем — она множитель, и
        /// делится на неё именно затем, чтобы игра домножила обратно.
        /// </summary>
        internal static void Holds(UnitAttribute who, float whole)
        {
            Look();

            if (hpmd == null || who == null || who.info == null || whole <= 0f) return;

            try
            {
                float share = Read(percent, who, 1f);
                if (share <= 0f) share = 1f;

                hpmd.SetValue(who, whole / share - who.info.BShp);
            }
            catch
            {
            }
        }

        // ----------------------------------------------------------------- шаг

        /// <summary>Во сколько раз игра собиралась ускорить или замедлить шаг.</summary>
        internal static float Quickness(UnitAttribute who)
        {
            Look();

            return Read(pace, who, 1f);
        }

        /// <summary>
        /// Кладёт наш шаг в игровую ручку.
        ///
        /// Общее замедление времени («TimeSpeedMD») остаётся при ней: оно ко всему порядку
        /// слагаемых отношения не имеет и должно домножиться поверх нашего.
        /// </summary>
        internal static void Walks(UnitAttribute who, float speed)
        {
            Look();

            if (pace == null || who == null || who.info == null || speed <= 0f) return;

            try
            {
                float born = who.info.BSms;
                if (born <= 0.0001f) return;

                pace.SetValue(who, speed / born);
            }
            catch
            {
            }
        }

        // ----------------------------------------------------------------- замах и дыхание

        private static FieldInfo swing;
        private static FieldInfo breath;

        private static void More(ref FieldInfo what, string name, UnitAttribute who, float times)
        {
            if (who == null || times <= 0f || Mathf.Abs(times - 1f) < 0.0001f) return;

            try
            {
                if (what == null) what = AccessTools.Field(typeof(UnitAttribute), name);
                if (what == null) return;

                object got = what.GetValue(who);
                if (!(got is float)) return;

                what.SetValue(who, (float)got * times);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Во столько раз быстрее замах.
        ///
        /// Кладём в «AttackSpeedMD», а не в готовое «attackspeed», и разница тут не в чистоте.
        /// Готовое — это среднее по рукам, а бьют не средним: у каждого оружия своё время
        /// удара, и считается оно из той же ручки. Домножая среднее, мы меняли подпись в окне
        /// и не меняли самого удара.
        /// </summary>
        internal static void Swings(UnitAttribute who, float times)
        {
            More(ref swing, "AttackSpeedMD", who, times);
        }

        /// <summary>И во столько раз шире дыхание: «maxsp = (BSsp + SPMD) × spPercentMD».</summary>
        internal static void Breathes(UnitAttribute who, float times)
        {
            More(ref breath, "spPercentMD", who, times);
        }
    }
}
