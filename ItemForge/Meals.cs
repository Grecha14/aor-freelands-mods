using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// What a man thinks of the fifteenth bowl of the same porridge.
    ///
    /// В игре у еды есть сытость, бодрость и мораль — и ни у кого нет памяти. Можно кормить
    /// отряд одной солониной месяцами, и никто этого не заметит: каждая порция встречается
    /// как первая.
    ///
    /// Здесь у каждого есть память на неделю. Пока стол разный, всё как было. Но если одно и
    /// то же блюдо попадается слишком часто, каждая следующая порция сбивает настроение, и
    /// чем дальше, тем сильнее. Лечится это не лекарством, а другой едой.
    ///
    /// Память живёт только в этом запуске. В сохранение мод не пишет, а заводить ради каши
    /// отдельный файл с якорем — цена выше пользы: худшее, что даёт забывчивость, это что
    /// после перезагрузки неделя считается заново.
    /// </summary>
    internal static class Meals
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Window;
        internal static ConfigEntry<int> Patience;
        internal static ConfigEntry<float> Bite;
        internal static ConfigEntry<float> Worst;

        private sealed class Bite_
        {
            internal int dish;
            internal int day;
        }

        private static readonly Dictionary<int, List<Bite_>> memory =
            new Dictionary<int, List<Bite_>>();

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Meals", "Enabled", true,
                "Let people tire of the same dish. Without this a company can live on salt "
                + "pork for a year and nobody minds.");

            Window = config.Bind("Meals", "Window", 7,
                new ConfigDescription(
                    "How many days a man remembers what he was fed.",
                    new AcceptableValueRange<int>(1, 60)));

            Patience = config.Bind("Meals", "Patience", 15,
                new ConfigDescription(
                    "How many helpings of one dish inside that window pass without complaint. "
                    + "Every helping past this costs morale, and the cost grows.",
                    new AcceptableValueRange<int>(1, 200)));

            Bite = config.Bind("Meals", "Bite", 1.5f,
                new ConfigDescription(
                    "Morale lost by the first helping past patience. The second past it costs "
                    + "twice this, the third three times, and so on.",
                    new AcceptableValueRange<float>(0.1f, 50f)));

            Worst = config.Bind("Meals", "Worst", 15f,
                new ConfigDescription(
                    "The most one helping can cost, however long the monotony has run.",
                    new AcceptableValueRange<float>(1f, 100f)));
        }

        /// <summary>Remembers a helping, and says what it cost.</summary>
        internal static void Ate(UnitAttribute who, UIItemInfo dish)
        {
            if (!Enabled.Value || who == null || who.Data == null || dish == null) return;

            // Только еда. Зелья, факелы и отмычки памяти не оставляют.
            UIConsumableInfo food = dish as UIConsumableInfo;
            if (food == null || food.hungryRestore <= 0f) return;

            try
            {
                int today = TimeManager.TotalDay;
                int id = who.Data.id;

                List<Bite_> eaten;
                if (!memory.TryGetValue(id, out eaten))
                {
                    eaten = new List<Bite_>();
                    memory[id] = eaten;
                }

                // Забываем то, что старше недели, тут же: иначе список растёт всю игру.
                int since = today - Window.Value;
                eaten.RemoveAll(delegate (Bite_ one) { return one.day < since; });

                int same = 0;
                foreach (Bite_ one in eaten)
                {
                    if (one.dish == dish.ID) same++;
                }

                eaten.Add(new Bite_ { dish = dish.ID, day = today });

                // Язык помнит меньше желудка: за скуку наказывает неделя, а радость от блюда
                // сбивают только последние несколько порций. Одно и то же дважды подряд уже не
                // праздник, даже если в прошлый раз было вкусно.
                bool again = false;
                int look = Mathf.Min(Spirit.Fresh.Value, eaten.Count - 1);

                for (int i = eaten.Count - 2; i >= eaten.Count - 1 - look && i >= 0; i--)
                {
                    if (eaten[i].dish == dish.ID) { again = true; break; }
                }

                Spirit.Fed(who, dish, again);
                Craving.Ate(who, dish);

                int over = same + 1 - Patience.Value;
                if (over <= 0) return;

                float lost = Mathf.Min(Worst.Value, over * Bite.Value);

                NPCSaveData mind = who.Data as NPCSaveData;
                if (mind == null) return;

                mind.AddMorale(0f - lost);

                ItemForgePlugin.Log.LogInfo($"«{who.Data.unitname}» ест "
                    + $"«{dish.Name}» {same + 1}-й раз за {Window.Value} дней: "
                    + $"настроение −{lost:0.#}.");

                if ((object)who == (object)gameManager.currentplayUnit)
                {
                    GameController.ShowMessage(dish.Name);
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог запомнить съеденное: " + e.Message);
            }
        }
    }

    // Единственное место, через которое проходит всякая съеденная вещь.
    [HarmonyPatch(typeof(SpellManager), "OnConsumableUsed")]
    internal static class OnConsumableUsed_Patch
    {
        private static void Postfix(SpellManager __instance, UIItemInfo item)
        {
            try
            {
                if (__instance != null) Meals.Ate(__instance.unit, item);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог учесть съеденное: " + e.Message);
            }
        }
    }
}
