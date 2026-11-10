import {test} from 'node:test';
import assert from 'node:assert/strict';
import {exactTaskAmount,runAgent} from '../src/agent.ts';
import {task,context,gateway,tool,fixtureModel} from './fixtures.mjs';

const amount='1000000000000000000000';
const input=()=>({...task(),task:'Propose a payment of exactly 0.001 NEAR to 0xsameey-agent.testnet for Sentinel AI integration testing. Do not execute the payment.',recipient:'0xsameey-agent.testnet',maxAmountYocto:amount});
function setup(proposed=amount){
 const g=gateway();g.getContext=async()=>({...context(),recipient:input().recipient,maxPerTransaction:amount,dailyLimit:amount,availableBalance:amount});
 const model=fixtureModel({3:tool('propose_payment',{recipient:input().recipient,amountYocto:proposed,purpose:'Sentinel AI integration testing'})},input().recipient);
 return {g,model};
}
test('exact NEAR amounts use 24 decimal places without floating point',()=>{
 assert.equal(exactTaskAmount(input().task),amount);
 assert.equal(exactTaskAmount('Pay EXACTLY 0.000001 near.'),'1000000000000000000');
 assert.equal(exactTaskAmount('Pay exactly 0.000000000000000000000001 NEAR.'),'1');
 assert.equal(exactTaskAmount('Pay exactly 1.000000000000000000000001 NEAR.'),'1000000000000000000000001');
 assert.equal(exactTaskAmount('Pay up to 0.001 NEAR.'),undefined);
 for(const s of ['0','-0.001','1e-3','0.0000000000000000000000001'])
   assert.throws(()=>exactTaskAmount(`Pay exactly ${s} NEAR.`),/INVALID_EXACT_AMOUNT/);
 assert.throws(()=>exactTaskAmount('Pay exactly 0.001 NEAR then exactly 0.002 NEAR.'),/AMBIGUOUS_EXACT_AMOUNT/);
});
test('0.001 NEAR reaches security assessment unchanged with no execution',async()=>{
 const {g,model}=setup();let dispatches=0;g.dispatch=async()=>{dispatches++;};
 const result=await runAgent(input(),{next:async(payload,signal)=>{
   assert.equal(JSON.parse(payload.task).exactAmountYocto,amount);
   assert.deepEqual(payload.tools.find(x=>x.name==='propose_payment').parameters.properties.amountYocto.enum,[amount]);
   return model.next(payload,signal);
 }},g);
 assert.equal(g.proposed.length,1);
 assert.equal(g.records.get(result.assessmentId).intent.amountYocto,amount);
 assert.equal(g.proposed[0].recipient,input().recipient);
 assert.equal(dispatches,0);
});
for(const proposed of ['1000000000000000000','100000000000000000000'])
 test(`undersized model proposal ${proposed} cannot be assessed`,async()=>{
   const {g,model}=setup(proposed);
   await assert.rejects(runAgent(input(),model,g),/EXACT_AMOUNT_MISMATCH/);
   assert.equal(g.proposed.length,0);
 });
test('exact prose never raises the structured ceiling or bypasses policy',async()=>{
 const {g,model}=setup();
 await assert.rejects(runAgent({...input(),maxAmountYocto:'1'},model,g),/TASK_SPENDING_CONSTRAINT/);
 g.getContext=async()=>({...context(),recipient:input().recipient,maxPerTransaction:'1',dailyLimit:amount,availableBalance:amount});
 await assert.rejects(runAgent(input(),model,g),/POLICY_SPENDING_CONSTRAINT/);
 assert.equal(g.proposed.length,0);
});
