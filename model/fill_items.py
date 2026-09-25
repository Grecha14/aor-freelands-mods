# -*- coding: utf-8 -*-
"""Всё снаряжение поимённо: каждому классу брони своя страница, оружию — по типу удара.

Берётся не сводка по классам, а сама выгрузка предметов: восемьсот восемнадцать доспехов и
четыреста сорок клинков, как они есть в игре, с ценой, прочностью и весом.
"""
import io
import re

import arena as A
import arena_t2 as T
import plate as P
import sheets

DUMP = "items_dump.txt"

LADDER = [10, 20, 30, 40, 50]
MASTERY = [10, 20, 40, 75, 100]


def num(text):
    if not text:
        return 0.0
    try:
        return float(text.strip().rstrip(".,").replace(",", "."))
    except ValueError:
        return 0.0


head = re.compile(r"\[(\d+)\] (\S+).*?тип (\w+), качество (\w+), ступень (T\d), "
                  r"цена ([\d]+), вес ([\d,]+), прочность (-?[\d,]+)")

arms, coats = [], []

for line in io.open(DUMP, encoding="utf-8", errors="replace"):
    got = head.search(line)
    if not got:
        continue

    ident, asset, kind, quality, tier, price, weight, dur = got.groups()
    t = int(tier[1:])

    name = (asset.replace("items_Equipments_Weapons_", "")
                 .replace("items_Equipments_Armors_", "")
                 .replace("items_Equipments_", ""))

    if kind == "Weapon":
        hit = re.search(r"бьёт: (.+?) \|", line)
        cls = re.search(r"оружие (\w+)/(\w+)", line)
        if not (hit and cls):
            continue

        kinds = re.findall(r"(sharp|blunt|stab|flame|cold|poison|elec|positive|negative) "
                           r"([\d,]+)-([\d,]+)", hit.group(1))
        if not kinds:
            continue

        lead, most = kinds[0][0], 0.0
        total = 0.0
        for k, lo, hi in kinds:
            mid = (num(lo) + num(hi)) * 0.5
            total += mid
            if mid > most:
                most, lead = mid, k

        blend = re.search(r"сила/ловкость ([\d,]+)/([\d,]+)", line)
        s_f = num(blend.group(1)) if blend else 0.7
        a_f = num(blend.group(2)) if blend else 0.3

        speed = re.search(r"скорость ([\d,]+)", line)
        hold = cls.group(1)
        weapon_cls = cls.group(2)

        arms.append({
            "id": int(ident), "name": name, "cls": weapon_cls, "hold": hold,
            "tier": t, "q": quality, "kinds": len(kinds), "lead": lead,
            "hit": total, "kg": num(weight), "sp": num(speed.group(1)) if speed else 0.4,
            "s": s_f, "a": a_f, "price": int(price), "dur": num(dur),
        })
        continue

    if kind != "Armor":
        continue

    cls = re.search(r"броня (\w+)/(\w+)", line)
    slot = re.search(r"слот (\w+)", line)
    if not cls:
        continue

    coats.append({
        "id": int(ident), "name": name, "cls": cls.group(2), "weight_kind": cls.group(1),
        "slot": slot.group(1) if slot else "", "tier": t, "q": quality,
        "kg": num(weight), "dur": num(dur), "price": int(price),
    })

# ------------------------------------------------------------------ броня

HEAD_A = ["id", "имя", "тир", "качество", "кусок", "вес игровой", "вес новый",
          "держит рез", "держит обух", "держит укол",
          "прочность игровая", "цена игровая"]

TABS_A = {"Cloth": "Броня — ткань", "PaddingArmor": "Броня — стёганка",
          "LightLeatherArmor": "Броня — лёгкая кожа", "HardLeaterArmor": "Броня — твёрдая кожа",
          "SplintArmor": "Броня — шинная", "ChainArmor": "Броня — кольчуга",
          "ScaleMail": "Броня — чешуя", "LamellarArmor": "Броня — ламеллярная",
          "HalfPlate": "Броня — полулаты", "PlateArmor": "Броня — латы",
          "metalHelmet": "Шлемы — металл", "leatherHelemt": "Шлемы — кожа"}

