using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;

namespace DemonLook
{
    /// <summary>
    /// Writes down every place the game can open, so a start can be chosen by name.
    ///
    /// Which areas exist is not in the code — scenes are data, and the one thing the code
    /// holds is three names it happens to load by hand. Twice already an invented name has
    /// cost a whole run of the game (a mage who was not called WildMage, a spell that was not
    /// called BlackFog), and both times the answer came from asking the game to list what it
    /// really has. This does the same for places, in advance of needing one.
    ///
    /// Two stores are asked, because a scene can live in either: the ones built into the
    /// executable, and the ones the catalogue loads on demand.
    /// </summary>
    internal static class Scenes
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Interesting;

        private static bool told;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Scenes", "ListScenes", true,
                "Write the names of every scene to the log, once. Turn off once a start has been "
                + "chosen: the list is long and only useful while choosing.");

            Interesting = config.Bind("Scenes", "Interesting",
                "cave,cavern,ruin,crypt,tomb,dungeon,temple,shrine,lair,mine,catacomb,grotto",
                "Words that mark a place worth starting a ritual in, separated by commas. Names "
                + "containing any of them are listed first and in full; the rest are counted.");
        }

        internal static void Report()
        {
            if (told || !Enabled.Value) return;
            told = true;

            try
            {
                List<string> likely = new List<string>();
                List<string> rest = new List<string>();

                Sort(FromBuild(), likely, rest);
                Sort(FromCatalogue(), likely, rest);

                DemonLookPlugin.Log.LogInfo($"=== сцены игры: подходящих {likely.Count}, "
                    + $"прочих {rest.Count} ===");

                if (likely.Count > 0)
                {
                    DemonLookPlugin.Log.LogInfo("Похожие на пещеру или руины: "
                        + string.Join(", ", likely.ToArray()));
                }

                // Остальные печатаем частями: одной строкой на сотню имён лог читать нельзя.
                for (int i = 0; i < rest.Count; i += 25)
                {
                    DemonLookPlugin.Log.LogInfo($"Прочие {i + 1}-{Math.Min(i + 25, rest.Count)}: "
                        + string.Join(", ", rest.GetRange(i, Math.Min(25, rest.Count - i)).ToArray()));
                }
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogError("Перебор сцен сорвался: " + e);
            }
        }

        private static void Sort(IEnumerable<string> names, List<string> likely, List<string> rest)
        {
            string[] words = (Interesting.Value ?? "").Split(',');

            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;
                if (likely.Contains(name) || rest.Contains(name)) continue;

                bool wanted = false;

                foreach (string entry in words)
                {
                    string word = entry.Trim();
                    if (word.Length == 0) continue;

                    if (name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        wanted = true;
                        break;
                    }
                }

                (wanted ? likely : rest).Add(name);
            }
        }

        /// <summary>The scenes compiled into the game itself.</summary>
        private static List<string> FromBuild()
        {
            List<string> found = new List<string>();

            int count = SceneManager.sceneCountInBuildSettings;
            for (int i = 0; i < count; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                if (string.IsNullOrEmpty(path)) continue;

                int slash = path.LastIndexOf('/');
                int dot = path.LastIndexOf('.');
                if (dot <= slash) continue;

                found.Add(path.Substring(slash + 1, dot - slash - 1));
            }
            return found;
        }

        /// <summary>
        /// Asks the catalogue what kinds of keys it holds, instead of guessing one.
        ///
        /// Догадка про «.unity» и «scene/» не дала ничего: у каталога своя раскладка, и
        /// угадывать её в третий раз смысла нет. Перепись видов ключей отвечает сразу и
        /// на этот вопрос, и на все будущие — сколько чего есть и как оно называется.
        /// </summary>
        private static List<string> FromCatalogue()
        {
            List<string> found = new List<string>();
            Dictionary<string, int> kinds = new Dictionary<string, int>();

            foreach (UnityEngine.AddressableAssets.ResourceLocators.IResourceLocator locator
                     in Addressables.ResourceLocators)
            {
                if (locator == null || locator.Keys == null) continue;

                foreach (object key in locator.Keys)
                {
                    string text = key as string;
                    if (text == null) continue;

                    int slash = text.IndexOf('/');
                    string kind = slash > 0 ? text.Substring(0, slash) : "(без вида)";

                    int had;
                    kinds[kind] = kinds.TryGetValue(kind, out had) ? had + 1 : 1;

                    // Само место действия ищем по имени, какой бы вид ключа ему ни достался.
                    if (text.IndexOf("brewood", StringComparison.OrdinalIgnoreCase) >= 0
                        || text.IndexOf("scene", StringComparison.OrdinalIgnoreCase) >= 0
                        || text.IndexOf("area", StringComparison.OrdinalIgnoreCase) >= 0
                        || text.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                    {
                        found.Add(text);
                    }
                }
            }

            List<string> census = new List<string>();
            foreach (KeyValuePair<string, int> pair in kinds) census.Add($"{pair.Key}:{pair.Value}");
            census.Sort();

            DemonLookPlugin.Log.LogInfo($"Виды ключей каталога ({kinds.Count}): "
                + string.Join(", ", census.ToArray()));

            return found;
        }
    }
}
