using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Keeps the blow-by-blow account of a fight without making the fight unplayable.
    ///
    /// Разбор боя писался в общий журнал игры, а тот раздаёт каждую строку всем приёмникам —
    /// консоли, файлу, окну разработчика — и сбрасывает её на диск немедленно. На одном ударе
    /// это четыре-пять таких строк, в большой драке — тысячи в секунду, и кадры падают до
    /// семи. Подробность была нужна, цена оказалась непомерной.
    ///
    /// Здесь строки копятся в памяти и уходят на диск пачками, в свой отдельный файл. Диск
    /// дёргается раз в несколько сотен записей вместо каждой, а читать потом проще: в файле
    /// только бой, без чужих сообщений вперемешку.
    /// </summary>
    internal static class Chronicle
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Batch;
        internal static ConfigEntry<float> Pause;

        private static readonly List<string> waiting = new List<string>();
        private static string file;
        private static bool broken;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Chronicle", "Enabled", true,
                "Keep the blow-by-blow account in a file of its own rather than in the game's "
                + "log. Same detail, a hundredth of the cost, and afterwards there is a page "
                + "with nothing on it but the fight.");

            Pause = config.Bind("Chronicle", "Pause", 3f,
                new ConfigDescription(
                    "Seconds of real time between one flush to disk and the next. Lines "
                    + "gather in memory in between, so a fight can be read while it is still "
                    + "going on instead of after it ends.",
                    new AcceptableValueRange<float>(0.2f, 120f)));

            Batch = config.Bind("Chronicle", "Batch", 400,
                new ConfigDescription(
                    "How many lines to gather before touching the disk. Higher is cheaper and "
                    + "risks losing the last few lines if the game is killed outright.",
                    new AcceptableValueRange<int>(1, 5000)));
        }

        /// <summary>Puts a line aside, and empties the pile when it grows.</summary>
        internal static void Say(string line)
        {
            if (broken || !Enabled.Value || line == null) return;

            waiting.Add(line);

            if (waiting.Count >= Batch.Value) Flush();
        }

        /// <summary>Writes what has gathered, and forgets it.</summary>
        internal static void Flush()
        {
            if (broken || waiting.Count == 0) return;

            try
            {
                if (file == null)
                {
                    file = Path.Combine(BepInEx.Paths.ConfigPath, "aor.combat.log");

                    // Новый запуск — новый разбор. Дописывать к вчерашнему бою бессмысленно:
                    // в нём другие правила и другие числа.
                    File.WriteAllText(file, "Разбор боя, "
                        + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + Environment.NewLine,
                        Encoding.UTF8);
                }

                File.AppendAllLines(file, waiting, Encoding.UTF8);
                waiting.Clear();
            }
            catch (Exception e)
            {
                // Один раз пожалуемся и замолчим навсегда: разбор боя не та вещь, ради которой
                // стоит сыпать ошибками в каждом кадре.
                broken = true;
                waiting.Clear();

                ItemForgePlugin.Log.LogError("Не смог вести разбор боя, выключаю: " + e.Message);
            }
        }

        private static float last;

        /// <summary>
        /// Empties the pile at a natural pause, so nothing is lost on a quit.
        ///
        /// Раньше этот метод существовал, но его никто не звал, и разбор уходил на диск
        /// только когда накапливалось четыреста строк. Пока идёт бой, посмотреть на него
        /// было нельзя: он весь лежал в памяти. Теперь пауза наступает сама — раз в
        /// несколько секунд, если есть что писать.
        /// </summary>
        internal static void Settle()
        {
            if (waiting.Count == 0) return;

            float now = Time.realtimeSinceStartup;
            if (now - last < Pause.Value) return;

            last = now;
            Flush();
        }
    }
}
