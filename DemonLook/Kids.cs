using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using spell;
using UnityEngine;

namespace DemonLook
{
    /// <summary>
    /// Children in the houses of the couples: out of harm's way, and nobody's business but their parents'.
    ///
    /// Детских моделей в игре две — у Люси и у Питера. Здесь с них сняты копии под своими номерами:
    /// обычные девочки и мальчики, без их имён, разговоров и сюжетов. В доме, где живёт пара,
    /// растёт от нуля до двух детей; днём они играют у дома, ночью спят дома.
    ///
    /// Ребёнка нельзя ударить, выпить, обокрасть, унести или взять в плен: ни один удар, ни одно
    /// зелье и ни одна команда на него не ложатся. Работы, жалованья и преступлений у детей нет.
    /// </summary>
    internal static class Kids
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> SceneCap;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Kids", "Enabled", true,
                "Let the couples of the towns and villages have children: nobody can harm, rob or carry them off.");

            SceneCap = config.Bind("Kids", "SceneCap", 12,
                new ConfigDescription("The most children a single town or village holds.", new AcceptableValueRange<int>(0, 100)));
        }

        internal const int GirlId = 908001;
        internal const int BoyId = 908002;
        private const int LucyId = 1013;
        private const int PeterId = 1006;

        private static UnitInfo girl;
        private static UnitInfo boy;

        internal static bool Is(UnitAttribute u)
        {
            if (u == null) return false;
            int id = u.info != null ? u.info.unitId : (u.Data != null ? u.Data.unitId : -1);
            return id == GirlId || id == BoyId || (u.Data != null && (u.Data.unitId == GirlId || u.Data.unitId == BoyId));
        }

        // ------------------------------------------------------------------ образцы

        /// <summary>Copies of the two child templates under numbers of our own, before any save is read.</summary>
        internal static void Register()
        {
            if (Enabled == null || !Enabled.Value) return;

            UIUnitDatabase db;
            try { db = UIUnitDatabase.Instance; }
            catch { return; }
            if (db == null || db.indexes == null) return;

            girl = Copy(db, LucyId, GirlId, "ChildGirl", "Девочка");
            boy = Copy(db, PeterId, BoyId, "ChildBoy", "Мальчик");
        }

        private static UnitInfo Copy(UIUnitDatabase db, int from, int id, string asset, string title)
        {
            UnitInfo have = db.GetInfoByID(id);
            if (have != null) return have;

            UnitInfo source = db.GetInfoByID(from);
            if (source == null) return null;

            UnitInfo copy = UnityEngine.Object.Instantiate(source);
            copy.name = asset;
            copy.unitId = id;
            copy.unitName = title;
            copy.utype = UnitType.NPC;
            copy.portrait = null;
            copy.linkSameUnitInfo = null;
            copy.canLootCorpse = false;
            copy.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            copy.trophies = new UIItemInfo[0];

            List<UnitInfo> all = new List<UnitInfo>(db.indexes) { copy };
            db.indexes = all.ToArray();
            return copy;
        }

        // ------------------------------------------------------------------ неприкосновенность

        /// <summary>What makes a child a child here: untouchable, silent to strangers, and at home at night.</summary>
        internal static void Guard(UnitAttribute u)
        {
            if (u == null || u.Data == null) return;

            UnitInfo own = u.Data.unitId == GirlId ? girl : u.Data.unitId == BoyId ? boy : null;
            if (own != null) u.info = own;
            u.portrait = null;
            u.immuneAll = true;

            CharacterSaveData d = u.Data;
            d.targetable = false;
            d.hitable = false;
            d.hpLock = true;
            d.canKill = false;
            d.allowattack = false;
            d.hasSight = false;
            d.useCommonNPCDialogue = false;
            d.dialogueNames = new List<string>();

            try
            {
                u.RemoveDialogue();
                UnitDialogueManager talk = u.GetComponent<UnitDialogueManager>();
                if (talk != null) talk.useCommonNPCDialogue = false;
            }
            catch
            {
            }

            try
            {
                AIBehaviorControllerBase ai = u.EnsureAIController();
                if (ai != null)
                {
                    ai.canRest = true;
                    ai.wakeTime = 7;
                    ai.sleepTime = 20;
                    ai.behaviorObjs = new List<AIBehaviorBase>
                    {
                        new AIBehaviorBase
                        {
                            name = "ChildPlay",
                            type = UncombatAIType.wander,
                            canLoop = true,
                            wanderStyle = WanderStyle.around_origin,
                            wanderRange = 3f,
                        },
                    };
                    ai.InitBehaviorObjects();
                }
            }
            catch
            {
            }
        }

        // ------------------------------------------------------------------ рождение

        private static float next;

        internal static void Tick()
        {
            if (Enabled == null || !Enabled.Value || (bool)WorldTravelManager.instance) return;
            VillageManager village = VillageManager.instance;
            if (village == null || AreaManager.Instance == null || SaveLoadManager.Instance == null) return;

            float now = Time.unscaledTime;
            if (now < next) return;
            next = now + 30f;

            if (girl == null || boy == null) Register();
            if (girl == null || boy == null) return;

            try { Grow(village); }
            catch (Exception e) { DemonLookPlugin.Log.LogWarning("Дети: не родились: " + e.Message); }
        }

        private static void Grow(VillageManager village)
        {
            int here = 0;
            foreach (UnitAttribute u in UnityEngine.Object.FindObjectsOfType<UnitAttribute>())
            {
                if (Is(u)) { here++; Guard(u); }
            }
            if (here >= SceneCap.Value) return;

            foreach (BuildingController house in UnityEngine.Object.FindObjectsOfType<BuildingController>())
            {
                if (house == null || !house.isPrivateBuilding || house.isPlayerHouse || house.owners == null) continue;

                int men = 0, women = 0, kids = 0;
                foreach (UnitAttribute o in house.owners)
                {
                    if (o == null || o.Data == null || o.Data.isdead) continue;
                    if (Is(o)) { kids++; continue; }
                    if (!(o is HumaniodUnit) || o.inParty) continue;
                    if (o.Data.gender == UnitGender.male) men++; else women++;
                }
                if (men == 0 || women == 0) continue;

                // Пара — от нуля до двух детей, у каждого дома свой счёт.
                float lot = Crime.Lot(house.GetInstanceID() & 0x7fffffff, 31) ;
                int want = lot < 0.3f ? 0 : lot < 0.7f ? 1 : 2;
                if (kids >= want) continue;

                Born(house, village.faction, lot < 0.5f);
                return;
            }
        }

        private static void Born(BuildingController house, Faction team, bool asGirl)
        {
            UnitInfo kind = asGirl ? girl : boy;
            if (kind == null || kind.Prefab == null) return;

            AreaManager am = AreaManager.Instance;
            am.CheckUnitHolder();
            Vector3 at = house.spawnPoint != null ? house.spawnPoint.position : house.transform.position;

            GameObject go = gameManager.CreateUnit(kind.Prefab, at, Quaternion.identity, am.unitsHolder);
            UnitAttribute u = go != null ? go.GetComponent<UnitAttribute>() : null;
            if (u == null || u.Data == null) return;

            u.info = kind;
            HumaniodUnit h = u as HumaniodUnit;
            if (h != null) h.warPetPrefabs = new UnitInfo[0];

            CharacterSaveData d = u.Data;
            d.unitId = kind.unitId;
            d.instanceId = null;
            d.groupId = 0;
            d.originGroupId = 0;
            d.id = SaveLoadManager.Instance.IdAllocate();

            NPCSaveData npc = d as NPCSaveData;
            if (npc != null)
            {
                npc.career = CareerType.none;
                npc.lover = -1;
                try { d.unitname = RandomName.Get(npc); } catch { d.unitname = kind.unitName; }
            }
            go.name = "Child_" + d.id;
            d.GOName = go.name;

            u.ChangeTeam(team);
            am.AddUnitToScene(u);
            am.onCharacterCreate.Invoke(u);
            am.OnNewUnitSpawn(u);

            Guard(u);
            house.AddOwner(u);
            Homes.Doors(house);

            DemonLookPlugin.Log.LogInfo($"Дети: в доме родился{(asGirl ? "а девочка" : " мальчик")} «{d.unitname}».");
        }
    }

    [HarmonyPatch(typeof(gameManager), "Awake")]
    internal static class GameAwake_Kids_Patch
    {
        private static void Postfix()
        {
            try { Kids.Register(); } catch { }
        }
    }

    [HarmonyPatch(typeof(AreaManager), "DynamicCharacterCreation")]
    internal static class Creation_Kids_Patch
    {
        private static void Prefix()
        {
            try { Kids.Register(); } catch { }
        }
    }

    // Ребёнок из сохранения — снова ребёнок, а не вторая Люси.
    [HarmonyPatch(typeof(SaveLoadManager), "DataRecovery")]
    internal static class Recovery_Kids_Patch
    {
        private static void Postfix(UnitAttribute unit, CharacterSaveData csd)
        {
            try
            {
                if (csd != null && (csd.unitId == Kids.GirlId || csd.unitId == Kids.BoyId)) Kids.Guard(unit);
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "TakeDamage")]
    internal static class Damage_Kids_Patch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(UnitAttribute __instance, ref bool __result)
        {
            if (!Kids.Is(__instance)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "Die")]
    internal static class Die_Kids_Patch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(UnitAttribute __instance)
        {
            return !Kids.Is(__instance);
        }
    }

    [HarmonyPatch(typeof(BuffManager), "AddBuff", new[] { typeof(BuffBase) })]
    internal static class Buff_Kids_Patch
    {
        private static bool Prefix(BuffManager __instance, BuffBase buff)
        {
            try
            {
                UnitAttribute u = __instance.GetComponent<UnitAttribute>();
                if (!Kids.Is(u) || buff == null || buff.buffInfo == null) return true;
                return buff.buffInfo.type == bufftype.positive;
            }
            catch
            {
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "ApplyForceEffect")]
    internal static class Force_Kids_Patch
    {
        private static bool Prefix(UnitAttribute __instance)
        {
            return !Kids.Is(__instance);
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "OnCarryUnit")]
    internal static class Carry_Kids_Patch
    {
        private static bool Prefix(UnitAttribute interactUnit)
        {
            return !Kids.Is(interactUnit);
        }
    }

    // Украсть, унести, казнить, напасть — на ребёнка ни одна такая команда не ложится.
    [HarmonyPatch(typeof(UnitStateMachine), "HandleCommand", new[] { typeof(UnitCommand), typeof(bool) })]
    internal static class Command_Kids_Patch
    {
        private static bool Prefix(UnitCommand command)
        {
            if (command == null || command.target == null) return true;
            switch (command.name)
            {
                case commandsName.steal:
                case commandsName.kill:
                    break;
                default:
                    if (command.name.ToString() != "carry" && command.name.ToString() != "execute") return true;
                    break;
            }

            try
            {
                UnitAttribute target = UnitAttribute.GetUnitFromObject(command.target);
                if (!Kids.Is(target)) return true;
                if (command.name == commandsName.steal) GameController.ShowMessage("GameMessage_TargetCannotSteal");
                return false;
            }
            catch
            {
                return true;
            }
        }
    }

    // Ночные и дневные дела взрослых дому раздаёт на всех жильцов — у детей свои.
    [HarmonyPatch(typeof(BuildingController), "UpdateBehaviors")]
    internal static class Jobs_Kids_Patch
    {
        private static void Postfix(BuildingController __instance)
        {
            try
            {
                if (__instance.owners == null) return;
                foreach (UnitAttribute o in __instance.owners)
                {
                    if (Kids.Is(o)) Kids.Guard(o);
                }
            }
            catch
            {
            }
        }
    }

    // Дети не занимают в деревне мест взрослых: пока игра считает, кого пополнить, их не видно.
    [HarmonyPatch(typeof(VillageManager), "Spawn")]
    internal static class VillageSpawn_Kids_Patch
    {
        private static void Prefix(VillageManager __instance, out List<KeyValuePair<BuildingController, UnitAttribute>> __state)
        {
            __state = new List<KeyValuePair<BuildingController, UnitAttribute>>();
            try
            {
                if (__instance.buildings == null) return;
                foreach (BuildingController b in __instance.buildings)
                {
                    if (b == null || b.owners == null) continue;
                    for (int i = b.owners.Count - 1; i >= 0; i--)
                    {
                        if (!Kids.Is(b.owners[i])) continue;
                        __state.Add(new KeyValuePair<BuildingController, UnitAttribute>(b, b.owners[i]));
                        b.owners.RemoveAt(i);
                    }
                }
            }
            catch
            {
            }
        }

        private static Exception Finalizer(List<KeyValuePair<BuildingController, UnitAttribute>> __state, Exception __exception)
        {
            try
            {
                if (__state != null)
                {
                    foreach (KeyValuePair<BuildingController, UnitAttribute> one in __state)
                    {
                        if (one.Key != null && one.Key.owners != null && !one.Key.owners.Contains(one.Value)) one.Key.owners.Add(one.Value);
                    }
                }
            }
            catch
            {
            }
            return __exception;
        }
    }
}
