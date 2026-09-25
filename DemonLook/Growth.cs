using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace DemonLook
{
    /// <summary>
    /// Everybody grows like a hero: loots the dead, wears what is better and what he may wear,
    /// and learns from the fights he wins.
    ///
    /// После боя в городе или в пещере победитель — не из вашего отряда — обходит тела в пятнадцати
    /// шагах: берёт деньги, зелья и еду, а из снаряжения — то, что лучше надетого и по его рукам.
    /// «По рукам» — как у вас: хватает силы, ловкости и прочего, подходит раса и пол, вещь не
    /// разбита. И по его ремеслу: защитнику тяжёлое, бойцу среднее, дуэлянту и лучнику лёгкое,
    /// магу ткань; оружие — того рода, в котором он мастер. Остальное лежит для вас.
    ///
    /// Кто одолел врага на карте мира, получает опыт, как и в бою вблизи: шесть десятых силы
    /// поверженных на всех победителей. Игра тратит его сама — по навыкам и статам его ремесла.
    /// На ночлеге отряд делится снаряжением: каждому то, что подходит ему лучше.
    /// </summary>
    internal static class Growth
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> LootRange;
        internal static ConfigEntry<float> MapExp;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Growth", "Enabled", true,
                "Let the non-party folk loot the enemies they kill, wear better gear they are able to wear, "
                + "learn from world-map fights and share gear inside their band.");

            LootRange = config.Bind("Growth", "LootRange", 15f,
                new ConfigDescription("How far, in metres, a winner walks to search the dead.", new AcceptableValueRange<float>(1f, 60f)));

            MapExp = config.Bind("Growth", "MapExp", 0.6f,
                new ConfigDescription("What share of the beaten side's power the winners of a world-map fight learn.",
                    new AcceptableValueRange<float>(0f, 5f)));
        }

        internal static bool On()
        {
            return Enabled != null && Enabled.Value;
        }

        // ------------------------------------------------------------------ ремесло и вещи

        internal enum Build { Heavy, Medium, Light, Caster, Any }

        internal static Build Of(NPCSaveData n)
        {
            if (n == null) return Build.Any;
            if (n.skillSet != null)
            {
                if (n.skillSet.Contains(SkillSet.commander) || n.skillSet.Contains(SkillSet.defender)) return Build.Heavy;
                if (n.skillSet.Contains(SkillSet.fighter) || n.skillSet.Contains(SkillSet.berserker) || n.skillSet.Contains(SkillSet.marksman)) return Build.Medium;
                if (n.skillSet.Contains(SkillSet.duelist) || n.skillSet.Contains(SkillSet.rogue) || n.skillSet.Contains(SkillSet.ranger)) return Build.Light;
                foreach (SkillSet s in n.skillSet)
                {
                    int v = (int)s;
                    if (v >= 101 && v <= 113) return Build.Caster;
                }
            }
            return n.isMagician ? Build.Caster : Build.Any;
        }

        private static WeaponType BestWeapon(NPCSaveData n)
        {
            if (n == null || n.weaponMastery == null) return WeaponType.none;
            int best = -1, top = 0;
            for (int i = 1; i < n.weaponMastery.Length; i++)
            {
                if (n.weaponMastery[i] > top) { top = n.weaponMastery[i]; best = i; }
            }
            return best > 0 ? (WeaponType)best : WeaponType.none;
        }

        /// <summary>Whether he may wear it: the same rules the hero is held to, and his own trade.</summary>
        internal static bool Fits(NPCSaveData n, Inventory inv)
        {
            UIEquipmentInfo e = inv != null ? inv.itemInfo as UIEquipmentInfo : null;
            if (n == null || e == null || inv.DurState >= 3 || e.isUnique) return false;
            if (!string.IsNullOrEmpty(e.personRquire) && n.unitname != e.personRquire) return false;
            if (e.gender != UnitGender.none && e.gender != n.gender) return false;
            if (e.race != UnitRace.none && e.race != n.race) return false;
            if (e.attributeRquire != null && n.humanAttribute != null && !(n.humanAttribute >= e.attributeRquire)) return false;

            Build b = Of(n);
            UIArmorInfo armour = e as UIArmorInfo;
            if (armour != null)
            {
                switch (b)
                {
                    case Build.Medium: return armour.armourType != ArmourType.Heavy;
                    case Build.Light: return armour.armourType == ArmourType.Light || armour.armourType == ArmourType.None;
                    case Build.Caster: return armour.armourType == ArmourType.None || armour.armourType == ArmourType.Light;
                }
                return true;
            }

            UIWeaponInfo weapon = e as UIWeaponInfo;
            if (weapon != null && e.EquipType == EquipSlotType.mainhand)
            {
                WeaponType own = BestWeapon(n);
                return own == WeaponType.none || weapon.WeaponType == own;
            }
            return true;
        }

        /// <summary>What a thing is worth in a fight: hurt and pace for a weapon, cover for armour.</summary>
        internal static float Score(Inventory inv)
        {
            if (inv == null || inv.itemInfo == null) return 0f;
            float wear = Mathf.Max(0.05f, inv.DurPercent);

            UIWeaponInfo weapon = inv.itemInfo as UIWeaponInfo;
            if (weapon != null && weapon.damage != null)
            {
                float hurt = 0f;
                foreach (KeyValuePair<DamageType, Damage> d in weapon.damage)
                {
                    if (d.Value != null) hurt += (d.Value.minDamage + d.Value.maxDamage) * 0.5f;
                }
                return hurt * Mathf.Max(0.1f, weapon.AttackSpeed) * wear + weapon.BlockRate;
            }

            UIArmorInfo armour = inv.itemInfo as UIArmorInfo;
            if (armour != null && armour.damageDR != null)
            {
                float cover = 0f;
                foreach (float dr in armour.damageDR) cover += dr;
                return cover * wear;
            }

            return inv.Value;
        }

        private static int SlotFor(UIEquipmentInfo e, EquipmentManager gear)
        {
            if (e == null || gear == null || gear.equipInfos == null) return -1;
            for (int i = 0; i < gear.equipInfos.Length; i++)
            {
                EquipInfo slot = gear.equipInfos[i];
                if (slot == null || slot.slotType != e.EquipType) continue;
                if (e.EquipType == EquipSlotType.finger && slot.IsEquiped() && i == 8) continue;
                return i;
            }
            return -1;
        }

        /// <summary>Puts it on if it is better than what he wears there; what came off goes into his bag.</summary>
        private static bool Wear(HumaniodUnit man, Inventory inv)
        {
            EquipmentManager gear = man.equipmentmanger;
            NPCSaveData n = man.Data;
            UIEquipmentInfo e = inv.itemInfo as UIEquipmentInfo;
            if (gear == null || !gear.isInited || e == null || !Fits(n, inv)) return false;

            // Щит — только к одноручному; двуручное не надевается, пока в левой щит.
            if (e.EquipType == EquipSlotType.offhand)
            {
                UIWeaponInfo main = gear.equipInfos[0].IsEquiped() ? gear.equipInfos[0].inventory.itemInfo as UIWeaponInfo : null;
                if (main != null && main.WeaponType != WeaponType.onehand) return false;
            }

            int slot = SlotFor(e, gear);
            if (slot < 0) return false;

            Inventory old = gear.equipInfos[slot].IsEquiped() ? gear.equipInfos[slot].inventory : null;
            if (old != null && Score(old) >= Score(inv)) return false;

            Nightwatch.Muted = true;
            try
            {
                if (old != null) gear.UnequipItem(slot);
                gear.EquipItem(inv, slot);
            }
            finally
            {
                Nightwatch.Muted = false;
            }

            if (old != null && man.items != null) man.items.AddInventory(old);
            return true;
        }

        // ------------------------------------------------------------------ обыск

        private static bool Looter(UnitAttribute u)
        {
            if (!(u is HumaniodUnit) || u.Data == null || u.Data.isdead || u.inParty || Kids.Is(u)) return false;
            try
            {
                if (PartyManager.instance != null && PartyManager.instance.IsHeldByPlayer(u)) return false;
            }
            catch
            {
            }
            NPCSaveData n = u.Data as NPCSaveData;
            return n != null && !n.lockInParty && (u.info == null || u.info.utype == UnitType.NPC || u.info.utype == UnitType.monster);
        }

        private static readonly Dictionary<UnitAttribute, List<UnitAttribute>> pending = new Dictionary<UnitAttribute, List<UnitAttribute>>();

        private class Search
        {
            internal HumaniodUnit man;
            internal List<UnitAttribute> dead;
            internal float since;
            internal bool walking;
        }

        private static readonly List<Search> searches = new List<Search>();
        private static GameEventManager hooked;

        private static void Hook()
        {
            GameEventManager g = GameEventManager.Instance;
            if (g == null || ReferenceEquals(g, hooked)) return;
            hooked = g;
            g.onUnitKill.AddListener(OnKill);
            g.onDisengage.AddListener(OnDisengage);
        }

        private static void OnKill(UnitAttribute killer, UnitAttribute victim)
        {
            if (!On() || !Looter(killer) || victim == null || victim.inParty || Kids.Is(victim)) return;
            if ((bool)victim.summonComponent) return;

            List<UnitAttribute> list;
            if (!pending.TryGetValue(killer, out list)) pending[killer] = list = new List<UnitAttribute>();
            if (!list.Contains(victim)) list.Add(victim);
            if (pending.Count > 200) pending.Clear();
        }

        private static void OnDisengage(UnitAttribute unit)
        {
            List<UnitAttribute> list;
            if (unit == null || !pending.TryGetValue(unit, out list)) return;
            pending.Remove(unit);

            HumaniodUnit man = unit as HumaniodUnit;
            if (man == null || list.Count == 0) return;
            searches.Add(new Search { man = man, dead = list, since = Time.time });
        }

        private static float next;

        internal static void Tick()
        {
            if (!On()) return;
            Hook();

            float now = Time.time;
            if (now < next || searches.Count == 0) return;
            next = now + 0.5f;

            for (int i = searches.Count - 1; i >= 0; i--)
            {
                Search s = searches[i];
                if (s.man == null || s.man.Data == null || s.man.Data.isdead || s.man.isEngaged || s.dead.Count == 0)
                {
                    searches.RemoveAt(i);
                    continue;
                }

                UnitAttribute corpse = s.dead[0];
                if (corpse == null || corpse.items == null
                    || (corpse.transform.position - s.man.transform.position).magnitude > LootRange.Value)
                {
                    s.dead.RemoveAt(0);
                    s.walking = false;
                    continue;
                }

                float far = (corpse.transform.position - s.man.transform.position).magnitude;
                if (far > 1.8f && now - s.since < 10f)
                {
                    if (!s.walking)
                    {
                        s.walking = true;
                        try { s.man.stateMachine.HandleCommand(new UnitCommand(commandsName.move, corpse.transform.position, 0.6f)); } catch { }
                    }
                    continue;
                }

                try { Take(s.man, corpse); } catch (Exception e) { DemonLookPlugin.Log.LogWarning("Рост: обыск не удался: " + e.Message); }
                s.dead.RemoveAt(0);
                s.walking = false;
                s.since = now;
            }
        }

        private static void Take(HumaniodUnit man, UnitAttribute corpse)
        {
            // Герой уже роется в этом теле — не мешать.
            if (LootManager.instance != null && LootManager.instance.isLooting && LootManager.instance.stocks == corpse.items) return;
            if (man.items == null) return;

            ItemStock bag = corpse.items;
            int took = 0;

            if (bag.money > 0)
            {
                man.items.money += bag.money;
                bag.money = 0;
                took++;
            }

            int potions = 0, food = 0;
            foreach (Inventory inv in new List<Inventory>(bag.items))
            {
                if (inv == null || inv.itemInfo == null) continue;

                UIConsumableInfo drink = inv.itemInfo as UIConsumableInfo;
                if (drink != null)
                {
                    bool potion = drink.consumableType == consumableType.potion;
                    if (potion ? potions >= 5 : food >= 5) continue;
                    if (!potion && drink.hungryRestore <= 0f) continue;
                    bag.items.Remove(inv);
                    man.items.AddInventory(inv);
                    if (potion) potions++; else food++;
                    took++;
                    continue;
                }

                if (!(inv.itemInfo is UIEquipmentInfo) || !Fits(man.Data, inv)) continue;

                // Вещь снимается с тела совсем, чтобы не остаться и на нём, и у нового хозяина.
                HumaniodUnit body = corpse as HumaniodUnit;
                if (body != null && body.equipmentmanger != null && body.equipmentmanger.equipInfos != null)
                {
                    foreach (EquipInfo slot in body.equipmentmanger.equipInfos)
                    {
                        if (slot != null && ReferenceEquals(slot.inventory, inv)) slot.UnEquip();
                    }
                }

                bag.items.Remove(inv);
                if (Wear(man, inv)) took++;
                else bag.items.Add(inv);
            }

            if (took > 0) Crime.Bark(man, "Моё.");
        }

        // ------------------------------------------------------------------ опыт на карте мира

        internal static void Fought(WorldMapFight fight)
        {
            if (!On() || fight == null || fight.winGroup == null) return;

            bool attackerWon = fight.winGroup == fight.attackerGroup;
            List<TravelGroup> won = new List<TravelGroup>();
            List<TravelGroup> lost = new List<TravelGroup>();
            (attackerWon ? won : lost).Add(fight.attackerGroup);
            (attackerWon ? won : lost).AddRange(fight.attackerReinforcements);
            (attackerWon ? lost : won).Add(fight.defenderGroup);
            (attackerWon ? lost : won).AddRange(fight.defenderReinforcements);

            float power = 0f;
            foreach (TravelGroup g in lost)
            {
                if (g == null) continue;
                try { power += g.CalculatePower(); } catch { }
            }
            if (power <= 0f) return;

            List<NPCSaveData> learners = new List<NPCSaveData>();
            foreach (TravelGroup g in won)
            {
                if (g == null || g.isPlayer) continue;
                if (g.leader != null && g.leader.Data is NPCSaveData lead && Learner(g.leader)) learners.Add(lead);
                if (g.members == null) continue;
                foreach (CharacterSaveData m in g.members)
                {
                    NPCSaveData n = m as NPCSaveData;
                    if (n != null && !n.isdead) learners.Add(n);
                }
            }
            if (learners.Count == 0) return;

            int each = Mathf.Max(1, Mathf.RoundToInt(power * MapExp.Value / learners.Count));
            foreach (NPCSaveData n in learners)
            {
                try
                {
                    if (PartyManager.instance != null && PartyManager.instance.IsHeldByPlayer(n)) continue;
                    HumaniodUnit live = n.Unit as HumaniodUnit;
                    if (live != null) live.GainExp(each);
                    else HeroUnitMaker.GainExp(n, each);
                }
                catch
                {
                }
            }
        }

        private static bool Learner(UnitAttribute u)
        {
            return u is HumaniodUnit && !u.inParty && !Kids.Is(u);
        }

        // ------------------------------------------------------------------ делёж в отряде

        /// <summary>At a night's lodging a band shares its gear: each takes from the pack what suits him better.</summary>
        internal static void Share(TravelGroup group)
        {
            if (!On() || group == null || group.members == null || group.members.Count < 2) return;

            List<NPCSaveData> men = new List<NPCSaveData>();
            foreach (CharacterSaveData m in group.members)
            {
                NPCSaveData n = m as NPCSaveData;
                if (n == null || n.isdead || n.equips == null || n.items == null || n.Unit != null) continue;
                try { if (PartyManager.instance != null && PartyManager.instance.IsHeldByPlayer(n)) continue; } catch { }
                men.Add(n);
            }
            if (men.Count < 2) return;

            int[] slots = { 2, 4, 7 };
            foreach (NPCSaveData taker in men)
            {
                foreach (int slot in slots)
                {
                    if (slot >= taker.equips.Length) continue;
                    Inventory now = taker.equips[slot];
                    float have = now != null ? Score(now) : 0f;

                    NPCSaveData giver = null;
                    ItemSaveData best = null;
                    float bestScore = have;
                    foreach (NPCSaveData other in men)
                    {
                        foreach (ItemSaveData isd in other.items)
                        {
                            Inventory inv = isd;
                            UIEquipmentInfo e = inv != null ? inv.itemInfo as UIEquipmentInfo : null;
                            if (e == null || !(e is UIArmorInfo) || (int)e.EquipType != SlotType(slot)) continue;
                            if (!Fits(taker, inv)) continue;
                            float s = Score(inv);
                            if (s > bestScore) { bestScore = s; best = isd; giver = other; }
                        }
                    }
                    if (best == null) continue;

                    giver.items.Remove(best);
                    if (taker.equips[slot] != null)
                    {
                        ItemSaveData off = taker.equips[slot];
                        off.slotIndex = -1;
                        taker.items.Add(off);
                    }
                    best.slotIndex = slot;
                    taker.equips[slot] = best;
                    taker.equipsInited = true;
                    try { taker.CalculatePower(); } catch { }
                }
            }
        }

        private static int SlotType(int slot)
        {
            switch (slot)
            {
                case 2: return (int)EquipSlotType.head;
                case 4: return (int)EquipSlotType.chest;
                case 7: return (int)EquipSlotType.pants;
            }
            return -1;
        }
    }

    [HarmonyPatch(typeof(GlobalEncounterManager), "CalculateBattleResult")]
    internal static class Battle_Growth_Patch
    {
        private static void Prefix(WorldMapFight fight)
        {
            try { Growth.Fought(fight); } catch { }
        }
    }

    // Игра выбирает НПС оружие не того рода, в котором он мастер: сравнивает мастерство с номером.
    // Пока она выбирает, видно только его лучшее мастерство — и выбор выходит верным.
    [HarmonyPatch(typeof(HeroUnitMaker), "UpgradeWeapons")]
    internal static class Weapons_Growth_Patch
    {
        private static void Prefix(NPCSaveData psd, out int[] __state)
        {
            __state = null;
            if (!Growth.On() || psd == null || psd.weaponMastery == null) return;

            int best = -1, top = 0;
            for (int i = 1; i < psd.weaponMastery.Length; i++)
            {
                if (psd.weaponMastery[i] > top) { top = psd.weaponMastery[i]; best = i; }
            }
            if (best < 0) return;

            __state = (int[])psd.weaponMastery.Clone();
            for (int i = 0; i < psd.weaponMastery.Length; i++)
            {
                if (i != best) psd.weaponMastery[i] = 0;
            }
        }

        private static Exception Finalizer(NPCSaveData psd, int[] __state, Exception __exception)
        {
            if (__state != null && psd != null) psd.weaponMastery = __state;
            return __exception;
        }
    }
}
