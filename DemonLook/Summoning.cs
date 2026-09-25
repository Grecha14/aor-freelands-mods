using System;
using System.Globalization;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace DemonLook
{
internal static class Summoning
{
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<bool> OnlyNewGame;

    internal static ConfigEntry<string> MageUnit;

    internal static ConfigEntry<int> MageCount;

    internal static ConfigEntry<float> Radius;

    internal static ConfigEntry<string> RitualSpell;

    internal static ConfigEntry<int> CastTimes;

    internal static ConfigEntry<float> CastGap;

    internal static ConfigEntry<int> HeroCount;

    internal static ConfigEntry<bool> HeroInstant;

    internal static ConfigEntry<float> HeroDelay;

    internal static ConfigEntry<float> HeroDistance;

    internal static ConfigEntry<float> MageEdge;

    internal static ConfigEntry<string> ArrivalSpell;

    internal static ConfigEntry<bool> HideUntilCalled;

    internal static ConfigEntry<float> FromDoor;

    internal static ConfigEntry<int> DoorIndex;

    internal static ConfigEntry<string> WordsBefore;

    internal static ConfigEntry<string> WordsCasting;

    internal static ConfigEntry<string> WordsHeroes;

    internal static ConfigEntry<string> MageFaction;

    internal static ConfigEntry<float> MarchTime;

    internal static ConfigEntry<float> MarchClose;

    internal static ConfigEntry<string> HeroSpot;

    internal static ConfigEntry<string> HeroWay;

    internal static ConfigEntry<string> HeroEnd;

    internal static ConfigEntry<int> HeroLevel;
    internal static ConfigEntry<string> HeroAttributes;

    internal static ConfigEntry<float> MarkReach;

    internal static ConfigEntry<string> MageName;

    internal static ConfigEntry<float> MageChiefHp;

    internal static ConfigEntry<bool> Untouchable;

    internal static ConfigEntry<int> MageWit;

    internal static ConfigEntry<string> Persons;

    internal static ConfigEntry<int> MageSpellLevel;

    internal static ConfigEntry<string> HunterUnit;

    internal static ConfigEntry<bool> ArmTheirLeader;

    internal static ConfigEntry<string> LeaderTier;

    internal static ConfigEntry<string> LeaderQuality;

    internal static ConfigEntry<string> Roles;

    internal static ConfigEntry<string> MageRoles;

    internal static ConfigEntry<string> HunterRoles;

    internal static ConfigEntry<string> HunterTier;

    internal static ConfigEntry<string> HunterQuality;

    internal static ConfigEntry<string> HunterClad;

    internal static ConfigEntry<string> LeaderClad;

    internal static ConfigEntry<string> MageTier;

    internal static ConfigEntry<string> MageQuality;

    internal static ConfigEntry<string> MageArms;

    internal static ConfigEntry<string> Trinkets;

    internal static ConfigEntry<string> TrinketTier;

    internal static ConfigEntry<string> TrinketQuality;

    internal static ConfigEntry<int> Purse;

    internal static ConfigEntry<bool> MagesGuard;

    internal static ConfigEntry<string> WordsAfter;

    internal static ConfigEntry<string> WordsQuest;

    internal static ConfigEntry<float> AfterWait;

    internal static ConfigEntry<int> RitualPotential;
    internal static ConfigEntry<int> RitualAttributes;

    private static bool staged;

    private static bool counted;

    private static bool decided;

    private static readonly List<UnitInfo> party = new List<UnitInfo>();

    private static readonly List<UnitAttribute> came = new List<UnitAttribute>();

    // Маги круга. Прежде список жил только внутри сцены, а вражда и приказы в бою спрашивают
    // о нём снаружи: «этот — из круга?» и «кому драться с кем».
    private static readonly List<UnitAttribute> ring = new List<UnitAttribute>();

    // Где каждый маг стоял, когда круг ещё был кругом. Места мы и так задаём сами — остаётся
    // их не забыть, чтобы после боя вернуть всех туда же.
    private static readonly List<Vector3> places = new List<Vector3>();

    private static Vector3 heart;

    private static bool listed;

    private static bool spelled;

    // Кто уже стоит там, куда его поставили. Заполняется наблюдателем у каждого охотника:
    // так речь ждёт весь отряд, а не одного добежавшего.
    private static readonly HashSet<UnitAttribute> standing = new HashSet<UnitAttribute>();

    // Пока это правда, распорядитель боя раздаёт боевые приказы. С концом драки
    // он должен замолкать сразу, а не дожидаться своего срока: иначе он ещё три минуты
    // командует теми, кого сцена уже уводит обратно в круг.
    private static bool melee;

    private static bool guarded;

    private static bool told;

    internal static bool Truce;

    internal static void Bind(ConfigFile config)
    {
        //IL_0058: Unknown result type (might be due to invalid IL or missing references)
        //IL_0062: Expected O, but got Unknown
        //IL_0090: Unknown result type (might be due to invalid IL or missing references)
        //IL_009a: Expected O, but got Unknown
        //IL_00dc: Unknown result type (might be due to invalid IL or missing references)
        //IL_00e6: Expected O, but got Unknown
        //IL_0114: Unknown result type (might be due to invalid IL or missing references)
        //IL_011e: Expected O, but got Unknown
        //IL_0141: Unknown result type (might be due to invalid IL or missing references)
        //IL_014b: Expected O, but got Unknown
        //IL_0179: Unknown result type (might be due to invalid IL or missing references)
        //IL_0183: Expected O, but got Unknown
        //IL_01b1: Unknown result type (might be due to invalid IL or missing references)
        //IL_01bb: Expected O, but got Unknown
        //IL_01e9: Unknown result type (might be due to invalid IL or missing references)
        //IL_01f3: Expected O, but got Unknown
        //IL_0240: Unknown result type (might be due to invalid IL or missing references)
        //IL_024a: Expected O, but got Unknown
        //IL_026c: Unknown result type (might be due to invalid IL or missing references)
        //IL_0276: Expected O, but got Unknown
        //IL_0301: Unknown result type (might be due to invalid IL or missing references)
        //IL_030b: Expected O, but got Unknown
        //IL_0339: Unknown result type (might be due to invalid IL or missing references)
        //IL_0343: Expected O, but got Unknown
        //IL_0385: Unknown result type (might be due to invalid IL or missing references)
        //IL_038f: Expected O, but got Unknown
        //IL_048a: Unknown result type (might be due to invalid IL or missing references)
        //IL_0494: Expected O, but got Unknown
        //IL_051b: Unknown result type (might be due to invalid IL or missing references)
        //IL_0525: Expected O, but got Unknown
        Enabled = config.Bind<bool>("Summoning", "Enabled", true, "Begin a demon's game in the middle of the ritual that called him.");
        OnlyNewGame = config.Bind<bool>("Summoning", "OnlyNewGame", true, "Play the summoning only when a game is begun, never when one is loaded. The circle is called once in a life; without this, loading a save made in that cave stages the whole scene again on top of the one already played — fresh hunters, fresh summoners, and the floor knee-deep in their gear. Turn it off only to rehearse the scene without starting over.");
        MageUnit = config.Bind<string>("Summoning", "MageUnit", "DarkMage_T3", "Name of the UnitInfo asset to use for the summoners. If the name is unknown the scene is skipped and the names that do exist are written to the log, so it can be corrected without guessing twice.");
        MageCount = config.Bind<int>("Summoning", "MageCount", 5, new ConfigDescription("How many summoners stand in the ring.", (AcceptableValueBase)(object)new AcceptableValueRange<int>(2, 12), Array.Empty<object>()));
        Radius = config.Bind<float>("Summoning", "Radius", 4f, new ConfigDescription("How far the ring stands from the demon, in metres. Wide enough that the spells have somewhere to travel and the figures do not overlap.", (AcceptableValueBase)(object)new AcceptableValueRange<float>(2f, 15f), Array.Empty<object>()));
        RitualSpell = config.Bind<string>("Summoning", "RitualSpell", "BlackMist", "The spell the summoners throw. Black fog was chosen because it is dark, slow and wide, which reads as a working rather than as an attack.");
        CastTimes = config.Bind<int>("Summoning", "CastTimes", 3, new ConfigDescription("How many times each summoner casts it.", (AcceptableValueBase)(object)new AcceptableValueRange<int>(1, 15), Array.Empty<object>()));
        CastGap = config.Bind<float>("Summoning", "CastGap", 2.5f, new ConfigDescription("Seconds between casts.", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0.5f, 15f), Array.Empty<object>()));
        HeroInstant = config.Bind<bool>("Summoning", "HeroInstant", true, "Let the hunters be there already instead of walking in. The screen darkens, they take their places around the circle, the leader speaks, the light comes back and the fight begins. Marching looked better on paper and was a standing source of grief: a man who lost his way, a mark left over from another cave, a path that did not exist — any of them left the scene half-played. Nobody walks, nothing can go wrong.");

        HeroCount = config.Bind<int>("Summoning", "HeroCount", 10, new ConfigDescription("How many come to stop it. Ten against five, because numbers are what the world has and the circle has everything else: five men who can call a lord of hell are not in danger from ten men with swords, and the scene is not a contest — it is the demon watching what his summoners can do.", (AcceptableValueBase)(object)new AcceptableValueRange<int>(0, 12), Array.Empty<object>()));
        HeroLevel = config.Bind<int>("Summoning", "HeroLevel", 34, new ConfigDescription("What level the hunters come at, matched to the gear they wear: full plate of the third tier is a man of fifty, not of a hundred and fifty. A hundred and fifty in purple was the old number, and at it the hunters cut down the circle. Their templates are ordinary world units — a town marksman is level seven — and a party sent to stop a summoning would not be the town watch. Raising the level runs the game’s own level bonus, so they gain health and damage the way any levelled creature does.", (AcceptableValueBase)(object)new AcceptableValueRange<int>(1, 999), Array.Empty<object>()));
        HeroAttributes = config.Bind<string>("Summoning", "HeroAttributes", "30,24,20,18,8,15", "What the hunters are made of, as strength, endurance, agility, precision, intelligence and willpower. Eighty of strength and twenty of wit: these are knights sent to break a circle, not to argue with it. The six add up to the level above through the game’s own reckoning, so the number over their heads is not a claim their bodies cannot back. Empty, and the templates are left as they came.");
        MarkReach = config.Bind<float>("Summoning", "MarkReach", 60f, new ConfigDescription("How far from the circle a marked spot may be and still count, in metres. A mark taken by accident outside the cave lands a hundred and forty metres away and leaves its owner standing there for the whole scene; a mark taken properly is never further than a few dozen. Distance is the only honest test here: whether the engine can draw a complete path is not, because in a narrow cave with five men standing in the middle it almost never can, and the hunters walk it anyway.", (AcceptableValueBase)(object)new AcceptableValueRange<float>(5f, 500f), Array.Empty<object>()));
        HeroDelay = config.Bind<float>("Summoning", "HeroDelay", 12f, new ConfigDescription("Seconds before they arrive. Long enough that the ritual is seen finishing rather than interrupted at the first cast.", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 120f), Array.Empty<object>()));
        HeroDistance = config.Bind<float>("Summoning", "HeroDistance", 18f, new ConfigDescription("How far off they appear, in metres.", (AcceptableValueBase)(object)new AcceptableValueRange<float>(5f, 60f), Array.Empty<object>()));
        MageEdge = config.Bind<float>("Summoning", "MageEdge", 0.25f, new ConfigDescription("How much stronger the summoners are than the party sent against them, as a fraction. A quarter keeps the fight close while leaving the ritual likely to hold — which is what «roughly equal, mages slightly stronger» asks for.", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 2f), Array.Empty<object>()));
        MageName = config.Bind<string>("Summoning", "MageName", "Высший жрец Короля демонов;Высший жрец Короля демонов;Высший жрец Короля демонов;Высший жрец Короля демонов;Высший жрец Короля демонов", "What the summoners are called, one name per member, separated by semicolons — the last one leads the circle and is the one built to outlast the fight. Numbers were for my benefit while I was building the scene and had to know which of them fell first; the player has no such need and should be reading names. Fewer names than there are summoners and the rest are numbered.");
        MageChiefHp = config.Bind<float>("Summoning", "MageChiefHp", 3000f, new ConfigDescription("How much health the fifth summoner has. The circle has to outlast the fight: if the hunters cut down all five, nobody is left to close the ritual and the scene ends with nothing. One of them is built to hold — the one who leads it.", (AcceptableValueBase)(object)new AcceptableValueRange<float>(100f, 100000f), Array.Empty<object>()));
        Untouchable = config.Bind<bool>("Summoning", "Untouchable", true, "Let nothing touch the demon until the circle has finished. He stands in the middle of a fight he is not allowed to join — held still, unable to step aside, and worth killing to everyone in the cave. Dying there would end the story before it began, and surviving it by luck is no better: the scene is not a fight he is having, it is a fight being had over him.");
        MageWit = config.Bind<int>("Summoning", "MageWit", 100, new ConfigDescription("What wit the summoners have. Magic damage in this game leans on intelligence, and the templates these five are built from are ordinary sorcerers with ordinary heads — which is why a circle that raised a lord of hell threw fireballs like a village hedge-witch. A hundred is not a number a person reaches; it is the number that makes them what the scene says they are.", (AcceptableValueBase)(object)new AcceptableValueRange<int>(1, 500), Array.Empty<object>()));
        Persons = config.Bind<string>("Summoning", "Persons", "Высший жрец Короля демонов|130|10,60,30,15,120,40|0|15;Высший жрец Короля демонов|130|10,60,30,15,120,40|0|15;Высший жрец Короля демонов|130|10,60,30,15,120,40|0|15;Высший жрец Короля демонов|130|10,60,30,15,120,40|0|15;Высший жрец Короля демонов|130|10,60,30,15,120,40|0|15", "The five summoners, one card each, separated by semicolons: name, level, the six attributes in order (strength, endurance, agility, precision, intelligence, willpower), health, and the rank they know their school at. Without cards the game makes them itself — it takes thirty-odd points, divides them among six attributes and clamps each between five and ten, so five men from one template come out as five of the same man. These five are meant to be people: one holds the line, one does the killing, one keeps the circle fed, and the last is built to outlast the fight. The level written on a card is the one its six attributes add up to through the game's own reckoning, so nothing has to be forced afterwards and nothing drifts: a hundred of wit is what makes a summoner dangerous, and the rest is what a man who spent his life reading has. A dash leaves a stat to the world. Strength is written as one on purpose: it grows from level by itself (four plus three quarters of it), so a card that insists on a number of its own argues with that rule a hundred times over and settles nothing. Health is left at zero for the same reason — endurance and level make it, and a number written by hand is padding. What the card does say is wit, which is magic damage at one per cent a point, and endurance, which is what a body in a robe has instead of armour. The level that comes out is what those two ask for — the circle has to survive its own summoning or the scene ends with nobody to close it.");
        ArrivalSpell = config.Bind<string>("Summoning", "ArrivalSpell", "ShadowMove", "The spell the demon arrives by. ShadowMove is the dark school's own way of crossing distance, so a thing stepping out of nowhere is the game's own picture of it rather than an effect invented for the occasion.");
        FromDoor = config.Bind<float>("Summoning", "FromDoor", 9f, new ConfigDescription("How far from the area's entrance the circle stands, in metres. The entrance is the only ground the game guarantees is solid, but it is also the way out: standing on it asked «do you want to leave?» before the ritual had begun. Far enough inside that the question does not come up.", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 40f), Array.Empty<object>()));
        DoorIndex = config.Bind<int>("Summoning", "DoorIndex", 1, new ConfigDescription("Which of the area's entrances to begin at. Caves have more than one, and the first is the mouth the torch lies in.", (AcceptableValueBase)(object)new AcceptableValueRange<int>(0, 8), Array.Empty<object>()));
        WordsBefore = config.Bind<string>("Summoning", "WordsBefore", "Пятеро вошли в пещеру затемно и встали там, где круг был вычерчен ещё с вечера.\n\n«Времени до рассвета в обрез, — сказал старший. — Если оборвём на середине, оно вернётся туда, откуда шло, и заберёт нас с собой».\n\nНикто не ответил. Каждый знал своё место в круге.", "The line before the summoners appear.");
        WordsCasting = config.Bind<string>("Summoning", "WordsCasting", "«Мы зовём не слугу и не тварь. Мы зовём того, кто правит там, где не правит никто, — и просим его руку против тех, кто пришёл нас жечь».\n\nТьма пошла от их ладоней тремя волнами, и на третьей воздух в круге стал тяжёлым, как перед грозой.", "The line as the summoners begin.");
        WordsHeroes = config.Bind<string>("Summoning", "WordsHeroes", "Снаружи хрустнул камень, и в пещеру хлынули люди — не крадучись, а как входят в дом, который собираются сжечь.\n\n«Ах вы твари, — сказал тот, что шёл первым, и оружие уже было у него в руке. — Всё-таки успели. Всё-таки призвали».\n\nОн обвёл взглядом круг, догорающие знаки и то, что стояло посреди них, и голос у него не дрогнул.\n\n«Ничего. Помешать ещё не поздно. Убить их всех».", "The line as the hunters come in.");
        MarchClose = config.Bind<float>("Summoning", "MarchClose", 9f, new ConfigDescription("How near the nearest hunter must come before he speaks, in metres.", (AcceptableValueBase)(object)new AcceptableValueRange<float>(2f, 40f), Array.Empty<object>()));
        MarchTime = config.Bind<float>("Summoning", "MarchTime", 40f, new ConfigDescription("The longest the scene will wait for them, in seconds. Not how long they are given: the scene goes on as soon as the nearest one is close. This is only the point at which it stops waiting — for a marked spot that turns out to be unreachable, so the ritual does not hang on somebody who is never coming.", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 300f), Array.Empty<object>()));
        HeroSpot = config.Bind<string>("Summoning", "HeroSpot", "95.72,39.91,112.8;97.73,39.92,111.98;99.25,39.88,111.29;100.97,39.92,110.22;104.3,39.9,107.51", "Where each hunter waits, as x,y,z separated by semicolons. The first is the leader and gets the gear; the rest follow him. Marking a spot adds to this list rather than replacing it, so a party is laid out by standing in each place in turn. Empty the list to have the mod search for places itself.");
        HeroWay = config.Bind<string>("Summoning", "HeroWay", "", "Where each hunter runs first, as x,y,z separated by semicolons, one per member in the same order as HeroSpot. A cave is not a room: an order to walk straight at the circle puts a man into the rock he cannot see through, and he stands there while the others arrive. A point at the turn gives him the corner to round, and from there the way to his place is straight. Empty, and they go to their places directly.");
        HeroEnd = config.Bind<string>("Summoning", "HeroEnd", "", "Where each hunter stops, as x,y,z separated by semicolons, one per member. The party then forms up as it was laid out rather than piling onto the circle, and the line is spoken once every one of them is standing where he was put. Empty, and they all run at the circle itself.");
        MageSpellLevel = config.Bind<int>("Summoning", "MageSpellLevel", 15, new ConfigDescription("What level the summoners know their school at. Units built straight from a template arrive with an empty spell list — the game normally fills it through its own spawners — so in a fight they simply swung at people. Handing them the dark school explicitly is what lets them cast at all.", (AcceptableValueBase)(object)new AcceptableValueRange<int>(1, 15), Array.Empty<object>()));
        HunterUnit = config.Bind<string>("Summoning", "HunterUnit", "WhiteSteedChampion,Guard_T2_EliteKnight,WhiteSteedChampion,Guard_T2_EliteKnight,WhiteSteedChampion,Guard_T2_EliteKnight,WhiteSteedChampion,Guard_T2_EliteKnight,WhiteSteedChampion,WhiteSteedChampion", "The hunting party, one UnitInfo asset name per member, separated by commas. A band rather than five of the same man: the first is the leader and gets the gear, the rest follow in the order written. Fewer names than there are hunters and the list repeats. Empty, and the names the catalogue holds are written to the log so a party can be chosen from them.");
        MageFaction = config.Bind<string>("Summoning", "MageFaction", "neutralNPC", "Which faction the summoners stand in. Not the player's: five outsiders sitting in the player's own faction is a state the game never produces by itself, and when a fight began its side-sorting ran on that odd arrangement and dragged the player across with them — turning him into an enemy of everyone, himself included. They are friendly because of the kinship rule instead, which answers the question «is this an enemy» without touching factions at all.");
        HideUntilCalled = config.Bind<bool>("Summoning", "HideUntilCalled", true, "Keep the demon out of sight until the summoners have finished. He has to exist from the first frame — the game needs somebody to hand the controls to — so he is hidden and held still rather than created late, and the spell is what reveals him.");
        ArmTheirLeader = config.Bind<bool>("Summoning", "ArmTheirLeader", true, "Give the one leading them the best the world has. A hunting party sent after a demon would not be sent in rags, and the gear shows at a glance which of the five is worth killing first.");
        LeaderTier = config.Bind<string>("Summoning", "LeaderTier", "T3", "Which tier of gear the leader wears: T0 through T5.");
        Roles = config.Bind<string>("Summoning", "Roles", "Heavy/GreatSword,Heavy/LongSword,Medium/BastardSword,Light/Longbow,Light/Shortbow", "What each man of the CRUSADE wears and carries, as armour/weapon separated by commas, one entry per member in order. The party that comes to the summoning has its own list, HunterRoles, because that one is all plate and this one wants its archers. Armour is Light, Medium or Heavy; the weapon is a WeaponClass name. A band with roles reads as a party that was put together rather than five of the same man: two in plate, one in mail, two archers behind them in leather.");
        MageRoles = config.Bind<string>("Summoning", "MageRoles", "Light/Staff,Light/Dagger,Light/Staff,Light/Stick,Light/Dagger", "What the summoners wear and carry, in the same armour/weapon form as the hunters. Staves and light cloth rather than plate: they were drawing a circle all night, not standing a watch.");
        HunterRoles = config.Bind<string>("Summoning", "HunterRoles", "Heavy/GreatSword,Heavy/LongSword/HeaterShield,Medium/BastardSword,Heavy/Mace/KiteShield,Medium/BattleAxe/HeaterShield,Heavy/TwoHandMace,Medium/GreatAxe,Heavy/LongSword/TowerShield,Medium/Sword/RoundShield,Medium/Halberd", "What the party that comes to break the circle wears and carries, in the same armour/weapon/offhand form as Roles, one entry per member in order. Kept apart from Roles because that list is the crusade's, and the crusade wants archers behind its knights; these ten are all plate and all in reach. Two-handers, one-handers with shields, swords, maces and axes: ten men who look like ten men rather than one man ten times.");
        HunterTier = config.Bind<string>("Summoning", "HunterTier", "T2", "Which tier the rank and file of that party wears. The leader has his own, one shade better, and nothing else in the cave out-dresses him.");
        HunterQuality = config.Bind<string>("Summoning", "HunterQuality", "Common", "And of what quality: Poor, Common, Uncommon, Rare, Epic or Legendary. Plain steel. What marks the leader out is the colour, not the tier, and a colour only reads as rare when the men beside him have none.");
        HunterClad = config.Bind<string>("Summoning", "HunterClad", "PlateArmor,ChainArmor", "Which armour classes that party is allowed to wear, by the game's own ArmourClass names, separated by commas. Full plate is a class, not a weight: asking only for Heavy gives scale, lamellar and half-plate as readily as plate. Helmets carry their own classes and never PlateArmor, so metalHelmet stands beside it — without that line the whole party arrives bare-headed or in whatever hat the template was born in. Empty, and only the weight is asked for.");
        LeaderClad = config.Bind<string>("Summoning", "LeaderClad", "PlateArmor", "And the same for the one leading them, who is dressed apart from the rest. Plate while they wear mail: a captain is told from twenty paces by his shape before his colour, and that is the whole point of dressing him separately.");
        MageTier = config.Bind<string>("Summoning", "MageTier", "T4", "Which tier the summoners wear. They were the ones who could call a lord of hell; their robes are the best in the cave and their colour is the plainest.");
        MageQuality = config.Bind<string>("Summoning", "MageQuality", "Common", "And of what quality. Common on purpose: what the circle had was knowledge, not treasure, and the demon is here to collect the treasure himself.");
        MageArms = config.Bind<string>("Summoning", "MageArms", "Uncommon", "What colour the summoners' staves are, apart from the rest of what they wear. A staff in this game carries no bonus of its own at any tier — the plain ones are blank, and only the colour puts anything on them. Theirs is the one thing in the cave that was not issued but chosen. Empty, and the staff is the same colour as their robes.");
        Trinkets = config.Bind<string>("Summoning", "Trinkets", "neck,finger", "Which trinket slots everyone in the cave gets filled, by the game's EquipSlotType names, separated by commas. Set apart from the rest of the gear because an amulet is not armour: it is what a man buys, and it does not follow the tier of what he was issued. Empty, and they wear whatever the general search happened to find.");
        TrinketTier = config.Bind<string>("Summoning", "TrinketTier", "T2", "Which tier those trinkets are.");
        TrinketQuality = config.Bind<string>("Summoning", "TrinketQuality", "Uncommon", "And of what quality. Green at the second tier is what a man who was paid can afford, and it is the same for everyone in the cave — the leader is marked out by his armour, not by his ring.");
        Purse = config.Bind<int>("Summoning", "Purse", 10000, new ConfigDescription("Coin each of them carries, in the smallest unit. Ten thousand is one gold piece: the game shows money as gold, silver and copper, and a gold is ten thousand of the smallest. People who came to kill a demon did not walk out of town empty-handed.", (AcceptableValueBase)(object)new AcceptableValueRange<int>(0, 1000000), Array.Empty<object>()));
        MagesGuard = config.Bind<bool>("Summoning", "MagesGuard", true, "Have the summoners stay at the demon's side once the fighting starts, rather than each going for the nearest hunter. They spent the night calling him; standing between him and the people who came to end it is the same errand. Off, and they scatter to whoever is closest.");
        WordsAfter = config.Bind<string>("Summoning", "WordsAfter", "Последний из пришедших затих, и в пещере стало слышно, как капает вода.\n\nСтарший из магов опустился на колено — не устало, а так, как встают перед тем, кого звали всю жизнь.\n\n«Владыка. Круг сомкнулся, и ты здесь. Но то, чем ты был там, осталось там: сила твоя запечатана, и снять печать нам не по силам.\n\nРаскрой её сам и повелевай нами. А пока прими то немногое, что у нас есть».\n\nНожи вышли из рукавов разом и ни один не дрогнул.", "The line after the fight, before the summoners give what they have.");
        WordsQuest = config.Bind<string>("Summoning", "WordsQuest", "Пятеро лежали в круге, и круг наконец догорел.\n\nТо, что они отдали, осело в вас коротким жаром — и вместе с ним пришло знание, которого не было минуту назад. Печать не снимается словом и не рубится клинком. Она разложена по миру на части, и каждая носит чужое имя и чужую цену.\n\nМонеты судьбы. Одна уже лежит у вас в суме — её вложили в руку мертвецу на дороге в Бреа, и он донёс её сюда, сам того не зная.\n\nСоберите остальные, и Владыка Ада станет собой.", "The line that sets the demon on his way.");
        RitualAttributes = config.Bind<int>("Summoning", "RitualAttributes", 10, new ConfigDescription("How much every one of the six attributes gains from the circle's death. Room to grow is a promise; this is the thing itself. Five men spent their lives, and what they bought should be felt on the first morning rather than earned later — the ceiling is raised to match, so nothing sits above what the creature is allowed to become.", (AcceptableValueBase)(object)new AcceptableValueRange<int>(0, 100), Array.Empty<object>()));

        RitualPotential = config.Bind<int>("Summoning", "RitualPotential", 50, new ConfigDescription("How much room to grow the circle buys him with their lives. Potential is the ceiling on everything a character can ever become, and the ritual is what raises his: five men spent themselves to make the vessel bigger. Shown in the character window as the second number beside potential.", (AcceptableValueBase)(object)new AcceptableValueRange<int>(0, 500), Array.Empty<object>()));

        AfterWait = config.Bind<float>("Summoning", "AfterWait", 180f, new ConfigDescription("The longest the mod waits for the fight to end, in seconds, before giving up on the closing scene. A fight the player walks away from would otherwise leave it waiting for ever.", (AcceptableValueBase)(object)new AcceptableValueRange<float>(10f, 900f), Array.Empty<object>()));
        LeaderQuality = config.Bind<string>("Summoning", "LeaderQuality", "Uncommon", "Which quality the one leading them wears: Poor, Common, Uncommon, Rare, Epic or Legendary. Green, and everything on him is green — armour, greatsword and all. It is one step above the men behind him and nothing more: what should be read at twenty paces is that he is the captain, not that he is carrying the prize. Purple in this cave belongs to nobody.");
    }

    // Ярус и качество снаряжения можно задать снаружи — так одевается отряд святого
    // похода, у которого они растут вместе с уровнем. Пусто — берётся из настроек.
    internal static string tierOverride;
    internal static string qualityOverride;

    internal static void Outfit(UnitAttribute hunter, int which, string rolesText,
        string tierWanted = null, string gradeWanted = null, string cladWanted = null,
        bool adorn = false, string armsGrade = null)
    {
        //IL_0063: Unknown result type (might be due to invalid IL or missing references)
        //IL_0068: Unknown result type (might be due to invalid IL or missing references)
        //IL_0088: Unknown result type (might be due to invalid IL or missing references)
        //IL_008d: Unknown result type (might be due to invalid IL or missing references)
        //IL_00e3: Unknown result type (might be due to invalid IL or missing references)
        //IL_00e4: Unknown result type (might be due to invalid IL or missing references)
        //IL_00e5: Unknown result type (might be due to invalid IL or missing references)
        //IL_00e6: Unknown result type (might be due to invalid IL or missing references)
        //IL_00e8: Unknown result type (might be due to invalid IL or missing references)
        //IL_00f5: Unknown result type (might be due to invalid IL or missing references)
        //IL_00f6: Unknown result type (might be due to invalid IL or missing references)
        //IL_00f7: Unknown result type (might be due to invalid IL or missing references)
        //IL_00f8: Unknown result type (might be due to invalid IL or missing references)
        //IL_00fa: Unknown result type (might be due to invalid IL or missing references)
        //IL_0107: Unknown result type (might be due to invalid IL or missing references)
        //IL_0108: Unknown result type (might be due to invalid IL or missing references)
        //IL_0109: Unknown result type (might be due to invalid IL or missing references)
        //IL_010a: Unknown result type (might be due to invalid IL or missing references)
        //IL_010c: Unknown result type (might be due to invalid IL or missing references)
        //IL_0140: Unknown result type (might be due to invalid IL or missing references)
        //IL_0177: Unknown result type (might be due to invalid IL or missing references)
        //IL_02bb: Unknown result type (might be due to invalid IL or missing references)
        //IL_02c4: Unknown result type (might be due to invalid IL or missing references)
        //IL_02cd: Unknown result type (might be due to invalid IL or missing references)
        //IL_02d6: Unknown result type (might be due to invalid IL or missing references)
        //IL_02e3: Unknown result type (might be due to invalid IL or missing references)
        //IL_02ec: Unknown result type (might be due to invalid IL or missing references)
        if (!ArmTheirLeader.Value || hunter == null)
        {
            return;
        }
        HumaniodUnit val = (HumaniodUnit)(object)((hunter is HumaniodUnit) ? hunter : null);
        if (val == null || val.items == null || val.equipmentmanger == null)
        {
            return;
        }
        try
        {
            ItemTier val2;
            UIItemQuality val3;
            try
            {
                // Заказ: снаружи (у похода свой), затем поимённо от того, кто одевает, и
                // лишь напоследок капитанский. Прежде капитанский был единственным, оттого
                // весь отряд и приходил в том же, в чём капитан.
                val2 = (ItemTier)Enum.Parse(typeof(ItemTier),
                    (tierOverride ?? tierWanted ?? LeaderTier.Value).Trim(), ignoreCase: true);
                val3 = (UIItemQuality)Enum.Parse(typeof(UIItemQuality),
                    (qualityOverride ?? gradeWanted ?? LeaderQuality.Value).Trim(), ignoreCase: true);
            }
            catch
            {
                DemonLookPlugin.Log.LogWarning("Не понял ярус или качество, оставляю как есть.");
                return;
            }
            if (!Role(which, rolesText, out var wears, out var carries, out var holds))
            {
                return;
            }
            UIItemDatabase instance = UIItemDatabase.Instance;
            if (instance == null || instance.items == null)
            {
                return;
            }
            Dictionary<EquipSlotType, List<UIEquipmentInfo>> dictionary = new Dictionary<EquipSlotType, List<UIEquipmentInfo>>();
            Census(instance);
            Inventory();
            Spells();
            HashSet<ArmourClass> clad = Clads(cladWanted);

            Gather(instance, dictionary, val2, val3, wears, carries, holds, clad, sameTier: true, sameQuality: true);
            Gather(instance, dictionary, val2, val3, wears, carries, holds, clad, sameTier: true, sameQuality: false);
            Gather(instance, dictionary, val2, val3, wears, carries, holds, clad, sameTier: false, sameQuality: false);
            Fill(instance, dictionary, wears, carries, holds, val2, val3, clad);
            Dictionary<EquipSlotType, UIEquipmentInfo> dictionary2 = new Dictionary<EquipSlotType, UIEquipmentInfo>();
            List<string> list = new List<string>();
            foreach (KeyValuePair<EquipSlotType, List<UIEquipmentInfo>> item in dictionary)
            {
                list.Add($"{item.Key}:{item.Value.Count}");
                if (item.Value.Count != 0)
                {
                    dictionary2[item.Key] = item.Value[UnityEngine.Random.Range(0, item.Value.Count)];
                }
            }
            list.Sort();
            DemonLookPlugin.Log.LogInfo(($"Охотник {which + 1}, подходящих вещей: " + ((list.Count > 0) ? string.Join(", ", list.ToArray()) : "ни одной")));
            int num = 0;
            int cast = 0;

            foreach (KeyValuePair<EquipSlotType, UIEquipmentInfo> item2 in dictionary2)
            {
                // Сначала освободить место. Игра ставит вещь только в пустой слот: при slotIndex -1
                // она ищет незанятый, и если такого нет, молча выходит. Именно поэтому все
                // тринадцать ходили в одном и том же шаблонном «FirstRanger»: корпус и ноги заняты у
                // всех, и латы создавались только затем, чтобы быть выброшенными.
                //
                // Шаблонное снимается насовсем: в сумку «UnequipItem» ничего не кладёт, а вещь,
                // кроме слота, нигде и не числилась. Оно никому не нужно и в добычу не пойдёт.
                cast += Free(val.equipmentmanger, item2.Key);

                Inventory put = val.equipmentmanger.EquipItem(item2.Value, -1);

                // Считаем надетое, а не попытки. Преждяя проверка смотрела на возврат, а он
                // никогда не пустой: вещь создаётся до того, как выясняется, есть ли ей место. Оттого
                // в логе и стояло «вещей 8» у того, на ком их было шесть.
                if (Wearing(val.equipmentmanger, put)) num++;
            }
            if (Purse.Value > 0 && val.items != null)
            {
                val.items.money = Purse.Value;
            }
            // Только там, где просили: у похода украшения свои, и менять их заодно значило бы
            // тронуть то, о чём разговора не было.
            int charms = adorn ? Adorn(val) : 0;
            Regrade(val, dictionary2, armsGrade);
            DemonLookPlugin.Log.LogInfo(($"Охотник {which + 1}: вещей {num} " + $"({val3}, {val2}, броня {wears}, оружие {carries}" + (((int)holds == 0) ? "" : $", вторая рука {holds}") + ")"
                + ((cast > 0) ? $", шаблонного снято {cast}" : "")
                + ((charms > 0) ? $", украшений {charms}" : "") + "."));
        }
        catch (Exception ex)
        {
            DemonLookPlugin.Log.LogError(("Не смог одеть охотника: " + ex));
        }
    }

    /// <summary>
    /// Gives what he holds a colour of its own.
    ///
    /// Посох в этой игре пуст на любом ярусе: ни Т3, ни Т4 не несут ни одного свойства —
    /// это видно в выгрузке предметов, «бонусов нет». Всё, что на посохе может оказаться,
    /// кладёт туда качество, и оттого зелёный посох — не украшение, а единственный способ
    /// сделать его чем-то, кроме палки.
    /// </summary>
    private static int Regrade(HumaniodUnit man, Dictionary<EquipSlotType, UIEquipmentInfo> chosen,
        string grade)
    {
        if (man == null || man.equipmentmanger == null || chosen == null) return 0;
        if (grade == null || grade.Trim().Length == 0) return 0;

        UIEquipmentInfo held;
        if (!chosen.TryGetValue((EquipSlotType)0, out held) || held == null) return 0;

        UIItemQuality want;

        try
        {
            want = (UIItemQuality)Enum.Parse(typeof(UIItemQuality), grade.Trim(), ignoreCase: true);
        }
        catch
        {
            DemonLookPlugin.Log.LogWarning(("Не понял качество оружия «" + grade + "»."));
            return 0;
        }

        try
        {
            Free(man.equipmentmanger, (EquipSlotType)0);

            Inventory made = new Inventory(held);
            EquipmentMaker.EnhanceEquipmentToQuality(made, want);

            // Надетое живёт в слоте; в сумку — только если не наделось.
            man.equipmentmanger.EquipItem(made);
            if (man.items != null && !Wearing(man.equipmentmanger, made)) man.items.AddInventoryNoEvent(made);

            DemonLookPlugin.Log.LogInfo($"«{man.Data.unitname}»: оружие перековано в {want} — "
                + $"«{((UIItemInfo)held).Name}».");

            return 1;
        }
        catch (Exception ex)
        {
            DemonLookPlugin.Log.LogWarning("Не смог перековать оружие: " + ex.Message);
            return 0;
        }
    }

    /// <summary>Which armour classes this one may wear; an empty set means «any».</summary>
    private static HashSet<ArmourClass> Clads(string written)
    {
        HashSet<ArmourClass> made = new HashSet<ArmourClass>();
        if (written == null) return made;

        foreach (string one in written.Split(','))
        {
            string word = one.Trim();
            if (word.Length == 0) continue;

            try
            {
                made.Add((ArmourClass)Enum.Parse(typeof(ArmourClass), word, ignoreCase: true));
            }
            catch
            {
                DemonLookPlugin.Log.LogWarning(("Не знаю класса брони «" + word + "»."));
            }
        }

        return made;
    }

    /// <summary>
    /// Puts the same small things on everyone in the cave: an amulet and a ring.
    ///
    /// Отдельно от прочего снаряжения, потому что украшение не выдают со склада — его
    /// покупают, и оно не обязано быть того же яруса, что кираса. Общий подбор клал в шею и
    /// на палец вещи заказанного яруса, и у отряда в эпических латах амулеты выходили
    /// эпическими же.
    /// </summary>
    private static int Adorn(HumaniodUnit man)
    {
        if (man == null || man.equipmentmanger == null) return 0;

        string written = (Trinkets.Value ?? "").Trim();
        if (written.Length == 0) return 0;

        ItemTier tier;
        UIItemQuality grade;

        try
        {
            tier = (ItemTier)Enum.Parse(typeof(ItemTier), TrinketTier.Value.Trim(), ignoreCase: true);
            grade = (UIItemQuality)Enum.Parse(typeof(UIItemQuality), TrinketQuality.Value.Trim(), ignoreCase: true);
        }
        catch
        {
            DemonLookPlugin.Log.LogWarning("Не понял ярус или качество украшений.");
            return 0;
        }

        int given = 0;

        foreach (string one in written.Split(','))
        {
            EquipSlotType where;

            try
            {
                where = (EquipSlotType)Enum.Parse(typeof(EquipSlotType), one.Trim(), ignoreCase: true);
            }
            catch
            {
                continue;
            }

            UIEquipmentInfo found = Charm(where, tier);
            if (found == null) continue;

            Free(man.equipmentmanger, where);

            Inventory made = new Inventory(found);
            EquipmentMaker.EnhanceEquipmentToQuality(made, grade);

            man.equipmentmanger.EquipItem(made);
            if (man.items != null && !Wearing(man.equipmentmanger, made)) man.items.AddInventoryNoEvent(made);

            given++;
        }

        return given;
    }

    private static readonly Dictionary<string, UIEquipmentInfo> charms =
        new Dictionary<string, UIEquipmentInfo>(StringComparer.Ordinal);

    /// <summary>The first trinket of this kind and tier the world has.</summary>
    private static UIEquipmentInfo Charm(EquipSlotType where, ItemTier tier)
    {
        string key = where.ToString() + "/" + tier.ToString();

        UIEquipmentInfo kept;
        if (charms.TryGetValue(key, out kept)) return kept;

        kept = null;

        UIItemDatabase db = UIItemDatabase.Instance;
        if (db != null && db.items != null)
        {
            UIItemInfo[] all = db.items;
            foreach (UIItemInfo thing in all)
            {
                UIEquipmentInfo gear = (UIEquipmentInfo)(object)((thing is UIEquipmentInfo) ? thing : null);
                if (gear == null || ((UIItemInfo)gear).isUnique) continue;
                if (gear.EquipType != where || ((UIItemInfo)gear).tier != tier) continue;

                kept = gear;
                break;
            }
        }

        charms[key] = kept;
        return kept;
    }

    /// <summary>Takes off whatever holds this slot, and lets it go.</summary>
    private static int Free(EquipmentManager kit, EquipSlotType which)
    {
        if (kit == null || kit.equipInfos == null) return 0;

        // Сначала ищем пустое место этого же рода. У колец и рук слотов по нескольку, и
        // если хоть один свободен, снимать не надо ничего: игра сама положит туда.
        // Без этого прохода первое кольцо снималось бы зря, при пустом втором.
        foreach (EquipInfo slot in kit.equipInfos)
        {
            if (slot == null || slot.slotType != which) continue;
            if (!slot.IsEquiped()) return 0;
        }

        // Свободных нет — освобождаем одно, и только одно.
        foreach (EquipInfo slot in kit.equipInfos)
        {
            if (slot == null || slot.slotType != which || !slot.IsEquiped()) continue;

            kit.UnequipItem(slot);
            return 1;
        }

        return 0;
    }

    /// <summary>True when this very thing ended up in a slot.</summary>
    private static bool Wearing(EquipmentManager kit, Inventory thing)
    {
        if (kit == null || thing == null || kit.equipInfos == null) return false;

        foreach (EquipInfo slot in kit.equipInfos)
        {
            if (slot == null || !slot.IsEquiped()) continue;
            if (ReferenceEquals(slot.inventory, thing)) return true;
        }

        return false;
    }

    internal static void Census(UIItemDatabase db)
    {
        //IL_0067: Unknown result type (might be due to invalid IL or missing references)
        //IL_0073: Unknown result type (might be due to invalid IL or missing references)
        //IL_007f: Unknown result type (might be due to invalid IL or missing references)
        //IL_00c9: Unknown result type (might be due to invalid IL or missing references)
        //IL_00d5: Unknown result type (might be due to invalid IL or missing references)
        //IL_00e1: Unknown result type (might be due to invalid IL or missing references)
        //IL_012b: Unknown result type (might be due to invalid IL or missing references)
        //IL_0137: Unknown result type (might be due to invalid IL or missing references)
        //IL_0143: Unknown result type (might be due to invalid IL or missing references)
        if (counted || db == null || db.items == null)
        {
            return;
        }
        counted = true;
        try
        {
            Dictionary<string, int> dictionary = new Dictionary<string, int>();
            Dictionary<string, int> dictionary2 = new Dictionary<string, int>();
            Dictionary<string, int> dictionary3 = new Dictionary<string, int>();
            int num = 0;
            UIItemInfo[] items = db.items;
            foreach (UIItemInfo obj in items)
            {
                UIEquipmentInfo val = (UIEquipmentInfo)(object)((obj is UIEquipmentInfo) ? obj : null);
                if (!(val == null))
                {
                    num++;
                    string key = $"{val.EquipType}/{((UIItemInfo)val).tier}/{((UIItemInfo)val).Quality}";
                    dictionary[key] = ((!dictionary.ContainsKey(key)) ? 1 : (dictionary[key] + 1));
                    UIWeaponInfo val2 = (UIWeaponInfo)(object)((val is UIWeaponInfo) ? val : null);
                    if (val2 != null)
                    {
                        string key2 = $"{val2.weaponClass}/{((UIItemInfo)val).tier}/{((UIItemInfo)val).Quality}";
                        dictionary2[key2] = ((!dictionary2.ContainsKey(key2)) ? 1 : (dictionary2[key2] + 1));
                    }
                    UIArmorInfo val3 = (UIArmorInfo)(object)((val is UIArmorInfo) ? val : null);
                    if (val3 != null)
                    {
                        string key3 = $"{val3.armourType}/{val.EquipType}/{((UIItemInfo)val).tier}";
                        dictionary3[key3] = ((!dictionary3.ContainsKey(key3)) ? 1 : (dictionary3[key3] + 1));
                    }
                }
            }
            DemonLookPlugin.Log.LogInfo($"=== перепись снаряжения: {num} вещей ===");
            Print("слоты", dictionary);
            Print("оружие", dictionary2);
            Print("броня", dictionary3);
        }
        catch (Exception ex)
        {
            DemonLookPlugin.Log.LogError(("Перепись снаряжения сорвалась: " + ex));
        }
    }

    private static void Print(string what, Dictionary<string, int> counts)
    {
        List<string> list = new List<string>();
        foreach (KeyValuePair<string, int> count in counts)
        {
            list.Add($"{count.Key}={count.Value}");
        }
        list.Sort();
        for (int i = 0; i < list.Count; i += 15)
        {
            DemonLookPlugin.Log.LogInfo(("  " + what + ": " + string.Join(", ", list.GetRange(i, Math.Min(15, list.Count - i)).ToArray())));
        }
    }

    private static void Gather(UIItemDatabase db, Dictionary<EquipSlotType, List<UIEquipmentInfo>> found, ItemTier tier, UIItemQuality quality, ArmourType wears, WeaponClass carries, WeaponClass holds, HashSet<ArmourClass> clad, bool sameTier, bool sameQuality)
    {
        //IL_0033: Unknown result type (might be due to invalid IL or missing references)
        //IL_0038: Unknown result type (might be due to invalid IL or missing references)
        //IL_0043: Unknown result type (might be due to invalid IL or missing references)
        //IL_0048: Unknown result type (might be due to invalid IL or missing references)
        //IL_005f: Unknown result type (might be due to invalid IL or missing references)
        //IL_0065: Invalid comparison between Unknown and I4
        //IL_0053: Unknown result type (might be due to invalid IL or missing references)
        //IL_0059: Invalid comparison between Unknown and I4
        //IL_0068: Unknown result type (might be due to invalid IL or missing references)
        //IL_006e: Invalid comparison between Unknown and I4
        //IL_0071: Unknown result type (might be due to invalid IL or missing references)
        //IL_0077: Invalid comparison between Unknown and I4
        //IL_00a3: Unknown result type (might be due to invalid IL or missing references)
        //IL_00a5: Unknown result type (might be due to invalid IL or missing references)
        //IL_00b0: Unknown result type (might be due to invalid IL or missing references)
        //IL_00c6: Unknown result type (might be due to invalid IL or missing references)
        //IL_0099: Unknown result type (might be due to invalid IL or missing references)
        //IL_009e: Unknown result type (might be due to invalid IL or missing references)
        UIItemInfo[] items = db.items;
        foreach (UIItemInfo obj in items)
        {
            UIEquipmentInfo val = (UIEquipmentInfo)(object)((obj is UIEquipmentInfo) ? obj : null);
            // Запасной проход прежде брал «редкое и выше»: он писался под капитана в
            // эпике, и просьба одеть отряд в обычное им же и отменялась — не нашлось вещи
            // нужного качества, пришла фиолетовая. Теперь предел сверху, а не снизу.
            if (val == null || ((UIItemInfo)val).isUnique || (sameTier && ((UIItemInfo)val).tier != tier) || (sameQuality && ((UIItemInfo)val).Quality != quality) || (!sameQuality && (int)((UIItemInfo)val).Quality > (int)quality))
            {
                continue;
            }
            if ((int)val.EquipType == 2 || (int)val.EquipType == 4 || (int)val.EquipType == 7)
            {
                UIArmorInfo val2 = (UIArmorInfo)(object)((val is UIArmorInfo) ? val : null);

                // Класс спрашивается всегда, когда его назвали. «Полные латы» — это класс, а
                // не вес: по весу в ту же корзину ложатся и чешуя, и полулаты, и отряд,
                // одетый «в тяжёлое», выходит одетым кто во что.
                if (val2 != null && clad != null && clad.Count > 0 && !clad.Contains(val2.armourClass))
                {
                    continue;
                }
                if (val2 != null && val2.armourType != wears && (sameTier | sameQuality))
                {
                    continue;
                }
            }
            if (Wanted(val, carries, holds))
            {
                if (!found.TryGetValue(val.EquipType, out var value))
                {
                    value = new List<UIEquipmentInfo>();
                    found[val.EquipType] = value;
                }
                else if (value.Count > 0 && !(sameTier & sameQuality))
                {
                    continue;
                }
                value.Add(val);
            }
        }
    }

    private static bool Role(int which, string rolesText, out ArmourType wears, out WeaponClass carries, out WeaponClass holds)
    {
        //IL_006b: Unknown result type (might be due to invalid IL or missing references)
        //IL_0071: Expected I4, but got Unknown
        //IL_008a: Unknown result type (might be due to invalid IL or missing references)
        //IL_0090: Expected I4, but got Unknown
        //IL_00c0: Unknown result type (might be due to invalid IL or missing references)
        //IL_00c6: Expected I4, but got Unknown
        wears = (ArmourType)3;
        carries = (WeaponClass)18;
        holds = (WeaponClass)0;
        string[] array = (rolesText ?? "").Split(',');
        if (array.Length == 0)
        {
            return false;
        }
        string[] array2 = array[which % array.Length].Trim().Split('/');
        if (array2.Length < 2)
        {
            return false;
        }
        try
        {
            wears = (ArmourType)(int)(ArmourType)Enum.Parse(typeof(ArmourType), array2[0].Trim(), ignoreCase: true);
            carries = (WeaponClass)(int)(WeaponClass)Enum.Parse(typeof(WeaponClass), array2[1].Trim(), ignoreCase: true);
            if (array2.Length >= 3 && array2[2].Trim().Length > 0)
            {
                holds = (WeaponClass)(int)(WeaponClass)Enum.Parse(typeof(WeaponClass), array2[2].Trim(), ignoreCase: true);
            }
            return true;
        }
        catch
        {
            DemonLookPlugin.Log.LogWarning(("Не разобрал роль «" + array[which % array.Length] + "»."));
            return false;
        }
    }

    /// <summary>Fills the slots the exact search left empty, with the best the world has.</summary>
    private static void Fill(UIItemDatabase db, Dictionary<EquipSlotType, List<UIEquipmentInfo>> found,
        ArmourType wears, WeaponClass carries, WeaponClass holds,
        ItemTier tier, UIItemQuality quality, HashSet<ArmourClass> clad)
    {
        // Точный подбор просит ровно свой класс оружия своего яруса и качества, и когда
        // такой вещи в базе нет, охотник остаётся без неё вовсе: в переписи у всех десяти
        // не было ни оружия, ни шлема — были пояс, кираса, штаны и украшения. Здесь пустые
        // ячейки добираются тем, что в игре действительно лежит, и в лог пишется, чем
        // именно пришлось заменить — чтобы спор «есть такие вещи или нет» решался списком.
        EquipSlotType[] needed = ((int)holds == 0)
            ? new EquipSlotType[] { (EquipSlotType)0, (EquipSlotType)2, (EquipSlotType)4, (EquipSlotType)7 }
            : new EquipSlotType[] { (EquipSlotType)0, (EquipSlotType)1, (EquipSlotType)2, (EquipSlotType)4, (EquipSlotType)7 };

        foreach (EquipSlotType slot in needed)
        {
            List<UIEquipmentInfo> had;
            if (found.TryGetValue(slot, out had) && had != null && had.Count > 0) continue;

            UIEquipmentInfo best = null;
            int bestScore = -1;

            UIItemInfo[] items = db.items;
            foreach (UIItemInfo any in items)
            {
                UIEquipmentInfo gear = (UIEquipmentInfo)(object)((any is UIEquipmentInfo) ? any : null);
                if (gear == null || ((UIItemInfo)gear).isUnique) continue;
                if (gear.EquipType != slot) continue;

                // Оценка: сперва свой класс, потом заказанный ярус, потом качество не выше
                // заказанного. Прежде она читалась «чем выше, тем лучше» — и добор молча
                // выдавал эпическую вещь четвёртого яруса отряду, одетому в третий обычный:
                // не нашлось шлема нужного рода, пришёл лучший шлем в игре.
                int score = 0;

                if (((UIItemInfo)gear).tier == tier) score += 500;
                else score -= 60 * Math.Abs((int)((UIItemInfo)gear).tier - (int)tier);

                if ((int)((UIItemInfo)gear).Quality <= (int)quality)
                {
                    score += 200 + (int)((UIItemInfo)gear).Quality * 10;
                }
                else
                {
                    score -= 200 * ((int)((UIItemInfo)gear).Quality - (int)quality);
                }

                if ((int)slot == 0 || (int)slot == 1)
                {
                    UIWeaponInfo weapon = (UIWeaponInfo)(object)((gear is UIWeaponInfo) ? gear : null);
                    if (weapon == null) continue;

                    WeaponClass wanted = ((int)slot == 0) ? carries : holds;
                    if (weapon.weaponClass == wanted) score += 1000;
                }
                else
                {
                    UIArmorInfo armour = (UIArmorInfo)(object)((gear is UIArmorInfo) ? gear : null);

                    if (armour != null && clad != null && clad.Count > 0 && !clad.Contains(armour.armourClass))
                    {
                        continue;
                    }
                    if (armour != null && armour.armourType == wears) score += 1000;
                }

                if (score > bestScore) { bestScore = score; best = gear; }
            }

            if (best == null) continue;

            found[slot] = new List<UIEquipmentInfo> { best };

            DemonLookPlugin.Log.LogInfo($"  добор {slot}: «{((UIItemInfo)best).Name}» "
                + $"({((UIItemInfo)best).Quality}, {((UIItemInfo)best).tier})"
                + ((bestScore >= 1000) ? "" : " — своего класса в игре не нашлось."));
        }
    }

    private static bool Wanted(UIEquipmentInfo gear, WeaponClass carries, WeaponClass holds)
    {
        //IL_0001: Unknown result type (might be due to invalid IL or missing references)
        //IL_0006: Unknown result type (might be due to invalid IL or missing references)
        //IL_0007: Unknown result type (might be due to invalid IL or missing references)
        //IL_000a: Unknown result type (might be due to invalid IL or missing references)
        //IL_000c: Invalid comparison between Unknown and I4
        //IL_0040: Unknown result type (might be due to invalid IL or missing references)
        //IL_0045: Unknown result type (might be due to invalid IL or missing references)
        //IL_000e: Unknown result type (might be due to invalid IL or missing references)
        //IL_0024: Unknown result type (might be due to invalid IL or missing references)
        //IL_0029: Unknown result type (might be due to invalid IL or missing references)
        EquipSlotType equipType = gear.EquipType;
        if ((int)equipType != 0)
        {
            if ((int)equipType == 1)
            {
                if ((int)holds == 0)
                {
                    return false;
                }
                UIWeaponInfo val = (UIWeaponInfo)(object)((gear is UIWeaponInfo) ? gear : null);
                if (val != null)
                {
                    return val.weaponClass == holds;
                }
                return false;
            }
            return true;
        }
        UIWeaponInfo val2 = (UIWeaponInfo)(object)((gear is UIWeaponInfo) ? gear : null);
        if (val2 != null)
        {
            return val2.weaponClass == carries;
        }
        return false;
    }

    internal static void Watch()
    {
        if (decided || !Enabled.Value)
        {
            return;
        }
        PartyManager instance = PartyManager.instance;
        HumaniodUnit val = ((instance != null) ? instance.leader : null);
        if (!(val == null))
        {
            decided = true;
            bool flag = SaveLoadManager.Instance != null && SaveLoadManager.Instance.isNewGame;
            bool flag2 = Racial.IsDemon((UnitAttribute)(object)val);
            DemonLookPlugin.Log.LogInfo(("Ритуал: предводитель «" + ((CharacterSaveData)val.Data).unitname + "», " + $"демон {flag2}, новая игра {flag}."));
            if (!flag2)
            {
                DemonLookPlugin.Log.LogInfo("Ритуал не ставлю: отрядом правит не демон.");
            }
            else if (!flag && OnlyNewGame.Value)
            {
                // Круг зовут один раз за жизнь, а не при каждом входе в пещеру. Признак
                // новой игры игра держит сама и гасит, как только начатое сохранено; до сих
                // пор он вычислялся, писался в лог и ни на что не влиял — оттого загрузка
                // сохранения в той же пещере ставила сцену заново, поверх уже сыгранной.
                DemonLookPlugin.Log.LogInfo("Ритуал не ставлю: игра загружена, а не начата.");
            }
            else
            {
                Stage(val);
            }
        }
    }

    internal static void Forget()
    {
        staged = false;
        decided = false;
        party.Clear();
        came.Clear();
        ring.Clear();
        places.Clear();
        standing.Clear();
        Unpost();
        guarded = false;
        told = false;
        melee = false;
        Truce = false;
    }

    internal static void Stage(HumaniodUnit demon)
    {
        if (Enabled.Value && !staged && !(demon == null) && Racial.IsDemon((UnitAttribute)(object)demon))
        {
            staged = true;
            ((MonoBehaviour)demon).StartCoroutine(Play(demon));
        }
    }

    private static IEnumerator Play(HumaniodUnit demon)
    {
        UnitInfo template = null;
        AsyncOperationHandle<UnitInfo> load = Addressables.LoadAssetAsync<UnitInfo>((object)("unitdata/" + MageUnit.Value.Trim()));
        yield return load;
        if ((int)load.Status == 1)
        {
            template = load.Result;
        }
        UISpellInfo spell = FindSpell();
        if (template == null)
        {
            DemonLookPlugin.Log.LogWarning(("Существа «" + MageUnit.Value + "» в каталоге нет — ритуал не ставлю."));
            Suggest();
            yield break;
        }
        Vector3 centre = Ground(demon);
        Show(demon, visible: false);
        Hold((UnitAttribute)(object)demon, held: true);
        Untouched(demon, true);
        Scene.Close();
        guarded = true;
        Truce = true;
        ((MonoBehaviour)demon).StartCoroutine(Guard(demon));
        yield return Scene.Say("Пещера Бревудс", WordsBefore.Value);
        yield return ((MonoBehaviour)demon).StartCoroutine(FindHunters());
        List<UnitAttribute> mages = Ring(template, centre, demon);
        centre = Middle(mages, centre);
        heart = centre;
        demon.transform.position = centre;
        DemonLookPlugin.Log.LogInfo(($"Ритуал: магов {mages.Count}, заклинание " + ((spell != null) ? spell.Name : "не найдено") + "."));
        yield return Scene.Say("Круг", WordsCasting.Value);
        if (spell != null)
        {
            for (int round = 0; round < CastTimes.Value; round++)
            {
                foreach (UnitAttribute item in mages)
                {
                    if (!(item == null) && !item.Data.isdead)
                    {
                        Cast(item, demon, spell);
                    }
                }
                yield return (object)new WaitForSeconds(CastGap.Value);
            }
        }
        Arrive(demon, centre);

        if (HeroInstant.Value)
        {
            yield return ((MonoBehaviour)demon).StartCoroutine(Appear(demon, centre));
        }
        else
        {
            yield return ((MonoBehaviour)demon).StartCoroutine(Scene.Open(2f));
            Hunters(centre, demon);
            March(demon, mages, centre);
            yield return ((MonoBehaviour)demon).StartCoroutine(Await(demon));
        }
        if (demon.Data != null && demon.maxhp > 0f)
        {
            ((CharacterSaveData)demon.Data).currenthp = demon.maxhp;
        }
        yield return (object)new WaitForSeconds(3f);
        yield return Scene.Say("У входа", WordsHeroes.Value);
        guarded = false;
        told = false;
        Truce = false;

        // Демона не отпускаем: бой ведут маги, он в нём — то, за чем пришли, а не участник.
        // Управление вернётся, когда круг догорит и последний из пятерых упадёт сам.
        Counter(mages, demon);
        DemonLookPlugin.Log.LogInfo("Перемирие снято, бой начинается.");
        yield return ((MonoBehaviour)demon).StartCoroutine(After(demon, mages));
    }

    private static void Show(HumaniodUnit demon, bool visible)
    {
        if (!HideUntilCalled.Value || demon == null)
        {
            return;
        }
        try
        {
            Renderer[] componentsInChildren = demon.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer val in componentsInChildren)
            {
                if (val != null)
                {
                    val.enabled = visible;
                }
            }
        }
        catch (Exception ex)
        {
            DemonLookPlugin.Log.LogWarning(("Не смог скрыть демона: " + ex.Message));
        }
    }

    private static void Arrive(HumaniodUnit demon, Vector3 centre)
    {
        //IL_0168: Unknown result type (might be due to invalid IL or missing references)
        //IL_0173: Expected O, but got Unknown
        //IL_017d: Unknown result type (might be due to invalid IL or missing references)
        Show(demon, visible: true);
        string text = (ArrivalSpell.Value ?? "").Trim();
        if (text.Length == 0 || demon.spellmanger == null)
        {
            return;
        }
        try
        {
            UISpellDatabase instance = UISpellDatabase.Instance;
            if (instance == null || instance.spells == null)
            {
                return;
            }
            UISpellInfo val = null;
            UISpellInfo[] spells = instance.spells;
            foreach (UISpellInfo val2 in spells)
            {
                if (!(val2 == null) && (val2).name != null && (val2).name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    val = val2;
                    break;
                }
            }
            if (val == null)
            {
                DemonLookPlugin.Log.LogWarning(("Заклинания прихода «" + text + "» не нашлось."));
                return;
            }
            if (!demon.spellmanger.ContainSpell(val))
            {
                demon.spellmanger.AddSpell(val, 1);
            }
            NPCSaveData data = demon.Data;
            if (data != null)
            {
                int level = ((CharacterSaveData)data).level;
                int agility = data.agility;
                int intelligence = data.intelligence;
                float currentmp = ((CharacterSaveData)data).currentmp;
                ((CharacterSaveData)data).level = Math.Max(((CharacterSaveData)data).level, 20);
                data.agility = Math.Max(data.agility, 20);
                data.intelligence = Math.Max(data.intelligence, 20);
                ((CharacterSaveData)data).currentmp = demon.maxmp;
                demon.stateMachine.HandleCommand(new UnitCommand((commandsName)14, demon.gameObject, val), false);
                ((MonoBehaviour)demon).StartCoroutine(Restore(demon, level, agility, intelligence, currentmp, centre));
                DemonLookPlugin.Log.LogInfo(("Демон приходит через «" + (val).name + "»."));
            }
        }
        catch (Exception ex)
        {
            DemonLookPlugin.Log.LogWarning(("Приход демона сорвался: " + ex.Message));
        }
    }

    private static IEnumerator Restore(HumaniodUnit demon, int level, int agility, int wit, float mana, Vector3 centre)
    {
        //IL_002b: Unknown result type (might be due to invalid IL or missing references)
        //IL_002d: Unknown result type (might be due to invalid IL or missing references)
        yield return (object)new WaitForSeconds(1.5f);
        if (demon != null)
        {
            demon.transform.position = centre;
        }
        yield return (object)new WaitForSeconds(2.5f);
        NPCSaveData val = ((demon != null) ? demon.Data : null);
        if (val == null)
        {
            yield break;
        }
        ((CharacterSaveData)val).level = level;
        val.agility = agility;
        val.intelligence = wit;
        ((CharacterSaveData)val).currentmp = mana;
        if (demon.spellmanger != null)
        {
            UISpellInfo val2 = null;
            UISpellInfo[] spells = UISpellDatabase.Instance.spells;
            foreach (UISpellInfo val3 in spells)
            {
                if (val3 != null && (val3).name != null && (val3).name.IndexOf(ArrivalSpell.Value.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    val2 = val3;
                    break;
                }
            }
            if (val2 != null)
            {
                demon.spellmanger.RemoveSpell(val2);
            }
        }
        DemonLookPlugin.Log.LogInfo("Приход завершён, одолженное возвращено.");
    }

    private static Vector3 Ground(HumaniodUnit demon)
    {
        //IL_0006: Unknown result type (might be due to invalid IL or missing references)
        //IL_000b: Unknown result type (might be due to invalid IL or missing references)
        //IL_000c: Unknown result type (might be due to invalid IL or missing references)
        //IL_001e: Unknown result type (might be due to invalid IL or missing references)
        //IL_00f8: Unknown result type (might be due to invalid IL or missing references)
        //IL_0053: Unknown result type (might be due to invalid IL or missing references)
        //IL_0054: Unknown result type (might be due to invalid IL or missing references)
        //IL_0059: Unknown result type (might be due to invalid IL or missing references)
        //IL_00fa: Unknown result type (might be due to invalid IL or missing references)
        //IL_00a0: Unknown result type (might be due to invalid IL or missing references)
        //IL_00a5: Unknown result type (might be due to invalid IL or missing references)
        //IL_00aa: Unknown result type (might be due to invalid IL or missing references)
        //IL_00b1: Unknown result type (might be due to invalid IL or missing references)
        //IL_00c7: Unknown result type (might be due to invalid IL or missing references)
        //IL_008f: Unknown result type (might be due to invalid IL or missing references)
        //IL_0090: Unknown result type (might be due to invalid IL or missing references)
        //IL_0095: Unknown result type (might be due to invalid IL or missing references)
        Vector3 val = demon.transform.position;
        NavMeshHit val2 = default(NavMeshHit);
        if (NavMesh.SamplePosition(val, out val2, 2f, -1))
        {
            return val2.position;
        }
        DemonLookPlugin.Log.LogInfo("Демон стоит вне проходимой земли — ищу вход области.");
        try
        {
            AreaManager instance = AreaManager.Instance;
            if (instance == null || instance.entrances == null || instance.entrances.Length == 0)
            {
                return Walkable(val);
            }
            int num = Mathf.Clamp(DoorIndex.Value, 0, instance.entrances.Length - 1);
            MapSwitcher val3 = instance.entrances[num];
            if (val3 == null)
            {
                return Walkable(val);
            }
            val = Around(val3.transform.position);
            demon.transform.position = val;
            DemonLookPlugin.Log.LogInfo($"Демон поставлен у входа {num}: {val}.");
        }
        catch (Exception ex)
        {
            DemonLookPlugin.Log.LogWarning(("Не смог найти вход области: " + ex.Message));
        }
        return val;
    }

    private static Vector3 Around(Vector3 door)
    {
        //IL_0024: Unknown result type (might be due to invalid IL or missing references)
        //IL_0036: Unknown result type (might be due to invalid IL or missing references)
        //IL_003c: Unknown result type (might be due to invalid IL or missing references)
        //IL_0041: Unknown result type (might be due to invalid IL or missing references)
        //IL_0086: Unknown result type (might be due to invalid IL or missing references)
        //IL_0087: Unknown result type (might be due to invalid IL or missing references)
        //IL_0057: Unknown result type (might be due to invalid IL or missing references)
        //IL_005c: Unknown result type (might be due to invalid IL or missing references)
        //IL_005d: Unknown result type (might be due to invalid IL or missing references)
        //IL_0062: Unknown result type (might be due to invalid IL or missing references)
        //IL_0078: Unknown result type (might be due to invalid IL or missing references)
        float value = FromDoor.Value;
        NavMeshHit val = default(NavMeshHit);
        for (int i = 0; i < 8; i++)
        {
            float num = (float)i * (float)Math.PI * 2f / 8f;
            if (NavMesh.SamplePosition(door + new Vector3(Mathf.Cos(num), 0f, Mathf.Sin(num)) * value, out val, 3f, -1))
            {
                Vector3 val2 = val.position - door;
                if (!(val2.sqrMagnitude < value * value * 0.25f))
                {
                    return val.position;
                }
            }
        }
        return Walkable(door);
    }

    private static Vector3 Walkable(Vector3 wanted)
    {
        //IL_0000: Unknown result type (might be due to invalid IL or missing references)
        //IL_0014: Unknown result type (might be due to invalid IL or missing references)
        //IL_0010: Unknown result type (might be due to invalid IL or missing references)
        NavMeshHit val = default(NavMeshHit);
        if (!NavMesh.SamplePosition(wanted, out val, 12f, -1))
        {
            return wanted;
        }
        return val.position;
    }

    private static List<UnitAttribute> Ring(UnitInfo template, Vector3 centre, HumaniodUnit demon)
    {
        //IL_0028: Unknown result type (might be due to invalid IL or missing references)
        //IL_003a: Unknown result type (might be due to invalid IL or missing references)
        //IL_0049: Unknown result type (might be due to invalid IL or missing references)
        //IL_004e: Unknown result type (might be due to invalid IL or missing references)
        //IL_0053: Unknown result type (might be due to invalid IL or missing references)
        //IL_0055: Unknown result type (might be due to invalid IL or missing references)
        //IL_0056: Unknown result type (might be due to invalid IL or missing references)
        //IL_005b: Unknown result type (might be due to invalid IL or missing references)
        //IL_0078: Unknown result type (might be due to invalid IL or missing references)
        //IL_0085: Unknown result type (might be due to invalid IL or missing references)
        //IL_008f: Unknown result type (might be due to invalid IL or missing references)
        //IL_0095: Unknown result type (might be due to invalid IL or missing references)
        List<UnitAttribute> list = new List<UnitAttribute>();
        for (int i = 0; i < MageCount.Value; i++)
        {
            float num = (float)i * (float)Math.PI * 2f / (float)MageCount.Value;
            Vector3 wanted = centre + new Vector3(Mathf.Cos(num), 0f, Mathf.Sin(num)) * Radius.Value;
            UnitAttribute val = Make(template, Walkable(wanted), MageSide());
            if (!(val == null))
            {
                val.transform.LookAt(new Vector3(centre.x, val.transform.position.y, centre.z));
                Strengthen(val, MageEdge.Value);
                Stay(val);
                Person(val, list.Count + 1);
                Outfit(val, list.Count, MageRoles.Value, MageTier.Value, MageQuality.Value, null,
                    adorn: true, armsGrade: MageArms.Value);
                Name(val, list.Count + 1);

                list.Add(val);
                ring.Add(val);
                places.Add(val.transform.position);
            }
        }
        return list;
    }

    /// <summary>
    /// Makes a summoner a dark magician outright: the mark, the school, the branch at full.
    ///
    /// Три замка стоят перед тем, кто должен колдовать, и открыть надо все три. Знак магии —
    /// без него школа складывается в данные и не показывается, а панель для не-мага не
    /// строится вовсе. Сама школа в списке — иначе её нет. И талант мастерства — это и есть
    /// открытая ветка; без него заклинания выданы и недоступны.
    ///
    /// Уровень ветки ставится предельным, а не прибавляется по единице: «AddTalent» для уже
    /// имеющегося таланта поднимает его ровно на одну ступень, и добирать так до пятнадцатой
    /// значило бы звать его пятнадцать раз.
    /// </summary>
    private static void Blackened(UnitAttribute mage)
    {
        try
        {
            if (mage.Data != null && !mage.Data.isMagician) mage.Data.isMagician = true;

            NPCSaveData mind = mage.Data as NPCSaveData;

            if (mind != null)
            {
                if (mind.skillSet == null) mind.skillSet = new List<SkillSet>();
                if (!mind.skillSet.Contains(SkillSet.black)) mind.skillSet.Add(SkillSet.black);
            }

            UITalentDatabase lore = UITalentDatabase.Instance;
            if (lore == null || mage.talentmanger == null) return;

            UITalentInfo school = lore.GetMasteryTalent(SkillSet.black);
            if (school == null) return;

            int top = school.maxPoints > 0 ? school.maxPoints : 15;

            spell.TalentBase held = mage.talentmanger.FindTalent(school);

            if (held == null)
            {
                mage.talentmanger.AddTalent(school, top);
                held = mage.talentmanger.FindTalent(school);
            }

            if (held != null && held.level < top) held.level = top;
        }
        catch (Exception e)
        {
            DemonLookPlugin.Log.LogWarning("Не смог открыть магу школу тьмы: " + e.Message);
        }
    }

    private static void Arm(UnitAttribute mage, int rank)
    {
        //IL_0053: Unknown result type (might be due to invalid IL or missing references)
        //IL_005a: Invalid comparison between Unknown and I4
        if (mage == null || mage.spellmanger == null)
        {
            return;
        }
        try
        {
            // Сперва школа, потом заклинания. Эти пятеро — тёмные маги, и всё их устройство
            // держится на одной ветке: без знака магии игра не считает их способными
            // колдовать вовсе, а без мастерства школы выданное лежит мёртвым грузом.
            //
            // Ветка ставится на полную: они подняли Владыку, а не сдали зачёт подмастерья.
            // И ставится здесь же, чтобы не разойтись с общим правилом «школы по уровню» —
            // то успевает раньше и открыло бы им боевые школы по посоху в руках.
            Blackened(mage);

            UISpellDatabase instance = UISpellDatabase.Instance;
            if (instance == null || instance.spells == null)
            {
                return;
            }
            // Школа тьмы у заклинания помечена и номером набора, и именем. По одному номеру
            // магам доставалось всего два заклинания на пятерых — остальное тёмное лежало под
            // другими наборами и мимо них проходило. Берём и по имени тоже, и сразу на полную
            // ступень: пятеро, поднявшие Владыку, знают своё дело лучше подмастерьев.
            List<string> given = new List<string>();
            UISpellInfo[] spells = instance.spells;

            foreach (UISpellInfo val in spells)
            {
                if (val == null || mage.spellmanger.ContainSpell(val)) continue;

                bool dark = (int)val.SkillSet == 105
                    || (((UnityEngine.Object)val).name ?? "").StartsWith("Spell_Dark", StringComparison.OrdinalIgnoreCase);

                if (!dark) continue;

                mage.spellmanger.AddSpell(val, rank);
                given.Add(((UnityEngine.Object)val).name);
            }

            int num = given.Count;
            mage.spellmanger.onlyActionBar = false;
            mage.spellmanger.useAutoCast = true;
            if (num > 0)
            {
                DemonLookPlugin.Log.LogInfo($"Магу дано заклинаний {num} на ступени "
                    + $"{rank}: {string.Join(", ", given.ToArray())}");
            }
        }
        catch (Exception ex)
        {
            DemonLookPlugin.Log.LogWarning(("Не смог вооружить мага: " + ex.Message));
        }
    }

    /// <summary>Reads a list of points written as x,y,z separated by semicolons.</summary>
    internal static List<Vector3> Parse(string written)
    {
        List<Vector3> found = new List<Vector3>();

        foreach (string entry in (written ?? "").Split(';'))
        {
            string[] parts = entry.Trim().Split(',');
            if (parts.Length != 3) continue;

            float x, y, z;
            NumberStyles style = NumberStyles.Float;
            CultureInfo where = CultureInfo.InvariantCulture;

            if (!float.TryParse(parts[0].Trim(), style, where, out x)) continue;
            if (!float.TryParse(parts[1].Trim(), style, where, out y)) continue;
            if (!float.TryParse(parts[2].Trim(), style, where, out z)) continue;

            found.Add(new Vector3(x, y, z));
        }

        return found;
    }

    private static List<Vector3> Spots()
    {
        return Parse(HeroSpot.Value);
    }

    /// <summary>Adds the place the player is standing in to one of the point lists.</summary>
    private static void Mark(ConfigEntry<string> list, string what)
    {
        UnitAttribute player = (UnitAttribute)(object)gameManager.currentplayUnit;
        if (player == null)
        {
            DemonLookPlugin.Log.LogWarning("Некого отмечать: игрока нет.");
            return;
        }

        Vector3 here = player.transform.position;
        string one = string.Format(CultureInfo.InvariantCulture, "{0:0.##},{1:0.##},{2:0.##}",
            here.x, here.y, here.z);

        // Список набирается по порядку и начинается заново, когда полон.
        //
        // Было скользящее окно: каждое нажатие дописывало точку в конец, а самая старая
        // выпадала. Задумано было, чтобы не мешали остатки с прошлого раза, а вышло хуже
        // некуда: увидеть, сколько точек уже поставлено, нельзя; лишнее нажатие навсегда
        // сдвигает весь набор; метки с прошлой пещеры доживают до следующей и встают в
        // строй наравне с новыми. В конфиге от этого оказались три пары дублей и пять точек
        // за полсотни метров от круга.
        //
        // Теперь просто: набрали восемь — список полон, следующее нажатие начинает первый
        // заново. И каждое нажатие говорит, которое оно по счёту.
        List<Vector3> had = Parse(list.Value);

        // Полон — значит полон. Прежде следующее нажатие молча стирало всю разметку и
        // начинало заново; одно случайное нажатие уничтожало работу целиком, и ровно это и
        // случилось: восемь мест превратились в одно, а весь отряд стал выходить из него.
        // Очистка есть отдельной клавишей, и решать это должна она, а не догадка.
        if (had.Count >= HeroCount.Value)
        {
            DemonLookPlugin.Log.LogWarning($"{what}: список полон, {had.Count} из "
                + $"{HeroCount.Value}. Чтобы начать заново — Shift и та же клавиша.");
            return;
        }

        // Двух бойцов в одной точке не бывает. Нажали дважды не сходя с места — считаем это
        // промахом и говорим об этом, а не молча записываем дубль.
        foreach (Vector3 kept in had)
        {
            if (Vector3.Distance(kept, here) < 1f)
            {
                DemonLookPlugin.Log.LogWarning(what + ": здесь метка уже стоит, "
                    + "отойдите хотя бы на метр.");
                return;
            }
        }

        had.Add(here);

        bool dropped = false;

        List<string> written = new List<string>();
        foreach (Vector3 kept in had)
        {
            written.Add(string.Format(CultureInfo.InvariantCulture, "{0:0.##},{1:0.##},{2:0.##}",
                kept.x, kept.y, kept.z));
        }

        list.Value = string.Join(";", written.ToArray());

        DemonLookPlugin.Log.LogInfo($"{what} {had.Count} из {HeroCount.Value}: {one}"
            + (dropped ? " — самая старая отметка вытеснена." : ""));
    }

    private static void Unmark(ConfigEntry<string> list, string what)
    {
        list.Value = "";
        DemonLookPlugin.Log.LogInfo(what + ": список стёрт, размечайте заново.");
    }

    internal static void Mark() { Mark(HeroSpot, "Место появления"); }

    internal static void Unmark() { Unmark(HeroSpot, "Места появления"); }

    internal static void MarkWay() { Mark(HeroWay, "Промежуточная точка"); }

    internal static void UnmarkWay() { Unmark(HeroWay, "Промежуточные точки"); }

    internal static void MarkEnd() { Mark(HeroEnd, "Конечная точка"); }

    internal static void UnmarkEnd() { Unmark(HeroEnd, "Конечные точки"); }

    private static bool Reachable(Vector3 wanted, Vector3 to, out Vector3 stand)
    {
        //IL_0001: Unknown result type (might be due to invalid IL or missing references)
        //IL_0002: Unknown result type (might be due to invalid IL or missing references)
        //IL_0007: Unknown result type (might be due to invalid IL or missing references)
        //IL_001c: Unknown result type (might be due to invalid IL or missing references)
        //IL_0021: Unknown result type (might be due to invalid IL or missing references)
        //IL_0026: Unknown result type (might be due to invalid IL or missing references)
        //IL_002c: Expected O, but got Unknown
        //IL_002d: Unknown result type (might be due to invalid IL or missing references)
        //IL_0032: Unknown result type (might be due to invalid IL or missing references)
        //IL_003f: Unknown result type (might be due to invalid IL or missing references)
        //IL_0045: Invalid comparison between Unknown and I4
        stand = wanted;
        NavMeshHit val = default(NavMeshHit);
        if (!NavMesh.SamplePosition(wanted, out val, 4f, -1))
        {
            return false;
        }
        stand = val.position;
        NavMeshPath val2 = new NavMeshPath();
        if (!NavMesh.CalculatePath(stand, to, -1, val2))
        {
            return false;
        }
        return (int)val2.status == 0;
    }

    private static IEnumerator Guard(HumaniodUnit demon)
    {
        while (guarded && !(demon == null) && demon.Data != null)
        {
            if (((CharacterSaveData)demon.Data).currenthp < demon.maxhp)
            {
                ((CharacterSaveData)demon.Data).currenthp = demon.maxhp;
            }
            ((CharacterSaveData)demon.Data).isdead = false;
            yield return (object)new WaitForSeconds(0.2f);
        }
    }

    /// <summary>Puts the demon beyond harm for as long as the scene owns him.</summary>
    private static void Untouched(HumaniodUnit demon, bool safe)
    {
        if (!Untouchable.Value || demon == null || demon.Data == null) return;

        try
        {
            // Родной замок игры на здоровье: она сама пользуется им там, где герой обязан
            // дожить до конца сцены. Удары проходят, и кровь, и звук — а полоска стоит.
            ((CharacterSaveData)(object)demon.Data).hpLock = safe;

            DemonLookPlugin.Log.LogInfo(safe
                ? "Демон неуязвим: сцена его не отпустила."
                : "Демон снова уязвим: круг догорел.");
        }
        catch (Exception e)
        {
            DemonLookPlugin.Log.LogWarning("Не смог закрыть демона от урона: " + e.Message);
        }
    }

    private static void Hold(UnitAttribute who, bool held)
    {
        if (who == null)
        {
            return;
        }
        try
        {
            who.Pause(held);
        }
        catch (Exception ex)
        {
            DemonLookPlugin.Log.LogWarning(("Не смог удержать демона: " + ex.Message));
        }
    }

    private static IEnumerator FindHunters()
    {
        party.Clear();
        string text = (HunterUnit.Value ?? "").Trim();
        if (text.Length > 0)
        {
            string[] array = text.Split(',');
            foreach (string text2 in array)
            {
                string name = text2.Trim();
                if (name.Length != 0)
                {
                    AsyncOperationHandle<UnitInfo> load = Addressables.LoadAssetAsync<UnitInfo>((object)("unitdata/" + name));
                    yield return load;
                    if ((int)load.Status == 1 && load.Result != null)
                    {
                        party.Add(load.Result);
                    }
                    else
                    {
                        DemonLookPlugin.Log.LogWarning(("Шаблона «" + name + "» в каталоге нет, пропускаю."));
                    }
                }
            }
        }
        if (party.Count > 0)
        {
            DemonLookPlugin.Log.LogInfo($"Отряд охотников собран: {party.Count} шаблонов.");
        }
        else
        {
            Candidates();
        }
    }

    private static void Candidates()
    {
        try
        {
            string[] array = new string[19]
            {
                "knight", "paladin", "templar", "soldier", "warrior", "swordsman", "guard", "merc", "hero", "hunter",
                "slayer", "champion", "captain", "veteran", "armor", "armour", "fighter", "ranger", "gladiator"
            };
            List<string> list = new List<string>();
            List<string> list2 = new List<string>();
            foreach (UnityEngine.AddressableAssets.ResourceLocators.IResourceLocator resourceLocator in Addressables.ResourceLocators)
            {
                if (resourceLocator == null || resourceLocator.Keys == null)
                {
                    continue;
                }
                foreach (object key in resourceLocator.Keys)
                {
                    if (!(key is string text) || !text.StartsWith("unitdata/", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    string text2 = text.Substring("unitdata/".Length);
                    if (list.Contains(text2) || list2.Contains(text2))
                    {
                        continue;
                    }
                    bool flag = false;
                    string[] array2 = array;
                    foreach (string value in array2)
                    {
                        if (text2.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            flag = true;
                            break;
                        }
                    }
                    (flag ? list : list2).Add(text2);
                }
            }
            list.Sort();
            list2.Sort();
            DemonLookPlugin.Log.LogInfo(("Шаблон охотников не задан. Существ всего " + $"{list.Count + list2.Count}, из них похожих на воинов {list.Count}:"));
            for (int j = 0; j < list.Count; j += 20)
            {
                DemonLookPlugin.Log.LogInfo(("  воины: " + string.Join(", ", list.GetRange(j, Math.Min(20, list.Count - j)).ToArray())));
            }
            for (int k = 0; k < list2.Count && k < 200; k += 20)
            {
                DemonLookPlugin.Log.LogInfo(("  прочие: " + string.Join(", ", list2.GetRange(k, Math.Min(20, list2.Count - k)).ToArray())));
            }
        }
        catch (Exception ex)
        {
            DemonLookPlugin.Log.LogWarning(("Не смог перебрать каталог: " + ex.Message));
        }
    }

    private static IEnumerator After(HumaniodUnit demon, List<UnitAttribute> mages)
    {
        float waited = 0f;
        while (waited < AfterWait.Value)
        {
            bool flag = false;
            foreach (UnitAttribute item in came)
            {
                if (!(item == null) && item.Data != null && !item.Data.isdead)
                {
                    flag = true;
                    break;
                }
            }
            if (!flag)
            {
                break;
            }
            waited += 0.5f;
            yield return (object)new WaitForSeconds(0.5f);
        }
        if (waited >= AfterWait.Value)
        {
            DemonLookPlugin.Log.LogInfo("Бой затянулся — заключительную сцену пропускаю.");
            Unpost();
            Untouched(demon, false);
            Hold((UnitAttribute)(object)demon, held: false);
            yield break;
        }
        DemonLookPlugin.Log.LogInfo("Охотники пали, играю заключение.");

        yield return ((MonoBehaviour)(object)demon).StartCoroutine(Regather());

        // Сколько круг потерял на этом бою. Без этой строки о ходе драки можно судить только
        // по тому, кто остался стоять, — а это ответ на другой вопрос.
        foreach (UnitAttribute mage in mages)
        {
            if (mage == null || mage.Data == null) continue;

            DemonLookPlugin.Log.LogInfo($"  {mage.Data.unitname}: "
                + $"{((CharacterSaveData)(object)mage.Data).currenthp:0} из {mage.maxhp:0}"
                + (mage.Data.isdead ? " (пал)" : ""));
        }
        yield return (object)new WaitForSeconds(2f);
        Hold((UnitAttribute)(object)demon, held: true);
        yield return Scene.Say("Круг", WordsAfter.Value);
        int num = 0;
        foreach (UnitAttribute mage in mages)
        {
            if (!(mage == null) && mage.Data != null && !mage.Data.isdead)
            {
                mage.Die(mage, true);
                num++;
            }
        }
        DemonLookPlugin.Log.LogInfo($"Маги отдали сердца: {num}.");

        Wider(demon);
        yield return (object)new WaitForSeconds(2f);
        yield return Scene.Say("Печать", WordsQuest.Value);
        Untouched(demon, false);
        Hold((UnitAttribute)(object)demon, held: false);
        Unpost();

        DemonLookPlugin.Log.LogInfo("Заключение сыграно, управление возвращено.");
    }

    /// <summary>
    /// Makes the vessel bigger, which is what the circle died for.
    ///
    /// Потенциал — это потолок всего, чем персонаж когда-либо станет: сумма характеристик
    /// не поднимется выше него, и в окне персонажа он стоит вторым числом рядом с «потенциал».
    /// Демону он и так даётся при рождении сверх обычного, но то — порода. А это — цена
    /// пяти жизней, отданных добровольно, и приходит она в тот самый миг, когда их отдают.
    ///
    /// Даётся за сам ритуал, а не за число погибших: круг собрался и довёл дело до конца,
    /// а сколько его к тому мигу осталось — их беда, не его заслуга.
    /// </summary>
    private static void Wider(HumaniodUnit demon)
    {
        if (demon == null) return;
        if (RitualPotential.Value <= 0 && RitualAttributes.Value <= 0) return;

        try
        {
            NPCSaveData mind = demon.Data as NPCSaveData;
            if (mind == null || mind.humanAttribute == null) return;

            mind.humanAttribute.potential += RitualPotential.Value;

            // Не только простор для роста, но и сама сила: пятеро отдали жизни, и отданное
            // должно быть видно сразу, а не однажды потом. Потолок поднимается первым, иначе
            // прибавленное встанет выше того, до чего этому существу позволено дорасти.
            if (RitualAttributes.Value > 0)
            {
                for (int i = 0; i < 6; i++)
                {
                    mind.humanAttribute[i] = UnityEngine.Mathf.Min(
                        100, mind.humanAttribute[i] + RitualAttributes.Value);
                }

                if (mind.humanAttribute.potential < mind.humanAttribute.Sum)
                {
                    mind.humanAttribute.potential = mind.humanAttribute.Sum;
                }

                mind.SetLevel();
            }

            // Без пересчёта окно персонажа покажет прежнее число, пока что-нибудь другое не
            // заставит его обновиться.
            demon.UpdateAttribute();

            DemonLookPlugin.Log.LogInfo("Маги отдали свои жизни ради меня: потенциал "
                + $"увеличен на {RitualPotential.Value}, "
                + $"каждая характеристика на {RitualAttributes.Value}, "
                + $"уровень {mind.level}.");

            GameController.ShowMessage($"Потенциал +{RitualPotential.Value}, "
                + $"все характеристики +{RitualAttributes.Value}", 4f);
        }
        catch (Exception e)
        {
            DemonLookPlugin.Log.LogWarning("Не смог принять дар круга: " + e.Message);
        }
    }

    /// <summary>Keeps both sides fighting each other rather than only the demon.</summary>
    private static IEnumerator Battle(HumaniodUnit demon)
    {
        // Приказ отдаётся один раз на цель и повторяется, только когда цель пала: если
        // командовать каждые две секунды, боец бросает замах и начинает подход заново, и со
        // стороны отряд топчется на месте вместо драки.
        Dictionary<UnitAttribute, UnitAttribute> aiming = new Dictionary<UnitAttribute, UnitAttribute>();
        float waited = 0f;

        melee = true;

        while (melee && waited < AfterWait.Value)
        {
            int fighting = 0;

            fighting += Send(came, ring, aiming);
            fighting += Send(ring, came, aiming);

            if (fighting == 0) yield break;

            waited += 1f;
            yield return (object)new WaitForSeconds(1f);
        }
    }

    /// <summary>Points everyone on one side at the nearest living one on the other.</summary>
    private static int Send(List<UnitAttribute> ours, List<UnitAttribute> theirs,
        Dictionary<UnitAttribute, UnitAttribute> aiming)
    {
        int fighting = 0;

        foreach (UnitAttribute one in ours)
        {
            if (one == null || one.Data == null || one.Data.isdead) continue;
            if (one.stateMachine == null) continue;

            fighting++;

            UnitAttribute had;
            if (aiming.TryGetValue(one, out had) && had != null && had.Data != null && !had.Data.isdead)
            {
                continue;
            }

            UnitAttribute mark = Nearest(one, theirs);
            if (mark == null) continue;

            aiming[one] = mark;
            one.stateMachine.HandleCommand(new UnitCommand((commandsName)5, mark.gameObject), false);
        }

        return fighting;
    }

    /// <summary>
    /// Sends the surviving circle back to the places it stood in, and waits.
    ///
    /// Приказ вернуться не держался, и причина не в том, что его мало повторяли. Боевой
    /// приказ в этой игре — не команда, а распорядок. «MoveAttackingState» живёт в маге
    /// постоянно и каждый тик сам отдаёт ему «move» к своей цели — см. его Moving().
    /// Наш «иди на место» — состояние временное, оно живёт до следующего кадра, и спорить
    /// временным с постоянным бесполезно, сколько ни повторяй.
    ///
    /// Поэтому здесь меняется сам распорядок. «standGuard» — тоже распорядок, но его
    /// CanEngage равен false: маг перестаёт искать, с кем драться, и держится той точки, куда
    /// поставлен. После этого одного приказа идти достаточно.
    /// </summary>
    private static IEnumerator Regather()
    {
        if (places.Count == 0) yield break;

        // Драка кончилась — распорядитель боя тоже.
        melee = false;

        int sent = 0;

        for (int i = 0; i < ring.Count && i < places.Count; i++)
        {
            UnitAttribute mage = ring[i];
            if (mage == null || mage.Data == null || mage.Data.isdead) continue;
            if (mage.stateMachine == null) continue;

            // Место как предмет в мире: «standGuard» берёт точку стояния из объекта-цели,
            // а не из координат, — так же, как игра держит своих на idlePoint.
            GameObject post = Post(places[i]);

            mage.stateMachine.HandleCommand(new UnitCommand((commandsName)2, post), false);
            mage.stateMachine.HandleCommand(new UnitCommand((commandsName)10, post), true);

            sent++;
        }

        DemonLookPlugin.Log.LogInfo($"Круг возвращается на места: {sent}.");

        float waited = 0f;
        float closest = float.MaxValue;
        float still = 0f;

        // Сколько терпеть, когда дело перестало двигаться. Ждать надо не по часам, а
        // пока расстояние сокращается: если маг упёрся в стену или ему перебили ногу,
        // следующие три минуты этого не поправят, а сцена всё это время стоит молча.
        const float patience = 6f;

        while (waited < MarchTime.Value)
        {
            int walking = 0;
            float left = 0f;

            for (int i = 0; i < ring.Count && i < places.Count; i++)
            {
                UnitAttribute mage = ring[i];
                if (mage == null || mage.Data == null || mage.Data.isdead) continue;

                float far = Vector3.Distance(mage.transform.position, places[i]);
                if (far <= 2f) continue;

                walking++;
                left += far;
            }

            if (walking == 0) break;

            if (left < closest - 0.5f)
            {
                closest = left;
                still = 0f;
            }
            else
            {
                still += 0.25f;

                if (still >= patience)
                {
                    DemonLookPlugin.Log.LogInfo($"Больше никто не приближается — ждать нечего "
                        + $"(ещё в пути {walking}).");
                    break;
                }
            }

            waited += 0.25f;
            yield return (object)new WaitForSeconds(0.25f);
        }

        // Кто не дошёл — с именем и расстоянием. По одной строке видно, спор это о
        // полуметре или маг вообще никуда не шёл.
        for (int i = 0; i < ring.Count && i < places.Count; i++)
        {
            UnitAttribute mage = ring[i];
            if (mage == null || mage.Data == null || mage.Data.isdead) continue;

            float far = Vector3.Distance(mage.transform.position, places[i]);
            if (far > 2f)
            {
                DemonLookPlugin.Log.LogInfo($"  {mage.Data.unitname} не дошёл: {far:0.#} м.");
            }
        }

        // Лицом к середине, как стояли, когда звали. Даже если кто-то не дошёл — пусть
        // смотрит туда: сцена важнее точности до метра.
        for (int i = 0; i < ring.Count; i++)
        {
            UnitAttribute mage = ring[i];
            if (mage == null || mage.Data == null || mage.Data.isdead) continue;

            mage.transform.LookAt(new Vector3(heart.x, mage.transform.position.y, heart.z));
        }

        DemonLookPlugin.Log.LogInfo(waited < MarchTime.Value
            ? "Круг сомкнулся заново."
            : "Не все вернулись в круг — играю заключение так.");

        yield return (object)new WaitForSeconds(1f);
    }

    private static void Counter(List<UnitAttribute> mages, HumaniodUnit demon)
    {
        //IL_005f: Unknown result type (might be due to invalid IL or missing references)
        //IL_006a: Expected O, but got Unknown
        //IL_0092: Unknown result type (might be due to invalid IL or missing references)
        //IL_009d: Expected O, but got Unknown
        if (mages == null)
        {
            return;
        }
        int num = 0;
        foreach (UnitAttribute mage in mages)
        {
            if (mage == null || mage.Data == null || mage.Data.isdead || mage.stateMachine == null)
            {
                continue;
            }
            if (MagesGuard.Value)
            {
                mage.stateMachine.HandleCommand(new UnitCommand((commandsName)6, demon.gameObject), false);
                num++;
                continue;
            }
            UnitAttribute val = Nearest(mage, came);
            if (!(val == null))
            {
                mage.stateMachine.HandleCommand(new UnitCommand((commandsName)5, val.gameObject), false);
                num++;
            }
        }
        DemonLookPlugin.Log.LogInfo((MagesGuard.Value ? $"Маги встали при демоне: {num}." : $"Маги приняли бой: {num}."));

        // Охотники шли за демоном и в него одного и упирались — маги стояли рядом и в счёт
        // не шли. Теперь у каждой стороны есть свой противник, и демон в этой драке один из
        // многих, а не единственная мишень.
        ((MonoBehaviour)(object)demon).StartCoroutine(Battle(demon));
    }

    /// <summary>The living mage nearest this hunter, or nothing once the circle is gone.</summary>
    internal static UnitAttribute Guarded(UnitAttribute hunter)
    {
        // Пока идёт сцена — никого и ни на кого. Перенаправление цели я сперва поставил без
        // этой оглядки, и вышло хуже, чем было: раньше охотник во время перемирия не находил
        // никого и стоял, а теперь получал от меня мага там, где сам не нашёл ничего, — и
        // кидался в бой до первой реплики.
        if (Truce) return null;

        if (hunter == null || ring.Count == 0 || !came.Contains(hunter)) return null;

        return Nearest(hunter, ring);
    }

    /// <summary>True when these two stand on opposite sides of the summoning.</summary>
    internal static bool Foes(UnitAttribute A, UnitAttribute B)
    {
        if (A == null || B == null || A == B) return false;
        if (ring.Count == 0 || came.Count == 0) return false;

        // Маги стоят в neutralNPC, охотники — в playerEnemy, и игра считает эти две стороны
        // друг другу никем: отсюда «маги видят и не атакуют». Вражду между ними объявляем
        // сами, как и перемирие до этого, не трогая сами фракции.
        return (ring.Contains(A) && came.Contains(B)) || (came.Contains(A) && ring.Contains(B));
    }

    // Пустые метки мест. Живут до конца сцены: «standGuard» держится за сам объект, и
    // если стереть его раньше времени, маг останется без точки прямо во время речи.
    private static readonly List<GameObject> posts = new List<GameObject>();

    private static GameObject Post(Vector3 where)
    {
        GameObject post = new GameObject("DemonLookRingPost");
        post.transform.position = where;

        posts.Add(post);
        return post;
    }

    /// <summary>Clears the markers once nobody is standing on them any more.</summary>
    internal static void Unpost()
    {
        foreach (GameObject post in posts)
        {
            if (post != null) UnityEngine.Object.Destroy(post);
        }

        posts.Clear();
    }

    private static UnitAttribute Nearest(UnitAttribute from, List<UnitAttribute> mages)
    {
        //IL_0042: Unknown result type (might be due to invalid IL or missing references)
        //IL_004d: Unknown result type (might be due to invalid IL or missing references)
        if (mages == null)
        {
            return null;
        }
        UnitAttribute result = null;
        float num = float.MaxValue;
        foreach (UnitAttribute mage in mages)
        {
            if (!(mage == null) && mage.Data != null && !mage.Data.isdead)
            {
                float num2 = Vector3.Distance(mage.transform.position, from.transform.position);
                if (!(num2 >= num))
                {
                    num = num2;
                    result = mage;
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Brings the hunters in without making them walk.
    ///
    /// Поход был красив и ненадёжен. Он держался на том, что у каждого бойца есть путь до
    /// своего места, а путь зависел от меток, от навигации пещеры, от того, не осталась ли
    /// метка от прошлого раза. Стоило одному не дойти — и речь не начиналась вовсе, потому
    /// что ждала всех. Столько раз, сколько это ломалось, чинить это дальше — упрямство.
    ///
    /// Здесь никто никуда не идёт. Экран гаснет, отряд встаёт там, где ему положено стоять,
    /// экран возвращается. Ни путей, ни ожидания, ни причин не сложиться.
    /// </summary>
    private static IEnumerator Appear(HumaniodUnit demon, Vector3 centre)
    {
        bool dark = Dim(true);

        // Полсекунды на затемнение — и только потом расстановка, чтобы появление не
        // случилось на глазах.
        if (dark) yield return (object)new WaitForSecondsRealtime(0.6f);

        Hunters(centre, demon);
        Stand(centre);

        yield return (object)new WaitForSecondsRealtime(0.4f);

        yield return ((MonoBehaviour)demon).StartCoroutine(Scene.Open(2f));

        Dim(false);

        if (dark) yield return (object)new WaitForSecondsRealtime(0.5f);
    }

    /// <summary>Darkens the screen, or gives it back. True when it worked.</summary>
    private static bool Dim(bool down)
    {
        try
        {
            if (FadeOverlay.instance == null) return false;

            if (down)
            {
                gameManager.GM.EnterCutScene();
                FadeOverlay.instance.FadeOut(0.5f);
            }
            else
            {
                FadeOverlay.instance.FadeIn(0.5f);
                gameManager.GM.QuitCutScene();
            }

            return true;
        }
        catch (Exception e)
        {
            DemonLookPlugin.Log.LogWarning("Не смог затемнить экран: " + e.Message);
            return false;
        }
    }

    /// <summary>Puts every hunter on his place at once, facing the circle.</summary>
    private static void Stand(Vector3 centre)
    {
        List<Vector3> ends = Parse(HeroEnd.Value);

        int stood = 0;

        for (int i = 0; i < came.Count; i++)
        {
            UnitAttribute hunter = came[i];
            if (hunter == null || hunter.Data == null || hunter.Data.isdead) continue;

            // Места те же, что были конечными для похода. Кому метки не досталось — тому
            // своё место в кольце вокруг середины: пускать по второму кругу нельзя, двое
            // встанут в одну точку и влезут друг в друга.
            Vector3 spot = (i < ends.Count)
                ? ends[i]
                : Around(centre, i, came.Count);

            try
            {
                // Телепорт, а не подмена координат: у бойца есть навигация, и её надо
                // переставить вместе с ним, иначе он будет считать, что стоит в другом месте.
                hunter.Teleport(spot);
            }
            catch
            {
                hunter.transform.position = spot;
            }

            hunter.transform.LookAt(new Vector3(centre.x, hunter.transform.position.y, centre.z));

            standing.Add(hunter);
            Hold(hunter, held: false);

            stood++;
        }

        told = true;

        DemonLookPlugin.Log.LogInfo($"Охотники встали вокруг круга: {stood} из {came.Count}"
            + ((ends.Count > 0) ? $", мест размечено {ends.Count}" : ", мест нет — встали кольцом")
            + ".");
    }

    /// <summary>A place on a ring around the middle, when nothing was marked.</summary>
    private static Vector3 Around(Vector3 centre, int which, int total)
    {
        float angle = (total > 0) ? ((float)which * Mathf.PI * 2f / total) : 0f;
        float far = Mathf.Max(3f, MarchClose.Value);

        Vector3 spot = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * far;

        return Walkable(spot);
    }

    private static IEnumerator Await(HumaniodUnit demon)
    {
        float waited = 0f;
        bool byPlace = Parse(HeroEnd.Value).Count > 0 || Parse(HeroWay.Value).Count > 0;

        while (waited < MarchTime.Value)
        {
            int walking = 0;

            foreach (UnitAttribute hunter in came)
            {
                if (hunter == null || hunter.Data == null || hunter.Data.isdead) continue;

                // Когда места расставлены, «пришёл» значит «стоит там, куда его поставили»:
                // до демона от дальнего края строя может быть и тридцать метров, и мерить
                // приход по нему — значит требовать, чтобы строй сошёлся в одну точку.
                if (byPlace)
                {
                    if (!standing.Contains(hunter)) walking++;
                }
                else if (Vector3.Distance(hunter.transform.position, demon.transform.position) > MarchClose.Value)
                {
                    walking++;
                }
            }

            // Ждём всех, а не ближайшего. Прежде довольно было одного подошедшего — и речь
            // звучала, пока девять ещё шли от тридцати метров. Со стороны это выглядело так,
            // будто бегут только те, кто стоял поближе, хотя шли все.
            if (walking == 0)
            {
                DemonLookPlugin.Log.LogInfo("Охотники на местах — все.");
                yield break;
            }

            waited += 0.25f;
            yield return (object)new WaitForSeconds(0.25f);
        }

        DemonLookPlugin.Log.LogInfo("Не все дошли за отведённое время — говорю с теми, кто есть.");
    }

    private static void March(HumaniodUnit demon, List<UnitAttribute> mages, Vector3 centre)
    {
        List<Vector3> way = Parse(HeroWay.Value);
        List<Vector3> ends = Parse(HeroEnd.Value);

        standing.Clear();
        int sent = 0;

        // На размеченное место встают точно: у каждого своя точка, и соседние отстоят на
        // шаг-другой. Прежний допуск в девять метров годится только для «добежал до круга» —
        // на строю он означал бы, что охотник зачтён, стоя у чужой отметки.
        float near = (ends.Count > 0) ? 2.5f : MarchClose.Value;

        for (int i = 0; i < came.Count; i++)
        {
            UnitAttribute hunter = came[i];
            if (hunter == null || hunter.Data == null || hunter.Data.isdead) continue;
            if (hunter.stateMachine == null) continue;

            Hold(hunter, held: false);

            Vector3 stop = (ends.Count > 0) ? ends[i % ends.Count] : centre;

            // Меток столько же, сколько бойцов — у каждого своя. Меньше — значит это общий
            // коридор, и тогда по нему идут все подряд, от первой метки до последней.
            List<Vector3> route = (way.Count == 0)
                ? new List<Vector3>()
                : ((way.Count >= came.Count) ? new List<Vector3> { way[i] } : way);

            Road(hunter, i, route, stop);

            ((MonoBehaviour)(object)hunter).StartCoroutine(Walk(hunter, route, stop, near));

            sent++;
        }

        DemonLookPlugin.Log.LogInfo($"Охотники пошли на демона: {sent}; промежуточных точек "
            + $"{way.Count}, конечных {ends.Count}.");
    }

    /// <summary>Checks, and says in the log, whether this hunter has a road to his place.</summary>
    private static void Road(UnitAttribute hunter, int which, List<Vector3> route, Vector3 stop)
    {
        try
        {
            Vector3 from = hunter.transform.position;
            List<Vector3> legs = new List<Vector3>(route);
            legs.Add(stop);

            List<string> said = new List<string>();

            foreach (Vector3 leg in legs)
            {
                NavMeshPath path = new NavMeshPath();
                bool found = NavMesh.CalculatePath(from, leg, -1, path);

                // Полная дорога — ноль, обрывок — единица, никакой — двойка. Приказ идти туда,
                // куда дороги нет, боец принимает и не трогается с места: снаружи это выглядит
                // как «никто не бежит», а на деле путь обрывается о камень.
                string verdict = (!found) ? "нет пути"
                    : (((int)path.status == 0) ? "дорога есть"
                    : (((int)path.status == 1) ? "путь обрывается" : "пути нет"));

                said.Add($"{Vector3.Distance(from, leg):0.0} м — {verdict}");
                from = leg;
            }

            DemonLookPlugin.Log.LogInfo($"Охотник {which + 1} к своему месту: "
                + string.Join("; ", said.ToArray()));
        }
        catch (Exception e)
        {
            DemonLookPlugin.Log.LogWarning("Не смог промерить дорогу: " + e.Message);
        }
    }

    /// <summary>Walks one hunter through his waypoint and on to his place.</summary>
    private static IEnumerator Walk(UnitAttribute hunter, List<Vector3> route, Vector3 stop, float near)
    {
        // Промежуточные точки — только углы, за которые надо завернуть: ждём каждую грубо,
        // по расстоянию, и сразу отправляем дальше. Стоять на них никто не должен.
        float waited = 0f;

        foreach (Vector3 through in route)
        {
            if (hunter == null || hunter.Data == null || hunter.Data.isdead) yield break;
            if (hunter.stateMachine == null) yield break;

            // Метка под ногами — не приказ. Отдать её значит сбить бойца с шага ради того,
            // где он уже стоит, а следующую он получит только через четверть секунды.
            if (Vector3.Distance(hunter.transform.position, through) <= 3.5f) continue;

            float since = 99f;

            while (waited < MarchTime.Value)
            {
                if (hunter == null || hunter.Data == null || hunter.Data.isdead) yield break;
                if (Vector3.Distance(hunter.transform.position, through) <= 3.5f) break;

                // Приказ повторяется раз в две секунды. Боец, которого толкнули, обошли или
                // задели, кончает своё дело и встаёт — а свою метку помним только мы.
                if (since >= 2f && hunter.stateMachine != null)
                {
                    hunter.stateMachine.HandleCommand(new UnitCommand((commandsName)10, through), false);
                    since = 0f;
                }

                since += 0.25f;
                waited += 0.25f;
                yield return (object)new WaitForSeconds(0.25f);
            }
        }

        if (hunter == null || hunter.Data == null || hunter.Data.isdead) yield break;
        if (hunter.stateMachine == null) yield break;

        yield return (object)Watch(hunter, stop, near);
    }

    /// <summary>Marks a hunter as arrived once he is standing where he was put.</summary>
    private static IEnumerator Watch(UnitAttribute hunter, Vector3 stop, float near)
    {
        float waited = 0f;
        float since = 99f;

        while (waited < MarchTime.Value)
        {
            if (hunter == null || hunter.Data == null || hunter.Data.isdead) yield break;

            if (Vector3.Distance(hunter.transform.position, stop) <= near)
            {
                standing.Add(hunter);
                DemonLookPlugin.Log.LogInfo($"«{hunter.Data.unitname}» на месте.");
                yield break;
            }

            if (since >= 2f && hunter.stateMachine != null)
            {
                hunter.stateMachine.HandleCommand(new UnitCommand((commandsName)10, stop), false);
                since = 0f;
            }

            since += 0.25f;
            waited += 0.25f;
            yield return (object)new WaitForSeconds(0.25f);
        }

        DemonLookPlugin.Log.LogInfo($"«{hunter.Data.unitname}» не дошёл: до места "
            + $"{Vector3.Distance(hunter.transform.position, stop):0.0} м.");
    }

    private static Vector3 Middle(List<UnitAttribute> mages, Vector3 fallback)
    {
        //IL_000b: Unknown result type (might be due to invalid IL or missing references)
        //IL_000d: Unknown result type (might be due to invalid IL or missing references)
        //IL_0012: Unknown result type (might be due to invalid IL or missing references)
        //IL_002f: Unknown result type (might be due to invalid IL or missing references)
        //IL_0036: Unknown result type (might be due to invalid IL or missing references)
        //IL_003b: Unknown result type (might be due to invalid IL or missing references)
        //IL_0040: Unknown result type (might be due to invalid IL or missing references)
        //IL_0064: Unknown result type (might be due to invalid IL or missing references)
        //IL_0067: Unknown result type (might be due to invalid IL or missing references)
        //IL_006c: Unknown result type (might be due to invalid IL or missing references)
        //IL_0062: Unknown result type (might be due to invalid IL or missing references)
        if (mages == null || mages.Count == 0)
        {
            return fallback;
        }
        Vector3 val = Vector3.zero;
        int num = 0;
        foreach (UnitAttribute mage in mages)
        {
            if (!(mage == null))
            {
                val += mage.transform.position;
                num++;
            }
        }
        if (num <= 0)
        {
            return fallback;
        }
        return Walkable(val / (float)num);
    }

    private static Faction MageSide()
    {
        //IL_001f: Unknown result type (might be due to invalid IL or missing references)
        //IL_0024: Unknown result type (might be due to invalid IL or missing references)
        //IL_0029: Unknown result type (might be due to invalid IL or missing references)
        //IL_002c: Unknown result type (might be due to invalid IL or missing references)
        try
        {
            return (Faction)Enum.Parse(typeof(Faction), MageFaction.Value.Trim(), ignoreCase: true);
        }
        catch
        {
            return (Faction)4;
        }
    }

    private static void Hunters(Vector3 centre, HumaniodUnit demon)
    {
        //IL_003f: Unknown result type (might be due to invalid IL or missing references)
        //IL_0044: Unknown result type (might be due to invalid IL or missing references)
        //IL_005b: Unknown result type (might be due to invalid IL or missing references)
        //IL_005d: Unknown result type (might be due to invalid IL or missing references)
        //IL_0070: Unknown result type (might be due to invalid IL or missing references)
        //IL_0067: Unknown result type (might be due to invalid IL or missing references)
        //IL_0069: Unknown result type (might be due to invalid IL or missing references)
        //IL_0149: Unknown result type (might be due to invalid IL or missing references)
        //IL_015d: Unknown result type (might be due to invalid IL or missing references)
        //IL_0164: Unknown result type (might be due to invalid IL or missing references)
        //IL_0169: Unknown result type (might be due to invalid IL or missing references)
        //IL_016e: Unknown result type (might be due to invalid IL or missing references)
        //IL_038e: Unknown result type (might be due to invalid IL or missing references)
        //IL_0393: Unknown result type (might be due to invalid IL or missing references)
        //IL_03af: Unknown result type (might be due to invalid IL or missing references)
        //IL_03cd: Unknown result type (might be due to invalid IL or missing references)
        //IL_03da: Unknown result type (might be due to invalid IL or missing references)
        //IL_03e4: Unknown result type (might be due to invalid IL or missing references)
        //IL_03ea: Unknown result type (might be due to invalid IL or missing references)
        //IL_0421: Unknown result type (might be due to invalid IL or missing references)
        //IL_0432: Unknown result type (might be due to invalid IL or missing references)
        //IL_0434: Unknown result type (might be due to invalid IL or missing references)
        //IL_0264: Unknown result type (might be due to invalid IL or missing references)
        //IL_0278: Unknown result type (might be due to invalid IL or missing references)
        //IL_027f: Unknown result type (might be due to invalid IL or missing references)
        //IL_0284: Unknown result type (might be due to invalid IL or missing references)
        //IL_0186: Unknown result type (might be due to invalid IL or missing references)
        //IL_018b: Unknown result type (might be due to invalid IL or missing references)
        //IL_018d: Unknown result type (might be due to invalid IL or missing references)
        //IL_0192: Unknown result type (might be due to invalid IL or missing references)
        //IL_01c5: Unknown result type (might be due to invalid IL or missing references)
        //IL_02a6: Unknown result type (might be due to invalid IL or missing references)
        //IL_02ad: Unknown result type (might be due to invalid IL or missing references)
        //IL_02b2: Unknown result type (might be due to invalid IL or missing references)
        //IL_02b7: Unknown result type (might be due to invalid IL or missing references)
        //IL_02ec: Unknown result type (might be due to invalid IL or missing references)
        if (HeroCount.Value <= 0)
        {
            return;
        }
        if (party.Count == 0)
        {
            DemonLookPlugin.Log.LogWarning("Шаблонов для охотников нет — никого не зову.");
            return;
        }
        List<Vector3> list = new List<Vector3>();

        // Каждая метка используется один раз и только если она к этому кругу относится.
        //
        // Метки живут в настройках, а круг всякий раз новый: их легко потерять, легко
        // недоставить, и ничто не мешает им остаться от прошлой пещеры — тогда боец выходит
        // за полсотни метров от места действия и в сцену уже не попадает. Опираться на них
        // как на обязательное условие нельзя.
        //
        // Поэтому метка теперь пожелание, а не условие. Своё место она даёт, только если
        // стоит в разумной близости от круга; всё, чего не хватило, добирается кольцами
        // ниже — тем самым расчётом, которым сцена и обходилась до всякой ручной разметки.
        // Так любой запуск выходит целым: с метками красивее, без них всё равно работает.
        float reach = Mathf.Max(10f, HeroDistance.Value * 3f);

        int taken = 0, tossed = 0;

        foreach (Vector3 item in Spots())
        {
            if (list.Count >= HeroCount.Value) break;

            if (Vector3.Distance(item, centre) > reach) { tossed++; continue; }

            list.Add(item);
            taken++;
        }

        if (taken > 0 || tossed > 0)
        {
            DemonLookPlugin.Log.LogInfo($"Мест размечено вручную: {taken}"
                + ((tossed > 0) ? $", отброшено чужих {tossed} (дальше {reach:0} м)" : "")
                + ((taken < HeroCount.Value) ? $", остальные добираю расчётом" : "") + ".");
        }
        float[] array = new float[5]
        {
            HeroDistance.Value,
            HeroDistance.Value * 0.8f,
            HeroDistance.Value * 0.6f,
            HeroDistance.Value * 1.3f,
            HeroDistance.Value * 1.6f
        };
        float[] array2 = array;
        Vector3 val;
        foreach (float num in array2)
        {
            for (int j = 0; j < 24; j++)
            {
                if (list.Count >= HeroCount.Value)
                {
                    break;
                }
                float num2 = (float)j * (float)Math.PI * 2f / 24f;
                if (!Reachable(centre + new Vector3(Mathf.Cos(num2), 0f, Mathf.Sin(num2)) * num, centre, out var stand2))
                {
                    continue;
                }
                bool flag = false;
                foreach (Vector3 item2 in list)
                {
                    val = item2 - stand2;
                    if (val.sqrMagnitude < 2.25f)
                    {
                        flag = true;
                        break;
                    }
                }
                if (!flag)
                {
                    list.Add(stand2);
                }
            }
            if (list.Count >= HeroCount.Value)
            {
                break;
            }
        }
        if (list.Count < HeroCount.Value)
        {
            int count = list.Count;
            array2 = array;
            NavMeshHit val2 = default(NavMeshHit);
            foreach (float num3 in array2)
            {
                for (int k = 0; k < 24; k++)
                {
                    if (list.Count >= HeroCount.Value)
                    {
                        break;
                    }
                    float num4 = (float)k * (float)Math.PI * 2f / 24f + 0.13f;
                    if (!NavMesh.SamplePosition(centre + new Vector3(Mathf.Cos(num4), 0f, Mathf.Sin(num4)) * num3, out val2, 3f, -1))
                    {
                        continue;
                    }
                    bool flag2 = false;
                    foreach (Vector3 item3 in list)
                    {
                        val = item3 - val2.position;
                        if (val.sqrMagnitude < 2.25f)
                        {
                            flag2 = true;
                            break;
                        }
                    }
                    if (!flag2)
                    {
                        list.Add(val2.position);
                    }
                }
                if (list.Count >= HeroCount.Value)
                {
                    break;
                }
            }
            if (list.Count > count)
            {
                DemonLookPlugin.Log.LogInfo($"Добрано мест без строгой проверки: {list.Count - count}.");
            }
        }
        if (list.Count == 0)
        {
            DemonLookPlugin.Log.LogWarning("Ни одного места с дорогой к кругу — охотников нет.");
            return;
        }
        int num5 = 0;
        foreach (Vector3 item4 in list)
        {
            UnitInfo val3 = party[num5 % party.Count];
            UnitAttribute val4 = Make(val3, item4, (Faction)6);
            if (!(val4 == null))
            {
                val4.transform.LookAt(new Vector3(centre.x, val4.transform.position.y, centre.z));
                came.Add(val4);
                Hold(val4, held: true);
                DemonLookPlugin.Log.LogInfo(($"Охотник {num5 + 1} «{(val3).name}» встал в {item4}, " + $"до круга {Vector3.Distance(item4, centre):0.0} м."));
                Outfit(val4, num5, HunterRoles.Value,
                    (num5 == 0) ? LeaderTier.Value : HunterTier.Value,
                    (num5 == 0) ? LeaderQuality.Value : HunterQuality.Value,
                    (num5 == 0) ? LeaderClad.Value : HunterClad.Value, adorn: true);
                Grow(val4, HeroLevel.Value);
                num5++;
            }
        }
        DemonLookPlugin.Log.LogInfo($"За демоном пришли: бойцов {num5}.");
    }

    /// <summary>Writes the six numbers a fighter is actually made of.</summary>
    internal static void Build(CharacterSaveData data, string written)
    {
        if (data == null || string.IsNullOrEmpty(written)) return;

        try
        {
            string[] cells = written.Split(',');
            if (cells.Length < 6) return;

            NPCSaveData mind = data as NPCSaveData;
            if (mind == null || mind.humanAttribute == null) return;

            int sum = 0;

            for (int i = 0; i < 6; i++)
            {
                int much;
                if (!int.TryParse(cells[i].Trim(), out much)) continue;

                mind.humanAttribute[i] = UnityEngine.Mathf.Clamp(much, 1, 100);
                sum += mind.humanAttribute[i];
            }

            // Потенциал — это потолок, за который характеристики не пускают. Поднятые выше
            // него они держались бы ровно до первого пересчёта.
            if (mind.humanAttribute.potential < sum) mind.humanAttribute.potential = sum;
        }
        catch (Exception e)
        {
            DemonLookPlugin.Log.LogWarning("Не смог собрать охотника: " + e.Message);
        }
    }

    /// <summary>Raises a hunter to the level the scene needs, and makes his death real.</summary>
    internal static void Grow(UnitAttribute unit, int level)
    {
        if (unit == null || unit.Data == null) return;

        try
        {
            CharacterSaveData data = (CharacterSaveData)(object)unit.Data;

            // Сперва то, из чего боец сделан, и только потом уровень. Шаблон городского
            // стражника — это стражник и есть: полсотни здоровья и рука, привыкшая к пьяным.
            // Уровень сам по себе даёт лишь прибавку к здоровью и урону, а бьёт и уворачивается
            // человек своими характеристиками, и без них полторы сотни над головой означали бы
            // только толстого стражника.
            Build(data, HeroAttributes.Value);

            if (data.level < level)
            {
                data.level = level;

                // Через родной пересчёт, а не руками по цифрам: он же вызывает ApplyUnitLevelBonus,
                // а тот, в свою очередь, проходит через EncounterScale — охотник получает ту же
                // прибавку за уровень, что и любое существо в мире.
                unit.DoUpdateAttribute();

                // Предел здоровья вырос, а налитое в него осталось прежним, и охотник вышел бы
                // на бой наполовину пустым. Доливаем родным лечением — с запасом, лишнее игра
                // срежет сама.
                unit.Heal(100000f, unit);
            }

            // Убитый охотник должен умереть насовсем. Игра по умолчанию решает это броском:
            // почти всех она лишь сбивает с ног, вешает «Knockout» на восемь секунд — и они
            // встают. Отсюда и «не могу убить и забрать вещи»: с оглушённого нечего снимать,
            // а сцена ждёт мертвецов, которых нет.
            data.canKill = true;
            data.forceKill = true;
        }
        catch (Exception e)
        {
            DemonLookPlugin.Log.LogWarning("Не смог поднять охотника в уровне: " + e.Message);
        }
    }

    internal static UnitAttribute Make(UnitInfo template, Vector3 where, Faction side)
    {
        //IL_0062: Unknown result type (might be due to invalid IL or missing references)
        //IL_005d: Unknown result type (might be due to invalid IL or missing references)
        //IL_0081: Unknown result type (might be due to invalid IL or missing references)
        //IL_0070: Unknown result type (might be due to invalid IL or missing references)
        //IL_0086: Unknown result type (might be due to invalid IL or missing references)
        //IL_0089: Unknown result type (might be due to invalid IL or missing references)
        //IL_008a: Unknown result type (might be due to invalid IL or missing references)
        //IL_008b: Unknown result type (might be due to invalid IL or missing references)
        //IL_00b2: Unknown result type (might be due to invalid IL or missing references)
        //IL_00b3: Unknown result type (might be due to invalid IL or missing references)
        //IL_00bf: Unknown result type (might be due to invalid IL or missing references)
        //IL_00f3: Unknown result type (might be due to invalid IL or missing references)
        //IL_00f4: Unknown result type (might be due to invalid IL or missing references)
        try
        {
            AreaManager instance = AreaManager.Instance;
            if (instance == null)
            {
                return null;
            }
            Vector3 val = default(Vector3);
            val = new Vector3(0f, UnityEngine.Random.Range(0f, 360f), 0f);
            Faction team = (Faction)((gameManager.currentplayUnit != null && gameManager.currentplayUnit.Data != null) ? ((int)((CharacterSaveData)gameManager.currentplayUnit.Data).team) : 0);
            Vector3 position = ((gameManager.currentplayUnit != null) ? ((Component)gameManager.currentplayUnit).transform.position : Vector3.zero);
            UnitAttribute val2 = instance.CreateCharacter(template, where, val, side);
            if (val2 != null && val2 == gameManager.currentplayUnit)
            {
                val2.Data.team = team;
                val2.transform.position = position;
                DemonLookPlugin.Log.LogError("Создание вернуло самого игрока — отменяю.");
                return null;
            }
            if (val2 != null && val2.Data != null)
            {
                val2.Data.team = side;
            }
            return val2;
        }
        catch (Exception ex)
        {
            DemonLookPlugin.Log.LogError(("Не смог создать участника ритуала: " + ex));
            return null;
        }
    }

    private static void Strengthen(UnitAttribute unit, float edge)
    {
        if (!(edge <= 0f) && !(unit == null))
        {
            unit.maxhp *= 1f + edge;
            if (unit.Data != null)
            {
                unit.Data.currenthp = unit.maxhp;
            }
        }
    }

    /// <summary>Lets a summoner leave a body instead of dissolving into a sack.</summary>
    private static void Stay(UnitAttribute mage)
    {
        if (mage == null || mage.info == null || !mage.info.leftTrophyBag) return;

        // Флаг в шаблоне: с ним игра играет «растворение духа», удаляет тело через две
        // секунды и роняет мешок на землю. Смерть тут ни при чём — это решение авторов о том,
        // как выглядит их уход. Снимаем, и маг умирает как все: тело остаётся и обыскивается.
        mage.info.leftTrophyBag = false;

        DemonLookPlugin.Log.LogInfo("Магам возвращено тело: мешок вместо трупа отменён.");
    }

    /// <summary>Makes one summoner the person the card describes, and keeps him that way.</summary>
    private static void Person(UnitAttribute mage, int which)
    {
        if (mage == null || mage.Data == null) return;

        string[] cards = (Persons.Value ?? "").Split(';');
        if (which < 1 || which > cards.Length) return;

        string[] parts = cards[which - 1].Split('|');
        if (parts.Length < 4) return;

        try
        {
            int level = Read(parts[1], 30);
            string[] six = parts[2].Split(',');
            float health = Read(parts[3], 0);
            int rank = (parts.Length > 4) ? Read(parts[4], MageSpellLevel.Value) : MageSpellLevel.Value;

            // Прочерк в карточке значит «не трогать»: эта характеристика остаётся той,
            // какой её сделал мир. Сила у мага как раз такая — она растёт от уровня сама
            // (Burden: четыре плюс три четверти на уровень), и если карточка спорит с этим
            // правилом, спор идёт по кругу до конца окна и заканчивается ничем.
            int[] stats = new int[6];
            for (int i = 0; i < 6; i++) stats[i] = (i < six.Length) ? Read(six[i], -1) : -1;

            ((MonoBehaviour)(object)mage).StartCoroutine(Hold(mage, level, stats, health, rank));
        }
        catch (Exception e)
        {
            DemonLookPlugin.Log.LogWarning($"Не разобрал карточку мага {which}: " + e.Message);
        }
    }

    private static int Read(string what, int fallback)
    {
        int got;
        return int.TryParse((what ?? "").Trim(), out got) ? got : fallback;
    }

    /// <summary>
    /// Holds a summoner to his card until the game stops arguing.
    ///
    /// Характеристики игра расставляет сама и уже после того, как существо создано: берёт
    /// 34-45 очков, делит на шестерых и зажимает каждую между пятью и десятью. Поэтому пятеро
    /// из одного шаблона выходят пятью почти одинаковыми людьми, и всё, что им ни назначь в
    /// первый кадр, она перепишет своим броском.
    ///
    /// Писать надо в «humanAttribute» — хранимый источник, а не в «Data.strength», которое
    /// пересчитывается из него при каждом обновлении. И писать не один раз, а пока не сойдётся.
    /// </summary>
    private static IEnumerator Hold(UnitAttribute mage, int level, int[] stats, float health, int rank)
    {
        bool given = false;
        int wrote = 0;

        for (int tries = 0; tries < 60; tries++)
        {
            yield return (object)new WaitForSeconds(0.25f);

            if (mage == null || mage.Data == null) yield break;

            NPCSaveData mind = mage.Data as NPCSaveData;
            if (mind == null || mind.humanAttribute == null) continue;

            bool argued = false;

            for (int i = 0; i < 6; i++)
            {
                if (stats[i] < 0) continue;                 // прочерк: не наша забота
                if (mind.humanAttribute[i] == stats[i]) continue;

                mind.humanAttribute[i] = stats[i];
                argued = true;
            }

            CharacterSaveData data = (CharacterSaveData)(object)mage.Data;

            // Уровень выводится из характеристик, а не держится числом. В этой игре он и так
            // считается из них: «SetLevel» складывает вложенное во все шесть и делит. А от
            // уровня зависит не одна табличка: «ApplyUnitLevelBonus» даёт существу и запас
            // здоровья, и урон пропорционально тому, насколько оно переросло свой шаблон.
            // Пока карточка возвращала тридцатый, маг с сотней разума дрался как тридцатый —
            // и умирал соответственно. Число из карточки остаётся нижней границей: поднять
            // им можно, опустить — нет.
            int was = data.level;

            mind.SetLevel();
            if (data.level < level) data.level = level;

            if (data.level != was) argued = true;

            if (argued)
            {
                wrote++;
                mage.DoUpdateAttribute();
            }

            // Оружие и здоровье выдаются один раз и сразу, как только карточка легла в первый
            // раз. Раньше их ждал «тихий» кадр — такой, в котором игра не переписала ни одного
            // числа, — и если спор затягивался, маг так и оставался безоружным и с исходным
            // здоровьем, то есть умирал в первую же минуту. Спор при этом никому не мешает:
            // карточка накладывается заново каждую четверть секунды до конца окна.
            if (!given && tries >= 1)
            {
                given = true;

                if (health > 0f)
                {
                    mage.maxhp = health;
                    data.currenthp = health;
                    ((MonoBehaviour)(object)mage).StartCoroutine(Keep(mage, health));
                }

                Arm(mage, rank);

                DemonLookPlugin.Log.LogInfo($"«{mage.Data.unitname}» ур. {data.level}: "
                    + $"сила {mind.humanAttribute[0]}, выносл {mind.humanAttribute[1]}, "
                    + $"ловк {mind.humanAttribute[2]}, точн {mind.humanAttribute[3]}, "
                    + $"разум {mind.humanAttribute[4]}, воля {mind.humanAttribute[5]} | "
                    + $"здоровье {mage.maxhp:0} | урон заклинаний ×{mage.MagicDamageMD:0.00}");
            }
        }

        if (wrote > 2)
        {
            DemonLookPlugin.Log.LogInfo($"«{mage.Data.unitname}»: карточку пришлось "
                + $"накладывать {wrote} раз — игра спорила до конца.");
        }
    }

    /// <summary>Numbers a summoner so the circle can be told apart.</summary>
    private static void Name(UnitAttribute mage, int which)
    {
        if (mage == null || mage.Data == null) return;

        try
        {
            // Имена берём по порядку; кончились — дальше по счёту, чтобы сцена не сломалась
            // оттого, что магов в настройке больше, чем имён.
            // Точкой с запятой, а не запятой: в имени «Тот, кто считает дни» запятая своя.
            string[] names = (MageName.Value ?? "").Split(';');
            string called = (which >= 1 && which <= names.Length && names[which - 1].Trim().Length > 0)
                ? names[which - 1].Trim()
                : "Тёмный маг " + which;
            mage.Data.unitname = called;

            // Полоска над головой берёт имя при привязке к бойцу, поэтому просим её
            // привязаться заново — иначе новое имя появится только после перезагрузки сцены.
            if (mage.lifebar != null) mage.lifebar.SetUnit(mage);
        }
        catch (Exception e)
        {
            DemonLookPlugin.Log.LogWarning("Не смог назвать мага: " + e.Message);
        }
    }

    /// <summary>Gives one summoner the health to outlast the whole fight.</summary>
    private static void Mighty(UnitAttribute mage, float health)
    {
        if (mage == null || health <= 0f) return;

        mage.maxhp = health;
        if (mage.Data != null) mage.Data.currenthp = health;

        ((MonoBehaviour)(object)mage).StartCoroutine(Keep(mage, health));

        DemonLookPlugin.Log.LogInfo($"«{mage.Data.unitname}» держит круг: здоровья {health:0}.");
    }

    /// <summary>Puts the health ceiling back whenever the game recalculates it away.</summary>
    private static IEnumerator Keep(UnitAttribute mage, float health)
    {
        // Предел здоровья игра пересчитывает из шаблона всякий раз, когда что-то меняется в
        // снаряжении или в бафах, и наше число молча стирается. Возвращаем его, не трогая
        // налитое: удары считаются честно, просто запас у него другой.
        while (mage != null && mage.Data != null && !mage.Data.isdead)
        {
            if (mage.maxhp < health)
            {
                // Только потолок, и ничего кроме. Прежде здесь вместе с потолком подтягивалось
                // и налитое — ради того, чтобы маг не стоял с полоской в ниточку после пересчёта.
                // Стоило это вот чего: игра сбрасывала потолок по десятку раз за бой, и каждый
                // раз маг получал здоровье обратно до той же доли. В логе это выглядело так: четыреста
                // двадцать семь урона при четырёхстах здоровья — и маг жив, потому что его лечили
                // быстрее, чем по нему били. Запас у него большой, но тратится честно и один раз.
                mage.maxhp = health;
            }

            yield return (object)new WaitForSeconds(0.5f);
        }
    }

    private static void Cast(UnitAttribute mage, HumaniodUnit at, UISpellInfo spell)
    {
        //IL_0049: Unknown result type (might be due to invalid IL or missing references)
        //IL_0054: Expected O, but got Unknown
        try
        {
            if (mage.spellmanger != null && !mage.spellmanger.ContainSpell(spell))
            {
                mage.spellmanger.AddSpell(spell, 3);
            }
            if (!(mage.stateMachine == null))
            {
                mage.stateMachine.HandleCommand(new UnitCommand((commandsName)14, mage.gameObject, spell), false);
            }
        }
        catch (Exception ex)
        {
            DemonLookPlugin.Log.LogWarning(("Маг не прочёл заклинание: " + ex.Message));
        }
    }

    private static UISpellInfo FindSpell()
    {
        UISpellDatabase instance = UISpellDatabase.Instance;
        if (instance == null || instance.spells == null)
        {
            return null;
        }
        string text = (RitualSpell.Value ?? "").Trim();
        if (text.Length == 0)
        {
            return null;
        }
        UISpellInfo val = null;
        UISpellInfo[] spells = instance.spells;
        foreach (UISpellInfo val2 in spells)
        {
            if (!(val2 == null))
            {
                if (string.Equals((val2).name, text, StringComparison.OrdinalIgnoreCase))
                {
                    return val2;
                }
                if (val == null && (val2).name != null && (val2).name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    val = val2;
                }
            }
        }
        if (val == null)
        {
            DemonLookPlugin.Log.LogWarning(("Заклинания «" + text + "» не нашлось. Тёмная школа целиком: " + DarkSpells(instance)));
        }
        return val;
    }

    private static string DarkSpells(UISpellDatabase db)
    {
        //IL_001f: Unknown result type (might be due to invalid IL or missing references)
        //IL_0026: Invalid comparison between Unknown and I4
        List<string> list = new List<string>();
        UISpellInfo[] spells = db.spells;
        foreach (UISpellInfo val in spells)
        {
            if (val != null && (int)val.SkillSet == 105 && list.Count < 40)
            {
                list.Add((val).name);
            }
        }
        return string.Join(", ", list.ToArray());
    }

    /// <summary>Writes every spell the game knows, with its school, next to the config.</summary>
    internal static void Spells()
    {
        if (spelled) return;
        spelled = true;

        try
        {
            UISpellDatabase db = UISpellDatabase.Instance;
            if (db == null || db.spells == null) return;

            List<string> rows = new List<string>();

            foreach (UISpellInfo one in db.spells)
            {
                if (one == null) continue;

                string asset = ((UnityEngine.Object)one).name;
                string called = one.Name;

                rows.Add($"{(int)one.SkillSet}	{one.SkillSet}	{asset}	{called}");
            }

            rows.Sort(StringComparer.OrdinalIgnoreCase);

            string file = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "aor.spells.txt");
            System.IO.File.WriteAllLines(file, rows.ToArray());

            DemonLookPlugin.Log.LogInfo($"Список заклинаний записан: {rows.Count}, «{file}».");
        }
        catch (Exception e)
        {
            DemonLookPlugin.Log.LogWarning("Не смог выписать заклинания: " + e.Message);
        }
    }

    /// <summary>Writes every creature name the catalogue holds next to the config file.</summary>
    internal static void Inventory()
    {
        if (listed) return;
        listed = true;

        try
        {
            List<string> list = new List<string>();

            foreach (UnityEngine.AddressableAssets.ResourceLocators.IResourceLocator where in Addressables.ResourceLocators)
            {
                if (where == null || where.Keys == null) continue;

                foreach (object key in where.Keys)
                {
                    string text = key as string;
                    if (text == null) continue;
                    if (!text.StartsWith("unitdata/", StringComparison.OrdinalIgnoreCase)) continue;

                    string name = text.Substring("unitdata/".Length);
                    if (!list.Contains(name)) list.Add(name);
                }
            }

            list.Sort(StringComparer.OrdinalIgnoreCase);

            string file = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "aor.units.txt");
            System.IO.File.WriteAllLines(file, list.ToArray());

            DemonLookPlugin.Log.LogInfo($"Список существ записан: {list.Count} имён, «{file}».");
        }
        catch (Exception e)
        {
            DemonLookPlugin.Log.LogWarning("Не смог выписать список существ: " + e.Message);
        }
    }

    private static void Suggest()
    {
        try
        {
            List<string> list = new List<string>();
            foreach (UnityEngine.AddressableAssets.ResourceLocators.IResourceLocator resourceLocator in Addressables.ResourceLocators)
            {
                if (resourceLocator == null || resourceLocator.Keys == null)
                {
                    continue;
                }
                foreach (object key in resourceLocator.Keys)
                {
                    if (key is string text && list.Count < 30 && text.StartsWith("unitdata/", StringComparison.OrdinalIgnoreCase) && text.IndexOf("mage", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        list.Add(text);
                    }
                }
            }
            DemonLookPlugin.Log.LogInfo(("Похожие существа в каталоге: " + ((list.Count > 0) ? string.Join(", ", list.ToArray()) : "ни одного")));
        }
        catch (Exception ex)
        {
            DemonLookPlugin.Log.LogWarning(("Не смог перебрать каталог: " + ex.Message));
        }
    }
}
}
