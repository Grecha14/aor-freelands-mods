using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using spell;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace DemonLook
{
    /// <summary>
    /// The Soul Feast: the demon drinks a life, and the life becomes his.
    ///
    /// У демона «Губительное прикосновение» — не проклятие оружию, а «Поглощение души». В бою
    /// оно не применяется вовсе: только к спящему, к лежащему без сознания и к пленнику, и
    /// только пока сам демон не в схватке. Четыре секунды он стоит над жертвой, и жизнь
    /// уходит из неё к нему струйкой тьмы — тем же видом, каким «Истощение души» тянет силы.
    /// Потом жертва умирает, а её душа ложится ему в силу и в Тьму.
    ///
    /// Кто это увидит — горожанин или стражник того же города, не спящий и заметивший его
    /// своими глазами, по игровым правилам зрения, света и слуха, — тот поднимет город:
    /// город становится врагом, а молва о демоне обходит весь мир. Не увидел никто — утром
    /// найдут тело, и подозрение ляжет на него: шкала преступлений города вырастет втрое
    /// против кражи, меньше — где ему верят.
    ///
    /// Спутники этого не выносят: все, кроме тёмных магов, бегут в ужасе и уходят из отряда.
    /// Питомцы — не люди и остаются.
    ///
    /// У всех прочих «Губительное прикосновение» — как в игре: у демона своя копия заклинания,
    /// и в его книге она стоит на месте подлинника.
    /// </summary>
    internal static class Feast
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Seconds;
        internal static ConfigEntry<float> Theft;
        internal static ConfigEntry<float> Witness;
        internal static ConfigEntry<bool> Flee;
        internal static ConfigEntry<float> RevelHours;
        internal static ConfigEntry<float> RevelNear;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Feast", "Enabled", true,
                "Turn the demon own Corrupting Touch into the Soul Feast: out of battle, on the "
                + "sleeping, the unconscious and the captive, it drinks the life and takes the soul.");

            Seconds = config.Bind("Feast", "Seconds", 4f,
                new ConfigDescription("How long the demon stands over the victim.",
                    new AcceptableValueRange<float>(0.5f, 30f)));

            Theft = config.Bind("Feast", "Theft", 3f,
                new ConfigDescription("How many times the town crime for a theft the body of one drunk "
                    + "unseen adds when it is found in the morning.",
                    new AcceptableValueRange<float>(0f, 100f)));

            Witness = config.Bind("Feast", "Witness", 25f,
                new ConfigDescription("How far, in metres, a townsman may see the feast.",
                    new AcceptableValueRange<float>(1f, 100f)));

            Flee = config.Bind("Feast", "Flee", true,
                "Let every companion but the dark mages flee in horror and leave the party at a feast.");

            RevelHours = config.Bind("Feast", "RevelHours", 4f,
                new ConfigDescription("Game hours the demon wears the traces of a feast: Revelling in "
                    + "Soul Blood. Whoever of a town sees him up close in that time knows him.",
                    new AcceptableValueRange<float>(0f, 72f)));

            RevelNear = config.Bind("Feast", "RevelNear", 2f,
                new ConfigDescription("How close, in metres, a townsman must stand to see the traces.",
                    new AcceptableValueRange<float>(0.5f, 20f)));
        }

        internal const int FeastId = 907101;
        private const int TouchId = 237;

        private static UISpellInfo feast;
        private static UISpellInfo touch;
        private static bool registered;

        internal static bool IsFeast(UISpellInfo s)
        {
            return s != null && (s.ID == FeastId || (feast != null && (object)s == (object)feast));
        }

        internal static bool IsTouchOriginal(UISpellInfo s)
        {
            return s != null && s.ID != FeastId && (s.ID == TouchId || s.name == "CorruptingTouch");
        }

        internal static UISpellInfo FeastSpell() { Register(); return feast; }
        internal static UISpellInfo TouchSpell() { Register(); return touch; }

        /// <summary>Makes the demon own copies of the two dark spells, once, as early as the book exists.</summary>
        internal static void Register()
        {
            if (registered || Enabled == null || !Enabled.Value) return;

            // Пока нет самой игры, книгу не ищем: её поиск без игры шарит по всей сцене.
            if (gameManager.GM == null) return;

            UISpellDatabase db;
            try { db = UISpellDatabase.Instance; }
            catch { return; }
            if (db == null || db.spells == null || db.spells.Length == 0) return;

            try
            {
                UISpellInfo original = db.GetByID(TouchId) ?? db.GetByName("CorruptingTouch");
                if (original == null)
                {
                    registered = true;
                    DemonLookPlugin.Log.LogWarning("Поглощение души: «Губительного прикосновения» нет в книге.");
                    return;
                }

                touch = original;

                UISpellInfo copy = db.GetByID(FeastId);
                bool fresh = copy == null;
                if (fresh)
                {
                    copy = UnityEngine.Object.Instantiate(original);
                    copy.name = "DemonSoulFeast";
                    copy.ID = FeastId;
                }

                copy.Name = "Поглощение души";
                copy.description = "Только вне боя: над спящим, лежащим без сознания или пленником. "
                    + "Четыре секунды демон пьёт жизнь, жертва умирает, её душа становится его силой и "
                    + "его Тьмой. Увидит горожанин — город станет врагом, и мир узнает, кто вы. Спутники, "
                    + "кроме тёмных магов, в ужасе уходят.";
                copy.DescriptionParam = new List<DecriptionParam>();
                copy.RequireMastery = 0;
                copy.RequireLevel = 0;

                List<UISpellInfo> all = new List<UISpellInfo>(db.spells);
                if (fresh) all.Add(copy);

                Veil.Register(db, all);

                db.spells = all.ToArray();
                feast = copy;
                registered = true;

                // Школа кэширует свой перечень — пусть соберёт заново, уже с нашими.
                try
                {
                    var sets = Traverse.Create(db).Field("sets").GetValue() as System.Collections.IDictionary;
                    if (sets != null) sets.Clear();
                }
                catch
                {
                }

                DemonLookPlugin.Log.LogInfo("Поглощение души и Вуаль тьмы заведены для демона.");
            }
            catch (Exception e)
            {
                registered = true;
                DemonLookPlugin.Log.LogError("Поглощение души: не смог завести: " + e);
            }
        }

        // ------------------------------------------------------------------ кто годится

        internal enum Kind { None, Sleeper, Fallen, Prisoner }

        internal static Kind Victim(UnitAttribute demon, UnitAttribute target)
        {
            if (target == null || demon == null || (object)target == (object)demon) return Kind.None;
            if (target.Data == null || target.Data.trueDead || target.inParty) return Kind.None;
            if (!(target is HumaniodUnit) || Kids.Is(target)) return Kind.None;
            if (!Racial.Counts(target.Data.race)) return Kind.None;
            if (Hostility.IsKin(target)) return Kind.None;

            try
            {
                if (target.Data.isdead) return Kind.Fallen;

                if (target.buffmanger != null && (target.buffmanger.ContainBuff("Knockout") || target.buffmanger.ContainBuff("knockoutBuff")))
                    return Kind.Fallen;

                NPCSaveData npc = target.Data as NPCSaveData;
                if (target.isCaged || (npc != null && PartyManager.instance != null && PartyManager.instance.prisoners.Contains(npc)))
                    return Kind.Prisoner;

                if (target.isSleeping) return Kind.Sleeper;
            }
            catch
            {
            }

            return Kind.None;
        }

        /// <summary>Whether the demon is aiming or casting the feast right now.</summary>
        internal static bool Aiming(UnitAttribute user)
        {
            try
            {
                if (GameController.GC != null && gameManager.GM != null && gameManager.GM.mousestate == CursorState.targetspell
                    && IsFeast(GameController.GC.currentSpell)) return true;

                SpellManager book = user.spellmanger;
                return book != null && book.OnCastSpell != null && IsFeast(book.OnCastSpell.theSpell)
                    && user.stateMachine != null && user.stateMachine.simpleSstate == SimpleState.casting;
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------------ сам ритуал

        private sealed class Rite
        {
            internal HumaniodUnit demon;
            internal UnitAttribute victim;
            internal Kind kind;
            internal SpellBase cast;
            internal float started;
            internal Vector3 from;
            internal float nextWisp;
            internal float nextLook;
            internal float nextPose;
            internal bool peaceful;
            internal bool seen;
            internal string seenBy;
        }

        private static Rite rite;
        private static readonly List<UnitAttribute> toEnd = new List<UnitAttribute>();

        internal static bool Active => rite != null;

        internal static void EndLater(UnitAttribute unit)
        {
            if (unit != null && !toEnd.Contains(unit)) toEnd.Add(unit);
        }

        /// <summary>The cast has been made: start the feast, or say why it cannot be.</summary>
        internal static void Begin(UnitAttribute caster, SpellBase cast)
        {
            EndLater(caster);

            HumaniodUnit demon = caster as HumaniodUnit;
            if (demon == null || !Racial.IsDemon(demon)) return;

            if (rite != null)
            {
                Souls.Say("Демон уже пьёт душу.");
                return;
            }

            if (demon.isEngaged)
            {
                Souls.Say("Поглощение души — не для боя.");
                return;
            }

            UnitAttribute victim = cast.targetUnit;
            Kind kind = Victim(demon, victim);
            if (kind == Kind.None)
            {
                Souls.Say("Душу можно выпить только у спящего, у лежащего без сознания или у пленника.");
                return;
            }

            float cost = cast.theSpell.EPCost(demon);
            if (!demon.CostMP(cost))
            {
                Souls.Say("Не хватает маны на поглощение души.");
                return;
            }

            rite = new Rite
            {
                demon = demon,
                victim = victim,
                kind = kind,
                cast = cast,
                started = Time.time,
                from = demon.transform.position,
                peaceful = kind != Kind.Prisoner && Racial.Peaceful(victim),
            };

            try { demon.Stop(); } catch { }
            Face(demon, victim);
            Pose(demon, true);

            // Жертва в параличе: замирает, как лежала, и ничего не может сделать.
            try { victim.Pause(true); } catch { }

            Wisps.Load();

            try { if (demon.lifebar != null) demon.lifebar.ShowTextTag("<color=#B01020>Поглощение души</color>", 2f); } catch { }
        }

        private static void Face(UnitAttribute demon, UnitAttribute victim)
        {
            try
            {
                Vector3 way = victim.transform.position - demon.transform.position;
                way.y = 0f;
                if (way.sqrMagnitude > 0.0001f) demon.transform.rotation = Quaternion.LookRotation(way);
            }
            catch
            {
            }
        }

        private static void Pose(UnitAttribute demon, bool on)
        {
            try
            {
                if (demon.ani == null) return;
                if (on)
                {
                    string anim = null;
                    UISpellInfo look = Wisps.DrainSpell();
                    if (look != null && !string.IsNullOrEmpty(look.prepareAnim)) anim = look.prepareAnim;
                    else if (feast != null && !string.IsNullOrEmpty(feast.prepareAnim)) anim = feast.prepareAnim;

                    if (anim != null) demon.CrossFadeAni(anim, 0.2f);
                }
                demon.ani.SetBool("isCasting", on);
            }
            catch
            {
            }
        }

        private static void Stop(string why)
        {
            Rite was = rite;
            rite = null;
            if (was == null) return;

            try { if (was.victim != null && was.victim.Data != null) was.victim.Pause(false); } catch { }
            Pose(was.demon, false);
            if (!string.IsNullOrEmpty(why)) Souls.Say(why);
        }

        internal static void Tick()
        {
            // Игровое состояние каста закрываем на кадр позже: игра ещё дописывает своё.
            if (toEnd.Count > 0)
            {
                foreach (UnitAttribute unit in toEnd)
                {
                    try
                    {
                        if (unit != null && unit.spellmanger != null && unit.stateMachine != null
                            && unit.stateMachine.simpleSstate == SimpleState.casting)
                        {
                            unit.spellmanger.EndCastSpell();
                        }
                    }
                    catch
                    {
                    }
                }
                toEnd.Clear();

                if (rite != null) Pose(rite.demon, true);
            }

            Wisps.Tick();

            try { WatchRevel(); } catch { }

            if (rite == null) return;

            try
            {
                Rite r = rite;
                float now = Time.time;

                if (r.demon == null || r.demon.Data == null || r.demon.Data.isdead) { Stop(null); return; }
                if (r.victim == null || r.victim.Data == null || r.victim.Data.trueDead) { Stop("Жертвы больше нет."); return; }
                if (r.demon.isEngaged) { Stop("На демона напали — поглощение прервано."); return; }
                if ((r.demon.transform.position - r.from).sqrMagnitude > 0.8f * 0.8f) { Stop("Поглощение прервано."); return; }

                if (now >= r.nextPose)
                {
                    r.nextPose = now + 0.5f;
                    try { if (r.demon.ani != null) r.demon.ani.SetBool("isCasting", true); } catch { }
                }

                if (now >= r.nextWisp)
                {
                    r.nextWisp = now + 0.18f;
                    Wisps.Send(r.victim, r.demon);
                }

                if (now >= r.nextLook && r.kind != Kind.Prisoner && !r.seen)
                {
                    r.nextLook = now + 0.25f;
                    Look(r);
                }

                if (now - r.started >= Seconds.Value) Finish(r);
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Поглощение души сорвалось: " + e.Message);
                Stop(null);
            }
        }

        private static bool Townsman(UnitAttribute u, UnitAttribute victim, UnitAttribute demon)
        {
            if (u == null || (object)u == (object)demon || (object)u == (object)victim) return false;
            if (u.Data == null || u.Data.isdead || u.inParty || u.Data.team == Faction.player || u.isSleeping) return false;
            if (!(u is HumaniodUnit) || Hostility.IsKin(u)) return false;
            if (u.Data.team == victim.Data.team) return true;
            if (GameController.IsAlly(u, victim)) return true;
            NPCSaveData npc = u.Data as NPCSaveData;
            return npc != null && npc.career == CareerType.Guard;
        }

        /// <summary>Who of the victim people looks this way and sees him. Crouched, he is found only as the game finds the hidden.</summary>
        private static void Look(Rite r)
        {
            if (r.demon.isCrouching) return;

            foreach (UnitAttribute u in UnitAttribute.GetUnitsInRangeNoCollider(r.demon, Witness.Value, r.demon.transform.position, TargetAllow.all))
            {
                if (!Townsman(u, r.victim, r.demon)) continue;
                if (!u.InSenseRange(r.demon)) continue;

                r.seen = true;
                r.seenBy = u.Data.unitname;
                return;
            }
        }

        /// <summary>Someone has just noticed the demon: if it is one of the victim people, the feast is seen.</summary>
        internal static void Sensed(UnitAttribute who, UnitAttribute whom)
        {
            Rite r = rite;
            if (r == null || r.seen || r.kind == Kind.Prisoner || (object)whom != (object)r.demon) return;
            if (!Townsman(who, r.victim, r.demon)) return;

            r.seen = true;
            r.seenBy = who.Data != null ? who.Data.unitname : "";
        }

        private static void Finish(Rite r)
        {
            rite = null;
            Pose(r.demon, false);

            UnitAttribute victim = r.victim;
            try { victim.Pause(false); } catch { }
            string whose = victim.Data != null ? victim.Data.unitname : "";
            int level = victim.Data != null ? victim.Data.level : 1;
            Faction team = victim.Data.team;

            try
            {
                victim.Data.canKill = true;
                victim.Die(null, forceDie: true);
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Поглощение души: жертва не умерла: " + e.Message);
            }

            try { Wisps.Absorb(r.demon); } catch { }
            try { if (r.cast != null) r.cast.StartCD(); } catch { }

            Revel(r.demon);

            Souls.Feed(r.demon, whose, Souls.Gain(level));

            if (r.kind != Kind.Prisoner)
            {
                bool town = false;
                try { town = team.FactionHasCrime(); } catch { }

                if (town)
                {
                    try
                    {
                        if (r.seen)
                        {
                            int hostile = UICrimeDatabase.Instance.GetCrimeValueFactor(CrimeDataType.HostileThreshold);
                            FactionManager.Instance.AddCrimeToPlayer(team, Mathf.Max(1, hostile));
                            Souls.Reveal();
                            Souls.Say($"Вас видели над телом «{whose}»" + (string.IsNullOrEmpty(r.seenBy) ? "" : $" — {r.seenBy}")
                                + ". Город объявил вас врагом, и молва о демоне уже идёт по миру.");
                        }
                        else
                        {
                            // Никто не видел: тело найдут утром, и тогда подозрение ляжет на него.
                            Souls.Bury(team, whose);
                        }
                    }
                    catch (Exception e)
                    {
                        DemonLookPlugin.Log.LogWarning("Поглощение души: преступление не записалось: " + e.Message);
                    }
                }

                if (r.peaceful) Racial.Slew();
            }

            Scatter(r.demon);
        }

        // ------------------------------------------------------------------ следы пира

        private const string RevelId = "DemonLookRevel";
        private static readonly HashSet<int> knownIn = new HashSet<int>();
        private static float nextRevelLook;

        /// <summary>The demon wears the traces of the feast for some hours: revelling, sinister, knowable up close.</summary>
        private static void Revel(HumaniodUnit demon)
        {
            if (demon == null || demon.buffmanger == null || RevelHours.Value <= 0f) return;

            try
            {
                UIBuffInfo card = Souls.Card(RevelId, "Упивание кровью души",
                    "Демон ликует, и в нём проступают зловещие черты. Кто из горожан или стражи увидит "
                    + $"его ближе чем на {RevelNear.Value:0.#} м, тот узнает демона.", null, true);
                if (card == null) return;

                card.durationIsHour = true;
                card.duration = RevelHours.Value;
                card.showDuration = true;

                if (demon.buffmanger.ContainBuff(RevelId)) demon.buffmanger.RemoveBuff(RevelId);

                BuffBase buff = new BuffBase(card, demon);
                buff.duration = RevelHours.Value;
                demon.buffmanger.AddBuff(buff);

                knownIn.Clear();
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Поглощение души: следы не легли: " + e.Message);
            }
        }

        /// <summary>Whether this one sees the demon with his own eyes, this close.</summary>
        private static bool Sees(UnitAttribute u, UnitAttribute demon)
        {
            Vector3 way = demon.transform.position - u.transform.position;
            float reach = Mathf.Min(RevelNear.Value, u.senseRange * demon.SeeAvoidanceMD);
            if (way.magnitude > reach) return false;

            way.y = 0f;
            if (way.sqrMagnitude > 0.0001f && Vector3.Angle(u.transform.forward, way) > u.senseAngel * demon.AngleAvoidanceMD / 2f) return false;

            return u.SightNotBlocked(demon);
        }

        /// <summary>While the traces are on him, a townsman who looks at him up close knows him.</summary>
        private static void WatchRevel()
        {
            float now = Time.time;
            if (now < nextRevelLook) return;
            nextRevelLook = now + 0.5f;

            HumaniodUnit demon = Souls.Demon();
            if (demon == null || demon.buffmanger == null || !demon.buffmanger.ContainBuff(RevelId)) return;

            foreach (UnitAttribute u in UnitAttribute.GetUnitsInRangeNoCollider(demon, RevelNear.Value + 0.5f, demon.transform.position, TargetAllow.all))
            {
                if (u == null || (object)u == (object)demon || u.Data == null || u.Data.isdead) continue;
                if (u.inParty || u.Data.team == Faction.player || u.isSleeping || !(u is HumaniodUnit)) continue;
                if (Hostility.IsKin(u)) continue;

                Faction team = u.Data.team;
                bool town = false;
                try { town = team.FactionHasCrime(); } catch { }
                NPCSaveData npc = u.Data as NPCSaveData;
                bool guard = npc != null && npc.career == CareerType.Guard;
                if (!town && !guard) continue;

                if (!Sees(u, demon)) continue;
                if (!knownIn.Add((int)team)) continue;

                try
                {
                    if (town)
                    {
                        int hostile = UICrimeDatabase.Instance.GetCrimeValueFactor(CrimeDataType.HostileThreshold);
                        FactionManager.Instance.AddCrimeToPlayer(team, Mathf.Max(1, hostile));
                    }
                }
                catch
                {
                }

                Souls.Reveal();
                Souls.Say($"«{u.Data.unitname}» разглядел на вас следы выпитой души. Город поднят, "
                    + "и молва о демоне уже идёт по миру.");
                return;
            }
        }

        // ------------------------------------------------------------------ спутники

        private static bool DarkMage(HumaniodUnit m)
        {
            try
            {
                if (Hostility.IsKin(m)) return true;
                if (m.Data != null && m.Data.skillSet != null && m.Data.skillSet.Contains(SkillSet.black)) return true;
                if (m.talentmanger != null && m.talentmanger.FindTalent("DarknessAffinity") != null) return true;
            }
            catch
            {
            }
            return false;
        }

        /// <summary>Everyone who is not of the dark flees in horror and leaves the party.</summary>
        private static void Scatter(HumaniodUnit demon)
        {
            if (!Flee.Value || PartyManager.instance == null || PartyManager.instance.partyMembers == null) return;

            List<HumaniodUnit> fleeing = new List<HumaniodUnit>();
            foreach (HumaniodUnit m in PartyManager.instance.partyMembers)
            {
                if (m == null || (object)m == (object)demon || m.Data == null || m.Data.isdead) continue;
                if (Racial.IsDemon(m) || DarkMage(m)) continue;
                if ((bool)m.summonComponent) continue;
                fleeing.Add(m);
            }

            UIBuffInfo fear = null;
            try { fear = UIBuffDatabase.Instance.GetByID("Fleeing"); } catch { }

            foreach (HumaniodUnit m in fleeing)
            {
                try
                {
                    NPCSaveData npc = m.Data as NPCSaveData;
                    if (npc != null && PartyManager.instance.companions.Contains(npc)) PartyManager.instance.RemoveCompanion(npc);
                    else PartyManager.instance.RemovePartyMember(m);

                    if (fear != null && m.buffmanger != null)
                    {
                        BuffBase run = new BuffBase(fear, demon);
                        run.duration = 10f;
                        m.buffmanger.AddBuff(run);
                    }

                    if (m.lifebar != null) m.lifebar.ShowTextTag("<color=#E0E0E0>В ужасе!</color>", 3f);
                    Souls.Say($"«{m.Data.unitname}» в ужасе бежит от вас и покидает отряд.");
                }
                catch (Exception e)
                {
                    DemonLookPlugin.Log.LogWarning("Поглощение души: спутник не ушёл: " + e.Message);
                }
            }
        }
    }

    /// <summary>The look of a soul being drawn out: the same wisps Drain Soul sends.</summary>
    internal static class Wisps
    {
        private static UISpellInfo drain;
        private static bool asked;
        private static GameObject template;
        private static List<UnitEffects> absorb;

        private sealed class Wisp
        {
            internal GameObject go;
            internal UnitAttribute from;
            internal UnitAttribute to;
            internal float born;
        }

        private static readonly List<Wisp> flying = new List<Wisp>();

        internal static UISpellInfo DrainSpell()
        {
            if (drain != null) return drain;
            try
            {
                UISpellDatabase db = UISpellDatabase.Instance;
                if (db != null) drain = db.GetByName("DrainSoul");
            }
            catch
            {
            }
            return drain;
        }

        internal static void Load()
        {
            if (asked) return;
            asked = true;

            try
            {
                UISpellInfo spell = DrainSpell();
                if (spell == null || spell.behaviorPrefab == null) return;

                BehaviorDrainSoul how = spell.behaviorPrefab.GetComponentInChildren<BehaviorDrainSoul>(true);
                if (how == null) return;

                absorb = how.absorbEffects;

                if (how.flyObjReference == null || !how.flyObjReference.RuntimeKeyIsValid()) return;

                AsyncOperationHandle<GameObject> handle = Addressables.LoadAssetAsync<GameObject>(how.flyObjReference.RuntimeKey);
                handle.Completed += Loaded;
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Поглощение души: вид не запросился: " + e.Message);
            }
        }

        private static void Loaded(AsyncOperationHandle<GameObject> handle)
        {
            try
            {
                if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null) return;

                GameObject copy = UnityEngine.Object.Instantiate(handle.Result);
                copy.name = "DemonSoulWispTemplate";
                copy.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(copy);

                // Только вид: всё, что умеет действовать само, вон.
                foreach (MonoBehaviour part in copy.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (part != null) UnityEngine.Object.Destroy(part);
                }
                foreach (Collider part in copy.GetComponentsInChildren<Collider>(true))
                {
                    if (part != null) UnityEngine.Object.Destroy(part);
                }
                foreach (Rigidbody part in copy.GetComponentsInChildren<Rigidbody>(true))
                {
                    if (part != null) UnityEngine.Object.Destroy(part);
                }

                template = copy;
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Поглощение души: вид не загрузился: " + e.Message);
            }
        }

        private static Vector3 Chest(UnitAttribute u)
        {
            if (u.chest != null) return u.chest.position;
            return u.transform.position + Vector3.up * 1.2f;
        }

        internal static void Send(UnitAttribute from, UnitAttribute to)
        {
            if (template == null || from == null || to == null || flying.Count > 40) return;

            try
            {
                GameObject go = UnityEngine.Object.Instantiate(template, Chest(from), Quaternion.identity);
                go.SetActive(true);
                flying.Add(new Wisp { go = go, from = from, to = to, born = Time.time });
            }
            catch
            {
            }
        }

        internal static void Tick()
        {
            for (int i = flying.Count - 1; i >= 0; i--)
            {
                Wisp w = flying[i];
                float t = (Time.time - w.born) / 0.7f;

                if (w.go == null || w.from == null || w.to == null || t >= 1f)
                {
                    if (w.go != null) UnityEngine.Object.Destroy(w.go);
                    flying.RemoveAt(i);
                    continue;
                }

                Vector3 a = Chest(w.from);
                Vector3 b = Chest(w.to);
                w.go.transform.position = Vector3.Lerp(a, b, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.5f;
            }
        }

        internal static void Absorb(UnitAttribute demon)
        {
            if (demon == null || absorb == null || absorb.Count == 0) return;
            demon.PlayUnitEffects(absorb.ToArray());
        }
    }

    // Целиться поглощением можно только в годную жертву, и только вне боя.
    [HarmonyPatch(typeof(GameController), "TargetIsAllowed", new[] { typeof(UnitAttribute), typeof(UnitAttribute), typeof(TargetAllow) })]
    internal static class TargetIsAllowed_Feast_Patch
    {
        private static void Postfix(UnitAttribute user, UnitAttribute target, ref bool __result)
        {
            if (Feast.Enabled == null || !Feast.Enabled.Value || user == null || target == null) return;
            if (!Racial.IsDemon(user) || !Feast.Aiming(user)) return;

            __result = !user.isEngaged && Feast.Victim(user, target) != Feast.Kind.None;
        }
    }

    // В бою поглощение не готово: ни у демона, ни у кого-либо ещё. Готовность заклинания
    // игра спрашивает у менеджера заклинаний, а не у контроллера игры: здесь стояла не та
    // цель, и загрузка правок обрывалась на ней целиком.
    [HarmonyPatch(typeof(SpellManager), "IsSpellReady", new[] { typeof(SpellBase), typeof(UnitAttribute) })]
    internal static class IsSpellReady_Feast_Patch
    {
        private static void Postfix(SpellBase spell, UnitAttribute unit, ref bool __result)
        {
            if (!__result || spell == null || unit == null || !Feast.IsFeast(spell.theSpell)) return;
            if (unit.isEngaged || !Racial.IsDemon(unit)) __result = false;
        }
    }

    // Каст дошёл до дела: у демона вместо заклинания — поглощение или вуаль.
    [HarmonyPatch(typeof(SpellManager), "PrepareSpell")]
    [HarmonyPriority(Priority.First)]
    internal static class PrepareSpell_Feast_Patch
    {
        private static bool Prefix(SpellManager __instance)
        {
            try
            {
                SpellBase cast = __instance.OnCastSpell;
                UnitAttribute unit = __instance.unit;
                if (cast == null || cast.theSpell == null || unit == null) return true;

                if (Feast.IsFeast(cast.theSpell))
                {
                    Feast.Begin(unit, cast);
                    return false;
                }

                if (Veil.IsVeil(cast.theSpell))
                {
                    Veil.Cast(unit, cast);
                    return false;
                }
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Поглощение души: каст не перехватился: " + e.Message);
            }

            return true;
        }
    }

    // Кто кого заметил: свидетель поглощения, и срыв вуали.
    [HarmonyPatch(typeof(UnitAttribute), "AddSense")]
    internal static class AddSense_Feast_Patch
    {
        private static void Prefix(UnitAttribute __instance, UnitAttribute enemyUnit)
        {
            if (enemyUnit == null || __instance == null) return;

            try
            {
                if (__instance.sensedUnit.Contains(enemyUnit)) return;
                Feast.Sensed(__instance, enemyUnit);
                Veil.Sensed(__instance, enemyUnit);
            }
            catch
            {
            }
        }
    }

    // В древе навыков у демона на месте двух заклинаний Тьмы стоят его собственные.
    [HarmonyPatch(typeof(SkillsetManager), "UpdateInfo")]
    internal static class SkillsetUpdate_Feast_Patch
    {
        private static void Prefix(SkillsetManager __instance, HumaniodUnit t_unit)
        {
            if (Feast.Enabled == null || !Feast.Enabled.Value || __instance == null || t_unit == null) return;
            if (__instance.spellslots == null) return;

            try
            {
                Feast.Register();

                bool demon = Racial.IsDemon(t_unit);
                UISpellInfo feast = Feast.FeastSpell();
                UISpellInfo touch = Feast.TouchSpell();
                UISpellInfo veil = Veil.VeilSpell();
                UISpellInfo shield = Veil.ShieldSpell();

                foreach (UISpellSlot slot in __instance.spellslots)
                {
                    if (slot == null || slot.isPassive) continue;
                    UISpellInfo now = slot.GetSpellInfo();
                    if (now == null) continue;

                    UISpellInfo want = now;
                    if (demon)
                    {
                        if (Feast.IsTouchOriginal(now) && feast != null) want = feast;
                        else if (Veil.IsShieldOriginal(now) && veil != null) want = veil;
                    }
                    else
                    {
                        if (Feast.IsFeast(now) && touch != null) want = touch;
                        else if (Veil.IsVeil(now) && shield != null) want = shield;
                    }

                    if ((object)want != (object)now) slot.Assign(want);
                }
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Поглощение души: древо не переставилось: " + e.Message);
            }
        }
    }
}
