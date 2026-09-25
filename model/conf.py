# -*- coding: utf-8 -*-
"""Живой конфиг мода как источник чисел для таблицы.

Прежде числа стояли в скриптах списком, переписанным с конфига руками. Мод с тех пор
уехал — появилась ступень Titanic, поменялись когти и стены, — а таблица осталась на
старом, и никто бы этого не заметил: числа правдоподобные. Потому теперь читаем сам
`aor.itemforge.cfg`: что стоит в игре, то и в таблице.
"""
import io
import os

CFG = (r"C:\Program Files (x86)\Steam\steamapps\common"
       r"\Age of Reforging The Freelands\BepInEx\config\aor.itemforge.cfg")


def read(path=None):
    """Весь конфиг: раздел -> ключ -> строка значения."""
    path = path or CFG
    if not os.path.exists(path):
        raise IOError("Конфига нет: " + path)

    got = {}
    where = None

    for line in io.open(path, encoding="utf-8-sig", errors="replace").read().split("\n"):
        line = line.strip()

        if not line or line.startswith("#"):
            continue

        if line.startswith("[") and line.endswith("]"):
            where = line[1:-1].strip()
            got.setdefault(where, {})
            continue

        if "=" in line and where:
            key, _, value = line.partition("=")
            got[where][key.strip()] = value.strip()

    return got


_all = None


def get(section, key, default=None):
    global _all
    if _all is None:
        _all = read()
    return _all.get(section, {}).get(key, default)


def sized(section, key, cast=float):
    """«Small=3,4,16,12,2,2;Medium=…» — в словарь размер -> кортеж чисел.

    Тем же разбором читается и «Small=5,Medium=18,…»: на точку с запятой делится список
    размеров, на запятую — числа внутри. Где число одно, кортежа не выходит, и отдаём его
    само по себе.
    """
    written = get(section, key)
    if not written:
        return {}

    got = {}

    for piece in written.replace(";", "\n").split("\n"):
        piece = piece.strip()
        if not piece or "=" not in piece:
            continue

        name, _, rest = piece.partition("=")
        numbers = []

        for one in rest.split(","):
            one = one.strip()
            if not one:
                continue
            try:
                numbers.append(cast(one))
            except ValueError:
                numbers = None
                break

        if not numbers:
            continue

        got[name.strip()] = numbers[0] if len(numbers) == 1 else tuple(numbers)

    return got


def flat(section, key, default=0.0):
    """Одно число из конфига."""
    written = get(section, key)
    try:
        return float(written)
    except (TypeError, ValueError):
        return default


def listed(section, key):
    """«Small=5,Medium=18,Large=45» — в словарь размер -> число.

    Отдельным разбором, потому что здесь запятая делит размеры, а не числа внутри.
    """
    written = get(section, key)
    if not written:
        return {}

    got = {}

    for piece in written.split(","):
        piece = piece.strip()
        if "=" not in piece:
            continue

        name, _, rest = piece.partition("=")
        try:
            got[name.strip()] = float(rest)
        except ValueError:
            continue

    return got


# --- вычет доспеха -------------------------------------------------------------
# Считается той же дорогой, что в «Breach.Guard»: основа класса, лестница тира от того
# тира, с которого класс существует, и доля куска. Пока он стоял здесь списком, смена
# «Steep» проходила мимо таблицы.
FIRSTS = {"ChainArmor": 2, "ScaleMail": 2, "LamellarArmor": 2,
          "HalfPlate": 2, "PlateArmor": 2}

NAMED_CLASS = {"ткань": "Cloth", "кожа": "LightLeatherArmor", "кольчуга": "ChainArmor",
               "чешуя": "ScaleMail", "полулаты": "HalfPlate", "латы": "PlateArmor"}


def guard(cls, tier, slot="chest"):
    """Вычет одного куска этого класса и тира."""
    base = listed("Breach", "Guards").get(cls)
    if not base:
        return None

    hard = set((get("Breach", "Hard") or "").replace(" ", "").split(","))
    grow = flat("Breach", "Steep", 1.8) if cls in hard else flat("Breach", "Soft", 1.2)

    first = FIRSTS.get(cls, 1)
    if tier < first:
        return None

    share = {"chest": flat("Breach", "Chest", 1.2), "head": 1.0,
             "pants": flat("Breach", "Legs", 0.8), "belt": flat("Breach", "Legs", 0.8)}

    return base * grow ** (tier - first) * share.get(slot, 1.0)


def guards(slot="chest"):
    """Тот же вычет по всем шести привычным материалам, с первого тира по пятый."""
    got = {}

    for name, cls in NAMED_CLASS.items():
        got[name] = [guard(cls, t, slot) for t in range(1, 6)]

    return got
