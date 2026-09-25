# -*- coding: utf-8 -*-
"""Проставить диапазон уровней: от своего до тройного.

Исключения — те, кого трогать нельзя: драконы и червь. У них уровень один и точный, они
последние в лестнице, и растягивать их некуда.

Поимённые вилки, заданные вручную, перебивают общее правило.
"""
import io
import re

import sheets

# Драконы стоят на своём уровне и вилки не имеют: они вершина лестницы.
# Сюда же король червей: он им вровень.
DRAGON = re.compile(r"DesertDragon|ForestDragon|LavaDragon|Mountain Dragon|MountainDragon|TheKingOfRot", re.I)
DRAGON_LEVEL = 300

# Прочие, кого не растягиваем.
FIXED = re.compile(r"Wyvern|Weredragon|SandWorm_Summon", re.I)

# Что задано руками.
BY_HAND = {
    "Wolf": "1-15",
    "Wolf King": "15-25",
    "White Wolf": "11-15",
    "Werewolf": "55-75",
}

TABS = ["Звери", "Насекомые", "Мифические", "Ящеры и драконы", "Нежить", "Демоны",
        "Без расы", "Громилы", "Орки", "Гномы", "Эльфы", "Феи", "Люди"]

NAME, LEVEL, RANGE = 1, 3, 4          # номера колонок, считая с нуля

said = []
skipped = []

for tab in TABS:
    got = sheets.api().spreadsheets().values().get(
        spreadsheetId=sheets.SHEET, range="'%s'!A:U" % tab).execute().get("values", [])

    if len(got) < 2:
        continue

    column = []
    touched = 0

    for row in got[1:]:
        name = row[NAME] if len(row) > NAME else ""
        level = row[LEVEL] if len(row) > LEVEL else ""

        try:
            level = int(level)
        except (TypeError, ValueError):
            column.append([""])
            continue

        # Поимённое сильнее общего.
        hand = None
        for key, span in BY_HAND.items():
            if name.strip().lower() == key.lower():
                hand = span
                break

        if hand:
            column.append([hand])
            touched += 1
            continue

        if DRAGON.search(name):
            column.append(["%d" % DRAGON_LEVEL])
            skipped.append("%s (%s): уровень %d" % (name, tab, DRAGON_LEVEL))
            continue

        if FIXED.search(name):
            column.append(["%d" % level])         # ровно свой, без вилки
            skipped.append("%s (%s): свой %d, без вилки" % (name, tab, level))
            continue

        column.append(["%d-%d" % (level, level * 3)])
        touched += 1

    sheets.api().spreadsheets().values().update(
        spreadsheetId=sheets.SHEET,
        range="'%s'!E2" % tab,
        valueInputOption="RAW",
        body={"values": column}).execute()

    said.append("%s: %d из %d" % (tab, touched, len(got) - 1))

io.open("diapazony.txt", "w", encoding="utf-8").write(
    "\n".join(said) + "\n\nБез вилки оставлены:\n" + "\n".join("  " + s for s in sorted(set(skipped))))
print("gotovo")
