import type {Data} from './data';
export function activity(person:Data,history?:Data){
 const intent=history?.Input?.People.find((p:Data)=>p.Person===person.Id);
 const kind=intent?.Action.Kind;
 const results=history?.Settlement?.Results.filter((r:Data)=>r.Subject===person.Id&&(r.Kind==='action'||r.Kind==='intent'))||[];
 const result=results.find((r:Data)=>!r.Success)||results.at(-1);
 const names:Record<string,string>={Labor:'⚒ 劳动',Rest:'☾ 休息',Eat:'◒ 进食',Travel:'➜ 旅行',Haul:'▣ 搬运',Idle:'· 等待'};
 const dead=Number(person.Health)<=0;
 const failed=result?.Success===false;
 return {label:dead?'已离世':!intent?'尚无行动记录':`${names[kind]||kind}${failed?' · 未完成':''}`,animate:!dead&&!failed&&!!result?.Success&&kind!=='Idle',speed:kind==='Rest'?1.5:5,amplitude:kind==='Rest'?1:2.5,tilt:kind==='Labor'?.09:kind==='Eat'?.05:0};
}
