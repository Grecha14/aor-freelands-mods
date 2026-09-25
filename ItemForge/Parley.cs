using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using PixelCrushers.DialogueSystem;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// The list of things you might say, made big enough to read.
    ///
    /// Разговор — это половина игры, а окно ответов сделано под консольный телевизор: узкая
    /// полоса у нижнего края, кегль в шесть пикселей, и читать её приходится наклоняясь к
    /// экрану.
    ///
    /// Здесь она уезжает в середину и растёт втрое. Растёт целиком, вместе с текстом и
    /// отступами, — не подбором шрифта, а масштабом самой панели: тогда ничего не съезжает и
    /// не переносится не там, где задумано.
    ///
    /// И растёт не слепо. Панель, увеличенная втрое, легко вылезает за края холста, а
    /// уехавшую за край кнопку нечем нажать; поэтому кратность урезается до той, при которой
    /// окно ещё помещается. Заказать можно втрое — получить столько, сколько влезет.
    /// </summary>
    internal static class Parley
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Scale;
        internal static ConfigEntry<bool> Center;
        internal static ConfigEntry<bool> Fits;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Parley", "Enabled", true,
                "Move the list of replies to the middle of the screen and make it bigger.");

            Scale = config.Bind("Parley", "Scale", 3f,
                new ConfigDescription(
                    "How much bigger. Three, and the whole panel grows with its text.",
                    new AcceptableValueRange<float>(0.5f, 6f)));

            Center = config.Bind("Parley", "Center", true,
                "Put the panel in the middle of the screen rather than where it was authored.");

            Fits = config.Bind("Parley", "Fits", true,
                "Cut the size down if the panel would not fit on the canvas. Without this a "
                + "button can end up past the edge, where nothing can press it.");
        }

        // Что уже поправлено и до какой кратности. Панель живёт дольше одного разговора, и
        // трогать её каждый раз незачем — но сцена может её пересоздать, и тогда она придёт
        // сюда новым предметом.
        private static readonly Dictionary<int, float> done = new Dictionary<int, float>();

        /// <summary>Takes the reply panel in hand, once it is up.</summary>
        internal static void Fit()
        {
            if (!Enabled.Value) return;

            try
            {
                StandardUIMenuPanel[] panels =
                    UnityEngine.Object.FindObjectsOfType<StandardUIMenuPanel>();

                if (panels == null || panels.Length == 0) return;

                foreach (StandardUIMenuPanel panel in panels)
                {
                    if (panel == null) continue;
                    Shape(panel.transform as RectTransform, panel.name);
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог поправить окно разговора: " + e.Message);
            }
        }

        private static void Shape(RectTransform box, string name)
        {
            if (box == null) return;

            float want = Scale.Value;

            if (Fits.Value)
            {
                float room = Room(box);
                if (room > 0.1f && room < want) want = room;
            }

            float had;
            bool known = done.TryGetValue(box.GetInstanceID(), out had);

            if (Center.Value)
            {
                Vector2 middle = new Vector2(0.5f, 0.5f);
                box.anchorMin = middle;
                box.anchorMax = middle;
                box.pivot = middle;
                box.anchoredPosition = Vector2.zero;
            }

            box.localScale = new Vector3(want, want, 1f);

            if (known && Mathf.Abs(had - want) < 0.01f) return;

            done[box.GetInstanceID()] = want;

            if (want < Scale.Value - 0.01f)
            {
                ItemForgePlugin.Log.LogInfo("Окно разговора «" + name + "» увеличено в "
                    + want.ToString("0.##") + " раза вместо " + Scale.Value.ToString("0.##")
                    + ": втрое оно не помещается на экране.");
            }
            else
            {
                ItemForgePlugin.Log.LogInfo("Окно разговора «" + name + "» увеличено в "
                    + want.ToString("0.##") + " раза и поставлено по центру.");
            }
        }

        /// <summary>How much the canvas has room for, as a multiple of the panel itself.</summary>
        private static float Room(RectTransform box)
        {
            if (box.rect.width <= 1f || box.rect.height <= 1f) return 0f;

            Canvas top = box.GetComponentInParent<Canvas>();
            if (top == null) return 0f;

            Canvas root = top.rootCanvas;
            if (root == null) return 0f;

            RectTransform sheet = root.transform as RectTransform;
            if (sheet == null || sheet.rect.width <= 1f || sheet.rect.height <= 1f) return 0f;

            return Mathf.Min(sheet.rect.width / box.rect.width,
                sheet.rect.height / box.rect.height);
        }
    }

    // Список ответов собран и вот-вот покажется. Тут панель уже поднята и её размер известен,
    // а до того он ещё нулевой и считать по нему нечего.
    [HarmonyPatch(typeof(ConversationView), "StartResponses",
        new Type[] { typeof(Subtitle), typeof(Response[]) })]
    internal static class StartResponses_Parley_Patch
    {
        private static void Postfix()
        {
            try { Parley.Fit(); }
            catch { }
        }
    }
}
