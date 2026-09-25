# -*- coding: utf-8 -*-
"""Починка припасом: сколько единиц уходит на восстановление доспеха.

    единиц = потолок( восстановленной прочности / Ратио )

Припас берётся по тому, из чего вещь сделана, и по её ступени. Легендарке пятого тира сверх
того нужен золотой слиток — по одному на починку, а не на каждые триста.
"""
import io
import math

RATIO = 300          # сколько прочности чинит одна единица припаса

# вес комплекта, кг, на первой своей ступени
SUITS = {"ткань": 4.2, "кожа": 6.5, "кольчуга": 17.4, "чешуя": 22.5,
         "полулаты": 29.5, "латы": 40.0}
GUARDS = {"ткань": 10, "кожа": 25, "кольчуга": 50, "чешуя": 62,
          "полулаты": 78, "латы": 100}
FIRST = {"ткань": 1, "кожа": 1, "кольчуга": 2, "чешуя": 2, "полулаты": 2, "латы": 2}
STEEP = {"кольчуга", "чешуя", "полулаты", "латы"}

PIECES = {"шлем": 1.0, "корпус": 2.0, "ноги": 1.5}
SHARE = {"шлем": 1.0, "корпус": 1.2, "ноги": 0.8}
FATTER = 1.07

# цена единицы припаса по ступени
PRICE = {2: 180, 3: 900, 4: 3600, 5: 3000}
GOLD = 7000


def piece(klass, tier, part):
    first = FIRST[klass]
    steps = max(0, tier - first)
    grow = 1.80 if klass in STEEP else 1.20

    kilos = SUITS[klass] * FATTER ** steps * PIECES[part] / sum(PIECES.values())
    guard = GUARDS[klass] * grow ** steps * SHARE[part]

    return kilos, guard, guard * kilos


said = ["Починка с нуля: сколько единиц припаса и во что обойдётся", ""]
said.append("%-10s %-5s %-7s %9s %8s %10s %12s" % (
    "класс", "тир", "кусок", "прочность", "единиц", "припас", "всего монет"))
said.append("-" * 72)

for klass in ("ткань", "кожа", "кольчуга", "латы"):
    for tier in (2, 3, 4, 5):
        if tier < FIRST[klass]:
            continue

        kilos, guard, dur = piece(klass, tier, "корпус")
        units = math.ceil(dur / RATIO)
        cost = units * PRICE.get(tier, 3000)
        if tier == 5:
            cost += GOLD

        said.append("%-10s %-5s %-7s %9.0f %8d %10d %12s" % (
            klass, "Т%d" % tier, "корпус", dur, units, PRICE.get(tier, 3000),
            format(cost, ",d").replace(",", " ")))
    said.append("")

said.append("на Т5 сверх припаса — один золотой слиток за починку (%d монет)" % GOLD)
said.append("")
said.append("если чинить не с нуля, а с половины — вдвое меньше, и так далее")

io.open("mend.txt", "w", encoding="utf-8").write("\n".join(said))
