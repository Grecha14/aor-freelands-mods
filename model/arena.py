# -*- coding: utf-8 -*-
"""Испытания на математической модели: кто кого и за сколько.

Считается ровно тем, чем считает мод, и числа берутся из живого конфига и из выгрузки
предметов, а не сочиняются здесь:

  попадание   Aim.Steady:   0.4 + мастерство × 0.005 + точность × 0.002
                            + доспех × 0.002 − уворот цели × 0.004,  зажато в 0.05…0.95
  место       Anatomy.Spots: грудь 45, ноги 25, голова 20, пояс 10
  пробитие    Breach.Push:  ведущий стат × вес оружия × (1 + мастерство × 0.01)
                            × (1 + своё пробитие вещи × Penned)
  вычет       Breach.Guard: основа класса × рост^(тир − первый) × доля куска
  прошло      Breach.Share: урон × мимо + max(0, урон × (1 − мимо) − (вычет − пробитие))
  мимо        крит — доля из Gap (0.30…0.50); дробящее — Through класса
  статы       WriteUnitAttribute: урон +1 % за силу, замах +1 % за ловкость,
                            уворот = ловкость + 0.5 × ума, крит = точность,
                            здоровье = 100 + уровень × 5 + 5 × выносливости
  доспех      Garb: ход, уворот, меткость и замах по весу набора

Чего в модели нет и о чём судить по ней нельзя: заклинаний, ядов, блока щитом, позиции и
дистанции, талантов, лечения в бою. Это счёт голой рубки.
"""
import io
import json
import math
import random

import conf

random.seed(20260923)

GEAR = json.load(io.open("gear.json", encoding="utf-8"))

# ------------------------------------------------------------------ из конфига

KEEN = [float(x) for x in (conf.get("Breach", "Keen") or "1,1.21,1.43,1.86,2.5").split(",")]
GUARDS = conf.listed("Breach", "Guards")
HARD = set((conf.get("Breach", "Hard") or "").replace(" ", "").split(","))
SOFT = conf.flat("Breach", "Soft", 1.2)
STEEP = conf.flat("Breach", "Steep", 1.8)
RUNGS = [float(x) for x in
         (conf.get("Breach", "Rungs") or "1,1.44,1.88,2.77,4.09,6.30").split(",") if x.strip()]
CHEST = conf.flat("Breach", "Chest", 1.2)
LEGS = conf.flat("Breach", "Legs", 0.8)
PENNED = conf.flat("Breach", "Penned", 1.0)
SKILL = conf.flat("Breach", "Skill", 0.01)


def _through():
    """«Сквозь» по классам: у записи может быть вилка «0.15-0.35», как в моде."""
    got = {}

    for piece in (conf.get("Breach", "Through") or "").split(","):
        if "=" not in piece:
            continue

        name, _, said = piece.partition("=")
        ends = said.strip().split("-")

        try:
            if len(ends) == 2:
                got[name.strip()] = (float(ends[0]), float(ends[1]))
            else:
                one = float(said)
                got[name.strip()] = (one, one)
        except ValueError:
            continue

    return got


THROUGH = _through()
GAP = [float(x) for x in (conf.get("Breach", "Gap") or "0.30,0.50").split(",")]

AIM_BASE = conf.flat("Aim", "Base", 0.4)
PER_MASTERY = conf.flat("Aim", "PerMastery", 0.005)
PER_PRECISION = conf.flat("Aim", "PerPrecision", 0.002)
PER_DODGE = conf.flat("Aim", "PerDodge", 0.004)
PER_ARMOUR = conf.flat("Aim", "PerArmour", 0.002)
STEADIEST = conf.flat("Aim", "Steadiest", 0.95)

MATERIALS = conf.listed("Burden", "Materials")
TIER_KG = [float(x) for x in (conf.get("Burden", "TierLadder") or "1,1.042,1.083,1.167,1.292,1.5").split(",")]
SET_SHARE = [float(x) for x in (conf.get("Burden", "SetShare") or "0.2,0.5,0.3").split(",")]

