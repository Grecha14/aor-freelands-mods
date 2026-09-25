# -*- coding: utf-8 -*-
"""Оружие и броня по вкладкам: что в игре и что делает с этим наша модель."""
import io
import re

import conf
import sheets

DUMP = "items_dump.txt"

# Числа берём из живого конфига мода, а не переписываем сюда. Переписанное отстаёт молча:
# так у шлемов ступень считалась от второго тира, хотя мод давно считает от первого, а веса
# доспехов стояли те, с которыми мод не работал ни дня.
_keen = conf.get("Breach", "Keen") or "1.00,1.21,1.43,1.86,2.50"
_rungs = [float(x) for x in _keen.split(",") if x.strip()]

LADDER = {"T0": _rungs[0]}
for _i, _step in enumerate(_rungs):
    LADDER["T%d" % (_i + 1)] = _step
LEGEND = _rungs[-1]                 # легендарное считается последней ступенью

# --- броня ---------------------------------------------------------------
SUITS = conf.listed("Burden", "Materials")
GUARDS = conf.listed("Breach", "Guards")
STEEP = set((conf.get("Breach", "Hard") or "").replace(" ", "").split(","))
THROUGH = conf.listed("Breach", "Through")

# С какого тира класс вообще существует: лат ниже второго в игре нет. Ровно тот же список,
# что в «Breach.firsts» — шлем среди них не значится и считается от первого.
FIRST = {"ChainArmor": 2, "ScaleMail": 2, "LamellarArmor": 2, "HalfPlate": 2,
         "PlateArmor": 2}

SHARE = {"chest": conf.flat("Breach", "Chest", 1.2), "head": 1.0,
         "pants": conf.flat("Breach", "Legs", 0.8), "belt": conf.flat("Breach", "Legs", 0.8)}

SOFT = conf.flat("Breach", "Soft", 1.2)
HARSH = conf.flat("Breach", "Steep", 1.8)

# Вес доспеха: полный набор материала на нижнем тире, лестница тира и доля куска — тем же
# счётом, каким его считает «Burden». Пояс в дележе не участвует, и веса ему не пишем.
WEIGHT_LADDER = [float(x) for x in
                 (conf.get("Burden", "TierLadder") or "1,1.042,1.083,1.167,1.292,1.5")
                 .split(",") if x.strip()]
_share = [float(x) for x in
          (conf.get("Burden", "SetShare") or "0.2,0.5,0.3").split(",") if x.strip()]
PIECES = {"head": _share[0], "chest": _share[1], "pants": _share[2]}

# --- посохи --------------------------------------------------------------
STAVES = set((conf.get("Wield", "Staves") or "Staff,Stick,Wand").replace(" ", "").split(","))
STAFF_POWER = [float(x) for x in
               (conf.get("Wield", "StaffPower") or "0.15,0.3,0.5,0.8,1.3").split(",")
               if x.strip()]

ARMOUR_TAB = {
    "Cloth": "Броня — ткань", "PaddingArmor": "Броня — стёганка",
    "LightLeatherArmor": "Броня — лёгкая кожа", "HardLeaterArmor": "Броня — твёрдая кожа",
    "SplintArmor": "Броня — шинная", "ChainArmor": "Броня — кольчуга",
    "ScaleMail": "Броня — чешуя", "LamellarArmor": "Броня — ламеллярная",
    "HalfPlate": "Броня — полулаты", "PlateArmor": "Броня — латы",
    "metalHelmet": "Шлемы — металл", "leatherHelemt": "Шлемы — кожа",
}

KIND_TAB = {"sharp": "Оружие — режущее", "blunt": "Оружие — дробящее",
            "stab": "Оружие — колющее"}


def num(text):
    # В выгрузке числа идут через запятую и нередко с хвостом: «0,8.» или «12,».
    if not text:
        return 0.0
    text = text.strip().rstrip(".,").replace(",", ".")
    try:
        return float(text)
    except ValueError:
        return 0.0


def tier_step(tier, quality):
    if quality == "Legendary":
        return LEGEND, 5
    return LADDER.get(tier, 1.0), int(tier[1:]) if tier[1:].isdigit() else 1


def spell(cls, tier_no):
    """Что посох добавляет заклинаниям на своей ступени. Прочему оружию — ничего.

    По тиру и только: «Wield.Bless» берёт «(int)blade.tier», качество в счёт не идёт —
    легендарный посох четвёртой ступени прибавляет столько же, сколько простой той же
    ступени. Именной посох со своей прибавкой мод не трогает вовсе, и такие здесь считаются
    по общему правилу.
    """
    if cls not in STAVES:
        return ""

    at = min(max(tier_no, 1), len(STAFF_POWER)) - 1
    return "+%d%%" % round(STAFF_POWER[at] * 100)


