# -*- coding: utf-8 -*-
# Six inputs and nothing else.
#   strength x weight -> penetration AND damage
#   weapon damage     -> belongs to the weapon, the base
#   mastery           -> how well the weapon is used (the games own 1 + m/100)
#   armour durability -> stands against penetration
#   damage type       -> how it penetrates; a thrust does not defeat armour at all,
#                        it lives on the head and on crits
import io,json,math,random,statistics

G=json.load(io.open("gear.json",encoding="utf-8"))
W,A=G["W"],G["A"]
SHARP,BLUNT,STAB=0,1,2
KIND={"sharp":SHARP,"blunt":BLUNT,"stab":STAB}
NAME=["sharp","blunt","stab"]

HP=180.0
POOL={"head":HP*0.15,"chest":HP*0.50,"pants":HP*0.35,"belt":HP*0.50}
GAME_MASTERY=0.01
BASE="T1"

DC={}
def dmg_of(w):
    i=id(w)
    if i not in DC: DC[i]=sum((lo+hi)/2 for k,(lo,hi) in w["hits"].items() if k in KIND)
    return DC[i]


# One weapon, one kind of blow. The games own tables give a mace half a cutting edge
# and a rapier no point at all, which makes nonsense of a system built on damage type.
RETYPE={"Rapier":"stab","Trident":"stab",
        "Mace":"blunt","Club":"blunt","TwoHandMace":"blunt","TwoHandClub":"blunt",
        "TwoHandFlail":"blunt","Flail":"blunt",
        "Poleaxe":"blunt","Glaive":"sharp","Halberd":"sharp"}

SC={}
def split_raw(w):
    """damage per type. A mace is half crushing and half cutting in this games own
    numbers, and a single lead type would throw a coin over which it is."""
    i=id(w)
    if i in SC: return SC[i]
    out=[0.0,0.0,0.0]
    for k,(lo,hi) in w["hits"].items():
        if k in KIND: out[KIND[k]]+=(lo+hi)/2
    fix=DMGFIX.get(w["cls"])
    if fix: out=[v*fix for v in out]
    want=RETYPE.get(w["cls"])
    if want:                                   # keep the total, move it all to one kind
        tot=sum(out); out=[0.0,0.0,0.0]; out[KIND[want]]=tot
    SC[i]=out
    return out

def split(w,P=None):
    out=split_raw(w)
    if P is None: return out
    f=dmgf(w,P)
    return [v*f for v in out]

LC={}
def lead(w):
    i=id(w)
    if i in LC: return LC[i]
    best=-1.0; kind=SHARP
    for k,(lo,hi) in w["hits"].items():
        if k in KIND and (lo+hi)/2>best: best=(lo+hi)/2; kind=KIND[k]
    LC[i]=kind
    return kind

WC={}
def weap(cls,tier=None,q="Common"):
    if tier is None: tier=BASE
    key=(cls,tier,q)
    if key in WC: return WC[key]
    g=[w for w in W if w["cls"]==cls and w["tier"]==tier and w["q"]==q]
    if not g: g=[w for w in W if w["cls"]==cls and w["q"]==q]
    WC[key]=g[0] if g else None
    return WC[key]

DURC={}
USED={}
SIZE={"head":1.0,"chest":2.0,"pants":1.5,"belt":0.6}

# The game stores durability as class quality x the size of the piece: every class and
# tier divides out to the same number. So a plate helm is made of the same steel as the
# cuirass -- it is merely smaller. Quality is what stops a blow; size is only how much
# punishment the piece can take before it is ruined.
QUAL={}
def quality(acls,tier=None):
    """how good this armour is, regardless of which piece it is"""
    if acls is None: return 0.0
    if tier is None: tier=BASE
    key=(acls,tier)
    if key in QUAL: return QUAL[key]
    got=[]
    for a in A:
        if a["cls"]!=acls or a["dur"]<=0: continue
        if a["slot"] not in SIZE: continue
        if a["tier"]==tier: got.append(a["dur"]/SIZE[a["slot"]])
    if not got:
        low=None
        for a in A:
            if a["cls"]!=acls or a["dur"]<=0 or a["slot"] not in SIZE: continue
            if low is None or a["tier"]<low: low=a["tier"]
        got=[a["dur"]/SIZE[a["slot"]] for a in A
             if a["cls"]==acls and a["tier"]==low and a["dur"]>0 and a["slot"] in SIZE]
        USED[acls]=low
    else:
        USED[acls]=tier
    QUAL[key]=min(got) if got else 0.0
    return QUAL[key]

