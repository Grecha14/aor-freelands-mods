# -*- coding: utf-8 -*-
# Armour as an ablative pool. A blow that cannot pierce still hammers the plate:
# whatever damage exceeds the reduction is taken out of the armour itself, and once
# the armour is worn thin enough the same blow goes through.
import io,json
import six as M
P=json.load(io.open("six.json",encoding="utf-8"))

PEN=3.6          # penetration = strength x weight x type x PEN
WEAR=1.0         # how fast excess damage eats durability (to be calibrated)
SIZE={"head":1.0,"chest":2.0,"pants":1.5}

# suit weights the player gave, and the class ladder of quality and reduction
QUAL={"ткань":28,"лёгкая кожа":150,"твёрдая кожа":180,"чешуя":375,
      "кольчуга":270,"полулаты":330,"латы":450,"латы Т5":2250}
RED ={"ткань":3.0,"лёгкая кожа":5.0,"твёрдая кожа":7.0,"чешуя":9.0,
      "кольчуга":7.0,"полулаты":7.0,"латы":10.0,"латы Т5":30.0}

def punch(w,strength):
    sp=M.split(w,P); tot=sum(sp)
    if tot<=0: return 0.0
    coef=sum(sp[i]/tot*[1.0,P[1],P[2]][i] for i in range(3))
    return strength*M.kg(w,P)*coef*PEN

def dmg(w,strength,mastery=50):
    return sum(M.split(w,P))*(1.0+strength*M.kg(w,P)*P[4])*(1.0+mastery*0.01)

def fight(cls,armour,strength=10,slot="chest",wear=WEAR,cap=400):
    w=M.weap(cls)
    if not w: return None
    q0=QUAL[armour]; size=SIZE[slot]
    dur=q0*size                      # the pool on this piece
    red=RED[armour]*size
    p=punch(w,strength); d=dmg(w,strength)
    blows=0; broke=None
    while blows<cap:
        blows+=1
        q=dur/size                   # protection falls with the pool
        if p>=q:
            if broke is None: broke=blows
            return broke,blows,d     # through: full damage from here on
        excess=max(0.0,d-red)
        if excess<=0: return None,cap,0.0   # cannot even scratch it
        dur-=excess*wear
        if dur<0: dur=0
    return None,cap,0.0

SHOW=["Dagger","Rapier","Spear","Sword","BattleAxe","GreatSword","GreatAxe",
      "Mace","TwoHandHammer","TwoHandMace","Poleaxe"]
ARMS=["ткань","лёгкая кожа","твёрдая кожа","кольчуга","чешуя","полулаты","латы"]

for wear in (1.0,5.0,10.0,20.0):
    print("WEAR = %.0f  (blows to wear the armour through, chest, strength 10)"%wear)
    print("  %-16s"%"weapon"+"".join(a.rjust(14) for a in ARMS))
    for c in SHOW:
        line="  %-16s"%c
        for a in ARMS:
            r=fight(c,a,10,wear=wear)
            line+=(("%d"%r[0]) if r and r[0] else "-").rjust(14)
        print(line)
    print()
