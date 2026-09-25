# -*- coding: utf-8 -*-
"""Тело по частям: что получилось и как теперь идут бои."""
import io
import random

import arena as A

OUT = []


def say(line=""):
    OUT.append(line)


say("=" * 78)
say("ТЕЛО ПО ЧАСТЯМ")
say("Своё здоровье у каждой части; выносливость прибавляет единицу туловищу, половину")
say("руке и ноге, четверть голове; уровень — по половине всем.")
say()
say("%-12s %6s %8s %9s %9s %9s %8s" %
    ("этап", "вын.", "голова", "туловище", "рука", "нога", "всего"))

for stage in A.STAGES:
    blade = A.weapon("Sword", 3)
    m = A.Man(stage[0], stage[1], stage[2], stage[3], blade, "ChainArmor", 3)

    say("%-12s %6d %8.0f %9.0f %9.0f %9.0f %8.0f" %
        (stage[0], stage[2][1], m.limbmax["голова"], m.limbmax["туловище"],
         m.limbmax["рука правая"], m.limbmax["нога правая"], m.hpmax))

say()
say("было по старому счёту (100 + уровень × 5 + 5 × выносливости): 200 / 335 / 525")
say()

say("=" * 78)
say("КУДА И СКОЛЬКО ДОХОДИТ (середина Т3, меч против кольчуги)")
say()

stage = [s for s in A.STAGES if s[0] == "середина"][0]
blade = A.weapon("Sword", 3)
a = A.Man("x", stage[1], stage[2], stage[3], blade, "ChainArmor", 3)
b = A.Man("y", stage[1], stage[2], stage[3], blade, "ChainArmor", 3)

random.seed(31)
tally = {}
for _ in range(20000):
    much, part = A.swing(a, b)
    if part is None:
        continue
    got = tally.setdefault(part, [0, 0.0])
    got[0] += 1
    got[1] += much

say("%-14s %8s %10s %10s %12s" % ("часть", "прикрыта", "попаданий", "средний", "ударов насмерть"))
for part in A.PARTS:
    n, sum_ = tally.get(part, [0, 0.0])
    mid = sum_ / n if n else 0
    wall = b.cover.get(part) or 0
    need = b.limbmax[part] / mid if mid > 0 else 0
    say("%-14s %8.0f %10d %10.1f %12.0f" % (part, wall, n, mid, need))

say()
say("=" * 78)
say("БОИ НА НОВОМ ТЕЛЕ")
say()

say("--- одноручное со щитом против двуручного (побед из ста у щитоносца)")
say("%-16s %10s %10s %10s" % ("", "начало", "середина", "конец"))

pairs = [("меч + щит", "Sword", "двуручный меч", "GreatSword"),
         ("копьё + щит", "Spear", "алебарда", "Halberd"),
         ("булава + щит", "Mace", "двуручный молот", "TwoHandHammer")]

for ru_a, cls_a, ru_b, cls_b in pairs:
    line = "%-16s" % (ru_a + " / " + ru_b)
    for name, tier in (("начало", 2), ("середина", 3), ("конец", 4)):
        st = [s for s in A.STAGES if s[0] == name][0]
        arm = A.weapon(cls_a, tier)
        other = A.weapon(cls_b, tier)
        sh = A.shield("KiteShield", tier)

        if arm is None or other is None:
            line += "%11s" % "нет"
            continue

        random.seed(37)
        share, mid, draw = A.duel(
            lambda: A.Man("x", st[1], st[2], st[3], arm, "ChainArmor", tier, sh),
            lambda: A.Man("y", st[1], st[2], st[3], other, "ChainArmor", tier), runs=150)
        line += "%10.0f%%" % share
    say(line)

say()
say("--- доспех против доспеха, меч у обоих (побед из ста у строки)")

COATS = [("ткань", "Cloth"), ("твёрдая кожа", "HardLeaterArmor"), ("кольчуга", "ChainArmor"),
         ("чешуя", "ScaleMail"), ("латы", "PlateArmor")]

for name, tier in (("начало", 2), ("середина", 3), ("конец", 4)):
    st = [s for s in A.STAGES if s[0] == name][0]
    blade = A.weapon("Sword", tier)

    say("%s, тир %d" % (name, tier))
    head = "%-14s" % ""
    for ru, cls in COATS:
        head += "%14s" % ru
    say(head)

    for ru_a, cls_a in COATS:
        if A.guard(cls_a, tier, "chest") is None:
            continue
        line = "%-14s" % ru_a
        for ru_b, cls_b in COATS:
            if A.guard(cls_b, tier, "chest") is None:
                line += "%14s" % "—"
                continue
            if cls_a == cls_b:
                line += "%14s" % "—"
                continue

            random.seed(41)
            share, mid, draw = A.duel(
                lambda: A.Man("x", st[1], st[2], st[3], blade, cls_a, tier),
                lambda: A.Man("y", st[1], st[2], st[3], blade, cls_b, tier), runs=150)
            line += "%13.0f%%" % share
        say(line)
    say()

io.open("arena_body.txt", "w", encoding="utf-8").write("\n".join(OUT))
print("строк:", len(OUT))
