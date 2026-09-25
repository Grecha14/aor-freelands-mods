# -*- coding: utf-8 -*-
"""Дракон: укус режущий, лапы дробящие. Сила 40, коготь 3 кг, мастерство 100.

Дробящее у нас проносит долю мимо вычета — у лат треть с небольшим, у кольчуги четыре пятых.
Оттого лапы достают там, где укус упирается в стену, и разница между ударами становится
видна не в числах урона, а в том, кого они пробивают.
"""
import io

STR = 40
CLAW = 3.0
MAST = 2.0
HERO_HP = 420

PIERCE = STR * CLAW * MAST
GROW = (1 + STR * 0.01) * MAST

# удар, основа, тип, скорость
HITS = [("укус",   63.0, "режущий",  0.40),
        ("лапа L", 75.0, "дробящий", 0.15),
        ("лапа R", 63.0, "дробящий", 0.25)]

# Доля дробящего, идущая мимо стены, по классу брони.
THROUGH = {"ткань": 1.0, "кожа": 1.0, "кольчуга": 0.8, "чешуя": 0.7,
           "полулаты": 0.5, "латы": 0.35}

ARM = [("голый", 0, "ткань"), ("кольчуга Т3", 108, "кольчуга"),
       ("кольчуга Т5", 350, "кольчуга"), ("чешуя Т5", 434, "чешуя"),
       ("латы Т4", 389, "латы"), ("полулаты Т5", 546, "полулаты"),
       ("латы Т5", 700, "латы")]

said = ["Дракон: сила %d, коготь %.1f, мастерство 100  ->  пробитие %.0f" % (STR, CLAW, PIERCE),
        ""]

for name, base, kind, speed in HITS:
    said.append("  %-7s %-9s основа %.0f -> удар %.0f, скорость %.2f" % (
        name, kind, base, base * GROW, speed))

said.append("")
said.append("%-14s %7s %9s | %-26s %8s %9s" % (
    "на герое", "вычет", "остаток", "доходит: укус / лапа / лапа", "в сек", "смерть"))
said.append("-" * 88)

for aname, wall, klass in ARM:
    left = max(0.0, wall - PIERCE)
    cells = []
    dps = 0.0

    for name, base, kind, speed in HITS:
        hit = base * GROW

        if kind == "дробящий":
            past = THROUGH[klass] if wall > 0 else 1.0
            always = hit * past
            stopped = hit * (1 - past)
            clean = always + max(0.0, stopped - left)
        else:
            clean = max(0.0, hit - left)

        cells.append("%.0f" % clean)
        dps += clean * speed

    said.append("%-14s %7d %9.0f | %-26s %8.0f %8s" % (
        aname, wall, left, " / ".join(cells), dps,
        "%.0f с" % (HERO_HP / dps) if dps > 0 else "—"))

said.append("")
said.append("у героя 420 здоровья; «в сек» считает все три удара вместе")
io.open("dragon40.txt", "w", encoding="utf-8").write("\n".join(said))