FIRSTS = {"ChainArmor": 2, "ScaleMail": 2, "LamellarArmor": 2, "HalfPlate": 2, "PlateArmor": 2}

SPOTS = {"chest": 45, "pants": 25, "head": 20, "belt": 10}

# --- тело по частям ------------------------------------------------------------
# Своё здоровье у каждой части, и выносливость прибавляет к нему скупо: единицу туловищу,
# половину руке и ноге, четверть голове. Всё прочее, что давала выносливость, остаётся при
# ней — вычет, запас сил, восполнение, — но не превращается в мешок здоровья.
PARTS = {"голова": 40.0, "туловище": 110.0,
         "рука правая": 35.0, "рука левая": 35.0,
         "нога правая": 50.0, "нога левая": 50.0}

PER_END = {"голова": 0.25, "туловище": 1.0,
           "рука правая": 0.5, "рука левая": 0.5,
           "нога правая": 0.5, "нога левая": 0.5}

# Куда приходится удар. Голова меньше прочего, но и защищена хуже всего.
WHERE = {"голова": 15, "туловище": 40,
         "рука правая": 10, "рука левая": 10,
         "нога правая": 12, "нога левая": 13}

# Чем закрыта часть: в игре есть шлем, нагрудник и поножи, наручей отдельным слотом нет —
# руки считаем прикрытыми нагрудником вполсилы.
COVER = {"голова": ("head", 1.0), "туловище": ("chest", 1.0),
         "рука правая": ("chest", 0.6), "рука левая": ("chest", 0.6),
         "нога правая": ("pants", 1.0), "нога левая": ("pants", 1.0)}
SLOT_SHARE = {"chest": CHEST, "head": 1.0, "pants": LEGS, "belt": LEGS}
PIECE_KG = {"head": SET_SHARE[0], "chest": SET_SHARE[1], "pants": SET_SHARE[2], "belt": 0.0}


def ladder(written, kilos):
    """Лестница «вес=значение» из настроек Garb, с чтением между точками."""
    steps = []
    for piece in (written or "").split(","):
        if "=" not in piece:
            continue
        a, _, b = piece.partition("=")
        try:
            steps.append((float(a), float(b)))
        except ValueError:
            continue

    if not steps:
        return 0.0

    steps.sort()
    if kilos <= steps[0][0]:
        return steps[0][1]
    if kilos >= steps[-1][0]:
        return steps[-1][1]

    for i in range(1, len(steps)):
        if kilos <= steps[i][0]:
            (x0, y0), (x1, y1) = steps[i - 1], steps[i]
            return y0 + (y1 - y0) * (kilos - x0) / (x1 - x0)

    return steps[-1][1]


GARB_SPEED = conf.get("Garb", "Speed") or \
    "3.6=20,5.3=18,7.1=15,9.8=8,14.2=0,17.8=0,19.6=-6,23.1=-13,26.7=-20,40=-25"
GARB_DODGE = conf.get("Garb", "Dodging") or \
    "3.6=20,5.3=18,7.1=15,9.8=10,14.2=4,16=3,17.8=2,19.6=-7,23.1=-13,26.7=-20,40=-25"
GARB_AIM = conf.get("Garb", "Aiming") or \
    "3.6=0,5.3=2,7.1=4,9.8=15,14.2=12,16=10,17.8=8,19.6=-6,23.1=-13,26.7=-20,40=-25"
GARB_SWING = conf.get("Garb", "Swing") or \
    "3.6=5,5.3=4,7.1=3,9.8=2,14.2=1,16=0,17.8=0,19.6=-6,23.1=-13,26.7=-20,40=-25"


# ------------------------------------------------------------------ снаряжение

