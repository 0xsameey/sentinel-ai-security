import {randomUUID} from 'node:crypto';
export const task=()=>({runId:randomUUID(),task:'Pay 100 yoctoNEAR for API credits.',recipient:'merchant.testnet',maxAmountYocto:'100'});
export const context=()=>({network:'testnet',contractId:'treasury.testnet',paused:false,recipient:'merchant.testnet',recipientExists:true,
  recipientAllowed:true,recipientCodeHash:'11111111111111111111111111111111',maxPerTransaction:'1000',dailyLimit:'10000',
  authorizedToday:'0',executedToday:'0',executingToday:'0',availableBalance:'100000',blockHash:'11111111111111111111111111111111',observedAtUtc:new Date().toISOString()});
export function assessment(intent,decision='ALLOW') {
  const observed=Date.now();
  return {assessmentId:randomUUID(),intentHash:'a'.repeat(64),stage:'Authorization',paymentId:null,intent,
    risk:{decision,score:decision==='BLOCK'?100:decision==='WARN'?50:0,reasonCodes:[decision==='BLOCK'?'COUNTERPARTY_BLOCKLISTED':decision==='WARN'?'INDEXED_HISTORY_UNAVAILABLE':'POLICY_AND_EVIDENCE_PASS'],
      fingerprint:'b'.repeat(64),evidence:[{code:'NEAR_FINALIZED_STATE',source:'https://rpc.example',observedAtUtc:new Date().toISOString(),blockHash:'final-block',detail:'test fixture'}],
      assessedAtUtc:new Date(observed).toISOString(),expiresAtUtc:new Date(observed+120000).toISOString()}};
}
export function gateway(decision='ALLOW') {
  const records=new Map();const proposed=[];
  return {records,proposed,getContext:async()=>context(),
    assessPayment:async intent=>{proposed.push(intent);const a=assessment(intent,decision);records.set(a.assessmentId,a);return a;},
    getAssessment:async id=>records.get(id)};
}
export function tool(name,args={},id=randomUUID()){return {calls:[{id,name,arguments:JSON.stringify(args)}],continuation:[{type:'function_call',call_id:id,name,arguments:JSON.stringify(args)}]};}
export function fixtureModel(change={},recipient='merchant.testnet') {
  return {async next({history}){
    const turn=history.filter(x=>x.type==='tool_result').length+1;
    if(change[turn]) return typeof change[turn]==='function'?change[turn](history):change[turn];
    if(turn===1)return tool('read_spending_policy');
    if(turn===2)return tool('inspect_recipient',{recipient});
    if(turn===3)return tool('propose_payment',{recipient,amountYocto:'100',purpose:'API credits'});
    const proposal=history.filter(x=>x.type==='tool_result').map(x=>JSON.parse(x.output)).find(x=>x.proposalId);
    return tool('assess_payment',{proposalId:proposal?.proposalId});
  }};
}
