# -*- coding: utf-8 -*-
# Bleeding, which the game already stacks: dotDamage x stackNum, one timer per stack.
# An edge and a point do not need to out-hit a hammer. They need to open a man up and
# let him empty out, which is what they were always for.
import io,json
import six as M
P=json.load(io.open("six.json",encoding="utf-8"))

RATE=0.09     # of the damage that got through, bled per second per stack
DUR=8.0       # how long one stack runs
MAXST=5       # the cap, maxStackNum on the buff

ARM=[None,"Cloth","LightLeatherArmor","ChainArmor","SplintArmor","HalfPlate","PlateArmor"]
SH={None:"bare","Cloth":"cloth","LightLeatherArmor":"leather","ChainArmor":"mail",
    "SplintArmor":"splint","HalfPlate":"halfplt","PlateArmor":"plate"}

def landed(cls,acls,slot="chest",strength=10,mastery=50):
    w=M.weap(cls)
    if not w: return None,0.0,0.0
    hit=M.hit(w,acls,slot,P,strength,mastery)
    sp=M.split(w,P); tot=sum(sp)
    if tot<=0: return w,hit,0.0
    cut=0.0
    for i in (M.SHARP,M.STAB):
        if sp[i]<=0: continue
        cut+=sp[i]/tot*M.share(w,M.dur(acls,slot),slot,P,strength,i,acls)
    return w,hit,cut

def both(cls,acls,strength=10,mastery=50):
    w,hit,cut=landed(cls,acls,strength=strength,mastery=mastery)
    if not w: return 0.0,0.0
    direct=hit*w["sp"]
    if cut<=0: return direct,0.0
    stacks=min(MAXST,w["sp"]*DUR)          # how many run at once at this rate of blows
    per=hit*cut*RATE                        # one stack, per second
    return direct,stacks*per

SHOW=["Dagger","Katar","Rapier","ShortSword","Sword","Sabre","LongSword","GreatSword",
      "Katana","BattleAxe","GreatAxe","Halberd","Spear","HeavySpear","TwoHandSpear",
      "Mace","TwoHandHammer","Poleaxe"]

print("BLEEDING STACKS -- %.0f%% of what got through, per second, per stack; %ds each, %d at most"%(RATE*100,DUR,MAXST))
print()
print("  %-16s %-6s %5s"%("weapon","type","speed")+"".join(SH[a].rjust(13) for a in ARM))
print("  %-16s %-6s %5s"%("","","")+"".join("blow+bleed".rjust(13) for a in ARM))
for c in SHOW:
    w=M.weap(c)
    if not w: continue
    sp=M.split(w,P); kind=["sharp","blunt","stab"][sp.index(max(sp))]
    line="  %-16s %-6s %5.2f"%(c,kind,w["sp"])
    for a in ARM:
        d,b=both(c,a)
        line+=("%.1f+%.1f"%(d,b)).rjust(13)
    print(line)
print()
print("TOTAL, and what it was without bleeding")
print("  %-16s %10s %10s %10s %10s"%("weapon","bare now","bare was","plate now","plate was"))
for c in SHOW:
    d0,b0=both(c,None); d1,b1=both(c,"PlateArmor")
    if d0<=0: continue
    print("  %-16s %10.1f %10.1f %10.1f %10.1f"%(c,d0+b0,d0,d1+b1,d1))
