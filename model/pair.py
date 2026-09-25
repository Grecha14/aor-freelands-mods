# -*- coding: utf-8 -*-
"""Свести обычное и легендарное оружие Т4 по классу — что игра даёт за легендарность."""
import io, re

s = io.open("items_dump.txt", encoding="utf-8", errors="replace").read().split("\n")

head = re.compile(r"\[(\d+)\] (\S+).*?тип Weapon, качество (\w+), ступень T4")
wt   = re.compile(r"вес (\d+(?:,\d+)?),")
dmg  = re.compile(r"бьёт: (\w+) (\d+)-(\d+)")
kind = re.compile(r"оружие (\w+)/(\w+)")
frc  = re.compile(r"сила (\d+)")
spd  = re.compile(r"скорость (\d+(?:,\d+)?)")

rows = {}
for l in s:
    h = head.search(l)
    if not h:
        continue
    w, d, k, f, v = wt.search(l), dmg.search(l), kind.search(l), frc.search(l), spd.search(l)
    if not (w and d and k):
        continue
    rows.setdefault(k.group(2), {}).setdefault(h.group(3), []).append({
        "name":  h.group(2).replace("items_Equipments_Weapons_", ""),
        "kg":    float(w.group(1).replace(",", ".")),
        "lo":    int(d.group(2)),
        "hi":    int(d.group(3)),
        "force": int(f.group(1)) if f else 0,
        "speed": float(v.group(1).replace(",", ".")) if v else 0.0,
    })

LADDER = {4: 1.86, 5: 2.50}   # ступень Фибоначчи: Т4 и легендарное (оно же пятая)

print("%-13s %-26s %5s %9s %6s  %7s %7s" % ("class", "item", "kg", "damage", "force", "kg new", "x T4"))
shown = 0
for cls in sorted(rows):
    pair = rows[cls]
    if "Common" not in pair or "Legendary" not in pair:
        continue
    c = sorted(pair["Common"], key=lambda x: -x["hi"])[0]
    g = sorted(pair["Legendary"], key=lambda x: -x["hi"])[0]

    ck = c["kg"] * LADDER[4]
    gk = g["kg"] * LADDER[5]

    print("%-13s %-26s %5.1f %4d-%-4d %6d  %7.2f" % (
        cls, c["name"][:26], c["kg"], c["lo"], c["hi"], c["force"], ck))
    print("%-13s %-26s %5.1f %4d-%-4d %6d  %7.2f %7.2f" % (
        "", g["name"][:26], g["kg"], g["lo"], g["hi"], g["force"], gk, gk / ck))
    print("%-13s %-26s %5s %4.0f%%      %4.0f%%" % (
        "", "-> легендарка даёт", "", 100.0 * g["hi"] / c["hi"], 100.0 * g["force"] / max(1, c["force"])))
    print()

    shown += 1
    if shown >= 8:
        break
