# -*- coding: utf-8 -*-
# Полный прогон: пробитие всех броней всеми видами оружия, по тирам и редкости,
# с критами и статами.
import io,json
import six as M
P=json.load(io.open("six.json",encoding="utf-8"))

KGG=1.20        # вес: +20% за тир
GR=1.20         # пробитие: x1.20 за ступень редкости
GV=1.44         # вычет брони: x1.44 за тир и за ступень редкости
BASE_V={"ткань":1.0,"лёгкая кожа":2.5,"твёрдая кожа":4.0,"кольчуга":12.0,
        "чешуя":19.0,"полулаты":27.5,"латы":43.5}
BLUNT={"TwoHandHammer","TwoHandMace","TwoHandClub","TwoHandFlail","Poleaxe",
       "Mace","Hammer","Club","Flail"}
TWO={"twohand","polearms"}
CRIT={("sharp",0):1.00,("sharp",1):1.50,("stab",0):3.00,("stab",1):1.50,
      ("blunt",0):0.50,("blunt",1):1.00}
RAR=["Common","Uncommon","Rare","Epic","Legendary"]

def kind(c):
    sp=M.split(M.weap(c),P); return ["sharp","blunt","stab"][sp.index(max(sp))]
def critb(c):
    w=M.weap(c); return CRIT[(kind(c),1 if w["wt"] in TWO else 0)]
def punch(c,wt,wr,S,crit=False):
    kg=M.kg(M.weap(c),P)*KGG**wt
    p=S*kg*GR**wr
    return p*(1+critb(c)) if crit else p
def resist(a,at,ar,c,head=False,crit=False):
    v=BASE_V[a]*GV**(at+ar)
    if c in BLUNT: v*=0.50                    # дробящее игнорирует половину
    if head and kind(c)=="stab" and crit: v*=0.10   # щель в забрале, только критом
    return v
def frac(c,wt,wr,a,at,ar,S,head=False,crit=False):
    p=punch(c,wt,wr,S,crit); v=resist(a,at,ar,c,head,crit)
    return max(0.0,(p-v)/p) if p>0 else 0.0

WEAPONS=[("кинжал","Dagger"),("рапира","Rapier"),("копьё","Spear"),
         ("меч","Sword"),("топор","BattleAxe"),("булава","Mace"),
         ("двуручный меч","GreatSword"),("большой топор","GreatAxe"),
         ("двуручное копьё","TwoHandSpear"),("алебарда","Halberd"),
         ("поллекс","Poleaxe"),("двуручный молот","TwoHandHammer"),
         ("двуручная булава","TwoHandMace")]
ARMOURS=list(BASE_V)

def table(title,S,wt,wr,at,ar,crit=False,head=False):
    print(title)
    print("  %-18s"%"оружие"+"".join(a.rjust(14) for a in ARMOURS))
    for ru,c in WEAPONS:
        line="  %-18s"%ru
        for a in ARMOURS:
            line+=("%.0f%%"%(100*frac(c,wt,wr,a,at,ar,S,head,crit))).rjust(14)
        print(line)
    print()

if __name__=="__main__":
    print("="*110)
    print("ПРОБИТИЕ БРОНИ — полный прогон")
    print("  пробитие = сила x вес(+20%/тир) x 1,20^редкость")
    print("  вычет    = базовый x 1,44^(тир+редкость), дробящему x0,50")
    print("  крит умножает пробитие по типу и рукам; укол в голову критом находит щель (x0,10)")
    print("="*110); print()

    for S,wt,wr,at,ar,nm in [
        (10,0,0,0,0,"НАЧАЛО ИГРЫ: сила 10, оружие Т1 Common, броня Т1 Common"),
        (20,1,1,1,1,"РАННЯЯ СЕРЕДИНА: сила 20, оружие Т2 Uncommon, броня Т2 Uncommon"),
        (35,2,2,2,2,"СЕРЕДИНА: сила 35, оружие Т3 Rare, броня Т3 Rare"),
        (50,3,3,3,3,"ПОЗДНЯЯ: сила 50, оружие Т4 Epic, броня Т4 Epic"),
        (65,3,4,4,4,"КОНЕЦ: сила 65, оружие Т4 Legendary, броня Т5 Legendary")]:
        table("### %s — ОБЫЧНЫЙ УДАР В КОРПУС"%nm,S,wt,wr,at,ar)
        table("### %s — КРИТ В КОРПУС"%nm,S,wt,wr,at,ar,crit=True)
        table("### %s — КРИТ В ГОЛОВУ"%nm,S,wt,wr,at,ar,crit=True,head=True)
