# -*- coding: utf-8 -*-
# What a crushing blow leaves behind. The thrust has the heart; the hammer has this.
#   force = strength x weight x the crushing share of the blow
#   head  -> knocked senseless      body -> broken ribs      legs -> crippled
# Armour softens it but cannot stop it: that is the whole point of a hammer.
import io,json,math
import six as M

P=json.load(io.open("six.json",encoding="utf-8"))
C=120.0          # the force at which a blow is even money to tell
SOAK=0.25        # the least that reaches the bone through armour a blow can shift
THRU=0.9         # how much force it takes to ring a man through his own harness
BODY=0.70        # ribs are harder to break than a skull is to ring
LEGS=0.85

def force(cls,strength=10):
    w=M.weap(cls)
    if not w: return 0.0,None
    parts=M.split(w,P); tot=sum(parts)
    if tot<=0: return 0.0,w
    return strength*M.kg(w,P)*(parts[M.BLUNT]/tot),w

def chance(cls,acls,slot,strength=10):
    f,w=force(cls,strength)
    if f<=0 or w is None: return 0.0
    # Shaking a man through his armour is a matter of sheer force against that armour.
    # A quarterstaff cannot do it to plate; a maul can do it to anything.
    q=M.dur(acls,slot)
    carry=1.0 if q<=0 else f/(f+q*THRU)
    got=M.share(w,q,slot,P,strength,M.BLUNT,acls)
    soft=max(SOAK*carry,carry)
    scale={"head":1.0,"chest":BODY,"pants":LEGS}[slot]
    return f/(f+C/scale)*soft

print("CRUSHING BLOWS -- chance the blow leaves its mark, strength 10")
print("  formula: force / (force + %.0f), force = strength x weight x crushing share"%C)
print("  armour softens to %.0f%% at worst; it never stops it outright"%(SOAK*100))
print()
ARM=[None,"Cloth","LightLeatherArmor","ChainArmor","SplintArmor","PlateArmor"]
SH={None:"bare","Cloth":"cloth","LightLeatherArmor":"leather","ChainArmor":"mail",
    "SplintArmor":"splint","PlateArmor":"plate"}
blunts=[c for c in sorted(set(w["cls"] for w in M.W if w["q"]=="Common"))
        if M.weap(c) and sum(M.split(M.weap(c),P))>0
        and M.split(M.weap(c),P)[M.BLUNT]/sum(M.split(M.weap(c),P))>0.5]
for slot,name in (("head","KNOCKED OUT (head)"),("chest","BROKEN RIBS (body)"),("pants","CRIPPLED (legs)")):
    print(name)
    print("    %-18s %4s"%("weapon","kg")+"".join(SH[a].rjust(9) for a in ARM))
    for c in blunts:
        w=M.weap(c)
        line="    %-18s %4.0f"%(c,w["kg"])
        for a in ARM:
            line+=("%.0f%%"%(100*chance(c,a,slot))).rjust(9)
        print(line)
    print()
print("A CUTTING WEAPON FOR COMPARISON (no crushing share at all)")
for c in ("Sword","GreatSword","BattleAxe","Dagger"):
    print("    %-18s head %.0f%%"%(c,100*chance(c,None,"head")))
print()
print("AND BY STRENGTH -- two-hand hammer to a plate helm")
print("    "+"".join(("str %d"%s).rjust(9) for s in (5,10,15,20,25)))
print("    "+"".join(("%.0f%%"%(100*chance("TwoHandHammer","PlateArmor","head",s))).rjust(9) for s in (5,10,15,20,25)))
