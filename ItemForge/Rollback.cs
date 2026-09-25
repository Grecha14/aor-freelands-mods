using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// A way back from a broken save, and no way back from a lost fight.
    ///
    /// Железный человек — это одно сохранение и никаких перезагрузок, и в этом весь смысл. Но
    /// он же означает, что испорченный файл или падение игры стоят всего прохождения, а это уже
    /// не суровость правил, а поломка.
    ///
    /// Поэтому копии складываются **вне игровой папки сохранений**. Игра их не видит и не
    /// перечисляет: внутри неё загрузиться по-прежнему некуда, и железный человек остаётся
    /// железным. Вернуться можно только выйдя из игры и положив копию на место руками.
    ///
    /// И копия берётся не каждый раз, а раз в несколько записей. Это не экономия места, а
    /// правило: откат всегда отстаёт на несколько сохранений, и отменить им один неудачный бой
    /// нельзя — придётся потерять и всё, что было после. Спасти после краха он позволяет,
    /// переиграть поражение — нет.
    /// </summary>
    internal static class Rollback
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Every;
        internal static ConfigEntry<int> Keep;
        internal static ConfigEntry<bool> SkipShots;
        internal static ConfigEntry<bool> IronOnly;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Rollback", "Enabled", true,
                "Keep copies of the save outside the game's own save folder, so a crash or a "
                + "corrupted file does not cost the whole run. The game cannot see them: there "
                + "is still nothing to load from inside it.");

            Every = config.Bind("Rollback", "Every", 5,
                new ConfigDescription(
                    "A copy is taken every this many saves. Five, so a rollback always costs "
                    + "several saves' worth of progress — enough that nobody will use it to undo "
                    + "a lost fight, little enough to be worth having after a crash.",
                    new AcceptableValueRange<int>(1, 100)));

            Keep = config.Bind("Rollback", "Keep", 2,
                new ConfigDescription(
                    "How many copies to keep. Two: the last one, and the one before it in case "
                    + "the last was already taken after the damage was done.",
                    new AcceptableValueRange<int>(1, 20)));

            SkipShots = config.Bind("Rollback", "SkipShots", true,
                "Leave the screenshot out of the copy. It is a good half of a save's twelve "
                + "megabytes and nothing depends on it.");

            IronOnly = config.Bind("Rollback", "IronOnly", false,
                "Only for an ironman run. Прежде стояло «да»: считалось, что прочим довольно "
                + "игровых сохранений. Но копия эта не про перезагрузку после неудачи, а про "
                + "испорченный файл и вылет посреди записи — от такого не спасает и десяток "
                + "слотов, потому что портится именно последний. Оттого теперь всем");
        }

        private static int made;
        private static bool read;

        private static string Home
        {
            get { return Path.Combine(Application.persistentDataPath, "Откат"); }
        }

        private static string Counter
        {
            get { return Path.Combine(BepInEx.Paths.ConfigPath, "aor.rollback.txt"); }
        }

        /// <summary>Puts a copy aside, every so many saves.</summary>
        internal static void Save(SaveLoadManager slm)
        {
            if (!Enabled.Value || slm == null) return;

            try
            {
                if (IronOnly.Value
                    && (slm.archiveProfile == null || !slm.archiveProfile.isIronMan))
                {
                    return;
                }

                string who = slm.archive;
                if (string.IsNullOrEmpty(who)) return;

                Load();

                made++;
                Write();

                int every = Mathf.Max(1, Every.Value);
                if (made % every != 0) return;

                string from = Path.Combine(slm.SavePath, who);
                from = Path.Combine(from, "SaveData");
                from = Path.Combine(from, slm.saveDir);

                if (!Directory.Exists(from))
                {
                    ItemForgePlugin.Log.LogWarning($"Откат: сохранения нет там, где ожидалось — {from}");
                    return;
                }

                string nest = Path.Combine(Home, who);
                Directory.CreateDirectory(nest);

                // Копии крутятся по кругу и называются по порядку, а не по времени: так
                // разобраться в них можно и без нашего файла, просто глядя в папку.
                Rotate(nest);

                string to = Path.Combine(nest, "1");
                Copy(from, to);

                ItemForgePlugin.Log.LogInfo($"Откат: копия снята после {made}-го сохранения — "
                    + $"«{to}».");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог снять копию для отката: " + e.Message);
            }
        }

        // Самая старая уходит, остальные сдвигаются на номер вниз, первое место освобождается.
        private static void Rotate(string nest)
        {
            int keep = Mathf.Max(1, Keep.Value);

            string oldest = Path.Combine(nest, keep.ToString(CultureInfo.InvariantCulture));
            if (Directory.Exists(oldest)) Directory.Delete(oldest, true);

            for (int i = keep - 1; i >= 1; i--)
            {
                string was = Path.Combine(nest, i.ToString(CultureInfo.InvariantCulture));
                if (!Directory.Exists(was)) continue;

                string now = Path.Combine(nest, (i + 1).ToString(CultureInfo.InvariantCulture));
                Directory.Move(was, now);
            }
        }

        private static void Copy(string from, string to)
        {
            Directory.CreateDirectory(to);

            foreach (string file in Directory.GetFiles(from))
            {
                string name = Path.GetFileName(file);

                if (SkipShots.Value && name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                File.Copy(file, Path.Combine(to, name), true);
            }

            foreach (string nook in Directory.GetDirectories(from))
            {
                Copy(nook, Path.Combine(to, Path.GetFileName(nook)));
            }
        }

        private static void Load()
        {
            if (read) return;
            read = true;

            try
            {
                if (!File.Exists(Counter)) return;

                int was;
                if (int.TryParse(File.ReadAllText(Counter).Trim(), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out was))
                {
                    made = was;
                }
            }
            catch
            {
            }
        }

        private static void Write()
        {
            try { File.WriteAllText(Counter, made.ToString(CultureInfo.InvariantCulture)); }
            catch { }
        }
    }

    // Копия снимается после записи, а не до: до неё копировать нечего, а в случае сорванной
    // записи не появится и копии сорванного.
    [HarmonyPatch(typeof(SaveLoadManager), "SaveAll")]
    internal static class SaveAll_Rollback_Patch
    {
        private static void Postfix(SaveLoadManager __instance)
        {
            try { Rollback.Save(__instance); }
            catch { }
        }
    }
}
