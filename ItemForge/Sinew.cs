using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// The armour a man is born with: muscle over the body, bone under it.
    ///
    /// Доспех в этой игре — единственное, что стоит между ударом и телом, и голый человек
    /// оттого равен голому человеку: кузнец с плечами в косую сажень принимает нож ровно так
    /// же, как писарь. Между тем мышца держит клинок, а кость держит дубину, и это не образ, а
    /// то, из-за чего в первую очередь перестают драться на кулаках с тем, кто больше.
    ///
    /// Здесь сила даёт стену мышц — больше всего на груди, — а выносливость стену костей, и
    /// голова в ней тоже есть, потому что череп это и есть кость. Считается всё в той же
    /// мерке, что написана на доспехах: сколько урона надо продавить.
    ///
    /// Мера взята пятой долей от задуманной: в полную голый новичок держал грудью тридцать
    /// семь, а кольчужный нагрудник держит тридцать шесть, и вся лестница доспехов до лат
    /// теряла смысл с первого дня. В пятой доле новичок держит семь — чуть меньше стёганки, —
    /// а боец с полусотней силы и выносливости тридцать пять, вровень с кольчугой.
    /// </summary>
    internal static class Sinew
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Flesh;
        internal static ConfigEntry<string> Bone;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Sinew", "Enabled", true,
                "Let a man's own body stop a blow. Without this the naked are all equally naked, "
                + "and a blacksmith takes a knife exactly as a clerk does.");

            Flesh = config.Bind("Sinew", "Flesh", "chest=0.15,legs=0.075,arms=0.075",
                "What one point of strength is worth as armour, by place, in the same reckoning "
                + "written on armour: how much damage has to be pushed through. Muscle, so the "
                + "body has most of it and the skull none at all.");

            Bone = config.Bind("Sinew", "Bone", "head=0.033,chest=0.1,legs=0.033,arms=0.033",
                "And what one point of endurance is worth, by place. Bone: the skull counts "
                + "here, and the ribs count for most.");
        }

        private static readonly Dictionary<string, Dictionary<int, float>> tables =
            new Dictionary<string, Dictionary<int, float>>();
        private static readonly Dictionary<string, string> reads = new Dictionary<string, string>();

        private static Dictionary<int, float> Read(string name, ConfigEntry<string> from)
        {
            string written = from != null ? (from.Value ?? "") : "";

            string was;
            if (reads.TryGetValue(name, out was) && was == written) return tables[name];

            reads[name] = written;

            Dictionary<int, float> table = new Dictionary<int, float>();

            foreach (string one in written.Split(','))
            {
                int split = one.IndexOf('=');
                if (split <= 0) continue;

                float much;
                if (!float.TryParse(one.Substring(split + 1).Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out much)) continue;

                int slot = Anatomy.Where(one.Substring(0, split).Trim());
                if (slot >= 0) table[slot] = much;
            }

            tables[name] = table;
            return table;
        }

        /// <summary>What this man's own body stops at this place, in damage.</summary>
        internal static float Wall(UnitAttribute who, int slot)
        {
            if (Enabled == null || !Enabled.Value) return 0f;

            try
            {
                // У зверя своя шкура, и считает её «Beastly». Здесь только те, у кого есть
                // шесть характеристик.
                HumaniodUnit man = who as HumaniodUnit;
                if (man == null || man.Data == null) return 0f;

                float much = 0f;

                float mine;
                if (Read("flesh", Flesh).TryGetValue(slot, out mine)) much += man.Strength * mine;

                // Порода даёт кость сверх собственной: орочья шкура и гномья кость считаются
                // так, будто выносливости у них на столько-то больше — но только на кость.
                if (Read("bone", Bone).TryGetValue(slot, out mine))
                {
                    much += (man.Endurance + Blood.Of(man, "bone")) * mine;
                }

                return much > 0f ? much : 0f;
            }
            catch
            {
                return 0f;
            }
        }
    }
}
