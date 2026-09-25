# -*- coding: utf-8 -*-
"""Новые правила боя на той же модели арены: было и стало.

Что прибавлено к arena.py (ровно те числа, что стоят в конфиге мода по умолчанию):

  замах     Blows:    15 % вскользь ×0.5, 70 % как есть, 15 % всем весом ×1.5 — до брони
  фланг     Blows:    второй, кто бьёт одного, бьёт в бок ×1.5, третий и дальше — в спину ×2,
                      на итог после брони
  шаг       Sidestep: шанс 15 % + 0.5 % за ловкость сверх 10, не выше 45 %, умножен на
                      доспех: лёгкий ×1, средний ×0.7, тяжёлый ×0.35. Шагает только тот, кто
                      видит бьющего (своего противника). Половина шагов уводит из-под удара
                      целиком, остальные смягчают его на 40 %

Остальное — как в arena.py: попадание, щит, крит, пробитие, части тела.
Каждая пара — 400 боёв.
"""
import io
import random

import arena as A

random.seed(20260925)

SWING = [(0.15, 0.5), (0.70, 1.0), (0.15, 1.5)]
SIDE = 1.5
BACK = 2.0
STEP_BASE = 0.15
STEP_PER_AGI = 0.005
STEP_MOST = 0.45
SOFTEN = 0.4
ESCAPE = 0.5

KIND = {"Cloth": "Light", "PaddingArmor": "Light", "LightLeatherArmor": "Light",
        "HardLeaterArmor": "Medium", "SplintArmor": "Medium", "ChainArmor": "Medium",
        "ScaleMail": "Medium", "LamellarArmor": "Medium",
        "HalfPlate": "Heavy", "PlateArmor": "Heavy"}
STEP_ARMOUR = {"None": 1.1, "Light": 1.0, "Medium": 0.7, "Heavy": 0.35}


def roll():
    dice = random.random()
    edge = 0.0
    for share, times in SWING:
        edge += share
        if dice < edge:
            return times
    return 1.0


def step_chance(man):
    chance = STEP_BASE + (man.agi - 10) * STEP_PER_AGI
    chance *= STEP_ARMOUR.get(KIND.get(man.coat, "None"), 1.0)
    return max(0.0, min(STEP_MOST, chance))


def swing(a, b, facing=True, flank=0, new=True):
    """Один удар по правилам arena.swing, с замахом, шагом и флангом."""
    sure = A.AIM_BASE + (a.mastery - 50) * A.PER_MASTERY + a.pre * A.PER_PRECISION \
        + a.aimbonus * A.PER_ARMOUR - b.dodge * A.PER_DODGE
    sure = max(0.05, min(A.STEADIEST, sure))

    if random.random() > sure:
        return 0.0, None

    if facing and b.shield is not None and b.sp > 0:
        chance = (b.block - A.breaks(a.arm)) / 100.0
        if random.random() < chance:
            cost = a.damage
            if b.sp >= cost:
                b.sp -= cost
                return 0.0, None

    soft = 1.0
    if new and facing and random.random() < step_chance(b):
        if random.random() < ESCAPE:
            return 0.0, None
        soft = 1.0 - SOFTEN

    spot = random.choices(list(A.WHERE.keys()), weights=list(A.WHERE.values()))[0]
    wall = b.cover.get(spot)

    coming = a.damage * (roll() if new else 1.0)
    crit = random.random() < a.crit
    if crit:
        coming *= a.critmul

    always = 0.0
    if a.arm["kind"] == "blunt":
        low, high = A.THROUGH.get(b.coat, (0.3, 0.3))
        always = random.uniform(low, high) if high > low else high
    if crit:
        always = max(always, random.uniform(A.GAP[0], A.GAP[1]))

    if wall is None or wall <= 0:
        got = coming
    else:
        rest = wall - a.pierce
        if rest <= 0:
            got = coming
        else:
            stopped = coming * (1 - always)
            got = coming * always + max(0.0, stopped - rest)

    if new:
        got *= soft
        if flank == 1:
            got *= SIDE
        elif flank >= 2:
            got *= BACK

    return got, spot


