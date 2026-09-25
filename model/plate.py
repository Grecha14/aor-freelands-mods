# -*- coding: utf-8 -*-
"""Новая защита: толщина в миллиметрах и стойкость материала к типу удара.

Одно место, откуда числа берут и модель боя, и таблица. Пока мод считает по-старому, это
опережающая запись: сюда сведено всё, о чём договорились, и отсюда пойдёт перенос.
"""
import arena as A

# Сколько единиц «стат × вес оружия» идёт на один миллиметр.
K = 50.0

# Концентрация удара: во что собрана сила. Болт и клевец бьют в точку, клинок — в полосу.
FOCUS = {"Crossbow": 2.2, "HeavyCrossbow": 2.2, "Longbow": 1.2, "Shortbow": 1.2,
         "TwoHandHammer": 1.3, "TwoHandMace": 1.3, "TwoHandClub": 1.3, "TwoHandFlail": 1.3,
         "Poleaxe": 1.3, "GreatSword": 1.3, "Katana": 1.3, "GreatAxe": 1.3, "DanAxe": 1.3,
         "BastardSword": 1.3, "LongSword": 1.3, "Falchion": 1.3,
         "Dagger": 1.8, "Katar": 1.8, "Rapier": 1.3, "Estoc": 1.0}

# Толщина набора на первой ступени класса, в миллиметрах стального эквивалента.
# Броня держит столько-то урона — та же таблица, что легла в «Breach.Holds».
STOPS = {"Cloth": (4, 2, 1), "PaddingArmor": (8, 5, 3), "LightLeatherArmor": (13, 8, 8),
         "HardLeaterArmor": (22, 14, 14), "SplintArmor": (30, 16, 22),
         "ChainArmor": (30, 8, 16), "ScaleMail": (38, 20, 30), "LamellarArmor": (44, 24, 34),
         "HalfPlate": (52, 34, 40), "PlateArmor": (90, 45, 62),
         "metalHelmet": (52, 34, 40), "leatherHelemt": (22, 14, 14)}

HONE = [float(x) for x in (A.conf.get("Hone", "Ladder") or "1.00,1.23,1.68,2.36,3.50").split(",")]


def stops(cls, tier, kind):
    """Сколько урона держит эта вещь: растёт по той же лестнице, что и урон."""
    first = A.FIRSTS.get(cls, 1)
    if tier < first:
        return None
    a = HONE[min(max(first, 1), len(HONE)) - 1]
    b = HONE[min(max(tier, 1), len(HONE)) - 1]
    return STOPS[cls][kind] * b / a


MM = {"Cloth": 0.05, "PaddingArmor": 0.12, "LightLeatherArmor": 0.25,
      "HardLeaterArmor": 0.45, "SplintArmor": 0.9, "ChainArmor": 1.0, "ScaleMail": 1.3,
      "LamellarArmor": 1.5, "HalfPlate": 1.6, "PlateArmor": 2.6,
      "metalHelmet": 2.0, "leatherHelemt": 0.45}

# Стойкость материала: во сколько раз толще он кажется этому удару. Рез, укол, обух.
HOLD = {"Cloth": (1.5, 0.5, 0.8), "PaddingArmor": (1.5, 0.5, 0.8),
        "LightLeatherArmor": (1.18, 0.7, 0.7), "HardLeaterArmor": (1.18, 0.7, 0.7),
        "SplintArmor": (2.2, 1.5, 0.9), "ChainArmor": (2.0, 1.0, 0.5),
        "ScaleMail": (2.2, 1.7, 1.1), "LamellarArmor": (2.4, 1.8, 1.2),
        "HalfPlate": (4.0, 3.0, 2.6), "PlateArmor": (5.0, 4.0, 3.5),
        "metalHelmet": (4.0, 3.0, 2.6), "leatherHelemt": (1.18, 0.7, 0.7)}

RU = {"Cloth": "ткань", "PaddingArmor": "стёганка", "LightLeatherArmor": "лёгкая кожа",
      "HardLeaterArmor": "твёрдая кожа", "SplintArmor": "шинная", "ChainArmor": "кольчуга",
      "ScaleMail": "чешуя", "LamellarArmor": "ламеллярная", "HalfPlate": "полулаты",
      "PlateArmor": "латы", "metalHelmet": "шлем металлический",
      "leatherHelemt": "шлем кожаный"}

KIND = {"sharp": 0, "stab": 1, "blunt": 2}
KIND_RU = {"sharp": "рез", "stab": "укол", "blunt": "обух"}

# Доля куска от набора — та же, по которой делится вес.
SLOT = {"head": 0.2, "chest": 0.5, "pants": 0.3}
SLOT_RU = {"head": "шлем", "chest": "нагрудник", "pants": "поножи"}


def thick(cls, tier, slot=None):
    """Толщина этой вещи: набор по лестнице тира, и доля куска, если он назван."""
    first = A.FIRSTS.get(cls, 1)
    if tier < first:
        return None

    much = MM[cls] * A.RUNGS[min(tier, 5)] / A.RUNGS[min(first, 5)]

    # Кусок не тоньше набора — он и есть та же сталь; доля меняет вес, а не толщину.
    return much


def need(cls, tier, kind):
    """Сколько миллиметров надо продавить, чтобы пройти этот доспех этим ударом."""
    t = thick(cls, tier)
    return None if t is None else t * HOLD[cls][KIND.get(kind, 0)]


def share(mm, must):
    """Доля удара, прошедшая внутрь: ниже трёх четвертей ничего, выше четверти сверх — всё."""
    if must is None or must <= 0:
        return 1.0
    return max(0.0, min(1.0, (mm - 0.75 * must) / (0.5 * must)))


def punch(stat, kg, cls):
    """Пробитие в миллиметрах: рука, железо и то, во что собран удар."""
    return stat * kg / K * FOCUS.get(cls, 1.0)
