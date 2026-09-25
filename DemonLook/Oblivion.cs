using System;
using BepInEx.Configuration;
using UnityEngine;

namespace DemonLook
{
    /// <summary>
    /// Closes the prologue at the gates of Brea.
    ///
    /// Everything the demon did in the cave has one witness, and she walks into town beside
    /// him. The scene where she drinks it away is what lets the rest of the game carry on
    /// untouched: the caravan work, the quests and every conversation stay exactly as written,
    /// because as far as anyone in Brea knows, nothing happened.
    ///
    /// That is not only a story: it is the reason this mod can be small. A prologue that leaves
    /// no trace needs nothing rewritten, and the amnesia is the seam made visible rather than
    /// hidden. The potion is the game's own — Amnesia Potion, which it has shipped all along —
    /// so the thing she drinks is a real item and not a piece of scenery.
    ///
    /// Drawn with the mod's own window rather than the game's conversation system. A real
    /// dialogue would have to be written into the authored database, in somebody else's format,
    /// where a mistake shows up as silence rather than as an error.
    /// </summary>
    internal static class Oblivion
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Town;
        internal static ConfigEntry<string> NotIn;
        internal static ConfigEntry<string> Text;
        internal static ConfigEntry<bool> TakeThePotion;
        internal static ConfigEntry<string> PotionName;

        private static bool shown;
        private static bool showing;
        private static Rect frame = new Rect(0f, 0f, 640f, 340f);
        private static GUIStyle body;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Oblivion", "Enabled", true,
                "Close the prologue with the scene at the gates.");

            Town = config.Bind("Oblivion", "Town", "001-Brea",
                "The area that means the town has been reached. Now that the game's own list of "
                + "scenes is known, this is the town's real name rather than a word to search "
                + "for: «Brea» alone also matched the road to Brea and the Breawoods cave, and "
                + "the scene played in both.");

            NotIn = config.Bind("Oblivion", "NotIn", "AWayToBrea,Breawoods",
                "Area names that must not count, separated by commas, even though they match the "
                + "name above. The opening scene is called 000-AWayToBrea — the road to Brea — so "
                + "looking for «Brea» in the name matched the road itself and the scene played on "
                + "the very first screen, before there was anything to forget.");

            TakeThePotion = config.Bind("Oblivion", "TakeThePotion", true,
                "Take the potion out of her bag when she drinks it, so the event leaves a mark on "
                + "the world and not only on the screen.");

            PotionName = config.Bind("Oblivion", "PotionName", "Amnesia Potion",
                "The item she drinks. The game ships one under this name.");

            Text = config.Bind("Oblivion", "Text",
                "Перед самыми воротами она остановилась.\n\n"
                + "Всю дорогу из пещеры она молчала — и молчала не как человек, которому нечего "
                + "сказать, а как тот, кто боится услышать собственный голос. Теперь она "
                + "обернулась, и стало видно, что решение принято давно, ещё там, в темноте, "
                + "пока догорал круг.\n\n"
                + "«Я не смогу с этим жить, — сказала она. — И не смогу об этом молчать. "
                + "Значит, остаётся третье».\n\n"
                + "Склянка была маленькая и мутная. Она выпила её до дна, не поморщившись, и "
                + "какое-то время стояла, глядя мимо, будто прислушиваясь к чему-то внутри. "
                + "Потом моргнула — и посмотрела на вас с ровным, вежливым, ничего не значащим "
                + "выражением случайного попутчика.\n\n"
                + "«Далеко ещё до Бреа?»\n\n"
                + "В Бреа никто не узнает, что случилось в пещере. Теперь и она тоже.",
                "What is shown at the gates. Written as narration rather than as a conversation, "
                + "because it is the one moment the game will not be told about by anyone.");
        }

        /// <summary>Forgets that the gates were reached, for a new game in the same session.</summary>
        internal static void Forget()
        {
            shown = false;
            showing = false;
        }

        internal static void Check()
        {
            if (!Enabled.Value || shown || showing) return;

            try
            {
                AreaManager area = AreaManager.Instance;
                if (area == null || string.IsNullOrEmpty(area.sceneName)) return;

                string wanted = (Town.Value ?? "").Trim();
                if (wanted.Length == 0) return;

                if (area.sceneName.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) < 0) return;

                foreach (string entry in (NotIn.Value ?? "").Split(','))
                {
                    string barred = entry.Trim();
                    if (barred.Length == 0) continue;
                    if (area.sceneName.IndexOf(barred, StringComparison.OrdinalIgnoreCase) >= 0) return;
                }

                // Сцена только для демона и только однажды: обычному герою прощаться не с чем.
                PartyManager party = PartyManager.instance;
                HumaniodUnit leader = party != null ? party.leader : null;
                if (leader == null || !Racial.IsDemon(leader)) return;

                showing = true;
                Drink(party);

                DemonLookPlugin.Log.LogInfo($"Забвение у ворот: область «{area.sceneName}».");
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogError("Проверка на ворота сорвалась: " + e);
            }
        }

        /// <summary>Takes the potion out of the party's bags: she drank it.</summary>
        private static void Drink(PartyManager party)
        {
            if (!TakeThePotion.Value || party == null) return;

            string wanted = (PotionName.Value ?? "").Trim();
            if (wanted.Length == 0) return;

            int taken = party.RemoveItemInParty(wanted, 1);

            DemonLookPlugin.Log.LogInfo(taken > 0
                ? $"Зелье забвения выпито: «{wanted}»."
                : $"Зелья «{wanted}» в отряде не было — сцена всё равно состоялась.");
        }

        internal static void Draw()
        {
            if (!showing) return;

            if (body == null)
            {
                body = new GUIStyle(GUI.skin.label);
                body.wordWrap = true;
                body.fontSize = 15;
                body.richText = true;
                body.padding = new RectOffset(18, 18, 12, 12);
            }

            frame.width = Mathf.Min(680f, Screen.width - 80f);
            frame.height = Mathf.Min(420f, Screen.height - 120f);
            frame.x = (Screen.width - frame.width) * 0.5f;
            frame.y = (Screen.height - frame.height) * 0.5f;

            GUI.depth = 0;
            frame = GUI.Window(907_002, frame, Body, "У ворот Бреа");
        }

        private static void Body(int id)
        {
            GUILayout.Space(6f);
            GUILayout.Label(Text.Value, body);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Дальше", GUILayout.Height(30f)))
            {
                showing = false;
                shown = true;
            }

            GUI.DragWindow(new Rect(0f, 0f, frame.width, 22f));
        }
    }
}
