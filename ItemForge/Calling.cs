using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using spell;
using TroopManagement;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Two callings a fighter grows into by what he does: the Paladin and the Mage Hunter.
    ///
    /// Призвание не выбирают в меню — в него врастают делом. У каждого пять ступеней.
    ///
    /// Охотник на магов — тот, кто убивает магов, сам магом не будучи и не закованный в латы.
    /// Ступени — первый, пятый, пятнадцатый, тридцать пятый и семидесятый убитый маг. На
    /// ступени N: его оружие пробивает магическую защиту — щит мага держит его удар на 20 %·N
    /// хуже (на пятой не держит вовсе), сопротивления стихиям у жертвы не спасают от стихии
    /// его клинка на ту же долю; по магам он бьёт на 6 %·N сильнее; удар по колдующему магу
    /// с шансом 20 %·N срывает заклинание; чужая магия ранит его на 5 %·N слабее.
    ///
    /// Паладин — латник, что бьёт тьму: нежить, чудовищ, порождения ночи. Ступени — третий,
    /// десятый, двадцать пятый, пятидесятый и сотый повергнутый. На ступени N: по тьме он бьёт
    /// на 8 %·N сильнее и возвращает себе здоровьем 2 %·N нанесённого ей урона; магия ранит
    /// его на 4 %·N слабее; со второй ступени ужас расправы его не ломает, а товарищей рядом
    /// с ним ломает на 10 %·N реже.
    ///
    /// Призвание действует, пока боец ему верен: охотник — без лат и без магии, паладин — в
    /// латах. Снял латы — паладин ждёт, пока их наденут снова; ступени не теряются. Ступень
    /// видна на бойце постоянным эффектом с описанием.
    /// </summary>
    internal static class Calling
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> HunterSteps;
        internal static ConfigEntry<string> PaladinSteps;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Calling", "Enabled", true,
                "Let fighters grow into two callings by what they do: the Mage Hunter by killing "
                + "mages, the Paladin by striking down the undead and monsters in heavy armour.");

            HunterSteps = config.Bind("Calling", "HunterSteps", "1,5,15,35,70",
                "How many mages must fall to the hand of a Mage Hunter for each of his five steps.");

            PaladinSteps = config.Bind("Calling", "PaladinSteps", "3,10,25,50,100",
                "How many undead and monsters a Paladin must strike down for each of his five steps.");

            Telling = config.Bind("Calling", "Telling", true,
                "Say in the log and the chat who rose to which step.");
        }

        internal enum Kind { Hunter = 0, Paladin = 1 }

        private static readonly string[] Roman = { "", "I", "II", "III", "IV", "V" };

        // ------------------------------------------------------------------ счёт и ступени

        private sealed class Deeds { internal int hunter; internal int paladin; }

        private static readonly Dictionary<int, Deeds> deeds = new Dictionary<int, Deeds>();

        private static int[] Steps(ConfigEntry<string> entry)
        {
            List<int> got = new List<int>();
            foreach (string one in (entry.Value ?? "").Split(','))
            {
                int n;
                if (int.TryParse(one.Trim(), out n) && n > 0) got.Add(n);
            }
            while (got.Count < 5) got.Add(got.Count > 0 ? got[got.Count - 1] * 2 : 1);
            return got.ToArray();
        }

        private static int StepOf(int count, int[] steps)
        {
            int step = 0;
            for (int i = 0; i < 5; i++) if (count >= steps[i]) step = i + 1;
            return step;
        }

        internal static bool Faithful(UnitAttribute u, Kind kind)
        {
            HumaniodUnit man = u as HumaniodUnit;
            if (man == null || man.Data == null) return false;

            if (kind == Kind.Hunter) return man.armourtype != ArmourType.Heavy && !Hunt.Mage(man);

            // Паладин служит свету; демону этот путь закрыт, в каких бы латах он ни был.
            if (man.Data.race == UnitRace.demon) return false;
            return man.armourtype == ArmourType.Heavy;
        }

        /// <summary>The step this fighter stands on in this calling, 0 if none or if he has left it for now.</summary>
        internal static int Rank(UnitAttribute u, Kind kind)
        {
            if (Enabled == null || !Enabled.Value || u == null || u.Data == null || !u.inParty) return 0;

            Sync();

            Deeds d;
            if (!deeds.TryGetValue(u.Data.id, out d)) return 0;

            int step = kind == Kind.Hunter ? StepOf(d.hunter, Steps(HunterSteps)) : StepOf(d.paladin, Steps(PaladinSteps));
            if (step <= 0 || !Faithful(u, kind)) return 0;
            return step;
        }

        /// <summary>How much less often a slaughter breaks this one, for the Paladins standing by him.</summary>
        internal static float Courage(UnitAttribute u)
        {
            if (Enabled == null || !Enabled.Value || u == null || PartyManager.instance == null || PartyManager.instance.partyMembers == null) return 0f;

            float best = 0f;
            foreach (HumaniodUnit man in PartyManager.instance.partyMembers)
            {
                if (man == null || man.Data == null || man.Data.isdead) continue;
                int step = Rank(man, Kind.Paladin);
                if (step <= 0) continue;
                if ((object)man != (object)u && Vector3.Distance(man.transform.position, u.transform.position) > 10f) continue;
                best = Mathf.Max(best, 0.1f * step);
            }
            return Mathf.Clamp01(best);
        }

        /// <summary>The dark a Paladin is sworn against: the undead, monsters and beasts of the night.</summary>
        internal static bool Darkness(UnitAttribute u)
        {
            if (u == null || u.Data == null) return false;
            UnitRace race = u.Data.race;
            if (race == UnitRace.undead || race == UnitRace.mythological) return true;
            return u.Data.team == Faction.monster;
        }

        /// <summary>Somebody has died: whoever struck him down may count it to his calling.</summary>
        internal static void Fallen(UnitAttribute dead, UnitAttribute killer)
        {
            if (Enabled == null || !Enabled.Value || dead == null || dead.Data == null || !dead.Data.trueDead) return;
            if (killer == null || killer.Data == null || !killer.inParty || !(killer is HumaniodUnit)) return;
            if ((object)killer == (object)dead || GameController.IsAlly(killer, dead)) return;

            Sync();

            Deeds d;
            if (!deeds.TryGetValue(killer.Data.id, out d))
            {
                d = new Deeds();
                deeds[killer.Data.id] = d;
            }

            if (Hunt.Mage(dead) && Faithful(killer, Kind.Hunter))
            {
                int was = StepOf(d.hunter, Steps(HunterSteps));
                d.hunter++;
                int now = StepOf(d.hunter, Steps(HunterSteps));
                if (now > was) Rose(killer, Kind.Hunter, now);
            }

            if (Darkness(dead) && Faithful(killer, Kind.Paladin))
            {
                int was = StepOf(d.paladin, Steps(PaladinSteps));
                d.paladin++;
                int now = StepOf(d.paladin, Steps(PaladinSteps));
                if (now > was) Rose(killer, Kind.Paladin, now);
            }
        }

        private static void Rose(UnitAttribute who, Kind kind, int step)
        {
            string name = (kind == Kind.Hunter ? "Охотник на магов " : "Паладин ") + Roman[step];
            string said = $"«{who.Data.unitname}» — {name}.";

            try { GameController.ShowLocalizedMessage(said); } catch { }
            try { if (ChatManager.instance != null) ChatManager.instance.AddSystemMessage(said); } catch { }
            try { if (who.lifebar != null) who.lifebar.ShowTextTag("<color=#E8C66A>" + name + "</color>", 3f); } catch { }

            if (Telling.Value) ItemForgePlugin.Log.LogInfo("Призвание: " + said);

            ticked = -100f;
        }

        // ------------------------------------------------------------------ знак на бойце

        private static readonly Dictionary<string, UIBuffInfo> cards = new Dictionary<string, UIBuffInfo>();
        private static bool carded;

        private static string CardId(Kind kind, int step)
        {
            return (kind == Kind.Hunter ? "ItemForgeHunter" : "ItemForgePaladin") + step;
        }

        private static string Words(Kind kind, int step)
        {
            if (kind == Kind.Hunter)
            {
                return $"Убивает магов, сам магом не будучи и без лат. Щит мага держит его удар на {20 * step}% хуже, "
                    + $"сопротивление стихиям не спасает от стихии его оружия на ту же долю; по магам бьёт на "
                    + $"{6 * step}% сильнее; удар по колдующему магу с шансом {20 * step}% срывает заклинание; "
                    + $"магия ранит его на {5 * step}% слабее.";
            }

            string steady = step >= 2
                ? $" Ужас расправы его не ломает, а товарищей рядом с ним ломает на {10 * step}% реже."
                : "";
            return $"Латник, что бьёт тьму: нежить и чудовищ. По ним бьёт на {8 * step}% сильнее и возвращает себе "
                + $"здоровьем {2 * step}% нанесённого им урона; магия ранит его на {4 * step}% слабее." + steady;
        }

        /// <summary>Makes the cards that show a calling on a fighter, once.</summary>
        private static void Cards()
        {
            if (carded) return;

            UIBuffDatabase db;
            try { db = UIBuffDatabase.Instance; }
            catch { return; }
            if (db == null || db.buffs == null) return;

            UIBuffInfo pattern = db.GetByID("WeightDebuff");
            if (pattern == null) return;

            carded = true;

            try
            {
                UIBuffInfo hunterLook = db.GetByID("Hunter'sMarkBuff");
                UIBuffInfo paladinLook = db.GetByID("Guardian");

                List<UIBuffInfo> all = new List<UIBuffInfo>(db.buffs);

                foreach (Kind kind in new[] { Kind.Hunter, Kind.Paladin })
                {
                    for (int step = 1; step <= 5; step++)
                    {
                        string id = CardId(kind, step);
                        UIBuffInfo card = db.GetByID(id);

                        if (card == null)
                        {
                            card = UnityEngine.Object.Instantiate(pattern);
                            card.name = id;
                            card.id = id;
                            all.Add(card);
                        }

                        card.buffname = (kind == Kind.Hunter ? "Охотник на магов " : "Паладин ") + Roman[step];
                        card.description = Words(kind, step);
                        card.type = bufftype.positive;
                        card.isVisible = true;
                        card.showDuration = false;
                        card.canForceRemove = false;
                        card.immuneDispel = true;
                        if (card.addAttrs != null) card.addAttrs.Clear();

                        UIBuffInfo look = kind == Kind.Hunter ? hunterLook : paladinLook;
                        if (look != null && look.icon != null) card.icon = look.icon;

                        cards[id] = card;
                    }
                }

                db.buffs = all.ToArray();
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Призвание: не смог завести знаки: " + e);
            }
        }

        private static float ticked = -100f;

        /// <summary>Keeps on every party member the card of the step he stands on, and no other.</summary>
        internal static void Tick()
        {
            if (Enabled == null || !Enabled.Value) return;

            float now = Time.unscaledTime;
            if (now - ticked < 2f && now >= ticked) return;
            ticked = now;

            try
            {
                if (PartyManager.instance == null || PartyManager.instance.partyMembers == null) return;
                if (PartyManager.instance.leader == null) return;

                Cards();
                if (cards.Count == 0) return;

                foreach (HumaniodUnit man in PartyManager.instance.partyMembers)
                {
                    if (man == null || man.Data == null || man.buffmanger == null || man.Data.isdead) continue;

                    foreach (Kind kind in new[] { Kind.Hunter, Kind.Paladin })
                    {
                        int step = Rank(man, kind);
                        string want = step > 0 ? CardId(kind, step) : null;

                        for (int s = 1; s <= 5; s++)
                        {
                            string id = CardId(kind, s);
                            if (id == want) continue;
                            if (man.buffmanger.ContainBuff(id)) man.buffmanger.RemoveBuff(id);
                        }

                        if (want != null && !man.buffmanger.ContainBuff(want))
                        {
                            UIBuffInfo card;
                            if (cards.TryGetValue(want, out card)) man.buffmanger.AddBuff(new BuffBase(card, man));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Призвание: не смог обновить знаки: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ сохранение

        private static System.Reflection.FieldInfo archiveField;
        private static string loadedFor;

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
            return Path.Combine(BepInEx.Paths.ConfigPath, "aor.callings." + archive + ".txt");
        }

        private static float synced = -100f;

        /// <summary>The deeds belong to the save in hand; asked often, so looked at every few seconds.</summary>
        private static void Sync()
        {
            float now = Time.unscaledTime;
            if (loadedFor != null && now - synced < 5f && now >= synced) return;
            synced = now;

            string path = FilePath();
            if (!string.Equals(path, loadedFor, StringComparison.OrdinalIgnoreCase)) Load();
        }

        internal static void Save()
        {
            if (Enabled == null || !Enabled.Value) return;

            try
            {
                Sync();
                List<string> lines = new List<string>();
                foreach (KeyValuePair<int, Deeds> one in deeds)
                {
                    lines.Add(one.Key + "\t" + one.Value.hunter + "\t" + one.Value.paladin);
                }
                File.WriteAllLines(FilePath(), lines.ToArray());
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Призвание: не смог сохранить: " + e.Message);
            }
        }

        internal static void Load()
        {
            deeds.Clear();

            try
            {
                string path = FilePath();
                loadedFor = path;
                if (!File.Exists(path)) return;

                foreach (string line in File.ReadAllLines(path))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length < 3) continue;

                    int id, hunter, paladin;
                    if (!int.TryParse(parts[0], out id) || !int.TryParse(parts[1], out hunter) || !int.TryParse(parts[2], out paladin)) continue;

                    deeds[id] = new Deeds { hunter = hunter, paladin = paladin };
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Призвание: не смог поднять: " + e.Message);
            }

            ticked = -100f;
        }

        // ------------------------------------------------------------------ в бою

        internal sealed class Hit
        {
            internal float hp;
            internal UIBuffInfo shield;
            internal float absorb;
        }
    }

    // Охотник: щит мага держит его удар хуже; раненый маг теряет заклинание; паладин пьёт тьму.
    [HarmonyPatch(typeof(UnitAttribute), "TakeDamage")]
    [HarmonyPriority(Priority.High)]
    internal static class TakeDamage_Calling_Patch
    {
        private static void Prefix(UnitAttribute __instance, Attack attack, out Calling.Hit __state)
        {
            __state = null;
            if (Calling.Enabled == null || !Calling.Enabled.Value || __instance == null || __instance.Data == null) return;
            if (attack == null || attack.attacker == null || !attack.attacker.inParty || !Blows.Weapon(attack)) return;

            try
            {
                Calling.Hit hit = new Calling.Hit { hp = __instance.Data.currenthp };

                int hunter = Calling.Rank(attack.attacker, Calling.Kind.Hunter);
                if (hunter > 0 && __instance.buffmanger != null)
                {
                    BuffBase shield = __instance.buffmanger.FindBuffofType(bufftype.magicShield);
                    if (shield != null && shield.buffInfo != null)
                    {
                        hit.shield = shield.buffInfo;
                        hit.absorb = shield.buffInfo.absorbRate;
                        shield.buffInfo.absorbRate = hit.absorb * Mathf.Clamp01(1f - 0.2f * hunter);
                    }
                }

                __state = hit;
            }
            catch
            {
                __state = null;
            }
        }

        private static void Postfix(UnitAttribute __instance, Attack attack, bool __result, Calling.Hit __state)
        {
            if (__state == null || !__result || attack == null || attack.attacker == null) return;

            try
            {
                int hunter = Calling.Rank(attack.attacker, Calling.Kind.Hunter);
                if (hunter > 0 && Hunt.Mage(__instance) && __instance.spellmanger != null && __instance.spellmanger.isCasting
                    && !__instance.Data.isdead && UnityEngine.Random.value < 0.2f * hunter)
                {
                    __instance.spellmanger.BreakCastSpell();
                    Blows.Tag(__instance, "Заклинание сорвано", "#B28CFF");
                }

                int paladin = Calling.Rank(attack.attacker, Calling.Kind.Paladin);
                if (paladin > 0 && Calling.Darkness(__instance))
                {
                    float dealt = __state.hp - __instance.Data.currenthp;
                    if (dealt > 0f) attack.attacker.Heal(dealt * 0.02f * paladin, attack.attacker);
                }
            }
            catch
            {
            }
        }

        private static void Finalizer(Calling.Hit __state)
        {
            if (__state != null && __state.shield != null) __state.shield.absorbRate = __state.absorb;
        }
    }

    // Охотник: сопротивление стихиям не спасает от стихии его оружия.
    [HarmonyPatch(typeof(UnitAttribute), "DamageReduce")]
    [HarmonyPriority(Priority.High)]
    internal static class DamageReduce_CallingResist_Patch
    {
        private static void Prefix(UnitAttribute __instance, Attack attack, out float[] __state)
        {
            __state = null;
            if (Calling.Enabled == null || !Calling.Enabled.Value || __instance == null || __instance.damageResist == null) return;
            if (attack == null || attack.attacker == null || !attack.attacker.inParty || !Blows.Weapon(attack)) return;

            int hunter = Calling.Rank(attack.attacker, Calling.Kind.Hunter);
            if (hunter <= 0) return;

            float keep = Mathf.Clamp01(1f - 0.2f * hunter);
            __state = (float[])__instance.damageResist.Clone();

            for (int i = 3; i < __instance.damageResist.Length && i <= 8; i++)
            {
                if (__instance.damageResist[i] > 0f) __instance.damageResist[i] *= keep;
            }
        }

        private static void Finalizer(UnitAttribute __instance, float[] __state)
        {
            if (__state == null || __instance == null || __instance.damageResist == null) return;
            for (int i = 0; i < __state.Length && i < __instance.damageResist.Length; i++) __instance.damageResist[i] = __state[i];
        }
    }

    // Итог: охотник по магам, паладин по тьме — сильнее; магия по ним — слабее.
    [HarmonyPatch(typeof(UnitAttribute), "DamageReduce")]
    [HarmonyPriority(Priority.VeryLow)]
    internal static class DamageReduce_Calling_Patch
    {
        private static void Postfix(UnitAttribute __instance, Attack attack, ref DamageBase __result)
        {
            if (Calling.Enabled == null || !Calling.Enabled.Value || __result == null || attack == null) return;

            try
            {
                float times = 1f;

                if (attack.attacker != null && attack.attacker.inParty && Blows.Weapon(attack))
                {
                    int hunter = Calling.Rank(attack.attacker, Calling.Kind.Hunter);
                    if (hunter > 0 && Hunt.Mage(__instance)) times *= 1f + 0.06f * hunter;

                    int paladin = Calling.Rank(attack.attacker, Calling.Kind.Paladin);
                    if (paladin > 0 && Calling.Darkness(__instance)) times *= 1f + 0.08f * paladin;
                }

                if (__instance.inParty && Pierce.Spell(attack))
                {
                    int hunter = Calling.Rank(__instance, Calling.Kind.Hunter);
                    if (hunter > 0) times *= 1f - 0.05f * hunter;

                    int paladin = Calling.Rank(__instance, Calling.Kind.Paladin);
                    if (paladin > 0) times *= 1f - 0.04f * paladin;
                }

                if (!Mathf.Approximately(times, 1f)) __result *= times;
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "Die")]
    internal static class Die_Calling_Patch
    {
        private static void Postfix(UnitAttribute __instance, UnitAttribute killer)
        {
            try { Calling.Fallen(__instance, killer); } catch { }
        }
    }

    [HarmonyPatch(typeof(SaveLoadManager), "WriteDataToSaveFile")]
    internal static class WriteSave_Calling_Patch
    {
        private static void Postfix() { Calling.Save(); }
    }

    [HarmonyPatch(typeof(ArenaMatchManager), "LoadInstance")]
    internal static class LoadInstance_Calling_Patch
    {
        private static void Postfix() { Calling.Load(); }
    }
}
