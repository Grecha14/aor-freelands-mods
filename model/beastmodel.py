# -*- coding: utf-8 -*-
"""Полная модель зверя: статы, мастерство, пробитие, вычет, здоровье.

Всё считается той же формулой, что у человека. Разницу задают три вещи: размер, уровень и
то, чем зверь бьёт — когтем или настоящим оружием, если оно есть на модели.

Мастерство меряется местом внутри своей вилки, а не абсолютным уровнем: полусотня внизу,
сотня наверху. Волк к пятнадцатому знает про свои зубы всё, что можно знать, — и незачем
требовать от него сотого уровня, которого у волков не бывает.
"""

#            сила  вын  лов  точн  ум  воля
BASE = {"Small": (3, 4, 16, 12, 2, 2), "Medium": (8, 10, 18, 10, 3, 3),
        "Large": (30, 26, 8, 8, 4, 4), "Giant": (50, 40, 4, 6, 5, 5)}
STEP = {"Small": (0.05, 0.08, 0.30, 0.20, 0.03, 0.03),
        "Medium": (0.12, 0.20, 0.40, 0.20, 0.05, 0.05),
        "Large": (0.55, 0.45, 0.10, 0.12, 0.06, 0.06),
        "Giant": (0.80, 0.60, 0.05, 0.10, 0.08, 0.08)}

LEADS = {"Small": 2, "Medium": 2, "Large": 0, "Giant": 0}   # 2 ловкость, 0 сила
CLAW = {"Small": 2.0, "Medium": 2.2, "Large": 2.0, "Giant": 4.0}   # кг когтя и челюсти
BULK = {"Small": 1, "Medium": 2, "Large": 6, "Giant": 25}          # туша, множитель здоровья

WALL = {"Small": 5, "Medium": 15}          # вычет ровный
WALL_BASE = {"Large": 60, "Giant": 150}    # вычет растущий
WALL_GROWTH = 0.015

LEAST, MOST = 50, 100                      # мастерство по краям своей вилки


def stats(size, level):
    return [int(round(b + s * level)) for b, s in zip(BASE[size], STEP[size])]


def mastery(level, low, high):
    """Полусотня внизу вилки, сотня наверху. Фиксированный уровень — значит наверху."""
    if high <= low:
        return MOST
    part = (level - low) / float(high - low)
    return LEAST + (MOST - LEAST) * max(0.0, min(1.0, part))


def pierce(size, level, low, high, arms=None):
    six = stats(size, level)
    kilos = arms if arms else CLAW[size]
    return six[LEADS[size]] * kilos * (1 + mastery(level, low, high) / 100.0)


def wall(size, level):
    if size in WALL:
        return WALL[size]
    return WALL_BASE[size] * (1 + level * WALL_GROWTH)


def health(size, level):
    six = stats(size, level)
    return (100 + level * 6 + 6 * six[1]) * BULK[size]
