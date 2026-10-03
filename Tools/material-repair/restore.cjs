// Restore exactly this repair. Refuse to overwrite any later edits.
const fs=require('fs'),path=require('path'),crypto=require('crypto');
const root=path.resolve(__dirname,'../..'),report=require('./applied-repair.json');
const hash=b=>crypto.createHash('sha256').update(b).digest('hex');
for(const c of report.changes){
 const current=hash(fs.readFileSync(path.join(root,c.file)));
 if(current!==c.afterHash && current!==c.beforeHash)throw Error('Later edits detected, refusing to overwrite: '+c.file);
 if(hash(fs.readFileSync(path.join(root,report.backup,c.file)))!==c.beforeHash)throw Error('Backup hash mismatch: '+c.file);
}
if(!process.argv.includes('--apply')){console.log('Rollback preflight passed. Add --apply to restore '+report.changes.length+' files.');process.exit(0);}
for(const c of report.changes)fs.copyFileSync(path.join(root,report.backup,c.file),path.join(root,c.file));
console.log('Restored original material files and texture import settings. Refresh Unity Assets.');
