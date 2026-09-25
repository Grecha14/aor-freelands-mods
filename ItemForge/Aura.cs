using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// The dark glow the set gives off, and the watch that keeps it in step with what is worn.
    ///
    /// Two ways of glowing were tried here. Writing emission into the model's own material works
    /// on weapons and does nothing at all on the character: the shaders this game dresses people
    /// in, RFS_SimpleSkin and RFS_Hair, carry no emission property, so there is nowhere to write.
    /// The armour therefore glows by borrowed particles instead, which is what WeaponVfx does.
    ///
    /// Renderer.material hands back a copy the first time it is touched, so every change here
    /// lands on one character's own instance and never on the shared asset behind it.
    /// </summary>
    internal static class Aura
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Color;
        internal static ConfigEntry<float> Intensity;
        internal static ConfigEntry<bool> OnBody;
        internal static ConfigEntry<int> BodyParts;
        internal static ConfigEntry<bool> ForDemons;

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        // Тела, которым свечение уже выдано: чтобы не трогать материалы каждый кадр.
        private static readonly HashSet<int> litBodies = new HashSet<int>();

        // Последнее замеченное число надетых частей, чтобы не писать одно и то же в лог.
        private static readonly Dictionary<int, int> lastWorn = new Dictionary<int, int>();

        private static float nextCheck;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Aura", "Enabled", true,
                "Keep the forged pieces in step with what is worn: the glow, the blade's size and "
                + "the borrowed effect.");

            Color = config.Bind("Aura", "Color", "#5A0E2D",
                "Colour of the glow, as HTML. Dark shades read as an aura rather than a lamp.");

            Intensity = config.Bind("Aura", "Intensity", 1.6f,
                new ConfigDescription(
                    "How strongly the glow burns. Emission multiplies the colour, so raising this "
                    + "past about 3 washes the shade out towards white.",
                    new AcceptableValueRange<float>(0f, 10f)));

            OnBody = config.Bind("Aura", "OnBody", false,
                "Light the wearer's body by its own materials. Off, because the character shaders "
                + "in this game — RFS_SimpleSkin and RFS_Hair — carry no emission property at all, "
                + "so there is nothing to write to and the attempt is silent. The armour glow is "
                + "done with borrowed particles instead.");

            ForDemons = config.Bind("Aura", "ForDemons", true,
                "Give the aura to every demon, whatever they are wearing, instead of only to those "
                + "in the set's armour. The glow then belongs to the race rather than to a suit of "
                + "armour that can be taken off.");

            BodyParts = config.Bind("Aura", "BodyParts", 1,
                new ConfigDescription(
                    "Pieces that must be worn before the body is lit, when OnBody is on at all.",
                    new AcceptableValueRange<int>(1, 9)));
        }

        private static Color Shade()
        {
            Color parsed;
            if (!ColorUtility.TryParseHtmlString(Color.Value, out parsed))
            {
                ItemForgePlugin.Log.LogWarning($"Не разобрал цвет ауры '{Color.Value}', беру тёмно-красный.");
                parsed = new Color(0.35f, 0.05f, 0.18f);
            }
            return parsed * Intensity.Value;
        }

        /// <summary>Lights every renderer under a node, or puts it out again.</summary>
        internal static void Paint(Transform root, bool lit)
        {
            if (root == null) return;

            try
            {
                Color shade = lit ? Shade() : UnityEngine.Color.black;

                int painted = 0, skipped = 0;
                HashSet<string> refused = new HashSet<string>();

                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (Material material in renderer.materials)
                    {
                        if (material == null) continue;

                        // Шейдер может не иметь свечения вовсе — тогда писать некуда, и молчать
                        // об этом нельзя: снаружи это выглядит как «аура не работает».
                        if (!material.HasProperty(EmissionColor))
                        {
                            skipped++;
                            if (material.shader != null) refused.Add(material.shader.name);
                            continue;
                        }

                        if (lit) material.EnableKeyword("_EMISSION");
                        material.SetColor(EmissionColor, shade);
                        material.globalIlluminationFlags = lit
                            ? MaterialGlobalIlluminationFlags.RealtimeEmissive
                            : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                        painted++;
                    }
                }

                if (painted > 0 || skipped > 0)
                {
                    ItemForgePlugin.Log.LogInfo($"Аура на «{root.name}»: {(lit ? "зажечь" : "погасить")}, "
                        + $"материалов со свечением {painted}, без него {skipped}"
                        + (refused.Count > 0 ? " — шейдеры: " + string.Join(", ", new List<string>(refused).ToArray()) : "")
                        + ".");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Свечение не легло: " + e);
            }
        }

        /// <summary>
        /// Keeps every party member's gear in step, a few times a second.
        ///
        /// The weapons are re-examined here rather than only when the game hangs a model on the
        /// hand: at that moment the model does not yet know which item it is, so a check made
        /// then reads a forged blade as somebody else's. Asking again a moment later costs
        /// nothing and is always right.
        /// </summary>
        internal static void Tick()
        {
            if (!Enabled.Value) return;
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.5f;

            try
            {
                PartyManager party = PartyManager.instance;
                if (party == null || party.partyMembers == null) return;

                foreach (HumaniodUnit unit in party.partyMembers)
                {
                    if (unit == null || unit.equipmentmanger == null) continue;

                    int key = unit.GetInstanceID();
                    int worn = Forge.WornCount(unit);

                    int last;
                    if (!lastWorn.TryGetValue(key, out last) || last != worn)
                    {
                        lastWorn[key] = worn;
                        ItemForgePlugin.Log.LogInfo($"На {unit.Data.unitname} надето частей комплекта: {worn}.");
                    }

                    EquipWeaponModel_Patch.Refresh(unit.equipmentmanger);

                    if (!OnBody.Value) continue;

                    bool shouldGlow = worn >= BodyParts.Value;
                    bool glowing = litBodies.Contains(key);
                    if (shouldGlow == glowing) continue;

                    Paint(unit.transform, shouldGlow);
                    if (shouldGlow) litBodies.Add(key); else litBodies.Remove(key);
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Проверка ауры сорвалась: " + e);
            }
        }

        /// <summary>True for a demon, by the race stored on the character rather than by gear.</summary>
        private static bool IsDemon(HumaniodUnit unit)
        {
            return unit != null && unit.Data != null && unit.Data.race == UnitRace.demon;
        }

        /// <summary>How many of the set's three armour pieces are on: helmet, cuirass, greaves.</summary>
        private static int ArmourCount(HumaniodUnit unit)
        {
            HashSet<UIEquipmentInfo> mine = Forge.Gather(unit);
            if (mine == null) return 0;

            int worn = 0;
            foreach (UIEquipmentInfo piece in mine)
            {
                if (piece.EquipType == EquipSlotType.head
                    || piece.EquipType == EquipSlotType.chest
                    || piece.EquipType == EquipSlotType.pants) worn++;
            }
            return worn;
        }

        /// <summary>Everything the party wears, written out once so the state is not a guess.</summary>
        internal static void ReportWorn()
        {
            try
            {
                PartyManager party = PartyManager.instance;
                if (party == null || party.partyMembers == null) return;

                foreach (HumaniodUnit unit in party.partyMembers)
                {
                    if (unit == null || unit.equipmentmanger == null) continue;

                    ItemForgePlugin.Log.LogInfo($"--- надето на {unit.Data.unitname} ---");

                    Show("equips", unit.equipmentmanger.equips);
                    Show("standByWeapons", unit.equipmentmanger.standByWeapons);
                    Show("equipInfos", unit.equipmentmanger.equipInfos);
                    Show("standByWeaponInfos", unit.equipmentmanger.standByWeaponInfos);

                    ItemForgePlugin.Log.LogInfo($"  итого наших частей: {Forge.WornCount(unit)}");

                    // Школы и таланты печатаем здесь же: панель «Сорт» строится из списка школ
                    // персонажа, и увидеть его глазами короче, чем рассуждать, дошла ли запись.
                    NPCSaveData npc = unit.Data as NPCSaveData;
                    if (npc != null && npc.skillSet != null)
                    {
                        List<string> sets = new List<string>();
                        foreach (SkillSet set in npc.skillSet) sets.Add(set.ToString());
                        ItemForgePlugin.Log.LogInfo($"  школы: [{string.Join(", ", sets.ToArray())}]");
                    }
                    else ItemForgePlugin.Log.LogInfo("  школы: списка нет");

                    if (unit.talentmanger != null && unit.talentmanger.talents != null)
                    {
                        ItemForgePlugin.Log.LogInfo($"  талантов: {unit.talentmanger.talents.Count}");
                    }

                    if (unit.talentmanger != null && unit.talentmanger.traits != null)
                    {
                        List<string> marks = new List<string>();
                        foreach (UITalentInfo trait in unit.talentmanger.traits)
                        {
                            if (trait != null) marks.Add($"[{trait.ID}] {trait.name}");
                        }
                        ItemForgePlugin.Log.LogInfo($"  черты: " + string.Join(", ", marks.ToArray()));
                    }
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог перечислить надетое: " + e);
            }
        }

        private static void Show(string where, UIEquipmentInfo[] slots)
        {
            if (slots == null) { ItemForgePlugin.Log.LogInfo($"  {where}: нет"); return; }

            foreach (UIEquipmentInfo eq in slots)
            {
                if (eq == null) continue;
                ItemForgePlugin.Log.LogInfo($"  {where} / {eq.EquipType}: [{eq.ID}] «{eq.Name}»"
                    + (Forge.Built.Contains(eq) ? " — НАШЕ" : ""));
            }
        }

        private static void Show(string where, EquipInfo[] slots)
        {
            if (slots == null) { ItemForgePlugin.Log.LogInfo($"  {where}: нет"); return; }

            foreach (EquipInfo slot in slots)
            {
                if (slot == null || slot.inventory == null || slot.inventory.itemInfo == null) continue;

                UIItemInfo item = slot.inventory.itemInfo;
                ItemForgePlugin.Log.LogInfo($"  {where} / {slot.slotType}: [{item.ID}] «{item.Name}»"
                    + (Forge.Built.Contains(item as UIEquipmentInfo) ? " — НАШЕ" : ""));
            }
        }
    }
}
