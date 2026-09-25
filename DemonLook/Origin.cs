using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;

namespace DemonLook
{
    /// <summary>
    /// Tells a demon's beginning in his own words.
    ///
    /// Every origin the game offers is a life that was lived: a beggar with a broken bowl, a
    /// merchant's son, a soldier discharged. None of them fit something that did not arrive in
    /// the world so much as it was pulled into it, and reading «beggar» over a creature the
    /// guards will kill on sight makes the whole start read as a mistake.
    ///
    /// Only the two lines on screen are replaced. The origin itself — its money, its gear, its
    /// starting skills — is left exactly as the game wrote it, because those are balance and
    /// this is a name. Changing the data would mean changing it for every race that picks the
    /// same origin, and it lives in an asset shared by the whole game.
    /// </summary>
    internal static class Origin
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Name;
        internal static ConfigEntry<string> Lore;
        internal static ConfigEntry<bool> ShowSchool;
        internal static ConfigEntry<int> StartSpells;
        internal static ConfigEntry<string> Utility;
        internal static ConfigEntry<int> TraitPoints;
        internal static ConfigEntry<int> TraitSlots;
        internal static ConfigEntry<bool> Ironman;
        internal static ConfigEntry<bool> SkipPrologue;
        internal static ConfigEntry<bool> NoEasy;
        internal static ConfigEntry<string> Demand;

        /// <summary>True while a demon is being made and his school should be named on screen.</summary>
        internal static bool Showing;

        private static LivingSkill lentSkills;
        private static Dictionary<int, int> hadSkills;

        /// <summary>Which living skills the origin lends, by their place in the game's own list.</summary>
        private static Dictionary<int, int> Utilities()
        {
            Dictionary<int, int> got = new Dictionary<int, int>();

            foreach (string one in (Utility.Value ?? "").Split(','))
            {
                string[] halves = one.Split('=');
                if (halves.Length != 2) continue;

                int much;
                if (!int.TryParse(halves[1].Trim(), out much)) continue;

                try
                {
                    HumanUtilityType kind = (HumanUtilityType)Enum.Parse(
                        typeof(HumanUtilityType), halves[0].Trim(), true);

                    got[(int)kind] = much;
                }
                catch
                {
                    DemonLookPlugin.Log.LogWarning($"Навыка «{halves[0].Trim()}» в игре нет.");
                }
            }

            return got;
        }

        /// <summary>The school's name as the game would print it.</summary>
        internal static string SchoolName()
        {
            string name = (School.Value ?? "").Trim();
            if (name.Length == 0) return "";

            // Игра пишет название с заглавной и переводит по нему же: «black» → «Black» →
            // «Магия тьмы». Повторяем ровно это, чтобы строка совпала слово в слово с той,
            // что игра печатает сама, когда в наборе есть заклинания.
            string key = char.ToUpperInvariant(name[0]) + name.Substring(1);

            try { return gameManager.LocalizedString(key); }
            catch { return key; }
        }
        internal static ConfigEntry<string> School;
        internal static ConfigEntry<string> Skills;

