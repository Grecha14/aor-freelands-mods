using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace DemonLook
{
    /// <summary>
    /// Gives every townsman a bed of his own, and lets him through his own locked door.
    ///
    /// Дома в игре запираются и днём, а пускает запертая дверь только тех, кто вписан хозяином в
    /// саму дверь. Жильцов дома игра туда не вписывает: хозяин упирался бы в собственный порог.
    /// Здесь все жильцы вписываются в двери своего дома — свою дверь проходят, чужую нет.
    ///
    /// Кому дома не досталось, получает место там, где есть свободная кровать: двуспальная —
    /// на двоих; ночлег — и в таверне. Стража живёт в городе, по домам, и получает кровати
    /// первой. Пары, которых игра знает как возлюбленных, селятся вместе; из остальных в семи
    /// домах из десяти селится пара — мужчина и женщина, — прочие как придётся. Лавочникам,
    /// кузнецам и лекарям без дома тоже находится кровать: ночью лавка закрыта, и они спят.
    /// Не селятся отряд, герои и наёмники, трактирщик, пленники и те, кто ходит между локациями.
    ///
    /// Запертая дверь в игре вырезает проход из навигации, стоит ей раз открыться: тогда путь
    /// в дом не строится ни для кого, и жилец стоит у двери. Такой двери проход возвращается —
    /// чужих всё равно остановит сама дверь.
    /// </summary>
    internal static class Homes
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> GuardsInTown;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Homes", "Enabled", true,
                "Give the homeless townsfolk a free bed in a house of their own town, and let every "
                + "resident through the locked door of his own house.");

            GuardsInTown = config.Bind("Homes", "GuardsInTown", true,
                "Let the town guards live in the town houses rather than in the barracks, and get "
                + "their beds first.");

            Couples = config.Bind("Homes", "Couples", 0.7f,
                new ConfigDescription("The share of settled houses that take a couple, a man and a woman.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Inn = config.Bind("Homes", "Inn", true,
                "Let the townsfolk and the guards who find no room in a house sleep in the tavern.");
        }

        internal static ConfigEntry<float> Couples;
        internal static ConfigEntry<bool> Inn;

        private static readonly AccessTools.FieldRef<DoorController, NavMeshObstacle[]> doorObstacles =
            AccessTools.FieldRefAccess<DoorController, NavMeshObstacle[]>("obs");

        /// <summary>Gives a closed door its passage back in the navigation mesh.</summary>
        internal static void Uncarve(DoorController door)
        {
            if (door == null || door.isOpen) return;

            try
            {
                NavMeshObstacle[] obs = doorObstacles(door);
                if (obs == null)
                {
                    obs = door.GetComponentsInChildren<NavMeshObstacle>();
                    doorObstacles(door) = obs;
                }

                foreach (NavMeshObstacle one in obs)
                {
                    if (one != null && one.carving) one.carving = false;
                }
            }
            catch
            {
            }
        }

        /// <summary>Writes every resident of the house into the owners of its doors.</summary>
        internal static void Doors(BuildingController house)
        {
            if (house == null || house.mainDoors == null || house.owners == null) return;

            foreach (DoorController door in house.mainDoors)
            {
                if (door == null) continue;

                foreach (UnitAttribute owner in house.owners)
                {
                    if (owner == null || owner.Data == null || owner.Data.isdead) continue;
                    if (door.owners != null && door.owners.Contains(owner)) continue;

                    try { door.AddOwner(owner); } catch { }
                }
            }
        }

        private static int Spots(BuildingController house)
        {
            int spots = 0;
            if (house.beds == null) return 0;

            foreach (Furniture bed in house.beds)
            {
                if (bed != null && bed.f_type == FunitureType.bed && !bed.isPlayerBed) spots += Mathf.Max(1, bed.sitInfos.Count);
            }
            return spots;
        }

        private static int Living(BuildingController house)
        {
            int n = 0;
            if (house.owners == null) return 0;

            foreach (UnitAttribute owner in house.owners)
            {
                if (owner != null && owner.Data != null && !owner.Data.isdead) n++;
            }
            return n;
        }

        private static bool Settler(UnitAttribute u, Faction place)
        {
            if (!(u is HumaniodUnit) || u.Data == null || u.Data.isdead || u.inParty || Kids.Is(u)) return false;
            if (u.Data.team == Faction.player || u.Data.team != place) return false;
            if (u.isCrossSceneUnit || u.isCaged) return false;

            NPCSaveData npc = u.Data as NPCSaveData;
            if (npc == null || npc.heroCareer != null || npc.lockInParty) return false;

            switch (npc.career)
            {
                case CareerType.Guard:
                case CareerType.Bartender:
                case CareerType.Mercenary:
                case CareerType.Adventurer:
                case CareerType.Bandit:
                    return false;
            }

            try
            {
                if (PartyManager.instance != null && PartyManager.instance.prisoners != null
                    && PartyManager.instance.prisoners.Contains(npc)) return false;
            }
            catch
            {
            }

            return !Crime.QuestPerson(u);
        }

        private static float next;

        internal static void Tick()
        {
            if (Enabled == null || !Enabled.Value) return;
            if ((bool)WorldTravelManager.instance) return;

            VillageManager place = VillageManager.instance;
            if (place == null) return;

            float now = Time.unscaledTime;
            if (now < next) return;
            next = now + 20f;

            try
            {
                Settle(place);
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Дома: расселение не удалось: " + e.Message);
            }
        }

        private static void Settle(VillageManager place)
        {
            List<BuildingController> houses = new List<BuildingController>();
            Dictionary<BuildingController, int> free = new Dictionary<BuildingController, int>();

            foreach (BuildingController house in UnityEngine.Object.FindObjectsOfType<BuildingController>())
            {
                if (house == null || !house.isPrivateBuilding || house.isPlayerHouse) continue;

                Doors(house);

                int room = Spots(house) - Living(house);
                if (room <= 0) continue;

                houses.Add(house);
                free[house] = room;
            }

            // Таверна — ночлег для тех, кому в домах не хватит места.
            BuildingController inn = Inn.Value ? Tavern() : null;
            if (inn != null && !free.ContainsKey(inn))
            {
                int room = Spots(inn) - Living(inn);
                if (room > 0) free[inn] = room;
                else inn = null;
            }

            if (houses.Count == 0 && inn == null) return;

            Barrack barrack = CityTownManager.instance != null ? CityTownManager.instance.barrack : null;
            BuildingController quarters = barrack != null ? barrack.buildingController : null;

            List<UnitAttribute> guards = new List<UnitAttribute>();
            List<UnitAttribute> homeless = new List<UnitAttribute>();

            foreach (AIBehaviorController brain in UnityEngine.Object.FindObjectsOfType<AIBehaviorController>())
            {
                if (brain == null) continue;
                UnitAttribute u = brain.GetComponent<UnitAttribute>();
                if (u == null || u.Data == null) continue;

                if (GuardsInTown.Value && barrack != null && barrack.guards != null && barrack.guards.Contains(u))
                {
                    if (u.Data.isdead || u.inParty || u.Data.team != place.faction) continue;
                    if (brain.home == null || brain.home == quarters) guards.Add(u);
                    continue;
                }

                if (brain.home == null && Settler(u, place.faction)) homeless.Add(u);
            }

            guards.Sort((a, b) => a.Data.id.CompareTo(b.Data.id));
            homeless.Sort((a, b) => a.Data.id.CompareTo(b.Data.id));

            // Все, кого селим: стража впереди.
            List<UnitAttribute> all = new List<UnitAttribute>(guards);
            all.AddRange(homeless);
            HashSet<UnitAttribute> waiting = new HashSet<UnitAttribute>(all);

            int settled = 0;

            // Возлюбленные — вместе: к тому, у кого уже есть дом с местом, или вдвоём в новый.
            foreach (UnitAttribute u in all)
            {
                if (!waiting.Contains(u)) continue;

                UnitAttribute lover = Lover(u);
                if (lover == null) continue;
                AIBehaviorController loverBrain = lover.aiController as AIBehaviorController;

                if (loverBrain != null && loverBrain.home != null && loverBrain.home.isPrivateBuilding
                    && free.ContainsKey(loverBrain.home) && free[loverBrain.home] > 0)
                {
                    if (Move(u, loverBrain.home, free)) settled++;
                    waiting.Remove(u);
                    continue;
                }

                if (waiting.Contains(lover))
                {
                    BuildingController nest = Roomiest(houses, free, 2, u.transform.position);
                    if (nest == null) continue;
                    if (Move(u, nest, free)) settled++;
                    if (Move(lover, nest, free)) settled++;
                    waiting.Remove(u);
                    waiting.Remove(lover);
                }
            }

            // Пары: в семи домах из десяти — мужчина и женщина.
            List<UnitAttribute> men = all.FindAll(x => waiting.Contains(x) && Man(x));
            List<UnitAttribute> women = all.FindAll(x => waiting.Contains(x) && !Man(x));
            System.Random dice = new System.Random(unchecked(Souls.Today() * 7919 + houses.Count));

            foreach (BuildingController house in houses)
            {
                if (men.Count == 0 || women.Count == 0) break;

                int room;
                if (!free.TryGetValue(house, out room) || room < 2) continue;
                if (dice.NextDouble() >= Couples.Value) continue;

                UnitAttribute he = men[0], she = women[0];
                men.RemoveAt(0);
                women.RemoveAt(0);
                if (Move(he, house, free)) settled++;
                if (Move(she, house, free)) settled++;
                waiting.Remove(he);
                waiting.Remove(she);
            }

            // Остальные — по одному, куда есть место; не хватило домов — в таверну.
            foreach (UnitAttribute u in all)
            {
                if (!waiting.Contains(u)) continue;

                BuildingController house = Roomiest(houses, free, 1, u.transform.position);
                if (house == null && inn != null && free.ContainsKey(inn) && free[inn] > 0) house = inn;
                if (house == null) break;

                if (Move(u, house, free)) settled++;
                waiting.Remove(u);
            }

            if (settled > 0) DemonLookPlugin.Log.LogInfo($"Дома: расселено {settled}.");
        }

        private static bool Man(UnitAttribute u)
        {
            try { return u.Data.gender == UnitGender.male; }
            catch { return true; }
        }

        /// <summary>The house the tavern stands in, if the town has one.</summary>
        internal static BuildingController Tavern()
        {
            try
            {
                CitytownBarManager bar = CitytownBarManager.instance;
                if (bar == null) return null;

                BuildingController inn = bar.GetComponentInParent<BuildingController>();
                if (inn == null)
                {
                    float best = 30f * 30f;
                    foreach (BuildingController b in UnityEngine.Object.FindObjectsOfType<BuildingController>())
                    {
                        if (b == null || b.isPrivateBuilding || b.isPlayerHouse || b.beds == null || b.beds.Length == 0) continue;
                        float d = (b.transform.position - bar.transform.position).sqrMagnitude;
                        if (d < best) { best = d; inn = b; }
                    }
                }

                if (inn == null || inn.isPrivateBuilding || inn.isPlayerHouse) return null;
                Barrack barrack = CityTownManager.instance != null ? CityTownManager.instance.barrack : null;
                if (barrack != null && (inn == barrack.buildingController || inn == barrack.prison)) return null;
                return inn;
            }
            catch
            {
                return null;
            }
        }

        private static UnitAttribute Lover(UnitAttribute u)
        {
            try
            {
                NPCSaveData npc = u.Data as NPCSaveData;
                if (npc == null || npc.lover < 0 || AreaManager.Instance == null) return null;
                return AreaManager.Instance.FindUnit(npc.lover);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>The house with the most free beds, and of those the nearest.</summary>
        private static BuildingController Roomiest(List<BuildingController> houses, Dictionary<BuildingController, int> free,
            int need, Vector3 near)
        {
            BuildingController best = null;
            int bestRoom = 0;
            float bestFar = float.MaxValue;

            foreach (BuildingController house in houses)
            {
                int room;
                if (house == null || !free.TryGetValue(house, out room) || room < need) continue;

                float far = (house.transform.position - near).sqrMagnitude;
                if (room > bestRoom || (room == bestRoom && far < bestFar))
                {
                    best = house;
                    bestRoom = room;
                    bestFar = far;
                }
            }
            return best;
        }

        private static bool Move(UnitAttribute u, BuildingController house, Dictionary<BuildingController, int> free)
        {
            try
            {
                AIBehaviorController brain = u.aiController as AIBehaviorController;
                if (brain == null) return false;

                brain.bed = null;
                house.AddOwner(u);
                Doors(house);

                // Игра раздаёт жильцам все кровати дома подряд — и ту, что сдана герою, тоже.
                if (brain.bed != null && brain.bed.isPlayerBed) brain.bed = null;

                int room;
                if (free.TryGetValue(house, out room)) free[house] = room - 1;
                return true;
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Дома: не поселился: " + e.Message);
                return false;
            }
        }
    }

    // Новый жилец — сразу и хозяин дверей своего дома.
    [HarmonyPatch(typeof(BuildingController), "AddOwner")]
    internal static class AddOwner_Homes_Patch
    {
        private static void Postfix(BuildingController __instance)
        {
            if (Homes.Enabled == null || !Homes.Enabled.Value) return;
            try { Homes.Doors(__instance); } catch { }
        }
    }

    // Ночная раздача постов кладёт свободных стражников на койки казармы; у кого дом в городе,
    // тот спит дома.
    [HarmonyPatch(typeof(Barrack), "UpdateGuardBehaviorsNight")]
    internal static class GuardsNight_Homes_Patch
    {
        private static void Postfix(Barrack __instance)
        {
            if (Homes.Enabled == null || !Homes.Enabled.Value || !Homes.GuardsInTown.Value) return;

            try
            {
                if (__instance.guards == null) return;
                foreach (UnitAttribute guard in __instance.guards)
                {
                    AIBehaviorController brain = guard != null ? guard.aiController as AIBehaviorController : null;
                    if (brain == null || brain.home == null || brain.home == __instance.buildingController) continue;
                    if (brain.bed != null && __instance.beds != null && __instance.beds.Contains(brain.bed)) brain.bed = null;
                }
            }
            catch
            {
            }
        }
    }
}
