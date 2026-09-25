using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using DuloGames.UI;
using HarmonyLib;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Looks at what the animator actually has before anything is built on it.
    ///
    /// Высота удара упирается в один вопрос, на который код не отвечает: сколько у оружия
    /// замахов и какие они. Игра на каждый удар тянет жребий — `attackset`, номер варианта, —
    /// и отдаёт его аниматору; сами же варианты живут в аниматоре, а не в скриптах, и по
    /// исходникам их не видно вовсе. Поле по умолчанию стоит в единицу, так что вполне может
    /// оказаться, что замах один на всё и выбирать не из чего.
    ///
    /// Поэтому сперва разведка, а уже потом решения. На каждый удар пишется строка: чем бьют,
    /// сколько у этого оружия вариантов, какой выпал и как называется клип, который на самом
    /// деле играет. Имена клипов у аниматоров обычно говорящие, и по ним будет видно, есть ли
    /// среди замахов верхний и нижний или все они об одном.
    ///
    /// Заодно проверяются кости. Наклон корпуса упирается в то, отдаёт ли скелет грудь и плечи
    /// по имени: игра свою грудь так и берёт, но одной её мало — нужно знать, что на месте и
    /// остальные.
    /// </summary>
    internal static class Scout
    {
        internal static ConfigEntry<bool> Swings;
        internal static ConfigEntry<bool> Bones;
        internal static ConfigEntry<bool> PlayerOnly;
        internal static ConfigEntry<int> Times;
        internal static ConfigEntry<bool> Listing;
        internal static ConfigEntry<string> Matching;
        internal static ConfigEntry<KeyCode> MeasureKey;
        internal static ConfigEntry<string> Measuring;
        internal static ConfigEntry<float> Upper;
        internal static ConfigEntry<float> Lower;

        internal static void Bind(ConfigFile config)
        {
            Swings = config.Bind("Scout", "Swings", true,
                "Write a line for every blow struck: the weapon, how many swings its animator "
                + "holds, which was drawn and what the clip now playing is called. This is the "
                + "only way to learn whether a weapon has an overhead swing and a low one or the "
                + "same motion every time.");

            Bones = config.Bind("Scout", "Bones", true,
                "Write out, once, which bones the skeleton answers to by name. A leaning body "
                + "needs the chest and both shoulders; if the rig does not give them up there is "
                + "nothing to lean.");

            PlayerOnly = config.Bind("Scout", "PlayerOnly", true,
                "Only watch the character being played. A battle of twenty writes twenty lines a "
                + "second otherwise, and the one blow you wanted is lost in them.");

            Listing = config.Bind("Scout", "Listing", true,
                "Write out, once, every swing the animator holds, grouped by family. This is the "
                + "whole list at a glance: how many there are for one-handers, for two-handers, "
                + "for polearms, and whether their names say anything about where they land.");

            Matching = config.Bind("Scout", "Matching", "Attack",
                "Only clips whose name contains this are listed. Empty lists everything the "
                + "animator holds, which is hundreds of lines of walking and standing.");

            MeasureKey = config.Bind("Scout", "MeasureKey", KeyCode.None,
                "Press this in game to measure every swing: each clip is played to the frame of "
                + "its own blow and the height of the striking hand is written down. The fighter "
                + "flickers for one frame while it runs, which is the whole cost.");

            Measuring = config.Bind("Scout", "Measuring", "_Attack_",
                "Only clips whose name contains this are measured. The ordinary swings are named "
                + "this way; the named ones — the gladiator's rush, the monk's landing — are not.");

            Upper = config.Bind("Scout", "Upper", 0.80f,
                new ConfigDescription(
                    "At and above this height the blow counts as going for the head, measured "
                    + "from the belt as nought and the crown as one.",
                    new AcceptableValueRange<float>(0f, 2f)));

            Lower = config.Bind("Scout", "Lower", 0.25f,
                new ConfigDescription(
                    "Below this height it counts as going for the legs. Between the two it is the "
                    + "body.",
                    new AcceptableValueRange<float>(-1f, 2f)));

            Times = config.Bind("Scout", "Times", 3,
                new ConfigDescription(
                    "How many blows to write per weapon and swing before falling silent about that "
                    + "pair. Counted by the pair and not by the weapon: there are six swings to "
                    + "each weapon and they are drawn at random, so counting by the weapon alone "
                    + "would spend the whole allowance on whichever two came up first.",
                    new AcceptableValueRange<int>(1, 100)));
        }

        // Сколько уже написано про каждое оружие, чтобы лог не заполнялся одним и тем же.
        private static readonly Dictionary<string, int> said = new Dictionary<string, int>();
        private static bool boned;
        private static bool listed;

        // Что было выбрано на этот замах. Записывается в начале удара, читается в его середине:
        // в кадре, где ставится триггер, аниматор ещё стоит в стойке и показывать нечего —
        // переход случится только на следующем его шаге. Первая разведка оттого и сняла
        // сплошные «StandCombat» вместо самих замахов.
        private static string named = "?";
        private static int drawn = -1;
        private static int subtype = -1;

        internal static void Swing(UnitAttribute who, int weaponIndex)
        {
            if (!Swings.Value || who == null || who.ani == null) return;

            try
            {
                if (PlayerOnly.Value && (object)who != (object)gameManager.currentplayUnit) return;

                Weapon arm = null;
                if (who.weapons != null)
                {
                    foreach (Weapon one in who.weapons)
                    {
                        if (one != null && one.index == weaponIndex) { arm = one; break; }
                    }
                }

                named = arm != null ? arm.weaponClass.ToString() : "без оружия";
                drawn = Mathf.RoundToInt(who.ani.GetFloat("attackset"));
                subtype = Mathf.RoundToInt(who.ani.GetFloat("weaponsubtype"));
            }
            catch
            {
            }
        }

        /// <summary>The blow lands; by now the animator is well inside the swing.</summary>
        internal static void Landed(UnitAttribute who)
        {
            if (!Swings.Value || who == null || who.ani == null || drawn < 0) return;

            try
            {
                if (PlayerOnly.Value && (object)who != (object)gameManager.currentplayUnit) return;

                // Считаем по паре «оружие и номер замаха»: нам нужно увидеть все шесть, а не
                // шесть раз один. Иначе первые четыре удара выпадут случайно и половина
                // вариантов останется неизвестной.
                string key = named + "#" + drawn;

                string word = Struck(who);
                string clip = Playing(who.ani);

                // В файл идёт каждый удар без исключения. В журнал — только несколько на пару
                // «оружие и набор», иначе его нельзя читать глазами.
                Note(named, drawn, subtype, clip);

                int told;
                said.TryGetValue(key, out told);
                if (told >= Times.Value) return;
                said[key] = told + 1;

                StringBuilder sb = new StringBuilder();
                sb.Append("Замах: ").Append(named);
                sb.Append(", набор ").Append(drawn);
                sb.Append(", подтип ").Append(subtype);

                // Высота острия — здесь и только здесь. Это живой кадр настоящего удара: игра
                // уже поставила оружие туда, где оно есть, и мерить можно прямо с модели.
                // В подстроенной позе этого не выходит — оружие висит на узле, который игра
                // двигает своим кодом, а он вне отрисовки не работает.
                sb.Append(" | ").Append(word);
                sb.Append(" | клипы: ").Append(clip);

                ItemForgePlugin.Log.LogInfo(sb.ToString());
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог посмотреть замах: " + e.Message);
            }
            finally
            {
                seen++;

                if (seen % 25 == 0)
                {
                    ItemForgePlugin.Log.LogInfo("Ударов замечено: " + seen
                        + ", в очереди на запись " + book.Count + ".");
                }
            }
        }

        // Последний удар в числах: кисть, остриё, длина, вперёд, вбок, до цели. Кладётся сюда
        // ради записи в файл — считать раскладку надо по сотням ударов, а не по тем трём, что
        // попали в журнал.
        internal static float[] Last;

        // Копится в памяти и сбрасывается на диск нечасто: запись каждого удара по файлу — это
        // заминка в бою на ровном месте.
        private static readonly List<string> book = new List<string>();
        private static float flush;

        private static string Blows
        {
            get { return System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "aor.blows.tsv"); }
        }

        /// <summary>Puts one blow into the ledger, every blow, without exception.</summary>
        private static void Note(string weapon, int set, int kind, string clip)
        {
            if (Last == null || Last.Length < 6) return;

            try
            {
                Open();

                System.Globalization.CultureInfo plain =
                    System.Globalization.CultureInfo.InvariantCulture;

                string[] cells = new string[]
                {
                    weapon, kind.ToString(), set.ToString(), clip,
                    Last[0].ToString("0.###", plain), Last[1].ToString("0.###", plain),
                    Last[2].ToString("0.###", plain), Last[3].ToString("0.###", plain),
                    Last[4].ToString("0.###", plain), Last[5].ToString("0.###", plain)
                };

                book.Add(string.Join("\t", cells));

                if (Time.unscaledTime < flush) return;

                flush = Time.unscaledTime + 10f;
                Flush();
            }
            catch
            {
            }
        }

        /// <summary>Puts the waiting lines at the end of the file, and only at the end.</summary>
        private static void Flush()
        {
            if (book.Count == 0) return;

            try
            {
                // Только дописывание. Переписывать файл целиком значит каждые десять секунд
                // ставить всё накопленное на кон: одна моя ошибка, один вылет игры посреди
                // записи — и сотен ударов нет. К тому, что дописывают, вернуться и стереть
                // нечем.
                int had = book.Count;

                System.IO.File.AppendAllLines(Blows, book);
                book.Clear();

                noted += had;

                ItemForgePlugin.Log.LogInfo("Ударов записано: " + had + ", всего за сессию "
                    + noted + ".");
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог дописать удары (" + book.Count
                    + " ждут): " + e.Message);
            }
        }

        private static bool opened;
        private static int noted;
        private static int seen;

        /// <summary>Starts the ledger if it is not there, and puts yesterday's aside.</summary>
        private static void Open()
        {
            if (opened) return;
            opened = true;

            try
            {
                if (!System.IO.File.Exists(Blows))
                {
                    System.IO.File.WriteAllLines(Blows, new string[]
                    {
                        string.Join("\t", new string[]
                        {
                            "оружие", "подтип", "набор", "клип",
                            "кисть", "остриё", "длина", "вперёд", "вбок", "доцели"
                        })
                    });

                    return;
                }

                // Копия при каждом запуске, с датой в имени. Стоит килобайты, а прошлые бои
                // становятся неубиваемыми — что бы я ни сломал в следующей сборке.
                string aside = System.IO.Path.Combine(BepInEx.Paths.ConfigPath,
                    "aor.blows." + DateTime.Now.ToString("yyyy-MM-dd-HHmm") + ".tsv");

                if (!System.IO.File.Exists(aside)) System.IO.File.Copy(Blows, aside);
            }
            catch
            {
            }
        }

        /// <summary>How high hand and weapon are at this very moment, in body fractions.</summary>
        private static string Struck(UnitAttribute who)
        {
            Last = null;

            try
            {
                Transform hips = who.ani.GetBoneTransform(HumanBodyBones.Hips);
                Transform head = who.ani.GetBoneTransform(HumanBodyBones.Head);
                if (hips == null || head == null) return "рост не взялся";

                float tall = head.position.y - hips.position.y;
                if (tall <= 0.01f) return "рост не взялся";

                float waist = hips.position.y;

                Transform right = who.ani.GetBoneTransform(HumanBodyBones.RightHand);
                float hand = right != null ? (right.position.y - waist) / tall : 0f;

                string word = "кисть " + hand.ToString("0.00") + " " + Zone(hand);

                Transform held = Held(who, right);
                if (held == null) return word;

                // Бьющий конец — самый дальний от руки, а не самый высокий. У алебарды с
                // опущенным лезвием выше всего оказывается затыльник древка, и по высоте
                // выходило бы, что она всегда бьёт в голову.
                Vector3 fist = right != null ? right.position : held.position;
                Vector3 far = fist;
                float away = 0f;
                bool found = false;

                // Дальний конец берётся по длинной оси самого меша, а не по углам коробки
                // вокруг него. Коробка выровнена по осям мира: у наклонённого топора её углы
                // не имеют отношения ни к лезвию, ни к рукояти, и самым дальним оказывается
                // случайный угол пустоты — оттого все шесть замахов топора и читались как удар
                // по ногам. Длинная ось меша — это само древко, и её дальний конец есть голова
                // топора, наконечник копья, остриё меча.
                foreach (MeshFilter part in held.GetComponentsInChildren<MeshFilter>())
                {
                    if (part == null || part.sharedMesh == null) continue;
                    Ends(part.transform, part.sharedMesh.bounds, fist, ref far, ref away, ref found);
                }

                foreach (SkinnedMeshRenderer part in held.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (part == null || part.sharedMesh == null) continue;
                    Ends(part.transform, part.sharedMesh.bounds, fist, ref far, ref away, ref found);
                }

                if (!found) return word;

                float high = (far.y - waist) / tall;

                // Вынос считается в осях самого бойца: вперёд — то, что перед ним, вбок — то,
                // что справа. По этим двум числам и строится точка, куда полетит удар, а
                // расстояние до цели говорит, с какого удаления такие удары случаются на самом
                // деле — чтобы рабочую дистанцию оружия не выдумывать из головы.
                Vector3 mine = who.transform.InverseTransformPoint(far);

                float range = 0f;
                UnitAttribute mark = who.Target;
                if (mark != null)
                {
                    range = Vector3.Distance(who.transform.position, mark.transform.position);
                }

                Last = new float[] { hand, high, away, mine.z, mine.x, range };

                return word + " | ОСТРИЁ " + high.ToString("0.00") + " " + Zone(high)
                       + " в " + away.ToString("0.0") + " м, вперёд " + mine.z.ToString("0.0")
                       + ", вбок " + mine.x.ToString("0.0")
                       + ", до цели " + range.ToString("0.0");
            }
            catch
            {
                return "не смерилось";
            }
        }

        /// <summary>Takes both ends of a mesh's long axis and keeps whichever is further out.</summary>
        private static void Ends(Transform where, Bounds local, Vector3 fist,
            ref Vector3 far, ref float away, ref bool found)
        {
            try
            {
                Vector3 ext = local.extents;

                Vector3 axis = ext.x >= ext.y && ext.x >= ext.z
                    ? new Vector3(ext.x, 0f, 0f)
                    : (ext.y >= ext.z ? new Vector3(0f, ext.y, 0f) : new Vector3(0f, 0f, ext.z));

                Vector3[] tips =
                {
                    where.TransformPoint(local.center + axis),
                    where.TransformPoint(local.center - axis)
                };

                foreach (Vector3 one in tips)
                {
                    float gap = Vector3.Distance(one, fist);
                    if (!found || gap > away) { away = gap; far = one; found = true; }
                }
            }
            catch
            {
            }
        }

        /// <summary>What the animator is playing right now, across its layers.</summary>
        private static string Playing(Animator ani)
        {
            StringBuilder sb = new StringBuilder();

            try
            {
                for (int layer = 0; layer < ani.layerCount; layer++)
                {
                    AnimatorClipInfo[] now = ani.GetCurrentAnimatorClipInfo(layer);
                    AnimatorClipInfo[] next = ani.GetNextAnimatorClipInfo(layer);

                    foreach (AnimatorClipInfo one in now)
                    {
                        if (one.clip == null || one.weight <= 0.01f) continue;
                        if (sb.Length > 0) sb.Append(", ");
                        sb.Append(one.clip.name).Append(" (").Append(one.weight.ToString("0.0#")).Append(")");
                    }

                    // Удар начинается со смены состояния, и в первый кадр «текущим» ещё стоит
                    // стойка, а замах уже в следующих. Без этого половина строк сообщала бы про
                    // то, как персонаж стоял.
                    foreach (AnimatorClipInfo one in next)
                    {
                        if (one.clip == null || one.weight <= 0.01f) continue;
                        if (sb.Length > 0) sb.Append(", ");
                        sb.Append("-> ").Append(one.clip.name);
                    }
                }
            }
            catch (Exception e)
            {
                sb.Append("не прочитались: ").Append(e.Message);
            }

            return sb.Length > 0 ? sb.ToString() : "ничего";
        }

        /// <summary>Writes out every swing the animator knows, once.</summary>
        internal static void Clips(UnitAttribute who)
        {
            if (!Listing.Value || listed || who == null || who.ani == null) return;

            try
            {
                if ((object)who != (object)gameManager.currentplayUnit) return;

                RuntimeAnimatorController book = who.ani.runtimeAnimatorController;
                if (book == null || book.animationClips == null) return;

                listed = true;

                string want = (Matching.Value ?? "").Trim();

                // Клипы в контроллере повторяются — один и тот же стоит в нескольких переходах.
                SortedDictionary<string, List<string>> sets =
                    new SortedDictionary<string, List<string>>();

                foreach (AnimationClip clip in book.animationClips)
                {
                    if (clip == null) continue;

                    string name = clip.name;
                    if (want.Length > 0
                        && name.IndexOf(want, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    // Семейство — это всё до первого подчёркивания: «2HAxe», «1H», «Dual».
                    int cut = name.IndexOf('_');
                    string family = cut > 0 ? name.Substring(0, cut) : "прочее";

                    List<string> had;
                    if (!sets.TryGetValue(family, out had))
                    {
                        had = new List<string>();
                        sets[family] = had;
                    }

                    if (!had.Contains(name)) had.Add(name);
                }

                ItemForgePlugin.Log.LogInfo("Замахи в аниматоре, семейств " + sets.Count + ":");

                foreach (KeyValuePair<string, List<string>> one in sets)
                {
                    one.Value.Sort(StringComparer.OrdinalIgnoreCase);

                    ItemForgePlugin.Log.LogInfo("  " + one.Key + " (" + one.Value.Count + "): "
                        + string.Join(", ", one.Value.ToArray()));
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог перечислить замахи: " + e.Message);
            }
        }

        /// <summary>
        /// Measures where every swing actually arrives, by playing each clip to the frame of
        /// its own blow and looking at how high the hand is.
        ///
        /// Имена движений ничего не говорят о высоте, а смотреть глазами по сорок ударов —
        /// долго и всё равно на глазок. Но у каждого замаха в клипе стоит событие, которым игра
        /// засчитывает попадание: это и есть тот самый кадр. Проигрываем клип ровно в него и
        /// смотрим, где оказалась кисть — в долях роста от пояса до макушки. Ноль это пояс,
        /// единица макушка, выше единицы удар идёт над головой.
        /// </summary>
        internal static void Measure(UnitAttribute who)
        {
            if (who == null || who.ani == null)
            {
                ItemForgePlugin.Log.LogWarning("Замер: некого мерить.");
                return;
            }

            try
            {
                RuntimeAnimatorController book = who.ani.runtimeAnimatorController;
                if (book == null || book.animationClips == null)
                {
                    ItemForgePlugin.Log.LogWarning("Замер: у бойца нет аниматора.");
                    return;
                }

                Transform hips = who.ani.GetBoneTransform(HumanBodyBones.Hips);
                Transform head = who.ani.GetBoneTransform(HumanBodyBones.Head);
                Transform right = who.ani.GetBoneTransform(HumanBodyBones.RightHand);
                Transform left = who.ani.GetBoneTransform(HumanBodyBones.LeftHand);

                if (hips == null || head == null || right == null)
                {
                    ItemForgePlugin.Log.LogWarning("Замер: скелет не отдал нужные кости.");
                    return;
                }

                // Привязка снимается до первого проигрывания: сейчас поза живая, и оружие
                // стоит там, где его поставила игра.
                Calibrate(who, right);
                if (!Calibrated) Calibrate(who, left);

                string want = (Measuring.Value ?? "_Attack_").Trim();

                SortedDictionary<string, List<string>> rows =
                    new SortedDictionary<string, List<string>>();

                int counted = 0;
                HashSet<string> done = new HashSet<string>();

                foreach (AnimationClip clip in book.animationClips)
                {
                    if (clip == null) continue;

                    string name = clip.name;
                    if (want.Length > 0
                        && name.IndexOf(want, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (!done.Add(name)) continue;

                    float when = Blow(clip);
                    if (when < 0f) continue;

                    clip.SampleAnimation(who.gameObject, when);

                    // Рост считаем по самому бойцу, а не в метрах: у гнома и у орка разный
                    // рост, а «выше плеча» у обоих значит одно и то же.
                    float tall = head.position.y - hips.position.y;
                    if (tall <= 0.01f) continue;

                    Transform hand = name.IndexOf("_L", StringComparison.OrdinalIgnoreCase) >= 0
                                     && left != null ? left : right;

                    float high = (hand.position.y - hips.position.y) / tall;

                    // У копья и древкового остриё далеко от кисти: рука у бедра, а наконечник
                    // идёт в грудь. Мерить по кисти для них значит соврать на полтуловища, и
                    // соврать всегда в одну сторону.
                    float stem, lean, point;
                    bool armed = Tip(who, hand, hips.position.y, tall, out stem, out lean, out point);

                    int cut = name.IndexOf('_');
                    string family = cut > 0 ? name.Substring(0, cut) : "прочее";

                    List<string> had;
                    if (!rows.TryGetValue(family, out had))
                    {
                        had = new List<string>();
                        rows[family] = had;
                    }

                    string row = name + " кисть " + high.ToString("0.00") + " " + Zone(high);

                    if (armed)
                    {
                        // Узел и наклон — это то, из чего считается остриё любой длины:
                        // высота удара равна «узел плюс наклон на длину вещи».
                        row += " | узел " + stem.ToString("0.00")
                               + " наклон " + lean.ToString("0.00")
                               + " | ОСТРИЁ " + point.ToString("0.00") + " " + Zone(point);
                    }

                    row += " (кадр " + when.ToString("0.00") + " из "
                           + clip.length.ToString("0.00") + ")";

                    had.Add(row);

                    counted++;
                }

                ItemForgePlugin.Log.LogInfo("ЗАМЕР ЗАМАХОВ: измерено " + counted
                    + ". Высота в долях роста от пояса (0) до макушки (1).");

                foreach (KeyValuePair<string, List<string>> one in rows)
                {
                    one.Value.Sort(StringComparer.OrdinalIgnoreCase);

                    ItemForgePlugin.Log.LogInfo("  --- " + one.Key + " ---");
                    foreach (string line in one.Value) ItemForgePlugin.Log.LogInfo("    " + line);
                }

                GameController.ShowMessage("Замеров замахов: " + counted, 3f);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Замер не вышел: " + e);
            }
        }

        /// <summary>
        /// How high the far end of whatever is in that hand reaches. Minus a hundred when the
        /// hand is empty and there is nothing to measure.
        /// </summary>
        /// <summary>
        /// Where the far end of a weapon of a given length would be, in body fractions. Works
        /// from the node the weapon hangs on and the axis it points along — not from any
        /// bounding box: those are recomputed when a frame is drawn, and a pose sampled outside
        /// of drawing leaves them showing the pose before it. That was the mistake that made
        /// every swing read the same two and a bit.
        /// </summary>
        private static bool Tip(UnitAttribute who, Transform hand, float waist, float tall,
            out float stem, out float lean, out float point)
        {
            stem = 0f; lean = 0f; point = 0f;

            if (!Calibrated || who == null || who.ani == null) return false;

            try
            {
                // Ту же руку, что и при привязке: иначе плечо считается от чужого запястья.
                hand = who.ani.GetBoneTransform(
                    HeldLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);

                if (hand == null) return false;

                // Всё считается от кости кисти, и только от неё. Кость — часть скелета, клип
                // двигает её по определению; узел же, на котором висит оружие, игра подтягивает
                // своим кодом каждый кадр, а при проигрывании клипа вне отрисовки этот код не
                // работает — оттого он и стоял на месте, пока рука летала.
                Vector3 grip = hand.TransformPoint(Hold);
                Vector3 way = hand.TransformDirection(Axis);

                stem = (grip.y - waist) / tall;
                lean = way.y / tall;

                point = stem + lean * Long(who);

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>The length of whatever is in the main hand, in metres.</summary>
        private static float Long(UnitAttribute who)
        {
            try
            {
                HumaniodUnit man = who as HumaniodUnit;
                if (man == null || man.equipmentmanger == null) return 0f;

                UIWeaponInfo blade = man.equipmentmanger.Weapon_mainhand;
                return blade != null ? Reach.Of(blade) : 0f;
            }
            catch
            {
                return 0f;
            }
        }


        private static bool toldHold;

        // Куда из узла торчит оружие, в его собственных осях. Узел один на всё, что берут в
        // правую руку, поэтому достаточно узнать это один раз на любом клинке — дальше остриё
        // любой вещи считается как «узел плюс её длина вдоль этой оси».
        internal static Vector3 Axis = Vector3.zero;
        internal static Vector3 Hold = Vector3.zero;
        internal static bool HeldLeft;
        internal static bool Calibrated;

        internal static void Calibrate(UnitAttribute who, Transform hand)
        {
            if (Calibrated || hand == null) return;

            try
            {
                Transform held = Held(who, hand);
                if (held == null || (object)held == (object)hand) return;

                Renderer[] parts = held.GetComponentsInChildren<Renderer>();
                if (parts == null || parts.Length == 0) return;

                Vector3 far = held.position;
                float most = 0f;

                foreach (Renderer part in parts)
                {
                    if (part == null || !part.enabled) continue;

                    Bounds box = part.bounds;
                    Vector3 mid = box.center;
                    Vector3 ext = box.extents;

                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 corner = mid + new Vector3(
                            (i & 1) == 0 ? ext.x : 0f - ext.x,
                            (i & 2) == 0 ? ext.y : 0f - ext.y,
                            (i & 4) == 0 ? ext.z : 0f - ext.z);

                        float away = Vector3.Distance(corner, held.position);
                        if (away > most) { most = away; far = corner; }
                    }
                }

                if (most < 0.05f) return;

                // К той руке, что и правда держит, а не к той, которую назвали. Двуручное
                // висит на левом узле, и привязка к правой кисти давала плечо в метр длиной:
                // от одного поворота запястья расчётная точка улетала через полэкрана.
                Transform near = Nearest(who, held);
                if (near != null) hand = near;

                Hold = hand.InverseTransformPoint(held.position);
                Axis = hand.InverseTransformDirection((far - held.position).normalized);
                Calibrated = true;

                HeldLeft = who != null && who.ani != null
                           && (object)hand == (object)who.ani.GetBoneTransform(HumanBodyBones.LeftHand);

                ItemForgePlugin.Log.LogInfo("Оружие привязано к "
                    + (HeldLeft ? "левой" : "правой") + " кисти, отстоит на "
                    + Hold.magnitude.ToString("0.00") + " м: рукоять "
                    + Hold.x.ToString("0.00") + ", " + Hold.y.ToString("0.00") + ", "
                    + Hold.z.ToString("0.00") + "; клинок "
                    + Axis.x.ToString("0.00") + ", " + Axis.y.ToString("0.00") + ", "
                    + Axis.z.ToString("0.00") + "; длина " + most.ToString("0.00") + " м.");
            }
            catch
            {
            }
        }


        /// <summary>Which hand is actually nearest the thing being held.</summary>
        private static Transform Nearest(UnitAttribute who, Transform held)
        {
            try
            {
                if (who == null || who.ani == null || held == null) return null;

                Transform right = who.ani.GetBoneTransform(HumanBodyBones.RightHand);
                Transform left = who.ani.GetBoneTransform(HumanBodyBones.LeftHand);

                if (right == null) return left;
                if (left == null) return right;

                return Vector3.Distance(right.position, held.position)
                       <= Vector3.Distance(left.position, held.position) ? right : left;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Whatever the fighter is actually holding in that hand.</summary>
        private static Transform Held(UnitAttribute who, Transform hand)
        {
            try
            {
                bool leftish = hand != null && who != null
                               && who.ani != null
                               && (object)hand == (object)who.ani.GetBoneTransform(HumanBodyBones.LeftHand);

                // Само оружие знает, на каком узле висит, — и для лука с арбалетом это вовсе
                // не кисть, а свои отдельные узлы.
                if (who != null && who.weapons != null)
                {
                    int want = leftish ? 1 : 0;

                    foreach (Weapon one in who.weapons)
                    {
                        if (one == null || one.index != want) continue;
                        if (one.weaponNode != null) return one.weaponNode;
                    }
                }

                HumaniodUnit man = who as HumaniodUnit;
                EquipmentManager kit = man != null ? man.equipmentmanger : null;

                if (kit != null)
                {
                    Transform node = leftish ? kit.LeftWeaponNode : kit.RightWeaponNode;
                    if (node != null) return node;
                }

                return hand;
            }
            catch
            {
                return hand;
            }
        }

        /// <summary>The moment the clip says its own blow lands.</summary>
        private static float Blow(AnimationClip clip)
        {
            try
            {
                if (clip.events != null)
                {
                    foreach (AnimationEvent one in clip.events)
                    {
                        if (one != null && one.functionName == "Attackdone") return one.time;
                    }
                }
            }
            catch
            {
            }

            // Без события — берём середину: это не точно, и такие строки будут видны по кадру.
            return clip.length > 0f ? clip.length * 0.5f : -1f;
        }

        private static string Zone(float high)
        {
            if (high >= Upper.Value) return "ГОЛОВА";
            if (high >= Lower.Value) return "корпус";
            return "ноги";
        }

        /// <summary>Says once which bones a leaning body would have to work with.</summary>
        internal static void Skeleton(UnitAttribute who)
        {
            if (!Bones.Value || boned || who == null || who.ani == null) return;

            try
            {
                if ((object)who != (object)gameManager.currentplayUnit) return;

                boned = true;

                HumanBodyBones[] wanted =
                {
                    HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest,
                    HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head,
                    HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm,
                    HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                    HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm,
                    HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand
                };

                StringBuilder sb = new StringBuilder();
                sb.Append("Скелет «").Append(who.Data != null ? who.Data.unitname : "?");
                sb.Append("»: тип ").Append(who.ani.isHuman ? "человеческий" : "свой");
                sb.Append(" | ");

                foreach (HumanBodyBones bone in wanted)
                {
                    Transform had = who.ani.GetBoneTransform(bone);
                    sb.Append(bone).Append(had != null ? " есть" : " НЕТ").Append("; ");
                }

                ItemForgePlugin.Log.LogInfo(sb.ToString());
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Не смог разобрать скелет: " + e.Message);
            }
        }
    }

    // Жребий тянется здесь же, где ставится триггер: читаем сразу после игры, чтобы увидеть
    // именно то число, которое ушло аниматору.
    //
    // Метода два, и нужны оба. У людей свой, перекрывающий, и базовый он не зовёт — так что
    // перехват на одном лишь базовом не сработал бы ни разу ни на одном ударе человека.
    // Базовый при этом остаётся за зверьём.
    [HarmonyPatch(typeof(HumaniodUnit), "BasicAttack")]
    internal static class BasicAttack_Human_Scout_Patch
    {
        private static void Postfix(HumaniodUnit __instance, int weaponIndex)
        {
            try
            {
                Scout.Skeleton(__instance);
                Scout.Clips(__instance);
                Scout.Swing(__instance, weaponIndex);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "BasicAttack")]
    internal static class BasicAttack_Scout_Patch
    {
        private static void Postfix(UnitAttribute __instance, int weaponIndex)
        {
            try
            {
                if (__instance is HumaniodUnit) return;

                Scout.Skeleton(__instance);
                Scout.Clips(__instance);
                Scout.Swing(__instance, weaponIndex);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(UnitAttribute), "MoveAttack")]
    internal static class MoveAttack_Scout_Patch
    {
        private static void Postfix(UnitAttribute __instance, int weaponIndex)
        {
            try { Scout.Swing(__instance, weaponIndex); }
            catch { }
        }
    }

    // Середина замаха: сюда игру приводит событие самой анимации, в кадре удара. Здесь клип
    // уже играет, и его имя — это имя того самого движения, а не стойки перед ним.
    [HarmonyPatch(typeof(UnitAttribute), "Attackdone")]
    internal static class Attackdone_Scout_Patch
    {
        private static void Postfix(UnitAttribute __instance)
        {
            try { Scout.Landed(__instance); }
            catch { }
        }
    }
}
