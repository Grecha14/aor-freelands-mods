# -*- coding: utf-8 -*-
"""Вычет зверя по размеру, а не по процентам шкуры.

Мелочь и средние стоят на ровных числах — их дело не держать удар, а быть тем, на ком
качаются в начале. Крупное и исполинское крепчает с уровнем: это уже охота, и к ней
готовятся.
"""

FLAT = {"Small": 5, "Medium": 15}          # ровно, без роста
GROWN = {"Large": 60, "Giant": 150}        # основа, дальше растёт с уровнем

# Чем бьёт игрок на каждой ступени: стат и вес меча/топора/молота.
STAGES = [
    ("Т1", 10, 3.00, 4.00, 8.00),
    ("Т2", 15, 3.63, 4.84, 9.68),
    ("Т3", 25, 4.29, 5.72, 11.44),
    ("Т4", 35, 5.58, 7.44, 14.88),
    ("Т5", 45, 7.50, 10.00, 20.00),
]


def wall(size, level, growth):
    if size in FLAT:
        return FLAT[size]
    return GROWN[size] * (1 + level * growth)


def who_beats(value):
    got = []
    for tier, stat, sword, axe, hammer in STAGES:
        if stat * sword >= value:
            got.append(tier + " меч")
        elif stat * axe >= value:
            got.append(tier + " топор")
        elif stat * hammer >= value:
            got.append(tier + " молот")
    return ", ".join(got[:3]) if got else "никто"


WHO = [
    ("Крыса", "Small", 1), ("Волк", "Medium", 5), ("Кабан", "Medium", 7),
    ("Белый волк", "Medium", 11), ("Медведь", "Large", 14),
    ("Гигантский паук", "Large", 28), ("Оборотень", "Large", 31),
    ("Минотавр", "Large", 35), ("Химера", "Large", 40),
    ("Порченый волк", "Medium", 34),
    ("Слон", "Giant", 40), ("Циклоп", "Giant", 43),
    ("Король червей", "Large", 300), ("Дракон", "Giant", 300),
]

for growth in (0.010, 0.015, 0.020):
    print("\n=== рост %.1f%% за уровень ===" % (growth * 100))
    print("%-18s %-7s %5s %8s | %s" % ("зверь", "размер", "ур.", "вычет", "кто берёт"))
    for name, size, level in WHO:
        value = wall(size, level, growth)
        print("%-18s %-7s %5d %8.0f | %s" % (name, size, level, value, who_beats(value)))

print("\nчем бьёт игрок:")
for tier, stat, sword, axe, hammer in STAGES:
    print("  %-3s стат %2d: меч %4.0f  топор %4.0f  молот %4.0f" % (
        tier, stat, stat * sword, stat * axe, stat * hammer))