        // Набор, который мы вложили в происхождение, и куда именно: чтобы вынуть его
        // обратно, когда игрок уйдёт с демона на другую расу.
        private static SpellSet lent;
        private static StartSetting lentTo;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Origin", "Enabled", true,
                "Replace the origin's name and description while a demon is being made.");

            Name = config.Bind("Origin", "Name", "Король Ада",
                "What the origin is called for a demon.");

            Lore = config.Bind("Origin", "Lore",
                "Вы не родились и не пришли — вас вытащили.\n\n"
                + "Круг догорал, когда вы открыли глаза, и те, кто чертил его, ещё стояли на "
                + "своих местах. Они звали не вас лично: им нужен был кто-нибудь оттуда, любой, "
                + "и хватило бы меньшего. Достался им Король Ада, и они поняли это раньше, чем "
                + "успели обрадоваться.\n\n"
                + "У вас нет имени, которое здесь можно назвать, нет дома, куда можно вернуться, "
                + "и нет ничего, кроме того, чем вы были до круга. Мир узнаёт вас с первого "
                + "взгляда — не по лицу, а по тому холоду, который идёт впереди. Стража возьмётся "
                + "за оружие, не спросив, а герои найдут вас сами: слухи о тёмном ритуале уже "
                + "идут по дорогам.\n\n"
                + "Зато у вас есть время. Больше, чем у любого, кто попытается его отнять.",
                "The text shown under the name. Written without figures, like the racial trait: "
                + "what the demon is, not what he is worth.");

            ShowSchool = config.Bind("Origin", "ShowSchool", true,
                "Offer the dark school through the origin's starting skills as well. Mind what "
                + "that row is: the game does not merely print it, it hands out everything "
                + "standing in it. See StartSpells below — at zero nothing is given and this "
                + "changes nothing.");

            StartSpells = config.Bind("Origin", "StartSpells", 0,
                new ConfigDescription(
                    "How many of the school's spells the origin actually hands the demon at "
                    + "birth. Zero, and none: the school itself he already has at the first "
                    + "rank from his blood, and that is the whole of what he should start with. "
                    + "This row was once filled with the entire school — ten spells at once — "
                    + "on the belief that it was shown rather than granted. It is granted. "
                    + "Raise it and the lowest-ranked spells come first.",
                    new AcceptableValueRange<int>(0, 20)));

            School = config.Bind("Origin", "School", "black",
                "Which school to show there.");

            Utility = config.Bind("Origin", "Utility", "Intimidate=10",
                "Living skills the origin actually grants, written as «name=number» and "
                + "separated by commas. The game both prints this line and hands it out from "
                + "the same field, so what is written here is what the demon starts with — no "
                + "second rule, no risk of one saying what the other does not do. Names are the "
                + "game's own: Persuade, Bargain, Intimidate, Scholarly, Pathfind, Insight, "
                + "Mechanics, Sneak, Theft, Smithing, Alchemy, Cooking, Medical, Training, "
                + "Torture. Lent while a demon is chosen and taken back when he is not, so a "
                + "human beggar stays a beggar.");

            TraitPoints = config.Bind("Origin", "TraitPoints", 5,
                new ConfigDescription(
                    "How many points of traits a demon may carry at his making. The game allows "
                    + "three to everyone; a creature dragged out of hell is not everyone, and "
                    + "what he is arrives with him rather than being chosen. Raised only while "
                    + "a demon is on the screen — every other race keeps the game's own three.",
                    new AcceptableValueRange<int>(1, 20)));

            TraitSlots = config.Bind("Origin", "TraitSlots", 7,
                new ConfigDescription(
                    "And how many traits he may hold at once. The game gives five slots to "
                    + "everyone and writes «сколько-то из пяти» under them with the five nailed "
                    + "into the code, so raising the count means correcting that line too — "
                    + "otherwise it counts down from a five that is no longer true.",
                    new AcceptableValueRange<int>(1, 20)));

            Ironman = config.Bind("Origin", "Ironman", false,
                "Require ironman of a demon. Прежде стояло «да»: Король Ада играется без "
                + "второго шанса или не играется вовсе. Но принуждение это выходило поперёк "
                + "самой затеи — играть демоном хотят не затем, чтобы играть сложнее, а затем, "
                + "чтобы играть демоном. Кто хочет железного человека, тот его и поставит: "
                + "галочка на месте и никем не заперта. Поставьте «да», чтобы вернуть запрет.");

            SkipPrologue = config.Bind("Origin", "SkipPrologue", true,
                "And the prologue is skipped. He did not come to this world along that road.");

            NoEasy = config.Bind("Origin", "NoEasy", true,
                "And easy mode is off. The game already refuses to hold ironman and easy at "
                + "once, but saying it here does not depend on somebody else's handler.");

            Demand = config.Bind("Origin", "Demand",
                "Король Ада играется без пролога.",
                "What is said when a demon is made with those conditions off.");

            Skills = config.Bind("Origin", "Skills", "",
                "Added to the origin's line of useful skills, after what the game prints itself. "
                + "The dark school is not a living skill and has no place in that line, so it is "
                + "named here. Intimidation is a living skill and is now granted for real through "
                + "Utility above, so the game prints it on its own and it is not repeated here.");
        }

        /// <summary>
        /// Lends the origin a school for as long as a demon is being made.
        ///
        /// Происхождения — общий на всю игру список, и один и тот же «Нищий» достаётся любой
        /// расе. Поэтому набор именно одалживается: кладётся, пока выбран демон, и вынимается
        /// обратно, стоит выбрать другую расу. Так нищий-человек остаётся нищим.
        /// </summary>
        internal static void Lend(StartSetting set, bool demon)
        {
            try
            {
                if (lent != null && lentTo != null && lentTo.skillSets != null)
                {
                    lentTo.skillSets.Remove(lent);
                    lent = null;
                    lentTo = null;
                }

                // Живые навыки возвращаются на место первыми, ещё до всякой проверки: этот
                // набор общий на всю игру, и оставленная в нём десятка запугивания досталась
                // бы человеку-нищему, стоило переключить расу.
                if (lentSkills != null && hadSkills != null)
                {
                    foreach (KeyValuePair<int, int> was in hadSkills) lentSkills[was.Key] = was.Value;

                    lentSkills = null;
                    hadSkills = null;
                }

                Showing = demon && ShowSchool.Value;

                if (!demon || set == null) return;

                // Одну и ту же строку игра и печатает, и раздаёт: «+10 Запугивание» на экране
                // и «humanTalent[i] = utilitySkills[i]» в тот же проход. Поэтому написанное
                // здесь — это и есть то, с чем демон начнёт, и надпись не может разойтись с делом.
                if (set.utilitySkills != null)
                {
                    Dictionary<int, int> want = Utilities();

                    if (want.Count > 0)
                    {
                        hadSkills = new Dictionary<int, int>();

                        foreach (KeyValuePair<int, int> one in want)
                        {
                            hadSkills[one.Key] = set.utilitySkills[one.Key];
                            set.utilitySkills[one.Key] = one.Value;
                        }

                        lentSkills = set.utilitySkills;
                    }
                }

                if (!ShowSchool.Value || set.skillSets == null) return;

                SkillSet school;
                try { school = (SkillSet)Enum.Parse(typeof(SkillSet), School.Value.Trim(), true); }
                catch { return; }

                UISpellDatabase db = UISpellDatabase.Instance;
                if (db == null || db.spells == null) return;

                SpellSet ours = new SpellSet();
                ours.set = school;
                ours.spells = new List<UISpellInfo>();
                ours.talents = new List<UITalentInfo>();

                // Сама школа — это талант мастерства, и у набора для талантов свой список.
                // Игра раздаёт его отдельно от заклинаний и первым уровнем — ровно то, что
                // демону и причитается при рождении. Так школа назначается прямо отсюда, из
                // окна создания, где о ней и написано, а не приходит неизвестно откуда.
                UITalentDatabase lore = UITalentDatabase.Instance;

                if (lore != null)
                {
                    UITalentInfo mark = lore.GetMasteryTalent(school);
                    if (mark != null) ours.talents.Add(mark);
                }

                foreach (UISpellInfo spell in db.spells)
                {
                    if (spell != null && spell.SkillSet == school) ours.spells.Add(spell);
                }

                // А вот список заклинаний игра не показывает, а раздаёт: проходит по нему и
                // зовёт «AddSpell» на каждое. Класть сюда всю школу значило отдать герою при
                // рождении десяток заклинаний разом — чего он не заслужил и что делает
                // ненужным весь путь к ним. Оставляем столько, сколько велено, начиная с тех,
                // что просят меньше всего мастерства.
                ours.spells.Sort(delegate (UISpellInfo a, UISpellInfo b)
                {
                    int by = a.RequireMastery.CompareTo(b.RequireMastery);
                    return by != 0 ? by : a.ID.CompareTo(b.ID);
                });

                int keep = UnityEngine.Mathf.Clamp(StartSpells.Value, 0, ours.spells.Count);

                if (keep == 0) ours.spells.Clear();
                else if (ours.spells.Count > keep) ours.spells.RemoveRange(keep, ours.spells.Count - keep);

                if (ours.spells.Count == 0 && ours.talents.Count == 0) return;

                set.skillSets.Insert(0, ours);
                lent = ours;
                lentTo = set;
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Не смог показать школу в происхождении: " + e.Message);
            }
        }
    }

    // Постфикс на обновление окна, а не правка самих происхождений: переписывать данные
    // значило бы менять их для всех рас разом, ведь список один на всю игру. Здесь же
    // подменяются только две строки на экране, и стоит игроку выбрать другую расу, как
    // игра сама нарисует их заново из своих данных.
    // Вложение школы — префиксом, а не постфиксом. Значки стартовых навыков игра строит
    // внутри самого метода, читая список происхождения; положенное туда после его работы
    // на экран уже не попадало, и строка оставалась пустой.
    [HarmonyPatch(typeof(CharacterCreaterController), "ChangeStartSet")]
    internal static class ChangeStartSet_Lend_Patch
    {
        private static void Prefix(CharacterCreaterController __instance)
        {
            try
            {
                if (!Origin.Enabled.Value) return;

                CharacterCustomizationController looks =
                    UnityEngine.Object.FindObjectOfType<CharacterCustomizationController>();

                // Берём набор так же, как его возьмёт сам метод первой своей строкой:
                // «currentSet = startSets[setIndex]». Читать «currentSet» здесь нельзя — он
                // ещё держит предыдущее происхождение, и одолженное легло бы не туда. Со
                // школой это сходило с рук, потому что набор обычно тот же; с живыми навыками
                // и с первым заходом — нет.
                StartSetting set = null;

                if (__instance.startSets != null
                    && __instance.setIndex >= 0
                    && __instance.setIndex < __instance.startSets.Count)
                {
                    set = __instance.startSets[__instance.setIndex];
                }

                Origin.Lend(set, looks != null && looks.race == DemonLookPlugin.DemonRaceName);
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogError("Не смог одолжить школу происхождению: " + e);
            }
        }
    }

    // Название школы игра печатает внутри той же ветки, что и раздаёт заклинания: нет
    // заклинаний — нет и надписи, строка пустая. Но школа у демона есть, она в расовом пакете
    // и первого уровня, и сказать об этом читателю надо, ничего ему при этом не выдавая.
    // Поэтому надпись ставится отдельно, а список остаётся пустым.
    // Потолок очков за черты игра держит общим для всех и равным трём. Демон не выбирает,
    // каким он вышел из круга, — он таким пришёл, — и трёх очков на это мало. Поднимаем, пока
    // на экране демон, и возвращаем игре её число, стоит выбрать другую расу.
    //
    // Правится на обновлении окна, а не при входе в него: игрок может вернуться и сменить
    // расу, и потолок должен пойти следом.
    [HarmonyPatch(typeof(UITraitManager), "UpdateUIState")]
    internal static class TraitCap_Patch
    {
        private const int NativeSlots = 5;

        private static int native = -1;
        private static CharacterCustomizationController looks;
        private static System.Reflection.FieldInfo chosenField;

        private static int slots = NativeSlots;

        private static void Prefix(UITraitManager __instance)
        {
            try
            {
                if (!Origin.Enabled.Value || __instance == null) return;

                if (native < 0) native = __instance.pointCap;

                if (looks == null)
                {
                    looks = UnityEngine.Object.FindObjectOfType<CharacterCustomizationController>();
                }

                bool demon = looks != null && looks.race == DemonLookPlugin.DemonRaceName;

                int cap = demon ? Origin.TraitPoints.Value : native;
                if (__instance.pointCap != cap) __instance.pointCap = cap;

                slots = demon ? UnityEngine.Mathf.Max(1, Origin.TraitSlots.Value) : NativeSlots;

                // Число ячеек игра держит не как предел, а как остаток: пять при входе, минус
                // единица за каждую взятую черту. Записать туда семёрку один раз нельзя —
                // неизвестно, когда игрок выбрал расу и сколько уже успел набрать. Поэтому
                // держим верным не остаток, а сумму: взятое плюс оставшееся всегда равно
                // пределу. Так и смена расы посреди выбора не ломает счёт, и ни одна уже
                // взятая черта не пропадает.
                int chosen = Chosen(__instance);
                if (chosen < 0) return;

                if (chosen + __instance.traitPoint != slots)
                {
                    __instance.traitPoint = slots - chosen;
                }
            }
            catch
            {
            }
        }

        // Подпись «сколько из пяти» собрана в коде с зашитой пятёркой, поэтому при семи ячейках
        // она считала бы от неверного числа и уходила в минус. Переписываем её после игры.
        private static void Postfix(UITraitManager __instance)
        {
            try
            {
                if (!Origin.Enabled.Value || __instance == null) return;
                if (slots == NativeSlots) return;
                if (__instance.remainPointText == null) return;

                int chosen = Chosen(__instance);
                if (chosen < 0) return;

                __instance.remainPointText.text = chosen + "/" + slots;
            }
            catch
            {
            }
        }

        private static int Chosen(UITraitManager panel)
        {
            if (chosenField == null)
            {
                chosenField = AccessTools.Field(typeof(UITraitManager), "selectTraits");
            }

            if (chosenField == null) return -1;

            System.Collections.ICollection taken = chosenField.GetValue(panel) as System.Collections.ICollection;

            return taken == null ? -1 : taken.Count;
        }
    }

    [HarmonyPatch(typeof(CharacterCreaterController), "ChangeStartSpell")]
    internal static class ChangeStartSpell_Name_Patch
    {
        private static void Postfix(CharacterCreaterController __instance)
        {
            try
            {
                if (!Origin.Enabled.Value || !Origin.Showing) return;
                if (__instance == null || __instance.skillText == null) return;

                // Если игра уже что-то написала сама — значит в наборе были заклинания, и
                // спорить с ней не о чем.
                if (!string.IsNullOrEmpty(__instance.skillText.text)) return;

                __instance.skillText.text = Origin.SchoolName();
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Не смог назвать школу в происхождении: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(CharacterCreaterController), "ChangeStartSet")]
    internal static class ChangeStartSet_Origin_Patch
    {
        // Второй постфикс на тот же метод: первый, в StartRestriction, решает какое
        // происхождение демону позволено, а этот — как оно называется. Гармония держит
        // их порядком объявления, и спорить им не о чем.
        private static void Postfix(CharacterCreaterController __instance)
        {
            try
            {
                if (!Origin.Enabled.Value) return;

                CharacterCustomizationController looks =
                    UnityEngine.Object.FindObjectOfType<CharacterCustomizationController>();

                bool demon = looks != null && looks.race == DemonLookPlugin.DemonRaceName;

                if (!demon) return;

                if (__instance.startSetNameText != null)
                {
                    __instance.startSetNameText.text = Origin.Name.Value;
                }

                if (__instance.backGroundText != null)
                {
                    __instance.backGroundText.text = Origin.Lore.Value;
                }

                // Дописываем к тому, что игра уже посчитала сама, а не вместо него: строка
                // общая, и затирать её значило бы спрятать то, что происхождение и вправду даёт.
                string extra = (Origin.Skills.Value ?? "").Trim();

                if (__instance.utilitySkillText != null && extra.Length > 0)
                {
                    string had = __instance.utilitySkillText.text ?? "";
                    __instance.utilitySkillText.text = had.Trim().Length > 0 ? had + "   " + extra : extra;
                }
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogError("Не смог переписать происхождение: " + e);
            }
        }
    }
    // Условия демона: железный человек и пропуск пролога. Ставятся два слоя, потому что
    // одного мало. Первый — сами галочки: они включаются и запираются, чтобы читалось как
    // условие, а не как запрет. Второй — отказ при создании: галочку могут снять и мимо окна,
    // а правило должно быть правилом.
    [HarmonyPatch(typeof(CharacterCreaterController), "ChangeStartSet")]
    internal static class Conditions_Patch
    {
        private static bool had, hadEasy, hadSkip;
        private static bool lent;

        private static void Postfix(CharacterCreaterController __instance)
        {
            try
            {
                if (!Origin.Enabled.Value || __instance == null) return;

                CharacterCustomizationController looks =
                    UnityEngine.Object.FindObjectOfType<CharacterCustomizationController>();

                bool demon = looks != null && looks.race == DemonLookPlugin.DemonRaceName;

                if (!demon)
                {
                    // Окно создания одно на всю игру: оставить в нём запертые галочки человеку
                    // было бы чужой бедой.
                    if (lent)
                    {
                        Give(__instance.ironmanToggle, had, true);
                        Give(__instance.easyModeToggle, hadEasy, true);
                        Give(__instance.skipPrologueToggle, hadSkip, true);
                        lent = false;
                    }
                    return;
                }

                if (!lent)
                {
                    had = __instance.ironmanToggle != null && __instance.ironmanToggle.isOn;
                    hadEasy = __instance.easyModeToggle != null && __instance.easyModeToggle.isOn;
                    hadSkip = __instance.skipPrologueToggle != null && __instance.skipPrologueToggle.isOn;
                    lent = true;
                }

                if (Origin.Ironman.Value) Give(__instance.ironmanToggle, true, false);
                if (Origin.NoEasy.Value) Give(__instance.easyModeToggle, false, false);
                if (Origin.SkipPrologue.Value) Give(__instance.skipPrologueToggle, true, false);
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Не смог выставить условия демона: " + e.Message);
            }
        }

        private static void Give(UnityEngine.UI.Toggle toggle, bool on, bool free)
        {
            if (toggle == null) return;

            if (toggle.isOn != on) toggle.isOn = on;
            toggle.interactable = free;
        }
    }

    // Второй слой. Игра создаётся в одном месте, и здесь же читаются условия — значит здесь их
    // и спрашивать. Отказ оставляет игрока в окне создания, ничего не потеряв.
    [HarmonyPatch(typeof(CharacterCreaterController), "CreateArchiveAndEnterGame")]
    internal static class Conditions_Create_Patch
    {
        private static bool Prefix(CharacterCreaterController __instance)
        {
            try
            {
                if (!Origin.Enabled.Value || __instance == null) return true;

                CharacterCustomizationController looks =
                    UnityEngine.Object.FindObjectOfType<CharacterCustomizationController>();

                if (looks == null || looks.race != DemonLookPlugin.DemonRaceName) return true;

                bool iron = __instance.ironmanToggle != null && __instance.ironmanToggle.isOn;
                bool easy = __instance.easyModeToggle != null && __instance.easyModeToggle.isOn;
                bool skip = __instance.skipPrologueToggle != null && __instance.skipPrologueToggle.isOn;

                bool ok = (!Origin.Ironman.Value || iron)
                    && (!Origin.NoEasy.Value || !easy)
                    && (!Origin.SkipPrologue.Value || skip);

                if (ok) return true;

                DemonLookPlugin.Log.LogInfo("Создание отклонено: железный "
                    + $"{iron}, простой {easy}, пролог пропущен {skip}.");

                GameController.ShowMessage(Origin.Demand.Value, 4f);
                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}
