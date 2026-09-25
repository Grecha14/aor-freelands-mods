# -*- coding: utf-8 -*-
import sim,statistics,collections,math
HP=180.0; HEAD=HP*0.15; BODY=HP*0.50
STR=10
HEADK={0:2.5,1:0.5,2:1.5}          # рубящее, дробящее, колющее — цена удара в голову

typ={}
for z in ("head","chest","pants","belt"):
    v=[a["dur"] for a in sim.A if a["slot"]==z and a["dur"]>0]
    typ[z]=statistics.median(v)

def best(cls,tier=None):
    g=[w for w in sim.W if w["cls"]==cls and sum(sim.dmg_of(w))>1]
    if tier: g=[w for w in g if w["tier"]==tier]
    if not g: return None
    return sorted(g,key=lambda w:sum(sim.dmg_of(w)))[len(g)//2]

def piece(cls,slot):
    g=[a for a in sim.A if a["cls"]==cls and a["slot"]==slot and a["dur"]>0]
    if not g: return None
    return sorted(g,key=lambda a:a["dur"])[len(g)//2]

def punch(w,strength=STR,k=1.0):
    return strength*w["kg"]*sum(sim.dmg_of(w))*k

def through(p,armour,slot,m):
    # мягкое отношение: сколько ни брони, что-то проходит; сколько ни силы, всё не пройдёт
    if armour is None: return 1.0
    q=armour["dur"]/typ[slot]*m
    return p/(p+q)

def blows(w,armour,slot,pool,k,m,strength=STR):
    d=sum(sim.dmg_of(w))
    kind=sim.lead(w["hits"])
    t=through(punch(w,strength,k),armour,slot,m)
    got=d*t*(HEADK[kind] if slot=="head" else 1.0)
    return pool/max(0.01,got)

TARGET=[
    ("GreatSword",None,"head",HEAD,1.0),
    ("GreatSword","PlateArmor","head",HEAD,2.0),
    ("Sword",None,"head",HEAD,2.0),
    ("BattleAxe",None,"head",HEAD,1.0),
    ("Mace",None,"head",HEAD,2.5),
    ("Dagger",None,"head",HEAD,4.5),
    ("GreatSword",None,"chest",BODY,5.0),
    ("Sword","ChainArmor","chest",BODY,9.0),
]
def cost(k,m):
    bad=0.0
    for cls,acls,slot,pool,want in TARGET:
        w=best(cls)
        if not w: continue
        a=piece(acls,slot) if acls else None
        got=blows(w,a,slot,pool,k,m)
        bad+=(math.log(max(0.3,got))-math.log(want))**2
    return bad
