const d=require('./fbx-connections.json');
const p=n=>Object.fromEntries((n.children.find(x=>x.name==='Properties70')?.children||[]).map(x=>[x.props[0],x.props.slice(4)]));
for(const n of d.objects.filter(x=>x.name==='Material' && /Pampas op|ZEM|DOĞ|CRN.MS|de#76|Napkin 3/.test(x.props[1]))) console.log(JSON.stringify({name:n.props[1],props:Object.fromEntries(Object.entries(p(n)).filter(([k])=>/diffuse|Diffuse|bump|Bump|opacity|Opacity|texmap.*On/.test(k)))}));
const plan=require('./mapping-plan.json');
for(const r of plan) for(const s of r.sources) for(const l of s.links.filter(l=>/Diffuse$|Bump$/.test(l.channel))) {
 const transforms=l.leaves.filter(x=>Object.keys(x.transform).some(k=>!k.endsWith('UVSet')));
 if(transforms.length) console.log(JSON.stringify({name:r.name,channel:l.channel,transforms}));
}
