const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '../..');
const model = path.join(root, "Assets/Scenes/Models/Apartment/VLOGGER'S APARTMENT (FBX).FBX");
const b = fs.readFileSync(model);
if (!b.subarray(0, 20).toString().startsWith('Kaydara FBX Binary')) throw Error('Not binary FBX');
const version = b.readUInt32LE(23), wide = version >= 7500, header = wide ? 25 : 13;
function node(at, parent = '') {
  let p = at;
  const uint = () => { const v = wide ? Number(b.readBigUInt64LE(p)) : b.readUInt32LE(p); p += wide ? 8 : 4; return v; };
  const end = uint(), count = uint(), bytes = uint();
  const len = b[p++];
  if (!end) return null;
  if (end > b.length || end <= at) throw Error('Invalid FBX offset');
  const name = b.toString('utf8', p, p + len); p += len;
  const start = p, props = [];
  const keep = parent !== 'Objects' || ['Material', 'Texture', 'Video', 'LayeredTexture'].includes(name);
  if (!keep) return { end, name, props, children: [] };
  for (let i = 0; i < count; i++) {
    const type = String.fromCharCode(b[p++]);
    let v;
    switch (type) {
      case 'Y': v = b.readInt16LE(p); p += 2; break;
      case 'C': v = !!b[p++]; break;
      case 'I': v = b.readInt32LE(p); p += 4; break;
      case 'F': v = b.readFloatLE(p); p += 4; break;
      case 'D': v = b.readDoubleLE(p); p += 8; break;
      case 'L': v = b.readBigInt64LE(p).toString(); p += 8; break;
      case 'S': case 'R': { const n = b.readUInt32LE(p); p += 4; v = type === 'S' ? b.toString('utf8', p, p+n) : { bytes: n }; p += n; break; }
      case 'f': case 'd': case 'l': case 'i': case 'b': case 'c': { const n = b.readUInt32LE(p), size = b.readUInt32LE(p+8); p += 12 + size; v = { array: type, count: n }; break; }
      default: throw Error('Unknown property ' + type);
    }
    props.push(v);
  }
  if (p !== start + bytes) throw Error('Property length mismatch');
  const children = [];
  while (p + header <= end) { const n = node(p, name); if (!n) break; children.push(n); p = n.end; }
  return { end, name, props, children };
}
const top = [];
for (let p = 27; p + header < b.length;) { const n = node(p); if (!n) break; top.push(n); p = n.end; }
const objects = top.find(n => n.name === 'Objects').children.filter(n => n.props.length);
const connections = top.find(n => n.name === 'Connections').children.map(n => n.props);
const result = { version, objects, connections };
fs.writeFileSync(path.join(__dirname, 'fbx-connections.json'), JSON.stringify(result, null, 2));
console.log(JSON.stringify({ version, counts: objects.reduce((a,n) => (a[n.name]=(a[n.name]||0)+1,a),{}), connections: connections.length }, null, 2));
for (const n of objects.filter(n => n.name === 'Texture').slice(0, 5)) console.log(JSON.stringify(n));
