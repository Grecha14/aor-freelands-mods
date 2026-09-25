using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace DemonLook
{
    /// <summary>
    /// A trait that exists purely so the racial package is visible on the Traits tab.
    /// It carries no addAttrs of its own: the actual bonuses are applied by the runtime
    /// patches, because two of them are percentages and one tracks current health, none
    /// of which the flat data model can express. The description therefore has to state
    /// what the patches really do.
    /// </summary>
    internal static class RacialTrait
    {
        // Far above anything the game ships, so it cannot collide with a vanilla id
        // or with one a future update introduces.
        internal const int TraitId = 907001;

        internal static ConfigEntry<bool> ShowTrait;
        internal static ConfigEntry<string> TraitName;
        internal static ConfigEntry<string> TraitDescription;

        private static UITalentInfo instance;
        private static bool failed;

        internal static void Bind(ConfigFile config)
        {
            ShowTrait = config.Bind("Racial", "ShowTrait", true,
                "Show the demon package as a trait on the character's Traits tab.");

            TraitName = config.Bind("Racial", "TraitName", "Кровь Владыки Ада",
                "Name of the racial trait.");

            TraitDescription = config.Bind("Racial", "TraitDescription",
                "Его не нужно искать в бою — бой находит его сам. Всё живое чует в нём тот конец, к которому идёт, и оборачивается на него прежде, чем на занесённый клинок. Ненависть эта не выбор, а узнавание.\n\n"
                + "Тьма в его руках послушна, как старый пёс: он не заклинает её, он зовёт её домой. Свет же немеет. Слова, что жгут в чужих устах, в его рту гаснут, не долетев до воздуха, — стихия не служит тому, кто отрицает её самим своим существованием.\n\n"
                + "Смерть в бою ему ничего не даёт: душа убитого уходит в землю. Свою пищу он берёт тише — над спящим, над упавшим без памяти, над пленником. Жизнь уходит из них к нему струйкой тьмы, и то, что было их силой, прирастает к нему навсегда; чем крепче был выпитый, тем больше прирастает.\n\n"
                + "Из выпитых растёт и его Тьма: десятая душа открывает первую её ступень, трёхсотая — всю. Заклинаний Тьмы он не учит — они приходят к нему сами. Ночь его: в темноте его видно хуже, а под Вуалью, крадучись, — почти не видно.\n\n"
                + "Голод его не отпускает. Неделю он терпит, дальше тоска съедает его дух день за днём. И пить приходится тайком: увидят над жертвой — город поднимется на него, и молва о демоне обойдёт весь мир. Найдут тело утром — подозрение падёт на чужака, тем слабее, чем больше ему верят. А те, кто идёт за ним, не вынесут этого зрелища: останутся лишь посвящённые во Тьму.",
                "Text shown in the trait tooltip. Written as lore rather than a table, and without "
                + "figures: each sentence still answers to a real rule, but says which way it "
                + "leans instead of by how much. The exact numbers live in the settings beside "
                + "this, where they can be read and changed.");
        }

        internal static UITalentInfo Get()
        {
            if (instance != null || failed) return instance;

            try
            {
                UITalentDatabase database = UITalentDatabase.Instance;
                if (database == null) return null;

                UITalentInfo trait = ScriptableObject.CreateInstance<UITalentInfo>();
                trait.name = "DemonRacialTrait";
                trait.ID = TraitId;
                trait.Name = TraitName.Value;
                trait.description = TraitDescription.Value;
                trait.type = TalentType.Trait;
                trait.traitType = TraitType.inborn;
                trait.maxPoints = 1;
                trait.raceRequire = UnitRace.demon;

                // A trait loaded from the game's own assets arrives with every collection
                // present but empty. One created from scratch leaves them null, and the game
                // reads several of them without checking: CheckRequirement goes straight to
                // rejectedTraits.Length, and the tooltip reads attributeRquire. Leaving these
                // null is what stopped the trait being granted at all.
                trait.rejectedTraits = new UITalentInfo[0];
                trait.RequireWeapon = new WeaponType[0];
                trait.RequireWeaponClass = new WeaponClass[0];
                trait.ForbidWeaponClass = new WeaponClass[0];
                trait.RequireArmour = new ArmourType[0];
                trait.addtionBuffs = new spell.UIBuffInfo[0];
                trait.addWeapons = new List<AdditionWeaponInfo>();
                trait.sizeAllow = new List<UnitSize>();
                trait.raceAllow = new List<UnitRace>();
                trait.DescriptionParam = new List<DecriptionParam>();
                trait.attributeRquire = new HumanAttribute(0, 0, 0, 0, 0, 0);

                // A null sprite would leave the trait slot with nothing to draw, so the
                // icon is borrowed from a race trait the game already ships.
                trait.Icon = BorrowIcon(database);

                if (!Append(database, trait))
                {
                    failed = true;
                    return null;
                }

                instance = trait;
                DemonLookPlugin.Log.LogInfo($"Racial trait registered as id {TraitId}.");
                return instance;
            }
            catch (Exception e)
            {
                failed = true;
                DemonLookPlugin.Log.LogError("Could not create the racial trait, the Traits tab will stay empty: " + e);
                return null;
            }
        }

        private static Sprite BorrowIcon(UITalentDatabase database)
        {
            if (database.humanTrait != null && database.humanTrait.Icon != null) return database.humanTrait.Icon;
            if (database.brutemanTrait != null && database.brutemanTrait.Icon != null) return database.brutemanTrait.Icon;

            if (database.traits != null)
            {
                foreach (UITalentInfo existing in database.traits)
                {
                    if (existing != null && existing.Icon != null) return existing.Icon;
                }
            }
            return null;
        }

        private static bool Append(UITalentDatabase database, UITalentInfo trait)
        {
            if (database.traits == null)
            {
                DemonLookPlugin.Log.LogError("The trait list is missing, cannot register the racial trait.");
                return false;
            }

            foreach (UITalentInfo existing in database.traits)
            {
                if (existing != null && existing.ID == TraitId) return true;
            }

            UITalentInfo[] extended = new UITalentInfo[database.traits.Length + 1];
            Array.Copy(database.traits, extended, database.traits.Length);
            extended[database.traits.Length] = trait;
            database.traits = extended;
            return true;
        }
    }

    // The game hands every unit its race trait through this lookup, which returns null for
    // any race it does not know. Filling in the demon answer is enough for the trait to be
    // granted, displayed and re-granted on load without touching the save.
    [HarmonyPatch(typeof(UITalentDatabase), "GetRaceTrait")]
    internal static class GetRaceTrait_Patch
    {
        private static void Postfix(UnitRace race, ref UITalentInfo __result)
        {
            if (race != UnitRace.demon) return;
            if (!RacialTrait.ShowTrait.Value) return;
            if (__result != null) return;

            __result = RacialTrait.Get();
        }
    }

    // Выдаётся заново при каждом пересчёте мирных навыков, а не один раз при создании
    // персонажа. Список черт помечен NonSerialized, и наш id вдобавок вычищается при
    // сохранении, чтобы сейв читался без мода, — поэтому после загрузки черты у демона
    // просто нет, и выдать её может только повторяющаяся проверка. Пересчёт случается
    // и при загрузке, и при смене снаряжения, а сама проверка стоит один поиск в списке.
    [HarmonyPatch(typeof(HumaniodUnit), "CalculateLivingSkills")]
    internal static class GrantRaceTrait_Patch
    {
        private static void Postfix(HumaniodUnit __instance)
        {
            try
            {
                if (!RacialTrait.ShowTrait.Value) return;
                if (!Racial.IsDemon(__instance)) return;

                // Выдаём через менеджер талантов, а не записью номера в сохранение. Панель
                // «Черты» перебирает talentmanger.traits — список самих черт, — а номер в
                // Data.traits она не смотрит вовсе. Черта была выдана и всё равно не
                // показывалась именно поэтому: два разных хранилища.
                if (__instance.talentmanger == null) return;
                // Раса демона построена на человеческой модели, и человеческую расовую черту
                // существо получает заодно с ней. В списке природных черт их оказывалось две,
                // и «Быстрое обучение» стояло рядом с «Кровью Владыки Ада» — чего у демона
                // быть не должно.
                Foreign.Strip(__instance);

                if (__instance.talentmanger.ContainTrait(RacialTrait.TraitId)) return;

                UITalentInfo trait = RacialTrait.Get();
                if (trait == null) return;

                __instance.talentmanger.AddTrait(trait, false);
                DemonLookPlugin.Log.LogInfo("Racial trait granted through the talent manager.");

            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogError("Could not grant the racial trait: " + e);
            }
        }
    }

    // Чужие расовые черты у демона: снимаются здесь, а не запрещаются при выдаче, потому
    // что выдаёт их игра при создании персонажа — до того, как мод успевает вмешаться.
    internal static class Foreign
    {
        internal static void Strip(HumaniodUnit unit)
        {
            UITalentDatabase db = UITalentDatabase.Instance;
            if (db == null || unit.talentmanger == null) return;

            Take(unit, db.humanTrait);
            Take(unit, db.brutemanTrait);
        }

        private static void Take(HumaniodUnit unit, UITalentInfo trait)
        {
            if (trait == null) return;
            if (!unit.talentmanger.ContainTrait(trait.ID)) return;

            unit.talentmanger.RemoveTrait(trait);
            DemonLookPlugin.Log.LogInfo($"С демона снята чужая расовая черта «{trait.Name}».");
        }
    }

    // Belt and braces: the trait list is already marked NonSerialized, but stripping the
    // modded id here means that even if it ever were written out, a save made with the mod
    // stays readable without it.
    [HarmonyPatch(typeof(CharacterSaveData), "SaveStatus")]
    internal static class SaveStatus_Patch
    {
        private static void Postfix(CharacterSaveData __instance)
        {
            try
            {
                List<int> traits = __instance.traits;
                if (traits == null) return;
                traits.RemoveAll(id => id == RacialTrait.TraitId);
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogError("Could not strip the modded trait id from the save: " + e);
            }
        }
    }
}
