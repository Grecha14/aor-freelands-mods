using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace DemonLook
{
    /// <summary>
    /// What a demon is born knowing, and what it pays for that.
    ///
    /// The dark school and the pool of potential are given once and then left alone: they are
    /// stored on the character, so adding to them every time the game recounts a unit's skills
    /// would inflate them without end. The price is paid the other way round, on every gain,
    /// because that is where experience actually arrives.
    /// </summary>
    internal static class RacialSchool
    {
        internal static ConfigEntry<bool> GiveSchool;
        internal static ConfigEntry<bool> MakeMagician;
        internal static ConfigEntry<int> Potential;
        internal static ConfigEntry<bool> OnlyOwnMagic;
        internal static ConfigEntry<string> Allowed;
        internal static ConfigEntry<string> Refusal;
        internal static ConfigEntry<bool> Strict;
        internal static ConfigEntry<float> GainRate;
        internal static ConfigEntry<bool> SpareConsumables;
        internal static ConfigEntry<int> SchoolLevel;
        internal static ConfigEntry<int> MagicTrait;

        // Кому уже выдано: и школа, и потенциал хранятся у персонажа, поэтому выдавать их
        // при каждом пересчёте нельзя — они росли бы без конца.
        private static readonly HashSet<int> served = new HashSet<int>();

        internal static void Bind(ConfigFile config)
        {
            GiveSchool = config.Bind("Racial", "GiveDarkSchool", true,
                "Let a demon know the dark school from birth, without having to learn it.");

            MakeMagician = config.Bind("Racial", "MakeMagician", true,
                "Mark a demon as a magic user at all. The game keeps one flag for this on the "
                + "character, apart from any school or trait, and without it the schools simply do "
                + "not appear however many are granted — which is exactly what was happening: the "
                + "dark school was handed over, stored, and never shown.");

            SchoolLevel = config.Bind("Racial", "DarkSchoolLevel", 1,
                new ConfigDescription(
                    "Level the dark school starts at. One means a demon begins already able to "
                    + "cast, rather than merely permitted to learn.",
                    new AcceptableValueRange<int>(0, 10)));

            MagicTrait = config.Bind("Racial", "MagicTrait", 0,
                new ConfigDescription(
                    "Trait granted along with the school, or zero for none. Zero by default: the "
                    + "Sign of Magic, 318, was tried and refused — the game allows the trait but "
                    + "will not add it, because the character already carries its full share of "
                    + "inborn ones. It turned out not to matter: what actually opens magic is the "
                    + "magician flag above, and the school appears without any trait at all.",
                    new AcceptableValueRange<int>(0, 999999)));

            OnlyOwnMagic = config.Bind("Racial", "OnlyOwnMagic", true,
                "Keep a demon to the magic he was born with. Every other school in this world "
                + "was written by men for men — white magic above all, which exists to burn what "
                + "he is — and a King of Hell leafing through an introduction to it reads as a "
                + "joke. Schools of fighting are not touched: a blade is a blade.");

            Allowed = config.Bind("Racial", "Allowed", "black",
                "The schools that are a demon's, separated by commas. He is born holding every "
                + "one of them and may learn no other: this single list is both the gift and the "
                + "limit, so the two can never disagree. Darkness, necromancy and blood — all "
                + "three are about death and other people's lives, and none of them contradicts "
                + "what came out of the circle. Names are the game's own: fire, ice, lightning, "
                + "white, black, air, spirit, necromancy, blood, forest, earth, spatial. Add one "
                + "here and an old demon receives it on his next load.");

            Strict = config.Bind("Racial", "Strict", true,
                "Take back any school of magic that is not his. Without this a school once "
                + "granted stays granted: strike one off the list above and an old demon keeps "
                + "it for ever, because nothing ever looked for schools to remove. Schools of "
                + "fighting are never touched.");

            Refusal = config.Bind("Racial", "Refusal",
                "Чужая магия не идёт Королю Ада.",
                "What is said when he tries.");

            Potential = config.Bind("Racial", "PotentialBonus", 0,
                new ConfigDescription(
                    "Points of potential a demon is born with, over and above its kind. Zero: "
                    + "the circle's sacrifice is where that room comes from now, and a demon "
                    + "should not be handed twice over what the ritual is for. Given once, in "
                    + "the moment the demon is first made one — never again on a later load.",
                    new AcceptableValueRange<int>(0, 500)));

            GainRate = config.Bind("Racial", "GainRate", 0.5f,
                new ConfigDescription(
                    "Share of the experience a demon keeps, for its level, its attributes and its "
                    + "weapon mastery alike. Half means everything is earned twice as slowly.",
                    new AcceptableValueRange<float>(0.1f, 1f)));

            SpareConsumables = config.Bind("Racial", "SpareConsumables", true,
                "Let experience drunk from a bottle arrive in full, while experience earned by "
                + "fighting and by errands is halved. The game has one door for all experience and "
                + "it says nothing about where the experience came from, so what is watched instead "
                + "is whether an item is being used at that moment.");
        }

        private static readonly HashSet<SkillSet> own = new HashSet<SkillSet>();
        private static string listed;

        /// <summary>
        /// The schools that belong to a demon: given at birth, and the only ones he may learn.
        ///
        /// Один список на оба правила нарочно. Разойтись им негде, потому что списка два быть
        /// не может: то, что ему открыто, и то, чему он может научиться, — это одно и то же.
        /// </summary>
        internal static HashSet<SkillSet> Own()
        {
            string written = Allowed.Value ?? "";

            if (written != listed)
            {
                listed = written;
                own.Clear();

                foreach (string one in written.Split(','))
                {
                    string name = one.Trim();
                    if (name.Length == 0) continue;

                    try { own.Add((SkillSet)Enum.Parse(typeof(SkillSet), name, true)); }
                    catch { DemonLookPlugin.Log.LogWarning($"Школы «{name}» в игре нет."); }
                }
            }

            return own;
        }

        /// <summary>True for a book of a school of magic that is not a demon's own.</summary>
        internal static bool Foreign(UIItemInfo book)
        {
            if (book == null) return false;

            SkillSet school = book.learnSkillSet;

            // Школы магии в игре пронумерованы от сотни; всё, что ниже, — боевые, и их демон
            // волен учить наравне со всеми.
            int number = (int)school;
            if (number < 101 || number >= 200) return false;

            return !Own().Contains(school);
        }

        /// <summary>True while the game is working through a used item.</summary>
        internal static bool UsingItem;

        internal static void Serve(HumaniodUnit unit)
        {
            if (!Racial.IsDemon(unit) || unit.Data == null) return;

            int key = unit.GetInstanceID();
            if (served.Contains(key)) return;
            served.Add(key);

            try
            {
                NPCSaveData npc = unit.Data as NPCSaveData;

                // Впервые ли мы одеваем этого демона. Признак мага, школа тьмы и знак магии
                // ложатся в сохранение, поэтому при второй загрузке игра сама отвечает «уже
                // есть» и ни одно из трёх не срабатывает. Это и есть память о том, что пакет
                // уже выдан, — та самая, которой не хватало потенциалу.
                bool born = false;

                // Признак мага ставится первым: без него всё остальное складывается в данные
                // и не показывается, потому что панель школ для не-мага не строится вовсе.
                if (MakeMagician.Value && !unit.Data.isMagician)
                {
                    unit.Data.isMagician = true;
                    born = true;
                    DemonLookPlugin.Log.LogInfo($"{unit.Data.unitname}: отмечен как маг.");
                }

                // Доступ к магии и сама школа — разные вещи: знак магии открывает школы
                // вообще, а талант мастерства даёт конкретную и сразу с уровнем.
                UITalentDatabase tdb = UITalentDatabase.Instance;

                // Школы демона — не право учиться, а то, с чем он приходит. Список один и тот
                // же и для выдачи, и для запрета: что его — то и открыто, и учить он может
                // только это. Двум спискам разойтись было бы негде, потому что списка два быть
                // не может.
                //
                // Выдаётся не однократно, а всякий раз, когда чего-то недостаёт: так уже
                // сохранённый демон, у которого была одна тьма, получает недостающие при первой
                // же загрузке — без переноса сохранений и без отдельного правила.
                if (GiveSchool.Value && npc != null)
                {
                    if (npc.skillSet == null) npc.skillSet = new List<SkillSet>();

                    foreach (SkillSet school in Own())
                    {
                        if (npc.skillSet.Contains(school)) continue;

                        npc.skillSet.Add(school);
                        DemonLookPlugin.Log.LogInfo($"{unit.Data.unitname}: дана школа {school}.");
                    }

                    // И обратный ход: школа, вычеркнутая из списка, должна уйти и у того, кому
                    // её уже успели выдать. Иначе список правится только вперёд, и однажды
                    // выданное остаётся навсегда. Боевые школы не трогаем — счёт идёт от сотни.
                    if (Strict.Value)
                    {
                        for (int i = npc.skillSet.Count - 1; i >= 0; i--)
                        {
                            SkillSet school = npc.skillSet[i];

                            int number = (int)school;
                            if (number < 101 || number >= 200) continue;
                            if (Own().Contains(school)) continue;

                            npc.skillSet.RemoveAt(i);

                            if (tdb != null && unit.talentmanger != null)
                            {
                                UITalentInfo mark = tdb.GetMasteryTalent(school);
                                if (mark != null && unit.talentmanger.ContainTalent(mark))
                                {
                                    unit.talentmanger.RemoveTalent(mark);
                                }
                            }

                            DemonLookPlugin.Log.LogInfo($"{unit.Data.unitname}: школа {school} "
                                + "отобрана — она не его.");
                        }
                    }
                }

                if (MagicTrait.Value > 0 && tdb != null && unit.talentmanger != null
                    && !unit.talentmanger.ContainTrait(MagicTrait.Value))
                {
                    UITalentInfo mark = tdb.GetTraitByID(MagicTrait.Value);
                    if (mark != null)
                    {
                        // AddTrait отвечает, приняли черту или нет. Раньше ответ выбрасывался,
                        // и отказ выглядел в логе точно так же, как удача.
                        bool taken = unit.talentmanger.AddTrait(mark, false);
                        bool allowed = unit.talentmanger.CheckTraitAllow(mark);

                        if (taken) born = true;

                        DemonLookPlugin.Log.LogInfo($"{unit.Data.unitname}: черта «{mark.Name}» — "
                            + $"принята: {taken}, разрешена: {allowed}, "
                            + $"тип {mark.traitType}, раса {mark.raceRequire}.");
                    }
                }

                if (GiveSchool.Value && SchoolLevel.Value > 0 && tdb != null && unit.talentmanger != null)
                {
                    foreach (SkillSet set in Own())
                    {
                        UITalentInfo school = tdb.GetMasteryTalent(set);

                        if (school == null)
                        {
                            DemonLookPlugin.Log.LogWarning($"Талант школы {set} не найден.");
                            continue;
                        }

                        if (unit.talentmanger.ContainTalent(school)) continue;

                        unit.talentmanger.AddTalent(school, SchoolLevel.Value);
                        DemonLookPlugin.Log.LogInfo($"{unit.Data.unitname}: школа {set} "
                            + $"«{school.Name}» на уровне {SchoolLevel.Value}.");
                    }
                }

                // Только в тот заход, когда демона одевают впервые. Прежде потенциал прибавлялся
                // при каждом входе в игру: защита от повторной выдачи была, но жила внутри
                // запуска и умирала вместе с ним, а число в сохранении оставалось. Школа и знак
                // сохраняются, потому и не текли; теперь потенциал идёт вместе с ними.
                if (Potential.Value > 0 && npc != null && born)
                {
                    npc.humanAttribute.potential += Potential.Value;
                    DemonLookPlugin.Log.LogInfo($"{unit.Data.unitname}: потенциал +{Potential.Value}, "
                        + $"стало {npc.humanAttribute.potential}.");
                }
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogError("Не смог выдать расовый пакет: " + e);
            }
        }

        /// <summary>How much of an incoming gain a demon actually keeps.</summary>
        internal static float Rate(HumaniodUnit unit)
        {
            if (!Racial.IsDemon(unit)) return 1f;
            if (SpareConsumables.Value && UsingItem) return 1f;

            return GainRate.Value;
        }
    }

    [HarmonyPatch(typeof(HumaniodUnit), "CalculateLivingSkills")]
    internal static class ServeDemon_Patch
    {
        private static void Postfix(HumaniodUnit __instance)
        {
            RacialSchool.Serve(__instance);
        }
    }

    // Опыт характеристик и мастерства оружия режется тем же множителем, что и уровень:
    // демон учится медленнее во всём, а не только в одном.
    [HarmonyPatch(typeof(HumaniodUnit), "GainAttributeExp")]
    internal static class GainAttributeExp_Patch
    {
        private static void Prefix(HumaniodUnit __instance, ref float exp)
        {
            exp *= RacialSchool.Rate(__instance);
        }
    }

    [HarmonyPatch(typeof(HumaniodUnit), "GainWeaponMasteryExp", new[] { typeof(WeaponType), typeof(int) })]
    internal static class GainMasteryExp_Patch
    {
        private static void Prefix(HumaniodUnit __instance, ref int exp)
        {
            exp = Mathf.RoundToInt(exp * RacialSchool.Rate(__instance));
        }
    }

    // Пока игра разбирает использованный предмет, штраф снимается. Другого способа отличить
    // опыт из бутылки от опыта за бой нет: вход для опыта один и об источнике не сообщает.
    [HarmonyPatch(typeof(ItemSlotSelector), "ConsumeInventoryObject")]
    internal static class Consume_Patch
    {
        private static void Prefix() { RacialSchool.UsingItem = true; }
        private static void Postfix() { RacialSchool.UsingItem = false; }
    }
    // Книга школы принимается или отвергается в одном месте, и оно же говорит игроку почему.
    // Сюда и встаём: отказ выглядит ровно как родной, без своих окон и без сломанной середины —
    // книга не тратится, время не идёт, обучение не начинается вовсе.
    [HarmonyPatch(typeof(HumaniodUnit), "StartBookLearn")]
    internal static class StartBookLearn_Patch
    {
        private static bool Prefix(HumaniodUnit __instance, Inventory bookInv, ref bool __result)
        {
            try
            {
                if (!RacialSchool.OnlyOwnMagic.Value) return true;
                if (__instance == null || bookInv == null || bookInv.itemInfo == null) return true;
                if (!Racial.IsDemon((UnitAttribute)(object)__instance)) return true;

                if (!RacialSchool.Foreign(bookInv.itemInfo)) return true;

                GameController.ShowMessage(RacialSchool.Refusal.Value, 3f);
                __result = false;
                return false;
            }
            catch
            {
                return true;
            }
        }


    }
    // Запрет на начало не помогает книге, начатой раньше — до запрета, или до того, как школу
    // вычеркнули из списка. Поэтому проверка стоит и на самом ходе обучения: чужая книга
    // отменяется родной отменой игры, которая и вернёт её в сумку, а прочитанное так и
    // останется недочитанным.
    [HarmonyPatch(typeof(HumaniodUnit), "BookLearnProgress")]
    internal static class BookLearnProgress_Patch
    {
        private static bool Prefix(HumaniodUnit __instance)
        {
            try
            {
                if (!RacialSchool.OnlyOwnMagic.Value) return true;
                if (__instance == null || __instance.currentLearningBook == null) return true;
                if (!Racial.IsDemon((UnitAttribute)(object)__instance)) return true;

                if (!RacialSchool.Foreign(__instance.currentLearningBook.itemInfo)) return true;

                DemonLookPlugin.Log.LogInfo($"«{__instance.Data.unitname}»: чужая книга снята "
                    + "с обучения.");

                GameController.ShowMessage(RacialSchool.Refusal.Value, 3f);
                __instance.StopBookLearn();

                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}
