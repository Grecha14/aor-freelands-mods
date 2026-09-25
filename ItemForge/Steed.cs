using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using PixelCrushers.DialogueSystem;
using UnityEngine;
using UnityEngine.Events;

namespace ItemForge
{
    /// <summary>
    /// A bought horse: it follows, it carries, it grows and it dies.
    ///
    /// Почти всё нужное у игры уже построено, и построено для другого. У каждого героя есть
    /// поле `warpet` — боевой питомец, один на человека: так бандиту достаётся волк. Питомец
    /// привязывается к хозяину, ходит за ним в `summonGroup`, переживает сохранение через
    /// `warpetID` и слушается команд `follow` и `standGuard`, потому что команды живут на
    /// `UnitAttribute`, а не на людях.
    ///
    /// Чего игра не даёт — лошади в ряду отряда. `partyMembers` это список людей, и запись
    /// туда лезет в снаряжение, в карьеру и в боевую формацию, которых у лошади нет. Поэтому
    /// лошадь ходит питомцем: своя ячейка в панели призванных у неё будет, строки в отряде нет.
    ///
    /// Две вещи игра делает не так, как нам надо, и обе поправлены здесь. Своих питомцев она
    /// объявляет бессмертными — `canKill = false`, — а купленная лошадь смертна, иначе она не
    /// вещь, а украшение. И родится она уже вьючной: вьючные модели в игре есть, с мешками на
    /// боках, ими ходят караваны торговцев.
    /// </summary>
    internal static class Steed
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Price;
        internal static ConfigEntry<string> Prices;
        internal static ConfigEntry<string> Loads;
        internal static ConfigEntry<string> Where;
        internal static ConfigEntry<bool> Keepers;
        internal static ConfigEntry<string> Trades;
        internal static ConfigEntry<bool> InTalk;
        internal static ConfigEntry<string> TalkLine;
        internal static ConfigEntry<bool> Reset;
        internal static ConfigEntry<bool> Telling;
        internal static ConfigEntry<string> Breeds;
        internal static ConfigEntry<string> Model;
        internal static ConfigEntry<float> Basic;
        internal static ConfigEntry<float> Carry;
        internal static ConfigEntry<float> Grows;
        internal static ConfigEntry<bool> Shy;
        internal static ConfigEntry<bool> Bolts;
        internal static ConfigEntry<float> Away;
        internal static ConfigEntry<int> Ceiling;

        // Сколько работы лошадь успела сделать с прошлого уровня, и где мы видели её в
        // последний раз. Работа — это пройденный путь, помноженный на долю занятой спины.
        private static readonly Dictionary<UnitAttribute, float> toil =
            new Dictionary<UnitAttribute, float>();

