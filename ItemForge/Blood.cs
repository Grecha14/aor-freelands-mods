using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// What a man is born with: his kind and his sex.
    ///
    /// Породы в этой игре различаются картинкой и парой строк в описании. Брать орка вместо
    /// человека незачем: дерутся они одинаково, учатся одинаково, и единственное, что меняется,
    /// — лицо. Здесь у каждой породы своё.
    ///
    /// Человек умён и переимчив: учится быстрее всех и заклинание в его руках сильнее.
    /// Орк огромен: сила сама по себе, шкура сама по себе, и рубит он тяжелее прочих.
    /// Эльф лёгок и быстр: с луком ему равных нет, и рука у него скорее любой другой.
    /// Гном коренаст и жилист: кость толстая, дыхание долгое, а в кузне ему везёт.
    ///
    /// И пол: мужчина крупнее и выносливее, женщина ловчее и твёрже духом. Обе прибавки
    /// вешаются как прибавки — видны в листе и входят во всё, что из них считается.
    /// </summary>
    internal static class Blood
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Races;
        internal static ConfigEntry<string> Men;
        internal static ConfigEntry<string> Women;
        internal static ConfigEntry<int> Ceiling;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Blood", "Enabled", true,
                "Give the kinds and the two sexes something of their own. Without this a kind is "
                + "a face and nothing else, and choosing one over another is choosing a portrait.");

            Races = config.Bind("Blood", "Races",
                "human=learn 0.15|spell 0.30;"
                + "orc=strength 15|sharp 0.15|blunt 0.15|bone 15;"
                + "elf=swing 0.15|bows 15|brew 0.10|spare 0.10;"
                + "dwarf=bone 20|effort 0.20|forge 0.10",
                "What each kind is born with, written as kind=term value|term value, kinds "
                + "separated by semicolons.\n\n"
                + "The terms: strength, endurance, agility, precision, intelligence and "
                + "willpower add whole points and show in the sheet; spell is a share added to "
                + "the force of spells; sharp and blunt are shares added to damage of that "
                + "kind; swing is a share on how fast the arms move; effort is a share taken "
                + "off everything that costs stamina; bone is armour under the skin, reckoned "
                + "as that many points of endurance would give; learn is a share added to "
                + "every kind of experience; bows is mastery added with any bow but the "
                + "crossbow; brew is a share added to the chance a cauldron gives anything at "
                + "all and spare a share added to the chance its reagents survive; "
                + "forge is a share added to the roll that colours a thing made at "
                + "the anvil.");

            Men = config.Bind("Blood", "Men", "strength 10|endurance 10",
                "And what a man is born with on top of his kind: heavier of frame and longer of "
                + "wind.");

            Women = config.Bind("Blood", "Women", "agility 10|willpower 10",
                "The same for a woman: quicker of hand and harder to break.");

            Ceiling = config.Bind("Blood", "Ceiling", 999,
                new ConfigDescription(
                    "The highest an attribute may be raised to. The game stops at ninety nine "
                    + "and clamps everything above a hundred back down, which also means the "
                    + "gifts of kind and sex would be quietly eaten by that clamp and written "
                    + "into the stored number besides. Both are lifted here: the stored number "
                    + "is left exactly as it was, and the ceiling is what this says.",
                    new AcceptableValueRange<int>(99, 9999)));

            Telling = config.Bind("Blood", "Telling", false,
                "Write out the first few readings of blood.");
        }

        // ----------------------------------------------------------------- чтение

        private static readonly Dictionary<string, Dictionary<string, float>> byRace =
            new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, float> byMen =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, float> byWomen =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        private static string racesRead;
        private static string menRead;
        private static string womenRead;

        private static void Terms(string written, Dictionary<string, float> into)
        {
            into.Clear();

            foreach (string one in (written ?? "").Split('|'))
            {
                string bit = one.Trim();
                if (bit.Length == 0) continue;

                int gap = bit.LastIndexOf(' ');
                if (gap <= 0) continue;

                float much;
                if (!float.TryParse(bit.Substring(gap + 1).Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out much)) continue;

                into[bit.Substring(0, gap).Trim()] = much;
            }
        }

        private static void Read()
        {
            string written = Races != null ? (Races.Value ?? "") : "";

            if (written != racesRead)
            {
                racesRead = written;
                byRace.Clear();

                foreach (string one in written.Split(';'))
                {
                    int split = one.IndexOf('=');
                    if (split <= 0) continue;

                    Dictionary<string, float> mine = new Dictionary<string, float>(
                        StringComparer.OrdinalIgnoreCase);

                    Terms(one.Substring(split + 1), mine);

                    byRace[one.Substring(0, split).Trim()] = mine;
                }
            }

            string male = Men != null ? (Men.Value ?? "") : "";
            if (male != menRead) { menRead = male; Terms(male, byMen); }

            string female = Women != null ? (Women.Value ?? "") : "";
            if (female != womenRead) { womenRead = female; Terms(female, byWomen); }
        }

        /// <summary>What this one's blood is worth on this count.</summary>
        internal static float Of(UnitAttribute who, string term)
        {
            if (Enabled == null || !Enabled.Value || who == null) return 0f;

            try
            {
                HumaniodUnit man = who as HumaniodUnit;
                if (man == null || man.Data == null) return 0f;

                Read();

                float much = 0f;

                if (who.info != null)
                {
                    Dictionary<string, float> mine;

                    if (byRace.TryGetValue(who.info.race.ToString(), out mine))
                    {
                        float got;
                        if (mine.TryGetValue(term, out got)) much += got;
                    }
                }

                Dictionary<string, float> sex = man.Data.gender == UnitGender.female
                    ? byWomen : byMen;

                float also;
                if (sex.TryGetValue(term, out also)) much += also;

                return much;
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>Whole points of an attribute, which is what the sheet shows.</summary>
        internal static int Points(UnitAttribute who, string term)
        {
            return Mathf.RoundToInt(Of(who, term));
        }

        /// <summary>Мастерство, прибавленное породой к этому оружию в руках.</summary>
        internal static int Mastery(UnitAttribute who, UIWeaponInfo blade)
        {
            if (blade == null) return 0;

            try
            {
                // Луку — да, арбалету — нет: у арбалета своя наука, и она не в руке.
                if (blade.WeaponType != WeaponType.range) return 0;

                string kind = blade.weaponClass.ToString();

                if (kind.IndexOf("bow", StringComparison.OrdinalIgnoreCase) < 0) return 0;
                if (kind.IndexOf("crossbow", StringComparison.OrdinalIgnoreCase) >= 0) return 0;

                return Points(who, "bows");
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>Кто сейчас стоит у верстака и за каким делом.</summary>
        internal static HumaniodUnit Bench;
        internal static CraftType Craft;

        private static int told;

        /// <summary>Всё, что порода делает поверх характеристик, — на пересчёте.</summary>
        internal static void Tell(HumaniodUnit who)
        {
            if (Enabled == null || !Enabled.Value || who == null) return;

            try
            {
                float spell = Of(who, "spell");
                if (spell != 0f) who.MagicDamageMD += spell;

                float effort = Of(who, "effort");
                if (effort != 0f) who.EPsave = Mathf.Min(0.8f, who.EPsave + effort);

                float swing = Of(who, "swing");
                float sharp = Of(who, "sharp");
                float blunt = Of(who, "blunt");

                if (who.weapons != null && (swing != 0f || sharp != 0f || blunt != 0f))
                {
                    foreach (Weapon arm in who.weapons)
                    {
                        if (arm == null) continue;

                        if (swing != 0f) arm.attackSpeed *= Mathf.Max(0.1f, 1f + swing);

                        if (arm.damage == null) continue;

                        foreach (KeyValuePair<DamageType, Damage> one in arm.damage)
                        {
                            if (one.Value == null) continue;

                            float much = one.Key == DamageType.sharp ? sharp
                                : (one.Key == DamageType.blunt ? blunt : 0f);

                            if (much == 0f) continue;

                            one.Value.minDamage *= 1f + much;
                            one.Value.maxDamage *= 1f + much;
                            one.Value.currentDamage *= 1f + much;
                        }
                    }
                }

                if (Telling.Value && told < 10)
                {
                    told++;
                    ItemForgePlugin.Log.LogInfo($"Кровь «{who.Data?.unitname}» "
                        + $"({who.info?.race}, {who.Data?.gender}): сила +{Points(who, "strength")}, "
                        + $"выносливость +{Points(who, "endurance")}, ловкость "
                        + $"+{Points(who, "agility")}, воля +{Points(who, "willpower")}, "
                        + $"заклинание +{spell * 100f:0}%, кость {Of(who, "bone"):0}.");
                }
            }
            catch
            {
            }
        }
    }

    // Усечение характеристик.
    //
    // Игра здесь делает две вещи разом, и обе нам мешают. Она рубит всё выше сотни — потолок,
    // который мы поднимаем, — и пишет усечённое обратно в хранимое число, читая его при этом
    // через свойство. А свойство у нас теперь отдаёт хранимое вместе с прибавкой породы: стало
    // быть, прибавка на каждом пересчёте вливалась бы в саму характеристику и копилась там,
    // покуда не упёрлась бы в ту же сотню.
    //
    // Оттого запоминаем хранимое до усечения и возвращаем его после, усечённым по своему
    // потолку. Всё прочее, что игра усекает в этом же вызове, остаётся при ней.
    [HarmonyPatch(typeof(HumaniodUnit), "ClampUnitAttribute")]
    internal static class Clamp_Blood_Patch
    {
        private static void Prefix(HumaniodUnit __instance, ref int[] __state)
        {
            __state = null;

            try
            {
                if (__instance == null || __instance.Data == null) return;

                __state = new[]
                {
                    __instance.Data.strength, __instance.Data.endurance,
                    __instance.Data.agility, __instance.Data.precision,
                    __instance.Data.intelligence, __instance.Data.willpower
                };
            }
            catch
            {
            }
        }

        private static void Postfix(HumaniodUnit __instance, int[] __state)
        {
            if (__state == null || __state.Length < 6) return;

            try
            {
                int top = Blood.Ceiling != null ? Blood.Ceiling.Value : 99;

                __instance.Data.strength = Mathf.Clamp(__state[0], 1, top);
                __instance.Data.endurance = Mathf.Clamp(__state[1], 1, top);
                __instance.Data.agility = Mathf.Clamp(__state[2], 1, top);
                __instance.Data.precision = Mathf.Clamp(__state[3], 1, top);
                __instance.Data.intelligence = Mathf.Clamp(__state[4], 1, top);
                __instance.Data.willpower = Mathf.Clamp(__state[5], 1, top);
            }
            catch
            {
            }
        }
    }

    // Кнопка «плюс» и сама трата: у игры и там и там стоит девяносто девять.
    [HarmonyPatch(typeof(UIApplyUnitAttribute), "IncreaseAtriibuteValue")]
    internal static class Increase_Blood_Patch
    {
        private static bool Prefix(UIApplyUnitAttribute __instance, int index)
        {
            try
            {
                HumaniodUnit who = __instance.Unit as HumaniodUnit;
                if (who == null || who.Data == null || who.Data.humanAttribute == null) return true;

                int top = Blood.Ceiling != null ? Blood.Ceiling.Value : 99;
                if (top <= 99) return true;

                int price = who.Data.humanAttribute.GetNextLVExp(index);
                if (who.Data.exp < price) return false;

                if (who.Data.humanAttribute[index] < top)
                {
                    who.Data.exp -= price;
                    who.Data.humanAttribute[index]++;
                    who.Data.humanAttribute.attEXP[index] = 0f;
                }

                __instance.ShowAddButtons();
                who.UpdateAttribute();
                who.Data.SetLevel();

                return false;
            }
            catch
            {
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(UIApplyUnitAttribute), "ShowAddButtons")]
    internal static class Buttons_Blood_Patch
    {
        private static bool Prefix(UIApplyUnitAttribute __instance)
        {
            try
            {
                HumaniodUnit who = __instance.Unit as HumaniodUnit;
                if (who == null || who.Data == null || who.Data.humanAttribute == null) return true;
                if (__instance.addBut == null || EquipSlotManager.instance == null) return true;

                int top = Blood.Ceiling != null ? Blood.Ceiling.Value : 99;
                if (top <= 99) return true;

                for (int i = 0; i < __instance.addBut.Length; i++)
                {
                    bool full = who.Data.humanAttribute[i] >= top;
                    int price = who.Data.humanAttribute.GetNextLVExp(i);

                    __instance.addBut[i].interactable = !full && who.Data.exp >= price
                        && EquipSlotManager.instance.canSpendPoint;
                }

                return false;
            }
            catch
            {
                return true;
            }
        }
    }

    // Шесть характеристик: прибавка породы и пола входит в само число, а не стоит рядом с ним.
    // Оттого её видно в листе и с неё считается всё прочее — здоровье, урон, вес, шаг.
    [HarmonyPatch(typeof(HumaniodUnit), "get_Strength")]
    internal static class Strength_Blood_Patch
    {
        private static void Postfix(HumaniodUnit __instance, ref int __result)
        {
            try { __result += Blood.Points(__instance, "strength"); } catch { }
        }
    }

    [HarmonyPatch(typeof(HumaniodUnit), "get_Endurance")]
    internal static class Endurance_Blood_Patch
    {
        private static void Postfix(HumaniodUnit __instance, ref int __result)
        {
            try { __result += Blood.Points(__instance, "endurance"); } catch { }
        }
    }

    [HarmonyPatch(typeof(HumaniodUnit), "get_Agility")]
    internal static class Agility_Blood_Patch
    {
        private static void Postfix(HumaniodUnit __instance, ref int __result)
        {
            try { __result += Blood.Points(__instance, "agility"); } catch { }
        }
    }

    [HarmonyPatch(typeof(HumaniodUnit), "get_Precision")]
    internal static class Precision_Blood_Patch
    {
        private static void Postfix(HumaniodUnit __instance, ref int __result)
        {
            try { __result += Blood.Points(__instance, "precision"); } catch { }
        }
    }

    [HarmonyPatch(typeof(HumaniodUnit), "get_Intelligence")]
    internal static class Intelligence_Blood_Patch
    {
        private static void Postfix(HumaniodUnit __instance, ref int __result)
        {
            try { __result += Blood.Points(__instance, "intelligence"); } catch { }
        }
    }

    [HarmonyPatch(typeof(HumaniodUnit), "get_Willpower")]
    internal static class Willpower_Blood_Patch
    {
        private static void Postfix(HumaniodUnit __instance, ref int __result)
        {
            try { __result += Blood.Points(__instance, "willpower"); } catch { }
        }
    }

    // И всё, что не характеристика: сила заклинания, дыхание, замах, тяжесть удара.
    [HarmonyPatch(typeof(HumaniodUnit), "WriteUnitAttribute")]
    internal static class Write_Blood_Patch
    {
        private static void Postfix(HumaniodUnit __instance)
        {
            try { Blood.Tell(__instance); } catch { }
        }
    }

    // Кто взялся за работу. Ловим на расчёте стоимости: к тому мигу, когда вещь ложится в
    // сумку, окно ремесла может быть уже закрыто, а порода мастера нужна именно тогда.
    [HarmonyPatch(typeof(CraftManager), "Cost")]
    internal static class Cost_Blood_Patch
    {
        private static void Postfix()
        {
            try
            {
                CraftManager bench = CraftManager.Instance;

                // Поля закрыты, и это разумно с их стороны: читаем отражением, один раз найдя.
                Blood.Bench = bench != null
                    ? AccessTools.Field(typeof(CraftManager), "creator")
                        ?.GetValue(bench) as HumaniodUnit
                    : null;

                object kind = bench != null
                    ? AccessTools.Field(typeof(CraftManager), "craftType")?.GetValue(bench)
                    : null;

                Blood.Craft = kind is CraftType ? (CraftType)kind : CraftType.none;
            }
            catch
            {
                Blood.Bench = null;
            }
        }
    }

    // Переимчивость: человек берёт от всякой науки больше прочих.
    [HarmonyPatch(typeof(HumaniodUnit), "GainExp")]
    internal static class GainExp_Blood_Patch
    {
        private static void Prefix(HumaniodUnit __instance, ref int exp)
        {
            try
            {
                float more = Blood.Of(__instance, "learn");
                if (more != 0f && exp > 0) exp = Mathf.RoundToInt(exp * (1f + more));
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(HumaniodUnit), "GainProfessionExp")]
    internal static class GainCraft_Blood_Patch
    {
        private static void Prefix(HumaniodUnit __instance, ref float exp)
        {
            try
            {
                float more = Blood.Of(__instance, "learn");
                if (more != 0f && exp > 0f) exp *= 1f + more;
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(HumaniodUnit), "GainWeaponMasteryExp", new[] { typeof(int) })]
    internal static class GainMastery_Blood_Patch
    {
        private static void Prefix(HumaniodUnit __instance, ref int exp)
        {
            try
            {
                float more = Blood.Of(__instance, "learn");
                if (more != 0f && exp > 0) exp = Mathf.RoundToInt(exp * (1f + more));
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// Третья цифра.
    ///
    /// Поля под характеристики в этой игре считаны на два знака: выше девяноста девяти она
    /// подняться не давала, и мерить их шире было незачем. Потолок мы подняли — и сотня стала
    /// показываться десятком. Перенос по словам уносит третий знак на вторую строку, а вторая
    /// обрезана по высоте поля: число не обрублено, оно спрятано.
    ///
    /// Оттого строке разрешается выйти за поле. Она короткая, соседей не задевает, а
    /// выравнивание остаётся прежним — с середины поля лишний знак расходится в обе стороны
    /// поровну.
    /// </summary>
    internal static class Fits
    {
        internal static void Widen(UnityEngine.UI.Text one)
        {
            if (one == null) return;

            if (one.horizontalOverflow != HorizontalWrapMode.Overflow)
            {
                one.horizontalOverflow = HorizontalWrapMode.Overflow;
            }

            if (one.verticalOverflow != VerticalWrapMode.Overflow)
            {
                one.verticalOverflow = VerticalWrapMode.Overflow;
            }
        }

        internal static void Widen(params UnityEngine.UI.Text[] all)
        {
            if (all == null) return;

            foreach (UnityEngine.UI.Text one in all) Widen(one);
        }
    }

    // Окно персонажа.
    [HarmonyPatch(typeof(UIApplyUnitAttribute), "UpdateInfoWindow")]
    internal static class Window_Blood_Patch
    {
        private static void Postfix(UIApplyUnitAttribute __instance)
        {
            try
            {
                if (__instance == null) return;

                Fits.Widen(__instance.strength, __instance.endurance, __instance.agility,
                    __instance.precision, __instance.intelligence, __instance.willpower,
                    __instance.potential, __instance.remainPointText);
            }
            catch
            {
            }
        }
    }

    // И окно осмотра: числа в нём те же.
    [HarmonyPatch(typeof(InspectPanelManager), "ShowInspectPanel",
        new[] { typeof(UnitAttribute), typeof(bool) })]
    internal static class Inspect_Blood_Patch
    {
        private static void Postfix(InspectPanelManager __instance)
        {
            try
            {
                if (__instance == null) return;

                Fits.Widen(__instance.s_Strength, __instance.s_Endurance,
                    __instance.s_Agility, __instance.s_Precision,
                    __instance.s_Intelligence, __instance.s_Willpower);
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// Цена очка там, где игра назвала её числом внутри себя.
    ///
    /// У этой игры десяток правил вида «столько-то за очко», и писаны они все под потолок в
    /// девяносто девять: там больше удвоения не выходило никогда. С потолком в девять сотен
    /// такое правило перестаёт быть прибавкой — при ловкости в сотню человек бьёт вдвое чаще,
    /// при трёх сотнях вчетверо, а дальше счёт идёт на то, сколько ударов помещается в кадр.
    ///
    /// Своего числа сюда не подставить снаружи: оно вписано в саму строку пересчёта. Оттого
    /// меняются сами строки — ровно те, где цена названа, — и вместо числа в них встаёт спрос
    /// к нашей настройке. Правило остаётся игровым и работает игровым порядком, просто цена в
    /// нём теперь наша, и повернуть её можно не перезапуская игру.
    ///
    /// Всё прочее в пересчёте не тронуто, включая прибавку боевого монаха: она ложится в то же
    /// поле замаха строкой ниже, но число у неё своё, и под замену не подходит.
    /// </summary>
    internal static class Nimble
    {
        internal static ConfigEntry<float> Swing;
        internal static ConfigEntry<float> Crit;

        internal static void Bind(ConfigFile config)
        {
            Swing = config.Bind("Nimble", "Swing", 0.005f,
                new ConfigDescription(
                    "What one point of agility adds to the rate of striking, as a share. The "
                    + "game writes a hundredth into its own reckoning; this is half of that.",
                    new AcceptableValueRange<float>(0f, 0.05f)));

            Crit = config.Bind("Nimble", "Crit", 0.5f,
                new ConfigDescription(
                    "How many points of critical chance one point of perception buys. The game "
                    + "writes a whole one; this is half.",
                    new AcceptableValueRange<float>(0f, 5f)));
        }

        internal static float Swings()
        {
            return Swing != null ? Swing.Value : 0.01f;
        }

        internal static float Crits()
        {
            return Crit != null ? Crit.Value : 1f;
        }

        /// <summary>Цена ума в силе заклинания. Живёт в «Wits», спрашивается отсюда.</summary>
        internal static float Spell()
        {
            return Wits.PerPoint != null ? Wits.PerPoint.Value : 0.01f;
        }
    }

    // Те самые строки.
    [HarmonyPatch(typeof(HumaniodUnit), "WriteUnitAttribute")]
    internal static class Swing_Nimble_Patch
    {
        private static bool told;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            List<CodeInstruction> all = new List<CodeInstruction>(code);

            // Поле, число, которое в нём стоит, и наш спрос ему на замену.
            string[] fields = { "AttackSpeedMD", "MagicDamageMD" };
            string[] asks = { "Swings", "Spell" };

            FieldInfo sharp = AccessTools.Field(typeof(UnitAttribute), "CriMD");
            MethodInfo crits = AccessTools.Method(typeof(Nimble), "Crits");

            int replaced = 0, inserted = 0;

            // Идём с конца: вставка сдвигает всё, что после неё.
            for (int i = all.Count - 1; i >= 0; i--)
            {
                // Там, где число названо: «… × 0.01; сложить; записать». Меняем число.
                if (i + 3 < all.Count
                    && all[i].opcode == OpCodes.Ldc_R4
                    && all[i].operand is float
                    && Mathf.Abs((float)all[i].operand - 0.01f) < 0.0000001f
                    && all[i + 1].opcode == OpCodes.Mul
                    && all[i + 2].opcode == OpCodes.Add
                    && all[i + 3].opcode == OpCodes.Stfld
                    && all[i + 3].operand is FieldInfo)
                {
                    FieldInfo put = (FieldInfo)all[i + 3].operand;

                    for (int k = 0; k < fields.Length; k++)
                    {
                        if (put != AccessTools.Field(typeof(UnitAttribute), fields[k])) continue;

                        MethodInfo ask = AccessTools.Method(typeof(Nimble), asks[k]);
                        if (ask == null) break;

                        all[i] = new CodeInstruction(OpCodes.Call, ask);
                        replaced++;
                        break;
                    }

                    continue;
                }

                // А где не названо — очко идёт за очко, — вставляем своё перед сложением.
                if (sharp != null && crits != null
                    && i >= 1
                    && all[i].opcode == OpCodes.Stfld
                    && all[i].operand is FieldInfo
                    && (FieldInfo)all[i].operand == sharp
                    && all[i - 1].opcode == OpCodes.Add)
                {
                    all.Insert(i - 1, new CodeInstruction(OpCodes.Mul));
                    all.Insert(i - 1, new CodeInstruction(OpCodes.Call, crits));
                    inserted++;
                }
            }

            // Говорим об этом однажды: Harmony пересобирает тело метода заново всякий раз,
            // когда на него садится ещё одна заплата, а заплат на этом пересчёте у нас с
            // десяток.
            if (!told)
            {
                told = true;

                ItemForgePlugin.Log.LogInfo($"Цена очка переписана: названных чисел {replaced} "
                    + $"из двух, вставлено {inserted} из одного.");
            }

            return all;
        }
    }
}
