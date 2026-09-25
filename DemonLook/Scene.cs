using System;
using System.Collections;
using UnityEngine;

namespace DemonLook
{
    /// <summary>
    /// Runs a staged scene one beat at a time, waiting for the player between them.
    ///
    /// The ritual was built on a timer at first — mages appeared, cast on a count, and the
    /// hunters arrived whether or not anybody had understood what was happening. A scene told
    /// that way has no beats: it is a list of events that happen near each other.
    ///
    /// Here each beat is framed by words and waits for the player to move it on. Nothing runs
    /// until he has read what is on screen, so the order is his as much as ours: the mages
    /// speak, then they appear; they speak again, then they cast; and only when the summoning
    /// is done do the people who came to stop it get their line.
    ///
    /// Drawn with the mod's own window for the same reason the amnesia scene is: writing into
    /// the game's authored conversation database means writing in somebody else's format,
    /// where a mistake shows up as silence rather than as an error.
    /// </summary>
    internal static class Scene
    {
        private static string title;
        private static string body;
        private static bool waiting;

        // Занавес: пока он поднят, экран чёрный. Нужен для того, чтобы игрок увидел сцену
        // уже сложившейся, а не наблюдал, как она собирается по частям.
        private static float curtain;
        private static Texture2D black;

        private static Rect frame = new Rect(0f, 0f, 680f, 300f);
        private static GUIStyle text;

        internal static bool Busy { get { return waiting; } }

        internal static void Close() { curtain = 1f; }

        /// <summary>Takes the curtain down over the given time.</summary>
        internal static IEnumerator Open(float seconds)
        {
            float went = 0f;

            while (went < seconds)
            {
                went += UnityEngine.Time.deltaTime;
                curtain = Mathf.Clamp01(1f - went / Mathf.Max(0.01f, seconds));
                yield return null;
            }

            curtain = 0f;
        }

        /// <summary>
        /// True while the game's own narration window is up and waiting for a click.
        ///
        /// Оно же показывает вступление к игре: картинка во весь экран, текст в рамке и
        /// «Продолжить» внизу. Наше собственное окно рядом с ним выглядит накладкой поверх
        /// игры, и правильно выглядит — оно ею и было.
        /// </summary>
        private static bool Native()
        {
            try
            {
                return LoadingOverlay.instance != null && gameManager.GM != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Shows a line and holds the scene until the player has read it.</summary>
        internal static IEnumerator Say(string who, string what)
        {
            if (string.IsNullOrEmpty(what)) yield break;

            // Игровым окном, когда оно есть. Текст уходит туда, куда игра кладёт ключ строки, —
            // а её переводчик возвращает незнакомое слово как есть, так что наши строки
            // доходят до экрана нетронутыми.
            if (Native())
            {
                LoadingOverlay.instance.Narratage(what);

                // Пока игрок не нажал «Продолжить». Флаг ставит сама игра — тот же, которым
                // она держит вступление.
                while (gameManager.GM.waitForContinue) yield return null;

                yield return null;
                yield break;
            }

            title = who;
            body = what;
            waiting = true;

            while (waiting) yield return null;

            // Кадр передышки: иначе следующее окно откроется тем же щелчком, которым
            // закрыли предыдущее, и реплики промелькнут неразличимо.
            yield return null;
        }

        internal static void Draw()
        {
            // Занавес рисуется до окна и поверх всего остального, чтобы реплики читались
            // на чёрном, а мир за ними оставался скрыт.
            if (curtain > 0f)
            {
                if (black == null)
                {
                    black = new Texture2D(1, 1);
                    black.SetPixel(0, 0, UnityEngine.Color.black);
                    black.Apply();
                }

                UnityEngine.Color had = GUI.color;
                GUI.color = new UnityEngine.Color(0f, 0f, 0f, curtain);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), black);
                GUI.color = had;
            }

            if (!waiting) return;

            if (text == null)
            {
                text = new GUIStyle(GUI.skin.label);
                text.wordWrap = true;
                text.fontSize = 15;
                text.richText = true;
                text.padding = new RectOffset(18, 18, 12, 12);
            }

            frame.width = Mathf.Min(720f, Screen.width - 80f);
            frame.height = Mathf.Min(400f, Screen.height - 120f);
            frame.x = (Screen.width - frame.width) * 0.5f;
            frame.y = Screen.height - frame.height - 60f;

            frame = GUI.Window(907_003, frame, Body, title ?? "");
        }

        private static void Body(int id)
        {
            GUILayout.Space(6f);
            GUILayout.Label(body, text);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Дальше", GUILayout.Height(30f))) waiting = false;

            GUI.DragWindow(new Rect(0f, 0f, frame.width, 22f));
        }

        /// <summary>Drops any line still on screen, for a new game in the same session.</summary>
        internal static void Forget()
        {
            waiting = false;
            title = null;
            body = null;
            curtain = 0f;
        }
    }
}
