using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using I2.Loc;
using spell;
using UnityEngine;

namespace DemonLook
{
    /// <summary>
    /// The Darkness That Breaks the World's Limits: the demon's own mist, in place of the black one.
    ///
    /// Замер Чёрного тумана показал, как он устроен. Заклинание ставит вокруг заклинателя
    /// призванное облако — отдельный предмет со своим радиусом урона («damageRange»), —
    /// держит его «durationBS + durationLV × уровень» секунд и, пока оно стоит, бьёт по
    /// врагам в нём тьмой. Перезарядка сорок секунд, сорок две маны, три уровня, с 16-го
    /// уровня и 5-го мастерства.
    ///
    /// Демону вместо него — своё заклинание, собранное из того же тумана: свой номер, имя и
    /// описание. Облако впятеро шире по площади, тьма в нём почти не ранит, стоит оно тридцать
    /// секунд и ещё пять за каждый уровень после первого. А главное — внутри ломается время:
    /// демон, стоящий в тумане, живёт вдвое быстрее — ходит, бьёт, читает заклинания. Это
    /// «TimeSpeedMD»: игра множит на него шаг, скорость удара, анимации и собственные часы
    /// человека. Вышел из тумана — время вернулось.
    ///
    /// Перезарядка — неделя, и неделя мира: семь дней по его календарю, того самого, что идёт
    /// и на глобальной карте. Игровой счёт перезарядки для этого не годится — на карте мира
    /// игра обнуляет его каждый кадр, — поэтому время каста записывается само по себе, и
    /// каждый кадр значок получает столько, сколько осталось. Цифра на нём — часы.
    ///
    /// Остальные — заклинатели в сцене ритуала, тёмные маги — бросают Чёрный туман, как
    /// бросали.
    /// </summary>
    internal static class Worldbreak
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Id;
        internal static ConfigEntry<float> Area;
        internal static ConfigEntry<float> Damage;
        internal static ConfigEntry<float> First;
        internal static ConfigEntry<float> PerLevel;
        internal static ConfigEntry<float> Days;
        internal static ConfigEntry<float> Speed;
        internal static ConfigEntry<string> Ledger;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Worldbreak", "Enabled", true,
                "Give the demon «The Darkness That Breaks the World's Limits» in place of the "
                + "Black Mist: a far wider cloud that barely wounds, inside which the demon lives "
                + "twice as fast.");

            Id = config.Bind("Worldbreak", "Id", 9236,
                new ConfigDescription(
                    "The number the spell is kept under, in the game's database and in saves. "
                    + "Change it and a demon who knew the spell forgets it.",
                    new AcceptableValueRange<int>(1000, 999999)));

            Area = config.Bind("Worldbreak", "Area", 5f,
                new ConfigDescription(
                    "How many times the Black Mist's ground it covers. Five: the radius grows by "
                    + "the square root, a little over twice.",
                    new AcceptableValueRange<float>(1f, 50f)));

            Damage = config.Bind("Worldbreak", "Damage", 0.05f,
                new ConfigDescription(
                    "The share of the Black Mist's darkness damage left in it. Five hundredths: "
                    + "it stings and nothing more.",
                    new AcceptableValueRange<float>(0f, 1f)));

            First = config.Bind("Worldbreak", "First", 30f,
                new ConfigDescription(
                    "How long the cloud stands at the first level, in seconds.",
                    new AcceptableValueRange<float>(1f, 600f)));

            PerLevel = config.Bind("Worldbreak", "PerLevel", 5f,
                new ConfigDescription(
                    "How much every level learned after the first adds, in seconds.",
                    new AcceptableValueRange<float>(0f, 600f)));

            Days = config.Bind("Worldbreak", "Days", 7f,
                new ConfigDescription(
                    "How long the darkness takes to gather again, in days of the world's own "
                    + "calendar — the one that runs on the world map as well.",
                    new AcceptableValueRange<float>(0f, 365f)));

            Speed = config.Bind("Worldbreak", "Speed", 2f,
                new ConfigDescription(
                    "How much faster time runs for a demon standing in the cloud.",
                    new AcceptableValueRange<float>(1f, 10f)));

            Ledger = config.Bind("Worldbreak", "Ledger", "",
                "When the darkness was last called in each save, in hours of the world "
                + "calendar. Kept by the mod; leave it alone.");
        }

        // ------------------------------------------------------------------ сборка

        private const string NameKey = "Spell_Dark_WorldBreaker";
        private const string TextKey = "Spell_Dark_WorldBreaker_Des1";
        private const string BuffId = "WorldBreakerTime";
        private const string BuffKey = "Buff_WorldBreakerTime";
        private const string BuffTextKey = "Buff_WorldBreakerTime_Des";

        internal static UISpellInfo Mist;
        internal static UISpellInfo Spell;
        private static UIBuffInfo Haste;
        private static bool built;
        private static float nextWords;
        private static float nextFogs;

        internal static bool Mine(UISpellInfo info)
        {
            return info != null && Spell != null && (object)info == (object)Spell;
        }

        internal static bool Demon(UnitAttribute who)
        {
            return who != null && who.Data != null && who.Data.race == UnitRace.demon;
        }

        internal static void Tick()
        {
            if (Enabled == null || !Enabled.Value) return;

            try
            {
                if (!built)
                {
                    Build();
                    return;
                }

                if (Spell == null) return;

                if (Time.unscaledTime >= nextWords)
                {
                    nextWords = Time.unscaledTime + 5f;
                    Words();
                }

                if (Time.time >= nextFogs)
                {
                    nextFogs = Time.time + 0.2f;
                    Fogs();
                }
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Тьма, ломающая пределы: " + e.Message);
            }
        }

        private static void Build()
        {
            // База заклинаний живёт на менеджере игры; до его появления спрашивать её нельзя —
            // её собственный вызов тогда падает, и замер тумана так и падал на старте.
            if (gameManager.GM == null || gameManager.GM.spellDataBase == null) return;
            if (UIBuffDatabase.Instance == null) return;

            built = true;

            UISpellDatabase db = gameManager.GM.spellDataBase;
            if (db.spells == null) return;

            Mist = db.GetByID(236) ?? db.GetByName("BlackMist");
            if (Mist == null || Mist.behaviorPrefab == null)
            {
                DemonLookPlugin.Log.LogWarning("Тьма, ломающая пределы: Чёрного тумана в базе нет.");
                return;
            }

            int id = Id.Value;
            if (db.GetByID(id) != null)
            {
                DemonLookPlugin.Log.LogWarning($"Тьма, ломающая пределы: номер {id} занят, "
                    + "заклинание не собрано.");
                return;
            }

            UISpellInfo made = UnityEngine.Object.Instantiate(Mist);
            made.name = "WorldBreaker";
            made.ID = id;
            made.Name = NameKey;
            made.description = TextKey;
            if (made.DescriptionParam != null) made.DescriptionParam.Clear();

            // Цифра перезарядки — часы: значок делит остаток на неё, и полная неделя — это
            // семь раз по двадцать четыре.
            made.Cooldown = Mathf.Max(1f, Days.Value * 24f);
            made.CooldownLvReduce = 0f;

            // Поведение — своя копия, иначе правка облака легла бы и на Чёрный туман. Копия
            // хранится выключенной внутри выключенного, чтобы не ожить до каста; игра создаёт
            // её заново при каждом касте, и та уже живая.
            GameObject shelf = new GameObject("DemonLook.Templates");
            shelf.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(shelf);

            GameObject behav = UnityEngine.Object.Instantiate(Mist.behaviorPrefab, shelf.transform);
            behav.name = "WorldBreakerBehav";

            BehavSummon summon = behav.GetComponent<BehavSummon>();
            if (summon != null)
            {
                // «durationBS + durationLV × уровень»: тридцать на первом, дальше по пять.
                summon.durationBS = Mathf.Max(1f, First.Value - PerLevel.Value);
                summon.durationLV = PerLevel.Value;

                BehavDamageInfo hit = summon.damageInfo;
                if (hit != null)
                {
                    hit.damage *= Damage.Value;
                    hit.damageBaseMD *= Damage.Value;
                    hit.damageLevelMD *= Damage.Value;
                    hit.force = 0f;
                    hit.forceBaseMD = 0f;
                    hit.forceLevelMD = 0f;
                }
            }
            else
            {
                DemonLookPlugin.Log.LogWarning("Тьма, ломающая пределы: у тумана нет призыва — "
                    + "облако останется прежним.");
            }

            made.behaviorPrefab = behav;

            List<UISpellInfo> all = new List<UISpellInfo>(db.spells);
            all.Add(made);
            db.spells = all.ToArray();

            Spell = made;

            // Время внутри тумана — отдельная карточка, собранная из обычной бессрочной.
            UIBuffInfo template = UIBuffDatabase.Instance.GetByID("LethalChaserBuff");
            if (template != null)
            {
                Haste = UnityEngine.Object.Instantiate(template);
                Haste.name = BuffId;
                Haste.id = BuffId;
                Haste.buffname = BuffKey;
                Haste.description = BuffTextKey;
                Haste.icon = made.Icon;
                Haste.duration = -1f;
                Haste.type = bufftype.positive;
                Haste.canStack = false;
                Haste.visualEffect = new UnitEffects[0];
                Haste.endEffect = null;
                if (Haste.UMArecipes != null) Haste.UMArecipes.Clear();
            }

            Words();

            DemonLookPlugin.Log.LogInfo($"Тьма, ломающая пределы мира: собрана из Чёрного тумана под "
                + $"номером {id} — площадь ×{Area.Value:0.#}, урон ×{Damage.Value:0.##}, "
                + $"{First.Value:0}+{PerLevel.Value:0} с за уровень, перезарядка {Days.Value:0} дн., "
                + $"время в тумане ×{Speed.Value:0.#}"
                + (Haste == null ? " (карточки эффекта не нашлось — ускорения не будет)" : "") + ".");
        }

        // ------------------------------------------------------------------ слова

        private static void Words()
        {
            string when = Days.Value >= 1f
                ? $"{Days.Value:0} {Plural(Mathf.RoundToInt(Days.Value), "день", "дня", "дней")} по календарю мира"
                : $"{Days.Value * 24f:0} ч по календарю мира";

            Put(NameKey, "Тьма, ломающая пределы мира");

            Put(TextKey, "Демон призывает вокруг себя тьму, впятеро шире Чёрного тумана. "
                + "Ранит она едва-едва, зато внутри неё ломается само время: демон, стоящий в "
                + "тумане, ходит, бьёт и читает заклинания вдвое быстрее. Тьма держится "
                + $"{First.Value:0} секунд, и каждый уровень после первого прибавляет ещё "
                + $"{PerLevel.Value:0}. Собирается заново {when} — время идёт и на карте мира. "
                + "Цифра на значке — сколько часов осталось.");

            Put(BuffKey, "Время сломано");
            Put(BuffTextKey, "Во тьме, ломающей пределы мира, демон живёт вдвое быстрее: ход, "
                + "удары и чтение заклинаний.");
        }

        private static string Plural(int n, string one, string few, string many)
        {
            int ten = n % 10, hundred = n % 100;
            if (ten == 1 && hundred != 11) return one;
            if (ten >= 2 && ten <= 4 && (hundred < 12 || hundred > 14)) return few;
            return many;
        }

        /// <summary>Puts a line into the game's table under its key, making the key if it is new.</summary>
        private static void Put(string key, string said)
        {
            if (LocalizationManager.Sources == null) return;

            string now;
            if (LocalizationManager.TryGetTranslation(key, out now, false) && now == said) return;

            foreach (LanguageSourceData source in LocalizationManager.Sources)
            {
                if (source == null) continue;

                int lang = source.GetLanguageIndex(LocalizationManager.CurrentLanguage, true, false);
                if (lang < 0) continue;

                TermData term = source.GetTermData(key) ?? source.AddTerm(key);
                if (term == null || term.Languages == null || lang >= term.Languages.Length) continue;

                term.Languages[lang] = said;
                return;
            }
        }

        // ------------------------------------------------------------------ у демона

        /// <summary>Gives a demon the new spell in place of the Black Mist, at the same level.</summary>
        internal static void Convert(HumaniodUnit man)
        {
            if (Spell == null || Mist == null || !Demon(man) || man.spellmanger == null) return;

            try
            {
                SpellBase had = man.spellmanger.FindSpell(Mist);
                if (had == null) return;

                int level = Mathf.Max(1, had.level);

                man.spellmanger.RemoveSpell(Mist);
                if (!man.spellmanger.ContainSpell(Spell)) man.spellmanger.AddSpell(Spell, level);

                // Кнопки на панели держат номер заклинания — переписываем его, иначе кнопка
                // останется пустой.
                if (man.Data.actionSlots != null)
                {
                    foreach (ActionSlotData[] row in man.Data.actionSlots)
                    {
                        if (row == null) continue;

                        foreach (ActionSlotData slot in row)
                        {
                            if (slot != null && slot.isSpellSlot && slot.ID == Mist.ID) slot.ID = Spell.ID;
                        }
                    }
                }

                DemonLookPlugin.Log.LogInfo($"«{man.Data.unitname}»: Чёрный туман стал Тьмой, "
                    + $"ломающей пределы мира, уровень {level}.");
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Не смог сменить туман демону: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ перезарядка

        private static System.Reflection.FieldInfo archiveField;

        private static string Archive()
        {
            try
            {
                SaveLoadManager keeper = SaveLoadManager.Instance;
                if (keeper == null) return "—";

                if (archiveField == null) archiveField = AccessTools.Field(typeof(SaveLoadManager), "currentArchive");

                string where = archiveField != null ? archiveField.GetValue(keeper) as string : null;
                return string.IsNullOrEmpty(where) ? "—" : where.Replace("=", "_").Replace(";", "_");
            }
            catch
            {
                return "—";
            }
        }

        /// <summary>The world's own clock, in hours.</summary>
        private static float Now()
        {
            if (TimeManager.Instance == null) return 0f;

            return TimeManager.TotalDay * 24f + TimeManager.Hour
                + TimeManager.Instance.GameTime.Minutes / 60f;
        }

        private static Dictionary<string, float> cache;
        private static string cacheRead;

        // Спрашивают об этом каждый кадр, а меняется оно раз в неделю: разбираем строку, только
        // когда она поменялась.
        private static Dictionary<string, float> Read()
        {
            string written = Ledger.Value ?? "";
            if (cache != null && written == cacheRead) return new Dictionary<string, float>(cache, StringComparer.Ordinal);

            Dictionary<string, float> got = new Dictionary<string, float>(StringComparer.Ordinal);

            foreach (string row in written.Split(';'))
            {
                int split = row.LastIndexOf('=');
                if (split <= 0) continue;

                float at;
                if (float.TryParse(row.Substring(split + 1), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out at))
                {
                    got[row.Substring(0, split)] = at;
                }
            }

            cache = new Dictionary<string, float>(got, StringComparer.Ordinal);
            cacheRead = written;

            return got;
        }

        private static void Write(Dictionary<string, float> all)
        {
            List<string> rows = new List<string>();
            foreach (KeyValuePair<string, float> one in all)
            {
                rows.Add(one.Key + "=" + one.Value.ToString("0.##", CultureInfo.InvariantCulture));
            }

            Ledger.Value = string.Join(";", rows.ToArray());
        }

        /// <summary>Hours left until the darkness gathers again in this save.</summary>
        internal static float Left()
        {
            Dictionary<string, float> all = Read();
            string here = Archive();

            float last;
            if (!all.TryGetValue(here, out last)) return 0f;

            float week = Days.Value * 24f;
            float left = last + week - Now();

            // Записано позже, чем сейчас, — значит загружено сохранение раньше того каста: в
            // этом мире его ещё не было.
            if (left > week + 0.5f)
            {
                all.Remove(here);
                Write(all);
                return 0f;
            }

            return Mathf.Max(0f, left);
        }

        internal static void Cast()
        {
            Dictionary<string, float> all = Read();
            all[Archive()] = Now();
            Write(all);
        }

        /// <summary>Lays the world's clock onto the icon: what is left, in hours.</summary>
        internal static void Gate(SpellManager kit)
        {
            if (Spell == null || kit == null || kit.unit == null || !Demon(kit.unit)) return;

            SpellBase ours = kit.FindSpell(Spell);
            if (ours == null) return;

            float left = Left();
            if (left <= 0f) return;

            ours.cdtimer = left * Mathf.Max(0.05f, 1f - kit.unit.cooldownReduce);
        }

        // ------------------------------------------------------------------ облако

        private static readonly List<SummonedUnit> fogs = new List<SummonedUnit>();
        private static readonly HashSet<UnitAttribute> quickened = new HashSet<UnitAttribute>();

        /// <summary>Makes the new cloud as wide as it is meant to be.</summary>
        internal static void Enlarge(SummonedUnit cloud)
        {
            if (cloud == null || cloud.bindSpell == null || !Mine(cloud.bindSpell.theSpell)) return;

            float k = Mathf.Sqrt(Mathf.Max(1f, Area.Value));

            cloud.damageRange *= k;
            cloud.transform.localScale = cloud.transform.localScale * k;

            // Частицы по умолчанию не растут вслед за тем, на чём висят: без этого радиус
            // вырос бы, а облако на глаз осталось прежним.
            foreach (ParticleSystem one in cloud.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = one.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }

            fogs.Add(cloud);

            DemonLookPlugin.Log.LogInfo($"Тьма, ломающая пределы мира: облако радиусом "
                + $"{cloud.damageRange:0.#} м на {cloud.lifespan:0} с.");
        }

        /// <summary>Time runs twice as fast for every demon inside a cloud, and normally outside.</summary>
        private static void Fogs()
        {
            fogs.RemoveAll(one => one == null || !one.isActive || !one.gameObject.activeInHierarchy);

            List<UnitAttribute> inside = new List<UnitAttribute>();

            if (fogs.Count > 0 && AreaManager.Instance != null && AreaManager.Instance.characters != null)
            {
                foreach (UnitAttribute who in AreaManager.Instance.characters)
                {
                    if (who == null || !Demon(who) || who.Data.isdead) continue;

                    foreach (SummonedUnit cloud in fogs)
                    {
                        Vector3 d = who.transform.position - cloud.transform.position;
                        d.y = 0f;

                        if (d.magnitude <= cloud.damageRange)
                        {
                            inside.Add(who);
                            break;
                        }
                    }
                }
            }

            foreach (UnitAttribute who in inside)
            {
                if (quickened.Contains(who)) continue;
                if (Quicken(who, true)) quickened.Add(who);
            }

            foreach (UnitAttribute who in new List<UnitAttribute>(quickened))
            {
                if (who == null)
                {
                    quickened.Remove(who);
                    continue;
                }

                if (inside.Contains(who)) continue;

                Quicken(who, false);
                quickened.Remove(who);
            }
        }

        private static bool Quicken(UnitAttribute who, bool on)
        {
            if (Haste == null || who.buffmanger == null) return false;

            try
            {
                who.buffmanger.RemoveBuff(BuffId);
                if (!on) return true;

                BuffBase time = new BuffBase(Haste, who);
                time.addAttrs.Clear();
                time.addAttrs.Add(new AddonAttributes(AddonAttribute.TimeSpeed, Speed.Value - 1f));

                who.buffmanger.AddBuff(time);
                return true;
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogWarning("Не смог сломать время: " + e.Message);
                return false;
            }
        }
    }

    // ------------------------------------------------------------------ перехваты

    // Облако ставится здесь: теперь у него есть заклинание, от которого оно, и радиус.
    [HarmonyPatch(typeof(SummonedUnit), "SetSummon")]
    internal static class SetSummon_Worldbreak_Patch
    {
        private static void Postfix(SummonedUnit __instance)
        {
            try { Worldbreak.Enlarge(__instance); } catch { }
        }
    }

    // Счёт перезарядки игра ведёт здесь; мы кладём поверх часы мира.
    [HarmonyPatch(typeof(SpellManager), "CalculateCoolDown")]
    internal static class CalculateCoolDown_Worldbreak_Patch
    {
        private static void Postfix(SpellManager __instance)
        {
            try { Worldbreak.Gate(__instance); } catch { }
        }
    }

    // И спрашивает, готово ли заклинание, — здесь же отвечаем за неделю.
    [HarmonyPatch(typeof(SpellManager), "IsSpellReady")]
    internal static class IsSpellReady_Worldbreak_Patch
    {
        private static void Postfix(SpellBase spell, ref bool __result)
        {
            try
            {
                if (__result && spell != null && Worldbreak.Mine(spell.theSpell) && Worldbreak.Left() > 0f)
                {
                    __result = false;
                }
            }
            catch
            {
            }
        }
    }

    // Каст: отсюда отсчитывается неделя.
    [HarmonyPatch(typeof(SpellBase), "StartCast")]
    internal static class StartCast_Worldbreak_Patch
    {
        private static void Postfix(SpellBase __instance)
        {
            try
            {
                if (__instance != null && Worldbreak.Mine(__instance.theSpell)) Worldbreak.Cast();
            }
            catch
            {
            }
        }
    }

    // Демон учит Чёрный туман — выучит своё.
    [HarmonyPatch(typeof(SpellManager), "AddSpell")]
    internal static class AddSpell_Worldbreak_Patch
    {
        private static void Prefix(SpellManager __instance, ref UISpellInfo spell)
        {
            try
            {
                if (Worldbreak.Spell == null || spell == null || Worldbreak.Mist == null) return;
                if ((object)spell != (object)Worldbreak.Mist) return;
                if (__instance == null || !Worldbreak.Demon(__instance.unit)) return;

                spell = Worldbreak.Spell;
            }
            catch
            {
            }
        }
    }

    // В окне школы демону — своё заклинание на месте Чёрного тумана, прочим — как было.
    [HarmonyPatch(typeof(SkillsetManager), "UpdateInfo")]
    internal static class SkillsetUpdate_Worldbreak_Patch
    {
        private static void Prefix(SkillsetManager __instance, HumaniodUnit t_unit)
        {
            try
            {
                if (Worldbreak.Spell == null || Worldbreak.Mist == null || __instance.spellslots == null) return;

                bool demon = Worldbreak.Demon(t_unit);

                foreach (UISpellSlot slot in __instance.spellslots)
                {
                    if (slot == null || slot.isPassive) continue;

                    UISpellInfo shown = slot.GetSpellInfo();

                    if (demon && (object)shown == (object)Worldbreak.Mist) slot.Assign(Worldbreak.Spell);
                    else if (!demon && Worldbreak.Mine(shown)) slot.Assign(Worldbreak.Mist);
                }
            }
            catch
            {
            }
        }
    }

    // Демон, выучивший туман раньше, получает новое при загрузке — на том же уровне.
    [HarmonyPatch(typeof(HumaniodUnit), "InitializeUnit")]
    internal static class InitializeUnit_Worldbreak_Patch
    {
        private static void Postfix(HumaniodUnit __instance)
        {
            try { Worldbreak.Convert(__instance); } catch { }
        }
    }

    // Школу НПС учат по её списку. Новое заклинание — демоново, в общий список школы оно не
    // входит: тёмный маг и дальше учит Чёрный туман.
    [HarmonyPatch(typeof(UISpellDatabase), "GetSetSpells")]
    internal static class GetSetSpells_Worldbreak_Patch
    {
        private static void Postfix(List<ScriptableObject> __result)
        {
            try
            {
                if (__result != null && Worldbreak.Spell != null) __result.Remove(Worldbreak.Spell);
            }
            catch
            {
            }
        }
    }

    // Подсказка пишет перезарядку в секундах; у этого заклинания она в днях мира.
    [HarmonyPatch(typeof(UISpellTip), "SetSpellAttr")]
    internal static class SpellTip_Worldbreak_Patch
    {
        private static void Postfix(UISpellTip __instance, UISpellInfo spell)
        {
            try
            {
                if (!Worldbreak.Mine(spell) || __instance.cd == null) return;

                float days = Worldbreak.Days.Value;
                __instance.cd.text = days >= 1f ? $"{days:0} дн." : $"{days * 24f:0} ч";
            }
            catch
            {
            }
        }
    }
}
