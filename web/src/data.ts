export type Data = {[key:string]:any};
export const show=(v:unknown):string=>v===null||v===undefined?'未定义':typeof v==='object'?JSON.stringify(v,null,2):String(v);
export const money=(v:string|null|undefined)=>{if(v==null)return '未定义';const n=BigInt(v);return `${n<0n?'-':''}${(n<0n?-n:n)/100n}.${((n<0n?-n:n)%100n).toString().padStart(2,'0')} 元`;};
export async function api(path:string,body?:unknown):Promise<Data>{const r=await fetch(path,body?{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)}:undefined);if(!r.ok)throw Error(await r.text());return r.json();}
export function parseInputs(text:string){return JSON.parse(text.replace(/(:\s*)(-?\d{16,})(?=\s*[,}])/g,'$1"$2"'));}
