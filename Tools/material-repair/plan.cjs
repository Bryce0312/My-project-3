const fs = require('fs'), path = require('path');
const root = path.resolve(__dirname, '../..');
const data = require('./fbx-connections.json');
const byId = new Map(data.objects.map(n => [n.props[0], n]));
const name = n => n.props[1].split('\0')[0];
const props = n => Object.fromEntries((n.children.find(c => c.name === 'Properties70')?.children || []).map(c => [c.props[0], c.props.slice(4)]));
const incoming = id => data.connections.filter(c => c[2] === id);
const files = dir => fs.readdirSync(dir, {withFileTypes:true}).flatMap(e => e.isDirectory() ? files(path.join(dir,e.name)) : [path.join(dir,e.name)]);
const all = files(path.join(root,'Assets'));
const guid = file => fs.readFileSync(file+'.meta','utf8').match(/^guid: (\w+)/m)?.[1];
const images = all.filter(f => /\.(jpg|png|tga|jpeg|tif|dds)$/i.test(f));
const mats = all.filter(f => f.endsWith('.mat'));
const importer = fs.readFileSync(path.join(root,"Assets/Scenes/Models/Apartment/VLOGGER'S APARTMENT (FBX).FBX.meta"),'utf8');
const decode = s => s.startsWith('"') ? JSON.parse(s.replace(/\\x([0-9a-f]{2})/gi, '\\u00$1')) : s;
const remaps = [...importer.matchAll(/      name: (.+)\r?\n    second: \{fileID: 2100000, guid: (\w+), type: 2\}/g)].map(m=>({name:decode(m[1]),guid:m[2]}));
function leaves(id, seen=new Set()) {
  if(seen.has(id)) return []; seen.add(id);
  const n=byId.get(id); if(!n) return [];
  const p=props(n);
  const names = [...n.children.filter(c => ['FileName','Filename','RelativeFilename'].includes(c.name)).map(c=>c.props[0]), ...Object.entries(p).filter(([k])=> /filename$/i.test(k)).flatMap(([,v])=>v)].filter(v=>typeof v==='string' && v.length);
  if(names.length) return [...new Set(names)].map(f=>({fbxPath:f, node:id, transform: Object.fromEntries(Object.entries(p).filter(([k])=>/uvwScale|uvwOffset|Angle|UVSet/.test(k)))}));
  return incoming(id).filter(c=>['Texture','Video'].includes(byId.get(c[1])?.name)).flatMap(c=>leaves(c[1],new Set(seen)));
}
const report = remaps.map(r=>{
  const mat = mats.find(f=>guid(f)===r.guid);
  const objects = data.objects.filter(n=>n.name==='Material' && name(n).replace(/[. ]+$/, s=>'_'.repeat(s.length))===r.name || n.name==='Material' && name(n)===r.name);
  return {...r, file: mat && path.relative(root,mat), sourceCount:objects.length, sources:objects.map(n=>({id:n.props[0], properties:props(n), links:incoming(n.props[0]).filter(c=>byId.get(c[1])?.name==='Texture').map(c=>({channel:c[3], texture:c[1], leaves:leaves(c[1]).map(l=>({...l,matches:images.filter(f=>path.basename(f).toLowerCase()===path.win32.basename(l.fbxPath).toLowerCase()).map(f=>path.relative(root,f))}))}))}))};
});
fs.writeFileSync(path.join(__dirname,'mapping-plan.json'),JSON.stringify(report,null,2));
console.log('Remaps:',remaps.length,'missing materials:',report.filter(r=>!r.file).length,'ambiguous source materials:',report.filter(r=>r.sourceCount!==1).map(r=>[r.name,r.sourceCount]));
for(const r of report) {
 const links=r.sources.flatMap(s=>s.links);
 if(links.length) console.log(JSON.stringify({name:r.name,links:links.map(l=>({channel:l.channel,files:[...new Set(l.leaves.map(f=>path.win32.basename(f.fbxPath)))],missing:l.leaves.filter(f=>f.matches.length!==1).map(f=>f.fbxPath)}))}));
}
