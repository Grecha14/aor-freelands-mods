using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using HarmonyLib;
using PixelCrushers.DialogueSystem;
using TroopManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ItemForge
{
    /// <summary>
    /// The arena champion can be called out, not only waited for.
    ///
    /// Игра выводит чемпиона арены сама, в свой черёд: когда игрок одолел здесь столько-то
    /// вызовов. Теперь его можно вызвать и самому — там же, где выдают значок гладиатора: в
    /// разговоре появляется ответ «Я хочу бросить вызов чемпиону арены». Вызов ложится на
    /// доску этой арены вместо обычного; чемпион выходит на свой уровень — у каждого свой, от
    /// трёхсот до пятисот, — со своим легендарным снаряжением в награду.
    ///
    /// Побеждённый чемпион больше не выходит — ни по вызову, ни в свой черёд, как и в игре.
    /// Сперва зовётся первый чемпион арены, после его поражения — второй, если он есть.
    ///
    /// Строка дописывается так же, как строка о покупке лошади у трактирщика: в тот миг, когда
    /// разговор открывается, от каждой развилки выбора к нашей строке и от неё обратно; игрок —
    /// тот, чьими словами начат разговор; что делать по выбору, говорит сама строка через Lua.
    /// </summary>
    internal static class Duel
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Menu;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Duel", "Enabled", true,
                "Let the player call out the arena champion from the conversation where the "
                + "gladiator badges are given.");

            Menu = config.Bind("Duel", "Menu", "Я хочу бросить вызов чемпиону арены.",
                "The line the player picks to call out the champion.");

            Telling = config.Bind("Duel", "Telling", true,
                "Say in the log where the line was added and who was called out.");
        }

        private const string Mark = "ItemForgeDuel";
        private const string Function = "ItemForgeChallengeChampion";

        // ------------------------------------------------------------------ строка в разговоре

        private static bool listed;

        /// <summary>Tells the dialogue system what our line does when it is picked.</summary>
        internal static void Register()
        {
            if (listed) return;
            listed = true;

            try
            {
                Lua.RegisterFunction(Function, null, PixelCrushers.DialogueSystem.SymbolExtensions.GetMethodInfo(() => Chosen()));
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Вызов чемпиона: не смог объявить строку в разговоре: " + e);
            }
        }

        /// <summary>The line was chosen — call the champion out.</summary>
        public static void Chosen()
        {
            try { Call(); }
            catch (Exception e) { ItemForgePlugin.Log.LogError("Вызов чемпиона сорвался: " + e); }
        }

        /// <summary>Puts the call-out line into a conversation that hands out gladiator badges.</summary>
        internal static void Offer(Conversation talk)
        {
            if (Enabled == null || !Enabled.Value || talk == null || talk.dialogueEntries == null) return;

            try
            {
                if (talk.dialogueEntries.Find(delegate (DialogueEntry x) { return x != null && x.Title == Mark; }) != null) return;

                DialogueEntry promote = null;
                foreach (DialogueEntry step in talk.dialogueEntries)
                {
                    if (step == null) continue;
                    string seq = (step.Sequence ?? "").Replace(" ", "");
                    string script = (step.userScript ?? "").Replace(" ", "");
                    if (seq.IndexOf("Gladiator(Promote", StringComparison.OrdinalIgnoreCase) >= 0
                        || script.IndexOf("Gladiator(Promote", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        promote = step;
                        break;
                    }
                }
                if (promote == null) return;

                DialogueEntry root = talk.GetFirstDialogueEntry();
                if (root == null) return;

                // Игрок — тот, чьими словами начат разговор: так это устроено в этой игре.
                int mine = root.ActorID;

                DialogueEntry hub;
                DialogueEntry sibling;
                if (!Hub(talk, promote, mine, out hub, out sibling)) return;

                int id = 1;
                foreach (DialogueEntry step in talk.dialogueEntries)
                {
                    if (step != null && step.id >= id) id = step.id + 1;
                }

                DialogueEntry line = Template.FromDefault().CreateDialogueEntry(id, talk.id, Mark);
                line.ActorID = sibling.ActorID;
                line.ConversantID = sibling.ConversantID;
                line.isGroup = sibling.isGroup;
                line.DialogueText = Menu.Value;
                line.MenuText = Menu.Value;
                line.conditionsString = "";
                line.userScript = Function + "()";

                hub.outgoingLinks.Add(new Link(talk.id, hub.id, talk.id, line.id));
                line.outgoingLinks.Add(new Link(talk.id, line.id, talk.id, hub.id));
                talk.dialogueEntries.Add(line);

                if (Telling.Value)
                {
                    ItemForgePlugin.Log.LogInfo($"Вызов чемпиона: строка дописана в разговор «{talk.Title}» "
                        + $"(строка {id}, развилка {hub.id}, образец «{sibling.DialogueText}»).");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Вызов чемпиона: не смог дописать разговор: " + e);
            }
        }

        /// <summary>
        /// The fork nearest above the badge: the line after which the player chooses what to say.
        /// Её дети — ответы игрока; к ним и прибавляется наш.
        /// </summary>
        private static bool Hub(Conversation talk, DialogueEntry from, int mine, out DialogueEntry hub, out DialogueEntry sibling)
        {
            hub = null;
            sibling = null;

            Dictionary<int, List<DialogueEntry>> parents = new Dictionary<int, List<DialogueEntry>>();
            foreach (DialogueEntry step in talk.dialogueEntries)
            {
                if (step == null || step.outgoingLinks == null) continue;
                foreach (Link link in step.outgoingLinks)
                {
                    if (link == null || link.destinationConversationID != talk.id) continue;
                    List<DialogueEntry> list;
                    if (!parents.TryGetValue(link.destinationDialogueID, out list))
                    {
                        list = new List<DialogueEntry>();
                        parents[link.destinationDialogueID] = list;
                    }
                    list.Add(step);
                }
            }

            Queue<KeyValuePair<DialogueEntry, int>> open = new Queue<KeyValuePair<DialogueEntry, int>>();
            HashSet<int> seen = new HashSet<int>();
            open.Enqueue(new KeyValuePair<DialogueEntry, int>(from, 0));
            seen.Add(from.id);

            while (open.Count > 0)
            {
                KeyValuePair<DialogueEntry, int> at = open.Dequeue();
                DialogueEntry here = at.Key;

                if (here.ActorID != mine && here.outgoingLinks != null)
                {
                    DialogueEntry first = null;
                    int answers = 0;
                    foreach (Link link in here.outgoingLinks)
                    {
                        if (link == null || link.destinationConversationID != talk.id) continue;
                        DialogueEntry child = talk.GetDialogueEntry(link.destinationDialogueID);
                        if (child == null || child.ActorID != mine) continue;
                        answers++;
                        if (first == null) first = child;
                    }

                    if (answers >= 2)
                    {
                        hub = here;
                        sibling = first;
                        return true;
                    }
                }

                if (at.Value >= 6) continue;

                List<DialogueEntry> up;
                if (!parents.TryGetValue(here.id, out up)) continue;
                foreach (DialogueEntry parent in up)
                {
                    if (parent == null || !seen.Add(parent.id)) continue;
                    open.Enqueue(new KeyValuePair<DialogueEntry, int>(parent, at.Value + 1));
                }
            }

            return false;
        }

        // ------------------------------------------------------------------ сам вызов

        private static readonly HashSet<int> beaten = new HashSet<int>();

        internal static bool Beaten(NPCSaveData hero)
        {
            return hero != null && beaten.Contains(hero.id);
        }

        private static void Say(string said)
        {
            try { GameController.ShowLocalizedMessage(said); } catch { }
            try { if (ChatManager.instance != null) ChatManager.instance.AddSystemMessage(said); } catch { }
        }

        private static Arena Here()
        {
            if (ArenaMatchManager.instance == null || ArenaMatchManager.instance.arenas == null) return null;

            string scene = SceneManager.GetActiveScene().name ?? "";
            Arena found = null;

            foreach (Arena one in ArenaMatchManager.instance.arenas)
            {
                if (one == null || one.isPlayerArena || one.isGortusShrine) continue;
                if (string.Equals(one.connectScene, scene, StringComparison.OrdinalIgnoreCase))
                {
                    if (found == null || (found.champion == null && one.champion != null)) found = one;
                }
            }

            return found;
        }

        private static bool Contains(ArenaMatch m, NPCSaveData hero)
        {
            if (m == null || m.teams == null || hero == null) return false;
            foreach (GladiatorTeam team in m.teams)
            {
                if (team == null || team.members == null) continue;
                foreach (NPCSaveData one in team.members) if ((object)one == (object)hero) return true;
            }
            return false;
        }

        /// <summary>Calls out the champion of the arena the player stands in.</summary>
        internal static void Call()
        {
            Sync();

            Arena arena = Here();
            if (arena == null)
            {
                Say("Здесь нет арены, чей чемпион принял бы вызов.");
                return;
            }

            // Кого уже одолели в свой черёд, тот считается побеждённым и здесь.
            if (arena.champion != null && arena.beatenByPlayer > 3 + arena.rank) beaten.Add(arena.champion.id);
            if (arena.champion2 != null && arena.beatenByPlayer > 4 + arena.rank) beaten.Add(arena.champion2.id);

            NPCSaveData hero = null;
            int step = 0;

            if (arena.champion != null && !Beaten(arena.champion))
            {
                hero = arena.champion;
                step = 3 + arena.rank;
            }
            else if (arena.champion2 != null && !Beaten(arena.champion2)
                && arena.champion2.heroCareer != null && arena.champion2.heroCareer.heroType != HeroType.Freeman)
            {
                hero = arena.champion2;
                step = 4 + arena.rank;
            }

            if (hero == null)
            {
                Say($"Чемпионов арены «{Name(arena)}» больше не осталось: все они побеждены.");
                return;
            }

            ArenaMatch old = arena.challenge;
            if (old != null)
            {
                if (Contains(old, hero))
                {
                    Say($"«{hero.unitname}» уже ждёт вас на арене.");
                    return;
                }

                if (old.isStarted || old.playerIn || (object)ArenaMatchManager.currentMatch == (object)old)
                {
                    Say("Сейчас на арене идёт вызов — дождитесь его конца.");
                    return;
                }

                // Отменять вызов через «CancelMatch» нельзя: тот гасит обычный бой арены, а не вызов.
                old.ClearTeams();
                arena.challenge = null;
            }

            int keep = arena.beatenByPlayer;
            ArenaMatch made = null;

            try
            {
                arena.beatenByPlayer = step;
                made = arena.SpawnChallenge();
            }
            finally
            {
                arena.beatenByPlayer = keep;
            }

            if (made == null || !Contains(made, hero))
            {
                if (made != null) { try { made.ClearTeams(); } catch { } }
                Say($"«{hero.unitname}» не вышел на вызов.");
                return;
            }

            arena.challenge = made;

            Say($"«{hero.unitname}» принял вызов: поединок ждёт вас на арене «{Name(arena)}», "
                + $"уровень {made.level}.");

            if (Telling.Value)
            {
                ItemForgePlugin.Log.LogInfo($"Вызов чемпиона: «{hero.unitname}» (id {hero.id}), арена "
                    + $"«{arena.arenaName}», уровень {made.level}, награда {made.prize}.");
            }
        }

        private static string Name(Arena arena)
        {
            try
            {
                string said = gameManager.LocalizedString(arena.arenaName);
                return string.IsNullOrEmpty(said) ? arena.arenaName : said;
            }
            catch
            {
                return arena.arenaName;
            }
        }

        /// <summary>A challenge has ended: a champion who lost it is beaten for good.</summary>
        internal static void Ended(ArenaMatch m, int before)
        {
            if (m == null || m.site == null) return;
            Arena arena = m.site;

            // Игра за победу в вызове прибавляет счёт побед на этой арене.
            if (arena.beatenByPlayer <= before) return;

            Sync();

            foreach (NPCSaveData hero in new[] { arena.champion, arena.champion2 })
            {
                if (hero == null || !Contains(m, hero)) continue;
                beaten.Add(hero.id);

                if (Telling.Value)
                {
                    ItemForgePlugin.Log.LogInfo($"Вызов чемпиона: «{hero.unitname}» побеждён и больше не выйдет.");
                }
            }
        }

        // ------------------------------------------------------------------ сохранение

        private static System.Reflection.FieldInfo archiveField;
        private static string loadedFor;

        private static string FilePath()
        {
            string archive = "";
            try
            {
                if (archiveField == null) archiveField = AccessTools.Field(typeof(SaveLoadManager), "currentArchive");
                archive = (archiveField != null && SaveLoadManager.Instance != null
                    ? archiveField.GetValue(SaveLoadManager.Instance) as string : null) ?? "";
            }
            catch
            {
            }

            foreach (char bad in Path.GetInvalidFileNameChars()) archive = archive.Replace(bad, '_');
            return Path.Combine(BepInEx.Paths.ConfigPath, "aor.duels." + archive + ".txt");
        }

        /// <summary>The list belongs to the save in hand; another save — another list.</summary>
        private static void Sync()
        {
            string path = FilePath();
            if (!string.Equals(path, loadedFor, StringComparison.OrdinalIgnoreCase)) Load();
        }

        internal static void Save()
        {
            if (Enabled == null || !Enabled.Value) return;

            try
            {
                Sync();
                List<string> ids = new List<string>();
                foreach (int id in beaten) ids.Add(id.ToString());
                File.WriteAllText(FilePath(), string.Join(",", ids.ToArray()));
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Вызов чемпиона: не смог сохранить побеждённых: " + e.Message);
            }
        }

        internal static void Load()
        {
            beaten.Clear();

            try
            {
                string path = FilePath();
                loadedFor = path;
                if (!File.Exists(path)) return;

                foreach (string one in File.ReadAllText(path).Split(','))
                {
                    int id;
                    if (int.TryParse(one.Trim(), out id)) beaten.Add(id);
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Вызов чемпиона: не смог поднять побеждённых: " + e.Message);
            }
        }
    }

    // Разговор начался — самое время дописать в него свою строку.
    [HarmonyPatch(typeof(DialogueManager), "StartConversation",
        new[] { typeof(string), typeof(Transform), typeof(Transform), typeof(int) })]
    internal static class StartConversation_Duel_Patch
    {
        private static void Prefix(string title)
        {
            if (Duel.Enabled == null || !Duel.Enabled.Value) return;

            try
            {
                if (DialogueManager.instance == null || DialogueManager.instance.masterDatabase == null) return;

                Duel.Register();
                Duel.Offer(DialogueManager.instance.masterDatabase.GetConversation(title));
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Вызов чемпиона: не смог войти в разговор: " + e);
            }
        }
    }

    // Побеждённый чемпион не выходит и в свой черёд: на время набора вызова его будто нет.
    [HarmonyPatch(typeof(Arena), "SpawnChallenge")]
    [HarmonyPriority(Priority.First)]
    internal static class SpawnChallenge_Duel_Patch
    {
        private static void Prefix(Arena __instance, out NPCSaveData[] __state)
        {
            __state = null;
            if (Duel.Enabled == null || !Duel.Enabled.Value || __instance == null) return;

            bool hideOne = Duel.Beaten(__instance.champion);
            bool hideTwo = Duel.Beaten(__instance.champion2);
            if (!hideOne && !hideTwo) return;

            __state = new[] { __instance.champion, __instance.champion2 };
            if (hideOne) __instance.champion = null;
            if (hideTwo) __instance.champion2 = null;
        }

        private static void Finalizer(Arena __instance, NPCSaveData[] __state)
        {
            if (__state == null || __instance == null) return;
            __instance.champion = __state[0];
            __instance.champion2 = __state[1];
        }
    }

    [HarmonyPatch(typeof(ArenaMatch), "EndMatch")]
    internal static class EndMatch_Duel_Patch
    {
        private static void Prefix(ArenaMatch __instance, out int __state)
        {
            __state = __instance != null && __instance.site != null ? __instance.site.beatenByPlayer : 0;
        }

        private static void Postfix(ArenaMatch __instance, int __state)
        {
            if (Duel.Enabled == null || !Duel.Enabled.Value) return;
            try { Duel.Ended(__instance, __state); } catch { }
        }
    }

    [HarmonyPatch(typeof(SaveLoadManager), "WriteDataToSaveFile")]
    internal static class WriteSave_Duel_Patch
    {
        private static void Postfix() { Duel.Save(); }
    }

    [HarmonyPatch(typeof(ArenaMatchManager), "LoadInstance")]
    internal static class LoadInstance_Duel_Patch
    {
        private static void Postfix() { Duel.Load(); }
    }
}
