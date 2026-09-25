# -*- coding: utf-8 -*-
# The thrust that finds the heart. Agility drives the point in, precision finds the gap.
import io,json,math,random
import six as M

P=json.load(io.open("six.json",encoding="utf-8"))

def chance(base,ca,cp,agility,precision,armour_share):
    # armour over the organs means there is no heart to find
    return base*(1.0+agility*ca+precision*cp)*armour_share

def blows(cls,acls,base,ca,cp,agility=10,precision=10,strength=10,mastery=50):
    w=M.weap(cls)
    n=M.POOL["chest"]/max(0.001,M.hit(w,acls,"chest",P,strength,mastery))
    parts=M.split(w,P); tot=sum(parts)
    if parts[M.STAB]<=0: return n
    sh=M.share(w,M.dur(acls,"chest"),"chest",P,strength,M.STAB)*parts[M.STAB]/tot
    p=chance(base,ca,cp,agility,precision,sh)
    if p<=1e-9: return n
    return 1.0/(p+(1.0-p)/max(1e-6,n))

def cost(x):
    base,ca,cp=x
    bad=0.0
    def want(got,aim,w=1.0): return w*(math.log(max(0.2,got))-math.log(aim))**2
    # an even hand, ten and ten
    # the knife alone takes seven; the heart is what makes a quick hand deadly
    bad+=want(blows("Dagger",None,base,ca,cp,1,1),6.8,2)
    bad+=want(blows("Dagger",None,base,ca,cp,10,10),5.6,3)
    bad+=want(blows("Dagger",None,base,ca,cp,20,20),4.2,3)
    bad+=want(blows("Spear",None,base,ca,cp,10,10),4.2,2)
    # agility and precision should matter about equally
    bad+=2.0*(math.log(max(1e-6,ca))-math.log(max(1e-6,cp)))**2*0.25
    return bad

best=None
random.seed(7)
for seed in range(400):
    x=[math.exp(random.uniform(math.log(0.005),math.log(0.25))),
       math.exp(random.uniform(math.log(0.005),math.log(0.30))),
       math.exp(random.uniform(math.log(0.005),math.log(0.30)))]
    c=cost(x); step=0.5
    for it in range(4000):
        y=list(x); i=random.randrange(3); y[i]*=math.exp(random.gauss(0,step))
        y=[min(0.4,max(0.002,v)) for v in y]
        d=cost(y)
        if d<c: c,x=d,y
        if it%800==799: step*=0.7
    if best is None or c<best[0]: best=(c,x)

c,(base,ca,cp)=best
print("cost %.4f"%c)
print("  heart_base %.4f   per agility %.4f   per precision %.4f"%(base,ca,cp))
print()
print("chance of an outright kill per thrust, unarmoured chest:")
print("  %-10s"%"agi/pre"+"".join(("%d"%v).rjust(8) for v in (1,5,10,15,20,25)))
for cls in ("Dagger","Katar","Spear","Rapier","Estoc","TwoHandSpear","HeavyCrossbow"):
    w=M.weap(cls)
    if not w or M.split(w,P)[M.STAB]<=0: continue
    line="  %-10s"%cls
    for v in (1,5,10,15,20,25):
        parts=M.split(w,P); sh=parts[M.STAB]/sum(parts)
        line+=("%.1f%%"%(100*chance(base,ca,cp,v,v,sh))).rjust(8)
    print(line)
print()
print("blows to kill an unarmoured man, chest, with the heart counted:")
print("  %-10s"%"agi/pre"+"".join(("%d"%v).rjust(8) for v in (1,5,10,15,20,25)))
for cls in ("Dagger","Katar","Spear","Rapier","Estoc","TwoHandSpear"):
    if not M.weap(cls): continue
    line="  %-10s"%cls
    for v in (1,5,10,15,20,25):
        line+=("%.1f"%blows(cls,None,base,ca,cp,v,v)).rjust(8)
    print(line)
print()
print("and through armour -- the organs are covered, so it stops:")
print("  %-10s"%"Dagger"+"".join(a.rjust(11) for a in ("bare","cloth","leather","mail","plate")))
line="  %-10s"%"chance"
for a in (None,"Cloth","LightLeatherArmor","ChainArmor","PlateArmor"):
    w=M.weap("Dagger"); parts=M.split(w,P)
    sh=M.share(w,M.dur(a,"chest"),"chest",P,10,M.STAB)*parts[M.STAB]/sum(parts)
    line+=("%.1f%%"%(100*chance(base,ca,cp,10,10,sh))).rjust(11)
print(line)
io.open("heart.json","w",encoding="utf-8").write(json.dumps([base,ca,cp]))
