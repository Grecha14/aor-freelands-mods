using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Whether the blow got through, reckoned on the game's own numbers.
    ///
    /// У каждого оружия в игре записана своя сила — `UIWeaponInfo.Force`, от тридцати у
    /// кинжала до двухсот двадцати у двуручной дубины. Рядом лежит раздел между Силой и
    /// Ловкостью: `strFactor` и `agiFactor`, у кинжала 0,1/0,9, у молота 0,9/0,1, у меча
    /// поровну. Это готовая лестница пробития и готовые билды, и мы их не придумывали —
    /// они лежали в предметах всё это время.
    ///
    /// Отсюда пробитие: сила оружия, помноженная на то, чем боец её прикладывает. При всех
    /// статах по десять оно равно силе оружия один в один.
    ///
    /// Против него стоит вычет брони — одно число на класс, растущее по тиру и редкости.
    /// Вычет один на весь доспех: кираса и шлем из одного железа, разница только в площади,
    /// а площадь на пробой не влияет.
    ///
    /// Разность, делённая на пробитие, и есть доля, которая дойдёт до тела. Не пробил —
    /// ноль, и никакого потолка: игра свои проценты упирает в восемьдесят, латы же должны
    /// уметь не пустить вовсе.
    /// </summary>
    internal static class Breach
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Guards;
        internal static ConfigEntry<string> Hard;
        internal static ConfigEntry<float> Soft;
        internal static ConfigEntry<float> Steep;
        internal static ConfigEntry<string> Rungs;
        internal static ConfigEntry<string> Thick;
        internal static ConfigEntry<string> Hold;
        internal static ConfigEntry<string> Holds;
        internal static ConfigEntry<string> Focus;
        internal static ConfigEntry<float> PerMm;
        internal static ConfigEntry<float> Units;
        internal static ConfigEntry<string> Gate;
        internal static ConfigEntry<float> Chest;
        internal static ConfigEntry<float> Sleeve;
        internal static ConfigEntry<float> Legs;
        internal static ConfigEntry<float> Scale;
        internal static ConfigEntry<float> Ignore;
        internal static ConfigEntry<float> Spared;
        internal static ConfigEntry<bool> Telling;
        internal static ConfigEntry<string> Gap;
        internal static ConfigEntry<string> Drive;
        internal static ConfigEntry<float> Beast;
        internal static ConfigEntry<string> Keen;
        internal static ConfigEntry<float> Skill;
        internal static ConfigEntry<string> Through;
        internal static ConfigEntry<bool> Sole;
        internal static ConfigEntry<float> Penned;
        internal static ConfigEntry<float> PerPoint;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Breach", "Enabled", true,
                "Reckon armour as a gate rather than a percentage. The game's own way caps at "
                + "eighty percent, so a harnessed man is always hurt a little by everything; "
                + "here plate can refuse a sword outright, which is what plate was for.");

            Guards = config.Bind("Breach", "Guards",
                "None=2,Cloth=10,PaddingArmor=16,LightLeatherArmor=25,HardLeaterArmor=38,SplintArmor=42,ChainArmor=50,ScaleMail=62,LamellarArmor=62,HalfPlate=78,PlateArmor=100,hat=10,headband=10,tiara=10,leatherHelemt=38,metalHelmet=78",
                "What each class of armour takes to defeat, in the same units as a weapon's "
                + "Force. A dagger carries thirty, a sword eighty, a two-handed hammer a hundred "
                + "and eighty: so cloth falls to anything, mail turns a dagger, and plate stands "
                + "against everything but a hammer.");

            PerPoint = config.Bind("Breach", "PerPoint", 0.01f,
                new ConfigDescription(
                    "What one point of the arm behind the blow adds to its piercing, as a "
                    + "share. Strength leads a blow in hand, agility a shot: drawing a stiff bow "
                    + "and holding it steady is the arm's work, not the shoulder's.\n\n"
                    + "Без этого выходила гонка, которую оружие всегда проигрывало. Мясо с "
                    + "костью держат по семь десятых за очко, а урон растёт по сотой доле от "
                    + "себя — впятеро медленнее. К середине игры меч переставал брать даже "
                    + "голого человека, а латы не брало ничего, кроме дробящего и крита.",
                    new AcceptableValueRange<float>(0f, 0.1f)));

            Hard = config.Bind("Breach", "Hard",
                "ChainArmor,ScaleMail,LamellarArmor,SplintArmor,HalfPlate,PlateArmor,metalHelmet",
                "Which classes climb steeply with tier. Piercing grows elevenfold across the "
                + "game — the stat four and a half times over, the weight two and a half — so "
                + "armour that gains only a fifth a step is left five times behind by the middle "
                + "of it. Heavy armour must keep pace; cloth and leather need not, being worn by "
                + "those whose defence is not standing still.");

            Soft = config.Bind("Breach", "Soft", 1.20f,
                new ConfigDescription(
                    "How much light armour gains per step of tier.",
                    new AcceptableValueRange<float>(1f, 3f)));

            Steep = config.Bind("Breach", "Steep", 1.80f,
                new ConfigDescription(
                    "How much heavy armour gains per step of tier. Nine tenths a step is steep "
                    + "on purpose: plate of the fifth tier is meant to be the best thing there "
                    + "is, and a sword is not meant to cut it. It was lowered once on the "
                    + "reckoning that nothing could get through, and that reckoning was wrong "
                    + "— it left out the weapon's own written penetration, which counts in "
                    + "full by «Penned», the third to half of every critical blow that goes "
                    + "past armour entirely by «Gap», and the share of a crushing blow that "
                    + "passes whatever is worn by «Through». Plate has answers; it simply has "
                    + "no answer to a sword.",
                    new AcceptableValueRange<float>(1f, 3f)));

            Rungs = config.Bind("Breach", "Rungs", "1,1.44,1.88,2.77,4.09,6.30",
                "One ladder for every class of armour, from tier nothing to tier five, laid "
                + "out the way weight is laid out — by Fibonacci, so the early steps are "
                + "barely felt and the last one is a third of the whole climb.\n\n"
                + "Before this each class grew by its own factor — nine tenths a step for the "
                + "hard, a fifth for the soft — and the two ladders had different shapes and "
                + "different ends: weight climbed half again over the whole game while the "
                + "reckoning of plate climbed tenfold. The wall a kilogram of harness gives "
                + "therefore quadrupled for plate and grew by half for cloth, though the "
                + "reckoning is defined as exactly that: the wall per kilogram, the quality of "
                + "the forging. Now one ladder serves all, and what tells classes apart is "
                + "their own make and their own weight, as it should be. At six and three "
                + "tenths the forging of the fifth tier is four times the forging of the "
                + "second, which is what it was before.\n\n"
                + "A class enters the ladder at the tier it first exists at: plate below the "
                + "second does not exist, and its reckoning counts from there.");

            Thick = config.Bind("Breach", "Thick",
                "Cloth=0.05,PaddingArmor=0.12,LightLeatherArmor=0.25,HardLeaterArmor=0.45,"
                + "SplintArmor=0.9,ChainArmor=1.0,ScaleMail=1.3,LamellarArmor=1.5,"
                + "HalfPlate=1.6,PlateArmor=2.6,metalHelmet=2.0,leatherHelemt=0.45",
                "How thick a full suit of each material is, in millimetres of steel or its "
                + "equal, at the lowest tier the class exists at. These are the figures of the "
                + "thing itself: a gambeson of twenty to thirty layers of linen stops about as "
                + "much as two tenths of a millimetre of plate, a riveted hauberk of 1.2 to "
                + "1.6 millimetre wire about one, a fifteenth century breastplate two and a "
                + "half. Everything else is reckoned against these.");

            Holds = config.Bind("Breach", "Holds",
                "Cloth=4|2|1,PaddingArmor=8|5|3,LightLeatherArmor=13|8|8,"
                + "HardLeaterArmor=22|14|14,SplintArmor=30|16|22,ChainArmor=30|8|16,"
                + "ScaleMail=38|20|30,LamellarArmor=44|24|34,HalfPlate=52|34|40,"
                + "PlateArmor=90|45|62,metalHelmet=52|34|40,leatherHelemt=22|14|14",
                "How much of a blow each material holds, in the same units the blow is "
                + "measured in — its damage. Three numbers: cutting, crushing, thrusting, at "
                + "the tier the class first exists at; they climb by «Hone.Ladder», the same "
                + "one damage climbs, so tier against tier reads the same at both ends of the "
                + "game.\n\n"
                + "Here the character of a material lives. Mail holds thirty against a cut and "
                + "eight against a mace — which is why men with maces were sent against mail. "
                + "Plate holds ninety against a sword and cannot be cut by one at all. A "
                + "gambeson turns a cut and comes apart at a point.");

            Hold = config.Bind("Breach", "Hold",
                "Cloth=1.5|0.8|0.5,PaddingArmor=1.5|0.8|0.5,LightLeatherArmor=1.18|0.7|0.7,"
                + "HardLeaterArmor=1.18|0.7|0.7,SplintArmor=2.2|0.9|1.5,ChainArmor=2.0|0.5|1.0,"
                + "ScaleMail=2.2|1.1|1.7,LamellarArmor=2.4|1.2|1.8,HalfPlate=4.0|2.6|3.0,"
                + "PlateArmor=5.0|3.5|4.0,metalHelmet=4.0|2.6|3.0,leatherHelemt=1.18|0.7|0.7",
                "How thick the same armour seems to each kind of blow: cutting, crushing, "
                + "thrusting, in that order. Here the character of a material lives. Mail is "
                + "the best thing in the world against a cut and almost nothing against a mace "
                + "— which is why men with maces were sent against mail. Plate is not cut by a "
                + "sword at all, hence half-swording and the murder-stroke. A gambeson turns a "
                + "cut well and comes apart at a point.");

            Focus = config.Bind("Breach", "Focus",
                "Crossbow=1.5,HeavyCrossbow=1.6,Longbow=1.1,Shortbow=1.1,Dagger=1.4,Katar=1.4,"
                + "TwoHandHammer=1.3,TwoHandMace=1.3,TwoHandClub=1.3,TwoHandFlail=1.3,"
                + "Poleaxe=1.3,GreatSword=1.3,Katana=1.3,GreatAxe=1.3,DanAxe=1.3,"
                + "BastardSword=1.3,LongSword=1.3,Falchion=1.3,Rapier=1.2,Estoc=1.0",
                "Into how small a place a weapon gathers its blow. A bolt and a beak-hammer "
                + "put everything through a point; a rondel dagger is made for nothing else, "
                + "and that is why it finished men whom no sword could open. A blade spreads "
                + "the same force along an edge. Whatever is not written here gathers "
                + "nothing — one.");

            Units = config.Bind("Breach", "Units", 1.9f,
                new ConfigDescription(
                    "What one millimetre of the cutting reckoning is worth in the old units, "
                    + "for everything built on top of it: the durability a smith writes into a "
                    + "piece, and the wear that eats it. Chosen so a fourth tier cuirass keeps "
                    + "the durability it had.",
                    new AcceptableValueRange<float>(1f, 1000f)));

            PerMm = config.Bind("Breach", "PerMm", 50f,
                new ConfigDescription(
                    "How much of «attribute times kilograms» goes into one millimetre of steel "
                    + "pierced. This is the one knob that sets the whole scale: raise it and "
                    + "everything holds better, lower it and everything opens.",
                    new AcceptableValueRange<float>(1f, 1000f)));

            Gate = config.Bind("Breach", "Gate", "0.75,1.25",
                "Where a blow starts to tell and where it passes whole, as shares of what has "
                + "to be pierced. Below three quarters nothing goes in but what goes past the "
                + "iron — a critical hit through a gap, the share of a crushing blow. At a "
                + "quarter over, everything does.");

            Sleeve = config.Bind("Breach", "Sleeve", 0.6f,
                new ConfigDescription(
                    "What the armour of the chest is worth on the arm it also covers. Arms have "
                    + "no slot of their own in this game, so what stands on them is the sleeve "
                    + "of whatever is on the body — and a sleeve of mail or the wing of a "
                    + "pauldron is not a cuirass. Six tenths: it is not nothing either.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Chest = config.Bind("Breach", "Chest", 1.20f,
                new ConfigDescription(
                    "What a cuirass is worth against the class reckoning. The breastplate is the "
                    + "thickest piece there is.",
                    new AcceptableValueRange<float>(0.1f, 3f)));

            Legs = config.Bind("Breach", "Legs", 0.80f,
                new ConfigDescription(
                    "The same for the legs and the belt. Thinner than the rest, for a man must "
                    + "walk in them.",
                    new AcceptableValueRange<float>(0.1f, 3f)));

            Scale = config.Bind("Breach", "Scale", 10f,
                new ConfigDescription(
                    "What the attacker's stats are measured against. Ten: a fighter with ten in "
                    + "everything drives a weapon at exactly its own Force, and the whole ladder "
                    + "reads off the item as written.",
                    new AcceptableValueRange<float>(1f, 100f)));

            Ignore = config.Bind("Breach", "Ignore", 0.30f,
                new ConfigDescription(
                    "The share of a crushing blow that always arrives, whatever is worn and "
                    + "however thick. Three tenths: a hammer does not cut the plate, it carries "
                    + "through it, and the man inside feels it. This is why a hammer answers "
                    + "harness where a sword does not — and why crushing weapons carry less "
                    + "damage to begin with, for what they carry is never refused.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Penned = config.Bind("Breach", "Penned", 1f,
                new ConfigDescription(
                    "How much a weapon's own written penetration adds to ours. The game gave "
                    + "weapons a «resist penetration» that cut through resistances, and we took "
                    + "resistances out of the physical reckoning entirely — which left the "
                    + "number standing in the tooltip promising something that no longer "
                    + "happened. Here it means what it looks like instead: ten per cent written "
                    + "on a mace is ten per cent more of our own punch-through. At one it "
                    + "counts in full; at zero it is ignored and the tooltip is the only thing "
                    + "left of it.",
                    new AcceptableValueRange<float>(0f, 5f)));

            Sole = config.Bind("Breach", "Sole", true,
                "Take the game's own armour penetration out of the world entirely, leaving only "
                + "ours. The game gives every weapon and some creatures a percentage that eats "
                + "into the target's resistance — a second piercing system beside the one we "
                + "built, working on different numbers and in a different direction. "
                + "Against flesh and steel it changed nothing already: our reckoning sets the "
                + "game's resistance aside for anything physical. What it still touched was "
                + "flame, frost and poison, where a sword's «piercing sharp 10%» quietly ate at "
                + "a harness's fire resistance for no reason anyone could name.");

            Telling = config.Bind("Breach", "Telling", false,
                "Write a line to the log for every blow that meets armour: who struck, where it "
                + "landed, what was worn there, the deduction, the piercing and what share got "
                + "through. Off by default, because a fight writes a hundred lines. Turn it on "
                + "when a number looks wrong and you want to see the reckoning rather than "
                + "reason about it.");

            Gap = config.Bind("Breach", "Gap", "0.30,0.50",
                "What share of a critical blow goes past the armour entirely, drawn afresh for "
                + "every such blow: least and most. A critical hit is not a harder swing, it is "
                + "a found seam — the armpit, the visor slit, the gap over the knee — and iron "
                + "has nothing to say about what goes through a gap. Set both to zero and a "
                + "critical hit meets the wall like any other.");

            Spared = config.Bind("Breach", "Spared", 0f,
                new ConfigDescription(
                    "What still reaches a man whose armour turned the blow entirely. Zero: armour "
                    + "that holds, holds. "
                    + "This was a twentieth for a while, on the reasoning that plate stopping a "
                    + "sword does not stop the man behind it feeling it. True of one blow and "
                    + "false of a pack: four wolves that the harness turns completely still "
                    + "gnawed a knight down, a twentieth at a time. What comes through whole "
                    + "armour now is the critical hit, which goes past the iron rather than "
                    + "through it.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Keen = config.Bind("Breach", "Keen", "1.00,1.31,1.92,2.84,4.38",
                "What a weapon's piercing is worth by tier, T1 through T5. This is the common "
                + "ladder «Rungs» read from the first tier instead of from nothing, so that a "
                + "blade and a harness climb in step: tier two against tier two reads the same "
                + "as tier five against tier five, which was not so while the two ladders had "
                + "different heights — a harness climbed tenfold across the game and a blade "
                + "two and a half times. "
                + "Legendary counts as tier five whatever its tier field says: eighty-two of "
                + "them are named T5 and seventy-seven of those are marked T4, and the name is "
                + "the honest one.");

            Skill = config.Bind("Breach", "Skill", 0.01f,
                new ConfigDescription(
                    "What each rank of weapon mastery adds to piercing. A hundredth: at rank "
                    + "fifty a blow drives half again as hard, at ninety-nine twice. Mastery "
                    + "thus opens armour that raw strength never would — a swordsman of the "
                    + "twenty-seventh rank defeats mail his own arm could not.",
                    new AcceptableValueRange<float>(0f, 0.1f)));

            Through = config.Bind("Breach", "Through",
                "Cloth=0.25,PaddingArmor=0.25,LightLeatherArmor=0.25,HardLeaterArmor=0.25,"
                + "SplintArmor=0.25,ChainArmor=0.25,ScaleMail=0.25,LamellarArmor=0.25,"
                + "HalfPlate=0.25,PlateArmor=0.25,metalHelmet=0.25,"
                + "leatherHelemt=0.25",
                "The share of a crushing blow that arrives whatever is worn, by class of "
                + "armour: a quarter through everything, whatever it is made of.\n\n"
                + "В этом вся палица и есть: доспех ей не помеха. Она не разрезает и не "
                + "колет — она проносит вес, и четверть его доходит сквозь ткань, "
                + "кольчугу и латы одинаково. Сильной её это не делает: урон у неё "
                + "срезан до семи десятых, и берёт она не ударом, а тем, что доходит "
                + "всегда, ломает кости и бьёт площадью. This is the "
                + "whole of what a hammer is for, and the reason it carries less damage: what "
                + "it carries is never wholly refused.");

            Beast = config.Bind("Breach", "Beast", 10f,
                new ConfigDescription(
                    "What a creature without the six attributes drives its blows with. Beasts "
                    + "have no Strength written down — a troll reads as one — so they need a "
                    + "figure of their own or claws would bounce off a padded jacket.",
                    new AcceptableValueRange<float>(1f, 100f)));

            Drive = config.Bind("Breach", "Drive", "weight",
                new ConfigDescription(
                    "What carries a blow against armour. \"force\": the weapon's own Force as "
                    + "the game records it. \"weight\": its weight, which is what this mod used "
                    + "before the Force field was found — but weight alone cannot tell a mace "
                    + "from a spear, both being four kilos. \"both\": weight times Force, which "
                    + "reads as it should — heavy and powerful gets through, light and feeble "
                    + "does not — and spreads the ladder six times wider than Force alone.",
                    new AcceptableValueList<string>("force", "weight", "both")));
        }

        /// <summary>How hard this blow is driven against armour.</summary>
        internal static float Punch(Attack attack)
        {
            if (attack == null) return 0f;

            try
            {
                // Пробитие и есть сам удар: сколько он несёт, столько и продавливает. Своего
                // числа у него больше нет — ни у вещи, ни выведенного из руки и веса. Остаётся
                // только то, во что удар собран: болт и клевец кладут всё в точку, клинок
                // ведёт по полосе.
                //
                // И рука, которая его прикладывает. Без неё выходила гонка, которую оружие
                // всегда проигрывало: мясо с костью растут по семь десятых за очко, а урон —
                // по сотой доле от себя, то есть впятеро медленнее. К середине игры меч
                // переставал брать даже голого, и это не преувеличение, а посчитанное.
                //
                // Сила ведёт ближний бой, ловкость — дальний: натянуть тугой лук и удержать
                // его ровно решает не плечо, а рука.
                return Coming(attack) * FocusOf(Blade(attack)) * Hand(attack);
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>Во сколько раз рука бойца усиливает пробитие.</summary>
        private static float Hand(Attack attack)
        {
            try
            {
                if (PerPoint == null || PerPoint.Value <= 0f) return 1f;

                HumaniodUnit man = attack.attacker as HumaniodUnit;
                if (man == null) return 1f;

                bool far = attack.attackType == AttackType.ranged;

                int stat = far ? man.Agility : man.Strength;
                if (stat <= 0) return 1f;

                return 1f + PerPoint.Value * stat;
            }
            catch
            {
                return 1f;
            }
        }

        /// <summary>
        /// Пробитие как таковое: боец, то, что у него в руке, и чем он это прикладывает.
        ///
        /// Отделено от удара затем, чтобы то же самое число можно было показать в меню
        /// персонажа, где никакого удара ещё нет.
        /// </summary>
        internal static float Push(UnitAttribute who, Weapon arm, UIWeaponInfo blade, int lead)
        {
            try
            {
                if (who == null) return 0f;

                // Шесть статов есть только у людей. У зверя их нет вовсе, и без своей
                // цифры его когти отскакивали бы от стёганки.
                HumaniodUnit man = who as HumaniodUnit;

                float str, agi;

                if (man != null)
                {
                    str = man.Strength;
                    agi = man.Agility;
                }
                else
                {
                    // У зверя шесть статов свои, выведенные из роста и уровня. Пока их не
                    // было, коготь считался по запасной цифре, и паук выходил вровень с
                    // медведем.
                    float[] six = Beastly.Enabled != null && Beastly.Enabled.Value
                        ? Beastly.Mine(who)
                        : null;

                    if (six != null)
                    {
                        str = six[0];
                        agi = six[2];
                    }
                    else
                    {
                        float floor = Bestiary.Floor(who);
                        str = floor > 0f ? floor : Beast.Value;
                        agi = str;
                    }
                }

                float kilos = 3f;

                if (blade != null && blade.weight > 0f)
                {
                    kilos = blade.weight;
                }
                else if (who is HumaniodUnit)
                {
                    if (arm != null && arm.weaponForce > 0f) kilos = arm.weaponForce / 30f;
                }
                else
                {
                    // Зверю — коготь по росту, а не сила удара, проставленная вразнобой:
                    // у паука она ноль, у мантикоры тысяча, и обе цифры одинаково случайны.
                    float paw = Beastly.Paw(who);
                    if (paw > 0f) kilos = paw;
                    else if (arm != null && arm.weaponForce > 0f) kilos = arm.weaponForce / 30f;
                }

                // Чем боец прикладывает оружие к броне — решает тип удара, а не оружие.
                // Молот вгоняют силой, остриё ведут ловкостью, а клинок берёт тем, чего
                // у бойца больше: рубят и с плеча, и с кисти.
                // Ловкость ведёт остриё, всё прочее гонится силой. Режущее было «что
                // больше», но тогда меч требовал одного стата, а работал от другого: игровое
                // поле требований умеет только «И», выразить им «сила либо ловкость» нельзя.
                float stat = lead == 2 ? agi : str;

                float driven = stat * kilos;

                // Лестница тира сидит в самом весе: Smithy её туда и вписал. Домножать
                // второй раз значило бы считать её в квадрате — Т4 давал бы три с половиной
                // раза вместо неполных двух. Если перековка оружия выключена, вес игровой и
                // лестницу приходится нести здесь.
                if (blade != null && !Smithy.Bladed) driven *= Steps(blade.tier, blade.Quality);

                // Мастерство из пробития убрано: умение решает, попал ли ты и куда, а не
                // сколько железа продавил. Оно ушло в «Aim», где отсчитывается от половины —
                // ниже пятидесяти штрафует, выше ведёт.
                //
                // У зверя иначе: в людской расчёт меткости он не заходит вовсе, и владение
                // своим когтем ему считать больше негде. Ему оно остаётся здесь.
                if (man == null)
                {
                    // Зверю ступень оружия никто не пишет, а владение своим когтем у него
                    // есть: молодой волк рвёт наугад, матёрый идёт в горло. Считаем по месту
                    // его уровня в вилке вида и множим тем же счётом, что человеку, — иначе
                    // коготь упирается в стёганку и дальше не идёт.
                    int rank = Wild.Honed(who);
                    if (rank > 0) driven *= 1f + rank * Skill.Value;
                }

                // Это число нужно окну персонажа, где удара ещё нет: там показываем то же,
                // чем боец будет пробивать, — свой урон, собранный тем, что в руке.
                float much = 0f;

                if (arm != null && arm.damage != null)
                {
                    foreach (KeyValuePair<DamageType, Damage> one in arm.damage)
                    {
                        if (one.Value == null || (int)one.Key > 2) continue;
                        much += (one.Value.minDamage + one.Value.maxDamage) * 0.5f;
                    }
                }

                if (much <= 0f) much = driven * 0.01f;

                return Mathf.Max(0f, much * FocusOf(blade));
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>What the weapon itself claims to punch through, as a share.</summary>
        internal static float Written(UIWeaponInfo blade, int lead)
        {
            if (blade == null || blade.resistPen == null) return 0f;

            try
            {
                // Берём по тому же типу, которым бьёт: у дробящего своё проникновение,
                // у режущего своё, и смешивать их незачем.
                int type = (lead >= 0 && lead < blade.resistPen.Length) ? lead : 0;

                float much = blade.resistPen[type];

                // Ноль по ведущему типу — берём лучшее, какое у вещи есть: оружие, которому
                // проставили одно колющее, всё равно этим славится.
                if (much <= 0f)
                {
                    for (int i = 0; i < 3 && i < blade.resistPen.Length; i++)
                    {
                        if (blade.resistPen[i] > much) much = blade.resistPen[i];
                    }
                }

                return Mathf.Max(0f, much) * 0.01f;
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>What the armour at the struck place asks to be defeated.</summary>
        internal static float Guard(UIArmorInfo coat, float wear, int slot, float worth = 1f)
        {
            if (coat == null) return 0f;

            try
            {
                // Старое имя оставлено ради тех, кто его зовёт: прочность, подсказка,
                // кузница. Значит оно теперь «сколько миллиметров держит против реза» —
                // число того же смысла, только в честной мерке.
                if (Thickness(coat.armourClass) > 0f)
                {
                    return Must(coat, wear, slot, 0, worth);
                }

                float held;
                if (!Table().TryGetValue(coat.armourClass, out held)) return 0f;
                if (held <= 0f) return 0f;

                // Ступени считаются от того тира, с которого класс вообще существует:
                // лат ниже второго в игре нет, кожи ниже первого тоже.
                int first;
                if (!Firsts().TryGetValue(coat.armourClass, out first)) first = 1;

                int t = (int)coat.tier;

                // Лестница одна на всех, и класс входит в неё со своей ступени. Прежде у
                // жёстких и мягких были свои множители, и вычет с весом росли по-разному.
                float bottom = Rung(first);
                held *= bottom > 0f ? Rung(t) / bottom : 1f;

                // Кираса самая толстая, шлем приходится держать лёгким, чтобы носить,
                // конечности тоньше всех ради подвижности.
                if (slot == Anatomy.Chest) held *= Chest.Value;
                else if (slot == Anatomy.Pants) held *= Legs.Value;
                else if (slot == Anatomy.Arms) held *= Sleeve.Value;

                // Качество ковки: у этой вещи прочность на килограмм своя, и стена вместе с
                // ней. Единица — вещь ровно по таблице класса.
                if (worth > 0f) held *= worth;

                // Разбитая вещь держит хуже, ровно по игровой поправке.
                return held * Mathf.Clamp01(wear);
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>
        /// What share of the blow survives the armour. Reckoned by subtraction, not by
        /// proportion: the piercing eats the armour reckoning, and whatever is left of the
        /// reckoning eats the damage. Eat it all and the weapon strikes at its full strength.
        /// </summary>
        internal static float Share(Attack attack, UIArmorInfo coat, float wear, int slot,
            float worth = 1f, UnitAttribute who = null)
        {
            int lead = Lead(attack);

            // Сколько надо продавить здесь и этим ударом: доспех и то, что под ним. Мышца и
            // кость держат сами по себе, и голый человек больше не равен голому человеку.
            float must = Must(coat, wear, slot, lead, worth) + Sinew.Wall(who, slot);
            if (must <= 0f) return 1f;

            float mm = Punch(attack);

            float coming = Coming(attack);
            if (coming <= 0.01f) return Spared.Value;

            // Дробящее не рубит железо, оно проносит удар сквозь него: доля всегда доходит
            // до тела, сколько бы ни было надето. Остальное встречает вычет как обычно.
            float always = 0f;

            // Дробящее проносит свою долю сквозь железо. Сквозь голое тело оно проносит
            // столько же, сколько сквозь ткань: мягкому нечего ему противопоставить.
            if (Crushing(attack))
            {
                always = Pass(coat != null ? coat.armourClass : ArmourClass.Cloth);
            }

            // Вычитания больше нет: есть порог. Ниже трёх четвертей того, что надо
            // продавить, не проходит ничего, кроме того, что идёт мимо железа; на четверть
            // сверх — проходит всё. Между — по прямой.
            float[] gate = Gates();
            float low = gate[0], high = gate.Length > 1 ? gate[1] : 1.25f;

            // Крит — это не удар посильнее, это найденная щель: подмышка, прорезь забрала,
            // зазор над коленом. Железу нечего сказать о том, что идёт мимо него. Берётся
            // большее из двух: молот, нашедший щель, не проносит вдвое.
            if (attack.isCrit)
            {
                float found = Found() * Reach(mm, must, low);
                if (found > always) always = found;
            }

            float through = (mm - low * must) / Mathf.Max(0.0001f, (high - low) * must);
            through = Mathf.Clamp01(through);

            // Мимо железа идёт своё: крит в щель и доля дробящего. Остальное решает порог.
            float share = Mathf.Clamp01(always + (1f - always) * through);

            // Что прошло сквозь само железо и сколько железа пробито — для износа. Щель в
            // латах доспеха не портит: удар прошёл мимо него. Дробящее портит: его доля
            // проходит сквозь сталь, сминая её.
            if (who != null)
            {
                float crush = Crushing(attack)
                    ? Pass(coat != null ? coat.armourClass : ArmourClass.Cloth) : 0f;

                // Нет на месте доспеха — нечему и портиться: удар пришёл в голое.
                float iron = 0f;
                if (coat != null)
                {
                    iron = (crush > 0f && Mathf.Approximately(always, crush)) ? crush : 0f;
                    iron += (1f - always) * through;
                }

                float plate = coat != null ? Must(coat, wear, slot, lead, worth) : 0f;

                Remember(who, coming, Mathf.Clamp01(iron), Mathf.Min(mm, plate));
            }

            if (Telling.Value && coat != null)
            {
                Tell(attack, coat, slot, must, Mathf.Max(0f, must - mm), coming, always, share);
            }

            return share;
        }

        /// <summary>What one blow did to the iron it met.</summary>
        internal sealed class Blow
        {
            internal float coming;    // физический урон удара до брони
            internal float iron;      // доля, прошедшая сквозь само железо, от нуля до единицы
            internal float pierced;   // сколько брони пробито, в тех же мерах, что и вычет
        }

        private static readonly Dictionary<UnitAttribute, Blow> blows =
            new Dictionary<UnitAttribute, Blow>();

        private static void Remember(UnitAttribute who, float coming, float iron, float pierced)
        {
            if (who == null) return;

            // Павшие и ушедшие со сцены своих записей не забирают. Их немного, но копятся.
            if (blows.Count > 400) blows.Clear();

            blows[who] = new Blow { coming = coming, iron = iron, pierced = pierced };
        }

        /// <summary>Takes the record of the last blow at this man, once.</summary>
        internal static Blow Took(UnitAttribute who)
        {
            Blow got;
            if (who == null || !blows.TryGetValue(who, out got)) return null;

            blows.Remove(who);
            return got;
        }

        /// <summary>
        /// What share of a crushing blow this class is able to stop at all. The rest goes
        /// through whatever is worn: mail keeps four fifths of it out, plate stops barely more
        /// than a third. Sharp and stabbing meet the whole wall, so this is for crushing alone.
        /// </summary>
        internal static float Stops(UIArmorInfo coat)
        {
            if (coat == null) return 1f;

            try
            {
                // В карточке пишем середину вилки: число должно стоять на месте, а не
                // прыгать при каждом открытии.
                return 1f - Middle(coat.armourClass);
            }
            catch
            {
                return 1f;
            }
        }

        /// <summary>
        /// Есть ли в ударе то, что наша модель умеет считать, — рубящее, дробящее, колющее.
        ///
        /// Огонь, холод, яд и прочее наша стена не останавливает и останавливать не должна:
        /// у доспеха на это свои сопротивления, писанные в игре. Если физики в ударе нет,
        /// мы в расчёт не вмешиваемся вовсе и оставляем игре её собственный счёт.
        /// </summary>
        private static void Tell(Attack attack, UIArmorInfo coat, int slot,
            float guard, float rest, float coming, float always, float share)
        {
            try
            {
                string who = "?";
                if (attack.attacker != null && attack.attacker.Data != null)
                {
                    who = attack.attacker.Data.unitname ?? attack.attacker.name;
                }

                string where = slot == Anatomy.Head ? "голова"
                    : slot == Anatomy.Chest ? "корпус"
                    : slot == Anatomy.Pants ? "ноги" : "место " + slot;

                ItemForgePlugin.Log.LogInfo(
                    "Удар: «" + who + "» в " + where
                    + " | " + (coat != null ? coat.armourClass.ToString() + " " + coat.tier : "голо")
                    + " | вычет " + guard.ToString("0.#")
                    + ", пробитие " + (guard - rest).ToString("0.#")
                    + ", остаток " + rest.ToString("0.#")
                    + " | урон " + coming.ToString("0.#")
                    + (always > 0f ? ", мимо стены " + (always * 100f).ToString("0") + "%" : "")
                    + (attack.isCrit ? " (крит)" : "")
                    + " -> прошло " + (share * 100f).ToString("0") + "%");
            }
            catch
            {
            }
        }

        /// <summary>Whether this weapon leads with a crushing blow.</summary>
        internal static bool Crushes(UIWeaponInfo blade)
        {
            if (blade == null || blade.damage == null) return false;

            try
            {
                float blunt = 0f, all = 0f;

                foreach (KeyValuePair<DamageType, Damage> one in blade.damage)
                {
                    if (one.Value == null || (int)one.Key > 2) continue;

                    float much = one.Value.maxDamage;
                    all += much;
                    if ((int)one.Key == 1) blunt += much;
                }

                return all > 0f && blunt > all * 0.5f;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Которая доля дробящего проходит мимо стены, классом за классом.
        ///
        /// Это свойство удара, а не куртки: молот не встречает стену пониже, он проносит часть
        /// удара сквозь железо, и остальное упирается в полный вычет. Оттого строка и стоит у
        /// оружия — броня о ней ничего не знает.
        /// </summary>
        internal static string Passage()
        {
            // Числа здесь были бы враньём наполовину: доля зависит от того, на ком доспех, а
            // не от того, чем бьют. Оружие знает про себя одно — что железо ему не помеха.
            return "Наносит урон сквозь любую броню";
        }

        /// <summary>
        /// Пробитие бойца тем, что у него в основной руке. Число меняется со сменой оружия, и
        /// так и должно быть: пробитие — свойство пары «боец и оружие», а не одного бойца.
        /// </summary>
        /// <summary>
        /// What this fighter's blow comes to when everything has been counted.
        ///
        /// То же, чем он пробивает, но без собранности удара: собранность — это про то, как
        /// удар входит в железо, а не про то, сколько он несёт. В карточке нужно второе.
        /// </summary>
        internal static float Hitting(UnitAttribute who)
        {
            try
            {
                if (who == null || who.weapons == null || who.weapons.Count == 0) return 0f;

                Weapon arm = who.weapons[0];
                if (arm == null || arm.damage == null) return 0f;

                float much = 0f;

                foreach (KeyValuePair<DamageType, Damage> one in arm.damage)
                {
                    if (one.Value == null) continue;
                    much += (one.Value.minDamage + one.Value.maxDamage) * 0.5f;
                }

                return much;
            }
            catch
            {
                return 0f;
            }
        }

        internal static float Piercing(UnitAttribute who)
        {
            try
            {
                if (who == null) return 0f;

                HumaniodUnit man = who as HumaniodUnit;
                if (man == null) return 0f;

                UIWeaponInfo blade = null;
                EquipmentManager kit = man.equipmentmanger;

                if (kit != null && kit.equipInfos != null && kit.equipInfos.Length > 0
                    && kit.equipInfos[0] != null && kit.equipInfos[0].IsEquiped())
                {
                    blade = kit.equipInfos[0].inventory.itemInfo as UIWeaponInfo;
                }

                Weapon arm = null;
                if (man.weapons != null && man.weapons.Count > 0) arm = man.weapons[0];

                return Push(who, arm, blade, Lead(blade));
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>Which kind of blow this weapon leads with, without an attack to ask.</summary>
        internal static int Lead(UIWeaponInfo blade)
        {
            // Кулак — дробящий: бьют костяшкой, а не кромкой.
            if (blade == null || blade.damage == null) return 1;

            try
            {
                float sharp = 0f, blunt = 0f, stab = 0f;

                foreach (KeyValuePair<DamageType, Damage> one in blade.damage)
                {
                    if (one.Value == null) continue;

                    if (one.Key == DamageType.sharp) sharp += one.Value.maxDamage;
                    else if (one.Key == DamageType.blunt) blunt += one.Value.maxDamage;
                    else if (one.Key == DamageType.stab) stab += one.Value.maxDamage;
                }

                if (blunt >= sharp && blunt >= stab) return 1;
                if (stab > sharp) return 2;
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        internal static bool Physical(Attack attack)
        {
            return attack != null && Coming(attack) > 0.01f;
        }

        private static float[] gap;
        private static string gapRead;

        /// <summary>
        /// Насколько оружию по силам воспользоваться щелью.
        ///
        /// Щель в латах есть у всякого латника, но найти её и пройти в неё может только
        /// оружие, которому доспех почти по зубам. Прежде крит проносил свою треть-половину
        /// мимо железа, кто бы ни бил: грабитель с пробитием 32 против лат, где надо 248,
        /// доносил мимо стены сорок два процента удара. Теперь доля щели растёт вместе с
        /// пробитием и становится полной, когда оружие дотягивает до трёх четвертей стены —
        /// до того же порога, с которого удар начинает проходить и сам.
        /// </summary>
        private static float Reach(float mm, float must, float low)
        {
            if (must <= 0f) return 1f;

            return Mathf.Clamp01(mm / Mathf.Max(0.0001f, low * must));
        }

        /// <summary>Сколько крит проносит мимо стены — свой жребий на каждый удар.</summary>
        private static float Found()
        {
            string written = Gap.Value ?? "";

            if (written != gapRead || gap == null)
            {
                gapRead = written;

                List<float> got = new List<float>();
                foreach (string one in written.Split(','))
                {
                    float much;
                    if (float.TryParse(one.Trim(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out much)) got.Add(much);
                }

                gap = got.ToArray();
            }

            if (gap.Length == 0) return 0f;
            if (gap.Length == 1) return Mathf.Clamp01(gap[0]);

            float least = Mathf.Min(gap[0], gap[1]);
            float most = Mathf.Max(gap[0], gap[1]);

            return Mathf.Clamp01(UnityEngine.Random.Range(least, most));
        }

        /// <summary>
        /// Доля по стене, заданной прямо — без вещи, из которой её выводить.
        ///
        /// У зверя доспеха нет, у него шкура, и её вычет считает Beastly по росту и уровню.
        /// Счёт дальше тот же самый: пробитие съедает стену, остаток съедает урон.
        /// </summary>
        internal static float ShareAt(Attack attack, float wall, float through,
            UnitAttribute who = null)
        {
            if (wall <= 0f) return 1f;

            // Шкура — тоже то, что клинку приходится пробивать.
            if (who != null) Remember(who, Coming(attack), 0f, Mathf.Min(Punch(attack), wall));

            float rest = wall - Punch(attack);
            if (rest <= 0f) return 1f;

            float coming = Coming(attack);
            if (coming <= 0.01f) return Spared.Value;

            float always = 0f;
            if (Crushing(attack)) always = Mathf.Clamp01(through);

            if (attack.isCrit)
            {
                float[] gate = Gates();
                float found = Found() * Reach(Punch(attack), wall, gate[0]);
                if (found > always) always = found;
            }

            float stopped = coming * (1f - always);
            float left = coming * always + Mathf.Max(0f, stopped - rest);

            return left <= 0f ? 0f : Mathf.Clamp01(left / coming);
        }

        private static float Coming(Attack attack)
        {
            try
            {
                float all = 0f;
                foreach (KeyValuePair<DamageType, Damage> one in attack.damage)
                {
                    if ((int)one.Key > 2 || one.Value == null) continue;
                    all += one.Value.currentDamage;
                }
                return all;
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>Which kind of blow leads: 0 cutting, 1 crushing, 2 thrusting.</summary>
        private static int Lead(Attack attack)
        {
            try
            {
                if (attack == null || attack.damage == null) return 0;

                float most = 0f;
                int lead = 0;

                foreach (KeyValuePair<DamageType, Damage> one in attack.damage)
                {
                    int kind = (int)one.Key;
                    if (kind > 2 || one.Value == null) continue;

                    if (one.Value.currentDamage > most)
                    {
                        most = one.Value.currentDamage;
                        lead = kind;
                    }
                }

                return lead;
            }
            catch
            {
                return 0;
            }
        }

        internal static bool Crushing(Attack attack)
        {
            return Lead(attack) == 1;
        }

        /// <summary>Which kind of blow leads: 0 cutting, 1 crushing, 2 thrusting.</summary>
        internal static int Kind(Attack attack)
        {
            return Lead(attack);
        }






        /// <summary>The item behind the weapon in hand, for its tier and quality.</summary>
        private static UIWeaponInfo Blade(Attack attack)
        {
            try
            {
                HumaniodUnit man = attack.attacker as HumaniodUnit;
                if (man == null || man.equipmentmanger == null || attack.weapon == null) return null;

                int index = attack.weapon.index;
                if (index < 0 || index >= man.equipmentmanger.equipInfos.Length) return null;

                EquipInfo slot = man.equipmentmanger.equipInfos[index];
                if (slot == null || !slot.IsEquiped()) return null;

                return slot.inventory.itemInfo as UIWeaponInfo;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Tier and quality together, as steps of growth above the lowest.
        ///
        /// Легендарному тир не считается: у него своя мера. Иначе множитель ложится на
        /// предмет, который и так выкован особо, и боевой молот в двадцать кило становится
        /// тридцатисемикилограммовым — такого не поднять ничем.
        /// </summary>
        /// <summary>The ladder rung for a weapon of this tier and make.</summary>
        internal static float Rung(ItemTier tier, UIItemQuality quality)
        {
            return Steps(tier, quality);
        }

        private static float Steps(ItemTier tier, UIItemQuality quality)
        {
            int q = (int)quality;

            // Легендарное и есть пятая ступень: в игре его поле тира стоит четвёртым, а имя
            // говорит пятое, и имя честнее.
            int t = q >= (int)UIItemQuality.Legendary ? 5 : (int)tier;
            if (t < 1) t = 1;

            float[] ladder = Ladder();
            if (ladder.Length == 0) return 1f;

            int i = t - 1;
            if (i >= ladder.Length) i = ladder.Length - 1;

            return ladder[i];
        }

        private static float[] ladder;
        private static string ladderRead;

        private static float[] Ladder()
        {
            string written = Keen.Value ?? "";

            if (written != ladderRead || ladder == null)
            {
                ladderRead = written;

                List<float> got = new List<float>();
                foreach (string one in written.Split(','))
                {
                    float much;
                    if (float.TryParse(one.Trim(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out much)) got.Add(much);
                }

                ladder = got.ToArray();
            }

            return ladder;
        }

        // Сколько дробящего проходит сквозь каждый класс, что бы ни было под ним.
        private static readonly Dictionary<ArmourClass, float> passes =
            new Dictionary<ArmourClass, float>();
        private static string passed;

        private static Dictionary<ArmourClass, float> Passes()
        {
            string written = Through.Value ?? "";
            if (written == passed) return passes;

            passed = written;
            passes.Clear();

            floors.Clear();

            foreach (string one in written.Split(','))
            {
                string[] halves = one.Split('=');
                if (halves.Length != 2) continue;

                // Записать можно и одним числом, и вилкой «0.15-0.35»: удар молота ложится
                // по-разному — по кирасе вскользь, по наплечнику в стык, — и одно число на
                // все случаи врёт в обе стороны сразу.
                string said = halves[1].Trim();
                string[] ends = said.Split('-');

                float low, high;

                if (ends.Length == 2
                    && float.TryParse(ends[0].Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out low)
                    && float.TryParse(ends[1].Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out high))
                {
                    if (low > high) { float swap = low; low = high; high = swap; }
                }
                else
                {
                    if (!float.TryParse(said, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out high)) continue;
                    low = high;
                }

                try
                {
                    ArmourClass kind = (ArmourClass)Enum.Parse(typeof(ArmourClass),
                        halves[0].Trim(), true);

                    passes[kind] = high;
                    floors[kind] = low;
                }
                catch
                {
                }
            }

            return passes;
        }

        // Нижний конец вилки «сквозь». Верхний лежит в «passes».
        private static readonly Dictionary<ArmourClass, float> floors =
            new Dictionary<ArmourClass, float>();

        /// <summary>
        /// What share of this crushing blow goes through the harness whatever it is.
        ///
        /// Бросается на каждый удар: молот, пришедший по кирасе вскользь, проносит меньше
        /// того, что попал в стык наплечника. Где записано одно число, вилка вырождается в
        /// него, и бросок ничего не меняет.
        /// </summary>
        private static float Pass(ArmourClass kind)
        {
            float high;
            if (!Passes().TryGetValue(kind, out high)) return Mathf.Clamp01(Ignore.Value);

            float low;
            if (!floors.TryGetValue(kind, out low) || low >= high) return Mathf.Clamp01(high);

            return Mathf.Clamp01(UnityEngine.Random.Range(low, high));
        }

        /// <summary>The middle of that spread: what a card should say, unchanging.</summary>
        private static float Middle(ArmourClass kind)
        {
            float high;
            if (!Passes().TryGetValue(kind, out high)) return Mathf.Clamp01(Ignore.Value);

            float low;
            if (!floors.TryGetValue(kind, out low) || low >= high) return Mathf.Clamp01(high);

            return Mathf.Clamp01((low + high) * 0.5f);
        }

        // --- толщина, стойкость и собранность удара ---------------------------

        private static readonly Dictionary<ArmourClass, float> thicks =
            new Dictionary<ArmourClass, float>();
        private static string thickRead;

        /// <summary>How thick a suit of this class is at the tier it starts from.</summary>
        private static float Thickness(ArmourClass kind)
        {
            string written = Thick != null ? (Thick.Value ?? "") : "";

            if (written != thickRead)
            {
                thickRead = written;
                thicks.Clear();

                foreach (string one in written.Split(','))
                {
                    int split = one.IndexOf('=');
                    if (split <= 0) continue;

                    float much;
                    if (!float.TryParse(one.Substring(split + 1).Trim(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out much)) continue;

                    try
                    {
                        thicks[(ArmourClass)Enum.Parse(typeof(ArmourClass),
                            one.Substring(0, split).Trim(), true)] = much;
                    }
                    catch
                    {
                    }
                }
            }

            float got;
            return thicks.TryGetValue(kind, out got) ? got : 0f;
        }

        private static readonly Dictionary<ArmourClass, float[]> stops =
            new Dictionary<ArmourClass, float[]>();
        private static string stopRead;

        /// <summary>How much of a blow this class holds: 0 cut, 1 crush, 2 thrust.</summary>
        private static float Stopping(ArmourClass kind, int lead)
        {
            string written = Holds != null ? (Holds.Value ?? "") : "";

            if (written != stopRead)
            {
                stopRead = written;
                stops.Clear();

                foreach (string one in written.Split(','))
                {
                    int split = one.IndexOf('=');
                    if (split <= 0) continue;

                    string[] three = one.Substring(split + 1).Split('|');
                    if (three.Length < 3) continue;

                    float[] much = new float[3];
                    bool good = true;

                    for (int i = 0; i < 3; i++)
                    {
                        if (!float.TryParse(three[i].Trim(), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out much[i])) { good = false; break; }
                    }

                    if (!good) continue;

                    try
                    {
                        stops[(ArmourClass)Enum.Parse(typeof(ArmourClass),
                            one.Substring(0, split).Trim(), true)] = much;
                    }
                    catch
                    {
                    }
                }
            }

            float[] row;
            if (!stops.TryGetValue(kind, out row)) return 0f;

            int at = lead >= 0 && lead < 3 ? lead : 0;
            return row[at];
        }

        private static readonly Dictionary<ArmourClass, float[]> holds =
            new Dictionary<ArmourClass, float[]>();
        private static string holdRead;

        /// <summary>How thick this class seems to that kind of blow: 0 cut, 1 crush, 2 thrust.</summary>
        private static float Holding(ArmourClass kind, int lead)
        {
            string written = Hold != null ? (Hold.Value ?? "") : "";

            if (written != holdRead)
            {
                holdRead = written;
                holds.Clear();

                foreach (string one in written.Split(','))
                {
                    int split = one.IndexOf('=');
                    if (split <= 0) continue;

                    string[] three = one.Substring(split + 1).Split('|');
                    if (three.Length < 3) continue;

                    float[] much = new float[3];
                    bool good = true;

                    for (int i = 0; i < 3; i++)
                    {
                        if (!float.TryParse(three[i].Trim(), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out much[i])) { good = false; break; }
                    }

                    if (!good) continue;

                    try
                    {
                        holds[(ArmourClass)Enum.Parse(typeof(ArmourClass),
                            one.Substring(0, split).Trim(), true)] = much;
                    }
                    catch
                    {
                    }
                }
            }

            float[] row;
            if (!holds.TryGetValue(kind, out row)) return 1f;

            int at = lead >= 0 && lead < 3 ? lead : 0;
            return row[at] > 0f ? row[at] : 1f;
        }

        private static readonly Dictionary<string, float> focuses =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private static string focusRead;

        /// <summary>Into how small a place this weapon gathers its blow.</summary>
        private static float FocusOf(UIWeaponInfo blade)
        {
            string written = Focus != null ? (Focus.Value ?? "") : "";

            if (written != focusRead)
            {
                focusRead = written;
                focuses.Clear();

                foreach (string one in written.Split(','))
                {
                    int split = one.IndexOf('=');
                    if (split <= 0) continue;

                    float much;
                    if (!float.TryParse(one.Substring(split + 1).Trim(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out much)) continue;

                    focuses[one.Substring(0, split).Trim()] = much;
                }
            }

            if (blade == null) return 1f;

            float got;
            return focuses.TryGetValue(blade.weaponClass.ToString(), out got) ? got : 1f;
        }

        /// <summary>How thick this very piece is, in millimetres.</summary>
        internal static float Mm(UIArmorInfo coat, float wear, int slot, float worth = 1f)
        {
            if (coat == null) return 0f;

            try
            {
                float much = Thickness(coat.armourClass);
                if (much <= 0f) return 0f;

                int first;
                if (!Firsts().TryGetValue(coat.armourClass, out first)) first = 1;

                float bottom = Rung(first);
                much *= bottom > 0f ? Rung((int)coat.tier) / bottom : 1f;

                // Кираса толще поножей: там, где бьют чаще, железа кладут больше. На руке
                // от неё остаётся рукав, и держит он вполсилы.
                if (slot == Anatomy.Chest) much *= Chest.Value;
                else if (slot == Anatomy.Pants) much *= Legs.Value;
                else if (slot == Anatomy.Arms) much *= Sleeve.Value;

                if (worth > 0f) much *= worth;

                return much * Mathf.Clamp01(wear);
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>
        /// The old reckoning's magnitude, kept for what is built on top of it.
        ///
        /// Прочность вещи и разброс по ней выводились из вычета, а вычет был числом порядка
        /// сотен. Толщина — числом порядка единиц, и подставить её прямо значило бы обрушить
        /// прочность в сто раз. Потому здесь та же толщина, поднятая до прежней высоты: бой
        /// считает миллиметрами, а кузница и износ — этой меркой.
        /// </summary>
        internal static float Wall(UIArmorInfo coat, float wear, int slot, float worth = 1f)
        {
            return Must(coat, wear, slot, 0, worth) * Units.Value;
        }

        /// <summary>
        /// Сколько держит человек целиком против этого рода удара.
        ///
        /// Стена у каждого места своя: кираса толще поножей, рукав тоньше кирасы, а под всем
        /// этим ещё мышцы и кости. Одним числом это сводится единственным честным способом —
        /// средним по тому, как часто в эти места попадают: грудь чаще всего, голова реже.
        ///
        /// Число это и есть то, с чем сравнивают чужой общий урон: «держу 52» против удара в
        /// 49 значит, что не пройдёт ничего, кроме крита.
        /// </summary>
        internal static float Holding(UnitAttribute who, int lead)
        {
            if (who == null) return 0f;

            try
            {
                List<KeyValuePair<int, int>> places = Anatomy.Table();
                if (places == null || places.Count == 0) return 0f;

                float much = 0f;
                float all = 0f;

                foreach (KeyValuePair<int, int> one in places)
                {
                    if (one.Value <= 0) continue;

                    float wear;
                    UIArmorInfo coat = Anatomy.Worn(who, one.Key, out wear);

                    float worth = coat != null
                        ? Temper.Worth(Anatomy.WornKit(who, one.Key))
                        : 1f;

                    much += one.Value * (Must(coat, wear, one.Key, lead, worth)
                        + Sinew.Wall(who, one.Key));

                    all += one.Value;
                }

                return all > 0f ? much / all : 0f;
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>How many millimetres this blow has to push through here.</summary>
        internal static float Must(UIArmorInfo coat, float wear, int slot, int lead,
            float worth = 1f)
        {
            if (coat == null) return 0f;

            try
            {
                float much = Stopping(coat.armourClass, lead);
                if (much <= 0f) return 0f;

                // Растёт той же лестницей, что и урон: иначе с каждым тиром одно обгоняет
                // другое, и то, что было равновесием в начале игры, к концу им быть перестаёт.
                int first;
                if (!Firsts().TryGetValue(coat.armourClass, out first)) first = 1;

                much *= Hone.Step((int)coat.tier, first);

                // Кираса толще поножей: там, где бьют чаще, железа кладут больше. На руке
                // от неё остаётся рукав, и держит он вполсилы.
                if (slot == Anatomy.Chest) much *= Chest.Value;
                else if (slot == Anatomy.Pants) much *= Legs.Value;
                else if (slot == Anatomy.Arms) much *= Sleeve.Value;

                if (worth > 0f) much *= worth;

                return much * Mathf.Clamp01(wear);
            }
            catch
            {
                return 0f;
            }
        }

        private static float[] gates;
        private static string gateRead;

        private static float[] Gates()
        {
            string written = Gate != null ? (Gate.Value ?? "") : "";

            if (written != gateRead || gates == null)
            {
                gateRead = written;

                List<float> two = new List<float>();
                foreach (string one in written.Split(','))
                {
                    float much;
                    if (float.TryParse(one.Trim(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out much)) two.Add(much);
                }

                gates = two.Count >= 2 ? two.ToArray() : new float[] { 0.75f, 1.25f };
            }

            return gates;
        }

        private static float[] rungs;
        private static string rungsRead;

        /// <summary>Where this tier stands on the common ladder.</summary>
        private static float Rung(int tier)
        {
            string written = Rungs != null ? (Rungs.Value ?? "") : "";

            if (written != rungsRead || rungs == null)
            {
                rungsRead = written;

                List<float> steps = new List<float>();

                foreach (string one in written.Split(','))
                {
                    float much;
                    if (float.TryParse(one.Trim(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out much)) steps.Add(much);
                }

                rungs = steps.Count > 0 ? steps.ToArray() : new float[] { 1f };
            }

            if (tier < 0) tier = 0;
            return tier < rungs.Length ? rungs[tier] : rungs[rungs.Length - 1];
        }

        private static readonly Dictionary<ArmourClass, float> grows =
            new Dictionary<ArmourClass, float>();
        private static string grew;

        private static Dictionary<ArmourClass, float> Grows()
        {
            string written = (Hard.Value ?? "") + "|" + Soft.Value + "|" + Steep.Value;
            if (written == grew) return grows;

            grew = written;
            grows.Clear();

            foreach (ArmourClass which in Enum.GetValues(typeof(ArmourClass)))
                grows[which] = Soft.Value;

            foreach (string one in (Hard.Value ?? "").Split(','))
            {
                string name = one.Trim();
                if (name.Length == 0) continue;

                try
                {
                    grows[(ArmourClass)Enum.Parse(typeof(ArmourClass), name, true)] = Steep.Value;
                }
                catch
                {
                }
            }

            return grows;
        }

        // Ступени тира считаются от того, с которого класс начинается: лат ниже второго
        // в игре нет, и считать им ступень от первого значило бы дарить лишнюю.
        private static readonly Dictionary<ArmourClass, int> firsts =
            new Dictionary<ArmourClass, int>
            {
                { ArmourClass.Cloth, 1 }, { ArmourClass.PaddingArmor, 1 },
                { ArmourClass.LightLeatherArmor, 1 }, { ArmourClass.HardLeaterArmor, 1 },
                { ArmourClass.SplintArmor, 1 }, { ArmourClass.metalHelmet, 1 },
                { ArmourClass.ChainArmor, 2 }, { ArmourClass.ScaleMail, 2 },
                { ArmourClass.LamellarArmor, 2 }, { ArmourClass.HalfPlate, 2 },
                { ArmourClass.PlateArmor, 2 },
            };

        private static Dictionary<ArmourClass, int> Firsts()
        {
            return firsts;
        }

        private static readonly Dictionary<ArmourClass, float> table =
            new Dictionary<ArmourClass, float>();
        private static string read;

        private static Dictionary<ArmourClass, float> Table()
        {
            string written = Guards.Value ?? "";
            if (written == read) return table;

            read = written;
            table.Clear();

            foreach (string one in written.Split(','))
            {
                string[] halves = one.Split('=');
                if (halves.Length != 2) continue;

                float much;
                if (!float.TryParse(halves[1].Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out much)) continue;

                try
                {
                    ArmourClass which = (ArmourClass)Enum.Parse(
                        typeof(ArmourClass), halves[0].Trim(), true);
                    table[which] = much;
                }
                catch
                {
                }
            }

            return table;
        }
    }

    /// <summary>
    /// Снимает игровое пробитие брони — второе, чужое.
    ///
    /// В игре у всякого оружия есть resistPen: доля, съедающая сопротивление жертвы. Это вторая
    /// система пробития рядом с нашей, считающая другие числа в другую сторону. На физике она и
    /// так ничего не значила — наш расчёт отставляет игровое сопротивление в сторону, — а вот
    /// стихий касалась: «проникающая режущая сила 10 %» отчего-то подъедала стойкость доспеха к
    /// огню.
    ///
    /// Снимается на выходе, а не у источника: так разом уходит и написанное в вещи, и
    /// доставшееся существу от природы, и надбавки с самоцветов.
    /// </summary>
    [HarmonyPatch(typeof(UnitAttribute), "WriteUnitAttribute")]
    internal static class Sole_Breach_Patch
    {
        private static void Postfix(UnitAttribute __instance)
        {
            if (Breach.Sole == null || !Breach.Sole.Value) return;

            try
            {
                if (__instance.damageResistPen != null)
                {
                    Array.Clear(__instance.damageResistPen, 0, __instance.damageResistPen.Length);
                }

                if (__instance.weapons == null) return;

                foreach (Weapon arm in __instance.weapons)
                {
                    if (arm == null || arm.resistPen == null) continue;
                    Array.Clear(arm.resistPen, 0, arm.resistPen.Length);
                }
            }
            catch
            {
            }
        }
    }
}
