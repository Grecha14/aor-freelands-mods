# -*- coding: utf-8 -*-
"""Второй тир целиком: боец ровно под требование вещи, мастерство 50, вся броня Т2.

Стат для урона и пробития берётся не «сила или ловкость», а игровой смесью самой вещи —
в выгрузке у каждого оружия записано «сила/ловкость 0,7/0,3».
"""
import io
import re

import arena as A

# --- требования по «Wield» -----------------------------------------------------
LADDER = [10, 20, 30, 40, 50]
HALF = 0.5

TIER = 2
FULL = LADDER[TIER - 1]
PART = int(round(FULL * HALF))
MASTERY = 50

# --- смесь статов из выгрузки --------------------------------------------------
BLEND = {}
line_re = re.compile(r"оружие (\w+)/(\w+).*?ступень (T\d)", re.S)

for line in io.open("items_dump.txt", encoding="utf-8", errors="replace"):
    if "оружие " not in line:
        continue

    kind = re.search(r"оружие (\w+)/(\w+)", line)
    tier = re.search(r"ступень (T\d)", line)
    blend = re.search(r"сила/ловкость ([\d,]+)/([\d,]+)", line)

    if not (kind and tier and blend):
        continue

    key = (kind.group(2), tier.group(1))
    s = float(blend.group(1).replace(",", "."))
    a = float(blend.group(2).replace(",", "."))

    got = BLEND.setdefault(key, [0.0, 0.0, 0])
    got[0] += s
    got[1] += a
    got[2] += 1


def blend(cls, tier):
    got = BLEND.get((cls, "T%d" % tier))
    if not got:
        got = BLEND.get((cls, "T%d" % (tier + 1))) or BLEND.get((cls, "T%d" % (tier - 1)))
    if not got:
        return 0.7, 0.3
    return got[0] / got[2], got[1] / got[2]


# --- толщина и стойкость -------------------------------------------------------
K = 233.0
FOCUS = {"Crossbow": 2.2, "HeavyCrossbow": 2.2, "Longbow": 1.2, "Shortbow": 1.2,
         "TwoHandHammer": 1.3, "TwoHandMace": 1.3, "Poleaxe": 1.3,
         "GreatSword": 1.3, "Katana": 1.3, "Estoc": 1.3, "GreatAxe": 1.3, "DanAxe": 1.3}

MM = {"Cloth": 0.05, "PaddingArmor": 0.12, "LightLeatherArmor": 0.25,
      "HardLeaterArmor": 0.45, "SplintArmor": 0.9, "ChainArmor": 1.0, "ScaleMail": 1.3,
      "LamellarArmor": 1.5, "HalfPlate": 1.8, "PlateArmor": 2.2}

HOLD = {"Cloth": (1.5, 0.5, 0.8), "PaddingArmor": (1.5, 0.5, 0.8),
        "LightLeatherArmor": (1.3, 0.7, 0.7), "HardLeaterArmor": (1.3, 0.7, 0.7),
        "SplintArmor": (1.4, 0.9, 0.5), "ChainArmor": (1.2, 0.6, 0.3),
        "ScaleMail": (1.3, 1.0, 0.6), "LamellarArmor": (1.4, 1.0, 0.6),
        "HalfPlate": (2.4, 1.2, 1.0), "PlateArmor": (3.0, 1.4, 1.1)}

KIND = {"sharp": 0, "stab": 1, "blunt": 2}


def thick(cls, tier):
    first = A.FIRSTS.get(cls, 1)
    if tier < first:
        return None
    return MM[cls] * A.RUNGS[min(tier, 5)] / A.RUNGS[min(first, 5)]


def share(mm, need):
    return max(0.0, min(1.0, (mm - 0.75 * need) / (0.5 * need))) if need > 0 else 1.0


