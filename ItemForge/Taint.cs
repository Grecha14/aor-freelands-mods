using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using spell;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace ItemForge
{
    /// <summary>
    /// The smoke of Corrupting Touch, black and crimson, on the King of Hell set.
    ///
    /// Дым берётся у самого заклинания — у той его части, что игра вешает на поражённого.
    /// Это не эффект, а только вид: из копии вынимается всё, что умеет действовать, и
    /// остаются частицы. Цвет — от чёрного к багровому.
    ///
    /// Дым висит на клинке и щите комплекта, пока они в руке, и сочится с плеч и груди того,
    /// кто носит его доспех. Значка на панели эффектов нет, на характеристики он не влияет, и
    /// в сохранение не попадает ничего: снял вещь — дыма нет.
    /// </summary>
    internal static class Taint
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Dark;
        internal static ConfigEntry<string> Crimson;
        internal static ConfigEntry<float> OnBlade;
        internal static ConfigEntry<float> OnBody;

        private const int CorruptingTouch = 237;
        private const string BladeName = "KingOfHellTaint";
        private const string BodyName = "KingOfHellTaintBody";

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Taint", "Enabled", true,
                "Hang the smoke of Corrupting Touch, recoloured black and crimson, on the weapon "
                + "and armour of the King of Hell set. Only the look: no effect, no icon.");

            Dark = config.Bind("Taint", "Dark", "#0A0000",
                "The darker end of the smoke, in the usual six digits.");

            Crimson = config.Bind("Taint", "Crimson", "#7A0612",
                "The redder end of the smoke.");

            OnBlade = config.Bind("Taint", "OnBlade", 0.5f,
                new ConfigDescription("How large the smoke is on a blade, against its size on a body.",
                    new AcceptableValueRange<float>(0.05f, 3f)));

            OnBody = config.Bind("Taint", "OnBody", 0.8f,
                new ConfigDescription("How large the smoke is on the one wearing the armour.",
                    new AcceptableValueRange<float>(0.05f, 3f)));
        }

        // ----------------------------------------------------------------- образец

        private static GameObject template;
        private static bool asked;

        private static void Prepare()
        {
            if (asked || template != null || Enabled == null || !Enabled.Value) return;

            UISpellDatabase book = null;
            try { book = UISpellDatabase.Instance; } catch { }
            if (book == null) return;

            asked = true;

            try
            {
                UISpellInfo touch = book.GetByID(CorruptingTouch);
                if (touch == null)
                {
                    ItemForgePlugin.Log.LogWarning("Дым для комплекта: «Губительного прикосновения» нет в книге.");
                    return;
                }

                foreach (UIBuffInfo card in Spells.CardsOf(touch))
                {
                    if (card == null || card.visualEffect == null) continue;

                    foreach (UnitEffects look in card.visualEffect)
                    {
                        if (look == null || look.effectReference == null) continue;
                        if (!look.effectReference.RuntimeKeyIsValid()) continue;

                        AsyncOperationHandle<GameObject> handle =
                            Addressables.LoadAssetAsync<GameObject>(look.effectReference.RuntimeKey);

                        string from = card.id;
                        handle.Completed += done => Loaded(done, from);
                        return;
                    }
                }

                ItemForgePlugin.Log.LogWarning("Дым для комплекта: у «Губительного прикосновения» не нашлось вида эффекта.");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Дым для комплекта не запросился: " + e.Message);
            }
        }

        private static void Loaded(AsyncOperationHandle<GameObject> handle, string from)
        {
            try
            {
                if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
                {
                    ItemForgePlugin.Log.LogWarning("Дым для комплекта не загрузился.");
                    return;
                }

                GameObject copy = UnityEngine.Object.Instantiate(handle.Result);
                copy.name = "KingOfHellTaintTemplate";
                copy.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(copy);

                // Только вид: всё, что умеет действовать, вон.
                foreach (MonoBehaviour part in copy.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (part != null) UnityEngine.Object.Destroy(part);
                }

                Paint(copy);
                template = copy;

                ItemForgePlugin.Log.LogInfo($"Дым для комплекта взят с «{from}» ({handle.Result.name}): "
                    + $"систем частиц {copy.GetComponentsInChildren<ParticleSystem>(true).Length}.");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог забрать дым для комплекта: " + e.Message);
            }
        }

        /// <summary>Black to crimson, looping, in world space so nothing culls it by a local box.</summary>
        private static void Paint(GameObject copy)
        {
            Color dark, red;
            if (!ColorUtility.TryParseHtmlString(Dark.Value, out dark)) dark = new Color(0.04f, 0f, 0f);
            if (!ColorUtility.TryParseHtmlString(Crimson.Value, out red)) red = new Color(0.48f, 0.02f, 0.07f);

            foreach (ParticleSystem system in copy.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = system.main;

                float alpha = main.startColor.color.a;
                if (alpha <= 0.01f) alpha = 1f;

                dark.a = alpha;
                red.a = alpha;

                main.startColor = new ParticleSystem.MinMaxGradient(dark, red);
                main.loop = true;
                main.playOnAwake = true;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

                ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
                if (fade.enabled)
                {
                    UnityEngine.Gradient g = new UnityEngine.Gradient();
                    g.SetKeys(
                        new[] { new GradientColorKey(red, 0f), new GradientColorKey(dark, 0.6f),
                                new GradientColorKey(dark, 1f) },
                        new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(alpha, 0.15f),
                                new GradientAlphaKey(alpha * 0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
                    fade.color = new ParticleSystem.MinMaxGradient(g);
                }
            }
        }

        // ----------------------------------------------------------------- повесить и снять

        private static Transform Existing(Transform root, string name)
        {
            if (root == null) return null;

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child != root && child.name == name) return child;
            }

            return null;
        }

        private static void Hang(Transform host, string name, float size)
        {
            if (host == null) return;

            Transform already = Existing(host, name);
            if (already != null)
            {
                if (!already.gameObject.activeSelf) already.gameObject.SetActive(true);
                return;
            }

            Prepare();
            if (template == null) return;

            GameObject copy = UnityEngine.Object.Instantiate(template, host);
            copy.name = name;
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = Vector3.one * size;
            copy.SetActive(true);

            foreach (ParticleSystem system in copy.GetComponentsInChildren<ParticleSystem>(true))
            {
                system.Clear(true);
                system.Play(true);
            }
        }

        private static void Drop(Transform host, string name)
        {
            Transform already = Existing(host, name);
            if (already != null) UnityEngine.Object.Destroy(already.gameObject);
        }

        /// <summary>The blade or shield of the set in a hand: smoke on it while it is ours.</summary>
        internal static void OnWeapon(Transform weaponRoot, bool ours)
        {
            if (weaponRoot == null) return;

            try
            {
                Transform host = weaponRoot;
                foreach (Transform child in weaponRoot.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == "Model") { host = child; break; }
                }

                if (!ours || Enabled == null || !Enabled.Value) { Drop(host, BladeName); return; }

                Hang(host, BladeName, OnBlade.Value);
            }
            catch
            {
            }
        }

        private static float next;

        /// <summary>Now and then: smoke on everyone wearing the set armour, off everyone who is not.</summary>
        internal static void Tick()
        {
            if (Enabled == null || !Enabled.Value) return;
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 2f;

            if (Forge.Built.Count == 0) return;

            try
            {
                foreach (HumaniodUnit man in UnityEngine.Object.FindObjectsOfType<HumaniodUnit>())
                {
                    if (man == null || man.Data == null) continue;

                    Transform chest = man.GetBodyPart(BodyPart.chest);
                    if (chest == null) continue;

                    bool wears = !man.Data.isdead && Wears(man);

                    if (wears) Hang(chest, BodyName, OnBody.Value);
                    else Drop(chest, BodyName);
                }
            }
            catch
            {
            }
        }

        /// <summary>True when any armour piece of the set is on him.</summary>
        private static bool Wears(HumaniodUnit man)
        {
            EquipmentManager gear = man.equipmentmanger;
            if (gear == null || gear.equipInfos == null) return false;

            for (int i = 2; i < gear.equipInfos.Length; i++)
            {
                EquipInfo slot = gear.equipInfos[i];
                if (slot == null || !slot.IsEquiped() || slot.inventory == null) continue;

                UIEquipmentInfo piece = slot.inventory.itemInfo as UIEquipmentInfo;
                if (piece is UIArmorInfo && piece.EquipType != EquipSlotType.finger
                    && piece.EquipType != EquipSlotType.neck && Forge.Built.Contains(piece)) return true;
            }

            return false;
        }
    }
}
