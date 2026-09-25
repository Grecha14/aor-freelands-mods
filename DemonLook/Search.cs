using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DuloGames.UI;
using spell;
using UnityEngine;

namespace DemonLook
{
    /// <summary>
    /// A guard stops the stranger the town suspects, and asks where he was.
    ///
    /// Когда у города на демона собралось подозрение — от тел, найденных утром, — стражник,
    /// увидевший его вблизи, останавливает его: не чаще раза в день на город. Ответить можно
    /// тремя способами. Дать себя обыскать: если на нём ещё не сошли следы выпитой души, его
    /// узнают; если сошли — подозрение слабеет. Откупиться: только с торговлей от пятой ступени
    /// и не больше трёх раз в неделю; чем лучше торгует, тем дешевле. Отказать: подозрение
    /// растёт. Своего убеждения у демона нет — только торг и золото.
    /// </summary>
    internal static class Search
    {
        internal static ConfigEntry<int> SearchAt;
        internal static ConfigEntry<float> Near;
        internal static ConfigEntry<int> BribeSkill;
        internal static ConfigEntry<int> BribeWeek;
        internal static ConfigEntry<int> BribePrice;
        internal static ConfigEntry<int> Step;

        internal static void Bind(ConfigFile config)
        {
            SearchAt = config.Bind("Search", "SearchAt", 30,
                new ConfigDescription("How much suspicion a town must hold before its guards stop the demon.",
                    new AcceptableValueRange<int>(1, 100)));

            Near = config.Bind("Search", "Near", 5f,
                new ConfigDescription("How close, in metres, a guard must see him to stop him.",
                    new AcceptableValueRange<float>(1f, 30f)));

            BribeSkill = config.Bind("Search", "BribeSkill", 5,
                new ConfigDescription("The trading skill needed to buy a guard off.",
                    new AcceptableValueRange<int>(0, 100)));

            BribeWeek = config.Bind("Search", "BribeWeek", 3,
                new ConfigDescription("How many times a week a guard can be bought off.",
                    new AcceptableValueRange<int>(0, 100)));

            BribePrice = config.Bind("Search", "BribePrice", 20,
                new ConfigDescription("Gold for each point of the town crime, at the least trading "
                    + "skill; every point of skill above it takes five per cent off, down to half.",
                    new AcceptableValueRange<int>(0, 100000)));

            Step = config.Bind("Search", "Step", 10,
                new ConfigDescription("How much a clean search takes off the suspicion, and a refusal adds.",
                    new AcceptableValueRange<int>(0, 100)));
        }

        private static UnitAttribute guard;
        private static Faction team;
        private static bool open;
        private static bool answered;
        private static string said;
        private static float scale = 1f;
        private static Rect frame = new Rect(0f, 0f, 620f, 330f);
        private static GUIStyle body;

        /// <summary>Looks for a guard of a suspicious town who sees the demon up close.</summary>
        internal static void Look()
        {
            if (open) return;

            HumaniodUnit demon = Souls.Demon();
            if (demon == null || demon.Data == null || demon.Data.isdead || demon.isEngaged) return;

            foreach (UnitAttribute u in UnitAttribute.GetUnitsInRangeNoCollider(demon, Near.Value, demon.transform.position, TargetAllow.all))
            {
                if (u == null || u.Data == null || u.Data.isdead || u.isSleeping || u.isEngaged || u.inParty) continue;
                if (!Crime.Guard(u)) continue;

                Faction town = u.Data.team;
                if (Souls.Suspicion(town) < SearchAt.Value || Souls.SearchedToday(town)) continue;

                bool hostile = false;
                try { hostile = FactionManager.Instance.IsEnemy(u, demon); } catch { }
                if (hostile) continue;

                if (!u.InSenseRange(demon)) continue;

                Open(u);
                return;
            }
        }

        // Ночной допрос в облаву — раз за ночь в городе.
        private static readonly Dictionary<int, int> nightStops = new Dictionary<int, int>();

        internal static bool CanStopAtNight(Faction town)
        {
            if (open || Souls.Demon() == null) return false;
            int night;
            return !nightStops.TryGetValue((int)town, out night) || night != Crime.Night();
        }

        internal static void OpenNight(UnitAttribute who)
        {
            if (open || who == null || who.Data == null) return;
            nightStops[(int)who.Data.team] = Crime.Night();

            Open(who, "Из темноты выступает стражник с фонарём и заступает дорогу.\n\n"
                + "— Стой! Ночь на дворе, а ты бродишь по улицам. В городе нашли мёртвого, и всех, "
                + "кто шатается в темноте, мы теперь спрашиваем. Кто таков и куда идёшь?", false);
        }

        internal static void Open(UnitAttribute who)
        {
            Open(who, "Стражник преграждает дорогу и кладёт ладонь на рукоять.\n\n"
                + "— Стой, чужак. Этой ночью у нас нашли мёртвого: ни раны, ни крови, а лицо серое, "
                + "будто из него вынули душу. Такого здесь не бывало, пока ты не пришёл. Где ты был, "
                + "когда стемнело?", true);
        }

