# -*- coding: utf-8 -*-
"""Пробитие зверья: подбираем множитель от того, что кто должен открывать.

Не от массы туши. Зуб и коготь концентрируют усилие на точке — крыса кожу прокусывает, хотя
весит ничего. Оттого множитель ставится по паре «размер и чем бьёт», а проверяется по тому,
до какого класса брони зверь доходит.
"""

GUARD = {                                   # вычет корпуса, Т1..Т5
    "tkan":      [12, 14, 17, 21, 25],
    "kozha":     [30, 36, 43, 52, 62],
    "kolchuga":  [None, 60, 108, 194, 350],
    "cheshuya":  [None, 74, 134, 241, 434],
    "poluliaty": [None, 94, 168, 303, 546],
    "laty":      [None, 120, 216, 389, 700],
}

BASE = {          # сила  вын  лов  точн  ум  воля
    "Small":  (3,   3,  14,  12,  2,  2),
    "Medium": (10, 10,  10,  10,  3,  3),
    "Large":  (25, 28,   6,   8,  4,  4),
    "Giant":  (45, 50,   3,   6,  5,  5),
}
STEP = {"Small": 0.15, "Medium": 0.35, "Large": 0.60, "Giant": 0.80}

# Чем зверь прикладывает усилие. Укус и жало бьют в точку, оттого у них множитель высокий
# при малой туше; лапа режет плашмя; таран работает массой и растёт с размером.
BITE = {
    ("Small",  "stab"):  3.6,   ("Small",  "sharp"): 2.2,   ("Small",  "blunt"): 1.0,
    ("Medium", "stab"):  5.0,   ("Medium", "sharp"): 3.4,   ("Medium", "blunt"): 2.4,
    ("Large",  "stab"):  5.5,   ("Large",  "sharp"): 4.4,   ("Large",  "blunt"): 4.0,
    ("Giant",  "stab"):  7.5,   ("Giant",  "sharp"): 7.0,   ("Giant",  "blunt"): 7.0,
}


def stats(size, level):
    step = STEP[size]
    return [int(round(v + step * level)) for v in BASE[size]]


def pierce(size, level, kind):
    six = stats(size, level)
    stat = six[2] if kind == "stab" else six[0]      # остриё ловкостью, прочее силой
    return stat * BITE[(size, kind)]


def opens(p):
    got = []
    for name, row in GUARD.items():
        best = None
        for i, g in enumerate(row):
            if g is not None and p >= g:
                best = i + 1
        if best:
            got.append("%s T%d" % (name, best))
    return ", ".join(got) if got else "nichego"


WHO = [
    ("Giant Rat",     "Small",  1,  "stab"),
    ("Bat",           "Small",  2,  "sharp"),
    ("Wolf",          "Medium", 5,  "sharp"),
    ("Boar",          "Medium", 7,  "stab"),
    ("DemonBat",      "Medium", 10, "sharp"),
    ("CorruptedWolf", "Medium", 34, "sharp"),
    ("Brown Bear",    "Large",  14, "sharp"),
    ("Giant Spider",  "Large",  28, "sharp"),
    ("Werewolf",      "Large",  31, "sharp"),
    ("Minotaur",      "Large",  35, "blunt"),
    ("Chimera",       "Large",  40, "sharp"),
    ("Elephant",      "Giant",  40, "stab"),
    ("Cyclops",       "Giant",  43, "blunt"),
    ("GiantSandWorm", "Giant",  46, "stab"),
]

print("%-16s %-7s %4s %-6s %6s %8s  %s" % (
    "zver", "size", "lvl", "udar", "stat", "probitie", "chto otkryvaet"))
print("-" * 118)

for name, size, level, kind in WHO:
    six = stats(size, level)
    stat = six[2] if kind == "stab" else six[0]
    p = pierce(size, level, kind)
    print("%-16s %-7s %4d %-6s %6d %8.0f  %s" % (name, size, level, kind, stat, p, opens(p)))
