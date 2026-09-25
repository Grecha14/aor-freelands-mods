# -*- coding: utf-8 -*-
"""Итог: рука кузнеца, клеймо мастера, цена ремесла, возведение легендарок в пятую ступень."""

SPREAD, PLAIN, KIND, STEADY, MARK = 0.10, 10, 50, 100, 0.10
FIB = [1, 1, 2, 3, 5, 8, 13, 21, 34]
WHOLE = float(sum(FIB))


def narrow(hand):
    if hand < 0 or hand <= PLAIN:
        return 1.0
    if hand >= STEADY:
        return 0.0
    step = (STEADY - PLAIN) / float(len(FIB))
    gone = (hand - PLAIN) / step
    done = 0.0
    for i, f in enumerate(FIB):
        if gone >= i + 1:
            done += f
        elif gone > i:
            done += f * (gone - i)
            break
        else:
            break
    return max(0.0, 1.0 - done / WHOLE)


def band(hand):
    """Каким выходит вычет против меры: худшее и лучшее."""
    if hand >= STEADY:
        return 1.222, 1.222                       # клеймо: +10 % прочности, -10 % веса
    m = SPREAD * narrow(hand)
    if m <= 0:
        return 1.0, 1.0
    best = (1 + m) / (1 - m)
    worst = 1.0 if hand >= KIND else (1 - m) / (1 + m)
    return worst, best


print("RUKA KUZNETSA\n")
print("%7s %9s %9s %20s" % ("skill", "razbros", "brak", "vychet"))
for s in (0, 10, 30, 49, 50, 60, 70, 80, 90, 95, 99, 100):
    w, b = band(s)
    print("%7d %8.1f%% %9s %9.0f%% .. %3.0f%%" % (
        s, SPREAD * narrow(s) * 100, "net" if s >= KIND else "est", w * 100, b * 100))

print("\n\nKLEYMO MASTERA (100)\n")
print("%-26s %9s %11s %9s" % ("", "ves", "prochnost", "vychet"))
for name, kg, dur, guard, blade in (
        ("laty T5 korpus", 20.42, 14289, 700, False),
        ("kolchuga T4 korpus", 8.30, 1614, 194, False),
        ("mech T4", 5.58, 750, 0, True),
        ("boevoy topor T4", 7.44, 750, 0, True)):
    if blade:
        k, d = 1 + MARK, 1 + MARK
        print("%-26s %6.2f kg %10.0f %9s" % (name, kg, dur, "-"))
        print("%-26s %6.2f kg %10.0f %9s" % ("  s kleymom", kg * k, dur * d, "+10% probitie"))
    else:
        k, d = 1 - MARK, 1 + MARK
        print("%-26s %6.2f kg %10.0f %9.0f" % (name, kg, dur, guard))
        print("%-26s %6.2f kg %10.0f %9.0f" % ("  s kleymom", kg * k, dur * d, guard * d / k))

print("\n\nCENA REMESLA pri 1.065\n")
BASE, LV, F = 100, 10, 1.065
tot = 0
print("%7s %14s %16s" % ("uroven", "za uroven", "vsego s nulya"))
for n in range(100):
    tot += int(BASE * F ** n + LV * n)
    if n + 1 in (10, 15, 50, 100):
        print("%7d %14s %16s" % (n + 1,
                                 format(int(BASE * F ** n + LV * n), ",d").replace(",", " "),
                                 format(tot, ",d").replace(",", " ")))