def pool(acls,slot,tier=None):
    """how much the piece can take before it is ruined -- wear, not protection"""
    return quality(acls,tier)*SIZE.get(slot,1.0)

def dur(acls,slot,tier=None):
    # what the gate is measured against: the class, not the size of the piece
    return quality(acls,tier)


FAMILY={}
def family(w):
    c=w["cls"]
    if c in FAMILY: return FAMILY[c]
    n=c.lower()
    if n=="poleaxe": f="blunt"          # a hammer on a shaft, not an axe
    elif "axe" in n or n in ("bardiche","halberd","glaive","hatchet"): f="axe"
    elif any(t in n for t in ("mace","hammer","club","flail","staff","stick","buckler","shield","wand","lute","poleaxe")): f="blunt"
    elif any(t in n for t in ("dagger","katar")): f="dagger"
    elif any(t in n for t in ("spear","trident","estoc","rapier","pike")): f="spear"
    elif any(t in n for t in ("bow","crossbow","quiver")): f="range"
    else: f="blade"
    FAMILY[c]=f
    return f

FAMS=["blade","axe","blunt","spear","dagger","range"]

# A few weights in the game are plainly wrong: a quarterstaff does not outweigh a mace,
# and a wand is not four kilos. These are corrected downwards, which is also what stops
# a stick from ringing a helmet.
KGFIX={"Staff":2.0,"Stick":2.0,"Wand":1.0,"Lute":2.0}

# A staff is for casting. It was doing as much per second as a greatsword, which is
# nonsense for a stick in a wizards hands: cut to a third.
DMGFIX={"Staff":1.0/3.0,"Stick":1.0/3.0,"Wand":1.0/5.0}
def kg(w,P):
    return KGFIX.get(w["cls"],w["kg"])
def dmgf(w,P):
    return P[10+FAMS.index(family(w))]  # damage is the lever instead

# ---- the model ----------------------------------------------------------
# Crushing beats harness and nothing else. Against mail or leather a hammer has no
# trick to play: the same bite as an edge, and half the damage to show for it.
HEAVY={"PlateArmor","HalfPlate","ScaleMail","SplintArmor","metalHelmet"}
def heavy(acls):
    return acls in HEAVY

def share(w,d,slot,P,strength,kind,acls=None):
    """what share of one types damage gets past the armour on that spot"""
    k,penb,pens,alpha,s,hs,hb,hst,gap,crit=P[:10]
    if not d: return 1.0                      # bare flesh takes the whole blow
    pen=[1.0,penb,pens][kind]
    if kind==BLUNT and not heavy(acls): pen=1.0      # no edge over an edge, save against plate
    punch=strength*kg(w,P)*pen
    resist=d**alpha/k
    if kind==STAB and slot=="head": resist*=gap
    return max(0.0,1.0-resist/max(1e-9,punch))

ACLS={}
def through(w,d,slot,P,strength):
    """the whole blow, each part of it judged by its own type"""
    parts=split(w,P); tot=sum(parts)
    if tot<=0: return 0.0
    return sum(parts[i]*share(w,d,slot,P,strength,i,ACLS.get("now")) for i in range(3))/tot

def hit(w,acls,slot,P,strength,mastery,tier=None):
    k,penb,pens,alpha,s,hs,hb,hst,gap,crit=P[:10]
    ACLS["now"]=acls
    d=dur(acls,slot,tier)
    parts=split(w,P); tot=sum(parts)
    if tot<=0: return 0.0
    got=0.0
    for i in range(3):
        if parts[i]<=0: continue
        worth=[hs,hb,hst][i] if slot=="head" else {"chest":1.0,"belt":0.8,"pants":0.5}[slot]
        got+=parts[i]*share(w,d,slot,P,strength,i,acls)*worth
    return got*(1.0+strength*kg(w,P)*s)*(1.0+mastery*GAME_MASTERY)

def blows(cls,acls,slot,P,strength=10,mastery=50,tier=None):
    w=weap(cls,tier)
    if not w: return None
    crit=P[9]
    n=POOL[slot]/max(0.001,hit(w,acls,slot,P,strength,mastery,tier))
    # A crit to the torso is a hit on the vital organs, and a thrust that finds the
    # heart ends it there. Harness covers the organs, so it cannot happen through plate.
    if slot=="chest" and split(w,P)[STAB]>0:
        p=crit*share(w,dur(acls,slot,tier),slot,P,strength,STAB,acls)*split(w,P)[STAB]/sum(split(w,P))
        if p>1e-6: n=1.0/(p+(1.0-p)/max(1e-6,n))
    return n

