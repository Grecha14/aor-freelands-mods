using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Закалка: две вещи одного вида выходят из-под молота разными.
    ///
    /// Smithy пишет в базу основу — сколько весит комплект такого класса на такой ступени и
    /// сколько он держит. Но база одна на всех, и по ней всякая кольчуга Т3 выходит той же
    /// кольчугой Т3, грамм в грамм. Вещей это не даёт: даёт таблицу.
    ///
    /// Здесь на каждую вещь при рождении бросается своя доля — десятая часть в обе стороны на
    /// вес и отдельно столько же на прочность. Брошенное записывается в саму вещь и больше не
    /// меняется: вещь из сундука, из торга, из сохранения остаётся собой.
    ///
    /// Вычет от этого движется сам. Smithy кладёт прочность как «вычет, помноженный на вес»,
    /// значит обратное верно по построению:
    ///
    ///     вычет = прочность / вес
    ///
    /// Оттого вычет — это качество ковки, стена на килограмм. Кираса, вышедшая лёгкой и
    /// крепкой, и держит лучше, и носится легче; вышедшая тяжёлой и хрупкой — брак, и это
    /// видно по числам, а не по подписи. Два броска по десятой доле дают вычету размах около
    /// пятой части, от 0,82 до 1,22.
    ///
    /// Легендарному броска нет вовсе: оно писано по одному и должно быть ровно тем, что в нём
    /// написано.
    /// </summary>
    internal static class Temper
    {
        // Игровой перечень надбавок кончается на 408 (NegativeSpellDamageModifier). Всё, что
        // выше, игре незнакомо: её читатели — разбор по случаям и поиск по типу — незнакомое
        // пропускают молча, а вот сохранение пишет и возвращает его как своё. Оттого это и
        // годится под тайник.
        private const AddonAttribute MarkKilos = (AddonAttribute)9001;
        private const AddonAttribute MarkHolds = (AddonAttribute)9002;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Spread;
        internal static ConfigEntry<float> Plain;
        internal static ConfigEntry<float> Kind;
        internal static ConfigEntry<float> Steady;
        internal static ConfigEntry<float> Master;

        /// <summary>
        /// Мастерство того, кто сейчас у наковальни, или минус один, если вещь никто не ковал:
        /// подобрана, куплена, снята с убитого. Ставится на один предмет и тут же снимается —
        /// рука кузнеца кладётся на то, что он делает, и ни на что больше.
        /// </summary>
        internal static int Hand = -1;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Temper", "Enabled", true,
                "Let each thing come out of the smithy a little its own. Off, and every mail "
                + "shirt of a tier is the same shirt to the gram, which reads as a table rather "
                + "than as a world.");

            Spread = config.Bind("Temper", "Spread", 0.10f,
                new ConfigDescription(
                    "How far one thing may stray from its class reckoning, either way, in weight "
                    + "and again in how much punishment it takes. A tenth each.\n\n"
                    + "The deduction follows from both, being durability over weight, so it "
                    + "strays about a fifth: a cuirass that came out light and tough is better "
                    + "armour than its sister, and that is the thing worth hunting. Narrow this "
                    + "to a fourteenth and the deduction moves about a seventh instead.\n\n"
                    + "Legendary things take no stray at all.",
                    new AcceptableValueRange<float>(0f, 0.5f)));

            Plain = config.Bind("Temper", "Plain", 10f,
                new ConfigDescription(
                    "Up to this much smithing a hand is a hand: the work strays the full amount "
                    + "and skill shows in what he can make, not in how evenly he makes it.",
                    new AcceptableValueRange<float>(0f, 100f)));

            Kind = config.Bind("Temper", "Kind", 50f,
                new ConfigDescription(
                    "From this much smithing no bad work leaves the shop. The throw is the same "
                    + "throw, but the better of the two numbers goes to durability, so the "
                    + "deduction never falls below the reckoning and may rise above it. A man "
                    + "this good has an eye for when a piece is going wrong.",
                    new AcceptableValueRange<float>(0f, 999f)));

            Steady = config.Bind("Temper", "Steady", 100f,
                new ConfigDescription(
                    "The smithing at which a hand no longer wavers at all. Between Plain and "
                    + "this the stray narrows along the Fibonacci rungs, so the first forty "
                    + "levels barely tell and the last twenty tell everything: mastery pays at "
                    + "the top, as the tier ladder does.",
                    new AcceptableValueRange<float>(1f, 999f)));

            Master = config.Bind("Temper", "Master", 0.10f,
                new ConfigDescription(
                    "The master's mark, laid on work from a flawless hand. Armour gains this in "
                    + "durability and sheds it in weight, so its wall climbs by better than a "
                    + "fifth: lighter and tougher is what a master is for. A weapon gains it in "
                    + "both, because a blade does the opposite work and its piercing rides on "
                    + "weight.",
                    new AcceptableValueRange<float>(0f, 0.5f)));
        }

        /// <summary>
        /// Бросить долю, если она ещё не брошена. Дважды одной вещи не бросает: в этом и смысл
        /// записи — вещь, однажды выкованная, дальше только своя.
        /// </summary>
        internal static void Fit(Inventory kit)
        {
            // Рука снимается с наковальни при всяком рождении, годном или нет, чтобы она не
            // легла на следующую вещь, которой кузнец не касался.
            int hand = Hand;
            Hand = -1;

            if (Enabled == null || !Enabled.Value) return;
            if (kit == null || kit.itemInfo == null) return;

            // Куётся снаряжение. Хлеб и склянки пусть весят одинаково.
            if (!(kit.itemInfo is UIEquipmentInfo)) return;

            try
            {
                if (kit.addAttrs == null) kit.addAttrs = new List<AddonAttributes>();
                if (Mark(kit, MarkKilos) > 0f) return;

                // Легендарному ни броска, ни клейма: оно писано по одному и должно быть
                // ровно тем, что в нём написано.
                if ((int)kit.itemInfo.Quality >= (int)UIItemQuality.Legendary)
                {
                    kit.addAttrs.Add(new AddonAttributes(MarkKilos, 1f));
                    kit.addAttrs.Add(new AddonAttributes(MarkHolds, 1f));
                    return;
                }

                float much = Spread.Value * Narrow(hand);

                float kilos = Roll(much);

                // Прочность только вверх: вниз она ломает окончание ремонта.
                float holds = 1f + Mathf.Abs(Roll(much) - 1f);

                // С порога доброй руки брака не бывает: бросок тот же, но старший из двух
                // уходит в прочность. Вычет — это прочность на килограмм, и оттого он больше
                // не опускается ниже меры, а подняться может.
                if (hand >= Kind.Value && holds < kilos)
                {
                    float swap = holds;
                    holds = kilos;
                    kilos = swap;
                }

                // Клеймо мастера. Броня выигрывает от того, что легче: вычет — стена на
                // килограмм. Оружие, напротив, гонит пробитие весом, оттого ему в плюс идёт
                // и то, и другое.
                if (hand >= Steady.Value)
                {
                    float mark = Master.Value;
                    holds = 1f + mark;
                    kilos = kit.itemInfo is UIWeaponInfo ? 1f + mark : 1f - mark;
                }

                kit.addAttrs.Add(new AddonAttributes(MarkKilos, kilos));
                kit.addAttrs.Add(new AddonAttributes(MarkHolds, holds));
            }
            catch
            {
            }
        }

        // Приросты по Фибоначчи на девять ступеней от ровной руки до безупречной.
        private static readonly int[] Rungs = { 1, 1, 2, 3, 5, 8, 13, 21, 34 };

        /// <summary>
        /// Во сколько раз уже разброс против полного при таком мастерстве.
        ///
        /// До порога ровной руки не меняется вовсе, дальше садится по Фибоначчи и на пороге
        /// безупречной обращается в ноль. Оттого первые сорок уровней почти не видны, а
        /// последние двадцать решают всё.
        /// </summary>
        private static float Narrow(int hand)
        {
            // Никто не ковал: подобрано, куплено, снято с убитого. Полный разброс.
            if (hand < 0) return 1f;

            float plain = Plain.Value;
            float steady = Steady.Value;

            if (steady <= plain) return hand >= steady ? 0f : 1f;
            if (hand <= plain) return 1f;
            if (hand >= steady) return 0f;

            float step = (steady - plain) / Rungs.Length;
            if (step <= 0f) return 0f;

            float gone = (hand - plain) / step;

            float whole = 0f;
            foreach (int rung in Rungs) whole += rung;
            if (whole <= 0f) return 1f;

            float done = 0f;

            for (int i = 0; i < Rungs.Length; i++)
            {
                if (gone >= i + 1)
                {
                    done += Rungs[i];
                }
                else if (gone > i)
                {
                    // Внутри ступени — ровно, чтобы уровень не пропадал впустую.
                    done += Rungs[i] * (gone - i);
                    break;
                }
                else
                {
                    break;
                }
            }

            return Mathf.Clamp01(1f - done / whole);
        }

        private static float Roll(float much)
        {
            if (much <= 0f) return 1f;
            return 1f + UnityEngine.Random.Range(-much, much);
        }

        private static float Mark(Inventory kit, AddonAttribute which)
        {
            if (kit == null || kit.addAttrs == null) return 0f;

            for (int i = 0; i < kit.addAttrs.Count; i++)
            {
                AddonAttributes one = kit.addAttrs[i];
                if (one != null && one.type == which) return one.value;
            }

            return 0f;
        }

        /// <summary>Во сколько раз эта вещь тяжелее написанного в базе.</summary>
        internal static float Weighs(Inventory kit)
        {
            if (Enabled == null || !Enabled.Value) return 1f;
            float much = Mark(kit, MarkKilos);
            return much > 0f ? much : 1f;
        }

        /// <summary>
        /// Во сколько раз эта вещь держит больше написанного в базе.
        ///
        /// Ниже единицы не опускается, и вот почему. Игра считает ремонт оконченным по
        /// сравнению с базовой прочностью — `durability >= itemInfo.durability`. Наш потолок
        /// ниже базового означал бы, что условие не наступит никогда: мастер чинит, дни идут,
        /// хозяин голодает, а работа не кончается. Так и вышло в первый же раз.
        ///
        /// Оттого прочность разбрасывается только вверх. Вычет от этого не страдает: он
        /// считается как прочность на килограмм, а вес по-прежнему ходит в обе стороны, и
        /// ладная вещь выходит ладной за счёт лёгкости.
        /// </summary>
        internal static float Holds(Inventory kit)
        {
            if (Enabled == null || !Enabled.Value) return 1f;
            float much = Mark(kit, MarkHolds);
            return much > 1f ? much : 1f;
        }

        /// <summary>
        /// Качество ковки: прочность на килограмм. На него и множится вычет.
        /// </summary>
        /// <summary>
        /// Writes our deduction into a row of the item's own list; true when it took one.
        ///
        /// Ряд берём первый из трёх отменённых — тот, где стояло «Сопротивление режущему».
        /// Подпись у ряда своя, и мы её переписываем; для доспеха этот ряд теперь всегда наш,
        /// поэтому возвращать прежнюю надпись некому и незачем.
        /// </summary>
        internal static bool Guarding(UIItemTip tip, Inventory bit)
        {
            try
            {
                UIArmorInfo coat = bit.itemInfo as UIArmorInfo;
                if (coat == null) return false;

                // Пояс доспехом быть перестал: он теперь того же рода, что кольцо и амулет, —
                // украшение со статами. Защиты на нём не пишем, потому что её на нём и нет.
                if (coat.EquipType == EquipSlotType.belt) return false;

                // Прибавки за подвижность вписываем в саму вещь, если ещё не вписаны: иначе
                // в карточке их не будет — игра списывает их с образца лишь при рождении
                // предмета, а всё, что лежало в мире раньше, осталось без них.
                Garb.Dress(bit);

                int spot = Anatomy.Chest;
                switch (coat.EquipType)
                {
                    case EquipSlotType.head: spot = Anatomy.Head; break;
                    case EquipSlotType.pants: spot = Anatomy.Pants; break;
                }

                float worth = Worth(bit);

                float much = Breach.Mm(coat, 1f, spot, worth);
                if (much <= 0f) return false;

                // Три отменённые строки сопротивлений отдаём под то, что теперь и решает бой:
                // сколько миллиметров надо продавить каждым видом удара. Порядок тот же, каким
                // их считает «Breach.Lead»: рез, обух, укол.
                string[] said = { "Защита от режущего", "Защита от дробящего",
                                  "Защита от колющего" };
                bool drawn = false;

                for (int i = 0; i < 3 && i < tip.damageReduce.Length; i++)
                {
                    UnityEngine.UI.Text value = tip.damageReduce[i];
                    if (value == null || value.transform.parent == null) continue;

                    GameObject row = value.transform.parent.gameObject;

                    foreach (UnityEngine.UI.Text one in
                        row.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                    {
                        if (one != value) { one.text = said[i]; break; }
                    }

                    // Число уже в той же мерке, что и урон: «держит 22» значит, что удар в
                    // двадцать два она остановит, а в тридцать — нет. Сравнивать можно прямо
                    // с уроном оружия, ничего не переводя.
                    value.text = Breach.Must(coat, 1f, spot, i, worth).ToString("0");
                    row.SetActive(true);
                    drawn = true;

                    // Число само по себе ничего не говорит: надо объяснить, что с ним делать.
                    Hints.Say(row, Hints.Wall.Value);
                }

                return drawn;
            }
            catch
            {
                return false;
            }
        }

        internal static float Worth(Inventory kit)
        {
            float kilos = Weighs(kit);
            if (kilos <= 0.01f) return 1f;
            return Holds(kit) / kilos;
        }

        /// <summary>Сколько эта вещь весит на деле.</summary>
        internal static float Kilos(Inventory kit)
        {
            if (kit == null || kit.itemInfo == null) return 0f;
            return kit.itemInfo.weight * Weighs(kit);
        }

        /// <summary>Сколько эта вещь держит, когда цела.</summary>
        internal static float Cap(Inventory kit)
        {
            if (kit == null || kit.itemInfo == null) return 0f;
            float written = kit.itemInfo.durability;
            if (written <= 0f) return written;
            return written * Holds(kit);
        }

        /// <summary>Насколько тяжелее написанного, в килограммах. Для поправок к суммам.</summary>
        internal static float Over(Inventory kit)
        {
            if (kit == null || kit.itemInfo == null) return 0f;
            return kit.itemInfo.weight * (Weighs(kit) - 1f);
        }
    }

    /// <summary>
    /// Вещь рождается здесь. Init зовут на всём новом, и зовут его уже после того, как
    /// EquipmentMaker перебрал надбавки, — оттого метку кладём именно тут, иначе её сотрут.
    /// </summary>
    [HarmonyPatch(typeof(Inventory), "Init")]
    internal static class Init_Temper_Patch
    {
        private static void Postfix(Inventory __instance)
        {
            Temper.Fit(__instance);

            try
            {
                // Init ставит прочность вровень с базовой. У этой вещи своя.
                if (__instance.itemInfo != null
                    && __instance.itemInfo.durability > 0f
                    && !__instance.itemInfo.noDurability)
                {
                    __instance.durability = Temper.Cap(__instance);
                }
            }
            catch
            {
            }
        }
    }

    /// <summary>Вещь, сделанная прямо из образца, минуя Init.</summary>
    [HarmonyPatch(typeof(Inventory), MethodType.Constructor,
        new Type[] { typeof(UIItemInfo), typeof(int), typeof(int), typeof(bool), typeof(int), typeof(float) })]
    internal static class Born_Temper_Patch
    {
        private static void Postfix(Inventory __instance, float dur)
        {
            Temper.Fit(__instance);

            try
            {
                // Минус один означал «целая». Целая по-своему.
                if (dur == -1f
                    && __instance.itemInfo != null
                    && __instance.itemInfo.durability > 0f
                    && !__instance.itemInfo.noDurability)
                {
                    __instance.durability = Temper.Cap(__instance);
                }
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// Вещь из сохранения. Метка приходит вместе с ней и остаётся; а вещь из старого
    /// сохранения, до этого мода, получает свою долю при первой встрече и держит её дальше.
    /// </summary>
    [HarmonyPatch(typeof(Inventory), MethodType.Constructor, new Type[] { typeof(ItemSaveData) })]
    internal static class Loaded_Temper_Patch
    {
        private static void Postfix(Inventory __instance)
        {
            Temper.Fit(__instance);
        }
    }

    /// <summary>
    /// EquipmentMaker перебирает надбавки заново и складывает список с нуля, стирая метку.
    /// Здесь она откладывается и возвращается на место: выкованное однажды не перековывается
    /// оттого, что вещи добавили самоцвет.
    /// </summary>
    [HarmonyPatch(typeof(EquipmentMaker), "EnhanceEquipment")]
    internal static class Enhance_Temper_Patch
    {
        private static void Prefix(Inventory inv, out List<AddonAttributes> __state)
        {
            __state = null;

            try
            {
                if (inv == null || inv.addAttrs == null) return;

                foreach (AddonAttributes one in inv.addAttrs)
                {
                    if (one == null) continue;
                    if ((int)one.type < 9000) continue;

                    if (__state == null) __state = new List<AddonAttributes>();
                    __state.Add(new AddonAttributes(one));
                }
            }
            catch
            {
            }
        }

        private static void Postfix(Inventory inv, List<AddonAttributes> __state)
        {
            if (__state == null) return;

            try
            {
                if (inv.addAttrs == null) inv.addAttrs = new List<AddonAttributes>();

                foreach (AddonAttributes kept in __state)
                {
                    bool there = false;

                    foreach (AddonAttributes one in inv.addAttrs)
                    {
                        if (one != null && one.type == kept.type) { there = true; break; }
                    }

                    if (!there) inv.addAttrs.Add(kept);
                }
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// Доля целого считается от того, сколько держит эта вещь, а не образец. Иначе крепкая
    /// показывалась бы потрёпанной в день, когда её выковали. DurState читает эту же долю,
    /// оттого и он выправляется заодно.
    /// </summary>
    [HarmonyPatch(typeof(Inventory), "DurPercent", MethodType.Getter)]
    internal static class DurPercent_Temper_Patch
    {
        private static void Postfix(Inventory __instance, ref float __result)
        {
            try
            {
                float cap = Temper.Cap(__instance);
                if (cap > 0f) __result = Mathf.Clamp(__instance.durability / cap, 0f, 1f);
            }
            catch
            {
            }
        }
    }

    /// <summary>Цена считалась по доле от базовой прочности. Долю мы сдвинули.</summary>
    [HarmonyPatch(typeof(Inventory), "Value", MethodType.Getter)]
    internal static class Value_Temper_Patch
    {
        private static void Postfix(Inventory __instance, ref int __result)
        {
            try
            {
                if (__result == 0) return;

                float holds = Temper.Holds(__instance);
                if (holds > 0.01f) __result = Mathf.RoundToInt(__result / holds);
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// Починка доводит до потолка этой вещи, а не до базового. Игровой код здесь короток, и
    /// проще переписать его целиком, чем чинить потолок задним числом: зажатое до базового уже
    /// не вернуть.
    /// </summary>
    [HarmonyPatch(typeof(Inventory), "RestoreDurability")]
    internal static class Restore_Temper_Patch
    {
        private static bool Prefix(Inventory __instance, float value, ref bool __result)
        {
            try
            {
                if (__instance.durability == -1f || __instance.itemInfo == null
                    || __instance.itemInfo.noDurability)
                {
                    __result = true;
                    return false;
                }

                float cap = Temper.Cap(__instance);
                if (cap <= 0f) return true;

                int was = __instance.DurState;

                if (__instance.durability < cap) __instance.durability += value;
                __instance.durability = Mathf.Min(__instance.durability, cap);

                __instance.onDurabilityChange.Invoke(__instance);
                if (__instance.DurState != was) __instance.onDurStateChange.Invoke(__instance);

                // «Целая» меряется не нашим потолком, а тем числом, по которому звавший считал
                // долив. Ремонт спрашивает у шаблона: «сколько не хватает до itemInfo.durability»
                // — и доливает ровно столько. У вещи удачной ковки потолок выше шаблонного,
                // и сравнение с потолком не сходилось никогда: слот не уходил из списка, окно
                // ремонта не закрывалось, а дни шли. Берём то из двух, что меньше.
                float whole = Mathf.Min(cap, __instance.itemInfo.durability);

                __result = __instance.durability >= whole - 0.01f;
                return false;
            }
            catch
            {
                return true;
            }
        }
    }

    /// <summary>
    /// «Починить дочиста» в игре означает «поставить прочность вровень с базовой». У этой вещи
    /// своя — иначе крепкую нельзя было бы довести до её собственного целого.
    /// </summary>
    [HarmonyPatch(typeof(Inventory), "SetDurability")]
    internal static class SetDur_Temper_Patch
    {
        private static void Prefix(Inventory __instance, ref float value)
        {
            try
            {
                if (__instance.itemInfo == null) return;

                float written = __instance.itemInfo.durability;
                if (written > 0f && value >= written) value = Temper.Cap(__instance);
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// Нагрузка бойца считается по базовому весу образца. Здесь добавляется разница на всём,
    /// что на нём надето, с теми же поправками за умения, какие кладёт игра.
    /// </summary>
    [HarmonyPatch(typeof(HumaniodUnit), "CountCurrentWeight")]
    internal static class Weight_Temper_Patch
    {
        private static void Postfix(HumaniodUnit __instance)
        {
            try
            {
                EquipmentManager kit = __instance.equipmentmanger;
                if (kit == null) return;

                TalentManager talents = __instance.talentmanger;

                float light = 1f, middling = 1f, heavy = 1f;

                if (talents != null)
                {
                    spell.TalentBase one = talents.FindTalent("Light_Armour_Expert");
                    if (one != null) light = 1f - (one.level + 1) * 0.05f;

                    spell.TalentBase two = talents.FindTalent("Medium_Armour_Expert");
                    if (two != null) middling = 1f - two.level * 0.1f;

                    spell.TalentBase three = talents.FindTalent("Heavy_Armour_Expert");
                    if (three != null) heavy = 1f - three.level * 0.1f;

                    if (talents.ContainTalentAndFit("ArmorMaintainer"))
                    {
                        light -= 0.05f; middling -= 0.05f; heavy -= 0.05f;
                    }

                    if (talents.ContainTalentAndFit("Flexible"))
                    {
                        light += 0.1f; middling += 0.1f; heavy += 0.1f;
                    }

                    if (talents.ContainTrait("SonOfRock"))
                    {
                        light -= 0.1f; middling -= 0.1f; heavy -= 0.1f;
                    }
                }

                float more = 0f;

                if (kit.isInited && kit.equipInfos != null)
                {
                    foreach (EquipInfo worn in kit.equipInfos)
                    {
                        if (worn == null || !worn.IsEquiped()) continue;

                        Inventory bit = worn.inventory;
                        UIEquipmentInfo gear = bit != null ? bit.itemInfo as UIEquipmentInfo : null;
                        if (gear == null || !(gear.weight > 0f)) continue;

                        float over = Temper.Over(bit);
                        if (over == 0f) continue;

                        UIArmorInfo coat = gear as UIArmorInfo;
                        if (coat != null)
                        {
                            switch (coat.armourType)
                            {
                                case ArmourType.Light: more += over * light; break;
                                case ArmourType.Medium: more += over * middling; break;
                                case ArmourType.Heavy: more += over * heavy; break;
                                default: more += over; break;
                            }

                            continue;
                        }

                        UIWeaponInfo arm = gear as UIWeaponInfo;
                        if (arm != null
                            && (arm.WeaponType == WeaponType.range || arm.WeaponType == WeaponType.quiver)
                            && talents != null && talents.ContainTalent("NeverOffHand"))
                        {
                            more += over * (1f - (0.25f + 0.25f * talents.FindTalent("NeverOffHand").level));
                            continue;
                        }

                        more += over;
                    }
                }

                if (kit.standByWeaponInfos != null)
                {
                    float spare = 1f;

                    if (talents != null && talents.ContainTalent("Multi_Weapon_Master"))
                    {
                        spare = 0.75f - 0.25f * talents.FindTalent("Multi_Weapon_Master").level;
                    }

                    foreach (EquipInfo put in kit.standByWeaponInfos)
                    {
                        if (put == null || !put.IsEquiped()) continue;

                        Inventory bit = put.inventory;
                        if (bit == null || !(bit.itemInfo is UIEquipmentInfo)) continue;

                        more += spare * Temper.Over(bit);
                    }
                }

                if (more == 0f) return;

                __instance.currentWeight += more;
            }
            catch
            {
            }
        }
    }

    /// <summary>Сумма веса сумки — та же поправка, только без умений.</summary>
    [HarmonyPatch(typeof(ItemStock), "SumWeight")]
    internal static class Sum_Temper_Patch
    {
        private static void Postfix(ItemStock __instance, ref float __result)
        {
            try
            {
                if (__instance.items == null) return;

                float more = 0f;

                foreach (Inventory bit in __instance.items)
                {
                    if (bit == null || bit.itemInfo == null) continue;
                    more += Temper.Over(bit) * bit.stackNum;
                }

                __result += more;
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// Строка защиты в подсказке показывала проценты игрового сопротивления. Их больше нет:
    /// Anatomy обнуляет сопротивление и ставит на его место вычет. Здесь подсказка говорит то
    /// же, что происходит в бою, — сколько урона стена съедает целиком.
    ///
    /// У дробящего число своё: часть его проходит сквозь любую броню помимо вычета, и стеной
    /// ему служит только остальное.
    /// </summary>
    [HarmonyPatch(typeof(UIItemTip), "SetValue")]
    internal static class Tip_Temper_Patch
    {
        /// <summary>Вещь, чью подсказку сейчас рисуют: подписям она нужна, а им её не дают.</summary>
        internal static Inventory Showing;

        private static void Prefix(UISlotBase slot)
        {
            Showing = null;

            try
            {
                // Только по ссылке. Ячейка панели сравнения создана через «new» и в сцене не
                // стоит, а родной «!=» у Unity отвечает про живой объект, а не про ссылку, —
                // и такую ячейку он считает пустой. Оттого у сравниваемой вещи пропадали и
                // вес, и запас прочности, и строка «Вычет урона»: подписи строились, но им
                // нечего было показывать.
                UIItemSlot bag = slot as UIItemSlot;
                if ((object)bag != null) { Showing = bag.inventory; return; }

                UIEquipSlot worn = slot as UIEquipSlot;
                if ((object)worn != null) Showing = worn.inventory;
            }
            catch
            {
            }
        }

        private static void Postfix(UIItemTip __instance)
        {
            try
            {
                Inventory bit = Showing;
                Showing = null;

                if (bit == null || bit.itemInfo == null) return;

                // Вес и запас прочности показываются из образца, одни на весь вид. У этой вещи
                // они свои, и врать об этом незачем.
                if (__instance.weight != null && bit.itemInfo.weight > 0f)
                {
                    __instance.weight.text = Temper.Kilos(bit).ToString("0.##");
                }

                float cap = Temper.Cap(bit);

                if (__instance.durabilityText != null
                    && __instance.durabilityText.gameObject.activeSelf
                    && cap > 0f)
                {
                    // Запас у вещи не может быть больше её же предела. Он и не был, пока
                    // предел не переписали: старые вещи несут в себе число, посчитанное по
                    // прежней мерке, и в карточке выходило «258,6 из 202,6». Подрезаем по
                    // месту — это не отнимает ничего, чего вещь могла бы лишиться.
                    if (bit.durability > cap) bit.durability = cap;

                    int state = bit.DurState;
                    string tint = "<color=#FFFFFF>";
                    if (state == 1) tint = "<color=#E2B35D>";
                    else if (state == 2) tint = "<color=#B3432B>";
                    else if (state >= 3) tint = "<color=#C30000>";

                    __instance.durabilityText.text = tint + bit.durability.ToString("0.#")
                        + "</color>/" + cap.ToString("0.#");
                }

                // Ряды сопротивления к рубящему, дробящему и колющему показывали проценты,
                // которых в бою больше нет. Стихии оставляем: они снова работают по-игровому.
                //
                // Первый из трёх не прячем, а занимаем своим: у брони в нашем счёте одно
                // число — вычет урона, — и его место здесь, среди свойств вещи, а не ярлыком
                // поверх окна. Поля под него в игре нет: у доспеха есть только проценты
                // сопротивлений, а вычет придуман нами, и писать его больше некуда.
                if (Breach.Enabled != null && Breach.Enabled.Value
                    && bit.itemInfo is UIArmorInfo
                    && __instance.damageReduce != null && __instance.damageReduce.Length >= 3)
                {
                    bool took = Temper.Guarding(__instance, bit);

                    // Заняли все три: у брони теперь три числа — защита от режущего,
                    // дробящего и колющего, — и прятать из них нечего. Прежде первое
                    // занимали под вычет, а два оставшихся гасили; отсюда и выходило, что
                    // в карточке стоит одна строка.
                    for (int i = took ? 3 : 0; i < 3; i++)
                    {
                        UnityEngine.UI.Text row = __instance.damageReduce[i];
                        if (row != null && row.transform.parent != null)
                        {
                            row.transform.parent.gameObject.SetActive(false);
                        }
                    }
                }
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// Цеховая кузница: кузнец за день доводит заказ. Его рука ложится на то, что выходит
    /// из-под молота именно сейчас.
    /// </summary>
    [HarmonyPatch(typeof(TroopSmithyManager), "OnDayPassed")]
    internal static class Smithy_Temper_Patch
    {
        private static void Prefix(TroopSmithyManager __instance)
        {
            try
            {
                Temper.Hand = __instance.staff != null ? __instance.staff.smithing : -1;
            }
            catch
            {
                Temper.Hand = -1;
            }
        }

        private static void Postfix()
        {
            Temper.Hand = -1;
        }
    }

    /// <summary>
    /// Верстак: списание припаса идёт вплотную перед тем, как вещь берётся из ниоткуда. Сама
    /// ковка живёт в сопрограмме, куда с патчем не подобраться по-хорошему, а вот этот шаг —
    /// обычный метод и стоит ровно там, где нужно.
    /// </summary>
    [HarmonyPatch(typeof(CraftManager), "Cost")]
    internal static class Craft_Temper_Patch
    {
        private static void Postfix(CraftManager __instance)
        {
            try
            {
                Temper.Hand = __instance.SkillLevel;
            }
            catch
            {
                Temper.Hand = -1;
            }
        }
    }

    /// <summary>
    /// Подписи под вещью. Игра вешает сюда «уникальное», вид предмета и прочие короткие
    /// пометки; ставим рядом своё.
    ///
    /// У брони — вычет, одной строкой и без разбора по типам: стена одна на всех, и число под
    /// значком «рубящее» читалось бы как «вычет против рубящего», чего как раз не бывает.
    ///
    /// У дробящего оружия — доля, идущая мимо стены. Она стоит здесь, а не у брони, потому что
    /// это свойство удара: молот не встречает стену пониже, он проносит часть удара сквозь
    /// железо, и остальное упирается в полный вычет.
    /// </summary>
    [HarmonyPatch(typeof(UIItemTip), "GetLabels")]
    internal static class Labels_Temper_Patch
    {
        private static void Postfix(List<string> __result)
        {
            if (__result == null) return;
            if (Breach.Enabled == null || !Breach.Enabled.Value) return;

            try
            {
                Inventory bit = Tip_Temper_Patch.Showing;
                if (bit == null || bit.itemInfo == null) return;

                // Доспеху ярлык больше не нужен: вычет урона стоит строкой среди свойств самой
                // вещи — там же, где всё остальное, что о ней известно. Ярлык поверх окна был
                // временной мерой, пока для числа не нашлось своего места.
                if (bit.itemInfo is UIArmorInfo) return;

                UIWeaponInfo blade = bit.itemInfo as UIWeaponInfo;

                if (blade != null && Breach.Crushes(blade))
                {
                    string said = Breach.Passage();
                    if (!string.IsNullOrEmpty(said)) __result.Add(said);
                }
            }
            catch
            {
            }
        }
    }
}
