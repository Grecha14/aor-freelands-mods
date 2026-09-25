using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using DuloGames.UI;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// The pictures of the King of Hell set, burnt darker than any honest smith would leave them.
    ///
    /// Своих картинок у комплекта нет: вещи одолжены у других, и значки при них чужие — чистые,
    /// светлые, мастерские. Здесь каждый значок перерисовывается по точкам: краски гаснут и
    /// уходят в запёкшуюся кровь, тени чернеют, по металлу идут трещины, в которых тлеет
    /// угольный свет, а края вещи обводит багровое марево. Трещины у каждой вещи свои и
    /// одни и те же от раза к разу: рисунок выводится из номера вещи, а не из случая.
    /// </summary>
    internal static class Glyphs
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> Dump;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Glyphs", "Enabled", true,
                "Redraw the icons of the King of Hell set: darker, bloodier, cracked, with a "
                + "smouldering crimson rim.");

            Dump = config.Bind("Glyphs", "Dump", true,
                "Also write the redrawn icons as pictures beside the mod, to look at them "
                + "outside the game.");
        }

        private static bool done;
        private static readonly List<UnityEngine.Object> kept = new List<UnityEngine.Object>();

        /// <summary>Redraws every icon of the set, once.</summary>
        internal static void Paint()
        {
            if (done || Enabled == null || !Enabled.Value) return;
            if (Forge.Built.Count == 0) return;

            done = true;
            int drawn = 0;

            foreach (UIEquipmentInfo piece in Forge.Built)
            {
                if (piece == null || piece.Icon == null) continue;

                try
                {
                    Sprite made = Burn(piece.Icon, piece.ID, piece.Name);
                    if (made == null) continue;

                    piece.Icon = made;
                    drawn++;
                }
                catch (Exception e)
                {
                    ItemForgePlugin.Log.LogWarning($"Не смог перерисовать значок «{piece.Name}»: {e.Message}");
                }
            }

            ItemForgePlugin.Log.LogInfo("Значки комплекта Короля Ада перерисованы: " + drawn + ".");
        }

        /// <summary>Reads a sprite even out of an atlas the game keeps unreadable.</summary>
        private static Texture2D Read(Sprite from)
        {
            Texture2D atlas = from.texture;
            if (atlas == null) return null;

            Rect rect = from.textureRect;
            int w = Mathf.Max(1, Mathf.RoundToInt(rect.width));
            int h = Mathf.Max(1, Mathf.RoundToInt(rect.height));

            RenderTexture whole = RenderTexture.GetTemporary(atlas.width, atlas.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            RenderTexture was = RenderTexture.active;

            try
            {
                Graphics.Blit(atlas, whole);
                RenderTexture.active = whole;

                Texture2D copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(rect.x, rect.y, w, h), 0, 0);
                copy.Apply();
                return copy;
            }
            finally
            {
                RenderTexture.active = was;
                RenderTexture.ReleaseTemporary(whole);
            }
        }

        private static Sprite Burn(Sprite source, int id, string name)
        {
            Texture2D tex = Read(source);
            if (tex == null) return null;

            int w = tex.width, h = tex.height;
            Color[] px = tex.GetPixels();
            System.Random dice = new System.Random(id * 7919 + 13);

            Color ember = new Color(1f, 0.32f, 0.04f);
            Color blood = new Color(0.55f, 0.02f, 0.03f);

            // 1. Краски гаснут и уходят в запёкшуюся кровь; тени чернеют.
            for (int i = 0; i < px.Length; i++)
            {
                Color c = px[i];
                if (c.a <= 0.02f) continue;

                float lum = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
                Color dark = new Color(lum * 0.9f, lum * 0.12f, lum * 0.1f, c.a);

                Color mixed = Color.Lerp(c, dark, 0.55f);
                float shade = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(lum * 1.4f));
                mixed.r *= shade; mixed.g *= shade * 0.85f; mixed.b *= shade * 0.85f;

                px[i] = mixed;
            }

            // 2. Трещины: несколько ломаных, только по самой вещи, с тлеющей сердцевиной.
            int cracks = 3 + dice.Next(3);
            for (int k = 0; k < cracks; k++)
            {
                int x = dice.Next(w), y = dice.Next(h);
                int guard = 0;
                while (px[y * w + x].a < 0.5f && guard++ < 200) { x = dice.Next(w); y = dice.Next(h); }
                if (guard >= 200) continue;

                double angle = dice.NextDouble() * Math.PI * 2d;
                int length = Mathf.Max(8, Mathf.Min(w, h) / 3 + dice.Next(Mathf.Max(1, Mathf.Min(w, h) / 3)));

                float fx = x, fy = y;
                for (int s = 0; s < length; s++)
                {
                    angle += (dice.NextDouble() - 0.5d) * 0.9d;
                    fx += (float)Math.Cos(angle);
                    fy += (float)Math.Sin(angle);

                    int cx = Mathf.RoundToInt(fx), cy = Mathf.RoundToInt(fy);
                    if (cx < 1 || cy < 1 || cx >= w - 1 || cy >= h - 1) break;

                    int at = cy * w + cx;
                    if (px[at].a < 0.4f) break;

                    float heat = 1f - (float)s / length;
                    px[at] = Color.Lerp(px[at], ember, 0.85f * heat + 0.15f);
                    px[at].a = Mathf.Max(px[at].a, 0.9f);

                    // Ореол вокруг трещины — тусклее.
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int n = (cy + dy) * w + (cx + dx);
                            if (px[n].a < 0.3f) continue;
                            px[n] = Color.Lerp(px[n], blood, 0.35f * heat);
                        }
                    }

                    // Иногда трещина ветвится.
                    if (dice.NextDouble() < 0.04d) angle += (dice.NextDouble() < 0.5d ? 1d : -1d) * 0.8d;
                }
            }

            // 3. Багровое марево по краю: там, где вещь граничит с пустотой.
            float[] alpha = new float[px.Length];
            for (int i = 0; i < px.Length; i++) alpha[i] = px[i].a;

            int reach = Mathf.Max(2, Mathf.Min(w, h) / 40);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (alpha[i] > 0.1f) continue;

                    float near = 0f;
                    for (int dy = -reach; dy <= reach; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= h) continue;

                        for (int dx = -reach; dx <= reach; dx++)
                        {
                            int xx = x + dx;
                            if (xx < 0 || xx >= w) continue;

                            float a = alpha[yy * w + xx];
                            if (a <= 0.3f) continue;

                            float d = Mathf.Sqrt(dx * dx + dy * dy) / (reach + 0.5f);
                            near = Mathf.Max(near, (1f - d) * a);
                        }
                    }

                    if (near <= 0f) continue;

                    Color glow = Color.Lerp(blood, ember, near * 0.4f);
                    glow.a = near * 0.75f;
                    px[i] = glow;
                }
            }

            tex.SetPixels(px);
            tex.Apply();
            tex.name = "KingOfHell_" + id;

            Sprite made = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f),
                source.pixelsPerUnit);
            made.name = source.name + "_hell";

            kept.Add(tex);
            kept.Add(made);

            if (Dump.Value)
            {
                try
                {
                    string dir = Path.Combine(Path.GetDirectoryName(typeof(Glyphs).Assembly.Location) ?? ".", "icons");
                    Directory.CreateDirectory(dir);
                    File.WriteAllBytes(Path.Combine(dir, id + ".png"), ImageConversion.EncodeToPNG(tex));
                }
                catch
                {
                }
            }

            return made;
        }
    }
}
