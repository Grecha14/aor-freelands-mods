using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ItemForge
{
    /// <summary>
    /// Lets a beast be read the way a man is read.
    ///
    /// Окно осмотра разделено надвое, и правая половина у зверя пуста не потому, что нечего
    /// показать, а потому, что её выключают целиком:
    ///
    ///     if (humaniodUnit) { humanUI.SetActive(true);  ... }
    ///     else              { humanUI.SetActive(false); }
    ///
    /// Гаснет короб, а не переключатель. Отсюда прежняя правка и промахнулась: переключатель
    /// был открыт, числа в строки написаны — но строки лежали в выключенном коробе, и вкладка
    /// открывалась пустой. Чинить надо дверь, а не замок.
    ///
    /// Сами статы у зверя нигде не хранятся: `Beastly` выводит их заново всякий раз, когда
    /// спрашивают, — из размера, уровня и написанного руками для отдельных зверей. По ним
    /// считается всё: сколько зверь бьёт, сколько держит, сколько тащит. Поле под них есть
    /// (`humanAttribute` объявлен у `NPCSaveData`, а запись зверя за общей ссылкой вполне может
    /// оказаться той же), и если оно доступно — пишем туда, а не поверх окна.
    ///
    /// Остальные полки того же короба у зверя пусты по существу: владения оружием, ремёсел и
    /// школ у него нет, и там встаёт «нет», а не чужие числа от прошлого осмотренного. Черты
    /// же есть — `talentmanger` объявлен у `UnitAttribute`, а не у людей, — и рисуются той же
    /// дорогой, какой игра рисует их человеку.
    /// </summary>
    internal static class Reading
    {
        internal static ConfigEntry<bool> Enabled;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Reading", "Enabled", true,
                "Show a beast's six attributes in the inspect window. The game switches the "
                + "whole right-hand box off for anything that is not a person, so the tab opens "
                + "on nothing. The numbers are not kept anywhere either — they are worked out "
                + "afresh from size and level every time somebody asks — but every number that "
                + "decides how hard a bear hits and how much a horse carries comes from them, "
                + "and until now they could only be read out of the log.");

            Word = config.Bind("Reading", "Word", "Опыт",
                "The word by which a line about experience is recognised in the inspect window. "
                + "The window carries one of its own and the borrowed box brings another, so "
                + "all but one are hidden - and they are told apart by what they say, because "
                + "the fields themselves belong to somebody else's window.");
        }

        /// <summary>Fills the six rows from what the beast actually has.</summary>
        internal static void Show(InspectPanelManager panel, UnitAttribute who)
        {
            if (!Enabled.Value || panel == null || who == null) return;
            if (who is HumaniodUnit) return;                  // людьми занята сама игра

            try
            {
                if (Beastly.Enabled == null || !Beastly.Enabled.Value) return;

                float[] six = Beastly.Mine(who);
                if (six == null || six.Length < 6) return;

                // Сперва пробуем положить числа туда, где им и место. Поле объявлено у
                // «NPCSaveData», а у зверя запись общая — но за общей ссылкой вполне может
                // лежать та же людская, и тогда всё, что читает эти статы, начнёт видеть
                // настоящее, а не одно лишь окно.
                Fill(who, six);

                // Короб игра выключила целиком — включаем обратно. Без этой строки всё
                // дальнейшее пишется в поля, которых на экране нет.
                if (panel.humanUI != null) panel.humanUI.SetActive(true);

                Say(panel.s_Strength, six[0]);
                Say(panel.s_Endurance, six[1]);
                Say(panel.s_Agility, six[2]);
                Say(panel.s_Precision, six[3]);
                Say(panel.s_Intelligence, six[4]);
                Say(panel.s_Willpower, six[5]);

                // «Потенциал» у человека — сколько вложено из положенного. У зверя вкладывать
                // некуда: статы не хранятся, а считаются из размера и уровня. Их туда и пишем —
                // не подделку под человеческую строку, а то, из чего эти шесть выведены.
                //
                // А у прирученного есть что вкладывать, и там же встаёт его опыт: это его окно
                // характеристик, другого у зверя нет.
                bool ours = Taming.Enabled != null && Taming.Enabled.Value && Taming.Mine(who);

                // Уровень зверя выводится из его шести, но пересчитывался только при покупке
                // очка. Оттого в окне стояла единица у зверя, который по своим числам давно
                // двенадцатый. Пересчитываем перед тем, как показать.
                if (ours) Taming.Recount(who);

                if (panel.potential != null)
                {
                    // У человека здесь два числа: сколько набрано и сколько отпущено. У
                    // прирученного пишем ровно так же — окно людское, и читаться должно как
                    // людское.
                    //
                    // Опыту здесь не место, и это не мелочь: поле правое, во всю ширину
                    // строки, а подпись «Потенциал» лежит под ним слева. Длинное число
                    // уползало влево и садилось прямо на подпись.
                    if (ours)
                    {
                        int sum = 0;
                        for (int i = 0; i < 6; i++) sum += Mathf.RoundToInt(six[i]);

                        panel.potential.text = sum + " / " + Taming.Nature(who);
                    }
                    else
                    {
                        panel.potential.text = Grown(who);
                    }

                    panel.potential.horizontalOverflow = HorizontalWrapMode.Wrap;
                }

                // Опыт — своей строкой над потенциалом, как в окне персонажа. Но если короб
                // взят напрокат, он несёт свою строку опыта, и наша была бы второй.
                Coins(panel, who, ours && lent == null && lentFailed);

                // И всё же строк выходило две. Своя пряталась исправно, а вторую несёт само
                // окно осмотра: у него есть собственное поле опыта, наверху справа, и к
                // взятому напрокат коробу оно отношения не имеет. Гасим всё, что говорит об
                // опыте вне нашей копии: одно число об одном и том же — и довольно.
                if (ours) Once(panel);

                // Короб характеристик берём готовый, из окна персонажа: свои кнопки я ставил
                // дважды и дважды промахнулся мимо видимой части окна.
                Lend(panel, who, six, ours);

                Plus(panel, who, ours && lent == null, six);

                // Открыли окно зверя — в лог ложится полный разбор его чисел: что от породы,
                // что вложено, какая глубина и во что обойдётся следующее очко.
                if (ours) Taming.Explain(who);

                // Три полки, которых у зверя нет по существу. Если их не тронуть, на них
                // останутся числа прошлого осмотренного человека — а это хуже пустоты.
                Blank(panel.s_WeaponMastery, panel.weaponMasteryUnknown);
                Blank(panel.s_Ability, panel.abilityUnknown);
                Blank(panel.s_Genres, panel.genresUnknown);

                Traits(panel, who);

                // Панель героя — про того, кем играют, и зверю она не принадлежит.
                if (panel.playerAbilityPanel != null) panel.playerAbilityPanel.SetActive(false);

                // Переключатель игра тоже заперла: открываем, иначе на заполненный короб
                // нельзя перейти.
                Toggle open = AccessTools.Field(typeof(InspectPanelManager), "humanToggle")
                    .GetValue(panel) as Toggle;

                if (open != null) open.interactable = true;
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог показать статы зверя: " + e);
            }
        }

        // ------------------------------------------------------ панель напрокат

        // Короб характеристик, взятый из окна персонажа. Свои кнопки я ставил дважды и дважды
        // промахивался: то за рамкой окна, то за краем короба. Проще не строить своё, а взять
        // готовое — то самое, которое игрок видит у людей, — и заполнить его зверем.
        private static GameObject lent;
        private static GameObject native;
        private static GameObject nativeTitle;
        private static UnityEngine.UI.Text lentPotential;
        private static UnityEngine.UI.Text lentExp;

        internal static ConfigEntry<string> Word;
        private static readonly UnityEngine.UI.Text[] lentSix = new UnityEngine.UI.Text[6];
        private static readonly UnityEngine.UI.Button[] lentPlus =
            new UnityEngine.UI.Button[6];
        private static bool lentFailed;

        /// <summary>The path from one transform down to another, for finding it in a copy.</summary>
        private static string Road(Transform child, Transform root)
        {
            if (child == null || root == null) return null;

            string road = "";

            for (Transform at = child; at != null && at != root; at = at.parent)
            {
                road = road.Length == 0 ? at.name : at.name + "/" + road;
            }

            return road;
        }

        /// <summary>The nearest object that holds both of these.</summary>
        private static Transform Common(Transform a, Transform b)
        {
            for (Transform at = a; at != null; at = at.parent)
            {
                for (Transform down = b; down != null; down = down.parent)
                {
                    if (down == at) return at;
                }
            }

            return null;
        }

        /// <summary>
        /// Borrows the attribute block out of the character window and hangs it here.
        ///
        /// Берётся не вся панель персонажа — в ней и здоровье, и урон, и сопротивления, — а
        /// только тот её кусок, который держит «Потенциал», шесть строк и шесть кнопок: общий
        /// предок потенциала и воли. Он и встаёт на место родного короба окна осмотра.
        ///
        /// Игровой обработчик с копии снимается: он писан на человека и на звере выбросит
        /// ошибку в первый же кадр. Заполнять её будем сами.
        /// </summary>
        private static bool Borrow(InspectPanelManager panel)
        {
            if (lent != null) return true;
            if (lentFailed || panel == null || panel.potential == null) return false;

            try
            {
                UIApplyUnitAttribute source = null;

                foreach (UIApplyUnitAttribute one in
                    Resources.FindObjectsOfTypeAll<UIApplyUnitAttribute>())
                {
                    if (one == null || one.potential == null || one.willpower == null) continue;
                    if (one.addBut == null || one.addBut.Length < 6) continue;

                    source = one;
                    break;
                }

                if (source == null)
                {
                    lentFailed = true;
                    ItemForgePlugin.Log.LogWarning("Короба характеристик у персонажа "
                        + "не нашлось — панель напрокат не взять.");
                    return false;
                }

                // Берём кусок пошире: от строки опыта до силы воли. У персонажа опыт стоит
                // над заголовком «Атрибуты» отдельной строкой без значка — «remainPointText»,
                // — и раз уж мы переносим короб, пусть переезжает и она. Свою самодельную
                // строку опыта тогда не рисуем вовсе.
                Transform root = source.remainPointText != null
                    ? Common(source.remainPointText.transform, source.willpower.transform)
                    : Common(source.potential.transform, source.willpower.transform);

                if (root == null) { lentFailed = true; return false; }

                // Пути запоминаем до копирования: в копии они те же.
                string wayPotential = Road(source.potential.transform, root);
                string wayExp = source.remainPointText != null
                    ? Road(source.remainPointText.transform, root)
                    : null;

                UnityEngine.UI.Text[] six =
                {
                    source.strength, source.endurance, source.agility,
                    source.precision, source.intelligence, source.willpower
                };

                string[] waySix = new string[6];
                string[] wayPlus = new string[6];

                // И пути к строкам владения оружием: из них зверю остаётся одна.
                string[] wayMastery = null;

                if (source.masteryLevel != null)
                {
                    wayMastery = new string[source.masteryLevel.Length];

                    for (int i = 0; i < wayMastery.Length; i++)
                    {
                        wayMastery[i] = source.masteryLevel[i] != null
                            ? Road(source.masteryLevel[i].transform, root)
                            : null;
                    }
                }

                for (int i = 0; i < 6; i++)
                {
                    waySix[i] = six[i] != null ? Road(six[i].transform, root) : null;
                    wayPlus[i] = source.addBut[i] != null
                        ? Road(source.addBut[i].transform, root)
                        : null;
                }

                native = panel.potential.transform.parent != null
                    ? panel.potential.transform.parent.parent.gameObject
                    : null;

                if (native == null) { lentFailed = true; return false; }

                // Заголовок «Атрибуты» стоит отдельной строкой перед коробом, а взятая
                // напрокат панель несёт свой. Родной прячем вместе с родным коробом, иначе
                // заголовок стоит дважды.
                int spot = native.transform.GetSiblingIndex();

                if (spot > 0 && native.transform.parent != null)
                {
                    nativeTitle = native.transform.parent.GetChild(spot - 1).gameObject;
                }

                GameObject copy = UnityEngine.Object.Instantiate(root.gameObject,
                    native.transform.parent);

                copy.name = "ItemForgeAttributes";
                copy.transform.SetSiblingIndex(native.transform.GetSiblingIndex());

                UIApplyUnitAttribute mind = copy.GetComponent<UIApplyUnitAttribute>();
                if (mind != null) UnityEngine.Object.Destroy(mind);

                Transform at = copy.transform.Find(wayPotential);
                lentPotential = at != null ? at.GetComponent<UnityEngine.UI.Text>() : null;

                if (wayExp != null)
                {
                    Transform found = copy.transform.Find(wayExp);
                    lentExp = found != null ? found.GetComponent<UnityEngine.UI.Text>() : null;
                }

                for (int i = 0; i < 6; i++)
                {
                    if (waySix[i] != null)
                    {
                        Transform found = copy.transform.Find(waySix[i]);
                        lentSix[i] = found != null
                            ? found.GetComponent<UnityEngine.UI.Text>()
                            : null;
                    }

                    if (wayPlus[i] != null)
                    {
                        Transform found = copy.transform.Find(wayPlus[i]);
                        lentPlus[i] = found != null
                            ? found.GetComponent<UnityEngine.UI.Button>()
                            : null;
                    }
                }

                lent = copy;

                Trim(copy.transform, waySix[5], wayMastery);

                int got = 0;
                for (int i = 0; i < 6; i++) if (lentSix[i] != null && lentPlus[i] != null) got++;

                ItemForgePlugin.Log.LogInfo($"Короб характеристик взят напрокат из «{root.name}»: "
                    + $"строк с кнопкой {got} из шести, потенциал "
                    + $"{(lentPotential != null ? "есть" : "нет")}.");

                return true;
            }
            catch (Exception e)
            {
                lentFailed = true;
                ItemForgePlugin.Log.LogError("Не смог взять короб характеристик: " + e);
                return false;
            }
        }

        /// <summary>
        /// Срезает с одолженного короба всё людское.
        ///
        /// Короб берётся целиком, от строки опыта до силы воли, а несёт он на себе и владение
        /// оружием, и навыки игрока, и полезные навыки — потому что в окне персонажа они лежат
        /// в том же ящике. Зверю из этого принадлежит одно: безоружный бой. Когтем и клыком он
        /// и бьёт, и растёт в этом, а убеждать, торговать и разбирать замки не станет никогда —
        /// и полокна занимали строки, в которых у него вечный нуль.
        /// </summary>
        private static void Trim(Transform copy, string wayWillpower, string[] wayMastery)
        {
            try
            {
                if (copy == null) return;

                Transform keepAtt = Branch(copy, wayWillpower != null ? copy.Find(wayWillpower) : null);
                Transform keepMas = null;
                Transform firstRow = null;

                if (wayMastery != null && wayMastery.Length > 0 && wayMastery[0] != null)
                {
                    Transform at = copy.Find(wayMastery[0]);

                    keepMas = Branch(copy, at);
                    if (at != null) firstRow = at.parent;
                }

                int cut = 0;

                if (keepAtt != null)
                {
                    for (int i = 0; i < copy.childCount; i++)
                    {
                        Transform one = copy.GetChild(i);

                        if (one == keepAtt || (keepMas != null && one == keepMas)) continue;

                        one.gameObject.SetActive(false);
                        cut++;
                    }
                }

                // А из владения оружием остаётся одна строка.
                int hidden = 0;

                if (wayMastery != null)
                {
                    for (int i = 1; i < wayMastery.Length; i++)
                    {
                        if (wayMastery[i] == null) continue;

                        Transform at = copy.Find(wayMastery[i]);
                        if (at == null || at.parent == null) continue;

                        // Если строки владения сидят в одном ящике, ящик трогать нельзя: с ним
                        // уйдёт и безоружный. Тогда гасим само число.
                        if (at.parent == firstRow || at.parent == keepMas)
                        {
                            at.gameObject.SetActive(false);
                        }
                        else
                        {
                            at.parent.gameObject.SetActive(false);
                        }

                        hidden++;
                    }
                }

                ItemForgePlugin.Log.LogInfo($"Короб зверя обрезан: полок снято {cut}, "
                    + $"строк владения скрыто {hidden}.");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог обрезать короб: " + e.Message);
            }
        }

        /// <summary>Тот прямой потомок корня, внутри которого лежит эта ветка.</summary>
        private static Transform Branch(Transform root, Transform deep)
        {
            if (root == null || deep == null || deep == root) return null;

            Transform at = deep;

            while (at != null && at.parent != root) at = at.parent;

            return at;
        }

        /// <summary>Re-reads the borrowed block after a point has been spent.</summary>
        private static void Again(UnitAttribute beast)
        {
            try
            {
                InspectPanelManager panel = InspectPanelManager.Instance;
                if (panel == null || beast == null) return;

                float[] six = Beastly.Mine(beast);
                if (six == null || six.Length < 6) return;

                Fill(beast, six);

                Say(panel.s_Strength, six[0]);
                Say(panel.s_Endurance, six[1]);
                Say(panel.s_Agility, six[2]);
                Say(panel.s_Precision, six[3]);
                Say(panel.s_Intelligence, six[4]);
                Say(panel.s_Willpower, six[5]);

                // Своя строка опыта нужна только тогда, когда короб взять не удалось: иначе
                // она вторая. Здесь стояло безусловное «да» — оттого после каждого нажатия
                // «плюс» строка возвращалась и удваивалась.
                Coins(panel, beast, lent == null && lentFailed);

                Lend(panel, beast, six, true);

                Once(panel);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог перечитать короб зверя: " + e.Message);
            }
        }

        /// <summary>Fills that borrowed block with this beast, and wires its buttons to it.</summary>
        private static void Lend(InspectPanelManager panel, UnitAttribute who, float[] six,
            bool ours)
        {
            if (!ours)
            {
                // Человека показываем родным коробом: нам он не нужен.
                if (lent != null) lent.SetActive(false);
                if (native != null) native.SetActive(true);
                if (nativeTitle != null) nativeTitle.SetActive(true);
                return;
            }

            if (!Borrow(panel)) return;

            lent.SetActive(true);
            if (native != null) native.SetActive(false);
            if (nativeTitle != null) nativeTitle.SetActive(false);

            int sum = 0;
            for (int i = 0; i < 6; i++) sum += Mathf.RoundToInt(six[i]);

            if (lentPotential != null)
            {
                lentPotential.text = sum + " / " + Taming.Nature(who);
            }

            if (lentExp != null)
            {
                lentExp.text = Taming.Purse(who).ToString("0");
            }

            for (int i = 0; i < 6; i++)
            {
                if (lentSix[i] != null)
                {
                    lentSix[i].text = Mathf.RoundToInt(six[i]).ToString();
                    Fits.Widen(lentSix[i]);
                }

                if (lentPlus[i] == null) continue;

                int which = i;
                UnitAttribute beast = who;

                float price = Taming.Cost(who, which);

                int top = Blood.Ceiling != null ? Blood.Ceiling.Value : 99;
                bool can = Mathf.RoundToInt(six[i]) < top && Taming.Purse(who) >= price;

                lentPlus[i].interactable = can;
                lentPlus[i].gameObject.SetActive(true);

                lentPlus[i].onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                lentPlus[i].onClick.AddListener(delegate
                {
                    Taming.Spend(beast, which);

                    // Не переоткрываем окно целиком: оно возвращается на вкладку статистики,
                    // и игрок после каждого очка оказывается не там, где нажимал. Достаточно
                    // перечитать сам короб.
                    Again(beast);
                });
            }
        }

        // Шесть кнопок «+», по одной на строку. Заводятся один раз на окно и переезжают от
        // зверя к зверю: окно в игре одно, и плодить их на каждый осмотр незачем.
        private static readonly UnityEngine.UI.Button[] plus = new UnityEngine.UI.Button[6];

        /// <summary>
        /// Puts a «+» beside each of the six, the way the character window has one.
        ///
        /// Меню по правому щелчку для этого не годилось: трата характеристик — дело окна
        /// характеристик, и рядом с числом, которое растёт. У зверя такое окно одно — это,
        /// и кнопки строятся прямо в нём.
        /// </summary>
        private static void Plus(InspectPanelManager panel, UnitAttribute who, bool ours,
            float[] six)
        {
            try
            {
                UnityEngine.UI.Text[] rows =
                {
                    panel.s_Strength, panel.s_Endurance, panel.s_Agility,
                    panel.s_Precision, panel.s_Intelligence, panel.s_Willpower
                };

                for (int i = 0; i < 6; i++)
                {
                    if (rows[i] == null) continue;

                    if (plus[i] == null) plus[i] = Build(rows[i], i);
                    if (plus[i] == null) continue;

                    plus[i].gameObject.SetActive(ours);

                    // Числу уступают место под кнопку — и возвращают, как только в окне снова
                    // человек: окно на всех одно, и чужую строку за собой прибирают.
                    Room(rows[i], ours);

                    if (!ours) continue;

                    int which = i;
                    UnitAttribute beast = who;

                    // Целиком новое событие: «RemoveAllListeners» снимает только навешанное на
                    // ходу, и прежний зверь остался бы в обработчике.
                    plus[i].onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                    plus[i].onClick.AddListener(delegate
                    {
                        Taming.Spend(beast, which);

                        // Окно перерисовываем целиком: числа, цена и опыт меняются разом.
                        if (InspectPanelManager.Instance != null)
                        {
                            InspectPanelManager.Instance.ShowInspectPanel(beast, true);
                        }
                    });

                    // Цена — в подсказке той же строки: место, куда смотрят, решая, куда
                    // вложить.
                    float price = Taming.Cost(who, which);

                    int top = Blood.Ceiling != null ? Blood.Ceiling.Value : 99;
                    bool can = Mathf.RoundToInt(six[i]) < top && Taming.Purse(who) >= price;

                    // Игра гасит человеку «+», когда опыта не хватает, — гасим и зверю.
                    plus[i].interactable = can;

                    if (mark[i] != null)
                    {
                        mark[i].color = can
                            ? new Color(0.55f, 0.78f, 0.35f, 1f)
                            : new Color(0.45f, 0.45f, 0.45f, 1f);
                    }

                    Tip(panel, i, $"Следующее очко: {price:0} опыта");
                }

                Look(rows, ours);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог поставить кнопки зверю: " + e.Message);
            }
        }

        /// <summary>Builds one such button beside a row.</summary>
        private static UnityEngine.UI.Button Build(UnityEngine.UI.Text row, int which)
        {
            if (row == null || row.transform.parent == null) return null;

            GameObject made = new GameObject("ItemForgePlus" + which, typeof(RectTransform));
            made.transform.SetParent(row.transform.parent, false);

            // Вплотную к самому числу, а не к краю короба.
            //
            // Короб шире того места, где стоят цифры: строка «StrengthValue» кончается на
            // четыреста двадцать третьей точке, а короб тянется до пятисотой. Кнопка,
            // прибитая к его краю, вставала на четыреста восьмидесятой — за той границей,
            // которую видно на экране. Оттого её и не было.
            //
            // Поэтому считаем от правого края самой строки: она и есть то место, куда
            // смотрят.
            RectTransform line = row.rectTransform;

            RectTransform rect = made.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(Step, Step);
            rect.anchoredPosition = new Vector2(line.offsetMax.x + 4f, 0f);

            // Подложка прозрачная: она нужна только затем, чтобы нажатие ловилось. У людей в
            // окне характеристик стоит зелёный «+» без рамки, и зверю положен такой же.
            UnityEngine.UI.Image paint = made.AddComponent<UnityEngine.UI.Image>();
            paint.color = new Color(1f, 1f, 1f, 0f);

            UnityEngine.UI.Button press = made.AddComponent<UnityEngine.UI.Button>();
            press.targetGraphic = paint;

            UnityEngine.UI.ColorBlock tint = press.colors;
            tint.normalColor = new Color(1f, 1f, 1f, 1f);
            tint.highlightedColor = new Color(0.8f, 1f, 0.8f, 1f);
            tint.pressedColor = new Color(0.6f, 0.9f, 0.6f, 1f);
            press.colors = tint;

            GameObject word = new GameObject("plus", typeof(RectTransform));
            word.transform.SetParent(made.transform, false);

            RectTransform hold = word.GetComponent<RectTransform>();
            hold.anchorMin = Vector2.zero;
            hold.anchorMax = Vector2.one;
            hold.offsetMin = Vector2.zero;
            hold.offsetMax = Vector2.zero;

            UnityEngine.UI.Text sign = word.AddComponent<UnityEngine.UI.Text>();
            sign.text = "+";
            sign.font = row.font;
            sign.fontSize = Mathf.Max(12, row.fontSize + 2);
            sign.fontStyle = FontStyle.Bold;
            sign.color = new Color(0.55f, 0.78f, 0.35f, 1f);
            sign.alignment = TextAnchor.MiddleCenter;
            sign.raycastTarget = false;

            mark[which] = sign;

            return press;
        }

        // Ширина кнопки и того места, что ей уступает число.
        private const float Step = 18f;

        // Сами знаки «+»: их гасят серым, когда опыта не хватает.
        private static readonly UnityEngine.UI.Text[] mark = new UnityEngine.UI.Text[6];

        // Каким был правый край строки до нас. Окно осмотра одно на всех, и человеку его
        // возвращают в прежнем виде.
        private static readonly Dictionary<UnityEngine.UI.Text, Vector2> edges =
            new Dictionary<UnityEngine.UI.Text, Vector2>();

        /// <summary>Frees a strip at the row's right end, or gives it back.</summary>
        private static void Room(UnityEngine.UI.Text row, bool ours)
        {
            RectTransform rect = row != null ? row.rectTransform : null;
            if (rect == null) return;

            Vector2 was;

            if (!edges.TryGetValue(row, out was))
            {
                was = rect.offsetMax;
                edges[row] = was;
            }

            // «offsetMax» двигает правый край при любой привязке: и у растянутой строки, и у
            // прибитой к краю. Потому считаем от него, а не от ширины.
            Vector2 want = ours ? new Vector2(was.x - (Step + 4f), was.y) : was;

            if (rect.offsetMax != want) rect.offsetMax = want;
        }

        /// <summary>
        /// Leaves exactly one line about experience in the window.
        ///
        /// Ищем по самому тексту, а не по имени поля: окно собрано чужими руками, поля у него
        /// свои, а слово «Опыт» — общее. Всё, что нашлось вне нашей копии, прячем; строку
        /// внутри копии не трогаем, она стоит там же, где у человека.
        /// </summary>
        private static void Once(InspectPanelManager panel)
        {
            try
            {
                if (panel == null) return;

                string word = Word.Value ?? "";
                if (word.Length == 0) return;

                foreach (UnityEngine.UI.Text one in
                    panel.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                {
                    if (one == null || one.text == null) continue;
                    if (one.text.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    // Внутри взятого напрокат короба — наша строка, её и оставляем.
                    if (lent != null && one.transform.IsChildOf(lent.transform)) continue;

                    // И своя строка, если короб взять не удалось, тоже нужна.
                    if (purseRow != null && one.transform.IsChildOf(purseRow.transform))
                    {
                        if (lent == null) continue;
                    }

                    if (one.transform.parent != null
                        && one.transform.parent.gameObject.activeSelf)
                    {
                        one.transform.parent.gameObject.SetActive(false);
                    }
                    else
                    {
                        one.gameObject.SetActive(false);
                    }
                }
            }
            catch
            {
            }
        }

        // Строка опыта: копия строки потенциала, вставленная над ней. Окно в игре одно, и
        // заводится она однажды.
        private static GameObject purseRow;
        private static UnityEngine.UI.Text purseValue;

        /// <summary>Shows what the beast has left to spend, on a line of its own.</summary>
        private static void Coins(InspectPanelManager panel, UnitAttribute who, bool ours)
        {
            try
            {
                if (purseRow == null) Coin(panel);
                if (purseRow == null) return;

                purseRow.SetActive(ours);
                if (!ours || purseValue == null) return;

                purseValue.text = "Опыт: " + Taming.Purse(who).ToString("0");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог показать опыт зверя: " + e.Message);
            }
        }

        /// <summary>
        /// Builds that line by copying the potential one.
        ///
        /// Строить с нуля — гадать про отступы, шрифт и подложку; копия встаёт в кладку окна
        /// сама. Но копировать нужно строку, а не короб: если за «потенциалом» окажется вся
        /// правая половина окна, мы удвоим окно. Потому сперва проверяем, что взяли строку.
        /// </summary>
        private static void Coin(InspectPanelManager panel)
        {
            if (panel.potential == null) return;

            Transform line = panel.potential.transform.parent;
            if (line == null || line.parent == null) return;

            UnityEngine.UI.Text[] inside = line.GetComponentsInChildren<UnityEngine.UI.Text>(true);
            if (inside == null || inside.Length > 4) return;

            foreach (UnityEngine.UI.Text one in inside)
            {
                if (one == panel.s_Strength || one == panel.s_Willpower) return;
            }

            int spot = panel.potential.transform.GetSiblingIndex();

            GameObject copy = UnityEngine.Object.Instantiate(line.gameObject, line.parent);
            copy.name = "ItemForgePurse";

            // Выше заголовка «Атрибуты», как в окне персонажа. Заголовок лежит не в том
            // коробе, где строки, а этажом выше — «ItemForgePurse, Potential, Strength…» в
            // логе показал, что рядом со строками его нет. Потому поднимаемся на этаж и
            // встаём там первыми.
            if (line.parent != null && line.parent.parent != null)
            {
                copy.transform.SetParent(line.parent.parent, false);
            }

            copy.transform.SetSiblingIndex(0);
            copy.SetActive(true);

            if (Taming.Telling != null && Taming.Telling.Value && line.parent != null)
            {
                string kept = "";
                for (int i = 0; i < line.parent.childCount && i < 5; i++)
                {
                    kept += (i > 0 ? ", " : "") + line.parent.GetChild(i).name;
                }

                ItemForgePlugin.Log.LogInfo("Короб атрибутов сверху вниз: " + kept);

                Transform over = line.parent.parent;

                if (over != null)
                {
                    string above = "";
                    for (int i = 0; i < over.childCount && i < 6; i++)
                    {
                        above += (i > 0 ? ", " : "") + over.GetChild(i).name;
                    }

                    ItemForgePlugin.Log.LogInfo("Этажом выше: " + above);
                }
            }

            Transform where = spot < copy.transform.childCount
                ? copy.transform.GetChild(spot)
                : null;

            UnityEngine.UI.Text value = where != null
                ? where.GetComponent<UnityEngine.UI.Text>()
                : null;

            if (value == null)
            {
                UnityEngine.Object.Destroy(copy);

                ItemForgePlugin.Log.LogWarning("Строку опыта скопировать не вышло: потенциал "
                    + "в окне лежит не так, как думалось.");

                return;
            }

            // Подпись в копии гасим, а число растягиваем на всю строку и пишем в него самоё
            // слово: иначе длинный опыт наедет на подпись — ровно то, из-за чего его отсюда и
            // убрали.
            foreach (UnityEngine.UI.Text one in copy.GetComponentsInChildren<UnityEngine.UI.Text>(true))
            {
                if (one != value) one.gameObject.SetActive(false);
            }

            // И значок: строка потенциала принесла с собой росток, а к опыту он не идёт.
            // Гасим картинки на детях, не трогая подложку самой строки.
            foreach (UnityEngine.UI.Image one in
                copy.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (one != null && one.gameObject != copy) one.gameObject.SetActive(false);
            }

            RectTransform box = value.rectTransform;
            box.anchorMin = new Vector2(0f, box.anchorMin.y);
            box.offsetMin = new Vector2(4f, box.offsetMin.y);

            value.horizontalOverflow = HorizontalWrapMode.Overflow;
            value.text = "";

            purseRow = copy;
            purseValue = value;
        }

        // Один раз за запуск: где в точках лежит строка и где кнопка. Промах по кладке видно
        // сразу, а не после третьей сборки.
        private static bool measured;

        private static void Look(UnityEngine.UI.Text[] rows, bool ours)
        {
            if (!ours) return;
            if (Taming.Telling == null || !Taming.Telling.Value) return;
            if (rows[0] == null || plus[0] == null) return;


            RectTransform line = rows[0].rectTransform;
            RectTransform box = plus[0].GetComponent<RectTransform>();
            RectTransform over = rows[0].transform.parent as RectTransform;

            ItemForgePlugin.Log.LogInfo(
                $"Кладка окна зверя: строка «{rows[0].name}» {line.rect.width:0}x{line.rect.height:0}, "
                + $"правый край {line.offsetMax.x:0}; вмещающая «{(over != null ? over.name : "?")}» "
                + $"{(over != null ? over.rect.width : 0f):0}; кнопка "
                + $"{box.anchoredPosition.x:0},{box.anchoredPosition.y:0} "
                + $"{box.sizeDelta.x:0}x{box.sizeDelta.y:0}, сама включена "
                + $"{plus[0].gameObject.activeSelf}, на виду "
                + $"{plus[0].gameObject.activeInHierarchy}; строка опыта "
                + $"{(purseRow != null ? "есть" : "нет")}.");
        }

        private static readonly string[] tips =
            { "strengthTip", "enduranceTip", "agilityTip", "precisionTip",
              "intelligenceTip", "willpowerTip" };

        /// <summary>Writes the price into the row's own tooltip.</summary>
        private static void Tip(InspectPanelManager panel, int which, string said)
        {
            try
            {
                object had = AccessTools.Field(typeof(InspectPanelManager), tips[which])
                    .GetValue(panel);

                UIShowToolTip show = had as UIShowToolTip;
                if (show != null) show.tip = said;
            }
            catch
            {
            }
        }

        /// <summary>Writes the six into the creature's own record, if it has room for them.</summary>
        private static void Fill(UnitAttribute who, float[] six)
        {
            try
            {
                NPCSaveData mind = who.Data as NPCSaveData;
                if (mind == null || mind.humanAttribute == null) return;

                for (int i = 0; i < 6; i++)
                {
                    mind.humanAttribute[i] = Mathf.RoundToInt(six[i]);
                }

                if (!told)
                {
                    told = true;
                    ItemForgePlugin.Log.LogInfo("У зверей запись людская — статы легли в неё, "
                        + "а не поверх окна.");
                }
            }
            catch
            {
            }
        }

        private static bool told;

        private static void Say(Text where, float much)
        {
            if (where != null) where.text = Mathf.RoundToInt(much).ToString();
        }

        /// <summary>Empties a shelf the beast has nothing to put on, and says so.</summary>
        private static void Blank(Text[] rows, Text word)
        {
            if (rows != null)
            {
                foreach (Text row in rows)
                {
                    if (row != null) row.transform.parent.gameObject.SetActive(false);
                }
            }

            if (word == null) return;

            word.transform.parent.gameObject.SetActive(true);
            word.text = gameManager.LocalizedString("None");
        }

        /// <summary>
        /// Draws the beast's traits, the same way the game draws a man's.
        ///
        /// Ячейки берутся игровые и те же самые: свои пришлось бы плодить при каждом осмотре,
        /// а игра их прячет и переиспользует. Оттого и лезем в её список — он закрыт, но
        /// другого нет.
        /// </summary>
        private static void Traits(InspectPanelManager panel, UnitAttribute who)
        {
            try
            {
                List<UITalentInfo> has = who.talentmanger != null
                    ? who.talentmanger.traits
                    : null;

                if (has == null) has = new List<UITalentInfo>();

                List<UITraitSlot> slots = AccessTools.Field(typeof(InspectPanelManager), "traitSlots")
                    .GetValue(panel) as List<UITraitSlot>;

                if (slots == null || panel.traitSlotPrefab == null)
                {
                    Blank(null, panel.traitUnknown);
                    return;
                }

                for (int i = has.Count; i < slots.Count; i++)
                {
                    if (slots[i] != null) slots[i].gameObject.SetActive(false);
                }

                for (int i = 0; i < has.Count; i++)
                {
                    if (slots.Count <= i)
                    {
                        slots.Add(UnityEngine.Object.Instantiate(
                            panel.traitSlotPrefab, panel.traitSlotPrefab.transform.parent));
                    }

                    if (slots[i] == null) continue;

                    slots[i].SetTrait(has[i], true);
                    slots[i].gameObject.SetActive(true);
                }

                if (panel.traitUnknown != null)
                {
                    panel.traitUnknown.transform.parent.gameObject.SetActive(has.Count <= 0);
                    if (has.Count <= 0) panel.traitUnknown.text = gameManager.LocalizedString("None");
                }
            }
            catch
            {
                Blank(null, panel.traitUnknown);
            }
        }

        /// <summary>What a beast has instead of potential: what it grew out of.</summary>
        private static string Grown(UnitAttribute who)
        {
            string size = who.size.ToString();
            int level = who.Data != null ? who.Data.level : 1;

            switch (who.size)
            {
                case UnitSize.Small: size = "мелкий"; break;
                case UnitSize.Medium: size = "средний"; break;
                case UnitSize.Large: size = "крупный"; break;
                case UnitSize.Giant: size = "громадный"; break;
                case UnitSize.Titanic: size = "исполинский"; break;
            }

            return size + ", ур. " + level;
        }
    }

    // Окно осмотра заполняется здесь, и здесь же гасится правый короб. Дописываем после игры:
    // она успевает решить, что зверю показывать нечего, а мы — что есть.
    [HarmonyPatch(typeof(InspectPanelManager), "ShowInspectPanel",
        new[] { typeof(UnitAttribute), typeof(bool) })]
    internal static class ShowInspectPanel_Reading_Patch
    {
        private static void Postfix(InspectPanelManager __instance, UnitAttribute target)
        {
            Reading.Show(__instance, target);
        }
    }
}
