# -*- coding: utf-8 -*-
"""Уровни существ — единственный источник правды.

Таблицу я правил цепочкой разовых скриптов, и они затирали друг друга: один ставил драконам
триста, другой читал вилку из самой таблицы и, не найдя её, возвращал чертёжные пятьдесят
пять. Сверять такую таблицу нельзя — она зависит от того, в каком порядке запускали.

Теперь порядок один: здесь написано, что задумано, и всякая пересборка вкладок исходит
отсюда, а не из того, что осталось в ячейках с прошлого раза.
"""

# Уровень стоит намертво, вилки нет. Это вершины лестницы.
FIXED = {
    "DesertDragon": 300,
    "ForestDragon": 300,
    "LavaDragon": 300,
    "Mountain Dragon": 300,
    "TheKingOfRot": 300,
}

# Вилка задана поимённо и перебивает общее правило.
BY_HAND = {
    "Wolf": (1, 15),
    "Wolf King": (15, 25),
    "White Wolf": (11, 15),
    "Werewolf": (55, 75),
}

# Общее правило: от своего до тройного.
SPREAD = 3


def look(name, born):
    """Вернуть (низ, верх, как записать в колонку «диапазон»)."""
    plain = (name or "").strip()

    # В переписи имя идёт с внутренним в скобках: «Wolf (WildWolf)». Для поимённых вилок
    # сверяем по видимой части, иначе ни одна не совпадёт.
    bare = plain.split("(")[0].strip()
    low_name = bare.lower()

    for key, level in FIXED.items():
        if key.lower() in plain.lower():
            return level, level, str(level)

    for key, span in BY_HAND.items():
        if low_name == key.lower():
            return span[0], span[1], "%d-%d" % span

    if born <= 0:
        return born, born, ""

    return born, born * SPREAD, "%d-%d" % (born, born * SPREAD)
