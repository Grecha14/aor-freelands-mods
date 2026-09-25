# -*- coding: utf-8 -*-
"""Разложить перепись существ по вкладкам — своя на каждую расу."""
import io
import re

import sheets

CENSUS = (r"C:\Program Files (x86)\Steam\steamapps\common"
          r"\Age of Reforging The Freelands\BepInEx\ЗВЕРИНЕЦ.txt")

#            сила  вын  лов  точн  ум  воля
BASE = {
    "Small":  (3,   4,  16,  12,  2,  2),
    "Medium": (8,  10,  18,  10,  3,  3),
    "Large":  (30, 26,   8,   8,  4,  4),
    "Giant":  (50, 40,   4,   6,  5,  5),
}
STEP = {
    "Small":  (0.05, 0.08, 0.30, 0.20, 0.03, 0.03),
    "Medium": (0.12, 0.20, 0.40, 0.20, 0.05, 0.05),
    "Large":  (0.55, 0.45, 0.10, 0.12, 0.06, 0.06),
    "Giant":  (0.80, 0.60, 0.05, 0.10, 0.08, 0.08),
}
LEADS = {"Small": 2, "Medium": 2, "Large": 0, "Giant": 0}
REACH = {"Small": 3.0, "Medium": 3.2, "Large": 3.8, "Giant": 6.5}

GUARD = {
    "ткань":    [12, 14, 17, 21, 25],
    "кожа":     [30, 36, 43, 52, 62],
    "кольчуга": [None, 60, 108, 194, 350],
    "чешуя":    [None, 74, 134, 241, 434],
    "полулаты": [None, 94, 168, 303, 546],
    "латы":     [None, 120, 216, 389, 700],
}

# Как зовём вкладки. Люди и прочие разумные идут отдельно от зверья.
NAMED = {
    "animal": "Звери",
    "insect": "Насекомые",
    "mythological": "Мифические",
    "lizard": "Ящеры и драконы",
    "undead": "Нежить",
    "demon": "Демоны",
    "none": "Без расы",
    "human": "Люди",
    "elf": "Эльфы",
    "dwarf": "Гномы",
    "orc": "Орки",
    "bruteman": "Громилы",
    "fairy": "Феи",
}

KIND = {"sharp": "режущий", "blunt": "дробящий", "stab": "колющий",
        "flame": "огонь", "cold": "холод", "poison": "яд",
        "elec": "молния", "pos": "светлое", "neg": "тёмное"}

HEAD = ["id", "имя", "размер", "уровень", "диапазон уровней", "мощь", "здоровье",
        "тип удара", "урон", "второй удар", "сила удара игровая",
        "сила", "вынос", "ловк", "точн", "ум", "воля",
        "ведёт", "пробитие", "что открывает", "оружие на модели"]


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


by_race = {}
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

    level = int(grab(line, r"уровень (\d+)", "1"))
    size = grab(line, r"размер (\w+)", "")
    six = stats(size or "Medium", level)
    lead = LEADS.get(size, 2)
    pierce = six[lead] * REACH.get(size, 3.2)

    hits = re.findall(r"(sharp|blunt|stab|flame|cold|poison|elec) ([\d,]+)-([\d,]+)", line)

    model = ("нет" if "оружия на модели нет" in line
             else ("есть" if ("узел" in line or "оружие в модели" in line) else ""))

    by_race.setdefault(race, []).append([
        int(named.group(1)),
        named.group(2).strip(),
        size,
        level,
        "",                                   # диапазон уровней — заполняем вместе
        int(grab(line, r"мощь (\d+)", "0")),
        int(grab(line, r"здоровье ([\d]+)", "0")),
        KIND.get(hits[0][0], "") if hits else "",
        ("%s-%s" % (hits[0][1].rstrip(","), hits[0][2].rstrip(","))) if hits else "",
        ("%s %s-%s" % (KIND.get(hits[1][0], hits[1][0]),
                       hits[1][1].rstrip(","), hits[1][2].rstrip(",")))
        if len(hits) > 1 else "",
        grab(line, r"сила ([\d,]+)", "").rstrip(","),
        six[0], six[1], six[2], six[3], six[4], six[5],
        "ловкость" if lead == 2 else "сила",
        int(round(pierce)),
        opens(pierce),
        model,
    ])

# Сперва зверьё, потом разумные: с ними мы ещё не работали.
FIRST = ["animal", "insect", "mythological", "lizard", "undead", "demon", "none"]
rest = sorted(r for r in by_race if r not in FIRST)

said = []
for race in FIRST + rest:
    rows = by_race.get(race)
    if not rows:
        continue

    rows.sort(key=lambda r: (r[3], r[1]))          # по уровню, потом по имени
    tab = NAMED.get(race, race)
    n = sheets.put(tab, HEAD, rows)
    said.append("%s: %d" % (tab, n))

io.open("zalito.txt", "w", encoding="utf-8").write("\n".join(said))
print("gotovo")
