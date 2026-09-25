using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;

namespace EncounterScale
{
    /// <summary>
    /// Teaches the great creatures to fight with more than their statistics.
    ///
    /// A multiplier on health and damage makes a boss take longer to kill and hurt more while
    /// doing it, and nothing else: the fight has the same shape as before, only slower. Skills
    /// change the shape. The game will use them without being asked — SpellManager.AutoCastSpell
    /// walks a unit's own spell list every so often and casts whatever fits the moment, so a
    /// spell handed to a boss is a spell that boss will actually throw.
    ///
    /// Attributes for the casting come from elsewhere and deliberately so: everything in this
    /// section's reach is already carried by the level buff, where an apex creature is given
    /// thirty points in every attribute at once — intelligence and willpower among them. Adding
    /// them again here would be paying twice for the same thing.
    /// </summary>
    internal static class Abilities
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> DragonSkills;
        internal static ConfigEntry<string> RotKingSkills;
        internal static ConfigEntry<string> BossSkills;
        internal static ConfigEntry<int> SkillLevel;
        internal static ConfigEntry<int> BossLevel;
        internal static ConfigEntry<int> DragonLevel;

        // Кого уже обучили. Расчёт характеристик повторяется часто, а выдача заклинаний —
        // работа разовая: без этой отметки список проверялся бы по нескольку раз в секунду.
        private static readonly HashSet<int> taught = new HashSet<int>();

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Skills", "Enabled", false,
                "Give the great creatures schools of their own.");

            DragonSkills = config.Bind("Skills", "DragonSkills", "unarmed,berserker",
                "Schools for a dragon, separated by commas. Unarmed and berserker suit a beast "
                + "that carries no weapon and fights by rage rather than by drill.");

            RotKingSkills = config.Bind("Skills", "RotKingSkills", "earth",
                "Schools for TheKingOfRot, which is an apex creature but not a dragon and so is "
                + "given its own. Earth for a thing that rules over decay and rising ground.");

            BossSkills = config.Bind("Skills", "BossSkills", "fighter,fire",
                "Schools for an ordinary boss: one of arms and one of magic, so that a boss can "
                + "answer a fight it is losing instead of only swinging harder.");

            SkillLevel = config.Bind("Skills", "SkillLevel", 3,
                new ConfigDescription(
                    "What level the granted spells are known at, where nothing more exact is "
                    + "said below.",
                    new AcceptableValueRange<int>(1, 15)));

            BossLevel = config.Bind("Skills", "BossLevel", 12,
                new ConfigDescription(
                    "What rank a boss knows its schools at, out of ten. Eight: a boss has spent "
                    + "a life on this and is better at it than anyone the player has met, but "
                    + "the last two ranks are somebody else's — and now that a rank is worth "
                    + "nearly half again in damage, the difference between eight and ten is a "
                    + "real distance rather than a decoration.",
                    new AcceptableValueRange<int>(1, 15)));

            DragonLevel = config.Bind("Skills", "DragonLevel", 15,
                new ConfigDescription(
                    "What rank a dragon knows its schools at. Ten, and there is nothing above "
                    + "it: a dragon did not study fire, it is what fire studied.",
                    new AcceptableValueRange<int>(1, 15)));
        }

        private static string Wanted(UnitAttribute unit, bool dragon, bool boss)
        {
            if (!dragon && !boss) return null;

            string asset = unit.info.name ?? "";

            if (asset.IndexOf("KingOfRot", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return RotKingSkills.Value;
            }

            return dragon ? DragonSkills.Value : BossSkills.Value;
        }

        internal static void Grant(UnitAttribute unit, bool dragon, bool boss)
        {
            if (!Enabled.Value || unit == null || unit.info == null) return;

            string wanted = Wanted(unit, dragon, boss);
            if (string.IsNullOrEmpty(wanted)) return;

            if (!taught.Add(unit.GetInstanceID())) return;

            try
            {
                foreach (string entry in wanted.Split(','))
                {
                    string name = entry.Trim();
                    if (name.Length == 0) continue;

                    SkillSet school;
                    try { school = (SkillSet)Enum.Parse(typeof(SkillSet), name, true); }
                    catch
                    {
                        EncounterScalePlugin.Log.LogWarning($"Школы «{name}» в игре нет, пропускаю.");
                        continue;
                    }

                    Teach(unit, school, dragon ? DragonLevel.Value : (boss ? BossLevel.Value : SkillLevel.Value));
                }
            }
            catch (Exception e)
            {
                EncounterScalePlugin.Log.LogError("Обучение босса сорвалось: " + e);
            }
        }

        private static void Teach(UnitAttribute unit, SkillSet school, int rank)
        {
            int given = 0;

            // Мастерство школы выдаём первым: без него заклинания и приёмы считаются
            // неосвоенными, и существо будет владеть ими хуже, чем задумано.
            UITalentDatabase tdb = UITalentDatabase.Instance;
            if (tdb != null && unit.talentmanger != null)
            {
                UITalentInfo mastery = tdb.GetMasteryTalent(school);
                if (mastery != null && !unit.talentmanger.ContainTalent(mastery.Name))
                {
                    unit.talentmanger.AddTalent(mastery, rank);
                }
            }

            UISpellDatabase sdb = UISpellDatabase.Instance;
            if (sdb != null && sdb.spells != null && unit.spellmanger != null)
            {
                foreach (UISpellInfo spell in sdb.spells)
                {
                    if (spell == null || spell.SkillSet != school) continue;
                    if (unit.spellmanger.ContainSpell(spell)) continue;

                    unit.spellmanger.AddSpell(spell, rank);
                    given++;
                }
            }

            EncounterScalePlugin.Log.LogInfo($"«{unit.info.name}» обучен школе «{school}»: "
                + $"заклинаний и приёмов {given}.");
        }
    }
}
