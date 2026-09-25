using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Repair as a shift at the anvil: so many hours, and what they were worth.
    ///
    /// Кнопка «починить» — это не работа, а желание. Работа занимает время, время занимает
    /// человека, и человек за этим временем голодает и устаёт. Отсюда и весь смысл: отряд
    /// встаёт на день, кузнец правит доспех, остальные едят и спят, а к вечеру видно, что
    /// успели.
    ///
    /// Окно для этого у игры есть и рисовать своё не нужно. `CampingManager` — то самое
    /// «ожидание»: ползунок часов, «сейчас», «станет», «+N» и кнопка. Открытое без походного
    /// снаряжения (`campingTool` пуст), оно не укладывает никого в постель, а просто
    /// прокручивает время — ровно смена у наковальни.
    ///
    /// Печь считается отдельно от вещей, и это главное. Уголь горит не «на доспех», а в печи:
    /// восемь часов с одного куска. Ковали девять — сожгли два, семь часов горения осталось, и
    /// следующая вещь чинится на них даром. Печь не гаснет оттого, что мастер сменил заказ.
    /// </summary>
    internal static class Anvil
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> CoalHours;
        internal static ConfigEntry<string> CoalName;
        internal static ConfigEntry<string> Hot;
        internal static ConfigEntry<float> Ember;

        /// <summary>True while the wait window was opened to work, not to rest.</summary>
        internal static bool Working;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Anvil", "Enabled", true,
                "Let repair take hours instead of a click. The forge button opens the waiting "
                + "window, the hours are chosen there, and what those hours were worth is "
                + "shown before they are spent.");

            CoalHours = config.Bind("Anvil", "CoalHours", 8f,
                new ConfigDescription(
                    "How long one lump of coal keeps the furnace lit. It burns in the furnace "
                    + "and not on the piece: work nine hours and two lumps go, seven hours of "
                    + "heat are left, and the next piece is mended on them for nothing.",
                    new AcceptableValueRange<float>(1f, 48f)));

            CoalName = config.Bind("Anvil", "CoalName", "items_Materials_Coal",
                "What the coal is called in the item database.");

            Hot = config.Bind("Anvil", "Hot", "ChainArmor,ScaleMail,LamellarArmor,SplintArmor,"
                + "HalfPlate,PlateArmor",
                "Which kinds of armour need the furnace lit at all. Mail and everything heavier "
                + "is worked hot; cloth, padding and leather are mended cold, by hand, and cost "
                + "no coal. Written as armour class names, separated by commas.");

            Ember = config.Bind("Anvil", "Ember", 0f,
                new ConfigDescription(
                    "How many hours of heat are left in the furnace right now. Written down "
                    + "after every shift so it survives a night and a restart — the coal was "
                    + "paid for, and it goes on burning whether or not anybody is watching.",
                    new AcceptableValueRange<float>(0f, 1000f)));
        }

        // ------------------------------------------------------------------ что чинить

        /// <summary>Every damaged piece the party has, worst first.</summary>
        internal static List<Inventory> Broken()
        {
            List<Inventory> hurt = new List<Inventory>();

            try
            {
                if (PartyManager.instance == null) return hurt;

                foreach (HumaniodUnit man in PartyManager.instance.partyMembers)
                {
                    if (man == null) continue;

                    if (man.equipmentmanger != null && man.equipmentmanger.equipInfos != null)
                    {
                        foreach (EquipInfo slot in man.equipmentmanger.equipInfos)
                        {
                            if (slot != null && slot.IsEquiped()) Maybe(hurt, slot.inventory);
                        }
                    }

                    if (man.items != null && man.items.items != null)
                    {
                        foreach (Inventory bit in man.items.items) Maybe(hurt, bit);
                    }
                }

                // Самое побитое вперёд: мастер берётся за то, что вот-вот развалится.
                hurt.Sort(delegate (Inventory a, Inventory b)
                {
                    float left = a.durability / Mathf.Max(1f, a.itemInfo.durability);
                    float right = b.durability / Mathf.Max(1f, b.itemInfo.durability);
                    return left.CompareTo(right);
                });
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог собрать побитое: " + e);
            }

            return hurt;
        }

        private static void Maybe(List<Inventory> into, Inventory bit)
        {
            if (bit == null || bit.itemInfo == null) return;
            if (bit.itemInfo.noDurability || bit.durability < 0f) return;
            if (!(bit.itemInfo is UIEquipmentInfo)) return;

            float whole = Whole(bit);
            if (bit.durability >= whole - 0.01f) return;

            into.Add(bit);
        }

        /// <summary>How whole this thing can be — our ceiling, never above the template's.</summary>
        internal static float Whole(Inventory bit)
        {
            float cap = Temper.Enabled != null && Temper.Enabled.Value
                ? Temper.Cap(bit) : bit.itemInfo.durability;

            return Mathf.Min(cap, bit.itemInfo.durability);
        }

        // ------------------------------------------------------------------ печь

        private static HashSet<string> hot;
        private static string hotRead;

        /// <summary>True when this piece has to be worked hot.</summary>
        internal static bool NeedsFire(Inventory bit)
        {
            UIArmorInfo coat = bit != null ? bit.itemInfo as UIArmorInfo : null;
            if (coat == null) return false;

            string written = Hot.Value ?? "";

            if (hot == null || written != hotRead)
            {
                hot = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string one in written.Split(','))
                {
                    string name = one.Trim();
                    if (name.Length > 0) hot.Add(name);
                }
                hotRead = written;
            }

            return hot.Contains(coat.armourClass.ToString());
        }

        /// <summary>How much coal a shift of this length needs, given what is still burning.</summary>
        internal static int Coals(float hours, bool fire)
        {
            if (!fire) return 0;

            float short_ = hours - Ember.Value;
            if (short_ <= 0f) return 0;

            return Mathf.CeilToInt(short_ / Mathf.Max(1f, CoalHours.Value));
        }

        /// <summary>How much coal the party actually has to hand.</summary>
        internal static int Stock()
        {
            try
            {
                UIItemInfo coal = UIItemDatabase.Instance.GetByName(CoalName.Value);
                if (coal == null || PartyManager.instance == null) return 0;

                int much = 0;
                foreach (Inventory bit in PartyManager.instance.FindItemsInParty(coal))
                {
                    if (bit != null) much += Mathf.Max(1, bit.stackNum);
                }

                return much;
            }
            catch
            {
                return 0;
            }
        }

        // ------------------------------------------------------------------ смена

        /// <summary>What a shift of this length would come to, without spending anything.</summary>
        internal static string Promise(float hours)
        {
            if (!Enabled.Value) return null;

            try
            {
                float rate = Camp.Speed(Camp.Hands());
                float points = rate * hours;

                List<Inventory> hurt = Broken();
                if (hurt.Count == 0) return "Чинить нечего";

                bool fire = false;
                float left = points;
                int done = 0;

                foreach (Inventory bit in hurt)
                {
                    if (left <= 0f) break;

                    float need = Whole(bit) - bit.durability;
                    if (NeedsFire(bit)) fire = true;

                    if (left >= need) { done++; left -= need; }
                    else left = 0f;
                }

                int coals = Coals(hours, fire);
                int have = Stock();

                string said = $"Починит {points:0} прочности, вещей целиком: {done}";

                // Что уйдёт из припаса — считаем по тем же вещам и той же доле, что и чинить.
                Smithing.Bill bill = Reckoning(hurt, points);
                string stuff = Smithing.Word(bill);
                if (!string.IsNullOrEmpty(stuff)) said += ". Уйдёт: " + stuff;

                if (coals > 0)
                {
                    said += have >= coals
                        ? $". Угля: {coals} (есть {have})"
                        : $". Угля надо {coals}, а есть {have}";
                }
                else if (fire)
                {
                    said += $". В печи ещё {Ember.Value:0.#} ч";
                }

                return said;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>What the whole shift would take out of the party's sacks.</summary>
        private static Smithing.Bill Reckoning(List<Inventory> hurt, float points)
        {
            Smithing.Bill bill = new Smithing.Bill();

            float left = points;

            foreach (Inventory bit in hurt)
            {
                if (left <= 0f) break;

                float need = Whole(bit) - bit.durability;
                float given = Mathf.Min(need, left);

                Smithing.Reckon(bill, bit, given);
                left -= given;
            }

            return bill;
        }

        /// <summary>Works the shift: spends the coal, mends what the hours reach.</summary>
        internal static void Work(float hours)
        {
            if (!Enabled.Value || hours <= 0f) return;

            try
            {
                List<Inventory> hurt = Broken();
                if (hurt.Count == 0)
                {
                    GameController.ShowMessage("Чинить нечего", 2f);
                    return;
                }

                bool fire = false;
                foreach (Inventory bit in hurt)
                {
                    if (NeedsFire(bit)) { fire = true; break; }
                }

                int coals = Coals(hours, fire);

                if (coals > 0)
                {
                    int have = Stock();
                    if (have < coals)
                    {
                        // Не хватило — работаем ровно столько, на сколько хватит жара и угля.
                        float could = Ember.Value + have * CoalHours.Value;
                        if (could <= 0f)
                        {
                            GameController.ShowMessage("Нет угля, печь не растопить", 2f);
                            return;
                        }

                        ItemForgePlugin.Log.LogInfo($"Угля на {hours:0.#} ч не хватило: "
                            + $"есть {have}, в печи {Ember.Value:0.#} ч. Ковали {could:0.#} ч.");

                        hours = could;
                        coals = have;
                    }

                    UIItemInfo coal = UIItemDatabase.Instance.GetByName(CoalName.Value);
                    if (coal != null) PartyManager.instance.RemoveItemInParty(coal, coals);
                }

                // Печь: сожгли столько-то часов горения, отработали столько-то, остаток живёт.
                if (fire)
                {
                    Ember.Value = Mathf.Max(0f,
                        Ember.Value + coals * CoalHours.Value - hours);
                }

                float points = Camp.Speed(Camp.Hands()) * hours;

                // Припас списываем прежде работы: не на что чинить — не о чем и говорить.
                // Уголь к этому времени уже сожжён, и это правильно: печь топили, значит
                // топливо ушло, вышло из работы что-нибудь или нет.
                if (!Smithing.Pay(Reckoning(hurt, points))) return;

                float left = points;
                int done = 0;

                foreach (Inventory bit in hurt)
                {
                    if (left <= 0f) break;

                    float need = Whole(bit) - bit.durability;
                    float given = Mathf.Min(need, left);

                    bit.durability += given;
                    left -= given;

                    bit.onDurabilityChange.Invoke(bit);
                    if (given >= need - 0.01f) done++;
                }

                GameController.ShowMessage(
                    $"Починено {points - left:0} прочности, вещей целиком: {done}", 3f);

                ItemForgePlugin.Log.LogInfo($"Смена у наковальни: {hours:0.#} ч, "
                    + $"{points - left:0} прочности, целиком {done}, угля сожжено {coals}, "
                    + $"в печи осталось {Ember.Value:0.#} ч.");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Смена у наковальни сорвалась: " + e);
            }
        }

        /// <summary>
        /// Opens the waiting window as a shift at the anvil.
        ///
        /// Собрано так, что каждая мелочь падает сама по себе. Один общий перехват уже стоил
        /// нам рабочего ремонта: вызов чужого «OnSliderValueChange» падал на неготовом
        /// ползунке, перехват гасил «Working», и окно открывалось немым — часы есть, подсказки
        /// нет, кузнец ничего не делает. Ошибка была в косметике, а сломалось всё.
        /// </summary>
        internal static void Open()
        {
            try
            {
                CampingManager camp = CampingManager.instance;
                if (camp == null) return;

                // Походное снаряжение от прошлого привала осталось бы полем, и тогда окно
                // уложило бы всех в постели вместо работы.
                try
                {
                    System.Reflection.FieldInfo tool =
                        AccessTools.Field(typeof(CampingManager), "campingTool");

                    if (tool != null) tool.SetValue(camp, null);
                }
                catch (Exception e)
                {
                    ItemForgePlugin.Log.LogWarning("Не смог убрать снаряжение привала: " + e.Message);
                }

                camp.ShowWindow();

                if (camp.window == null || !camp.window.IsOpen)
                {
                    Working = false;
                    ItemForgePlugin.Log.LogWarning("Окно ожидания не открылось.");
                    return;
                }

                // Вот теперь смена началась, и ниже её уже ничем не отменить.
                Working = true;

                try
                {
                    if (CraftManager.Instance != null && CraftManager.Instance.window != null)
                    {
                        CraftManager.Instance.window.Hide();
                    }
                }
                catch (Exception e)
                {
                    ItemForgePlugin.Log.LogWarning("Не смог убрать окно ковки: " + e.Message);
                }

                // Подсказку пишем сами, а не чужим пересчётом: тот падает на ползунке, который
                // ещё не собрался, и до строки дело не доходит.
                Say(camp);
            }
            catch (Exception e)
            {
                Working = false;
                ItemForgePlugin.Log.LogError("Не смог открыть смену у наковальни: " + e);
            }
        }

        /// <summary>Writes what the chosen hours come to, into the window's own line.</summary>
        internal static void Say(CampingManager camp)
        {
            if (camp == null || camp.passTime == null) return;

            try
            {
                int hours = camp.hour;
                if (hours <= 0 && camp.timeSlider != null) hours = (int)camp.timeSlider.value;
                if (hours <= 0) hours = CampingManager.MIN_CAMPING_HOUR;

                string said = Promise(hours);
                if (said == null) return;

                camp.passTime.text = $"+{hours} ч. {said}";
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог написать подсказку смены: " + e.Message);
            }
        }
    }

    // Подтвердили часы — значит смена началась. Работу делаем здесь, до того как побежит
    // время: иначе окно закроется раньше, чем мы успеем что-то сказать.
    [HarmonyPatch(typeof(CampingManager), "ComfirmWait")]
    internal static class ComfirmWait_Anvil_Patch
    {
        private static void Prefix(CampingManager __instance)
        {
            if (!Anvil.Enabled.Value || !Anvil.Working) return;

            int hours = __instance.hour;
            if (hours <= 0 && __instance.timeSlider != null) hours = (int)__instance.timeSlider.value;

            Anvil.Work(hours);
        }
    }

    // Ползунок двинули — надо сказать, что за эти часы выйдет. Пишем в ту же строку, где
    // игра показывает «+N»: она свободна ровно настолько, насколько нам нужно.
    [HarmonyPatch(typeof(CampingManager), "OnSliderValueChange")]
    internal static class OnSlider_Anvil_Patch
    {
        private static void Postfix(CampingManager __instance)
        {
            if (!Anvil.Enabled.Value || !Anvil.Working) return;

            Anvil.Say(__instance);
        }
    }

    // Окно закрылось — смена кончилась, чем бы она ни кончилась.
    [HarmonyPatch(typeof(CampingManager), "CloseWindow")]
    internal static class CloseWindow_Anvil_Patch
    {
        private static void Postfix()
        {
            Anvil.Working = false;
        }
    }
}
