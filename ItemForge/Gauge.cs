using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ItemForge
{
    /// <summary>
    /// Мерило: строка пробития в окне персонажа.
    ///
    /// Пробитие — главное число новой боевой модели, и до сих пор его нельзя было увидеть
    /// нигде. Игрок видел вычет на доспехе и не видел, чем этот вычет одолевать.
    ///
    /// Своей строки в окне нет, и рисовать поверх чужого окна — последнее дело: оно само
    /// раскладывается, само красит, само переводится. Оттого строка не рисуется, а
    /// **размножается**: берём готовую строку «Размер тела», делаем её копию, переписываем
    /// подпись и ставим своё число. Копия живёт с окном, делается один раз.
    ///
    /// Число считается по тому, что в основной руке, и меняется со сменой оружия. Так и
    /// должно быть: пробитие — свойство пары «боец и оружие», а не одного бойца. Сменил меч
    /// на молот — и стена, которая была непреодолима, поддалась.
    ///
    /// Чего здесь нет и быть не может: доли дробящего, идущей мимо стены, и щели крита. То и
    /// другое — про столкновение с конкретным доспехом, а не про самого бойца.
    /// </summary>
    internal static class Gauge
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Title;
        internal static ConfigEntry<string> Guard;
        internal static ConfigEntry<bool> Wall;
        internal static ConfigEntry<string> Body;
        internal static ConfigEntry<bool> Parts;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Gauge", "Enabled", true,
                "Show piercing in the character window, beneath body size. It is the number the "
                + "whole reckoning turns on, and until now it could be seen nowhere: a man could "
                + "read the deduction on a harness and not what he had to answer it with.");

            Title = config.Bind("Gauge", "Title", "Общий урон",
                "What the row is called. The game's own rows are localized; this one is not, so "
                + "it is written here.");

            Guard = config.Bind("Gauge", "Guard", "Общая броня",
                "And what the row beneath it is called: everything the man has standing between "
                + "himself and a blow, his own body included.");

            Body = config.Bind("Gauge", "Body", "Тело",
                "What the row of body parts is called: how many of the six still work.");

            Parts = config.Bind("Gauge", "Parts", true,
                "Show that row. A man is six healths now, and a window that shows one number "
                + "tells him nothing about which arm he is about to lose.");

            Wall = config.Bind("Gauge", "Wall", true,
                "Show that row at all. Armour is no longer one number on a card — it is what "
                + "each place holds, and the body under it holds too — so the window needs a "
                + "figure to answer the damage above it.");
        }

        // Копии строк живут с окном. Второй раз плодить их незачем.
        private static Text mine;
        private static Text label;
        private static Text guard;
        private static Text guardLabel;
        private static Text body;
        private static Text bodyLabel;

        /// <summary>Разводит ещё одну строку от образца, вернув её число и подпись.</summary>
        private static bool Sprout(Text like, string name, int below, out Text value,
            out Text said)
        {
            value = null;
            said = null;

            if (like == null || like.transform.parent == null) return false;

            Transform row = like.transform.parent;

            // Своё место в копии ищем по тому же порядку, в каком оно стоит в образце:
            // копия — та же иерархия, и номера совпадают.
            Text[] was = row.GetComponentsInChildren<Text>(true);
            int at = Array.IndexOf(was, like);
            if (at < 0) return false;

            GameObject born = UnityEngine.Object.Instantiate(row.gameObject, row.parent);
            born.name = name;
            born.transform.SetSiblingIndex(row.GetSiblingIndex() + below);

            Text[] now = born.GetComponentsInChildren<Text>(true);
            if (at >= now.Length) return false;

            value = now[at];

            // Подпись — единственный другой текст в строке.
            for (int i = 0; i < now.Length; i++)
            {
                if (i == at) continue;
                said = now[i];
                break;
            }

            born.SetActive(true);

            return true;
        }

        /// <summary>Makes the rows, once, by copying the one above them.</summary>
        private static bool Grow(UIApplyUnitAttribute tip)
        {
            if (mine != null && (guard != null || !Wall.Value)
                && (body != null || !Parts.Value)) return mine != null;

            try
            {
                if (mine == null)
                {
                    if (!Sprout(tip.bodySize, "PiercingRow", 1, out mine, out label)) return false;
                    if (label != null) label.text = Title.Value;
                }

                if (Wall.Value && guard == null)
                {
                    if (Sprout(tip.bodySize, "GuardRow", 2, out guard, out guardLabel))
                    {
                        if (guardLabel != null) guardLabel.text = Guard.Value;
                    }
                }

                if (Parts.Value && body == null && Limb.Enabled != null && Limb.Enabled.Value)
                {
                    if (Sprout(tip.bodySize, "BodyRow", 3, out body, out bodyLabel))
                    {
                        if (bodyLabel != null) bodyLabel.text = Body.Value;
                    }
                }

                return mine != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Сколько частей тела ещё работает, и что с ними.</summary>
        private static void Told(UnitAttribute who)
        {
            if (!Parts.Value || body == null || !Limb.Counts(who)) return;

            try
            {
                if (bodyLabel != null && bodyLabel.text != Body.Value) bodyLabel.text = Body.Value;

                Limb.Body six = Limb.Of(who);

                if (!six.known)
                {
                    body.text = "—";
                    return;
                }

                int alive = 0;
                for (int i = 0; i < Limb.Count; i++)
                {
                    if (six.now[i] > 0f) alive++;
                }

                body.text = alive + "/" + Limb.Count;

                string bones = Break.Tell(who);

                // Цвет по тяжести, и той же мерой, какой покрашены знаки недобора: трещина
                // оранжевая, перелом и выбитая часть красные, целое — как все прочие строки.
                byte worst = Break.Gravest(who);

                if (alive < Limb.Count || worst == Break.Bad)
                {
                    body.color = new Color(0.85f, 0.35f, 0.3f, 1f);
                }
                else if (worst == Break.Light)
                {
                    body.color = new Color(1f, 0.6f, 0.15f, 1f);
                }
                else if (label != null)
                {
                    body.color = label.color;
                }

                if (body.transform.parent == null) return;

                string said = "Шесть здоровий вместо одного. Умирают от двух: туловища и "
                    + "головы.\n\n" + Limb.Tell(who);

                if (bones.Length > 0) said += "\n\nСломано: " + bones + ".";

                said += "\n\nВыбитая рука не держит оружия — двуручное перехватывается "
                    + "одной, вполсилы. Выбитая нога отнимает три четверти хода, обе — весь ход. "
                    + "Переломы сами не срастаются: их берут в лубок аптечкой или у лекаря.";

                Hints.Say(body.transform.parent.gameObject, said);
            }
            catch
            {
            }
        }

        internal static void Show(UIApplyUnitAttribute tip, UnitAttribute who)
        {
            if (!Enabled.Value || tip == null || who == null) return;
            if (Breach.Enabled == null || !Breach.Enabled.Value) return;

            try
            {
                if (!Grow(tip)) return;

                if (label != null && label.text != Title.Value) label.text = Title.Value;

                // Итог удара, когда всё посчитано: вещь, рука, прибавки снаряжения, штрафы
                // за груз и всё прочее, что игра успела наложить. Собранность удара сюда не
                // входит — она про то, как удар входит в железо, а не сколько несёт.
                float much = Breach.Hitting(who);

                mine.text = much > 0f ? much.ToString("0") : "—";

                if (mine.transform.parent != null)
                {
                    Hints.Say(mine.transform.parent.gameObject, Hints.Blow.Value);
                }

                Told(who);

                if (!Wall.Value || guard == null) return;

                if (guardLabel != null && guardLabel.text != Guard.Value)
                {
                    guardLabel.text = Guard.Value;
                }

                // Три числа: против реза, обуха и укола. В строке — среднее, в подсказке все
                // три, потому что доспех против них разный и это главное, что о нём надо знать.
                float cut = Breach.Holding(who, 0);
                float dent = Breach.Holding(who, 1);
                float jab = Breach.Holding(who, 2);

                float middle = (cut + dent + jab) / 3f;

                guard.text = middle > 0f ? middle.ToString("0") : "—";

                if (guard.transform.parent != null)
                {
                    Hints.Say(guard.transform.parent.gameObject,
                        $"Сколько урона держит человек целиком, вместе со своим телом.\n\n"
                        + $"Режущий {cut:0}, дробящий {dent:0}, колющий {jab:0}.\n\n"
                        + "Считается средним по местам: грудь держит больше всего, голова "
                        + "реже всего под ударом. Мышцы и кости входят сюда же — это тоже "
                        + "броня, только своя.\n\n"
                        + "Сравнивайте с чужим общим уроном: удар слабее трёх четвертей этого "
                        + "числа не проходит вовсе, сильнее на четверть — проходит целиком.");
                }
            }
            catch
            {
            }
        }
    }

    /// <summary>Окно персонажа перерисовывается само; мы дописываем свою строку следом.</summary>
    [HarmonyPatch(typeof(UIApplyUnitAttribute), "UpdateInfoWindow")]
    internal static class UnitTip_Gauge_Patch
    {
        private static void Postfix(UIApplyUnitAttribute __instance)
        {
            try
            {
                Gauge.Show(__instance, __instance.Unit);
            }
            catch
            {
            }
        }
    }
}
