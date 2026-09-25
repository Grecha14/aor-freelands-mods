# -*- coding: utf-8 -*-
"""Лавовый дракон: физика и огонь по отдельности, и десять вариантов уреза физики.

Огонь нашей стены не встречает — он идёт к огнестойкости доспеха и к тому, что на шее и
пальцах. Оттого резать физику можно смело: дракон всё равно жжёт.
"""
import io

STR = 40
CLAW = 3.0
MAST = 2.0
HERO_HP = 420

PIERCE = STR * CLAW * MAST
GROW = (1 + STR * 0.01) * MAST

# лавовый: удар, физика (середина), огонь (середина), тип физики, скорость
HITS = [("укус",   36.5, 36.5, "режущий",  0.40),
        ("лапа L", 33.0, 33.0, "дробящий", 0.15),
        ("лапа R", 33.0, 33.0, "дробящий", 0.25)]

THROUGH = {"ткань": 1.0, "кольчуга": 0.8, "чешуя": 0.7, "полулаты": 0.5, "латы": 0.35}
FIRE_RESIST = 0.35          # огнестойкость лат Т4 с амулетом, доля

WALL, KLASS = 389, "латы"   # латы Т4 — лучшее до дракона

said = []
said.append("ЛАВОВЫЙ ДРАКОН")
said.append("  сила %d, коготь %.1f кг, мастерство 100" % (STR, CLAW))
said.append("  ПРОБИТИЕ БРОНИ  %.0f" % PIERCE)
said.append("  вычет самого дракона  700")
said.append("")
said.append("%-8s %-9s %9s %9s %8s" % ("удар", "тип", "физика", "огонь", "скорость"))

for name, phys, fire, kind, speed in HITS:
    said.append("%-8s %-9s %9.0f %9.0f %8.2f" % (
        name, kind, phys * GROW, fire * GROW, speed))

said.append("")
said.append("на герое: латы Т4, вычет %d, огнестойкость %.0f%%" % (WALL, FIRE_RESIST * 100))
said.append("")


def clean(cut):
    """Сколько доходит в секунду: физика сквозь стену, огонь сквозь стойкость."""
    phys_sum = 0.0
    fire_sum = 0.0
    left = max(0.0, WALL - PIERCE)

    for name, phys, fire, kind, speed in HITS:
        p = phys * cut * GROW

        if kind == "дробящий":
            past = THROUGH[KLASS]
            got = p * past + max(0.0, p * (1 - past) - left)
        else:
            got = max(0.0, p - left)

        phys_sum += got * speed
        fire_sum += fire * GROW * (1 - FIRE_RESIST) * speed

    return phys_sum, fire_sum


said.append("%-8s %10s %10s %10s %10s" % (
    "урез", "физика/с", "огонь/с", "всего/с", "смерть"))
said.append("-" * 54)

for cut in (1.00, 0.90, 0.80, 0.70, 0.60, 0.50, 0.40, 0.30, 0.20, 0.10):
    p, f = clean(cut)
    total = p + f
    said.append("x%-7.2f %10.0f %10.0f %10.0f %9.1f с" % (
        cut, p, f, total, HERO_HP / total if total else 0))

io.open("lava.txt", "w", encoding="utf-8").write("\n".join(said))
