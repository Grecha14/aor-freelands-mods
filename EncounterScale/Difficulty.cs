using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using DuloGames.UI;
using spell;

namespace EncounterScale
{
    /// <summary>
    /// Makes a fight's difficulty follow the strength of what stands in it.
    ///
    /// The game already knows how to make a unit stronger for its level, but only above the
    /// level its template was authored at: ApplyUnitLevelBonus compares Data.level against
    /// info.level and hands out five per cent of health and damage for each step between
    /// them. A monster standing at its own template level therefore gets nothing, however
    /// high that level happens to be, so a level-forty brute and a level-five one differ
    /// only by whatever their two templates were written with.
    ///
    /// Everything here goes through the same buff the game itself uses. That matters more
    /// than it looks: caps, resistances, portraits and the recalculation that follows all
    /// read the result of the buff pipeline, so a figure written straight onto a field would
    /// be quietly overwritten or quietly uncapped. Re-adding a buff under the same name
    /// replaces its bonuses rather than adding to them, which is what makes it safe to
    /// recompute this on every attribute update — and the game relies on that too.
    /// </summary>
    internal static class Difficulty
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> FromLevel;
        internal static ConfigEntry<float> HealthPerLevel;
        internal static ConfigEntry<float> DamagePerLevel;

        internal static ConfigEntry<float> BossHealth;
        internal static ConfigEntry<float> BossDamage;
        internal static ConfigEntry<float> BossAttributes;
        internal static ConfigEntry<float> BossArmour;

        internal static ConfigEntry<float> DragonPower;
        internal static ConfigEntry<float> DragonAttributes;
        internal static ConfigEntry<string> DragonNames;

        internal static ConfigEntry<bool> VerboseLog;

        private static readonly AddonAttribute[] Attributes =
        {
            AddonAttribute.Strength, AddonAttribute.Endurance, AddonAttribute.Agility,
            AddonAttribute.Precision, AddonAttribute.Intelligence, AddonAttribute.Willpower
        };

