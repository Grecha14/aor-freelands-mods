# -*- coding: utf-8 -*-
"""Оружие после двух правок: верной скорости замаха и общей лестницы.

И заодно проба: что будет, если урон оружия по тирам растёт не как в игре, а лестницей
Фибоначчи — всего в три с половиной раза и всего в пять.
"""
import io
import random

import arena as A

FIB = [0, 1, 2, 4, 7, 12]


def rung(total, tier, first):
    """Та же лестница, что у брони и пробития, нормированная на первую ступень класса."""
    L = [1 + (total - 1) * f / 12.0 for f in FIB]
    a = L[min(first, 5)]
    return L[min(tier, 5)] / a if a else 1.0


def first_tier(cls):
    # Нулевая ступень — это «Poor», хлам без урона; за начало лестницы её брать нельзя.
    have = [int(w["tier"][1:]) for w in A.GEAR["W"]
            if w["cls"] == cls and w["tier"][1:].isdigit() and int(w["tier"][1:]) >= 1]
    return min(have) if have else 1


def blade(cls, tier, total=None):
    """Оружие класса и ступени; при total урон переписывается по лестнице."""
    w = A.weapon(cls, tier)
    if w is None:
        return None
    if total is None:
        return w

    low = first_tier(cls)
    base = A.weapon(cls, low)
    if base is None:
        return w

    w = dict(w)
    w["hit"] = base["hit"] * rung(total, tier, low)
    return w


ARMS = [("кинжал", "Dagger"), ("меч", "Sword"), ("рапира", "Rapier"), ("сабля", "Sabre"),
        ("топор", "BattleAxe"), ("булава", "Mace"), ("копьё", "Spear"),
        ("двуручный меч", "GreatSword"), ("катана", "Katana"), ("эсток", "Estoc"),
        ("двуручный топор", "GreatAxe"), ("двуручный молот", "TwoHandHammer"),
        ("алебарда", "Halberd"), ("двуручное копьё", "TwoHandSpear")]

OUT = []


def say(line=""):
    OUT.append(line)


def table(stage_name, tier, total, tag):
    stage = [s for s in A.STAGES if s[0] == stage_name][0]
    made = [(ru, blade(cls, tier, total)) for ru, cls in ARMS]
    made = [(ru, b) for ru, b in made if b is not None]

    say("--- %s, тир %d, %s" % (stage_name, tier, tag))
    say("%-20s %7s %7s %8s %9s %10s" %
        ("оружие", "урон", "замах", "побед", "ударов", "с одного"))

    rank = []

    for ru, b in made:
        won = 0
        runs = 0

        for ru2, b2 in made:
            if b2 is b:
                continue

            mk = lambda bb=b: A.Man("x", stage[1], stage[2], stage[3], bb, "ChainArmor", tier)
            mk2 = lambda bb=b2: A.Man("y", stage[1], stage[2], stage[3], bb, "ChainArmor", tier)

            random.seed(13)
            share, mid, draw = A.duel(mk, mk2, runs=60)
            won += share
            runs += 1

        man = A.Man("x", stage[1], stage[2], stage[3], b, "ChainArmor", tier)
        foe = A.Man("y", stage[1], stage[2], stage[3], b, "ChainArmor", tier)

        random.seed(17)
        blows = [A.swing(man, foe) for _ in range(4000)]
        landed = [x for x in blows if x > 0]
        big = 100.0 * len([x for x in landed if x >= foe.hpmax * 0.5]) / max(1, len(landed))
        need = foe.hpmax / (sum(landed) / len(landed)) if landed else 0

        rank.append((won / max(1, runs), ru, b, need, big, man.rate))

    rank.sort(reverse=True)

    for share, ru, b, need, big, rate in rank:
        say("%-20s %7.0f %7.2f %7.0f%% %9.0f %9.0f%%" %
            (ru, b["hit"], rate, share, need, big))

    say()


for total, tag in ((None, "урон как в игре"), (3.5, "урон лестницей ×3.5"),
                   (5.0, "урон лестницей ×5")):
    say("=" * 78)
    say("ОРУЖИЕ: %s" % tag)
    say("«побед» — среднее по кругу против всех прочих; «ударов» — сколько прошедших ударов")
    say("нужно на убийство равного; «с одного» — доля ударов, снимающих половину здоровья.")
    say()

    for name, tier in (("начало", 2), ("середина", 3), ("конец", 4)):
        table(name, tier, total, tag)

io.open("arena_arms.txt", "w", encoding="utf-8").write("\n".join(OUT))
print("строк:", len(OUT))
