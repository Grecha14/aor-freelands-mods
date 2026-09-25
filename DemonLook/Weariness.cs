using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using spell;
using UnityEngine;

namespace DemonLook
{
    /// <summary>
    /// Travellers of the world map tire and lie down for the night: under a roof if one is near,
    /// where they stand if they can go no further.
    ///
    /// Каждый отряд людей на карте мира — караван, отряд героев, дозор, шайка — копит часы без
    /// сна. С шестнадцати часов он устал, с двадцати устал сильнее, с суток валится с ног: на
    /// каждом из отряда та же усталость, что бывает у героя, первого, второго и третьего уровня,
    /// а идёт отряд на десятую часть медленнее, потом на пятую.
    ///
    /// Усталый отряд сворачивает на ночлег в ближайший город или деревню, если до них не больше
    /// четырёх часов пути; шайка в город не пойдёт — она уходит в ближайшее логово или руины.
    /// Там он спит восемь–десять часов, платит трактиру и ест с его склада, а потом идёт своей
    /// дорогой дальше. Кто не дошёл до ночлега и дотянул до суток без сна, встаёт лагерем там,
    /// где стоит, на восемь часов. Отряд задания и охотники за головами спят только на месте:
    /// уйди они на ночлег — их бы не нашли там, где ищут. Звери и чудища спят по-своему.
    /// </summary>
    internal static class Weariness
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Levels;
        internal static ConfigEntry<string> Slow;
        internal static ConfigEntry<float> Reach;
        internal static ConfigEntry<string> Night;
        internal static ConfigEntry<int> Camp;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Weariness", "Enabled", true,
                "Let the travellers of the world map tire and lie down for the night, in a town nearby or where they stand; "
                + "let the guard sleep by day after a night on duty, and a townsman two nights without sleep lie down where he is.");

            Levels = config.Bind("Weariness", "Levels", "16,20,24",
                "Hours without sleep for the first, second and third level of weariness. At the first a group looks for a roof; "
                + "at the third it camps where it stands.");

            Slow = config.Bind("Weariness", "Slow", "0.1,0.2",
                "How much slower a weary group walks at the first and at the second level.");

            Reach = config.Bind("Weariness", "Reach", 4f,
                new ConfigDescription("Hours of road a weary group will go for a bed.", new AcceptableValueRange<float>(0f, 24f)));

            Night = config.Bind("Weariness", "Night", "8,10",
                "Hours a group sleeps under a roof: the fewest and the most.");

            Camp = config.Bind("Weariness", "Camp", 8,
                new ConfigDescription("Hours a group sleeps where it stands.", new AcceptableValueRange<int>(1, 24)));
        }

        internal static bool On()
        {
            return Enabled != null && Enabled.Value;
        }

        private enum Mode { Awake, Heading, Lodged, Camped }

        private class Traveller
        {
            internal int id;
            internal float awake;
            internal Mode mode;
            internal string inn;
            internal float until;
            internal float since;
            internal float nudged = -100f;
            internal int level = -1;
            internal bool aiWas = true;
            internal Vector3 last;
            internal bool seen;
        }

        private static readonly Dictionary<int, Traveller> known = new Dictionary<int, Traveller>();

        // Сколько отряд проходит за час дороги — по тому, сколько они на деле проходят.
        private static float pace;

        private static float Now()
        {
            try { return TimeManager.TotalDay * 24f + TimeManager.Hour + TimeManager.Instance.GameTime.Minutes / 60f; }
            catch { return -1f; }
        }

        private static float[] Floats(string s, float[] fallback)
        {
            float[] v = (float[])fallback.Clone();
            string[] parts = (s ?? "").Split(',');
            for (int i = 0; i < v.Length && i < parts.Length; i++)
            {
                float x;
                if (float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x) && x >= 0f) v[i] = x;
            }
            return v;
        }

        private static int Level(float awake)
        {
            float[] l = Floats(Levels.Value, new[] { 16f, 20f, 24f });
            if (awake >= l[2]) return 3;
            if (awake >= l[1]) return 2;
            if (awake >= l[0]) return 1;
            return 0;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        // ------------------------------------------------------------------ кто спит

        private static bool Sleeper(TravelGroup g)
        {
            if (g == null || g.isPlayer || g.isCarriage || g.isFollowing || g.isCaptured) return false;
            UnitAttribute lead = g.leader;
            return lead != null && lead.Data != null && !lead.Data.isdead && lead is HumaniodUnit;
        }

        // Их ищут там, где они есть: такие уходить на ночлег не могут.
        private static bool Anchored(TravelGroup g)
        {
            return g.isQuestGroup || g.bountyTargetTown != null;
        }

        private static bool Outlaw(TravelGroup g)
        {
            return g.faction == Faction.outlaw || (g.leader.Data != null && g.leader.Data.team == Faction.outlaw);
        }

        private static Traveller Of(TravelGroup g)
        {
            Traveller t;
            if (!known.TryGetValue(g.id, out t))
            {
                // Кого видим впервые, тот уже сколько-то в пути: у каждого своё, от нуля до двенадцати часов.
                t = new Traveller { id = g.id, awake = Mathf.Floor(Crime.Lot(g.id, 17) * 12f) };
                known[g.id] = t;
                dirty = true;
            }
            return t;
        }

        // ------------------------------------------------------------------ час

        /// <summary>An hour of a group's road: it tires, and at length it goes to bed.</summary>
        internal static void Hour(TravelGroup g, int passed)
        {
            if (!On() || !Sleeper(g) || WorldTravelManager.instance == null) return;
            Sync();
            Traveller t = Of(g);
            int hours = Mathf.Max(1, passed);

            if (t.mode == Mode.Lodged || t.mode == Mode.Camped)
            {
                if (Now() >= t.until) Wake(g, t);
                return;
            }

            // Спрятан в городе самой игрой — отряд героев на отдыхе: это и есть ночь под крышей.
            if (!g.leader.gameObject.activeSelf)
            {
                if (t.awake > 0f) { t.awake = 0f; dirty = true; }
                t.seen = false;
                Card(g, t, 0);
                return;
            }

            Measure(g, t, hours);
            t.awake += hours;
            dirty = true;

            int level = Level(t.awake);
            Card(g, t, level);
            if (g.currentBattle != null) return;

            if (level >= 3) { Lie(g, t); return; }
            if (level < 1) return;

            if (t.mode == Mode.Awake)
            {
                if (Anchored(g)) { Lie(g, t); return; }
                WorldPlace inn = Inn(g);
                if (inn != null) Head(g, t, inn);
            }
            else if (t.mode == Mode.Heading && Now() - t.since > Reach.Value + 3f)
            {
                // Не дошёл, куда шёл: ночует там, где стоит.
                Lie(g, t);
            }
        }

        private static void Measure(TravelGroup g, Traveller t, int hours)
        {
            Vector3 at = g.leader.transform.position;
            if (t.seen && hours == 1 && t.mode == Mode.Awake)
            {
                float moved = Flat(at - t.last).magnitude;
                if (moved > 0.05f && moved < 1000f) pace = pace <= 0f ? moved : pace * 0.95f + moved * 0.05f;
            }
            t.last = at;
            t.seen = true;
        }

        // ------------------------------------------------------------------ ночлег

        /// <summary>The nearest roof within the hours a weary group will walk: a town or a village, or for outlaws a lair.</summary>
        private static WorldPlace Inn(TravelGroup g)
        {
            WorldTravelManager wtm = WorldTravelManager.instance;
            float reach = pace * Reach.Value;
            if (reach <= 0f || wtm == null) return null;

            bool outlaw = Outlaw(g);
            Vector3 at = g.leader.transform.position;
            WorldPlace best = null;
            float least = reach * reach;
            foreach (WorldPlace p in Places(wtm))
            {
                bool fits = outlaw
                    ? p.areaType == WorldPlaceType.wildArea || p.areaType == WorldPlaceType.dungeon
                    : p.areaType == WorldPlaceType.city || p.areaType == WorldPlaceType.village;
                if (!fits) continue;

                float d = Flat(Door(p) - at).sqrMagnitude;
                if (d < least) { least = d; best = p; }
            }
            return best;
        }

        private static IEnumerable<WorldPlace> Places(WorldTravelManager wtm)
        {
            HashSet<WorldPlace> seen = new HashSet<WorldPlace>();
            foreach (List<WorldPlace> list in new[] { wtm.worldPlaces, wtm.dungeons, wtm.wildPlaces })
            {
                if (list == null) continue;
                foreach (WorldPlace p in list)
                {
                    if (p != null && seen.Add(p)) yield return p;
                }
            }
            if (wtm.worldTowns != null)
            {
                foreach (WorldTown p in wtm.worldTowns)
                {
                    if (p != null && seen.Add(p)) yield return p;
                }
            }
        }

        private static WorldPlace Find(string name)
        {
            WorldTravelManager wtm = WorldTravelManager.instance;
            if (wtm == null || string.IsNullOrEmpty(name)) return null;
            foreach (WorldPlace p in Places(wtm))
            {
                if (p.name == name) return p;
            }
            return null;
        }

        private static Vector3 Door(WorldPlace p)
        {
            if (p.interactPoints != null && p.interactPoints.Length > 0 && p.interactPoints[0] != null) return p.interactPoints[0].position;
            return p.transform.position;
        }

        private static void Remember(TravelGroup g, Traveller t)
        {
            AIBehaviorControllerBase ai = g.leader.aiController;
            t.aiWas = ai == null || ai.enabled;
        }

        // Пока отряд идёт спать и спит, своя голова у вожака молчит: иначе он тут же вернётся на дорогу.
        private static void Hold(TravelGroup g)
        {
            AIBehaviorControllerBase ai = g.leader.aiController;
            if (ai != null && ai.enabled) ai.enabled = false;
        }

        private static void Release(TravelGroup g, Traveller t)
        {
            AIBehaviorControllerBase ai = g.leader.aiController;
            if (ai != null && t.aiWas && !ai.enabled) ai.enabled = true;
        }

        private static void Go(TravelGroup g, Traveller t, Vector3 to)
        {
            t.nudged = Time.unscaledTime;
            try
            {
                g.leader.stateMachine.HandleStop();
                g.leader.stateMachine.HandleCommand(new UnitCommand(commandsName.move, to, 0.5f));
            }
            catch
            {
            }
        }

        private static void Head(TravelGroup g, Traveller t, WorldPlace inn)
        {
            Remember(g, t);
            t.mode = Mode.Heading;
            t.inn = inn.name;
            t.since = Now();
            Hold(g);
            Go(g, t, Door(inn));
            dirty = true;
        }

        private static void Lie(TravelGroup g, Traveller t)
        {
            if (t.mode == Mode.Awake) Remember(g, t);
            t.mode = Mode.Camped;
            t.inn = null;
            t.until = Now() + Camp.Value;
            Hold(g);
            try { g.leader.stateMachine.HandleStop(); } catch { }
            dirty = true;
        }

        private static void Lodge(TravelGroup g, Traveller t)
        {
            WorldPlace inn = Find(t.inn);
            float[] n = Floats(Night.Value, new[] { 8f, 10f });
            t.mode = Mode.Lodged;
            t.until = Now() + Mathf.Lerp(n[0], Mathf.Max(n[0], n[1]), Crime.Lot(g.id, 23));
            Hide(g, inn);
            Pay(g, inn);
            dirty = true;
        }

        private static void Hide(TravelGroup g, WorldPlace inn)
        {
            try { g.leader.stateMachine.HandleStop(); } catch { }
            g.leader.bypassCulling = true;
            if (inn != null) g.leader.transform.position = inn.transform.position;
            g.leader.gameObject.SetActive(false);
            if (g.caravan != null) g.caravan.gameObject.SetActive(false);
        }

        private static void Wake(TravelGroup g, Traveller t)
        {
            if (t.mode == Mode.Lodged)
            {
                WorldPlace inn = Find(t.inn);
                g.leader.bypassCulling = false;
                g.leader.gameObject.SetActive(true);
                Vector3 door = inn != null ? Door(inn) : g.leader.transform.position;
                try { g.leader.Teleport(door); } catch { }
                if (g.caravan != null)
                {
                    g.caravan.gameObject.SetActive(true);
                    try { g.caravan.Teleport(door); } catch { }
                }
            }

            t.mode = Mode.Awake;
            t.inn = null;
            t.awake = 0f;
            t.seen = false;
            Card(g, t, 0);
            Release(g, t);
            dirty = true;
        }

        /// <summary>A night at an inn: a hero band pays and buys as it does in town; anyone else pays for the bed and eats.</summary>
        private static void Pay(TravelGroup g, WorldPlace inn)
        {
            if (inn == null || !Economy.On()) return;
            try
            {
                Economy.Place p = Economy.Get(inn.name);
                if (p == null) return;
                if (g.isHeroAdventurer && !p.village)
                {
                    Economy.Lodge(g, inn);
                    return;
                }

                int men = 1 + (g.members != null ? g.members.Count : 0);
                p.tavernCoin += 20f * men;
                p.Eat(men);
                Economy.Touch();
            }
            catch
            {
            }
        }

        // ------------------------------------------------------------------ усталость на людях

        private static IEnumerable<UnitAttribute> Units(TravelGroup g)
        {
            if (g.leader != null) yield return g.leader;
            if (g.unitMembers == null) yield break;
            foreach (UnitAttribute u in g.unitMembers)
            {
                if (u != null && u != g.leader) yield return u;
            }
        }

        /// <summary>The same weariness a hero carries, on every one of the group: on those who walk, and on those who will be called up to fight.</summary>
        private static void Card(TravelGroup g, Traveller t, int level)
        {
            if (t.level == level) return;
            t.level = level;

            UIBuffInfo card = level > 0 ? Economy.TiredCard() : null;
            string id = Economy.TiredId;

            foreach (UnitAttribute u in Units(g))
            {
                try
                {
                    if (u.buffmanger == null) continue;
                    if (u.buffmanger.ContainBuff(id)) u.buffmanger.RemoveBuff(id);
                    if (card != null) Economy.Mark(u, card, 48f, level);
                }
                catch
                {
                }
            }

            if (g.members == null) return;
            foreach (CharacterSaveData m in g.members)
            {
                if (m == null) continue;
                if (m.buffs == null) m.buffs = new List<BuffSaveData>();
                m.buffs.RemoveAll(b => b != null && b.id == id);
                if (card != null) m.buffs.Add(new BuffSaveData { id = id, duration = 48f, level = level });
            }
        }

        /// <summary>A weary group walks slower: a tenth at the first level, a fifth at the second.</summary>
        internal static void Speed(TravelGroup g)
        {
            if (!On() || g == null || g.isPlayer || g.leader == null) return;

            Traveller t;
            if (!known.TryGetValue(g.id, out t) || t.level <= 0 || t.level >= 3) return;

            float[] slow = Floats(Slow.Value, new[] { 0.1f, 0.2f });
            float f = Mathf.Clamp01(1f - slow[Mathf.Clamp(t.level - 1, 0, 1)]);
            g.realtravelSpeed *= f;
            g.leader.maxSpeed = g.realtravelSpeed;
            g.leader.SetMoveSpeed();
            if (g.caravan != null)
            {
                g.caravan.maxSpeed = g.realtravelSpeed;
                g.caravan.SetMoveSpeed();
            }
        }

        // ------------------------------------------------------------------ каждую секунду

        private static float next;
        private static float mapSince = -1f;
        private static float pruned;
        private static bool carded;

        internal static void Tick()
        {
            if (!On()) return;

            // Карточка усталости нужна раньше всех: её несут и сохранённые отряды.
            if (!carded)
            {
                try { carded = UIBuffDatabase.Instance != null && Economy.TiredCard() != null; } catch { }
            }

            WorldTravelManager wtm = WorldTravelManager.instance;
            if (wtm == null || !wtm.inited)
            {
                mapSince = -1f;
                return;
            }

            float now = Time.unscaledTime;
            if (now < next) return;
            next = now + 1f;
            if (mapSince < 0f) mapSince = now;

            Sync();
            float hour = Now();

            List<Traveller> all = new List<Traveller>(known.Values);
            foreach (Traveller t in all)
            {
                if (t.mode == Mode.Awake) continue;
                TravelGroup g = wtm.FindGroupById(t.id);
                if (g == null || g.leader == null || g.leader.Data == null) continue;

                if (t.mode != Mode.Heading && hour >= 0f && hour >= t.until)
                {
                    Wake(g, t);
                    continue;
                }
                Keep(g, t);
            }

            // Кого на карте больше нет — того и помнить незачем; но сперва дать карте населиться.
            if (now - mapSince > 30f && now - pruned > 30f)
            {
                pruned = now;
                foreach (Traveller t in all)
                {
                    if (wtm.FindGroupById(t.id) == null) { known.Remove(t.id); dirty = true; }
                }
            }
        }

        /// <summary>Holds a group to what it is doing: walking to its bed, sleeping in it, or sleeping by the road.</summary>
        private static void Keep(TravelGroup g, Traveller t)
        {
            switch (t.mode)
            {
                case Mode.Heading:
                {
                    Hold(g);
                    if (g.currentBattle != null) return;
                    WorldPlace inn = Find(t.inn);
                    if (inn == null) { Lie(g, t); return; }
                    Vector3 door = Door(inn);
                    if (Flat(g.leader.transform.position - door).magnitude < 2f) { Lodge(g, t); return; }
                    if (!g.leader.isMoving && Time.unscaledTime - t.nudged > 5f) Go(g, t, door);
                    return;
                }
                case Mode.Camped:
                    Hold(g);
                    if (g.leader.isMoving && g.currentBattle == null)
                    {
                        try { g.leader.stateMachine.HandleStop(); } catch { }
                    }
                    return;
                case Mode.Lodged:
                    if (g.leader.gameObject.activeSelf) Hide(g, Find(t.inn));
                    return;
            }
        }

        // ------------------------------------------------------------------ файл

        private static string loadedFor;
        private static bool dirty;
        private static float synced = -100f;
        private static System.Reflection.FieldInfo archiveField;

        private static string FilePath()
        {
            string archive = "";
            try
            {
                if (archiveField == null) archiveField = AccessTools.Field(typeof(SaveLoadManager), "currentArchive");
                archive = (archiveField != null && SaveLoadManager.Instance != null
                    ? archiveField.GetValue(SaveLoadManager.Instance) as string : null) ?? "";
            }
            catch
            {
            }

            foreach (char bad in Path.GetInvalidFileNameChars()) archive = archive.Replace(bad, '_');
            return Path.Combine(BepInEx.Paths.ConfigPath, "aor.weary." + archive + ".txt");
        }

        private static void Sync()
        {
            float now = Time.unscaledTime;
            if (loadedFor != null && now - synced < 5f && now >= synced) return;
            synced = now;

            string path = FilePath();
            if (!string.Equals(path, loadedFor, StringComparison.OrdinalIgnoreCase))
            {
                Load(path);
                dirty = false;
            }
        }

        private static void Load(string path)
        {
            known.Clear();
            loadedFor = path;

            try
            {
                if (!File.Exists(path)) return;

                foreach (string line in File.ReadAllLines(path))
                {
                    int split = line.IndexOf('=');
                    if (split <= 0 || line.Substring(0, split).Trim() != "g") continue;

                    string[] v = line.Substring(split + 1).Split(';');
                    if (v.Length < 5) continue;

                    Traveller t = new Traveller();
                    int mode;
                    if (!int.TryParse(v[0], out t.id)) continue;
                    float.TryParse(v[1], NumberStyles.Float, CultureInfo.InvariantCulture, out t.awake);
                    int.TryParse(v[2], out mode);
                    t.mode = (Mode)Mathf.Clamp(mode, 0, 3);
                    float.TryParse(v[3], NumberStyles.Float, CultureInfo.InvariantCulture, out t.until);
                    t.inn = v[4].Length > 0 ? v[4] : null;
                    if (t.mode == Mode.Heading) t.since = Now();
                    known[t.id] = t;
                }
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Усталость: не смог прочесть: " + e.Message);
            }
        }

        internal static void Flush()
        {
            if (!On()) return;
            Sync();
            if (!dirty) return;

            try
            {
                List<string> lines = new List<string>();
                foreach (Traveller t in known.Values)
                {
                    lines.Add("g=" + t.id + ";" + t.awake.ToString("0.#", CultureInfo.InvariantCulture) + ";" + (int)t.mode + ";"
                        + t.until.ToString("0.##", CultureInfo.InvariantCulture) + ";" + (t.inn ?? ""));
                }
                File.WriteAllLines(loadedFor ?? FilePath(), lines.ToArray());
                dirty = false;
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Усталость: не смог записать: " + e.Message);
            }
        }

        internal static void Reload()
        {
            if (!On()) return;
            synced = Time.unscaledTime;
            Load(FilePath());
            dirty = false;
        }
    }

    [HarmonyPatch(typeof(TravelGroup), "OnHourPassed")]
    internal static class Hour_Weariness_Patch
    {
        private static void Postfix(TravelGroup __instance, int passed)
        {
            try { Weariness.Hour(__instance, passed); } catch { }
        }
    }

    [HarmonyPatch(typeof(TravelGroup), "UpdateTraverSpeed")]
    internal static class Speed_Weariness_Patch
    {
        private static void Postfix(TravelGroup __instance)
        {
            try { Weariness.Speed(__instance); } catch { }
        }
    }

    [HarmonyPatch(typeof(SaveLoadManager), "WriteDataToSaveFile")]
    internal static class WriteSave_Weariness_Patch
    {
        private static void Postfix() { try { Weariness.Flush(); } catch { } }
    }

    [HarmonyPatch(typeof(TroopManagement.ArenaMatchManager), "LoadInstance")]
    internal static class LoadInstance_Weariness_Patch
    {
        private static void Postfix() { try { Weariness.Reload(); } catch { } }
    }
}
