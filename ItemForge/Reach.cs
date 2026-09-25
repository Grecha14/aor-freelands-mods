using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using DuloGames.UI;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// How far each weapon reaches past the hand that holds it.
    ///
    /// Точка удара — это не кисть, а конец оружия. У кинжала они почти совпадают, у алебарды
    /// расходятся на два метра, и мерить высоту удара по кисти значит для древкового ошибаться
    /// на полтуловища, причём всегда в одну сторону.
    ///
    /// Держать в руках каждую вещь ради этого не нужно. Длина записана в самой модели: у
    /// каждого предмета есть свой образец, и его габариты читаются без всякой экипировки.
    /// Направление же, вдоль которого оружие торчит из руки, у всех одно — его задаёт узел, на
    /// который игра его вешает, — и снимается один раз с чего угодно.
    ///
    /// Дальше остриё любой вещи в любом замахе считается само: точка узла плюс её длина вдоль
    /// этой оси. У безоружного длина равна нулю, и точка удара совпадает с кулаком — тот же
    /// счёт, без особого случая.
    /// </summary>
    internal static class Reach
    {
        internal static ConfigEntry<KeyCode> Key;
        internal static ConfigEntry<bool> Rewrite;

        internal static void Bind(ConfigFile config)
        {
            Key = config.Bind("Reach", "Key", KeyCode.F9,
                "Press this in game to measure the length of every weapon model in the world. "
                + "It loads four hundred odd models one after another and takes a few seconds, "
                + "so it is a key rather than something done at every start; the answer is "
                + "written to a file and read from there afterwards. Not F8: that one loads the "
                + "game, and a measurement that throws away the fight it was measuring is no "
                + "measurement at all.");

            Rewrite = config.Bind("Reach", "Rewrite", false,
                "Measure again even when the file already holds the answer.");
        }

        private static string Ledger
        {
            get { return Path.Combine(BepInEx.Paths.ConfigPath, "aor.reach.tsv"); }
        }

        private static readonly Dictionary<int, float> lengths = new Dictionary<int, float>();
        private static bool read;

        /// <summary>How far this weapon reaches past the hand, in metres.</summary>
        internal static float Of(UIItemInfo thing)
        {
            if (thing == null) return 0f;

            Load();

            float much;
            return lengths.TryGetValue(thing.ID, out much) ? much : 0f;
        }

        internal static void Sweep()
        {
            try
            {
                UIItemDatabase book = UIItemDatabase.Instance;
                if (book == null || book.items == null)
                {
                    ItemForgePlugin.Log.LogWarning("Длины: база предметов ещё не поднята.");
                    return;
                }

                if (!Rewrite.Value && File.Exists(Ledger))
                {
                    Load();
                    ItemForgePlugin.Log.LogInfo("Длины уже сняты, записей " + lengths.Count
                        + ". Чтобы перемерить, включите Reach.Rewrite.");
                    return;
                }

                lengths.Clear();

                int done = 0, failed = 0;

                foreach (UIItemInfo thing in book.items)
                {
                    UIWeaponInfo blade = thing as UIWeaponInfo;
                    if (blade == null) continue;

                    float much = Measure(blade);

                    if (much <= 0f) { failed++; continue; }

                    lengths[blade.ID] = much;
                    done++;
                }

                read = true;
                Save();

                ItemForgePlugin.Log.LogInfo("Длины сняты: измерено " + done + ", не далось "
                    + failed + ". Записано в «" + Ledger + "».");

                GameController.ShowMessage("Длины оружия: " + done, 3f);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Замер длин не вышел: " + e);
            }
        }

        /// <summary>The longest span of a weapon's own model, in metres.</summary>
        private static float Measure(UIWeaponInfo blade)
        {
            try
            {
                GameObject model = blade.ItemPrefab;
                if (model == null) return 0f;

                Bounds box = new Bounds();
                bool any = false;

                // Габариты берём из самого меша, а не из отрисовщика: образец лежит в памяти
                // невыставленным, и мировых границ у него ещё нет.
                foreach (MeshFilter part in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (part == null || part.sharedMesh == null) continue;

                    Bounds mine = part.sharedMesh.bounds;
                    mine.center = part.transform.localPosition + mine.center;

                    if (!any) { box = mine; any = true; }
                    else box.Encapsulate(mine);
                }

                foreach (SkinnedMeshRenderer part in
                         model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (part == null || part.sharedMesh == null) continue;

                    Bounds mine = part.sharedMesh.bounds;
                    mine.center = part.transform.localPosition + mine.center;

                    if (!any) { box = mine; any = true; }
                    else box.Encapsulate(mine);
                }

                if (!any) return 0f;

                Vector3 size = box.size;

                return Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            }
            catch
            {
                return 0f;
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
                    if (cells.Length < 2) continue;

                    int id;
                    float much;

                    if (!int.TryParse(cells[0], out id)) continue;
                    if (!float.TryParse(cells[1], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out much)) continue;

                    lengths[id] = much;
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог прочитать длины: " + e.Message);
            }
        }

        private static void Save()
        {
            try
            {
                StringBuilder text = new StringBuilder();

                foreach (KeyValuePair<int, float> one in lengths)
                {
                    text.Append(one.Key).Append('\t')
                        .Append(one.Value.ToString("0.###", CultureInfo.InvariantCulture))
                        .AppendLine();
                }

                File.WriteAllText(Ledger, text.ToString());
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог записать длины: " + e.Message);
            }
        }
    }
}
