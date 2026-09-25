# -*- coding: utf-8 -*-
import io,json,math
import six as M

P=json.load(io.open("six.json",encoding="utf-8"))
k,penb,pens,alpha,s,hs,hb,hst,gap,crit=P[:10]
DMGF=dict(zip(M.FAMS,P[10:]))

ARM=[None,"Cloth","PaddingArmor","LightLeatherArmor","HardLeaterArmor","SplintArmor",
     "ChainArmor","ScaleMail","HalfPlate","PlateArmor"]
SH={None:"bare","Cloth":"cloth","PaddingArmor":"padded","LightLeatherArmor":"leather",
    "HardLeaterArmor":"hardlth","SplintArmor":"splint","ChainArmor":"mail",
    "ScaleMail":"scale","HalfPlate":"halfplt","PlateArmor":"plate"}
SKIP={"Quiver","CrossbowQuiver","Lute","Wand","Buckler","HeaterShield","KiteShield",
      "RoundShield","TowerShield"}

def constants():
    print("="*96)
    print("FORMULAS -- six inputs and nothing else")
    print("="*96)
    print("  weight       = as the game has it, untouched")
    print("  weapon damage= game damage x family lever:")
    for f in M.FAMS: print("                   %-8s x %.2f"%(f,DMGF[f]))
    print("  penetration  = strength x weight x type      sharp 1.00  blunt %.2f  stab %.2f"%(penb,pens))
    print("  resistance   = armour durability ^ %.3f / %.0f"%(alpha,k))
    print("  gets through = max(0, 1 - resistance / penetration)      -- 0 means armour ate it whole")
    print("  bare flesh   = no resistance at all: the blow lands in full")
    print("  damage       = weapon damage x (1 + strength x weight x %.4f) x (1 + mastery/100)"%s)
    print("  each type in a mixed weapon is judged on its own; a mace is half and half")
    print("  head worth   = sharp %.2f  blunt %.2f  stab %.2f   (chest 1.00, belt 0.80, legs 0.50)"%(hs,hb,hst))
    print("  a thrust to the head finds the gap: armour there counts x%.2f"%gap)
    print("  a thrust to an unarmoured chest finds the heart: %.0f%% outright kill per blow"%(crit*100))
    print()
    print("  health 180 (all stats 10, level 6). Head pool 27, chest 90, legs 63.")
    print("  Death is emptying the HEAD or the CHEST. Legs and arms never kill:")
    print("  they cripple and they bleed.")
    print()

def weights():
    print("="*96)
    print("DAMAGE AFTER BALANCING  (T1; weight left as the game has it)")
    print("="*96)
    print("  %-20s %-8s %5s %8s %8s"%("weapon","family","kg","was","now"))
    for c in sorted(set(w["cls"] for w in M.W if w["q"]=="Common")):
        if c in SKIP: continue
        w=M.weap(c)
        if not w: continue
        print("  %-20s %-8s %5.1f %8.1f %8.1f"%(c,M.family(w),w["kg"],
              sum(M.split_raw(w)),sum(M.split(w,P))))
    print()

def targets():
    print("="*96)
    print("ASKED FOR vs CAME OUT   (blows to kill, strength 10, mastery 50, T1 gear)")
    print("="*96)
    rows=[("GreatSword",None,"head","1"),("GreatAxe",None,"head","1"),
          ("TwoHandHammer",None,"head","1"),("BattleAxe",None,"head","1"),
          ("Sword",None,"head","1-3"),("Mace",None,"head","2-3"),("Dagger",None,"head","4-5"),
          ("GreatSword","PlateArmor","head","2"),("GreatSword","ChainArmor","head","1"),
          ("GreatSword",None,"chest","5"),("Dagger",None,"chest","~7"),
          ("Spear",None,"chest","~5"),("Dagger","PlateArmor","head","~6")]
    for cls,ac,slot,aim in rows:
        b=M.blows(cls,ac,slot,P)
        print("  %-16s %-9s %-6s asked %-5s got %s"%(cls,SH.get(ac,ac),slot,aim,
              ("%.1f"%b) if b<100 else "never"))
    for st,aim in ((20,"1"),(5,"3")):
        b=M.blows("Sword",None,"head",P,strength=st)
        print("  Sword bare head, strength %-2d          asked %-5s got %.1f"%(st,aim,b))
    print()

