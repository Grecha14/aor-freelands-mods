using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Puts a weapon's damage on the same ladder everything else climbs.
    ///
    /// Урон оружия в игре проставлен поштучно, и лестницы в нём нет: между первой ступенью и
    /// четвёртой у меча полтора раза, у кинжала столько же, у двуручного молота меньше. При
    /// этом вес, пробитие и вычет брони растут своими лестницами, и чем дальше по игре, тем
    /// сильнее расходятся: удар почти не растёт, а стена растёт круто.
    ///
    /// Здесь урон кладётся на ту же лестницу Фибоначчи, что и всё прочее: от первой ступени
    /// до пятой ровно в три с половиной раза. Двигается не каждая вещь по отдельности, а вся
    /// тройка «класс и ступень» разом, одним множителем — тогда вещь, которая была сильнее
    /// сверстниц, остаётся сильнее.
    /// </summary>
    internal static class Hone
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Ladder;
        internal static ConfigEntry<string> Kinds;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Hone", "Enabled", true,
                "Put the damage of weapons on a ladder by tier, instead of leaving it as the "
                + "game wrote it piece by piece. Without this a blade barely grows across the "
                + "game while the wall it meets grows steeply.");

            Ladder = config.Bind("Hone", "Ladder", "1.00,1.23,1.68,2.36,3.50",
                "What a weapon of each tier hits for, against its own class at the first tier "
                + "it exists at. Read off the common ladder «Breach.Rungs» from the first tier, "
                + "so damage, piercing and the wall all climb in step; three and a half times "
                + "from end to end.");

            Kinds = config.Bind("Hone", "Kinds", "sharp=1,blunt=0.7,stab=1",
                "What each kind of damage is worth against the others, applied to the weapons "
                + "themselves after the ladder.\n\n"
                + "Дробящее бьёт долго и ровно: доспех ему не помеха, и берёт оно не силой "
                + "удара, а тем, что доходит всегда. Клинок наоборот — бьёт сильно, а большую "
                + "броню пробивает только критом, найденной щелью. Прежде дробящее умело и то, "
                + "и другое сразу: доходило сквозь всё и било вровень с клинком, и латник с "
                + "булавой не проигрывал никому. Семь десятых возвращают ему его роль.");

            Telling = config.Bind("Hone", "Telling", true,
                "Write out what was moved and by how much.");
        }

        private static float[] rungs;
        private static string read;

        private static float Rung(int tier)
        {
            string written = Ladder.Value ?? "";

            if (written != read || rungs == null)
            {
                read = written;

                List<float> steps = new List<float>();

                foreach (string one in written.Split(','))
                {
                    float much;
                    if (float.TryParse(one.Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out much))
                    {
                        steps.Add(much);
                    }
                }

                rungs = steps.Count > 0 ? steps.ToArray() : new float[] { 1f };
            }

            int i = tier - 1;
            if (i < 0) i = 0;
            if (i >= rungs.Length) i = rungs.Length - 1;

            return rungs[i];
        }

        /// <summary>
        /// How much this tier is worth against the one a thing starts from.
        ///
        /// Одна лестница на урон и на то, что его держит: иначе с каждым тиром одно обгоняет
        /// другое, и равновесие начала игры к концу перестаёт им быть.
        /// </summary>
        internal static float Step(int tier, int first)
        {
            float bottom = Rung(first);
            return bottom > 0f ? Rung(tier) / bottom : 1f;
        }

        private static readonly Dictionary<string, float> worths =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private static string worthsRead;

        /// <summary>Чего стоит этот род урона против прочих.</summary>
        private static float Worth(DamageType kind)
        {
            string written = Kinds != null ? (Kinds.Value ?? "") : "";

            if (written != worthsRead)
            {
                worthsRead = written;
                worths.Clear();

                foreach (string one in written.Split(','))
                {
                    int split = one.IndexOf('=');
                    if (split <= 0) continue;

                    float much;
                    if (float.TryParse(one.Substring(split + 1).Trim(),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out much))
                    {
                        worths[one.Substring(0, split).Trim()] = much;
                    }
                }
            }

            float got;
            return worths.TryGetValue(kind.ToString(), out got) ? got : 1f;
        }

        /// <summary>The middle of what this weapon deals, over every kind of damage it has.</summary>
        private static float Middle(UIWeaponInfo blade)
        {
            if (blade == null || blade.damage == null) return 0f;

            float sum = 0f;

            foreach (KeyValuePair<DamageType, Damage> one in blade.damage)
            {
                if (one.Value == null) continue;
                sum += (one.Value.minDamage + one.Value.maxDamage) * 0.5f;
            }

            return sum;
        }

        private static bool honed;

        internal static void Sharpen()
        {
            if (honed || Enabled == null || !Enabled.Value) return;

            try
            {
                UIItemDatabase db;
                try { db = UIItemDatabase.Instance; }
                catch { return; }

                if (db == null || db.items == null) return;

                honed = true;

                // Сперва перепись: какой класс на какой ступени сколько бьёт в среднем.
                Dictionary<string, Dictionary<int, List<UIWeaponInfo>>> kinds =
                    new Dictionary<string, Dictionary<int, List<UIWeaponInfo>>>();

                foreach (UIItemInfo thing in db.items)
                {
                    UIWeaponInfo blade = thing as UIWeaponInfo;
                    if (blade == null || Middle(blade) <= 0f) continue;

                    // Легендарные писаны по одной и существуют в единственном числе —
                    // их не трогаем: они должны быть ровно тем, что в них написано.
                    if ((int)blade.Quality >= (int)UIItemQuality.Legendary) continue;

                    string kind = blade.weaponClass.ToString();
                    int tier = (int)blade.tier;
                    if (tier < 1) continue;

                    Dictionary<int, List<UIWeaponInfo>> tiers;
                    if (!kinds.TryGetValue(kind, out tiers))
                    {
                        tiers = new Dictionary<int, List<UIWeaponInfo>>();
                        kinds[kind] = tiers;
                    }

                    List<UIWeaponInfo> same;
                    if (!tiers.TryGetValue(tier, out same))
                    {
                        same = new List<UIWeaponInfo>();
                        tiers[tier] = same;
                    }

                    same.Add(blade);
                }

                int moved = 0;

                foreach (KeyValuePair<string, Dictionary<int, List<UIWeaponInfo>>> kind in kinds)
                {
                    int first = int.MaxValue;
                    foreach (int tier in kind.Value.Keys) if (tier < first) first = tier;
                    if (first == int.MaxValue) continue;

                    float anchor = Mean(kind.Value[first]);
                    if (anchor <= 0f) continue;

                    float bottom = Rung(first);

                    foreach (KeyValuePair<int, List<UIWeaponInfo>> tier in kind.Value)
                    {
                        float want = anchor * (bottom > 0f ? Rung(tier.Key) / bottom : 1f);
                        float have = Mean(tier.Value);

                        if (have <= 0f || want <= 0f) continue;

                        float much = want / have;
                        if (Mathf.Abs(much - 1f) < 0.01f) continue;

                        foreach (UIWeaponInfo blade in tier.Value)
                        {
                            foreach (KeyValuePair<DamageType, Damage> one in blade.damage)
                            {
                                if (one.Value == null) continue;

                                // Вверх до десятой доли: после перемножения выходило
                                // «17,60842 — 24,21158», и карточка становилась нечитаемой.
                                // Округляем в большую сторону — вещь не должна дешеветь от
                                // того, что мы её пересчитали.
                                one.Value.minDamage = Up(one.Value.minDamage * much);
                                one.Value.maxDamage = Up(one.Value.maxDamage * much);
                            }

                            moved++;
                        }

                        if (Telling.Value)
                        {
                            ItemForgePlugin.Log.LogInfo($"Урон: {kind.Key} Т{tier.Key} "
                                + $"{have:0.#} → {want:0.#} (×{much:0.00}), вещей "
                                + $"{tier.Value.Count}.");
                        }
                    }
                }

                // И род урона: каждому своя цена. Считается после лестницы и по самой вещи —
                // у оружия смешанного урона своя доля падает, а чужая остаётся.
                int typed = 0;

                foreach (UIItemInfo thing in db.items)
                {
                    UIWeaponInfo blade = thing as UIWeaponInfo;
                    if (blade == null || blade.damage == null) continue;

                    bool touched = false;

                    foreach (KeyValuePair<DamageType, Damage> one in blade.damage)
                    {
                        if (one.Value == null) continue;

                        float worth = Worth(one.Key);
                        if (Mathf.Abs(worth - 1f) < 0.001f) continue;

                        one.Value.minDamage = Up(one.Value.minDamage * worth);
                        one.Value.maxDamage = Up(one.Value.maxDamage * worth);
                        touched = true;
                    }

                    if (touched) typed++;
                }

                if (Telling.Value && typed > 0)
                {
                    ItemForgePlugin.Log.LogInfo($"Род урона взвешен: правлено {typed} вещей "
                        + $"({Kinds.Value}).");
                }

                // Проникающая сила, вписанная в вещь, больше не пробивает: пробитие теперь
                // считается из руки и веса, и чужому числу там места нет. Но кузнец, ковавший
                // бронебойный молот, ковал его не зря — эта доля становится прибавкой к
                // урону, а из карточки убирается, чтобы не обещать того, чего нет.
                int paid = 0;

                foreach (UIItemInfo thing in db.items)
                {
                    UIWeaponInfo blade = thing as UIWeaponInfo;
                    if (blade == null || blade.damage == null || blade.resistPen == null) continue;

                    float extra = Breach.Written(blade, Breach.Lead(blade));
                    if (extra <= 0f) continue;

                    foreach (KeyValuePair<DamageType, Damage> one in blade.damage)
                    {
                        if (one.Value == null) continue;

                        one.Value.minDamage = Up(one.Value.minDamage * (1f + extra));
                        one.Value.maxDamage = Up(one.Value.maxDamage * (1f + extra));
                    }

                    Array.Clear(blade.resistPen, 0, blade.resistPen.Length);
                    paid++;
                }

                if (Telling.Value && paid > 0)
                {
                    ItemForgePlugin.Log.LogInfo($"Проникающая сила стала уроном: "
                        + $"пересчитано {paid} вещей.");
                }

                if (Telling.Value)
                {
                    ItemForgePlugin.Log.LogInfo($"Урон оружия положен на лестницу: "
                        + $"поправлено {moved} вещей в {kinds.Count} классах.");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог положить урон на лестницу: " + e);
            }
        }

        /// <summary>Rounded up to a tenth: a card should read, not spell out a fraction.</summary>
        private static float Up(float much)
        {
            return Mathf.Ceil(much * 10f) / 10f;
        }

        private static float Mean(List<UIWeaponInfo> same)
        {
            if (same == null || same.Count == 0) return 0f;

            float sum = 0f;
            foreach (UIWeaponInfo blade in same) sum += Middle(blade);

            return sum / same.Count;
        }
    }
}
