// Conservative, reversible repair. Run without flags to inspect; --apply to write.
const fs=require('fs'), path=require('path'), crypto=require('crypto');
const root=path.resolve(__dirname,'../..');
const plan=require('./mapping-plan.json');
const hash=b=>crypto.createHash('sha256').update(b).digest('hex');
const changes=new Map(), bindings=[], skipped=[];
function read(file){return changes.get(file)?.after ?? fs.readFileSync(path.join(root,file),'utf8');}
function stage(file,after){const before=changes.get(file)?.before ?? fs.readFileSync(path.join(root,file),'utf8');if(before!==after)changes.set(file,{file,before,after});}
function resolve(material,channel){
 const perSource=material.sources.map(s=>{
  const links=s.links.filter(l=>l.channel===channel);
  const enabled=s.properties[channel.replace('texmap','texmapOn')]?.[0];
  if(!links.length || enabled===0) return [];
  return links.flatMap(l=>l.leaves);
 });
 if(!perSource.some(ls=>ls.length)) return null;
 if(perSource.some(ls=>!ls.length || ls.some(l=>l.matches.length!==1))) {skipped.push({material:material.name,channel,reason:'Missing file or differing same-name material sources'});return null;}
 const leaves=perSource.flat();
 const targets=[...new Set(leaves.flatMap(l=>l.matches))];
 if(targets.length!==1){skipped.push({material:material.name,channel,reason:'Multiple image sources / procedural blend',targets});return null;}
 const transforms=leaves.map(l=>({scale:l.transform['3dsMax|BitmapTex|uvwScale']?.slice(0,2)||[1,1],offset:l.transform['3dsMax|BitmapTex|uvwOffset']?.slice(0,2)||[0,0]}));
 if(leaves.some(l=>Object.entries(l.transform).some(([k,v])=>/Angle$/.test(k)&&v.some(x=>Math.abs(x)>1e-6))) || new Set(transforms.map(t=>JSON.stringify(t))).size!==1){skipped.push({material:material.name,channel,reason:'Rotation or inconsistent UV transforms requires visual conversion'});return null;}
 return {file:targets[0],...transforms[0]};
}
function bind(m,slot,target){
 const before=read(m.file), guid=read(target.file+'.meta').match(/^guid: (\w+)/m)[1];
 const re=new RegExp('(    - '+slot+':\\r?\\n)(        m_Texture: \\{[^\\r\\n]+\\}\\r?\\n        m_Scale: \\{[^\\r\\n]+\\}\\r?\\n        m_Offset: \\{[^\\r\\n]+\\})');
 const match=before.match(re);if(!match)throw Error('Slot absent '+m.file+' '+slot);
 if(!match[2].includes('m_Texture: {fileID: 0}')){skipped.push({material:m.name,channel:slot,reason:'Existing texture preserved'});return false;}
 const nl=before.includes('\r\n')?'\r\n':'\n';
 let after=before.replace(re,()=>match[1]+`        m_Texture: {fileID: 2800000, guid: ${guid}, type: 3}${nl}        m_Scale: {x: ${target.scale[0]}, y: ${target.scale[1]}}${nl}        m_Offset: {x: ${target.offset[0]}, y: ${target.offset[1]}}`);
 if(slot==='_BumpMap') {
  if(!after.includes('  m_ValidKeywords: []'))throw Error('Unexpected existing shader keywords');
  after=after.replace('  m_ValidKeywords: []','  m_ValidKeywords:'+nl+'  - _NORMALMAP');
 }
 stage(m.file,after);bindings.push({material:m.name,materialFile:m.file,slot,texture:target.file,guid});return true;
}
for(const m of plan){
 if(!m.file || !m.sourceCount) throw Error('Invalid remap '+m.name);
 const diffuse=resolve(m,'3dsMax|CoronaMtlPb|texmapDiffuse');
 if(diffuse) bind(m,'_MainTex',diffuse);
 const bump=resolve(m,'3dsMax|CoronaMtlPb|texmapBump');
 if(bump && path.basename(bump.file)==='Textile Normal N.jpg') {
  if(bind(m,'_BumpMap',bump)){
   const meta=bump.file+'.meta', before=read(meta);
   stage(meta,before.replace('  textureType: 0','  textureType: 1').replace('    sRGBTexture: 1','    sRGBTexture: 0'));
  }
 } else if(bump) skipped.push({material:m.name,channel:'Bump',reason:'Height image requires a separate converted normal asset; original image preserved',texture:bump.file});
 for(const s of m.sources) for(const l of s.links){
  if(!/texmapDiffuse$|texmapBump$/.test(l.channel)&&l.leaves.length)skipped.push({material:m.name,channel:l.channel,reason:'Corona channel requires shader/packing conversion',textures:[...new Set(l.leaves.flatMap(v=>v.matches))]});
 }
}
const summary={materials: new Set(bindings.map(x=>x.material)).size,bindings:bindings.length,files:changes.size,skipped:[...new Map(skipped.map(s=>[JSON.stringify(s),s])).values()]};
const result={summary,bindings,changes:[...changes.values()].map(c=>({file:c.file,beforeHash:hash(c.before),afterHash:hash(c.after)}))};
fs.writeFileSync(path.join(__dirname,'repair-plan.json'),JSON.stringify(result,null,2));
console.log(JSON.stringify({materials:summary.materials,bindings:summary.bindings,files:summary.files,reviewItems:summary.skipped.length},null,2));
if(process.argv.includes('--apply')){
 if(!changes.size)process.exit(0);
 const backup=path.join(root,'Backups','material-repair-'+new Date().toISOString().replace(/[:.]/g,'-'));
 fs.mkdirSync(backup,{recursive:true});
 for(const c of changes.values()){
  const dest=path.join(backup,c.file);fs.mkdirSync(path.dirname(dest),{recursive:true});fs.copyFileSync(path.join(root,c.file),dest);
 }
 fs.writeFileSync(path.join(backup,'manifest.json'),JSON.stringify(result,null,2));
 for(const c of changes.values())if(hash(fs.readFileSync(path.join(root,c.file)))!==hash(c.before))throw Error('File changed during repair: '+c.file);
 for(const c of changes.values())fs.writeFileSync(path.join(root,c.file),c.after);
 for(const c of changes.values())if(hash(fs.readFileSync(path.join(root,c.file)))!==hash(c.after))throw Error('Verification failed: '+c.file);
 for(const b of bindings){if(!fs.existsSync(path.join(root,b.texture))||!read(b.texture+'.meta').includes('guid: '+b.guid))throw Error('Missing texture '+b.texture);}
 fs.writeFileSync(path.join(__dirname,'applied-repair.json'),JSON.stringify({...result,backup:path.relative(root,backup),verified:true},null,2));
 console.log('Applied and verified; backup: '+backup);
}
