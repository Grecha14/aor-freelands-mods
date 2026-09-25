using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Writes out what an equipped weapon is actually built from.
    ///
    /// Two questions need this. Whether the blade can be enlarged on its own depends on
    /// whether blade and grip are separate objects or a single mesh — scaling a part of one
    /// mesh means editing geometry, which is out of reach at runtime. And whether a glow can
    /// be moved or recoloured depends on what the glow is made of: particles and a tinted
    /// shader can be changed, colour baked into a texture cannot.
    /// </summary>
    internal static class Inspect
    {
        /// <summary>
        /// Writes out every visual effect alive in the scene at this moment.
        ///
        /// The weapon inspector only ever sees what is in the character's hands, so spells,
        /// summons and auras went unrecorded however many times it was pressed — pointing the
        /// camera at a thing is not the same as the tool knowing about it. This walks the whole
        /// scene instead and writes down each particle system that is actually running, with the
        /// object it belongs to, so whatever is on screen when the key is pressed gets caught.
        ///
        /// Only systems that are playing are listed. A scene holds hundreds that sit idle, and
        /// naming them all would bury the one being looked for.
        /// </summary>
        /// <summary>
        /// Writes out a named object's whole tree: its meshes, its materials and their shaders.
        ///
        /// The scene sweep only ever looked at particle systems, so a creature made dark by its
        /// own skin rather than by a cloud of sparks went unrecorded. What makes the darkness
        /// elemental black is not an effect hanging off it but the material on its mesh, and to
        /// borrow that the material has to be named first.
        /// </summary>
        internal static void NamedObject(string wanted)
        {
            if (string.IsNullOrEmpty(wanted)) return;

            try
            {
                int found = 0;

                foreach (Transform node in UnityEngine.Object.FindObjectsOfType<Transform>())
                {
                    if (node == null || found >= 3) continue;
                    if (node.name.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (node.parent != null
                        && node.parent.name.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0) continue;

                    found++;
                    ItemForgePlugin.Log.LogInfo($"=== объект «{node.name}» ===");
                    Describe(node, 0);
                }

                if (found == 0) ItemForgePlugin.Log.LogInfo($"Объекта «{wanted}» в сцене нет.");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Осмотр объекта сорвался: " + e);
            }
        }

        internal static void SceneEffects()
        {
            try
            {
                ParticleSystem[] all = UnityEngine.Object.FindObjectsOfType<ParticleSystem>();
                ItemForgePlugin.Log.LogInfo($"=== эффекты в сцене: систем всего {all.Length} ===");

                // Группируем по корню: одна аура — это дерево из десятка систем, и интересен
                // корень, который можно скопировать целиком, а не каждый листок отдельно.
                Dictionary<Transform, List<ParticleSystem>> byRoot = new Dictionary<Transform, List<ParticleSystem>>();

                foreach (ParticleSystem system in all)
                {
                    if (system == null || !system.isPlaying) continue;

                    Transform root = Instance(system.transform);
                    List<ParticleSystem> list;
                    if (!byRoot.TryGetValue(root, out list))
                    {
                        list = new List<ParticleSystem>();
                        byRoot[root] = list;
                    }
                    list.Add(system);
                }

                ItemForgePlugin.Log.LogInfo($"Живых деревьев эффектов: {byRoot.Count}.");

                foreach (KeyValuePair<Transform, List<ParticleSystem>> pair in byRoot)
                {
                    int particles = 0;
                    HashSet<string> shaders = new HashSet<string>();
                    List<string> names = new List<string>();

                    foreach (ParticleSystem system in pair.Value)
                    {
                        ParticleSystem.MainModule main = system.main;
                        particles += main.maxParticles;

                        if (names.Count < 20) names.Add(Path(system.transform, pair.Key));

                        ParticleSystemRenderer art = system.GetComponent<ParticleSystemRenderer>();
                        if (art != null && art.sharedMaterial != null && art.sharedMaterial.shader != null)
                        {
                            shaders.Add(art.sharedMaterial.shader.name);
                        }
                    }

                    ItemForgePlugin.Log.LogInfo($"ЭФФЕКТ «{pair.Key.name}»: систем {pair.Value.Count}, "
                        + $"частиц до {particles}"
                        + (shaders.Count > 0 ? ", шейдеры: " + string.Join(", ", new List<string>(shaders).ToArray()) : "")
                        + " | " + string.Join(", ", names.ToArray()));
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Обход эффектов сцены сорвался: " + e);
            }
        }

        /// <summary>
        /// The effect a system belongs to, rather than the scene it happens to live in.
        ///
        /// Grouping by scene root put three hundred unrelated systems under one heading and hid
        /// the very thing being looked for. What matters is the instance: the game hangs each
        /// spawned effect under a holder and names the copy with a bracketed suffix, and a
        /// weapon or creature keeps its own under a node named for the effect. Either of those
        /// is a thing that can be copied whole, so either is where the grouping stops.
        /// </summary>
        private static Transform Instance(Transform node)
        {
            Transform best = null;
            Transform walk = node;

            while (walk != null)
            {
                if (walk.name.EndsWith("(Clone)", StringComparison.Ordinal)
                    || walk.name.StartsWith("VFX", StringComparison.OrdinalIgnoreCase))
                {
                    best = walk;
                }
                walk = walk.parent;
            }

            return best != null ? best : node.root;
        }

        private static string Path(Transform node, Transform root)
        {
            string name = node.name;
            Transform walk = node.parent;

            while (walk != null && walk != root)
            {
                name = walk.name + "/" + name;
                walk = walk.parent;
            }
            return name;
        }

        internal static void EquippedWeapon()
        {
            try
            {
                PartyManager party = PartyManager.instance;
                HumaniodUnit leader = party != null ? party.leader : null;
                if (leader == null)
                {
                    ItemForgePlugin.Log.LogWarning("Нет персонажа. Зайдите в сохранение.");
                    return;
                }

                if (leader.weapons == null || leader.weapons.Count == 0)
                {
                    ItemForgePlugin.Log.LogWarning("В руках ничего нет.");
                    return;
                }

                for (int i = 0; i < leader.weapons.Count; i++)
                {
                    Weapon w = leader.weapons[i];
                    if (w == null) continue;

                    Transform node = w.weaponNode;
                    ItemForgePlugin.Log.LogInfo($"=== оружие {i}: класс {w.weaponClass}, тип {w.weaponType}, точка крепления {(node != null ? node.name : "нет")} ===");

                    if (node == null) continue;
                    Describe(node, 0);
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Осмотр оружия не удался: " + e);
            }
        }

        private static void Describe(Transform t, int depth)
        {
            if (depth > 6) return;

            StringBuilder sb = new StringBuilder();
            sb.Append(new string(' ', depth * 2));
            sb.Append(t.name);
            // Поворот и положение важнее масштаба, когда эффект переносится с одной модели на
            // другую: габариты меша заданы в его собственных координатах и ничего не говорят о
            // том, как узел повёрнут. Без этих трёх чисел ось приходилось угадывать.
            sb.Append($"  [поз {t.localPosition.x:0.##},{t.localPosition.y:0.##},{t.localPosition.z:0.##}");
            sb.Append($" | поворот {t.localEulerAngles.x:0.#},{t.localEulerAngles.y:0.#},{t.localEulerAngles.z:0.#}");
            sb.Append($" | масштаб {t.localScale.x:0.##},{t.localScale.y:0.##},{t.localScale.z:0.##}]");

            // Куда смотрит узел в мире — это и есть ответ на вопрос «вдоль какой оси клинок».
            sb.Append($"  мир: вперёд {t.forward.x:0.##},{t.forward.y:0.##},{t.forward.z:0.##}");
            sb.Append($" вверх {t.up.x:0.##},{t.up.y:0.##},{t.up.z:0.##}");

            MeshFilter mf = t.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                Vector3 size = mf.sharedMesh.bounds.size;
                sb.Append($"  меш {mf.sharedMesh.name}, габариты {size.x:0.##}x{size.y:0.##}x{size.z:0.##}");
            }

            SkinnedMeshRenderer smr = t.GetComponent<SkinnedMeshRenderer>();
            if (smr != null && smr.sharedMesh != null) sb.Append($"  скин-меш {smr.sharedMesh.name}");

            Renderer r = t.GetComponent<Renderer>();
            if (r != null)
            {
                List<string> mats = new List<string>();
                foreach (Material m in r.sharedMaterials)
                {
                    if (m != null) mats.Add($"{m.name}({(m.shader != null ? m.shader.name : "без шейдера")})");
                }
                if (mats.Count > 0) sb.Append("  материалы: " + string.Join(", ", mats.ToArray()));
            }

            ParticleSystem ps = t.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                ParticleSystem.MainModule main = ps.main;
                // Состояние важнее настроек: система может быть целой, на месте и при этом
                // молчать. Без этих полей «эффект не виден» и «эффект не играет» неразличимы.
                ParticleSystemRenderer art = ps.GetComponent<ParticleSystemRenderer>();

                sb.Append($"  ЧАСТИЦЫ: цвет {main.startColor.color}, потолок {main.maxParticles}");
                sb.Append($", играет {ps.isPlaying}, испускает {ps.isEmitting}, живых {ps.particleCount}");
                sb.Append($", сам при старте {main.playOnAwake}, петля {main.loop}");
                sb.Append($", рендер {(art != null ? art.enabled.ToString() : "нет")}");
                sb.Append($", объект {ps.gameObject.activeInHierarchy}");
            }

            TrailRenderer tr = t.GetComponent<TrailRenderer>();
            if (tr != null) sb.Append("  СЛЕД");

            Light light = t.GetComponent<Light>();
            if (light != null) sb.Append($"  СВЕТ: цвет {light.color}, сила {light.intensity}");

            foreach (Component c in t.GetComponents<Component>())
            {
                if (c != null && c.GetType().Name == "Weaponeffects") sb.Append("  <- помечен как эффект оружия");
            }

            ItemForgePlugin.Log.LogInfo(sb.ToString());

            for (int i = 0; i < t.childCount; i++) Describe(t.GetChild(i), depth + 1);
        }
    }
}
