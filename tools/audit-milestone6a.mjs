// Read-only deployment audit. No credential loading, signing, key generation or write RPC.
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {createHash} from 'node:crypto';
const dir='.test-artifacts/milestone6a';await mkdir(dir,{recursive:true});
const sha=b=>createHash('sha256').update(b).digest('hex');
const b58=hex=>{let n=BigInt('0x'+hex),out='';const a='123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz';while(n){out=a[Number(n%58n)]+out;n/=58n;}for(let i=0;i<hex.length&&hex.slice(i,i+2)==='00';i+=2)out='1'+out;return out;};
const report={checkedAtUtc:new Date().toISOString(),network:'testnet',contract:'0xsameey-sentinel-v2.testnet',live:'not verified',files:[]};
const manifest=JSON.parse(await readFile('docs/milestone-5b-artifact.json','utf8'));
for(const f of manifest.files){try{const data=await readFile(f.path);report.files.push({path:f.path,expected:f.sha256,actual:sha(data),matches:sha(data)===f.sha256});}catch{report.files.push({path:f.path,matches:false,missing:true});}}
const artifact=manifest.files.find(f=>f.path.endsWith('corrected.wasm'));const proposed=await readFile(artifact.path);
const exportsOf=bytes=>WebAssembly.Module.exports(new WebAssembly.Module(bytes)).filter(x=>x.kind==='function').map(x=>x.name).sort();
report.proposedSha256=sha(proposed);report.proposedCodeHash=b58(sha(proposed));if(report.proposedCodeHash!==manifest.reviewedCodeHash)throw Error('Manifest code hash mismatch');report.proposedExports=exportsOf(proposed);
try{const old=await readFile('.test-artifacts/milestone1b/deployed.wasm');report.historical={path:'.test-artifacts/milestone1b/deployed.wasm',sha256:sha(old),exports:exportsOf(old),identicalToProposed:old.equals(proposed),fresh:false};}catch{}
let stage='status';
async function rpc(method,params){
 if(!['status','block','query','gas_price'].includes(method))throw Error('READ_ONLY_RPC');
 const res=await fetch('https://rpc.testnet.near.org',{method:'POST',redirect:'error',signal:AbortSignal.timeout(15000),headers:{'Content-Type':'application/json'},body:JSON.stringify({jsonrpc:'2.0',id:'sentinel-6a-readonly',method,params})});
 if(!res.ok)throw Error('HTTP_FAILURE');const data=await res.json();if(data.error)throw Error('RPC_FAILURE');return data.result;
}
try{
 const status=await rpc('status',[]);if(status.chain_id!=='testnet')throw Error('WRONG_NETWORK');
 stage='finalized block';const block=await rpc('block',{finality:'final'});report.blockHash=block.header.hash;report.blockHeight=block.header.height;report.blockTimestampNs=block.header.timestamp_nanosec;
 const q=(account_id,request_type,extra={})=>rpc('query',{account_id,request_type,block_id:report.blockHash,...extra});
 stage='contract code';const code=await q(report.contract,'view_code');const deployed=Buffer.from(code.code_base64,'base64');await writeFile(`${dir}/deployed.wasm`,deployed);
 if(code.hash!==b58(sha(deployed)))throw Error('DEPLOYED_CODE_HASH_MISMATCH');
 report.deployed={sha256:sha(deployed),codeHash:code.hash,exports:exportsOf(deployed),identicalToProposed:deployed.equals(proposed)};
 stage='state snapshot';const state=await q(report.contract,'view_state',{prefix_base64:'',include_proof:false});await writeFile(`${dir}/state.json`,JSON.stringify(state,null,2));
 report.stateEntries=state.values.length;
 stage='contract balance';report.account=await q(report.contract,'view_account');if(report.account.code_hash!==code.hash)throw Error('ACCOUNT_CODE_HASH_MISMATCH');
 async function view(method_name,args={}){const v=await q(report.contract,'call_function',{method_name,args_base64:Buffer.from(JSON.stringify(args)).toString('base64')});return JSON.parse(Buffer.from(v.result).toString());}
 stage='deploy-account permissions';report.deploymentKeys=await q(report.contract,'view_access_key_list');
 stage='contract views';report.config=await view('get_config');report.policy=await view('get_policy');report.dailyUsage=await view('get_daily_usage');report.availableBalance=await view('get_available_balance');
 stage='agent permissions';report.agentKeys=await q(report.config[1],'view_access_key_list');report.gasPrice=await rpc('gas_price',{block_id:report.blockHash});
 report.live='read-only snapshot complete';
}catch(error){process.exitCode=2;report.live='blocked';report.blockedAt=stage;report.blocker={type:error.name,code:error.cause?.code??error.code??null};}
await writeFile(`${dir}/audit.json`,JSON.stringify(report,null,2));
console.log(JSON.stringify(report,null,2));