        private static Vector3 stood;
        private static bool seen;
        private static float ticked;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Steed", "Enabled", true,
                "Let a horse be bought and kept. It follows, it stands where it is told, it "
                + "carries what the party cannot, it learns from fighting and it can be "
                + "killed.");

            Price = config.Bind("Steed", "Price", 30000,
                new ConfigDescription(
                    "What a horse costs, in the game's own coin. A gold coin is ten thousand "
                    + "of these, so thirty thousand is three gold — about four ingots of "
                    + "mithril, which is the point: a horse is an investment and not a "
                    + "purchase.",
                    new AcceptableValueRange<int>(0, 10000000)));

            Prices = config.Bind("Steed", "Prices", "Camel=150000",
                "What particular breeds cost, where it is not the price above. Written as "
                + "breed=coin, several separated by commas. A camel goes for fifteen gold "
                + "against a horse's three: it is bought in the south, it carries half again "
                + "what a horse does and it does it on water a horse would die without.");

            Where = config.Bind("Steed", "Where", "Camel=desert,пустын,sand,dune,oasis",
                "Where a particular breed may be bought at all, written as breed=words. The "
                + "words are matched against the name of the world area the party stands in, "
                + "as pieces of it and without regard to case, so one word catches a whole "
                + "region. A camel is a beast of the desert and is not sold in the north; a "
                + "breed that is not written here is sold wherever it stands. "
                + "Имя области, в которой стоит отряд, пишется в журнал при каждой попытке "
                + "купить — по нему и уточняется этот список, если слова не совпали.");

            Loads = config.Bind("Steed", "Loads", "Camel=1.5",
                "And how much more such a breed carries, as a multiple of what its strength "
                + "would give it. Half again for a camel — that is what the beast is for.");

            Trades = config.Bind("Steed", "Trades", "Bartender,Merchant",
                "Which careers will sell a horse, by the game's own CareerType names. An inn is "
                + "where a traveller asks about horses, and a trader is where he buys one; "
                + "neither has to be standing beside the animal to sell it.");

            InTalk = config.Bind("Steed", "InTalk", true,
                "Put the offer into the innkeeper's own conversation, as a line among the "
                + "others, rather than only in the menu you get by right-clicking him. The "
                + "game adds lines to conversations at runtime itself — that is how a doctor "
                + "offers to treat each wounded man by name — and this is the same road.");

            TalkLine = config.Bind("Steed", "TalkLine", "Я хотел бы купить лошадь.",
                "What that line says.");

            Reset = config.Bind("Steed", "Reset", false,
                "Pull every horse the party owns back to the level its own kind is born at, "
                + "once, and then leave it alone. For horses bought before the summon-levelling "
                + "was fixed: the game had matched them to their owner and that number went "
                + "into the save, where no later fix reaches it. Turn this on, load, see the "
                + "log, turn it off.");

            Telling = config.Bind("Steed", "Telling", false,
                "Write down who was right-clicked and what the game thinks his career is. The "
                + "only honest way to learn whether a given innkeeper counts as one.");

            Keepers = config.Bind("Steed", "Keepers", true,
                "Let the innkeeper sell a horse as well. An inn is where a traveller asks about "
                + "horses, and a man who keeps one does not have to be standing beside it to "
                + "sell it. Right-click him and the offer is there.");

            Breeds = config.Bind("Steed", "Breeds",
                "Horse,horse_02,ArabianHorse,Pony,Camel,HorseCart,CartHorse_Caravan",
                "Which creatures can be bought, by their UnitInfo asset name, separated by "
                + "commas. Anything not on this list is somebody else's animal.");

            Model = config.Bind("Steed", "Model", "CargoHorse_Armour",
                "Which creature the bought horse is born as. The game ships six pack horses "
                + "with bags already on their flanks — CargoHorse_Armour, _Foods, _Material "
                + "and the rest — and they are what a merchant's caravan walks with. A bought "
                + "horse is born as one of them rather than having its model swapped, which "
                + "is the same route the game takes for its own pets.");

            Basic = config.Bind("Steed", "Basic", 40f,
                new ConfigDescription(
                    "How much a horse can carry before its strength is counted at all, in "
                    + "kilogrammes. This is the packsaddle rather than the animal, and it does "
                    + "not care how young the horse is: a fresh one is worth owning from the "
                    + "day it is bought, and what it learns afterwards comes on top.",
                    new AcceptableValueRange<float>(0f, 500f)));

            Carry = config.Bind("Steed", "Carry", 3f,
                new ConfigDescription(
                    "How much every point of strength adds to that, in kilogrammes. Most of "
                    + "what a horse carries is the horse and not the saddle, which is the "
                    + "point: a grown animal is worth visibly more than one bought yesterday, "
                    + "and there is a reason to work it.",
                    new AcceptableValueRange<float>(0f, 50f)));

            Grows = config.Bind("Steed", "Grows", 4000f,
                new ConfigDescription(
                    "How much work a horse must do to gain a level, multiplied by the level it "
                    + "already has. Work is distance walked times the share of its back that "
                    + "is loaded: a mile under a full pack counts for a mile, the same mile "
                    + "empty counts for nothing. "
                    + "It was damage dealt before, and that was wrong twice over. The game "
                    + "turns a pet's own wits off, so a horse never strikes anything and never "
                    + "grew at all; and a pack animal's strength comes from what it carries, "
                    + "not from what it bites.",
                    new AcceptableValueRange<float>(1f, 10000000f)));

            Shy = config.Bind("Steed", "Shy", true,
                "Keep the horse out of fights. A pack animal is not a fighter and never learns "
                + "to be one: it carries everything the party owns, and losing it to a stray "
                + "axe costs more than the fight was worth. While the party is engaged it "
                + "stands off; when the fighting ends it comes back.");

            Bolts = config.Bind("Steed", "Bolts", true,
                "And let it bolt outright when something actually hits it. A horse that has "
                + "been struck does not stand off politely, it runs — and that is the right "
                + "answer for the party too, because whatever is hitting it is not the thing "
                + "they can afford to let it stand near.");

            Keep = config.Bind("Steed", "Keep", 10f,
                new ConfigDescription(
                    "How far behind its owner the horse walks, in metres, unless told to come. "
                    + "Close enough to be seen, far enough not to be underfoot.",
                    new AcceptableValueRange<float>(2f, 50f)));

            Away = config.Bind("Steed", "Away", 18f,
                new ConfigDescription(
                    "How far from its owner the horse stands off during a fight, in metres. Far "
                    + "enough to be out of the swinging, near enough to be walked back to.",
                    new AcceptableValueRange<float>(5f, 100f)));

            Ceiling = config.Bind("Steed", "Ceiling", 60,
                new ConfigDescription(
                    "The level a horse stops growing at.",
                    new AcceptableValueRange<int>(1, 300)));
        }

        // ------------------------------------------------------------------ кто есть кто

        /// <summary>True when this creature is one somebody can be sold.</summary>
        internal static bool Buyable(UnitAttribute who)
        {
            if (!Enabled.Value || who == null || who.info == null) return false;
            if (who.Data == null || who.Data.isdead) return false;

            // Уже чья-то — ни своя, ни продажная.
            if (who.Data.isWarpet) return false;

            string asset = who.info.name ?? "";

            foreach (string one in (Breeds.Value ?? "").Split(','))
            {
                if (string.Equals(one.Trim(), asset, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when this is one of our bought horses, whatever state it is in now.
        ///
        /// Проверяем по самому существу и больше ни по чему. На отметку «это питомец»
        /// опираться нельзя, и это стоило одной поломки: при загрузке области игра пересоздаёт
        /// питомца, кладёт его в группу призванных и равняет по хозяину — а отметку
        /// восстанавливает уже после. Проверка по отметке в тот миг отвечала «нет», и лошадь
        /// получала хозяйский уровень, хотя при покупке была первого.
        ///
        /// Дикую лошадь это не задевает: никто её не призывает, и равнять её не с кем.
        /// </summary>
        internal static bool Mine(UnitAttribute who)
        {
            if (!Enabled.Value || who == null || who.info == null || who.Data == null) return false;

            string asset = who.info.name ?? "";

            if (string.Equals(asset, (Model.Value ?? "").Trim(),
                    StringComparison.OrdinalIgnoreCase)) return true;

            foreach (string one in (Breeds.Value ?? "").Split(','))
            {
                if (string.Equals(one.Trim(), asset, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when this man keeps an inn and could sell a horse.</summary>
        internal static bool Keeper(UnitAttribute who)
        {
            if (!Enabled.Value || !Keepers.Value) return false;

            HumaniodUnit man = who as HumaniodUnit;
            if (man == null || man.Data == null || man.Data.isdead) return false;

            string his = man.Data.career.ToString();

            foreach (string one in (Trades.Value ?? "").Split(','))
            {
                if (string.Equals(one.Trim(), his, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Says who was clicked and what the game calls him, when asked to.</summary>
        internal static void Note(UnitAttribute who)
        {
            if (!Telling.Value || who == null) return;

            HumaniodUnit man = who as HumaniodUnit;

            ItemForgePlugin.Log.LogInfo($"Меню на «{(who.Data != null ? who.Data.unitname : "?")}» "
                + $"(существо «{(who.info != null ? who.info.name : "?")}»): "
                + $"карьера {(man != null && man.Data != null ? man.Data.career.ToString() : "нет")}, "
                + $"продаст лошадь: {(Buyable(who) || Keeper(who) ? "да" : "нет")}.");
        }

        /// <summary>Finds a horse to sell: any loose one in the area, else born on the spot.</summary>
        private static UnitAttribute Stabled(UnitAttribute keeper)
        {
            try
            {
                UnitAttribute[] all = UnityEngine.Object.FindObjectsOfType<UnitAttribute>();
                UnitAttribute nearest = null;
                float best = float.MaxValue;

                foreach (UnitAttribute one in all)
                {
                    if (one == null || !Buyable(one)) continue;

                    float far = (one.transform.position - keeper.transform.position).sqrMagnitude;
                    if (far >= best) continue;

                    best = far;
                    nearest = one;
                }

                return nearest;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>The innkeeper sells whichever horse stands nearest his door.</summary>
        internal static void FromKeeper(UnitAttribute keeper)
        {
            UnitAttribute horse = Stabled(keeper);

            if (horse == null)
            {
                GameController.ShowMessage("Лошадей сейчас нет", 2f);
                return;
            }

            Ask(horse);
        }

        /// <summary>True when this is a horse the player has already paid for.</summary>
        internal static bool Ours(UnitAttribute who)
        {
            if (!Mine(who) || who.Data == null) return false;

            return who.Data.team == Faction.player;
        }

        /// <summary>
        /// Whose horse this is.
        ///
        /// Раньше хозяин был записан полем `warpet`, и вместе с ним приходила вся машинерия
        /// призыва. Теперь хозяин — просто тот, кем играют: лошадь идёт за ним, как вьючная
        /// торговца идёт за торговцем, и ничьей собственностью в коде не числится.
        /// </summary>
        internal static HumaniodUnit Owner(UnitAttribute horse)
        {
            return gameManager.currentplayUnit;
        }

        // ------------------------------------------------------------------ покупка

        /// <summary>Asks the price, and buys if the answer is yes.</summary>
        internal static void Ask(UnitAttribute horse)
        {
            if (horse == null) return;

            HumaniodUnit buyer = gameManager.currentplayUnit;
            if (buyer == null) return;

            if (buyer.warpet != null || Already() != null)
            {
                GameController.ShowMessage("Лошадь у вас уже есть", 2f);
                return;
            }

            if (!Here(horse))
            {
                GameController.ShowMessage("Здесь такую не держат", 2f);
                return;
            }

            try
            {
                // Верблюд и лошадь покупаются одной дверью, но за разные деньги и под
                // своим именем: кому что попалось на пути, тот то и берёт.
                bool camel = Table(Loads, horse, 1f) > 1.01f;
                int coin = Worth(horse);

                CostDialog.Show(camel ? "Купить верблюда" : "Купить лошадь",
                    camel
                        ? "Пойдёт за отрядом и повезёт в полтора раза против лошади."
                        : "Пойдёт за отрядом и повезёт то, что вам не поднять.",
                    coin, ManagementModeCore.wealth, delegate { Buy(horse, buyer); });
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог спросить цену за лошадь: " + e);
            }
        }

        /// <summary>Pays, and puts a pack horse where the plain one stood.</summary>
        private static void Buy(UnitAttribute horse, HumaniodUnit buyer)
        {
            if (horse == null || buyer == null) return;

            try
            {
                int coin = Worth(horse);

                if (ManagementModeCore.wealth < coin)
                {
                    GameController.ShowMessage("Не хватает денег", 2f);
                    return;
                }

                // Кем куплен, тем и остаётся: вьючная лошадь — вьючной, верблюд — верблюдом.
                // Своей вьючной породы у верблюда в игре нет, и подменять его лошадью значило
                // бы продать одно, а отдать другое.
                string born = Table(Loads, horse, 1f) > 1.01f && horse.info != null
                    ? horse.info.name
                    : Model.Value;

                Vector3 spot = horse.transform.position;
                Vector3 turn = horse.transform.eulerAngles;

                // Рождаем вьючную и убираем прежнюю. Порядок такой, а не обратный: если
                // рождение сорвётся, лучше остаться при своей лошади и при своих деньгах.
                UnitAttribute pack = AreaManager.Instance.CreateCharacter(
                    born, spot, turn, Faction.player);

                if (pack == null)
                {
                    ItemForgePlugin.Log.LogError(
                        $"Вьючной «{born}» в базе не нашлось, покупка отменена.");
                    GameController.ShowMessage("Купить не вышло", 2f);
                    return;
                }

                ManagementModeCore.TakeMoney(-coin);
                AreaManager.Instance.DeleteCharacter(horse, trueDelete: true);

                // Питомцем — и только ради одного: `warpetID` пишется в сохранение, и без
                // него лошадь пропадала при первой же загрузке. Спутник на стороне игрока —
                // обычное существо в области, и восстанавливать его некому.
                //
                // Уровень при этом больше не равняется: игра ровняет призванных по хозяину в
                // «MatchSummonLevel», и мы её туда не пускаем. Прежде эта преграда держалась
                // на отметке «это питомец», которой при загрузке ещё нет, — оттого лошадь и
                // выходила восемьдесят первой. Теперь опознаём по самому существу.
                buyer.AddWarpet(pack);

                // Своих питомцев игра объявляет бессмертными. Купленная лошадь смертна:
                // иначе она не спутник, а украшение при седле.
                pack.Data.canKill = true;
                pack.Data.useCull = false;

                // Без этого по ней нельзя щёлкнуть: игра строит правое меню только для того,
                // кого считает выбираемым мышью, и молча ничего не показывает для прочих.
                pack.isMouseSelectable = true;

                Follow(pack);
                Weigh(pack);

                ItemForgePlugin.Log.LogInfo(
                    $"«{buyer.Data.unitname}» купил лошадь за {Price.Value} "
                    + $"({Price.Value / 10000f:0.##} зол.), уровень {pack.Data.level}, "
                    + $"увезёт {Load(pack):0.#} кг.");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Покупка лошади сорвалась: " + e);
            }
        }

        // ------------------------------------------------------------------ разговор

        private const string Mark = "ItemForgeSteed";

        /// <summary>
        /// Puts the offer into a conversation, as one of its lines.
        ///
        /// Строится это по образцу самой игры: она так же на ходу дописывает врачу по строке
        /// на каждого раненого. Берём корень разговора, вешаем от него связь на новую строку,
        /// а от неё — обратно в корень, чтобы после покупки разговор не обрывался.
        ///
        /// Разговор находим не по имени — имени я не знаю и знать не обязан, — а по тому,
        /// который сейчас открылся. Кто собеседник, игра говорит сама.
        /// </summary>
        internal static void Offer(Conversation talk, UnitAttribute keeper)
        {
            if (!Enabled.Value || !InTalk.Value) return;
            if (talk == null || talk.dialogueEntries == null) return;
            if (!Keeper(keeper)) return;

            try
            {
                // Уже дописано — второй раз не надо: разговор живёт до конца игры.
                if (talk.dialogueEntries.Find(delegate (DialogueEntry x)
                        { return x.Title == Mark; }) != null) return;

                DialogueEntry root = talk.GetFirstDialogueEntry();
                if (root == null) return;

                // Корень — не то место. Из корня разговор идёт к приветствию, и повешенная
                // там строка встаёт репликой трактирщика, а не выбором игрока: ровно это и
                // случилось в первый раз.
                //
                // Выбор игрока строится из связей того узла, что сейчас звучит. Поэтому ищем
                // все узлы, из которых уже ведут связи к репликам игрока, — это и есть
                // развилки, — и вешаемся на каждую. Их больше одной: главное меню и «а
                // поговорим о чём-нибудь другом» — разные узлы с одним смыслом.
                // Игрок — это ActorID корня, а не ConversantID. Я взял наоборот, и вышло
                // ровно то, что должно было выйти: строка получила идентификатор трактирщика
                // и стала его репликой. В списке выбора её не показывали, потому что выбирать
                // чужие слова игрок не может.
                int mine = root.ActorID;
                int his = root.ConversantID;

                List<DialogueEntry> hubs = new List<DialogueEntry>();
                DialogueEntry sibling = null;

                foreach (DialogueEntry step in talk.dialogueEntries)
                {
                    if (step.outgoingLinks == null) continue;

                    foreach (Link went in step.outgoingLinks)
                    {
                        DialogueEntry to = talk.dialogueEntries.Find(delegate (DialogueEntry x)
                            { return x.id == went.destinationDialogueID; });

                        if (to == null || to.ActorID != mine) continue;

                        hubs.Add(step);

                        // Сосед по списку — лучший образец: он уже стоит в том самом меню,
                        // куда мы просимся, и всё, чем он туда попал, у него на виду.
                        if (sibling == null) sibling = to;
                        break;
                    }
                }

                if (hubs.Count == 0) hubs.Add(root);

                int id = 1;
                foreach (DialogueEntry step in talk.dialogueEntries)
                {
                    if (step.id >= id) id = step.id + 1;
                }

                DialogueEntry line = PixelCrushers.DialogueSystem.Template.FromDefault()
                    .CreateDialogueEntry(id, talk.id, Mark);

                // Чьими глазами сказана строка — не угадываем, а списываем у соседа. Игра
                // отбирает в меню игрока по этим самым полям, и один неверный идентификатор
                // означает не ошибку, а просто пустоту: строка есть, её не видно.
                if (sibling != null)
                {
                    line.ActorID = sibling.ActorID;
                    line.ConversantID = sibling.ConversantID;
                    line.isGroup = sibling.isGroup;
                }
                else
                {
                    line.ActorID = mine;
                    line.ConversantID = his;
                }

                line.DialogueText = TalkLine.Value;
                line.conditionsString = "";

                if (Telling.Value || sibling != null)
                {
                    ItemForgePlugin.Log.LogInfo($"Образец строки: "
                        + (sibling != null
                            ? $"«{sibling.DialogueText}» актёр {sibling.ActorID}, "
                              + $"собеседник {sibling.ConversantID}, группа {sibling.isGroup}"
                            : "соседа не нашлось, беру корень"));
                }

                // Что делать по выбору — говорим самой записи, а не патчем: у системы для
                // этого есть userScript, а угадывать подпись чужого метода я уже пробовал.
                line.userScript = "ItemForgeBuyHorse()";

                // Из каждой развилки к нам, и от нас обратно в ту, откуда пришли. Без
                // обратной связи разговор обрывался бы на покупке, а трактирщику есть что
                // ещё предложить.
                foreach (DialogueEntry hub in hubs)
                {
                    hub.outgoingLinks.Add(new Link(talk.id, hub.id, talk.id, line.id));
                }

                line.outgoingLinks.Add(new Link(talk.id, line.id, talk.id, hubs[0].id));

                talk.dialogueEntries.Add(line);

                asked = keeper;

                ItemForgePlugin.Log.LogInfo($"В разговор «{talk.Title}» дописана строка "
                    + $"«{TalkLine.Value}» (строка {id}), развилок {hubs.Count}.");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог дописать строку в разговор: " + e);
            }
        }

        // Кого спрашивали последним: строку выбирают уже без нас, и связать её с человеком
        // можно только так.
        private static UnitAttribute asked;

        private static bool listed;

        /// <summary>Tells the dialogue system what our line does when it is picked.</summary>
        internal static void Register()
        {
            if (listed) return;
            listed = true;

            try
            {
                Lua.RegisterFunction("ItemForgeBuyHorse", null,
                    PixelCrushers.DialogueSystem.SymbolExtensions.GetMethodInfo(() => Chosen()));

                ItemForgePlugin.Log.LogInfo("Покупка лошади в разговоре объявлена.");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог объявить покупку в разговоре: " + e);
            }
        }

        /// <summary>The line was chosen — so make the offer.</summary>
        public static void Chosen()
        {
            if (asked != null) FromKeeper(asked);
        }

        // ------------------------------------------------------------------ бережёного

        internal static ConfigEntry<float> Keep;

        // Каких лошадей позвали к себе: они идут рядом, как по игре. Остальные держатся поодаль.
        private static readonly HashSet<int> called = new HashSet<int>();

        internal static bool Called(UnitAttribute horse)
        {
            return horse != null && called.Contains(horse.GetInstanceID());
        }

        /// <summary>Come: walk close, the way the game has it, until told to keep off.</summary>
        internal static void Come(UnitAttribute horse)
        {
            if (horse == null) return;
            called.Add(horse.GetInstanceID());
            Follow(horse);
            ItemForgePlugin.Log.LogInfo("Лошадь подходит.");
        }

        /// <summary>Keep off: walk behind, at the distance set, and never underfoot.</summary>
        internal static void Off(UnitAttribute horse)
        {
            if (horse == null) return;
            called.Remove(horse.GetInstanceID());
            Follow(horse);
            ItemForgePlugin.Log.LogInfo($"Лошадь отходит и держится в {Keep.Value:0} м.");
        }

        /// <summary>
        /// One step of following at a distance.
        ///
        /// Игра ведёт спутника за хозяином на двух шагах и начинает догонять, как только он
        /// дальше трёх: для человека это правильно, а лошадь при этом путается под ногами.
        /// Здесь лошадь идёт сзади, на своём расстоянии: отстала — нагоняет, подошли к ней
        /// ближе — отходит, а в остальное время стоит и ждёт.
        /// </summary>
        internal static bool Trail(UnitAttribute horse)
        {
            if (!Enabled.Value || horse == null || horse.Data == null || horse.Data.isdead) return false;
            if (!Ours(horse) || Called(horse)) return false;

            UnitAttribute owner = horse.leader;
            if (owner == null) return false;

            Vector3 from = owner.transform.position;
            Vector3 way = horse.transform.position - from;
            way.y = 0f;

            float apart = way.magnitude;
            float keep = Mathf.Max(2f, Keep.Value);

            if (way.sqrMagnitude < 0.01f) way = -owner.transform.forward;
            Vector3 spot = from + way.normalized * keep;

            if (apart > keep + 2f)
            {
                horse.MoveTo(spot, apart > keep + 8f ? 1f : 0.5f);
            }
            else if (apart < keep - 4f)
            {
                horse.MoveTo(spot, 0.5f);
            }
            else if (horse.isMoving && Mathf.Abs(apart - keep) < 1f)
            {
                horse.Stop();
            }

            return true;
        }

        // Отведена ли уже: команду шлём однажды на бой, а не каждую секунду.
        private static bool aside;

        /// <summary>
        /// Keeps the horse out of the fighting, and brings it back when it is over.
        ///
        /// Вьючное животное не боец и бойцом не станет: на нём всё, что есть у отряда, и
        /// потерять его под случайным топором дороже любой драки. Пока отряд дерётся, лошадь
        /// стоит в стороне; кончилось — возвращается.
        /// </summary>
        internal static void Mind(UnitAttribute horse)
        {
            if (!Enabled.Value || !Shy.Value || horse == null || horse.Data == null) return;
            if (horse.Data.isdead || horse.stateMachine == null) return;

            bool fighting = false;

            try { fighting = PartyManager.instance != null && PartyManager.instance.partyEngaged; }
            catch { return; }

            if (fighting)
            {
                if (aside) return;
                aside = true;

                Step(horse);
                return;
            }

            if (!aside) return;

            aside = false;
            Follow(horse);

            ItemForgePlugin.Log.LogInfo("Бой кончился, лошадь идёт обратно.");
        }

        /// <summary>Walks the horse away from where the swinging is.</summary>
        private static void Step(UnitAttribute horse)
        {
            try
            {
                HumaniodUnit owner = Owner(horse);
                if (owner == null) return;

                horse.isFollower = false;
                horse.leader = null;
                horse.stateMachine.ClearChain();

                // Прочь от хозяина, а не в случайную сторону: он стоит там, где рубятся, и
                // отойти от него значит отойти от драки.
                Vector3 back = horse.transform.position - owner.transform.position;
                if (back.sqrMagnitude < 0.01f) back = -owner.transform.forward;

                Vector3 where = owner.transform.position + back.normalized * Away.Value;

                horse.stateMachine.HandleCommand(
                    new UnitCommand(commandsName.move, null, where));

                ItemForgePlugin.Log.LogInfo($"Отряд дерётся — лошадь отходит на {Away.Value:0} шагов.");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог отвести лошадь: " + e.Message);
            }
        }

        /// <summary>Something struck it — so it runs, and from that thing.</summary>
        internal static void Bolt(UnitAttribute horse, UnitAttribute from)
        {
            if (!Enabled.Value || !Bolts.Value || horse == null) return;
            if (!Ours(horse) || horse.stateMachine == null) return;

            try
            {
                aside = true;

                horse.isFollower = false;
                horse.leader = null;
                horse.stateMachine.ClearChain();

                horse.stateMachine.HandleCommand(from != null
                    ? new UnitCommand(commandsName.flee, from.gameObject, "AlwaysRun")
                    : new UnitCommand(commandsName.flee));

                ItemForgePlugin.Log.LogInfo("Лошадь ударили — бежит.");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог спугнуть лошадь: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ команды

        internal static void Follow(UnitAttribute horse)
        {
            aside = false;

            HumaniodUnit owner = Owner(horse);
            if (horse == null || owner == null || horse.stateMachine == null) return;

            horse.stateMachine.ClearChain();
            horse.leader = owner;
            horse.isFollower = true;
            horse.stateMachine.HandleCommand(
                new UnitCommand(commandsName.follow, owner.gameObject));
        }

        internal static void Stay(UnitAttribute horse)
        {
            if (horse == null || horse.stateMachine == null) return;

            horse.stateMachine.ClearChain();
            horse.isFollower = false;
            horse.leader = null;
            horse.stateMachine.HandleCommand(new UnitCommand(commandsName.standGuard));
        }

        internal static void Release(UnitAttribute horse)
        {
            if (horse == null || horse.Data == null) return;

            HumaniodUnit owner = Owner(horse);

            // Отпускаем тем же путём, каким брали: игра снимет отметку, вернёт ей свой ум и
            // уберёт из сохранения. Руками этого не сделать — останется висеть в записи.
            if (owner != null && owner.warpet == horse) owner.RemoveWarpet(horse);
            else horse.ChangeTeam(Faction.none);

            horse.isFollower = false;
            horse.leader = null;

            if (horse.stateMachine != null) horse.stateMachine.HandleStop();

            toil.Remove(horse);

            ItemForgePlugin.Log.LogInfo("Лошадь отпущена на волю.");
        }

        /// <summary>Opens the packsaddle, both ways.</summary>
        internal static void Pack(UnitAttribute horse)
        {
            if (horse == null || horse.items == null) return;
            if (LootManager.instance == null) return;

            // Контейнер отдаём тот, что на самой лошади, если он есть: окно закрывается
            // через «container.EndLoot()», и с пустотой вместо него кнопка закрытия падает,
            // не дойдя до «window.Hide()». Escape при этом работает — он идёт другой дорогой,
            // и оттого беда выглядит как каприз кнопки, а не как оборванный вызов.
            Container box = horse.GetComponent<Container>();

            LootManager.instance.StartLooting(horse.items, box);
        }

        // ------------------------------------------------------------------ груз

        /// <summary>How much this horse can carry, in kilogrammes.</summary>
        /// <summary>Число из таблицы «порода=число» для этой породы, или то, что по умолчанию.</summary>
        private static float Table(ConfigEntry<string> from, UnitAttribute who, float plain)
        {
            try
            {
                if (from == null || who == null || who.info == null) return plain;

                string asset = who.info.name ?? "";
                if (asset.Length == 0) return plain;

                foreach (string one in (from.Value ?? "").Split(','))
                {
                    int split = one.IndexOf('=');
                    if (split <= 0) continue;

                    if (!string.Equals(one.Substring(0, split).Trim(), asset,
                            StringComparison.OrdinalIgnoreCase)) continue;

                    float much;
                    if (float.TryParse(one.Substring(split + 1).Trim(),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out much))
                    {
                        return much;
                    }
                }
            }
            catch
            {
            }

            return plain;
        }

        /// <summary>Имя области, в которой стоит отряд.</summary>
        internal static string Land()
        {
            try
            {
                WorldTravelManager world = WorldTravelManager.instance;

                if (world != null && world.currentArea != null)
                {
                    string said = world.currentArea.areaName;
                    if (!string.IsNullOrEmpty(said)) return said;

                    return world.currentArea.name ?? "";
                }
            }
            catch
            {
            }

            return "";
        }

        /// <summary>
        /// Продаётся ли эта порода здесь.
        ///
        /// Верблюда держат в пустыне, а не в северном городе, и купить его надо там, где он
        /// водится. Сверяем не по точному имени области, а по куску его: имён у областей много,
        /// а пустыня одна на всех.
        /// </summary>
        internal static bool Here(UnitAttribute who)
        {
            try
            {
                if (Where == null || who == null || who.info == null) return true;

                string asset = who.info.name ?? "";
                if (asset.Length == 0) return true;

                string words = null;

                foreach (string one in (Where.Value ?? "").Split(';'))
                {
                    int split = one.IndexOf('=');
                    if (split <= 0) continue;

                    if (string.Equals(one.Substring(0, split).Trim(), asset,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        words = one.Substring(split + 1);
                        break;
                    }
                }

                // Про эту породу ничего не сказано — значит продаётся где стоит.
                if (words == null) return true;

                string land = Land();

                ItemForgePlugin.Log.LogInfo($"Область: «{land}», торг о «{asset}».");

                if (land.Length == 0) return true;

                foreach (string word in words.Split(','))
                {
                    string bit = word.Trim();

                    if (bit.Length > 0
                        && land.IndexOf(bit, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>What this particular beast is asked for.</summary>
        internal static int Worth(UnitAttribute who)
        {
            return Mathf.RoundToInt(Table(Prices, who, Price.Value));
        }

        internal static float Load(UnitAttribute horse)
        {
            if (horse == null) return 0f;

            // Шесть статов зверя лежат в том же порядке, что у людей, и сила стоит первой.
            float[] six = Beastly.Enabled.Value ? Beastly.Mine(horse) : null;
            float might = (six != null && six.Length > 0) ? six[0] : 10f;

            // Верблюд везёт больше при той же силе: на то он и верблюд.
            return (Basic.Value + Carry.Value * Mathf.Max(0f, might))
                * Table(Loads, horse, 1f);
        }

        /// <summary>
        /// Weighs what the horse is carrying, and stops it if that is too much.
        ///
        /// Правило одно на всех: человек под непосильным грузом встаёт, и лошадь встаёт тоже.
        /// Иначе вьючное животное превращается в дыру, куда сваливают всё подряд без счёта, и
        /// тогда незачем было и считать.
        /// </summary>
        internal static void Weigh(UnitAttribute horse)
        {
            if (!Enabled.Value || horse == null || horse.Data == null) return;
            if (!Mine(horse) || horse.items == null) return;

            // Выбираемость мышью теряется при загрузке вместе с прочим, что не пишется в
            // сохранение. Возвращаем на каждом пересчёте: иначе лошадь стоит, её видно, а
            // сделать с ней нельзя ничего.
            if (Ours(horse)) horse.isMouseSelectable = true;

            Born(horse);

            try
            {
                float load = horse.items.SumWeight();
                float most = Load(horse);

                bool may = load <= most + 0.01f;

                CharacterSaveData data = (CharacterSaveData)(object)horse.Data;
                if (data.allowmove == may) return;

                data.allowmove = may;

                ItemForgePlugin.Log.LogInfo(may
                    ? $"Лошадь пошла: {load:0.#} из {most:0.#} кг."
                    : $"Лошадь встала под грузом: {load:0.#} из {most:0.#} кг.");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог взвесить поклажу: " + e);
            }
        }

        private static readonly HashSet<int> pulled = new HashSet<int>();

        /// <summary>
        /// Puts a horse back to the level its own kind is born at, once.
        ///
        /// Для тех, что куплены прежде починки: игра тогда сравняла их с хозяином, и это число
        /// ушло в сохранение, куда никакая позднейшая правка не достаёт.
        ///
        /// Выше потолка лошадь откатывается сама, и рассуждение простое: своим трудом туда не
        /// добраться — рост останавливается на потолке. Значит уровень выше него мог взяться
        /// только от старого равнения по хозяину, и спрашивать тут нечего. Настройка остаётся
        /// для случая потяжелее: когда хозяин был ниже потолка и подделку по числу не отличить.
        /// </summary>
        private static void Born(UnitAttribute horse)
        {
            if (horse.info == null || horse.Data == null) return;

            bool overshot = horse.Data.level > Ceiling.Value;
            if (!Reset.Value && !overshot) return;

            if (!pulled.Add(horse.GetInstanceID())) return;

            int was = horse.Data.level;
            if (was <= horse.info.level) return;

            horse.Data.level = horse.info.level;
            horse.UpdateAttribute();

            ItemForgePlugin.Log.LogInfo($"Лошадь возвращена к своему уровню: {was} → "
                + $"{horse.Data.level} ("
                + (overshot ? $"выше потолка в {Ceiling.Value}, трудом столько не берётся"
                            : "по настройке Steed.Reset") + ").");
        }

        /// <summary>
        /// Gives a bought horse its mortality back after the game has taken it away.
        ///
        /// Своих питомцев игра объявляет бессмертными — `canKill = false` в `AddWarpet`, и то
        /// же самое ещё раз при загрузке области. Мы ставили смертность при покупке, и до
        /// первой перезагрузки она держалась, а после неё лошадь становилась неубиваемой.
        /// Теперь ставится здесь: `AddSummoned` вызывается последним на обеих дорогах —
        /// и когда лошадь покупают, и когда её поднимают из сохранения.
        /// </summary>
        internal static void Alive(UnitAttribute horse)
        {
            if (!Enabled.Value || horse == null || horse.Data == null) return;
            if (!Mine(horse) || horse.Data.canKill) return;

            horse.Data.canKill = true;

            if (mortal.Add(horse.GetInstanceID()))
            {
                ItemForgePlugin.Log.LogInfo("Лошади вернули смертность: игра записала её в "
                    + "неубиваемые питомцы.");
            }
        }

        private static readonly HashSet<int> mortal = new HashSet<int>();

        /// <summary>The horse the party already has, if it has one.</summary>
        internal static UnitAttribute Already()
        {
            try
            {
                foreach (UnitAttribute one in UnityEngine.Object.FindObjectsOfType<UnitAttribute>())
                {
                    if (one != null && Ours(one) && !one.Data.isdead) return one;
                }
            }
            catch
            {
            }

            return null;
        }

        /// <summary>Every horse the party owns, for when something has to be asked of all.</summary>
        internal static void WeighAll()
        {
            UnitAttribute horse = Already();
            if (horse != null) Weigh(horse);
        }

        // ------------------------------------------------------------------ рост

        /// <summary>
        /// Measures the work done since the last look, and grows the horse for it.
        ///
        /// Раз в секунду, а не каждый кадр: одна лошадь, одно вычитание векторов, и то нечасто.
        /// </summary>
        internal static void Tick()
        {
            if (!Enabled.Value) return;

            ticked += Time.unscaledDeltaTime;
            if (ticked < 1f) return;

            ticked = 0f;

            try
            {
                UnitAttribute horse = Already();

                if (horse == null || horse.Data == null || horse.Data.isdead)
                {
                    seen = false;
                    return;
                }

                // Отвести и вернуть надо и тогда, когда лошадь стоит на месте: иначе она
                // так и простоит в свалке, раз не сдвинулась ни на шаг.
                Mind(horse);

                Vector3 now = horse.transform.position;

                if (!seen) { stood = now; seen = true; return; }

                float went = Vector3.Distance(stood, now);
                stood = now;

                // Прыжок через полсотни шагов — это не дорога, а смена области. Такое не
                // считаем: иначе один переход по карте давал бы лошади уровень даром.
                if (went <= 0.05f || went > 50f) return;

                float most = Load(horse);
                if (most <= 0f || horse.items == null) return;

                float share = Mathf.Clamp01(horse.items.SumWeight() / most);
                if (share <= 0.01f) return;

                Dealt(horse, went * share);
                Mind(horse);
            }
            catch
            {
            }
        }

        /// <summary>Counts the work and grows the horse for it.</summary>
        internal static void Dealt(UnitAttribute horse, float damage)
        {
            if (!Enabled.Value || horse == null || damage <= 0f) return;
            if (!Ours(horse) || horse.Data == null) return;
            if (horse.Data.level >= Ceiling.Value) return;

            float done;
            toil.TryGetValue(horse, out done);
            done += damage;

            float needed = Grows.Value * Mathf.Max(1, horse.Data.level);

            if (done < needed)
            {
                toil[horse] = done;

                if (Telling.Value)
                {
                    ItemForgePlugin.Log.LogInfo($"Лошадь наработала {damage:0.#}, "
                        + $"всего {done:0} из {needed:0} до уровня {horse.Data.level + 1}.");
                }

                return;
            }

            toil[horse] = done - needed;
            horse.Data.level++;

            // Игра сама доращивает здоровье и урон, когда уровень особи выше чертёжного —
            // ровно тем же путём, каким растут звери в Wild.
            horse.UpdateAttribute();

            GameController.ShowMessage(
                $"{horse.Data.unitName}: уровень {horse.Data.level}", 2f);

            ItemForgePlugin.Log.LogInfo(
                $"Лошадь подросла до {horse.Data.level}, увезёт {Load(horse):0.#} кг.");
        }
    }

    // Правое меню собирается словарём, и словарь этот ещё не разложен по кнопкам, когда сюда
    // приходит управление. Дописываем в него своё, прочитав, на кого нажали.
    [HarmonyPatch(typeof(RightClickMenuManager), "ShowRightClickMenu")]
    internal static class ShowRightClickMenu_Steed_Patch
    {
        private static void Prefix(RightClickMenuManager __instance,
            Dictionary<string, UnityAction> menuList)
        {
            if (!Steed.Enabled.Value || menuList == null) return;

            try
            {
                UnitAttribute target = AccessTools
                    .Field(typeof(RightClickMenuManager), "target")
                    .GetValue(__instance) as UnitAttribute;

                if (target == null) return;

                Steed.Note(target);

                if (Steed.Buyable(target))
                {
                    Add(menuList, "Купить лошадь", delegate { Steed.Ask(target); });
                    return;
                }

                // Трактирщик торгует той, что стоит ближе прочих к его двери.
                if (Steed.Keeper(target))
                {
                    Add(menuList, "Купить лошадь", delegate { Steed.FromKeeper(target); });
                    return;
                }

                if (!Steed.Ours(target)) return;

                Add(menuList, "Поклажа", delegate { Steed.Pack(target); });
                Add(menuList, "Подойди", delegate { Steed.Come(target); });
                Add(menuList, "Отойди", delegate { Steed.Off(target); });
                Add(menuList, "Следовать за мной", delegate { Steed.Follow(target); });
                Add(menuList, "Стоять", delegate { Steed.Stay(target); });
                Add(menuList, "Отпустить", delegate { Steed.Release(target); });
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог дописать меню лошади: " + e);
            }
        }

        // «Отмена» всегда последняя: игра кладёт её сама, и наши пункты должны встать до неё.
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

    // Разговор начался — самое время дописать в него своё, пока строки ещё не разложены.
    // Имя разговора нам не нужно и знать его неоткуда: берём тот, который открылся, и того,
    // с кем он открылся.
    [HarmonyPatch(typeof(DialogueManager), "StartConversation",
        new[] { typeof(string), typeof(Transform), typeof(Transform), typeof(int) })]
    internal static class StartConversation_Steed_Patch
    {
        private static void Prefix(string title, Transform conversant)
        {
            if (!Steed.Enabled.Value || !Steed.InTalk.Value) return;

            try
            {
                if (conversant == null || DialogueManager.instance == null) return;

                UnitAttribute who = conversant.GetComponent<UnitAttribute>();
                if (who == null) who = conversant.GetComponentInParent<UnitAttribute>();
                if (who == null) return;

                Steed.Register();

                Conversation talk = DialogueManager.instance.masterDatabase.GetConversation(title);
                Steed.Offer(talk, who);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог войти в разговор: " + e);
            }
        }
    }

    // Призванных игра равняет по призывателю: «unit.Data.level = summoner.Data.level». Для
    // вызванного скелета это верно — он и есть продолжение своего мага. Купленная лошадь не
    // продолжение никого: она жила до встречи с хозяином и растёт своим трудом. Оттого её это
    // правило обходит стороной, а вместе с уровнем при ней остаются и её собственные статы.
    [HarmonyPatch(typeof(SummonGroup), "MatchSummonLevel")]
    internal static class MatchSummonLevel_Steed_Patch
    {
        private static bool Prefix(UnitAttribute unit)
        {
            // Тот же обход и для прирученных: пойманный зверь остаётся тем, кого поймали.
            return !Steed.Mine(unit) && !Taming.Mine(unit);
        }
    }

    // Последний шаг обеих дорог: и покупки, и подъёма из сохранения. Здесь игра уже успела
    // объявить лошадь неубиваемой, и здесь смертность возвращается.
    [HarmonyPatch(typeof(SummonGroup), "AddSummoned")]
    internal static class AddSummoned_Steed_Patch
    {
        private static void Postfix(UnitAttribute unit)
        {
            try { Steed.Alive(unit); }
            catch { }

            try { Taming.Alive(unit); }
            catch { }
        }
    }

    // По лошади ударили — она бежит. Ловим здесь: это единственное место, где известно и
    // кого ударили, и кто ударил.
    [HarmonyPatch(typeof(UnitAttribute), "TakeDamage")]
    internal static class TakeDamage_Bolt_Patch
    {
        private static void Postfix(UnitAttribute __instance, Attack attack, bool __result)
        {
            if (!Steed.Enabled.Value || !__result || attack == null) return;

            try { Steed.Bolt(__instance, attack.attacker); }
            catch { }
        }
    }

    // Пересчёт зверя: заодно смотрим, не навьючили ли на него больше, чем он поднимет.
    [HarmonyPatch(typeof(UnitAttribute), "WriteUnitAttribute")]
    internal static class WriteUnitAttribute_Steed_Patch
    {
        private static void Postfix(UnitAttribute __instance)
        {
            Steed.Weigh(__instance);

            try { Taming.Weigh(__instance); }
            catch { }
        }
    }

    // Своя лошадь следует за хозяином на своём расстоянии, а не на игровых двух шагах.
    [HarmonyPatch(typeof(FollowingState), "Following")]
    internal static class Following_Steed_Patch
    {
        private static bool Prefix(FollowingState __instance)
        {
            try
            {
                return !Steed.Trail(__instance.unit);
            }
            catch
            {
                return true;
            }
        }
    }
}
