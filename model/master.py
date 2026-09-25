# -*- coding: utf-8 -*-
# Everything at once: damage, the armour gate, crit, speed and bleeding.
# All stats at 10, so crit chance is 10; mastery 50; tier T1.
import io,json,statistics
import six as M
P=json.load(io.open("six.json",encoding="utf-8"))

BONUS={M.SHARP:(1.00,1.50),M.BLUNT:(0.50,1.00),M.STAB:(3.00,1.50)}
TWO={"twohand","polearms"}
RATE=0.09; DUR=8.0; MAXST=5
PRE=10; STR=10; MAST=50

ARM=[None,"Cloth","PaddingArmor","LightLeatherArmor","HardLeaterArmor",
     "ChainArmor","SplintArmor","ScaleMail","HalfPlate","PlateArmor"]
SH={None:"bare","Cloth":"cloth","PaddingArmor":"padded","LightLeatherArmor":"leathr",
    "HardLeaterArmor":"hardlt","ChainArmor":"mail","SplintArmor":"splint",
    "ScaleMail":"scale","HalfPlate":"halfpl","PlateArmor":"plate"}
SKIP={"Quiver","CrossbowQuiver","Lute","Wand","Buckler","HeaterShield","KiteShield",
      "RoundShield","TowerShield","Crossbow","HeavyCrossbow","Longbow","Shortbow"}

def critb(w):
    sp=M.split(w,P); tot=sum(sp)
    if tot<=0: return 0.0
    h=1 if w["wt"] in TWO else 0
    return sum(sp[i]/tot*BONUS[i][h] for i in range(3))

def dps(cls,acls,slot="chest"):
    w=M.weap(cls)
    if not w: return 0.0
    hit=M.hit(w,acls,slot,P,STR,MAST)
    hit*=1.0+PRE/100.0*critb(w)
    direct=hit*w["sp"]
    sp=M.split(w,P); tot=sum(sp)
    cut=0.0
    if tot>0:
        for i in (M.SHARP,M.STAB):
            if sp[i]<=0: continue
            cut+=sp[i]/tot*M.share(w,M.dur(acls,slot),slot,P,STR,i,acls)
    bleed=min(MAXST,w["sp"]*DUR)*hit*cut*RATE if cut>0 else 0.0
    return direct+bleed

cl=[c for c in sorted(set(w["cls"] for w in M.W if w["q"]=="Common"))
    if c not in SKIP and M.weap(c)]

print("DAMAGE PER SECOND -- body, T1, all stats 10 (crit 10%), mastery 50")
print("  with crit, attack speed and bleeding all counted")
print()
print("  %-17s %4s %5s %-6s"%("weapon","kg","speed","type")+"".join(SH[a].rjust(8) for a in ARM))
rows=[]
for c in cl:
    w=M.weap(c); sp=M.split(w,P)
    kind=["sharp","blunt","stab"][sp.index(max(sp))]
    vals=[dps(c,a) for a in ARM]
    rows.append((c,w,kind,vals))
    print("  %-17s %4.1f %5.2f %-6s"%(c,M.kg(w,P),w["sp"],kind)+"".join(("%.1f"%v).rjust(8) for v in vals))

print()
print("WHO WINS WHERE")
for i,a in enumerate(ARM):
    best=sorted(rows,key=lambda r:-r[3][i])[:3]
    worst=sorted(rows,key=lambda r:r[3][i])[:2]
    print("  %-8s best: %-38s worst: %s"%(SH[a],
        ", ".join("%s %.0f"%(b[0],b[3][i]) for b in best),
        ", ".join("%s %.0f"%(b[0],b[3][i]) for b in worst)))

print()
print("HOW FLAT IS IT  (spread of dps across all weapons, per armour)")
print("  %-8s %7s %7s %7s %7s"%("armour","low","high","median","high/low"))
for i,a in enumerate(ARM):
    v=[r[3][i] for r in rows if r[3][i]>0.05]
    if not v: continue
    print("  %-8s %7.1f %7.1f %7.1f %7.1f"%(SH[a],min(v),max(v),statistics.median(v),max(v)/min(v)))