def guard_of(cls, tier, quality, slot):
    base = GUARDS.get(cls)
    if base is None:
        return 0
    step = 5 if quality == "Legendary" else (int(tier[1:]) if tier[1:].isdigit() else 1)
    first = FIRST.get(cls, 1)
    grow = HARSH if cls in STEEP else SOFT
    held = base * grow ** max(0, step - first)
    return held * SHARE.get(slot, 1.0)


head = re.compile(r"\[(\d+)\] (\S+).*?тип (\w+), качество (\w+), ступень (T\d)")
blades, coats = {}, {}

for line in io.open(DUMP, encoding="utf-8", errors="replace").read().split("\n"):
    got = head.search(line)
    if not got:
        continue

    ident, asset, kind, quality, tier = got.groups()
    name = asset.replace("items_Equipments_Weapons_", "").replace("items_Equipments_Armors_", "")
    weight = num(re.search(r"вес ([\d,]+)", line).group(1)) if re.search(r"вес ([\d,]+)", line) else 0
    dur = num(re.search(r"прочность (-?[\d,]+)", line).group(1)) if re.search(r"прочность (-?[\d,]+)", line) else 0
    rung, step = tier_step(tier, quality)

    if kind == "Weapon":
        hit = re.search(r"бьёт: (\w+) ([\d,]+)-([\d,]+)", line)
        cls = re.search(r"оружие (\w+)/(\w+)", line)
        if not (hit and cls):
            continue
        dtype = hit.group(1)
        blades.setdefault(KIND_TAB.get(dtype, "Оружие — прочее"), []).append([
            int(ident), name, cls.group(2), cls.group(1), tier, quality,
            round(weight, 2), round(weight * rung, 2),
            num(hit.group(2)), num(hit.group(3)), dtype,
            num(re.search(r"сила (\d+)", line).group(1)) if re.search(r"сила (\d+)", line) else 0,
            num(re.search(r"скорость ([\d,]+)", line).group(1)) if re.search(r"скорость ([\d,]+)", line) else 0,
            num(re.search(r"дальность ([\d,]+)", line).group(1)) if re.search(r"дальность ([\d,]+)", line) else 0,
            round(weight * rung * 35, 0),           # пробитие при стате 35
            spell(cls.group(2), int(tier[1:]) if tier[1:].isdigit() else 1),
        ])
        continue

    if kind != "Armor":
        continue

    cls = re.search(r"броня (\w+)/(\w+)", line)
    slot = re.search(r"слот (\w+)", line)
    if not cls:
        continue
    klass = cls.group(2)
    place = slot.group(1) if slot else ""

    suit = SUITS.get(klass)
    kilos = ""
    если_прочность = ""
    if suit and place in PIECES:
        rung_w = WEIGHT_LADDER[min(step, len(WEIGHT_LADDER) - 1)]
        kilos = suit * rung_w * PIECES[place]
        если_прочность = round(guard_of(klass, tier, quality, place) * kilos, 0)
        kilos = round(kilos, 2)

    coats.setdefault(ARMOUR_TAB.get(klass, "Броня — прочее"), []).append([
        int(ident), name, klass, cls.group(1), place, tier, quality,
        round(weight, 2), kilos,
        round(dur, 0), если_прочность,
        round(guard_of(klass, tier, quality, place), 0),
        THROUGH.get(klass, ""),
    ])

BLADE_HEAD = ["id", "имя", "класс", "хват", "тир", "качество",
              "вес игровой", "вес новый", "урон мин", "урон макс", "тип",
              "сила игровая", "скорость", "дальность", "пробитие при стате 35",
              "сила заклинаний"]
COAT_HEAD = ["id", "имя", "класс", "вес брони", "кусок", "тир", "качество",
             "вес игровой", "вес новый", "прочность игровая", "прочность новая",
             "вычет", "доля дробящего мимо"]

said = []
for tab in ["Оружие — режущее", "Оружие — дробящее", "Оружие — колющее", "Оружие — прочее"]:
    rows = blades.get(tab)
    if not rows:
        continue
    rows.sort(key=lambda r: (r[2], r[4], r[1]))
    said.append("%s: %d" % (tab, sheets.put(tab, BLADE_HEAD, rows)))

for klass in ["Cloth", "PaddingArmor", "LightLeatherArmor", "HardLeaterArmor",
              "SplintArmor", "ChainArmor", "ScaleMail", "LamellarArmor",
              "HalfPlate", "PlateArmor", "metalHelmet", "leatherHelemt"]:
    tab = ARMOUR_TAB[klass]
    rows = coats.get(tab)
    if not rows:
        continue
    rows.sort(key=lambda r: (r[5], r[4], r[1]))
    said.append("%s: %d" % (tab, sheets.put(tab, COAT_HEAD, rows)))

rows = coats.get("Броня — прочее")
if rows:
    rows.sort(key=lambda r: (r[2], r[5], r[1]))
    said.append("%s: %d" % ("Броня — прочее", sheets.put("Броня — прочее", COAT_HEAD, rows)))

io.open("zalito_gear.txt", "w", encoding="utf-8").write("\n".join(said))
print("gotovo")
