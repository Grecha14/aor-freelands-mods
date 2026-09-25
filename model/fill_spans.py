# -*- coding: utf-8 -*-
"""Пересобрать вкладки существ: что выходит на нижнем и верхнем конце диапазона.

Вилки берутся из самой таблицы — там уже и правило «своё-тройное», и поимённые, и
трёхсотые у драконов. Остальное считается: игровой бонус за уровни сверх чертежа даёт
здоровье и урон, наша формула — статы и пробитие.
"""
import io
import re

import levels
import sheets

CENSUS = (r"C:\Program Files (x86)\Steam\steamapps\common"
          r"\Age of Reforging The Freelands\BepInEx\ЗВЕРИНЕЦ.txt")

BASE = {"Small": (3, 4, 16, 12, 2, 2), "Medium": (8, 10, 18, 10, 3, 3),
        "Large": (30, 26, 8, 8, 4, 4), "Giant": (50, 40, 4, 6, 5, 5)}
STEP = {"Small": (0.05, 0.08, 0.30, 0.20, 0.03, 0.03),
        "Medium": (0.12, 0.20, 0.40, 0.20, 0.05, 0.05),
        "Large": (0.55, 0.45, 0.10, 0.12, 0.06, 0.06),
        "Giant": (0.80, 0.60, 0.05, 0.10, 0.08, 0.08)}
LEADS = {"Small": 2, "Medium": 2, "Large": 0, "Giant": 0}
REACH = {"Small": 3.0, "Medium": 3.2, "Large": 3.8, "Giant": 6.5}

PER_LEVEL = 0.05        # игровой levelHealthBonus и levelDamageBonus

GUARD = {"ткань": [12, 14, 17, 21, 25], "кожа": [30, 36, 43, 52, 62],
         "кольчуга": [None, 60, 108, 194, 350], "чешуя": [None, 74, 134, 241, 434],
         "полулаты": [None, 94, 168, 303, 546], "латы": [None, 120, 216, 389, 700]}

KIND = {"sharp": "режущий", "blunt": "дробящий", "stab": "колющий", "flame": "огонь",
        "cold": "холод", "poison": "яд", "elec": "молния"}

TABS = ["Звери", "Насекомые", "Мифические", "Ящеры и драконы", "Нежить", "Демоны",
        "Без расы", "Громилы", "Орки", "Гномы", "Эльфы", "Феи", "Люди"]

HEAD = ["id", "имя", "размер", "уровень", "диапазон", "мощь",
        "здоровье", "здоровье макс",
        "тип удара", "урон", "урон макс", "второй удар", "сила удара игровая",
        "сила от", "сила до", "вынос от", "вынос до", "ловк от", "ловк до",
        "точн", "ум", "воля",
        "ведёт", "пробитие от", "пробитие до", "что открывает (макс)",
        "оружие на модели"]


def stats(size, level):
    if size not in BASE:
        size = "Medium"
    return [int(round(b + s * level)) for b, s in zip(BASE[size], STEP[size])]


def opens(pierce):
    got = []
    for name, row in GUARD.items():
        best = None
        for i, guard in enumerate(row):
            if guard is not None and pierce >= guard:
                best = i + 1
        if best:
            got.append("%s Т%d" % (name, best))
    return "; ".join(got) if got else "ничего"


def grab(line, pattern, default=""):
    found = re.search(pattern, line)
    return found.group(1) if found else default


# --- перепись --------------------------------------------------------------
NAMED = {"animal": "Звери", "insect": "Насекомые", "mythological": "Мифические",
         "lizard": "Ящеры и драконы", "undead": "Нежить", "demon": "Демоны",
         "none": "Без расы", "human": "Люди", "elf": "Эльфы", "dwarf": "Гномы",
         "orc": "Орки", "bruteman": "Громилы", "fairy": "Феи"}

by_tab = {}
race = None

for line in io.open(CENSUS, encoding="utf-8", errors="replace").read().split("\n"):
    if line.startswith("=== "):
        race = line.strip("= ").strip()
        continue
    if not line.startswith("["):
        continue

    named = re.match(r"\[(\d+)\] ([^|]+?)\s*\|", line)
    if not named:
        continue

    ident = named.group(1)
    born = int(grab(line, r"уровень (\d+)", "1"))
    size = grab(line, r"размер (\w+)", "")
    hp = int(grab(line, r"здоровье ([\d]+)", "0"))

    # Уровни берём из levels.py — не из того, что осталось в ячейках.
    low, high, span = levels.look(named.group(2).strip(), born)

    six_low, six_high = stats(size, low), stats(size, high)
    lead = LEADS.get(size, 2)
    reach = REACH.get(size, 3.2)

    # Игровой бонус считается от уровня чертежа.
    grown = 1 + PER_LEVEL * max(0, high - born)

    hits = re.findall(r"(sharp|blunt|stab|flame|cold|poison|elec) ([\d,]+)-([\d,]+)", line)
    dmg = ("%s-%s" % (hits[0][1].rstrip(","), hits[0][2].rstrip(","))) if hits else ""
    dmg_high = ""
    if hits:
        lo = float(hits[0][1].rstrip(",").replace(",", "."))
        hi = float(hits[0][2].rstrip(",").replace(",", "."))
        dmg_high = "%d-%d" % (round(lo * grown), round(hi * grown))

    model = ("нет" if "мешей нет" in line
             else ("есть: " + line.split("оружие в модели: ")[1].split("|")[0].strip()
                   if "оружие в модели: " in line else "не узнано"))

    by_tab.setdefault(NAMED.get(race, race), []).append([
        int(ident), named.group(2).strip(), size, low, span,
        int(grab(line, r"мощь (\d+)", "0")),
        hp, int(round(hp * grown)),
        KIND.get(hits[0][0], "") if hits else "", dmg, dmg_high,
        ("%s %s-%s" % (KIND.get(hits[1][0], hits[1][0]),
                       hits[1][1].rstrip(","), hits[1][2].rstrip(","))) if len(hits) > 1 else "",
        grab(line, r"сила ([\d,]+)", "").rstrip(","),
        six_low[0], six_high[0], six_low[1], six_high[1], six_low[2], six_high[2],
        six_high[3], six_high[4], six_high[5],
        "ловкость" if lead == 2 else "сила",
        int(round(six_low[lead] * reach)), int(round(six_high[lead] * reach)),
        opens(six_high[lead] * reach),
        model,
    ])

said = []
for tab in TABS:
    rows = by_tab.get(tab)
    if not rows:
        continue
    rows.sort(key=lambda r: (r[3], r[1]))
    said.append("%s: %d" % (tab, sheets.put(tab, HEAD, rows)))

io.open("spans.txt", "w", encoding="utf-8").write("\n".join(said))
print("gotovo")
