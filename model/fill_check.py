# -*- coding: utf-8 -*-
"""Сверка: на каком этапе игры зверь по зубам, и не ломается ли лестница.

Замысел прост: мелочью и средними качаются в начале, крупных выслеживают в середине,
исполинских бьют толпой в конце. Проверяем, выходит ли так на числах.
"""
import io
import re

import conf
import levels
import sheets

CENSUS = (r"C:\Program Files (x86)\Steam\steamapps\common"
          r"\Age of Reforging The Freelands\BepInEx\ЗВЕРИНЕЦ.txt")

BEAST = {"animal", "insect", "mythological", "undead", "demon", "lizard", "none"}

# Из живого конфига, как и в прочих листах: переписанное руками отстаёт молча. Здесь коготь
# исполина стоял 4.0 против 3.0 в моде, и сверка врала на треть.
ORDER = ["Small", "Medium", "Large", "Giant", "Titanic"]

BASE = conf.sized("Beastly", "Stats")
STEP = conf.sized("Beastly", "Growth")
CLAW = conf.listed("Beastly", "Claw")
BULK = conf.listed("Beastly", "Bulk")
WALLS = conf.listed("Beastly", "Walls")
WALL_GROWTH = conf.flat("Beastly", "WallGrowth", 0.015)
HURT = conf.listed("Wild", "Hurt")
DMG = conf.listed("Beastly", "Bearing").get("damage", 0.01)
SKILL = conf.flat("Breach", "Skill", 0.01)

LEADS = {"Small": 2, "Medium": 2, "Large": 0, "Giant": 0, "Titanic": 0}

# Игрок на трёх этапах: имя, стат, мастерство, вес меча и молота, вычет доспеха, здоровье
# Доспех героя на этапах: лёгкая кожа Т1, чешуя Т3, латы Т4 — считаем их вычет из
# конфига, чтобы сверка шла за лестницей тира, а не за числом, вписанным однажды.
STAGES = [
    ("начало",   10, 10, 3.00,  8.00, conf.guard("LightLeatherArmor", 1), 150),
    ("середина", 25, 30, 4.29, 11.44, conf.guard("ScaleMail", 3), 260),
    ("конец",    45, 50, 7.50, 20.00, conf.guard("PlateArmor", 4), 420),
]


def stats(size, level):
    return [int(round(b + s * level)) for b, s in zip(BASE[size], STEP[size])]


def mastery(level, low, high):
    return 100.0 if high <= low else 50 + 50 * max(0.0, min(1.0, (level - low) / float(high - low)))


def beast(size, level, low, high, arms):
    six = stats(size, level)

    # Ровно то, что делает мод: ведущий стат на коготь, и всё это на владение когтем —
    # «Breach.Push» множит зверю пробитие на «1 + ступень × Skill» из «Wild.Honed».
    mast = mastery(level, low, high)
    pierce = six[LEADS[size]] * (arms if arms else CLAW[size]) * (1 + mast * SKILL)
    hit = HURT[size] * (1 + six[0] * DMG)

    first = WALLS.get(size, 0.0)
    wall = first if ORDER.index(size) < ORDER.index("Large") \
        else first * (1 + level * WALL_GROWTH)

    # Туша множит выносливость, а не всё здоровье разом: так считает «Beastly.Health».
    hp = 100 + level * 6 + 6 * six[1] * BULK[size]

    return pierce, hit, wall, hp


def verdict(rows):
    """На каком этапе зверь становится честной дракой."""
    for name, blows_to_kill, blows_to_die in rows:
        # честно: игрок убивает не мгновенно и сам живёт дольше зверя
        if blows_to_kill <= 60 and blows_to_die >= 3:
            return name
    return "толпой"


got = []
race = None

for line in io.open(CENSUS, encoding="utf-8", errors="replace").read().split("\n"):
    if line.startswith("=== "):
        race = line.strip("= ").strip()
        continue
    if not line.startswith("[") or race not in BEAST:
        continue

    named = re.match(r"\[(\d+)\] ([^|]+?)\s*\|", line)
    size = re.search(r"размер (\w+)", line)
    born = re.search(r"уровень (\d+)", line)
    if not (named and size and born) or size.group(1) not in BASE:
        continue

    name = named.group(2).strip()
    low, high, span = levels.look(name, int(born.group(1)))

    # Оружие на модели считается только у тех, кто может его держать: минотавр, нежить,
    # демон — у них руки. У простого зверя оружия быть не может, и коготь ему считается по
    # росту, как и в моде. Прежде тринадцать килограммов доставались и болонке, отчего она
    # пробивала четыреста сорок.
    handed = race not in ("animal", "insect")
    arms = 13.0 if (handed and "оружие в модели" in line) else None

    pierce, hit, wall, hp = beast(size.group(1), high, low, high, arms)

    cells = []
    stages = []
    for label, stat, mast, sword, hammer, armour, hero in STAGES:
        # игрок бьёт мечом; пробитие и урон по человеческой формуле
        mine = stat * sword * (1 + mast / 100.0)
        left = max(0.0, wall - mine)
        clean = max(0.0, 40 * (1 + stat * 0.01) * (1 + mast / 100.0) - left)
        to_kill = hp / clean if clean > 0 else 9999

        # зверь бьёт в ответ
        back = max(0.0, hit - max(0.0, armour - pierce))
        to_die = hero / back if back > 0 else 9999

        cells += [int(round(mine)), int(round(to_kill)) if to_kill < 9999 else "—",
                  int(round(back)), int(round(to_die)) if to_die < 9999 else "—"]
        stages.append((label, to_kill, to_die))

    got.append([int(named.group(1)), name, size.group(1), race, low, high,
                int(round(pierce)), int(round(hit)), int(round(wall)), int(round(hp))]
               + cells + [verdict(stages)])

got.sort(key=lambda r: (ORDER.index(r[2]), r[5], r[1]))

HEAD = ["id", "имя", "размер", "раса", "ур. от", "ур. до",
        "пробитие", "удар", "вычет", "здоровье"]
for label, _, _, _, _, _, _ in STAGES:
    HEAD += ["%s: моё пробитие" % label, "%s: ударов на него" % label,
             "%s: его удар" % label, "%s: ударов по мне" % label]
HEAD += ["когда по зубам"]

sheets.put("Сверка зверья", HEAD, got)
io.open("check.txt", "w", encoding="utf-8").write("строк: %d" % len(got))
