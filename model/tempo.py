# -*- coding: utf-8 -*-
# Damage is only half of it. A blow that lands twice as hard half as often is the
# same blow. What separates weapons is what they hit hard AGAINST.
import io,json
import six as M
P=json.load(io.open("six.json",encoding="utf-8"))

ARM=[None,"Cloth","LightLeatherArmor","ChainArmor","SplintArmor","HalfPlate","PlateArmor"]
SH={None:"bare","Cloth":"cloth","LightLeatherArmor":"leather","ChainArmor":"mail",
    "SplintArmor":"splint","HalfPlate":"halfplt","PlateArmor":"plate"}

def dps(cls,acls,slot="chest",strength=10,mastery=50):
    w=M.weap(cls)
    if not w: return 0.0
    return M.hit(w,acls,slot,P,strength,mastery)*w["sp"]

SHOW=["Dagger","Rapier","ShortSword","Sword","Sabre","LongSword","BastardSword",
      "GreatSword","Katana","BattleAxe","GreatAxe","Bardiche","Halberd",
      "Spear","HeavySpear","TwoHandSpear","Mace","Hammer","Flail",
      "Poleaxe","TwoHandHammer","TwoHandMace","Staff"]

print("DAMAGE PER SECOND -- body, strength 10, mastery 50, T1")
print("  (weapon damage x armour x zone, times the games own attack speed)")
print()
print("  %-16s %4s %5s %-6s"%("weapon","kg","speed","type")+"".join(SH[a].rjust(9) for a in ARM))
rows=[]
for c in SHOW:
    w=M.weap(c)
    if not w: continue
    sp=M.split(w,P); kind=["sharp","blunt","stab"][sp.index(max(sp))]
    line="  %-16s %4.1f %5.2f %-6s"%(c,M.kg(w,P),w["sp"],kind)
    vals=[]
    for a in ARM:
        v=dps(c,a); vals.append(v)
        line+=("%.1f"%v).rjust(9)
    rows.append((c,vals))
    print(line)
print()
print("  spread across armour -- how much a weapon cares what it faces")
print("  %-16s %10s %10s %10s"%("weapon","vs bare","vs plate","plate/bare"))
for c,vals in rows:
    if vals[0]<=0: continue
    print("  %-16s %10.1f %10.1f %9.0f%%"%(c,vals[0],vals[-1],100*vals[-1]/vals[0]))
