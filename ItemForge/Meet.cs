using System;
using BepInEx.Configuration;
using DuloGames.UI;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// A man is born able to use what he is born with.
    ///
    /// Снаряжение раздаётся по ступеням: грабитель пятого уровня может получить лук второго
    /// яруса, стражник — щит третьего. У каждой вещи есть свои требования — от игры и наши,
    /// от яруса оружия, — а характеристики существа писались под его прежние вещи. Выходило,
    /// что бандит стоял с оружием, которое ему не по силам: не мог им махать, не мог с ним
    /// ходить и скользил по полю, как тень.
    ///
    /// Здесь каждому, кроме своих, характеристики подтягиваются до того, что просят его вещи:
    /// сила и ловкость — оружие, выносливость — щит, разум — посох, и всё это вместе с тем,
    /// что просит доспех. Мастерство в ветке оружия — до того, что нужно, чтобы бить в полную
    /// силу. Только вверх и только до нужного: кто сильнее своих вещей, тем и остаётся.
    /// </summary>
    internal static class Meet
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Meet", "Enabled", true,
                "Raise the stats of everyone outside the party to what the things they wear and "
                + "hold ask of them: the game own requirements of every piece, and what a weapon "
                + "of its tier asks by our reckoning, mastery included. Only upwards.");

            Telling = config.Bind("Meet", "Telling", true,
                "Say in the log, for the first few, whose stats were raised and to what.");
        }

        private static int told;

        /// <summary>Raises a man to what his things ask, if he is not up to it already.</summary>
        internal static void Fit(UnitAttribute who)
        {
            if (Enabled == null || !Enabled.Value || who == null || who.Data == null) return;
            if (who.Data.isdead || who.Data.team == Faction.player || who.inParty) return;

            HumaniodUnit man = who as HumaniodUnit;
            if (man == null || man.equipmentmanger == null || man.equipmentmanger.equipInfos == null) return;

            NPCSaveData mind = who.Data as NPCSaveData;
            if (mind == null || mind.humanAttribute == null) return;

            try
            {
                // Сила, выносливость, ловкость, восприятие, разум, воля — в порядке игры.
                int[] need = new int[6];
                int mastery = 0;
                WeaponType branch = WeaponType.none;

                EquipInfo[] slots = man.equipmentmanger.equipInfos;

                for (int i = 0; i < slots.Length; i++)
                {
                    EquipInfo slot = slots[i];
                    if (slot == null || !slot.IsEquiped() || slot.inventory == null) continue;

                    UIEquipmentInfo gear = slot.inventory.itemInfo as UIEquipmentInfo;
                    if (gear == null) continue;

                    HumanAttribute asks = gear.attributeRquire;
                    if (asks != null)
                    {
                        need[0] = Mathf.Max(need[0], asks.BSstrength);
                        need[1] = Mathf.Max(need[1], asks.BSendurance);
                        need[2] = Mathf.Max(need[2], asks.BSagility);
                        need[3] = Mathf.Max(need[3], asks.BSprecision);
                        need[4] = Mathf.Max(need[4], asks.BSintelligence);
                        need[5] = Mathf.Max(need[5], asks.BSwillpower);
                    }

                    // Оружие и щит — ещё и то, что просит их ярус.
                    if (i > 1) continue;

                    UIWeaponInfo blade = gear as UIWeaponInfo;
                    if (blade == null) continue;

                    Wield.Demand want = Wield.Ask(blade);
                    if (want == null) continue;

                    Ask(need, want.first, want.firstNeed);
                    Ask(need, want.second, want.secondNeed);
                    Ask(need, want.third, want.thirdNeed);

                    if (want.masteryNeed > mastery)
                    {
                        mastery = want.masteryNeed;
                        branch = want.branch;
                    }
                }

                HumanAttribute has = mind.humanAttribute;
                int raised = 0;

                raised += Lift(ref has.BSstrength, ref mind.strength, need[0]);
                raised += Lift(ref has.BSendurance, ref mind.endurance, need[1]);
                raised += Lift(ref has.BSagility, ref mind.agility, need[2]);
                raised += Lift(ref has.BSprecision, ref mind.precision, need[3]);
                raised += Lift(ref has.BSintelligence, ref mind.intelligence, need[4]);
                raised += Lift(ref has.BSwillpower, ref mind.willpower, need[5]);

                int hand = (int)branch;
                if (mastery > 0 && mind.weaponMastery != null && hand >= 0 && hand < mind.weaponMastery.Length
                    && mind.weaponMastery[hand] < mastery)
                {
                    mind.weaponMastery[hand] = mastery;
                    raised++;
                }

                if (raised <= 0) return;

                who.DoUpdateAttribute();

                if (Telling.Value && told < 20)
                {
                    told++;
                    ItemForgePlugin.Log.LogInfo($"«{who.Data.unitname}» подтянут под свои вещи: "
                        + $"сила {has.BSstrength}, вын. {has.BSendurance}, лов. {has.BSagility}, "
                        + $"разум {has.BSintelligence}"
                        + (mastery > 0 ? $", мастерство {branch} {mastery}" : "") + ".");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог подтянуть характеристики под вещи: " + e.Message);
            }
        }

        private static void Ask(int[] need, int which, int much)
        {
            if (which < 0 || which >= need.Length || much <= 0) return;
            if (much > need[which]) need[which] = much;
        }

        /// <summary>Raises the born value to what is needed; the total with it, until the game recounts.</summary>
        private static int Lift(ref int born, ref int total, int need)
        {
            if (need <= 0 || born >= need) return 0;

            int more = need - born;
            born = need;
            total = Mathf.Max(total + more, need);
            return 1;
        }
    }
}