def guard(cls, tier, slot):
    """Вычет куска — тем же счётом, что «Breach.Guard»."""
    base = GUARDS.get(cls)
    if not base:
        return 0.0

    first = FIRSTS.get(cls, 1)
    if tier < first:
        return None

    # Лестница одна на все классы, класс входит в неё со своей ступени — как в
    # «Breach.Guard» после правки.
    bottom = RUNGS[min(first, len(RUNGS) - 1)]
    step = RUNGS[min(tier, len(RUNGS) - 1)] / bottom if bottom else 1.0

    return base * step * SLOT_SHARE.get(slot, 1.0)


def suit_kg(cls, tier):
    """Вес полного набора этого материала и ступени — по «Burden»."""
    base = MATERIALS.get(cls)
    if not base:
        return 0.0
    return base * TIER_KG[min(tier, len(TIER_KG) - 1)]


def dressed(cls, tier):
    """Что доспех даёт и отнимает: ход, уворот, меткость, замах."""
    kg = suit_kg(cls, tier)
    if kg <= 0:
        return {"speed": 0.0, "dodge": 0.0, "aim": 0.0, "swing": 0.0}

    return {"speed": ladder(GARB_SPEED, kg), "dodge": ladder(GARB_DODGE, kg),
            "aim": ladder(GARB_AIM, kg), "swing": ladder(GARB_SWING, kg)}


LEAD = {"sharp": 0, "blunt": 0, "stab": 2}          # что ведёт удар: сила или ловкость

# Лестница урона по ступеням — та, что мод пишет в вещи через «Hone».
HONE = [float(x) for x in
        (conf.get("Hone", "Ladder") or "1.00,1.23,1.68,2.36,3.50").split(",") if x.strip()]


def honed(cls, tier, hit, first):
    """Урон этой ступени по лестнице, считая от той, с которой класс существует."""
    if not HONE:
        return hit

    a = HONE[min(max(first, 1), len(HONE)) - 1]
    b = HONE[min(max(tier, 1), len(HONE)) - 1]

    return hit * (b / a if a else 1.0)


def weapon(cls, tier):
    """Средняя вещь этого класса и ступени из выгрузки: урон, вес, скорость."""
    same = [w for w in GEAR["W"]
            if w["cls"] == cls and w["tier"] == "T%d" % tier and w.get("q") == "Common"]

    if not same:
        same = [w for w in GEAR["W"] if w["cls"] == cls and w["tier"] == "T%d" % tier]
    if not same:
        return None

    hit = 0.0
    kind = "sharp"
    best = 0.0

    for w in same:
        for k, (lo, hi) in w["hits"].items():
            hit += (lo + hi) * 0.5 / len(same)
            if k in LEAD and (lo + hi) * 0.5 > best:
                best = (lo + hi) * 0.5
                kind = k

    kg = sum(w.get("kg") or 0.0 for w in same) / len(same)
    sp = sum(w.get("sp") or 0.4 for w in same) / len(same)

    # Урон кладём на ту же лестницу, что мод пишет в вещи: считаем от низшей ступени,
    # на которой класс вообще есть, и от её же настоящего урона.
    lowest = [int(w["tier"][1:]) for w in GEAR["W"]
              if w["cls"] == cls and w["tier"][1:].isdigit() and int(w["tier"][1:]) >= 1]
    first = min(lowest) if lowest else 1

    if first != tier:
        base = [w for w in GEAR["W"]
                if w["cls"] == cls and w["tier"] == "T%d" % first and w.get("q") == "Common"]
        if not base:
            base = [w for w in GEAR["W"] if w["cls"] == cls and w["tier"] == "T%d" % first]

        if base:
            low = 0.0
            for w in base:
                for k, (lo, hi) in w["hits"].items():
                    low += (lo + hi) * 0.5 / len(base)

            if low > 0:
                hit = honed(cls, tier, low, first)

    # Сколько разных типов урона несёт вещь и как её держат: по этому «Edge» решает,
    # чего стоит крит.
    kinds = set()
    for w in same:
        for k, (lo, hi) in w["hits"].items():
            if hi > 0:
                kinds.add(k)

    held = same[0].get("wt", "onehand")

    return {"cls": cls, "tier": tier, "hit": hit, "kind": kind, "held": held,
            "kinds": len(kinds),
            "kg": kg * KEEN[min(tier, len(KEEN)) - 1], "sp": sp}


