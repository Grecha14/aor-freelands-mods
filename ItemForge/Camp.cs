using System;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ItemForge
{
    /// <summary>
    /// Lets the forge on the wagon do what a forge is for.
    ///
    /// Кузнечная повозка каравана умеет ковать и больше ничего. Починить у неё нельзя: окно
    /// ремонта в игре одно и открывается либо из лавки, либо ремкомплектом, а у повозки нет
    /// кнопки, которая бы его позвала. Отряд возит с собой наковальню и при этом чинит доспех
    /// расходниками из мешка — это и есть та нелепость, которую здесь убирают.
    ///
    /// Кнопка ставится в окно ковки: она клонируется из кнопки «Ковать», чтобы выглядеть как
    /// родная, и зовёт то же самое окно ремонта, что и городская лавка. Мастером встаёт тот,
    /// кто приписан к кузне, а если никто не приписан — сам герой, ровно как это уже решает
    /// сама игра для ковки.
    ///
    /// Чинится при этом не мгновенно и не заказом: заказ оставить негде, склада в дороге нет.
    /// Работа делается на месте, а время прокручивается вперёд — столько, сколько её и заняло.
    /// Скорость берётся родной формулой игры, той самой, по которой считается починка на
    /// привале: пять прочности в час за уровень кузни плюс две за ремесло того, кто работает.
    /// </summary>
    internal static class Camp
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> PerForge;
        internal static ConfigEntry<float> PerSkill;
        internal static ConfigEntry<string> Label;
        internal static ConfigEntry<float> ShiftX;
        internal static ConfigEntry<float> ShiftY;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Camp", "Enabled", true,
                "Put a repair button on the caravan's forge. It can forge and nothing else, so "
                + "a company that carries an anvil still mends its armour out of a sack of kits.");

            PerForge = config.Bind("Camp", "PerForge", 5f,
                new ConfigDescription(
                    "Durability an hour for every level of the forge wagon. Five is the game's "
                    + "own number: it is what a man assigned to repair restores at a camp stop.",
                    new AcceptableValueRange<float>(0f, 100f)));

            PerSkill = config.Bind("Camp", "PerSkill", 2f,
                new ConfigDescription(
                    "And an hour's worth for every rank of smithing the one working has. Also "
                    + "the game's own. Set it to zero to let the wagon alone decide.",
                    new AcceptableValueRange<float>(0f, 100f)));

            Label = config.Bind("Camp", "Label", "Ремонт",
                "What the button says.");

            ShiftX = config.Bind("Camp", "ShiftX", 0f,
                new ConfigDescription(
                    "Where the button sits, sideways from the forge button it was copied from.",
                    new AcceptableValueRange<float>(-2000f, 2000f)));

            ShiftY = config.Bind("Camp", "ShiftY", 45f,
                new ConfigDescription(
                    "And how far above it. The window is not mine to know the shape of, so both "
                    + "of these are here to be nudged until the button sits where it should.",
                    new AcceptableValueRange<float>(-2000f, 2000f)));
        }

        /// <summary>True while the repair window was opened from the wagon, not from a shop.</summary>
        internal static bool AtForge;

        private static HumaniodUnit smith;
        private static GameObject button;

        /// <summary>Remembers who works this forge, and puts the button in place.</summary>
        internal static void Ready(CraftType type, HumaniodUnit worker)
        {
            if (!Enabled.Value) return;

            try
            {
                CraftManager craft = CraftManager.Instance;
                if (craft == null || craft.makeBtn == null) return;

                bool forge = type == CraftType.blacksmith;
                smith = worker;

                if (button == null && forge) Build(craft);

                if (button != null) button.SetActive(forge);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог поставить кнопку ремонта: " + e.Message);
            }
        }

        // Кнопка не рисуется с нуля, а снимается с уже стоящей рядом: так она наследует и вид,
        // и шрифт, и поведение при наведении, и остаётся похожей на родную даже если игра
        // сменит оформление.
        private static void Build(CraftManager craft)
        {
            Button made = craft.makeBtn;

            GameObject copy = UnityEngine.Object.Instantiate(made.gameObject, made.transform.parent);
            copy.name = "ItemForgeRepairButton";

            RectTransform from = made.GetComponent<RectTransform>();
            RectTransform to = copy.GetComponent<RectTransform>();

            if (from != null && to != null)
            {
                to.anchoredPosition = from.anchoredPosition
                    + new Vector2(ShiftX.Value, ShiftY.Value);
            }

            Button press = copy.GetComponent<Button>();
            if (press != null)
            {
                // Целиком новое событие, а не «RemoveAllListeners». Тот снимает только то,
                // что навешено на ходу, а у кнопки ковки обработчик прописан в разметке —
                // и переживает снятие. Оттого нажатие на наш переключатель запускало ковку:
                // сперва чужое дело, потом наше.
                press.onClick = new Button.ButtonClickedEvent();
                press.onClick.AddListener(Open);
                press.interactable = true;
            }

            foreach (Text word in copy.GetComponentsInChildren<Text>(true))
            {
                word.text = Label.Value;
            }

            button = copy;

            ItemForgePlugin.Log.LogInfo("Кнопка ремонта поставлена в окно ковки: «"
                + Label.Value + "», "
                + (to != null ? $"место {to.anchoredPosition.x:0}, {to.anchoredPosition.y:0}" : "места нет")
                + (from != null ? $" (кнопка ковки: {from.anchoredPosition.x:0}, {from.anchoredPosition.y:0})" : ""));
        }

        /// <summary>Opens the game's own repair window, with the forge's man at the anvil.</summary>
        internal static void Open()
        {
            try
            {
                // Молчаливый отказ здесь — худшее, что может быть: кнопка нажимается, ничего
                // не происходит, и понять почему нельзя. Поэтому каждая причина называется.
                if (RepairManager.instance == null)
                {
                    ItemForgePlugin.Log.LogWarning("Кнопка ремонта: окна ремонта в этой сцене нет.");
                    return;
                }

                HumaniodUnit hands = smith != null ? smith : gameManager.currentplayUnit;

                if (hands == null)
                {
                    ItemForgePlugin.Log.LogWarning("Кнопка ремонта: некому встать за наковальню.");
                    return;
                }

                AtForge = true;

                // Смена у наковальни вместо мгновенного окна: часы выбираются, работа идёт,
                // время прокручивается. Родное окно ремонта остаётся для лавки и ремкомплекта.
                if (Anvil.Enabled.Value)
                {
                    Anvil.Open();

                    ItemForgePlugin.Log.LogInfo("Смена у наковальни открыта: мастер "
                        + $"«{(hands.Data != null ? hands.Data.unitname : "?")}», "
                        + $"повозка {Forge()}-го уровня, {Speed(hands):0.#} прочности в час, "
                        + $"в печи {Anvil.Ember.Value:0.#} ч.");
                    return;
                }

                RepairManager.instance.StartRepair(null, hands);

                ItemForgePlugin.Log.LogInfo("Ремонт в кузне открыт: мастер "
                    + $"«{(hands.Data != null ? hands.Data.unitname : "?")}», "
                    + $"повозка {Forge()}-го уровня, {Speed(hands):0.#} прочности в час.");
            }
            catch (Exception e)
            {
                AtForge = false;
                ItemForgePlugin.Log.LogWarning("Не смог открыть ремонт в кузне: " + e.Message);
            }
        }

        /// <summary>Who stands at this anvil: the one assigned to it, or whoever is played.</summary>
        internal static UnitAttribute Hands()
        {
            if (smith != null) return smith;
            return gameManager.currentplayUnit;
        }

        /// <summary>How fast the wagon works, in durability an hour.</summary>
        internal static float Speed(UnitAttribute hands)
        {
            float rate = Forge() * PerForge.Value;

            NPCSaveData mind = hands == null ? null : hands.Data as NPCSaveData;
            if (mind != null) rate += mind.smithing * PerSkill.Value;

            return Mathf.Max(0.5f, rate);
        }

        /// <summary>How long this thing would take at the wagon, or null when it is whole.</summary>
        internal static string Word(Inventory thing)
        {
            if (!Enabled.Value || !AtForge || thing == null || thing.itemInfo == null) return null;

            try
            {
                float whole = thing.itemInfo.durability;
                if (whole <= 0f || thing.itemInfo.noDurability) return null;

                float missing = whole - thing.durability;
                if (missing <= 0.01f) return null;

                float hours = missing / Mathf.Max(0.01f, Speed(smith != null
                    ? (UnitAttribute)smith
                    : gameManager.currentplayUnit));

                return hours >= 1f
                    ? $"ремонт {hours:0.#} ч"
                    : $"ремонт {hours * 60f:0} мин";
            }
            catch
            {
                return null;
            }
        }

        /// <summary>What level the forge wagon has been built up to.</summary>
        internal static int Forge()
        {
            try
            {
                CaravanUIManager caravan = CaravanUIManager.Instance;
                if (caravan == null || !caravan.HasCaravan || caravan.saveData == null) return 0;

                return Mathf.Max(0, caravan.saveData.caravanSmithLevel);
            }
            catch
            {
                return 0;
            }
        }
    }

    // Окно ковки открывается одним способом, и здесь же известно, кто за наковальней: игра
    // сама подставляет приписанного к кузне, а если такого нет — того, кем играют.
    [HarmonyPatch(typeof(CraftManager), "OpenWithType")]
    internal static class OpenWithType_Camp_Patch
    {
        private static void Postfix(CraftType type, HumaniodUnit newCreator)
        {
            Camp.Ready(type, newCreator);
        }
    }

    // Окно ремонта закрылось — значит и кузня кончилась. Флаг снимается здесь, а не по таймеру:
    // иначе следующий ремонт ремкомплектом посчитался бы по скорости повозки.
    [HarmonyPatch(typeof(RepairManager), "EndRepair")]
    internal static class EndRepair_Camp_Patch
    {
        private static void Postfix()
        {
            Camp.AtForge = false;
        }
    }

    // «Сколько это займёт» надо знать до того, как нажал, а не из лога после. Пишем к строке
    // прочности — там же, где живут прочие заметки о ремонте.
    [HarmonyPatch(typeof(UIItemTip), "SetValue")]
    internal static class ItemTip_Camp_Patch
    {
        private static void Postfix(UIItemTip __instance, UISlotBase slot)
        {
            if (!Camp.AtForge) return;

            try
            {
                Text line = __instance.durabilityText;
                if (line == null || !line.gameObject.activeSelf) return;

                Inventory thing = null;

                // Только по ссылке: ячейка панели сравнения создана через «new», и родной
                // «==» Unity считает её пустой.
                UIItemSlot bag = slot as UIItemSlot;
                if ((object)bag != null) thing = bag.inventory;
                else
                {
                    UIEquipSlot worn = slot as UIEquipSlot;
                    if ((object)worn != null) thing = worn.inventory;
                }

                string word = Camp.Word(thing);
                if (word == null) return;

                line.horizontalOverflow = HorizontalWrapMode.Overflow;
                line.text = line.text + "  <size=11>" + word + "</size>";
            }
            catch
            {
            }
        }
    }
}
