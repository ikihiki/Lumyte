export const number=(v?:number)=>v==null?'—':new Intl.NumberFormat(undefined,{maximumFractionDigits:2}).format(v);
export const bytes=(v?:number)=>v==null?'—':new Intl.NumberFormat(undefined,{style:'unit',unit:v>=1048576?'megabyte':v>=1024?'kilobyte':'byte',unitDisplay:'short',maximumFractionDigits:1}).format(v/(v>=1048576?1048576:v>=1024?1024:1));
export const duration=(ms?:number)=>ms==null?'—':ms<1?`${number(ms*1000)} μs`:ms<1000?`${number(ms)} ms`:`${number(ms/1000)} s`;
export const relative=(value?:Date|string)=>{if(!value)return'Never';const seconds=(Date.now()-new Date(value).getTime())/1000;return seconds<5?'just now':seconds<60?`${Math.floor(seconds)}s ago`:`${Math.floor(seconds/60)}m ago`};
export const download=(name:string,type:string,data:string)=>{const url=URL.createObjectURL(new Blob([data],{type}));const a=document.createElement('a');a.href=url;a.download=name;a.click();URL.revokeObjectURL(url)};
