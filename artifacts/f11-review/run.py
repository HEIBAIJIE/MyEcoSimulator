import json,urllib.request,uuid,time,pathlib
base='http://localhost:5184'; out=pathlib.Path('artifacts/f11-review')
def get(p):
 with urllib.request.urlopen(base+p) as r:return json.load(r)
def post(p,b):
 req=urllib.request.Request(base+p,json.dumps(b).encode(),{'Content-Type':'application/json'})
 with urllib.request.urlopen(req) as r:return json.load(r)
start=get('/game/start'); run=post('/runs',dict(RequestId=str(uuid.uuid4()),Variant=start['Variant'],Configuration=None,Policy=start['Policy']))['Id']
(out/'run.json').write_text(json.dumps(dict(Id=run,Start=start),ensure_ascii=False,indent=2),encoding='utf-8')
for i in range(720):
 s=post('/runs/'+run+'/commands',dict(RequestId=str(uuid.uuid4()),Operation='step',Speed=None))
 if (i+1)%60==0:print(json.dumps(s),flush=True)
 if int(s['Turn'])!=i+1:raise RuntimeError(s)
with urllib.request.urlopen(base+'/runs/'+run+'/export') as r,(out/'run.ndjson').open('wb') as f:
 while b:=r.read(1024*1024):f.write(b)
(out/'final.json').write_text(json.dumps(get('/runs/'+run+'/snapshot'),ensure_ascii=False),encoding='utf-8')
print('COMPLETE '+run,flush=True)
