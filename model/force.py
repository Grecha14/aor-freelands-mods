# -*- coding: utf-8 -*-
# Пробитие на собственных числах игры: у оружия есть "сила" и раздел между
# Силой и Ловкостью. Ничего не выдумываем.
import io,json,math
import six as M

F=json.load(io.open("force.json",encoding="utf-8"))
KGG=1.20; GR=1.20
BLUNT={"TwoHandHammer","TwoHandMace","TwoHandClub","TwoHandFlail","Poleaxe",
       "Mace","Hammer","Club","Flail"}
IGN=0.30

def punch(c,wt,wr,S,A):
    """пробитие = сила оружия x (Сила x доля + Ловкость x доля) / 10"""
    d=F[c]
    return d["force"]*(S*d["str"]+A*d["agi"])/10.0*GR**wr*KGG**wt

def resist(a,at,ar,c,V,GV):
    v=V[a]*GV**(at+ar)
    return v*(1-IGN) if c in BLUNT else v

def frac(c,wt,wr,a,at,ar,S,A,V,GV):
    p=punch(c,wt,wr,S,A); v=resist(a,at,ar,c,V,GV)
    return max(0.0,(p-v)/p) if p>0 else 0.0

if __name__=="__main__":
    print("ПРОБИТИЕ ПРИ ВСЕХ СТАТАХ 10 = сила оружия, один в один")
    for c in ["Dagger","Rapier","Sword","GreatSword","BattleAxe","Mace","TwoHandHammer","TwoHandMace"]:
        d=F[c]
        print("  %-18s сила %3d  раздел %.1f/%.1f  ->  пробитие %.0f"%(
            c,d["force"],d["str"],d["agi"],punch(c,0,0,10,10)))
    print()
    print("ЧТО ДАЮТ СТАТЫ (пробитие, Т1 Common)")
    print("  %-18s"%"оружие"+"".join(("С%d/Л%d"%(s,a)).rjust(11)
          for s,a in [(10,10),(30,10),(10,30),(30,30),(50,10),(10,50)]))
    for c in ["Dagger","Rapier","Sword","GreatSword","Mace","TwoHandHammer"]:
        line="  %-18s"%c
        for s,a in [(10,10),(30,10),(10,30),(30,30),(50,10),(10,50)]:
            line+=("%.0f"%punch(c,0,0,s,a)).rjust(11)
        print(line)
