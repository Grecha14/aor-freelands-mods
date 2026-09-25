using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;

namespace ItemForge
{
    /// <summary>
    /// A tamed beast that is kept rather than merely owned.
    ///
    /// Приручение в игре уже есть, и работает оно так: заклинание с поведением
    /// <c>BehavCapture</c> считает шанс — сорок плюс десять за ступень, помноженные на
    /// недостающее здоровье цели, плюс разница уровней, — и при удаче делает
    /// <c>AddWarpet(зверь)</c>. Дальше зверь ходит за хозяином, слушает «следовать» и
    /// «стоять» и переживает сохранение. То есть машинерия та же самая, на которой стоит
    /// купленная лошадь, и строить её заново незачем.
    ///
    /// Не хватало трёх вещей, и все три здесь.
    ///
    /// Первое — уровень. Питомца игра равняет по хозяину (<c>MatchSummonLevel</c>), и медведь
    /// четырнадцатого уровня, взятый героем восьмидесятого, становился восьмидесятым. Ловить
    /// медведя послабее тогда не имело смысла вовсе: любой становился тем же самым. Теперь
    /// зверь остаётся тем, кого поймали, и растёт своим трудом.
    ///
    /// Второе — место. Зверь занимает место в отряде: предел отряда уменьшается на число
    /// прирученных, и медведь идёт вместо человека, а не сверх него. Брать зверя, когда мест
    /// нет, нельзя — об этом говорится вслух.
    ///
    /// Третье — он спутник, а не украшение. Своя сумка (она есть у всякого существа, на ней же
    /// стоит вьюк лошади), своё поведение в бою — <c>combatStyle</c> и <c>engageStyle</c>
    /// лежат в записи любого существа, — и свой рост за то, в чём он участвовал.
    /// </summary>
    internal static class Taming
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> KeepLevel;
        internal static ConfigEntry<bool> Mortal;
        internal static ConfigEntry<bool> PartySlot;
        internal static ConfigEntry<int> Many;
        internal static ConfigEntry<float> Grows;
        internal static ConfigEntry<int> Ceiling;
        internal static ConfigEntry<float> ExpShare;
        internal static ConfigEntry<KeyCode> GiftKey;
        internal static ConfigEntry<double> Gift;
        internal static ConfigEntry<bool> Orders;
        internal static ConfigEntry<KeyCode> OrderKey;
        internal static ConfigEntry<bool> Carries;
        internal static ConfigEntry<bool> Evolve;
        internal static ConfigEntry<string> Ladders;
        internal static ConfigEntry<int> EvolveEvery;
        internal static ConfigEntry<bool> Sizes;
        internal static ConfigEntry<float> Scale;
        internal static ConfigEntry<bool> Panel;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Taming", "Enabled", true,
                "Let a tamed beast be a companion rather than a trophy: its own level, its own "
                + "bag, its own orders in a fight, and a place in the party it has to earn.");

            KeepLevel = config.Bind("Taming", "KeepLevel", true,
                "Let a tamed beast keep the level it was caught at. The game levels every pet "
                + "to its owner, which makes the catch meaningless — a cub and an old bear come "
                + "out the same creature. Kept as caught, the hunt is a hunt: a strong beast is "
                + "worth stalking and a weak one is worth raising.");

            Mortal = config.Bind("Taming", "Mortal", true,
                "Let it die. The game marks every pet unkillable, which is right for a summoned "
                + "spirit and wrong for an animal somebody caught and fed.");

            PartySlot = config.Bind("Taming", "PartySlot", true,
                "Let a beast take a place in the party, not a place beside it. The limit falls "
                + "by one for every tamed beast the company keeps, so a bear walks instead of a "
                + "man rather than in addition to him — and when the party is full, there is "
                + "nobody to take the beast. The caravan's own room is reckoned from the same "
                + "number, so a beast costs a place there too.");

            Many = config.Bind("Taming", "Many", 1,
                new ConfigDescription(
                    "How many tamed beasts the whole company may keep at once. One: a beast is "
                    + "a companion and not a kennel, and the game gives each person a single pet "
                    + "slot of its own — without this the party could walk four wolves and eat "
                    + "four of its own places doing it.",
                    new AcceptableValueRange<int>(1, 8)));

            Grows = config.Bind("Taming", "Grows", 150f,
                new ConfigDescription(
                    "How much work a beast must do to gain a level, multiplied by the level it "
                    + "already has. Work is what it landed its teeth on: every blow that tells "
                    + "counts for the level of the one it struck. A beast of ten hunting things "
                    + "of twenty comes up in about seventy-five blows; hunting rats it never "
                    + "comes up at all, and that is the point.",
                    new AcceptableValueRange<float>(1f, 100000f)));

            Ceiling = config.Bind("Taming", "Ceiling", 9999,
                new ConfigDescription(
                    "The level a tamed beast stops growing at. Nine hundred and ninety-nine is "
                    + "no ceiling at all, and deliberately: a beast that has hunted beside "
                    + "somebody for a hundred levels has earned every one of them, and the "
                    + "crown of the wolf ladder — the werewolf — stands far enough up it that "
                    + "a ceiling of sixty would have made the last two steps decoration.",
                    new AcceptableValueRange<int>(1, 9999)));

            ExpShare = config.Bind("Taming", "ExpShare", 1f,
                new ConfigDescription(
                    "What share of a person's price a beast's next point of an attribute costs. "
                    + "One means exactly the same: the game asks two hundred experience for the "
                    + "first point and a ninth more for every point already there, and the beast "
                    + "is asked the same. It earns it the same way too — six tenths of what it "
                    + "kills is worth, which is the number a person is given for the same body. "
                    + "Levels are not handed to a beast and give it nothing: its level is what "
                    + "its six numbers add up to, exactly as a person's is.",
                    new AcceptableValueRange<float>(0.1f, 10f)));

            GiftKey = config.Bind("Taming", "GiftKey", KeyCode.F11,
                "A key that hands experience to the whole company and to every beast it keeps. "
                + "For trying things out: growing a beast honestly takes a hundred kills, and "
                + "a rule nobody can see working is a rule nobody can judge. Set it to None "
                + "when the trying is over.");

            Gift = config.Bind("Taming", "Gift", 100000000000.0,
                new ConfigDescription(
                    "How much that key hands out. People take it through the game's own door, "
                    + "which counts in whole numbers and stops at two thousand million, so they "
                    + "get that much; a beast's ledger is ours and takes the whole sum.",
                    new AcceptableValueRange<double>(1.0, 1e15)));

            Orders = config.Bind("Taming", "Orders", true,
                "Let a beast be pointed at the ground and at a throat, the way the one you are "
                + "playing is. The game reads a click and hands the order to whoever is being "
                + "played and to nobody else — a beast cannot be played, because everything "
                + "about a played character, from the equipment panel to dialogue, is written "
                + "for a person. So the click is intercepted instead: held key and a click, and "
                + "the order goes to the beast rather than to you.");

            OrderKey = config.Bind("Taming", "OrderKey", KeyCode.LeftControl,
                "Which key to hold while clicking, to send the order to the beast instead.\n\n"
                + "Был левый Alt — и был занят: игра держит на нём подсветку людей на поле, и "
                + "две работы на одной клавише мешали друг другу. Теперь левый Ctrl. Если и он "
                + "у вас занят, сюда годится любая клавиша: пишется именем, как в Unity — "
                + "«LeftControl», «CapsLock», «BackQuote», «Z».");

            Carries = config.Bind("Taming", "Carries", false,
                "Let a beast carry only what a beast its size can, and stand where it is when "
                + "it cannot. The same reckoning as a man's — strength times three — so a dog "
                + "takes a loaf and a bow and a bear takes a suit of armour. The horse keeps "
                + "its own, wider allowance: it was bought to carry.");

            Evolve = config.Bind("Taming", "Evolve", true,
                "Let a beast that has grown enough become the next thing of its kind. The game "
                + "ships whole families — a wolf, a white wolf, a pack king, a barghest, a "
                + "werewolf — and they are the same animal at different ages as far as anybody "
                + "looking at them is concerned.");

            Ladders = config.Bind("Taming", "Ladders",
                "WildWolf,WhiteWolf,Wolf King,Barghest,Werewolf;"
                + "Spider,MediumSpider,Giant Spider,Werespider;"
                + "GiantViper,GiantViperKing",
                "The ladders themselves, by UnitInfo asset name: kinds separated by semicolons, "
                + "steps within a kind by commas, weakest first. Only what is honestly the same "
                + "beast grown older belongs here — a lion and a lioness are a pair, not a "
                + "ladder, and a hippopotamus is not a young rhinoceros. What the game has is "
                + "mostly wolves and spiders; everything else grows in size instead.");

            EvolveEvery = config.Bind("Taming", "EvolveEvery", 10,
                new ConfigDescription(
                    "How many levels a beast must gain since it was caught, or since it last "
                    + "changed, before it changes again. Ten: a wolf that is being raised in "
                    + "earnest should see its ladder, not one rung of it.",
                    new AcceptableValueRange<int>(1, 500)));

            Sizes = config.Bind("Taming", "Sizes", false,
                "When a beast has no older form left — and most have none at all — let it grow "
                + "in size instead. Size is not decoration in this game: every one of a beast's "
                + "six attributes is worked out from its size and its level, so a bear that "
                + "becomes a huge bear is a different animal to fight beside. Five steps, from "
                + "small to titanic.");

            Scale = config.Bind("Taming", "Scale", 0.2f,
                new ConfigDescription(
                    "How much the body itself grows at each step of size, as a share. A fifth: "
                    + "enough to see across a clearing, little enough that the animation and the "
                    + "ground it walks on still agree with each other.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Panel = config.Bind("Taming", "Panel", true,
                "Show a tamed beast in the party row, the way a hired man is shown: portrait, "
                + "name, health, and the same click to pick it out. The game builds exactly "
                + "such a row for a temporary follower — prefab, grid and list are all its own "
                + "— and refuses it to a pet only because a pet carries a summon component. A "
                + "beast somebody caught and feeds is nearer to a companion than to a conjured "
                + "spirit, so it gets the row.");

            Telling = config.Bind("Taming", "Telling", true,
                "Write down every beast taken, every level it gains, and what the game's own "
                + "taming school actually holds — which is the only honest way to learn whether "
                + "the spell exists in this build at all.");
        }

        // Пойманные этой игрой: отметка «питомец» ставится игрой раньше нас, но при некоторых
        // дорогах восстановления приходит позже, чем нас спрашивают.
        private static readonly HashSet<int> tamed = new HashSet<int>();

        /// <summary>True when this is a tamed beast of ours: an animal, kept by somebody.</summary>
        internal static bool Mine(UnitAttribute who)
        {
            if (!Enabled.Value || who == null || who.Data == null) return false;

            // Сперва дешёвое: ничей зверь спрашивается на каждом ударе в игре, и разбирать
            // породы для всякого волка в лесу незачем.
            if (!who.Data.isWarpet && !tamed.Contains(who.Data.id)) return false;

            // Человекоподобных сюда не пускаем — кроме тех, кого сами и вырастили: последняя
            // ступень волчьей лестницы, оборотень, сложен по-людски, и отказать ему значило бы
            // отобрать у него на полпути и ряд в отряде, и сумку, и приказы.
            if (who is HumaniodUnit && !tamed.Contains(who.Data.id)) return false;

            // Лошадь — своё хозяйство: она вьючная, в бою не дерётся и места в отряде не ест.
            return !(Steed.Enabled != null && Steed.Enabled.Value && Steed.Mine(who));
        }

        /// <summary>Whose beast this is.</summary>
        /// <summary>
        /// Opens on the portrait the very menu the creature opens in the world.
        ///
        /// Своё меню мы строили сами, и оттого оно отличалось от того, что игрок видит, когда
        /// щёлкает по зверю на земле. Незачем: у каждого существа висит «Interactable», и
        /// правый щелчок по нему собирает меню и показывает его. Зовём то же самое — тогда
        /// иконка и зверь отвечают одинаково, и всё, что мы дописали в игровое меню, окажется
        /// и здесь.
        /// </summary>
        internal static void Hail(UnitAttribute beast)
        {
            if (beast == null) return;

            try
            {
                Interactable door = beast.GetComponent<Interactable>();
                if (door == null) door = beast.GetComponentInChildren<Interactable>(true);

                if (door != null)
                {
                    door.OnRightClick();
                    return;
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог открыть игровое меню зверя: " + e.Message);
            }

            // Если «Interactable» не нашёлся — своё меню лучше, чем никакого.
            Open(beast);
        }

        /// <summary>Wires one of those dropdowns to this beast and to nobody else.</summary>
        private static void Style(UnityEngine.UI.Dropdown list, Action<int> put)
        {
            if (list == null || put == null) return;

            try
            {
                // Прежние слушатели снимаем целиком: они писаны на чужого.
                list.onValueChanged = new UnityEngine.UI.Dropdown.DropdownEvent();
                list.onValueChanged.AddListener(delegate (int chosen) { put(chosen); });

                if (list.transform.parent != null)
                {
                    list.transform.parent.gameObject.SetActive(true);
                }
            }
            catch
            {
            }
        }

        internal static HumaniodUnit Owner(UnitAttribute beast)
        {
            if (beast == null || beast.Data == null) return null;

            try
            {
                foreach (HumaniodUnit man in Company())
                {
                    if (man != null && man.warpet == beast) return man;
                }
            }
            catch
            {
            }

            return null;
        }

        /// <summary>Everyone in the party, the leader among them.</summary>
        private static List<HumaniodUnit> Company()
        {
            List<HumaniodUnit> all = new List<HumaniodUnit>();

            try
            {
                PartyManager party = PartyManager.instance;

                if (party != null)
                {
                    if (party.leader != null) all.Add(party.leader);

                    if (party.partyMembers != null)
                    {
                        foreach (HumaniodUnit one in party.partyMembers)
                        {
                            if (one != null && !all.Contains(one)) all.Add(one);
                        }
                    }
                }

                HumaniodUnit main = gameManager.mainCharUnit;
                if (main != null && !all.Contains(main)) all.Add(main);
            }
            catch
            {
            }

            return all;
        }

        // ------------------------------------------------------------------ место в отряде

        private static int seats = -1;

        /// <summary>How many tamed beasts the company keeps.</summary>
        internal static int Beasts()
        {
            int much = 0;

            foreach (HumaniodUnit man in Company())
            {
                if (man != null && man.warpet != null && Mine(man.warpet)) much++;
            }

            return much;
        }

        /// <summary>
        /// Takes the beasts out of the party's places, one for one.
        ///
        /// Предел отряда — одно число на всю игру, и через него же считается место в караване.
        /// Уменьшаем его, а не заводим своё: тогда и вербовка, и полоска в окне отряда, и всё
        /// прочее, что смотрит на предел, узнают о звере сами, без единой правки.
        /// </summary>
        internal static void Seats()
        {
            if (!Enabled.Value || !PartySlot.Value) return;

            try
            {
                if (seats < 0) seats = PartyManager.maxPartySize;

                int taken = Beasts();
                int now = Mathf.Max(1, seats - taken);

                if (PartyManager.maxPartySize == now) return;

                PartyManager.maxPartySize = now;

                if (Telling.Value)
                {
                    ItemForgePlugin.Log.LogInfo($"Мест в отряде: {now} из {seats} — "
                        + $"{taken} занято зверями.");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог пересчитать места в отряде: " + e.Message);
            }
        }

        /// <summary>True when there is a place in the party for one more beast.</summary>
        internal static bool Room(HumaniodUnit owner, UnitAttribute pet)
        {
            if (!Enabled.Value || !PartySlot.Value) return true;
            if (owner == null || pet == null || pet.Data == null) return true;
            if (pet is HumaniodUnit) return true;

            // Чужие питомцы — не наша забота: волк при бандите ничьего места не занимает.
            if (owner.Data == null || owner.Data.team != Faction.player) return true;

            // Лошадь идёт вьюком и места не ест.
            if (Steed.Enabled != null && Steed.Enabled.Value && Steed.Mine(pet)) return true;

            try
            {
                if (seats < 0) seats = PartyManager.maxPartySize;

                PartyManager party = PartyManager.instance;
                int people = (party != null && party.partyMembers != null)
                    ? party.partyMembers.Count : 0;

                // Зверь, которого уже держит этот же человек, места не добавляет: он его
                // просто меняет.
                int beasts = Beasts();
                if (owner.warpet != null && Mine(owner.warpet)) beasts--;

                // Один на весь отряд: зверь — спутник, а не псарня.
                if (beasts >= Many.Value)
                {
                    GameController.ShowMessage(Many.Value == 1
                        ? "Зверь в отряде уже есть."
                        : $"Зверей в отряде уже {beasts} — больше {Many.Value} не водят.", 3f);

                    if (Telling.Value)
                    {
                        ItemForgePlugin.Log.LogInfo($"Зверя не взяли: в отряде уже {beasts} "
                            + $"при пределе {Many.Value}.");
                    }

                    return false;
                }

                if (people + beasts < seats) return true;

                GameController.ShowMessage($"В отряде нет места: {people} человек и "
                    + $"{beasts} зверей из {seats}.", 3f);

                if (Telling.Value)
                {
                    ItemForgePlugin.Log.LogInfo($"Зверя не взяли: мест {seats}, людей {people}, "
                        + $"зверей {beasts}.");
                }

                return false;
            }
            catch
            {
                return true;
            }
        }

        // ------------------------------------------------------------------ взятие

        /// <summary>Remembers a beast somebody has just tamed.</summary>
        internal static void Took(HumaniodUnit owner, UnitAttribute pet)
        {
            if (!Enabled.Value || owner == null || pet == null || pet.Data == null) return;
            if (pet is HumaniodUnit) return;
            if (Steed.Enabled != null && Steed.Enabled.Value && Steed.Mine(pet)) return;

            tamed.Add(pet.Data.id);

            // Книга ведётся на хозяина, и у нового зверя она должна начаться с чистого листа:
            // иначе пойманный сегодня волк получил бы в наследство вложенное во вчерашнего.
            books.Remove(Archive() + "|" + (owner.Data != null ? owner.Data.id.ToString() : "?"));

            Alive(pet);
            Seats();
            Show(pet);

            // И сразу за хозяина: диким зверь ждал, чтобы к нему подошли, — «passive» или
            // «holdposition», смотря по породе, — и этот настрой переезжал вместе с ним.
            // Прирученный ложился у ног и смотрел, как хозяина бьют, покуда не доставалось ему
            // самому. Питомец в драке — питомец, а не зритель; захочет хозяин иначе — скажет
            // «Стоять», и зверь встанет.
            Fierce(pet);

            Follow(pet);

            if (!since.ContainsKey(pet)) since[pet] = pet.Data.level;

            if (Telling.Value)
            {
                ItemForgePlugin.Log.LogInfo($"Приручён «{pet.Data.unitname}»: уровень "
                    + $"{pet.Data.level}, хозяин «{owner.Data.unitname}».");
            }
        }

        /// <summary>
        /// Ставит зверю боевой нрав: идти на врага и драться в полную силу.
        ///
        /// Диким он ждал, чтобы к нему подошли, и этот настрой переезжал вместе с ним. Держать
        /// место — значит не нападать вовсе: игра ищет цель только тем, кто «вступает первым».
        /// Захочет хозяин иначе — скажет «Стоять», и зверь встанет.
        /// </summary>
        internal static void Fierce(UnitAttribute beast)
        {
            if (beast == null || beast.Data == null) return;

            beast.Data.engageStyle = EngageStyle.initiative;
            beast.Data.defaultEngageStyle = EngageStyle.initiative;
            beast.Data.combatStyle = CombatStyle.aggressive;

            if (beast.Data.id != 0) posted.Remove(beast.Data.id);
        }

        /// <summary>Gives a tamed beast its mortality back.</summary>
        internal static void Alive(UnitAttribute beast)
        {
            if (!Enabled.Value || !Mortal.Value || beast == null || beast.Data == null) return;
            if (!Mine(beast) || beast.Data.canKill) return;

            beast.Data.canKill = true;
        }

        // ------------------------------------------------------------------ спутник

        /// <summary>Opens the beast's own bag.</summary>
        internal static void Pack(UnitAttribute beast)
        {
            if (beast == null || beast.items == null || LootManager.instance == null) return;

            // Контейнер отдаём тот, что на самом звере: окно закрывается через него, и с
            // пустотой вместо него кнопка закрытия не доходит до конца.
            Container box = beast.GetComponent<Container>();

            LootManager.instance.StartLooting(beast.items, box);
        }

        // Куда зверю велено встать и стоять. Пока это помнится, никто не вправе сделать его
        // снова провожатым — ни игра, ни мы сами.
        private static readonly Dictionary<int, Vector3> posted = new Dictionary<int, Vector3>();

        /// <summary>
        /// Sends the beast to a place and leaves it there.
        ///
        /// Одного приказа идти мало: зверь доходил и возвращался. Он у игры провожатый, и её
        /// собственный присмотр за питомцем снова цепляет его к хозяину, едва тот отойдёт.
        /// Поэтому здесь не только приказ, но и запись: пока место помнится, «Tick» всякий раз
        /// снимает с него поводок, а поведение ставится «держать позицию» — тем самым словом,
        /// каким игра велит стоять своим.
        /// </summary>
        internal static void Post(UnitAttribute beast, Vector3 where)
        {
            if (beast == null || beast.stateMachine == null) return;

            try
            {
                if (beast.Data != null)
                {
                    beast.Data.engageStyle = EngageStyle.holdposition;
                    beast.Data.defaultEngageStyle = EngageStyle.holdposition;
                }

                beast.stateMachine.ClearChain();
                beast.isFollower = false;
                beast.leader = null;
                beast.stateMachine.HandleCommand(new UnitCommand(commandsName.move, where));

                if (beast.Data != null) posted[beast.Data.id] = where;
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог поставить зверя на место: " + e.Message);
            }
        }

        /// <summary>Takes the post back: from here on the beast walks with its owner again.</summary>
        internal static void Unpost(UnitAttribute beast)
        {
            if (beast != null && beast.Data != null) posted.Remove(beast.Data.id);
        }

        /// <summary>Holds a posted beast where it was put, whatever tries to call it back.</summary>
        private static void Keep(UnitAttribute beast)
        {
            if (beast == null || beast.Data == null) return;

            Vector3 where;
            if (!posted.TryGetValue(beast.Data.id, out where)) return;

            if (!beast.isFollower && beast.leader == null) return;

            beast.isFollower = false;
            beast.leader = null;
        }

        internal static void Follow(UnitAttribute beast)
        {
            HumaniodUnit owner = Owner(beast);
            if (beast == null || owner == null || beast.stateMachine == null) return;

            Unpost(beast);

            if (beast.Data != null && beast.Data.engageStyle == EngageStyle.holdposition)
            {
                beast.Data.engageStyle = EngageStyle.initiative;
                beast.Data.defaultEngageStyle = EngageStyle.initiative;
            }

            beast.stateMachine.ClearChain();
            beast.leader = owner;
            beast.isFollower = true;
            beast.stateMachine.HandleCommand(
                new UnitCommand(commandsName.follow, owner.gameObject));
        }

        /// <summary>
        /// Стоять — значит стоять.
        ///
        /// Одного приказа мало ровно по той же причине, по какой его мало при «иди на место»:
        /// зверь у игры провожатый, и её собственный присмотр цепляет поводок обратно, едва
        /// хозяин отойдёт на шаг. Приказ отрабатывал, зверь замирал — и тут же шёл следом.
        ///
        /// Оттого место, на котором он встал, записывается так же, как и при отправке: пока
        /// оно помнится, «Tick» всякий раз снимает поводок заново.
        /// </summary>
        /// <summary>Записывает место, на котором зверь встал, каким бы приказом он ни встал.</summary>
        internal static void Stand(UnitAttribute beast)
        {
            if (beast == null || beast.Data == null) return;

            try
            {
                if (beast.Data.engageStyle != EngageStyle.holdposition)
                {
                    beast.Data.engageStyle = EngageStyle.holdposition;
                    beast.Data.defaultEngageStyle = EngageStyle.holdposition;
                }

                beast.isFollower = false;
                beast.leader = null;

                posted[beast.Data.id] = beast.transform.position;
            }
            catch
            {
            }
        }

        internal static void Stay(UnitAttribute beast)
        {
            if (beast == null || beast.stateMachine == null) return;

            if (beast.Data != null)
            {
                beast.Data.engageStyle = EngageStyle.holdposition;
                beast.Data.defaultEngageStyle = EngageStyle.holdposition;
            }

            beast.stateMachine.ClearChain();
            beast.isFollower = false;
            beast.leader = null;
            beast.stateMachine.HandleCommand(new UnitCommand(commandsName.standGuard));

            if (beast.Data != null) posted[beast.Data.id] = beast.transform.position;
        }

        internal static void Release(UnitAttribute beast)
        {
            if (beast == null || beast.Data == null) return;

            HumaniodUnit owner = Owner(beast);

            if (owner != null && owner.warpet == beast) owner.RemoveWarpet(beast);
            else beast.ChangeTeam(Faction.none);

            beast.isFollower = false;
            beast.leader = null;

            if (beast.stateMachine != null) beast.stateMachine.HandleStop();

            tamed.Remove(beast.Data.id);
            Hide(beast);
            Seats();

            if (Telling.Value)
            {
                ItemForgePlugin.Log.LogInfo($"«{beast.Data.unitname}» отпущен на волю.");
            }
        }

        /// <summary>The beast of whoever is being played, or the first the party keeps.</summary>
        internal static UnitAttribute Chosen()
        {
            try
            {
                HumaniodUnit who = gameManager.currentplayUnit;

                if (who != null && who.warpet != null && Mine(who.warpet)) return who.warpet;

                foreach (HumaniodUnit man in Company())
                {
                    if (man != null && man.warpet != null && Mine(man.warpet)) return man.warpet;
                }
            }
            catch
            {
            }

            return null;
        }

        /// <summary>
        /// Sets the beast on somebody.
        ///
        /// Приказ тот же, каким игра натравливает всякого: `kill` с целью. Своевольничать
        /// после него зверь не будет — цепочка прежних приказов сбрасывается, иначе он
        /// побежит исполнять «следовать» на полпути к горлу.
        /// </summary>
        internal static void Sic(UnitAttribute beast, UnitAttribute prey)
        {
            if (beast == null || prey == null || beast.stateMachine == null) return;

            try
            {
                beast.stateMachine.ClearChain();
                beast.isFollower = false;
                beast.leader = null;
                beast.stateMachine.HandleCommand(
                    new UnitCommand(commandsName.kill, prey.gameObject));

                GameController.ShowMessage($"«{beast.Data.unitname}» — на «{prey.Data.unitname}».", 2f);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог натравить зверя: " + e.Message);
            }
        }

        /// <summary>
        /// Sends the beast to where the player is standing.
        ///
        /// Приказа «иди в точку» игра мышью не даёт никому, кроме того, кем играют: щелчок по
        /// земле она разбирает сама и до чужих зверей не доносит. Точка, которую можно назвать
        /// без щелчка, одна — та, на которой стоишь сам. Дойти и позвать оказывается и проще,
        /// и понятнее любого режима приказов.
        /// </summary>
        internal static void Here(UnitAttribute beast)
        {
            HumaniodUnit who = gameManager.currentplayUnit;
            if (beast == null || who == null || beast.stateMachine == null) return;

            try
            {
                Post(beast, who.transform.position);
                GameController.ShowMessage($"«{beast.Data.unitname}» идёт на место.", 2f);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог послать зверя на место: " + e.Message);
            }
        }

        /// <summary>
        /// Reads the click and gives the beast the order it points at; true when it took one.
        ///
        /// Луч бросаем свой, а не пользуемся игровым: игровой принадлежит тому, кем играют, и
        /// разбирается по его же слоям. Нам нужно меньше — куда показали и стоит ли там враг.
        /// </summary>
        internal static bool Order(UnitAttribute beast)
        {
            if (beast == null || beast.stateMachine == null) return false;

            try
            {
                Camera eye = gameManager.mainCamera;
                if (eye == null) return false;

                RaycastHit hit;
                if (!Physics.Raycast(eye.ScreenPointToRay(Input.mousePosition), out hit, 300f))
                {
                    return false;
                }

                UnitAttribute who = UnitAttribute.GetUnitFromObject(hit.collider.gameObject);

                if (who != null && who != beast && who.Data != null && !who.Data.isdead
                    && GameController.IsEnemy(beast, who))
                {
                    Sic(beast, who);
                    return true;
                }

                Post(beast, hit.point);
                return true;
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог отдать приказ зверю: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// Everything one can tell a beast, in one place.
        ///
        /// Собрано вместе, потому что зовётся из двух дверей: из правого меню по самому зверю и
        /// из щелчка по его ячейке в ряду отряда. Расходиться этим двум спискам незачем.
        /// </summary>
        internal static Dictionary<string, UnityAction> Board(UnitAttribute beast)
        {
            Dictionary<string, UnityAction> said = new Dictionary<string, UnityAction>();

            if (beast == null || beast.Data == null) return said;

            said["Осмотреть"] = delegate { Look(beast); };
            said["Следовать за мной"] = delegate { Follow(beast); };
            said["Идти на моё место"] = delegate { Here(beast); };
            said["Стоять"] = delegate { Stay(beast); };
            said["В бою: " + Fights(beast)] = delegate { Fight(beast); };
            said["Держаться: " + Holds(beast)] = delegate { Hold(beast); };
            said["Отпустить"] = delegate { Release(beast); };

            return said;
        }

        /// <summary>Opens the beast's own window — the same one the world menu opens.</summary>
        internal static void Look(UnitAttribute beast)
        {
            if (beast == null) return;

            try
            {
                if (InspectPanelManager.Instance != null)
                {
                    InspectPanelManager.Instance.ShowInspectPanel(beast, true);
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог показать зверя: " + e.Message);
            }
        }

        /// <summary>Opens that board where the mouse is, as the right-click menu does.</summary>
        internal static void Open(UnitAttribute beast)
        {
            if (!Enabled.Value || beast == null || !Mine(beast)) return;

            try
            {
                RightClickMenuManager menu = RightClickMenuManager.Instance;
                if (menu == null) return;

                Dictionary<string, UnityAction> said = Board(beast);
                said["Cancel"] = delegate { menu.Cancel(); };

                menu.Cancel();
                menu.ShowRightClickMenu(said, null, Input.mousePosition);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог открыть меню зверя: " + e.Message);
            }
        }

        /// <summary>The words for each way of fighting, in the order the game keeps them.</summary>
        private static readonly string[] fighting =
            { "агрессивно", "осторожно", "оборонительно", "беречься", "только заклинания" };

        private static readonly string[] holding =
            { "вступать первым", "выжидать", "держать место" };

        internal static string Fights(UnitAttribute beast)
        {
            int at = (beast != null && beast.Data != null) ? (int)beast.Data.combatStyle : 0;
            return (at >= 0 && at < fighting.Length) ? fighting[at] : at.ToString();
        }

        internal static string Holds(UnitAttribute beast)
        {
            int at = (beast != null && beast.Data != null) ? (int)beast.Data.engageStyle : 0;
            return (at >= 0 && at < holding.Length) ? holding[at] : at.ToString();
        }

        /// <summary>
        /// Moves the beast on to the next way of fighting.
        ///
        /// Окно тактики в игре построено под человека — его поле «кого правим» объявлено
        /// людским, и зверя туда не подставить. Но сами числа лежат в записи любого существа,
        /// и менять их можно прямо из меню: щёлкнул — следующее.
        /// </summary>


        private static int urged;

        /// <summary>
        /// Посылает питомца в бой, когда сам он не идёт.
        ///
        /// Собственный присмотр игры за провожатым устроен так: пока зверь «следует», он
        /// проверяет, нет ли кого рядом, — но только если ему не отдано никакого приказа, и
        /// только чтобы тут же вернуться к следованию, если приказ уже висит. Замер показал
        /// ровно это: нрав боевой, бить позволено, отряд в бою, цель есть, сцеплено двое — а
        /// состояние по-прежнему «следую», и так минутами.
        ///
        /// Разбирать чужой присмотр дальше незачем: у игры есть прямой приказ «взять» — тот
        /// самый, которым игрок травит зверя вручную. Его и отдаём, раз в секунду, покуда зверь
        /// стоит при драке. Это не поверх её счёта: это её же приказ, отданный вовремя.
        ///
        /// Не трогаем поставленных стоять и тех, кому хозяин назначил иной нрав: «выжидать»
        /// значит выжидать.
        /// </summary>
        private static void Urge()
        {
            if (!Enabled.Value) return;

            try
            {
                foreach (HumaniodUnit man in Company())
                {
                    if (man == null || man.warpet == null) continue;

                    UnitAttribute beast = man.warpet;

                    if (beast.Data == null || beast.Data.isdead) continue;
                    if (!Mine(beast)) continue;

                    if (beast.Data.engageStyle != EngageStyle.initiative) continue;
                    if (posted.ContainsKey(beast.Data.id)) continue;
                    if (!beast.Data.allowattack) continue;

                    if (beast.stateMachine == null || beast.stateMachine.CState == null) continue;

                    // Дерётся или гонится — не мешаем.
                    if (beast.stateMachine.CState.thisStates != BehaviorState.following) continue;

                    UnitAttribute prey = beast.engagedEnemy != null && beast.engagedEnemy.Count > 0
                        ? beast.FindEngagedTarget()
                        : null;

                    if (prey == null) prey = beast.FindSensedTarget(true);
                    if (prey == null || prey.Data == null || prey.Data.isdead) continue;

                    beast.stateMachine.HandleCommand(
                        new UnitCommand(commandsName.kill, prey.gameObject));

                    if (Telling.Value && urged < 20)
                    {
                        urged++;

                        ItemForgePlugin.Log.LogInfo($"«{beast.Data.unitname}» послан на "
                            + $"«{prey.Data.unitname}»: сам не шёл.");
                    }
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог послать зверя в бой: " + e.Message);
            }
        }

        private static int watched;

        /// <summary>
        /// Пишет в журнал всё, от чего зависит, бросится зверь на врага или нет.
        ///
        /// Решает это не одно поле, а пять, и лежат они в разных местах. Зверь ищет цель сам
        /// только «вступающим первым»; ищет, только если ему позволено бить; и только если
        /// отряд уже в бою — либо если зверь не считается ни отрядным, ни временным спутником.
        /// Догадываться, которое из пяти держит его на месте, дороже, чем спросить.
        /// </summary>
        internal static void Watch()
        {
            if (!Telling.Value || watched >= 30) return;

            try
            {
                bool fighting = PartyManager.instance != null && PartyManager.instance.partyEngaged;

                foreach (HumaniodUnit man in Company())
                {
                    if (man == null || man.warpet == null) continue;

                    UnitAttribute beast = man.warpet;
                    if (beast.Data == null || beast.Data.isdead) continue;

                    watched++;

                    ItemForgePlugin.Log.LogInfo($"Зверь «{beast.Data.unitname}»: "
                        + $"нрав {beast.Data.engageStyle}/{beast.Data.combatStyle}, "
                        + $"бить {beast.Data.allowattack}, идти {beast.Data.allowmove}, "
                        + $"в отряде {beast.inParty}, временный {beast.isTempFollower}, "
                        + $"провожатый {beast.isFollower}, старший "
                        + $"{(beast.leader != null ? beast.leader.Data?.unitname : "нет")}, "
                        + $"цель {(beast.Target != null ? beast.Target.Data?.unitname : "нет")}, "
                        + $"сцеплен {(beast.engagedEnemy != null ? beast.engagedEnemy.Count : -1)}, "
                        + $"отряд в бою {fighting}, состояние "
                        + $"{(beast.stateMachine?.CState != null ? beast.stateMachine.CState.thisStates.ToString() : "нет")}.");

                    if (watched >= 30) return;
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог осмотреть зверя: " + e.Message);
            }
        }

        /// <summary>Выбирал ли хозяин нрав этому зверю.</summary>
        internal static bool Chosen(UnitAttribute beast)
        {
            try
            {
                Book book = Kept(beast);
                return book != null && book.chose;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Хозяин выбрал нрав сам — больше в это не вмешиваемся.</summary>
        internal static void Chose(UnitAttribute beast)
        {
            try
            {
                Book book = Kept(beast);
                if (book == null || book.chose) return;

                book.chose = true;
                Write();
            }
            catch
            {
            }
        }

        internal static void Fight(UnitAttribute beast)
        {
            if (beast == null || beast.Data == null) return;

            Chose(beast);

            int at = ((int)beast.Data.combatStyle + 1) % fighting.Length;
            beast.Data.combatStyle = (CombatStyle)at;

            GameController.ShowMessage($"«{beast.Data.unitname}» в бою: {fighting[at]}.", 2f);
        }

        internal static void Hold(UnitAttribute beast)
        {
            if (beast == null || beast.Data == null) return;

            Chose(beast);

            int at = ((int)beast.Data.engageStyle + 1) % holding.Length;
            beast.Data.engageStyle = (EngageStyle)at;

            GameController.ShowMessage($"«{beast.Data.unitname}»: {holding[at]}.", 2f);
        }

        // ------------------------------------------------------------------ ряд отряда

        // Чья панель какая: чтобы убрать именно свою и не тронуть чужую.
        private static readonly Dictionary<UnitAttribute, UnitSlot> rows =
            new Dictionary<UnitAttribute, UnitSlot>();

        private static System.Reflection.FieldInfo prefabField;
        private static System.Reflection.FieldInfo gridField;

        // Правда, если панели с полосами не нашлось и пришлось взять кружок спутника.
        private static bool plain;

        /// <summary>
        /// Gives a beast the row a hired man has.
        ///
        /// Собирается это из игровых же частей: `followerPanelPrefab` в `followerPanelGrid`,
        /// `Assign(зверь)` — он объявлен для всякого существа, а не для человека, — и запись
        /// в игровой же список `followerpanels`. Оттого обновление полоски, выделение и
        /// порядок достаются нам даром: список её собственный, и она сама за ним следит.
        ///
        /// Своим питомцам игра такой панели не даёт, и причина в одной проверке:
        /// `member.summonComponent == null`. У питомца он есть, и панели не бывает. Пойманный
        /// зверь, однако, ближе к наёмнику, чем к вызванному духу.
        /// </summary>
        internal static void Show(UnitAttribute beast)
        {
            if (!Enabled.Value || !Panel.Value || beast == null || beast.Data == null) return;
            if (rows.ContainsKey(beast)) return;

            try
            {
                PartyManager party = PartyManager.instance;
                if (party == null) return;

                if (prefabField == null)
                {
                    // Та самая панель, что у людей отряда: с портретом, именем, двумя полосами
                    // и выпадающими списками поведения. «BarController» наследован от «UnitSlot»,
                    // а тот объявлен для всякого существа, — значит зверю она подходит целиком,
                    // и городить свою незачем.
                    prefabField = AccessTools.Field(typeof(PartyManager), "statePanelPrefab");
                    gridField = AccessTools.Field(typeof(PartyManager), "statePanelGrid");

                    // Если этих полей нет, отступаем к прежней — кружку спутника.
                    if (prefabField == null || gridField == null)
                    {
                        prefabField = AccessTools.Field(typeof(PartyManager), "followerPanelPrefab");
                        gridField = AccessTools.Field(typeof(PartyManager), "followerPanelGrid");
                        plain = true;
                    }
                }

                if (prefabField == null || gridField == null) return;

                GameObject shape = prefabField.GetValue(party) as GameObject;
                GameObject grid = gridField.GetValue(party) as GameObject;

                if (shape == null || grid == null) return;

                UnitSlot row = UnityEngine.Object
                    .Instantiate(shape, grid.transform)
                    .GetComponent<UnitSlot>();

                if (row == null) return;

                row.gameObject.SetActive(true);
                row.Assign(beast);

                // Правым щелчком — приказы; левый оставляем игре, она им выбирает того, кем
                // командовать, и отбирать это у панели незачем.
                UnitAttribute whose = beast;
                row.onLeftClick.AddListener(delegate (UnitSlot slot) { Hail(whose); });
                row.onRightClick.AddListener(delegate (UnitSlot slot) { Hail(whose); });

                BarController bars = row as BarController;

                // Списки поведения на панели зверя. Своими руками, а не как придётся: у
                // готовой панели они писаны на того, кого она показывала до нас, и
                // переключение стиля у собаки поменяло бы его кому-то из людей.
                if (bars != null)
                {
                    Style(bars.engageStyle, delegate (int put)
                    {
                        Chose(beast);

                        beast.Data.engageStyle = (EngageStyle)put;
                        beast.Data.defaultEngageStyle = (EngageStyle)put;
                        beast.onCombatInfoChange.Invoke(beast);
                    });

                    Style(bars.combatStyle, delegate (int put)
                    {
                        Chose(beast);

                        beast.Data.combatStyle = (CombatStyle)put;
                        beast.onCombatInfoChange.Invoke(beast);
                    });

                    Style(bars.attackDirection, delegate (int put)
                    {
                        beast.Data.attackDirection = (AttackDirection)put;
                        beast.onCombatInfoChange.Invoke(beast);
                    });
                }

                if (bars != null && party.statepanels != null) party.statepanels.Add(bars);
                else if (party.followerpanels != null) party.followerpanels.Add(row);

                rows[beast] = row;

                // Зверь, доставшийся из сохранения, дерётся — покуда хозяин не решил иначе.
                //
                // Прежде поднимали только из «держать место», а собака приходит выжидающей:
                // выжидать — значит отвечать тому, кто ударил её саму, и стоять, покуда рвут
                // хозяина. Цели ищет только тот, кто вступает первым.
                if (beast.Data.engageStyle != EngageStyle.initiative
                    && !posted.ContainsKey(beast.Data.id)
                    && !Chosen(beast))
                {
                    Fierce(beast);
                }

                if (Telling.Value)
                {
                    ItemForgePlugin.Log.LogInfo($"«{beast.Data.unitname}» встал в ряд отряда.");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог поставить зверя в ряд: " + e.Message);
            }
        }

        /// <summary>Takes the row away again.</summary>
        internal static void Hide(UnitAttribute beast)
        {
            if (beast == null) return;

            UnitSlot row;
            if (!rows.TryGetValue(beast, out row)) return;

            rows.Remove(beast);

            try
            {
                PartyManager party = PartyManager.instance;

                if (party != null)
                {
                    BarController bars = row as BarController;

                    if (bars != null && party.statepanels != null) party.statepanels.Remove(bars);
                    if (party.followerpanels != null) party.followerpanels.Remove(row);
                }

                if (row != null) UnityEngine.Object.Destroy(row.gameObject);
            }
            catch
            {
            }
        }

        private static float ticked;

        /// <summary>
        /// Hands experience out, for trying the rules rather than living them.
        ///
        /// Зверь растёт сотней убитых, и проверять на этом правило нельзя: пока дождёшься
        /// уровня, забудешь, что проверял. Клавиша даёт людям через их же дверь — игровую
        /// «GainExp», которая считает целыми и дальше двух миллиардов не идёт, — а зверю в его
        /// книгу, где предела нет.
        /// </summary>
        private static void Handout()
        {
            try
            {
                double much = Gift.Value;

                int people = 0;

                foreach (HumaniodUnit man in Company())
                {
                    if (man == null || man.Data == null || man.Data.isdead) continue;

                    // Опыт человека — целое число, и целому есть предел. Даёшь ему разом
                    // сколько просили — и счёт перескакивает через край в минус два миллиарда,
                    // ровно как это и вышло. Даём столько, сколько в него влезает, и с запасом
                    // на игровые надбавки: человеку полагается ещё пять сотых за породу.
                    if (man.Data.exp < 0) man.Data.exp = 0;

                    double room = (int.MaxValue - 1 - (double)man.Data.exp) / 1.25;
                    int give = (int)System.Math.Max(0.0, System.Math.Min(much, room));

                    if (give > 0) man.GainExp(give);
                    people++;
                }

                int beasts = 0;

                foreach (HumaniodUnit man in Company())
                {
                    if (man == null || man.warpet == null || !Mine(man.warpet)) continue;

                    Book book = Kept(man.warpet);
                    book.exp += much;
                    beasts++;
                }

                if (beasts > 0) Write();

                // Перепись шести у людей отряда: чтобы было чем ответить на вопрос,
                // не поднимает ли раздача опыта чужие статы.
                if (Telling.Value)
                {
                    foreach (HumaniodUnit man in Company())
                    {
                        if (man == null || man.Data == null || man.Data.humanAttribute == null) continue;

                        HumanAttribute six = man.Data.humanAttribute;

                        ItemForgePlugin.Log.LogInfo($"После раздачи «{man.Data.unitname}»: "
                            + $"{six.BSstrength}, {six.BSendurance}, {six.BSagility}, "
                            + $"{six.BSprecision}, {six.BSintelligence}, {six.BSwillpower} "
                            + $"(сумма {six.Sum}, потенциал {six.potential}), "
                            + $"уровень {man.Data.level}, опыт {man.Data.exp}.");

                        // И то же про оружие: что написано в вещи, что вышло после всех
                        // множителей, и какие они. По этим строкам видно, доходит ли до
                        // окна усиленный урон или голый.
                        try
                        {
                            if (man.weapons != null && man.weapons.Count > 0
                                && man.weapons[0] != null)
                            {
                                Weapon arm = man.weapons[0];

                                string was = "", now = "";

                                if (arm.BSdamage != null)
                                {
                                    foreach (KeyValuePair<DamageType, Damage> one in arm.BSdamage)
                                    {
                                        if (one.Value == null) continue;
                                        was += $"{one.Key} {one.Value.minDamage:0.#}-"
                                            + $"{one.Value.maxDamage:0.#} ";
                                    }
                                }

                                if (arm.damage != null)
                                {
                                    foreach (KeyValuePair<DamageType, Damage> one in arm.damage)
                                    {
                                        if (one.Value == null) continue;
                                        now += $"{one.Key} {one.Value.minDamage:0.#}-"
                                            + $"{one.Value.maxDamage:0.#} ";
                                    }
                                }

                                ItemForgePlugin.Log.LogInfo($"  оружие «{arm.weaponType}»: "
                                    + $"в вещи [{was.Trim()}], после множителей [{now.Trim()}]; "
                                    + $"ближний ×{man.MeleeDamageMD:0.###}, "
                                    + $"дальний ×{man.RangeDamageMD:0.###}, "
                                    + $"весь ×{man.AllDamageMD:0.###}.");
                            }
                        }
                        catch
                        {
                        }
                    }
                }

                GameController.ShowMessage($"Роздано опыта: людей {people}, зверей {beasts}.", 3f);

                ItemForgePlugin.Log.LogInfo($"Клавишей роздан опыт: {much:0} — людей {people}, "
                    + $"зверей {beasts}.");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог раздать опыт: " + e.Message);
            }
        }

        /// <summary>
        /// Keeps the rows honest: one for every beast kept, none for anybody else.
        ///
        /// Раз в секунду, а не каждый кадр. Смерть и загрузка сюда приходят сами: мёртвому
        /// панель снимается, а поднятому из сохранения — ставится, и ловить для этого
        /// отдельные события не нужно.
        /// </summary>
        internal static void Tick()
        {
            if (!Enabled.Value || !Panel.Value) return;

            if (GiftKey.Value != KeyCode.None && Input.GetKeyDown(GiftKey.Value)) Handout();

            // Поводок снимаем каждый кадр, а не раз в секунду: игра цепляет питомца обратно
            // к хозяину чаще, и за секунду зверь успевает развернуться и уйти.
            //
            // И снимаем со всех прирученных, а не только с тех, у кого заведена строка в
            // отряде: строка — про показ, а стоять зверь должен и без неё.
            if (posted.Count > 0)
            {
                foreach (UnitAttribute one in new List<UnitAttribute>(since.Keys)) Keep(one);

                foreach (KeyValuePair<UnitAttribute, UnitSlot> one in rows)
                {
                    Keep(one.Key);
                }
            }

            ticked += Time.unscaledDeltaTime;
            if (ticked < 1f) return;
            ticked = 0f;

            Urge();
            Watch();

            try
            {
                // Убрать лишние: павших и отпущенных.
                List<UnitAttribute> gone = null;

                foreach (KeyValuePair<UnitAttribute, UnitSlot> one in rows)
                {
                    UnitAttribute beast = one.Key;

                    if (beast != null && beast.Data != null && !beast.Data.isdead
                        && Mine(beast)) continue;

                    if (gone == null) gone = new List<UnitAttribute>();
                    gone.Add(beast);
                }

                if (gone != null)
                {
                    foreach (UnitAttribute beast in gone) Hide(beast);
                }

                // Поставить недостающие: после загрузки зверь приходит без нашей панели.
                foreach (HumaniodUnit man in Company())
                {
                    if (man == null || man.warpet == null) continue;
                    if (!Mine(man.warpet) || man.warpet.Data == null) continue;
                    if (man.warpet.Data.isdead) continue;

                    Show(man.warpet);
                }
            }
            catch
            {
            }
        }

        // ------------------------------------------------------------------ книга зверя

        /// <summary>What a beast has earned and what has been spent on it.</summary>
        private sealed class Book
        {
            internal readonly int[] six = new int[6];
            internal int born;

            // Двойной точности, и это не роскошь. У зверя, которому скормили отрядный котёл,
            // опыта набирается за двести миллиардов, а одинарная точность на таких числах
            // считает с шагом в шестнадцать тысяч: вычесть из них цену очка в сто тысяч ещё
            // выходит, а в сто — уже нет, и трата просто не происходила.
            internal double exp;

            // На каком уровне зверь в последний раз стал старше. Живёт в записи, а не в
            // памяти: иначе всякая загрузка сбрасывала отсчёт, и лестница взросления
            // растягивалась вдвое-втрое против задуманного.
            internal int grew;

            // Выбирал ли хозяин зверю боевой нрав своей рукой. Покуда не выбирал, зверь дерётся:
            // дикий настрой, переехавший вместе с ним, означал бы, что питомец стоит и смотрит.
            // А выбрал — больше не трогаем, ни при загрузке, ни при взрослении.
            internal bool chose;
        }

        private static readonly Dictionary<string, Book> books =
            new Dictionary<string, Book>(StringComparer.Ordinal);

        private static bool read;
        private static System.Reflection.FieldInfo archiveField;

        internal static readonly string[] Named =
            { "сила", "выносливость", "ловкость", "точность", "разум", "воля" };

        /// <summary>Which save we are in: two runs must not share one beast's ledger.</summary>
        internal static string Archive()
        {
            try
            {
                SaveLoadManager keeper = SaveLoadManager.Instance;
                if (keeper == null) return "—";

                if (archiveField == null)
                {
                    archiveField = AccessTools.Field(typeof(SaveLoadManager), "currentArchive");
                }

                string where = archiveField != null
                    ? archiveField.GetValue(keeper) as string
                    : null;

                return string.IsNullOrEmpty(where) ? "—" : where;
            }
            catch
            {
                return "—";
            }
        }

        /// <summary>
        /// Under what name this beast's ledger is kept.
        ///
        /// По хозяину, а не по самому зверю, и вот почему: номер существа в этой игре не живёт.
        /// Питомца при каждой смене области пересоздают заново, и номер ему дают новый — один и
        /// тот же волк прошёл у меня как 204, потом 221, потом 222. Книга на такой ключ
        /// рассыпалась: вложенное оставалось в старой строке, зверь начинал с чистой, и со
        /// стороны это выглядело как «очки не тратятся».
        ///
        /// Хозяин же — человек, он записан в сохранение и номер свой сохраняет. Зверь у
        /// человека один, так что «сохранение и хозяин» опознают книгу без ошибки.
        /// </summary>
        // Кого из старых записей уже подобрали: одну запись — одному зверю.
        private static readonly HashSet<string> claimed = new HashSet<string>();

        /// <summary>
        /// Под каким именем записан этот зверь.
        ///
        /// Ведём на самого зверя, а не на хозяина. Прежде ключом была пара «сохранение и
        /// хозяин» — из расчёта, что питомец у человека один. Их бывает больше, и тогда
        /// второй волк открывал ту же книгу, что первый, а если связь с хозяином в этот миг
        /// не находилась — заводил себе третью, пустую, и опыт в окне «пропадал».
        ///
        /// Номер зверя игра хранит сама и держит на нём питомца при загрузке, так что ключ
        /// выходит и стойким, и своим у каждого. При взрослении зверь рождается заново с
        /// новым номером — запись переезжает за ним, это делает «Become».
        /// </summary>
        private static string Key(UnitAttribute beast)
        {
            if (beast == null || beast.Data == null) return Archive() + "|?";

            return Archive() + "|з" + beast.Data.id;
        }

        /// <summary>
        /// Подбирает старую запись, что велась на хозяина.
        ///
        /// Книги, заведённые прежним счётом, лежат под ключом хозяина. Отдаём такую первому
        /// зверю, который за ней придёт, и помечаем — второй получит свою, чистую.
        /// </summary>
        private static Book Inherit(UnitAttribute beast, string key)
        {
            try
            {
                HumaniodUnit owner = Owner(beast);
                if (owner == null || owner.Data == null) return null;

                string old = Archive() + "|" + owner.Data.id;
                if (claimed.Contains(old)) return null;

                Book was;
                if (!books.TryGetValue(old, out was)) return null;

                claimed.Add(old);
                books[key] = was;
                books.Remove(old);

                ItemForgePlugin.Log.LogInfo($"Запись «{old}» перешла к «{beast.Data.unitname}» "
                    + $"под ключом «{key}»: опыта {was.exp:0}, вложено "
                    + $"{was.six[0]}/{was.six[1]}/{was.six[2]}/{was.six[3]}/{was.six[4]}/"
                    + $"{was.six[5]}.");

                Write();

                return was;
            }
            catch
            {
                return null;
            }
        }

        private static string Ledger()
        {
            return System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location),
                "beasts.tsv");
        }

        /// <summary>
        /// Reads the ledger of what beasts have earned.
        ///
        /// Своя, потому что чужой нет: игровое сохранение не держит для зверя ни шести чисел,
        /// ни свободного поля, куда их спрятать, — это было видно ещё по окну осмотра, где
        /// статы приходится рисовать поверх. Ключ — пара «сохранение и номер существа»: номер
        /// игра хранит сама, на нём же держится питомец при загрузке.
        /// </summary>
        private static void Learn()
        {
            if (read) return;
            read = true;

            try
            {
                string file = Ledger();
                if (!System.IO.File.Exists(file)) return;

                foreach (string line in System.IO.File.ReadAllLines(file))
                {
                    if (line.Length == 0 || line[0] == '#') continue;

                    string[] cut = line.Split('	');
                    if (cut.Length < 9) continue;

                    Book book = new Book();

                    for (int i = 0; i < 6; i++)
                    {
                        int much;
                        int.TryParse(cut[i + 1], out much);
                        book.six[i] = much;
                    }

                    int first;
                    int.TryParse(cut[7], out first);
                    book.born = first;

                    double done;
                    double.TryParse(cut[8], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out done);
                    book.exp = done;

                    // Столбца с последним взрослением у старых записей нет — и не надо:
                    // ноль означает «ещё не считали», и отсчёт начнётся с текущего уровня.
                    if (cut.Length >= 10)
                    {
                        int last;
                        int.TryParse(cut[9], out last);
                        book.grew = last;
                    }

                    // Столбца о выбранном нраве у старых записей нет: значит, не выбирали.
                    if (cut.Length >= 11) book.chose = cut[10] == "1";

                    books[cut[0]] = book;
                }

                if (Telling.Value && books.Count > 0)
                {
                    ItemForgePlugin.Log.LogInfo($"Книга зверей прочитана: записей {books.Count}.");
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог прочесть книгу зверей: " + e.Message);
            }
        }

        private static void Write()
        {
            try
            {
                List<string> lines = new List<string>();

                foreach (KeyValuePair<string, Book> one in books)
                {
                    Book book = one.Value;

                    lines.Add(one.Key + "	" + book.six[0] + "	" + book.six[1] + "	"
                        + book.six[2] + "	" + book.six[3] + "	" + book.six[4] + "	"
                        + book.six[5] + "	" + book.born + "	"
                        + book.exp.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
                        + "	" + book.grew + "	" + (book.chose ? "1" : "0"));
                }

                System.IO.File.WriteAllLines(Ledger(), lines.ToArray());
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог записать книгу зверей: " + e.Message);
            }
        }

        private static Book Kept(UnitAttribute beast)
        {
            Learn();

            string key = Key(beast);

            Book book;
            if (!books.TryGetValue(key, out book))
            {
                // Сперва посмотрим, не лежит ли его добро под старым ключом — тем, что вёлся
                // на хозяина.
                book = Inherit(beast, key);

                if (book == null)
                {
                    book = new Book();
                    books[key] = book;
                }
            }

            // Каким зверя взяли, таким и стоит его табличная основа: шесть чисел выводятся из
            // роста и того уровня, а сверху ложится вложенное. Иначе вышел бы круг — уровень
            // из чисел, числа из уровня.
            if (book.born <= 0 && beast != null && beast.Data != null)
            {
                book.born = Mathf.Max(1, beast.Data.level);
            }

            return book;
        }

        /// <summary>What has been spent on this beast, for whoever works out its attributes.</summary>
        internal static int[] Bought(UnitAttribute beast)
        {
            if (Enabled == null || !Enabled.Value || beast == null || beast.Data == null) return null;
            if (!Mine(beast)) return null;

            Learn();

            Book book;
            return books.TryGetValue(Key(beast), out book) ? book.six : null;
        }

        /// <summary>
        /// The ceiling this beast grows against: its owner's own potential.
        ///
        /// Своего потенциала у зверя в записи нет, а какой-то нужен: без потолка цена очка
        /// никогда не растёт и вкладывать можно без конца. Сумма породы на эту роль не
        /// подошла — у одних она огромна, у других таблицы нет вовсе, и тогда потолка не
        /// было совсем.
        ///
        /// Берём человеческий: чем одарён хозяин, тем одарён и его зверь. Дальше расти можно,
        /// но каждое очко сверх потолка дороже — тем же счётом, каким игра держит человека:
        /// «num *= (Sum + 1 - potential) + 1».
        /// </summary>
        internal static int Nature(UnitAttribute beast)
        {
            try
            {
                HumaniodUnit owner = Owner(beast);

                // Зверь без хозяина — редкость (панель его ещё не нашла), и меряем тогда по
                // тому, кем играют: отряд один, потолок у него общий.
                if (owner == null) owner = gameManager.currentplayUnit;

                if (owner != null && owner.Data != null && owner.Data.humanAttribute != null)
                {
                    return owner.Data.humanAttribute.potential;
                }
            }
            catch
            {
            }

            // Столько игра кладёт человеку по умолчанию.
            return 150;
        }

        /// <summary>The level the beast was caught at: the floor of everything it has.</summary>
        internal static int Born(UnitAttribute beast)
        {
            if (Enabled == null || !Enabled.Value || beast == null || beast.Data == null) return 0;
            if (!Mine(beast)) return 0;

            return Kept(beast).born;
        }

        /// <summary>How much experience this beast has in hand.</summary>
        internal static double Purse(UnitAttribute beast)
        {
            return Kept(beast).exp;
        }

        /// <summary>
        /// What the next point of this attribute costs, by the game's own price.
        ///
        /// «Двести за первое очко, девятая доля сверху за каждое уже вложенное» — ровно то, во
        /// что оно обходится человеку. Чем выше стат, тем дороже следующий шаг, и оттого
        /// вложить всё в одну силу становится дороже, чем разложить по нескольким.
        /// </summary>
        internal static float Cost(UnitAttribute beast, int which)
        {
            float have = 0f;

            try
            {
                float[] six = Beastly.Enabled != null && Beastly.Enabled.Value
                    ? Beastly.Mine(beast)
                    : null;

                if (six != null && which < six.Length) have = six[which];
            }
            catch
            {
            }

            int at = Mathf.Max(0, Mathf.RoundToInt(have));

            float much = HumaniodUnit.ATTRIBUTE_EXP_BASE
                    * Mathf.Pow(HumaniodUnit.ATTRIBUTE_EXP_FACTOR, at)
                + HumaniodUnit.ATTRIBUTE_EXP_LV * at;

            // И то же, чем игра держит человека: сверх потенциала каждое очко дороже кратно
            // тому, насколько уже переросли. У человека потенциал — что ему отпущено от
            // рождения; у зверя это то, чем его наделила порода, то есть сумма его табличных
            // шести. Дальше расти можно, но всё дороже.
            int sum = 0;
            int room = Nature(beast);

            try
            {
                float[] six = Beastly.Mine(beast);

                if (six != null)
                {
                    for (int i = 0; i < 6 && i < six.Length; i++) sum += Mathf.RoundToInt(six[i]);
                }
            }
            catch
            {
            }

            if (room > 0 && sum + 1 > room) much *= (sum + 1 - room) + 1;

            return Mathf.Max(1f, much * ExpShare.Value);
        }

        /// <summary>
        /// Spends the beast's own experience on one of its six.
        ///
        /// Очков за уровень не бывает: уровень здесь ничего не раздаёт, он и сам выводится из
        /// шести чисел — так же, как у человека. Хозяин тратит накопленное зверем, зверь от
        /// этого растёт, и уровень над его головой поднимается следом.
        /// </summary>
        internal static void Spend(UnitAttribute beast, int which)
        {
            if (beast == null || beast.Data == null || which < 0 || which > 5) return;

            Book book = Kept(beast);

            // Тот же потолок, что у человека: «if (humanAttribute[index] <= 99)» в
            // «IncreaseAtriibuteValue». Выше девяноста девяти игра не пускает никого.
            try
            {
                float[] six = Beastly.Mine(beast);

                int top = Blood.Ceiling != null ? Blood.Ceiling.Value : 99;

                if (six != null && which < six.Length && Mathf.RoundToInt(six[which]) >= top)
                {
                    GameController.ShowMessage($"{Named[which]} у «{beast.Data.unitname}» "
                        + "уже на пределе.", 2f);
                    return;
                }
            }
            catch
            {
            }

            float price = Cost(beast, which);

            if (book.exp < (double)price)
            {
                GameController.ShowMessage($"Не хватает опыта: {book.exp:0} из {price:0}.", 2f);
                return;
            }

            book.exp -= price;
            book.six[which]++;

            Write();
            Recount(beast);

            GameController.ShowMessage($"«{beast.Data.unitname}»: {Named[which]} +1, "
                + $"осталось опыта {book.exp:0}.", 2f);

            Explain(beast);

            if (Telling.Value)
            {
                ItemForgePlugin.Log.LogInfo($"«{beast.Data.unitname}»: вложено в {Named[which]} "
                    + $"за {price:0}, всего вложено {book.six[which]}, опыта {book.exp:0}, "
                    + $"уровень {beast.Data.level}.");
            }
        }

        /// <summary>
        /// Writes out where a beast's numbers come from, piece by piece.
        ///
        /// Спор о росте нельзя выиграть чтением кода — я это уже проходил и дважды ошибся.
        /// Поэтому здесь ничего не рассуждается, а печатается: что даёт порода, что вложено
        /// руками, что из этого вышло, какая глубина набрана, сколько её до следующего уровня
        /// и во что обойдётся каждое из шести. По такой записи видно сразу, во что упёрлось.
        /// </summary>
        internal static void Explain(UnitAttribute beast)
        {
            if (!Enabled.Value || !Telling.Value || beast == null || beast.Data == null) return;
            if (!Mine(beast)) return;

            try
            {
                Book book = Kept(beast);

                float[] table = Beastly.Six(beast.size, Mathf.Max(1, book.born));
                float[] all = Beastly.Mine(beast);

                if (all == null) return;

                int depth = 0;
                int sum = 0;

                for (int i = 0; i < 6; i++)
                {
                    int at = Mathf.RoundToInt(all[i]);
                    sum += at;

                    for (int k = at - 1; k >= 0; k--) depth += Mathf.Min(k / 5, 3);
                }

                int level = Mathf.Max(book.born, Mathf.Max(1, depth / 5));
                int upto = (level + 1) * 5;

                string born = table != null
                    ? $"{table[0]:0.#}, {table[1]:0.#}, {table[2]:0.#}, {table[3]:0.#}, "
                        + $"{table[4]:0.#}, {table[5]:0.#}"
                    : "нет";

                ItemForgePlugin.Log.LogInfo(
                    $"Разбор зверя «{beast.Data.unitname}» ({(beast.info != null ? beast.info.name : "?")}, "
                    + $"{beast.size}):\n"
                    + $"  взят на уровне {book.born}, сейчас {beast.Data.level}, книга «{Key(beast)}»\n"
                    + $"  порода даёт: {born}\n"
                    + $"  вложено:     {book.six[0]}, {book.six[1]}, {book.six[2]}, "
                        + $"{book.six[3]}, {book.six[4]}, {book.six[5]}\n"
                    + $"  итого:       {all[0]:0.#}, {all[1]:0.#}, {all[2]:0.#}, {all[3]:0.#}, "
                        + $"{all[4]:0.#}, {all[5]:0.#}  (сумма {sum}, потенциал {Nature(beast)})\n"
                    + $"  глубина {depth} → уровень {level}, до следующего нужно {upto}\n"
                    + $"  цены: сила {Cost(beast, 0):0}, выносл {Cost(beast, 1):0}, "
                        + $"ловк {Cost(beast, 2):0}, точн {Cost(beast, 3):0}, "
                        + $"разум {Cost(beast, 4):0}, воля {Cost(beast, 5):0}\n"
                    + $"  опыта в руках {book.exp:0}");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог разобрать рост зверя: " + e.Message);
            }
        }

        /// <summary>Works the beast's level out of its six, the way a person's is worked out.</summary>
        internal static void Recount(UnitAttribute beast)
        {
            if (beast == null || beast.Data == null) return;

            try
            {
                float[] six = Beastly.Enabled != null && Beastly.Enabled.Value
                    ? Beastly.Mine(beast)
                    : null;

                if (six == null) return;

                int much = 0;

                for (int i = 0; i < 6; i++)
                {
                    for (int k = Mathf.RoundToInt(six[i]) - 1; k >= 0; k--)
                    {
                        much += Mathf.Min(k / 5, 3);
                    }
                }

                int was = beast.Data.level;
                // Без потолка: мы сняли его всем, снимаем и зверю. Ниже первого
                // уровня не бывает, выше — сколько нарастил.
                int now = Mathf.Max(Kept(beast).born, Mathf.Max(1, much / 5));

                beast.Data.level = now;
                beast.UpdateAttribute();

                if (now > was) Ripe(beast);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог пересчитать уровень зверя: " + e.Message);
            }
        }

        /// <summary>
        /// Gives the beast what it killed, the way the game gives it to a person.
        ///
        /// Число то же самое: шесть десятых от мощи павшего — столько игра кладёт человеку за
        /// то же тело. И показывается так же, строкой над головой, чтобы видно было, за что.
        /// </summary>
        internal static void Won(UnitAttribute beast, UnitAttribute fallen, float share = 1f)
        {
            if (!Enabled.Value || beast == null || fallen == null) return;
            if (!Mine(beast) || beast.Data == null || fallen.Data == null) return;
            if (beast.Data.level >= Ceiling.Value) return;
            if (share <= 0f) return;

            try
            {
                int power = (fallen is HumaniodUnit man)
                    ? man.Data.power
                    : (fallen.info != null ? fallen.info.power : 0);

                float much = Mathf.Max(1f, power * 0.6f * Mathf.Clamp01(share));

                Book book = Kept(beast);
                book.exp += much;

                Write();

                if (beast.lifebar != null)
                {
                    beast.lifebar.ShowTextTag($"<color=#FFD700FF>+{much:0}exp</color>", 3f);
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог зачесть добычу зверю: " + e.Message);
            }
        }

        /// <summary>
        /// Кладёт зверю его долю общего котла.
        ///
        /// Отдельного счёта у него больше нет: он боец отряда и ест оттуда же, откуда все.
        /// Оттого и котёл делится на едоков вместе с ним — взять волка в отряд значит
        /// подвинуться.
        /// </summary>
        internal static void Fed(UnitAttribute beast, float much)
        {
            if (!Enabled.Value || beast == null || beast.Data == null || much <= 0f) return;
            if (!Mine(beast)) return;
            if (beast.Data.level >= Ceiling.Value) return;

            try
            {
                Book book = Kept(beast);
                book.exp += much;

                Write();

                if (beast.lifebar != null)
                {
                    beast.lifebar.ShowTextTag($"<color=#FFD700FF>+{much:0}exp</color>", 3f);
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог зачесть зверю долю: " + e.Message);
            }
        }

        /// <summary>Звери отряда: те, кого его люди ведут при себе.</summary>
        internal static List<UnitAttribute> Pack()
        {
            List<UnitAttribute> pack = new List<UnitAttribute>();

            try
            {
                PartyManager party = PartyManager.instance;
                if (party == null || party.partyMembers == null) return pack;

                foreach (HumaniodUnit one in party.partyMembers)
                {
                    if (one == null || one.warpet == null) continue;
                    if (!Mine(one.warpet)) continue;

                    // Лошадь не боец: она везёт. Опыта ей не надо, и котла она не делит.
                    if (Steed.Mine(one.warpet)) continue;

                    if (!pack.Contains(one.warpet)) pack.Add(one.warpet);
                }
            }
            catch
            {
            }

            return pack;
        }

        // ------------------------------------------------------------------ чей вклад

        /// <summary>
        /// Кто сколько вложил в павшего.
        ///
        /// Прежде опыт зверю шёл за последний удар и только за него: добил — всё твоё, не
        /// добил — ничего. Оттого волк, продержавший врага весь бой, не получал ни очка, и
        /// значок над ним не всплывал почти никогда.
        ///
        /// Теперь считаем вложенное. Список живёт при самой жертве и уходит вместе с ней, так
        /// что чистить его не надо.
        /// </summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
            UnitAttribute, Dictionary<UnitAttribute, float>> blows =
            new System.Runtime.CompilerServices.ConditionalWeakTable<
                UnitAttribute, Dictionary<UnitAttribute, float>>();

        /// <summary>Records what this blow put into this victim.</summary>
        internal static void Blow(UnitAttribute victim, UnitAttribute hitter, float much)
        {
            if (!Enabled.Value || victim == null || hitter == null || much <= 0f) return;
            if (!Mine(hitter)) return;

            try
            {
                Dictionary<UnitAttribute, float> mine;

                if (!blows.TryGetValue(victim, out mine))
                {
                    mine = new Dictionary<UnitAttribute, float>();
                    blows.Add(victim, mine);
                }

                float was;
                mine[hitter] = (mine.TryGetValue(hitter, out was) ? was : 0f) + much;
            }
            catch
            {
            }
        }

        /// <summary>Shares out what the fallen was worth, by who actually spent him.</summary>
        internal static void Shared(UnitAttribute fallen, UnitAttribute killer)
        {
            if (!Enabled.Value || fallen == null) return;

            // Когда зверь ест из общего котла, своего счёта у него нет: иначе он получал бы
            // дважды за одну и ту же голову.
            if (Toll.Beasts != null && Toll.Beasts.Value) return;

            try
            {
                Dictionary<UnitAttribute, float> mine;

                if (!blows.TryGetValue(fallen, out mine) || mine.Count == 0)
                {
                    // Никто из зверей его не трогал — значит и делить нечего. Добивший зверь,
                    // если он всё же есть, своё получает целиком.
                    if (killer != null && Mine(killer)) Won(killer, fallen);
                    return;
                }

                // Делим по вложенному, а мерой берём всю жизнь павшего: не добили его звери —
                // не получат и всего.
                float whole = Mathf.Max(1f, fallen.maxhp);

                foreach (KeyValuePair<UnitAttribute, float> one in mine)
                {
                    Won(one.Key, fallen, Mathf.Clamp01(one.Value / whole));
                }

                blows.Remove(fallen);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог поделить добычу: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ груз

        private static readonly HashSet<int> stood = new HashSet<int>();

        /// <summary>What this beast can carry, by the same reckoning a man is held to.</summary>
        internal static float Room(UnitAttribute beast)
        {
            float might = 5f;

            try
            {
                if (Beastly.Enabled != null && Beastly.Enabled.Value)
                {
                    float[] six = Beastly.Mine(beast);
                    if (six != null && six.Length > 0) might = six[0];
                }
            }
            catch
            {
            }

            return Mathf.Max(1f, Burden.Base.Value + might * Burden.PerMight.Value);
        }

        /// <summary>
        /// Stops a beast that has been loaded past what it can carry.
        ///
        /// То же правило, что у человека и у лошади, и по той же причине: мешок на спине не
        /// бывает бесплатным. Считается силой зверя — а она у него выводится из размера, — так
        /// что собака унесёт хлеб и лук, а медведь комплект лат.
        /// </summary>
        internal static void Weigh(UnitAttribute beast)
        {
            if (!Enabled.Value || !Carries.Value || beast == null || beast.Data == null) return;
            if (!Mine(beast) || beast.items == null) return;

            try
            {
                float load = beast.items.SumWeight();
                float most = Room(beast);

                bool may = load <= most + 0.01f;

                CharacterSaveData data = (CharacterSaveData)(object)beast.Data;
                if (data.allowmove == may) return;

                data.allowmove = may;

                if (!may)
                {
                    if (stood.Add(beast.Data.id) && Telling.Value)
                    {
                        ItemForgePlugin.Log.LogInfo($"«{beast.Data.unitname}» встал под грузом: "
                            + $"{load:0.#} из {most:0.#}.");
                    }

                    GameController.ShowMessage($"«{beast.Data.unitname}» не унесёт столько: "
                        + $"{load:0.#} из {most:0.#}.", 3f);
                }
                else
                {
                    stood.Remove(beast.Data.id);
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог взвесить поклажу зверя: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ эволюция

        // С какого уровня зверь считает свой путь до следующего превращения. В сохранение это
        // не пишется, и после загрузки счёт начинается заново — зверь от этого не теряет
        // ничего, кроме нескольких уровней ожидания.
        private static readonly Dictionary<UnitAttribute, int> since =
            new Dictionary<UnitAttribute, int>();

        /// <summary>The next thing of this kind, or null when the kind has no elder.</summary>
        private static string Next(string asset)
        {
            if (string.IsNullOrEmpty(asset)) return null;

            // Юнити приписывает «(Clone)» всему, что создано из образца, и порода созданного
            // нами зверя может прийти сюда с этим хвостом. По хвосту лестница не ищется.
            int tail = asset.IndexOf("(Clone)", StringComparison.Ordinal);
            if (tail > 0) asset = asset.Substring(0, tail).Trim();

            foreach (string kind in (Ladders.Value ?? "").Split(';'))
            {
                string[] steps = kind.Split(',');

                for (int i = 0; i < steps.Length; i++)
                {
                    if (!string.Equals(steps[i].Trim(), asset, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    return (i + 1 < steps.Length) ? steps[i + 1].Trim() : null;
                }
            }

            return null;
        }

        /// <summary>
        /// Decides whether this beast has grown into something else.
        ///
        /// Отсчёт ведётся от того уровня, на котором зверь стал старше в прошлый раз, и этот
        /// уровень лежит в записи. Прежде он жил только в памяти: всякая загрузка сбрасывала
        /// его на текущий, и лестница растягивалась — волк, доросший до короля, шёл к
        /// следующей ступени не десять уровней, а столько, сколько игрок ни разу не выходил
        /// из игры подряд.
        ///
        /// У зверя, взятого до этой правки, отсчёт начинается с уровня, на котором его
        /// застали: иначе он превратился бы разом, едва запись завелась.
        /// </summary>
        private static void Ripe(UnitAttribute beast)
        {
            if (!Evolve.Value || beast == null || beast.Data == null) return;

            Book book = Kept(beast);

            if (book.grew <= 0)
            {
                book.grew = beast.Data.level;
                Write();
                return;
            }

            if (beast.Data.level - book.grew < EvolveEvery.Value) return;

            book.grew = beast.Data.level;
            since[beast] = beast.Data.level;
            Write();

            string kind = beast.info != null ? beast.info.name : null;
            string next = Next(kind);

            if (Telling.Value)
            {
                ItemForgePlugin.Log.LogInfo($"Пора расти: «{beast.Data.unitname}» ({kind}) "
                    + $"на {beast.Data.level} уровне, следом "
                    + (next ?? "никого — расти будет ростом") + ".");
            }

            if (next != null) Become(beast, next);
            else Bigger(beast);
        }

        /// <summary>
        /// Puts the elder beast where the younger one stood.
        ///
        /// Дорога та же, какой покупается лошадь: родить нового на том же месте, перенести на
        /// него всё своё и убрать прежнего. Переносится сумка, уровень и кличка — последняя
        /// только если её давали: волк, которого никто не называл, становится баргестом и
        /// зовётся баргестом, а Серый остаётся Серым.
        /// </summary>
        private static void Become(UnitAttribute beast, string next)
        {
            try
            {
                if (AreaManager.Instance == null) return;

                HumaniodUnit owner = Owner(beast);

                Vector3 spot = beast.transform.position;
                Vector3 turn = beast.transform.eulerAngles;

                int level = beast.Data.level;
                string was = beast.info != null ? beast.info.name : "?";

                // Как его звали и что в этом имени от породы.
                //
                // Зверя эта игра зовёт «прозвище + порода»: «злой Wolf». Прежде на новую форму
                // переезжало всё имя целиком, и оборотень до конца дней оставался злым волком.
                // Оттого имя разбирается: прозвище — его, порода — не его.
                string called = beast.Data.unitname;
                string kind = beast.info != null ? beast.info.unitName : null;

                string nick = null;
                bool named = false;

                if (!string.IsNullOrEmpty(called) && !string.IsNullOrEmpty(kind))
                {
                    if (called == kind)
                    {
                        // Безымянный: пусть зовётся новой породой.
                    }
                    else if (called.EndsWith(kind, StringComparison.Ordinal))
                    {
                        nick = called.Substring(0, called.Length - kind.Length);
                    }
                    else
                    {
                        // Кличка, данная руками: она не про породу, её не трогаем.
                        named = true;
                    }
                }

                UnitAttribute grown = AreaManager.Instance.CreateCharacter(
                    next, spot, turn, beast.Data.team);

                if (grown == null)
                {
                    ItemForgePlugin.Log.LogWarning($"Зверя «{next}» в базе нет, "
                        + "превращение отменено.");
                    return;
                }

                // Сперва пожитки, потом всё прочее: старого мы удаляем насовсем, и что не
                // перенесено — пропало.
                if (beast.items != null && beast.items.items != null && grown.items != null)
                {
                    foreach (Inventory thing in new List<Inventory>(beast.items.items))
                    {
                        if (thing == null) continue;

                        beast.items.RemoveInventory(thing, 0);
                        grown.items.AddInventoryNoEvent(thing);
                    }
                }

                grown.Data.level = level;

                // Новорождённый родится с нравом своей породы — то есть диким. Возвращаем.
                Fierce(grown);

                if (named)
                {
                    grown.Data.unitname = called;
                }
                else if (nick != null && grown.info != null
                    && !string.IsNullOrEmpty(grown.info.unitName))
                {
                    grown.Data.unitname = nick + grown.info.unitName;
                }

                // Запись переезжает за зверем: он рождается заново, с новым номером, а
                // вложенное в него и заработанное им остаётся тем же.
                try
                {
                    string from = Archive() + "|з" + beast.Data.id;
                    string into = Archive() + "|з" + grown.Data.id;

                    Book carried;
                    if (from != into && books.TryGetValue(from, out carried))
                    {
                        books[into] = carried;
                        books.Remove(from);
                        Write();
                    }
                }
                catch
                {
                }

                Hide(beast);
                tamed.Remove(beast.Data.id);
                    since.Remove(beast);

                if (owner != null && owner.warpet == beast) owner.RemoveWarpet(beast);

                AreaManager.Instance.DeleteCharacter(beast, trueDelete: true);

                if (owner != null) owner.AddWarpet(grown);

                grown.Data.canKill = true;
                grown.Data.useCull = false;
                grown.isMouseSelectable = true;

                grown.UpdateAttribute();

                tamed.Add(grown.Data.id);
                since[grown] = level;

                Follow(grown);
                Show(grown);

                if (Telling.Value)
                {
                    ItemForgePlugin.Log.LogInfo($"«{was}» вырос в «{next}» на {level} уровне.");
                }

                GameController.ShowMessage($"«{grown.Data.unitname}» вырос.", 3f);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог вырастить зверя в старшего: " + e);
            }
        }

        /// <summary>
        /// Grows the beast itself, when its kind has no elder to grow into.
        ///
        /// Размер в этой игре — не украшение: из него и уровня выводятся все шесть статов
        /// зверя, то есть он и есть его сила. Оттого громадный медведь — это не тот же медведь
        /// покрупнее, а другой противник.
        /// </summary>
        private static void Bigger(UnitAttribute beast)
        {
            if (!Sizes.Value || beast == null) return;
            if ((int)beast.size >= (int)UnitSize.Titanic) return;

            try
            {
                UnitSize was = beast.size;
                beast.size = (UnitSize)((int)beast.size + 1);

                if (Scale.Value > 0f)
                {
                    beast.transform.localScale *= 1f + Scale.Value;
                }

                beast.UpdateAttribute();

                if (Telling.Value)
                {
                    ItemForgePlugin.Log.LogInfo($"«{beast.Data.unitname}» вырос из {was} "
                        + $"в {beast.size} на {beast.Data.level} уровне.");
                }

                GameController.ShowMessage($"«{beast.Data.unitname}» вырос.", 3f);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог вырастить зверя в размере: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ перепись школы

        private static bool counted;

        /// <summary>
        /// Writes down what the game's own taming school actually holds.
        ///
        /// Школа в перечне есть, а есть ли в ней заклинания — из кода не видно: они лежат в
        /// ресурсах. Спор об этом решается не рассуждением, а списком.
        /// </summary>
        internal static void Census()
        {
            if (counted || !Enabled.Value || !Telling.Value) return;

            try
            {
                UISpellDatabase db = UISpellDatabase.Instance;
                if (db == null || db.spells == null) return;

                counted = true;

                List<string> found = new List<string>();

                foreach (UISpellInfo one in db.spells)
                {
                    if (one == null || one.SkillSet != SkillSet.Tamer) continue;

                    found.Add(((UnityEngine.Object)one).name);
                }

                ItemForgePlugin.Log.LogInfo(found.Count > 0
                    ? $"Школа приручения: заклинаний {found.Count} — "
                        + string.Join(", ", found.ToArray())
                    : "Школа приручения пуста: заклинаний за ней не числится.");

                Tree();
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог переписать школу приручения: " + e.Message);
            }
        }

        /// <summary>
        /// Writes down the tamer's own talent tree, node by node, as the game holds it.
        ///
        /// Переделывать то, чего не читал, нельзя. Ветка «Укротитель зверей» в игре есть, и
        /// что раздают её девять узлов — записано в базе талантов, а не в коде: имя, сколько
        /// в узле ступеней, какие прибавки и какое заклинание к нему привязано. Выгружаем всё
        /// это в файл рядом с модом, один узел в строку, и уже по нему разговариваем о том,
        /// что в ветке поменять.
        /// </summary>
        private static void Tree()
        {
            try
            {
                UITalentDatabase db = UITalentDatabase.Instance;
                if (db == null || db.talents == null) return;

                List<string> lines = new List<string>();
                lines.Add("--- ветка «Укротитель зверей», как её держит игра ---");

                int much = 0;

                foreach (UITalentInfo one in db.talents)
                {
                    if (one == null || one.talentClass != SkillSet.Tamer) continue;

                    much++;

                    string bonuses = "";

                    if (one.addAttrs != null && one.addAttrs.Count > 0)
                    {
                        List<string> parts = new List<string>();

                        foreach (AddonAttributes a in one.addAttrs)
                        {
                            if (a != null) parts.Add($"{a.type} {a.value}");
                        }

                        bonuses = " | прибавки: " + string.Join(", ", parts.ToArray());
                    }

                    string spell = one.connectedSpell != null
                        ? $" | заклинание {one.connectedSpell.Name} ({((UnityEngine.Object)one.connectedSpell).name})"
                        : "";

                    string desc = (one.description ?? "")
                        .Replace((char)13, (char)32)
                        .Replace((char)10, (char)32);

                    lines.Add($"[{one.ID}] {one.Name} ({((UnityEngine.Object)one).name}) | ступеней "
                        + $"{one.maxPoints}, нужен уровень {one.RequireLevel}, мастерство "
                        + $"{one.RequireMastery}{bonuses}{spell} | {desc}");
                }

                if (much == 0)
                {
                    ItemForgePlugin.Log.LogInfo("Ветки приручения в базе талантов нет.");
                    return;
                }

                string dir = System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);

                string file = System.IO.Path.Combine(dir, "tamer_tree.txt");

                System.IO.File.WriteAllLines(file, lines.ToArray());

                ItemForgePlugin.Log.LogInfo($"Ветка приручения выписана: узлов {much}, «{file}».");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог выписать ветку приручения: " + e.Message);
            }
        }
    }

    // Приручение и покупка идут одной дверью: «AddWarpet». Здесь спрашиваем, есть ли зверю
    // место в отряде, и здесь же запоминаем взятого.
    [HarmonyPatch(typeof(HumaniodUnit), "AddWarpet")]
    internal static class AddWarpet_Taming_Patch
    {
        private static bool Prefix(HumaniodUnit __instance, UnitAttribute pet)
        {
            if (!Taming.Enabled.Value) return true;

            try { return Taming.Room(__instance, pet); }
            catch { return true; }
        }

        private static void Postfix(HumaniodUnit __instance, UnitAttribute pet)
        {
            if (!Taming.Enabled.Value) return;

            try { Taming.Took(__instance, pet); }
            catch { }
        }
    }

    // Отпустили или потеряли — место возвращается отряду.
    [HarmonyPatch(typeof(HumaniodUnit), "RemoveWarpet")]
    internal static class RemoveWarpet_Taming_Patch
    {
        private static void Postfix()
        {
            if (!Taming.Enabled.Value) return;

            try { Taming.Seats(); }
            catch { }
        }
    }


    // Правое меню зверя: сумка, приказы и поведение в бою.
    [HarmonyPatch(typeof(RightClickMenuManager), "ShowRightClickMenu")]
    internal static class ShowRightClickMenu_Taming_Patch
    {
        private static void Prefix(RightClickMenuManager __instance,
            Dictionary<string, UnityAction> menuList)
        {
            if (!Taming.Enabled.Value || menuList == null) return;

            try
            {
                UnitAttribute target = AccessTools
                    .Field(typeof(RightClickMenuManager), "target")
                    .GetValue(__instance) as UnitAttribute;

                // Чужой в меню: если у отряда есть зверь и этот чужой ему враг — можно
                // натравить. Это и есть «кого атаковать», и другого способа отдать такой
                // приказ мышью игра не знает.
                if (target != null && !Taming.Mine(target))
                {
                    UnitAttribute beast = Taming.Chosen();

                    if (beast != null && beast != target && target.Data != null
                        && !target.Data.isdead && GameController.IsEnemy(beast, target))
                    {
                        UnitAttribute prey = target;
                        Add(menuList, "Натравить зверя", delegate { Taming.Sic(beast, prey); });
                    }
                }

                if (target == null || !Taming.Mine(target)) return;
                if (target.Data == null || target.Data.team != Faction.player) return;

                foreach (KeyValuePair<string, UnityAction> one in Taming.Board(target))
                {
                    Add(menuList, one.Key, one.Value);
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог дописать меню зверя: " + e);
            }
        }

        // «Отмена» всегда остаётся последней: меню читается сверху вниз, и выход из него
        // должен быть там, где его ищут.
        private static void Add(Dictionary<string, UnityAction> menu, string name, UnityAction what)
        {
            if (menu.ContainsKey(name)) return;

            UnityAction cancel;
            if (!menu.TryGetValue("Cancel", out cancel)) { menu.Add(name, what); return; }

            menu.Remove("Cancel");
            menu.Add(name, what);
            menu.Add("Cancel", cancel);
        }
    }

    // Щелчок по миру игра разбирает здесь и отдаёт приказ тому, кем играют. Зверем играть
    // нельзя — всё, что делает человека играемым, от окна снаряжения до разговоров, писано под
    // человека. Поэтому перехватываем сам щелчок: с зажатой клавишей приказ уходит зверю, и
    // родной разбор пропускается, чтобы герой не побежал туда же.
    [HarmonyPatch(typeof(GameController), "HandleCommandToRayTarget")]
    internal static class HandleCommandToRayTarget_Taming_Patch
    {
        private static bool Prefix()
        {
            if (!Taming.Enabled.Value || Taming.Orders == null || !Taming.Orders.Value) return true;

            try
            {
                if (!Input.GetKey(Taming.OrderKey.Value)) return true;

                UnitAttribute beast = Taming.Chosen();
                if (beast == null) return true;

                return !Taming.Order(beast);
            }
            catch
            {
                return true;
            }
        }
    }

    // Второй ход того же щелчка: пока кнопка зажата, игра ведёт героя за курсором сама, каждый
    // кадр и мимо разбора приказов. Оттого с зажатым Alt шли оба — зверь по приказу, герой по
    // этой дорожке. Пока клавиша приказа держится, герой стоит.
    [HarmonyPatch(typeof(GameController), "HoldMounseButton")]
    internal static class HoldMounseButton_Taming_Patch
    {
        private static bool Prefix()
        {
            if (!Taming.Enabled.Value || Taming.Orders == null || !Taming.Orders.Value) return true;

            try
            {
                if (Taming.OrderKey.Value == KeyCode.None) return true;
                if (!Input.GetKey(Taming.OrderKey.Value)) return true;

                return Taming.Chosen() == null;
            }
            catch
            {
                return true;
            }
        }
    }

    // Опыт зверю начисляет смерть его добычи — там же, где игра начисляет его человеку, и по
    // тому же числу.
    // «Стоять» приходит не только из нашего меню.
    //
    // Своё меню мы собрали, а игрок жмёт игровое: у питомца есть родной список приказов, и
    // «Стоять» там своё. Наша запись о месте не заводилась, зверь честно замирал — и через шаг
    // игровой присмотр цеплял поводок обратно и вёл его следом.
    //
    // Ловим не меню, а сам приказ: через него проходят все дороги — родное меню, наше, горячая
    // клавиша и приказ, отданный отряду разом.
    [HarmonyPatch(typeof(UnitStateMachine), "HandleCommand",
        new[] { typeof(UnitCommand), typeof(bool) })]
    internal static class Command_Taming_Patch
    {
        private static void Postfix(UnitStateMachine __instance, UnitCommand command)
        {
            try
            {
                if (Taming.Enabled == null || !Taming.Enabled.Value) return;
                if (__instance == null || command == null) return;

                UnitAttribute beast = __instance.unit;
                if (beast == null || beast.Data == null || !Taming.Mine(beast)) return;

                // Только по этому приказу и ни по какому другому.
                //
                // Здесь стояло ещё и «stop», и это было тихой бедой. «Stop» в этой игре
                // раздают все кому не лень: разговоры, отряд, переходы между областями,
                // собственный разум существа, когда тому некуда идти, — сотни мест. Всякий
                // такой «стоп» намертво ставил зверя держать место, а держащий место не
                // нападает ни на кого и никогда. Приручённый волк стоял столбом и смотрел,
                // как хозяина едят.
                if (command.name == commandsName.standGuard)
                {
                    Taming.Stand(beast);
                }
                else if (command.name == commandsName.follow)
                {
                    Taming.Unpost(beast);
                }
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "Die")]
    internal static class Die_Taming_Patch
    {
        private static void Postfix(UnitAttribute __instance, UnitAttribute killer)
        {
            if (!Taming.Enabled.Value) return;

            try { Taming.Shared(__instance, killer); }
            catch { }
        }
    }

    // Каждый удар записывается на того, кто его нанёс: по этому и делится добыча.
    [HarmonyPatch(typeof(UnitAttribute), "TakeDamage")]
    internal static class TakeDamage_Taming_Patch
    {
        private static void Postfix(UnitAttribute __instance, Attack attack)
        {
            if (!Taming.Enabled.Value || attack == null) return;

            try
            {
                Taming.Blow(__instance, attack.attacker, attack.realDamage);
            }
            catch
            {
            }
        }
    }

    // Наведённая подсказка у зверя показывала людские полки — «Полезные навыки» с единицами в
    // каждой строке. Игра их для зверя не заполняет и не прячет: окно одно на всех, и на нём
    // остаётся то, что показывал прошлый человек. Прячем сами.
    [HarmonyPatch(typeof(UIUnitTip), "SetValue")]
    internal static class SetValue_Taming_Patch
    {
        private static void Postfix(UIUnitTip __instance, CharacterSaveData nsd)
        {
            if (__instance == null || nsd == null || nsd is NPCSaveData) return;

            try
            {
                if (__instance.professionPanel != null)
                {
                    __instance.professionPanel.SetActive(false);
                }

                if (__instance.weaponMasteryPanel != null)
                {
                    __instance.weaponMasteryPanel.SetActive(false);
                }

                if (__instance.genresPanel != null)
                {
                    __instance.genresPanel.SetActive(false);
                }
            }
            catch
            {
            }
        }
    }
}
