using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using PixelCrushers.DialogueSystem;

namespace DemonLook
{
    /// <summary>
    /// Takes the pleasantries out of a demon's mouth.
    ///
    /// Разговор с любым встречным в этой игре предлагает один и тот же набор: похвалить,
    /// поторговать, позвать в отряд, оскорбить, вручить подарок, признаться в любви. Для Короля
    /// Ада половина этого списка — нелепость: существо, которое стража убьёт не спросив, стоит
    /// перед крестьянином и выбирает между комплиментом и признанием в любви.
    ///
    /// Строки эти живут не в коде игры, а в её диалоговой базе — разговор 448, у каждой свой
    /// номер, — и отбираются по номеру, а не по тексту: перевод меняется, номер нет.
    ///
    /// Три вещи не трогаются нарочно. Приглашение в отряд, которое русский перевод зовёт
    /// «приглашением на вечеринку», — без него демон остался бы без спутников навсегда.
    /// Торговля и ремонт — с ним и так мало кто станет говорить. И если вычеркнуть случилось бы
    /// всё, список возвращается целиком: разговор без единого ответа — это не суровость, а
    /// тупик, из которого игрок не выйдет.
    /// </summary>
    internal static class Manners
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> DemonOnly;
        internal static ConfigEntry<string> Hidden;
        internal static ConfigEntry<string> Mute;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Manners", "Enabled", true,
                "Take the kind answers out of a demon's dialogue. He is not somebody who "
                + "compliments a farmer.");

            DemonOnly = config.Bind("Manners", "DemonOnly", true,
                "Only while the party is led by a demon. Off, and it applies to any leader.");

            Hidden = config.Bind("Manners", "Hidden", "448:29,448:161,448:166",
                "Lines struck out of the menu, written as «conversation:entry» and separated by "
                + "commas. The three are the compliment, the gift and the confession of love. "
                + "Numbers rather than text, because the text is translated and the numbers are "
                + "not. Worth knowing before adding to this list: 448:30 is trade, 448:31 is the "
                + "invitation to the party — the squad, not a feast, whatever the Russian "
                + "translation calls it — 448:32 is the insult, 448:48 is introducing yourself, "
                + "448:159 is repair and 448:91 is attacking.");

            Mute = config.Bind("Manners", "Mute", "Persuade",
                "Talents a demon is held to have none of when anyone weighs his words, "
                + "separated by commas. Persuasion by default: nobody is talked round by a King "
                + "of Hell, and what he has instead is fear — his ten points of it come with the "
                + "blood. Nothing is cut out by this; the game simply finds the check failed and "
                + "does not offer the line, exactly as it does for a tongue-tied man. Names as "
                + "the dialogue system knows them: Persuade, Bargain, Intimidate, Insight, "
                + "Scholarly, Mechanics.");
        }

        private static readonly HashSet<long> hidden = new HashSet<long>();
        private static string listed;

        /// <summary>Which lines are struck out, keyed by conversation and entry together.</summary>
        private static HashSet<long> Struck()
        {
            string written = Hidden.Value ?? "";

            if (written != listed)
            {
                listed = written;
                hidden.Clear();

                foreach (string one in written.Split(','))
                {
                    string[] halves = one.Split(':');
                    if (halves.Length != 2) continue;

                    int talk, line;
                    if (!int.TryParse(halves[0].Trim(), out talk)) continue;
                    if (!int.TryParse(halves[1].Trim(), out line)) continue;

                    hidden.Add(Key(talk, line));
                }
            }

            return hidden;
        }

        private static long Key(int talk, int line)
        {
            return ((long)talk << 32) | (uint)line;
        }

        // Кто во главе отряда, за кадр не меняется, а разговор открывается не каждый кадр —
        // но проверка дешёвая, и держать её отдельно ни к чему.
        private static bool Ours()
        {
            if (!DemonOnly.Value) return true;

            PartyManager party = PartyManager.instance;
            HumaniodUnit leader = party != null ? party.leader : null;

            return leader != null && Racial.IsDemon(leader);
        }

        private static readonly List<string> muted = new List<string>();
        private static string mutedRead;

        /// <summary>Tells the dialogue system a demon has none of the talents he should not have.</summary>
        internal static void Silence(HumaniodUnit who)
        {
            if (!Ours()) return;

            // Заодно поправим запугивание. Расовый порог ставится другим постфиксом того же
            // метода, а порядок постфиксов Harmony не обещает — поэтому берём не готовое
            // значение, а считаем то же самое здесь. Тогда неважно, кто из нас двоих успел
            // первым: в диалог уйдёт число с порогом.
            if (who != null && who.Data != null)
            {
                int fear = who.Data.intimidate;

                if (Racial.IsDemon((UnitAttribute)(object)who) && fear < Racial.Intimidate.Value)
                {
                    fear = Racial.Intimidate.Value;
                }

                DialogueLua.SetVariable("Intimidate", fear);
            }

            string written = Mute.Value ?? "";

            if (written != mutedRead)
            {
                mutedRead = written;
                muted.Clear();

                foreach (string one in written.Split(','))
                {
                    string name = one.Trim();
                    if (name.Length > 0) muted.Add(name);
                }
            }

            foreach (string name in muted)
            {
                PixelCrushers.DialogueSystem.DialogueLua.SetVariable(name, 0);
            }
        }

        /// <summary>Strikes the forbidden lines out of a menu that is about to be shown.</summary>
        internal static Response[] Sift(Response[] answers)
        {
            if (!Enabled.Value || answers == null || answers.Length == 0) return answers;

            try
            {
                HashSet<long> struck = Struck();
                if (struck.Count == 0) return answers;

                if (!Ours()) return answers;

                List<Response> kept = new List<Response>(answers.Length);
                List<string> gone = null;

                foreach (Response answer in answers)
                {
                    DialogueEntry line = answer != null ? answer.destinationEntry : null;

                    if (line != null && struck.Contains(Key(line.conversationID, line.id)))
                    {
                        if (gone == null) gone = new List<string>();
                        gone.Add(line.conversationID + ":" + line.id);
                        continue;
                    }

                    kept.Add(answer);
                }

                if (gone == null) return answers;

                // Пустое меню — это тупик, а не суровость: игроку нечем закрыть разговор.
                // Такого не бывает при нынешнем списке, но список правится руками.
                if (kept.Count == 0)
                {
                    DemonLookPlugin.Log.LogWarning("Вычеркнулись все ответы — возвращаю список "
                        + "целиком, иначе из разговора не выйти.");
                    return answers;
                }

                DemonLookPlugin.Log.LogInfo("Из разговора убрано: "
                    + string.Join(", ", gone.ToArray()) + ".");

                return kept.ToArray();
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Не смог убрать строки из разговора: " + e.Message);
                return answers;
            }
        }
    }

    // Место, где список ответов уже собран, но ещё не показан. Правим сам список, а не окно:
    // окон у диалоговой системы несколько, а список один на всех.
    [HarmonyPatch(typeof(ConversationView), "StartResponses",
        new Type[] { typeof(Subtitle), typeof(Response[]) })]
    internal static class StartResponses_Patch
    {
        private static void Prefix(ref Response[] responses)
        {
            responses = Manners.Sift(responses);
        }
    }
    // Навыки уходят в диалог отдельными переменными, и условия строк смотрят на них. Значит
    // заявить, что убеждения у демона нет, дешевле и безопаснее, чем вычёркивать строки: игра
    // сама не покажет то, чьё условие не выполнено, и сама же знает, что делать дальше — путь
    // провала написан для каждой проверки, иначе косноязычный герой не прошёл бы игру.
    //
    // Пишем после игры, а не вместо: пусть она сперва выставит всё как считает нужным.
    [HarmonyPatch(typeof(HumaniodUnit), "CalculateLivingSkills")]
    internal static class LivingSkills_Manners_Patch
    {
        private static void Postfix(HumaniodUnit __instance)
        {
            try
            {
                if (!Manners.Enabled.Value || __instance == null) return;

                // Переменные пишет либо тот, кто в отряде, либо сам играемый. Для прочих игра
                // их не трогает, и нам тем более нечего.
                if (!__instance.inParty && (object)__instance != (object)gameManager.mainCharUnit)
                {
                    return;
                }

                Manners.Silence(__instance);
            }
            catch
            {
            }
        }
    }
}