EDGE = {}
for _piece in (conf.get("Edge", "Table") or "blunt=1.0,mixed=1.0,onehand=1.5,twohand=2.0").split(","):
    if "=" in _piece:
        _a, _, _b = _piece.partition("=")
        try:
            EDGE[_a.strip()] = float(_b)
        except ValueError:
            pass


def edge(arm):
    """Множитель крита по тому, чем бьют — как в «Edge.Kind»."""
    if arm["kinds"] > 1:
        return EDGE.get("mixed", 1.0)
    if arm["kind"] == "blunt":
        return EDGE.get("blunt", 1.0)
    if arm["held"] in ("twohand", "polearms"):
        return EDGE.get("twohand", 2.0)
    return EDGE.get("onehand", 1.5)


# ------------------------------------------------------------------ боец

# Сколько щит прибавляет к блоку и сколько весит. Вес игровой, прибавка наша: в выгрузке
# её нет, а щит без неё — просто дубина в левой руке.
# Сколько сам щит прибавляет к шансу блока. Потолок — тридцать пять у ростового, как в
# игре; статы прибавляют сверх этого своим чередом (десятка от вещи плюс точность).
SHIELDS = {"Buckler": 15.0, "RoundShield": 22.0, "HeaterShield": 25.0,
           "KiteShield": 28.0, "TowerShield": 35.0}

# Чем оружие ломает блок. В игре это «BSblockBreak» у вещи; в выгрузку оно не попало,
# поэтому берём по хвату и типу удара: тяжёлому двуручному щит держать труднее всего.
BREAKS = {"twohand_blunt": 30.0, "twohand": 20.0, "polearms": 15.0,
          "onehand_blunt": 10.0, "onehand": 5.0}


def shield(cls, tier):
    """Щит этого вида и ступени: вес и прибавка к блоку."""
    same = [w for w in GEAR["W"] if w["cls"] == cls and w["tier"] == "T%d" % tier]
    if not same:
        same = [w for w in GEAR["W"] if w["cls"] == cls]
    if not same:
        return None

    kg = sum(w.get("kg") or 0.0 for w in same) / len(same)

    return {"cls": cls, "kg": kg * KEEN[min(tier, len(KEEN)) - 1],
            "block": SHIELDS.get(cls, 20.0)}


def breaks(arm):
    """Сколько этот удар отнимает у чужого блока."""
    two = arm["held"] in ("twohand", "polearms")

    if arm["held"] == "polearms":
        return BREAKS["polearms"]
    if two:
        return BREAKS["twohand_blunt"] if arm["kind"] == "blunt" else BREAKS["twohand"]

    return BREAKS["onehand_blunt"] if arm["kind"] == "blunt" else BREAKS["onehand"]


