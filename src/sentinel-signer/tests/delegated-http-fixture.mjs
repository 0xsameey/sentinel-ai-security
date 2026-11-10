// Simulated submit callback only: no near-api-js, private key or broadcast RPC.
import {createExecutor} from '../dist/src/execution.js';
const api=process.env.TEST_API, near=process.env.TEST_NEAR,id=process.env.TEST_PAYMENT_ID;
const post=async body=>{const r=await fetch(`${near}/control`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});if(!r.ok)throw Error('Fixture control failed');};
const execute=createExecutor({apiUrl:api,workerKey:process.env.TEST_WORKER_KEY,contractId:'treasury.testnet',expectedSignerPublicKey:'ed25519:test-signer',delegated:true,
 submit:async call=>{
   if(call.methodName!=='execute_delegated_payment')throw Error('Wrong method');
   await post({payment:{id,recipient:call.args.recipient,amount_yocto:call.args.amount_yocto,expires_at_ns:call.args.expires_at_ns,status:'Executing'},paymentDelegation:{id,delegationId:call.args.delegation_id}});
   return {transaction:{hash:'1'.repeat(32)},status:{SuccessValue:''}};
 }});
await execute(id);console.log('DELEGATED_SIGNER_SIMULATED');
