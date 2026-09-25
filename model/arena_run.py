# -*- coding: utf-8 -*-
"""Прогон испытаний и отчёт по ним."""
import io

import arena as A

OUT = []


def say(line=""):
    OUT.append(line)


def make(stage, arm_cls, arm_tier, coat_cls, coat_tier):
    name, level, six, mastery = stage
    blade = A.weapon(arm_cls, arm_tier)
    if blade is None:
        return None
    return lambda: A.Man(name, level, six, mastery, blade, coat_cls, coat_tier)


# ---------------------------------------------------------------- 1. броня

say("=" * 78)
say("1. ОДИН НА ОДИН: та же рука, разный доспех")
say("Оба с мечом своей ступени, статы одинаковые. Слева тот, чей доспех в строке,")
say("справа тот, чей в столбце. Число — сколько побед из ста у строки.")
say()

for stage in A.STAGES:
    tier = {"начало": 2, "середина": 3, "конец": 4}[stage[0]]

    say("--- %s (уровень %d, тир вещей %d)" % (stage[0], stage[1], tier))
    head = "%-14s" % ""
    for ru, cls in A.COATS:
        if A.guard(cls, tier, "chest") is None:
            continue
        head += "%12s" % ru
    say(head)

    for ru_a, cls_a in A.COATS:
        if A.guard(cls_a, tier, "chest") is None:
            continue

        line = "%-14s" % ru_a
        for ru_b, cls_b in A.COATS:
            if A.guard(cls_b, tier, "chest") is None:
                continue

            if cls_a == cls_b:
                line += "%12s" % "—"
                continue

            share, mid, draw = A.duel(make(stage, "Sword", tier, cls_a, tier),
                                      make(stage, "Sword", tier, cls_b, tier), runs=200)
            line += "%11.0f%%" % share

        say(line)
    say()

# ---------------------------------------------------------------- 2. оружие

say("=" * 78)
say("2. ОДИН НА ОДИН: тот же доспех, разное оружие")
say("Обе стороны в кольчуге своей ступени. Число — побед из ста у строки против столбца.")
say()

for stage in A.STAGES:
    tier = {"начало": 2, "середина": 3, "конец": 4}[stage[0]]

    say("--- %s (тир вещей %d)" % (stage[0], tier))
    head = "%-18s" % ""
    for ru, cls in A.ARMS:
        head += "%14s" % ru
    say(head)

    for ru_a, cls_a in A.ARMS:
        line = "%-18s" % ru_a
        for ru_b, cls_b in A.ARMS:
            if cls_a == cls_b:
                line += "%14s" % "—"
                continue

            left = make(stage, cls_a, tier, "ChainArmor", tier)
            right = make(stage, cls_b, tier, "ChainArmor", tier)

            if left is None or right is None:
                line += "%14s" % "нет"
                continue

            share, mid, draw = A.duel(left, right, runs=200)
            line += "%13.0f%%" % share

        say(line)
    say()

# ---------------------------------------------------------------- 3. ступени

say("=" * 78)
say("3. СТУПЕНЬ ПРОТИВ СТУПЕНИ: всё одинаково, кроме тира")
say("Оба с мечом и в кольчуге. Строка — тир левого, столбец — тир правого.")
say()

for stage in A.STAGES[1:2]:
    # Оружия выше четвёртой ступени в игре нет: пятая — это легендарное качество на
    # четвёртой. Броня при этом доходит до пятой, и ниже второй кольчуги не бывает.
    head = "%-10s" % ""
    for t in range(2, 5):
        head += "%10s" % ("Т%d" % t)
    say(head)

    for ta in range(2, 5):
        line = "%-10s" % ("Т%d" % ta)
        for tb in range(2, 5):
            if ta == tb:
                line += "%10s" % "—"
                continue

            left, right = (make(stage, "Sword", ta, "ChainArmor", ta),
                           make(stage, "Sword", tb, "ChainArmor", tb))

            if left is None or right is None:
                line += "%10s" % "нет"
                continue

            share, mid, draw = A.duel(left, right, runs=200)
            line += "%9.0f%%" % share
        say(line)
    say()

# ---------------------------------------------------------------- 4. числа

say("=" * 78)
say("4. ЧИСЛА: сколько стоит лишний боец")
say("Все одинаковые: меч и кольчуга своей ступени. Число — побед из ста у левой стороны.")
say()

say("%-12s %10s %10s %10s %10s %10s" % ("этап", "1 на 1", "2 на 2", "3 на 3", "1 на 2", "1 на 3"))

for stage in A.STAGES:
    tier = {"начало": 2, "середина": 3, "конец": 4}[stage[0]]
    same = make(stage, "Sword", tier, "ChainArmor", tier)

    line = "%-12s" % stage[0]
    for n_a, n_b in ((1, 1), (2, 2), (3, 3), (1, 2), (1, 3)):
        share, mid, draw = A.duel(same, same, n_a, n_b, runs=200)
        line += "%10.0f%%" % share
    say(line)

say()

# ---------------------------------------------------------------- 5. неравные

say("=" * 78)
say("5. МОЖЕТ ЛИ СНАРЯЖЕНИЕ ПЕРЕВЕСИТЬ ЧИСЛО")
say("Слева один в латах с двуручным молотом, справа — двое и трое в ткани с мечом.")
say("Ступени те же. Число — побед из ста у одиночки.")
say()

say("%-12s %14s %14s" % ("этап", "1 против 2", "1 против 3"))

for stage in A.STAGES:
    tier = {"начало": 2, "середина": 3, "конец": 4}[stage[0]]

    strong = make(stage, "TwoHandHammer", tier, "PlateArmor", max(tier, 2))
    weak = make(stage, "Sword", tier, "Cloth", tier)

    line = "%-12s" % stage[0]
    for n in (2, 3):
        if strong is None or weak is None:
            line += "%13s" % "нет"
            continue
        share, mid, draw = A.duel(strong, weak, 1, n, runs=200)
        line += "%13.0f%%" % share
    say(line)

say()

# ---------------------------------------------------------------- 6. числа боя

say("=" * 78)
say("6. ЧТО ЗА ЭТИМ СТОИТ: голые числа одного бойца")
say()

for stage in A.STAGES:
    tier = {"начало": 2, "середина": 3, "конец": 4}[stage[0]]
    say("--- %s, тир %d" % (stage[0], tier))
    say("%-16s %8s %8s %8s %8s %8s %8s" %
        ("доспех", "вес", "вычет", "уворот", "меткость", "замах", "здор."))

    for ru, cls in A.COATS:
        if A.guard(cls, tier, "chest") is None:
            continue

        blade = A.weapon("Sword", tier)
        man = A.Man(stage[0], stage[1], stage[2], stage[3], blade, cls, tier)

        say("%-16s %8.1f %8.0f %8.0f %8.0f %8.2f %8.0f" %
            (ru, A.suit_kg(cls, tier), man.wall["chest"], man.dodge,
             man.aimbonus, man.rate, man.hpmax))

    blade = A.weapon("Sword", tier)
    man = A.Man(stage[0], stage[1], stage[2], stage[3], blade, "ChainArmor", tier)
    say("меч: урон %.0f, пробитие %.0f, крит %.0f%% ×%.2f" %
        (man.damage, man.pierce, man.crit * 100, man.critmul))
    say()

io.open("arena.txt", "w", encoding="utf-8").write("\n".join(OUT))
print("строк:", len(OUT))
