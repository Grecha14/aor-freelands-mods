using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using spell;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Makes a weapon's demand cost something when it is not met.
    ///
    /// `Wield` уже выводит из вещи, чего она просит: столько-то силы или ловкости по ярусу,
    /// столько-то мастерства в своей ветке. Но просьба эта до сих пор была советом — недобор
    /// резал урон и только, и двуручник пятого яруса в руках дохляка оставался двуручником
    /// пятого яруса, просто похуже.
    ///
    /// Здесь у каждого из трёх недоборов своя цена, и цены эти разные по смыслу:
    ///
    /// Силы не хватает — вещь не поднять в движении. Взять её можно, держать можно, а
    /// драться и ходить с ней нельзя: боец стоит. Это не штраф, а положение дел — руки заняты
    /// тем, что им не по силам.
    ///
    /// Ловкости не хватает — вещь не слушается. Замах уходит мимо в четырёх случаях из пяти:
    /// не потому, что боец слаб, а потому, что не поспевает за собственным оружием.
    ///
    /// Мастерства не хватает — вещь бьёт вполсилы. Это единственное из трёх, что уже
    /// работало: `Wield.Grip` режет урон по недобору, и здесь остаётся только показать, за
    /// что режут.
    ///
    /// Показывается всё тремя значками у портрета — тем же путём, каким показывается
    /// перегруз: заготовка из `UIBuffDatabase` с очищенными приписками.
    /// </summary>
    internal static class Strain
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> Rooted;
        internal static ConfigEntry<float> Fumble;
        internal static ConfigEntry<string> Shapes;
        internal static ConfigEntry<string> Words;
        internal static ConfigEntry<bool> TouchParty;
        internal static ConfigEntry<bool> Telling;

        internal const int Strength = 0;
        internal const int Agility = 2;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Strain", "Enabled", true,
                "Let a weapon's demand mean something. Wield works out what a weapon asks for "
                + "and, until now, only cut its damage when nobody met it — so the greatsword "
                + "in weak hands was still a greatsword.");

            Rooted = config.Bind("Strain", "Rooted", true,
                "Someone short of the strength a weapon asks cannot move or fight while it is "
                + "in his hands. He can carry it and he can put it down; he cannot use it. "
                + "Off, and short strength costs only damage, as it did before.");

            Fumble = config.Bind("Strain", "Fumble", 0.8f,
                new ConfigDescription(
                    "How often a swing goes wide when the one swinging is short of the agility "
                    + "the weapon asks. Four times in five: the man is not weak, he simply "
                    + "cannot keep up with what he is holding.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Shapes = config.Bind("Strain", "Shapes",
                "strength=SummonerBurden,agility=Fatigue,mastery=MentalHealthDebuff",
                "Which existing buff each of the three borrows its icon from, written as "
                + "kind=buff id. Only the picture is borrowed: the buff is cloned and given its "
                + "own name and its own words, because otherwise a man held back by a hammer he "
                + "cannot lift would be told he is tired from summoning. These are the game's "
                + "own buffs and I cannot see what their icons look like, so they are here to "
                + "be swapped for ones that read right.");

            Words = config.Bind("Strain", "Words",
                "strength=Не по силам|Оружие в руках тяжелее, чем их владелец может нести в "
                + "движении. Держать его можно, драться и ходить с ним — нет.;"
                + "agility=Не по руке|Оружие быстрее своего хозяина. Замах уходит мимо чаще, "
                + "чем попадает: рука не поспевает за тем, что в ней.;"
                + "mastery=Не по умению|Оружием этой ступени владеть не научились. Оно бьёт "
                + "настолько, насколько его понимают, и не более того.",
                "What each of the three is called and what it says, written as "
                + "kind=name|description, separated by semicolons.");

            TouchParty = config.Bind("Strain", "TouchParty", true,
                "Hold the player's own people to this too. Off, and it binds everybody else "
                + "while the party is exempt.");

            Telling = config.Bind("Strain", "Telling", false,
                "Write down every man held back by what he is carrying.");
        }

        // ------------------------------------------------------------------ чего не хватает

        internal sealed class Short
        {
            internal bool strength;
            internal bool agility;
            internal bool mastery;

            internal bool Any() { return strength || agility || mastery; }
        }

        /// <summary>What the weapon in this man's hands asks that he cannot give.</summary>
        internal static Short Lacking(HumaniodUnit who)
        {
            Short lack = new Short();

            if (!Enabled.Value || who == null || who.Data == null) return lack;
            if (who.Data.humanAttribute == null) return lack;
            if (who.equipmentmanger == null || who.equipmentmanger.equipInfos == null) return lack;

            try
            {
                NPCSaveData mind = who.Data;

                // Обе руки: то, чем бьют, и то, чем держат. Недобор в любой из них — недобор.
                for (int slot = 0; slot < 2 && slot < who.equipmentmanger.equipInfos.Length; slot++)
                {
                    EquipInfo held = who.equipmentmanger.equipInfos[slot];
                    if (held == null || !held.IsEquiped() || held.inventory == null) continue;

                    UIWeaponInfo blade = held.inventory.itemInfo as UIWeaponInfo;
                    if (blade == null) continue;

                    Wield.Demand want = Wield.Ask(blade);
                    if (want == null) continue;

                    if (Wants(who, want, Strength)) lack.strength = true;
                    if (Wants(who, want, Agility)) lack.agility = true;
                    if (Unskilled(mind, want)) lack.mastery = true;
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог свести требование оружия: " + e);
            }

            return lack;
        }

        /// <summary>
        /// True when the demand names this attribute and the man falls short of it.
        ///
        /// Считаем по тому, чего человек стоит сейчас, а не по тому, что у него вложено.
        /// Кольцо силы, наручи, пояс, зачарованный клинок — всё это и есть та рука, которой он
        /// держит: требование спрашивает, поднимет ли он вещь, а не сколько очков он потратил.
        /// Прежде сверялись с одним вложенным, и человек в перчатках силы всё равно считался
        /// слабым для своего же меча.
        ///
        /// Порода и пол тоже в счёт: орк сильнее не снаряжением, но сильнее.
        /// </summary>
        private static bool Wants(HumaniodUnit who, Wield.Demand want, int which)
        {
            int need = 0;

            if (want.first == which) need = want.firstNeed;
            else if (want.second == which) need = want.secondNeed;

            if (need <= 0) return false;

            return Has(who, which) < need;
        }

        /// <summary>Чего человек стоит сейчас: вложенное, снаряжение и порода вместе.</summary>
        internal static int Has(HumaniodUnit who, int which)
        {
            if (who == null) return 0;

            switch (which)
            {
                case 0: return who.Strength;
                case 1: return who.Endurance;
                case 2: return who.Agility;
                case 3: return who.Precision;
                case 4: return who.Intelligence;
                case 5: return who.Willpower;
                default: return 0;
            }
        }

        private static bool Unskilled(NPCSaveData mind, Wield.Demand want)
        {
            if (want.masteryNeed <= 0 || mind.weaponMastery == null) return false;

            int slot = (int)want.branch;
            if (slot < 0 || slot >= mind.weaponMastery.Length) return false;

            // Потолок ветки засчитывается за выполненное требование: выше уже не выучить.
            int have = mind.weaponMastery[slot];
            if (have >= Wield.Ceiling.Value) return false;

            return have < want.masteryNeed;
        }

        // ------------------------------------------------------------------ последствия

        private static readonly Dictionary<string, string> shapes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static string shapesRead;

        private static string Shape(string kind)
        {
            string written = Shapes.Value ?? "";

            if (written != shapesRead)
            {
                shapes.Clear();
                foreach (string row in written.Split(','))
                {
                    int split = row.IndexOf('=');
                    if (split <= 0) continue;
                    shapes[row.Substring(0, split).Trim()] = row.Substring(split + 1).Trim();
                }
                shapesRead = written;
            }

            string id;
            return shapes.TryGetValue(kind, out id) ? id : null;
        }

        /// <summary>Lays the three marks on a man, or takes them off him.</summary>
        internal static void Settle(HumaniodUnit who)
        {
            if (!Enabled.Value || who == null || who.buffmanger == null) return;
            if (!TouchParty.Value && who.inParty) return;

            try
            {
                Short lack = Lacking(who);

                Mark(who, "strength", lack.strength);
                Mark(who, "agility", lack.agility);
                Mark(who, "mastery", lack.mastery);

                // Сила: не ходит и не бьёт. Держать можно, снять можно — воевать нельзя.
                if (Rooted.Value) Bind(who, !lack.strength);

                if (Telling.Value && lack.Any())
                {
                    ItemForgePlugin.Log.LogInfo($"«{who.Data.unitname}» не тянет оружие:"
                        + (lack.strength ? " сила" : "") + (lack.agility ? " ловкость" : "")
                        + (lack.mastery ? " мастерство" : "") + ".");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог разложить требование оружия: " + e);
            }
        }

        /// <summary>Puts the mark on or takes it off, by the shape it borrows.</summary>
        private static void Mark(HumaniodUnit who, string kind, bool on)
        {
            string id = Shape(kind);
            if (string.IsNullOrEmpty(id)) return;

            string ours = "ItemForgeStrain_" + kind;

            if (!on) { who.buffmanger.RemoveBuff(ours); return; }

            // Уже висит — второй раз не вешаем, иначе значки множатся на каждом пересчёте.
            if (who.buffmanger.ContainBuff(ours)) return;

            UIBuffInfo shape = Ours(kind, id);
            if (shape == null) return;

            BuffBase mark = new BuffBase(shape, (UnitAttribute)(object)who);

            // Бессрочно: недобор не проходит сам, а знак с чужим сроком истекал бы и вешался
            // заново — значок мигал, и подсказку над ним нельзя было дочитать.
            mark.duration = -1f;

            // Заготовка чужая, и её собственные приписки нам ни к чему: значок берём, действие
            // пишем своё. Урон за мастерство режет Wield, ходьбу за силу — Bind, промах за
            // ловкость — Dodgecheck. Самой приписке делать нечего.
            mark.addAttrs.Clear();

            who.buffmanger.AddBuff(mark);
        }

        private static readonly Dictionary<string, UIBuffInfo> mine =
            new Dictionary<string, UIBuffInfo>(StringComparer.Ordinal);

        /// <summary>
        /// Our own buff, wearing somebody else's picture.
        ///
        /// Одолжить чужую заготовку целиком нельзя: вместе со значком приходит и её название с
        /// описанием, и человек, придавленный молотом не по силам, читает, что он устал от
        /// призыва. Поэтому заготовка копируется, а название и слова пишутся свои. Копия живёт
        /// до конца игры и делается однажды.
        /// </summary>
        private static UIBuffInfo Ours(string kind, string id)
        {
            UIBuffInfo kept;
            if (mine.TryGetValue(kind, out kept) && kept != null) return kept;

            try
            {
                UIBuffInfo shape = UIBuffDatabase.Instance != null
                    ? UIBuffDatabase.Instance.GetByID(id) : null;

                if (shape == null) return null;

                UIBuffInfo copy = UnityEngine.Object.Instantiate(shape);
                UnityEngine.Object.DontDestroyOnLoad(copy);

                copy.id = "ItemForgeStrain_" + kind;
                copy.name = copy.id;

                // Бессрочно, и это написано в самом образце: срок берётся отсюда при каждом
                // наложении, и правка на готовом знаке терялась.
                copy.duration = -1f;
                copy.durationIsHour = false;
                copy.showDuration = false;

                string said = Said(kind);
                if (said != null)
                {
                    int bar = said.IndexOf('|');
                    if (bar > 0)
                    {
                        copy.buffname = said.Substring(0, bar).Trim();
                        copy.description = said.Substring(bar + 1).Trim();
                    }
                }

                mine[kind] = copy;

                ItemForgePlugin.Log.LogInfo($"Знак «{copy.buffname}» готов "
                    + $"(значок одолжен у «{id}»).");

                return copy;
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог сделать свой знак: " + e);
                return null;
            }
        }

        private static string Said(string kind)
        {
            foreach (string row in (Words.Value ?? "").Split(';'))
            {
                int split = row.IndexOf('=');
                if (split <= 0) continue;
                if (!string.Equals(row.Substring(0, split).Trim(), kind,
                        StringComparison.OrdinalIgnoreCase)) continue;

                return row.Substring(split + 1);
            }
            return null;
        }

        /// <summary>Lets a man move and fight, or does not.</summary>
        private static void Bind(HumaniodUnit who, bool may)
        {
            if (who == null || who.Data == null) return;

            CharacterSaveData data = (CharacterSaveData)(object)who.Data;

            if (data.allowmove != may) data.allowmove = may;
            if (data.allowattack != may) data.allowattack = may;
        }
    }

    // Пересчёт бойца — то же место, где Burden раскладывает груз, и по той же причине: здесь
    // уже известно и что надето, и какие у человека статы.
    [HarmonyPatch(typeof(HumaniodUnit), "WriteUnitAttribute")]
    internal static class WriteUnitAttribute_Strain_Patch
    {
        private static void Postfix(HumaniodUnit __instance)
        {
            Strain.Settle(__instance);
        }
    }

    // Замах уходит мимо, когда рука не поспевает за вещью. Считаем это уворотом цели: игра
    // умеет показывать уворот, а отдельного промаха у неё нет, и заводить свой значило бы
    // рисовать то, чего игрок никогда не увидит.
    [HarmonyPatch(typeof(UnitAttribute), "Dodgecheck")]
    internal static class Dodgecheck_Strain_Patch
    {
        private static void Postfix(Attack t_attack, ref bool __result)
        {
            if (!Strain.Enabled.Value || __result || Strain.Fumble.Value <= 0f) return;
            if (t_attack == null) return;

            try
            {
                HumaniodUnit swinging = t_attack.attacker as HumaniodUnit;
                if (swinging == null) return;

                if (!Strain.Lacking(swinging).agility) return;

                if (UnityEngine.Random.value < Strain.Fumble.Value) __result = true;
            }
            catch
            {
            }
        }
    }
}
