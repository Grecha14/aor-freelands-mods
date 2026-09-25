using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// How fast a man takes a mending, which is a thing about the man and not about the bandage.
    ///
    /// Игра лечит всех одинаково. Зелье возвращает свои сорок очков и здоровяку, и доходяге, а
    /// множитель исцеления в ней есть — `HealMD`, — но читается он с того, кто лечит, а не с
    /// того, кого лечат: умелый лекарь льёт одно и то же зелье вдвое щедрее.
    ///
    /// Между тем срастается не зелье, а человек. Выносливость и без того держит восстановление
    /// в секунду — двадцатую долю очка здоровья и десятую запаса сил за очко, — и не держала
    /// только то, ради чего к лекарю и идут: сколько к тебе вернётся от перевязки, зелья или
    /// заклинания.
    ///
    /// Речь здесь про очки жизни. Полоса ран, которую лечит лекарь, живёт отдельно и считается
    /// в <see cref="Mending"/>.
    /// </summary>
    internal static class Knit
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> PerPoint;
        internal static ConfigEntry<float> Fight;
        internal static ConfigEntry<float> Regrown;
        internal static ConfigEntry<string> Kinds;
        internal static ConfigEntry<float> Wild;
        internal static ConfigEntry<bool> Hourly;
        internal static ConfigEntry<float> Spread;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Knit", "Enabled", true,
                "Let endurance decide how much of a healing a man actually takes. The game reads "
                + "its healing multiplier off the healer and never off the wounded, so a draught "
                + "mends the frail exactly as well as the hale.");

            PerPoint = config.Bind("Knit", "PerPoint", 0.01f,
                new ConfigDescription(
                    "What one point of endurance adds to everything that mends him, as a share. "
                    + "A hundredth: at fifty a bandage is worth half again, at a hundred twice. "
                    + "Only what comes from outside — draughts, bandages, spells, the surgeon. "
                    + "What his own body does in the meantime the game already reckons by the "
                    + "same attribute, a twentieth of a point of health a second.",
                    new AcceptableValueRange<float>(0f, 0.05f)));

            Fight = config.Bind("Knit", "Fight", 0.01f,
                new ConfigDescription(
                    "What is left of a man's own healing while he is in a fight, as a share. A "
                    + "hundredth — that is, nothing.\n\n"
                    + "Out of a fight the body works as the game says: a twentieth of a point "
                    + "of health a second for every point of endurance, and a man with fifty of "
                    + "it is whole again in a couple of minutes. In a fight that same rule "
                    + "healed a wolf faster than a swordsman could open it, and no blow ever "
                    + "added up to anything. Wounds close afterwards, not between blows.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Regrown = config.Bind("Knit", "Regrown", 0.25f,
                new ConfigDescription(
                    "And what is left of it for the kinds that regrow by nature. A quarter: a "
                    + "troll closes wounds while the fight is still going on, which is the whole "
                    + "of what a troll is, but he does it four times slower than he does at "
                    + "rest — otherwise there is no killing him at all.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Kinds = config.Bind("Knit", "Kinds", "troll,ogre,ogr,giant",
                "Which kinds regrow by nature, matched against the name of the creature's own "
                + "kind as a piece of it. Trolls in this game come as Troll, MountainTroll and "
                + "Troll_Armored, and one word catches all three.");

            Hourly = config.Bind("Knit", "Hourly", true,
                "Reckon what the body does for itself by the game's hour instead of by the "
                + "second.\n\n"
                + "Игра кладёт двадцатую долю очка здоровья в секунду за очко выносливости — "
                + "то есть полное выздоровление за пару минут стояния на месте. Теперь то же "
                + "самое кладётся за игровой час, а час на местности идёт две с половиной "
                + "минуты. Рана перестала быть делом одной передышки: чтобы зажило само, надо "
                + "прожить с этим день.");

            Spread = config.Bind("Knit", "Spread", 30f,
                new ConfigDescription(
                    "Over how many seconds a healing from outside is poured in, instead of "
                    + "arriving whole at the moment of the gulp.\n\n"
                    + "Тридцать: зелье больше не отменяет полученный удар, оно его переживает. "
                    + "Выпитое посреди драки успеет влить малую часть, выпитое после — всё. "
                    + "Так лечение становится делом времени, а не кнопкой: рану закрывают лекарь, "
                    + "аптечка и белая магия, и всем им нужно это время.",
                    new AcceptableValueRange<float>(0f, 300f)));

            Telling = config.Bind("Knit", "Telling", false,
                "Write out the first few times the body's own healing is cut, with the numbers: "
                + "who, how much the game wanted to give and how much was left of it.");

            Wild = config.Bind("Knit", "Wild", 2f,
                new ConfigDescription(
                    "How much longer a bone takes to knit in such a creature by itself than it "
                    + "takes a man whose bone was set. Twice: a troll needs no surgeon, only "
                    + "patience. Others need the surgeon.",
                    new AcceptableValueRange<float>(1f, 10f)));
        }

        /// <summary>Кому именно сейчас считается своё восстановление.</summary>
        internal static UnitAttribute Ticking;

        private static string[] kinds;
        private static string kindsRead;

        /// <summary>Whether this creature is one of the kinds that close their own wounds.</summary>
        internal static bool Regrows(UnitAttribute who)
        {
            if (Kinds == null || who == null) return false;

            try
            {
                string written = Kinds.Value ?? "";

                if (written != kindsRead || kinds == null)
                {
                    kindsRead = written;
                    kinds = written.Split(',');

                    for (int i = 0; i < kinds.Length; i++)
                    {
                        kinds[i] = kinds[i].Trim().ToLowerInvariant();
                    }
                }

                string name = who.info != null && who.info.name != null
                    ? who.info.name.ToLowerInvariant() : "";

                if (name.Length == 0) return false;

                foreach (string one in kinds)
                {
                    if (one.Length > 0 && name.Contains(one)) return true;
                }
            }
            catch
            {
            }

            return false;
        }

        /// <summary>Whether this one has somebody on him right now.</summary>
        internal static bool Fighting(UnitAttribute who)
        {
            try
            {
                if (who == null) return false;
                if (who.engagedEnemy != null && who.engagedEnemy.Count > 0) return true;

                return who.Target != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>How much of his own healing this one gets right now.</summary>
        internal static float Flowing(UnitAttribute who)
        {
            if (Enabled == null || !Enabled.Value) return 1f;

            float much = 1f;

            // Час вместо секунды. Игра зовёт этот счёт секундами местности, а час на ней идёт
            // полторы сотни секунд — на столько и делим.
            if (Hourly != null && Hourly.Value)
            {
                float hour = 150f;

                try
                {
                    if (TimeManager.LOCAL_MAP_HOUR_IN_SECOND > 1f)
                    {
                        hour = TimeManager.LOCAL_MAP_HOUR_IN_SECOND;
                    }
                }
                catch
                {
                }

                much /= hour;
            }

            if (!Fighting(who)) return much;

            return much * (Regrows(who) ? Regrown.Value : Fight.Value);
        }

        // ----------------------------------------------------------------- по каплям

        private sealed class Drip
        {
            internal float left;
            internal float rate;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
            UnitAttribute, Drip> drips =
            new System.Runtime.CompilerServices.ConditionalWeakTable<UnitAttribute, Drip>();

        /// <summary>Своя же выплата: её срезать нельзя, она и так растянута.</summary>
        internal static bool Paying;

        /// <summary>Takes a healing in and gives it back slowly.</summary>
        internal static void Pour(UnitAttribute who, float much, float speed = 1f)
        {
            if (who == null || much <= 0f) return;

            Drip mine;
            if (!drips.TryGetValue(who, out mine))
            {
                mine = new Drip();
                drips.Add(who, mine);
            }

            // Зелья не складываются: сильное заменяет слабое, слабое при сильном не пьётся
            // вовсе. Сравниваем с тем, что ещё не влилось, — допить остаток слабого поверх
            // сильного было бы тем же сложением с другого конца.
            if (much < mine.left) return;

            mine.left = much;
            mine.rate = much * Mathf.Max(0.1f, speed) / Mathf.Max(0.1f, Spread.Value);
        }

        /// <summary>And gives it, a second at a time.</summary>
        internal static void Flow(UnitAttribute who, float seconds)
        {
            if (who == null || seconds <= 0f) return;

            Drip mine;
            if (!drips.TryGetValue(who, out mine) || mine.left <= 0f) return;

            float much = Mathf.Min(mine.left, mine.rate * seconds);
            if (much <= 0f) return;

            mine.left -= much;
            if (mine.left <= 0.001f) { mine.left = 0f; mine.rate = 0f; }

            Paying = true;

            try
            {
                who.RestoreHP(much);
            }
            finally
            {
                Paying = false;
            }
        }

        /// <summary>What this man's own constitution makes of a mending.</summary>
        internal static float Worth(UnitAttribute who)
        {
            if (Enabled == null || !Enabled.Value || PerPoint.Value <= 0f) return 1f;

            try
            {
                HumaniodUnit man = who as HumaniodUnit;
                if (man == null || man.Data == null) return 1f;

                return Mathf.Max(0.1f, 1f + man.Endurance * PerPoint.Value);
            }
            catch
            {
                return 1f;
            }
        }
    }

    // Своё восстановление в секунду идёт через эти два места: одно в живом бою, другое в
    // пошаговом. Помечаем, что считается именно оно, — чтобы не срезать заодно и зелье,
    // выпитое посреди драки.
    [HarmonyPatch(typeof(UnitAttribute), "TickHPSPMP")]
    internal static class Tick_Knit_Patch
    {
        private static void Prefix(UnitAttribute __instance) { Knit.Ticking = __instance; }

        private static void Postfix(UnitAttribute __instance, float timePass)
        {
            Knit.Ticking = null;

            // И заодно вливаем то, что человек выпил: понемногу, покуда не кончится.
            try
            {
                Knit.Flow(__instance, timePass);
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "EndTurn")]
    internal static class Turn_Knit_Patch
    {
        private static void Prefix(UnitAttribute __instance) { Knit.Ticking = __instance; }

        private static void Postfix(UnitAttribute __instance)
        {
            Knit.Ticking = null;

            // В пошаговом бою секунды идут ходами, и вливать надо здесь: «TickHPSPMP» в нём
            // не работает вовсе, и выпитое зелье иначе застряло бы до конца боя.
            try
            {
                Knit.Flow(__instance, TurnBaseManager.turnDuration);
            }
            catch
            {
            }
        }
    }

    // И вот здесь оно срезается — только оно.
    [HarmonyPatch(typeof(UnitAttribute), "RestoreHP")]
    internal static class Restore_Knit_Patch
    {
        private static int told;

        private static void Prefix(UnitAttribute __instance, ref float restoreAmount)
        {
            if (restoreAmount <= 0f || Knit.Paying) return;
            if ((object)Knit.Ticking != (object)__instance) return;

            try
            {
                float part = Knit.Flowing(__instance);
                float was = restoreAmount;

                restoreAmount *= part;

                if (Knit.Telling != null && Knit.Telling.Value && told < 20)
                {
                    told++;

                    ItemForgePlugin.Log.LogInfo($"Своё заживление «{__instance.Data?.unitname}»: "
                        + $"игра давала {was:0.###}, осталось {restoreAmount:0.####} "
                        + $"(доля {part:0.#####}, в бою {Knit.Fighting(__instance)}, "
                        + $"зарастает {Knit.Regrows(__instance)}).");
                }
            }
            catch
            {
            }
        }
    }

    // Всё, что лечит со стороны, проходит здесь: зелья, повязки, заклинания белой школы,
    // лекарь. Здесь же оно и растягивается: целиком в тот же миг больше не приходит ничего.
    [HarmonyPatch(typeof(UnitAttribute), "Heal")]
    internal static class Heal_Knit_Patch
    {
        private static bool Prefix(UnitAttribute __instance, float restoreAmount)
        {
            if (restoreAmount <= 0f) return true;

            try
            {
                float much = restoreAmount * Knit.Worth(__instance);

                if (Knit.Enabled == null || !Knit.Enabled.Value || Knit.Spread == null
                    || Knit.Spread.Value <= 0f)
                {
                    return true;
                }

                Knit.Pour(__instance, much);

                // Игре здесь делать больше нечего: всё, что она хотела влить, мы взяли на себя.
                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}
