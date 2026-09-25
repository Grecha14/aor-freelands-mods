# -*- coding: utf-8 -*-
"""Настоящий счёт боя: обе стороны бьют, у обеих есть стена.

Прежде я считал только то, сколько отряд снимет с дракона, и выходило двадцать две секунды.
Но дракон в это время не стоит: он бьёт в ответ, и бьёт так, что доспех не помогает.
"""
import io

# --- герой на конце игры -------------------------------------------------
HERO_HP = 420
HERO_WALL = 700          # латы Т5, корпус
HERO_PIERCE = 45 * 20.0 * 1.5      # стат x вес молота x мастерство
HERO_HIT = 772           # с учётом всех множителей
HERO_SPEED = 0.42
HEROES = 5

# --- дракон ---------------------------------------------------------------
DRAGON_HP = 34900
DRAGON_WALL = 700
DRAGON_PIERCE = 290 * 4.0 * 2.0    # сила x коготь x мастерство
DRAGON_HIT = 60 * (1 + 290 * 0.01) * 2.0
DRAGON_SPEED = 0.5


def through(hit, pierce, wall):
    """Сколько доходит до тела: удар минус то, что осталось от стены."""
    return max(0.0, hit - max(0.0, wall - pierce))


said = []

mine = through(HERO_HIT, HERO_PIERCE, DRAGON_WALL)
his = through(DRAGON_HIT, DRAGON_PIERCE, HERO_WALL)

said.append("ГЕРОЙ ПО ДРАКОНУ")
said.append("  пробитие %.0f против стены %.0f -> остаток %.0f" % (
    HERO_PIERCE, DRAGON_WALL, max(0.0, DRAGON_WALL - HERO_PIERCE)))
said.append("  удар %.0f -> доходит %.0f" % (HERO_HIT, mine))
said.append("")

said.append("ДРАКОН ПО ГЕРОЮ")
said.append("  пробитие %.0f против лат Т5 %.0f -> остаток %.0f" % (
    DRAGON_PIERCE, HERO_WALL, max(0.0, HERO_WALL - DRAGON_PIERCE)))
said.append("  удар %.0f -> доходит %.0f" % (DRAGON_HIT, his))
said.append("  у героя %d здоровья: гибнет с %.1f удара" % (HERO_HP, HERO_HP / his if his else 0))
said.append("")

party = mine * HERO_SPEED * HEROES
to_kill = DRAGON_HP / party if party else 9999

# Дракон валит по герою за удар; отряд тает, и урон отряда падает.
alive = HEROES
time = 0.0
left = DRAGON_HP
step = 0.1

while left > 0 and alive > 0 and time < 600:
    left -= mine * HERO_SPEED * alive * step
    # сколько героев он успевает свалить
    blows = DRAGON_SPEED * step
    falls = blows * (his * 1.0 / HERO_HP if his < HERO_HP else 1.0)
    alive -= falls
    time += step

said.append("БОЙ")
said.append("  отряд из %d бьёт %.0f в секунду" % (HEROES, party))
said.append("  дракон падёт за %.0f секунд, если бы стоял смирно" % to_kill)
said.append("")
said.append("  на деле: через %.0f секунд у дракона %s здоровья, живых героев %.1f" % (
    time, format(int(max(0, left)), ",d").replace(",", " "), max(0, alive)))
said.append("  %s" % ("ОТРЯД ПОБЕДИЛ" if left <= 0 else "ОТРЯД ЛЁГ"))

io.open("fight.txt", "w", encoding="utf-8").write("\n".join(said))
