using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using DuloGames.UI;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Writes out every trait the game holds, so new ones can be earned from the real list.
    ///
    /// Черты лежат не в коде, а в ассетах: из исходника видно, что они есть и как устроены, но
    /// не видно ни одной по имени. Предлагать «за тысячу убитых мечом — вот такую черту», не
    /// зная списка, значит гадать; поэтому сперва список, а решения после.
    ///
    /// Пишется один раз за запуск, рядом с описями вещей и существ, и тем же порядком: имя,
    /// какого рода черта, чего требует, что даёт. Дальше по этому файлу и выбираем.
    /// </summary>
    internal static class Traits
    {
        internal static ConfigEntry<bool> Census;

        private static bool written;

        internal static void Bind(ConfigFile config)
        {
            Census = config.Bind("Traits", "Census", true,
                "Write every trait in the game to a file beside the settings, once at start. "
                + "Traits live in the game's assets rather than in its code, so there is no "
                + "other way to see the list.");
        }

        internal static void Write()
        {
            if (written || !Census.Value) return;

            try
            {
                UITalentDatabase book;
                try { book = UITalentDatabase.Instance; }
                catch { return; }

                if (book == null) return;

                written = true;

                StringBuilder text = new StringBuilder();
                text.AppendLine(string.Join("\t", new string[]
                {
                    "имя", "ID", "название", "род", "вид", "очки", "ветка",
                    "нужен уровень", "нужно мастерство", "оружие", "раса",
                    "прибавки", "описание"
                }));

                int rows = 0;

                rows += Put(text, book.traits, "черта");
                rows += Put(text, book.inbornTraits, "врождённая");
                rows += Put(text, book.talents, "талант");
                rows += Put(text, book.masteryTalents, "мастерство");

                string where = Path.Combine(BepInEx.Paths.ConfigPath, "aor.traits.tsv");
                File.WriteAllText(where, text.ToString(), new UTF8Encoding(true));

                ItemForgePlugin.Log.LogInfo($"Опись черт: {rows} записей, «{where}».");
            }
            catch (Exception e)
            {
                written = true;
                ItemForgePlugin.Log.LogWarning("Не смог выписать черты: " + e.Message);
            }
        }

        private static int Put(StringBuilder text, UITalentInfo[] list, string kind)
        {
            if (list == null) return 0;

            int rows = 0;

            foreach (UITalentInfo mark in list)
            {
                if (mark == null) continue;

                text.AppendLine(string.Join("\t", new string[]
                {
                    Flat(mark.name),
                    mark.ID.ToString(),
                    Flat(mark.Name),
                    kind,
                    mark.traitType.ToString(),
                    mark.traitPoint.ToString(),
                    mark.talentClass.ToString(),
                    mark.RequireLevel.ToString(),
                    mark.RequireMastery.ToString(),
                    Weapons(mark.RequireWeapon),
                    mark.raceRequire.ToString(),
                    Gifts(mark.addAttrs),
                    Flat(mark.description)
                }));

                rows++;
            }

            return rows;
        }

        private static string Weapons(WeaponType[] kinds)
        {
            if (kinds == null || kinds.Length == 0) return "";

            List<string> said = new List<string>();
            foreach (WeaponType one in kinds) said.Add(one.ToString());

            return string.Join("|", said.ToArray());
        }

        private static string Gifts(List<AddonAttributes> gifts)
        {
            if (gifts == null || gifts.Count == 0) return "";

            List<string> said = new List<string>();

            foreach (AddonAttributes one in gifts)
            {
                if (one == null) continue;
                said.Add($"{one.type} {one.value:0.###}"
                    + (Mathf.Approximately(one.levelAlter, 0f) ? "" : $"+{one.levelAlter:0.###}/ур"));
            }

            return string.Join("|", said.ToArray());
        }

        private static string Flat(string what)
        {
            if (string.IsNullOrEmpty(what)) return "";

            return what.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
