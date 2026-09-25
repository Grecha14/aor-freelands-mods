# -*- coding: utf-8 -*-
"""Heft на переписанном весе против игрового: что меняется в уроне.

Heft даёт оружию долю урона за килограмм. Smithy переписал вес по лестнице тира, и Heft
читает уже переписанный — оттого ступень качает не только пробитие, но и урон.
"""
import io
import re

PER_KILO = 0.10                      # Heft.PerKilo
SWINGS = {"sharp": 0.6, "blunt": 1.0, "stab": 0.4}   # Heft.Swings
MOST = 3.0                           # Heft.Most — потолок
LADDER = {"T1": 1.00, "T2": 1.21, "T3": 1.43, "T4": 1.86, "LEG": 2.50}

head = re.compile(r"\[(\d+)\] (\S+).*?тип Weapon, качество (\w+), ступень (T\d)")
wt = re.compile(r"вес (\d+(?:,\d+)?),")
dmg = re.compile(r"бьёт: (\w+) (\d+)-(\d+)")
kind = re.compile(r"оружие (\w+)/(\w+)")

rows = {}
for line in io.open("items_dump.txt", encoding="utf-8", errors="replace").read().split("\n"):
    h = head.search(line)
    if not h:
        continue
    w, d, k = wt.search(line), dmg.search(line), kind.search(line)
    if not (w and d and k):
        continue

    rung = "LEG" if h.group(3) == "Legendary" else h.group(4)
    if rung not in LADDER:
        continue

    rows.setdefault(k.group(2), {})[rung] = {
        "kg": float(w.group(1).replace(",", ".")),
        "type": d.group(1),
        "lo": int(d.group(2)),
        "hi": int(d.group(3)),
    }


def heft(kg, kind_name):
    swing = SWINGS.get(kind_name, 0.6)
    return min(MOST, max(1.0, 1.0 + kg * PER_KILO * swing))


WANT = ["Dagger", "Sword", "BastardSword", "TwoHandSword", "BattleAxe", "DoubleAxe",
        "Mace", "TwoHandMace", "Spear", "Polearm", "TwoHandAxe", "Club"]

print("%-14s %-4s %-6s %6s %6s  %6s %6s  %8s %8s %7s" % (
    "oruzhie", "tier", "type", "kg igr", "kg nov", "Heft i", "Heft n", "uron igr", "uron nov", "rost"))
print("-" * 96)

worst = []
for name in WANT:
    if name not in rows:
        continue
    for rung in ("T1", "T2", "T3", "T4", "LEG"):
        if rung not in rows[name]:
            continue
        r = rows[name][rung]
        kg_old = r["kg"]
        kg_new = kg_old * LADDER[rung]

        h_old = heft(kg_old, r["type"])
        h_new = heft(kg_new, r["type"])

        mid = (r["lo"] + r["hi"]) / 2.0
        d_old = mid * h_old
        d_new = mid * h_new
        grow = 100.0 * (d_new / d_old - 1)

        print("%-14s %-4s %-6s %6.1f %6.2f  %6.2f %6.2f  %8.1f %8.1f %6.0f%%" % (
            name, rung, r["type"], kg_old, kg_new, h_old, h_new, d_old, d_new, grow))
        worst.append((grow, name, rung))
    print()

worst.sort(reverse=True)
print("silnee vsego vyrosli:")
for g, n, t in worst[:6]:
    print("  %-14s %-4s +%.0f%%" % (n, t, g))
print("\nmenshe vsego:")
for g, n, t in worst[-4:]:
    print("  %-14s %-4s +%.0f%%" % (n, t, g))
