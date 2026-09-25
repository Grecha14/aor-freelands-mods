# -*- coding: utf-8 -*-
import io,json,math,random,collections,statistics
G=json.load(io.open("gear.json",encoding="utf-8"))
W,A=G["W"],G["A"]

SHARP,BLUNT,STAB=0,1,2
KIND={"sharp":SHARP,"blunt":BLUNT,"stab":STAB}

RARITY={"Poor":0.92,"Common":1,"Uncommon":1.08,"Rare":1.25,"Epic":1.5,"Legendary":3}
GRADE={"Poor":1,"Common":1,"Uncommon":1,"Rare":2,"Epic":3,"Legendary":5}

SOLID=0.30; BITE=0.45; BLUNTED=0.15; CAP=80.0
FACING=[1.4,0.5,1.3]
FORCE=[0.6,1.6,1.2]
SWING=[0.6,1.0,0.4]
PERKILO=0.10; MOST=3.0
STEADY=0.6; TEMPER=0.9

ZONES=["head","chest","pants","belt"]
WEIGHT={"head":20,"chest":45,"pants":25,"belt":10}

def lead(hits):
    best=-1;kind=SHARP
    for k,(lo,hi) in hits.items():
        i=KIND.get(k)
        if i is None: continue
        if hi>best: best=hi;kind=i
    return kind

def dmg_of(w):
    tot=[0.0,0.0,0.0]
    for k,(lo,hi) in w["hits"].items():
        i=KIND.get(k)
        if i is None: continue
        tot[i]+=(lo+hi)/2
    return tot

def weapon_damage(w,precision=50,mastery=50,rarity="Common"):
    kind=lead(w["hits"])
    base=sum(dmg_of(w))
    base*=RARITY.get(rarity,1)
    base*=min(1+w["kg"]*PERKILO*SWING[kind],MOST)
    if kind==BLUNT: base*=TEMPER
    # разброс по восприятию: средняя доля вилки
    part=1/(1+1/(1+precision/100*2))
    base*= (0.5+0.5*part)/0.75
    return base,kind

def armour_at(suit,zone):
    p=suit.get(zone)
    if not p: return 0.0,0.0,None
    dur=p["dur"]
    guard=SOLID*math.sqrt(dur)
    gate=BITE*math.sqrt(dur)
    return guard,gate,p

def strike(w,suit,precision=50,mastery=50,rarity="Common",aim=True):
    dmg,kind=weapon_damage(w,precision,mastery,rarity)
    if aim:
        wit=min(mastery*0.008,0.9)
        if random.random()<wit:
            zone=min(ZONES,key=lambda z:(suit[z]["dur"] if suit.get(z) else 0))
        else:
            zone=random.choices(ZONES,weights=[WEIGHT[z] for z in ZONES])[0]
    else:
        zone=random.choices(ZONES,weights=[WEIGHT[z] for z in ZONES])[0]
    sure=min(0.4+mastery*0.005+precision*0.002,0.95)
    if random.random()>sure:
        i=ZONES.index(zone); i=max(0,min(len(ZONES)-1,i+random.choice([-1,1]))); zone=ZONES[i]
    guard,gate,piece=armour_at(suit,zone)
    punch=dmg*FORCE[kind]
    if piece and punch<gate:
        return dmg*BLUNTED,zone,False
    if piece:
        g=guard*FACING[kind]
        pct=min(CAP,100*g/max(1,dmg))
        dmg*= (1-pct/100)
    return dmg,zone,True

def suits(kind,tier):
    out={}
    for z in ["head","chest","pants","belt"]:
        got=[a for a in A if a["kind"]==kind and a["tier"]==tier and a["slot"]==z]
        if got: out[z]=max(got,key=lambda a:a["dur"])
    return out

HP=800.0
def fight(w,suit,foes=1,precision=50,mastery=50,rarity="Common"):
    # сколько ударов нужно, чтобы снести одного, и сколько пропустишь от N врагов
    total=0;n=0
    left=HP
    while left>0 and n<500:
        d,_,_=strike(w,suit,precision,mastery,rarity)
        left-=d; n+=1
    return n
