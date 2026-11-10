// Executes actual WASM bytes, but with a deliberately limited read-only NEAR host adapter.
// NOT a NEAR metered VM, receipt simulator, gas proof, deployment or signing tool.
import fs from 'node:fs';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const dir='.test-artifacts/milestone6a';
const audit=JSON.parse(fs.readFileSync(`${dir}/audit.json`,'utf8'));
const snapshot=JSON.parse(fs.readFileSync(`${dir}/state.json`,'utf8'));
const manifest=JSON.parse(fs.readFileSync('docs/milestone-5b-artifact.json','utf8'));
const rehearsal=JSON.parse(fs.readFileSync(`${dir}/deployed-state-rehearsal.json`,'utf8'));
assert.equal(audit.live,'read-only snapshot complete');assert.equal(snapshot.block_hash,audit.blockHash);assert.equal(snapshot.block_height,audit.blockHeight);
const sha=b=>crypto.createHash('sha256').update(b).digest('hex');
const deployed=fs.readFileSync(`${dir}/deployed.wasm`), proposed=fs.readFileSync(manifest.files.find(x=>x.path.endsWith('corrected.wasm')).path);
assert.equal(sha(deployed),audit.deployed.sha256);assert.equal(sha(proposed),manifest.files.find(x=>x.path.endsWith('corrected.wasm')).sha256);
const storage=new Map(snapshot.values.map(x=>[x.key,Buffer.from(x.value,'base64')]));assert.equal(storage.size,snapshot.values.length);
const fingerprint=()=> { const entries=[...storage].sort(([a],[b])=>a.localeCompare(b)).map(([k,v])=>[k,v.toString('base64')]); return sha(Buffer.from(JSON.stringify(entries))); };
const before=fingerprint();const results=[];
const allowed=new Set(['get_config','get_policy','get_daily_usage','get_available_balance','is_recipient_allowed','get_payment','get_delegation','get_payment_delegation']);
function run(bytes,method,args={},usage=BigInt(audit.account.storage_usage)) {
 assert(allowed.has(method),'Method outside read-only whitelist');
 let instance;const registers=new Map();let returned;const calls={};
 const memory=()=>new Uint8Array(instance.exports.memory.buffer);
 function read(ptr,len){const a=Number(ptr),n=Number(len);assert(Number.isSafeInteger(a)&&Number.isSafeInteger(n)&&a>=0&&n>=0&&a+n<=memory().length,'Memory bounds');return Buffer.from(memory().slice(a,a+n));}
 function write(ptr,data){const a=Number(ptr);assert(Number.isSafeInteger(a)&&a>=0&&a+data.length<=memory().length,'Memory bounds');memory().set(data,a);}
 function u128(ptr,value){const b=Buffer.alloc(16);b.writeBigUInt64LE(BigInt(value)&((1n<<64n)-1n));b.writeBigUInt64LE(BigInt(value)>>64n,8);write(ptr,b);}
 const host={
  input:r=>registers.set(r,Buffer.from(JSON.stringify(args))),
  register_len:r=>registers.has(r)?BigInt(registers.get(r).length):(1n<<64n)-1n,
  read_register:(r,p)=>{assert(registers.has(r));write(p,registers.get(r));},
  storage_read:(len,p,r)=>{const v=storage.get(read(p,len).toString('base64'));if(!v)return 0n;registers.set(r,v);return 1n;},
  storage_has_key:(len,p)=>storage.has(read(p,len).toString('base64'))?1n:0n,
  value_return:(len,p)=>{returned=read(p,len);},
  storage_usage:()=>usage,
  account_balance:p=>u128(p,audit.account.amount),
  attached_deposit:p=>u128(p,0n),
  block_timestamp:()=>BigInt(audit.blockTimestampNs),
  current_account_id:r=>registers.set(r,Buffer.from(audit.contract)),
  predecessor_account_id:r=>registers.set(r,Buffer.from(audit.config[0])),
  panic_utf8:(len,p)=>{throw Error('WASM panic: '+read(p,len).toString('utf8'));},
  log_utf8:()=>{throw Error('Unexpected logging in read-only view');}
 };
 const module=new WebAssembly.Module(bytes),imports={};
 for(const imp of WebAssembly.Module.imports(module)){
  assert.equal(imp.kind,'function');imports[imp.module]??={};
  imports[imp.module][imp.name]=(...args)=>{calls[imp.name]=(calls[imp.name]??0)+1;if(!host[imp.name])throw Error('Unsupported or mutating host import: '+imp.name);return host[imp.name](...args);};
 }
 instance=new WebAssembly.Instance(module,imports);assert.equal(typeof instance.exports[method],'function');instance.exports[method]();assert(returned,'No value_return');assert.equal(fingerprint(),before,'Storage changed');
 return {value:JSON.parse(returned.toString('utf8')),hostCalls:calls};
}
function compare(method,args,expected){const old=run(deployed,method,args),next=run(proposed,method,args);assert.deepEqual(old.value,next.value);if(expected!==undefined)assert.deepEqual(next.value,expected);results.push({method,args,old:old.value,proposed:next.value,match:true});}
compare('get_config',{},audit.config);compare('get_policy',{},audit.policy);compare('get_daily_usage',{},audit.dailyUsage);compare('get_available_balance',{},audit.availableBalance);
compare('is_recipient_allowed',{recipient:audit.config[1]},true);compare('is_recipient_allowed',{recipient:'not-approved-m6a.testnet'},false);
for(const p of rehearsal.payments)compare('get_payment',{id:p.id},p);
compare('get_payment',{id:'m6a-absent-payment'},null);
assert.equal(run(proposed,'get_delegation',{id:'m6a-absent-delegation'}).value,null);
for(const p of rehearsal.payments)assert.equal(run(proposed,'get_payment_delegation',{id:p.id}).value,null);
const delta=proposed.length-deployed.length;
const projected=run(proposed,'get_available_balance',{},BigInt(audit.account.storage_usage)+BigInt(delta)).value;
const report={checkedAtUtc:new Date().toISOString(),runtime:`Node ${process.version} / V8 ${process.versions.v8}`,actualWasmExecuted:true,nearMeteredVm:false,readOnlyHostAdapter:true,gasAndReceiptsVerified:false,blockHash:audit.blockHash,deployedSha256:sha(deployed),proposedSha256:sha(proposed),stateSha256:sha(fs.readFileSync(`${dir}/state.json`)),allStoragePreserved:fingerprint()===before,comparedViewCases:results.length,actualWasmInvocations:results.length*2+1+rehearsal.payments.length+1,results,proposedDelegationAbsent:true,legacyPaymentBindingsAbsent:true,codeSizeDeltaBytes:delta,projectedAvailableAfterCodeGrowthYocto:projected,limitations:'Actual V8 WASM execution with simulated read-only host context. No NEAR instrumentation/metering, protocol validation, transactions, promise scheduling, callbacks, receipt rollback, chain proof or code deployment was tested.'};
fs.writeFileSync(`${dir}/wasm-readonly-rehearsal.json`,JSON.stringify(report,null,2));console.log(JSON.stringify({...report,results:undefined},null,2));
