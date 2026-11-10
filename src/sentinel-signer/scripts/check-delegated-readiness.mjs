// Read-only attestation. No Account instance, signing or transaction imports.
import dotenv from 'dotenv';
import {KeyPairSigner} from 'near-api-js';
import {requireRestrictedKey} from '../dist/src/keyPermissions.js';
dotenv.config({quiet:true});
let stage='configuration';
const checks={};
try {
 const e=process.env;
 Object.assign(checks,{delegatedMode:e.SENTINEL_PAYMENT_MODE==='delegated',testnet:e.NEAR_NETWORK==='testnet',contractMatches:e.NEAR_CONTRACT_ID==='0xsameey-sentinel-v2.testnet',agentMatches:e.NEAR_AGENT_ACCOUNT_ID==='0xsameey-agent.testnet'});
 if(Object.values(checks).some(x=>!x))throw Error();
 const rpc=new URL(e.NEAR_RPC_URL);
 if(rpc.protocol!=='https:'||rpc.username||rpc.password||rpc.search||rpc.hash)throw Error();
 const publicKey=(await KeyPairSigner.fromSecretKey(e.NEAR_AGENT_PRIVATE_KEY).getPublicKey()).toString();
 checks.publicPinMatches=!!e.SENTINEL_EXPECTED_PUBLIC_KEY&&e.SENTINEL_EXPECTED_PUBLIC_KEY===publicKey;
 async function read(method,params){
   const r=await fetch(rpc,{method:'POST',redirect:'error',signal:AbortSignal.timeout(10000),headers:{'Content-Type':'application/json'},body:JSON.stringify({jsonrpc:'2.0',id:'sentinel-readiness',method,params})});
   if(!r.ok)throw Error();const v=await r.json();if(v.error||v.result?.error)throw Error();return v.result;
 }
 stage='rpc';
 if((await read('status',[])).chain_id!=='testnet')throw Error();
 const access=await read('query',{request_type:'view_access_key',finality:'final',account_id:e.NEAR_AGENT_ACCOUNT_ID,public_key:publicKey});
 const price=await read('gas_price',{});
 stage='permissions';
 requireRestrictedKey(access.permission,e.NEAR_CONTRACT_ID,30000000000000n*BigInt(price.gas_price),'execute_delegated_payment');
 checks.restrictedMethodVerified=true;
 stage='public-pin';if(!checks.publicPinMatches)throw Error();
 console.log(JSON.stringify({event:'DELEGATED_READINESS_PASS',network:'testnet',restrictedMethodVerified:true,publicPinMatches:true,signed:false}));
}catch{
 console.error(JSON.stringify({event:'DELEGATED_READINESS_BLOCKED',stage,checks,signed:false}));process.exitCode=1;
}
