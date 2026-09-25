# -*- coding: utf-8 -*-
"""Вкладки существ: всё, что модель даёт зверю на обоих концах его вилки.

Числа здесь уже не игровые, а наши: статы от размера и уровня, мастерство от места в вилке,
пробитие и вычет из них, здоровье человеческой формулой, удар от роста.
"""
import io
import re

import conf
import levels
import sheets

CENSUS = (r"C:\Program Files (x86)\Steam\steamapps\common"
          r"\Age of Reforging The Freelands\BepInEx\ЗВЕРИНЕЦ.txt")

# Всё, что ниже, взято из живого конфига мода, а не переписано сюда руками. Список,
# переписанный руками, однажды отстаёт, и отстаёт молча: числа остаются правдоподобными.
# Так и вышло с Titanic — в моде ступень появилась, а в таблице её не было.
ORDER = ["Small", "Medium", "Large", "Giant", "Titanic"]

BASE = conf.sized("Beastly", "Stats")
STEP = conf.sized("Beastly", "Growth")
CLAW = conf.listed("Beastly", "Claw")
BULK = conf.listed("Beastly", "Bulk")
WALLS = conf.listed("Beastly", "Walls")
WALL_GROWTH = conf.flat("Beastly", "WallGrowth", 0.015)
HURT = conf.listed("Wild", "Hurt")
VARY = conf.flat("Wild", "Vary", 0.15)

# Что очко стата даёт зверю в бою. Нужна отсюда сила: она множит удар.
BEARING = conf.listed("Beastly", "Bearing")
DMG = BEARING.get("damage", 0.01)

# Чего стоит ступень владения: столько же, сколько человеку.
SKILL = conf.flat("Breach", "Skill", 0.01)

# Кого ведёт сила, а кого ловкость: у мелких и средних коготь идёт кистью, у крупных —
# плечом. Это решение мода, в конфиг оно не вынесено.
LEADS = {"Small": "ловкость", "Medium": "ловкость",
         "Large": "сила", "Giant": "сила", "Titanic": "сила"}
LEAD_AT = {"Small": 2, "Medium": 2, "Large": 0, "Giant": 0, "Titanic": 0}

# Вычет доспеха считается из конфига той же дорогой, что в «Breach.Guard»: списком он
# здесь стоял до тех пор, пока лестницу тира не сдвинули, и тогда «что открывает» стало
# врать на всю разницу.
GUARD = conf.guards()

NAMED = {"animal": "Звери", "insect": "Насекомые", "mythological": "Мифические",
         "lizard": "Ящеры и драконы", "undead": "Нежить", "demon": "Демоны",
         "none": "Без расы", "human": "Люди", "elf": "Эльфы", "dwarf": "Гномы",
         "orc": "Орки", "bruteman": "Громилы", "fairy": "Феи"}

TABS = ["Звери", "Насекомые", "Мифические", "Ящеры и драконы", "Нежить", "Демоны",
        "Без расы", "Громилы", "Орки", "Гномы", "Эльфы", "Феи", "Люди"]

HEAD = ["id", "имя", "размер", "уровень",
        "сила", "выносливость", "ловкость", "точность", "ум", "воля",
        "мастерство", "ведёт", "пробитие", "вычет",
        "здоровье", "здоровье в игре", "удар", "удар в игре",
        "разброс удара", "что открывает (макс)", "оружие на модели"]


def pair(low, high):
    """Одной ячейкой: «40/60», а если не меняется — просто «40»."""
    a, b = int(round(low)), int(round(high))
    return str(a) if a == b else "%d/%d" % (a, b)


# Звери, писанные в конфиге поимённо: шесть статов, коготь в килограммах, стена.
HANDED = {}
for _piece in (conf.get("Beastly", "Handed") or "").split(";"):
    if "=" not in _piece:
        continue
    _name, _, _rest = _piece.partition("=")
    try:
        HANDED[_name.strip().lower()] = tuple(float(x) for x in _rest.split(","))
    except ValueError:
        continue

SCALE = conf.flat("Beastly", "Scale", 0.185)   # очко стата стоит пятую долю уровня


def hand(name):
    low = (name or "").lower()
    for key, got in HANDED.items():
        if key in low:
            return got
    return None


