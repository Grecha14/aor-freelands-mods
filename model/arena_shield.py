# -*- coding: utf-8 -*-
"""Одноручные со щитом против двуручных, и щиты между собой."""
import io
import random

import arena as A

OUT = []


def say(line=""):
    OUT.append(line)


ONE = [("кинжал", "Dagger"), ("меч", "Sword"), ("сабля", "Sabre"), ("рапира", "Rapier"),
       ("топор", "BattleAxe"), ("булава", "Mace"), ("копьё", "Spear")]

TWO = [("двуручный меч", "GreatSword"), ("катана", "Katana"), ("эсток", "Estoc"),
       ("двуручный топор", "GreatAxe"), ("двуручный молот", "TwoHandHammer"),
       ("алебарда", "Halberd"), ("двуручное копьё", "TwoHandSpear")]

SHIELDS = [("баклер", "Buckler"), ("круглый", "RoundShield"), ("каплевидный", "KiteShield"),
           ("ростовой", "TowerShield")]


def man(stage, arm, tier, coat, shield=None):
    return lambda: A.Man(stage[0], stage[1], stage[2], stage[3], arm, coat, tier, shield)


say("=" * 78)
say("ЩИТ ПРОТИВ ДВУХ РУК")
say("Слева одноручное с каплевидным щитом, сверху двуручное. Оба в кольчуге своей ступени.")
say("Число — побед из ста у строки.")
say()

for name, tier in (("начало", 2), ("середина", 3), ("конец", 4)):
    stage = [s for s in A.STAGES if s[0] == name][0]
    shield = A.shield("KiteShield", tier)

    say("--- %s, тир %d" % (name, tier))
    head = "%-14s" % ""
    for ru, cls in TWO:
        head += "%17s" % ru
    say(head)

    for ru_a, cls_a in ONE:
        arm = A.weapon(cls_a, tier)
        if arm is None:
            continue

        line = "%-14s" % ru_a
        for ru_b, cls_b in TWO:
            other = A.weapon(cls_b, tier)
            if other is None:
                line += "%17s" % "нет"
                continue

            random.seed(21)
            share, mid, draw = A.duel(man(stage, arm, tier, "ChainArmor", shield),
                                      man(stage, other, tier, "ChainArmor"), runs=120)
            line += "%16.0f%%" % share
        say(line)
    say()

say("=" * 78)
say("ЧТО ДАЁТ САМ ЩИТ: меч со щитом против меча без щита, оба в кольчуге")
say()
say("%-16s %-10s %10s %10s" % ("щит", "этап", "побед", "бой, с"))

for ru, cls in SHIELDS:
    for name, tier in (("начало", 2), ("середина", 3), ("конец", 4)):
        stage = [s for s in A.STAGES if s[0] == name][0]
        arm = A.weapon("Sword", tier)
        shield = A.shield(cls, tier)

        random.seed(23)
        share, mid, draw = A.duel(man(stage, arm, tier, "ChainArmor", shield),
                                  man(stage, arm, tier, "ChainArmor"), runs=200)
        say("%-16s %-10s %9.0f%% %10.1f" % (ru, name, share, mid))
    say()

say("=" * 78)
say("ЩИТ ПРОТИВ ЧИСЛА: один со щитом против двоих и троих без щитов, всё прочее равно")
say()
say("%-16s %10s %10s" % ("этап", "1 против 2", "1 против 3"))

for name, tier in (("начало", 2), ("середина", 3), ("конец", 4)):
    stage = [s for s in A.STAGES if s[0] == name][0]
    arm = A.weapon("Sword", tier)
    shield = A.shield("KiteShield", tier)

    line = "%-16s" % name
    for n in (2, 3):
        random.seed(29)
        share, mid, draw = A.duel(man(stage, arm, tier, "ChainArmor", shield),
                                  man(stage, arm, tier, "ChainArmor"), 1, n, runs=150)
        line += "%9.0f%%" % share
    say(line)

say()
say("=" * 78)
say("ЧИСЛА ОДНОГО БОЙЦА СО ЩИТОМ (середина, Т3)")
stage = [s for s in A.STAGES if s[0] == "середина"][0]
arm = A.weapon("Sword", 3)

for ru, cls in SHIELDS:
    sh = A.shield(cls, 3)
    m = A.Man("x", stage[1], stage[2], stage[3], arm, "ChainArmor", 3, sh)
    say("%-16s вес %4.1f, блок %3.0f%%, запас сил %3.0f, восполнение %.1f/с, замах %.2f"
        % (ru, sh["kg"], m.block, m.spmax, m.sprest, m.rate))

m = A.Man("x", stage[1], stage[2], stage[3], arm, "ChainArmor", 3)
say("%-16s блок %3.0f%%, замах %.2f" % ("без щита", m.block, m.rate))

for ru, cls in TWO:
    b = A.weapon(cls, 3)
    if b:
        say("    %s ломает блок на %.0f" % (ru, A.breaks(b)))

io.open("arena_shield.txt", "w", encoding="utf-8").write("\n".join(OUT))
print("строк:", len(OUT))
