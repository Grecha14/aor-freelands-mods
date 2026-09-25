using System;
using System.Collections.Generic;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using DuloGames.UI;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Зверинец: перепись всего, что не человек.
    ///
    /// Список существ в игре есть — `UIUnitDatabase.Instance.indexes`, такой же полный, как
    /// список предметов. Берём его целиком и никуда ходить не надо.
    ///
    /// Но чертёж (UnitInfo) знает не всё. В нём записаны здоровье, раса, уровень, мощь,
    /// сопротивления и пробитие; **урона и размера в нём нет** — они лежат на префабе, в
    /// компоненте UnitAttribute, вместе с оружием, которым тварь бьёт. Префаб грузится
    /// синхронно и тут же отпускается, иначе полсотни моделей останутся висеть в памяти.
    ///
    /// Оттого перепись идёт в два слоя, и глубокий можно выключить, если она встанет колом.
    ///
    /// **Веса у существ нет вовсе.** Ни поля, ни числа: у них нет ни инвентаря, ни ноши, а
    /// масса тела живёт в физике и к бою отношения не имеет. Вместо веса игра держит
    /// UnitSize — пять ступеней от мелкого до исполинского, — и это единственная мерка туши.
    /// На неё и смотрит Heft, начисляя урон по размеру.
    ///
    /// **Шести статов у них тоже нет.** Сила, ловкость и прочие лежат в humanAttribute, а он
    /// есть только у NPCSaveData; существам самого поля не досталось. Оттого Bestiary и
    /// держит таблицу Might, подставляя силу вручную по имени и расе.
    /// </summary>
    internal static class Menagerie
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<KeyCode> Key;
        internal static ConfigEntry<bool> Deep;
        internal static ConfigEntry<string> Races;
        internal static ConfigEntry<int> Most;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Menagerie", "Enabled", true,
                "Let a key write out every creature in the game with its numbers.");

            Key = config.Bind("Menagerie", "Key", KeyCode.None,
                "Press this in game to write the census to a file beside the log. Off by default: F11 is now the one key that hands out experience, and two things on one key is how a debug key becomes a surprise.");

            Deep = config.Bind("Menagerie", "Deep", true,
                "Also load each creature's model to read what it hits with and how big it is. "
                + "Neither is written in the blueprint — both live on the prefab. Loading them "
                + "takes a moment and the models are released again straight away, but if the "
                + "game stalls on the key, turn this off and keep the blueprint half.");

            Most = config.Bind("Menagerie", "Most", 40,
                new ConfigDescription(
                    "How many mesh names to write out for a creature whose weapon could not be "
                    + "recognised by name. Enough to see what the model is actually made of.",
                    new AcceptableValueRange<int>(0, 200)));

            Races = config.Bind("Menagerie", "Races", "",
                "Which races to write out, by the game's own names. Empty — the default — means every "
                + "one of them, men and elves included: guessing which race a thing belongs to "
                + "costs more than the extra lines. Dragons, for instance, are lizards. Empty means every race, men "
                + "and elves included. The game knows: none, human, elf, dwarf, bruteman, "
                + "lizard, fairy, demon, undead, orc, mythological, animal, insect.");
        }

        /// <summary>Walk the whole roster and write it out.</summary>
        internal static void Write()
        {
            if (!Enabled.Value) return;

            try
            {
                UnitInfo[] all = UIUnitDatabase.Instance != null
                    ? UIUnitDatabase.Instance.indexes
                    : null;

                if (all == null || all.Length == 0)
                {
                    ItemForgePlugin.Log.LogWarning("Зверинец: список существ ещё не поднят.");
                    return;
                }

                HashSet<UnitRace> want = Wanted();

                // Сперва по расам, внутри расы по мощи: так видно лестницу, а не свалку.
                List<UnitInfo> taken = new List<UnitInfo>();

                foreach (UnitInfo who in all)
                {
                    if (who == null) continue;
                    if (want.Count > 0 && !want.Contains(who.race)) continue;
                    taken.Add(who);
                }

                taken.Sort(delegate (UnitInfo a, UnitInfo b)
                {
                    int by = a.race.CompareTo(b.race);
                    if (by != 0) return by;

                    by = a.power.CompareTo(b.power);
                    if (by != 0) return by;

                    return string.Compare(a.unitName, b.unitName, StringComparison.OrdinalIgnoreCase);
                });

                StringBuilder said = new StringBuilder();

                said.Append("Зверинец: ").Append(taken.Count).Append(" существ из ")
                    .Append(all.Length).Append(" в базе.").AppendLine();
                said.AppendLine("Веса у существ в игре нет — вместо него размер, пять ступеней.");
                said.AppendLine("Шести статов им тоже не досталось: где «статов нет», силу"
                    + " подставляет Bestiary.Might.");

                if (!Deep.Value)
                {
                    said.AppendLine("Урон и размер не читались: Deep выключен.");
                }

                said.AppendLine();

                UnitRace last = (UnitRace)(-1);
                int deep = 0;

                foreach (UnitInfo who in taken)
                {
                    if (who.race != last)
                    {
                        last = who.race;
                        said.AppendLine().Append("=== ").Append(last).Append(" ===")
                            .AppendLine();
                    }

                    said.AppendLine(Tell(who, ref deep));
                }

                string where = System.IO.Path.Combine(Paths.BepInExRootPath, "ЗВЕРИНЕЦ.txt");
                System.IO.File.WriteAllText(where, said.ToString(), Encoding.UTF8);

                ItemForgePlugin.Log.LogInfo("Зверинец записан: существ " + taken.Count
                    + ", с моделью прочитано " + deep + ", файл " + where);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Зверинец не записался: " + e);
            }
        }

        private static HashSet<UnitRace> Wanted()
        {
            HashSet<UnitRace> want = new HashSet<UnitRace>();

            foreach (string one in (Races.Value ?? "").Split(','))
            {
                string name = one.Trim();
                if (name.Length == 0) continue;

                try { want.Add((UnitRace)Enum.Parse(typeof(UnitRace), name, true)); }
                catch { ItemForgePlugin.Log.LogWarning("Расы «" + name + "» в игре нет."); }
            }

            return want;
        }

        private static string Tell(UnitInfo book, ref int deep)
        {
            StringBuilder said = new StringBuilder();

            said.Append("[").Append(book.unitId).Append("] ").Append(book.unitName);

            if (!string.IsNullOrEmpty(book.name) && book.name != book.unitName)
            {
                said.Append(" (").Append(book.name).Append(")");
            }

            said.Append(" | уровень ").Append(book.level)
                .Append(", мощь ").Append(book.power)
                .Append(", вид ").Append(book.utype);

            if (book.isBoss) said.Append(", ВОЖАК");
            if (book.canFly) said.Append(", летает");

            said.Append(" | здоровье ").Append(book.BShp.ToString("0.#"))
                .Append(", выносливость ").Append(book.BSsp.ToString("0.#"))
                .Append(", мана ").Append(book.BSmp.ToString("0.#"));

            said.Append(" | атака ").Append(book.BSattack)
                .Append(", блок ").Append(book.BSblock)
                .Append(", уклон ").Append(book.BSdodge)
                .Append(", крит ").Append(book.BScrit);

            said.Append(" | держит: ").Append(Row(book.BSdamageDR, "%"));
            said.Append(" | пробивает: ").Append(Row(book.BSresistPen, "%"));

            // ---------------------------------------------------- то, что на модели
            if (Deep.Value)
            {
                try
                {
                    GameObject made = book.Prefab;

                    if (made != null)
                    {
                        UnitAttribute body = made.GetComponentInChildren<UnitAttribute>(true);

                        if (body != null)
                        {
                            deep++;

                            said.Append(" | размер ").Append(body.size);
                            said.Append(" | ").Append(Hits(body));
                            said.Append(" | ").Append(Held(made, body));
                        }
                        else
                        {
                            said.Append(" | на модели нет бойца");
                        }
                    }

                    book.ReleaseAsset();
                }
                catch
                {
                    said.Append(" | модель не прочиталась");
                }
            }

            return said.ToString();
        }

        /// <summary>
        /// Что у существа в руках на самом деле — железка или лапа.
        ///
        /// У каждого удара есть узел на модели (`weaponNode`), к которому цепляется оружие.
        /// Если у огра дубина настоящая, на узле висит меш и его видно по имени и габаритам;
        /// если он бьёт кулаком, узел пуст либо его нет вовсе.
        ///
        /// Когда узлов нет ни у одного удара, смотрим шире: ищем на модели меши, чьи имена
        /// похожи на оружие. У зверья, которому оружие вшито в тушу, оно так и найдётся.
        /// </summary>
        private static string Held(GameObject made, UnitAttribute body)
        {
            try
            {
                StringBuilder said = new StringBuilder();
                bool any = false;

                if (body.weapons != null)
                {
                    for (int i = 0; i < body.weapons.Count; i++)
                    {
                        Weapon arm = body.weapons[i];
                        if (arm == null || arm.weaponNode == null) continue;

                        any = true;

                        if (said.Length > 0) said.Append("; ");
                        said.Append("узел ").Append(arm.weaponNode.name);

                        string mesh = Mesh(arm.weaponNode.gameObject);
                        said.Append(mesh != null ? ": " + mesh : ": пуст");
                    }
                }

                if (!any)
                {
                    // Узлов нет — поищем железку в самой туше по имени.
                    string found = Likely(made);
                    if (found != null) return "оружие в модели: " + found;

                    return "по именам оружия нет; " + Parts(made);
                }

                return said.ToString();
            }
            catch
            {
                return "модель не разобрана";
            }
        }

        /// <summary>The mesh hanging on a node, with how big it is.</summary>
        private static string Mesh(GameObject where)
        {
            try
            {
                MeshFilter flat = where.GetComponentInChildren<MeshFilter>(true);

                if (flat != null && flat.sharedMesh != null)
                {
                    Bounds how = flat.sharedMesh.bounds;
                    return flat.sharedMesh.name + " (" + how.size.x.ToString("0.##") + "x"
                        + how.size.y.ToString("0.##") + "x" + how.size.z.ToString("0.##") + ")";
                }

                SkinnedMeshRenderer skin = where.GetComponentInChildren<SkinnedMeshRenderer>(true);

                if (skin != null && skin.sharedMesh != null)
                {
                    Bounds how = skin.sharedMesh.bounds;
                    return skin.sharedMesh.name + " (шкура, " + how.size.x.ToString("0.##") + "x"
                        + how.size.y.ToString("0.##") + "x" + how.size.z.ToString("0.##") + ")";
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static readonly string[] Weaponish =
        {
            "weapon", "sword", "axe", "mace", "club", "hammer", "spear", "staff", "bow",
            "shield", "dagger", "blade", "knife", "claw", "scythe", "halberd", "trident",
            "polearm", "glaive", "flail", "cleaver", "sabre", "saber",
            "katana", "hatchet", "warhammer", "greatsword", "crossbow"
        };

        // Короткие слова вхождением не ищутся: «ax» сидит внутри «max», «bow» внутри «elbow».
        // Их сверяем с кусками имени, разбитого по разделителям.
        private static readonly string[] Short =
        {
            // «arm» сюда нельзя: он совпадает с рукой и наручами, и паук выходит вооружённым
            // наплечником. «bat» тоже — это имя зверя, а не дубина.
            "ax", "bow", "rod", "pick", "lance", "maul", "cane", "stick"
        };

        private static readonly char[] Apart = { '_', '-', '.', ' ', '(', ')' };

        private static bool Armed(string name)
        {
            string low = name.ToLowerInvariant();

            foreach (string word in Weaponish)
            {
                if (low.Contains(word)) return true;
            }

            foreach (string piece in low.Split(Apart))
            {
                // Хвостовые цифры и буквы-варианты отбрасываем: «ax_a», «ax2».
                string bare = piece.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');

                foreach (string word in Short)
                {
                    if (bare == word) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Что на туше похоже на оружие.
        ///
        /// Ищем и по имени объекта, и по имени меша, и в обоих видах отрисовки: у существ тело
        /// почти всегда SkinnedMeshRenderer, а не MeshFilter, и одним первым половину моделей
        /// не увидеть — на этом перепись и ошибалась, объявляя минотавра безоружным.
        /// </summary>
        private static string Likely(GameObject made)
        {
            try
            {
                StringBuilder said = new StringBuilder();
                int told = 0, all = 0;

                foreach (Transform part in made.GetComponentsInChildren<Transform>(true))
                {
                    if (part == null) continue;

                    string mesh = null;

                    MeshFilter flat = part.GetComponent<MeshFilter>();
                    if (flat != null && flat.sharedMesh != null) mesh = flat.sharedMesh.name;

                    if (mesh == null)
                    {
                        SkinnedMeshRenderer skin = part.GetComponent<SkinnedMeshRenderer>();
                        if (skin != null && skin.sharedMesh != null) mesh = skin.sharedMesh.name;
                    }

                    if (mesh != null) all++;

                    if (!Armed(part.name + " " + (mesh ?? ""))) continue;

                    if (told > 0) said.Append(", ");
                    said.Append(part.name);
                    if (mesh != null && mesh != part.name) said.Append(" [").Append(mesh).Append("]");

                    if (++told >= 5) break;
                }

                if (told > 0) return said.ToString();

                // Ничего не узналось по имени — скажем хотя бы, сколько там мешей вообще,
                // чтобы было видно, стоит ли смотреть глазами.
                return all > 0 ? null : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Все меши модели по именам.
        ///
        /// Когда по словам ничего не узналось, гадать бесполезно: авторы зовут части как
        /// придётся. Проще выписать что есть и посмотреть глазами — у минотавра, к примеру,
        /// тридцать два меша, и топор среди них наверняка есть под каким-то своим именем.
        /// </summary>
        private static string Parts(GameObject made)
        {
            try
            {
                StringBuilder said = new StringBuilder();
                int all = 0;

                foreach (Transform part in made.GetComponentsInChildren<Transform>(true))
                {
                    if (part == null) continue;

                    string mesh = null;

                    MeshFilter flat = part.GetComponent<MeshFilter>();
                    if (flat != null && flat.sharedMesh != null) mesh = flat.sharedMesh.name;

                    if (mesh == null)
                    {
                        SkinnedMeshRenderer skin = part.GetComponent<SkinnedMeshRenderer>();
                        if (skin != null && skin.sharedMesh != null) mesh = skin.sharedMesh.name;
                    }

                    if (mesh == null) continue;

                    all++;
                    if (all > Most.Value) continue;

                    if (said.Length > 0) said.Append(", ");
                    said.Append(part.name);
                    if (mesh != part.name) said.Append("[").Append(mesh).Append("]");
                }

                if (all == 0) return "мешей нет";

                string tail = all > Most.Value ? " и ещё " + (all - Most.Value) : "";
                return "мешей " + all + ": " + said + tail;
            }
            catch
            {
                return "мешей не прочесть";
            }
        }

        private static string Hits(UnitAttribute body)
        {
            try
            {
                if (body.weapons == null || body.weapons.Count == 0) return "оружия нет";

                StringBuilder said = new StringBuilder();

                for (int i = 0; i < body.weapons.Count; i++)
                {
                    Weapon arm = body.weapons[i];
                    if (arm == null) continue;

                    if (i > 0) said.Append("; ");

                    said.Append("удар ").Append(i).Append(": ");

                    bool first = true;

                    if (arm.BSdamage != null)
                    {
                        foreach (KeyValuePair<DamageType, Damage> one in arm.BSdamage)
                        {
                            if (one.Value == null || one.Value.maxDamage <= 0f) continue;

                            if (!first) said.Append(" и ");
                            first = false;

                            said.Append(one.Key).Append(' ')
                                .Append(one.Value.minDamage.ToString("0.#")).Append('-')
                                .Append(one.Value.maxDamage.ToString("0.#"));
                        }
                    }

                    if (first) said.Append("урона нет");

                    said.Append(", сила ").Append(arm.BSweaponForce.ToString("0.#"))
                        .Append(", скорость ").Append(arm.BSattackSpeed.ToString("0.##"))
                        .Append(", дальность ").Append(arm.BSattackDistance.ToString("0.##"));
                }

                return said.Length == 0 ? "оружия нет" : said.ToString();
            }
            catch
            {
                return "оружие не прочиталось";
            }
        }

        private static string Row(float[] all, string tail)
        {
            if (all == null || all.Length == 0) return "—";

            string[] named = { "рубящее", "дробящее", "колющее", "огонь", "холод",
                "молния", "яд", "светлое", "тёмное" };

            StringBuilder said = new StringBuilder();
            bool first = true;

            for (int i = 0; i < all.Length && i < named.Length; i++)
            {
                if (all[i] == 0f) continue;

                if (!first) said.Append(", ");
                first = false;

                said.Append(named[i]).Append(' ').Append(all[i].ToString("0.#")).Append(tail);
            }

            return first ? "ничего" : said.ToString();
        }
    }
}