        private static void Open(UnitAttribute who, string opening, bool daily)
        {
            if (open || who == null || who.Data == null) return;

            HumaniodUnit demon = Souls.Demon();
            if (demon == null) return;

            guard = who;
            team = who.Data.team;
            open = true;
            answered = false;
            said = opening;

            if (daily) Souls.MarkSearched(team);

            try { who.Stop(); } catch { }
            try { demon.Stop(); } catch { }

            scale = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;

            frame.x = (Screen.width - frame.width) / 2f;
            frame.y = (Screen.height - frame.height) / 2f;
        }

        private static void Close()
        {
            open = false;
            guard = null;
            Time.timeScale = scale > 0f ? scale : 1f;
        }

        private static int Bargain(HumaniodUnit demon)
        {
            try { return demon.Data.bargain; }
            catch { return 0; }
        }

        private static int Price(HumaniodUnit demon)
        {
            int crime = 0;
            try { FactionManager.Instance.factionCrimeToPlayer.TryGetValue(team, out crime); } catch { }

            float off = Mathf.Clamp01(1f - 0.05f * (Bargain(demon) - BribeSkill.Value));
            off = Mathf.Max(0.5f, off);
            return Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(1, crime) * BribePrice.Value * off));
        }

        private static void Tally(int delta)
        {
            if (delta == 0) return;
            try { FactionManager.Instance.AddCrimeToPlayer(team, delta, canSubFavorToPlayer: false); } catch { }
        }

        private static void Submit()
        {
            HumaniodUnit demon = Souls.Demon();
            bool traces = demon != null && demon.buffmanger != null && demon.buffmanger.ContainBuff("DemonLookRevel");

            if (traces)
            {
                try
                {
                    int hostile = UICrimeDatabase.Instance.GetCrimeValueFactor(CrimeDataType.HostileThreshold);
                    FactionManager.Instance.AddCrimeToPlayer(team, Mathf.Max(1, hostile));
                }
                catch
                {
                }
                Souls.Reveal();
                said = "Стражник отшатывается: под твоей кожей проступают тёмные прожилки, в глазах тлеет "
                    + "чужой огонь.\n\n— Демон! — кричит он так, что слышит вся улица.";
                Souls.Say("Вас узнали: город поднят, и молва о демоне идёт по миру.");
            }
            else
            {
                int off = Mathf.Min(Step.Value, Souls.Suspicion(team));
                Souls.Suspect(team, -off);
                Tally(-off);
                said = "Стражник долго шарит по сумке и смотрит тебе в лицо.\n\n"
                    + "— Чисто. Ступай. Но я тебя запомнил.";
                Souls.Say($"Подозрение −{off}.");
            }

            answered = true;
        }

        private static void Bribe()
        {
            HumaniodUnit demon = Souls.Demon();
            if (demon == null) return;

            int price = Price(demon);
            if (ManagementModeCore.Money < price) return;

            ManagementModeCore.TakeMoney(-price);
            int all = Souls.Suspicion(team);
            Souls.Suspect(team, -all);
            Tally(-all);
            Souls.Bribed();

            said = "Монеты исчезают в перчатке быстрее, чем ты договорил.\n\n"
                + "— Никого я тут не видел. И тебя тоже.";
            Souls.Say($"Взятка {price} — подозрение снято.");
            answered = true;
        }

        private static void Refuse()
        {
            Souls.Suspect(team, Step.Value);
            Tally(Step.Value);
            said = "— Вот как. — Стражник щурится. — Ну смотри, чужак. Ещё раз попадёшься мне на глаза — "
                + "поговорим иначе.";
            Souls.Say($"Подозрение +{Step.Value}.");
            answered = true;
        }

        internal static void Draw()
        {
            if (!open) return;

            if (body == null)
            {
                body = new GUIStyle(GUI.skin.label);
                body.wordWrap = true;
                body.fontSize = 16;
                body.padding = new RectOffset(18, 18, 12, 12);
            }

            GUI.depth = 0;
            frame = GUI.Window(907_011, frame, Body, "Стража");
        }

        private static void Body(int id)
        {
            GUILayout.Space(6f);
            GUILayout.Label(said ?? "", body);
            GUILayout.FlexibleSpace();

            if (answered)
            {
                if (GUILayout.Button("Уйти", GUILayout.Height(30f))) Close();
                GUI.DragWindow(new Rect(0f, 0f, frame.width, 22f));
                return;
            }

            HumaniodUnit demon = Souls.Demon();

            if (GUILayout.Button("Обыщите, если хотите. Мне скрывать нечего.", GUILayout.Height(30f))) Submit();

            int skill = demon != null ? Bargain(demon) : 0;
            int price = demon != null ? Price(demon) : 0;
            int used = Souls.BribesThisWeek();

            string why = null;
            if (skill < BribeSkill.Value) why = $"нужна торговля {BribeSkill.Value}";
            else if (used >= BribeWeek.Value) why = $"взяток на этой неделе уже {used}";
            else if (ManagementModeCore.Money < price) why = $"нужно {price} монет";

            GUI.enabled = why == null;
            if (GUILayout.Button("Может, это поможет вам забыть моё лицо? (" + price + " монет"
                    + (why != null ? "; " + why : "") + ")", GUILayout.Height(30f))) Bribe();
            GUI.enabled = true;

            if (GUILayout.Button("Не ваше дело.", GUILayout.Height(30f))) Refuse();

            GUI.DragWindow(new Rect(0f, 0f, frame.width, 22f));
        }
    }
}
