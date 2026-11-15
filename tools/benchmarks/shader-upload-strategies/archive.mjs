import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import zlib from 'node:zlib';
const base = path.resolve(process.argv[2]);
const output = path.resolve(process.argv[3]);
const dest = path.join(output, 'docs/performance/data/shader-upload-strategies-20261113');
fs.mkdirSync(path.join(dest, 'raw'), {recursive:true});
const hash = data => crypto.createHash('sha256').update(data).digest('hex');
const walk = dir => fs.readdirSync(dir,{withFileTypes:true}).flatMap(entry=>entry.isDirectory()?walk(path.join(dir,entry.name)):[path.join(dir,entry.name)]);
const entries=[];
function save(label, data) {
 const name=label.replaceAll('/','__')+'.gz';
 const compressed=zlib.gzipSync(data,{level:9,mtime:0});
 fs.writeFileSync(path.join(dest,'raw',name),compressed);
 const restored=zlib.gunzipSync(fs.readFileSync(path.join(dest,'raw',name)));
 if (!restored.equals(data)) throw new Error('Failed archive '+label);
 entries.push({source:label,archive:'raw/'+name,bytes:data.length,sha256:hash(data),archiveSha256:hash(compressed)});
}
for (const folder of ['artifacts','pilot-artifacts']) {
 for(const file of walk(path.join(base,folder))) {
  if(/(full\.json|\.log|\.json|\.csv|\.txt|\.stderr|\.sha256|\.diff|\.patch)$/.test(file))save(path.relative(base,file),fs.readFileSync(file));
 }
}
for (const variant of ['direct','buffered']) {
 const list=fs.readFileSync(path.join(base,'artifacts/candidate-files.txt'),'utf8').trim().split('\n');
 for(const relative of list)save('final-source/'+variant+'/'+relative,fs.readFileSync(path.join(base,variant,relative)));
}
for(const file of ['summary.json','summary.csv'])fs.copyFileSync(path.join(base,'artifacts',file),path.join(dest,file));
fs.writeFileSync(path.join(dest,'manifest.json'),JSON.stringify({schema:1,baseRevision:'693b969f85e3cd489de52050ed91c5df689cf21f',description:'main/pr columns mean direct/CPU-buffer; pilot superseded; source-complete final candidates',entries},null,2)+'\n');
console.log(JSON.stringify({files:entries.length,bytes:entries.reduce((sum,e)=>sum+e.bytes,0)}));
