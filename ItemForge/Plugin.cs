using System;
using System.Collections.Generic;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using DuloGames.UI;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// First stage: read the item database and write out what an existing item actually holds.
    /// Item definitions live in the game's Unity assets rather than in its code, so the only
    /// way to learn a real item's numbers is to ask the running game. Cloning comes next, and
    /// it needs these numbers to be worth anything.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class ItemForgePlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "aor.itemforge";
        public const string PluginName = "Item Forge";
        public const string PluginVersion = "2.0.0";

        internal static ManualLogSource Log;

        private static ConfigEntry<string> Filter;
        private static ConfigEntry<string> OnlyQuality;
        private static ConfigEntry<KeyCode> DumpKey;
        private static ConfigEntry<KeyCode> InspectKey;
        private static ConfigEntry<int> CompareWith;
        private static ConfigEntry<string> InspectName;

        private void Awake()
        {
            Log = Logger;

            Filter = Config.Bind("Dump", "Filter", "",
                "Only items whose internal name contains this are written out. Empty means no name filter.");

            OnlyQuality = Config.Bind("Dump", "OnlyQuality", "",
                "Only items of this quality are written out. Empty means every quality. Names are the "
                + "game's own, such as Legendary or Epic; the log prints what it finds.");

            DumpKey = Config.Bind("Dump", "DumpKey", KeyCode.Insert,
                "Press this in game to write the matching items and sets to the log.");

            InspectKey = Config.Bind("Dump", "InspectKey", KeyCode.Home,
                "Press this in game to write the equipped weapon's object tree to the log: which parts "
                + "it is built from, their meshes, sizes, materials and particle systems.");

            CompareWith = Config.Bind("Dump", "CompareWith", 829,
                new ConfigDescription(
                    "Item to compare the forged blade against, field by field. 829 is the Bane club, "
                    + "the weapon whose glow was borrowed.",
                    new AcceptableValueRange<int>(0, 999999)));

            InspectName = Config.Bind("Dump", "InspectName", "KingOfHellBodyAura",
                "Object in the scene whose whole tree the inspect key writes out: meshes, "
                + "materials and the shaders behind them. The effect sweep only sees particles, "
                + "so a creature made dark by its own skin needs this instead.");

            Forge.Bind(Config);
            Loot.Bind(Config);
            Repair.Bind(Config);
            Pierce.Bind(Config);
            Tatter.Bind(Config);
            Meet.Bind(Config);
            Gait.Bind(Config);
            Vigil.Bind(Config);
            Spells.Bind(Config);
            Steel.Bind(Config);
            Arcane.Bind(Config);
            Taint.Bind(Config);
            Stride.Bind(Config);
            Lingers.Bind(Config);
            Lairs.Bind(Config);
            Colosseum.Bind(Config);
            Glyphs.Bind(Config);
            Ladder.Bind(Config);
            Ribbon.Bind(Config);
            Blows.Bind(Config);
            Shade.Bind(Config);
            Hunt.Bind(Config);
            Sidestep.Bind(Config);
            Legend.Bind(Config);
            Bulwark.Bind(Config);
            Duel.Bind(Config);
            Dread.Bind(Config);
            Calling.Bind(Config);
            Ward.Bind(Config);
            Mastery.Bind(Config);
            Keeping.Bind(Config);
            Burden.Bind(Config);
            Breach.Bind(Config);
            Garb.Bind(Config);
            Demand.Bind(Config);
            Gore.Bind(Config);
            Edge.Bind(Config);
            Hone.Bind(Config);
            Blend.Bind(Config);
            Naming.Bind(Config);
            Hints.Bind(Config);
            Toll.Bind(Config);
            Knit.Bind(Config);
            Wits.Bind(Config);
            Brawn.Bind(Config);
            Sinew.Bind(Config);
            Limb.Bind(Config);
            Vigour.Bind(Config);
            Mass.Bind(Config);
            Break.Bind(Config);
            Kit.Bind(Config);
            Might.Bind(Config);
            Lore.Bind(Config);
            Pace.Bind(Config);
            Blood.Bind(Config);
            Iron.Bind(Config);
            Muster.Bind(Config);
            Birth.Bind(Config);
            Purse.Bind(Config);
            Sparks.Bind(Config);
            Wounds.Bind(Config);
            Sever.Bind(Config);
            Nimble.Bind(Config);
            Brew.Bind(Config);
            Smithy.Bind(Config);
            Temper.Bind(Config);
            Menagerie.Bind(Config);
            Gauge.Bind(Config);
            Rarity.Bind(Config);
            Chronicle.Bind(Config);
            Needs.Bind(Config);
            Workshop.Bind(Config);
            Craft.Bind(Config);
            Chancery.Bind(Config);
            Wild.Bind(Config);
            Beastly.Bind(Config);
            Steed.Bind(Config);
            Enchant.Bind(Config);
            Watch.Bind(Config);
            Anvil.Bind(Config);
            Strain.Bind(Config);
            Charm.Bind(Config);
            Reading.Bind(Config);
            Ritual.Bind(Config);
            Tongue.Bind(Config);
            Counter.Bind(Config);
            Smithing.Bind(Config);
            Healing.Bind(Config);
            Meals.Bind(Config);
            Stance.Bind(Config);
            Localize.Bind(Config);
            SetRules.Bind(Config);
            SetCombat.Bind(Config);
            SetKills.Bind(Config);
            Aura.Bind(Config);
            WeaponVfx.Bind(Config);
            Wield.Bind(Config);
            Grade.Bind(Config);
            Heft.Bind(Config);
            Ascent.Bind(Config);
            Bestiary.Bind(Config);
            Draught.Bind(Config);
            Camp.Bind(Config);
            Schooling.Bind(Config);
            Traits.Bind(Config);
            Renown.Bind(Config);
            Larder.Bind(Config);
            Rollback.Bind(Config);
            Spirit.Bind(Config);
            Mending.Bind(Config);
            Harvest.Bind(Config);
            Parley.Bind(Config);
            Shift.Bind(Config);
            Craving.Bind(Config);
            Anatomy.Bind(Config);
            Volley.Bind(Config);
            Scout.Bind(Config);
            Armoury.Bind(Config);
            Keenness.Bind(Config);
            Reach.Bind(Config);
            Practice.Bind(Config);
            Aim.Bind(Config);
            Dummy.Bind(Config);
            Lethal.Bind(Config);
            Taming.Bind(Config);
            Anodyne.Bind(Config);

            ApplyRecipe();
            ApplyPatches();

            Log.LogInfo($"Item Forge v{PluginVersion} loaded. Press {DumpKey.Value} in game to dump items matching '{Filter.Value}'.");
        }


        // Номер рецепта. Растёт всякий раз, когда меняются задуманные значения набора.
        private const int RecipeVersion = 94;

        /// <summary>
        /// Brings the settings file back in line with what this build intends.
        ///
        /// BepInEx writes the file once and then leaves it alone: a value stored there by an
        /// older build outlives any change to the default in code. That is the right behaviour
        /// for settings a player has chosen, and quietly wrong for a recipe that is still being
        /// worked out — several changes were made, reported and never reached the game, because
        /// the file kept answering with the old number. So the build carries a recipe number,
        /// and when it moves the stored values go back to the defaults this build declares.
        /// Anything tuned by hand afterwards stands until the number moves again.
        /// </summary>
        private void ApplyRecipe()
        {
            ConfigEntry<int> stored = Config.Bind("Meta", "RecipeVersion", 0,
                "Recipe this settings file was last brought in line with. Raising it in the mod "
                + "resets every other setting here to the value the mod ships with; lower it by "
                + "hand to force that reset on the next start.");

            if (stored.Value >= RecipeVersion) return;

            int reset = 0;
            foreach (ConfigDefinition key in new List<ConfigDefinition>(Config.Keys))
            {
                if (key.Section == "Meta") continue;

                ConfigEntryBase entry = Config[key];
                if (entry == null || entry.DefaultValue == null) continue;
                if (Equals(entry.BoxedValue, entry.DefaultValue)) continue;

                Log.LogInfo($"Настройка {key.Section}.{key.Key}: {entry.BoxedValue} -> {entry.DefaultValue}.");
                entry.BoxedValue = entry.DefaultValue;
                reset++;
            }

            stored.Value = RecipeVersion;
            Config.Save();

            Log.LogInfo($"Рецепт обновлён до версии {RecipeVersion}, настроек возвращено к умолчанию: {reset}.");
        }

        // Патчи ставятся поштучно, а не разом. PatchAll обрушивается целиком от одной
        // ошибки: так уже случилось из-за неверно названного параметра, и вместе с одним
        // патчем отвалились все прочие. Поштучно сбойный просто пропускается.
        private void ApplyPatches()
        {
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(PluginGuid);
            int ok = 0, failed = 0;

            foreach (System.Type type in System.Reflection.Assembly.GetExecutingAssembly().GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), true).Length == 0) continue;

                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                    ok++;
                }
                catch (System.Exception e)
                {
                    failed++;
                    Log.LogError($"Патч {type.Name} не встал, остальные продолжают работать: {e.Message}");
                }
            }

            if (failed == 0) Log.LogInfo($"Патчей поставлено: {ok}.");
            else Log.LogWarning($"Патчей поставлено: {ok}, не встало: {failed}.");
        }

        private void Update()
        {
            // Перевод ложится в таблицы, а таблицы собираются не разом: общая готова до
            // первого кадра, а вещи, имена и умения подтягиваются позже и по-разному.
            // Спрашивать о готовности некого, оттого просто пробуем свои — раз в секунду,
            // покуда не лягут все четыре. Потом это не стоит и сравнения строк.
            Tongue.Nudge(Time.unscaledDeltaTime);
            Sever.Tick();
            Sparks.Tick();
            Taint.Tick();
            Lingers.Tick();
            Shade.Tick();
            Sidestep.Tick();
            Bulwark.Tick();
            Calling.Tick();

            Steed.Tick();
            Taming.Tick();
            Anodyne.Tick();

            // Комплект создаётся сам при загрузке, а не по клавише: теперь он добывается в
            // мире, и описания вещей должны существовать до того, как кто-то из них выпадет.
            //
            // Но только когда мир действительно есть. В меню создания персонажа база
            // предметов ещё не поднята, и обращение к ней там не возвращает пустоту, а
            // бросает исключение — отчего в лог сыпалась ошибка каждый кадр. Наличие
            // предводителя отряда и есть признак, что игра началась.
            if (PartyManager.instance != null && PartyManager.instance.leader != null)
            {
                Forge.Build();
                Glyphs.Paint();
                Repair.SealKits();
                Ward.Enchant();
                Mastery.Raise();
                Burden.Roster();
                Rarity.Raise();
                Legend.Catalogue();
                Tatter.Catalogue();
                Spells.Catalogue();
                Steel.Catalogue();
                Arcane.Catalogue();
                Burden.Weigh();
                Anodyne.Weigh();
                Needs.Write();
                Needs.Pace();
                Craft.Shape();
                Wield.Mark();
                Wield.Bless();
                Garb.Sort();
                Garb.Round();
                Demand.Lift();
                Hone.Sharpen();
                Heft.Toils();
                Taming.Census();
                Grade.Engrave();
                Smithy.Forge();
                Ascent.Lift();
                Bestiary.Raise();
                Traits.Write();
            }

            // Подмена чисел в подсказке живёт один вызов отрисовки. Если тот сорвётся
            // посередине, список останется подменённым — здесь он снимается в любом случае,
            // задолго до того, как что-либо попадёт в сохранение.
            Grade.Undress();
            Arcane.Undress();

            Aura.Tick();
            Chronicle.Settle();
            Renown.Settle();
            Larder.Tick();
            Mending.Settle();
            Break.Settle();
            Craving.Tick();
            Dummy.Tend();

            if (Input.GetKeyDown(Forge.GiveKey.Value)) SetGift.Hand();
            else if (Armoury.Key.Value != KeyCode.None && Input.GetKeyDown(Armoury.Key.Value)) Armoury.Rack();
            else if (Input.GetKeyDown(Dummy.Key.Value)) Dummy.Call();
            else if (Input.GetKeyDown(Reach.Key.Value)) Reach.Sweep();
            else if (Input.GetKeyDown(InspectKey.Value)) { Inspect.EquippedWeapon(); Inspect.SceneEffects(); Inspect.NamedObject(InspectName.Value); Aura.ReportWorn(); }
            else if (Input.GetKeyDown(DumpKey.Value)) Dump();
            else if (Input.GetKeyDown(Burden.BreakdownKey.Value)) Burden.Nearby();
            else if (Input.GetKeyDown(Menagerie.Key.Value)) Menagerie.Write();
        }

        private static void Dump()
        {
            try
            {
                UIItemDatabase db = UIItemDatabase.Instance;
                if (db == null || db.items == null)
                {
                    Log.LogWarning("The item database is not ready yet, try again once a save is loaded.");
                    return;
                }

                Collected.Clear();

                string filter = Filter.Value ?? "";
                string quality = OnlyQuality.Value ?? "";
                int shown = 0;

                Say($"--- выгрузка: имя '{filter}', качество '{quality}', всего в базе {db.items.Length} ---");

                foreach (UIItemInfo item in db.items)
                {
                    if (item == null) continue;

                    if (filter.Length > 0
                        && item.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
                        && item.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    if (quality.Length > 0
                        && item.Quality.ToString().IndexOf(quality, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    shown++;
                    Say(Describe(item));
                }

                Say($"--- найдено {shown} ---");
                DumpUniqueEffects(db, quality);
                DumpCompare(db);
                DumpSpellEffects();
                DumpSets(db, filter);
                DumpTraits();
                WriteFile();
            }
            catch (Exception e)
            {
                Log.LogError("Dump failed: " + e);
            }
        }


        // Черты живут в отдельной базе талантов, и их описания — единственный способ
        // опознать нужную по эффекту, а не по названию.
        /// <summary>
        /// Lists the items that actually carry a unique property, with the text the tooltip
        /// shows for it.
        ///
        /// Worth its own section because the property is not where it looks like it should be:
        /// the effect is a talent on bindTalent, but the wording the player reads comes from
        /// readContent, a field that for books holds the book. An item can have one without the
        /// other, and that is exactly what made a forged piece wear one effect and describe a
        /// different one — so both are printed side by side here.
        /// </summary>
        private static void DumpUniqueEffects(UIItemDatabase db, string quality)
        {
            Say($"--- уникальные свойства, качество '{quality}' ---");

            int found = 0;
            foreach (UIItemInfo item in db.items)
            {
                UIEquipmentInfo eq = item as UIEquipmentInfo;
                if (eq == null) continue;

                if (quality.Length > 0
                    && eq.Quality.ToString().IndexOf(quality, StringComparison.OrdinalIgnoreCase) < 0) continue;

                bool hasText = !string.IsNullOrEmpty(eq.readContent);
                if (eq.bindTalent == null && eq.spell == null && eq.learnTrait == null && !hasText) continue;

                found++;

                StringBuilder sb = new StringBuilder();
                sb.Append($"[{eq.ID}] {eq.Name} (asset {eq.name}) | слот {eq.EquipType}");
                if (eq.bindTalent != null) sb.Append($" | талант {eq.bindTalent.name} «{eq.bindTalent.Name}»");
                if (eq.spell != null) sb.Append($" | заклинание {eq.spell.name}");
                if (eq.learnTrait != null) sb.Append($" | черта {eq.learnTrait.name}");
                Say(sb.ToString());

                if (hasText) Say("      НАВЫК: " + OneLine(Localized(eq.readContent), 400));
            }

            Say($"--- с уникальными свойствами: {found} ---");
        }

        /// <summary>The player-facing wording, or the raw key when the table has no entry.</summary>
        private static string Localized(string key)
        {
            try
            {
                string text = gameManager.LocalizedItemString(key);
                return string.IsNullOrEmpty(text) ? key : text;
            }
            catch { return key; }
        }

        /// <summary>Folds a paragraph onto one line so the dump stays one record per line.</summary>
        private static string OneLine(string text, int limit)
        {
            if (text == null) return "";
            text = text.Replace((char)13, (char)32).Replace((char)10, (char)32).Trim();
            return text.Length > limit ? text.Substring(0, limit) + "…" : text;
        }

        /// <summary>
        /// Lists the spells that carry a visual effect, and what that effect is made of.
        ///
        /// A spell's behaviour prefab is an ordinary reference, not an addressable one, so its
        /// contents can be read here and now. That makes spells the best place to look for an
        /// effect shaped to a person rather than to a blade — a ward or a blink is built to wrap
        /// a body, which is exactly what a glowing suit of armour needs.
        /// </summary>
        private static void DumpSpellEffects()
        {
            UISpellDatabase sdb = UISpellDatabase.Instance;
            if (sdb == null || sdb.spells == null) { Say("Базы заклинаний нет."); return; }

            Say($"--- заклинания с эффектами, всего {sdb.spells.Length} ---");

            int found = 0;
            foreach (UISpellInfo spell in sdb.spells)
            {
                if (spell == null || spell.behaviorPrefab == null) continue;

                ParticleSystem[] systems = spell.behaviorPrefab.GetComponentsInChildren<ParticleSystem>(true);
                if (systems.Length == 0) continue;

                found++;

                int particles = 0;
                List<string> parts = new List<string>();
                foreach (ParticleSystem system in systems)
                {
                    ParticleSystem.MainModule main = system.main;
                    particles += main.maxParticles;
                    if (parts.Count < 6) parts.Add(system.name);
                }

                Say($"[{spell.ID}] {spell.Name} | префаб {spell.behaviorPrefab.name} | систем {systems.Length}"
                    + $", частиц до {particles} | {string.Join(", ", parts.ToArray())}");
            }

            Say($"--- с эффектами: {found} ---");
        }

        /// <summary>
        /// Compares the borrowed weapon with ours field by field and writes down what differs.
        ///
        /// Guessing which single setting makes one weapon glow and the other not has cost several
        /// rounds already, so this asks the objects themselves instead: every field either class
        /// declares is read off both and the matching ones thrown away. What is left is the whole
        /// of the difference, and the ones we put there on purpose are labelled as such, so the
        /// unlabelled remainder is the part nobody has explained yet.
        /// </summary>
        private static void DumpCompare(UIItemDatabase db)
        {
            UIWeaponInfo donor = db.GetByID(CompareWith.Value) as UIWeaponInfo;
            UIWeaponInfo ours = Forge.Blade();

            if (donor == null || ours == null)
            {
                Say($"--- сравнение: нечего сличать (донор {(donor == null ? "не найден" : "есть")}, "
                    + $"наш клинок {(ours == null ? "не выкован" : "есть")}) ---");
                return;
            }

            // То, что мы изменили сами: имя, описание, цена и прочее. Их в отчёте помечаем,
            // чтобы взгляд не цеплялся за ожидаемое.
            HashSet<string> known = new HashSet<string>
            {
                "ID", "Name", "Description", "name", "readContent", "Icon", "value", "weight",
                "durability", "isUnique", "race", "set", "bindTalent", "spell", "learnTrait",
                "addAttrs", "Quality", "tier", "dropRate", "WeaponType", "WeaponType_Secondary",
                "AnimationType", "AnimationSubType", "weaponClass", "spCost", "AttackAngle",
                "AttackSpeed", "BlockAngle", "BlockRate", "damage", "resistPen", "PrefabReference",
            };

            Say($"--- сравнение [{donor.ID}] «{donor.Name}» и [{ours.ID}] «{ours.Name}» ---");

            int same = 0, differ = 0, hidden = 0;

            foreach (System.Reflection.FieldInfo field in typeof(UIWeaponInfo).GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance))
            {
                object a, b;
                try { a = field.GetValue(donor); b = field.GetValue(ours); }
                catch { continue; }

                string left = Show(a);
                string right = Show(b);

                if (left == right) { same++; continue; }

                differ++;
                if (known.Contains(field.Name)) { hidden++; continue; }

                Say($"  ОТЛИЧИЕ {field.Name}: у донора {left} | у нас {right}");
            }

            Say($"--- совпало {same}, отличается {differ}, из них известных и скрытых {hidden} ---");
        }

        private static string Show(object value)
        {
            if (value == null) return "нет";

            UnityEngine.Object asset = value as UnityEngine.Object;
            if (asset != null) return asset.name;

            System.Collections.IEnumerable list = value as System.Collections.IEnumerable;
            if (list != null && !(value is string))
            {
                List<string> parts = new List<string>();
                foreach (object item in list)
                {
                    if (parts.Count >= 12) { parts.Add("…"); break; }
                    parts.Add(Show(item));
                }
                return "[" + string.Join(",", parts.ToArray()) + "]";
            }

            return value.ToString();
        }

        private static void DumpTraits()
        {
            UITalentDatabase tdb = UITalentDatabase.Instance;
            if (tdb == null || tdb.traits == null) { Say("Черт в базе нет."); return; }

            Say($"--- черты, всего {tdb.traits.Length} ---");
            foreach (UITalentInfo trait in tdb.traits)
            {
                if (trait == null) continue;
                string desc = (trait.description ?? "").Replace((char)13, (char)32).Replace((char)10, (char)32);
                if (desc.Length > 200) desc = desc.Substring(0, 200) + "…";
                string bonuses = "";
                if (trait.addAttrs != null && trait.addAttrs.Count > 0)
                {
                    List<string> parts = new List<string>();
                    foreach (AddonAttributes a in trait.addAttrs)
                    {
                        if (a != null) parts.Add($"{a.type} {a.value}");
                    }
                    bonuses = " | бонусы: " + string.Join(", ", parts.ToArray());
                }
                // Ссылка на префаб эффекта важнее радиуса: именно её можно позаимствовать,
                // чтобы повесить готовую ауру на свою вещь, ничего не упаковывая.
                bool hasEffect = trait.aurasEffectReference != null
                                 && trait.aurasEffectReference.RuntimeKeyIsValid();
                string aura = (trait.aurasRadious > 0f || hasEffect)
                    ? $" | АУРА радиус {trait.aurasRadious}, на {trait.aurasTarget}"
                      + (hasEffect ? $", эффект {trait.aurasEffectReference.RuntimeKey}" : ", без эффекта")
                    : "";
                string buffs = (trait.addtionBuffs != null && trait.addtionBuffs.Length > 0) ? $" | баффов {trait.addtionBuffs.Length}" : "";
                string spell = trait.connectedSpell != null ? $" | заклинание {trait.connectedSpell.Name}" : "";
                Say($"[{trait.ID}] {trait.Name} ({trait.name}) | тип {trait.traitType}{bonuses}{aura}{buffs}{spell} | {desc}");
            }
        }

        private static string Describe(UIItemInfo item)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"[{item.ID}] {item.Name} (asset {item.name})");
            sb.Append($" | тип {item.itemType}, качество {item.Quality}, ступень {item.tier}");
            sb.Append($", цена {item.value}, вес {item.weight}, прочность {item.durability}");
            sb.Append($", уровень {item.RequiredLevel}, шанс выпадения {item.dropRate}");
            if (item.isUnique) sb.Append(", уникальный");

            UIEquipmentInfo eq = item as UIEquipmentInfo;
            if (eq != null)
            {
                sb.Append($" | слот {eq.EquipType}");
                if (eq.race != UnitRace.none) sb.Append($", раса {eq.race}");
                if (eq.gender != UnitGender.none) sb.Append($", пол {eq.gender}");
                if (eq.set != null) sb.Append($", комплект {eq.set.Name}");
                if (eq.bindTalent != null) sb.Append($", талант {eq.bindTalent.Name}");

                if (eq.addAttrs != null && eq.addAttrs.Count > 0)
                {
                    sb.Append(" | бонусы: ");
                    for (int i = 0; i < eq.addAttrs.Count; i++)
                    {
                        AddonAttributes a = eq.addAttrs[i];
                        if (i > 0) sb.Append(", ");
                        sb.Append($"{a.type} {a.value}");
                        if (a.levelAlter != 0f) sb.Append($" (+{a.levelAlter}/ур)");
                    }
                }
                else
                {
                    sb.Append(" | бонусов нет");
                }

                Armour(sb, eq as UIArmorInfo);
                Blade(sb, eq as UIWeaponInfo);
            }
            return sb.ToString();
        }

        // Названия типов урона в том порядке, в каком игра держит их в своих массивах.
        private static readonly string[] Kinds =
        {
            "рубящее", "дробящее", "колющее", "огонь", "холод",
            "молния", "яд", "свет", "тьма"
        };

        // Сопротивления по типам — единственное, чем одна кираса отличается от другой, и
        // ровно то, чего в отчёте не было. Без них считать пробитие не по чему.
        private static void Armour(StringBuilder sb, UIArmorInfo coat)
        {
            if (coat == null) return;

            sb.Append($" | броня {coat.armourType}/{coat.armourClass}");

            if (coat.damageDR == null) return;

            List<string> held = new List<string>();
            for (int i = 0; i < coat.damageDR.Length; i++)
            {
                if (coat.damageDR[i] == 0f) continue;
                held.Add((i < Kinds.Length ? Kinds[i] : i.ToString()) + " " + coat.damageDR[i]);
            }

            sb.Append(held.Count > 0
                ? " | держит: " + string.Join(", ", held.ToArray())
                : " | не держит ничего");
        }

        private static void Blade(StringBuilder sb, UIWeaponInfo blade)
        {
            if (blade == null) return;

            sb.Append($" | оружие {blade.WeaponType}/{blade.weaponClass}");

            if (blade.damage != null)
            {
                List<string> hits = new List<string>();
                foreach (KeyValuePair<DamageType, Damage> one in blade.damage)
                {
                    if (one.Value == null) continue;
                    hits.Add($"{one.Key} {one.Value.minDamage}-{one.Value.maxDamage}");
                }

                if (hits.Count > 0) sb.Append(" | бьёт: " + string.Join(", ", hits.ToArray()));
            }

            sb.Append($" | скорость {blade.AttackSpeed}, дальность {blade.AttackRange}");
            sb.Append($", сила {blade.Force}, сила/ловкость {blade.strFactor}/{blade.agiFactor}");

            if (blade.resistPen == null) return;

            List<string> through = new List<string>();
            for (int i = 0; i < blade.resistPen.Length; i++)
            {
                if (blade.resistPen[i] == 0f) continue;
                through.Add((i < Kinds.Length ? Kinds[i] : i.ToString()) + " " + blade.resistPen[i]);
            }

            if (through.Count > 0) sb.Append(" | пробивает: " + string.Join(", ", through.ToArray()));
        }

        private static void DumpSets(UIItemDatabase db, string filter)
        {
            if (db.sets == null) { Say("Комплектов в базе нет."); return; }

            Say($"--- комплекты, всего {db.sets.Length} ---");

            foreach (EquipmentSet set in db.sets)
            {
                if (set == null) continue;
                if (filter.Length > 0 && set.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                List<string> parts = new List<string>();
                if (set.parts != null)
                {
                    foreach (UIEquipmentInfo p in set.parts)
                    {
                        if (p != null) parts.Add($"{p.Name}({p.EquipType})");
                    }
                }
                Say($"Комплект {set.Name}, качество {set.quality}, частей {parts.Count}: {string.Join(", ", parts.ToArray())}");

                if (set.bonus == null) continue;
                for (int i = 0; i < set.bonus.Count; i++)
                {
                    Say($"    бонус {i}: {DescribeBonus(set.bonus[i])}");
                }
            }
        }

        // Выгрузка копится в памяти и пишется файлом рядом с плагином: журнал BepInEx
        // обрезается при каждом запуске, и собранное по крупицам терялось.
        private static readonly List<string> Collected = new List<string>();

        private static void Say(string line)
        {
            Log.LogInfo(line);
            Collected.Add(line);
        }

        private static void WriteFile()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                string path = System.IO.Path.Combine(dir, "items_dump.txt");

                System.IO.File.WriteAllLines(path, Collected.ToArray());
                Log.LogInfo($"Выгрузка записана в {path}, строк {Collected.Count}.");
            }
            catch (Exception e)
            {
                Log.LogError("Не смог записать выгрузку: " + e);
            }
        }

        private static string DescribeBonus(SetBonus b)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"частей {b.num}");
            if (b.bonus != null)
            {
                foreach (AddonAttributes a in b.bonus)
                {
                    if (a != null) sb.Append($" | {a.type} {a.value}");
                }
            }
            return sb.ToString();
        }
    }
}





