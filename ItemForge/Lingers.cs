using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// The dead lie for a week, and so does their blood.
    ///
    /// Игра хранит тело только того, кто живёт в этой местности: его труп сохраняется вместе с
    /// ней и лежит там всегда. Пришлых — бандитов, путников, отряды с дороги — она стирает,
    /// как только игрок уходит, живых и мёртвых вместе, и поле боя за спиной пустеет в тот же
    /// миг. Кровь живёт и того меньше: до смены местности.
    ///
    /// Здесь правило одно для всех. Всякое тело лежит неделю по игровому времени — от часа
    /// смерти, — а потом его больше нет. Лужи тоже неделю: уходя, мы запоминаем, где они были,
    /// и, вернувшись, находим их на тех же местах, пока неделя не вышла.
    /// </summary>
    internal static class Lingers
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Hours;
        internal static ConfigEntry<int> MaxCorpses;
        internal static ConfigEntry<int> MaxPools;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Lingers", "Enabled", true,
                "Let every body lie for a week of game time, strangers and residents alike, and "
                + "let the blood stay as long.");

            Hours = config.Bind("Lingers", "Hours", 168f,
                new ConfigDescription(
                    "How many game hours a body and its blood stay. A week.",
                    new AcceptableValueRange<float>(1f, 10000f)));

            MaxCorpses = config.Bind("Lingers", "MaxCorpses", 60,
                new ConfigDescription(
                    "How many strangers bodies one place keeps at most. Past that the oldest go "
                    + "first, the way the game took them before.",
                    new AcceptableValueRange<int>(0, 500)));

            MaxPools = config.Bind("Lingers", "MaxPools", 150,
                new ConfigDescription(
                    "How many pools of blood one place remembers.",
                    new AcceptableValueRange<int>(0, 1000)));

            Telling = config.Bind("Lingers", "Telling", true,
                "Say in the log what was kept, cleared and laid back.");
        }

        // ----------------------------------------------------------------- часы, игра, место

        internal static double Now()
        {
            try
            {
                if (TimeManager.Instance == null) return -1d;
                return TimeManager.TotalDay * 24d + TimeManager.Hour
                    + TimeManager.Instance.GameTime.Minutes / 60d;
            }
            catch
            {
                return -1d;
            }
        }

        private static System.Reflection.FieldInfo archiveField;

        private static string Archive()
        {
            try
            {
                if (SaveLoadManager.Instance == null) return "";
                if (archiveField == null) archiveField = AccessTools.Field(typeof(SaveLoadManager), "currentArchive");
                return (archiveField != null ? archiveField.GetValue(SaveLoadManager.Instance) as string : null) ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static string Scene()
        {
            try
            {
                return LoadingOverlay.instance != null ? (LoadingOverlay.instance.CurSceneName ?? "") : "";
            }
            catch
            {
                return "";
            }
        }

        // ----------------------------------------------------------------- журнал

        private sealed class Mark
        {
            internal char kind;          // 'c' тело, 'p' лужа
            internal string archive;
            internal string scene;
            internal int id;             // тело: номер существа; лужа: порядковый
            internal double hour;
            internal Vector3 at;
            internal int blood;
            internal int spray;
            internal float share;
        }

        private static readonly List<Mark> marks = new List<Mark>();
        private static bool read;
        private static bool dirty;
        private static float nextWrite;
        private static int nextPool = 1;

        private static string FilePath()
        {
            return Path.Combine(BepInEx.Paths.ConfigPath, "aor.lingers.tsv");
        }

        private static void Read()
        {
            if (read) return;
            read = true;

            try
            {
                string path = FilePath();
                if (!File.Exists(path)) return;

                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string[] p = line.Split('\t');
                    if (p.Length < 11 || p[0].Length != 1) continue;

                    Mark m = new Mark();
                    m.kind = p[0][0];
                    m.archive = p[1];
                    m.scene = p[2];
                    int.TryParse(p[3], out m.id);
                    double.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out m.hour);

                    float x, y, z;
                    float.TryParse(p[5], NumberStyles.Float, CultureInfo.InvariantCulture, out x);
                    float.TryParse(p[6], NumberStyles.Float, CultureInfo.InvariantCulture, out y);
                    float.TryParse(p[7], NumberStyles.Float, CultureInfo.InvariantCulture, out z);
                    m.at = new Vector3(x, y, z);

                    int.TryParse(p[8], out m.blood);
                    int.TryParse(p[9], out m.spray);
                    float.TryParse(p[10], NumberStyles.Float, CultureInfo.InvariantCulture, out m.share);

                    marks.Add(m);
                    if (m.kind == 'p' && m.id >= nextPool) nextPool = m.id + 1;
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не прочёл журнал тел и крови: " + e.Message);
            }
        }

        private static void Write()
        {
            if (!dirty) return;
            dirty = false;

            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (Mark m in marks)
                {
                    sb.Append(m.kind).Append('\t').Append(m.archive).Append('\t').Append(m.scene).Append('\t')
                      .Append(m.id).Append('\t').Append(m.hour.ToString("0.###", CultureInfo.InvariantCulture)).Append('\t')
                      .Append(m.at.x.ToString("0.###", CultureInfo.InvariantCulture)).Append('\t')
                      .Append(m.at.y.ToString("0.###", CultureInfo.InvariantCulture)).Append('\t')
                      .Append(m.at.z.ToString("0.###", CultureInfo.InvariantCulture)).Append('\t')
                      .Append(m.blood).Append('\t').Append(m.spray).Append('\t')
                      .Append(m.share.ToString("0.###", CultureInfo.InvariantCulture)).Append('\n');
                }

                File.WriteAllText(FilePath(), sb.ToString(), Encoding.UTF8);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не записал журнал тел и крови: " + e.Message);
            }
        }

        private static Mark Corpse(string archive, string scene, int id)
        {
            foreach (Mark m in marks)
            {
                if (m.kind == 'c' && m.id == id && m.archive == archive && m.scene == scene) return m;
            }
            return null;
        }

        private static bool Keeps(UnitAttribute unit)
        {
            if (unit == null || unit.Data == null || unit.info == null) return false;
            if (unit.inParty || unit.Data.team == Faction.player) return false;

            // Тех, кто нужен сюжету, не трогаем: их тело — часть истории места.
            if (unit.info.utype == UnitType.keyNPC || unit.info.utype == UnitType.companion) return false;

            return true;
        }

        // ----------------------------------------------------------------- тела

        /// <summary>Someone has died here: remember when.</summary>
        internal static void Died(UnitAttribute unit)
        {
            if (Enabled == null || !Enabled.Value || !Keeps(unit) || unit.Data.id == 0) return;

            double now = Now();
            if (now < 0d) return;

            Read();

            string archive = Archive(), scene = Scene();
            if (Corpse(archive, scene, unit.Data.id) != null) return;

            marks.Add(new Mark { kind = 'c', archive = archive, scene = scene, id = unit.Data.id, hour = now });
            dirty = true;
        }

        /// <summary>
        /// Leaving: the dead strangers stay, the fresh ones first, as many as a place keeps.
        ///
        /// Игра стирает пришлых перед тем, как сохранить местность. Мёртвых мы у неё забираем
        /// из этого списка — и тогда она сохраняет их вместе со всеми, кто здесь живёт.
        /// </summary>
        internal static void Leaving(AreaManager area)
        {
            if (Enabled == null || !Enabled.Value || area == null || area.guests == null) return;

            try
            {
                double now = Now();
                if (now < 0d) return;

                Read();
                string archive = Archive(), scene = Scene();

                List<KeyValuePair<double, UnitAttribute>> dead = new List<KeyValuePair<double, UnitAttribute>>();

                foreach (UnitAttribute guest in area.guests)
                {
                    if (guest == null || guest.Data == null || !guest.Data.isdead || !Keeps(guest)) continue;

                    Mark m = Corpse(archive, scene, guest.Data.id);
                    double died = m != null ? m.hour : now;

                    if (now - died > Hours.Value) continue;

                    dead.Add(new KeyValuePair<double, UnitAttribute>(died, guest));
                }

                // Свежие вперёд: если мест меньше, чем тел, уходят самые старые.
                dead.Sort((a, b) => b.Key.CompareTo(a.Key));

                int kept = 0;
                foreach (KeyValuePair<double, UnitAttribute> one in dead)
                {
                    if (kept >= MaxCorpses.Value) break;

                    area.DeleteGuest(one.Value);
                    kept++;
                }

                if (Telling.Value && kept > 0)
                {
                    ItemForgePlugin.Log.LogInfo($"Уходя из «{scene}»: тел пришлых оставлено {kept}.");
                }

                Write();
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог оставить тела: " + e.Message);
            }
        }

        // ----------------------------------------------------------------- кровь

        private static bool relaying;

        /// <summary>A pool was laid: remember where.</summary>
        internal static void Pool(Vector3 at, float share, BloodType blood, int spray)
        {
            if (Enabled == null || !Enabled.Value || relaying) return;

            double now = Now();
            if (now < 0d) return;

            Read();

            string archive = Archive(), scene = Scene();

            int here = 0;
            Mark oldest = null;
            foreach (Mark m in marks)
            {
                if (m.kind != 'p' || m.archive != archive || m.scene != scene) continue;
                here++;
                if (oldest == null || m.hour < oldest.hour) oldest = m;
            }

            if (here >= MaxPools.Value && oldest != null) marks.Remove(oldest);

            marks.Add(new Mark
            {
                kind = 'p', archive = archive, scene = scene, id = nextPool++, hour = now,
                at = at, blood = (int)blood, spray = spray, share = share
            });
            dirty = true;
        }

        // ----------------------------------------------------------------- пришли в местность

        private static string settled;
        private static float settleAt;

        internal static void Arrived()
        {
            settled = null;
            settleAt = Time.unscaledTime + 3f;
        }

        /// <summary>A few seconds after arriving: clear the old dead, lay the blood back.</summary>
        internal static void Tick()
        {
            if (Enabled == null || !Enabled.Value) return;

            if (dirty && Time.unscaledTime >= nextWrite)
            {
                nextWrite = Time.unscaledTime + 15f;
                Write();
            }

            if (settleAt <= 0f || Time.unscaledTime < settleAt) return;
            settleAt = 0f;

            try
            {
                AreaManager area = AreaManager.Instance;
                if (area == null || area.characters == null) return;

                double now = Now();
                if (now < 0d) return;

                Read();
                string archive = Archive(), scene = Scene();
                if (settled == scene) return;
                settled = scene;

                // Тела: неделя вышла — тела нет.
                int cleared = 0, counted = 0;
                foreach (UnitAttribute unit in area.characters.ToArray())
                {
                    if (unit == null || unit.Data == null || !unit.Data.isdead || !Keeps(unit)) continue;

                    Mark m = Corpse(archive, scene, unit.Data.id);
                    if (m == null)
                    {
                        marks.Add(new Mark { kind = 'c', archive = archive, scene = scene, id = unit.Data.id, hour = now });
                        dirty = true;
                        counted++;
                        continue;
                    }

                    if (now - m.hour <= Hours.Value) { counted++; continue; }

                    area.DeleteCharacter(unit, true);
                    marks.Remove(m);
                    dirty = true;
                    cleared++;
                }

                // Кровь: что младше недели — на своё место, остальное забыть.
                int laid = 0;
                relaying = true;

                try
                {
                    for (int i = marks.Count - 1; i >= 0; i--)
                    {
                        Mark m = marks[i];
                        if (m.kind != 'p' || m.archive != archive || m.scene != scene) continue;

                        double age = now - m.hour;
                        if (age > Hours.Value || age < 0d) { marks.RemoveAt(i); dirty = true; continue; }

                        float left = (float)((Hours.Value - age) * TimeManager.LOCAL_MAP_HOUR_IN_SECOND);
                        if (Pools.Relay(m.at, m.share, (BloodType)m.blood, m.spray, left)) laid++;
                    }
                }
                finally
                {
                    relaying = false;
                }

                // Сверх того — забыть всё, что старше недели, где бы оно ни было.
                int before = marks.Count;
                marks.RemoveAll(one => now - one.hour > Hours.Value + 24d);
                if (marks.Count != before) dirty = true;

                if (Telling.Value)
                {
                    ItemForgePlugin.Log.LogInfo($"В «{scene}»: тел лежит {counted}, убрано за давностью "
                        + $"{cleared}; луж возвращено {laid}.");
                }

                Write();
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог разобрать тела и кровь местности: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "Die")]
    internal static class Die_Lingers_Patch
    {
        private static void Postfix(UnitAttribute __instance)
        {
            try
            {
                if (__instance != null && __instance.Data != null && __instance.Data.isdead) Lingers.Died(__instance);
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(AreaManager), "LeaveScene")]
    internal static class LeaveScene_Lingers_Patch
    {
        private static void Prefix(AreaManager __instance)
        {
            Lingers.Leaving(__instance);
        }
    }

    [HarmonyPatch(typeof(AreaManager), "Init")]
    internal static class AreaInit_Lingers_Patch
    {
        private static void Postfix()
        {
            Lingers.Arrived();
        }
    }
}
