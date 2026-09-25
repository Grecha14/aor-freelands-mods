using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Удача кузнеца: не всякая вещь выходит.
    ///
    /// В игре ковка безотказна — заплатил припасом, получил вещь. Оттого мастерство значило
    /// только скорость да доступ к рецепту, а не то, выйдет ли работа. Здесь у каждой ступени
    /// свой порог: подмастерье берётся за латы и переводит железо, мастер кует их без промаха.
    ///
    ///     Т1  30 % при нуле, наверняка к десятому
    ///     Т2   5 % при нуле, наверняка к двадцатому
    ///     Т3   5 % при нуле, наверняка к тридцать пятому
    ///     Т4   5 % при нуле, наверняка к пятидесятому
    ///     Т5   5 % при нуле, наверняка к восьмидесятому
    ///
    /// Между порогами — ровно. Пятая ступень есть не только у оружия: зелья тоже доходят до
    /// неё, и алхимику восемьдесят нужны так же, как кузнецу.
    ///
    /// **За несделанное платят третью опыта.** Испорченная заготовка тоже учит — меньше, чем
    /// удачная, но учит; иначе низкий навык становился бы ловушкой, из которой не выбраться.
    ///
    /// Цепляется это не к методу ковки, а к одному вызову внутри неё: там, где игра кладёт
    /// готовое в сумку, теперь стоит наш посредник с той же подписью. Он бросает жребий и
    /// либо зовёт игровой метод, либо молча не зовёт.
    /// </summary>
    internal static class Chancery
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Odds;
        internal static ConfigEntry<float> Spoiled;
        internal static ConfigEntry<bool> Telling;

        /// <summary>Мастерство того, кто сейчас у горна. Ставится перед ковкой, снимается после.</summary>
        internal static int Hands = -1;

        /// <summary>Вышла ли последняя работа. По этому считается опыт.</summary>
        internal static bool Missed;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Chancery", "Enabled", true,
                "Let a craft fail. The game never refuses: pay the stock and take the thing, so "
                + "skill meant only speed and which recipes were open, never whether the work "
                + "came out.");

            Odds = config.Bind("Chancery", "Odds",
                "T1=30:10,T2=5:20,T3=5:35,T4=5:50,T5=5:80",
                "For each tier: the chance of success at no skill, and the skill at which it "
                + "becomes certain. Between the two it climbs evenly. An apprentice who takes on "
                + "plate spoils the iron; a master never does. The fifth tier is not weapons "
                + "only — potions reach it too, so an alchemist wants eighty as much as a smith.");

            Spoiled = config.Bind("Chancery", "Spoiled", 0.3333f,
                new ConfigDescription(
                    "What share of the experience a failed work still pays. A third: the ruined "
                    + "billet teaches too, less than a good one but it teaches. Without this a "
                    + "low skill is a pit with no ladder out of it.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Telling = config.Bind("Chancery", "Telling", true,
                "Write every attempt to the log: what was made, at what skill, what the odds "
                + "were and how it fell.");
        }

        /// <summary>Шанс, что работа выйдет, от нуля до единицы.</summary>
        internal static float Luck(ItemTier tier, int skill)
        {
            if (!Enabled.Value) return 1f;

            float least, full;
            if (!Table().TryGetValue(tier, out least) || !Full().TryGetValue(tier, out full))
            {
                return 1f;
            }

            if (skill < 0) skill = 0;
            if (full <= 0f) return 1f;
            if (skill >= full) return 1f;

            // Между порогом и уверенностью — ровно.
            return Mathf.Clamp01(Mathf.Lerp(least * 0.01f, 1f, skill / full));
        }

        /// <summary>Бросок на одну работу. Он же помечает, платить ли полным опытом.</summary>
        private static bool Rolls(UIItemInfo thing, int stack)
        {
            Missed = false;

            if (!Enabled.Value || thing == null) return true;
            if (Hands < 0) return true;              // кует не тот, за кем мы следим

            float luck = Luck(thing.tier, Hands);
            bool made = UnityEngine.Random.value <= luck;

            Missed = !made;

            if (Telling.Value)
            {
                ItemForgePlugin.Log.LogInfo("Ковка: " + thing.Name + " (" + thing.tier
                    + ")" + (stack > 1 ? " x" + stack : "")
                    + ", навык " + Hands
                    + ", шанс " + (luck * 100f).ToString("0") + "% -> "
                    + (made ? "вышло" : "испорчено"));
            }

            return made;
        }

        // ------------------------------------------------------------ посредники

        /// <summary>Стоит на месте PutItemInbag(Inventory, bool) внутри ковки.</summary>
        internal static void Made(InventoryManager bag, Inventory kit, bool autoStack)
        {
            try
            {
                if (kit != null && !Rolls(kit.itemInfo, kit.stackNum)) return;
            }
            catch
            {
            }

            bag.PutItemInbag(kit, autoStack);
        }

        /// <summary>Стоит на месте PutItemInbag(UIItemInfo, int, int, bool, bool).</summary>
        internal static void Made(InventoryManager bag, UIItemInfo thing, int stack,
            int isStolen, bool isNew, bool autoStack)
        {
            try
            {
                if (!Rolls(thing, stack)) return;
            }
            catch
            {
            }

            bag.PutItemInbag(thing, stack, isStolen, isNew, autoStack);
        }

        // ------------------------------------------------------------ разбор таблицы

        private static readonly Dictionary<ItemTier, float> least = new Dictionary<ItemTier, float>();
        private static readonly Dictionary<ItemTier, float> full = new Dictionary<ItemTier, float>();
        private static string read;

        private static void Parse()
        {
            string written = Odds.Value ?? "";
            if (written == read) return;

            read = written;
            least.Clear();
            full.Clear();

            foreach (string one in written.Split(','))
            {
                string[] halves = one.Split('=');
                if (halves.Length != 2) continue;

                ItemTier tier;
                try { tier = (ItemTier)Enum.Parse(typeof(ItemTier), halves[0].Trim(), true); }
                catch { continue; }

                string[] pair = halves[1].Split(':');
                if (pair.Length != 2) continue;

                float start, top;
                if (!float.TryParse(pair[0].Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out start)) continue;
                if (!float.TryParse(pair[1].Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out top)) continue;

                least[tier] = start;
                full[tier] = top;
            }
        }

        private static Dictionary<ItemTier, float> Table() { Parse(); return least; }
        private static Dictionary<ItemTier, float> Full() { Parse(); return full; }
    }

    /// <summary>
    /// Подменяет в ковке вызов «положить в сумку» на наш, с жребием. Меняется один адрес: у
    /// посредника подпись та же плюс сам InventoryManager первым доводом, как оно и лежит на
    /// стеке перед вызовом метода объекта.
    /// </summary>
    internal static class Gates
    {
        internal static IEnumerable<CodeInstruction> Guard(IEnumerable<CodeInstruction> code)
        {
            MethodInfo oneKit = AccessTools.Method(typeof(InventoryManager), "PutItemInbag",
                new[] { typeof(Inventory), typeof(bool) });
            MethodInfo manyKit = AccessTools.Method(typeof(InventoryManager), "PutItemInbag",
                new[] { typeof(UIItemInfo), typeof(int), typeof(int), typeof(bool), typeof(bool) });

            MethodInfo oneOurs = AccessTools.Method(typeof(Chancery), "Made",
                new[] { typeof(InventoryManager), typeof(Inventory), typeof(bool) });
            MethodInfo manyOurs = AccessTools.Method(typeof(Chancery), "Made",
                new[] { typeof(InventoryManager), typeof(UIItemInfo), typeof(int), typeof(int),
                    typeof(bool), typeof(bool) });

            int swapped = 0;

            foreach (CodeInstruction one in code)
            {
                if (one.opcode == OpCodes.Call || one.opcode == OpCodes.Callvirt)
                {
                    MethodInfo was = one.operand as MethodInfo;

                    if (was == oneKit)
                    {
                        swapped++;
                        yield return new CodeInstruction(OpCodes.Call, oneOurs);
                        continue;
                    }

                    if (was == manyKit)
                    {
                        swapped++;
                        yield return new CodeInstruction(OpCodes.Call, manyOurs);
                        continue;
                    }
                }

                yield return one;
            }

            if (swapped == 0)
            {
                ItemForgePlugin.Log.LogWarning("Жребий в ковке не встал: вызов «положить в "
                    + "сумку» не нашёлся, работа выходит всегда.");
            }
        }
    }

    /// <summary>Верстак. Ковка живёт в сопрограмме, оттого правится её MoveNext.</summary>
    [HarmonyPatch]
    internal static class BenchLuck_Patch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.EnumeratorMoveNext(
                AccessTools.Method(typeof(CraftManager), "DoCraft"));
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            return Gates.Guard(code);
        }
    }

    /// <summary>Цеховая кузница: работа доводится за день.</summary>
    [HarmonyPatch(typeof(TroopSmithyManager), "OnDayPassed")]
    internal static class ShopLuck_Patch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            return Gates.Guard(code);
        }

        private static void Prefix(TroopSmithyManager __instance)
        {
            try
            {
                Chancery.Hands = __instance.staff != null ? __instance.staff.smithing : -1;
            }
            catch
            {
                Chancery.Hands = -1;
            }
        }

        private static void Postfix()
        {
            Chancery.Hands = -1;
        }
    }

    /// <summary>
    /// Верстак. Списание припаса идёт вплотную перед тем, как вещь берётся из ниоткуда, — это
    /// обычный метод, и по нему узнаётся, чьи сейчас руки.
    /// </summary>
    [HarmonyPatch(typeof(CraftManager), "Cost")]
    internal static class BenchHands_Patch
    {
        private static void Postfix(CraftManager __instance)
        {
            try
            {
                Chancery.Hands = __instance.SkillLevel;
            }
            catch
            {
                Chancery.Hands = -1;
            }
        }
    }
}