def fight(left, right, new=True, limit=240.0, step=0.05):
    for m in left + right:
        m.hp = m.hpmax
        m.sp = m.spmax
        for part in m.limbs:
            m.limbs[part] = m.limbmax[part]
        m.timer = random.random() / max(0.1, m.rate)

    time = 0.0
    while time < limit:
        time += step

        for m in left + right:
            if m.alive and m.sp < m.spmax:
                m.sp = min(m.spmax, m.sp + m.sprest * step)

        for side, foes in ((left, right), (right, left)):
            alive = [m for m in side if m.alive]
            for place, m in enumerate(alive):
                m.timer -= step
                if m.timer > 0:
                    continue
                m.timer += 1.0 / m.rate

                target = next((f for f in foes if f.alive), None)
                if target is None:
                    continue

                # Цель смотрит на первого живого из нападающих; второй заходит сбоку, третий — со спины.
                much, part = swing(m, target, facing=(place == 0), flank=place, new=new)

                if much > 0 and part is not None:
                    target.limbs[part] -= much
                    target.hp -= much
                    if target.limbs[part] <= 0:
                        if part.startswith("рука"):
                            target.rate *= 0.6
                            if target.shield is not None:
                                target.shield = None
                                target.block = min(100.0, 10.0 + target.pre)
                        elif part.startswith("нога"):
                            target.dodge *= 0.5
                            target.rate *= 0.85

        if not any(m.alive for m in left):
            return "right", time
        if not any(m.alive for m in right):
            return "left", time

    return "ничья", time


def duel(make_a, make_b, n_a=1, n_b=1, runs=400, new=True):
    won = 0
    draws = 0
    seconds = []
    for _ in range(runs):
        left = [make_a() for _ in range(n_a)]
        right = [make_b() for _ in range(n_b)]
        who, when = fight(left, right, new=new)
        seconds.append(when)
        if who == "left":
            won += 1
        elif who == "ничья":
            draws += 1
    return 100.0 * won / runs, sum(seconds) / len(seconds), 100.0 * draws / runs


STAGE = A.STAGES[1]
TIER = 3

BUILDS = [
    ("кинжал, лёгкая кожа", "Dagger", "LightLeatherArmor", None),
    ("копьё, твёрдая кожа", "Spear", "HardLeaterArmor", None),
    ("меч+щит, кольчуга", "Sword", "ChainArmor", "HeaterShield"),
    ("двуруч. меч, чешуя", "GreatSword", "ScaleMail", None),
    ("двуруч. молот, полулаты", "TwoHandHammer", "HalfPlate", None),
    ("булава+щит, латы", "Mace", "PlateArmor", "KiteShield"),
]


def maker(build):
    name, arm, coat, guard = build
    blade = A.weapon(arm, TIER)
    shield = A.shield(guard, TIER) if guard else None
    return lambda: A.Man(name, STAGE[1], STAGE[2], STAGE[3], blade, coat, TIER, shield)


out = io.open("выживание.txt", "w", encoding="utf-8")


def P(line=""):
    out.write(line + "\n")
    print(line)


P("НОВЫЕ ПРАВИЛА БОЯ: БЫЛО И СТАЛО")
P("Середина игры: уровень 25, статы 25/22/20/20/14/14, мастерство 30, всё снаряжение Т3.")
P("")

P("1. Зеркало: каждый против такого же — сколько секунд идёт бой")
P("  %-26s %9s %9s %9s  %s" % ("построение", "было, с", "стало, с", "разница", "шанс шага"))
for b in BUILDS:
    mk = maker(b)
    _, old, _ = duel(mk, mk, new=False)
    _, now, _ = duel(mk, mk, new=True)
    P("  %-26s %9.1f %9.1f %8.0f%%  %4.0f%%" % (b[0], old, now, 100.0 * (now / old - 1.0), 100 * step_chance(mk())))
P("")

P("2. Все против всех — доля побед левого (было → стало)")
head = "  %-26s" % "" + "".join("%12s" % (str(i + 1)) for i in range(len(BUILDS)))
P(head)
for i, a in enumerate(BUILDS):
    row = "  %-26s" % ("%d %s" % (i + 1, a[0]))
    for j, b in enumerate(BUILDS):
        if i == j:
            row += "%12s" % "-"
            continue
        w_old, _, _ = duel(maker(a), maker(b), runs=300, new=False)
        w_new, _, _ = duel(maker(a), maker(b), runs=300, new=True)
        row += "%12s" % ("%d→%d" % (round(w_old), round(w_new)))
    P(row)
P("")

P("3. Один против толпы таких же — победы одиночки и сколько он продержался")
P("  %-26s %16s %16s %16s %16s" % ("построение", "1×2 было", "1×2 стало", "1×3 было", "1×3 стало"))
for b in BUILDS:
    mk = maker(b)
    w2o, t2o, _ = duel(mk, mk, 1, 2, runs=300, new=False)
    w2n, t2n, _ = duel(mk, mk, 1, 2, runs=300, new=True)
    w3o, t3o, _ = duel(mk, mk, 1, 3, runs=300, new=False)
    w3n, t3n, _ = duel(mk, mk, 1, 3, runs=300, new=True)
    P("  %-26s %8.0f%% %5.1fс %8.0f%% %5.1fс %8.0f%% %5.1fс %8.0f%% %5.1fс" % (
        b[0], w2o, t2o, w2n, t2n, w3o, t3o, w3n, t3n))

out.close()
