using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace ItemForge
{
    /// <summary>
    /// Heads come off, limbs come off, and the blood does not stop at once.
    ///
    /// Отдельных голов и рук у людей в этой игре нет. Тело — одна общая натянутая сетка
    /// («UMARenderer»), доспех вшит в неё же, и только ткань — плащ, подол — идёт своими
    /// сетками. Мёртвое тело становится тряпичной куклой из двенадцати частей: таз, поясница,
    /// грудь, голова, плечи, предплечья, бёдра, голени. Это показал замер тела Мартины.
    ///
    /// Значит резать надо саму сетку. В миг смерти каждая вершина спрашивается, к каким
    /// костям она привязана; всё, что держится за отрубаемую кость и её потомков, — голова со
    /// шлемом, рука с наручем, — уходит из тела в отдельный кусок. Кусок снимается в той
    /// позе, в какой застала смерть, и летит по направлению удара как отдельное тело. Кости
    /// отрубленного в кукле перестают быть телами и просто следуют за обрубком, чтобы шея не
    /// тянулась за невидимой головой.
    ///
    /// Что рубит, решает род удара:
    /// - в голову: рубящее срубает её, дробящее разбивает — голова исчезает в брызгах, колющее
    ///   и стрела пробивают, и тогда только кровь;
    /// - рука и нога: отлетают у павшего, если их выбил рубящий удар вдвое больше их запаса.
    ///   Живой в этой игре без руки драться не умеет — оружие висело бы в воздухе, — поэтому
    ///   конечность помечается и отлетает, когда боец падает;
    /// - в туловище: рубящее и колющее проходят насквозь — струя из спины по направлению
    ///   удара; дробящее даёт самый большой выброс крови.
    ///
    /// И после — кровь. Пятнадцать секунд обрубок бьёт струёй, слабея, а под телом растёт лужа
    /// и расползается по земле. Брызги и лужи берутся игровые: у игры их целый набор, по роду
    /// удара и по цвету крови.
    /// </summary>
    internal static class Sever
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> LimbTimes;
        internal static ConfigEntry<float> Bleed;
        internal static ConfigEntry<float> Flood;
        internal static ConfigEntry<float> Fountain;
        internal static ConfigEntry<string> Reach;
        internal static ConfigEntry<float> PoolLast;
        internal static ConfigEntry<int> MaxPools;
        internal static ConfigEntry<int> MaxBleeding;
        internal static ConfigEntry<bool> Probe;
        internal static ConfigEntry<int> ProbeCount;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Dismember", "Enabled", true,
                "Let a killing blow take off a head, let a limb hewn through come off when its "
                + "owner falls, and let the wound bleed onto the ground afterwards. People only: "
                + "beasts are built on other skeletons.");

            LimbTimes = config.Bind("Dismember", "LimbTimes", 2f,
                new ConfigDescription(
                    "How many times the limb's own share of health a cutting blow must deal to "
                    + "take the limb off. Two: the blow went through twice what the arm could "
                    + "hold.",
                    new AcceptableValueRange<float>(1f, 10f)));

            Bleed = config.Bind("Dismember", "Bleed", 15f,
                new ConfigDescription(
                    "How long the wound keeps bleeding, in seconds. Strong at first, weaker to the "
                    + "end, and the pool under the body keeps spreading the whole time.",
                    new AcceptableValueRange<float>(1f, 60f)));

            Flood = config.Bind("Dismember", "Flood", 2.5f,
                new ConfigDescription(
                    "How far the pool spreads from the wound by the time the bleeding stops, in "
                    + "metres.",
                    new AcceptableValueRange<float>(0.5f, 6f)));

            Fountain = config.Bind("Dismember", "Fountain", 2.5f,
                new ConfigDescription(
                    "How long the severed vessel spurts, in seconds. After that the wound only "
                    + "seeps and the pool under the body keeps spreading until the bleeding ends.",
                    new AcceptableValueRange<float>(0f, 60f)));

            Reach = config.Bind("Dismember", "Reach", "0.05,0.35",
                "How far the spurting blood flies, as a share of how far it used to: every jet "
                + "draws its own share between these two. A twentieth to a third keeps the blood "
                + "near the body instead of across the room.");

            PoolLast = config.Bind("Dismember", "PoolLast", 240f,
                new ConfigDescription(
                    "How long the pool stays on the ground before it dries away, in seconds. The "
                    + "game's own splashes last twelve.",
                    new AcceptableValueRange<float>(10f, 3600f)));

            MaxPools = config.Bind("Dismember", "MaxPools", 180,
                new ConfigDescription(
                    "How many patches of pooled blood may lie about at once. Past that the oldest "
                    + "dries first: a battlefield of fifty dead is still drawn in a frame.",
                    new AcceptableValueRange<int>(10, 1000)));

            MaxBleeding = config.Bind("Dismember", "MaxBleeding", 12,
                new ConfigDescription(
                    "How many wounds may bleed at the same time.",
                    new AcceptableValueRange<int>(1, 100)));

            Probe = config.Bind("Dismember", "Probe", true,
                "Write down, for the first few who fall, whether the body went limp as a doll "
                + "and whether anything touched it after death. This is how the standing dead "
                + "are to be caught.");

            ProbeCount = config.Bind("Dismember", "ProbeCount", 10,
                new ConfigDescription(
                    "How many of the fallen to write down.",
                    new AcceptableValueRange<int>(1, 100)));

            Telling = config.Bind("Dismember", "Telling", true,
                "Say in the log what was cut off whom, and with what.");
        }

        // ------------------------------------------------------------------ части

        private const int Cut = 0;      // рубящее: отсекает
        private const int Crush = 1;    // дробящее: разбивает
        private const int Thrust = 2;   // колющее: пробивает

        private enum How { Off, Smash, Pierce, Through, Burst }

        private sealed class Wound
        {
            internal int part;
            internal How how;
            internal int kind;
        }

        private sealed class Job
        {
            internal HumaniodUnit man;
            internal List<Wound> wounds = new List<Wound>();
            internal Vector3 from;
            internal bool hasFrom;
            internal float since;
        }

        // Выбитые насквозь конечности: отлетят, когда их хозяин упадёт.
        private static readonly Dictionary<int, HashSet<int>> hewn = new Dictionary<int, HashSet<int>>();

        // Последний смертельный удар, как его видел наш счёт частей тела.
        private static UnitAttribute lastVictim;
        private static int lastPart = -1;
        private static Attack lastAttack;
        private static int lastFrame = -1;

        private static readonly List<Job> waiting = new List<Job>();

        /// <summary>Marks a limb as hewn through, if the blow was enough to do it.</summary>
        internal static void Hewn(UnitAttribute who, int part, float dealt, float whole, Attack attack)
        {
            if (Enabled == null || !Enabled.Value || who == null) return;
            if (part == Limb.Head || part == Limb.Chest) return;
            if (attack == null || !Breach.Physical(attack) || Breach.Kind(attack) != Cut) return;
            if (whole <= 0f || dealt < whole * LimbTimes.Value) return;

            HashSet<int> mine;
            if (!hewn.TryGetValue(who.GetInstanceID(), out mine))
            {
                mine = new HashSet<int>();
                hewn[who.GetInstanceID()] = mine;
            }

            if (mine.Add(part) && Telling.Value)
            {
                ItemForgePlugin.Log.LogInfo($"«{who.Data.unitname}»: {Limb.Called[part]} "
                    + $"перерублена насквозь ({dealt:0} при запасе {whole:0}) — отлетит, когда упадёт.");
            }
        }

        /// <summary>Remembers where the killing blow landed, a moment before the game is told.</summary>
        internal static void Killing(UnitAttribute who, int part, Attack attack)
        {
            lastVictim = who;
            lastPart = part;
            lastAttack = attack;
            lastFrame = Time.frameCount;
        }

        /// <summary>True when the blow that is killing this man is taking his head off.</summary>
        internal static bool Beheads(UnitAttribute who)
        {
            if (Enabled == null || !Enabled.Value || who == null) return false;

            Attack attack = null;
            int part = -1;

            if ((object)lastVictim == (object)who && lastFrame == Time.frameCount)
            {
                attack = lastAttack;
                part = lastPart;
            }
            else
            {
                int spot;
                if (Anatomy.Fresh(who, out attack, out spot)) part = Limb.Place(spot, who, attack);
            }

            if (attack == null || part != Limb.Head || !Breach.Physical(attack)) return false;

            return Breach.Kind(attack) == Cut;
        }

        /// <summary>Works out what the death did to the body, and queues the cutting.</summary>
        internal static void Fell(UnitAttribute who, UnitAttribute killer)
        {
            if (Enabled == null || !Enabled.Value) return;

            HumaniodUnit man = who as HumaniodUnit;
            if (man == null || man.Data == null || !man.Data.isdead || !man.Data.trueDead) return;

            // Второй раз «Die» зовут и по уже лежащему: работу по нему уже поставили.
            foreach (Job queued in waiting)
            {
                if ((object)queued.man == (object)man) return;
            }

            try
            {
                Attack attack = null;
                int part = -1;

                if ((object)lastVictim == (object)who && lastFrame == Time.frameCount)
                {
                    attack = lastAttack;
                    part = lastPart;
                }
                else
                {
                    int spot;
                    if (Anatomy.Fresh(who, out attack, out spot)) part = Limb.Place(spot, who, attack);
                }

                Job job = new Job { man = man, since = Time.time };

                UnitAttribute striker = attack != null && attack.attacker != null ? attack.attacker : killer;
                if (striker != null)
                {
                    job.from = striker.transform.position;
                    job.hasFrom = true;
                }

                bool physical = attack != null && Breach.Physical(attack);
                int kind = physical ? Breach.Kind(attack) : -1;

                if (physical && part >= 0)
                {
                    if (part == Limb.Head)
                    {
                        How how = kind == Cut ? How.Off : kind == Crush ? How.Smash : How.Pierce;
                        job.wounds.Add(new Wound { part = Limb.Head, how = how, kind = kind });
                    }
                    else if (part == Limb.Chest || !Hewed(man, part))
                    {
                        How how = kind == Crush ? How.Burst : How.Through;
                        job.wounds.Add(new Wound { part = Limb.Chest, how = how, kind = kind });
                    }
                }

                HashSet<int> marked;
                if (hewn.TryGetValue(man.GetInstanceID(), out marked))
                {
                    foreach (int limb in marked)
                    {
                        job.wounds.Add(new Wound { part = limb, how = How.Off, kind = Cut });
                    }

                    hewn.Remove(man.GetInstanceID());
                }

                // Чем страшнее смерть, тем сильнее она ломает тех, кто её видел.
                int grim = 0;
                foreach (Wound w in job.wounds)
                {
                    if (w.how == How.Off || w.how == How.Smash) grim = Math.Max(grim, w.part == Limb.Head ? 3 : 2);
                    else if (w.how == How.Burst) grim = Math.Max(grim, 2);
                }
                if (grim > 0) Dread.Slaughter(man, striker, grim);

                if (job.wounds.Count > 0) waiting.Add(job);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог разобрать смерть для отсечения: " + e.Message);
            }
        }

        private static bool Hewed(HumaniodUnit man, int part)
        {
            HashSet<int> marked;
            return hewn.TryGetValue(man.GetInstanceID(), out marked) && marked.Contains(part);
        }

        // ------------------------------------------------------------------ ход

        internal static void Tick()
        {
            if (Enabled == null || !Enabled.Value) return;

            try
            {
                for (int i = waiting.Count - 1; i >= 0; i--)
                {
                    Job job = waiting[i];

                    if (job.man == null)
                    {
                        waiting.RemoveAt(i);
                        continue;
                    }

                    // Режем, когда тело уже легло куклой: до того кости ещё водит аниматор,
                    // и снятый с него кусок застыл бы в позе удара. Не легло за две секунды —
                    // режем как есть.
                    bool ready = job.man.isRagdolled || Time.time - job.since > 2f;
                    if (!ready || Time.time - job.since < 0.05f) continue;

                    waiting.RemoveAt(i);
                    Perform(job);
                }

                Review();
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Отсечение сорвалось: " + e.Message);
            }
        }

        private static void Perform(Job job)
        {
            HumaniodUnit man = job.man;

            foreach (Wound wound in job.wounds)
            {
                try
                {
                    Deal(man, wound, job);
                }
                catch (Exception e)
                {
                    ItemForgePlugin.Log.LogWarning($"Не смог нанести рану «{man.Data.unitname}»: "
                        + e.Message);
                }
            }
        }

        private static void Deal(HumaniodUnit man, Wound wound, Job job)
        {
            Vector3 push = Push(man, job);

            switch (wound.how)
            {
                case How.Off:
                {
                    Transform cut, stump;
                    if (!Bones(man, wound.part, out cut, out stump)) return;

                    bool flew = Carve(man, cut, true, push);
                    Burst(man, stump.position, Out(stump, cut, push), Cut, 3);
                    Bleeders.Start(man, stump, cut, Vector3.zero, Cut, 1f);

                    Tell(man, (wound.part == Limb.Head ? "голова срублена" : Limb.Called[wound.part] + " отрублена")
                        + (flew ? "" : " (сетка не читается — часть сжата)"));
                    break;
                }

                case How.Smash:
                {
                    Transform cut, stump;
                    if (!Bones(man, wound.part, out cut, out stump)) return;

                    Vector3 at = cut.position;
                    Carve(man, cut, false, push);
                    Burst(man, at, push.sqrMagnitude > 0f ? push.normalized : Vector3.up, Crush, 6);
                    Bleeders.Start(man, stump, cut, Vector3.zero, Crush, 1.2f);

                    Tell(man, "голова разбита");
                    break;
                }

                case How.Pierce:
                {
                    Transform head = Bone(man, HumanBodyBones.Head, "Head");
                    if (head == null) return;

                    Vector3 dir = push.sqrMagnitude > 0f ? push.normalized : head.forward;
                    Burst(man, head.position + dir * 0.1f, dir, Thrust, 2);
                    Bleeders.Start(man, head, null, head.InverseTransformDirection(dir), Thrust, 0.8f);

                    Tell(man, "голова пробита");
                    break;
                }

                case How.Through:
                {
                    Transform chest = Bone(man, HumanBodyBones.UpperChest, "Spine1")
                        ?? Bone(man, HumanBodyBones.Chest, "Spine");
                    if (chest == null) return;

                    // Насквозь: рана на спине, струя по направлению удара.
                    Vector3 dir = push.sqrMagnitude > 0f ? push.normalized : -chest.forward;
                    Burst(man, chest.position + dir * 0.15f, dir, wound.kind == Thrust ? Thrust : Cut, 3);
                    Bleeders.Start(man, chest, null, chest.InverseTransformDirection(dir),
                        wound.kind == Thrust ? Thrust : Cut, 1f);

                    Tell(man, "пронзён насквозь");
                    break;
                }

                case How.Burst:
                {
                    Transform chest = Bone(man, HumanBodyBones.UpperChest, "Spine1")
                        ?? Bone(man, HumanBodyBones.Chest, "Spine");
                    if (chest == null) return;

                    Burst(man, chest.position, Vector3.up, Crush, 7);
                    Bleeders.Start(man, chest, null, Vector3.up, Crush, 1.4f);

                    Tell(man, "грудь размозжена");
                    break;
                }
            }
        }

        private static void Tell(HumaniodUnit man, string what)
        {
            if (Telling.Value) ItemForgePlugin.Log.LogInfo($"«{man.Data.unitname}»: {what}.");
        }

        /// <summary>Which way the blow carried: from the striker through the body, a little upward.</summary>
        private static Vector3 Push(HumaniodUnit man, Job job)
        {
            if (!job.hasFrom) return Vector3.zero;

            Vector3 flat = man.transform.position - job.from;
            flat.y = 0f;

            if (flat.sqrMagnitude < 0.0001f) return Vector3.zero;

            return flat.normalized;
        }

        /// <summary>Out of the stump, along the limb that is no longer there.</summary>
        private static Vector3 Out(Transform stump, Transform cut, Vector3 fallback)
        {
            Vector3 way = cut.position - stump.position;
            if (way.sqrMagnitude > 0.0001f) return way.normalized;

            return fallback.sqrMagnitude > 0f ? fallback : Vector3.up;
        }

        // ------------------------------------------------------------------ кости

        private static Transform Bone(HumaniodUnit man, HumanBodyBones which, string name)
        {
            try
            {
                if (man.ani != null && man.ani.isHuman)
                {
                    Transform got = man.ani.GetBoneTransform(which);
                    if (got != null) return got;
                }
            }
            catch
            {
            }

            return Find(man.transform, name);
        }

        private static Transform Find(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform got = Find(root.GetChild(i), name);
                if (got != null) return got;
            }

            return null;
        }

        /// <summary>The bone to cut at, and the one left standing as the stump.</summary>
        private static bool Bones(HumaniodUnit man, int part, out Transform cut, out Transform stump)
        {
            cut = null;
            stump = null;

            // Руку и ногу рубят то у плеча или бедра, то у локтя или колена — у каждого своё,
            // и всегда одинаково у одного и того же человека.
            bool high = ((man.GetInstanceID() * 31 + part) & 1) == 0;

            switch (part)
            {
                case Limb.Head:
                    cut = Bone(man, HumanBodyBones.Head, "Head");
                    stump = Bone(man, HumanBodyBones.Neck, "Neck");
                    break;

                case Limb.ArmL:
                    cut = high ? Bone(man, HumanBodyBones.LeftUpperArm, "LeftArm")
                               : Bone(man, HumanBodyBones.LeftLowerArm, "LeftForeArm");
                    stump = high ? Bone(man, HumanBodyBones.LeftShoulder, "LeftShoulder")
                                 : Bone(man, HumanBodyBones.LeftUpperArm, "LeftArm");
                    break;

                case Limb.ArmR:
                    cut = high ? Bone(man, HumanBodyBones.RightUpperArm, "RightArm")
                               : Bone(man, HumanBodyBones.RightLowerArm, "RightForeArm");
                    stump = high ? Bone(man, HumanBodyBones.RightShoulder, "RightShoulder")
                                 : Bone(man, HumanBodyBones.RightUpperArm, "RightArm");
                    break;

                case Limb.LegL:
                    cut = high ? Bone(man, HumanBodyBones.LeftUpperLeg, "LeftUpLeg")
                               : Bone(man, HumanBodyBones.LeftLowerLeg, "LeftLeg");
                    stump = high ? Bone(man, HumanBodyBones.Hips, "Hips")
                                 : Bone(man, HumanBodyBones.LeftUpperLeg, "LeftUpLeg");
                    break;

                case Limb.LegR:
                    cut = high ? Bone(man, HumanBodyBones.RightUpperLeg, "RightUpLeg")
                               : Bone(man, HumanBodyBones.RightLowerLeg, "RightLeg");
                    stump = high ? Bone(man, HumanBodyBones.Hips, "Hips")
                                 : Bone(man, HumanBodyBones.RightUpperLeg, "RightUpLeg");
                    break;
            }

            if (cut != null && stump == null) stump = cut.parent;

            return cut != null && stump != null;
        }

        // ------------------------------------------------------------------ разрез

        internal static bool Cloth(SkinnedMeshRenderer skin)
        {
            foreach (Component one in skin.GetComponents<Component>())
            {
                if (one == null) continue;

                string name = one.GetType().Name;
                if (name.IndexOf("Cloth", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Magica", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Takes everything hung on this bone out of the body. With «fly», what was taken becomes
        /// a thing of its own and goes flying; without, it is simply gone.
        /// </summary>
        private static bool Carve(HumaniodUnit man, Transform cut, bool fly, Vector3 push)
        {
            HashSet<Transform> below = new HashSet<Transform>(cut.GetComponentsInChildren<Transform>(true));

            Remains keep = man.GetComponent<Remains>() ?? man.gameObject.AddComponent<Remains>();
            keep.Owner = man;

            // Отрубленное собирается под одним телом: и натянутая сетка со своими костями, и
            // всё, что висело на части само по себе.
            GameObject holder = null;
            if (fly)
            {
                holder = new GameObject("Отсечённое");
                holder.transform.SetPositionAndRotation(cut.position, cut.rotation);
                holder.layer = cut.gameObject.layer;
            }

            bool carved = false;

            foreach (SkinnedMeshRenderer skin in man.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin == null || skin.sharedMesh == null || !skin.gameObject.activeInHierarchy) continue;
                if (Cloth(skin)) continue;

                if (Split(skin, below, holder != null ? holder.transform : null, keep)) carved = true;
            }

            // Вещи, висящие на отрубленном не сеткой, а сами по себе, — шлем, серьга, — уходят
            // вместе с ним. Оружие со своим телом не трогаем: его игра роняет сама.
            List<Renderer> loose = new List<Renderer>();
            foreach (MeshRenderer one in cut.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (one == null || one.GetComponentInParent<OwnMesh>() != null) continue;

                Rigidbody owner = one.GetComponentInParent<Rigidbody>();
                if (owner != null && owner.GetComponent<UnitBodyPart>() == null) continue;

                loose.Add(one);
            }

            // Отрубленное в кукле больше не тело: пусть кости просто следуют за обрубком, иначе
            // шея тянется за невидимой головой, а та лежит где-то сама по себе.
            foreach (Transform bone in below)
            {
                // Только кости самой куклы: всё прочее, что висит на руке, — не наше тело.
                if (bone == null || bone.GetComponent<UnitBodyPart>() == null) continue;

                CharacterJoint joint = bone.GetComponent<CharacterJoint>();
                if (joint != null) UnityEngine.Object.Destroy(joint);

                Rigidbody body = bone.GetComponent<Rigidbody>();
                if (body != null) body.isKinematic = true;

                Collider col = bone.GetComponent<Collider>();
                if (col != null) col.enabled = false;
            }

            // Сетку прочесть не дали — снимать нечего. Тогда кость сжимается в точку, и всё,
            // что на ней, исчезает вместе с ней.
            if (!carved) cut.localScale = Vector3.one * 0.0001f;

            bool flew = false;

            if (holder != null)
            {
                foreach (Renderer one in loose) one.transform.SetParent(holder.transform, true);

                if (holder.GetComponentInChildren<Renderer>(true) != null)
                {
                    Throw(man, holder, cut, push);
                    flew = true;
                }
                else
                {
                    UnityEngine.Object.Destroy(holder);
                }
            }
            else
            {
                // Разбитое не летит — висевшее на нём исчезает вместе с ним.
                foreach (Renderer one in loose) one.enabled = false;
            }

            keep.Cuts.Add(below);

            return carved && (!fly || flew);
        }

        /// <summary>
        /// Splits one skinned mesh along the bones below the cut. With a holder, what was taken
        /// is rebuilt under it as a skinned mesh of its own.
        /// </summary>
        internal static bool Split(SkinnedMeshRenderer skin, HashSet<Transform> below, Transform holder,
            Remains keep)
        {
            Mesh mesh = skin.sharedMesh;
            if (mesh == null || !mesh.isReadable) return false;

            Transform[] bones = skin.bones;
            if (bones == null || bones.Length == 0) return false;

            bool[] ours = new bool[bones.Length];
            bool any = false;

            for (int i = 0; i < bones.Length; i++)
            {
                ours[i] = bones[i] != null && below.Contains(bones[i]);
                if (ours[i]) any = true;
            }

            if (!any) return false;

            BoneWeight[] weights = mesh.boneWeights;
            int count = mesh.vertexCount;
            if (weights == null || weights.Length != count) return false;

            bool[] taken = new bool[count];

            for (int v = 0; v < count; v++)
            {
                BoneWeight w = weights[v];
                float share = 0f;

                if (w.boneIndex0 < ours.Length && ours[w.boneIndex0]) share += w.weight0;
                if (w.boneIndex1 < ours.Length && ours[w.boneIndex1]) share += w.weight1;
                if (w.boneIndex2 < ours.Length && ours[w.boneIndex2]) share += w.weight2;
                if (w.boneIndex3 < ours.Length && ours[w.boneIndex3]) share += w.weight3;

                taken[v] = share >= 0.5f;
            }

            int subs = mesh.subMeshCount;
            List<int>[] keepTris = new List<int>[subs];
            List<int>[] takeTris = new List<int>[subs];
            int moved = 0;

            for (int sm = 0; sm < subs; sm++)
            {
                int[] tris = mesh.GetTriangles(sm);
                keepTris[sm] = new List<int>(tris.Length);
                takeTris[sm] = new List<int>();

                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                    int n = (taken[a] ? 1 : 0) + (taken[b] ? 1 : 0) + (taken[c] ? 1 : 0);

                    List<int> to = n >= 2 ? takeTris[sm] : keepTris[sm];
                    to.Add(a);
                    to.Add(b);
                    to.Add(c);

                    if (n >= 2) moved++;
                }
            }

            if (moved == 0) return false;

            // Отрубленное собирается прежде, чем тело его потеряет: кости ещё стоят в позе смерти.
            if (holder != null)
            {
                try
                {
                    Piece(skin, mesh, weights, takeTris, holder);
                }
                catch (Exception e)
                {
                    ItemForgePlugin.Log.LogWarning("Не смог собрать отрубленное: " + e.Message);
                }
            }

            Mesh body = UnityEngine.Object.Instantiate(mesh);
            body.name = mesh.name + " (рана)";

            for (int sm = 0; sm < subs; sm++) body.SetTriangles(keepTris[sm], sm, false);

            skin.sharedMesh = body;

            if (keep != null) keep.Remember(skin, body);

            return true;
        }

        private static int piecesTold;

        /// <summary>
        /// A skinned copy of what was taken, on bones of its own.
        ///
        /// Прежде отрубленное снималось в позе смерти в обычную неподвижную сетку — и у части
        /// доспехов и тканей выходило розовым: их материалы у игры рисуются только натянутыми
        /// на кости, и простой сетке показать их нечем. Теперь кусок остаётся натянутой сеткой
        /// с теми же материалами и тем же набором настроек отрисовки, что у тела, только кости у
        /// него свои: копии настоящих, поставленные туда, где те были в миг удара, и
        /// неподвижные внутри летящего куска.
        /// </summary>
        private static void Piece(SkinnedMeshRenderer skin, Mesh mesh, BoneWeight[] weights,
            List<int>[] takeTris, Transform holder)
        {
            Transform[] bones = skin.bones;

            Vector3[] pos = mesh.vertices;
            Vector3[] nor = mesh.normals;
            Vector4[] tan = mesh.tangents;
            Vector2[] uv = mesh.uv;
            Vector2[] uv2 = mesh.uv2;
            Vector2[] uv3 = mesh.uv3;
            Vector2[] uv4 = mesh.uv4;
            Color32[] tint = mesh.colors32;

            int count = pos.Length;
            int[] remap = new int[count];
            for (int i = 0; i < count; i++) remap[i] = -1;

            List<Vector3> p = new List<Vector3>();
            List<Vector3> n = new List<Vector3>();
            List<Vector4> tg = new List<Vector4>();
            List<Vector2> u = new List<Vector2>(), u2 = new List<Vector2>();
            List<Vector2> u3 = new List<Vector2>(), u4 = new List<Vector2>();
            List<Color32> col = new List<Color32>();
            List<BoneWeight> bw = new List<BoneWeight>();
            bool[] used = new bool[bones.Length];

            List<List<int>> subs = new List<List<int>>();
            List<Material> mats = new List<Material>();
            List<int> fromSub = new List<int>();
            Material[] shared = skin.sharedMaterials;

            for (int sm = 0; sm < takeTris.Length; sm++)
            {
                List<int> tris = takeTris[sm];
                if (tris == null || tris.Count == 0) continue;

                List<int> made = new List<int>(tris.Count);

                foreach (int old in tris)
                {
                    if (old < 0 || old >= count) continue;

                    if (remap[old] < 0)
                    {
                        remap[old] = p.Count;
                        p.Add(pos[old]);
                        if (nor != null && nor.Length == count) n.Add(nor[old]);
                        if (tan != null && tan.Length == count) tg.Add(tan[old]);
                        if (uv != null && uv.Length == count) u.Add(uv[old]);
                        if (uv2 != null && uv2.Length == count) u2.Add(uv2[old]);
                        if (uv3 != null && uv3.Length == count) u3.Add(uv3[old]);
                        if (uv4 != null && uv4.Length == count) u4.Add(uv4[old]);
                        if (tint != null && tint.Length == count) col.Add(tint[old]);

                        BoneWeight w = weights[old];
                        bw.Add(w);

                        if (w.weight0 > 0f && w.boneIndex0 < used.Length) used[w.boneIndex0] = true;
                        if (w.weight1 > 0f && w.boneIndex1 < used.Length) used[w.boneIndex1] = true;
                        if (w.weight2 > 0f && w.boneIndex2 < used.Length) used[w.boneIndex2] = true;
                        if (w.weight3 > 0f && w.boneIndex3 < used.Length) used[w.boneIndex3] = true;
                    }

                    made.Add(remap[old]);
                }

                subs.Add(made);
                mats.Add(shared != null && sm < shared.Length ? shared[sm] : null);
                fromSub.Add(sm);
            }

            if (p.Count == 0) return;

            Mesh cutOff = new Mesh();
            cutOff.name = mesh.name + " (отсечённое)";
            if (p.Count > 65000) cutOff.indexFormat = IndexFormat.UInt32;

            cutOff.SetVertices(p);
            if (n.Count == p.Count) cutOff.SetNormals(n);
            if (tg.Count == p.Count) cutOff.SetTangents(tg);
            if (u.Count == p.Count) cutOff.SetUVs(0, u);
            if (u2.Count == p.Count) cutOff.SetUVs(1, u2);
            if (u3.Count == p.Count) cutOff.SetUVs(2, u3);
            if (u4.Count == p.Count) cutOff.SetUVs(3, u4);
            if (col.Count == p.Count) cutOff.SetColors(col);

            cutOff.boneWeights = bw.ToArray();
            cutOff.bindposes = mesh.bindposes;

            cutOff.subMeshCount = subs.Count;
            for (int i = 0; i < subs.Count; i++) cutOff.SetTriangles(subs[i], i, false);

            cutOff.RecalculateBounds();
            if (n.Count != p.Count) cutOff.RecalculateNormals();

            // Кости — копии тех, на которых держались вершины куска, в той же позе, что в миг
            // удара. Внутри куска они неподвижны и уносятся вместе с ним.
            Transform[] own = new Transform[bones.Length];
            Transform anchor = null;

            for (int i = 0; i < bones.Length; i++)
            {
                if (!used[i] || bones[i] == null)
                {
                    own[i] = holder;
                    continue;
                }

                GameObject bone = new GameObject(bones[i].name);
                bone.transform.SetParent(holder, false);
                bone.transform.SetPositionAndRotation(bones[i].position, bones[i].rotation);
                bone.transform.localScale = bones[i].lossyScale;

                own[i] = bone.transform;
                if (anchor == null) anchor = bone.transform;
            }

            GameObject go = new GameObject(skin.name + " (отсечённое)");
            go.transform.SetParent(holder, false);
            go.layer = skin.gameObject.layer;

            SkinnedMeshRenderer made2 = go.AddComponent<SkinnedMeshRenderer>();
            made2.sharedMesh = cutOff;
            made2.bones = own;
            made2.rootBone = anchor != null ? anchor : holder;
            made2.updateWhenOffscreen = true;
            made2.quality = skin.quality;
            made2.shadowCastingMode = skin.shadowCastingMode;
            made2.receiveShadows = skin.receiveShadows;

            // Материал, которому нечем рисоваться, заменяем первым годным того же тела: лучше
            // кусок в цвете кожи, чем розовый.
            Material spare = null;
            if (shared != null)
            {
                foreach (Material one in shared)
                {
                    if (one != null && one.shader != null && one.shader.isSupported) { spare = one; break; }
                }
            }

            for (int i = 0; i < mats.Count; i++)
            {
                Material one = mats[i];
                if (one == null || one.shader == null || !one.shader.isSupported) mats[i] = spare;
            }

            // Свои копии материалов. Тело после смерти игра пересобирает — при обыске, при
            // снятии вещей — и свои материалы при этом уничтожает; кусок, что держал их же,
            // остался бы с пустыми, а пустой материал Unity рисует розовым.
            List<Material> owned = new List<Material>();
            for (int i = 0; i < mats.Count; i++)
            {
                if (mats[i] == null) continue;
                Material copy = new Material(mats[i]) { name = mats[i].name + " (отсечённое)" };
                mats[i] = copy;
                owned.Add(copy);
            }

            made2.sharedMaterials = mats.ToArray();

            // И настройки отрисовки — те же, что у тела. Игра выкидывает из сборки варианты
            // шейдеров, которых у неё нигде нет; сетка с иными настройками проб света или слоёв
            // попросила бы такой вариант — и тоже вышла бы розовой.
            made2.renderingLayerMask = skin.renderingLayerMask;
            made2.rendererPriority = skin.rendererPriority;
            made2.lightProbeUsage = skin.lightProbeUsage;
            made2.reflectionProbeUsage = skin.reflectionProbeUsage;
            made2.skinnedMotionVectors = skin.skinnedMotionVectors;
            made2.motionVectorGenerationMode = skin.motionVectorGenerationMode;
            made2.allowOcclusionWhenDynamic = skin.allowOcclusionWhenDynamic;

            // Часть настроек отрисовки игра держит не в материале, а на самой сетке тела —
            // переносим и их, по своей подсетке каждую.
            try
            {
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                skin.GetPropertyBlock(block);
                if (!block.isEmpty) made2.SetPropertyBlock(block);

                for (int i = 0; i < fromSub.Count; i++)
                {
                    if (!skin.HasPropertyBlock()) break;

                    MaterialPropertyBlock part = new MaterialPropertyBlock();
                    skin.GetPropertyBlock(part, fromSub[i]);
                    if (!part.isEmpty) made2.SetPropertyBlock(part, i);
                }
            }
            catch
            {
            }

            OwnMesh keeper = go.AddComponent<OwnMesh>();
            keeper.mesh = cutOff;
            keeper.materials = owned;
            go.AddComponent<PieceWatch>().body = skin;

            if (Telling.Value && piecesTold < 6)
            {
                piecesTold++;

                List<string> said = new List<string>();
                foreach (Material one in mats)
                {
                    said.Add(one == null ? "нет" : $"{one.name} ({(one.shader != null ? one.shader.name : "?")})");
                }

                ItemForgePlugin.Log.LogInfo($"Отсечённое из «{skin.name}»: вершин {p.Count}, "
                    + $"подсеток {subs.Count}, материалы: {string.Join(", ", said.ToArray())}.");
            }
        }

        /// <summary>Makes the holder a body and sends it off along the blow.</summary>
        private static void Throw(HumaniodUnit man, GameObject lead, Transform cut, Vector3 push)
        {
            Bounds all = new Bounds();
            bool first = true;

            foreach (Renderer one in lead.GetComponentsInChildren<Renderer>(true))
            {
                Bounds world = one.bounds;
                Vector3 centre = lead.transform.InverseTransformPoint(world.center);
                Vector3 size = lead.transform.InverseTransformVector(world.size);
                Bounds local = new Bounds(centre, new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)));

                if (first) { all = local; first = false; }
                else all.Encapsulate(local);
            }

            if (first || all.size.sqrMagnitude < 0.0001f)
            {
                all = new Bounds(Vector3.zero, Vector3.one * 0.25f);
            }

            BoxCollider box = lead.AddComponent<BoxCollider>();
            box.center = all.center;
            box.size = new Vector3(Mathf.Clamp(all.size.x, 0.06f, 1.2f), Mathf.Clamp(all.size.y, 0.06f, 1.2f),
                Mathf.Clamp(all.size.z, 0.06f, 1.2f));

            // Со своим трупом отрубленное не сталкивается: иначе они разлетятся, будто их
            // разорвало изнутри.
            foreach (Collider mine in man.GetComponentsInChildren<Collider>(false))
            {
                if (mine != null && mine.enabled) Physics.IgnoreCollision(box, mine, true);
            }

            Rigidbody body = lead.AddComponent<Rigidbody>();
            body.mass = cut.name.IndexOf("Leg", StringComparison.OrdinalIgnoreCase) >= 0 ? 9f : 4f;
            body.drag = 0.4f;
            body.angularDrag = 0.6f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            Vector3 way = push.sqrMagnitude > 0f ? push : UnityEngine.Random.insideUnitSphere;
            way.y = 0f;
            if (way.sqrMagnitude < 0.0001f) way = Vector3.forward;

            Vector3 speed = way.normalized * UnityEngine.Random.Range(1.6f, 3.2f)
                + Vector3.up * UnityEngine.Random.Range(1.4f, 2.6f);

            body.AddForce(speed, ForceMode.VelocityChange);
            body.AddTorque(UnityEngine.Random.insideUnitSphere * 8f, ForceMode.VelocityChange);

            Remains keep = man.GetComponent<Remains>();
            if (keep != null) keep.Pieces.Add(lead);

            // Кусок тоже кровит: капает, пока летит и пока лежит, и оставляет след.
            Bleeders.Start(man, lead.transform, null, Vector3.down, Cut, 0.35f);
        }

        // ------------------------------------------------------------------ кровь

        /// <summary>Whether the player has blood turned on in the game's own settings.</summary>
        internal static bool Gory()
        {
            try
            {
                return ConfigManager.Instance == null || ConfigManager.Instance.config == null
                    || !ConfigManager.Instance.config.ContainsKey("EnableBloodEffect")
                    || ConfigManager.Instance.config["EnableBloodEffect"] != "0";
            }
            catch
            {
                return true;
            }
        }

        internal static BloodType BloodOf(UnitAttribute who)
        {
            try
            {
                if (who != null && who.audioManager != null) return who.audioManager.bloodType;
            }
            catch
            {
            }

            return BloodType.redBlood;
        }

        /// <summary>A handful of the game's own sprays at once, fanned out around the way given.</summary>
        internal static void Burst(UnitAttribute who, Vector3 at, Vector3 way, int kind, int many)
        {
            if (!Gory()) return;

            EffectManager fx = EffectManager.instance;
            if (fx == null || AreaManager.Instance == null) return;

            BloodType blood = BloodOf(who);
            if (blood == BloodType.noBlood) return;

            for (int i = 0; i < many; i++)
            {
                Vector3 dir = (way + UnityEngine.Random.insideUnitSphere * 0.6f).normalized;
                if (dir.sqrMagnitude < 0.01f) dir = Vector3.up;

                try
                {
                    fx.CreatBloodEffect(Mathf.Clamp(kind, 0, 2), blood, 0.6f, at, at + dir);
                }
                catch
                {
                }
            }
        }

        // ------------------------------------------------------------------ замер павших

        private sealed class Look
        {
            internal HumaniodUnit man;
            internal string name;
            internal float at;
        }

        private static readonly List<Look> looks = new List<Look>();
        private static readonly HashSet<int> probed = new HashSet<int>();
        private static int noted;

        internal static void Note(UnitAttribute who)
        {
            if (Probe == null || !Probe.Value || noted >= ProbeCount.Value) return;

            HumaniodUnit man = who as HumaniodUnit;
            if (man == null || man.Data == null || !man.Data.trueDead) return;
            if (!probed.Add(man.GetInstanceID())) return;

            noted++;
            looks.Add(new Look { man = man, name = man.Data.unitname, at = Time.time + 2.5f });
        }

        private static void Review()
        {
            for (int i = looks.Count - 1; i >= 0; i--)
            {
                Look look = looks[i];
                if (Time.time < look.at) continue;

                looks.RemoveAt(i);

                if (look.man == null)
                {
                    ItemForgePlugin.Log.LogInfo($"Павший «{look.name}»: тела уже нет.");
                    continue;
                }

                HumaniodUnit man = look.man;

                int parts = 0, loose = 0;
                if (man.bodyParts != null)
                {
                    foreach (UnitBodyPart one in man.bodyParts)
                    {
                        if (one == null) continue;
                        parts++;
                        if (one.rig != null && !one.rig.isKinematic) loose++;
                    }
                }

                float hips = float.NaN;
                try
                {
                    Transform h = Bone(man, HumanBodyBones.Hips, "Hips");
                    if (h != null) hips = h.position.y - man.transform.position.y;
                }
                catch
                {
                }

                ItemForgePlugin.Log.LogInfo($"Павший «{look.name}»: кукла {(man.isRagdolled ? "да" : "нет")}"
                    + $" (разрешена {(man.useRagdoll ? "да" : "нет")}), модель собрана "
                    + $"{(man.umaCreated ? "да" : "нет")}, ждёт куклу {(man.umaWaitToRagdoll ? "да" : "нет")}, "
                    + $"аниматор {(man.ani != null && man.ani.enabled ? "включён" : "выключен")}, "
                    + $"частей куклы {parts}, свободных {loose}, таз над землёй {hips:0.00} м.");
            }
        }

        /// <summary>Something is being done to a body that should be lying still: write down what.</summary>
        internal static void Touched(UnitAttribute who, string what)
        {
            if (Probe == null || !Probe.Value || who == null || who.Data == null) return;
            if (!who.Data.isdead || !probed.Contains(who.GetInstanceID())) return;

            string trace = Environment.StackTrace;
            string[] lines = trace.Split('\n');
            List<string> kept = new List<string>();

            foreach (string line in lines)
            {
                string one = line.Trim();
                if (one.Length == 0 || one.Contains("Environment") || one.Contains("Sever.")) continue;
                if (one.Contains("Harmony") || one.Contains("DMD<")) continue;

                kept.Add(one.Replace("at ", ""));
                if (kept.Count >= 5) break;
            }

            ItemForgePlugin.Log.LogInfo($"Труп «{who.Data.unitname}»: {what} — "
                + string.Join(" ← ", kept.ToArray()));
        }
    }

    // ------------------------------------------------------------------ кровотечение

    internal static class Bleeders
    {
        private static float[] reach;
        private static string reachRead;

        /// <summary>How far this jet flies against the old reach — its own draw each time.</summary>
        internal static float Reach()
        {
            string written = Sever.Reach != null ? (Sever.Reach.Value ?? "") : "";

            if (reach == null || written != reachRead)
            {
                reachRead = written;

                List<float> got = new List<float>();
                foreach (string one in written.Split(','))
                {
                    float much;
                    if (float.TryParse(one.Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out much))
                    {
                        got.Add(Mathf.Clamp(much, 0.01f, 3f));
                    }
                }

                reach = got.ToArray();
            }

            if (reach.Length == 0) return 1f;
            if (reach.Length == 1) return reach[0];

            return UnityEngine.Random.Range(Mathf.Min(reach[0], reach[1]), Mathf.Max(reach[0], reach[1]));
        }

        private static readonly List<Bleeder> running = new List<Bleeder>();

        internal static void Start(UnitAttribute who, Transform from, Transform along, Vector3 local,
            int kind, float strength)
        {
            if (from == null || !Sever.Gory()) return;

            BloodType blood = Sever.BloodOf(who);
            if (blood == BloodType.noBlood) return;

            running.RemoveAll(one => one == null);

            // Больше заданного не кровит разом: старшая рана уступает место свежей.
            while (running.Count >= Mathf.Max(1, Sever.MaxBleeding.Value))
            {
                Bleeder oldest = running[0];
                running.RemoveAt(0);
                if (oldest != null) UnityEngine.Object.Destroy(oldest);
            }

            Bleeder made = from.gameObject.AddComponent<Bleeder>();
            made.from = from;
            made.along = along;
            made.local = local;
            made.kind = Mathf.Clamp(kind, 0, 2);
            made.blood = blood;
            made.strength = Mathf.Max(0.1f, strength);
            made.drip = strength < 0.5f;
            made.total = made.drip ? Mathf.Min(5f, Sever.Bleed.Value) : Sever.Bleed.Value;
            made.left = made.total;

            // Первый толчок — самый сильный: в миг удара жила выбрасывает больше всего.
            if (!made.drip) made.Pulse(0f, 2.2f);

            running.Add(made);
        }
    }

    /// <summary>
    /// A wound that keeps bleeding.
    ///
    /// Кровь из перерезанной жилы идёт толчками, в такт сердцу, и каждый толчок слабее
    /// прежнего; между толчками она сочится и капает. Капли летят по дуге, падают под своей
    /// тяжестью и там, где упали, оставляют пятно. Под раной растёт лужа и расползается
    /// неровно, в ту сторону, куда её повело. Кусок, отлетевший прочь, капает, пока летит и
    /// пока лежит, и оставляет за собой дорожку.
    /// </summary>
    internal sealed class Bleeder : MonoBehaviour
    {
        internal Transform from;
        internal Transform along;
        internal Vector3 local;
        internal int kind;
        internal BloodType blood;
        internal float strength = 1f;
        internal float total = 15f;
        internal float left = 15f;
        internal bool drip;

        private float nextPulse;
        private float nextTrickle;
        private float nextSpray;
        private float nextPool;
        private Vector2 drift;

        private void Update()
        {
            if (Time.deltaTime <= 0f) return;

            left -= Time.deltaTime;
            if (left <= 0f || from == null)
            {
                Destroy(this);
                return;
            }

            float k = Mathf.Clamp01(1f - left / Mathf.Max(0.01f, total));

            // Жила бьёт только первые мгновения, потом рана лишь сочится, а лужа растёт.
            bool spurting = !drip && (total - left) < Mathf.Max(0f, Sever.Fountain.Value);

            if (spurting && Time.time >= nextPulse)
            {
                // Толчки реже и слабее к концу: так останавливается сердце.
                nextPulse = Time.time + Mathf.Lerp(0.55f, 1.5f, k);
                Pulse(k, 1f);
            }

            if (Time.time >= nextTrickle)
            {
                nextTrickle = Time.time + (drip ? 0.09f : Mathf.Lerp(0.1f, 0.22f, k));
                Trickle(k);
            }

            if (spurting && Time.time >= nextSpray)
            {
                // Игровые брызги — редкие и крупные, поверх капель: они дают туман мелкой крови.
                nextSpray = Time.time + Mathf.Lerp(0.9f, 2.4f, k);
                Spray(k);
            }

            if (!drip && Time.time >= nextPool)
            {
                nextPool = Time.time + 0.55f;

                // Лужа ползёт неровно: каждое новое пятно чуть дальше в ту же сторону, куда
                // повело прошлое, и немного вбок.
                drift += UnityEngine.Random.insideUnitCircle * 0.18f;
                drift = Vector2.ClampMagnitude(drift, Sever.Flood.Value * Mathf.Lerp(0.25f, 1f, k));

                Pools.Spill(from.position + new Vector3(drift.x, 0f, drift.y), k * strength, blood, kind);
            }
        }

        private Vector3 Way()
        {
            if (along != null)
            {
                Vector3 way = along.position - from.position;
                if (way.sqrMagnitude > 0.0001f) return way.normalized;
            }

            if (local.sqrMagnitude > 0f) return from.TransformDirection(local).normalized;

            return Vector3.up;
        }

        /// <summary>One beat: a jet of drops out of the wound.</summary>
        internal void Pulse(float k, float boost)
        {
            if (from == null) return;

            int many = Mathf.RoundToInt(Mathf.Lerp(46f, 8f, k) * boost * Mathf.Clamp(strength, 0.4f, 1.5f));
            float fast = Mathf.Lerp(3.6f, 1.2f, k) * Mathf.Clamp(boost, 1f, 1.6f) * Bleeders.Reach();

            Drops.Throw(from.position + Way() * 0.03f, Way(), many, fast, 11f, blood);
        }

        /// <summary>Between the beats it only seeps.</summary>
        private void Trickle(float k)
        {
            if (from == null) return;

            Vector3 way = drip ? Vector3.down : Vector3.Lerp(Way(), Vector3.down, 0.5f).normalized;
            int many = drip ? 2 : Mathf.RoundToInt(Mathf.Lerp(4f, 1f, k));

            float fast = (drip ? 0.4f : Mathf.Lerp(0.9f, 0.3f, k)) * Bleeders.Reach();

            Drops.Throw(from.position, way, many, fast, 35f, blood);
        }

        private void Spray(float k)
        {
            EffectManager fx = EffectManager.instance;
            if (fx == null || AreaManager.Instance == null) return;

            Vector3 way = (Way() + UnityEngine.Random.insideUnitSphere * 0.25f).normalized;
            Vector3 at = from.position + way * 0.04f;

            float share = Mathf.Lerp(0.5f, 0.12f, k) * Mathf.Clamp(strength, 0.3f, 1.5f);

            try
            {
                int before = fx.particles != null ? fx.particles.Count : 0;

                fx.CreatBloodEffect(kind, blood, share, at, at + way);

                // Брызги игры летят далеко: они писаны под удар, а не под жилу. Укорачиваем
                // их полёт той же долей, что и у капель.
                if (fx.particles != null && fx.particles.Count > before)
                {
                    GameObject made = fx.particles[fx.particles.Count - 1];
                    if (made != null)
                    {
                        float short_ = Bleeders.Reach();
                        foreach (ParticleSystem one in made.GetComponentsInChildren<ParticleSystem>(true))
                        {
                            ParticleSystem.MainModule main = one.main;
                            main.startSpeedMultiplier *= short_;
                        }
                    }
                }
            }
            catch
            {
            }
        }
    }

    // ------------------------------------------------------------------ капли

    /// <summary>
    /// Drops of blood that fly, fall and land.
    ///
    /// Своих капель у игры нет — есть брызги, готовые целиком. Капли собраны здесь из того же
    /// материала, что у игровых брызг: вытянутые по полёту, под тяжестью, с падением на землю.
    /// Где капля ударилась о землю — там игровое пятно крови.
    /// </summary>
    internal static class Drops
    {
        private static ParticleSystem system;
        private static bool tried;
        private static bool told;

        private static readonly Color32[] shades =
        {
            new Color32(120, 6, 6, 255), new Color32(96, 4, 4, 255), new Color32(140, 12, 10, 255),
            new Color32(78, 2, 2, 255)
        };

        private static bool Ready()
        {
            if (system != null) return true;
            if (tried) return false;
            tried = true;

            try
            {
                Material ink = Ink();
                if (ink == null)
                {
                    ItemForgePlugin.Log.LogInfo("Капли крови: материала брызг не нашлось, будут только брызги.");
                    return false;
                }

                GameObject go = new GameObject("Кровь: капли");
                UnityEngine.Object.DontDestroyOnLoad(go);

                system = go.AddComponent<ParticleSystem>();
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                ParticleSystem.MainModule main = system.main;
                main.loop = true;
                main.playOnAwake = false;
                main.duration = 5f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.8f);
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.04f);
                main.gravityModifier = 1f;
                main.maxParticles = 2500;
                main.scalingMode = ParticleSystemScalingMode.Local;

                ParticleSystem.EmissionModule emission = system.emission;
                emission.enabled = false;

                ParticleSystem.ShapeModule shape = system.shape;
                shape.enabled = false;

                // Долетела до земли — умерла: дальше за неё отвечает пятно.
                ParticleSystem.CollisionModule hit = system.collision;
                hit.enabled = true;
                hit.type = ParticleSystemCollisionType.World;
                hit.mode = ParticleSystemCollisionMode.Collision3D;
                hit.quality = ParticleSystemCollisionQuality.Medium;
                hit.dampen = 1f;
                hit.bounce = 0f;
                hit.lifetimeLoss = 1f;
                hit.sendCollisionMessages = true;

                int ground = LayerMask.GetMask("Ground", "Default", "Water", "Terrain");
                hit.collidesWith = ground != 0 ? ground : ~0;

                ParticleSystemRenderer render = go.GetComponent<ParticleSystemRenderer>();
                render.sharedMaterial = ink;
                render.renderMode = ParticleSystemRenderMode.Stretch;
                render.velocityScale = 0.035f;
                render.lengthScale = 2.2f;
                render.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                go.AddComponent<Landing>().system = system;

                system.Play();

                if (!told)
                {
                    told = true;
                    ItemForgePlugin.Log.LogInfo($"Капли крови: материал «{ink.name}» "
                        + $"({(ink.shader != null ? ink.shader.name : "?")}).");
                }

                return true;
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог собрать капли крови: " + e.Message);
                system = null;
                return false;
            }
        }

        /// <summary>The material of the game's own blood drops.</summary>
        private static Material Ink()
        {
            EffectManager fx = EffectManager.instance;
            if (fx == null) return null;

            foreach (GameObject[] set in new[] { fx.bloodStab_M, fx.bloodSlash_M, fx.bloodStab_L, fx.bloodSlash_L })
            {
                if (set == null) continue;

                foreach (GameObject one in set)
                {
                    if (one == null) continue;

                    Material best = null;
                    foreach (ParticleSystemRenderer render in one.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    {
                        if (render == null || render.sharedMaterial == null) continue;

                        // Лучше всего — те, что уже тянутся по полёту: это и есть капли.
                        if (render.renderMode == ParticleSystemRenderMode.Stretch) return render.sharedMaterial;
                        if (best == null) best = render.sharedMaterial;
                    }

                    if (best != null) return best;
                }
            }

            return null;
        }

        /// <summary>Throws drops out of a point along a direction, fanned out within a cone.</summary>
        internal static void Throw(Vector3 at, Vector3 way, int many, float fast, float cone, BloodType blood)
        {
            if (many <= 0 || !Ready()) return;

            ParticleSystem.EmitParams one = new ParticleSystem.EmitParams();

            for (int i = 0; i < many; i++)
            {
                Vector3 dir = Quaternion.AngleAxis(UnityEngine.Random.Range(0f, 360f), way)
                    * Quaternion.AngleAxis(UnityEngine.Random.Range(0f, cone), Perp(way)) * way;

                one.position = at + UnityEngine.Random.insideUnitSphere * 0.015f;
                one.velocity = dir * fast * UnityEngine.Random.Range(0.6f, 1.15f);
                one.startSize = UnityEngine.Random.Range(0.012f, 0.045f);
                one.startLifetime = UnityEngine.Random.Range(1.1f, 1.8f);
                one.startColor = Shade(blood);

                system.Emit(one, 1);
            }
        }

        private static Vector3 Perp(Vector3 way)
        {
            Vector3 side = Vector3.Cross(way, Vector3.up);
            if (side.sqrMagnitude < 0.0001f) side = Vector3.Cross(way, Vector3.right);
            return side.normalized;
        }

        private static Color32 Shade(BloodType blood)
        {
            Color32 red = shades[UnityEngine.Random.Range(0, shades.Length)];

            if (blood == BloodType.greenBlood) return new Color32(red.g, red.r, red.b, 255);
            if (blood == BloodType.yellowBlood) return new Color32(red.r, red.r, red.b, 255);

            return red;
        }
    }

    /// <summary>Where a drop lands, a spot of blood is left — the game's own.</summary>
    internal sealed class Landing : MonoBehaviour
    {
        internal ParticleSystem system;

        private readonly List<ParticleCollisionEvent> events = new List<ParticleCollisionEvent>();
        private float nextSpot;

        private void OnParticleCollision(GameObject other)
        {
            if (system == null || Time.time < nextSpot) return;

            try
            {
                int got = system.GetCollisionEvents(other, events);

                for (int i = 0; i < got; i++)
                {
                    if (Time.time < nextSpot) break;

                    // Пятно — не с каждой капли: иначе на одну лужу приходится сто брызг.
                    if (UnityEngine.Random.value > 0.35f) continue;

                    nextSpot = Time.time + 0.05f;
                    Pools.Spot(events[i].intersection);
                }
            }
            catch
            {
            }
        }
    }

    // ------------------------------------------------------------------ лужи

    /// <summary>
    /// Blood on the ground.
    ///
    /// Готовых луж у игры нет. Пятна на земле оставляют её же брызги, если бросить их вниз, в
    /// землю, — так игра и рисует кровь под раненым. Лужа складывается из таких пятен, а
    /// держатся они дольше обычного: игровые исчезают через пятнадцать секунд, наши лежат
    /// столько, сколько сказано в «PoolLast».
    /// </summary>
    internal static class Pools
    {
        private static readonly List<GameObject> lying = new List<GameObject>();

        /// <summary>A patch of the pool under this point.</summary>
        internal static void Spill(Vector3 over, float k, BloodType blood, int kind)
        {
            Vector3 ground;
            if (!Ground(over, out ground)) return;

            float share = Mathf.Lerp(0.45f, 0.18f, Mathf.Clamp01(k));
            if (Lay(ground, share, blood, Mathf.Clamp(kind, 0, 2), 0.35f, -1f))
            {
                // Лужа лежит неделю: запоминаем, где она, чтобы найти её здесь же, вернувшись.
                Lingers.Pool(ground, share, blood, Mathf.Clamp(kind, 0, 2));
            }
        }

        /// <summary>A remembered pool, laid back where it was, for what is left of its week.</summary>
        internal static bool Relay(Vector3 ground, float share, BloodType blood, int kind, float life)
        {
            return Lay(ground, share, blood, Mathf.Clamp(kind, 0, 2), 0.35f, life);
        }

        /// <summary>A small spot where a drop landed.</summary>
        internal static void Spot(Vector3 at)
        {
            Lay(at, 0.05f, BloodType.redBlood, 2, 0.12f, -1f);
        }

        private static bool Lay(Vector3 ground, float share, BloodType blood, int kind, float above, float life)
        {
            EffectManager fx = EffectManager.instance;
            if (fx == null || AreaManager.Instance == null || !Sever.Gory()) return false;

            try
            {
                int before = fx.particles != null ? fx.particles.Count : 0;

                // Брызги вниз, в землю, — как игра бросает их под раненого.
                Vector3 from = ground + Vector3.up * above;
                fx.CreatBloodEffect(kind, blood, share, from, ground);

                if (fx.particles == null || fx.particles.Count <= before) return false;

                GameObject made = fx.particles[fx.particles.Count - 1];
                if (made == null) return false;

                // Лужа лежит неделю по игровому времени; пятно от капли — сколько сказано.
                float lasts = life > 0f ? life
                    : (Lingers.Enabled != null && Lingers.Enabled.Value && above > 0.3f)
                        ? Lingers.Hours.Value * TimeManager.LOCAL_MAP_HOUR_IN_SECOND
                        : Sever.PoolLast.Value;

                EffectBase keeper = made.GetComponent<EffectBase>();
                if (keeper != null) keeper.lifespan = lasts;

                lying.RemoveAll(one => one == null);
                lying.Add(made);

                while (lying.Count > Mathf.Max(10, Sever.MaxPools.Value))
                {
                    GameObject oldest = lying[0];
                    lying.RemoveAt(0);
                    if (oldest != null) UnityEngine.Object.Destroy(oldest);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>The ground under a point: the first thing below it that is not a body.</summary>
        private static bool Ground(Vector3 over, out Vector3 at)
        {
            at = over;

            RaycastHit[] hits = Physics.RaycastAll(over + Vector3.up * 1.5f, Vector3.down, 6f,
                ~0, QueryTriggerInteraction.Ignore);

            float best = float.MaxValue;
            bool found = false;

            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null) continue;
                if (hit.collider.GetComponentInParent<UnitAttribute>() != null) continue;
                if (hit.collider.GetComponentInParent<OwnMesh>() != null) continue;
                if (hit.collider.GetComponentInParent<Rigidbody>() != null
                    && !hit.collider.GetComponentInParent<Rigidbody>().isKinematic) continue;

                if (hit.distance < best)
                {
                    best = hit.distance;
                    at = hit.point;
                    found = true;
                }
            }

            return found;
        }
    }

    // ------------------------------------------------------------------ останки

    /// <summary>Owns what the cutting made, and puts the cut back if the game rebuilds the body.</summary>
    internal sealed class Remains : MonoBehaviour
    {
        internal HumaniodUnit Owner;
        internal readonly List<GameObject> Pieces = new List<GameObject>();
        internal readonly List<HashSet<Transform>> Cuts = new List<HashSet<Transform>>();

        private readonly Dictionary<SkinnedMeshRenderer, Mesh> ours = new Dictionary<SkinnedMeshRenderer, Mesh>();
        private float nextLook;

        internal void Remember(SkinnedMeshRenderer skin, Mesh made)
        {
            Mesh old;
            // Удаляем только то, что сделали сами: чужая сетка принадлежит игре.
            if (ours.TryGetValue(skin, out old) && old != null && old != made
                && old.name.EndsWith(" (рана)")) Destroy(old);

            ours[skin] = made;
        }

        // Игра пересобирает тело, когда с него снимают вещи, — например, при обыске. Тогда
        // сетка приходит новая и целая, и отрубленная голова вернулась бы на место.
        private void LateUpdate()
        {
            if (Time.time < nextLook) return;
            nextLook = Time.time + 0.5f;

            try
            {
                foreach (SkinnedMeshRenderer skin in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (skin == null || skin.sharedMesh == null) continue;
                    if (Sever.Cloth(skin)) continue;

                    Mesh mine;
                    if (ours.TryGetValue(skin, out mine) && (object)mine == (object)skin.sharedMesh) continue;

                    // Чужая, свежая сетка — режем её тем же разрезом, уже без летящих кусков.
                    bool touched = false;
                    foreach (HashSet<Transform> below in Cuts)
                    {
                        if (Sever.Split(skin, below, null, this)) touched = true;
                    }

                    if (!touched && !ours.ContainsKey(skin)) ours[skin] = skin.sharedMesh;
                }
            }
            catch
            {
            }
        }

        private void OnDestroy()
        {
            foreach (GameObject one in Pieces)
            {
                if (one != null) Destroy(one);
            }

            foreach (KeyValuePair<SkinnedMeshRenderer, Mesh> one in ours)
            {
                if (one.Value != null && one.Value.name.EndsWith(" (рана)")) Destroy(one.Value);
            }
        }
    }

    /// <summary>Destroys the mesh and the materials a severed piece was built from, with the piece.</summary>
    internal sealed class OwnMesh : MonoBehaviour
    {
        internal Mesh mesh;
        internal List<Material> materials;

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (materials == null) return;
            foreach (Material one in materials)
            {
                if (one != null) Destroy(one);
            }
        }
    }

    /// <summary>
    /// Keeps a severed piece drawn: a material that is gone or cannot draw is replaced with plain flesh.
    ///
    /// Отчего кусок бывает розовым, из журнала пока не видно. Поэтому кусок сам раз в секунду
    /// смотрит на свои материалы: пропал материал, нет у него шейдера или шейдер не рисует — на
    /// его место ложится простой цвет плоти, а в журнал пишется, что именно случилось, чтобы
    /// найти настоящую причину.
    /// </summary>
    internal sealed class PieceWatch : MonoBehaviour
    {
        internal SkinnedMeshRenderer body;

        private SkinnedMeshRenderer mine;
        private float next;
        private float born;
        private bool looked;

        private static Material flesh;
        private static int told;

        private void Start()
        {
            mine = GetComponent<SkinnedMeshRenderer>();
            born = Time.time;
        }

        private void Update()
        {
            if (mine == null || Time.time < next) return;
            next = Time.time + 1f;

            try
            {
                Material[] mats = mine.sharedMaterials;
                bool swapped = false;
                List<string> why = new List<string>();

                for (int i = 0; i < mats.Length; i++)
                {
                    Material one = mats[i];
                    string bad = one == null ? "материала нет"
                        : one.shader == null ? "у материала нет шейдера"
                        : !one.shader.isSupported ? "шейдер «" + one.shader.name + "» не рисует"
                        : null;
                    if (bad == null) continue;

                    Material plain = Flesh();
                    if (plain == null) continue;
                    mats[i] = plain;
                    swapped = true;
                    why.Add(bad);
                }

                if (swapped) mine.sharedMaterials = mats;

                // Один раз, через две секунды после удара, — как кусок выглядит изнутри.
                if ((swapped || (!looked && Time.time - born > 2f)) && told < 8)
                {
                    looked = true;
                    told++;
                    List<string> seen = new List<string>();
                    foreach (Material one in mine.sharedMaterials)
                    {
                        seen.Add(one == null ? "нет" : $"{one.name} ({(one.shader != null ? one.shader.name + (one.shader.isSupported ? "" : ", не рисует") : "без шейдера")})");
                    }
                    string probes = body != null
                        ? $"пробы света {mine.lightProbeUsage}/{body.lightProbeUsage}, отражений {mine.reflectionProbeUsage}/{body.reflectionProbeUsage}, слои {mine.renderingLayerMask}/{body.renderingLayerMask}"
                        : "тела уже нет";
                    ItemForgePlugin.Log.LogInfo($"Отсечённое «{name}»: {(swapped ? "заменено: " + string.Join(", ", why.ToArray()) + "; " : "")}"
                        + $"материалы: {string.Join(", ", seen.ToArray())}; {probes}.");
                }
            }
            catch
            {
            }
        }

        private static Material Flesh()
        {
            if (flesh != null) return flesh;

            foreach (string name in new[] { "Standard", "Legacy Shaders/Diffuse", "Mobile/Diffuse", "Unlit/Color" })
            {
                Shader shader = Shader.Find(name);
                if (shader == null || !shader.isSupported) continue;

                flesh = new Material(shader) { name = "Отсечённое: плоть" };
                flesh.color = new Color(0.32f, 0.07f, 0.05f);
                return flesh;
            }

            return null;
        }
    }

    // ------------------------------------------------------------------ перехваты

    // Смерть: разбираем, что она сделала с телом, и записываем павшего для замера.
    [HarmonyPatch(typeof(UnitAttribute), "Die")]
    internal static class Die_Sever_Patch
    {
        private static void Postfix(UnitAttribute __instance, UnitAttribute killer)
        {
            try { Sever.Note(__instance); } catch { }
            try { Sever.Fell(__instance, killer); } catch { }
        }
    }

    // Замер: кто трогает лежащее тело.
    [HarmonyPatch(typeof(HumaniodUnit), "UMABuildCharacter")]
    internal static class UMABuild_Probe_Patch
    {
        private static void Prefix(HumaniodUnit __instance)
        {
            try { Sever.Touched(__instance, "модель пересобирается"); } catch { }
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "Revive")]
    internal static class Revive_Probe_Patch
    {
        private static void Prefix(UnitAttribute __instance)
        {
            try { Sever.Touched(__instance, "поднимают из мёртвых"); } catch { }
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "UseRagdoll", new Type[] { typeof(bool) })]
    internal static class UseRagdoll_Probe_Patch
    {
        private static void Prefix(UnitAttribute __instance, bool newValue)
        {
            if (newValue) return;

            try { Sever.Touched(__instance, "с тела снимают куклу"); } catch { }
        }
    }
}
