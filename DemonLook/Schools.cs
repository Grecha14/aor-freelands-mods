using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using spell;
using UnityEngine;

namespace DemonLook
{
    /// <summary>
    /// One school of magic to a man; the hero alone gets one more for every ten points of mind.
    ///
    /// Школа магии у всякого одна. Кто родился магом с двумя — оставляет первую; кто растёт —
    /// растёт в своей и чужих не берёт; спутник, раскрыв книгу чужой школы, её не осилит. Одному
    /// герою дано больше: по школе за каждые десять очков своего интеллекта, без зелий и вещей, —
    /// при пятнадцати одна, при двадцати пяти две, при тридцати три; первая у мага есть всегда.
    /// Что уже выучено сверх этого, не отнимается, но новой школы не будет, пока ум не дорастёт.
    ///
    /// Книга школы теперь читается втрое дольше: триста очков вместо ста, а в час набирается
    /// половина очка и ещё десятая за каждое очко ума — при интеллекте двадцать это сто двадцать
    /// часов, пять суток над книгой.
    /// </summary>
    internal static class Schools
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> PerMind;
        internal static ConfigEntry<float> Reading;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Schools", "Enabled", true,
                "Keep everyone to one school of magic, and the hero to one for every so many points of intelligence.");

            PerMind = config.Bind("Schools", "PerMind", 10,
                new ConfigDescription("Points of the hero's own intelligence for each school of magic he may know.", new AcceptableValueRange<int>(1, 100)));

            Reading = config.Bind("Schools", "Reading", 3f,
                new ConfigDescription("How many times longer a book of a school of magic takes to read. Applied when the game starts.",
                    new AcceptableValueRange<float>(1f, 20f)));
        }

        internal static bool On()
        {
            return Enabled != null && Enabled.Value;
        }

        internal static bool Hero(UnitAttribute u)
        {
            if (u == null) return false;
            if (u == gameManager.mainCharUnit) return true;
            return HeroData(u.Data);
        }

        internal static bool HeroData(CharacterSaveData d)
        {
            return d != null && gameManager.mainCharData != null && d.id == gameManager.mainCharData.id;
        }

        internal static int Known(NPCSaveData d)
        {
            int n = 0;
            if (d != null && d.skillSet != null)
            {
                foreach (SkillSet s in d.skillSet) if (Magi.Magic(s)) n++;
            }
            return n;
        }

        private static int Mind(NPCSaveData d)
        {
            return d != null && d.humanAttribute != null ? d.humanAttribute.BSintelligence : 0;
        }

        internal static int Allowed(UnitAttribute u, NPCSaveData d)
        {
            if (!Hero(u)) return 1;
            return Mathf.Max(1, Mind(d) / Mathf.Max(1, PerMind.Value));
        }

        /// <summary>Whether he may take up that school now, and if not, why.</summary>
        internal static bool May(UnitAttribute u, SkillSet school, out string why)
        {
            why = null;
            if (!On() || !Magi.Magic(school)) return true;

            NPCSaveData d = u != null ? u.Data as NPCSaveData : null;
            if (d == null || (d.skillSet != null && d.skillSet.Contains(school))) return true;

            int known = Known(d);
            int allowed = Allowed(u, d);
            if (known < allowed) return true;

            why = Hero(u)
                ? $"Новая школа магии — за каждые {PerMind.Value} интеллекта. Интеллект {Mind(d)}: школ {allowed}, знаете {known}."
                : $"{d.unitName}: вторая школа магии не даётся.";
            return false;
        }

        // ------------------------------------------------------------------ рост НПС

        // Кто сейчас растёт и в каких школах ему расти: поштучно, пока идёт одна раздача опыта.
        private static NPCSaveData learner;
        private static List<SkillSet> own;
        private static bool open;
        internal static int forging;

        internal static List<SkillSet> Begin(NPCSaveData nsd)
        {
            if (!On() || nsd == null || nsd.skillSet == null || HeroData(nsd)) return null;

            // Только что рождённый маг с двумя школами оставляет первую — до того, как потратит опыт на вторую.
            if (forging > 0) Magi.OneSchool(nsd);

            List<SkillSet> mine = new List<SkillSet>();
            foreach (SkillSet s in nsd.skillSet) if (Magi.Magic(s)) mine.Add(s);

            learner = nsd;
            own = mine;
            open = mine.Count == 0;
            return mine;
        }

        internal static bool Allows(NPCSaveData nsd, ScriptableObject skill)
        {
            if (learner == null || nsd != learner || own == null || skill == null) return true;

            SkillSet school = SkillSet.none;
            UISpellInfo spell = skill as UISpellInfo;
            UITalentInfo talent = skill as UITalentInfo;
            if (spell != null) school = spell.SkillSet;
            else if (talent != null) school = talent.talentClass;

            if (!Magi.Magic(school) || own.Contains(school)) return true;

            // Не было ни одной — первая школа его, но только одна.
            if (open)
            {
                own.Add(school);
                open = false;
                return true;
            }
            return false;
        }

        internal static void End(NPCSaveData nsd, List<SkillSet> mine)
        {
            if (mine == null || nsd == null) return;
            try
            {
                // Что план роста вписал сверх своей школы — вычеркнуть: учиться в ней он всё равно не мог.
                if (nsd.skillSet != null) nsd.skillSet.RemoveAll(s => Magi.Magic(s) && !mine.Contains(s));
            }
            finally
            {
                learner = null;
                own = null;
                open = false;
            }
        }

        // ------------------------------------------------------------------ книги

        private static bool stretched;

        /// <summary>Once a session, as soon as the books are there: a book of a school of magic takes three times as long.</summary>
        internal static void Tick()
        {
            if (stretched || !On()) return;

            UIItemDatabase db;
            try { db = UIItemDatabase.Instance; }
            catch { return; }
            if (db == null || db.items == null || db.items.Length == 0) return;
            stretched = true;

            int n = 0;
            foreach (UIItemInfo i in db.items)
            {
                if (i == null || !Magi.Magic(i.learnSkillSet)) continue;
                i.learnTime *= Reading.Value;
                n++;
            }
            DemonLookPlugin.Log.LogInfo($"Школы магии: книг школ {n}, читать их теперь в {Reading.Value:0.#} раза дольше.");
        }

        internal static void Refuse(string why)
        {
            if (string.IsNullOrEmpty(why)) return;
            try { GameController.ShowMessage(why, 3f); } catch { }
        }
    }

    // Книгу чужой или лишней школы не открыть: отказ звучит там же, где игра отказывает сама.
    [HarmonyPatch(typeof(HumaniodUnit), "StartBookLearn")]
    internal static class StartBook_Schools_Patch
    {
        private static bool Prefix(HumaniodUnit __instance, Inventory bookInv, ref bool __result)
        {
            try
            {
                if (bookInv == null || bookInv.itemInfo == null) return true;
                string why;
                if (Schools.May(__instance, bookInv.itemInfo.learnSkillSet, out why)) return true;
                Schools.Refuse(why);
                __result = false;
                return false;
            }
            catch
            {
                return true;
            }
        }
    }

    // Начатая до правила книга лишней школы откладывается при первом же часе чтения.
    [HarmonyPatch(typeof(HumaniodUnit), "BookLearnProgress")]
    internal static class BookProgress_Schools_Patch
    {
        private static bool Prefix(HumaniodUnit __instance)
        {
            try
            {
                if (__instance == null || __instance.currentLearningBook == null || __instance.currentLearningBook.itemInfo == null) return true;
                string why;
                if (Schools.May(__instance, __instance.currentLearningBook.itemInfo.learnSkillSet, out why)) return true;
                Schools.Refuse(why);
                __instance.StopBookLearn();
                return false;
            }
            catch
            {
                return true;
            }
        }
    }

    // Свиток, что учит школе сразу, — то же правило.
    [HarmonyPatch(typeof(SpellManager), "OnConsumableUsed")]
    internal static class Consumable_Schools_Patch
    {
        private static bool Prefix(UIItemInfo item, UnitAttribute ___unit)
        {
            try
            {
                if (item == null) return true;
                string why;
                if (Schools.May(___unit, item.learnSkillSet, out why)) return true;
                Schools.Refuse(why);
                return false;
            }
            catch
            {
                return true;
            }
        }
    }

    // Рождение героя мира: пока оно идёт, лишняя школа мага отсекается до раздачи опыта.
    [HarmonyPatch(typeof(HeroUnitMaker), "ForgeTheHero")]
    internal static class Forge_Schools_Patch
    {
        private static void Prefix() { Schools.forging++; }

        private static void Postfix(NPCSaveData psd)
        {
            if (Schools.On()) { try { Magi.OneSchool(psd); } catch { } }
        }

        private static Exception Finalizer(Exception __exception)
        {
            Schools.forging = Mathf.Max(0, Schools.forging - 1);
            return __exception;
        }
    }

    // Рост НПС: учится только в своей школе, новой не заводит.
    [HarmonyPatch(typeof(HeroUnitMaker), "AutoLearnSkill")]
    internal static class Learn_Schools_Patch
    {
        private static void Prefix(NPCSaveData nsd, out List<SkillSet> __state)
        {
            __state = null;
            try { __state = Schools.Begin(nsd); } catch { }
        }

        private static void Postfix(NPCSaveData nsd, List<SkillSet> __state)
        {
            try { Schools.End(nsd, __state); } catch { }
        }
    }

    [HarmonyPatch(typeof(HeroUnitMaker), "TryLearnSkill")]
    internal static class TryLearn_Schools_Patch
    {
        private static bool Prefix(NPCSaveData nsd, ScriptableObject skill, out ScriptableObject attempted, ref bool __result)
        {
            attempted = null;
            try
            {
                if (Schools.Allows(nsd, skill)) return true;
                attempted = skill;
                __result = false;
                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}