by_class = {}

for c in coats:
    tab = TABS_A.get(c["cls"], "Броня — прочее")

    kg = A.suit_kg(c["cls"], c["tier"]) * P.SLOT.get(c["slot"], 0.0)
    share = {"chest": 1.2, "head": 1.0, "pants": 0.8, "belt": 0.8}.get(c["slot"], 1.0)

    cut = P.stops(c["cls"], c["tier"], 0) if c["cls"] in P.STOPS else None
    blunt = P.stops(c["cls"], c["tier"], 1) if c["cls"] in P.STOPS else None
    stab = P.stops(c["cls"], c["tier"], 2) if c["cls"] in P.STOPS else None

    by_class.setdefault(tab, []).append([
        c["id"], c["name"], "Т%d" % c["tier"], c["q"], c["slot"],
        round(c["kg"], 2), round(kg, 2) if kg else "",
        round(cut * share) if cut else "",
        round(blunt * share) if blunt else "",
        round(stab * share) if stab else "",
        round(c["dur"]), c["price"],
    ])

# ------------------------------------------------------------------ оружие

HEAD_W = ["id", "имя", "класс", "хват", "тир", "качество", "тип удара", "базовый урон",
          "вес", "скорость", "сила/ловкость", "концентрация",
          "требование стата", "требование мастерства",
          "бьёт под требование", "бьёт в середине", "бьёт в конце", "цена игровая"]

TABS_W = {"sharp": "Оружие — рез", "blunt": "Оружие — обух", "stab": "Оружие — укол"}

by_kind = {}

for w in arms:
    tab = "Оружие — смешанное" if w["kinds"] > 1 else TABS_W.get(w["lead"], "Оружие — прочее")

    t = min(max(w["tier"], 1), 5)
    full = LADDER[t - 1]
    part = int(round(full * 0.5))

    if w["hold"] in ("twohand", "polearms", "shield"):
        ask, st, ag = full, full, 10
    elif w["hold"] == "range":
        ask, st, ag = full, 10, full
    else:
        ask, st, ag = part, part, part

    kg = w["kg"] * A.KEEN[min(t, len(A.KEEN)) - 1]

    stat_min = st * w["s"] + ag * w["a"]
    stat_mid = 25 * w["s"] + 20 * w["a"]
    stat_late = 45 * w["s"] + 35 * w["a"]

    by_kind.setdefault(tab, []).append([
        w["id"], w["name"], w["cls"], w["hold"], "Т%d" % w["tier"], w["q"],
        P.KIND_RU.get(w["lead"], w["lead"]), round(w["hit"], 1), round(kg, 2), w["sp"],
        "%.1f/%.1f" % (w["s"], w["a"]), P.FOCUS.get(w["cls"], 1.0), ask, MASTERY[t - 1],
        round(w["hit"] * (1 + stat_min * 0.01) * P.FOCUS.get(w["cls"], 1.0)),
        round(w["hit"] * (1 + stat_mid * 0.01) * P.FOCUS.get(w["cls"], 1.0)),
        round(w["hit"] * (1 + stat_late * 0.01) * P.FOCUS.get(w["cls"], 1.0)),
        w["price"],
    ])

said = []

for tab in sorted(by_class):
    rows = sorted(by_class[tab], key=lambda r: (r[2], r[4], r[1]))
    said.append("%s: %d" % (tab, sheets.put(tab, HEAD_A, rows)))

for tab in sorted(by_kind):
    rows = sorted(by_kind[tab], key=lambda r: (r[4], r[2], r[1]))
    said.append("%s: %d" % (tab, sheets.put(tab, HEAD_W, rows)))

io.open("zalito_items.txt", "w", encoding="utf-8").write("\n".join(said))
print("вкладок:", len(said))
