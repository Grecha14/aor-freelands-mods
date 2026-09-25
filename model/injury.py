# -*- coding: utf-8 -*-
# What a blow leaves behind, by the kind of blow it was.
# The game already has every effect: SkullFracture, RibFracture, Arm/LegFracture,
# Knockout, Knockdown, Stunned, and a whole bufftype for bleeding. Nothing is invented
# here -- damage types are only wired to what exists.
#
#   crushing  carries force through armour: it breaks bones and rings skulls even
#             when the plate holds, because the plate does not hold the shock
#   cutting   only harms what it reaches: through armour it bleeds and maims,
#             stopped by armour it does nothing at all
#   thrusting the same, and it is the one that finds the heart
import io,json
import six as M

P=json.load(io.open("six.json",encoding="utf-8"))
C=120.0     # force at which a crushing blow is even money
THRU=0.9    # how much force it takes to carry shock through armour
SOAK=0.25   # least that reaches the bone once the shock does carry
CUT=0.55    # a cut that lands, how readily it maims
BLEED=0.85  # a cut or thrust that lands, how readily it bleeds

ZONE={"head":1.0,"chest":0.70,"pants":0.85}
HURT={"head":"SkullFracture","chest":"RibFracture","pants":"LegFracture"}

def parts(cls):
    w=M.weap(cls)
    if not w: return None,None,0.0
    sp=M.split(w,P); tot=sum(sp)
    return w,sp,tot

def crushed(cls,acls,slot,strength=10):
    """fracture and knockout: force against the armour, not through the gate"""
    w,sp,tot=parts(cls)
    if not w or tot<=0 or sp[M.BLUNT]<=0: return 0.0
    f=strength*M.kg(w,P)*(sp[M.BLUNT]/tot)
    q=M.dur(acls,slot)
    carry=1.0 if q<=0 else max(SOAK*f/(f+q*THRU),f/(f+q*THRU))
    return f/(f+C/ZONE[slot])*carry

def cut_maim(cls,acls,slot,strength=10,kind=None):
    """a cut or a thrust maims only what it reaches"""
    w,sp,tot=parts(cls)
    if not w or tot<=0: return 0.0
    share=(sp[M.SHARP]+sp[M.STAB])/tot if kind is None else sp[kind]/tot
    if share<=0: return 0.0
    got=0.0
    for i in (M.SHARP,M.STAB):
        if sp[i]<=0: continue
        got+=sp[i]/tot*M.share(w,M.dur(acls,slot),slot,P,strength,i,acls)
    return got*CUT*ZONE[slot]

def bleeds(cls,acls,slot,strength=10):
    w,sp,tot=parts(cls)
    if not w or tot<=0: return 0.0
    got=0.0
    for i in (M.SHARP,M.STAB):
        if sp[i]<=0: continue
        got+=sp[i]/tot*M.share(w,M.dur(acls,slot),slot,P,strength,i,acls)
    return got*BLEED

ARM=[None,"Cloth","LightLeatherArmor","ChainArmor","SplintArmor","PlateArmor"]
SH={None:"bare","Cloth":"cloth","LightLeatherArmor":"leather","ChainArmor":"mail",
    "SplintArmor":"splint","PlateArmor":"plate"}
SHOW=["Dagger","Rapier","Spear","Sword","GreatSword","BattleAxe","GreatAxe",
      "Mace","Hammer","Staff","Stick","Poleaxe","TwoHandHammer","TwoHandMace"]

def table(name,fn,slot):
    print("%s -- %s, strength 10"%(name,slot))
    print("    %-16s %-6s %4s"%("weapon","type","kg")+"".join(SH[a].rjust(9) for a in ARM))
    for c in SHOW:
        w,sp,tot=parts(c)
        if not w or tot<=0: continue
        kind=["sharp","blunt","stab"][sp.index(max(sp))]
        line="    %-16s %-6s %4.1f"%(c,kind,M.kg(w,P))
        for a in ARM:
            line+=("%.0f%%"%(100*fn(c,a,slot))).rjust(9)
        print(line)
    print()

print("INJURIES -- everything below already exists in the game as a buff")
print("  crushing -> SkullFracture / RibFracture / LegFracture + Knockout, Stunned")
print("  cutting  -> bleeding + maiming, ONLY where it gets through armour")
print("  thrust   -> bleeding + the heart, ONLY where it gets through armour")
print()
table("FRACTURE OR KNOCKOUT (crushing only)",crushed,"head")
table("FRACTURE (crushing only)",crushed,"chest")
table("MAIMED BY AN EDGE OR A POINT",cut_maim,"pants")
table("BLEEDING",bleeds,"chest")