def stats(size, level):
    return [int(round(b + s * level)) for b, s in zip(BASE[size], STEP[size])]


def mastery(level, low, high):
    if high <= low:
        return 100
    return int(round(50 + 50 * max(0.0, min(1.0, (level - low) / float(high - low)))))


def opens(value):
    got = []
    for name, row in GUARD.items():
        best = None
        for i, guard in enumerate(row):
            if guard is not None and value >= guard:
                best = i + 1
        if best:
            got.append("%s Т%d" % (name, best))
    return "; ".join(got) if got else "ничего"


def grab(line, pattern, default=""):
    found = re.search(pattern, line)
    return found.group(1) if found else default


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

    size = grab(line, r"размер (\w+)", "")
    if size not in BASE:
        size = "Medium"

    name = named.group(2).strip()
    low, high, span = levels.look(name, int(grab(line, r"уровень (\d+)", "1")))

    own = hand(name)

    if own:
        # Статы писаны руками, а уровень выводится из них — наоборот против прочих.
        a = b = list(own[:6])
        low = high = int(round(sum(own[:6]) * SCALE))
        span = str(low)
        m_low = m_high = 100
        at = 0
        pen_low = pen_high = own[0] * own[6] * (1 + 100 * SKILL)
    else:
        a, b = stats(size, low), stats(size, high)
        m_low, m_high = mastery(low, low, high), mastery(high, low, high)
        at = LEAD_AT[size]
        # Ведущий стат на вес когтя и на владение им: «Breach.Push» множит пробитие зверя
        # на «1 + ступень × Skill», где ступень берётся из «Wild.Honed» — пятьдесят у
        # молодого, сто у матёрого. Ровно то же считаем здесь.
        pen_low = a[at] * CLAW[size] * (1 + m_low * SKILL)
        pen_high = b[at] * CLAW[size] * (1 + m_high * SKILL)

    def wall(level):
        if own:
            return own[7]

        first = WALLS.get(size, 0.0)

        # Мелкие и средние стоят ровно — так и в моде: на них учатся, шкура им не защита.
        if ORDER.index(size) < ORDER.index("Large"):
            return first

        return first * (1 + level * WALL_GROWTH)

    def health(level, six):
        return 100 + level * 6 + 6 * six[1] * BULK[size]

    # Удар: «Wild.Arm» пишет зверю в оружие Hurt[рост], а «Beastly.Bear» домножает его на
    # силу — по очку за сотую, ровно как человеку через «MeleeDamageMD». Мастерства у зверя
    # нет вовсе, и надбавки за него здесь тоже нет.
    hit_low = HURT[size] * (1 + a[0] * DMG)
    hit_high = HURT[size] * (1 + b[0] * DMG)

    # Игровые числа — чтобы было с чем сверять наши.
    was_hp = grab(line, r"здоровье ([\d]+)", "")
    was_hit = re.findall(r"(?:sharp|blunt|stab|flame|cold|poison|elec) ([\d,]+)-([\d,]+)", line)
    was_hit = ("%s-%s" % (was_hit[0][0].rstrip(","), was_hit[0][1].rstrip(","))) if was_hit else ""

    model = ("есть" if "оружие в модели" in line or "узел" in line
             else ("нет" if "мешей нет" in line else "не узнано"))

    by_tab.setdefault(NAMED.get(race, race), []).append([
        int(named.group(1)), name, size, span,
        pair(a[0], b[0]), pair(a[1], b[1]), pair(a[2], b[2]),
        pair(a[3], b[3]), pair(a[4], b[4]), pair(a[5], b[5]),
        pair(m_low, m_high), LEADS[size],
        pair(pen_low, pen_high), pair(wall(low), wall(high)),
        pair(health(low, a), health(high, b)), was_hp,
        pair(hit_low, hit_high), was_hit,
        "±%d%%" % int(VARY * 100),
        opens(pen_high), model,
    ])

said = []
for tab in TABS:
    rows = by_tab.get(tab)
    if not rows:
        continue
    rows.sort(key=lambda r: (r[1],))
    said.append("%s: %d" % (tab, sheets.put(tab, HEAD, rows)))

io.open("beaststats.txt", "w", encoding="utf-8").write("\n".join(said))
