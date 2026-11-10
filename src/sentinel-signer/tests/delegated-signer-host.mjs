// Test-only signer composition: injected submission writes the explicit HTTP chain fixture.
import {createSignerApp} from '../dist/src/signerApp.js';
import {createExecutor} from '../dist/src/execution.js';
const execute=createExecutor({apiUrl:process.env.TEST_API_URL,workerKey:process.env.TEST_WORKER_KEY,
 contractId:'treasury.testnet',expectedSignerPublicKey:'ed25519:test-signer',delegated:true,
 submit:async call=>{
  const response=await fetch(process.env.TEST_NEAR+'/control',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({payment:{id:call.args.id,recipient:call.args.recipient,amount_yocto:call.args.amount_yocto,expires_at_ns:call.args.expires_at_ns,status:'Executing'},paymentDelegation:{id:call.args.id,delegationId:call.args.delegation_id}})});
  if(!response.ok)throw Error('Fixture failure');
  return {transaction:{hash:'1'.repeat(31)+(String(call.args.id).charCodeAt(0)%8+1)},status:{SuccessValue:''}};
 }});
createSignerApp(process.env.TEST_DISPATCH_KEY,execute).listen(0,'127.0.0.1',function(){console.log('DELEGATED_TEST_SIGNER_READY:'+this.address().port);});
