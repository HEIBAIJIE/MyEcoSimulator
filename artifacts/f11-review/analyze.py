import json,pathlib,collections
p=pathlib.Path('artifacts/f11-review'); counts=collections.Counter();sync=collections.Counter();positions=set();seq=collections.defaultdict(list); failures=collections.Counter();daily=[];met=[];examples=[]; health=[]
with (p/'run.ndjson').open(encoding='utf-8') as f:
 cfg=json.loads(next(f))['Configuration'];next(f)
 for line in f:
  r=json.loads(line);h=r['History'];t=int(h['Settlement']['Turn']);acts=collections.Counter(x['Action']['Kind'] for x in h['Input']['People']);counts.update(acts)
  if len(acts)==1:sync[next(iter(acts))]+=1
  if t<55:examples.append([t,dict(acts)])
  for x in h['Input']['People']:seq[x['Person']].append(x['Action']['Kind'])
  for x in h['Settlement']['Results']:
   if not x['Success']:failures[(x['Kind'],x['Reason'])]+=1
  for e in h['Settlement']['After']['Entities']:
   if e['Kind']=='Person':positions.add(tuple(e['Location'].values()))
  met.append(r['Metrics'])
  if t%24==23:
   people=[e for e in h['Settlement']['After']['Entities'] if e['Kind']=='Person'];daily.append(dict(day=t//24+1,alive=sum(int(e['Health'])>0 for e in people),health=min(int(e['Health']) for e in people),actions=dict(collections.Counter(a for s in seq.values() for a in s[-24:])),cash=h['Settlement']['After']['Accounts']['F']['Cash'],metrics=r['Metrics']))
summary=dict(actions=dict(counts),synchronizedTurns=dict(sync),positions=list(positions),failures=[dict(kind=k[0],reason=k[1],count=v) for k,v in failures.items()],first55=examples,daily=daily,personActions={k:dict(collections.Counter(v)) for k,v in seq.items()},firstMetrics=met[0],lastMetrics=met[-1])
(p/'summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(summary,ensure_ascii=True)[:22000])
