# -*- coding: utf-8 -*-
"""Статы зверью по размеру и уровню, и что из этого выходит с пробитием.

Сейчас пробитие зверя считается как Beast(10) * сила_удара/30, а силу удара авторы загнали
до двух тысяч — оттого червь пробивает 667 и проходит сквозь латы. Со своими статами формула
станет той же, что у человека: сила * вес того, чем бьют.
"""
import io
import re

# --- статы по размеру: основа и прирост за уровень -----------------------
BASE = {          # сила  вын  лов  точн  ум  воля
    "Small":  (3,   3,  14,  12,  2,  2),
    "Medium": (10, 10,  10,  10,  3,  3),
    "Large":  (25, 28,   6,   8,  4,  4),
    "Giant":  (45, 50,   3,   6,  5,  5),
}
STEP = {"Small": 0.15, "Medium": 0.35, "Large": 0.60, "Giant": 0.80}

# «Вес когтя» — чем зверь бьёт, в килограммах. Заменяет силу_удара/30.
CLAW = {"Small": 0.4, "Medium": 1.6, "Large": 4.0, "Giant": 7.0}

# Вычет корпуса по классам (из свода), Т1..Т5
GUARD = {
    "tkan":      [12, 14, 17, 21, 25],
    "kozha":     [30, 36, 43, 52, 62],
    "kolchuga":  [None, 60, 108, 194, 350],
    "cheshuya":  [None, 74, 134, 241, 434],
    "poluliaty": [None, 94, 168, 303, 546],
    "laty":      [None, 120, 216, 389, 700],
}


def stats(size, level):
    if size not in BASE:
        size = "Medium"
    step = STEP[size]
    return [int(round(v + step * level)) for v in BASE[size]]


def pierce(size, level):
    return stats(size, level)[0] * CLAW.get(size, 1.6)


def breaks(p):
    """Что это пробитие открывает по корпусу."""
    got = []
    for name, row in GUARD.items():
        best = None
        for i, g in enumerate(row):
            if g is not None and p >= g:
                best = "T%d" % (i + 1)
        if best:
            got.append("%s %s" % (name, best))
    return ", ".join(got) if got else "nichego"


# --- настоящие звери из переписи -----------------------------------------
path = r"C:\Program Files (x86)\Steam\steamapps\common\Age of Reforging The Freelands\BepInEx\ЗВЕРИНЕЦ.txt"
rows = []
for line in io.open(path, encoding="utf-8", errors="replace").read().split("\n"):
    if not line.startswith("["):
        continue
    name = re.match(r"\[(\d+)\] ([^|(]+)", line)
    lvl = re.search(r"уровень (\d+)", line)
    size = re.search(r"размер (\w+)", line)
    force = re.search(r"сила ([\d,]+)", line)
    if not (name and lvl and size):
        continue
    rows.append((name.group(2).strip(), int(lvl.group(1)), size.group(1),
                 float(force.group(1).replace(",", ".")) if force else 0.0))

WATCH = ["Giant Rat", "Wolf", "Bat", "Boar", "Brown Bear", "Giant Spider",
         "Werewolf", "Minotaur", "Manticore", "Cyclops", "GiantSandWorm", "Chimera"]

print("%-20s %-7s %5s | %6s %6s | %8s %9s | %s" % (
    "zver", "size", "lvl", "sila", "vyn", "bylo", "stalo", "chto probivaet"))
print("-" * 118)

for name, lvl, size, force in sorted(rows, key=lambda r: (r[2], r[1])):
    if not any(w.lower() in name.lower() for w in WATCH):
        continue
    six = stats(size, lvl)
    was = 10.0 * force / 30.0
    now = pierce(size, lvl)
    print("%-20s %-7s %5d | %6d %6d | %8.0f %9.0f | %s" % (
        name[:20], size, lvl, six[0], six[1], was, now, breaks(now)))
