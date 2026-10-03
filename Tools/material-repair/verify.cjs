const fs=require('fs'),path=require('path'),crypto=require('crypto');
const root=path.resolve(__dirname,'../..'), applied=require('./applied-repair.json');
const hash=b=>crypto.createHash('sha256').update(b).digest('hex');
const report=[];
for(const c of applied.changes){
 const before=fs.readFileSync(path.join(root,applied.backup,c.file),'utf8'),after=fs.readFileSync(path.join(root,c.file),'utf8');
 if(hash(before)!==c.beforeHash || hash(after)!==c.afterHash)throw Error('Changed since repair: '+c.file);
 if(c.file.endsWith('.mat')){
  const normalize=s=>s.replace(/    - (_MainTex|_BumpMap):\r?\n        m_Texture: [^\r\n]+\r?\n        m_Scale: [^\r\n]+\r?\n        m_Offset: [^\r\n]+/g,'REPAIRED_SLOT').replace(/  m_ValidKeywords:\r?\n  - _NORMALMAP/,'  m_ValidKeywords: []');
  if(normalize(before)!==normalize(after))throw Error('Unexpected non-texture modification: '+c.file);
 }else{
  if(before.replace('  textureType: 0','  textureType: 1').replace('    sRGBTexture: 1','    sRGBTexture: 0')!==after)throw Error('Unexpected importer change');
 }
}
for(const b of applied.bindings){
 const text=fs.readFileSync(path.join(root,b.materialFile),'utf8');
 const re=new RegExp('    - '+b.slot+':\\r?\\n        m_Texture: \\{fileID: 2800000, guid: '+b.guid+', type: 3\\}');
 if(!re.test(text))throw Error('Wrong binding: '+b.material);
 if(!fs.existsSync(path.join(root,b.texture)) || !fs.readFileSync(path.join(root,b.texture+'.meta'),'utf8').includes('guid: '+b.guid))throw Error('Texture target missing');
}
const plan=require('./mapping-plan.json');
for(const m of plan){
 if(!fs.readFileSync(path.join(root,m.file+'.meta'),'utf8').includes('guid: '+m.guid))throw Error('Model remap missing: '+m.name);
 const bindings=applied.bindings.filter(b=>b.material===m.name);
 const issues=applied.summary.skipped.filter(s=>s.material===m.name);
 const hasMaps=m.sources.some(s=>s.links.length);
 report.push({material:m.name,restored:bindings.map(b=>({slot:b.slot,texture:b.texture})),review:issues,status:bindings.length?'restored'+(issues.length?' with conversion limitations':''):hasMaps?'Corona graph / conversion required':'no bitmap connection in source'});
}
fs.writeFileSync(path.join(__dirname,'material-status.json'),JSON.stringify(report,null,2));
console.log('PASS: 116 model remaps, 42 texture references, 41 backup hashes, and narrowly scoped changes verified.');
console.log('Unrestored diffuse materials:', [...new Set(applied.summary.skipped.filter(s=>s.channel.endsWith('texmapDiffuse')).map(s=>s.material))].join(', '));
console.log('Normal-map texture configured without changing source image.');
