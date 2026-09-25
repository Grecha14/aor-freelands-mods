# -*- coding: utf-8 -*-
# Critical damage by kind of blow and by hands, as asked.
#   crit chance   = Precision (the games own CriMD += Precision)
#   crit multiple = 1.5 + Intelligence/100 in the game; here set per type and hands
import io,json
import six as M
P=json.load(io.open("six.json",encoding="utf-8"))

#            one-hand  two-hand
BONUS={M.SHARP:(1.00,1.50),
       M.BLUNT:(0.50,1.00),
       M.STAB :(3.00,1.50)}
TWO={"twohand","polearms"}

def hands(w): return 1 if w["wt"] in TWO else 0

def crit_bonus(cls):
    w=M.weap(cls)
    if not w: return 0.0
    sp=M.split(w,P); tot=sum(sp)
    if tot<=0: return 0.0
    h=hands(w)
    return sum(sp[i]/tot*BONUS[i][h] for i in range(3))

def with_crit(cls,acls,precision,slot="chest",strength=10,mastery=50):
    w=M.weap(cls)
    if not w: return 0.0
    base=M.hit(w,acls,slot,P,strength,mastery)*w["sp"]
    return base*(1.0+precision/100.0*crit_bonus(cls))

SHOW=["Dagger","Katar","Rapier","Spear","HeavySpear","TwoHandSpear","Estoc",
      "ShortSword","Sword","Sabre","LongSword","GreatSword","Katana",
      "BattleAxe","GreatAxe","Halberd","Mace","TwoHandHammer","Poleaxe"]

print("CRIT BONUS PER WEAPON  (sharp 1H +100%% 2H +150%%, stab 1H +300%% 2H +150%%, blunt 1H +50%% 2H +100%%)")
print("  %-16s %-9s %-6s %10s"%("weapon","hands","type","crit bonus"))
for c in SHOW:
    w=M.weap(c)
    if not w: continue
    sp=M.split(w,P); kind=["sharp","blunt","stab"][sp.index(max(sp))]
    print("  %-16s %-9s %-6s %9.0f%%"%(c,w["wt"],kind,100*crit_bonus(c)))
print()
print("DAMAGE PER SECOND vs BARE, as Precision rises")
print("  %-16s"%"weapon"+"".join(("Pre %d"%p).rjust(9) for p in (0,10,20,30,40)))
for c in SHOW:
    line="  %-16s"%c
    for p in (0,10,20,30,40):
        line+=("%.1f"%with_crit(c,None,p)).rjust(9)
    print(line)
print()
print("HOW MUCH A CRIT BUILD IS WORTH (Precision 40 vs 0)")
print("  %-16s %10s %10s %8s"%("weapon","Pre 0","Pre 40","gain"))
rows=[]
for c in SHOW:
    a=with_crit(c,None,0); b=with_crit(c,None,40)
    if a<=0: continue
    rows.append((b/a,c,a,b))
for g,c,a,b in sorted(rows,reverse=True):
    print("  %-16s %10.1f %10.1f %7.0f%%"%(c,a,b,100*(g-1)))
