using System;
using BepInEx.Configuration;
using DuloGames.UI;
using spell;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// The Defender school teaches a man to move in plate.
    ///
    /// Школа «Защитник» прежде давала два процента к сопротивлениям рубящему, дробящему и
    /// колющему за очко; эти сопротивления сняты у всего в игре, и школа осталась пустой. Теперь
    /// она учит ходить и бить в латах: каждое её очко снимает пять процентов со штрафа груза к
    /// скорости передвижения и атаки — в той доле, в какой груз состоит из тяжёлой брони.
    ///
    /// Штраф × (1 − 0,05 × очки × доля лат в грузе). Груз 50 кг, из них латы 40, штраф −40 %;
    /// при пяти очках: −40 % × (1 − 0,25 × 0,8) = −32 %. Мешок за спиной школа не облегчает:
    /// она про доспех. Уклонение, блок и расход выносливости остаются, как были.
    /// </summary>
    internal static class Bulwark
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> PerPoint;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Bulwark", "Enabled", true,
                "Let each point of the Defender school take a share off the load penalty to "
                + "movement and attack speed, in the share the load is made of heavy armour.");

            PerPoint = config.Bind("Bulwark", "PerPoint", 0.05f,
                new ConfigDescription("How much of the speed penalty each Defender point takes off, "
                    + "for a load made wholly of heavy armour.",
                    new AcceptableValueRange<float>(0f, 0.2f)));
        }

        internal static int Points(HumaniodUnit who)
        {
            if (who == null || who.talentmanger == null) return 0;

            TalentBase school = who.talentmanger.FindTalent("Defender");
            return school != null ? Mathf.Max(0, school.level) : 0;
        }

        /// <summary>How much of the load is heavy armour, from none to all of it.</summary>
        internal static float Share(HumaniodUnit who)
        {
            if (who == null || who.equipmentmanger == null || who.equipmentmanger.equipInfos == null) return 0f;

            float load = Burden.Load(who);
            if (load <= 0.01f) return 0f;

            float plate = 0f;
            foreach (EquipInfo worn in who.equipmentmanger.equipInfos)
            {
                if (worn == null || !worn.IsEquiped() || worn.inventory == null) continue;

                UIArmorInfo armour = worn.inventory.itemInfo as UIArmorInfo;
                if (armour != null && armour.armourType == ArmourType.Heavy) plate += Mathf.Max(0f, armour.weight);
            }

            return Mathf.Clamp01(plate / load);
        }

        /// <summary>What is left of the speed penalty: 1 when the school takes nothing off.</summary>
        internal static float Ease(HumaniodUnit who)
        {
            if (Enabled == null || !Enabled.Value || who == null) return 1f;

            int points = Points(who);
            if (points <= 0) return 1f;

            float share = Share(who);
            if (share <= 0f) return 1f;

            return Mathf.Clamp01(1f - PerPoint.Value * points * share);
        }

        private static bool told;
        private static float tried;

        /// <summary>The school description says what the school now does, once the table is up.</summary>
        internal static void Tick()
        {
            if (told || Enabled == null || !Enabled.Value) return;

            float now = Time.unscaledTime;
            if (now - tried < 1f && now >= tried) return;
            tried = now;

            try
            {
                const string key = "Skill_Defender_Defender_Des1";
                string was = Tongue.Get(key);
                if (string.IsNullOrEmpty(was) || was == key) return;

                int per = Mathf.RoundToInt(PerPoint.Value * 100f);
                Tongue.Put(key, "Учит ходить и бить в тяжёлой броне: каждое очко школы снимает " + per
                    + "% со штрафа груза к скорости передвижения и атаки — в той доле, в какой груз "
                    + "состоит из тяжёлой брони.");

                told = true;
                ItemForgePlugin.Log.LogInfo("«Защитник»: описание школы переписано. Было: «" + was + "».");
            }
            catch
            {
            }
        }
    }
}