class Man(object):
    def __init__(self, name, level, six, mastery, arm, coat, coat_tier, guard_shield=None):
        self.name = name
        self.level = level
        self.str, self.end, self.agi, self.pre, self.int_, self.wil = six
        self.mastery = mastery
        self.arm = arm
        self.coat = coat
        self.coat_tier = coat_tier

        worn = dressed(coat, coat_tier)
        self.shield = guard_shield

        # Тело по частям. Уровень прибавляет всем понемногу — он и есть «вырос и окреп», —
        # а выносливость по мерке из PER_END.
        self.limbs = {}
        self.limbmax = {}

        for part, base in PARTS.items():
            much = base + level * 0.5 + PER_END[part] * self.end
            self.limbmax[part] = much
            self.limbs[part] = much

        self.hpmax = sum(self.limbmax.values())
        self.hp = self.hpmax

        # Выносливость: запас и восполнение по «WriteUnitAttribute». Блок тратит её, и
        # когда она кончилась, щит больше не держит.
        self.spmax = 100 + 5 * self.end
        self.sp = self.spmax
        self.sprest = 0.1 * self.end + 0.2 * self.wil

        # Блок: игровые десять от вещи, плюс точность, плюс щит. Зажат сотней.
        self.block = min(100.0, 10.0 + self.pre + (guard_shield["block"] if guard_shield else 0.0))

        # Уворот и крит по игровому расчёту, плюс то, что дал доспех.
        self.dodge = self.agi + 0.5 * self.int_ + worn["dodge"]
        self.crit = min(0.75, self.pre / 100.0)
        self.critmul = edge(arm) + self.int_ * 0.01
        self.aimbonus = worn["aim"]

        # Замахов в секунду. Игра множит скорость анимации удара на «0.5 + attackspeed»
        # («ani.SetFloat("attackspeed", 0.5f + attackspeed)»), то есть чем число больше, тем
        # быстрее бьют: кинжал 0.77, двуручный молот 0.35.
        #
        # Прежде здесь стояло обратное — «1 / (0.5 + скорость)», — и молот в модели махал
        # чаще кинжала. Отсюда и вышла его непобедимость в прошлых прогонах.
        self.rate = (0.5 + arm["sp"]) * (1 + self.agi * 0.01 + worn["swing"] / 100.0)

        # Щит висит на той же руке, что и подвижность: его килограммы идут в общий счёт
        # так же, как доспешные.
        if guard_shield:
            extra = ladder(GARB_SWING, suit_kg(coat, coat_tier) + guard_shield["kg"])                 - worn["swing"]
            self.rate *= 1 + extra / 100.0

        self.damage = arm["hit"] * (1 + self.str * 0.01)
        lead = LEAD.get(arm["kind"], 0)
        stat = self.agi if lead == 2 else self.str
        # Пробитие — только рука и железо. Мастерство отсюда убрано: умение решает,
        # попал ли ты и куда, а не сколько железа продавил. Мастер бьёт в стык.
        self.pierce = stat * arm["kg"]

        self.wall = {}
        for slot in SPOTS:
            self.wall[slot] = guard(coat, coat_tier, slot)

        # И та же стена, разнесённая по частям тела: чем каждая прикрыта и насколько.
        self.cover = {}
        for part, (slot, share) in COVER.items():
            much = guard(coat, coat_tier, slot)
            self.cover[part] = None if much is None else much * share

        self.timer = 0.0

    @property
    def alive(self):
        """Жив, пока целы голова и туловище: руку и ногу можно потерять и стоять."""
        return self.limbs["голова"] > 0 and self.limbs["туловище"] > 0


def swing(a, b, facing=True):
    """Один удар: попал ли, отбили ли щитом, куда пришёлся и сколько дошло."""
    # Мастерство отсчитывается от половины: ниже пятидесяти рука ещё не своя и
    # штрафует, выше — ведёт. На нуле минус двадцать пять пунктов, на сотне плюс.
    sure = AIM_BASE + (a.mastery - 50) * PER_MASTERY + a.pre * PER_PRECISION \
        + a.aimbonus * PER_ARMOUR - b.dodge * PER_DODGE
    sure = max(0.05, min(STEADIEST, sure))

    if random.random() > sure:
        return 0.0, None

    # Щит. В игре блок ловит только то, что идёт в лицо, и тратит выносливость; когда её
    # нет, блок не удаётся. Мы считаем так же: заслониться можно от того, на кого сам
    # смотришь, — на второго и третьего рук не хватает.
    if facing and b.shield is not None and b.sp > 0:
        chance = (b.block - breaks(a.arm)) / 100.0

        if random.random() < chance:
            # Игра берёт за блок полную силу удара: «point = DamageReduce(attack).Damage()
            # × StaminaDamageMD × (1 − blockEPsave)». Половина, что стояла здесь раньше,
            # делала щит почти бесплатным.
            cost = a.damage
            if b.sp >= cost:
                b.sp -= cost
                return 0.0, None

    spot = random.choices(list(WHERE.keys()), weights=list(WHERE.values()))[0]
    wall = b.cover.get(spot)

    coming = a.damage
    crit = random.random() < a.crit

    if crit:
        coming *= a.critmul

    always = 0.0
    if a.arm["kind"] == "blunt":
        low, high = THROUGH.get(b.coat, (0.3, 0.3))
        always = random.uniform(low, high) if high > low else high
    if crit:
        always = max(always, random.uniform(GAP[0], GAP[1]))

    if wall is None or wall <= 0:
        return coming, spot

    rest = wall - a.pierce
    if rest <= 0:
        return coming, spot

    stopped = coming * (1 - always)
    return coming * always + max(0.0, stopped - rest), spot


