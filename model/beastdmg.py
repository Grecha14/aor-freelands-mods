# -*- coding: utf-8 -*-
"""Сколько зверь донесёт до тела сквозь доспех.

    остаток = вычет брони − пробитие зверя   (не ниже нуля)
    чистый  = урон зверя − остаток           (не ниже нуля)

Урон зверя множится тем же, чем человеческий: сила через MeleeDamageMD и мастерство ветки.
Размерная надбавка Heft при этом становится лишней — её работу теперь делают статы.
"""
import io

import beastmodel as bm

HEFT_SIZE = {"Small": 1.0, "Medium": 2.5, "Large": 4.0, "Giant": 5.5}   # что даёт Heft сейчас

# Вычет корпуса у игрока по классам, Т1..Т5
ARMOUR = {"ткань Т1": 12, "кожа Т2": 36, "кольчуга Т3": 108,
          "чешуя Т3": 134, "полулаты Т4": 303, "латы Т4": 389, "латы Т5": 700}

# зверь: размер, уровень, вилка, урон (низ, верх), оружие если есть
WHO = [
    ("Крыса", "Small", 3, (1, 3), (10, 16), None),
    ("Волк", "Medium", 15, (1, 15), (12, 18), None),
    ("Белый волк", "Medium", 15, (11, 15), (12, 20), None),
    ("Порченый волк", "Medium", 102, (34, 102), (41, 56), None),
    ("Медведь", "Large", 42, (14, 42), (30, 45), None),
    ("Оборотень", "Large", 75, (55, 75), (14, 27), None),
    ("Минотавр с топором", "Large", 105, (35, 105), (37, 51), 13.0),
    ("Циклоп", "Giant", 129, (43, 129), (59, 74), 20.0),
    ("Дракон", "Giant", 300, (300, 300), (45, 86), None),
]


def blow(size, level, low, high, damage, arms, with_heft):
    six = bm.stats(size, level)
    mast = bm.mastery(level, low, high)
    mid = sum(damage) / 2.0

    much = mid * (1 + six[0] * 0.01) * (1 + mast / 100.0)
    if with_heft:
        much *= HEFT_SIZE[size]
    return much


said = []
for with_heft in (True, False):
    said.append("=== %s ===" % ("с надбавкой Heft за размер" if with_heft
                                else "без неё, только статы"))
    said.append("%-20s %8s %9s | %s" % (
        "зверь", "удар", "пробитие", "  ".join("%-11s" % a for a in ARMOUR)))

    for name, size, level, span, damage, arms in WHO:
        hit = blow(size, level, span[0], span[1], damage, arms, with_heft)
        pen = bm.pierce(size, level, span[0], span[1], arms)

        cells = []
        for guard in ARMOUR.values():
            left = max(0.0, guard - pen)
            cells.append("%-11.0f" % max(0.0, hit - left))

        said.append("%-20s %8.0f %9.0f | %s" % (name, hit, pen, "  ".join(cells)))

    said.append("")

io.open("beastdmg.txt", "w", encoding="utf-8").write("\n".join(said))
