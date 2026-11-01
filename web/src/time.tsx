import type {Data} from './data';
export function simulationTime(turn:number,dayLength=24){
 if(dayLength!==24)return {day:Math.floor(turn/dayLength)+1,hour:turn%dayLength,text:`场景时间 · 回合 ${turn}`,phase:'自定义日长',tint:'transparent'};
 const hour=turn%24,day=Math.floor(turn/24)+1;
 const points=[{h:0,r:25,g:35,b:76,a:.30},{h:5,r:185,g:132,b:120,a:.20},{h:8,r:255,g:239,b:182,a:.03},{h:17,r:214,g:119,b:65,a:.19},{h:20,r:36,g:47,b:96,a:.29},{h:24,r:25,g:35,b:76,a:.30}];
 const i=points.findIndex((p,j)=>j<points.length-1&&hour>=p.h&&hour<points[j+1].h),a=points[i],b=points[i+1],f=(hour-a.h)/(b.h-a.h),mix=(x:number,y:number)=>x+(y-x)*f;
 return {day,hour,text:`第${day}天 ${String(hour).padStart(2,'0')}:00`,phase:hour<5?'深夜':hour<8?'黎明':hour<17?'白昼':hour<20?'傍晚':'夜晚',tint:`rgba(${mix(a.r,b.r)},${mix(a.g,b.g)},${mix(a.b,b.b)},${mix(a.a,b.a)})`};
}
export function Timepiece({turn,dayLength}:{turn:number,dayLength:number}){
 const t=simulationTime(turn,dayLength);
 return <section className="timepiece glass" aria-label={`当前已提交时间 ${t.text} ${t.phase}`}><svg viewBox="0 0 64 64" aria-hidden="true"><circle cx="32" cy="32" r="29" fill="#eee4c8" stroke="#9e895b"/>{Array.from({length:12},(_,i)=><line key={i} x1="32" y1="6" x2="32" y2="10" stroke="#827956" transform={`rotate(${i*30} 32 32)`}/>)}<line x1="32" y1="32" x2="32" y2="16" stroke="#405849" strokeWidth="3" transform={`rotate(${t.hour*30} 32 32)`}/><line x1="32" y1="32" x2="32" y2="9" stroke="#405849" strokeWidth="2"/><circle cx="32" cy="32" r="3" fill="#9e895b"/></svg><div><strong>{t.text}</strong><small>{t.hour>=8&&t.hour<17?'☀':'☾'} {t.phase} · 已完成{Math.floor(turn/dayLength)}天</small></div></section>;
}
export function summarize(records:Data[]){
 const results=records.flatMap(r=>r.History.Settlement.Results);
 return `经过${records.length}回合 · ${results.filter(r=>r.Kind==='action'&&r.Success&&r.Reason.includes('进食')).length}次进食 · ${results.filter(r=>r.Kind==='order'&&r.Success&&Number(r.Quantity)>0).length}笔订单成交`;
}
