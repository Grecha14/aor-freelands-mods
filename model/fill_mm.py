# -*- coding: utf-8 -*-
"""Три новые вкладки в книге: броня по толщине, оружие по базовому урону, и кто кого берёт."""
import io
import re

import arena as A
import arena_t2 as T
import plate as P
import sheets

TIERS = range(1, 6)

# --- 1. броня ------------------------------------------------------------------

COATS = ["Cloth", "PaddingArmor", "LightLeatherArmor", "HardLeaterArmor", "SplintArmor",
         "ChainArmor", "ScaleMail", "LamellarArmor", "HalfPlate", "PlateArmor",
         "metalHelmet", "leatherHelemt"]

HEAD_A = ["класс", "тир", "толщина, мм", "держит рез", "держит укол", "держит обух",
          "вес набора", "вес шлема", "вес нагрудника", "вес поножей",
          "стойкость рез", "стойкость укол", "стойкость обух", "цена множитель"]

PRICE = {"Cloth": 1.0, "PaddingArmor": 1.2, "LightLeatherArmor": 1.5, "HardLeaterArmor": 2.0,
         "SplintArmor": 3.0, "ChainArmor": 4.0, "ScaleMail": 5.0, "LamellarArmor": 5.0,
         "HalfPlate": 8.0, "PlateArmor": 10.0, "metalHelmet": 6.0, "leatherHelemt": 2.0}

armour = []

for cls in COATS:
    for tier in TIERS:
        t = P.thick(cls, tier)
        if t is None:
            continue

        kg = A.suit_kg(cls, tier)
        h = P.HOLD[cls]

        armour.append([
            P.RU[cls], "Т%d" % tier, round(t, 2),
            round(t * h[0], 2), round(t * h[1], 2), round(t * h[2], 2),
            round(kg, 1) if kg else "",
            round(kg * P.SLOT["head"], 1) if kg else "",
            round(kg * P.SLOT["chest"], 1) if kg else "",
            round(kg * P.SLOT["pants"], 1) if kg else "",
            h[0], h[1], h[2], PRICE.get(cls, 1.0),
        ])

# --- 2. оружие -----------------------------------------------------------------

HEAD_W = ["класс", "тир", "тип удара", "базовый урон", "вес", "скорость", "хват",
          "сила/ловкость", "концентрация", "требование стата", "требование мастерства",
          "мм под требование", "мм в середине", "мм в конце"]

LADDER = [10, 20, 30, 40, 50]
MASTERY = [10, 20, 40, 75, 100]

HOLDS = {}
for w in A.GEAR["W"]:
    HOLDS.setdefault(w["cls"], w.get("wt", "onehand"))

weapons = []
seen = set()

for w in A.GEAR["W"]:
    cls = w["cls"]
    tier = int(w["tier"][1:]) if w["tier"][1:].isdigit() else 0
    if tier < 1 or (cls, tier) in seen:
        continue
    seen.add((cls, tier))

    b = A.weapon(cls, tier)
    if b is None:
        continue

    hold = HOLDS.get(cls, "onehand")
    s_f, a_f = T.blend(cls, tier)

    full = LADDER[tier - 1]
    part = int(round(full * 0.5))

    if hold in ("twohand", "polearms"):
        ask, st, ag = full, full, 10
    elif hold == "range":
        ask, st, ag = full, 10, full
    elif hold == "shield":
        ask, st, ag = full, full, 10
    else:
        ask, st, ag = part, part, part

    stat_min = st * s_f + ag * a_f
    stat_mid = 25 * s_f + 20 * a_f
    stat_late = 45 * s_f + 35 * a_f

    weapons.append([
        cls, "Т%d" % tier, P.KIND_RU.get(b["kind"], b["kind"]),
        round(b["hit"], 1), round(b["kg"], 1), round(b["sp"], 2), hold,
        "%.1f/%.1f" % (s_f, a_f), P.FOCUS.get(cls, 1.0), ask, MASTERY[tier - 1],
        round(P.punch(stat_min, b["kg"], cls), 2),
        round(P.punch(stat_mid, b["kg"], cls), 2),
        round(P.punch(stat_late, b["kg"], cls), 2),
    ])

weapons.sort(key=lambda r: (r[0], r[1]))

# --- 3. кто кого берёт ---------------------------------------------------------

SHOW = ["Cloth", "PaddingArmor", "LightLeatherArmor", "HardLeaterArmor", "SplintArmor",
        "ChainArmor", "ScaleMail", "LamellarArmor", "HalfPlate", "PlateArmor"]

HEAD_M = ["оружие", "тир", "боец", "мм"] + [P.RU[c] for c in SHOW]

matrix = []

for who, tier, stats in (("под требование", 2, None), ("середина", 3, (25, 20)),
                         ("конец", 4, (45, 35))):
    for w in sorted(set((x["cls"]) for x in A.GEAR["W"])):
        b = A.weapon(w, tier)
        if b is None or HOLDS.get(w) in ("quiver", "lute"):
            continue

        hold = HOLDS.get(w, "onehand")
        s_f, a_f = T.blend(w, tier)

        if stats is None:
            full = LADDER[tier - 1]
            part = int(round(full * 0.5))
            st, ag = (full, 10) if hold in ("twohand", "polearms", "shield") else \
                     ((10, full) if hold == "range" else (part, part))
        else:
            st, ag = stats

        stat = st * s_f + ag * a_f
        mm = P.punch(stat, b["kg"], w)

        row = [w, "Т%d" % tier, who, round(mm, 2)]
        for coat in SHOW:
            must = P.need(coat, tier, b["kind"])
            row.append("" if must is None else round(100 * P.share(mm, must)))
        matrix.append(row)

said = []
said.append("Броня — толщина: %d" % sheets.put("Броня — толщина", HEAD_A, armour))
said.append("Оружие — пробитие: %d" % sheets.put("Оружие — пробитие", HEAD_W, weapons))
said.append("Пробитие: оружие × броня: %d" % sheets.put("Пробитие: оружие × броня", HEAD_M, matrix))

io.open("zalito_mm.txt", "w", encoding="utf-8").write("\n".join(said))
print("\n".join(said))
