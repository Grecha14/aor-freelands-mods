using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;

namespace DemonLook
{
    /// <summary>
    /// Makes the lawful world hunt a demon on sight.
    ///
    /// Hostility in this game is read from a single place. UnitAttribute.IsPlayerEnemy is what
    /// the targeting, the guards and FactionManager.IsEnemy all consult, and it answers from
    /// the unit's own saved flag. That makes it the one honest place to intervene: answering
    /// the question differently is a rule, and a rule can be switched off. Writing the flag
    /// into the save instead would leave a world permanently soured on the player even after
    /// the mod was gone.
    ///
    /// The game has a second mechanism nearby — a crime tally per faction, with a threshold
    /// where guards start making arrests and a higher one where they simply attack. That is
    /// the right tool for a player who has done something, and the wrong one here: it says the
    /// demon is wanted for what he did, when the intent is that he is hunted for what he is.
    ///
    /// Only the sworn and the glory-seeking are turned: guards, who answer to towns, and the
    /// heroes the game sends wandering. Merchants, villagers and quest-givers are left alone
    /// on purpose — a world where nobody will speak to you is not a harder game, only a
    /// shorter one, and the quests have to stay reachable.
    /// </summary>
    internal static class Hostility
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> DemonOnly;
        internal static ConfigEntry<bool> HuntedByGuards;
        internal static ConfigEntry<bool> HuntedByHeroes;
        internal static ConfigEntry<string> HostileCareers;
        internal static ConfigEntry<string> Kin;
        internal static ConfigEntry<int> AfterKills;

