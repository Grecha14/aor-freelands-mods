using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UMA;
using UMA.CharacterSystem;
using UnityEngine;

namespace DemonLook
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class DemonLookPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "aor.demonlook";
        public const string PluginName = "Demon Race";
        public const string PluginVersion = "0.22.0";

        // The string the game uses to identify the race in its own UI arrays.
        internal const string DemonRaceName = "Demon";

        // Demon has no UMA model of its own, so the avatar is built from the human one.
        internal const string AvatarBaseRace = "Human";

        internal static ManualLogSource Log;

        // The human UMA model exposes 46 DNA sliders, all 0..1 with 0.5 as neutral.
        // Listed here are the ones that carry a demonic read; the rest stay vanilla.
        // These values are a starting point, meant to be tuned from the config by eye.
        private static readonly KeyValuePair<string, float>[] Profile =
        {
            // build: solid rather than towering. Matching the orc exactly is not possible
            // here, since the orc is a separate UMA model whose neutral is already larger
            // than the human one these numbers are applied to.
            new KeyValuePair<string, float>("height",             0.60f),
            new KeyValuePair<string, float>("upperMuscle",        0.66f),
            new KeyValuePair<string, float>("lowerMuscle",        0.60f),
            new KeyValuePair<string, float>("upperWeight",        0.48f),
            new KeyValuePair<string, float>("lowerWeight",        0.45f),
            new KeyValuePair<string, float>("belly",              0.42f),
            new KeyValuePair<string, float>("waist",              0.42f),
            new KeyValuePair<string, float>("neckThickness",      0.62f),

            // arms and hands stay exactly human: the only difference there is skin colour
            new KeyValuePair<string, float>("armLength",          0.50f),
            new KeyValuePair<string, float>("forearmLength",      0.50f),
            new KeyValuePair<string, float>("armWidth",           0.50f),
            new KeyValuePair<string, float>("forearmWidth",       0.50f),
            new KeyValuePair<string, float>("handsSize",          0.50f),
            new KeyValuePair<string, float>("feetSize",           0.50f),
            new KeyValuePair<string, float>("legsSize",           0.55f),

            // skull: low heavy brow, gaunt sharp cheeks
            new KeyValuePair<string, float>("headSize",           0.47f),
            new KeyValuePair<string, float>("headWidth",          0.56f),
            new KeyValuePair<string, float>("foreheadSize",       0.66f),
            new KeyValuePair<string, float>("foreheadPosition",   0.34f),
            new KeyValuePair<string, float>("cheekSize",          0.30f),
            new KeyValuePair<string, float>("cheekPosition",      0.65f),
            new KeyValuePair<string, float>("lowCheekPronounced", 0.80f),
            new KeyValuePair<string, float>("lowCheekPosition",   0.40f),

            // eyes: narrow and slanted
            new KeyValuePair<string, float>("eyeSize",            0.42f),
            new KeyValuePair<string, float>("eyeRotation",        0.76f),

            // ears: long, swept back and up
            new KeyValuePair<string, float>("earsSize",           0.85f),
            new KeyValuePair<string, float>("earsPosition",       0.35f),
            new KeyValuePair<string, float>("earsRotation",       0.90f),

            // nose: small, flattened, bestial
            new KeyValuePair<string, float>("noseSize",           0.36f),
            new KeyValuePair<string, float>("noseWidth",          0.42f),
            new KeyValuePair<string, float>("nosePronounced",     0.30f),
            new KeyValuePair<string, float>("noseFlatten",        0.70f),
            new KeyValuePair<string, float>("noseInclination",    0.60f),
            new KeyValuePair<string, float>("noseCurve",          0.45f),

            // jaw and mouth: heavy jaw, wide thin mouth
            new KeyValuePair<string, float>("jawsSize",           0.76f),
            new KeyValuePair<string, float>("jawsPosition",       0.45f),
            new KeyValuePair<string, float>("mandibleSize",       0.70f),
            new KeyValuePair<string, float>("chinSize",           0.60f),
            new KeyValuePair<string, float>("chinPronounced",     0.70f),
            new KeyValuePair<string, float>("chinPosition",       0.45f),
            new KeyValuePair<string, float>("mouthSize",          0.60f),
            new KeyValuePair<string, float>("lipsSize",           0.34f),
        };

        internal static readonly Dictionary<string, ConfigEntry<float>> DnaConfig =
            new Dictionary<string, ConfigEntry<float>>();

        internal static ConfigEntry<bool> AddRaceOption;
        internal static ConfigEntry<string> RaceLabel;
        internal static ConfigEntry<KeyCode> ApplyKey;
        internal static ConfigEntry<KeyCode> CaptureKey;
        internal static ConfigEntry<KeyCode> ResetKey;
        internal static ConfigEntry<bool> ApplyColors;
        internal static ConfigEntry<string> SkinColor;
        internal static ConfigEntry<string> EyeColor;
        internal static ConfigEntry<string> HairColor;
        internal static ConfigEntry<string> ExtraColors;
        internal static ConfigEntry<string> BodyPaint;
        internal static ConfigEntry<string> FacePaint;
        internal static ConfigEntry<bool> ListPaints;

        // Номер рецепта. Растёт всякий раз, когда меняются задуманные значения набора.
        private const int RecipeVersion = 11;

        /// <summary>
        /// Brings the settings file back in line with what this build intends.
        ///
        /// BepInEx writes the file once and never revisits it, so a value stored by an older
        /// build outlives any change made in code. The trait's wording lives in that file, which
        /// is why a rewritten description kept showing the old text however many times it was
        /// changed. When the number below moves, stored values go back to what this build
        /// declares; anything tuned by hand afterwards stands until it moves again.
        /// </summary>
        private void ApplyRecipe()
        {
            ConfigEntry<int> stored = Config.Bind("Meta", "RecipeVersion", 0,
                "Recipe this settings file was last brought in line with. Raising it in the mod "
                + "resets every other setting here to the value the mod ships with.");

            if (stored.Value >= RecipeVersion) return;

            int reset = 0;
            foreach (ConfigDefinition key in new List<ConfigDefinition>(Config.Keys))
            {
                if (key.Section == "Meta") continue;

                // Разметку сброс не трогает никогда. Метки ставятся ногами по конкретной
                // пещере, их нельзя ни вывести из умолчаний, ни восстановить после — а
                // цена ошибки уже известна: восемь мест появления однажды превратились в
                // одно, и сцена сломалась. Настройка на то и настройка, что её задают; эти
                // же задаются трудом, и трогать их автоматике нечего.
                if (key.Key == "HeroSpot" || key.Key == "HeroEnd" || key.Key == "HeroWay")
                {
                    continue;
                }

                ConfigEntryBase entry = Config[key];
                if (entry == null || entry.DefaultValue == null) continue;
                if (Equals(entry.BoxedValue, entry.DefaultValue)) continue;

                Log.LogInfo($"Настройка {key.Section}.{key.Key} возвращена к умолчанию.");
                entry.BoxedValue = entry.DefaultValue;
                reset++;
            }

            stored.Value = RecipeVersion;
            Config.Save();

            Log.LogInfo($"Рецепт обновлён до версии {RecipeVersion}, настроек сброшено: {reset}.");
        }

        private void Awake()
        {
            Log = Logger;

            AddRaceOption = Config.Bind("Race", "AddRaceOption", true,
                "Add Demon as a fifth race in the character creator. Turn off to keep only the manual hotkeys.");
            RaceLabel = Config.Bind("Race", "RaceLabel", "Демон",
                "The name shown for the race in the creator.");

            CaptureKey = Config.Bind("Keys", "CaptureKey", KeyCode.F8,
                "Read the appearance of the character on screen and write it to the log in the "
                + "form the settings take. Made for copying a face that already exists instead "
                + "of rebuilding it slider by slider from memory.");

            ApplyKey = Config.Bind("Keys", "ApplyKey", KeyCode.F6,
                "Apply the demon look by hand to the character currently shown in the creator.");
            ResetKey = Config.Bind("Keys", "ResetKey", KeyCode.F7,
                "Reset every listed DNA slider back to neutral, for before and after comparison.");

            ApplyColors = Config.Bind("Colors", "ApplyColors", true,
                "Also recolour skin, eyes and hair when applying.");
            SkinColor = Config.Bind("Colors", "SkinColor", "#5C1512",
                "Skin colour as hex. Dark red over black by default.");
            EyeColor = Config.Bind("Colors", "EyeColor", "#090909",
                "Eye colour as hex. Black by default.");

            ExtraColors = Config.Bind("Colors", "ExtraColors", "BodyPaint=#0A0A0A,Complexion=#0A0A0A",
                "Any further colour channels to set, written as Channel=#RRGGBB and separated by commas, "
                + "for example BodyPaint=#0A0A0A,Complexion=#0A0A0A. The channel names this model actually "
                + "has are written to the log when ListPaints is on.");
            HairColor = Config.Bind("Colors", "HairColor", "#1A1012",
                "Hair colour as hex. Near black by default.");

            BodyPaint = Config.Bind("Marks", "BodyPaint", "M_BlackSorceror_Body_Tattoo_Recipe",
                "Name of the body paint recipe to wear, the closest the game has to a tattoo. "
                + "Leave empty for none. Turn on ListPaints to see what is available.");
            FacePaint = Config.Bind("Marks", "FacePaint", "M_BlackSorceror_Face_Tattoo_Recipe",
                "Name of the face paint recipe, worn in the Complexion slot. Leave empty for none.");
            ListPaints = Config.Bind("Marks", "ListPaints", true,
                "Write the available body and face paint recipe names to the log, once, so they can be chosen by name.");

            foreach (KeyValuePair<string, float> entry in Profile)
            {
                DnaConfig[entry.Key] = Config.Bind("DNA", entry.Key, entry.Value,
                    "Range 0 to 1, where 0.5 is the unmodified human value.");
            }

            Racial.Bind(Config);
            RacialTrait.Bind(Config);
            RacialSchool.Bind(Config);

            StartRestriction.Bind(Config);
            Company.Bind(Config);
            Hostility.Bind(Config);
            Paladins.Bind(Config);
            Manners.Bind(Config);
            Summoning.Bind(Config);
            Crusade.Bind(Config);
            Captain.Bind(Config);
            Oblivion.Bind(Config);
            Scenes.Bind(Config);
            NoPrologue.Bind(Config);
            Origin.Bind(Config);
            Poise.Bind(Config);
            Worldbreak.Bind(Config);
            Souls.Bind(Config);
            Feast.Bind(Config);
            Veil.Bind(Config);
            Crime.Bind(Config);
            Search.Bind(Config);
            Chronicle.Bind(Config);
            Sleep.Bind(Config);
            Homes.Bind(Config);
            Theft.Bind(Config);
            Nightwatch.Bind(Config);
            Errands.Bind(Config);
            Shutters.Bind(Config);
            Market.Bind(Config);
            Economy.Bind(Config);
            DemonLook.Trade.Bind(Config);
            Sites.Bind(Config);
            Beasts.Bind(Config);
            Kids.Bind(Config);
            Growth.Bind(Config);
            Bands.Bind(Config);
            Tavern.Bind(Config);
            Wardrobe.Bind(Config);
            Almanac.Bind(Config);
            Weariness.Bind(Config);
            Magi.Bind(Config);
            Schools.Bind(Config);

            // Сброс по версии рецепта идёт последним, когда все настройки уже
            // зарегистрированы: раньше сбрасывать было попросту нечего, и значения,
            // заданные после него, переживали обновление мода нетронутыми.
            ApplyRecipe();

            ApplyPatches();

            if (AddRaceOption.Value) RegisterRace();

            Log.LogInfo($"Demon Race v{PluginVersion} loaded. {Profile.Length} sliders, apply on {ApplyKey.Value}, reset on {ResetKey.Value}.");
        }

        // Правки ставятся поштучно, а не разом. PatchAll обрывается на первой же ошибке, и всё,
        // что шло за ней, остаётся без правок: так с утра 25.09.2026 молча не работала большая
        // часть мода из-за одной правки с неверной целью. Поштучно сбойная просто пропускается,
        // а в журнале видно, какая именно и почему.
        private void ApplyPatches()
        {
            Harmony harmony = new Harmony(PluginGuid);
            int ok = 0, failed = 0;

            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0) continue;

                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                    ok++;
                }
                catch (Exception e)
                {
                    failed++;
                    Log.LogError($"Правка {type.Name} не встала, остальные работают: {e.Message}");
                }
            }

            if (failed == 0) Log.LogInfo($"Правок поставлено: {ok}.");
            else Log.LogWarning($"Правок поставлено: {ok}, не встало: {failed}.");
        }

        // The creator cycles races by index through a private static string array.
        // Appending to it is enough for the existing prev/next buttons to reach Demon.
        private static void RegisterRace()
        {
            try
            {
                FieldInfo field = AccessTools.Field(typeof(CharacterCustomizationController), "races");
                if (field == null)
                {
                    Log.LogError("Could not find the race list, Demon will not appear in the creator.");
                    return;
                }

                string[] races = (string[])field.GetValue(null);
                if (races == null)
                {
                    Log.LogError("The race list is empty, Demon will not appear in the creator.");
                    return;
                }

                foreach (string race in races)
                {
                    if (race == DemonRaceName)
                    {
                        Log.LogInfo("Demon is already in the race list.");
                        return;
                    }
                }

                string[] extended = new string[races.Length + 1];
                Array.Copy(races, extended, races.Length);
                extended[races.Length] = DemonRaceName;
                field.SetValue(null, extended);

                Log.LogInfo($"Demon added to the race list, now {extended.Length} races: {string.Join(", ", extended)}.");
            }
            catch (Exception e)
            {
                Log.LogError("Could not add Demon to the race list: " + e);
            }
        }

        private void OnGUI()
        {
            Oblivion.Draw();
            Scene.Draw();
            Search.Draw();
        }
        private void Update()
        {
            Oblivion.Check();
            Summoning.Watch();
            Hostility.Watch();
            Crusade.Tick();
            Worldbreak.Tick();

            // Свои копии двух заклинаний Тьмы заводятся, как только книга есть: до загрузки
            // сохранения, чтобы демон нашёл их в ней на своих местах.
            Feast.Register();
            Chronicle.Register();
            Feast.Tick();
            Souls.Tick();
            Crime.Tick();
            Sleep.Tick();
            Homes.Tick();
            Economy.Tick();
            Tavern.Tick();
            Wardrobe.Tick();
            Beasts.Tick();
            Kids.Tick();
            Growth.Tick();
            Almanac.Tick();
            Weariness.Tick();
            Schools.Tick();
            if (PartyManager.instance != null && PartyManager.instance.leader != null)
            {
                Scenes.Report();
            }

            // Три списка на одной клавише: F5 — где отряд появляется, Ctrl+F5 — где встаёт,
            // Alt+F5 — промежуточный угол, если до места не по прямой. Shift к любому из них
            // стирает этот список. Разметка идёт ногами: встал, нажал, пошёл дальше.
            //
            // Ctrl отдан конечным точкам, а не промежуточным: промежуточные нужны не всегда,
            // а конечные — всякий раз, и второй по удобству клавишей должно быть то, чем
            // пользуются чаще. Первая разметка ушла не в тот список именно из-за этого.
            if (Input.GetKeyDown(KeyCode.F5))
            {
                bool clearing = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                bool ending = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                bool waypoint = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

                if (ending) { if (clearing) Summoning.UnmarkEnd(); else Summoning.MarkEnd(); }
                else if (waypoint) { if (clearing) Summoning.UnmarkWay(); else Summoning.MarkWay(); }
                else if (clearing) Summoning.Unmark();
                else Summoning.Mark();
            }
            else if (Input.GetKeyDown(CaptureKey.Value)) Capture();
            else if (Input.GetKeyDown(ApplyKey.Value)) ApplyManually(demon: true);
            else if (Input.GetKeyDown(ResetKey.Value)) ApplyManually(demon: false);
        }

        /// <summary>
        /// Writes down the face that is on screen, ready to be pasted into the settings.
        ///
        /// Повторять чужую внешность по памяти — та же догадка, что с именами мага и
        /// заклинания, и промахнулась бы она так же. Снимаем числа с готового персонажа и
        /// вставляем их как есть.
        /// </summary>
        private static void Capture()
        {
            CharacterCustomizationController controller =
                UnityEngine.Object.FindObjectOfType<CharacterCustomizationController>();

            DynamicCharacterAvatar avatar = controller != null ? controller.Avatar : null;
            if (avatar == null)
            {
                DynamicCharacterAvatar[] all = UnityEngine.Object.FindObjectsOfType<DynamicCharacterAvatar>();
                avatar = all.Length == 1 ? all[0] : null;
            }

            if (avatar == null)
            {
                Log.LogWarning("Некого снимать: ни одного персонажа на экране.");
                return;
            }

            try
            {
                Dictionary<string, DnaSetter> dna = avatar.GetDNA();
                List<string> lines = new List<string>();

                foreach (KeyValuePair<string, DnaSetter> entry in dna)
                {
                    if (entry.Value == null) continue;
                    lines.Add($"{entry.Key} = {entry.Value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                }

                lines.Sort();

                Log.LogInfo($"=== снимок внешности: ползунков {lines.Count} ===");

                // Порциями: одна строка на полсотни значений в логе нечитаема.
                for (int i = 0; i < lines.Count; i += 10)
                {
                    Log.LogInfo(string.Join(" | ", lines.GetRange(i, Math.Min(10, lines.Count - i)).ToArray()));
                }

                foreach (KeyValuePair<string, UMATextRecipe> worn in avatar.WardrobeRecipes)
                {
                    if (worn.Value != null) Log.LogInfo($"Слот {worn.Key}: {worn.Value.name}");
                }
            }
            catch (Exception e)
            {
                Log.LogError("Снимок внешности сорвался: " + e);
            }
        }

        private static void ApplyManually(bool demon)
        {
            CharacterCustomizationController controller =
                UnityEngine.Object.FindObjectOfType<CharacterCustomizationController>();

            DynamicCharacterAvatar avatar = controller != null ? controller.Avatar : null;
            if (avatar == null)
            {
                DynamicCharacterAvatar[] all = UnityEngine.Object.FindObjectsOfType<DynamicCharacterAvatar>();
                avatar = all.Length == 1 ? all[0] : null;
            }

            if (avatar == null)
            {
                Log.LogWarning("No character to work on. Open the character creator first.");
                return;
            }

            if (demon) DemonAppearance.Request();
            DemonAppearance.Apply(avatar, demon);
        }
    }

    internal static class DemonAppearance
    {
        // Guards against re-entering within a single call.
        internal static bool Applying;

        // ForceUpdate reports back through CharacterUpdated on a later frame, by which point
        // the re-entrancy flag is long cleared. Without a separate request flag the automatic
        // path therefore re-triggers itself forever. Work is done only when something actually
        // asked for it: a race change, an avatar rebuild, or the hotkey.
        private static bool requested;

        internal static void Request()
        {
            requested = true;
        }

        internal static bool IsRequested
        {
            get { return requested; }
        }

        internal static bool Apply(DynamicCharacterAvatar avatar, bool demon)
        {
            if (avatar == null || Applying) return false;

            try
            {
                Applying = true;

                Dictionary<string, DnaSetter> dna = avatar.GetDNA();
                if (dna == null || dna.Count == 0)
                {
                    // Not ready yet. The request stays pending so the next update retries.
                    return false;
                }

                int applied = 0;
                int missing = 0;
                foreach (KeyValuePair<string, ConfigEntry<float>> entry in DemonLookPlugin.DnaConfig)
                {
                    DnaSetter setter;
                    if (!dna.TryGetValue(entry.Key, out setter)) { missing++; continue; }
                    setter.Set(demon ? Mathf.Clamp01(entry.Value.Value) : 0.5f);
                    applied++;
                }

                bool recolour = demon && DemonLookPlugin.ApplyColors.Value;
                if (recolour)
                {
                    SetColor(avatar, "Skin", DemonLookPlugin.SkinColor.Value);
                    SetColor(avatar, "Eyes", DemonLookPlugin.EyeColor.Value);
                    SetColor(avatar, "Hair", DemonLookPlugin.HairColor.Value);
                }

                bool marksChanged = ApplyMarks(avatar, demon);

                // Цвета краски ставим после того, как краска надета, а не до. SetSlot
                // сбрасывает цвет канала на заложенный в самом рецепте — белый, — поэтому
                // заданный заранее чёрный затирался, и настройка выглядела нерабочей.
                if (recolour) SetExtraColors(avatar, DemonLookPlugin.ExtraColors.Value);

                // ForceUpdate rather than BuildCharacter: BuildCharacter restores saved DNA
                // by default, which would undo what was just set.
                //
                // Сборка может не состояться: рецепт краски в модели есть и ставится, а
                // накладка, на которую он ссылается, в сборке игры отсутствует — и UMA роняет
                // сборку посередине. Тело при этом остаётся полусобранным: рукава на месте, а
                // рук нет. Поэтому неудача здесь не ошибка, а развилка — снимаем краску и
                // собираем заново без неё. Лучше демон без узора, чем демон без рук.
                if (!Rebuild(avatar, recolour, marksChanged) && marksChanged)
                {
                    Scrub(avatar);
                    marksChanged = false;

                    Rebuild(avatar, recolour, true);
                }

                // Краска попадает в список цветовых каналов только после пересборки модели,
                // поэтому покрасить её в тот же заход нельзя — канала ещё не существует, и
                // чёрный ложился в пустоту. Просим ещё один заход: на нём марки уже на
                // месте, ApplyMarks вернёт «ничего не изменилось», и круг замкнётся сам.
                requested = marksChanged;

                DemonLookPlugin.Log.LogInfo(demon
                    ? $"Demon look applied: {applied} sliders set, {missing} absent from this model."
                    : $"Reset to neutral: {applied} sliders returned to 0.5.");
                return true;
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogError("Demon Race failed to apply the look: " + e);
                requested = false;
                return false;
            }
            finally
            {
                Applying = false;
            }
        }

        private static bool listed;

        // Body and face paint are ordinary wardrobe recipes the game already ships, so
        // demonic markings need no new art. The slot names are the game's own:
        // BodyPaint for the body, Complexion for the face.
        // Краски, на которых сборка уже срывалась. Второй раз их не надеваем: попытка стоит
        // двух пересборок модели и кончается тем же.
        private static readonly HashSet<string> broken = new HashSet<string>();

        /// <summary>Builds the model, and answers whether it came together.</summary>
        private static bool Rebuild(DynamicCharacterAvatar avatar, bool colours, bool marks)
        {
            try
            {
                avatar.ForceUpdate(true, colours, marks);
                return true;
            }
            catch (Exception e)
            {
                foreach (string slot in Painted) broken.Add(Wanted(slot));

                DemonLookPlugin.Log.LogWarning("Модель не собралась с краской, снимаю её: "
                    + e.Message);

                return false;
            }
        }

        private static readonly string[] Painted = new string[] { "BodyPaint", "Complexion" };

        private static string Wanted(string slot)
        {
            return slot == "Complexion"
                ? (DemonLookPlugin.FacePaint.Value ?? "")
                : (DemonLookPlugin.BodyPaint.Value ?? "");
        }

        /// <summary>Takes the paint back off, so the body can be built whole.</summary>
        private static void Scrub(DynamicCharacterAvatar avatar)
        {
            foreach (string slot in Painted)
            {
                try { avatar.ClearSlot(slot); }
                catch { }
            }
        }

        private static bool ApplyMarks(DynamicCharacterAvatar avatar, bool demon)
        {
            Dictionary<string, List<UMATextRecipe>> available = avatar.AvailableRecipes;
            if (available == null) return false;

            if (DemonLookPlugin.ListPaints.Value && !listed)
            {
                listed = true;
                ReportEverything(avatar, available);
            }

            bool changed = false;
            changed |= SetPaint(avatar, available, "BodyPaint", demon ? DemonLookPlugin.BodyPaint.Value : "");
            changed |= SetPaint(avatar, available, "Complexion", demon ? DemonLookPlugin.FacePaint.Value : "");
            return changed;
        }

        // Character appearances live in Unity assets rather than in code, so the only way to
        // learn what the game actually offers, including anything used by its own sorcerers
        // and bosses, is to ask the avatar at runtime and read the result.
        private static void ReportEverything(DynamicCharacterAvatar avatar,
            Dictionary<string, List<UMATextRecipe>> available)
        {
            DemonLookPlugin.Log.LogInfo($"--- wardrobe slots on this model: {available.Count} ---");

            foreach (KeyValuePair<string, List<UMATextRecipe>> slot in available)
            {
                List<UMATextRecipe> recipes = slot.Value;
                if (recipes == null || recipes.Count == 0)
                {
                    DemonLookPlugin.Log.LogInfo($"Slot {slot.Key}: empty.");
                    continue;
                }

                List<string> names = new List<string>();
                foreach (UMATextRecipe recipe in recipes)
                {
                    if (recipe != null) names.Add(recipe.name);
                }
                DemonLookPlugin.Log.LogInfo($"Slot {slot.Key}, {names.Count}: {string.Join(", ", names.ToArray())}");
            }

            List<DynamicCharacterAvatar.ColorValue> colors = avatar.ActiveColors != null
                ? new List<DynamicCharacterAvatar.ColorValue>(avatar.ActiveColors)
                : new List<DynamicCharacterAvatar.ColorValue>();

            List<string> channels = new List<string>();
            foreach (DynamicCharacterAvatar.ColorValue color in colors)
            {
                if (color != null) channels.Add(color.Name);
            }
            DemonLookPlugin.Log.LogInfo($"--- colour channels, {channels.Count}: {string.Join(", ", channels.ToArray())} ---");
        }

        private static bool SetPaint(DynamicCharacterAvatar avatar,
            Dictionary<string, List<UMATextRecipe>> available, string slot, string wanted)
        {
            if (string.IsNullOrEmpty(wanted))
            {
                // Nothing requested: leave whatever the character already wears alone.
                return false;
            }

            if (broken.Contains(wanted))
            {
                // Уже роняла сборку. Второй раз не пробуем: кончится тем же, а тело соберётся
                // дважды впустую.
                return false;
            }

            List<UMATextRecipe> recipes;
            if (!available.TryGetValue(slot, out recipes) || recipes == null)
            {
                DemonLookPlugin.Log.LogWarning($"Slot {slot} is not available on this model.");
                return false;
            }

            foreach (UMATextRecipe recipe in recipes)
            {
                if (recipe == null || recipe.name != wanted) continue;
                avatar.ClearSlot(slot);
                avatar.SetSlot(recipe);
                DemonLookPlugin.Log.LogInfo($"Slot {slot} set to {wanted}.");
                return true;
            }

            DemonLookPlugin.Log.LogWarning($"No recipe called {wanted} in slot {slot}. Turn on ListPaints to see the names.");
            return false;
        }

        // Channel names differ between models, so rather than hard-coding more of them the
        // remaining ones are driven from config once the log has revealed what exists.
        private static void SetExtraColors(DynamicCharacterAvatar avatar, string spec)
        {
            if (string.IsNullOrEmpty(spec)) return;

            foreach (string pair in spec.Split(','))
            {
                string entry = pair.Trim();
                if (entry.Length == 0) continue;

                int split = entry.IndexOf('=');
                if (split <= 0 || split == entry.Length - 1)
                {
                    DemonLookPlugin.Log.LogWarning($"Could not read colour entry {entry}, expected Channel=#RRGGBB.");
                    continue;
                }

                SetColor(avatar, entry.Substring(0, split).Trim(), entry.Substring(split + 1).Trim());
            }
        }

        // Защита от круга: перекраска дёргает обновление модели, которое снова приводит сюда.
        private static bool painting;

        /// <summary>Paints the marks once the channels for them exist.</summary>
        internal static void PaintMarks(DynamicCharacterAvatar avatar)
        {
            if (avatar == null || painting) return;
            if (!DemonLookPlugin.ApplyColors.Value) return;

            string wanted = (DemonLookPlugin.ExtraColors.Value ?? "").Trim();
            if (wanted.Length == 0) return;

            try
            {
                painting = true;

                SetExtraColors(avatar, wanted);
                avatar.UpdateColors(true);
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Не смог покрасить краску: " + e.Message);
            }
            finally
            {
                painting = false;
            }
        }

        private static void SetColor(DynamicCharacterAvatar avatar, string channel, string hex)
        {
            if (string.IsNullOrEmpty(hex)) return;

            Color color;
            if (!ColorUtility.TryParseHtmlString(hex, out color))
            {
                DemonLookPlugin.Log.LogWarning($"Could not read colour {hex} for {channel}, expected a form like #7A2218.");
                return;
            }
            avatar.SetColor(channel, color);
        }
    }

    // Demon has no UMA model, so while the avatar is being rebuilt the race string is
    // swapped to Human. Vanilla composes the UMA race name as race + gender, so this is
    // what makes "Demon" resolve to the human body instead of a model that does not exist.
    [HarmonyPatch(typeof(CharacterCustomizationController), "GetNewAvatar")]
    internal static class GetNewAvatar_Patch
    {
        private static void Prefix(CharacterCustomizationController __instance, out string __state)
        {
            __state = null;
            if (__instance.race != DemonLookPlugin.DemonRaceName) return;

            __state = __instance.race;
            __instance.race = DemonLookPlugin.AvatarBaseRace;

            // The avatar is about to be rebuilt from scratch, which wipes the demon DNA.
            // Ask for it to be stamped back on once the rebuild reports in.
            DemonAppearance.Request();
        }

        private static readonly FieldInfo RebuildingField =
            AccessTools.Field(typeof(CharacterCustomizationController), "isChangingRaceOrGender");

        private static void Postfix(CharacterCustomizationController __instance, string __state)
        {
            if (__state == null) return;
            __instance.race = __state;

            // A demon reuses the human model, so switching to it from human leaves the UMA
            // race name unchanged and the game skips the rebuild entirely. In that case
            // nothing will ever report back, so the look has to be stamped on here and now.
            // When a rebuild did start, applying now would be undone by it, so the work is
            // deferred to the update that follows.
            bool rebuilding = false;
            if (RebuildingField != null)
            {
                object value = RebuildingField.GetValue(__instance);
                if (value is bool) rebuilding = (bool)value;
            }

            if (!rebuilding) DemonAppearance.Apply(__instance.Avatar, demon: true);
        }
    }

    // Vanilla maps the race string to a UnitRace in a switch with no Demon case, so it
    // silently leaves the previous race in place. This sets it afterwards.
    [HarmonyPatch(typeof(CharacterCustomizationController), "ChangeRace")]
    internal static class ChangeRace_Patch
    {
        private static void Postfix(CharacterCustomizationController __instance, string raceName)
        {
            try
            {
                if (raceName != DemonLookPlugin.DemonRaceName) return;

                CharacterCreaterController creator = CharacterCreaterController.instance;
                if (creator == null || creator.theCharacter == null) return;

                creator.theCharacter.Data.race = UnitRace.demon;

                // The game builds the label from a localisation key that has no demon entry,
                // so the name is written directly instead.
                if (__instance.RaceName != null)
                    __instance.RaceName.text = DemonLookPlugin.RaceLabel.Value;

                // Switching to demon has to move the starting background straight away,
                // otherwise the restriction would only bite on the next click.
                StartRestriction.ReportSets(creator);
                StartRestriction.Pin(creator);

                DemonLookPlugin.Log.LogInfo("Race set to demon.");
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogError("Demon Race failed to set the race: " + e);
            }
        }
    }

    // Once the rebuilt avatar reports back, the demon appearance is stamped onto it.
    [HarmonyPatch(typeof(CharacterCustomizationController), "CharacterUpdated")]
    internal static class CharacterUpdated_Patch
    {
        private static void Postfix(CharacterCustomizationController __instance)
        {
            if (__instance.race != DemonLookPlugin.DemonRaceName) return;
            if (DemonAppearance.Applying) return;

            // Краску красим здесь, а не внутри общего наложения: каналы BodyPaint и
            // Complexion появляются только после того, как модель пересобрана с надетой
            // краской, а до пересборки их попросту нет — и чёрный ложился в пустоту.
            DemonAppearance.PaintMarks(__instance.Avatar);

            if (!DemonAppearance.IsRequested) return;
            DemonAppearance.Apply(__instance.Avatar, demon: true);
        }
    }
}

