using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;

namespace ItemForge
{
    /// <summary>
    /// Fills the gaps the game leaves in its own Russian text.
    ///
    /// Some attribute labels have no Russian entry, so the tooltip falls back to the raw
    /// localisation key or to English — that is why the set showed
    /// "Tooltip_Buff_NegativeSpellDamage" and "Negative Penetration". This substitutes them.
    ///
    /// Deliberately a plain dictionary lookup and nothing else: the mod that used to translate
    /// this game ran hundreds of string replacements on every piece of text it saw and cost
    /// about a fifth of the frame rate. A miss here costs one failed hash lookup.
    /// </summary>
    internal static class Localize
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Extra;

        private static readonly Dictionary<string, string> Words =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // тьма и свет
                { "Tooltip_Buff_NegativeSpellDamage", "Урон тьмой" },
                { "Tooltip_Buff_PositiveSpellDamage", "Урон светом" },
                { "Negative Penetration", "Пробивание тьмы" },
                { "Positive Penetration", "Пробивание света" },
                { "Negative Resistance", "Сопротивление тьме" },
                { "Positive Resistance", "Сопротивление свету" },

                // прочие стихии, на случай если всплывут в описаниях
                { "Tooltip_Buff_FlameSpellDamage", "Урон огнём" },
                { "Tooltip_Buff_ColdSpellDamage", "Урон холодом" },
                { "Tooltip_Buff_ElectricSpellDamage", "Урон электричеством" },
                { "Tooltip_Buff_PoisonSpellDamage", "Урон ядом" },
                { "Tooltip_Buff_SharpSpellDamage", "Режущий урон заклинаний" },
                { "Tooltip_Buff_BluntSpellDamage", "Тупой урон заклинаний" },
                { "Tooltip_Buff_StabSpellDamage", "Колющий урон заклинаний" },

                { "Sharp Penetration", "Пробивание режущего" },
                { "Blunt Penetration", "Пробивание тупого" },
                { "Stab Penetration", "Пробивание колющего" },
                { "Flame Penetration", "Пробивание огня" },
                { "Cold Penetration", "Пробивание холода" },
                { "Electric Penetration", "Пробивание электричества" },
                { "Poison Penetration", "Пробивание яда" },
            };

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Localize", "Enabled", true,
                "Substitute Russian text where the game has none, so modded tooltips do not show "
                + "raw keys or English.");

            Extra = config.Bind("Localize", "Extra", "",
                "Further substitutions of your own, written as key=перевод and separated by "
                + "semicolons, for example Some_Key=Мой текст;Other_Key=Другой текст.");

            LoadExtra();
        }

        private static void LoadExtra()
        {
            if (string.IsNullOrEmpty(Extra.Value)) return;

            foreach (string pair in Extra.Value.Split(';'))
            {
                string entry = pair.Trim();
                if (entry.Length == 0) continue;

                int split = entry.IndexOf('=');
                if (split <= 0 || split == entry.Length - 1)
                {
                    ItemForgePlugin.Log.LogWarning($"Не разобрал замену '{entry}', ожидается ключ=перевод.");
                    continue;
                }

                Words[entry.Substring(0, split).Trim()] = entry.Substring(split + 1).Trim();
            }

            ItemForgePlugin.Log.LogInfo($"Словарь замен: {Words.Count} записей.");
        }

        internal static bool TryGet(string key, out string value)
        {
            value = null;
            if (!Enabled.Value || string.IsNullOrEmpty(key)) return false;
            return Words.TryGetValue(key, out value);
        }
    }

    [HarmonyPatch(typeof(gameManager), "LocalizedString", new[] { typeof(string), typeof(bool), typeof(bool) })]
    internal static class LocalizedString_Patch
    {
        // Postfix rather than prefix: the game gets its own chance first, and only a result
        // that came back as the untranslated key is replaced.
        private static void Postfix(string s, ref string __result)
        {
            string replacement;

            // Сначала по исходному ключу, затем по тому, что вернула игра: недостающий
            // перевод она отдаёт либо самим ключом, либо английским текстом.
            if (Localize.TryGet(s, out replacement)) { __result = replacement; return; }
            if (Localize.TryGet(__result, out replacement)) __result = replacement;
        }
    }
}