        // Свойство опрашивается боевым ИИ помногу раз за кадр, а ответ для существа не
        // меняется. Считаем один раз на существо и дальше только достаём.
        private static readonly Dictionary<int, bool> decided = new Dictionary<int, bool>();

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Hostility", "Enabled", true,
                "Let the lawful world hunt the demon — once it has reason to. See AfterKills.");

            AfterKills = config.Bind("Hostility", "AfterKills", 500,
                new ConfigDescription(
                    "How many peaceful dead it takes before the world turns on the demon.\n\n"
                    + "Считаются только мирные: вырезанный караван, поножовщина в городе, "
                    + "убитый крестьянин. Наёмник, вышедший на тебя с оружием, не в счёт — это "
                    + "его ремесло, и мир не пугается своих же войн. Прежде счёт шёл по всем "
                    + "убитым разумным, и демон, честно отвоевавший тысячу боёв, становился "
                    + "врагом всему свету, не тронув ни одного горожанина.",
                    new AcceptableValueRange<int>(0, 1000000)));

            DemonOnly = config.Bind("Hostility", "DemonOnly", true,
                "Apply this only while the party is led by a demon.");

            HuntedByGuards = config.Bind("Hostility", "HuntedByGuards", true,
                "Town guards attack on sight instead of watching for a crime.");

            HuntedByHeroes = config.Bind("Hostility", "HuntedByHeroes", true,
                "The wandering heroes the game generates come after the demon. These are the "
                + "ones kept in its own roster of heroes, not every armed traveller.");

            Kin = config.Bind("Hostility", "Kin", "DarkMage",
                "Whose quarrel is not with the demon, by the name of their UnitInfo asset. They "
                + "are the only ones: a world that hunts him on sight would leave him without a "
                + "single door open, and the ones who called him here are the one exception that "
                + "the story already contains. Matched loosely, so all three tiers of dark mage "
                + "count as the same people.");

            HostileCareers = config.Bind("Hostility", "HostileCareers", "Guard",
                "Careers that count as sworn to the towns, separated by commas. Kept to guards "
                + "by default: adding merchants or villagers here empties the towns of anyone "
                + "left to talk to, and the quests go with them.");
        }

        // Кто во главе отряда, за кадр не меняется, а спрашивают об этом тысячи раз:
        // свойство опрашивает каждый боец про каждую цель. Считаем раз в кадр.
        private static int asked = -1;
        private static bool demonLed;

        private static bool LedByDemon()
        {
            if (asked == UnityEngine.Time.frameCount) return demonLed;
            asked = UnityEngine.Time.frameCount;

            PartyManager party = PartyManager.instance;
            HumaniodUnit leader = party != null ? party.leader : null;
            demonLed = leader != null && Racial.IsDemon(leader);

            return demonLed;
        }

        private static bool IsHero(NPCSaveData npc)
        {
            if (!HuntedByHeroes.Value || HeroUnitMaker.heros == null) return false;

            foreach (NPCSaveData hero in HeroUnitMaker.heros)
            {
                if (hero != null && hero.id == npc.id) return true;
            }
            return false;
        }

        private static bool IsSworn(NPCSaveData npc)
        {
            if (!HuntedByGuards.Value) return false;

            foreach (string entry in (HostileCareers.Value ?? "").Split(','))
            {
                string wanted = entry.Trim();
                if (wanted.Length == 0) continue;

                if (string.Equals(npc.career.ToString(), wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        // Что именно сделало игрока красным: гадать перестал, спрашиваю у полей напрямую.
        private static float looked;
        private static string said;

        internal static void Watch()
        {
            if (UnityEngine.Time.time - looked < 1f) return;
            looked = UnityEngine.Time.time;

            UnitAttribute you = gameManager.currentplayUnit;
            if (you == null || you.Data == null) return;

            bool red = you.IsPlayerEnemy;

            string now = $"фракция {you.Data.team}, помечен врагом {you.Data.isPlayerEnemy}, "
                + $"временно врагом {(you.buffmanger != null ? you.buffmanger.isTempPlayerEnemy.ToString() : "нет менеджера")}, "
                + $"наше правило {Hunts(you)}, отношение к своим "
                + $"{FactionManager.Instance.GetRelation(0, (int)you.Data.team)}, итог {red}";

            if (now == said) return;
            said = now;

            DemonLookPlugin.Log.LogInfo("Состояние игрока: " + now);
        }

        /// <summary>Whether this one counts as the demon's own.</summary>
        internal static bool IsKin(UnitAttribute unit)
        {
            if (unit == null || unit.info == null) return false;

            string asset = unit.info.name;
            if (string.IsNullOrEmpty(asset)) return false;

            foreach (string entry in (Kin.Value ?? "").Split(','))
            {
                string wanted = entry.Trim();
                if (wanted.Length == 0) continue;

                if (asset.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        /// <returns>True when this unit should count as an enemy of the player.</returns>
        internal static bool Hunts(UnitAttribute unit)
        {
            if (!Enabled.Value || unit == null || unit.Data == null) return false;

            // Пока за ним не числится счёта, мир его не узнаёт. Это не мягкость, а порядок:
            // стража, обнажающая клинок у ворот в первое же утро, превращает начало игры в
            // драку ни о чём, а весь смысл в том, что сперва он не палится. Счёт тот же, по
            // которому идёт дань, и порог тот же, что у походов, — мир замечает его однажды.
            // Один орден — исключение. Паладины поднимаются на демона с первого дня: они
            // не судят по делам, они знают породу, и знать им довольно.
            // Или его поймали над выпитым: тогда мир знает, кто он, сколько бы мирных ни было.
            if (Racial.Innocent < AfterKills.Value && !Souls.Revealed && !Paladins.Is(unit)) return false;

            try
            {
                // Своих не трогаем ни при каких настройках и ни при каких совпадениях.
                // Проверка идёт до кеша и по нескольким признакам сразу: одной проверки
                // фракции оказалось мало — игрок получил красную полосу и стал врагом сам
                // себе. Номера в реестре героев могут совпасть со своим, фракция на ранних
                // кадрах ещё не проставлена, и любой из этих случаев ловится ниже.
                // Самый надёжный признак — спросить у игры, кем сейчас играют. Проверок по
                // фракции и отряду дважды оказалось мало: во время сцены призыва демон на
                // мгновение выпадает из них обоих, и этого хватало, чтобы он стал врагом
                // самому себе — с красной полосой и навсегда, потому что ответ запоминался.
                if (gameManager.currentplayUnit == unit) return false;

                if (unit.Data.team == Faction.player) return false;
                if (unit.inParty) return false;

                PartyManager party = PartyManager.instance;
                if (party != null)
                {
                    if (party.leader == unit) return false;

                    if (party.leader != null && party.leader.Data != null
                        && party.leader.Data.id == unit.Data.id)
                    {
                        return false;
                    }

                    if (party.partyMembers != null)
                    {
                        foreach (HumaniodUnit member in party.partyMembers)
                        {
                            if (member == unit) return false;
                        }
                    }
                }

                if (DemonOnly.Value && !LedByDemon()) return false;

                int key = unit.GetInstanceID();

                bool answer;
                if (decided.TryGetValue(key, out answer)) return answer;

                NPCSaveData npc = unit.Data as NPCSaveData;
                answer = npc != null && (IsSworn(npc) || IsHero(npc));

                // В память кладём только отказ. Ошибочное «враг» однажды уже пережило все
                // последующие проверки просто потому, что было записано раньше них.
                if (!answer) decided[key] = false;

                return answer;
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogError("Проверка вражды сорвалась: " + e);
                return false;
            }
        }

    }

    [HarmonyPatch(typeof(UnitAttribute), "IsPlayerEnemy", MethodType.Getter)]
    internal static class IsPlayerEnemy_Patch
    {
        private static void Postfix(UnitAttribute __instance, ref bool __result)
        {
            if (__result) return;

            if (Hostility.Hunts(__instance)) __result = true;
        }
    }

    /// <summary>
    /// Says who is not the demon's enemy, whatever the factions think.
    ///
    /// Вражда в игре решается парой фракций, а нам нужно исключение по существу: тёмные маги
    /// стоят в своей фракции и по общему правилу дерутся с игроком, как и все. Отвечаем на
    /// сам вопрос «враг ли», а не переписываем фракции: иначе пришлось бы двигать отношения
    /// всей фракции разом, а с ней и тех, кто к делу не относится.
    /// </summary>
    [HarmonyPatch(typeof(FactionManager), "IsEnemy", new[] { typeof(UnitAttribute), typeof(UnitAttribute) })]
    internal static class IsEnemy_Kin_Patch
    {
        private static void Postfix(UnitAttribute A, UnitAttribute B, ref bool __result)
        {
            // Пока идёт сцена призыва — никто никому не враг. Паузой драку остановить не
            // вышло: движение и вражда в этой игре решаются порознь, и замороженные бойцы
            // всё равно стреляли. А вот на сам вопрос «враг ли» мы отвечаем сами.
            if (Summoning.Truce) { __result = false; return; }

            // И наоборот: маг круга и пришедший за демоном — враги, хотя игра считает их
            // никем друг другу. Маги стоят в neutralNPC, охотники в playerEnemy, отношений
            // между этими двумя сторонами нет, и оттого маги «видели и не атаковали».
            if (Summoning.Foes(A, B)) { __result = true; return; }

            // Орден и демон — враги всегда, что бы ни было между их городами. Это стоит
            // раньше проверки родства нарочно: родство прощает своим, а ордену прощать
            // нечего, он затем и есть.
            if (Crusade.Hunts(A, B)) { __result = true; return; }

            if (!__result) return;

            if (!Hostility.Enabled.Value) return;

            try
            {
                // Своим мага делает не то, что он маг, а то, что игрок — демон.
                bool oursA = A != null && A.Data != null && A.Data.team == Faction.player;
                bool oursB = B != null && B.Data != null && B.Data.team == Faction.player;

                if (!oursA && !oursB) return;

                UnitAttribute other = oursA ? B : A;
                if (!Hostility.IsKin(other)) return;

                __result = false;
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogError("Проверка родства сорвалась: " + e);
            }
        }
    }

    /// <summary>
    /// Catches whoever turns the player into an enemy, and names them.
    ///
    /// Трижды я «чинил» красноту по догадке и трижды промахнулся. Замер показал, что меняется
    /// фракция игрока, но не кем. Ставим ловушку на саму смену и просим её показать стек:
    /// после этого виновник будет назван по имени, а не выбран из подозреваемых.
    /// </summary>
    /// <summary>
    /// Sends the hunters at the circle rather than at the demon behind it.
    ///
    /// Приказ драться с магами они получали и тут же теряли: боец каждые десятые доли секунды
    /// сам ищет себе цель через «AutoFindTargetAndEngage» и отдаёт себе новую команду «kill».
    /// Выбирал он демона — тот ближе, слабее и стоит в середине. Спорить с этим приказами
    /// бесполезно, поэтому правим сам выбор: пока жив хоть один маг, охотник видит своей
    /// целью его. Это одностороннее правило — демон бьёт кого хочет, и когда круг падёт,
    /// охотники повернутся к нему сами.
    /// </summary>
    [HarmonyPatch(typeof(UnitAttribute), "FindSensedTarget")]
    internal static class FindSensedTarget_Ritual_Patch
    {
        private static void Postfix(UnitAttribute __instance, ref UnitAttribute __result)
        {
            // Только замена найденного, не подсказка. Нашёл боец пустоту — значит драться
            // ему не с кем, и не нам это менять.
            if (__result == null) return;

            UnitAttribute mage = Summoning.Guarded(__instance);
            if (mage != null) __result = mage;
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "FindEngagedTarget")]
    internal static class FindEngagedTarget_Ritual_Patch
    {
        private static void Postfix(UnitAttribute __instance, ref UnitAttribute __result)
        {
            // Только замена найденного, не подсказка. Нашёл боец пустоту — значит драться
            // ему не с кем, и не нам это менять.
            if (__result == null) return;

            UnitAttribute mage = Summoning.Guarded(__instance);
            if (mage != null) __result = mage;
        }
    }

    [HarmonyPatch(typeof(FactionManager), "IsNativeEnemy", new[] { typeof(UnitAttribute), typeof(UnitAttribute) })]
    internal static class IsNativeEnemy_Ritual_Patch
    {
        private static void Postfix(UnitAttribute A, UnitAttribute B, ref bool __result)
        {
            if (Summoning.Truce) { __result = false; return; }
            if (Summoning.Foes(A, B)) __result = true;
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "ChangeTeam")]
    internal static class ChangeTeam_Trace_Patch
    {
        private static void Prefix(UnitAttribute __instance, Faction team)
        {
            try
            {
                if (__instance == null || __instance.Data == null) return;
                if (gameManager.currentplayUnit != __instance) return;
                if (__instance.Data.team == team) return;

                DemonLookPlugin.Log.LogWarning($"ФРАКЦИЯ ИГРОКА: {__instance.Data.team} -> {team}");
                DemonLookPlugin.Log.LogWarning(new System.Diagnostics.StackTrace(1, false).ToString());
            }
            catch { }
        }
    }

    // Вторая дверь к тому же вопросу. Цвет полосы, наведение и часть решений ИИ спрашивают
    // не «враг ли A для B», а «враг ли он игроку», и это отдельный метод. Закрыв только
    // первый, я оставил тёмных магов красными при живом правиле родства.
    [HarmonyPatch(typeof(FactionManager), "IsPlayerEnemy", new[] { typeof(UnitAttribute) })]
    internal static class IsPlayerEnemy_Kin_Patch
    {
        // Имя параметра здесь именно B, как в самой игре: Harmony сопоставляет по имени,
        // и «A» вместо «B» уронило патч целиком — а с ним и весь мод, вплоть до пропавшей
        // расы в окне создания персонажа.
        private static void Postfix(UnitAttribute B, ref bool __result)
        {
            if (!__result) return;

            if (Summoning.Truce) { __result = false; return; }

            if (!Hostility.Enabled.Value) return;

            try
            {
                if (Hostility.IsKin(B)) __result = false;
            }
            catch (Exception e)
            {
                DemonLookPlugin.Log.LogError("Проверка родства сорвалась: " + e);
            }
        }
    }
}
