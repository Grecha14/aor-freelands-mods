using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// The difference between a man who knows the seam and a man who only sees the rock.
    ///
    /// Кузнечное дело и алхимия в игре полезны только у верстака: они говорят, что можно
    /// сделать, и ничего не говорят о том, что можно найти. Куст даёт свой пучок травы
    /// одинаково и знатоку, и первому встречному.
    ///
    /// Здесь навык начинает окупаться раньше верстака — прямо в поле. Знающий берёт с той же
    /// жилы больше: за каждые полные десять единиц умения кладёт в сумку ещё одну штуку
    /// наверняка, а начиная с пятой — иногда и сверх того. Руда и самоцветы считаются по
    /// кузнецу, травы и снадобное сырьё по алхимику, и считается умение того, чья рука
    /// подбирает, а не того, кто стоит в отряде старшим.
    /// </summary>
    internal static class Harvest
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Ore;
        internal static ConfigEntry<string> Herb;
        internal static ConfigEntry<int> Per;
        internal static ConfigEntry<int> From;
        internal static ConfigEntry<float> Chance;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Harvest", "Enabled", true,
                "Let smithing and alchemy pay off in the field, not only at the bench.");

            Ore = config.Bind("Harvest", "Ore", "Metal,Gem",
                "Kinds of goods a smith gathers better, by the game's own names. Metal and gems.");

            Herb = config.Bind("Harvest", "Herb", "Alchemy",
                "Kinds of goods an alchemist gathers better. Herbs and reagents.");

            Per = config.Bind("Harvest", "Per", 10,
                new ConfigDescription(
                    "One more piece, for certain, for every this many points of the craft. "
                    + "At thirty a man takes three more than a stranger would.",
                    new AcceptableValueRange<int>(1, 100)));

            From = config.Bind("Harvest", "From", 5,
                new ConfigDescription(
                    "The skill at which luck starts working at all. Below it, nothing.",
                    new AcceptableValueRange<int>(0, 100)));

            Chance = config.Bind("Harvest", "Chance", 0.20f,
                new ConfigDescription(
                    "Chance of one piece beyond the certain ones, once the craft is past the "
                    + "line above.",
                    new AcceptableValueRange<float>(0f, 1f)));
        }

        /// <summary>Adds to the handful before the hand closes on it.</summary>
        internal static void More(PickableItem what, UnitAttribute who)
        {
            if (!Enabled.Value || what == null || who == null) return;

            HumaniodUnit man = who as HumaniodUnit;
            if (man == null || man.Data == null) return;

            try
            {
                Inventory got = what.inventory;
                UIItemInfo info = got != null && got.itemInfo != null ? got.itemInfo : what.itemInfo;
                if (info == null || got == null) return;

                // Только то, что кладётся стопкой. Штучная вещь, случайно отнесённая к металлу,
                // от этого не удвоится.
                if (!info.stackable) return;

                int skill = Skill(info, man);
                if (skill < 0) return;

                int more = skill / Mathf.Max(1, Per.Value);

                if (skill >= From.Value && UnityEngine.Random.value < Chance.Value) more++;

                if (more <= 0) return;

                got.stackNum += more;
                what.stack = got.stackNum;

                ItemForgePlugin.Log.LogInfo("«" + man.Data.unitname + "» собрал «" + info.Name
                    + "»: " + more + " сверх обычного по умению " + skill + ".");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог добавить к собранному: " + e.Message);
            }
        }

        /// <summary>Which craft this is, and how good the hand is at it. Minus one if neither.</summary>
        private static int Skill(UIItemInfo info, HumaniodUnit man)
        {
            if (Fits(info, Named(Ore, ref ores, ref oresRead))) return Mathf.Max(0, man.Data.smithing);
            if (Fits(info, Named(Herb, ref herbs, ref herbsRead))) return Mathf.Max(0, man.Data.alchemy);

            return -1;
        }

        private static bool Fits(UIItemInfo info, HashSet<int> kinds)
        {
            if (kinds.Count == 0) return false;

            if (kinds.Contains((int)info.goodsType)) return true;

            if (info.goodsTypes != null)
            {
                foreach (GoodsType one in info.goodsTypes)
                {
                    if (kinds.Contains((int)one)) return true;
                }
            }

            return false;
        }

        private static HashSet<int> ores = new HashSet<int>();
        private static string oresRead;
        private static HashSet<int> herbs = new HashSet<int>();
        private static string herbsRead;

        private static HashSet<int> Named(ConfigEntry<string> which, ref HashSet<int> into,
            ref string last)
        {
            string written = which.Value ?? "";
            if (written == last) return into;

            last = written;
            into.Clear();

            foreach (string one in written.Split(','))
            {
                string name = one.Trim();
                if (name.Length == 0) continue;

                try
                {
                    into.Add((int)(GoodsType)Enum.Parse(
                        typeof(GoodsType), name, true));
                }
                catch
                {
                    ItemForgePlugin.Log.LogWarning("Такого рода товаров игра не знает: " + name);
                }
            }

            return into;
        }
    }

    // Подбор. Прибавка ставится до того, как вещь уйдёт в сумку: дальше стопка уже слита с
    // тем, что там лежало, и своё от чужого не отличить.
    [HarmonyPatch(typeof(PickableItem), "OnPick")]
    internal static class OnPick_Harvest_Patch
    {
        private static void Prefix(PickableItem __instance, UnitAttribute otherunit)
        {
            try { Harvest.More(__instance, otherunit); }
            catch { }
        }
    }
}