ARMS = [("кинжал", "Dagger", "onehand"), ("меч", "Sword", "onehand"),
        ("сабля", "Sabre", "onehand"), ("рапира", "Rapier", "onehand"),
        ("топор", "BattleAxe", "onehand"), ("булава", "Mace", "onehand"),
        ("молот", "Hammer", "onehand"), ("копьё", "Spear", "onehand"),
        ("катар", "Katar", "onehand"), ("мачете", "Machete", "onehand"),
        ("двуручный меч", "GreatSword", "twohand"), ("катана", "Katana", "twohand"),
        ("эсток", "Estoc", "twohand"), ("фальшион", "Falchion", "twohand"),
        ("двуручный топор", "GreatAxe", "twohand"),
        ("двуручный молот", "TwoHandHammer", "twohand"),
        ("двуручная булава", "TwoHandMace", "twohand"),
        ("алебарда", "Halberd", "polearms"), ("глефа", "Glaive", "polearms"),
        ("бардиш", "Bardiche", "polearms"), ("двуручное копьё", "TwoHandSpear", "polearms"),
        ("трезубец", "Trident", "polearms"),
        ("короткий лук", "Shortbow", "range"), ("длинный лук", "Longbow", "range"),
        ("арбалет", "Crossbow", "range"), ("тяжёлый арбалет", "HeavyCrossbow", "range")]

COATS = [("ткань", "Cloth"), ("стёганка", "PaddingArmor"), ("лёг. кожа", "LightLeatherArmor"),
         ("тв. кожа", "HardLeaterArmor"), ("шинная", "SplintArmor"),
         ("кольчуга", "ChainArmor"), ("чешуя", "ScaleMail"), ("полулаты", "HalfPlate"),
         ("латы", "PlateArmor")]

OUT = []


def say(line=""):
    OUT.append(line)


say("ВТОРОЙ ТИР ЦЕЛИКОМ")
say("Боец ровно под требование вещи: двуручное и древковое — %d силы, одноручное — %d"
    % (FULL, PART))
say("ведущего стата, дальнобойное — %d ловкости. Мастерство у всех %d." % (FULL, MASTERY))
say("Стат для урона и пробития — игровая смесь самой вещи «сила/ловкость».")
say()
say("%-18s %5s %6s %6s %6s" % ("оружие", "мм", "урон", "вес", "стат") +
    "".join("%10s" % ru for ru, _ in COATS))

rows = []

for ru, cls, hold in ARMS:
    b = A.weapon(cls, TIER)
    if b is None:
        continue

    s_f, a_f = blend(cls, TIER)

    if hold in ("twohand", "polearms"):
        strength, agility = FULL, 10
    elif hold == "range":
        strength, agility = 10, FULL
    else:
        lead_is_agi = b["kind"] == "stab"
        strength = 10 if lead_is_agi else PART
        agility = PART if lead_is_agi else 10

    stat = strength * s_f + agility * a_f

    mm = stat * b["kg"] / K * FOCUS.get(cls, 1.0)
    hit = b["hit"] * (1 + stat * 0.01)
    k = KIND.get(b["kind"], 0)

    line = "%-18s %5.2f %6.0f %6.1f %6.1f" % (ru, mm, hit, b["kg"], stat)

    for ru2, coat in COATS:
        t = thick(coat, TIER)
        if t is None:
            line += "%10s" % "—"
            continue
        line += "%9.0f%%" % (100 * share(mm, t * HOLD[coat][k]))

    rows.append((mm, line))

for mm, line in sorted(rows):
    say(line)

say()
say("толщина Т2, мм: " + ", ".join("%s %.2f" % (ru, thick(c, TIER) or 0) for ru, c in COATS))
say()
say("что надо пробить, мм — рез / укол / обух:")
for ru, coat in COATS:
    t = thick(coat, TIER)
    if t is None:
        continue
    h = HOLD[coat]
    say("  %-12s %5.2f / %5.2f / %5.2f" % (ru, t * h[0], t * h[1], t * h[2]))

io.open("arena_t2.txt", "w", encoding="utf-8").write("\n".join(OUT))
print("строк:", len(OUT))
