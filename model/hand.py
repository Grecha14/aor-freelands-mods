# -*- coding: utf-8 -*-
"""Рука кузнеца: до десятки разброс не меняется, с десятки до сотни садится по Фибоначчи.

И цена профессии до сотни при разных множителях кривой опыта.
"""

FLAT = 10      # до этого уровня рука не твердеет
FULL = 100     # здесь кует безупречно

# Приросты по Фибоначчи на девять ступеней от десятки до сотни.
FIB = [1, 1, 2, 3, 5, 8, 13, 21, 34]
WHOLE = float(sum(FIB))


def hand(skill):
    """Во сколько раз уже разброса против полного."""
    if skill <= FLAT:
        return 1.0
    if skill >= FULL:
        return 0.0

    step = (FULL - FLAT) / float(len(FIB))     # десять уровней на ступень
    gone = (skill - FLAT) / step               # сколько ступеней пройдено

    done = 0.0
    for i, f in enumerate(FIB):
        if gone >= i + 1:
            done += f
        elif gone > i:
            done += f * (gone - i)             # внутри ступени — ровно
            break
        else:
            break

    return max(0.0, 1.0 - done / WHOLE)


SPREAD = 0.10

print("ruka kuznetsa\n")
print("%6s %9s %11s %18s" % ("skill", "ot polnogo", "razbros", "vychet"))
for s in (0, 5, 10, 20, 30, 40, 50, 60, 70, 80, 90, 95, 100):
    m = hand(s)
    much = SPREAD * m
    lo = (1 - much) / (1 + much) * 100
    hi = (1 + much) / (1 - much) * 100
    print("%6d %9.0f%% %10.1f%% %8.0f%% .. %3.0f%%" % (s, m * 100, much * 100, lo, hi))

print("\n\ncena professii do sotni\n")
BASE, LV = 100, 10
print("%9s %14s %16s %16s" % ("faktor", "za 100-y", "vsego do 50", "vsego do 100"))
for factor in (1.15, 1.10, 1.08, 1.07, 1.05, 1.04):
    tot = 0.0
    at50 = 0.0
    for n in range(100):
        tot += int(BASE * factor ** n + LV * n)
        if n == 49:
            at50 = tot
    last = int(BASE * factor ** 99 + LV * 99)
    print("%9.2f %14s %16s %16s" % (
        factor,
        format(last, ",d").replace(",", " "),
        format(int(at50), ",d").replace(",", " "),
        format(int(tot), ",d").replace(",", " ")))
