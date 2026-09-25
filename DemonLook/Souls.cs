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
    /// What the demon takes from the souls he drinks: strength, the dark school, and a hunger.
    ///
    /// Сила. Каждая выпитая душа даёт «силу души» — уровень жертвы, со случайной долей от
    /// трёх четвертей до пяти четвертей. Очко силы стоит от двух до двадцати душ десятого
    /// уровня и дальше тем же шагом, без потолка, дорожая с каждым десятком очков; но одна душа никогда не даёт меньше
    /// сотой доли очка — чтобы и у сильного демона каждая жертва была заметна. Стражник
    /// сорокового уровня идёт за четверых горожан.
    ///
    /// Тьма. Школа Тьмы у демона растёт не от опыта, а от числа выпитых: десятая душа
    /// открывает первую ступень, тридцатая — вторую, восьмидесятая — третью, сто пятидесятая —
    /// четвёртую, трёхсотая — пятую, всю школу. Заклинания Тьмы приходят к нему сами, по мере
    /// ступеней; купить их опытом нельзя. У всех прочих школа Тьмы — как в игре.
    ///
    /// Голод. Неделю без души демон терпит; дальше боевой дух тает — по десять в день, а с
    /// двенадцатого дня по двадцать. Выпитая душа снимает голод и возвращает пятнадцать.
    ///
    /// Всё это хранится при сохранении, своим файлом рядом с настройками.
    /// </summary>
    internal static class Souls
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Ladder;
        internal static ConfigEntry<float> Person;
        internal static ConfigEntry<int> Tier;
        internal static ConfigEntry<int> Cap;
        internal static ConfigEntry<float> Floor;
        internal static ConfigEntry<float> Spread;
        internal static ConfigEntry<string> School;
        internal static ConfigEntry<int> Grace;
        internal static ConfigEntry<float> Soft;
        internal static ConfigEntry<int> HardFrom;
        internal static ConfigEntry<float> Hard;
        internal static ConfigEntry<float> Fed;
        internal static ConfigEntry<int> Morning;
        internal static ConfigEntry<float> Trust;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Souls", "Enabled", true,
                "Let the demon grow on the souls he drinks with the Soul Feast: strength, the dark "
                + "school and a hunger that will not let him rest.");

            Ladder = config.Bind("Souls", "Ladder", "2,4,6,8,10,12,14,16,18,20",
                "What one point of strength costs, in souls of the tenth level, by the tier of "
                + "points already taken: two souls a point for the first ten, twenty for the tenth "
                + "ten, and on by the same step past it. Twenty townsfolk give ten points, three "
                + "hundred give fifty. One soul still gives at least the Floor share of a point.");

            Person = config.Bind("Souls", "Person", 10f,
                new ConfigDescription("The soul power of one victim of the tenth level: a victim gives "
                    + "his level in soul power.",
                    new AcceptableValueRange<float>(1f, 1000f)));

            Tier = config.Bind("Souls", "Tier", 10,
                new ConfigDescription("How many points make up one tier of the ladder.",
                    new AcceptableValueRange<int>(1, 1000)));

            Cap = config.Bind("Souls", "Cap", 0,
                new ConfigDescription("The most strength the souls can ever give; nought for no limit, "
                    + "the ladder going on by the same step past its last written rung.",
                    new AcceptableValueRange<int>(0, 100000)));

            Floor = config.Bind("Souls", "Floor", 0.01f,
                new ConfigDescription("The least share of the next point one soul gives, whatever its "
                    + "level: a hundredth by default, so no point costs more than a hundred souls.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Spread = config.Bind("Souls", "Spread", 0.25f,
                new ConfigDescription("How far a soul strays from its victim level, either way, as a share.",
                    new AcceptableValueRange<float>(0f, 0.9f)));

            School = config.Bind("Souls", "School", "10,30,80,150,300",
                "How many souls open each of the five steps of the dark school, for the demon only.");

            Grace = config.Bind("Souls", "Grace", 7,
                new ConfigDescription("Days without a soul the demon bears with no harm.",
                    new AcceptableValueRange<int>(0, 365)));

            Soft = config.Bind("Souls", "Soft", 10f,
                new ConfigDescription("Morale lost each day of hunger after that.",
                    new AcceptableValueRange<float>(0f, 100f)));

            HardFrom = config.Bind("Souls", "HardFrom", 12,
                new ConfigDescription("From which day without a soul the hunger bites harder.",
                    new AcceptableValueRange<int>(1, 365)));

            Hard = config.Bind("Souls", "Hard", 20f,
                new ConfigDescription("Morale lost each day of the harder hunger.",
                    new AcceptableValueRange<float>(0f, 100f)));

            Fed = config.Bind("Souls", "Fed", 15f,
                new ConfigDescription("Morale a drunk soul gives back.",
                    new AcceptableValueRange<float>(0f, 100f)));

            Morning = config.Bind("Souls", "Morning", 7,
                new ConfigDescription("The hour the town wakes and finds the body of one drunk unseen.",
                    new AcceptableValueRange<int>(0, 23)));

            Trust = config.Bind("Souls", "Trust", 0.8f,
                new ConfigDescription("How much of the suspicion a town at the best standing with him "
                    + "(a hundred) spares him; less standing spares him less, in proportion.",
                    new AcceptableValueRange<float>(0f, 1f)));
        }

        // ------------------------------------------------------------------ состояние

        private static float power;
        private static int count;
        private static int fedDay = -1;
        private static int appliedDay = -1;
        private static bool revealed;
        private static bool veiled;
        private static bool chronicle;

        /// <summary>Whether the book of his kind has been given in this save.</summary>
        internal static bool HasChronicle
        {
            get { Sync(); return chronicle; }
            set { Sync(); if (chronicle == value) return; chronicle = value; Save(); }
        }

        internal static float Power { get { Sync(); return power; } }

        /// <summary>Whether the veil is on him; written with the game save, like everything here.</summary>
        internal static bool Veiled
        {
            get { Sync(); return veiled; }
            set { Sync(); if (veiled == value) return; veiled = value; Save(); }
        }
        internal static int Count { get { Sync(); return count; } }
        internal static bool Revealed { get { Sync(); return revealed; } }

        /// <summary>The one demon of this game, if the party is his.</summary>
        internal static HumaniodUnit Demon()
        {
            PartyManager party = PartyManager.instance;
            if (party == null) return null;

            if (party.leader != null && Racial.IsDemon(party.leader)) return party.leader;

            HumaniodUnit you = gameManager.currentplayUnit;
            if (you != null && Racial.IsDemon(you)) return you;

            return null;
        }

        internal static int Today()
        {
            try { return TimeManager.Instance != null ? TimeManager.TotalDay : -1; }
            catch { return -1; }
        }

        // ------------------------------------------------------------------ лестница силы

        private static float[] rungs;
        private static string rungsRead;

        private static float[] Rungs()
        {
            string written = Ladder.Value ?? "";
            if (rungs != null && written == rungsRead) return rungs;

            List<float> got = new List<float>();
            foreach (string one in written.Split(','))
            {
                float much;
                if (float.TryParse(one.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out much) && much > 0f)
                    got.Add(much);
            }

            rungsRead = written;
            rungs = got.Count > 0 ? got.ToArray() : new[] { 2f };
            return rungs;
        }

        /// <summary>
        /// Soul power the next point costs, with this many points already taken. Past the last
        /// written rung the ladder goes on by the same step.
        /// </summary>
        internal static float Price(int points)
        {
            float[] ladder = Rungs();
            int step = Mathf.Max(0, points / Mathf.Max(1, Tier.Value));

            float rung;
            if (step < ladder.Length) rung = ladder[step];
            else
            {
                float last = ladder[ladder.Length - 1];
                float rise = ladder.Length > 1 ? last - ladder[ladder.Length - 2] : last;
                rung = last + rise * (step - ladder.Length + 1);
            }

            return Mathf.Max(1f, rung * Person.Value);
        }

        private static int Limit()
        {
            return Cap.Value > 0 ? Cap.Value : 100000;
        }

        internal static int Points()
        {
            if (Enabled == null || !Enabled.Value) return 0;

            float left = Power;
            int cap = Limit();
            int points = 0;

            while (points < cap)
            {
                float price = Price(points);
                if (left < price) break;
                left -= price;
                points++;
            }

            return points;
        }

        /// <summary>How far toward the next point, from nought to one.</summary>
        internal static float Share()
        {
            float left = Power;
            int cap = Limit();
            int points = 0;

            while (points < cap)
            {
                float price = Price(points);
                if (left < price) return Mathf.Clamp01(left / price);
                left -= price;
                points++;
            }

            return 1f;
        }

        /// <summary>The soul power this victim gives: his level, drawn, never under the floor.</summary>
        internal static float Gain(int level)
        {
            float price = Price(Points());
            float spread = Mathf.Clamp(Spread.Value, 0f, 0.9f);
            float drawn = Mathf.Max(1, level) * UnityEngine.Random.Range(1f - spread, 1f + spread);
            return Mathf.Max(drawn, Floor.Value * price);
        }

        // ------------------------------------------------------------------ школа Тьмы

        private static int[] steps;
        private static string stepsRead;

        private static int[] Steps()
        {
            string written = School.Value ?? "";
            if (steps != null && written == stepsRead) return steps;

            List<int> got = new List<int>();
            foreach (string one in written.Split(','))
            {
                int n;
                if (int.TryParse(one.Trim(), out n) && n > 0) got.Add(n);
            }

            stepsRead = written;
            steps = got.ToArray();
            return steps;
        }

        internal static int SchoolLevel()
        {
            int n = Count;
            int level = 0;
            foreach (int step in Steps()) if (n >= step) level++;
            return level;
        }

        /// <summary>Souls still wanted for the next step of the school, or 0 at the top.</summary>
        internal static int NextStep()
        {
            int n = Count;
            foreach (int step in Steps()) if (n < step) return step - n;
            return 0;
        }

        private static UITalentInfo mastery;

        /// <summary>Keeps the demon dark school at the step his souls have opened, and its spells in his book.</summary>
        private static void SchoolSync(HumaniodUnit demon)
        {
            if (demon.talentmanger == null || demon.spellmanger == null) return;

            UITalentDatabase tdb = UITalentDatabase.Instance;
            if (tdb == null) return;
            if (mastery == null) mastery = tdb.GetMasteryTalent(SkillSet.black);

            int level = SchoolLevel();

            if (mastery != null)
            {
                int top = mastery.maxPoints > 0 ? mastery.maxPoints : 5;
                int want = Mathf.Clamp(level, 0, top);

                TalentBase have = demon.talentmanger.FindTalent(mastery);
                if (have == null)
                {
                    if (want > 0) demon.talentmanger.AddTalent(mastery, want);
                }
                else if (have.level != want)
                {
                    demon.talentmanger.SetTalentLevel(mastery, want);
                }
            }

            Feast.Register();

            UISpellDatabase sdb = UISpellDatabase.Instance;
            if (sdb == null || sdb.spells == null) return;

            foreach (UISpellInfo s in sdb.spells)
            {
                if (s == null || s.SkillSet != SkillSet.black || s.spellType == SpellType.item) continue;

                // Две школьные вещи у демона свои: вместо «Губительного прикосновения» —
                // «Поглощение души», вместо «Щита тьмы» — «Вуаль». Подлинники ему не даются,
                // а если были — забираются.
                if (Feast.IsTouchOriginal(s) || Veil.IsShieldOriginal(s))
                {
                    if (demon.spellmanger.FindSpell(s) != null) demon.spellmanger.RemoveSpell(s);
                    continue;
                }

                bool own = Feast.IsFeast(s);
                bool veil = Veil.IsVeil(s);

                int need = s.RequireMastery;
                if (veil) need = Veil.Needs();

                if (!own)
                {
                    if (level <= 0 || need > level) continue;
                }

                int top = Mathf.Max(1, s.maxPoints);
                int rank = Mathf.Clamp(Mathf.CeilToInt(top * level / 5f), 1, top);

                SpellBase known = demon.spellmanger.FindSpell(s);
                if (known == null) demon.spellmanger.AddSpell(s, rank);
                else if (known.level < rank) demon.spellmanger.SetSpellLevel(s, rank);
            }
        }

        // ------------------------------------------------------------------ пир

        /// <summary>A soul is drunk: it becomes his.</summary>
        internal static void Feed(HumaniodUnit demon, string whose, float gain)
        {
            Sync();

            int was = Points();
            int wasSchool = SchoolLevel();

            power += gain;
            count++;

            int today = Today();
            if (today >= 0)
            {
                fedDay = today;
                appliedDay = today;
            }

            Save();

            if (demon != null && demon.Data != null) demon.Data.AddMorale(Fed.Value);

            int now = Points();
            int school = SchoolLevel();

            string said = $"Выпита душа «{whose}»: сила души +{gain:0}. Душ {count}.";
            if (now > was) said += $" Сила +{now - was} (всего от душ +{now}).";
            else said += $" До следующего очка силы {Mathf.RoundToInt((1f - Share()) * 100f)}%.";
            if (school > wasSchool) said += $" Тьма — {school}-я ступень.";

            Say(said);
            DemonLookPlugin.Log.LogInfo("Души: " + said);

            try { if (demon != null) demon.UpdateAttribute(); } catch { }

            ticked = -100f;
        }

        // Тела, которых ещё не нашли: чей город, чьё тело и в какой день их найдут.
        private sealed class Body { internal int team; internal string whose; internal int day; }

        private static readonly List<Body> bodies = new List<Body>();

        // Подозрение по городам — та часть их счёта преступлений, что легла от найденных тел.
        private static readonly Dictionary<int, int> suspicion = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> searched = new Dictionary<int, int>();
        private static int bribeWeek = -1;
        private static int bribes;

        // Облава: в какой день город последний раз нашёл тело.
        private static readonly Dictionary<int, int> raids = new Dictionary<int, int>();

        /// <summary>Whether tonight falls within the raid that followed the last body found here.</summary>
        internal static bool RaidTonight(Faction team, int nights)
        {
            Sync();
            int day;
            if (!raids.TryGetValue((int)team, out day)) return false;
            int night = Crime.Night();
            return night >= day && night - day < nights;
        }

        internal static int Suspicion(Faction team)
        {
            Sync();
            int much;
            return suspicion.TryGetValue((int)team, out much) ? much : 0;
        }

        internal static void Suspect(Faction team, int delta)
        {
            Sync();
            int much = Suspicion(team) + delta;
            if (much <= 0) suspicion.Remove((int)team);
            else suspicion[(int)team] = much;
            Save();
        }

        internal static bool SearchedToday(Faction team)
        {
            Sync();
            int day;
            return searched.TryGetValue((int)team, out day) && day == Today();
        }

        internal static void MarkSearched(Faction team)
        {
            Sync();
            searched[(int)team] = Today();
            Save();
        }

        internal static int BribesThisWeek()
        {
            Sync();
            return bribeWeek == Today() / 7 ? bribes : 0;
        }

        internal static void Bribed()
        {
            Sync();
            int week = Today() / 7;
            if (bribeWeek != week) { bribeWeek = week; bribes = 0; }
            bribes++;
            Save();
        }

        /// <summary>An unseen feast: the town finds the body the next morning.</summary>
        internal static void Bury(Faction team, string whose)
        {
            Sync();
            int today = Today();
            int hour = 12;
            try { hour = TimeManager.Hour; } catch { }

            int day = hour < Morning.Value ? today : today + 1;
            bodies.Add(new Body { team = (int)team, whose = (whose ?? "").Replace(";", ","), day = day });
            Save();
        }

        /// <summary>The town wakes and finds its dead: the suspicion falls on him, less where he is trusted.</summary>
        private static void Dawn(int today)
        {
            if (bodies.Count == 0) return;

            int hour = 0;
            try { hour = TimeManager.Hour; } catch { }

            bool changed = false;

            for (int i = bodies.Count - 1; i >= 0; i--)
            {
                Body b = bodies[i];
                if (today < b.day || (today == b.day && hour < Morning.Value)) continue;

                bodies.RemoveAt(i);
                changed = true;
                raids[b.team] = today;

                try
                {
                    Faction team = (Faction)b.team;
                    if (!team.FactionHasCrime()) continue;

                    int theft = Crime.RawFactor(CrimeDataType.Steal);
                    int favor = FactionManager.Instance.GetFavorToPlayer(team);
                    float spared = Trust.Value * Mathf.Clamp01(favor / 100f);
                    int much = Mathf.RoundToInt(theft * Feast.Theft.Value * (1f - spared));

                    if (much > 0)
                    {
                        FactionManager.Instance.AddCrimeToPlayer(team, much);
                        Suspect(team, much);
                    }

                    Say($"Утром нашли тело «{b.whose}». Подозрение падает на вас: преступление +{much}"
                        + (spared > 0.005f ? $" (доверие города сняло {Mathf.RoundToInt(spared * 100f)}%)." : "."));
                }
                catch (Exception e)
                {
                    DemonLookPlugin.Log.LogWarning("Души: подозрение не записалось: " + e.Message);
                }
            }

            if (changed) Save();
        }

        internal static void Reveal()
        {
            Sync();
            if (revealed) return;
            revealed = true;
            Save();
        }

        internal static void Say(string said)
        {
            try { GameController.ShowLocalizedMessage(said); } catch { }
            try { if (ChatManager.instance != null) ChatManager.instance.AddSystemMessage(said); } catch { }
        }

        // ------------------------------------------------------------------ голод

        private static void Hunger(HumaniodUnit demon, int today)
        {
            if (fedDay > today) fedDay = today;
            if (appliedDay < fedDay) appliedDay = fedDay;
            if (appliedDay > today) appliedDay = today;
            if (today <= appliedDay) return;

            float lost = 0f;
            int deepest = 0;

            for (int d = appliedDay + 1; d <= today; d++)
            {
                int hungry = d - fedDay;
                if (hungry <= Grace.Value) continue;
                lost += hungry >= HardFrom.Value ? Hard.Value : Soft.Value;
                deepest = hungry;
            }

            appliedDay = today;
            Save();

            if (lost > 0f && demon.Data != null)
            {
                demon.Data.AddMorale(-lost);
                Say($"Голод души, {deepest}-й день: боевой дух −{lost:0}.");
            }
        }

        // ------------------------------------------------------------------ знаки на демоне

        internal static UIBuffInfo Card(string id, string name, string description, Sprite icon, bool good)
        {
            UIBuffDatabase db;
            try { db = UIBuffDatabase.Instance; }
            catch { return null; }
            if (db == null || db.buffs == null) return null;

            UIBuffInfo card = db.GetByID(id);
            if (card == null)
            {
                UIBuffInfo pattern = db.GetByID("WeightDebuff");
                if (pattern == null) return null;

                card = UnityEngine.Object.Instantiate(pattern);
                card.name = id;
                card.id = id;

                List<UIBuffInfo> all = new List<UIBuffInfo>(db.buffs);
                all.Add(card);
                db.buffs = all.ToArray();
            }

            card.buffname = name;
            card.description = description;
            card.type = good ? bufftype.positive : bufftype.negative;
            card.isVisible = true;
            card.showDuration = false;
            card.canForceRemove = false;
            card.immuneDispel = true;
            if (card.addAttrs != null) card.addAttrs.Clear();
            if (icon != null) card.icon = icon;

            return card;
        }

        private const string HungerId = "DemonLookHunger";

        private static void Cards(HumaniodUnit demon, int today)
        {
            if (demon.buffmanger == null) return;

            int hungry = fedDay >= 0 ? today - fedDay : 0;

            if (hungry <= Grace.Value)
            {
                if (demon.buffmanger.ContainBuff(HungerId)) demon.buffmanger.RemoveBuff(HungerId);
                return;
            }

            string words = $"Без души {hungry} дн. Неделю демон терпит; дальше боевой дух тает на "
                + $"{Soft.Value:0} в день, с {HardFrom.Value}-го дня — на {Hard.Value:0}. Выпитая душа "
                + $"снимает голод и возвращает {Fed.Value:0} боевого духа.";

            UIBuffInfo card = Card(HungerId, $"Голод души: {hungry}-й день", words, null, false);
            if (card == null) return;

            if (!demon.buffmanger.ContainBuff(HungerId)) demon.buffmanger.AddBuff(new BuffBase(card, demon));
        }

        // ------------------------------------------------------------------ такт

        private static float ticked = -100f;

        internal static void Tick()
        {
            if (Enabled == null || !Enabled.Value) return;

            float now = Time.unscaledTime;
            if (now - ticked < 2f && now >= ticked) return;
            ticked = now;

            try
            {
                HumaniodUnit demon = Demon();
                if (demon == null || demon.Data == null) return;

                Sync();

                int today = Today();
                if (today < 0) return;

                if (fedDay < 0)
                {
                    fedDay = today;
                    appliedDay = today;
                    Save();
                }

                Hunger(demon, today);
                Dawn(today);
                SchoolSync(demon);
                Cards(demon, today);
                Veil.Keep(demon);
                Chronicle.Give(demon);
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Души: такт сорвался: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ сохранение

        private static System.Reflection.FieldInfo archiveField;
        private static string loadedFor;
        private static float synced = -100f;

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
            return Path.Combine(BepInEx.Paths.ConfigPath, "aor.souls." + archive + ".txt");
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
            power = 0f;
            count = 0;
            fedDay = -1;
            appliedDay = -1;
            revealed = false;
            veiled = false;
            chronicle = false;
            bodies.Clear();
            suspicion.Clear();
            searched.Clear();
            raids.Clear();
            bribeWeek = -1;
            bribes = 0;
            loadedFor = path;

            try
            {
                if (!File.Exists(path)) return;

                foreach (string line in File.ReadAllLines(path))
                {
                    int split = line.IndexOf('=');
                    if (split <= 0) continue;

                    string key = line.Substring(0, split).Trim();
                    string value = line.Substring(split + 1).Trim();

                    switch (key)
                    {
                        case "power": float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out power); break;
                        case "count": int.TryParse(value, out count); break;
                        case "fed": int.TryParse(value, out fedDay); break;
                        case "applied": int.TryParse(value, out appliedDay); break;
                        case "revealed": revealed = value == "1"; break;
                        case "veil": veiled = value == "1"; break;
                        case "chronicle": chronicle = value == "1"; break;
                        case "suspect":
                        case "searched":
                        case "raid":
                        case "bribes":
                            {
                                string[] parts = value.Split(';');
                                int a, b;
                                if (parts.Length < 2 || !int.TryParse(parts[0], out a) || !int.TryParse(parts[1], out b)) break;
                                if (key == "suspect") suspicion[a] = b;
                                else if (key == "searched") searched[a] = b;
                                else if (key == "raid") raids[a] = b;
                                else { bribeWeek = a; bribes = b; }
                                break;
                            }
                        case "body":
                            {
                                string[] parts = value.Split(';');
                                int team, day;
                                if (parts.Length >= 3 && int.TryParse(parts[0], out team) && int.TryParse(parts[2], out day))
                                    bodies.Add(new Body { team = team, whose = parts[1], day = day });
                                break;
                            }
                    }
                }
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Души: не смог прочесть: " + e.Message);
            }
        }

        private static bool dirty;

        /// <summary>
        /// Marks the state changed. The file is written only when the game itself saves, and read
        /// when it loads: a soul drunk and then undone by loading an older save is undone too.
        /// </summary>
        private static void Save()
        {
            dirty = true;
        }

        /// <summary>The game is saving: so do we.</summary>
        internal static void Flush()
        {
            if (Enabled == null || !Enabled.Value) return;
            Sync();
            if (!dirty && System.IO.File.Exists(loadedFor ?? FilePath())) return;
            Write();
            dirty = false;
        }

        /// <summary>The game has loaded a save: take up what was written with it.</summary>
        internal static void Reload()
        {
            if (Enabled == null || !Enabled.Value) return;
            synced = Time.unscaledTime;
            Load(FilePath());
            dirty = false;
            ticked = -100f;
        }

        private static void Write()
        {
            try
            {
                string path = loadedFor ?? FilePath();
                List<string> lines = new List<string>
                {
                    "power=" + power.ToString("0.###", CultureInfo.InvariantCulture),
                    "count=" + count,
                    "fed=" + fedDay,
                    "applied=" + appliedDay,
                    "revealed=" + (revealed ? "1" : "0"),
                    "veil=" + (veiled ? "1" : "0"),
                    "chronicle=" + (chronicle ? "1" : "0"),
                };
                foreach (Body b in bodies) lines.Add("body=" + b.team + ";" + b.whose + ";" + b.day);
                foreach (KeyValuePair<int, int> one in suspicion) lines.Add("suspect=" + one.Key + ";" + one.Value);
                foreach (KeyValuePair<int, int> one in searched) lines.Add("searched=" + one.Key + ";" + one.Value);
                foreach (KeyValuePair<int, int> one in raids) lines.Add("raid=" + one.Key + ";" + one.Value);
                lines.Add("bribes=" + bribeWeek + ";" + bribes);
                File.WriteAllLines(path, lines.ToArray());
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Души: не смог записать: " + e.Message);
            }
        }
    }

    // Души пишутся вместе с сохранением игры и поднимаются вместе с ним.
    [HarmonyPatch(typeof(SaveLoadManager), "WriteDataToSaveFile")]
    internal static class WriteSave_Souls_Patch
    {
        private static void Postfix() { try { Souls.Flush(); } catch { } }
    }

    [HarmonyPatch(typeof(TroopManagement.ArenaMatchManager), "LoadInstance")]
    internal static class LoadInstance_Souls_Patch
    {
        private static void Postfix() { try { Souls.Reload(); } catch { } }
    }

    // Школа Тьмы у демона не покупается опытом: её ступени приходят с душами.
    [HarmonyPatch(typeof(UISpellSlot), "SkillLevelup")]
    internal static class SkillLevelup_Souls_Patch
    {
        private static bool Prefix(UISpellSlot __instance)
        {
            if (Souls.Enabled == null || !Souls.Enabled.Value) return true;

            try
            {
                HumaniodUnit unit = SkillTreeManager.instance != null ? SkillTreeManager.instance.unit : null;
                if (unit == null || !Racial.IsDemon(unit)) return true;

                bool dark;
                if (__instance.isPassive)
                {
                    UITalentInfo talent = __instance.GetTalentInfo();
                    dark = talent != null && talent.talentClass == SkillSet.black;
                }
                else
                {
                    UISpellInfo spell = __instance.GetSpellInfo();
                    dark = spell != null && spell.SkillSet == SkillSet.black;
                }

                if (!dark) return true;

                GameController.ShowLocalizedMessage("Школа Тьмы у демона растёт только от выпитых душ.");
                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}
