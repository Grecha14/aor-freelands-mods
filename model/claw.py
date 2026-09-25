# -*- coding: utf-8 -*-
"""Пробитие зверя по человеческой формуле: стат x вес x мастерство.

Веса когтя и челюсти берём по размеру — туша задаёт, сколько железа зверь может донести до
цели. У кого на модели настоящее оружие (минотавр с топором), берём вес оружия.

Мастерство у зверья от полусотни до сотни: он ничем другим всю жизнь и не занимался.
"""
import io

#            сила  вын  лов  точн  ум  воля
BASE = {"Small": (3, 4, 16, 12, 2, 2), "Medium": (8, 10, 18, 10, 3, 3),
        "Large": (30, 26, 8, 8, 4, 4), "Giant": (50, 40, 4, 6, 5, 5)}
STEP = {"Small": (0.05, 0.08, 0.30, 0.20, 0.03, 0.03),
        "Medium": (0.12, 0.20, 0.40, 0.20, 0.05, 0.05),
        "Large": (0.55, 0.45, 0.10, 0.12, 0.06, 0.06),
        "Giant": (0.80, 0.60, 0.05, 0.10, 0.08, 0.08)}
LEADS = {"Small": 2, "Medium": 2, "Large": 0, "Giant": 0}

# Чем зверь бьёт, в килограммах: коготь, челюсть, рог.
CLAW = {"Small": 2.0, "Medium": 2.2, "Large": 4.5, "Giant": 9.0}

LEAST, MOST, FULL = 50, 100, 100        # мастерство: от полусотни к сотне к сотому уровню

GUARD = {"ткань": [12, 14, 17, 21, 25], "кожа": [30, 36, 43, 52, 62],
         "кольчуга": [None, 60, 108, 194, 350], "чешуя": [None, 74, 134, 241, 434],
         "полулаты": [None, 94, 168, 303, 546], "латы": [None, 120, 216, 389, 700]}


def stats(size, level):
    return [int(round(b + s * level)) for b, s in zip(BASE[size], STEP[size])]


def skill(level):
    return min(MOST, LEAST + (MOST - LEAST) * min(1.0, level / float(FULL)))


def opens(value):
    got = []
    for name, row in GUARD.items():
        best = None
        for i, guard in enumerate(row):
            if guard is not None and value >= guard:
                best = i + 1
        if best:
            got.append("%s Т%d" % (name, best))
    return "; ".join(got) if got else "ничего"


# имя, размер, уровень, вес оружия если есть
WHO = [
    ("Крыса", "Small", 1, None), ("Мышь", "Small", 2, None),
    ("Волк", "Medium", 5, None), ("Кабан", "Medium", 7, None),
    ("Белый волк", "Medium", 11, None), ("Порченый волк", "Medium", 34, None),
    ("Медведь", "Large", 14, None), ("Гигантский паук", "Large", 28, None),
    ("Оборотень", "Large", 31, None),
    ("Минотавр с топором", "Large", 35, 13.0),
    ("Химера", "Large", 40, None),
    ("Слон", "Giant", 40, None), ("Циклоп с дубиной", "Giant", 43, 20.0),
    ("Король червей", "Large", 300, None), ("Дракон", "Giant", 300, None),
]

said = ["%-20s %-7s %4s %6s %6s %7s %9s | %s" % (
    "зверь", "размер", "ур.", "стат", "вес", "маст.", "пробитие", "что открывает")]
said.append("-" * 112)

for name, size, level, arms in WHO:
    six = stats(size, level)
    stat = six[LEADS[size]]
    kilos = arms if arms else CLAW[size]
    mast = skill(level)
    pierce = stat * kilos * (1 + mast / 100.0)

    said.append("%-20s %-7s %4d %6d %6.1f %6.0f %9.0f | %s" % (
        name, size, level, stat, kilos, mast, pierce, opens(pierce)))

said.append("")
said.append("чем бьёт игрок: Т1 меч 30 топор 40 молот 80 | Т3 меч 107 топор 143 молот 286")
said.append("                Т5 меч 338 топор 450 молот 900")

io.open("claw.txt", "w", encoding="utf-8").write("\n".join(said))
