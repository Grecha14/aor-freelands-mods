using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace ItemForge
{
    /// <summary>
    /// The glow around the blade, borrowed whole from a weapon that already has one.
    ///
    /// The game does not build these out of anything a mod can set. A glowing weapon simply
    /// carries an extra child object inside its model — on the Bane club it is called
    /// VFX_TwoHandedClub_T4_02_Bane — holding a small tree of particle systems: the halo, the
    /// sparks, two trails and two plumes of smoke. Nothing in the item data points at it, so it
    /// cannot be turned on by a number; the object itself has to be copied.
    ///
    /// So the donor's prefab is fetched through Addressables, the effect is lifted out of it and
    /// kept as a template, and a copy is hung on each forged weapon as it is drawn. The copy is
    /// then recoloured, which is what turns a fire aura into a dark one: the particle materials
    /// are additive and take their shade from the system's start colour.
    /// </summary>
    internal static class WeaponVfx
    {
        // Ниже — заимствование тумана у тёмного элементаля. Аура на персонаже из этого не
        // вышла и удалена, но тот же туман подмешан к клинку как «KingOfHellSmoke» и там
        // работает, поэтому машинерия остаётся: имена настроек исторические, «Body».
        internal static ConfigEntry<bool> StripScripts;
        internal static ConfigEntry<int> BodyBudget;
        internal static ConfigEntry<bool> BodyTrail;
        internal static ConfigEntry<string> BodyFromObject;
        internal static ConfigEntry<string> BodyFromAddress;
        internal static ConfigEntry<float> BodyDim;

        private static GameObject bodyTemplate;
        private static bool triedAssets;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> InPrefab;
        internal static ConfigEntry<string> ExtraFromObject;
        internal static ConfigEntry<float> ExtraScale;
        internal static ConfigEntry<int> SourceItem;
        internal static ConfigEntry<string> Color;
        internal static ConfigEntry<bool> Recolor;
        internal static ConfigEntry<float> Scale;
        internal static ConfigEntry<float> Length;
        internal static ConfigEntry<float> Shift;
        internal static ConfigEntry<float> Dim;
        internal static ConfigEntry<string> Axis;

        private static GameObject template;
        private static Vector3 offset;
        private static Quaternion rotation;
        private static Vector3 localScale = Vector3.one;

        private static bool asked;      // загрузку просим один раз за сессию

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Aura", "WeaponEffect", true,
                "Hang the glow of another weapon on the forged ones. Nothing is packed: the donor's "
                + "own effect object is copied at run time.");

            StripScripts = config.Bind("Aura", "StripScripts", false,
                "Strip the behaviour scripts from a borrowed effect. A borrowed prefab brings its "
                + "own logic along with its look, and a copy worn as decoration has no business "
                + "running any of it. Off: the fog carries none worth removing, and stripping cost "
                + "the effect more than it saved.");

            BodyBudget = config.Bind("Aura", "BodyEffectMaxParticles", 3000,
                new ConfigDescription(
                    "Total particles the borrowed fog may use. A spell's effect is built to flare "
                    + "for a few seconds, not to burn all day, and a permanent copy of one at full "
                    + "strength is what dropped the game to five frames once already.",
                    new AcceptableValueRange<int>(100, 50000)));

            BodyTrail = config.Bind("Aura", "BodyEffectTrail", false,
                "Leave the fog behind as the wearer moves instead of carrying it along.");

            BodyFromObject = config.Bind("Aura", "BodyEffectFromObject", "VFX_SK_DarknessElemental",
                "Name of the effect to borrow. Found inside the model the address below names, "
                + "chosen by this name rather than by the order children happen to sit in.");

            BodyFromAddress = config.Bind("Aura", "BodyEffectFromAddress", "DarknessElemental_Summon",
                "Model to fetch the effect from through the catalogue, so the creature carrying it "
                + "never has to be summoned. The whole creature is fetched and the effect lifted out.");

            BodyDim = config.Bind("Aura", "BodyEffectDim", 0.45f,
                new ConfigDescription("How much of the borrowed effect's own strength to keep.",
                    new AcceptableValueRange<float>(0.05f, 1f)));

            InPrefab = config.Bind("Aura", "WeaponEffectInPrefab", true,
                "Sew the glow into the model's prefab instead of hanging it on each weapon as it "
                + "is drawn. On, and the choice is a real one rather than an oversight: the "
                + "prefab is shared and has no second copy, so this lights the ordinary weapon "
                + "built from the same model too — but it is also the only way the glow reaches "
                + "the character screen, which draws its own copy of the model and never sees "
                + "anything hung on the one in hand. Off keeps the two weapons apart and gives "
                + "up the preview.");

            ExtraFromObject = config.Bind("Aura", "WeaponExtraFromObject", "VFX_SK_DarknessElemental",
                "A second effect laid over the blade, taken from the scene by name. The club's "
                + "glow is drawn with additive materials, which add light to what is behind them "
                + "and therefore cannot be black — asking for black there asks for nothing at all. "
                + "The elemental's darkness uses alpha-blended fog instead, which obscures rather "
                + "than shines, and that is why it can be black. Layering the two gives a blade "
                + "that burns red and smokes black at once. Empty for the glow alone.");

            ExtraScale = config.Bind("Aura", "WeaponExtraScale", 0.5f,
                new ConfigDescription(
                    "Size of that second effect. It was cut to wrap a creature, so at full size it "
                    + "swallows a sword whole.",
                    new AcceptableValueRange<float>(0.05f, 5f)));

            SourceItem = config.Bind("Aura", "WeaponEffectFrom", 829,
                new ConfigDescription(
                    "Item to borrow the effect from. 829 is the Bane club, whose glow runs the whole "
                    + "length of the weapon and throws light on the hand holding it.",
                    new AcceptableValueRange<int>(1, 999999)));

            Color = config.Bind("Aura", "WeaponEffectColor", "#2A0202",
                "Shade to repaint the borrowed effect in, as HTML. The donor's is an orange fire. "
                + "Black is not available: these particles are additive, they add light to what is "
                + "behind them, and adding black is the same as adding nothing — the aura would "
                + "simply vanish. A deep red turned down is as dark as it gets.");

            Recolor = config.Bind("Aura", "WeaponEffectRecolor", true,
                "Repaint the borrowed effect. Off keeps the donor's own orange fire.");

            Scale = config.Bind("Aura", "WeaponEffectScale", 1.0f,
                new ConfigDescription(
                    "Size of the borrowed effect relative to the donor's. The blade it hangs on may "
                    + "be drawn larger than the donor, and the effect does not follow that on its own.",
                    new AcceptableValueRange<float>(0.1f, 10f)));

            Length = config.Bind("Aura", "WeaponEffectLength", 0.78f,
                new ConfigDescription(
                    "How far the glow runs along the weapon, relative to the donor's. The trails "
                    + "reach about 1.92 at full size and the blade measures 1.5, so 0.78 is what "
                    + "stops the tail short of the grip instead of running past the hand.",
                    new AcceptableValueRange<float>(0.1f, 3f)));

            Shift = config.Bind("Aura", "WeaponEffectShift", 0.42f,
                new ConfigDescription(
                    "How far the glow is pushed along the blade, in metres. Positive moves it "
                    + "towards the hand, negative towards the point. The blade runs from plus 0.39 "
                    + "at the grip down to minus 1.11 at the tip, and the knot of light sits about "
                    + "a metre from the effect's own origin, so this figure is what lands it on "
                    + "the steel rather than past either end.",
                    new AcceptableValueRange<float>(-2f, 2f)));

            Axis = config.Bind("Aura", "WeaponEffectTurn", "0,0,0",
                "Extra turn given to the borrowed effect, in degrees. The effect is not "
                + "symmetrical: a dense knot of light sits at one end and the trails stream away "
                + "from it, so half a turn about Y swaps which end of the blade the knot sits on. "
                + "Which of the two is the point is not worth deriving — the arithmetic from the "
                + "mesh gave the opposite of what the screen showed twice over, so this is set by "
                + "looking rather than by calculating. The shift below moves along the same axis "
                + "and has to be flipped in sign whenever this is.");

            Dim = config.Bind("Aura", "WeaponEffectDim", 0.45f,
                new ConfigDescription(
                    "How much of the donor's brightness to keep. Turning it down is what makes the "
                    + "aura read as dark, since the colour alone cannot: the particles are "
                    + "additive and a dark shade at full brightness still burns bright. This is "
                    + "as close to black as an additive effect reaches — real black on the blade "
                    + "needs the elemental's smoke layered over it, which has to be caught from "
                    + "the scene first.",
                    new AcceptableValueRange<float>(0.05f, 1f)));

        }

        /// <summary>
        /// Asks Addressables for the donor prefab once, and keeps the effect out of it.
        /// The answer arrives a moment later, so the first weapon drawn may go without —
        /// the next one will have it.
        /// </summary>
        internal static void Prepare()
        {
            if (!Enabled.Value || asked || template != null) return;
            asked = true;

            try
            {
                UIItemDatabase db = UIItemDatabase.Instance;
                if (db == null) { asked = false; return; }

                UIItemInfo donor = db.GetByID(SourceItem.Value);
                if (donor == null)
                {
                    ItemForgePlugin.Log.LogWarning($"Не нашёл предмет {SourceItem.Value} для свечения.");
                    return;
                }

                if (donor.PrefabReference == null || !donor.PrefabReference.RuntimeKeyIsValid())
                {
                    ItemForgePlugin.Log.LogWarning($"У «{donor.Name}» нет ссылки на модель, свечение взять неоткуда.");
                    return;
                }

                AsyncOperationHandle<GameObject> handle =
                    Addressables.LoadAssetAsync<GameObject>(donor.PrefabReference.RuntimeKey);

                handle.Completed += Loaded;
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог запросить модель-донора свечения: " + e);
            }
        }

        private static void Loaded(AsyncOperationHandle<GameObject> handle)
        {
            try
            {
                if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
                {
                    ItemForgePlugin.Log.LogWarning("Модель-донор свечения не загрузилась.");
                    return;
                }

                Transform found = FindGlow(handle.Result.transform);
                if (found == null)
                {
                    ItemForgePlugin.Log.LogWarning("В модели-доноре нет объекта эффекта — искал имя, "
                        + "начинающееся с VFX.");
                    return;
                }

                // Запоминаем, как эффект стоял у донора: копия должна встать так же,
                // иначе свечение уедет в сторону от клинка.
                offset = found.localPosition;
                rotation = found.localRotation;
                localScale = found.localScale;

                template = UnityEngine.Object.Instantiate(found.gameObject);
                template.name = "KingOfHellAura";
                template.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(template);

                Measure("донор " + found.root.name, found.parent, found);

                int systems = template.GetComponentsInChildren<ParticleSystem>(true).Length;
                ItemForgePlugin.Log.LogInfo($"Свечение взято с «{found.name}»: систем частиц {systems}.");

                // Образец приехал — разбираем очередь тех, кто его дожидался.
                if (waiting.Count > 0)
                {
                    List<UIItemInfo> queue = new List<UIItemInfo>(waiting);
                    waiting.Clear();

                    ItemForgePlugin.Log.LogInfo($"Дошиваю отложенное: {queue.Count}.");
                    foreach (UIItemInfo item in queue) InjectInto(item);
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог забрать свечение из модели-донора: " + e);
            }
        }

        /// <summary>
        /// Puts the effect inside the model's prefab, the way the game itself does it.
        ///
        /// A glowing weapon in this game is not lit by anything at run time: the effect is simply
        /// part of its prefab, so it comes along wherever that model is drawn — in the world, in
        /// the hand, and in the preview on the character screen. Hanging a copy on the spawned
        /// model reached only the first of those, which is why the blade glowed in the world and
        /// stayed dark in the menu.
        ///
        /// Editing the loaded prefab reaches every future copy at once. It also reaches the item
        /// the model was borrowed from, which will now glow too — the prefab is shared, and there
        /// is no second copy of it to edit instead.
        /// </summary>
        internal static void InjectInto(UIItemInfo item)
        {
            if (!Enabled.Value || !InPrefab.Value || item == null) return;
            if (item.PrefabReference == null || !item.PrefabReference.RuntimeKeyIsValid()) return;
            if (injected.Contains(item.ID)) return;

            // Пока образца нет, вещь ждёт его в очереди, а не выбывает насовсем.
            if (template == null)
            {
                Prepare();
                if (!waiting.Contains(item)) waiting.Add(item);
                return;
            }

            injected.Add(item.ID);

            try
            {
                AsyncOperationHandle<GameObject> handle =
                    Addressables.LoadAssetAsync<GameObject>(item.PrefabReference.RuntimeKey);

                handle.Completed += loaded => Inject(loaded, item.Name);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError($"Не смог запросить модель «{item.Name}»: " + e);
            }
        }

        private static void Inject(AsyncOperationHandle<GameObject> handle, string what)
        {
            try
            {
                if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null) return;

                Prepare();
                if (template == null)
                {
                    // Образец ещё едет. Раньше на этом всё и кончалось: попытка была одна,
                    // а приезжал он секундой позже — и вшивать становилось некому.
                    ItemForgePlugin.Log.LogInfo($"Образец свечения ещё не готов, «{what}» отложу.");
                    return;
                }

                Transform root = handle.Result.transform;
                if (Existing(root) != null) return;

                // Если у модели уже есть своё свечение, второе не нужно: так бывает, когда
                // основой взято оружие, которое светится само.
                if (FindGlow(root) != null)
                {
                    ItemForgePlugin.Log.LogInfo($"У «{what}» своё свечение, вшивать нечего.");
                    return;
                }

                Transform host = Host(root);
                Measure("наш " + what, host, null);

                GameObject copy = UnityEngine.Object.Instantiate(template, host);
                copy.name = "KingOfHellAura";
                Place(copy.transform);
                copy.SetActive(true);

                if (Recolor.Value) Repaint(copy);

                ItemForgePlugin.Log.LogInfo($"Свечение вшито в модель «{what}», узел «{host.name}».");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError($"Не смог вшить свечение в «{what}»: " + e);
            }
        }

        private static readonly HashSet<int> injected = new HashSet<int>();

        // Вещи, для которых вшивание отложено до приезда образца. Загрузка образца
        // асинхронна, а ковка идёт сразу, поэтому первая попытка почти всегда ранняя.
        private static readonly List<UIItemInfo> waiting = new List<UIItemInfo>();
        /// <summary>The first object that looks like a glow: enough for a weapon donor,
        /// which carries exactly one.</summary>
        private static Transform FindGlow(Transform root)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child != root && child.name.StartsWith("VFX", StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }
            }
            return null;
        }
        /// <summary>Hangs a copy of the effect on a weapon, or takes the one already there away.</summary>
        internal static void Apply(Transform weaponRoot, bool wanted)
        {
            if (weaponRoot == null) return;

            try
            {
                // Ищем там же, куда вешаем, а не в корне. Раньше поиск шёл по прямым детям
                // корня, а копия жила внутри модели — она не находилась никогда, и мод вешал
                // новую каждые полсекунды. Полторы тысячи копий по пять тысяч частиц в каждой
                // уронили игру до пяти кадров.
                Transform host = Host(weaponRoot);
                Transform existing = Existing(host);

                if (!wanted || !Enabled.Value)
                {
                    if (existing != null) UnityEngine.Object.Destroy(existing.gameObject);
                    return;
                }

                // Своё свечение модели трогать не надо — оно уже стоит как задумано.
                foreach (Transform child in host.GetComponentsInChildren<Transform>(true))
                {
                    if (child != host && child.name.StartsWith("VFX", StringComparison.OrdinalIgnoreCase)) return;
                }

                // Дым пробуем подмешать при каждом обходе, а не только когда свечение вешается
                // впервые. Источник дыма появляется позже — когда в сцене мелькнёт элементаль, —
                // а свечение к тому времени уже висит, и прежний код выходил раньше этой строки.
                AddSmoke(host);

                Prepare();
                if (template == null || existing != null) return;

                GameObject copy = UnityEngine.Object.Instantiate(template, host);
                copy.name = "KingOfHellAura";
                Place(copy.transform);
                copy.SetActive(true);

                if (Recolor.Value) Repaint(copy);

                ItemForgePlugin.Log.LogInfo($"Свечение повешено на «{weaponRoot.name}», узел «{host.name}».");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог повесить свечение: " + e);
            }
        }
        /// <summary>
        /// Takes the working parts out of a borrowed spell, leaving only what is seen.
        ///
        /// A spell's prefab carries its behaviour as well as its look, and a copy worn as gear has
        /// no business casting anything.
        /// </summary>
        private static void Strip(GameObject copy)
        {
            if (!StripScripts.Value) return;

            foreach (MonoBehaviour part in copy.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (part != null) UnityEngine.Object.Destroy(part);
            }
        }
        /// <summary>Lights a copy back up if something has switched it off since.</summary>
        private static void Revive(GameObject copy)
        {
            if (copy == null) return;

            bool woken = false;

            if (!copy.activeSelf) { copy.SetActive(true); woken = true; }

            foreach (ParticleSystem system in copy.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (system.isPlaying) continue;

                system.Play(true);
                woken = true;
            }

            if (woken) ItemForgePlugin.Log.LogInfo($"Эффект «{copy.name}» зажжён заново.");
        }

        /// <summary>
        /// Starts a copy playing, instead of trusting it to start itself.
        ///
        /// A particle system only plays on its own when told to at creation, and an effect the
        /// game fires off mid-battle usually is not — something else starts it when the moment
        /// comes. A copy hung on a sword has nothing to start it, so it sat there complete,
        /// correctly placed and wholly invisible. The donor weapon's glow worked all along
        /// precisely because a weapon's own effect is set to play the moment it exists.
        /// </summary>
        private static void Wake(GameObject copy)
        {
            foreach (ParticleSystem system in copy.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = system.main;
                main.playOnAwake = true;
                main.loop = true;

                system.Clear(true);
                system.Play(true);
            }
        }

        /// <summary>Total particles a tree may spawn at once.</summary>
        private static int Count(GameObject copy)
        {
            int total = 0;
            foreach (ParticleSystem system in copy.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = system.main;
                total += main.maxParticles;
            }
            return total;
        }
        /// <summary>
        /// Lays the borrowed darkness over the blade, on top of the glow already there.
        ///
        /// Kept separate from the glow because the two are made of opposite stuff: one adds light
        /// and can never be black, the other blocks it and can never be bright. Together they
        /// read as a blade that burns and smokes at the same time, which neither could do alone.
        /// </summary>
        private static void AddSmoke(Transform host)
        {
            string wanted = (ExtraFromObject.Value ?? "").Trim();
            if (wanted.Length == 0 || host == null) return;

            try
            {
                Transform already = Existing(host, "KingOfHellSmoke");
                if (already != null) { Revive(already.gameObject); return; }

                GameObject source = BodySource();
                if (source == null) return;

                GameObject copy = UnityEngine.Object.Instantiate(source, host);
                copy.name = "KingOfHellSmoke";
                copy.transform.localPosition = offset + new Vector3(0f, 0f, Shift.Value);
                copy.transform.localRotation = Quaternion.identity;
                copy.transform.localScale = Vector3.one * ExtraScale.Value;
                copy.SetActive(true);
                Wake(copy);

                ItemForgePlugin.Log.LogInfo($"Тёмный дым подмешан к клинку, узел «{host.name}».");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог подмешать дым: " + e);
            }
        }

        /// <summary>The effect already hanging under a node, found by name at any depth.</summary>
        private static Transform Existing(Transform root, string name = "KingOfHellAura")
        {
            if (root == null) return null;

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child != root && child.name == name) return child;
            }
            return null;
        }

        /// <summary>
        /// Stands the copy where the donor kept it, then trims it to the blade.
        ///
        /// The donor's long axis is Y — its mesh runs 1.72 that way and its trails are scaled two
        /// along it — so shortening and shifting both happen on Y. A blade is shorter than a
        /// club, and at the donor's length the glow ran past the grip and out beyond the pommel.
        /// </summary>
        private static void Place(Transform copy)
        {
            // Поворот задан числами, а не выбором оси: разница между донором и нашим клинком
            // измерена по дампам обеих моделей, и это ровно половина оборота вокруг Y.
            copy.localPosition = offset + new Vector3(0f, 0f, Shift.Value);
            copy.localRotation = rotation * Quaternion.Euler(Degrees());

            // Длина эффекта идёт по Z, а не по Y: внутри него glow_001 смещён на 0.88 именно
            // по Z, и следы тянутся вдоль той же оси. Прежде сжималась Y — то есть не длина,
            // а поперечник, и эффект от этого только расплющивался.
            Vector3 size = localScale * Scale.Value;
            copy.localScale = new Vector3(size.x, size.y, size.z * Length.Value);
        }

        private static Vector3 Degrees()
        {
            string[] parts = (Axis.Value ?? "").Split(',');
            if (parts.Length != 3) return Vector3.zero;

            float x, y, z;
            if (!float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out x)) x = 0f;
            if (!float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out y)) y = 0f;
            if (!float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out z)) z = 0f;

            return new Vector3(x, y, z);
        }

        private static Transform Host(Transform weaponRoot)
        {
            foreach (Transform child in weaponRoot.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "Model") return child;
            }
            return weaponRoot;
        }
        /// <summary>
        /// Measures an effect that is known to be drawn, for comparison.
        ///
        /// У нашей ауры всё в порядке по всем цифрам, а движок её не рисует. Дальше гадать
        /// о причине бессмысленно: рядом на том же персонаже работает факел — такая же
        /// система частиц, которая видна. Разница между ними и есть ответ.
        /// </summary>
        private static void Control(string name)
        {
            GameObject found = GameObject.Find(name);
            if (found == null)
            {
                ItemForgePlugin.Log.LogInfo($"  образца «{name}» в сцене нет");
                return;
            }

            ItemForgePlugin.Log.LogInfo($"=== образец «{name}» (он виден на экране) ===");
        }
        private static void Repaint(GameObject copy)
        {
            Color shade;
            if (!ColorUtility.TryParseHtmlString(Color.Value, out shade))
            {
                ItemForgePlugin.Log.LogWarning($"Не разобрал цвет свечения '{Color.Value}', оставляю донорский.");
                return;
            }

            foreach (ParticleSystem system in copy.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = system.main;

                Color was = main.startColor.color;
                main.startColor = new Color(shade.r * Dim.Value, shade.g * Dim.Value,
                    shade.b * Dim.Value, was.a * Dim.Value);
            }
        }
    
        // ---------------------------------------------------------------------------
        // Заимствование тумана у тёмного элементаля.
        //
        // Ради ауры на персонаже это и писалось, и там не получилось: движок отсекал
        // копию по границам, которые оставались в локальных координатах, и до отрисовки
        // дело не доходило. Ауру с персонажа сняли. Но тот же туман подмешан к клинку
        // как «KingOfHellSmoke», и там он виден и работает, поэтому код остаётся —
        // вместе с историческими именами «Body» в настройках.
        // ---------------------------------------------------------------------------

    private static void Measure(string what, Transform host, Transform effect)
    {
        //IL_0044: Unknown result type (might be due to invalid IL or missing references)
        //IL_0054: Unknown result type (might be due to invalid IL or missing references)
        //IL_0064: Unknown result type (might be due to invalid IL or missing references)
        //IL_0081: Unknown result type (might be due to invalid IL or missing references)
        //IL_0091: Unknown result type (might be due to invalid IL or missing references)
        //IL_00a1: Unknown result type (might be due to invalid IL or missing references)
        //IL_0257: Unknown result type (might be due to invalid IL or missing references)
        //IL_0267: Unknown result type (might be due to invalid IL or missing references)
        //IL_0277: Unknown result type (might be due to invalid IL or missing references)
        //IL_0294: Unknown result type (might be due to invalid IL or missing references)
        //IL_02a4: Unknown result type (might be due to invalid IL or missing references)
        //IL_02b4: Unknown result type (might be due to invalid IL or missing references)
        //IL_02d1: Unknown result type (might be due to invalid IL or missing references)
        //IL_02e1: Unknown result type (might be due to invalid IL or missing references)
        //IL_02f1: Unknown result type (might be due to invalid IL or missing references)
        //IL_00ea: Unknown result type (might be due to invalid IL or missing references)
        //IL_00ef: Unknown result type (might be due to invalid IL or missing references)
        //IL_012a: Unknown result type (might be due to invalid IL or missing references)
        //IL_013b: Unknown result type (might be due to invalid IL or missing references)
        //IL_014c: Unknown result type (might be due to invalid IL or missing references)
        //IL_016a: Unknown result type (might be due to invalid IL or missing references)
        //IL_017b: Unknown result type (might be due to invalid IL or missing references)
        //IL_018c: Unknown result type (might be due to invalid IL or missing references)
        //IL_01ae: Unknown result type (might be due to invalid IL or missing references)
        //IL_01d0: Unknown result type (might be due to invalid IL or missing references)
        //IL_01e5: Unknown result type (might be due to invalid IL or missing references)
        try
        {
            if (host != null)
            {
                ItemForgePlugin.Log.LogInfo(("ЗАМЕР " + what + ": узел «" + host.name + "» " + $"поворот {host.localEulerAngles.x:0.#},{host.localEulerAngles.y:0.#},{host.localEulerAngles.z:0.#} " + $"масштаб {host.localScale.x:0.##},{host.localScale.y:0.##},{host.localScale.z:0.##}"));
                MeshFilter[] componentsInChildren = host.GetComponentsInChildren<MeshFilter>(true);
                foreach (MeshFilter val in componentsInChildren)
                {
                    if (!(val.sharedMesh == null))
                    {
                        Bounds bounds = val.sharedMesh.bounds;
                        ItemForgePlugin.Log.LogInfo(("ЗАМЕР " + what + ": меш «" + val.name + "» " + $"габариты {bounds.size.x:0.##},{bounds.size.y:0.##},{bounds.size.z:0.##} " + $"центр {bounds.center.x:0.##},{bounds.center.y:0.##},{bounds.center.z:0.##} " + $"поворот узла {val.transform.localEulerAngles.x:0.#}," + $"{val.transform.localEulerAngles.y:0.#},{val.transform.localEulerAngles.z:0.#}"));
                        break;
                    }
                }
            }
            if (effect != null)
            {
                ItemForgePlugin.Log.LogInfo(("ЗАМЕР " + what + ": эффект «" + effect.name + "» " + $"поз {effect.localPosition.x:0.##},{effect.localPosition.y:0.##},{effect.localPosition.z:0.##} " + $"поворот {effect.localEulerAngles.x:0.#},{effect.localEulerAngles.y:0.#},{effect.localEulerAngles.z:0.#} " + $"масштаб {effect.localScale.x:0.##},{effect.localScale.y:0.##},{effect.localScale.z:0.##}"));
            }
        }
        catch (Exception ex)
        {
            ItemForgePlugin.Log.LogError(("Замер не удался: " + ex));
        }
    }

    private static Transform FindEffect(Transform root, string wanted)
    {
        List<Transform> list = new List<Transform>();
        List<string> list2 = new List<string>();
        Transform[] componentsInChildren = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform val in componentsInChildren)
        {
            if (!(val == root) && (val.name.IndexOf("VFX", StringComparison.OrdinalIgnoreCase) >= 0 || !(val.GetComponent<ParticleSystem>() == null)))
            {
                list.Add(val);
                if (list2.Count < 40)
                {
                    int num = val.GetComponentsInChildren<ParticleSystem>(true).Length;
                    list2.Add($"{val.name}({num})");
                }
            }
        }
        if (list2.Count > 0)
        {
            ItemForgePlugin.Log.LogInfo(("Внутри «" + root.name + "» эффекты (в скобках систем частиц): " + string.Join(", ", list2.ToArray())));
        }
        wanted = (wanted ?? "").Trim();
        if (wanted.Length > 0)
        {
            foreach (Transform item in list)
            {
                if (string.Equals(item.name, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }
            foreach (Transform item2 in list)
            {
                if (item2.name.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return item2;
                }
            }
            ItemForgePlugin.Log.LogInfo(("Эффекта «" + wanted + "» внутри не оказалось, беру самый богатый."));
        }
        Transform result = null;
        int num2 = 0;
        foreach (Transform item3 in list)
        {
            int num3 = item3.GetComponentsInChildren<ParticleSystem>(true).Length;
            if (num3 > num2)
            {
                num2 = num3;
                result = item3;
            }
        }
        return result;
    }

    private static GameObject BodySource()
    {
        if (bodyTemplate != null)
        {
            return bodyTemplate;
        }
        string text = (BodyFromObject.Value ?? "").Trim();
        if (text.Length == 0)
        {
            return null;
        }
        if (!triedAssets)
        {
            triedAssets = true;
            Seek(BodyFromAddress.Value, insideModel: true);
            Seek(text, insideModel: false);
        }
        return FromScene(text);
    }

    private static string FindAddress(string fragment)
    {
        fragment = (fragment ?? "").Trim();
        if (fragment.Length == 0)
        {
            return null;
        }
        try
        {
            List<string> list = new List<string>();
            foreach (UnityEngine.AddressableAssets.ResourceLocators.IResourceLocator resourceLocator in Addressables.ResourceLocators)
            {
                if (resourceLocator == null || resourceLocator.Keys == null)
                {
                    continue;
                }
                foreach (object key in resourceLocator.Keys)
                {
                    if (key is string text && text.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0 && list.Count < 8)
                    {
                        list.Add(text);
                    }
                }
            }
            if (list.Count == 0)
            {
                ItemForgePlugin.Log.LogInfo(("В каталоге нет ключей со словом «" + fragment + "»."));
                return null;
            }
            ItemForgePlugin.Log.LogInfo(("Ключи каталога со словом «" + fragment + "»: " + string.Join(", ", list.ToArray())));
            string text2 = Prefer(list, "unitprefab/") ?? Prefer(list, "effectprefab/") ?? Prefer(list, "prefab");
            if (text2 == null)
            {
                ItemForgePlugin.Log.LogInfo(("Среди ключей «" + fragment + "» нет ни одной модели."));
                return null;
            }
            ItemForgePlugin.Log.LogInfo(("Беру ключ «" + text2 + "»."));
            return text2;
        }
        catch (Exception ex)
        {
            ItemForgePlugin.Log.LogWarning(("Обход каталога сорвался: " + ex.Message));
            return null;
        }
    }

    private static string Prefer(List<string> keys, string prefix)
    {
        foreach (string key in keys)
        {
            if (key.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return key;
            }
        }
        foreach (string key2 in keys)
        {
            if (key2.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return key2;
            }
        }
        return null;
    }

    private static void Seek(string name, bool insideModel)
    {
        //IL_0080: Unknown result type (might be due to invalid IL or missing references)
        //IL_0085: Unknown result type (might be due to invalid IL or missing references)
        name = (name ?? "").Trim();
        if (name.Length == 0)
        {
            return;
        }
        try
        {
            GameObject val = Resources.Load<GameObject>(name);
            if (val != null)
            {
                Take(val, name, "ресурсов", insideModel);
                return;
            }
            AsyncOperationHandle<GameObject> val2 = Addressables.LoadAssetAsync<GameObject>((object)(FindAddress(name) ?? name));
            val2.Completed += delegate(AsyncOperationHandle<GameObject> loaded)
            {
                //IL_0002: Unknown result type (might be due to invalid IL or missing references)
                //IL_0008: Invalid comparison between Unknown and I4
                if ((int)loaded.Status != 1 || loaded.Result == null)
                {
                    ItemForgePlugin.Log.LogInfo(("«" + name + "» в каталоге нет."));
                }
                else
                {
                    Take(loaded.Result, name, "каталога", insideModel);
                }
            };
        }
        catch (Exception ex)
        {
            ItemForgePlugin.Log.LogWarning(("Не смог достать «" + name + "»: " + ex.Message));
        }
    }

    private static void Take(GameObject found, string name, string where, bool insideModel)
    {
        if (bodyTemplate != null)
        {
            return;
        }
        GameObject source = found;
        if (insideModel)
        {
            Transform val = FindEffect(found.transform, BodyFromObject.Value);
            if (val == null)
            {
                ItemForgePlugin.Log.LogInfo(("В модели «" + name + "» эффекта не нашлось."));
                return;
            }
            ItemForgePlugin.Log.LogInfo(("Из модели «" + name + "» беру эффект «" + val.name + "»."));
            source = val.gameObject;
        }
        Adopt(source, name, where);
    }

    private static GameObject Adopt(GameObject source, string wanted, string where)
    {
        bodyTemplate = UnityEngine.Object.Instantiate<GameObject>(source);
        bodyTemplate.name = "KingOfHellBodyAura";
        Strip(bodyTemplate);
        int num = bodyTemplate.GetComponentsInChildren<ParticleSystem>(true).Length;
        int num2 = Budget(bodyTemplate);
        Thin(bodyTemplate);
        if (BodyTrail.Value)
        {
            Trail(bodyTemplate);
        }
        bodyTemplate.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(bodyTemplate);
        ItemForgePlugin.Log.LogInfo(($"Аура взята из {where}: «{wanted}», систем {num}, " + $"частиц было {num2}, стало {Count(bodyTemplate)}."));
        return bodyTemplate;
    }

    private static GameObject FromScene(string wanted)
    {
        try
        {
            ParticleSystem[] array = UnityEngine.Object.FindObjectsOfType<ParticleSystem>();
            foreach (ParticleSystem val in array)
            {
                if (!(val == null))
                {
                    Transform val2 = val.transform;
                    while (val2 != null && !val2.name.StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        val2 = val2.parent;
                    }
                    if (!(val2 == null))
                    {
                        return Adopt(val2.gameObject, wanted, "сцены");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ItemForgePlugin.Log.LogError(("Поиск эффекта в сцене сорвался: " + ex));
        }
        return null;
    }

    private static void Trail(GameObject copy)
    {
        //IL_0010: Unknown result type (might be due to invalid IL or missing references)
        //IL_0015: Unknown result type (might be due to invalid IL or missing references)
        //IL_001e: Unknown result type (might be due to invalid IL or missing references)
        //IL_0023: Unknown result type (might be due to invalid IL or missing references)
        ParticleSystem[] componentsInChildren = copy.GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem obj in componentsInChildren)
        {
            ParticleSystem.MainModule main = obj.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.InheritVelocityModule inheritVelocity = obj.inheritVelocity;
            inheritVelocity.enabled = false;
        }
    }

    private static void Thin(GameObject copy)
    {
        //IL_0021: Unknown result type (might be due to invalid IL or missing references)
        //IL_0026: Unknown result type (might be due to invalid IL or missing references)
        //IL_0029: Unknown result type (might be due to invalid IL or missing references)
        //IL_002e: Unknown result type (might be due to invalid IL or missing references)
        //IL_0032: Unknown result type (might be due to invalid IL or missing references)
        //IL_0037: Unknown result type (might be due to invalid IL or missing references)
        //IL_003a: Unknown result type (might be due to invalid IL or missing references)
        //IL_0040: Unknown result type (might be due to invalid IL or missing references)
        //IL_0046: Unknown result type (might be due to invalid IL or missing references)
        //IL_004c: Unknown result type (might be due to invalid IL or missing references)
        //IL_005d: Unknown result type (might be due to invalid IL or missing references)
        //IL_0062: Unknown result type (might be due to invalid IL or missing references)
        if (!(BodyDim.Value >= 1f))
        {
            ParticleSystem[] componentsInChildren = copy.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < componentsInChildren.Length; i++)
            {
                ParticleSystem.MainModule main = componentsInChildren[i].main;
                ParticleSystem.MinMaxGradient startColor = main.startColor;
                Color color = startColor.color;
                main.startColor = (ParticleSystem.MinMaxGradient)(new Color(color.r, color.g, color.b, color.a * BodyDim.Value));
            }
        }
    }

    private static int Budget(GameObject copy)
    {
        //IL_0035: Unknown result type (might be due to invalid IL or missing references)
        //IL_003a: Unknown result type (might be due to invalid IL or missing references)
        //IL_0058: Unknown result type (might be due to invalid IL or missing references)
        //IL_005d: Unknown result type (might be due to invalid IL or missing references)
        int num = Count(copy);
        if (num <= BodyBudget.Value)
        {
            return num;
        }
        float num2 = (float)BodyBudget.Value / (float)num;
        ParticleSystem[] componentsInChildren = copy.GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem obj in componentsInChildren)
        {
            ParticleSystem.MainModule main = obj.main;
            main.maxParticles = Mathf.Max(1, Mathf.RoundToInt((float)main.maxParticles * num2));
            ParticleSystem.EmissionModule emission = obj.emission;
            emission.rateOverTimeMultiplier = emission.rateOverTimeMultiplier * num2;
            emission.rateOverDistanceMultiplier = emission.rateOverDistanceMultiplier * num2;
        }
        return num;
    }
}
}
