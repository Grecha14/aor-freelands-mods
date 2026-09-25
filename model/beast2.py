# -*- coding: utf-8 -*-
"""Статы зверью: ведёт размер, а не тип удара.

Мелкое и среднее гонит урон ловкостью, крупное силой. Выводы — те же, что игра делает
человеку, оттого у вёрткого сама собой растёт скорость атаки.
"""

GUARD = {
    "tkan":      [12, 14, 17, 21, 25],
    "kozha":     [30, 36, 43, 52, 62],
    "kolchuga":  [None, 60, 108, 194, 350],
    "cheshuya":  [None, 74, 134, 241, 434],
    "poluliaty": [None, 94, 168, 303, 546],
    "laty":      [None, 120, 216, 389, 700],
}

#            сила  вын  лов  точн  ум  воля
BASE = {
    "Small":  (3,   4,  16,  12,  2,  2),
    "Medium": (8,  10,  18,  10,  3,  3),
    "Large":  (30, 26,   8,   8,  4,  4),
    "Giant":  (50, 40,   4,   6,  5,  5),
}
STEP = {          # прирост за уровень, по тем же шести
    "Small":  (0.05, 0.08, 0.30, 0.20, 0.03, 0.03),
    "Medium": (0.12, 0.20, 0.40, 0.20, 0.05, 0.05),
    "Large":  (0.55, 0.45, 0.10, 0.12, 0.06, 0.06),
    "Giant":  (0.80, 0.60, 0.05, 0.10, 0.08, 0.08),
}

# Ловкость ведёт у мелкого и среднего, сила — у крупного.
LEADS = {"Small": 2, "Medium": 2, "Large": 0, "Giant": 0}

# Во что ведущий стат превращается пробитием. Одно число на размер.
REACH = {"Small": 3.0, "Medium": 3.2, "Large": 3.8, "Giant": 6.5}


def stats(size, level):
    return [int(round(b + s * level)) for b, s in zip(BASE[size], STEP[size])]


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
    ("Giant Rat", "Small", 1), ("Bat", "Small", 2),
    ("Wolf", "Medium", 5), ("Boar", "Medium", 7), ("DemonBat", "Medium", 10),
    ("CorruptedWolf", "Medium", 34),
    ("Brown Bear", "Large", 14), ("Giant Spider", "Large", 28),
    ("Werewolf", "Large", 31), ("Minotaur", "Large", 35), ("Chimera", "Large", 40),
    ("Elephant", "Giant", 40), ("Cyclops", "Giant", 43), ("GiantSandWorm", "Giant", 46),
]

print("%-15s %-7s %4s | %4s %4s %4s | %5s %8s %7s | %s" % (
    "zver", "size", "lvl", "sil", "vyn", "lov", "vedet", "probitie", "+skor", "otkryvaet"))
print("-" * 125)

for name, size, level in WHO:
    six = stats(size, level)
    lead = LEADS[size]
    p = six[lead] * REACH[size]
    speed = six[2]                       # AttackSpeedMD += Lovkost * 0.01
    print("%-15s %-7s %4d | %4d %4d %4d | %5s %8.0f %6d%% | %s" % (
        name, size, level, six[0], six[1], six[2],
        "lov" if lead == 2 else "sila", p, speed, opens(p)))

print("\nrost stata za 20 urovney (vedushchiy):")
for size in ("Small", "Medium", "Large", "Giant"):
    a = stats(size, 1)[LEADS[size]]
    b = stats(size, 21)[LEADS[size]]
    print("  %-7s %3d -> %3d   (probitie %4.0f -> %4.0f)" % (
        size, a, b, a * REACH[size], b * REACH[size]))
