using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using DuloGames.UI;
using spell;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace DemonLook
{
    /// <summary>
    /// The world does not forget what it is living beside.
    ///
    /// Демон растёт с каждой иссушенной душой, и ничто ему не отвечает: мир смотрит, как
    /// прибывает сила, и молчит. Оттого игра за Короля Ада рано или поздно превращается в
    /// прогулку — противников нет, есть только расстояние между ними.
    ///
    /// Раз в месяц против него выходит поход. Не разъезд стражи и не наёмники: Герой с
    /// освящённым оружием и его люди, и каждый следующий поход сильнее предыдущего на пять
    /// ступеней. Первый придёт к неопытному демону и будет ему по силам; десятый придёт к
    /// тому, кто успел взять своё с тысяч, — и тоже будет по силам, потому что рос вместе с
    /// ним.
    ///
    /// Счёт походов живёт своим файлом рядом с настройками, а не в сохранении: мод в чужое
    /// сохранение не пишет. Потеряется файл — счёт пойдёт сначала, и худшее следствие этого
    /// в том, что очередной поход окажется слабее, чем мог бы.
    /// </summary>
    internal static class Crusade
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> EveryDays;
        internal static ConfigEntry<int> FirstDay;
        internal static ConfigEntry<int> StartLevel;
        internal static ConfigEntry<int> LevelStep;
        internal static ConfigEntry<int> Count;
        internal static ConfigEntry<string> Band;
        internal static ConfigEntry<float> Near;
        internal static ConfigEntry<float> HolyDamage;
        internal static ConfigEntry<float> HolyExp;
        internal static ConfigEntry<string> HolyBuff;
        internal static ConfigEntry<bool> RaiseHeroes;
        internal static ConfigEntry<bool> BlessHeroes;
        internal static ConfigEntry<string> Orders;
        internal static ConfigEntry<string> OrderNames;
        internal static ConfigEntry<bool> HeroesHunt;
        internal static ConfigEntry<int> AfterKills;
        internal static ConfigEntry<int> HostileKills;
        internal static ConfigEntry<string> Gear;
        internal static ConfigEntry<string> Schools;
        internal static ConfigEntry<int> BooksFrom;
        internal static ConfigEntry<int> BooksEvery;
        internal static ConfigEntry<float> MasteryPerLevel;

        private static int done = -1;
        private static string ledger;
        private static bool busy;
        private static float looked;

        // Кто из живущих несёт святость. Держим по номеру объекта, а не по ссылке: объект
        // умирает, номер остаётся, и мёртвому опыт всё равно не нужен.
        private static readonly HashSet<int> blessed = new HashSet<int>();

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Crusade", "Enabled", true,
                "Let the world answer. Once a month a Hero gathers a band and comes hunting "
                + "the demon, and every month the band that comes is stronger than the last.");

            EveryDays = config.Bind("Crusade", "EveryDays", 90,
                new ConfigDescription(
                    "Days between one hunt and the next. Ninety is three months by the game's "
                    + "own reckoning, which counts a month as thirty days: часто выходить "
                    + "против такого никто не станет, а вот раз в сезон собрать людей и пойти "
                    + "— дело.",
                    new AcceptableValueRange<int>(1, 3650)));

            FirstDay = config.Bind("Crusade", "FirstDay", 90,
                new ConfigDescription(
                    "The day the first hunt comes. Three months of quiet at the start: слух "
                    + "должен разойтись, и кто-то должен решиться.",
                    new AcceptableValueRange<int>(1, 36500)));

            StartLevel = config.Bind("Crusade", "StartLevel", 10,
                new ConfigDescription(
                    "What level the first band comes at. Ten: первыми идут те, кому нечего "
                    + "терять, и идут они в железе первого яруса. Настоящие охотники "
                    + "появляются потом, когда предыдущие не возвращаются.",
                    new AcceptableValueRange<int>(1, 200)));

            LevelStep = config.Bind("Crusade", "LevelStep", 10,
                new ConfigDescription(
                    "How much higher each following band stands.",
                    new AcceptableValueRange<int>(0, 50)));

            Schools = config.Bind("Crusade", "Schools",
                "white,defender,fighter,commander,berserker,duelist",
                "The schools a hero learns, in the order he learns them. One book to begin "
                + "with, another for every ten levels past the twentieth. A man who has read "
                + "four of these is not a guardsman with a bigger number — he fights "
                + "differently.");

            BooksFrom = config.Bind("Crusade", "BooksFrom", 20,
                new ConfigDescription(
                    "Up to this level a hero knows one school and no more.",
                    new AcceptableValueRange<int>(1, 200)));

            BooksEvery = config.Bind("Crusade", "BooksEvery", 10,
                new ConfigDescription(
                    "And one more school for every this many levels above that.",
                    new AcceptableValueRange<int>(1, 100)));

            MasteryPerLevel = config.Bind("Crusade", "MasteryPerLevel", 0.3f,
                new ConfigDescription(
                    "How deep his weapon mastery runs, per level. At three tenths a hero of "
                    + "fifty handles his weapon at fifteen — the game's own ceiling. Mastery "
                    + "is what makes a swing land; without it a levelled man is only sturdy.",
                    new AcceptableValueRange<float>(0f, 2f)));

            Gear = config.Bind("Crusade", "Gear",
                "1=T1:Common,20=T2:Common,30=T3:Uncommon,45=T3:Rare,60=T4:Rare,80=T5:Epic",
                "What the band wears, by the level it comes at. Each step is «level=tier:"
                + "quality», and the highest step a band qualifies for is the one it gets. "
                + "Below the first step they wear what the settings give ordinary hunters. "
                + "One step and no purple, deliberately. The band is not meant to out-equip the "
                + "demon — it is meant to be eight men who came to die for a reason, and what "
                + "makes them dangerous is their number, their books and their captain. He "
                + "alone wears the fourth tier, and it is what marks him out from twenty paces. "
                + "Give the rest what he has and there is no captain, only a crowd.");

            Count = config.Bind("Crusade", "Count", 8,
                new ConfigDescription(
                    "How many come, the Hero among them.",
                    new AcceptableValueRange<int>(1, 40)));

            Band = config.Bind("Crusade", "Band",
                "Guard_T2_EliteKnight,WhiteSteedChampion,WhiteSteedChampion,WhiteSteedChampion,WhiteSteedChampion,WhiteSteedChampion,Guard_T2_EliteKnight,Guard_T2_EliteKnight",
                "Who comes, in order. The first of them is the captain: he takes the plate and "
                + "the greatsword, and he is the only one who does, so a knight has to stand "
                + "first — put a mage there and the band is led by a man in a robe holding a "
                + "two-handed sword. After him come the five who get the books, one discipline "
                + "each, and behind them the rest of the knights. Eight in all, and every one "
                + "of them means something different when you look at the line.");

            Near = config.Bind("Crusade", "Near", 25f,
                new ConfigDescription(
                    "How far from the demon they appear, in metres. Far enough to be seen "
                    + "coming, near enough that there is no outrunning them.",
                    new AcceptableValueRange<float>(5f, 200f)));

            HolyDamage = config.Bind("Crusade", "HolyDamage", 2f,
                new ConfigDescription(
                    "What the Hero's blessed weapons add to his damage. Two is the plus two "
                    + "hundred percent: whatever he fights with is holy while he holds it.",
                    new AcceptableValueRange<float>(0f, 20f)));

            HolyExp = config.Bind("Crusade", "HolyExp", 3f,
                new ConfigDescription(
                    "What the Hero's experience is multiplied by. Three is the three hundred "
                    + "percent: he is hunting the whole way here, and arrives having grown.",
                    new AcceptableValueRange<float>(1f, 50f)));

            AfterKills = config.Bind("Crusade", "AfterKills", 500,
                new ConfigDescription(
                    "How many souls he must take before the hunts begin at all. Until then "
                    + "nobody is coming: a demon nobody has noticed is a rumour, and rumours "
                    + "do not raise bands. The count is the same one the tribute keeps.",
                    new AcceptableValueRange<int>(0, 1000000)));

            HostileKills = config.Bind("Crusade", "HostileKills", 1000,
                new ConfigDescription(
                    "How many souls before the world turns on him by itself — before heroes "
                    + "and sworn orders count him an enemy wherever they meet him. Below "
                    + "this he passes for one more armed man on the road.",
                    new AcceptableValueRange<int>(0, 1000000)));

            HeroesHunt = config.Bind("Crusade", "HeroesHunt", true,
                "Let the world's wandering heroes count a demon as an enemy on sight. Without "
                + "this they walk past him: the game reckons enmity by faction, and a hero "
                + "stands in the neutral citizenry, with which the player has no quarrel. "
                + "Being a demon is not a political fact to this game, and that is exactly "
                + "why nothing ever came for him.");

            Orders = config.Bind("Crusade", "Orders", "WhiteSteed",
                "Factions that hunt demons on sight, wherever they meet one and whatever the "
                + "standing between them otherwise. Paladins as such do not exist in this "
                + "world; the nearest thing to a holy order is the White Steed, whose "
                + "champions are the ones who already come for the summoning. Add others by "
                + "the game's own faction names, separated by commas.");

            OrderNames = config.Bind("Crusade", "OrderNames", "SilverMask,Inquisitor,Priest",
                "And the same for anyone whose unit name contains one of these, whatever "
                + "faction they stand in. The Silver Mask inquisitors belong to no faction of "
                + "their own, and an inquisitor who walks past a demon is not an inquisitor.");

            RaiseHeroes = config.Bind("Crusade", "RaiseHeroes", true,
                "Let the world's wandering heroes grow with the demon. The game keeps between "
                + "sixteen and thirty-two of them roaming the map hunting bandits, and it "
                + "never raises one of them a single step: a hero who was a threat in the "
                + "first spring is a nuisance by the third. They rise on the same ladder as "
                + "the hunts do.");

            BlessHeroes = config.Bind("Crusade", "BlessHeroes", true,
                "Lay the blessing on every wandering hero, not only on the one who leads a "
                + "hunt. They are the men the world sends against a demon; the light is what "
                + "they have instead of numbers.");

            HolyBuff = config.Bind("Crusade", "HolyBuff", "LightedWeapons",
                "The game's own buff to build the blessing on, by its id. LightedWeapons is "
                + "the light-imbued weapons of the priests, which is what this is.");
        }

        private static string File
        {
            get
            {
                if (ledger == null)
                {
                    ledger = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "aor.crusade.txt");
                }

                return ledger;
            }
        }

        /// <summary>How many hunts have already come.</summary>
        internal static int Done
        {
            get
            {
                if (done < 0)
                {
                    done = 0;

                    try
                    {
                        if (System.IO.File.Exists(File))
                        {
                            int read;
                            if (int.TryParse(System.IO.File.ReadAllText(File).Trim(), out read))
                            {
                                done = Mathf.Max(0, read);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        DemonLookPlugin.Log.LogWarning("Не смог прочесть счёт походов: " + e.Message);
                    }
                }

                return done;
            }
        }

        private static void Counted()
        {
            done = Done + 1;

            try { System.IO.File.WriteAllText(File, done.ToString()); }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Не смог записать счёт походов: " + e.Message);
            }
        }

        /// <summary>The day the next hunt is due.</summary>
        internal static int Due()
        {
            return FirstDay.Value + Done * Mathf.Max(1, EveryDays.Value);
        }

        /// <summary>What level the band that comes next stands at.</summary>
        internal static int Level()
        {
            return Mathf.Max(1, StartLevel.Value + Done * LevelStep.Value);
        }

        /// <summary>
        /// Looks, now and then, at whether it is time.
        ///
        /// Раз в несколько секунд, а не каждый кадр: день в игре не меняется чаще.
        /// </summary>
        internal static void Tick()
        {
            if (!Enabled.Value || busy) return;

            float now = Time.realtimeSinceStartup;
            if (now - looked < 5f) return;

            looked = now;

            try
            {
                if (TimeManager.Instance == null) return;

                // Реестр героев заполняем при каждом взгляде, а не только в день похода:
                // освящать их надо с первого дня, а не с тридцатого, да и подросли они,
                // может быть, ещё в прошлой жизни этого сохранения.
                Raise();

                if (TimeManager.TotalDay < Due()) return;

                // И не раньше, чем о нём есть что рассказать. Пока душ мало, демон — слух,
                // а на слух отряды не собирают.
                if (Racial.Taken < AfterKills.Value) return;

                // В пути по карте мира догонять некого: там нет ни сцены, ни земли под ногами.
                if (WorldTravelManager.instance != null) return;

                HumaniodUnit demon = gameManager.currentplayUnit;
                if (demon == null || demon.Data == null || demon.Data.isdead) return;
                if (!Racial.IsDemon((UnitAttribute)(object)demon)) return;

                // Посреди чужой сцены гостей не зовут.
                if (Summoning.Truce) return;

                busy = true;
                ((MonoBehaviour)(object)demon).StartCoroutine(Come(demon));
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Не смог свериться с календарём: " + e.Message);
            }
        }

        /// <summary>Brings the band, once.</summary>
        private static IEnumerator Come(HumaniodUnit demon)
        {
            int level = Level();
            int number = Done + 1;

            List<UnitInfo> band = new List<UnitInfo>();

            foreach (string one in (Band.Value ?? "").Split(','))
            {
                string name = one.Trim();
                if (name.Length == 0) continue;

                AsyncOperationHandle<UnitInfo> load =
                    Addressables.LoadAssetAsync<UnitInfo>((object)("unitdata/" + name));

                yield return load;

                if ((int)load.Status == 1 && load.Result != null) band.Add(load.Result);
                else DemonLookPlugin.Log.LogWarning($"Похода: шаблона «{name}» нет.");
            }

            if (band.Count == 0)
            {
                DemonLookPlugin.Log.LogWarning("Поход не состоялся: некому идти.");
                Summoning.tierOverride = null;
                Summoning.qualityOverride = null;
                busy = false;
                yield break;
            }

            Vector3 middle = demon.transform.position;
            int came = 0;
            UnitAttribute hero = null;

            // Подмена яруса и качества на время сбора отряда — и обязательно снять её после,
            // иначе в неё оденется и круг призыва, который к походу отношения не имеет.
            string tier, quality;

            Captain.Fresh();

            if (Wears(level, out tier, out quality))
            {
                Summoning.tierOverride = tier;
                Summoning.qualityOverride = quality;
            }

            for (int i = 0; i < Count.Value; i++)
            {
                float angle = (float)i * Mathf.PI * 2f / Mathf.Max(1, Count.Value);

                Vector3 where = middle
                    + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Near.Value;

                UnitAttribute one = Summoning.Make(band[i % band.Count], where, Faction.playerEnemy);
                if (one == null) continue;

                Summoning.Grow(one, level);
                Summoning.Outfit(one, i, Summoning.Roles.Value);

                if (hero == null)
                {
                    hero = one;
                    Bless(one);

                    // Вождь одевается после «Outfit», а не вместо него: тот выдал отряду
                    // общее, а это — то, чего остальным не досталось.
                    Captain.Lead(one);
                }
                else
                {
                    // Прочим — по книге ближнего боя, каждому своей. Маг, до которого дошли
                    // вплотную, беспомощен до неприличия, и весь поход от этого превращается
                    // в задачу на выносливость вместо боя.
                    Captain.Read(one);
                }

                came++;
            }

            if (came == 0)
            {
                DemonLookPlugin.Log.LogWarning("Поход не состоялся: никто не появился.");
                Summoning.tierOverride = null;
                Summoning.qualityOverride = null;
                busy = false;
                yield break;
            }

            Summoning.tierOverride = null;
            Summoning.qualityOverride = null;

            Counted();
            Raise();
            busy = false;

            DemonLookPlugin.Log.LogInfo($"Святой поход {number}: пришло {came}, "
                + $"уровень {level}, во главе "
                + ((hero != null && hero.Data != null) ? hero.Data.unitname : "никого")
                + $". Следующий на {Due()}-й день.");

            try { GameController.ShowMessage("Святой поход"); } catch { }
        }

        // Кто из реестра мира — герой. Обновляется вместе с ростом, раз в месяц.
        private static readonly HashSet<int> roster = new HashSet<int>();

        /// <summary>
        /// Raises every wandering hero to the height the demon has driven the world to.
        ///
        /// Уровень у героя не хранится — он выводится из характеристик: «SetLevel» складывает
        /// вложенное во все шесть и делит. Поэтому просто записать число нельзя, оно сотрётся
        /// при первом же пересчёте. Растить надо то, из чего оно считается.
        ///
        /// Добавляем по очку самой отстающей характеристике и спрашиваем игру, что из этого
        /// вышло, пока не дорастём. Выходит человек, а не число: он и бьёт сильнее, и держит
        /// дольше, и уровень у него настоящий.
        /// </summary>
        internal static void Raise()
        {
            if (!Enabled.Value || !RaiseHeroes.Value) return;

            int target = Level();
            int raised = 0, points = 0;

            try
            {
                List<NPCSaveData> heroes = HeroUnitMaker.heros;
                if (heroes == null) return;

                roster.Clear();

                foreach (NPCSaveData hero in heroes)
                {
                    if (hero == null || hero.humanAttribute == null) continue;

                    roster.Add(hero.id);

                    if (hero.level >= target) { Teach(hero, hero.level); continue; }

                    int was = hero.level;

                    for (int guard = 0; guard < 600 && hero.level < target; guard++)
                    {
                        int low = 0;

                        for (int i = 1; i < 6; i++)
                        {
                            if (hero.humanAttribute[i] < hero.humanAttribute[low]) low = i;
                        }

                        hero.humanAttribute[low]++;
                        points++;

                        hero.SetLevel();
                    }

                    if (hero.level > was) raised++;

                    Teach(hero, hero.level);
                }

                if (raised > 0)
                {
                    DemonLookPlugin.Log.LogInfo($"Герои мира подросли: {raised} из "
                        + $"{heroes.Count}, до {target}-го уровня, роздано очков {points}.");
                }
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Не смог поднять героев: " + e.Message);
            }
        }

        private static readonly HashSet<Faction> sworn = new HashSet<Faction>();
        private static string listedOrders;

        private static readonly List<string> swornNames = new List<string>();
        private static string listedNames;

        /// <summary>
        /// True for those whose whole purpose is to end things like him.
        ///
        /// Игра отношений с демоном не знает: демон для неё — раса, а вражда считается по
        /// фракциям. Оттого чемпион ордена, встретив Короля Ада на дороге, кивает и идёт
        /// мимо, если их города не в ссоре. Орден, который проходит мимо демона, — не орден.
        ///
        /// Здесь названные ордена считают демона врагом всегда: не по политике, а по тому,
        /// ради чего они существуют.
        /// </summary>
        internal static bool Sworn(UnitAttribute who)
        {
            if (!Enabled.Value || who == null || who.Data == null) return false;

            string written = Orders.Value ?? "";

            if (written != listedOrders)
            {
                listedOrders = written;
                sworn.Clear();

                foreach (string one in written.Split(','))
                {
                    string name = one.Trim();
                    if (name.Length == 0) continue;

                    try { sworn.Add((Faction)Enum.Parse(typeof(Faction), name, true)); }
                    catch { DemonLookPlugin.Log.LogWarning($"Ордена: фракции «{name}» нет."); }
                }
            }

            if (sworn.Contains(who.Data.team)) return true;

            string writtenNames = OrderNames.Value ?? "";

            if (writtenNames != listedNames)
            {
                listedNames = writtenNames;
                swornNames.Clear();

                foreach (string one in writtenNames.Split(','))
                {
                    string name = one.Trim();
                    if (name.Length > 0) swornNames.Add(name);
                }
            }

            if (swornNames.Count > 0 && who.info != null && who.info.name != null)
            {
                foreach (string mark in swornNames)
                {
                    if (who.info.name.IndexOf(mark, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// What a band of this height wears.
        ///
        /// Лестница снаряжения идёт отдельно от лестницы уровней, потому что это разные вещи:
        /// уровень растёт каждый месяц, а доспех меняется ступенями. Тридцатый выходит в
        /// добротном, сороковой в эпическом, пятидесятый — в легендарном, какое в этом мире
        /// вообще можно надеть. Именные реликвии сюда не попадают: их находят, а не выдают.
        /// </summary>
        internal static bool Wears(int level, out string tier, out string quality)
        {
            tier = null;
            quality = null;

            int best = -1;

            foreach (string step in (Gear.Value ?? "").Split(';'))
            {
                string[] halves = step.Split('=');
                if (halves.Length != 2) continue;

                int need;
                if (!int.TryParse(halves[0].Trim(), out need)) continue;
                if (level < need || need <= best) continue;

                string[] parts = halves[1].Split(':');
                if (parts.Length != 2) continue;

                best = need;
                tier = parts[0].Trim();
                quality = parts[1].Trim();
            }

            return best >= 0;
        }

        private static readonly List<SkillSet> lessons = new List<SkillSet>();
        private static string taught;

        /// <summary>How many books a man of this height has read.</summary>
        internal static int Books(int level)
        {
            int first = Mathf.Max(1, BooksFrom.Value);
            int every = Mathf.Max(1, BooksEvery.Value);

            if (level <= first) return 1;

            return 1 + (level - first) / every;
        }

        /// <summary>
        /// Teaches a hero what a man of his standing would know, and how to hold a weapon.
        ///
        /// Уровень сам по себе делает человека только крепче: больше здоровья, выше числа.
        /// Дерётся он всё так же — тем, что заложено в шаблон. Поэтому вместе с уровнем
        /// растёт и то, чем он на самом деле опасен: прочитанные книги и владение оружием.
        ///
        /// Школы кладутся прямо в сохранённые данные, а не выдаются живому существу: герой
        /// большую часть времени не загружен, он строка в реестре мира. Когда он выйдет в
        /// сцену, игра поднимет его уже знающим.
        /// </summary>
        private static void Teach(NPCSaveData hero, int level)
        {
            if (hero == null) return;

            try
            {
                string written = Schools.Value ?? "";

                if (written != taught)
                {
                    taught = written;
                    lessons.Clear();

                    foreach (string one in written.Split(','))
                    {
                        string name = one.Trim();
                        if (name.Length == 0) continue;

                        try { lessons.Add((SkillSet)Enum.Parse(typeof(SkillSet), name, true)); }
                        catch { DemonLookPlugin.Log.LogWarning($"Школы «{name}» в игре нет."); }
                    }
                }

                if (hero.skillSet == null) hero.skillSet = new List<SkillSet>();

                int want = Mathf.Min(Books(level), lessons.Count);

                for (int i = 0; i < want; i++)
                {
                    if (!hero.skillSet.Contains(lessons[i])) hero.skillSet.Add(lessons[i]);
                }

                // Владение оружием — по всем видам разом: какой шаблон какое возьмёт, заранее
                // не угадаешь, а держать он должен любое из них как человек своего уровня.
                if (hero.weaponMastery != null && MasteryPerLevel.Value > 0f)
                {
                    int deep = Mathf.Clamp(
                        Mathf.RoundToInt(level * MasteryPerLevel.Value), 0, 15);

                    for (int i = 0; i < hero.weaponMastery.Length; i++)
                    {
                        if (hero.weaponMastery[i] < deep) hero.weaponMastery[i] = deep;
                    }
                }
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Не смог обучить героя: " + e.Message);
            }
        }

        /// <summary>True for a wandering hero, when heroes are set to hunt.</summary>
        internal static bool Seeker(UnitAttribute who)
        {
            return HeroesHunt.Value && Wanderer(who);
        }

        /// <summary>True when one of these two is a sworn order and the other a demon.</summary>
        internal static bool Hunts(UnitAttribute A, UnitAttribute B)
        {
            if (!Enabled.Value || A == null || B == null || A == B) return false;
            if (A.Data == null || B.Data == null) return false;

            // Пока за ним не числится достаточно, он для мира просто ещё один вооружённый
            // человек на дороге. Узнают его позже и по делам, а не по крови.
            if (Racial.Taken < HostileKills.Value) return false;

            if (A.Data.race == UnitRace.demon && (Sworn(B) || Seeker(B))) return true;
            if (B.Data.race == UnitRace.demon && (Sworn(A) || Seeker(A))) return true;

            return false;
        }

        /// <summary>True for one of the world's wandering heroes.</summary>
        internal static bool Wanderer(UnitAttribute who)
        {
            if (who == null || who.Data == null) return false;

            NPCSaveData mind = who.Data as NPCSaveData;

            return mind != null && roster.Contains(mind.id);
        }

        /// <summary>Lays the blessing on the Hero: his weapons, and his learning.</summary>
        internal static void Bless(UnitAttribute hero)
        {
            if (hero == null) return;

            blessed.Add(hero.GetInstanceID());

            try
            {
                if (hero.buffmanger == null || UIBuffDatabase.Instance == null) return;

                UIBuffInfo shape = UIBuffDatabase.Instance.GetByID(HolyBuff.Value);

                if (shape == null)
                {
                    DemonLookPlugin.Log.LogWarning($"Освящения «{HolyBuff.Value}» в игре нет — "
                        + "Герой придёт без него.");
                    return;
                }

                BuffBase holy = new BuffBase(shape, hero, -1f);
                holy.addAttrs.Clear();
                holy.addAttrs.Add(new AddonAttributes(AddonAttribute.DamageIncrease,
                    HolyDamage.Value));

                hero.buffmanger.AddBuff(holy);
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Не смог освятить оружие Героя: " + e.Message);
            }
        }

        /// <summary>True for the one the light is on.</summary>
        internal static bool Blessed(UnitAttribute who)
        {
            return Enabled.Value && who != null && blessed.Contains(who.GetInstanceID());
        }
    }

    // Святой герой учится втрое быстрее: он не сидит в крепости, а идёт сюда через всю
    // страну, и всё, что попадается по дороге, идёт ему в счёт.
    [HarmonyPatch(typeof(HumaniodUnit), "GainExp")]
    internal static class GainExp_Holy_Patch
    {
        private static void Prefix(HumaniodUnit __instance, ref int exp)
        {
            try
            {
                if (!Crusade.Blessed((UnitAttribute)(object)__instance)) return;

                exp = Mathf.RoundToInt(exp * Crusade.HolyExp.Value);
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Не смог зачесть опыт Героя: " + e.Message);
            }
        }
    }
    // Герой мира, вышедший в сцену, получает своё освящение один раз. Проверка висит на
    // пересчёте характеристик — месте, через которое проходит всякий, кто появился, — и
    // стоит ровно одно обращение к множеству.
    [HarmonyPatch(typeof(UnitAttribute), "WriteUnitAttribute")]
    internal static class WriteUnitAttribute_Holy_Patch
    {
        private static readonly HashSet<int> given = new HashSet<int>();

        private static void Postfix(UnitAttribute __instance)
        {
            try
            {
                if (!Crusade.Enabled.Value || !Crusade.BlessHeroes.Value) return;
                if (__instance == null || __instance.Data == null) return;
                if (__instance.Data.isdead) return;

                if (!Crusade.Wanderer(__instance)) return;
                if (!given.Add(__instance.GetInstanceID())) return;

                Crusade.Bless(__instance);

                DemonLookPlugin.Log.LogInfo($"«{__instance.Data.unitname}» вышел освящённым: "
                    + $"урон +{Crusade.HolyDamage.Value * 100f:0}%, "
                    + $"опыт ×{Crusade.HolyExp.Value:0.#}.");
            }
            catch
            {
            }
        }
    }
}
