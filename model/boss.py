# -*- coding: utf-8 -*-
"""Сколько здоровья надо дракону, чтобы финальная битва шла столько, сколько нужно.

Считаем от того, что отряд вправду выдаёт: урон оружия, помноженный на всё, что его в игре
множит, поделённый на время замаха.
"""
import io

STAT = 45          # сила прокачанного героя
MASTERY = 50       # ветка оружия
HEROES = 5

# Легендарная двуручная булава: игровой урон и вес после перековки.
DAMAGE = (71, 112)
WEIGHT = 22.5
SPEED = 0.42       # ударов в секунду

HEFT = min(3.0, 1 + WEIGHT * 0.10)     # десятая доля урона за килограмм, потолок втрое
TEMPER = 0.9                            # дробящему сбавлено за ровность удара
MELEE = 1 + STAT * 0.01                 # MeleeDamageMD
SKILL = 1 + MASTERY * 0.01              # мастерство ветки
BONUS = 1.25                            # надбавки с вещей, скромно
CRIT = 1.15                             # поправка на долю критов

mid = sum(DAMAGE) / 2.0
blow = mid * HEFT * TEMPER * MELEE * SKILL * BONUS * CRIT
party = blow * SPEED * HEROES

said = []
said.append("удар одного героя: %.0f" % blow)
said.append("  урон %.0f, вес %.2f, ровность %.2f, сила %.2f, мастерство %.2f, надбавки %.2f"
            % (mid, HEFT, TEMPER, MELEE, SKILL, BONUS))
said.append("отряд из %d: %.0f урона в секунду" % (HEROES, party))
said.append("")
said.append("%-14s %14s" % ("бой длится", "надо здоровья"))

for minutes in (1, 2, 3, 5, 8, 12):
    said.append("%-14s %14s" % ("%d мин" % minutes,
                                format(int(party * minutes * 60), ",d").replace(",", " ")))

io.open("boss.txt", "w", encoding="utf-8").write("\n".join(said))
