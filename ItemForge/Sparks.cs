using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Finds the particle systems that try to spawn on nothing.
    ///
    /// В журнале Unity за один заход набралось двадцать четыре тысячи строк «Particle System is
    /// trying to spawn on a mesh with zero surface area». Началось с первой драки в городе и
    /// дальше шло каждый кадр. Сама Unity не говорит, какая система виновата, а по коду не
    /// видно: сетку частицам дают и игра («UpdatePSMesh» при надевании), и наш перенос
    /// свечения с дубины на клинок.
    ///
    /// Поэтому здесь не догадка, а перепись. Раз в десять секунд просматриваются все живые
    /// системы частиц, которые испускают с сетки, и та, чья сетка пуста — нет её, нет в ней
    /// треугольников, сжата в точку или выключена, — записывается один раз: путь в сцене,
    /// какая форма и чья сетка, и чей это человек или вещь.
    /// </summary>
    internal static class Sparks
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Count;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Sparks", "Enabled", true,
                "Look for particle systems that spawn on an empty mesh — the ones behind the "
                + "'zero surface area' warnings — and write each one down once.");

            Count = config.Bind("Sparks", "Count", 15,
                new ConfigDescription(
                    "How many different ones to write down.",
                    new AcceptableValueRange<int>(1, 100)));
        }

        private static float next;
        private static readonly HashSet<string> seen = new HashSet<string>();

        internal static void Tick()
        {
            if (Enabled == null || !Enabled.Value || seen.Count >= Count.Value) return;
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 10f;

            try
            {
                foreach (ParticleSystem system in UnityEngine.Object.FindObjectsOfType<ParticleSystem>())
                {
                    if (system == null || !system.isPlaying) continue;

                    ParticleSystem.ShapeModule shape = system.shape;
                    if (!shape.enabled) continue;

                    string why = Empty(shape, system.transform);
                    if (why == null) continue;

                    string path = Path(system.transform);
                    if (!seen.Add(path)) continue;

                    UnitAttribute owner = system.GetComponentInParent<UnitAttribute>();
                    string who = owner != null && owner.Data != null ? owner.Data.unitname : "ничей";

                    ItemForgePlugin.Log.LogInfo($"Частицы на пустой сетке: «{path}» — {why}; "
                        + $"владелец «{who}».");

                    if (seen.Count >= Count.Value) break;
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Перепись частиц сорвалась: " + e.Message);
                seen.Add("*сбой*");
            }
        }

        /// <summary>Why this shape has nothing to spawn on, or null when it has.</summary>
        private static string Empty(ParticleSystem.ShapeModule shape, Transform at)
        {
            Mesh mesh = null;
            Renderer by = null;
            string form;

            switch (shape.shapeType)
            {
                case ParticleSystemShapeType.Mesh:
                    form = "сетка";
                    mesh = shape.mesh;
                    break;

                case ParticleSystemShapeType.MeshRenderer:
                    form = "отрисовка сетки";
                    by = shape.meshRenderer;
                    if (shape.meshRenderer != null)
                    {
                        MeshFilter filter = shape.meshRenderer.GetComponent<MeshFilter>();
                        if (filter != null) mesh = filter.sharedMesh;
                    }
                    break;

                case ParticleSystemShapeType.SkinnedMeshRenderer:
                    form = "натянутая сетка";
                    by = shape.skinnedMeshRenderer;
                    if (shape.skinnedMeshRenderer != null) mesh = shape.skinnedMeshRenderer.sharedMesh;
                    break;

                default:
                    return null;
            }

            string whose = by != null ? $" «{by.name}» ({Path(by.transform)})" : "";

            if (mesh == null) return $"{form}{whose}: сетки нет";
            if (mesh.vertexCount == 0) return $"{form}{whose} «{mesh.name}»: вершин нет";

            long tris = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) tris += mesh.GetIndexCount(i) / 3;
            if (tris == 0) return $"{form}{whose} «{mesh.name}»: треугольников нет";

            if (mesh.bounds.size.sqrMagnitude < 1e-10f) return $"{form}{whose} «{mesh.name}»: сжата в точку";

            if (by != null && !by.enabled) return $"{form}{whose} «{mesh.name}»: отрисовка выключена";
            if (by != null && !by.gameObject.activeInHierarchy) return $"{form}{whose} «{mesh.name}»: выключена целиком";
            if (by is SkinnedMeshRenderer && by.bounds.size.sqrMagnitude < 1e-8f) return $"{form}{whose} «{mesh.name}»: кости сжаты в точку";

            Transform scaled = by != null ? by.transform : at;
            if (scaled.lossyScale.sqrMagnitude < 1e-8f) return $"{form}{whose} «{mesh.name}»: масштаб нулевой";

            if (shape.scale.sqrMagnitude < 1e-8f) return $"{form}{whose}: масштаб формы нулевой";

            return null;
        }

        private static string Path(Transform node)
        {
            List<string> parts = new List<string>();
            for (Transform at = node; at != null && parts.Count < 12; at = at.parent) parts.Add(at.name);

            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }
    }
}
