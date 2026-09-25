# -*- coding: utf-8 -*-
"""Дракон против того доспеха, который у игрока есть ДО дракона.

Драконьи латы с дракона и падают, надеть их заранее нельзя. Значит верхняя планка,
на которую стоит целиться, — около пятисот вычета, а не семисот.
"""
import io

CLAW = 3.0
MAST = 2.0
HERO_HP = 420

HITS = [("укус", 63.0, "режущий", 0.40),
        ("лапа L", 75.0, "дробящий", 0.15),
        ("лапа R", 63.0, "дробящий", 0.25)]

THROUGH = {"ткань": 1.0, "кожа": 1.0, "кольчуга": 0.8,
           "чешуя": 0.7, "полулаты": 0.5, "латы": 0.35}

# То, что игрок может носить до дракона.
ARM = [("голый", 0, "ткань"), ("кольчуга Т4", 194, "кольчуга"),
       ("латы Т3", 216, "латы"), ("чешуя Т4", 241, "чешуя"),
       ("полулаты Т4", 303, "полулаты"), ("кольчуга Т5", 350, "кольчуга"),
       ("латы Т4", 389, "латы"), ("чешуя Т5", 434, "чешуя"),
       ("полулаты Т5", 500, "полулаты")]


def beat(strength, double, wall, klass):
    pierce = strength * CLAW * MAST
    grow = (1 + strength * 0.01) * MAST
    left = max(0.0, wall - pierce)
    dps = 0.0

    for name, base, kind, speed in HITS:
        hit = base * double * grow

        if kind == "дробящий" and wall > 0:
            past = THROUGH[klass]
            clean = hit * past + max(0.0, hit * (1 - past) - left)
        else:
            clean = max(0.0, hit - left)

        dps += clean * speed

    return dps


said = []

for strength, double in ((40, 1.0), (40, 2.0), (60, 1.5), (80, 1.0)):
    pierce = strength * CLAW * MAST
    grow = (1 + strength * 0.01) * MAST

    said.append("=== сила %d, основа x%.1f -> пробитие %.0f, удары %.0f / %.0f / %.0f ===" % (
        strength, double, pierce,
        63 * double * grow, 75 * double * grow, 63 * double * grow))
    said.append("%-14s %7s %8s %9s %8s" % ("на герое", "вычет", "в сек", "смерть", "срезано"))

    naked = beat(strength, double, 0, "ткань")

    for name, wall, klass in ARM:
        dps = beat(strength, double, wall, klass)
        said.append("%-14s %7d %8.0f %8.1f с %7.0f%%" % (
            name, wall, dps, HERO_HP / dps if dps > 0 else 0,
            100 * (1 - dps / naked) if naked else 0))

    said.append("")

io.open("dragon500.txt", "w", encoding="utf-8").write("\n".join(said))
