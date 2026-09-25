using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using spell;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Broken bones: the wound that outlives the battle.
    ///
    /// Рана в этой игре кончается вместе с полосой здоровья: выпил зелье — и цел. Перелома нет
    /// вовсе, хотя именно он, а не порез, решал судьбу человека во всякой настоящей драке:
    /// рука с перебитой костью не держит меч ни через минуту, ни через неделю.
    ///
    /// Здесь у каждой части тела может быть свой перелом трёх видов. Лёгкий — трещина, с ней
    /// дерутся. Сильный — кость перебита, и часть эта работает вполсилы. Третий вид — не
    /// беда, а её лечение: перелом, за который взялись, ещё мешает, но уже срастается.
    ///
    /// Ломает то, что бьёт тяжело: дробящее ломает вдвое чаще режущего, а сила оружия ломает
    /// сама по себе. Часть, разбитая в ноль, ломается всегда.
    ///
    /// Перелом режет то, чем эта часть работает. Рука — удар и скорость руки. Нога — ход и
    /// уворот. Туловище — ход наполовину: со сломанными рёбрами не бегают. Голова — нокаут до
    /// конца боя, и лечить его надо руками, само не пройдёт.
    ///
    /// Само оно и не проходит: перелом снимается только лечением — аптечкой в поле или врачом
    /// после боя, — и переживает и бой, и выход из игры.
    /// </summary>
    internal static class Break
    {
        internal const byte None = 0;
        internal const byte Light = 1;
        internal const byte Bad = 2;
        internal const byte Healing = 3;

        internal static readonly string[] Named = { "", "трещина", "перелом", "срастается" };

        // Ломается не часть тела, а кость под ней: у груди это рёбра, у головы череп. Рука и
        // нога зовутся собой — своего имени у их костей в обиходе нет.
        internal static readonly string[] Bones =
        {
            "череп", "рёбра", "левая рука", "правая рука", "левая нога", "правая нога"
        };

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Chance;
        internal static ConfigEntry<float> Crush;
        internal static ConfigEntry<bool> Blunt;
        internal static ConfigEntry<bool> Handled;
        internal static ConfigEntry<float> PerForce;
        internal static ConfigEntry<float> Grave;
        internal static ConfigEntry<float> LightCost;
        internal static ConfigEntry<float> BadCost;
        internal static ConfigEntry<float> SlowLight;
        internal static ConfigEntry<float> SlowBad;
        internal static ConfigEntry<float> Hours;
        internal static ConfigEntry<float> PerMedic;
        internal static ConfigEntry<bool> Shown;
        internal static ConfigEntry<string> Shape;
        internal static ConfigEntry<bool> Telling;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Break", "Enabled", true,
                "Let bones break. A wound in this game ends with the health bar; a broken arm "
                + "does not, and that is the difference between a scratch and a battle a man "
                + "remembers.");

            Chance = config.Bind("Break", "Chance", 0.35f,
                new ConfigDescription(
                    "How readily a blow breaks what it lands on, reckoned against the share of "
                    + "that place it took away. A blow taking a third of an arm breaks it about "
                    + "one time in nine.",
                    new AcceptableValueRange<float>(0f, 2f)));

            Crush = config.Bind("Break", "Crush", 2f,
                new ConfigDescription(
                    "How much likelier crushing is to break than cutting, where cutting breaks "
                    + "at all. Twice: that is what a mace is for. A blade cuts the meat and "
                    + "leaves the bone.",
                    new AcceptableValueRange<float>(1f, 5f)));

            Blunt = config.Bind("Break", "Blunt", true,
                "Let nothing but crushing break a bone. A blade parts flesh and a point goes "
                + "between the ribs; neither of them snaps anything, and a wound from either is "
                + "a wound that closes. The hammer, the mace and the flail are carried for this "
                + "one purpose and should keep it to themselves.");

            Handled = config.Bind("Break", "Handled", true,
                "And let only a weapon do it. A wolf's jaws close on an arm and tear it; they "
                + "do not break it, whatever the beast weighs. What a beast does is done under "
                + "Limb, where the arm is spent in the ordinary way.");

            PerForce = config.Bind("Break", "PerForce", 0.00005f,
                new ConfigDescription(
                    "And what one point of the weapon's force adds to that chance by itself, "
                    + "whatever it did to the flesh. Force is the weight swinging, from about a "
                    + "hundred for a plain sword to fourteen hundred for the heaviest maul, so "
                    + "at a twenty-thousandth the sword adds half a percent and the maul seven.",
                    new AcceptableValueRange<float>(0f, 0.01f)));

            Grave = config.Bind("Break", "Grave", 0.4f,
                new ConfigDescription(
                    "The share of a place one blow must take for the break to be a bad one "
                    + "rather than a crack. A place spent to nothing is always a bad one.",
                    new AcceptableValueRange<float>(0f, 1f)));

            LightCost = config.Bind("Break", "LightCost", 0.25f,
                new ConfigDescription(
                    "What a crack takes off the work of its own place, as a share. A quarter.",
                    new AcceptableValueRange<float>(0f, 1f)));

            BadCost = config.Bind("Break", "BadCost", 0.5f,
                new ConfigDescription(
                    "And what a true break takes. A half — which for the body is the half of a "
                    + "man's pace, since one does not run with broken ribs.",
                    new AcceptableValueRange<float>(0f, 1f)));

            SlowLight = config.Bind("Break", "SlowLight", 0.25f,
                new ConfigDescription(
                    "What a cracked arm takes off how fast that hand swings, as a share. A "
                    + "quarter — the same it takes off the blow: a cracked bone hurts at every "
                    + "movement, and the hand goes carefully.",
                    new AcceptableValueRange<float>(0f, 1f)));

            SlowBad = config.Bind("Break", "SlowBad", 0.75f,
                new ConfigDescription(
                    "And a broken one. Three quarters, against a half off the blow: a broken arm "
                    + "still carries the weight of the weapon down, but bringing it back up is "
                    + "another matter entirely.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Hours = config.Bind("Break", "Hours", 168f,
                new ConfigDescription(
                    "Game hours a break needs to knit once it has been set. A week, and a crack "
                    + "half of that; a good physician shortens both.\n\n"
                    + "The week is not a wait but a mending: from the hour the bone is set its "
                    + "hindrance falls away evenly, from the whole of it to nothing. A man whose "
                    + "arm was set yesterday fights nearly as badly as one with it broken; by "
                    + "the sixth day he hardly notices it.",
                    new AcceptableValueRange<float>(1f, 1000f)));

            PerMedic = config.Bind("Break", "PerMedic", 0.02f,
                new ConfigDescription(
                    "What one point of medicine takes off that time, as a share. At twenty five "
                    + "points a break knits in half the time.",
                    new AcceptableValueRange<float>(0f, 0.05f)));

            Shown = config.Bind("Break", "Shown", true,
                "Hang a broken bone among the other afflictions, where the overload mark and "
                + "the marks of a weapon too heavy already hang. A break is an injury and "
                + "should be read where injuries are read, not only in a line of the character "
                + "window.");

            Shape = config.Bind("Break", "Shape", "",
                "Which existing buff the mark borrows its picture from, by id. Left empty it "
                + "borrows the game's own first injury, which is exactly the right picture for "
                + "a broken bone.");

            Telling = config.Bind("Break", "Telling", true,
                "Write down every bone broken and every one set.");
        }

        // ----------------------------------------------------------------- что где сломано

        private sealed class Hurt
        {
            internal readonly byte[] state = new byte[Limb.Count];
            internal readonly float[] left = new float[Limb.Count];

            // Весь срок сращивания и то, чем эта кость была до лубка: по ним и считается,
            // насколько она мешает сегодня.
            internal readonly float[] full = new float[Limb.Count];
            internal readonly byte[] was = new byte[Limb.Count];
        }

        private static readonly Dictionary<int, Hurt> broken = new Dictionary<int, Hurt>();

        // Какое сохранение сейчас на руках и что в книге писано про чужие.
        //
        // Номер существа в этой игре неповторим только внутри одного мира: в другом под
        // единицей ходит другой человек. Книга на один номер была книгой на все миры разом —
        // новая игра начиналась с чужими переломами, и первый, кто их получал, был игрок,
        // потому что игрок всегда первый.
        private static string read;
        private static readonly List<string> strangers = new List<string>();
        private static bool dirty;
        private static float next;

        private static string Ledger
        {
            get { return Path.Combine(Paths.ConfigPath, "aor.breaks.tsv"); }
        }

        private static Hurt Of(UnitAttribute who, bool make)
        {
            if (who == null || who.Data == null) return null;

            Load();

            int id = who.Data.id;

            Hurt mine;
            if (broken.TryGetValue(id, out mine)) return mine;

            if (!make) return null;

            mine = new Hurt();
            broken[id] = mine;
            return mine;
        }

        internal static byte State(UnitAttribute who, int part)
        {
            if (Enabled == null || !Enabled.Value) return None;

            Hurt mine = Of(who, false);
            return mine != null ? mine.state[part] : None;
        }

        /// <summary>
        /// Сколько от полной помехи осталось сегодня.
        ///
        /// Покуда кость просто сломана — вся. С той минуты, как её взяли в лубок, помеха тает
        /// ровно: к последнему часу срока от неё не остаётся ничего. Неделя — это не ожидание,
        /// а само выздоровление.
        /// </summary>
        private static float Share(Hurt mine, int part)
        {
            if (mine == null || mine.state[part] != Healing) return 1f;
            if (mine.full[part] <= 0f) return 0f;

            return Mathf.Clamp01(mine.left[part] / mine.full[part]);
        }

        /// <summary>Чем кость была до лубка — от этого и считается полная помеха.</summary>
        private static float Severity(Hurt mine, int part, bool swing)
        {
            byte state = mine.state[part];
            if (state == Healing) state = mine.was[part];

            switch (state)
            {
                case Light: return swing ? SlowLight.Value : LightCost.Value;
                case Bad: return swing ? SlowBad.Value : BadCost.Value;
                default: return 0f;
            }
        }

        /// <summary>What a break at this place takes off its own work.</summary>
        internal static float Cost(UnitAttribute who, int part)
        {
            if (Enabled == null || !Enabled.Value) return 0f;

            Hurt mine = Of(who, false);
            if (mine == null || mine.state[part] == None) return 0f;

            return Severity(mine, part, false) * Share(mine, part);
        }

        /// <summary>
        /// И что он отдельно берёт со скорости руки.
        ///
        /// Со скорости берётся больше, чем с удара, и берётся непропорционально: трещина
        /// стоит четверти того и другого, а перебитая кость — половины удара и трёх четвертей
        /// замаха. Вес оружия она вниз ещё проведёт, а вот обратно поднимет едва.
        /// </summary>
        internal static float Slow(UnitAttribute who, int part)
        {
            if (Enabled == null || !Enabled.Value) return 0f;

            Hurt mine = Of(who, false);
            if (mine == null || mine.state[part] == None) return 0f;

            return Severity(mine, part, true) * Share(mine, part);
        }

        // ----------------------------------------------------------------- как ломается

        /// <summary>Rolls whether this blow broke the bone under it.</summary>
        internal static void Take(UnitAttribute who, int part, float dealt, float whole,
            Attack attack, bool spent)
        {
            if (Enabled == null || !Enabled.Value || who == null || who.Data == null) return;
            if (dealt <= 0f || whole <= 0f) return;

            try
            {
                Hurt mine = Of(who, true);
                if (mine == null) return;

                // Хуже сильного уже не бывает, а лечимое ломать заново — значит лечить сначала.
                if (mine.state[part] == Bad) return;

                // Ломает дробящее и только оно: клинок разрезает мясо и оставляет кость,
                // остриё проходит между рёбер. И ломает оружие, а не зубы: волчьи челюсти руку
                // рвут, а не перебивают, сколько бы зверь ни весил.
                bool crushing = attack != null && Breach.Crushing(attack);
                bool handled = attack != null && attack.attacker is HumaniodUnit;

                if (Blunt.Value && !crushing) return;
                if (Handled.Value && !handled) return;

                float share = Mathf.Clamp01(dealt / whole);

                float odds = share * Chance.Value;

                if (attack != null)
                {
                    if (crushing) odds *= Crush.Value;
                    if (attack.force > 0f) odds += attack.force * PerForce.Value;
                }

                bool worst = spent || share >= Grave.Value;

                if (!worst && UnityEngine.Random.value > odds) return;

                byte was = mine.state[part];
                mine.state[part] = worst ? Bad : Light;

                if (mine.state[part] == was) return;

                mine.left[part] = 0f;
                dirty = true;

                Say(who, part, mine.state[part]);

                // Пересобрать: сломанное с этой минуты работает хуже.
                HumaniodUnit man = who as HumaniodUnit;
                if (man != null) man.UpdateAttribute();
            }
            catch
            {
            }
        }

        private static void Say(UnitAttribute who, int part, byte state)
        {
            if (!Telling.Value || who.Data == null) return;

            ItemForgePlugin.Log.LogInfo($"«{who.Data.unitname}»: {Bones[part]} — "
                + Named[state] + ".");

            try
            {
                if (who.lifebar != null)
                {
                    // Трещина оранжевым, перелом красным — той же мерой, какой покрашены
                    // знаки недобора и строка тела в окне.
                    string paint = state == Bad ? "#FF6060FF" : "#FFB040FF";

                    who.lifebar.ShowTextTag("<color=" + paint + ">" + Bones[part] + ": "
                        + Named[state] + "</color>", 1.5f);
                }
            }
            catch
            {
            }
        }

        // ----------------------------------------------------------------- чем платит

        // ----------------------------------------------------------------- знак недуга

        private static readonly Dictionary<string, UIBuffInfo> shapes =
            new Dictionary<string, UIBuffInfo>(StringComparer.Ordinal);

        /// <summary>
        /// Наш собственный недуг, носящий чужую картинку.
        ///
        /// Целиком чужую заготовку взять нельзя: вместе со значком придут её имя и слова, и
        /// человек с перебитой рукой прочтёт про чужую беду. Копируем заготовку, а имя и слова
        /// пишем свои. Картинку берём у игровой травмы — для сломанной кости это она и есть.
        /// </summary>
        private static UIBuffInfo Sign(int part, byte state)
        {
            string key = part + ":" + state;

            UIBuffInfo kept;
            if (shapes.TryGetValue(key, out kept) && kept != null) return kept;

            try
            {
                UIBuffDatabase book = UIBuffDatabase.Instance;
                if (book == null) return null;

                UIBuffInfo from = null;

                string asked = Shape.Value ?? "";
                if (asked.Length > 0) from = book.GetByID(asked);

                if (from == null && book.injuryBuffs != null && book.injuryBuffs.Count > 0)
                {
                    from = book.injuryBuffs[0];
                }

                if (from == null) return null;

                UIBuffInfo copy = UnityEngine.Object.Instantiate(from);
                UnityEngine.Object.DontDestroyOnLoad(copy);

                copy.id = "ItemForgeBreak_" + part + "_" + state;
                copy.name = copy.id;

                // Бессрочно — и написано это в самом образце, а не на готовом знаке: срок
                // берётся отсюда при каждом наложении, и правка на готовом терялась. Оттого
                // знак истекал, вешался заново, значок мигал, а подсказку над ним нельзя
                // было дочитать.
                copy.duration = -1f;
                copy.durationIsHour = false;
                copy.showDuration = false;
                copy.buffname = Bones[part].Initcap() + ": " + Named[state];

                copy.description = state == Healing
                    ? "Кость взята в лубок и срастается. Помеха тает с каждым часом и сойдёт "
                        + "на нет через неделю, а у хорошего лекаря раньше."
                    : (state == Bad
                        ? (part == Limb.Chest
                            ? "Рёбра перебиты. Со сломанными рёбрами не бегают: ход вдвое "
                                + "короче, и удар вполсилы. Сами они не срастутся — нужен "
                                + "лубок, аптечкой в поле или у лекаря."
                            : "Кость перебита. Эта часть тела работает вполсилы, и сама она "
                                + "не срастётся: нужен лубок — аптечкой в поле или у лекаря.")
                        : "Кость треснула. Работает четвертью хуже и сама не срастётся: "
                            + "нужен лубок — аптечкой в поле или у лекаря.");

                shapes[key] = copy;

                return copy;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Hangs a mark for every bone broken, and takes down the ones that mended.</summary>
        private static void Marks(HumaniodUnit who, Hurt mine)
        {
            if (Shown == null || !Shown.Value || who == null || who.buffmanger == null) return;

            try
            {
                for (int i = 0; i < Limb.Count; i++)
                {
                    byte state = mine.state[i];

                    for (byte kind = Light; kind <= Healing; kind++)
                    {
                        string id = "ItemForgeBreak_" + i + "_" + kind;

                        if (kind == state)
                        {
                            if (who.buffmanger.ContainBuff(id)) continue;

                            UIBuffInfo shape = Sign(i, kind);
                            if (shape == null) continue;

                            BuffBase mark = new BuffBase(shape, (UnitAttribute)(object)who);

                            // Чужие приписки нам ни к чему: значок берём, а что перелом делает,
                            // считается здесь же, своим счётом.
                            mark.addAttrs.Clear();

                            // И срока у него нет: кость не рассасывается сама, а знак с чужим
                            // сроком истекал бы и вешался заново — значок мигал, а подсказка
                            // над ним закрывалась, не дав себя дочитать.
                            mark.duration = -1f;

                            who.buffmanger.AddBuff(mark);
                        }
                        else if (who.buffmanger.ContainBuff(id))
                        {
                            who.buffmanger.RemoveBuff(id);
                        }
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>What the broken bones take away, applied after the game has counted.</summary>
        internal static void Bear(HumaniodUnit who)
        {
            if (Enabled == null || !Enabled.Value || who == null) return;

            try
            {
                Hurt mine = Of(who, false);
                if (mine == null) return;

                Marks(who, mine);

                // Голова. Разбитая голова — это не штраф, это конец боя для неё: боец лежит,
                // покуда его не подберут.
                if (mine.state[Limb.Head] == Bad)
                {
                    Knocked(who);
                    return;
                }

                float head = Cost(who, Limb.Head);

                if (head > 0f)
                {
                    who.attack *= 1f - head;
                    who.dodge *= 1f - head;
                }

                // Туловище: со сломанными рёбрами не бьют в полную силу. Ход считается не
                // здесь — он весь собран в одном счёте, до того как игра его выведет.
                float chest = Cost(who, Limb.Chest);

                if (chest > 0f && who.weapons != null)
                {
                    foreach (Weapon arm in who.weapons) Wield.Scale(arm, 1f - chest * 0.5f);
                }

                // Ноги: уворот держат обе.
                float legs = Mathf.Max(Cost(who, Limb.LegL), Cost(who, Limb.LegR));

                if (legs > 0f) who.dodge *= 1f - legs;

                // Руки: своя рука — своё оружие.
                Hand(who, 0, Cost(who, Limb.ArmR), Slow(who, Limb.ArmR));
                Hand(who, 1, Cost(who, Limb.ArmL), Slow(who, Limb.ArmL));
            }
            catch
            {
            }
        }

        /// <summary>
        /// Чем сломанное мешает ходу — в общий счёт шага.
        ///
        /// Разбитая голова кладёт бойца вовсе: девяносто восемь сотых долой, чтобы он не
        /// уполз, но и не примёрз к месту.
        /// </summary>
        internal static void Hinders(UnitAttribute who)
        {
            if (Enabled == null || !Enabled.Value || who == null) return;

            try
            {
                Hurt mine = Of(who, false);
                if (mine == null) return;

                if (mine.state[Limb.Head] == Bad)
                {
                    Pace.Hinder(who, 0.98f);
                    return;
                }

                float chest = Cost(who, Limb.Chest);
                if (chest > 0f) Pace.Hinder(who, chest);

                float both = (Cost(who, Limb.LegL) + Cost(who, Limb.LegR)) * 0.5f;
                if (both > 0f) Pace.Hinder(who, both);
            }
            catch
            {
            }
        }

        private static void Hand(HumaniodUnit who, int which, float cost, float slow)
        {
            if (cost <= 0f || who.weapons == null || which >= who.weapons.Count) return;

            Weapon arm = who.weapons[which];
            if (arm == null) return;

            Wield.Scale(arm, 1f - cost);
            arm.attackSpeed *= Mathf.Max(0.05f, 1f - slow);
        }

        private static void Knocked(HumaniodUnit who)
        {
            try
            {
                if (who.weapons != null)
                {
                    foreach (Weapon arm in who.weapons)
                    {
                        if (arm == null) continue;

                        arm.canAttack = false;
                        Wield.Scale(arm, 0f);
                    }
                }

                who.block = 0f;
                who.dodge = 0f;

                if (!who.isKnockdown)
                {
                    who.isKnockdown = true;

                    if (who.ani != null)
                    {
                        who.ani.SetTrigger("knockdown");
                        who.ani.SetBool("isKnockdown", true);
                    }
                }
            }
            catch
            {
            }
        }

        // ----------------------------------------------------------------- лечение

        /// <summary>Sets a bone: it still hurts, but from now on it is knitting.</summary>
        internal static bool Treat(UnitAttribute who, int part, int skill)
        {
            if (Enabled == null || !Enabled.Value) return false;

            try
            {
                Hurt mine = Of(who, false);
                if (mine == null) return false;
                if (mine.state[part] != Light && mine.state[part] != Bad) return false;

                float hours = Hours.Value * Mathf.Clamp(1f - skill * PerMedic.Value, 0.1f, 1f);
                if (mine.state[part] == Light) hours *= 0.5f;

                mine.was[part] = mine.state[part];
                mine.state[part] = Healing;
                mine.left[part] = hours;
                mine.full[part] = hours;
                dirty = true;

                if (Telling.Value && who.Data != null)
                {
                    ItemForgePlugin.Log.LogInfo($"«{who.Data.unitname}»: {Limb.Called[part]} "
                        + $"взята в лубок, срастётся за {hours:0} часов.");
                }

                HumaniodUnit man = who as HumaniodUnit;
                if (man != null) man.UpdateAttribute();

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Худшее, что с этим человеком сейчас: целость, трещина или перелом.</summary>
        internal static byte Gravest(UnitAttribute who)
        {
            Hurt mine = Of(who, false);
            if (mine == null) return None;

            byte worst = None;

            for (int i = 0; i < Limb.Count; i++)
            {
                byte state = mine.state[i];

                // Срастающееся — та же трещина: мешает меньше перелома и больше целого.
                if (state == Healing) state = Light;

                if (state > worst) worst = state;
            }

            return worst;
        }

        /// <summary>The worst break this one has, or minus one when he is whole.</summary>
        internal static int Worst(UnitAttribute who)
        {
            Hurt mine = Of(who, false);
            if (mine == null) return -1;

            int found = -1;
            byte worst = None;

            for (int i = 0; i < Limb.Count; i++)
            {
                if (mine.state[i] == Bad) return i;

                if (mine.state[i] == Light && worst < Light) { worst = Light; found = i; }
            }

            return found;
        }

        /// <summary>Time passes and set bones knit.</summary>
        internal static void Knit(HumaniodUnit who, float hourPassed)
        {
            if (Enabled == null || !Enabled.Value || hourPassed <= 0f) return;

            try
            {
                Hurt mine = Of(who, false);
                if (mine == null) return;

                bool moved = false;

                // Тому, кто зарастает по природе, лекарь не нужен: кость берётся в лубок сама
                // собой, только срастается дольше, чем у человека под присмотром.
                if (ItemForge.Knit.Regrows(who))
                {
                    for (int i = 0; i < Limb.Count; i++)
                    {
                        if (mine.state[i] != Light && mine.state[i] != Bad) continue;

                        mine.was[i] = mine.state[i];
                        mine.state[i] = Healing;
                        mine.full[i] = Hours.Value * ItemForge.Knit.Wild.Value
                            * (mine.was[i] == Light ? 0.5f : 1f);
                        mine.left[i] = mine.full[i];

                        dirty = true;
                        moved = true;
                    }
                }

                for (int i = 0; i < Limb.Count; i++)
                {
                    if (mine.state[i] != Healing) continue;

                    // Выносливый срастается быстрее: процент за очко, без предела.
                    mine.left[i] -= hourPassed * Vigil.Healing(who);
                    dirty = true;

                    // Пересобираем бойца каждый час, а не только в последний: помеха тает
                    // понемногу, и таять она должна на деле, а не на бумаге.
                    moved = true;

                    if (mine.left[i] > 0f) continue;

                    mine.state[i] = None;
                    mine.left[i] = 0f;
                    mine.full[i] = 0f;
                    mine.was[i] = None;
                    moved = true;

                    if (Telling.Value && who.Data != null)
                    {
                        ItemForgePlugin.Log.LogInfo($"«{who.Data.unitname}»: "
                            + $"{Limb.Called[i]} срослась.");
                    }
                }

                if (!moved) return;

                dirty = true;
                who.UpdateAttribute();
            }
            catch
            {
            }
        }

        /// <summary>Как этот человек переломан — для журнала и для окна.</summary>
        internal static string Tell(UnitAttribute who)
        {
            Hurt mine = Of(who, false);
            if (mine == null) return "";

            string said = "";

            for (int i = 0; i < Limb.Count; i++)
            {
                if (mine.state[i] == None) continue;

                if (said.Length > 0) said += ", ";
                said += Bones[i] + " — " + Named[mine.state[i]];
            }

            return said;
        }

        // ----------------------------------------------------------------- память

        internal static void Settle()
        {
            if (!dirty || Time.time < next) return;

            next = Time.time + 20f;
            dirty = false;

            try
            {
                StringBuilder said = new StringBuilder();

                foreach (KeyValuePair<int, Hurt> one in broken)
                {
                    bool any = false;
                    for (int i = 0; i < Limb.Count; i++)
                    {
                        if (one.Value.state[i] != None) { any = true; break; }
                    }

                    if (!any) continue;

                    said.Append(Taming.Archive()).Append('	').Append(one.Key);

                    for (int i = 0; i < Limb.Count; i++)
                    {
                        said.Append('\t').Append(one.Value.state[i])
                            .Append('\t').Append(one.Value.left[i].ToString("0.##",
                                CultureInfo.InvariantCulture))
                            .Append('\t').Append(one.Value.full[i].ToString("0.##",
                                CultureInfo.InvariantCulture))
                            .Append('\t').Append(one.Value.was[i]);
                    }

                    said.AppendLine();
                }

                // Чужие миры переписываем как были: мы их не читали и портить не станем.
                foreach (string line in strangers) said.AppendLine(line);

                File.WriteAllText(Ledger, said.ToString());
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог записать переломы: " + e.Message);
            }
        }

        private static void Load()
        {
            string world = Taming.Archive();

            if (read == world) return;

            read = world;
            broken.Clear();
            strangers.Clear();

            try
            {
                if (!File.Exists(Ledger)) return;

                foreach (string line in File.ReadAllLines(Ledger))
                {
                    string[] cells = line.Split('\t');
                    if (cells.Length < 2 + Limb.Count * 4) continue;

                    // Не наш мир — отложим нетронутым и вернём на место при записи.
                    if (cells[0] != world) { strangers.Add(line); continue; }

                    int id;
                    if (!int.TryParse(cells[1], out id)) continue;

                    Hurt mine = new Hurt();

                    for (int i = 0; i < Limb.Count; i++)
                    {
                        byte state, was;
                        float left, full;

                        byte.TryParse(cells[2 + i * 4], out state);
                        float.TryParse(cells[3 + i * 4], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out left);
                        float.TryParse(cells[4 + i * 4], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out full);
                        byte.TryParse(cells[5 + i * 4], out was);

                        mine.state[i] = state;
                        mine.left[i] = left;
                        mine.full[i] = full;
                        mine.was[i] = was;
                    }

                    broken[id] = mine;
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог прочесть переломы: " + e.Message);
            }
        }
    }

    // Час прошёл — что взято в лубок, то срослось на час.
    [HarmonyPatch(typeof(HumaniodUnit), "TickHealthAndMorale")]
    internal static class Tick_Break_Patch
    {
        private static void Postfix(HumaniodUnit __instance, float hourPassed)
        {
            try
            {
                Break.Knit(__instance, hourPassed);
            }
            catch
            {
            }
        }
    }
}
