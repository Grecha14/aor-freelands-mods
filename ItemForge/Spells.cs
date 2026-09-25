using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using spell;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// What spells and feats do, rewritten where the game wrote them.
    ///
    /// Всё правится там, где записано у самой игры: в числах заклинания, в его эффектах и в
    /// тех же числах, по которым строится подсказка. Тогда написанное в подсказке и сделанное в
    /// бою — одно число, а не два.
    ///
    /// Магия за ману бьёт втрое сильнее, и втрое сильнее её эффекты — и добрые, и дурные; зато
    /// готовится она вдвое дольше и стоит в полтора раза больше маны. Приёмы за выносливость
    /// этого не касается.
    ///
    /// «Мощная защита» двуручника становится «Мощью Геракла»: вся сила бойца, со всем, что её
    /// растит, умножается на десятую за уровень, а шанс блока падает на восемь за уровень —
    /// простым вычитанием. «Щитовая стена» зовётся «Каменный страж». «Боевая ярость» и
    /// «Крепость» дают вдесятеро больше за уровень.
    /// </summary>
    internal static class Spells
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Power;
        internal static ConfigEntry<float> Longer;
        internal static ConfigEntry<float> Dearer;
        internal static ConfigEntry<float> Might;
        internal static ConfigEntry<float> Guard;
        internal static ConfigEntry<float> Tenfold;
        internal static ConfigEntry<bool> Telling;

        private const int Hercules = 3;
        private const int Fury = 45;
        private const int Fortress = 68;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Spells", "Enabled", true,
                "Rewrite spells and feats in the numbers the game keeps for them, so that the "
                + "tooltip and the fight tell the same story.");

            Power = config.Bind("Spells", "Power", 3f,
                new ConfigDescription(
                    "How many times stronger every spell paid for with mana strikes, and how many "
                    + "times stronger its effects are, good and ill alike. Durations stay.",
                    new AcceptableValueRange<float>(0.1f, 20f)));

            Longer = config.Bind("Spells", "Longer", 2f,
                new ConfigDescription(
                    "How many times longer such a spell takes to prepare. A spell cast at once "
                    + "stays at once: there is nothing to lengthen.",
                    new AcceptableValueRange<float>(0.1f, 10f)));

            Dearer = config.Bind("Spells", "Dearer", 1.5f,
                new ConfigDescription(
                    "How many times more mana such a spell costs.",
                    new AcceptableValueRange<float>(0.1f, 10f)));

            Might = config.Bind("Spells", "Might", 0.1f,
                new ConfigDescription(
                    "Might of Hercules: by what share of the whole strength each level multiplies "
                    + "it. A tenth: at the third level all strength, with everything that raises "
                    + "it, counts three tenths more.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Guard = config.Bind("Spells", "Guard", 8f,
                new ConfigDescription(
                    "Might of Hercules: how much chance to block each level takes away, in plain "
                    + "points, not as a share. Eight: twenty-four at the third level.",
                    new AcceptableValueRange<float>(0f, 50f)));

            Tenfold = config.Bind("Spells", "Tenfold", 10f,
                new ConfigDescription(
                    "How many times more Battle Fury and Fortress give for every level.",
                    new AcceptableValueRange<float>(1f, 50f)));

            Telling = config.Bind("Spells", "Telling", true,
                "Say in the log what was rewritten in each spell, and which tooltip numbers "
                + "could not be matched to anything and were left as written.");
        }

        // ----------------------------------------------------------------- где что лежит

        /// <summary>Every behaviour a spell is made of, across all its levels.</summary>
        private static List<BehaviorBase> Parts(UISpellInfo spell)
        {
            List<BehaviorBase> all = new List<BehaviorBase>();

            if (spell.behaviorPrefab != null)
            {
                all.AddRange(spell.behaviorPrefab.GetComponentsInChildren<BehaviorBase>(true));
            }

            if (spell.UpgradeBehaviorPrefabs != null)
            {
                foreach (GameObject more in spell.UpgradeBehaviorPrefabs)
                {
                    if (more != null) all.AddRange(more.GetComponentsInChildren<BehaviorBase>(true));
                }
            }

            return all.Distinct().ToList();
        }

        /// <summary>The damage records and effect cards a spell carries.</summary>
        private static void Scan(UISpellInfo spell, List<BehavDamageInfo> hits, List<UIBuffInfo> cards)
        {
            foreach (BehaviorBase part in Parts(spell))
            {
                if (part == null) continue;

                foreach (FieldInfo field in part.GetType().GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    object held;
                    try { held = field.GetValue(part); }
                    catch { continue; }

                    if (held == null) continue;

                    BehavDamageInfo hit = held as BehavDamageInfo;
                    if (hit != null) { if (!hits.Contains(hit)) hits.Add(hit); continue; }

                    BehavBuffInfo pack = held as BehavBuffInfo;
                    if (pack != null) { Cards(pack.addonBuffs, cards); continue; }

                    BuffData one = held as BuffData;
                    if (one != null) { Card(one.addonBuff, cards); continue; }

                    List<BuffData> many = held as List<BuffData>;
                    if (many != null) { Cards(many, cards); continue; }

                    UIBuffInfo card = held as UIBuffInfo;
                    if (card != null) Card(card, cards);
                }
            }
        }

        /// <summary>The effect cards a spell carries, for anyone who needs them.</summary>
        internal static List<UIBuffInfo> CardsOf(UISpellInfo spell)
        {
            List<BehavDamageInfo> hits = new List<BehavDamageInfo>();
            List<UIBuffInfo> cards = new List<UIBuffInfo>();
            if (spell != null) Scan(spell, hits, cards);
            return cards;
        }

        private static void Cards(List<BuffData> from, List<UIBuffInfo> cards)
        {
            if (from == null) return;
            foreach (BuffData one in from) if (one != null) Card(one.addonBuff, cards);
        }

        private static void Card(UIBuffInfo card, List<UIBuffInfo> cards)
        {
            if (card != null && !cards.Contains(card)) cards.Add(card);
        }

        private static bool Near(float a, float b)
        {
            return Mathf.Abs(Mathf.Abs(a) - Mathf.Abs(b)) < 0.011f;
        }

        /// <summary>
        /// Which tooltip numbers stand for these effects.
        ///
        /// Подсказка заклинания держит свои числа отдельно от того, что оно делает: «база и
        /// прибавка за уровень». Какое из них про силу эффекта, а какое про срок, подсказка не
        /// говорит — узнаём по совпадению с самими эффектами, в долях и в процентах.
        /// </summary>
        private static HashSet<int> Match(UISpellInfo spell, List<UIBuffInfo> cards,
            List<BehavDamageInfo> hits)
        {
            HashSet<int> found = new HashSet<int>();
            if (spell.DescriptionParam == null) return found;

            for (int i = 0; i < spell.DescriptionParam.Count; i++)
            {
                DecriptionParam p = spell.DescriptionParam[i];
                if (p == null) continue;
                if (Mathf.Abs(p.baseParam) < 0.001f && Mathf.Abs(p.levelParam) < 0.001f) continue;

                bool hit = false;

                if (cards != null)
                {
                    foreach (UIBuffInfo card in cards)
                    {
                        if (card == null || card.addAttrs == null) continue;

                        foreach (AddonAttributes a in card.addAttrs)
                        {
                            if (a == null) continue;

                            if ((Near(p.baseParam, a.value) && Near(p.levelParam, a.levelAlter))
                                || (Near(p.baseParam, a.value * 100f) && Near(p.levelParam, a.levelAlter * 100f)))
                            {
                                hit = true;
                                break;
                            }
                        }

                        if (!hit && card.dotDamage > 0f
                            && Near(p.baseParam, card.dotDamage * card.buffbaseMD)
                            && Near(p.levelParam, card.dotDamage * card.bufflevelMD)) hit = true;

                        if (hit) break;
                    }
                }

                if (!hit && hits != null)
                {
                    foreach (BehavDamageInfo d in hits)
                    {
                        if (d == null) continue;

                        float whole = 1f + d.damageBaseMD;

                        if ((Near(p.baseParam, d.damage * whole) && Near(p.levelParam, d.damage * d.damageLevelMD))
                            || (Near(p.baseParam, whole * 100f) && Near(p.levelParam, d.damageLevelMD * 100f))
                            || (d.damage > 0f && Near(p.baseParam, d.damage) && Mathf.Abs(p.levelParam) < 0.001f))
                        {
                            hit = true;
                            break;
                        }
                    }
                }

                if (hit) found.Add(i);
            }

            return found;
        }

        private static void Scale(UISpellInfo spell, HashSet<int> which, float much)
        {
            foreach (int i in which)
            {
                DecriptionParam p = spell.DescriptionParam[i];
                p.baseParam *= much;
                p.levelParam *= much;
            }
        }

        // ----------------------------------------------------------------- переписать

        private static bool done;
        internal static readonly HashSet<string> hercules = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Rewrites the spell book, once.</summary>
        internal static void Catalogue()
        {
            if (done || Enabled == null || !Enabled.Value) return;

            try
            {
                UISpellDatabase book;
                try { book = UISpellDatabase.Instance; }
                catch { return; }

                if (book == null || book.spells == null) return;

                done = true;
                int magic = 0;

                foreach (UISpellInfo spell in book.spells)
                {
                    if (spell == null) continue;

                    try
                    {
                        if (spell.ID == Hercules) Herculean(spell);
                        else if (spell.ID == Fury || spell.ID == Fortress) Tenfolded(spell);

                        if (spell.spellType == SpellType.magic && Empowered(spell)) magic++;
                    }
                    catch (Exception e)
                    {
                        ItemForgePlugin.Log.LogWarning($"Не смог переписать «{spell.Name}»: {e.Message}");
                    }
                }

                ItemForgePlugin.Log.LogInfo($"Магия переписана: заклинаний за ману {magic} — урон и "
                    + $"эффекты ×{Power.Value:0.##}, подготовка ×{Longer.Value:0.##}, мана ×{Dearer.Value:0.##}.");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог переписать заклинания: " + e);
            }
        }

        /// <summary>A spell paid for with mana: stronger, slower, dearer.</summary>
        private static bool Empowered(UISpellInfo spell)
        {
            List<BehavDamageInfo> hits = new List<BehavDamageInfo>();
            List<UIBuffInfo> cards = new List<UIBuffInfo>();
            Scan(spell, hits, cards);

            // Сперва узнать, какие числа подсказки про силу, — пока сила ещё прежняя.
            HashSet<int> strong = Match(spell, cards, hits);

            float power = Mathf.Max(0.01f, Power.Value);

            // Урон заклинания — «источник × (1 + база + уровень × прибавка)». Втрое на каждом
            // уровне: база 2 + 3·база, прибавка 3·прибавка.
            foreach (BehavDamageInfo d in hits)
            {
                d.damageBaseMD = power - 1f + power * d.damageBaseMD;
                d.damageLevelMD = power * d.damageLevelMD;
            }

            Scale(spell, strong, power);

            spell.PowerCost *= Dearer.Value;
            spell.PowerCostPerSec *= Dearer.Value;

            if (spell.prepareTime > 0f) spell.prepareTime *= Longer.Value;
            if (spell.castTime > 0f) spell.castTime *= Longer.Value;

            if (Telling.Value && spell.DescriptionParam != null && spell.DescriptionParam.Count > strong.Count)
            {
                ItemForgePlugin.Log.LogInfo($"Заклинание «{spell.Name}»: чисел в подсказке "
                    + $"{spell.DescriptionParam.Count}, про силу узнано {strong.Count} "
                    + $"(ударов {hits.Count}, эффектов {cards.Count}).");
            }

            return true;
        }

        /// <summary>Battle Fury and Fortress: ten times as much for every level.</summary>
        private static void Tenfolded(UISpellInfo spell)
        {
            List<BehavDamageInfo> hits = new List<BehavDamageInfo>();
            List<UIBuffInfo> cards = new List<UIBuffInfo>();
            Scan(spell, hits, cards);

            HashSet<int> strong = Match(spell, cards, null);
            float much = Mathf.Max(1f, Tenfold.Value);

            foreach (UIBuffInfo card in cards)
            {
                if (card == null || card.addAttrs == null) continue;

                List<AddonAttributes> mine = new List<AddonAttributes>(card.addAttrs.Count);
                foreach (AddonAttributes a in card.addAttrs)
                {
                    if (a == null) continue;
                    mine.Add(new AddonAttributes(a.type, a.value * much, a.levelAlter * much));
                }

                card.addAttrs = mine;
            }

            Scale(spell, strong, much);

            if (Telling.Value)
            {
                ItemForgePlugin.Log.LogInfo($"«{spell.Name}»: эффектов ×{much:0.#} — {cards.Count}, "
                    + $"чисел подсказки {strong.Count} из {(spell.DescriptionParam != null ? spell.DescriptionParam.Count : 0)}.");
            }
        }

        /// <summary>Mighty Guard becomes the Might of Hercules.</summary>
        private static void Herculean(UISpellInfo spell)
        {
            List<BehavDamageInfo> hits = new List<BehavDamageInfo>();
            List<UIBuffInfo> cards = new List<UIBuffInfo>();
            Scan(spell, hits, cards);

            float guard = Mathf.Max(0f, Guard.Value);

            foreach (UIBuffInfo card in cards)
            {
                if (card == null) continue;

                hercules.Add(card.id ?? "");

                List<AddonAttributes> mine = new List<AddonAttributes>();
                bool block = false;

                if (card.addAttrs != null)
                {
                    foreach (AddonAttributes a in card.addAttrs)
                    {
                        if (a == null) continue;

                        if (a.type == AddonAttribute.Defence)
                        {
                            mine.Add(new AddonAttributes(AddonAttribute.Defence, 0f, -guard));
                            block = true;
                            continue;
                        }

                        // Прежний прирост урона ушёл: вместо него умножается сила.
                        if (a.value > 0f || a.levelAlter > 0f) continue;

                        mine.Add(new AddonAttributes(a));
                    }
                }

                if (!block) mine.Add(new AddonAttributes(AddonAttribute.Defence, 0f, -guard));

                card.addAttrs = mine;
            }

            if (spell.DescriptionParam == null) spell.DescriptionParam = new List<DecriptionParam>();
            while (spell.DescriptionParam.Count < 2) spell.DescriptionParam.Add(new DecriptionParam());

            spell.DescriptionParam[0].baseParam = 0f;
            spell.DescriptionParam[0].levelParam = Might.Value * 100f;
            spell.DescriptionParam[1].baseParam = 0f;
            spell.DescriptionParam[1].levelParam = guard;

            // Слова подсказки: ставим в той же записи, в какой писала сама игра.
            string key = spell.description;
            string was = Tongue.Get(key) ?? "";
            string a0 = was.Contains("{[0]}") ? "{[0]}" : "{0}";
            string a1 = was.Contains("{[1]}") ? "{[1]}" : "{1}";

            Tongue.Put(key, "При активации увеличивает всю силу на " + a0
                + "%, но снижает шанс блока на " + a1 + "%.");

            if (Telling.Value)
            {
                ItemForgePlugin.Log.LogInfo($"«Мощь Геракла»: эффектов {cards.Count} "
                    + $"({string.Join(", ", hercules.ToArray())}), сила ×(1 + {Might.Value:0.##} за уровень), "
                    + $"блок −{guard:0.#} за уровень. Прежняя подсказка: «{was}».");
            }
        }

        // ----------------------------------------------------------------- в бою

        private static readonly ConditionalWeakTable<BuffBase, object> powered =
            new ConditionalWeakTable<BuffBase, object>();

        /// <summary>True for a spell paid for with mana.</summary>
        internal static bool Magic(SpellBase spell)
        {
            return spell != null && spell.theSpell != null && spell.theSpell.spellType == SpellType.magic;
        }

        /// <summary>An effect laid by a mana spell is as much stronger as the spell itself.</summary>
        internal static void Empower(BuffBase buff, bool fresh)
        {
            if (Enabled == null || !Enabled.Value || buff == null || buff.addAttrs == null) return;
            if (!Magic(buff.sourceSpell)) return;

            object seen;
            if (!fresh && powered.TryGetValue(buff, out seen)) return;

            float power = Mathf.Max(0.01f, Power.Value);

            List<AddonAttributes> mine = new List<AddonAttributes>(buff.addAttrs.Count);
            foreach (AddonAttributes a in buff.addAttrs)
            {
                if (a == null) continue;
                mine.Add(new AddonAttributes(a.type, a.value * power, a.levelAlter * power));
            }

            buff.addAttrs = mine;

            powered.Remove(buff);
            powered.Add(buff, null);
        }

        /// <summary>Whole strength, multiplied by the Might of Hercules if it is on.</summary>
        internal static void Herculean(HumaniodUnit man)
        {
            if (Enabled == null || !Enabled.Value || hercules.Count == 0) return;
            if (man == null || man.buffmanger == null || man.buffmanger.buffs == null || man.Data == null) return;

            int level = 0;
            foreach (BuffBase one in man.buffmanger.buffs)
            {
                if (one != null && one.id != null && hercules.Contains(one.id))
                {
                    level = Mathf.Max(level, Mathf.Max(1, one.level));
                }
            }

            if (level <= 0) return;

            float times = 1f + Might.Value * level;
            man.Data.strength = Mathf.Max(1, Mathf.RoundToInt(man.Data.strength * times));
        }
    }

    [HarmonyPatch(typeof(BuffManager), "AddBuff", new Type[] { typeof(BuffBase) })]
    internal static class AddBuff_Spells_Patch
    {
        private static void Prefix(BuffBase buff)
        {
            try { Spells.Empower(buff, false); } catch { }
        }
    }

    [HarmonyPatch(typeof(BuffBase), "CalculateBuffLevel")]
    internal static class CalculateBuffLevel_Spells_Patch
    {
        private static void Postfix(BuffBase __instance)
        {
            try { Spells.Empower(__instance, true); } catch { }
        }
    }

    // Сила считается здесь: «база + прибавки». Множим итог, пока его никто не прочёл.
    [HarmonyPatch(typeof(HumaniodUnit), "WriteUnitAttribute")]
    [HarmonyPriority(Priority.First)]
    internal static class WriteUnitAttribute_Spells_Patch
    {
        private static void Postfix(HumaniodUnit __instance)
        {
            try { Spells.Herculean(__instance); } catch { }
        }
    }

    // Кровь, яд и жар от заклинания идут не через его удар, а через эффект: их урон берётся
    // из самой карточки эффекта. Втрое — здесь, пока идёт расчёт.
    [HarmonyPatch(typeof(UnitAttribute), "DamageReduce")]
    [HarmonyPriority(Priority.First)]
    internal static class DamageReduce_Spells_Patch
    {
        private static void Prefix(UnitAttribute __instance, Attack attack, out DamageBase __state)
        {
            __state = null;

            try
            {
                if (Spells.Enabled == null || !Spells.Enabled.Value || attack == null || attack.damage == null) return;
                if (!Spells.Magic(attack.bindSpell) || !Pierce.Inner(attack, __instance)) return;

                __state = attack.damage;
                attack.damage = attack.damage.Clone() * Mathf.Max(0.01f, Spells.Power.Value);
            }
            catch
            {
                __state = null;
            }
        }

        private static void Finalizer(Attack attack, DamageBase __state)
        {
            if (__state != null && attack != null) attack.damage = __state;
        }
    }
}
