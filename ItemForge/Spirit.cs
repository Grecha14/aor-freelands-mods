using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using spell;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// What a company feels, and what it costs them.
    ///
    /// Мораль в игре почти ничего не решает. Падает она только у голодных, поднимается сама
    /// собой, а наказание за неё такое, что его можно и не заметить: при нуле — три четверти
    /// от точности, разума и воли, и то лишь ниже семидесяти пяти.
    ///
    /// Здесь она становится второй шкалой сытости. Убывает постоянно, сама по себе, и
    /// возвращается только едой — чем лучше стол, тем выше настроение. А наказание бьёт шире:
    /// к точности, разуму и воле добавляется ловкость, и при нуле от них остаётся десятая
    /// часть. Сила и выносливость не трогаются: уныние не делает человека слабее, оно делает
    /// его мажущим.
    ///
    /// Оттого и получается круг. Скверно кормишь — отряд промахивается; промахивается —
    /// дольше лежит; дольше лежит — дольше ест. Повар в этом круге стоит дороже мечника.
    /// </summary>
    internal static class Spirit
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Threshold;
        internal static ConfigEntry<float> Depth;
        internal static ConfigEntry<bool> Wears;
        internal static ConfigEntry<float> PerDay;
        internal static ConfigEntry<bool> Feeds;
        internal static ConfigEntry<string> Tiers;
        internal static ConfigEntry<int> Fresh;
        internal static ConfigEntry<float> Repeat;
        internal static ConfigEntry<bool> Surplus;
        internal static ConfigEntry<float> Grace;
        internal static ConfigEntry<float> Full;
        internal static ConfigEntry<int> Gold;
        internal static ConfigEntry<int> InnDays;
        internal static ConfigEntry<int> FoodTier;
        internal static ConfigEntry<int> FoodDays;
        internal static ConfigEntry<float> Will;
        internal static ConfigEntry<float> Wound;
        internal static ConfigEntry<float> Maimed;
        internal static ConfigEntry<float> Half;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Spirit", "Enabled", true,
                "Make morale matter. Without this it is a number nobody watches.");

            Threshold = config.Bind("Spirit", "Threshold", 75f,
                new ConfigDescription(
                    "Morale above this costs nothing. The line the game itself draws, left alone.",
                    new AcceptableValueRange<float>(1f, 100f)));

            Depth = config.Bind("Spirit", "Depth", 0.90f,
                new ConfigDescription(
                    "How much is taken at morale zero, as a share. Nine tenths of agility, "
                    + "precision, intelligence and willpower. Strength and endurance are never "
                    + "touched: despair does not make a man weaker, it makes him miss.",
                    new AcceptableValueRange<float>(0f, 0.99f)));

            Wears = config.Bind("Spirit", "Wears", true,
                "Let morale fall of its own accord. The game only lowers it for the starving, "
                + "which is why a fed company never thinks about it again.");

            PerDay = config.Bind("Spirit", "PerDay", 10f,
                new ConfigDescription(
                    "Morale lost per day to nothing in particular: the road, the rain, the "
                    + "seventh month away from anywhere. Ten, so a decent meal is needed to hold "
                    + "the line and a good one gains ground.",
                    new AcceptableValueRange<float>(0f, 100f)));

            Feeds = config.Bind("Spirit", "Feeds", true,
                "Let good food lift morale. This is what the daily loss is meant to be paid with.");

            Tiers = config.Bind("Spirit", "Tiers", "0,2,5,9,14,20",
                "Morale a helping gives by the tier of the dish, T0 through T5, before the cook "
                + "is counted. Field rations hold nobody together; a proper table does.");

            Fresh = config.Bind("Spirit", "Fresh", 5,
                new ConfigDescription(
                    "How many recent helpings a tongue remembers. A dish found among them is no "
                    + "longer a treat.",
                    new AcceptableValueRange<int>(0, 50)));

            Repeat = config.Bind("Spirit", "Repeat", 0.34f,
                new ConfigDescription(
                    "What the same dish again is worth, as a share of the first. A third: the "
                    + "bread is still bread, but nobody looks up from the bowl.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Surplus = config.Bind("Spirit", "Surplus", true,
                "Let morale rise past a hundred when the company is genuinely well kept. Three "
                + "things count, and each is worth ten points of morale and a tenth of the "
                + "willpower a man was born with: money in the purse, a good dish lately, a bed "
                + "under a roof lately. All three together make a hundred and thirty.");

            Grace = config.Bind("Spirit", "Grace", 10f,
                new ConfigDescription(
                    "Morale each of the three conditions adds above a hundred.",
                    new AcceptableValueRange<float>(0f, 50f)));

            Full = config.Bind("Spirit", "Full", 90f,
                new ConfigDescription(
                    "Morale a man needs to have of his own before the surplus is laid on top. "
                    + "Nothing else can carry him past a hundred — not a potion, not a feast, "
                    + "not a victory. The surplus is the keeping, not the drink.",
                    new AcceptableValueRange<float>(0f, 100f)));

            Gold = config.Bind("Spirit", "Gold", 30,
                new ConfigDescription(
                    "Gold in the purse the company wants to see. Men who have been paid once "
                    + "sleep better for knowing there is more where it came from.",
                    new AcceptableValueRange<int>(0, 100000)));

            InnDays = config.Bind("Spirit", "InnDays", 2,
                new ConfigDescription(
                    "How long a night at an inn keeps counting.",
                    new AcceptableValueRange<int>(1, 30)));

            FoodTier = config.Bind("Spirit", "FoodTier", 3,
                new ConfigDescription(
                    "The tier a dish must reach to count as a good one. Three.",
                    new AcceptableValueRange<int>(0, 5)));

            FoodDays = config.Bind("Spirit", "FoodDays", 1,
                new ConfigDescription(
                    "How long a good dish keeps counting.",
                    new AcceptableValueRange<int>(1, 30)));

            Will = config.Bind("Spirit", "Will", 0.10f,
                new ConfigDescription(
                    "Willpower each of the three conditions is worth, as a share of what the man "
                    + "was born with. A tenth each, three tenths together.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Wound = config.Bind("Spirit", "Wound", 15f,
                new ConfigDescription(
                    "Morale lost the moment a man takes a lasting injury. A broken rib is bad "
                    + "news whoever hears it.",
                    new AcceptableValueRange<float>(0f, 100f)));

            Maimed = config.Bind("Spirit", "Maimed", 20f,
                new ConfigDescription(
                    "Morale lost when health falls past the line below. Once for the fall, not "
                    + "once a tick: the spring is set again only after he climbs back over it.",
                    new AcceptableValueRange<float>(0f, 100f)));

            Half = config.Bind("Spirit", "Half", 50f,
                new ConfigDescription(
                    "The health at which that blow lands. Half.",
                    new AcceptableValueRange<float>(0f, 100f)));
        }

        // ------------------------------------------------------------------ раны

        /// <summary>A man has just been maimed.</summary>
        internal static void Wounded(UnitAttribute who, string what)
        {
            if (!Enabled.Value || Wound.Value <= 0f || who == null || who.Data == null) return;
            if (!Ours(who)) return;

            NPCSaveData mind = who.Data as NPCSaveData;
            if (mind == null) return;

            mind.AddMorale(0f - Wound.Value);

            ItemForgePlugin.Log.LogInfo(who.Data.unitname + " получил увечье («" + what
                + "»): настроение −" + Wound.Value.ToString("0.#") + ".");
        }

        // У кого шкала уже провалена. Хранится, чтобы удар был один на падение, а не каждый
        // раз, когда здоровье шевельнулось ниже половины.
        private static readonly HashSet<int> low = new HashSet<int>();

        /// <summary>Health has moved; sees whether it has crossed the line.</summary>
        internal static void Bled(HumaniodUnit who)
        {
            if (!Enabled.Value || who == null || who.Data == null) return;

            try
            {
                int id = who.Data.id;
                bool under = who.Data.health < Half.Value;

                if (!under)
                {
                    low.Remove(id);
                    return;
                }

                if (!low.Add(id)) return;
                if (Maimed.Value <= 0f || !Ours(who)) return;

                who.Data.AddMorale(0f - Maimed.Value);

                ItemForgePlugin.Log.LogInfo(who.Data.unitname + " истёк до половины здоровья: "
                    + "настроение −" + Maimed.Value.ToString("0.#") + ".");
            }
            catch
            {
            }
        }

        private static bool Ours(UnitAttribute who)
        {
            HumaniodUnit man = who as HumaniodUnit;
            if (man == null) return false;

            return man.inParty || (object)man == (object)gameManager.mainCharUnit;
        }

        // ------------------------------------------------------------------ сверх сотни

        // Когда отряд последний раз ночевал под крышей, и когда каждый последний раз ел
        // по-человечески. Держится в памяти запуска: потеряется при перезагрузке — вернётся с
        // первой же ночёвкой и первым же обедом, а заводить ради этого файл дороже.
        private static int slept = int.MinValue;
        private static readonly Dictionary<int, int> dined = new Dictionary<int, int>();
        private static readonly Dictionary<int, float> given = new Dictionary<int, float>();

        /// <summary>The party has slept under a roof.</summary>
        internal static void Rested()
        {
            try { slept = TimeManager.TotalDay; }
            catch { }
        }

        /// <summary>How many of the three conditions are being kept.</summary>
        internal static int Kept(UnitAttribute who)
        {
            if (!Enabled.Value || !Surplus.Value || who == null || who.Data == null) return 0;

            int met = 0;

            try
            {
                int today = TimeManager.TotalDay;

                if (ManagementModeCore.wealth / 10000 >= Gold.Value) met++;
                if (today - slept < InnDays.Value) met++;

                int ate;
                if (dined.TryGetValue(who.Data.id, out ate) && today - ate < FoodDays.Value) met++;
            }
            catch
            {
                return 0;
            }

            return met;
        }

        /// <summary>Lays the surplus on a full bar, or takes back what is no longer earned.</summary>
        internal static void Bless(HumaniodUnit who)
        {
            if (!Enabled.Value || !Surplus.Value || who == null || who.Data == null) return;

            try
            {
                int id = who.Data.id;

                float had;
                if (!given.TryGetValue(id, out had)) had = 0f;

                // Своя часть шкалы — то, что осталось бы без нашей надбавки.
                float own = Mathf.Clamp(who.Data.morale - had, 0f, 100f);

                float want = own >= Full.Value ? Grace.Value * Kept(who) : 0f;

                if (Mathf.Abs(want - had) < 0.01f) return;

                // Пишем в поле напрямую. Через «AddMorale» выше сотни не пройти, и это
                // правильно: ни зелье, ни пир, ни победа туда не поднимут — только уход.
                who.Data.morale = own + want;
                given[id] = want;

                Lift(who);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог посчитать приподнятость: " + e.Message);
            }
        }

        private static UIBuffInfo uplift;
        private static bool cast;

        /// <summary>Hangs the good spirits on a man, or takes them off.</summary>
        internal static void Lift(HumaniodUnit who)
        {
            if (who == null || who.Data == null || who.buffmanger == null) return;

            try
            {
                int met = Enabled.Value && Surplus.Value ? Kept(who) : 0;

                float own = who.Data.morale;
                float had;
                if (given.TryGetValue(who.Data.id, out had)) own -= had;

                if (met <= 0 || Will.Value <= 0f || own < Full.Value)
                {
                    who.buffmanger.RemoveBuff("HighSpirits");
                    return;
                }

                UIBuffInfo mark = Uplift();
                if (mark == null) return;

                int more = Mathf.Max(1,
                    (int)(Will.Value * met * who.Data.humanAttribute.BSwillpower));

                BuffBase joy = new BuffBase(mark);
                joy.addAttrs.Clear();
                joy.addAttrs.Add(new AddonAttributes(AddonAttribute.Willpower, more));

                who.buffmanger.AddBuff(joy);
            }
            catch
            {
            }
        }

        // Своя карточка, а не чужая. «NPCAttributeBuff» игра переписывает сама при всяком
        // пересчёте уровня, и наша прибавка исчезала бы следом.
        private static UIBuffInfo Uplift()
        {
            if (cast) return uplift;
            cast = true;

            try
            {
                UIBuffInfo basis = UIBuffDatabase.Instance.GetByID("NPCAttributeBuff");
                if (basis == null) return null;

                uplift = UnityEngine.Object.Instantiate(basis);
                uplift.id = "HighSpirits";
                uplift.name = "HighSpirits";
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог завести карточку приподнятости: " + e.Message);
            }

            return uplift;
        }

        /// <summary>Hangs the despair on a man, or takes it off him.</summary>
        internal static bool Sink(HumaniodUnit who)
        {
            if (!Enabled.Value || who == null || who.Data == null) return false;
            if (who.buffmanger == null || UIBuffDatabase.Instance == null) return false;

            try
            {
                float line = Threshold.Value;

                if (who.Data.morale >= line)
                {
                    who.buffmanger.RemoveBuff("MentalHealthDebuff");
                    return true;
                }

                // Доля берётся от самого порога, а не от сотни. У игры делится на сто, и
                // оттого при нуле морали она снимает три четверти вместо объявленной глубины:
                // до дна шкалы расчёт просто не доходит.
                float share = (line - who.Data.morale) / Mathf.Max(1f, line) * Depth.Value;

                BuffBase grief = new BuffBase(UIBuffDatabase.Instance.GetByID("MentalHealthDebuff"));
                grief.addAttrs.Clear();

                Cut(grief, AddonAttribute.Agility, who.Data.humanAttribute.BSagility, share);
                Cut(grief, AddonAttribute.Precision, who.Data.humanAttribute.BSprecision, share);
                Cut(grief, AddonAttribute.Intelligence, who.Data.humanAttribute.BSintelligence, share);
                Cut(grief, AddonAttribute.Willpower, who.Data.humanAttribute.BSwillpower, share);

                who.buffmanger.AddBuff(grief);
                return true;
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог посчитать уныние: " + e.Message);
                return false;
            }
        }

        private static void Cut(BuffBase grief, AddonAttribute which, int had, float share)
        {
            grief.addAttrs.Add(new AddonAttributes(which,
                Mathf.Clamp((int)((0f - share) * (float)had), -100, -1)));
        }

        /// <summary>The slow leak, paid by the hour.</summary>
        internal static void Wear(HumaniodUnit who, float hourPassed)
        {
            if (!Enabled.Value || !Wears.Value || PerDay.Value <= 0f) return;
            if (who == null || who.Data == null || hourPassed <= 0f) return;
            if (who.Data.morale <= 0f) return;

            // Человек, чьё желание никто не исполнил, тоскует вдвое быстрее.
            float much = PerDay.Value * Craving.Burden(who);

            who.Data.AddMorale(0f - much / 24f * hourPassed);
        }

        /// <summary>What a helping does for the heart.</summary>
        internal static void Fed(UnitAttribute who, UIItemInfo dish, bool again)
        {
            if (!Enabled.Value || !Feeds.Value || who == null || dish == null) return;

            NPCSaveData mind = who.Data as NPCSaveData;
            if (mind == null) return;

            try
            {
                // Испорченное не радует ничем: за него уже заплачено болезнью.
                if (Larder.Spoiled(dish)) return;

                // Хороший стол засчитывается отдельно от настроения: он одно из трёх условий,
                // на которых шкала поднимается выше сотни.
                if ((int)dish.tier >= FoodTier.Value) dined[who.Data.id] = TimeManager.TotalDay;

                float gift = Step((int)dish.tier);
                if (gift <= 0f) return;

                if (again) gift *= Repeat.Value;

                // Рука повара считается той же лестницей, что и польза еды: одно умение —
                // один счёт, и качать его стоит ради всего сразу.
                if (Larder.Enriches.Value) gift *= Larder.Worth(Larder.Cook());

                if (gift < 0.5f) return;

                mind.AddMorale(gift);

                ItemForgePlugin.Log.LogInfo("«" + who.Data.unitname + "» поел «" + dish.Name
                    + "» (" + dish.tier + (again ? ", не впервой" : "") + "): настроение +"
                    + gift.ToString("0.#") + ".");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог порадовать едой: " + e.Message);
            }
        }

        private static float[] steps;
        private static string stepsRead;

        private static float Step(int tier)
        {
            string written = Tiers.Value ?? "";

            if (written != stepsRead)
            {
                stepsRead = written;

                List<float> got = new List<float>();
                foreach (string one in written.Split(','))
                {
                    float much;
                    if (float.TryParse(one.Trim(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out much))
                    {
                        got.Add(much);
                    }
                }

                steps = got.ToArray();
            }

            if (steps == null || steps.Length == 0) return 0f;
            if (tier < 0) tier = 0;
            if (tier >= steps.Length) tier = steps.Length - 1;

            return steps[tier];
        }
    }

    // Игра пересчитывает уныние сама при всяком изменении морали. Встаём вместо неё целиком:
    // дописывать поверх значило бы снять и повесить буф заново, то есть тот же расчёт дважды.
    [HarmonyPatch(typeof(HumaniodUnit), "AddMoraleDebuff")]
    internal static class AddMoraleDebuff_Patch
    {
        private static bool Prefix(HumaniodUnit __instance)
        {
            // Приподнятость считается там же, где уныние: оба висят на одной шкале, и
            // пересчитывать их порознь значило бы разойтись.
            try { Spirit.Lift(__instance); }
            catch { }

            // Не справились — пусть считает игра, как считала.
            return !Spirit.Sink(__instance);
        }
    }

    // Увечье вешается сюда же, куда и всё прочее. Считаем только по-настоящему новое: игра
    // зовёт этот метод и чтобы продлить уже висящее, а от второго напоминания о сломанном
    // ребре настроение падать не должно.
    [HarmonyPatch(typeof(BuffManager), "AddBuff", new Type[] { typeof(BuffBase) })]
    internal static class AddBuff_Spirit_Patch
    {
        private static void Prefix(BuffManager __instance, BuffBase buff)
        {
            try
            {
                if (__instance == null || buff == null || buff.buffInfo == null) return;
                if (buff.buffInfo.type != bufftype.injury) return;
                if (__instance.FindBuffbyID(buff.buffInfo.id) != null) return;

                Spirit.Wounded(__instance.unit, buff.buffInfo.id);
            }
            catch { }
        }
    }

    // Всякое движение шкалы здоровья проходит здесь, и здесь же видно, когда она перевалила
    // за половину вниз.
    [HarmonyPatch(typeof(HumaniodUnit), "OnHeathChange")]
    internal static class OnHeathChange_Spirit_Patch
    {
        private static void Postfix(HumaniodUnit __instance)
        {
            try { Spirit.Bled(__instance); }
            catch { }
        }
    }

    // Час отряда. Сюда игра приходит только для тех, кто идёт с героем: караванщиков и
    // городских эта убыль не касается, и правильно — они не в походе.
    [HarmonyPatch(typeof(HumaniodUnit), "TickHealthAndMorale")]
    internal static class Tick_Spirit_Patch
    {
        private static void Postfix(HumaniodUnit __instance, float hourPassed)
        {
            try { Spirit.Wear(__instance, hourPassed); }
            catch { }

            try { Spirit.Bless(__instance); }
            catch { }
        }
    }
}