def fight(left, right, limit=180.0, step=0.05):
    """Схватка двух отрядов. Бьют по первому живому, идут по времени."""
    for m in left + right:
        m.hp = m.hpmax
        m.sp = m.spmax
        for part in m.limbs:
            m.limbs[part] = m.limbmax[part]
        m.timer = random.random() / max(0.1, m.rate)

    time = 0.0

    while time < limit:
        time += step

        # Выносливость возвращается сама, и от неё зависит, сколько ещё щит удержит.
        for m in left + right:
            if m.alive and m.sp < m.spmax:
                m.sp = min(m.spmax, m.sp + m.sprest * step)

        for side, foes in ((left, right), (right, left)):
            for m in side:
                if not m.alive:
                    continue

                m.timer -= step
                if m.timer > 0:
                    continue

                m.timer += 1.0 / m.rate

                target = next((f for f in foes if f.alive), None)
                if target is None:
                    continue

                # Цель смотрит на своего противника; если это мы — она может заслониться.
                hers = next((f for f in side if f.alive), None)
                much, part = swing(m, target, facing=(hers is m))

                if much > 0 and part is not None:
                    target.limbs[part] -= much
                    target.hp -= much

                    # Рука вышла из строя — бить нечем и держать нечем; нога — не увернёшься.
                    # Голова и туловище решают жизнь.
                    if target.limbs[part] <= 0:
                        if part.startswith("рука"):
                            target.rate *= 0.6
                            if target.shield is not None:
                                target.shield = None
                                target.block = min(100.0, 10.0 + target.pre)
                        elif part.startswith("нога"):
                            target.dodge *= 0.5
                            target.rate *= 0.85

        if not any(m.alive for m in left):
            return "right", time
        if not any(m.alive for m in right):
            return "left", time

    return "ничья", time


def duel(maker_a, maker_b, n_a=1, n_b=1, runs=300):
    """Сколько раз из ста побеждает левая сторона и за сколько секунд."""
    won = 0
    draws = 0
    seconds = []

    for _ in range(runs):
        left = [maker_a() for _ in range(n_a)]
        right = [maker_b() for _ in range(n_b)]

        who, when = fight(left, right)

        if who == "left":
            won += 1
            seconds.append(when)
        elif who == "ничья":
            draws += 1

    share = 100.0 * won / runs
    mid = sum(seconds) / len(seconds) if seconds else float("nan")

    return share, mid, 100.0 * draws / runs


# ------------------------------------------------------------------ этапы

#          имя        уровень  сила вын лов точн ум воля  мастерство
STAGES = [("начало",   10, (12, 10, 12, 10, 8, 8), 10),
          ("середина", 25, (25, 22, 20, 20, 14, 14), 30),
          ("конец",    45, (45, 40, 35, 35, 25, 25), 50)]

COATS = [("ткань", "Cloth"), ("лёгкая кожа", "LightLeatherArmor"),
         ("твёрдая кожа", "HardLeaterArmor"), ("кольчуга", "ChainArmor"),
         ("чешуя", "ScaleMail"), ("полулаты", "HalfPlate"), ("латы", "PlateArmor")]

ARMS = [("меч", "Sword"), ("булава", "Mace"), ("копьё", "Spear"),
        ("двуручный молот", "TwoHandHammer"), ("двуручный меч", "GreatSword")]
