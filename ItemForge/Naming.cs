using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Says on the card what the thing is actually made of, and what it asks of the hand.
    ///
    /// Ярлыки у брони были два: вес («Средняя») и род вещи («Доспехи»). Из какого она
    /// материала — кольчуга это или чешуя, — на карточке не стояло нигде, хотя после того как
    /// защита стала считаться по материалу, это первое, что нужно знать.
    ///
    /// И требование по мастерству жило приписком к строке прочности мелким шрифтом, потому
    /// что своей строки у него нет: игра держит ровно десять строк требований, и все заняты.
    /// Берём ту, что пустует чаще прочих, — требование по уровню, — и ставим мастерство в неё,
    /// рядом с ловкостью и восприятием, куда ему и место.
    /// </summary>
    internal static class Naming
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Classes;
        internal static ConfigEntry<bool> Mastery;
        internal static ConfigEntry<bool> Grey;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Naming", "Enabled", true,
                "Write the material of a piece of armour on its card, beside its weight class. "
                + "Since protection is reckoned by material, «middling» alone says too little: "
                + "mail and scale of the same weight hold a blow quite differently.");

            Classes = config.Bind("Naming", "Classes",
                "Cloth=Ткань,PaddingArmor=Стёганка,LightLeatherArmor=Лёгкая кожа,"
                + "HardLeaterArmor=Твёрдая кожа,SplintArmor=Шинная,ChainArmor=Кольчуга,"
                + "ScaleMail=Чешуя,LamellarArmor=Ламеллярная,HalfPlate=Полулаты,"
                + "PlateArmor=Латы,metalHelmet=Шлем,leatherHelemt=Шлем кожаный",
                "What each class is called on the card.");

            Mastery = config.Bind("Naming", "Mastery", true,
                "Put the demand for weapon mastery among the other demands, instead of "
                + "appending it to the durability line in small print.");

            Grey = config.Bind("Naming", "Grey", true,
                "Draw a demand that is met in plain grey, and only an unmet one in red. The "
                + "game paints met ones green, which makes a card of a thing one can wear look "
                + "like a warning.");
        }

        private static readonly Dictionary<string, string> named =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static string read;

        internal static string Called(ArmourClass kind)
        {
            string written = Classes.Value ?? "";

            if (written != read)
            {
                read = written;
                named.Clear();

                foreach (string one in written.Split(','))
                {
                    int split = one.IndexOf('=');
                    if (split <= 0) continue;

                    named[one.Substring(0, split).Trim()] = one.Substring(split + 1).Trim();
                }
            }

            string said;
            return named.TryGetValue(kind.ToString(), out said) ? said : null;
        }
    }

    // Ярлыки наверху карточки. Дописываем к ним материал.
    [HarmonyPatch(typeof(UIItemTip), "GetType", new Type[] { typeof(UIItemInfo) })]
    internal static class ItemTip_Naming_Patch
    {
        private static void Postfix(UIItemInfo item, List<string> __result)
        {
            if (Naming.Enabled == null || !Naming.Enabled.Value) return;

            try
            {
                UIArmorInfo coat = item as UIArmorInfo;
                if (coat == null || __result == null) return;

                // Пояс носят не за материал: он украшение, и «кольчуга» на нём — ложь.
                if (coat.EquipType == EquipSlotType.belt) return;

                string said = Naming.Called(coat.armourClass);
                if (string.IsNullOrEmpty(said) || __result.Contains(said)) return;

                // Вторым по счёту: после веса, перед родом вещи — «Средняя, Кольчуга, Доспехи».
                if (__result.Count > 0) __result.Insert(1, said);
                else __result.Add(said);
            }
            catch
            {
            }
        }
    }

    // Требования: мастерство ставим в пустующую строку уровня, а зелёный цвет выполненного
    // меняем на серый — карточка носимой вещи не должна выглядеть предупреждением.
    [HarmonyPatch(typeof(UIItemTip), "SetValue")]
    internal static class ItemTip_Demands_Patch
    {
        private static System.Reflection.FieldInfo shown;

        private static readonly Color Plain = new Color(0.62f, 0.62f, 0.62f, 1f);

        /// <summary>Paints the word beside a number in the number's own colour.</summary>
        private static void Tint(UnityEngine.UI.Text value)
        {
            if (value == null || value.transform.parent == null) return;

            foreach (UnityEngine.UI.Text one in
                value.transform.parent.GetComponentsInChildren<UnityEngine.UI.Text>(true))
            {
                if (one != value) one.color = value.color;
            }
        }

        /// <summary>Writes the word beside a number: the label lives in the sibling text.</summary>
        private static void Word(UnityEngine.UI.Text value, string said)
        {
            if (value == null || value.transform.parent == null) return;

            foreach (UnityEngine.UI.Text one in
                value.transform.parent.GetComponentsInChildren<UnityEngine.UI.Text>(true))
            {
                if (one == value) continue;

                one.text = said;
                one.color = value.color;
                one.gameObject.SetActive(true);
                return;
            }
        }

        private static void Postfix(UIItemTip __instance)
        {
            if (Naming.Enabled == null || !Naming.Enabled.Value) return;

            try
            {
                // Пояснения к новому счёту: на строку урона оружия и на прибавки доспеха.
                try
                {
                    if (__instance.damage != null && __instance.damage.Length > 0
                        && __instance.damage[0] != null
                        && __instance.damage[0].transform.parent != null)
                    {
                        Hints.Say(__instance.damage[0].transform.parent.gameObject,
                            Hints.Spread.Value);
                    }
                }
                catch
                {
                }

                UnityEngine.UI.Text[] rows =
                {
                    __instance.strengthRequire, __instance.enduranceRequire,
                    __instance.agilityRequire, __instance.precisionRequire,
                    __instance.intelligenceRequire, __instance.willpowerRequire,
                    __instance.personRequire, __instance.genderRequire, __instance.raceRequire
                };

                string[] shortly =
                {
                    "Сила", "Выносливость", "Ловкость", "Восприятие", "Интеллект",
                    "Сила воли", "Человек", "Пол", "Раса"
                };

                bool first = true;

                for (int i = 0; i < rows.Length; i++)
                {
                    UnityEngine.UI.Text one = rows[i];
                    if (one == null || !one.gameObject.activeSelf) continue;

                    // В самой строке лежит число, а слово — в соседке. Переписываем соседку:
                    // «Требуемая ловкость 10» на каждой строке — это одно и то же слово шесть
                    // раз. Заголовок ставим однажды, строки называем коротко.
                    Word(one, first ? "Требуется:   " + shortly[i] : shortly[i]);
                    first = false;

                    if (Naming.Grey.Value && one.color.g > 0.6f && one.color.r < 0.6f)
                    {
                        one.color = Plain;
                    }

                    // Слово красим тем же цветом, что и число: строка должна читаться целиком.
                    Tint(one);
                }

                if (!Naming.Mastery.Value || __instance.lvRequire == null) return;

                if (shown == null) shown = AccessTools.Field(typeof(UIItemTip), "current");
                if (shown == null) return;

                UIItemInfo thing = shown.GetValue(__instance) as UIItemInfo;
                UIWeaponInfo blade = thing as UIWeaponInfo;
                if (blade == null) return;

                int need = Wield.Rank(blade);
                if (need <= 0) return;

                // Строку уровня занимаем только когда она пустует: своё требование игры
                // важнее нашего.
                if (__instance.lvRequire.gameObject.activeSelf
                    && thing.RequiredLevel > 0) return;

                int have = Wield.Have(blade);

                __instance.lvRequire.text = need.ToString();
                Word(__instance.lvRequire, "Мастерство");
                __instance.lvRequire.color = have >= need
                    ? (Naming.Grey.Value ? Plain : Color.green)
                    : Color.red;

                __instance.lvRequire.gameObject.SetActive(true);

                if (__instance.lvRequire.transform.parent != null)
                {
                    __instance.lvRequire.transform.parent.gameObject.SetActive(true);
                }
            }
            catch
            {
            }
        }
    }
}