def eats():
    print("="*96)
    print("HOW MUCH OF THE BLOW THE ARMOUR EATS  (chest, strength 10)")
    print("="*96)
    print("  %-18s"%"weapon"+"".join(SH[a].rjust(9) for a in ARM if a))
    for cls in ("Dagger","Rapier","Spear","Sword","BattleAxe","GreatSword","Mace",
                "TwoHandHammer","TwoHandMace","Poleaxe"):
        if not M.weap(cls): continue
        line="  %-18s"%cls
        for a in ARM:
            if a is None: continue
            line+=("%.0f%%"%(M.cut(cls,a,"chest",P)*100)).rjust(9)
        print(line)
    print()

def grid(slot):
    print("="*96)
    print("BLOWS TO KILL -- %s   (T1, strength 10, mastery 50)"%slot.upper())
    print("="*96)
    print("  "+"weapon".ljust(18)+"kg  "+"".join(SH[a].rjust(8) for a in ARM))
    for c in sorted(set(w["cls"] for w in M.W if w["q"]=="Common")):
        if c in SKIP: continue
        w=M.weap(c)
        if not w: continue
        line="  "+c.ljust(18)+("%.0f"%w["kg"]).ljust(4)
        for a in ARM:
            b=M.blows(c,a,slot,P)
            line+=(("%.1f"%b) if b<100 else "  -").rjust(8)
        print(line)
    print()

def kill(cls,acls,mastery=50,strength=10):
    """death is the head or the chest, whichever comes first"""
    out=[]
    for slot in ("head","chest"):
        b=M.blows(cls,acls,slot,P,strength=strength,mastery=mastery)
        if b is not None and b<1e6: out.append(b)
    return min(out) if out else None

def masterysweep():
    print("="*96)
    print("MASTERY 1 to 99  (blows to kill, best of head or chest, strength 10)")
    print("="*96)
    steps=[1,8,15,22,29,36,43,50,57,64,71,78,85,92,99]
    print("  %-16s %-9s"%("weapon","armour")+"".join(("%d"%m).rjust(6) for m in steps))
    for cls,ac in (("Sword",None),("Sword","ChainArmor"),("Sword","PlateArmor"),
                   ("GreatSword","PlateArmor"),("TwoHandHammer","PlateArmor"),
                   ("Dagger","LightLeatherArmor")):
        line="  %-16s %-9s"%(cls,SH.get(ac,ac))
        for m in steps:
            b=kill(cls,ac,mastery=m)
            line+=(("%.1f"%b) if b and b<100 else "  -").rjust(6)
        print(line)
    print()

def fights():
    print("="*96)
    print("1v1, 1v2, 1v3   (both sides same gear, T1, strength 10; enemy is an even hand at 50)")
    print("="*96)
    print("  %-16s %-9s %8s %10s %10s"%("weapon","armour","mastery","blows you","blows them"))
    for cls,ac in (("Sword","LightLeatherArmor"),("Sword","ChainArmor"),
                   ("GreatSword","PlateArmor"),("TwoHandHammer","PlateArmor")):
        for m in (1,50,99):
            mine=kill(cls,ac,mastery=m); theirs=kill(cls,ac,mastery=50)
            if not mine or not theirs: continue
            edge=theirs/mine
            says="1v%d"%int(edge) if edge>=1 else "loses 1v1"
            print("  %-16s %-9s %8d %10.1f %10.1f   holds %s"%(cls,SH.get(ac,ac),m,mine,theirs,says))
    print()

if __name__=="__main__":
    constants(); weights(); targets(); eats()
    grid("head"); grid("chest"); masterysweep(); fights()
