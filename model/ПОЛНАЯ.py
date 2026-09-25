# -*- coding: utf-8 -*-
# Полная выкладка: все предметы игры, пересчитанные по новым правилам.
import io,re,collections

MUL={'T0':0.85,'T1':1.00,'T2':1.21,'T3':1.43,'T4':1.86,'T5':2.50}
RAR=['Poor','Common','Uncommon','Rare','Epic','Legendary']
BASE={'Cloth':10,'PaddingArmor':16,'LightLeatherArmor':25,'HardLeaterArmor':38,
      'SplintArmor':42,'ChainArmor':50,'ScaleMail':62,'LamellarArmor':62,
      'HalfPlate':78,'PlateArmor':100,'metalHelmet':78,'leatherHelemt':38,
      'hat':10,'headband':10,'tiara':10,'None':2}
STEEP={'ChainArmor','ScaleMail','LamellarArmor','SplintArmor','HalfPlate','PlateArmor','metalHelmet'}
FIRST={'ChainArmor':2,'ScaleMail':2,'LamellarArmor':2,'HalfPlate':2,'PlateArmor':2}
PART={'head':1.0,'chest':1.2,'pants':0.8,'belt':0.8}
RU={'Cloth':'ткань','PaddingArmor':'стёганка','LightLeatherArmor':'лёгкая кожа',
    'HardLeaterArmor':'твёрдая кожа','SplintArmor':'шинная','ChainArmor':'кольчуга',
    'ScaleMail':'чешуя','LamellarArmor':'ламеллярная','HalfPlate':'полулаты',
    'PlateArmor':'латы','metalHelmet':'шлем железный','leatherHelemt':'шлем кожаный',
    'hat':'шляпа','headband':'повязка','tiara':'венец','None':'без класса'}
SLRU={'head':'шлем','chest':'корпус','pants':'ноги','belt':'пояс'}

def vych(cls,tier,slot):
    b=BASE.get(cls)
    if b is None: return None
    g=1.80 if cls in STEEP else 1.20
    first=FIRST.get(cls,1)
    t=int(tier[1:])
    steps=max(0,t-first)
    return b*(g**steps)*PART.get(slot,1.0)

txt=io.open('items_dump.txt',encoding='utf-8').read()
W=[];A=[]
for line in txt.split(chr(10)):
    t=re.search(r'ступень (T[0-9])',line); q=re.search(r'качество ([A-Za-z]+)',line)
    w=re.search(r'вес ([0-9]+(?:,[0-9]+)?)',line); n=re.search(r'\] ([A-Za-z0-9_]+) \(asset',line)
    d=re.search(r'прочность ([0-9]+(?:,[0-9]+)?)',line)
    if not (t and q and w): continue
    nm=(n.group(1) if n else '?').replace('items_Equipments_Weapons_','').replace('items_Equipments_Wearing_','').replace('items_Equipments_Armors_','')
    kg=float(w.group(1).replace(',','.'))
    mw=re.search(r'оружие ([a-zA-Z]+)/([A-Za-z]+)',line)
    ma=re.search(r'броня ([A-Za-z]+)/([A-Za-z]+)',line)
    if mw:
        h=re.search(r'бьёт: ([^|]+)',line)
        if not h: continue
        parts={}
        for bit in h.group(1).split(','):
            p=bit.strip().split()
            if len(p)>=2:
                try: parts[p[0]]=sum(float(x.replace(',','.')) for x in p[1].split('-'))/len(p[1].split('-'))
                except: pass
        dm=sum(v for k,v in parts.items() if k in ('sharp','blunt','stab'))
        lead=max([(v,k) for k,v in parts.items() if k in ('sharp','blunt','stab')],default=(0,'sharp'))[1]
        W.append((mw.group(2),t.group(1),q.group(1),nm,kg,dm,lead,mw.group(1)))
    elif ma:
        s=re.search(r'слот ([a-z]+)',line)
        if not s or s.group(1) not in PART: continue
        A.append((ma.group(2),t.group(1),q.group(1),nm,kg,s.group(1),
                  float(d.group(1).replace(',','.')) if d else 0))

TYPE={'sharp':'режущее','blunt':'дробящее','stab':'колющее'}
out=io.open('ПОЛНАЯ.txt','w',encoding='utf-8')
def P(x=''): out.write(x+'\n')

P('='*120)
P('ПОЛНАЯ ВЫКЛАДКА ПО НОВЫМ ПРАВИЛАМ')
P('  вес оружия = игровой x множитель тира (Т1 1,00 · Т2 1,21 · Т3 1,43 · Т4 1,86 · Т5 2,50)')
P('  пробитие   = стат x вес;  стат: дробящее — Сила, колющее — Ловкость, режущее — большее')
P('  вычет      = основа класса x рост^(тир − первый тир) x доля куска')
P('               рост 1,20 у ткани и кожи, 1,80 у кольчуги и выше')
P('               доли: корпус 1,2 · шлем 1,0 · ноги и пояс 0,8')
P('  урон       = урон оружия − max(0, вычет − пробитие)')
P('='*120); P()

P('ЧАСТЬ I — ОРУЖИЕ (%d предметов)'%len(W)); P()
by=collections.defaultdict(list)
for cls,t,q,nm,kg,dm,lead,hand in W: by[cls].append((t,q,nm,kg,dm,lead,hand))
for cls in sorted(by):
    P('  %s'%cls)
    P('    %-4s %-10s %-34s %6s %7s %6s %-9s %9s %9s'%('тир','качество','предмет','вес','вес нов','урон','тип','проб.С10','проб.С30'))
    for t,q,nm,kg,dm,lead,hand in sorted(by[cls],key=lambda r:(r[0],RAR.index(r[1]) if r[1] in RAR else 9)):
        nw=kg*MUL.get(t,1.0)
        P('    %-4s %-10s %-34s %6.1f %7.2f %6.1f %-9s %9.0f %9.0f'%(
            t,q,nm[:34],kg,nw,dm,TYPE.get(lead,lead),10*nw,30*nw))
    P()

P('='*120)
P('ЧАСТЬ II — БРОНЯ (%d предметов)'%len(A)); P()
by=collections.defaultdict(list)
for cls,t,q,nm,kg,sl,d in A: by[cls].append((t,q,nm,kg,sl,d))
for cls in sorted(by):
    P('  %s — %s'%(cls,RU.get(cls,cls)))
    P('    %-4s %-10s %-34s %-7s %6s %10s %8s %11s'%('тир','качество','предмет','кусок','вес','durab.было','ВЫЧЕТ','durab.станет'))
    for t,q,nm,kg,sl,d in sorted(by[cls],key=lambda r:(r[0],r[4],RAR.index(r[1]) if r[1] in RAR else 9)):
        v=vych(cls,t,sl)
        P('    %-4s %-10s %-34s %-7s %6.1f %10.0f %8s %11s'%(
            t,q,nm[:34],SLRU.get(sl,sl),kg,d,
            ('%.1f'%v) if v is not None else '—',
            ('%.0f'%(v*kg)) if v is not None else '—'))
    P()
out.close()
print('готово: ПОЛНАЯ.txt, %d оружий, %d броней'%(len(W),len(A)))
