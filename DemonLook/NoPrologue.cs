using System;
using BepInEx.Configuration;
using HarmonyLib;

namespace DemonLook
{
    /// <summary>
    /// Denies a demon the caravan road.
    ///
    /// The prologue is a good opening for somebody who walks into the world on his own feet:
    /// a stranger falls in with a merchant train, shares two quiet days on the road and parts
    /// at the gates of Brea. None of that can be true of a thing that was dragged here by a
    /// ritual and hunted from the first hour. Harold would not have taken him along, and the
    /// heroes would have found the caravan before it found Brea.
    ///
    /// The game already has the switch — a tick in the character creator that skips the
    /// prologue and starts the game with the quest already begun and the money already in
    /// hand. Rather than tear out the caravan, the tick is simply pressed on the demon's
    /// behalf and left out of his hands.
    /// </summary>
    internal static class NoPrologue
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> StartScene;

        // Взведён, когда партию только что создал демон: следующая загрузка мира — его.
        internal static bool Summoned;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Prologue", "SkipForDemons", true,
                "Start a demon's game past the caravan. The prologue tells of a stranger who "
                + "joins a merchant train and rides two days in company — a story that cannot "
                + "be told about someone the world is already hunting.");

            StartScene = config.Bind("Prologue", "StartScene", "003-Breawoods_Cave",
                "Where a demon opens his eyes. Left empty he starts where the game would put "
                + "him — outside Brea, on the road, which is no place to have been summoned. "
                + "Brewoods Cave is the game's own, one of a hundred and eighty-nine it holds, "
                + "and it is dark, enclosed and already on the way to town.");
        }

        internal static void Force(ArchiveProfile profile, UnitRace race)
        {
            if (!Enabled.Value || profile == null) return;
            if (race != UnitRace.demon || profile.skipPrologue) return;

            profile.skipPrologue = true;
            DemonLookPlugin.Log.LogInfo("Демону пролог не положен: караван пропущен.");
        }
    }

    // Гасим галочку при выборе расы, а не при создании партии. Правка в момент создания
    // срабатывала бы поздно и незаметно: в меню выключатель всё равно стоял бы доступным,
    // и игрок видел бы выбор, которого у него нет.
    [HarmonyPatch(typeof(CharacterCustomizationController), "ChangeRace")]
    internal static class ChangeRace_Prologue_Patch
    {
        private static void Postfix(CharacterCustomizationController __instance)
        {
            try
            {
                if (!NoPrologue.Enabled.Value) return;

                CharacterCreaterController creator =
                    UnityEngine.Object.FindObjectOfType<CharacterCreaterController>();

                if (creator == null || creator.skipPrologueToggle == null) return;

                bool demon = __instance.race == DemonLookPlugin.DemonRaceName;

                creator.skipPrologueToggle.isOn = demon || creator.skipPrologueToggle.isOn;
                creator.skipPrologueToggle.interactable = !demon;

                DemonLookPlugin.Log.LogInfo(demon
                    ? "Демону пролог не положен: галочка включена и закрыта."
                    : "Раса не демон: галочка пролога возвращена игроку.");

                // Окно происхождения при смене расы игра сама не перерисовывает — она делает
                // это только при смене самого происхождения. Из-за этого «Король Ада» и его
                // история оставались на экране, когда демона меняли на эльфа. Просим перерисовку
                // явно: игра впишет своё, а подмена ляжет поверх только если раса снова демон.
                creator.ChangeStartSet();
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogError("Не смог закрыть пролог: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(CharacterCreaterController), "CreateArchiveAndEnterGame")]
    internal static class CreateArchive_Patch
    {
        private static void Prefix(CharacterCreaterController __instance)
        {
            try
            {
                if (!NoPrologue.Enabled.Value) return;

                UnitRace race = __instance.theCharacter != null && __instance.theCharacter.Data != null
                    ? __instance.theCharacter.Data.race
                    : UnitRace.none;

                if (race != UnitRace.demon) return;

                // Галочку в окне давим до сборки профиля: она читается прямо при создании,
                // и переписывать профиль потом пришлось бы в двух местах сразу.
                if (__instance.skipPrologueToggle != null)
                {
                    __instance.skipPrologueToggle.isOn = true;
                    __instance.skipPrologueToggle.interactable = false;
                }

                NoPrologue.Summoned = true;
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogError("Не смог пропустить пролог демону: " + e);
            }
        }
    }

    /// <summary>
    /// Sends a demon's first step underground instead of onto the high road.
    ///
    /// Skipping the prologue drops the player on the world map outside Brea, which is where
    /// a traveller would arrive — and the one thing a demon has not done is travel. The load
    /// is caught once, on the way out of character creation, and pointed at a cave instead.
    /// Only that first load: every road afterwards is the player's own.
    /// </summary>
    [HarmonyPatch(typeof(gameManager), "LoadScene", new[] { typeof(string), typeof(int), typeof(bool) })]
    internal static class LoadScene_Patch
    {
        private static void Prefix(ref string sceneName)
        {
            if (!NoPrologue.Summoned) return;

            // Срабатывает ровно один раз: дальше демон ходит по миру сам.
            NoPrologue.Summoned = false;

            // Начинается новая партия — значит, всё, что «уже сыграно», сыграно в прошлой.
            Summoning.Forget();
            Oblivion.Forget();
            Scene.Forget();

            string wanted = (NoPrologue.StartScene.Value ?? "").Trim();
            if (wanted.Length == 0) return;

            DemonLookPlugin.Log.LogInfo($"Демон начинает не на дороге: «{sceneName}» заменено "
                + $"на «{wanted}».");

            sceneName = wanted;
        }
    }
}
