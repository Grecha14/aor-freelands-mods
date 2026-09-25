# -*- coding: utf-8 -*-
"""Переложить уровни зверья так, чтобы каждый размер стоял на своём этапе игры.

Авторские уровни расставлены вразнобой: здоровья на уровень выходит от девятнадцати до
пятидесяти девяти, а крупные улетают туда, где середины игры уже нет. При этом внутри
своего размера порядок у авторов осмысленный — кто сильнее сородича, тот и по уровню выше.

Оттого не выдумываем заново, а растягиваем: порядок внутри размера сохраняем, а всю полосу
сдвигаем на тот этап, которому она принадлежит.
"""
import io
import re

CENSUS = (r"C:\Program Files (x86)\Steam\steamapps\common"
          r"\Age of Reforging The Freelands\BepInEx\ЗВЕРИНЕЦ.txt")

BEAST = {"animal", "insect", "mythological", "undead", "demon", "lizard", "none"}

# Куда должна лечь каждая полоса: низ и верх уровня.
BANDS = {
    "Small":  (1, 6),        # на ком качаются в первый час
    "Medium": (2, 20),       # весь ранний и средний лес
    "Large":  (18, 55),      # охота середины игры
    "Giant":  (45, 90),      # конец игры, и лучше не в одиночку
}

FIXED = re.compile(r"DesertDragon|ForestDragon|LavaDragon|Mountain Dragon|TheKingOfRot", re.I)


def grab(line, pattern, default=""):
    found = re.search(pattern, line)
    return found.group(1) if found else default


rows = []
race = None

for line in io.open(CENSUS, encoding="utf-8", errors="replace").read().split("\n"):
    if line.startswith("=== "):
        race = line.strip("= ").strip()
        continue
    if not line.startswith("[") or race not in BEAST:
        continue

    named = re.match(r"\[(\d+)\] ([^|]+?)\s*\|", line)
    size = grab(line, r"размер (\w+)")
    if not named or size not in BANDS:
        continue

    rows.append({
        "id": named.group(1),
        "name": named.group(2).strip(),
        "size": size,
        "born": int(grab(line, r"уровень (\d+)", "1")),
        "power": int(grab(line, r"мощь (\d+)", "0")),
        "fixed": bool(FIXED.search(named.group(2))),
    })

# Внутри размера ранжируем по авторскому уровню, а при равенстве по мощи: это и есть
# порядок, который авторы имели в виду.
said = []
for size, band in BANDS.items():
    mine = [r for r in rows if r["size"] == size and not r["fixed"]]
    if not mine:
        continue

    mine.sort(key=lambda r: (r["born"], r["power"]))

    low, high = band
    last = len(mine) - 1

    for i, one in enumerate(mine):
        part = i / float(last) if last > 0 else 0.0
        one["level"] = int(round(low + (high - low) * part))

    said.append("=== %s: %d штук, полоса %d-%d ===" % (size, len(mine), low, high))
    said.append("%-30s %6s %7s %8s" % ("имя", "было", "мощь", "стало"))

    for one in mine[:6] + (["…"] if len(mine) > 12 else []) + mine[-6:]:
        if one == "…":
            said.append("   …")
            continue
        said.append("%-30s %6d %7d %8d" % (
            one["name"][:30], one["born"], one["power"], one["level"]))
    said.append("")

io.open("relevel.txt", "w", encoding="utf-8").write("\n".join(said))

# Готовая строка для настройки ByHand — уровень и вилка втрое.
pairs = []
for one in sorted(rows, key=lambda r: r["name"]):
    if one["fixed"] or "level" not in one:
        continue
    pairs.append("%s=%d-%d" % (one["name"].split("(")[0].strip(),
                               one["level"], one["level"] * 3))

io.open("relevel_config.txt", "w", encoding="utf-8").write(",".join(pairs))
