using System;
using BepInEx.Configuration;
using DuloGames.UI;
using UnityEngine;

namespace ItemForge
{
    /// <summary>
    /// Somebody to hit for as long as it takes.
    ///
    /// Замеры требуют сотен ударов одним и тем же оружием, а живой противник кончается с
    /// первого. Тренировочный столб стоит там, где стоит, и ходить к нему через полкарты ради
    /// одного числа — дороже самого числа.
    ///
    /// Поэтому здесь свой: вызывается под руку, не умирает и здоровье себе возвращает сам.
    /// Считается чучелом, как и городские столбы, — значит игра сама не станет писать ему в
    /// счёт убийства и не даст за него ни опыта, ни мастерства сверх десятой ступени.
    ///
    /// Драться он не будет: стоит и принимает. Нам нужны наши удары, а не его.
    /// </summary>
    internal static class Dummy
    {
        internal static ConfigEntry<KeyCode> Key;
        internal static ConfigEntry<string> Who;
        internal static ConfigEntry<float> Away;
        internal static ConfigEntry<bool> Harmless;

        internal static void Bind(ConfigFile config)
        {
            Key = config.Bind("Dummy", "Key", KeyCode.None,
                "Press this in game to call up something to hit. It does not die and does not "
                + "strike back; it stands there and takes it, which is the whole point.");

            Who = config.Bind("Dummy", "Who", "Guard_T2_EliteKnight",
                "Which creature to call, by the name of its blueprint. A knight in full harness "
                + "by default: heavy armour on every place, so aiming at the gaps has something "
                + "to aim at. The log lists what the world holds if the name is unknown.");

            Away = config.Bind("Dummy", "Away", 3f,
                new ConfigDescription(
                    "How far in front of you it appears, in metres. Within reach of a polearm "
                    + "and not on top of you.",
                    new AcceptableValueRange<float>(1f, 20f)));

            Harmless = config.Bind("Dummy", "Harmless", true,
                "Keep it from striking back. Off, and it fights — which is another kind of test "
                + "and a worse one for measuring, since half your blows go into stepping away.");
        }

        private static UnitAttribute standing;

        internal static void Call()
        {
            try
            {
                HumaniodUnit you = gameManager.currentplayUnit as HumaniodUnit;
                if (you == null)
                {
                    ItemForgePlugin.Log.LogWarning("Чучело: игра ещё не началась.");
                    return;
                }

                AreaManager area = AreaManager.Instance;
                UIUnitDatabase book = UIUnitDatabase.Instance;

                if (area == null || book == null || book.indexes == null)
                {
                    ItemForgePlugin.Log.LogWarning("Чучело: мир ещё не поднят.");
                    return;
                }

                string want = (Who.Value ?? "").Trim();
                UnitInfo template = null;

                foreach (UnitInfo one in book.indexes)
                {
                    if (one == null || one.name == null) continue;
                    if (string.Equals(one.name, want, StringComparison.OrdinalIgnoreCase))
                    {
                        template = one;
                        break;
                    }
                }

                if (template == null)
                {
                    ItemForgePlugin.Log.LogWarning("Чучело: «" + want + "» такого нет. "
                        + "Впишите имя из заготовок в настройку Dummy.Who.");
                    return;
                }

                // Прежнее убираем: иначе у наковальни соберётся толпа бессмертных.
                Dismiss();

                Vector3 where = you.transform.position + you.transform.forward * Away.Value;

                UnitAttribute made = area.CreateCharacter(template, where,
                    new Vector3(0f, you.transform.eulerAngles.y + 180f, 0f), Faction.monster);

                if (made == null || (object)made == (object)you)
                {
                    ItemForgePlugin.Log.LogWarning("Чучело: создать не вышло.");
                    return;
                }

                standing = made;

                Steady(made);

                ItemForgePlugin.Log.LogInfo("Чучело «" + template.name + "» поставлено в "
                    + Away.Value.ToString("0.#") + " м. Бейте сколько нужно.");

                GameController.ShowMessage("Чучело поставлено", 2.5f);
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogError("Не смог поставить чучело: " + e);
            }
        }

        /// <summary>Makes it a post rather than a man.</summary>
        private static void Steady(UnitAttribute made)
        {
            try
            {
                // Чучелом его объявляем по-игровому: тогда и счёт убийств, и мастерство, и
                // износ снаряжения игра сама рассудит так, как рассуждает у столбов во дворе.
                made.isPracticeDummy = true;

                if (made.Data != null)
                {
                    made.Data.canKill = false;
                    made.Data.forceKill = false;
                }

                if (Harmless.Value && made.Data != null)
                {
                    made.Data.allowattack = false;
                    made.Data.allowmove = false;
                }
            }
            catch (Exception e)
            {
                ItemForgePlugin.Log.LogWarning("Чучело встало не совсем смирно: " + e.Message);
            }
        }

        private static float next;

        /// <summary>Keeps it on its feet, whatever is done to it.</summary>
        internal static void Tend()
        {
            if (standing == null) return;

            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;

            try
            {
                if (standing.Data == null) { standing = null; return; }

                standing.Data.isdead = false;
                standing.Data.canKill = false;
                standing.Data.forceKill = false;

                // Доливаем и очки жизни, и медленную шкалу здоровья: без второй чучело за сотню
                // ударов свалится от увечий, даже не будучи убитым.
                standing.RestoreHP(standing.maxhp);

                NPCSaveData mind = standing.Data as NPCSaveData;
                if (mind != null && mind.health < 100f) mind.AddHealth(100f);
            }
            catch
            {
                standing = null;
            }
        }

        private static void Dismiss()
        {
            if (standing == null) return;

            try
            {
                if (AreaManager.Instance != null) AreaManager.Instance.DeleteCharacter(standing, true);
            }
            catch
            {
            }

            standing = null;
        }
    }
}
