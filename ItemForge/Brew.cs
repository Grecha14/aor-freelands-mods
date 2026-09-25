using System;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Brewing that can fail, and a hand steady enough that it stops failing.
    ///
    /// Кузнец в этой игре умеет испортить работу, а алхимик — нет: котёл всегда отдаёт ровно
    /// то, что написано в рецепте, и умение варить ни на что не влияет, кроме скидки на
    /// припасы. Оттого и качать его незачем.
    ///
    /// Здесь у котла есть исход. Половина — на ровном месте, и каждое очко алхимии прибавляет
    /// процент: новичок портит каждое второе варево, мастер к полусотне очков не портит почти
    /// ничего. А всё, что набралось сверх верной сотни, переходит в другое: в тот раз, когда
    /// припасы остались целы. Умение не может дать больше, чем одно готовое зелье за раз, —
    /// но может дать его даром.
    ///
    /// Провал стоит припасов и не стоит ничего больше: котёл не взрывается, просто выходит
    /// муть, которую выливают.
    /// </summary>
    internal static class Brew
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Base;
        internal static ConfigEntry<float> PerSkill;
        internal static ConfigEntry<string> Kinds;
        internal static ConfigEntry<string> Worth;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Brew", "Enabled", true,
                "Let the cauldron fail. Without this the alchemy skill buys a discount on "
                + "reagents and nothing else, and there is no reason to raise it.");

            Base = config.Bind("Brew", "Base", 0.5f,
                new ConfigDescription(
                    "The chance a brew comes out at all, before any skill. A half: the first "
                    + "hundred draughts of a man's life are half of them spoiled.",
                    new AcceptableValueRange<float>(0f, 1f)));

            PerSkill = config.Bind("Brew", "PerSkill", 0.01f,
                new ConfigDescription(
                    "What one point of the craft adds to that chance. A hundredth: at fifty "
                    + "points nothing is spoiled any more, and everything past that turns into "
                    + "the chance of keeping the reagents.",
                    new AcceptableValueRange<float>(0f, 0.1f)));

            Kinds = config.Bind("Brew", "Kinds", "alchemy",
                "Which crafts roll for it, by the game's own names: alchemy, cook, blacksmith. "
                + "The forge is left out on purpose — a bar of steel does not spoil, it is "
                + "reworked.");

            Worth = config.Bind("Brew", "Worth",
                "Common=1,Uncommon=5,Rare=15,Epic=45,Legendary=135",
                "How much the craft learns from a thing of each colour, as a multiple of the "
                + "ordinary. Green is worth five plain ones, blue fifteen, purple forty five — "
                + "and that is the right order of things, because the chance of a purple piece "
                + "coming off the anvil is small enough that a smith may work a season without "
                + "seeing one. A failed piece teaches the same as a plain one: the hand learns "
                + "from what went wrong too.");

            Telling = config.Bind("Brew", "Telling", true,
                "Say in the log what the cauldron gave and what it cost.");
        }

        /// <summary>Считается ли этому ремеслу удача.</summary>
        internal static bool Counts(CraftType kind)
        {
            if (Enabled == null || !Enabled.Value) return false;

            foreach (string one in (Kinds.Value ?? "").Split(','))
            {
                if (string.Equals(one.Trim(), kind.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Умение этой руки в этом ремесле.</summary>
        private static int Skill(HumaniodUnit who, CraftType kind)
        {
            if (who == null || who.Data == null) return 0;

            switch (kind)
            {
                case CraftType.alchemy: return who.Data.alchemy;
                case CraftType.cook: return who.Data.cooking;
                case CraftType.blacksmith: return who.Data.smithing;
                default: return 0;
            }
        }

        /// <summary>Сколько всего умения набралось, считая породу.</summary>
        internal static float Sure(HumaniodUnit who, CraftType kind)
        {
            float much = Base.Value + Skill(who, kind) * PerSkill.Value;

            // Эльфу у котла везёт: это его ремесло дольше, чем чьё-либо.
            much += Blood.Of(who, "brew");

            return much;
        }

        /// <summary>И сколько из этого перешло в бережливость.</summary>
        internal static float Spare(HumaniodUnit who, CraftType kind)
        {
            float over = Sure(who, kind) - 1f;

            return Mathf.Clamp01(Mathf.Max(0f, over) + Blood.Of(who, "spare"));
        }

        // Что решено об этой варке: вышла ли она и уцелели ли припасы.
        internal static bool Failed;
        internal static UIItemInfo Meant;

        // Какого цвета вышла последняя работа — по ней считается и наука.
        internal static UIItemQuality Made;
        internal static bool Coloured;

        /// <summary>Во сколько раз эта работа поучительнее обычной.</summary>
        internal static float Lesson(UIItemQuality colour)
        {
            try
            {
                foreach (string one in (Worth.Value ?? "").Split(','))
                {
                    int split = one.IndexOf('=');
                    if (split <= 0) continue;

                    if (!string.Equals(one.Substring(0, split).Trim(), colour.ToString(),
                            StringComparison.OrdinalIgnoreCase)) continue;

                    float much;
                    if (float.TryParse(one.Substring(split + 1).Trim(),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out much))
                    {
                        return Mathf.Max(0f, much);
                    }
                }
            }
            catch
            {
            }

            return 1f;
        }

        /// <summary>Решает исход варки, покуда припасы ещё не списаны.</summary>
        internal static bool Roll(HumaniodUnit who, CraftType kind, UIItemInfo product)
        {
            Failed = false;
            Meant = product;

            if (!Counts(kind)) return false;

            float sure = Sure(who, kind);
            float spare = Spare(who, kind);

            Failed = UnityEngine.Random.value > Mathf.Clamp01(sure);

            bool kept = !Failed && spare > 0f && UnityEngine.Random.value < spare;

            if (Telling.Value)
            {
                ItemForgePlugin.Log.LogInfo($"Котёл «{who?.Data?.unitname}»: удача "
                    + $"{Mathf.Clamp01(sure) * 100f:0}%, бережливость {spare * 100f:0}% — "
                    + (Failed ? "мимо" : (kept ? "вышло и припасы целы" : "вышло")) + ".");
            }

            if (Failed)
            {
                GameController.ShowMessage("Варево не удалось", 2f);
            }
            else if (kept)
            {
                GameController.ShowMessage("Припасы целы", 2f);
            }

            return kept;
        }
    }

    // Списание припасов. Здесь же решается и всё прочее: к этому мигу и рецепт под рукой, и
    // мастер ещё стоит у котла.
    [HarmonyPatch(typeof(CraftManager), "Cost")]
    internal static class Cost_Brew_Patch
    {
        private static bool Prefix(CraftManager __instance, CraftRecipe recipe, int itemCount)
        {
            try
            {
                if (recipe == null) return true;

                HumaniodUnit hand = Blood.Bench;
                CraftType kind = Blood.Craft;

                // Порода мастера ловится соседним патчем на этом же вызове, и он мог ещё не
                // отработать: спрашиваем окно сами.
                try
                {
                    hand = AccessTools.Field(typeof(CraftManager), "creator")
                        ?.GetValue(__instance) as HumaniodUnit;

                    object was = AccessTools.Field(typeof(CraftManager), "craftType")
                        ?.GetValue(__instance);

                    if (was is CraftType) kind = (CraftType)was;
                }
                catch
                {
                }

                if (!Brew.Counts(kind)) return true;

                bool kept = Brew.Roll(hand, kind, recipe.product);

                if (!kept) return true;

                // Припасы целы — значит списывать нечего. Деньги за работу берём сами: они не
                // припас, и мастер их потратил, удачно ли вышло или нет.
                try
                {
                    ManagementModeCore.TakeMoney(-__instance.CurrentCostOne * itemCount);
                }
                catch
                {
                }

                return false;
            }
            catch
            {
                return true;
            }
        }
    }

    // Неравипировка — зелья и снедь — ложится в сумку другой дверью, без своей вещи и без
    // цвета. Её тоже надо удержать, когда котёл подвёл.
    [HarmonyPatch(typeof(InventoryManager), "PutItemInbag",
        new[] { typeof(UIItemInfo), typeof(int), typeof(int), typeof(bool), typeof(bool) })]
    internal static class PutStack_Brew_Patch
    {
        private static bool Prefix(UIItemInfo item)
        {
            try
            {
                if (!Brew.Failed || item == null) return true;
                if (Brew.Meant != null && item != Brew.Meant) return true;

                Brew.Failed = false;
                Brew.Meant = null;

                return false;
            }
            catch
            {
                return true;
            }
        }
    }

    // Наука ремесла: за зелёное впятеро, за синее впятнадцатеро, за фиолетовое вчетверо с
    // лишним десятком. И за испорченное — как за обычное: рука учится и на этом.
    [HarmonyPatch(typeof(HumaniodUnit), "GainProfessionExp")]
    internal static class Craft_Brew_Patch
    {
        private static void Prefix(int index, ref float exp)
        {
            try
            {
                if (exp <= 0f || !Brew.Coloured) return;

                // Девятое ремесло — кузня, десятое — алхимия, одиннадцатое — стряпня.
                if (index < 9 || index > 11) return;

                float much = Brew.Lesson(Brew.Made);

                Brew.Coloured = false;

                if (much != 1f) exp *= much;
            }
            catch
            {
            }
        }
    }

    // Готовое ложится в сумку — или не ложится, если котёл подвёл.
    [HarmonyPatch(typeof(InventoryManager), "PutItemInbag", new[] { typeof(Inventory), typeof(bool) })]
    internal static class PutItemInbag_Brew_Patch
    {
        private static bool Prefix(Inventory inventory)
        {
            try
            {
                // Цвет готовой работы запоминаем всегда: по нему считается наука, и
                // считается она мигом позже, когда самой вещи уже не спросишь.
                if (inventory != null && Blood.Bench != null)
                {
                    Brew.Made = inventory.quality;
                    Brew.Coloured = true;
                }

                if (!Brew.Failed || inventory == null || inventory.itemInfo == null) return true;

                // Только то самое, что варили: всё прочее, что попадает в сумку в эту минуту,
                // нас не касается.
                if (Brew.Meant != null && inventory.itemInfo != Brew.Meant) return true;

                Brew.Failed = false;
                Brew.Meant = null;

                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}
