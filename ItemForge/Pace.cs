using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// How fast a man walks: his own pace, then what hinders him, then what helps.
    ///
    /// Игра складывает всё в одно число. Скорость передвижения — это «базовая скорость породы,
    /// помноженная на единицу плюс сумма всех поправок», и в этой сумме плащ скорохода и
    /// перебитая нога лежат рядом, складываясь и вычитаясь без порядка. Оттого набранные бафы
    /// прикрывают увечье: человек с половиной хода и плащом идёт как здоровый.
    ///
    /// Здесь порядок есть. Сперва своё — у каждой породы свой шаг, и гном не догоняет эльфа
    /// потому, что он гном. Потом отнимается всё, что мешает: груз, рана, лубок, чужое
    /// заклятие. И только от того, что осталось, считается прибавка: конь резв, но хромой конь
    /// резв вполсилы.
    ///
    /// Собственный счёт игры мы при этом не рушим — берём её же число и переставляем в нём
    /// слагаемые: делим на то, что вышло у неё, и множим на то, что вышло у нас. Всё
    /// остальное, чем она множит шаг, остаётся при ней.
    /// </summary>
    internal static class Pace
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Races;
        internal static ConfigEntry<float> Most;
        internal static ConfigEntry<float> Nimble;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Pace", "Enabled", true,
                "Give each kind its own walking pace, and put the reckoning in order: what "
                + "hinders is taken off first, what helps is added to what is left.");

            Races = config.Bind("Pace", "Races",
                "human=4,elf=4.4,dwarf=3.4,bruteman=3.8,lizard=4.2,fairy=4.8,demon=4.2,"
                + "undead=3.4,orc=4.1",
                "The pace each kind is born with, in the game's own measure, where four is what "
                + "it gives everybody now. An elf is quick and light, a dwarf short-legged and "
                + "heavy, the undead do not hurry. A kind that is not written here keeps "
                + "whatever its own creature was made with, so beasts and monsters are left "
                + "alone.");

            Most = config.Bind("Pace", "Most", 0.95f,
                new ConfigDescription(
                    "The most that can ever be taken off the pace by hindrances, as a share. "
                    + "Nineteen twentieths: however broken and however loaded, a man still "
                    + "shifts a little.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Nimble = config.Bind("Pace", "Nimble", 0.0025f,
                new ConfigDescription(
                    "What one point of agility adds to the pace, as a share. The game gives a "
                    + "half of a hundredth; this is half of that.\n\n"
                    + "Правило это писано под потолок в девяносто девять, где больше половины "
                    + "прибавки не выходило никогда. С потолком в девять сотен оно перестаёт "
                    + "быть прибавкой и становится способом передвижения: при ловкости в сто с "
                    + "лишним человек идёт вдвое быстрее себя, а дальше — быстрее лошади. "
                    + "Половинная цена возвращает ловкости смысл прибавки.",
                    new AcceptableValueRange<float>(0f, 0.05f)));

            Telling = config.Bind("Pace", "Telling", false,
                "Write out the first few reckonings of pace.");
        }

        // Что мешает и что помогает — порознь, по бойцу. Складывается пока игра пересчитывает.
        private sealed class Sum
        {
            internal float down;
            internal float up;
        }

        private static readonly Dictionary<int, Sum> sums = new Dictionary<int, Sum>();

        private static Sum Of(UnitAttribute who)
        {
            int id = who.GetInstanceID();

            Sum mine;
            if (!sums.TryGetValue(id, out mine))
            {
                mine = new Sum();
                sums[id] = mine;
            }

            return mine;
        }

        /// <summary>Начало пересчёта: суммы обнуляются вместе с игровыми.</summary>
        internal static void Start(UnitAttribute who)
        {
            if (Enabled == null || !Enabled.Value || who == null) return;

            Sum mine = Of(who);
            mine.down = 0f;
            mine.up = 0f;
        }

        /// <summary>Каждая прибавка к ходу ложится в свою сторону.</summary>
        internal static void Counted(UnitAttribute who, List<AddonAttributes> addon, float factor)
        {
            if (Enabled == null || !Enabled.Value || who == null || addon == null) return;

            try
            {
                Sum mine = Of(who);

                foreach (AddonAttributes one in addon)
                {
                    if (one == null || one.type != AddonAttribute.MoveSpeed) continue;

                    float much = one.value * factor;

                    if (much < 0f) mine.down += -much;
                    else mine.up += much;
                }
            }
            catch
            {
            }
        }

        private static readonly Dictionary<string, float> paces = new Dictionary<string, float>();
        private static string pacesRead;

        /// <summary>The pace this kind is born with, or nought when it is not written.</summary>
        private static float Born(UnitAttribute who)
        {
            try
            {
                string written = Races.Value ?? "";

                if (written != pacesRead)
                {
                    pacesRead = written;
                    paces.Clear();

                    foreach (string one in written.Split(','))
                    {
                        int split = one.IndexOf('=');
                        if (split <= 0) continue;

                        float much;
                        if (float.TryParse(one.Substring(split + 1).Trim(), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out much))
                        {
                            paces[one.Substring(0, split).Trim().ToLowerInvariant()] = much;
                        }
                    }
                }

                if (who.info == null) return 0f;

                float got;
                return paces.TryGetValue(who.info.race.ToString().ToLowerInvariant(), out got)
                    ? got : 0f;
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>
        /// Помеха ходу: перебитая нога, сломанные рёбра, груз сверх сил.
        ///
        /// Кладётся в общий счёт, а не поверх готового шага. Разница не в красоте: игра
        /// собирает шаг из «BSms × MovementSpeedMD», и всё, что домножается на готовое,
        /// живёт до её ближайшего пересчёта — а пересчитывает она часто. Что положено в счёт,
        /// то она посчитает сама и сохранит.
        /// </summary>
        internal static void Hinder(UnitAttribute who, float share)
        {
            if (Enabled == null || !Enabled.Value || who == null || share <= 0f) return;

            Of(who).down += share;
        }

        /// <summary>И подмога: плащ скорохода, резвость породы, чужое доброе слово.</summary>
        internal static void Help(UnitAttribute who, float share)
        {
            if (Enabled == null || !Enabled.Value || who == null || share <= 0f) return;

            Of(who).up += share;
        }

        private static int told;

        /// <summary>
        /// Собирает шаг и кладёт его в игровую ручку.
        ///
        /// Сперва своё — у каждой породы свой. Потом отнимается всё, что мешает: груз, рана,
        /// лубок, чужое заклятие. И только от того, что осталось, считается прибавка: конь
        /// резв, но хромой конь резв вполсилы.
        ///
        /// Прежде здесь стояло «maxSpeed умножить на наше и разделить на вышедшее у неё» —
        /// счёт поверх счёта, живший до первого её пересчёта. Теперь в «MovementSpeedMD»
        /// ложится наша доля, и шаг она выводит сама. Общее замедление времени остаётся при
        /// ней и домножается поверх, как и должно.
        /// </summary>
        internal static void Walk(UnitAttribute who)
        {
            if (Enabled == null || !Enabled.Value || who == null || who.info == null) return;

            try
            {
                Sum mine = Of(who);

                float born = Born(who);
                if (born <= 0f) born = who.info.BSms;
                if (born <= 0f) return;

                // Ловкость игра кладёт мимо этого счёта, прямо в свою долю, — а это подмога,
                // и ей место среди подмог. Цену ей назначаем свою: игровая писана под потолок,
                // которого больше нет.
                float up = mine.up;

                HumaniodUnit man = who as HumaniodUnit;
                if (man != null) up += man.Agility * Nimble.Value;

                float down = Mathf.Clamp(mine.down, 0f, Most.Value);

                float ours = born * (1f - down) * (1f + up);
                if (ours <= 0f) return;

                if (Telling.Value && told < 20)
                {
                    told++;

                    ItemForgePlugin.Log.LogInfo($"Шаг «{who.Data?.unitname}»: своё {born:0.##}, "
                        + $"помехи {down * 100f:0}%, подмога {up * 100f:0}% → {ours:0.##} "
                        + $"(у игры выходило {who.info.BSms * Dials.Quickness(who):0.##}).");
                }

                Dials.Walks(who, ours);
            }
            catch
            {
            }
        }
    }

    // Начало пересчёта: игра обнуляет свои доли, мы свои.
    //
    // Именно здесь, а не позже. Прежде обнуление висело на «WriteUnitAttribute» — а к тому
    // мигу прибавки с вещей и меток уже собраны шагом раньше, в «CaculateAllBonus», и
    // обнуление стирало их все. Оттого в счёт шага не попадала ни одна помеха: ни вес
    // доспеха, ни перегруз, ни голод. Человек в латах ходил как налегке, и это было не
    // послабление, а потерянное слагаемое.
    [HarmonyPatch(typeof(UnitAttribute), "InitializeModifier")]
    internal static class Write_Pace_Start_Patch
    {
        private static void Prefix(UnitAttribute __instance)
        {
            try
            {
                Pace.Start(__instance);
            }
            catch
            {
            }
        }
    }

    // Через это место проходит всякая прибавка от вещей, зелий, чар и талантов.
    [HarmonyPatch(typeof(UnitAttribute), "CountBonus")]
    internal static class CountBonus_Pace_Patch
    {
        private static void Prefix(UnitAttribute __instance, List<AddonAttributes> addon,
            float factor)
        {
            try
            {
                Pace.Counted(__instance, addon, factor);
            }
            catch
            {
            }
        }
    }

    // Весь счёт — в одном месте и в твёрдом порядке.
    //
    // Сперва предел здоровья: человеку по частям тела, зверю по туше. Потом помехи ходу —
    // выбитые ноги, переломы, резвость породы. И только после всего собранного шаг ложится в
    // игровую ручку. Порядок между отдельными заплатами Harmony не обещает, оттого они здесь
    // и собраны в одну.
    [HarmonyPatch(typeof(UnitAttribute), "WriteUnitAttribute")]
    internal static class Reckon_Patch
    {
        private static void Prefix(UnitAttribute __instance)
        {
            try { Limb.Vessel(__instance); } catch { }
            try { Beastly.Vessel(__instance); } catch { }

            try { Limb.Hinders(__instance); } catch { }
            try { Beastly.Hastens(__instance); } catch { }

            try { Pace.Walk(__instance); } catch { }
        }
    }
}
