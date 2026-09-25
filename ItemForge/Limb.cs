using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// A man is not a bucket of blood: he is a head, a body and four limbs, each with its own.
    ///
    /// Здоровье в этой игре — одно число на человека, и оттого место удара не значило ничего:
    /// двадцать ран в голень убивали так же надёжно, как одна в горло. Здесь этого числа больше
    /// нет. Вместо него шесть: голова, туловище, две руки и две ноги. Что пришлось в руку, то
    /// руку и тратит.
    ///
    /// Умирают от двух: туловища и головы. Рука и нога не убивают — они выходят из строя, и это
    /// хуже смерти не бывает, но и смертью не является. Выбитой рукой не держат: двуручное
    /// падает при любой, одноручное при своей, щит при своей. На одной ноге человек ползёт,
    /// без обеих не идёт вовсе.
    ///
    /// Полоса здоровья в игре осталась и показывает сумму: она честна, покуда все шесть целы,
    /// и остаётся честной после — просто половина её лежит в руках, которыми уже не поднять
    /// меча.
    ///
    /// По выбитому месту бить можно и дальше, но уходит это в туловище: у человека с
    /// перебитой рукой отнимают уже не руку.
    /// </summary>
    internal static class Limb
    {
        internal const int Head = 0;
        internal const int Chest = 1;
        internal const int ArmL = 2;
        internal const int ArmR = 3;
        internal const int LegL = 4;
        internal const int LegR = 5;
        internal const int Count = 6;

        internal static readonly string[] Called =
        {
            "голова", "туловище", "левая рука", "правая рука", "левая нога", "правая нога"
        };

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Start;
        internal static ConfigEntry<string> PerLevel;
        internal static ConfigEntry<string> PerEndurance;
        internal static ConfigEntry<float> Step;
        internal static ConfigEntry<float> Force;
        internal static ConfigEntry<float> Spill;
        internal static ConfigEntry<float> Area;
        internal static ConfigEntry<float> Lame;
        internal static ConfigEntry<float> Crawl;
        internal static ConfigEntry<float> Handed;
        internal static ConfigEntry<float> Clumsy;
        internal static ConfigEntry<float> Swing;
        internal static ConfigEntry<float> ClumsySwing;
        internal static ConfigEntry<bool> Polearm;
        internal static ConfigEntry<float> Swapped;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Limb", "Enabled", true,
                "Give a man six healths instead of one: head, body, two arms, two legs. A blow "
                + "spends the place it lands on, death comes from the body and the head alone, "
                + "and a limb that runs out stops working instead of killing him.");

            Start = config.Bind("Limb", "Start", "head=20,chest=100,arm=25,leg=35",
                "What each place holds at the first level, before anything is grown. Arms and "
                + "legs are written once and stand for each of the two.");

            PerLevel = config.Bind("Limb", "PerLevel", "head=0.25,chest=1,legs=0.5,arms=0.25",
                "How the health of a level divides among the places, by weight. Legs and arms "
                + "are written for the pair and split in half between the two of them.");

            PerEndurance = config.Bind("Limb", "PerEndurance",
                "head=0.25,chest=1,legs=0.5,arms=0.5",
                "The same for a point of endurance, which favours the limbs a little more than "
                + "a level does: meat is on the arms.");

            Step = config.Bind("Limb", "Step", 5f,
                new ConfigDescription(
                    "The health a level and a point of endurance each carry, which is the "
                    + "game's own five. The weights above divide this, they do not multiply it.",
                    new AcceptableValueRange<float>(0f, 50f)));

            Force = config.Bind("Limb", "Force", 0.0001f,
                new ConfigDescription(
                    "What one point of a weapon's force takes off the place it struck, as a "
                    + "share of everything that place holds. This is what force is for now: not "
                    + "getting through armour but breaking what is under it.\n\n"
                    + "Force is now the weight swinging, so it runs from about a hundred for a "
                    + "plain sword in plain hands to fourteen hundred for a maul of the fifth "
                    + "tier in a harnessed giant's. At a ten-thousandth that maul takes a "
                    + "seventh of an arm with every blow it lands and the sword a hundredth.",
                    new AcceptableValueRange<float>(0f, 0.01f)));

            Spill = config.Bind("Limb", "Spill", 1f,
                new ConfigDescription(
                    "What a blow to a place already spent carries over to the body, as a share. "
                    + "One: an arm that is already ruined cannot be ruined further, but the man "
                    + "behind it still feels the hammer.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Area = config.Bind("Limb", "Area", 0.3f,
                new ConfigDescription(
                    "What share of a crushing blow goes round the place it struck, into what "
                    + "lies next to it.\n\n"
                    + "Клинок и остриё режут и колют в одну точку: рана у них узкая и вся "
                    + "приходится туда, куда пришёлся удар. Палица бьёт площадью — она не "
                    + "разрезает, она проносит вес, и сотрясение расходится по телу дальше "
                    + "места удара. Оттого доля её уходит в соседнее: от головы в туловище, от "
                    + "туловища в руки и ноги, от руки и ноги обратно в туловище.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Lame = config.Bind("Limb", "Lame", 0.75f,
                new ConfigDescription(
                    "What one spent leg takes off a man's pace, as a share. Three quarters. "
                    + "Both legs take everything.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Clumsy = config.Bind("Limb", "Clumsy", 0.25f,
                new ConfigDescription(
                    "What a two-handed weapon is worth in one hand to a man who is barely strong "
                    + "enough to hold it at all, as a share of its damage. A quarter: try lifting "
                    + "a twenty-kilo maul with one arm.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Handed = config.Bind("Limb", "Handed", 0.67f,
                new ConfigDescription(
                    "And what it is worth to a man of twice the strength the thing demands. Two "
                    + "thirds. Between the two it is read by how much strength he has over the "
                    + "demand: the less to spare, the closer to hopeless.",
                    new AcceptableValueRange<float>(0f, 1f)));

            ClumsySwing = config.Bind("Limb", "ClumsySwing", 0.25f,
                new ConfigDescription(
                    "The same for how fast he swings it, at the bare demand.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Swing = config.Bind("Limb", "Swing", 0.5f,
                new ConfigDescription(
                    "And at twice the demand. Half: a one-handed grip on a two-handed haft is "
                    + "slow however strong the hand.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Swapped = config.Bind("Limb", "Swapped", 0.67f,
                new ConfigDescription(
                    "What a one-handed weapon is worth in the wrong hand, as a share of its "
                    + "damage. Two thirds: a man whose sword arm is spent takes the sword in "
                    + "the other one and fights on worse. Only if that hand is free — with a "
                    + "shield or a second blade in it there is nowhere to put the thing, and "
                    + "it falls.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Polearm = config.Bind("Limb", "Polearm", false,
                "Let polearms be held one-handed too. Off by default, and they simply drop: a "
                + "halberd is fought with two hands on the haft at two different heights, and "
                + "there is no such thing as a one-handed halberd — the game has no animation "
                + "for it either, so a man doing it would still be shown gripping with a hand "
                + "he no longer has.");

            Crawl = config.Bind("Limb", "Crawl", 0.1f,
                new ConfigDescription(
                    "What a man with both legs spent has left of his pace. A tenth: he crawls. "
                    + "Not nothing on purpose — he has to get himself off the field and to the "
                    + "infirmary, and a bandage will give him back the rest of it in time.",
                    new AcceptableValueRange<float>(0.01f, 1f)));

            Telling = config.Bind("Limb", "Telling", true,
                "Write down every limb that goes out and every death by the body or the head.");
        }

        // ----------------------------------------------------------------- тело

        internal sealed class Body
        {
            internal readonly float[] now = new float[Count];
            internal readonly float[] max = new float[Count];
            internal bool known;
        }

        private static readonly ConditionalWeakTable<UnitAttribute, Body> bodies =
            new ConditionalWeakTable<UnitAttribute, Body>();

        /// <summary>This man's six, made if he has none yet.</summary>
        internal static Body Of(UnitAttribute who)
        {
            Body body;
            if (bodies.TryGetValue(who, out body)) return body;

            body = new Body();
            bodies.Add(who, body);
            return body;
        }

        /// <summary>Whether this fighter is reckoned by places at all.</summary>
        internal static bool Counts(UnitAttribute who)
        {
            if (Enabled == null || !Enabled.Value) return false;
            if (!(who is HumaniodUnit)) return false;

            // Зверей не трогаем: у них своё сложение, и шести мест у них нет. Прирученный
            // зверь встаёт в отряд и становится с виду человеком этой игры — проверяем не
            // по тому, кем он записан, а по тому, кто он есть.
            try
            {
                if (who.info != null)
                {
                    if (who.info.utype == UnitType.animal) return false;
                    if (who.info.utype == UnitType.monster) return false;
                }
            }
            catch
            {
            }

            return true;
        }

        // ----------------------------------------------------------------- сколько где

        private static readonly Dictionary<string, float[]> shares =
            new Dictionary<string, float[]>();
        private static readonly Dictionary<string, string> read = new Dictionary<string, string>();

        /// <summary>Доли роста по местам, уже разнесённые на левое и правое.</summary>
        private static float[] Shares(string name, ConfigEntry<string> from)
        {
            string written = from != null ? (from.Value ?? "") : "";

            string was;
            if (read.TryGetValue(name, out was) && was == written) return shares[name];

            read[name] = written;

            float[] mine = new float[Count];
            float all = 0f;

            foreach (string one in written.Split(','))
            {
                int split = one.IndexOf('=');
                if (split <= 0) continue;

                float much;
                if (!float.TryParse(one.Substring(split + 1).Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out much)) continue;

                switch (one.Substring(0, split).Trim().ToLowerInvariant())
                {
                    case "head": mine[Head] += much; all += much; break;
                    case "chest":
                    case "body": mine[Chest] += much; all += much; break;

                    // Писано на пару, ложится на каждую по половине.
                    case "arms":
                        mine[ArmL] += much * 0.5f; mine[ArmR] += much * 0.5f; all += much; break;
                    case "legs":
                    case "pants":
                        mine[LegL] += much * 0.5f; mine[LegR] += much * 0.5f; all += much; break;

                    case "arm":
                        mine[ArmL] += much; mine[ArmR] += much; all += much * 2f; break;
                    case "leg":
                        mine[LegL] += much; mine[LegR] += much; all += much * 2f; break;
                }
            }

            // Доли — это доли: пять очков за уровень делятся между местами, а не множатся на них.
            if (all > 0f)
            {
                for (int i = 0; i < Count; i++) mine[i] /= all;
            }

            shares[name] = mine;
            return mine;
        }

        private static float[] first;
        private static string firstRead;

        /// <summary>Сколько каждое место держит на первом уровне.</summary>
        private static float[] First()
        {
            string written = Start.Value ?? "";

            if (written == firstRead && first != null) return first;

            firstRead = written;
            first = new float[Count];

            foreach (string one in written.Split(','))
            {
                int split = one.IndexOf('=');
                if (split <= 0) continue;

                float much;
                if (!float.TryParse(one.Substring(split + 1).Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out much)) continue;

                switch (one.Substring(0, split).Trim().ToLowerInvariant())
                {
                    case "head": first[Head] = much; break;
                    case "chest":
                    case "body": first[Chest] = much; break;
                    case "arm":
                    case "arms": first[ArmL] = first[ArmR] = much; break;
                    case "leg":
                    case "legs": first[LegL] = first[LegR] = much; break;
                }
            }

            return first;
        }

        /// <summary>
        /// Сколько человек вмещает — и в какую игровую ручку это положить.
        ///
        /// Зовётся до того, как игра сочтёт свой предел здоровья, и на то есть причина.
        /// Считает она его из «BShp + HPMD», а сразу следом правит по нему текущую жизнь:
        /// предел вырос — доливает разницу, упал — срезает. Подменять итог после неё значило
        /// бы разгребать правку, сделанную не по нашему числу.
        ///
        /// Оттого ничего не подменяем: своё кладётся в «HPMD», и предел она выводит наш.
        /// </summary>
        internal static void Vessel(UnitAttribute unit)
        {
            HumaniodUnit who = unit as HumaniodUnit;

            if (!Counts(who) || who.Data == null) return;

            try
            {
                Body body = Of(who);

                float[] plain = First();
                float[] byLevel = Shares("level", PerLevel);
                float[] byEnd = Shares("end", PerEndurance);

                float grown = Step.Value * Mathf.Max(0, who.Data.level);
                float tough = Step.Value * Mathf.Max(0, who.Endurance);

                // Всё, что игра насчитала сверх своей же простой мерки, остаётся в силе:
                // прибавки к здоровью с вещей, доли от зелий, надбавка за уровень встречи из
                // соседнего мода. Спрашиваем об этом не полосу — полоса уже наша, — а ту
                // самую ручку, из которой полоса считается.
                float bare = (who.info != null ? who.info.BShp : 100f) + grown + tough;

                float theirs = bare > 0f ? Dials.Meant(who) / bare : 1f;
                theirs = Mathf.Clamp(theirs, 0.2f, 20f);

                float[] want = new float[Count];
                float whole = 0f;

                for (int i = 0; i < Count; i++)
                {
                    want[i] = (plain[i] + grown * byLevel[i] + tough * byEnd[i]) * theirs;
                    if (want[i] < 1f) want[i] = 1f;
                    whole += want[i];
                }

                if (whole <= 0f) return;

                if (!body.known)
                {
                    // Впервые видим человека: делим то, что у него есть, по местам в их доле.
                    // Так раненый остаётся раненым, а не выздоравливает от одного нашего взгляда.
                    float full = who.maxhp > 0f ? who.maxhp : whole;
                    float part = Mathf.Clamp01(who.Data.currenthp / full);

                    for (int i = 0; i < Count; i++) body.now[i] = want[i] * part;

                    body.known = true;
                }
                else
                {
                    // Выросло место — выросло и то, что в нём осталось, в прежней доле: уровень
                    // не лечит, но и не отнимает.
                    for (int i = 0; i < Count; i++)
                    {
                        if (body.max[i] > 0f && Mathf.Abs(body.max[i] - want[i]) > 0.001f)
                        {
                            body.now[i] *= want[i] / body.max[i];
                        }

                        body.now[i] = Mathf.Clamp(body.now[i], 0f, want[i]);
                    }
                }

                for (int i = 0; i < Count; i++) body.max[i] = want[i];

                Dials.Holds(who, whole);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог собрать человека по частям: " + e.Message);
            }
        }

        /// <summary>
        /// И что стало с жизнью, покуда игра считала.
        ///
        /// Её правило не отменяется и не разгребается: предел вырос — она долила, упал —
        /// срезала, и это верно, потому что предел теперь наш. Остаётся разложить случившееся
        /// по местам — тем же порядком, каким разносится всякий урон помимо нашей воронки.
        /// </summary>
        internal static void Settle(HumaniodUnit who)
        {
            if (!Counts(who) || who.Data == null) return;

            try
            {
                Body body = Of(who);
                if (!body.known) return;

                float left = 0f;
                for (int i = 0; i < Count; i++) left += body.now[i];

                float now = who.Data.currenthp;

                if (now < left - 0.01f) Spread(body, left - now, false);
                else if (now > left + 0.01f) Spread(body, now - left, true);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Разносит по частям то, что случилось с целым помимо нас.
        ///
        /// Отнимаем — по тому, что в частях осталось; возвращаем — по тому, чего им не
        /// хватает. И то и другое соразмерно: неизвестно откуда взявшийся урон не должен
        /// выбивать руку, а неизвестно откуда взявшееся лечение — воскрешать её одну.
        /// </summary>
        private static void Spread(Body body, float much, bool giving)
        {
            if (body == null || much <= 0f) return;

            float all = 0f;

            for (int i = 0; i < Count; i++)
            {
                all += giving
                    ? Mathf.Max(0f, body.max[i] - body.now[i])
                    : Mathf.Max(0f, body.now[i]);
            }

            if (all <= 0f) return;

            for (int i = 0; i < Count; i++)
            {
                float share = giving
                    ? Mathf.Max(0f, body.max[i] - body.now[i])
                    : Mathf.Max(0f, body.now[i]);

                if (share <= 0f) continue;

                float part = much * (share / all);

                body.now[i] = giving
                    ? Mathf.Min(body.max[i], body.now[i] + part)
                    : Mathf.Max(0f, body.now[i] - part);
            }
        }

        // ----------------------------------------------------------------- куда пришлось

        /// <summary>Левая или правая: с какой стороны бьют, ту и находят.</summary>
        private static bool Left(UnitAttribute who, UnitAttribute target)
        {
            try
            {
                if (who != null && target != null)
                {
                    AttackDirection from = UnitAttribute.GetDirection(target, who);

                    if (from == AttackDirection.left) return true;
                    if (from == AttackDirection.right) return false;
                }
            }
            catch
            {
            }

            return UnityEngine.Random.value < 0.5f;
        }

        /// <summary>The place this blow spends, told from the zone and the side.</summary>
        internal static int Place(int spot, UnitAttribute target, Attack attack)
        {
            UnitAttribute who = attack != null ? attack.attacker : null;

            if (spot == Anatomy.Head) return Head;
            if (spot == Anatomy.Arms) return Left(who, target) ? ArmL : ArmR;
            if (spot == Anatomy.Pants) return Left(who, target) ? LegL : LegR;

            return Chest;
        }

        internal static bool Spent(UnitAttribute who, int part)
        {
            if (!Counts(who)) return false;

            Body body = Of(who);
            return body.known && body.now[part] <= 0f;
        }

        /// <summary>The place that has least left in it, among those still working.</summary>
        internal static int Weakest(UnitAttribute who)
        {
            if (!Counts(who)) return -1;

            Body body = Of(who);
            if (!body.known) return -1;

            int best = -1;
            float least = float.MaxValue;

            for (int i = 0; i < Count; i++)
            {
                if (body.now[i] <= 0f) continue;
                if (body.now[i] < least) { least = body.now[i]; best = i; }
            }

            return best;
        }

        // ----------------------------------------------------------------- удар

        /// <summary>Spends the place that was struck, and answers for what it costs the man.</summary>
        internal static void Hurt(UnitAttribute who, Attack attack, int spot, float dealt)
        {
            if (!Counts(who) || dealt <= 0f || who.Data == null || who.Data.isdead) return;

            try
            {
                Body body = Of(who);
                if (!body.known) return;

                int part = Place(spot, who, attack);

                // По уже перебитому бьют в тело: отнимать у руки больше нечего.
                if (body.now[part] <= 0f && part != Chest && part != Head)
                {
                    part = Chest;
                    dealt *= Spill.Value;
                    if (dealt <= 0f) return;
                }

                // Сила оружия ломает то, что под бронёй. Она снимает долю самого места, а не
                // урона: молоту не нужно разрезать руку, ему довольно её перебить.
                if (attack != null && attack.force > 0f && Force.Value > 0f)
                {
                    dealt += body.max[part] * attack.force * Force.Value;
                }

                // Дробящее бьёт площадью: доля уходит по соседству. Снимается она с самого
                // удара, а не прибавляется сверх, — палица от этого не становится сильнее, она
                // становится шире.
                if (attack != null && Area.Value > 0f && Breach.Crushing(attack))
                {
                    float round = dealt * Area.Value;
                    dealt -= round;

                    int[] near = Near(part);

                    if (near != null && near.Length > 0)
                    {
                        float each = round / near.Length;

                        foreach (int one in near)
                        {
                            body.now[one] = Mathf.Max(0f, body.now[one] - each);
                        }
                    }
                    else
                    {
                        dealt += round;
                    }
                }

                float was = body.now[part];
                body.now[part] = Mathf.Max(0f, was - dealt);

                // Лишнее сверх конечности уходит в тело: удар не останавливается на том, что
                // кончилось.
                float over = dealt - was;
                if (over > 0f && part != Chest && part != Head && Spill.Value > 0f)
                {
                    body.now[Chest] = Mathf.Max(0f, body.now[Chest] - over * Spill.Value);
                }

                float left = 0f;
                for (int i = 0; i < Count; i++) left += body.now[i];

                who.Data.currenthp = Mathf.Clamp(left, 0f, who.maxhp);

                bool spent = was > 0f && body.now[part] <= 0f;

                // И кость под этим местом: дробящее ломает вдвое чаще режущего, а место,
                // разбитое в ноль, ломается всегда.
                Break.Take(who, part, dealt, body.max[part], attack, spent);

                if (spent) Gone(who, part, attack);

                // Перерублена насквозь — отлетит, когда боец упадёт.
                if (spent) Sever.Hewn(who, part, dealt, body.max[part], attack);

                // Смерть — только от этих двух. Рука и нога человека не убивают.
                if (body.now[Chest] <= 0f || body.now[Head] <= 0f)
                {
                    if (Telling.Value)
                    {
                        ItemForgePlugin.Log.LogInfo($"«{who.Data.unitname}» убит: "
                            + $"{(body.now[Head] <= 0f ? "голова" : "туловище")}.");
                    }

                    who.Data.currenthp = 0f;

                    // Куда пришёлся смертельный удар — чтобы тело упало с той раной, что его
                    // убила, а не с угаданной.
                    Sever.Killing(who, part, attack);

                    who.Die(attack != null ? attack.attacker : null);
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог разнести урон по частям: " + e.Message);
            }
        }

        private static void Gone(UnitAttribute who, int part, Attack attack)
        {
            if (part == Chest || part == Head) return;

            if (Telling.Value)
            {
                ItemForgePlugin.Log.LogInfo($"«{who.Data.unitname}»: {Called[part]} выбита.");
            }

            try
            {
                if (who.lifebar != null)
                {
                    who.lifebar.ShowTextTag("<color=#FF6060FF>" + Called[part] + "</color>", 1.5f);
                }
            }
            catch
            {
            }

            // Пересобрать: с этой минуты рука не держит, а нога не несёт.
            //
            // Полным путём, а не одним «WriteUnitAttribute»: тот лишь складывает уже набранное
            // и кладёт в поля, а набирается оно шагом раньше. Позвать его одного — значит
            // сложить дважды.
            try
            {
                HumaniodUnit man = who as HumaniodUnit;
                if (man != null) man.UpdateAttribute();
            }
            catch
            {
            }
        }

        /// <summary>
        /// Чем выбитые ноги мешают ходу.
        ///
        /// Кладётся в счёт шага до того, как игра его сочтёт, а не домножается на готовый:
        /// готовый живёт до ближайшего её пересчёта, а положенное в счёт она посчитает сама.
        /// </summary>
        internal static void Hinders(UnitAttribute unit)
        {
            HumaniodUnit who = unit as HumaniodUnit;

            if (!Counts(who) || who.Data == null) return;

            try
            {
                Body body = Of(who);

                if (body.known)
                {
                    int legs = (body.now[LegL] <= 0f ? 1 : 0) + (body.now[LegR] <= 0f ? 1 : 0);

                    if (legs >= 2)
                    {
                        // Без обеих ног человек ползёт. Не отнимаем ход вовсе: выползти с поля
                        // боя он должен — в лазарет попадают своими силами.
                        Pace.Hinder(who, 1f - Mathf.Clamp01(Crawl.Value));
                    }
                    else if (legs == 1)
                    {
                        Pace.Hinder(who, Mathf.Clamp01(Lame.Value));
                    }
                }

                // И то, что сломано: оно мешает ходу независимо от того, цела ли часть.
                Break.Hinders(who);
            }
            catch
            {
            }
        }

        /// <summary>Что лежит рядом с этим местом: куда расходится удар площадью.</summary>
        private static int[] Near(int part)
        {
            switch (part)
            {
                case Head:  return new[] { Chest };
                case Chest: return new[] { ArmL, ArmR, LegL, LegR };
                case ArmL:  return new[] { Chest };
                case ArmR:  return new[] { Chest };
                case LegL:  return new[] { Chest };
                case LegR:  return new[] { Chest };
                default:    return null;
            }
        }

        // ----------------------------------------------------------------- чем платит

        /// <summary>What the spent limbs take away, applied after the game has counted.</summary>
        internal static void Cripple(HumaniodUnit who)
        {
            if (!Counts(who) || who.Data == null) return;

            try
            {
                Body body = Of(who);
                if (!body.known) return;

                int legs = (body.now[LegL] <= 0f ? 1 : 0) + (body.now[LegR] <= 0f ? 1 : 0);

                // Сам ход считается раньше и в другом месте — здесь только вид: человек на
                // двух перебитых ногах не идёт, а волочится, и видно это должно быть.
                Crawling(who, legs >= 2);

                // И то, что сломано: оно платит своё независимо от того, цела ли часть.
                Break.Bear(who);

                bool leftOut = body.now[ArmL] <= 0f;
                bool rightOut = body.now[ArmR] <= 0f;

                if (!leftOut && !rightOut) return;

                // Правая — рабочая: в ней главное оружие. Левая держит щит или вторую руку.
                Hands(who, leftOut, rightOut);
            }
            catch
            {
            }
        }

        // Кого мы посадили сами: чужое приседание — дело игрока, и снимать его не наше.
        private static readonly HashSet<int> crawling = new HashSet<int>();

        /// <summary>
        /// Ползёт ли он.
        ///
        /// Своей анимации ползка в игре нет, а вот пригнувшийся шаг есть — «walkcrouch», тот
        /// самый, которым крадутся. На двух перебитых ногах он и идёт в дело: со стороны это
        /// и есть человек, который волочётся к своим.
        ///
        /// Сажаем и поднимаем тем же способом, каким игра сажает спутников за старшим: зовём
        /// её собственные «Crouch» и «Uncrouch». И поднимаем только того, кого сами посадили.
        /// </summary>
        private static void Crawling(HumaniodUnit who, bool down)
        {
            try
            {
                int id = who.GetInstanceID();

                if (down)
                {
                    if (who.isCrouching || crawling.Contains(id)) return;

                    crawling.Add(id);
                    who.Invoke("Crouch", 0f);
                }
                else
                {
                    if (!crawling.Remove(id)) return;
                    if (!who.isCrouching) return;

                    who.Invoke("Uncrouch", 0f);
                }
            }
            catch
            {
            }
        }

        private static void Hands(HumaniodUnit who, bool leftOut, bool rightOut)
        {
            float widest = 0f;

            for (int i = 0; i < 2 && who.weapons != null && i < who.weapons.Count; i++)
            {
                Weapon arm = who.weapons[i];
                if (arm == null) continue;

                bool twoHanded = arm.weaponType == WeaponType.twohand
                    || arm.weaponType == WeaponType.polearms
                    || arm.weaponType == WeaponType.range;

                bool shield = arm.weaponType == WeaponType.shield;

                // Двуручное требует обеих рук, одноручное — своей: правая держит главное,
                // левая вторую или щит.
                bool gone = twoHanded
                    ? (leftOut || rightOut)
                    : (i == 0 ? rightOut : leftOut);

                if (!gone)
                {
                    if (!shield && arm.blockAngle > widest) widest = arm.blockAngle;
                    continue;
                }

                // Щит без своей руки падает: держать его нечем.
                if (shield)
                {
                    arm.canAttack = false;
                    Wield.Scale(arm, 0f);
                    continue;
                }

                // Одноручное подхватывает вторая рука — если она цела и свободна. Бьют ею
                // хуже: рука непривычная, треть удара долой. Если же вторая занята щитом или
                // вторым клинком, перекладывать некуда, и оружие падает.
                if (!twoHanded)
                {
                    int other = i == 0 ? 1 : 0;
                    bool otherOut = other == 0 ? rightOut : leftOut;

                    if (otherOut || Busy(who, other))
                    {
                        arm.canAttack = false;
                        Wield.Scale(arm, 0f);
                        continue;
                    }

                    Wield.Scale(arm, Swapped.Value);

                    if (arm.blockAngle > widest) widest = arm.blockAngle;
                    continue;
                }

                // Древковое одной рукой не держат вовсе: алебарду ведут двумя руками на
                // разной высоте древка, и одноручной алебарды не бывает ни в жизни, ни в
                // наборе движений этой игры.
                if (arm.weaponType == WeaponType.polearms && !Polearm.Value)
                {
                    arm.canAttack = false;
                    Wield.Scale(arm, 0f);
                    continue;
                }

                // А прочее двуручное человек перехватывает одной, и вот чего это стоит.
                //
                // Голая цена перехвата велика: четверть удара и четверть скорости — одной
                // рукой двадцатикилограммовую дубину не поднять. Но лишняя сила её выкупает:
                // у кого силы вдвое против того, что вещь спрашивает, тот теряет треть удара
                // и половину скорости. Между этими двумя читается по избытку, и чем его
                // меньше, тем ближе к безнадёжному.
                float over = 0f;

                try
                {
                    EquipInfo slot = who.equipmentmanger != null
                            && who.equipmentmanger.equipInfos != null
                            && i < who.equipmentmanger.equipInfos.Length
                        ? who.equipmentmanger.equipInfos[i] : null;

                    UIWeaponInfo blade = slot != null && slot.IsEquiped() && slot.inventory != null
                        ? slot.inventory.itemInfo as UIWeaponInfo : null;

                    int need = blade != null ? Wield.Asks(blade, 0) : 0;

                    if (need > 0) over = Mathf.Clamp01(who.Strength / (float)need - 1f);
                }
                catch
                {
                }

                float bites = Mathf.Lerp(Clumsy.Value, Handed.Value, over);
                float swings = Mathf.Lerp(ClumsySwing.Value, Swing.Value, over);

                if (bites <= 0f)
                {
                    arm.canAttack = false;
                    Wield.Scale(arm, 0f);
                    continue;
                }

                Wield.Scale(arm, bites);
                arm.attackSpeed *= swings;

                if (arm.blockAngle > widest) widest = arm.blockAngle;
            }

            // Щит держала левая. Нет левой — нет и щита, и в дело вступает блок оружием:
            // своя доля игры, а не наша выдумка. Отнимаем у бойца то, что давал сам щит, и
            // оставляем то, чем он владеет сам.
            if (leftOut)
            {
                UIWeaponInfo shield = Wield.Shield(who);

                if (shield != null)
                {
                    who.block = Mathf.Max(who.info != null ? who.info.BSblock : 0f,
                        who.block - Granted(shield));

                    // Угол блока игра берёт самым широким из того, что в руках. Щит из этого
                    // счёта выбывает, и остаётся угол клинка.
                    who.blockAngle = widest > 0f ? widest : 60f;
                }
            }

            if (who.weapons != null)
            {
                float sum = 0f;
                for (int i = 0; i < who.weapons.Count; i++) sum += who.weapons[i].attackSpeed;
                if (who.weapons.Count > 0) who.attackspeed = sum / who.weapons.Count;
            }
        }

        /// <summary>Занята ли эта рука чем-нибудь.</summary>
        private static bool Busy(HumaniodUnit who, int slot)
        {
            try
            {
                EquipInfo[] kit = who.equipmentmanger != null
                    ? who.equipmentmanger.equipInfos : null;

                if (kit == null || slot < 0 || slot >= kit.Length) return false;

                return kit[slot] != null && kit[slot].IsEquiped();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Сколько блока давала сама эта вещь: это и уходит вместе с рукой.</summary>
        private static float Granted(UIWeaponInfo thing)
        {
            float much = 0f;

            try
            {
                if (thing == null || thing.addAttrs == null) return 0f;

                foreach (AddonAttributes one in thing.addAttrs)
                {
                    if (one == null) continue;
                    if (one.type == AddonAttribute.Defence) much += one.value;
                }
            }
            catch
            {
            }

            return much;
        }

        // ----------------------------------------------------------------- поправка

        /// <summary>Spreads a mending across the places, by what each of them is missing.</summary>
        internal static void Mend(UnitAttribute who, float much)
        {
            if (!Counts(who) || much <= 0f) return;

            try
            {
                Body body = Of(who);
                if (!body.known) return;

                float lacking = 0f;
                for (int i = 0; i < Count; i++) lacking += Mathf.Max(0f, body.max[i] - body.now[i]);

                if (lacking <= 0f) return;

                float given = Mathf.Min(much, lacking);

                for (int i = 0; i < Count; i++)
                {
                    float gap = Mathf.Max(0f, body.max[i] - body.now[i]);
                    if (gap <= 0f) continue;

                    body.now[i] = Mathf.Min(body.max[i], body.now[i] + given * (gap / lacking));
                }

                float left = 0f;
                for (int i = 0; i < Count; i++) left += body.now[i];

                if (who.Data != null) who.Data.currenthp = Mathf.Clamp(left, 0f, who.maxhp);
            }
            catch
            {
            }
        }

        /// <summary>Как человек выглядит по частям — для журнала и для окна.</summary>
        internal static string Tell(UnitAttribute who)
        {
            if (!Counts(who)) return "";

            Body body = Of(who);
            if (!body.known) return "";

            string said = "";

            for (int i = 0; i < Count; i++)
            {
                if (said.Length > 0) said += ", ";
                said += $"{Called[i]} {body.now[i]:0}/{body.max[i]:0}";
            }

            return said;
        }
    }

    // Разложить по местам то, что игра сделала с жизнью, и взять с человека то,
    // чем платят выбитые руки и ноги.
    [HarmonyPatch(typeof(HumaniodUnit), "WriteUnitAttribute")]
    internal static class Write_Limb_Patch
    {
        private static void Postfix(HumaniodUnit __instance)
        {
            try
            {
                Limb.Settle(__instance);
                Limb.Cripple(__instance);
            }
            catch
            {
            }
        }
    }

    // Единственная воронка, через которую в этой игре уходит жизнь.
    //
    // Через неё проходит всё: удар, яд, падение, откат заклинания. Оттого разносим по местам
    // здесь, а не при ударе, — иначе урон помимо оружия шёл бы мимо частей, а при первом же
    // пересчёте полоса возвращалась бы к их сумме, то есть яд лечил бы.
    //
    // Сколько именно ушло, спрашиваем у самой жизни: что было до, что стало после. Поверх
    // расчёта лежат и щиты, и поглощения, и запреты вроде «hpLock», и всё это здесь уже
    // учтено. Место берём то, которое разыграно для нынешнего удара; если жизнь уходит не от
    // удара, платит туловище.
    [HarmonyPatch(typeof(UnitAttribute), "CostHP")]
    internal static class CostHP_Limb_Patch
    {
        private static void Prefix(UnitAttribute __instance, ref float __state)
        {
            __state = -1f;

            try
            {
                if (__instance == null || __instance.Data == null) return;
                if (!Limb.Counts(__instance)) return;

                __state = __instance.Data.currenthp;
            }
            catch
            {
            }
        }

        private static void Postfix(UnitAttribute __instance, float __state)
        {
            if (__state < 0f) return;

            try
            {
                if (__instance == null || __instance.Data == null) return;

                float dealt = __state - __instance.Data.currenthp;
                if (dealt <= 0f) return;

                Attack attack;
                int spot;

                if (!Anatomy.Fresh(__instance, out attack, out spot))
                {
                    attack = null;
                    spot = Anatomy.Chest;
                }

                // Рука платит за удар по себе ещё и силой: она держала оружие.
                if (attack != null) Lethal.Wrist(__instance, attack, spot, dealt);

                Limb.Hurt(__instance, attack, spot, dealt);
            }
            catch
            {
            }
        }
    }

    // Всё, что возвращает здоровье, возвращает его по местам.
    [HarmonyPatch(typeof(UnitAttribute), "RestoreHP")]
    internal static class RestoreHP_Limb_Patch
    {
        private static void Prefix(UnitAttribute __instance, ref float __state)
        {
            __state = -1f;

            try
            {
                // Только те, у кого есть части. Чужому здесь делать нечего: мы бы отобрали у
                // него лечение и не отдали никуда.
                if (__instance != null && __instance.Data != null && Limb.Counts(__instance))
                {
                    __state = __instance.Data.currenthp;
                }
            }
            catch
            {
            }
        }

        private static void Postfix(UnitAttribute __instance, float __state)
        {
            if (__state < 0f) return;

            try
            {
                if (__instance == null || __instance.Data == null) return;

                float given = __instance.Data.currenthp - __state;
                if (given <= 0f) return;

                // Игра уже подняла полосу. Раскладываем это по местам и приводим полосу к
                // сумме: иначе целое и части разойдутся.
                __instance.Data.currenthp = __state;
                Limb.Mend(__instance, given);
            }
            catch
            {
            }
        }
    }
}