        // Кого уже усилили: в лог пишем по одному разу на существо, иначе строка повторялась
        // бы при каждом пересчёте характеристик, а он случается часто.
        private static readonly HashSet<int> told = new HashSet<int>();

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Difficulty", "Enabled", true,
                "Scale a unit's strength by its own level. Off leaves every monster exactly as "
                + "the game built it.");

            FromLevel = config.Bind("Difficulty", "FromLevel", 10,
                new ConfigDescription(
                    "Level below which nothing changes at all. The opening hours are meant to be "
                    + "survivable by a character who has nothing yet, and scaling there punishes "
                    + "the part of the game with the least room to answer back.",
                    new AcceptableValueRange<int>(1, 99)));

            HealthPerLevel = config.Bind("Difficulty", "HealthPerLevel", 0.04f,
                new ConfigDescription(
                    "Extra health per level above the threshold, as a fraction. At four per cent "
                    + "a level-thirty monster carries eighty per cent more than it used to.",
                    new AcceptableValueRange<float>(0f, 1f)));

            DamagePerLevel = config.Bind("Difficulty", "DamagePerLevel", 0.04f,
                new ConfigDescription(
                    "Extra damage per level above the threshold, as a fraction. Kept level with "
                    + "health on purpose: raising only health makes fights longer rather than "
                    + "harder, which is the dullest way to add difficulty.",
                    new AcceptableValueRange<float>(0f, 1f)));

            BossHealth = config.Bind("Boss", "Health", 3.0f,
                new ConfigDescription("A boss's health, as a multiple of what it would otherwise have.",
                    new AcceptableValueRange<float>(1f, 20f)));

            BossDamage = config.Bind("Boss", "Damage", 1.5f,
                new ConfigDescription("A boss's damage, as a multiple.",
                    new AcceptableValueRange<float>(1f, 20f)));

            BossAttributes = config.Bind("Boss", "Attributes", 15f,
                new ConfigDescription(
                    "Points added to every attribute of a boss — strength, endurance, agility, "
                    + "precision, intelligence, willpower. These reach further than a damage "
                    + "multiple does: they move accuracy, dodging, stamina and carrying weight "
                    + "at once, so a boss fights better rather than merely hitting harder.",
                    new AcceptableValueRange<float>(0f, 100f)));

            BossArmour = config.Bind("Boss", "Armour", 15f,
                new ConfigDescription(
                    "Points of physical and magical armour added to a boss. The game caps both "
                    + "at seventy-five on its own, so a large figure here quietly stops counting.",
                    new AcceptableValueRange<float>(0f, 75f)));

            DragonPower = config.Bind("Dragon", "Power", 10f,
                new ConfigDescription(
                    "Health and damage of a dragon, as a multiple. Applied instead of the boss "
                    + "figures rather than on top of them: dragons are bosses as far as the game "
                    + "is concerned, and multiplying twice would put them past thirty times over.",
                    new AcceptableValueRange<float>(1f, 50f)));

            DragonAttributes = config.Bind("Dragon", "Attributes", 30f,
                new ConfigDescription("Points added to every attribute of a dragon.",
                    new AcceptableValueRange<float>(0f, 100f)));

            DragonNames = config.Bind("Dragon", "Names",
                "DesertDragon,ForestDragon,LavaDragon,TheKingOfRot",
                "Which units get the figures in this section, by the name of their UnitInfo asset. "
                + "The list is the game's own: these are the four it pins to the highest level of "
                + "an area and treats as its apex creatures. TheKingOfRot is not a dragon, but it "
                + "stands with them in every way that matters here.");

            VerboseLog = config.Bind("Difficulty", "VerboseLog", false,
                "Write one line for each unit strengthened.");
        }

        private static bool IsDragon(UnitAttribute unit)
        {
            string names = (DragonNames.Value ?? "").Trim();
            if (names.Length == 0 || unit.info == null) return false;

            string asset = unit.info.name;
            if (string.IsNullOrEmpty(asset)) return false;

            foreach (string entry in names.Split(','))
            {
                string wanted = entry.Trim();
                if (wanted.Length == 0) continue;
                if (string.Equals(asset, wanted, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// Builds the whole level bonus, the game's own part included.
        /// </summary>
        /// <returns>True when it has been handled and the original should not run.</returns>
        internal static bool Apply(UnitAttribute unit)
        {
            if (!Enabled.Value) return false;
            if (unit == null || unit.info == null || unit.Data == null || unit.buffmanger == null) return false;

            // Игрок и спутники остаются как есть: задача — сделать мир опаснее, а не отряд
            // сильнее, и усиление своих обессмыслило бы первое.
            UnitType kind = unit.info.utype;
            if (kind == UnitType.player || kind == UnitType.companion || kind == UnitType.DIYcompanion)
            {
                return false;
            }

            try
            {
                float health = 0f;
                float damage = 0f;
                float attributes = 0f;
                float armour = 0f;

                // Родная прибавка игры за уровни сверх шаблонного — повторяем её здесь, потому
                // что подменяем метод целиком, и потерять её значило бы ослабить тех, кого игра
                // намеренно подняла выше их шаблона.
                int above = unit.Data.level - unit.info.level;
                if (above > 0)
                {
                    health += unit.info.levelHealthBonus * above;
                    damage += unit.info.levelDamageBonus * above;
                }

                // И наша — от собственного уровня существа, а не от уровня игрока: мир тогда
                // не подстраивается под героя, слабые места остаются слабыми, а сильные
                // опасны независимо от того, кто в них пришёл.
                int over = unit.Data.level - FromLevel.Value;
                if (over > 0)
                {
                    health += HealthPerLevel.Value * over;
                    damage += DamagePerLevel.Value * over;
                }

                bool dragon = IsDragon(unit);
                bool boss = !dragon && unit.info.isBoss;

                // Школы приходят с годами, а не с чином — и всякому человеку, не только тому,
                // кого по уровню ещё и усиливают.
                Tutelage.Teach(unit);

                if (dragon)
                {
                    health += DragonPower.Value - 1f;
                    damage += DragonPower.Value - 1f;
                    attributes = DragonAttributes.Value;
                    armour = BossArmour.Value;
                }
                else if (boss)
                {
                    health += BossHealth.Value - 1f;
                    damage += BossDamage.Value - 1f;
                    attributes = BossAttributes.Value;
                    armour = BossArmour.Value;
                }

                if (health <= 0f && damage <= 0f && attributes <= 0f && armour <= 0f) return true;

                BuffBase buff = new BuffBase(UIBuffDatabase.Instance.GetByID("NPCAttributeBuff"), unit);
                buff.level = Math.Max(above, 0);
                buff.addAttrs.Clear();

                if (health > 0f) buff.addAttrs.Add(new AddonAttributes(AddonAttribute.HPPercent, health));
                if (damage > 0f) buff.addAttrs.Add(new AddonAttributes(AddonAttribute.DamageIncrease, damage));

                if (attributes > 0f)
                {
                    foreach (AddonAttribute one in Attributes)
                    {
                        buff.addAttrs.Add(new AddonAttributes(one, attributes));
                    }
                }

                if (armour > 0f)
                {
                    buff.addAttrs.Add(new AddonAttributes(AddonAttribute.PDR, armour));
                    buff.addAttrs.Add(new AddonAttributes(AddonAttribute.MDR, armour));
                }

                unit.buffmanger.AddBuff(buff);

                Abilities.Grant(unit, dragon, boss);

                if (VerboseLog.Value && told.Add(unit.GetInstanceID()))
                {
                    string what = dragon ? "дракон" : (boss ? "босс" : "враг");
                    EncounterScalePlugin.Log.LogInfo($"{what} «{unit.info.name}» уровня {unit.Data.level}: "
                        + $"здоровье +{health * 100f:0}%, урон +{damage * 100f:0}%"
                        + (attributes > 0f ? $", характеристики +{attributes:0}" : "")
                        + (armour > 0f ? $", броня +{armour:0}" : "") + ".");
                }

                return true;
            }
            catch (Exception e)
            {
                EncounterScalePlugin.Log.LogError("Усиление по уровню сорвалось: " + e);
                return false;
            }
        }
    }

    // Целиком подменяем родной расчёт, а не дописываем свой следом. Иначе на существо легли
    // бы два баффа с одним именем, а повторное добавление такого баффа заменяет прибавки
    // предыдущего — то есть один из двух расчётов молча пропадал бы, и какой именно, зависело
    // бы от порядка вызовов.
    [HarmonyPatch(typeof(UnitAttribute), "ApplyUnitLevelBonus")]
    internal static class ApplyUnitLevelBonus_Patch
    {
        private static bool Prefix(UnitAttribute __instance)
        {
            return !Difficulty.Apply(__instance);
        }
    }
}