def cut(cls,acls,slot,P,strength=10,tier=None):
    w=weap(cls,tier)
    return 1.0-through(w,dur(acls,slot,tier),slot,P,strength)

# ---- what the design asks for -------------------------------------------
def score(P):
    bad=0.0
    def want(got,aim,weight=1.0):
        if got is None: return 0.0
        return weight*(math.log(max(0.2,got))-math.log(aim))**2

    # bare head, in the players own words
    bad+=want(blows("GreatSword",None,"head",P),1.0,3)
    bad+=want(blows("GreatAxe",None,"head",P),1.0,2)
    bad+=want(blows("TwoHandHammer",None,"head",P),1.0,2)
    bad+=want(blows("BattleAxe",None,"head",P),1.5,1)
    bad+=want(blows("Sword",None,"head",P),2.0,2)
    bad+=want(blows("Mace",None,"head",P),2.5,2)
    bad+=want(blows("Dagger",None,"head",P),4.5,2)
    # armoured head
    bad+=want(blows("GreatSword","PlateArmor","head",P),2.0,3)
    bad+=want(blows("GreatSword","ChainArmor","head",P),1.0,1)
    bad+=want(blows("GreatSword","LightLeatherArmor","head",P),1.0,1)
    # body
    bad+=want(blows("GreatSword",None,"chest",P),5.0,2)
    bad+=want(blows("Sword",None,"chest",P),9.0,1)
    # strength band on a sword to a bare head
    bad+=want(blows("Sword",None,"head",P,strength=20),1.0,1)
    bad+=want(blows("Sword",None,"head",P,strength=5),3.0,1)
    # what armour eats off a one-hand sword to the body
    bad+=want(cut("Sword","LightLeatherArmor","chest",P),0.15,2)
    bad+=want(cut("Sword","ChainArmor","chest",P),0.40,4)
    bad+=want(cut("Sword","PlateArmor","chest",P),0.95,3)
    # a thrust does not defeat armour: it is stopped near dead
    bad+=want(cut("Spear","ChainArmor","chest",P),0.88,3)
    bad+=want(cut("Spear","PlateArmor","chest",P),0.97,3)
    # but a thrust still finds the face
    bad+=want(blows("Dagger","PlateArmor","head",P),6.5,2)
    bad+=want(blows("Dagger",None,"chest",P),7.0,2)
    bad+=want(blows("Spear",None,"chest",P),5.0,2)
    # blunt is what you bring against harness
    bad+=want(cut("TwoHandHammer","PlateArmor","chest",P),0.45,3)
    return bad

# ---- search -------------------------------------------------------------
#      k    penb  pens  alpha   s      hs   hb   hst  gap  flesh
#      k     penb  pens  alpha   s      hs    hb    hst   gap   crit
LO=[1e-2, 1.3, 0.01, 0.50, 0.005, 0.2, 0.1, 0.2, 0.02, 0.02]+[1.0,1.00,1.083,1.10,0.45,0.60]
HI=[1e+7, 4.0, 0.80, 2.50, 0.080,10.0, 8.0,12.0, 1.00, 0.35]+[1.0,2.20,1.083,2.00,1.60,1.60]
LOG=[True,True,True,False,False,True,True,True,True,True]+[True]*6

def clamp(P): return [min(HI[i],max(LO[i],P[i])) for i in range(len(P))]

def anneal(seed):
    random.seed(seed)
    P=[]
    for i in range(len(LO)):
        if LOG[i]: P.append(math.exp(random.uniform(math.log(LO[i]),math.log(HI[i]))))
        else: P.append(random.uniform(LO[i],HI[i]))
    P=clamp(P); best=score(P); step=0.6
    for it in range(30000):
        Q=list(P); i=random.randrange(len(Q))
        if LOG[i]: Q[i]*=math.exp(random.gauss(0,step))
        else: Q[i]+=random.gauss(0,step*0.05)
        Q=clamp(Q); s=score(Q)
        if s<best: best,P=s,Q
        if it%2500==2499: step*=0.72
    return best,P

if __name__=="__main__":
    best=None
    for s in range(36):
        b,P=anneal(s)
        if best is None or b<best[0]: best=(b,P)
    b,P=best
    print("cost %.4f"%b)
    for n,v in zip(["k","pen_blunt","pen_stab","alpha","str_dmg","head_sharp","head_blunt","head_stab","stab_gap","stab_crit"]+["dmg_"+f for f in FAMS],P):
        print("  %-11s %.5f"%(n,v))
    io.open("six.json","w",encoding="utf-8").write(json.dumps(P))
