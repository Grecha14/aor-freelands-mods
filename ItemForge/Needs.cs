using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;
using DuloGames.UI;
using spell;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// What hunger and sleeplessness actually cost a man.
    ///
    /// Про голод в коде игры видно только половину: расход сытости, три ступени беды и то,
    /// что ниже тридцати начинает падать мораль. А чем именно наказывают ступени — лежит не
    /// в коде, а в данных: «Hungry» и «Fatigue» это готовые описания баффов с набором
    /// прибавок, и прочесть их можно только у запущенной игры.
    ///
    /// Поэтому здесь пока ничего не меняется. Здесь только выписывается наружу то, что есть,
    /// — чтобы решение «поднять штраф вдвое» принималось против настоящих чисел, а не против
    /// моего представления о них.
    /// </summary>
    internal static class Needs
    {
        internal static ConfigEntry<bool> Survey;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> HungerRate;
        internal static ConfigEntry<float> TireRate;
        internal static ConfigEntry<float> LocalClock;

        internal static ConfigEntry<string> HungerRestore;
        internal static ConfigEntry<string> HungerStamina;
        internal static ConfigEntry<string> HungerBody;
        internal static ConfigEntry<string> HungerPace;

        internal static ConfigEntry<string> SleepStamina;
        internal static ConfigEntry<string> SleepHeal;
        internal static ConfigEntry<string> SleepAim;
        internal static ConfigEntry<string> SleepGuard;
        internal static ConfigEntry<string> SleepPace;
        internal static ConfigEntry<string> SleepCost;

        private static bool written;
        private static bool paced;
        private static bool inside;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Needs", "Enabled", true,
                "Let hunger and sleeplessness cost a man something he notices. The game gives "
                + "both three steps and then asks very little at each of them.");

            HungerRate = config.Bind("Needs", "HungerRate", 6f,
                new ConfigDescription(
                    "How fast the belly empties, in points an hour out of a hundred. The game "
                    + "uses one, so a single meal lasts four days and nobody ever thinks about "
                    + "food. Six empties a full belly in about sixteen waking hours, which "
                    + "comes to three meals a day. Resting still costs a quarter of this.",
                    new AcceptableValueRange<float>(0.1f, 50f)));

            LocalClock = config.Bind("Clock", "LocalClock", 24f,
                new ConfigDescription(
                    "How much slower a game hour runs on the tactical map. The game gives an "
                    + "hour a hundred and fifty seconds, so a day in a town or a battle takes "
                    + "an hour of yours and everything measured in game hours flies past. At "
                    + "twenty-four the clock runs one to one with your own: an hour is an "
                    + "hour. Travel on the world map keeps the game's own pace, which is "
                    + "where days are supposed to fall away. One leaves it as it was.",
                    new AcceptableValueRange<float>(1f, 240f)));

            TireRate = config.Bind("Needs", "TireRate", 4f,
                new ConfigDescription(
                    "The same for tiredness. The game uses one, so a man stays on his feet a "
                    + "hundred hours — four days — before he needs a bed. Four puts a night's "
                    + "sleep about every eighteen waking hours, and sooner if the day was "
                    + "spent marching: the game already doubles this while a man is moving "
                    + "or fighting, and stops it entirely while he rests.",
                    new AcceptableValueRange<float>(0.1f, 50f)));

            HungerRestore = config.Bind("Needs", "HungerRestore", "0.25,0.6,1",
                "What share of his healing and his wind a hungry man loses, at each of the "
                + "three steps. At the last of them he mends not at all: an empty man does not "
                + "knit bone.");

            HungerStamina = config.Bind("Needs", "HungerStamina", "0.1,0.3,0.6",
                "What share of endurance and of the stamina pool hunger takes, by step. This "
                + "is the one that bites: on an empty belly two fifths of a man's wind is left "
                + "to him, and it does not come back.");

            HungerBody = config.Bind("Needs", "HungerBody", "0,0.2,0.4",
                "What share of strength, agility and precision hunger takes. Nothing at the "
                + "first step — a man who missed a meal is not yet weaker, only slower.");

            HungerPace = config.Bind("Needs", "HungerPace", "0.1,0.3,0.6",
                "What share of attack speed and movement speed hunger takes, by step.");

            SleepStamina = config.Bind("Needs", "SleepStamina", "0.3,0.7,1",
                "What share of his wind a sleepless man stops recovering, by step.");

            SleepHeal = config.Bind("Needs", "SleepHeal", "0.2,0.5,0.8",
                "What share of his healing a sleepless man loses, by step.");

            SleepAim = config.Bind("Needs", "SleepAim", "0.1,0.3,0.5",
                "What share of precision sleeplessness takes. Tiredness is felt in the hands "
                + "before it is felt in the arms.");

            SleepGuard = config.Bind("Needs", "SleepGuard", "0.15,0.4,0.7",
                "What share of dodge and of the blocking arc sleeplessness takes.");

            SleepPace = config.Bind("Needs", "SleepPace", "0.1,0.25,0.5",
                "What share of attack speed sleeplessness takes.");

            SleepCost = config.Bind("Needs", "SleepCost", "0.15,0.4,0.8",
                "How much dearer every swing and block becomes, by step. A tired man spends "
                + "more on the same work.");

            Survey = config.Bind("Needs", "Survey", true,
                "Write out what every buff in the game actually does, once, at the first "
                + "moment the world exists. Hunger and fatigue keep their numbers in data "
                + "rather than in code, and there is no reading them from the outside.");
        }

        /// <summary>Writes the whole buff shelf to a file, once.</summary>
        internal static void Write()
        {
            if (written || !Survey.Value) return;

            UIBuffDatabase shelf;
            try { shelf = UIBuffDatabase.Instance; }
            catch { return; }

            if (shelf == null || shelf.buffs == null) return;

            written = true;

            try
            {
                List<string> rows = new List<string>();

                rows.Add("id\tимя\tтип\tдлительность\tчасы\tприбавки");

                foreach (UIBuffInfo buff in shelf.buffs)
                {
                    if (buff == null) continue;

                    rows.Add(string.Join("\t", new string[]
                    {
                        buff.id ?? "",
                        buff.buffname ?? "",
                        buff.type.ToString(),
                        buff.duration.ToString("0.##", CultureInfo.InvariantCulture),
                        buff.durationIsHour ? "час" : "сек",
                        Gifts(buff)
                    }));
                }

                string file = Path.Combine(BepInEx.Paths.ConfigPath, "aor.buffs.tsv");
                File.WriteAllLines(file, rows.ToArray(), Encoding.UTF8);

                ItemForgePlugin.Log.LogInfo($"Баффы выписаны: {rows.Count - 1}, «{file}».");

                // И отдельной строкой — те два, ради которых всё это писалось, сразу по
                // ступеням. Так видно, во что обходится пустой живот на каждой из трёх.
                Steps(shelf, "Hungry");
                Steps(shelf, "Fatigue");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог выписать баффы: " + e);
            }
        }

        /// <summary>Everything a buff hands out, as one readable line.</summary>
        private static string Gifts(UIBuffInfo buff)
        {
            if (buff.addAttrs == null || buff.addAttrs.Count == 0) return "нет";

            List<string> parts = new List<string>();

            foreach (AddonAttributes one in buff.addAttrs)
            {
                if (one == null) continue;

                parts.Add(one.type + " " + one.value.ToString("0.###", CultureInfo.InvariantCulture));
            }

            return (parts.Count > 0) ? string.Join(", ", parts.ToArray()) : "нет";
        }

        /// <summary>What one named buff costs at each of its three steps.</summary>
        private static void Steps(UIBuffDatabase shelf, string id)
        {
            UIBuffInfo buff = shelf.GetByID(id);

            if (buff == null)
            {
                ItemForgePlugin.Log.LogWarning($"Баффа «{id}» в базе нет.");
                return;
            }

            ItemForgePlugin.Log.LogInfo($"«{id}» ({buff.buffname}):");

            for (int level = 1; level <= 3; level++)
            {
                List<string> parts = new List<string>();

                if (buff.addAttrs != null)
                {
                    foreach (AddonAttributes one in buff.addAttrs)
                    {
                        if (one == null) continue;

                        AddonAttributes grown = one.CaculateLevelValue(one.type, level);
                        if (grown == null) continue;

                        parts.Add(grown.type + " "
                            + grown.value.ToString("0.###", CultureInfo.InvariantCulture));
                    }
                }

                ItemForgePlugin.Log.LogInfo($"    ступень {level}: "
                    + ((parts.Count > 0) ? string.Join(", ", parts.ToArray()) : "ничего"));
            }
        }

        /// <summary>Sets how fast the belly empties and the legs tire. Once.</summary>
        internal static void Pace()
        {
            if (paced || !Enabled.Value) return;

            paced = true;

            UnitAttribute.HUNGER_LOSE_HOUR_FACTOR = HungerRate.Value;
            UnitAttribute.VIGOR_LOSE_HOUR_FACTOR = TireRate.Value;

            ItemForgePlugin.Log.LogInfo($"Голод идёт по {HungerRate.Value:0.##} в час, "
                + $"усталость по {TireRate.Value:0.##}. "
                + $"Полный живот на {100f / Mathf.Max(0.01f, HungerRate.Value):0.#} часов, "
                + $"силы на {100f / Mathf.Max(0.01f, TireRate.Value):0.#} часов покоя "
                + $"или {50f / Mathf.Max(0.01f, TireRate.Value):0.#} часов дороги.");
        }

        /// <summary>The three steps of a ladder, read from one line of settings.</summary>
        private static float[] Rungs(ConfigEntry<string> written)
        {
            float[] got = new float[3];

            string[] parts = (written.Value ?? "").Split(',');

            for (int i = 0; i < 3; i++)
            {
                float much;
                if (i < parts.Length && float.TryParse(parts[i].Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out much))
                {
                    got[i] = Mathf.Clamp(much, 0f, 1f);
                }
            }

            return got;
        }

        private static float Rung(ConfigEntry<string> ladder, int step)
        {
            float[] rungs = Rungs(ladder);
            return rungs[Mathf.Clamp(step, 1, 3) - 1];
        }

        /// <summary>
        /// Rewrites what one of the two miseries costs this man, as shares of his own self.
        ///
        /// Игра даёт обоим три ступени и на каждой спрашивает фиксированные единицы:
        /// три силы, два ловкости. Для новичка это треть его самого, для ветерана — пустяк,
        /// и чем дальше идёт игра, тем меньше голод значит. Доля же значит одно и то же
        /// для всех и всегда.
        ///
        /// Считается от базовых чисел шаблона и хранимых характеристик, а не от нынешних:
        /// нынешние уже включают этот самый штраф, и счёт пошёл бы по кругу вниз.
        /// </summary>
        internal static void Dress(HumaniodUnit who, string id, int step)
        {
            if (!Enabled.Value || who == null || who.Data == null) return;
            if (who.buffmanger == null || who.info == null) return;
            if (inside) return;

            BuffBase misery = who.buffmanger.FindBuffbyID(id);
            if (misery == null) return;

            HumanAttribute mind = who.Data.humanAttribute;
            if (mind == null) return;

            try
            {
                inside = true;

                List<AddonAttributes> gifts = new List<AddonAttributes>();

                if (id == "Hungry")
                {
                    float mend = Rung(HungerRestore, step);
                    float wind = Rung(HungerStamina, step);
                    float body = Rung(HungerBody, step);
                    float pace = Rung(HungerPace, step);

                    Take(gifts, AddonAttribute.HPrestore, who.info.BShpr * mend);
                    Take(gifts, AddonAttribute.EPrestore, who.info.BSspr * mend);

                    Take(gifts, AddonAttribute.Endurance, Mathf.Round(mind.BSendurance * wind));
                    Take(gifts, AddonAttribute.EP, who.info.BSsp * wind);

                    Take(gifts, AddonAttribute.Strength, Mathf.Round(mind.BSstrength * body));
                    Take(gifts, AddonAttribute.Agility, Mathf.Round(mind.BSagility * body));
                    Take(gifts, AddonAttribute.Precision, Mathf.Round(mind.BSprecision * body));

                    Take(gifts, AddonAttribute.AttackSpeed, pace);
                    Take(gifts, AddonAttribute.MoveSpeed, pace);
                }
                else
                {
                    float wind = Rung(SleepStamina, step);
                    float mend = Rung(SleepHeal, step);
                    float aim = Rung(SleepAim, step);
                    float guard = Rung(SleepGuard, step);
                    float pace = Rung(SleepPace, step);
                    float cost = Rung(SleepCost, step);

                    Take(gifts, AddonAttribute.EPrestore, who.info.BSspr * wind);
                    Take(gifts, AddonAttribute.HPrestore, who.info.BShpr * mend);

                    Take(gifts, AddonAttribute.Precision, Mathf.Round(mind.BSprecision * aim));

                    Take(gifts, AddonAttribute.Dodge, who.info.BSdodge * guard);
                    Take(gifts, AddonAttribute.BlockAngle, guard);

                    Take(gifts, AddonAttribute.AttackSpeed, pace);

                    // Отрицательная экономия — это и есть возросший расход.
                    Take(gifts, AddonAttribute.EPsave, cost);
                    Take(gifts, AddonAttribute.BlockEPsave, cost);
                }

                misery.addAttrs = gifts;
                who.UpdateAttribute();

                if (who == gameManager.currentplayUnit)
                {
                    ItemForgePlugin.Log.LogInfo($"«{who.Data.unitname}», "
                        + (id == "Hungry" ? "голод" : "недосып")
                        + $" ступени {step}: " + Show(gifts));
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог назначить цену нужды: " + e);
            }
            finally
            {
                inside = false;
            }
        }

        /// <summary>Adds one loss, if it amounts to anything at all.</summary>
        private static void Take(List<AddonAttributes> gifts, AddonAttribute what, float much)
        {
            if (much <= 0.0001f) return;

            gifts.Add(new AddonAttributes(what, 0f - much));
        }

        private static string Show(List<AddonAttributes> gifts)
        {
            if (gifts.Count == 0) return "ничего";

            List<string> parts = new List<string>();

            foreach (AddonAttributes one in gifts)
            {
                parts.Add(one.type + " " + one.value.ToString("0.##", CultureInfo.InvariantCulture));
            }

            return string.Join(", ", parts.ToArray());
        }

        /// <summary>Which step a share of a hundred falls on, or none at all.</summary>
        internal static int Step(float share)
        {
            if (share <= 0f) return 3;
            if (share <= 0.25f) return 2;
            if (share <= 0.5f) return 1;

            return 0;
        }
    }

    // Ступень голода игра выставляет сама — мы ждём, пока она это сделает, и переписываем
    // то, во что эта ступень обходится. Сюда же приходит каждый час расхода, так что если
    // что-то сотрёт наши числа, они вернутся сами.
    [HarmonyPatch(typeof(HumaniodUnit), "OnHungerChange")]
    internal static class OnHungerChange_Patch
    {
        private static void Postfix(HumaniodUnit __instance)
        {
            if (!Needs.Enabled.Value || __instance == null || __instance.Data == null) return;

            int step = Needs.Step(__instance.Data.satiety / 100f);
            if (step == 0) return;

            Needs.Dress(__instance, "Hungry", step);
        }
    }

    [HarmonyPatch(typeof(HumaniodUnit), "OnVigorChange")]
    internal static class OnVigorChange_Patch
    {
        private static void Postfix(HumaniodUnit __instance)
        {
            if (!Needs.Enabled.Value || __instance == null || __instance.Data == null) return;

            int step = Needs.Step(__instance.Data.vigor / 100f);
            if (step == 0) return;

            Needs.Dress(__instance, "Fatigue", step);
        }
    }

    /// <summary>
    /// Slows the tactical clock to the pace of the man watching it.
    ///
    /// Час на местной карте идёт сто пятьдесят секунд: сутки в городе проходят за час вашего
    /// времени, и всё, что меряется игровыми часами, проносится мимо. Голод, заживление,
    /// наполнение оберегов, работа кузнеца — всё это задумано в часах, а часов за вечер
    /// набегает столько, что ни одна из этих вещей не успевает ничего значить.
    ///
    /// Здесь час становится часом. Трогается одно место — то, из которого игра берёт длину
    /// часа для хода стрелок: «GameHourInSecounds». Через него же считается и перемотка
    /// времени, так что и она остаётся верной себе.
    ///
    /// А вот «LOCAL_MAP_HOUR_IN_SECOND» не трогается намеренно, хотя название обещает то же
    /// самое. Игра пользуется им в одном-единственном месте и совсем для другого: начисляет
    /// по нему заживление за каждый час пути по карте мира. Подними его — и отряд станет
    /// исцеляться в двадцать четыре раза быстрее просто оттого, что мы замедлили часы в бою.
    /// Два разных смысла в одном поле, и поднимать надо только один из них.
    /// </summary>
    [HarmonyPatch(typeof(TimeManager), "GameHourInSecounds", MethodType.Getter)]
    internal static class GameHourInSecounds_Patch
    {
        private static void Postfix(ref float __result)
        {
            if (!Needs.Enabled.Value) return;

            float slower = Needs.LocalClock.Value;
            if (slower <= 1f) return;

            try
            {
                // В пути по карте мира — как было. Там дни и должны сыпаться горстями.
                if (WorldTravelManager.instance != null) return;

                __result *= slower;
            }
            catch
            {
            }
        }
    }
}
