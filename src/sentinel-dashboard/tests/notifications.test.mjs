import test from 'node:test';
import assert from 'node:assert/strict';
import React from 'react';
import {renderToStaticMarkup} from 'react-dom/server';
import {loadSource} from './render-helper.mjs';
const module=await import(await loadSource('notifications'));
const {createToastStore,toastStore,notifyDecision,notifyError,notifyProposal,notifyReconciliation,allowanceConfirmed}=module;
const {Feedback}=await import(await loadSource('components/ui'));
function clock(){let now=0;let next=0;const jobs=new Map();return {now:()=>now,set:(fn,ms)=>{const id=++next;jobs.set(id,{fn,at:now+ms});return id;},clear:id=>jobs.delete(id),advance:ms=>{now+=ms;for(const [id,job]of jobs)if(job.at<=now){jobs.delete(id);job.fn();}},jobs};}
function clear(){for(const toast of toastStore.getSnapshot())toastStore.dismiss(toast.id);}
for(const variant of ['success','warning','error','info'])test(`${variant} reuses the accessible feedback component`,()=>{
 const html=renderToStaticMarkup(React.createElement(Feedback,{message:`Fixture ${variant}`,variant,onDismiss:()=>{},description:'Fixture description'}));
 assert(html.includes(`feedback-${variant}`));assert(html.includes('Dismiss notification'));
 assert(html.includes(variant==='error'?'role="alert"':'role="status"'));assert(html.includes('aria-atomic="true"'));
});
test('duplicate action and Strict Mode replay produce one toast',()=>{
 const c=clock();const store=createToastStore(c);
 const message={variant:'success',title:'Fixture confirmed',key:'same-action'};
 store.push(message);store.push(message);assert.equal(store.getSnapshot().length,1);assert.equal(c.jobs.size,1);
 store.dismiss(store.getSnapshot()[0].id);store.push(message);assert.equal(store.getSnapshot().length,0);
 c.advance(6001);store.push(message);assert.equal(store.getSnapshot().length,1);
});
test('changed outcome updates an existing keyed toast instead of retaining contradictory feedback',()=>{
 const c=clock();const store=createToastStore(c);
 store.push({variant:'info',title:'Checking',key:'one-action'});const id=store.getSnapshot()[0].id;
 store.push({variant:'error',title:'Blocked',key:'one-action'});
 assert.equal(store.getSnapshot().length,1);assert.equal(store.getSnapshot()[0].id,id);assert.equal(store.getSnapshot()[0].variant,'error');assert.equal(c.jobs.size,1);
});
test('dismiss, timeout, sticky financial warnings and paused timers are independent',()=>{
 const c=clock();const store=createToastStore(c);
 store.push({variant:'success',title:'expires'});c.advance(2000);store.pause();c.advance(10000);
 assert.equal(store.getSnapshot().length,1);store.resume();c.advance(2500);assert.equal(store.getSnapshot().length,0);
 store.push({variant:'warning',title:'Uncertain payment',duration:0});c.advance(999999);assert.equal(store.getSnapshot().length,1);
 store.dismiss(store.getSnapshot()[0].id);assert.equal(store.getSnapshot().length,0);
});
test('route or subscriber unmount does not discard async notification delivery',()=>{
 const c=clock();const store=createToastStore(c);let updates=0;
 const unsubscribe=store.subscribe(()=>updates++);store.push({variant:'info',title:'Before route change'});unsubscribe();
 store.push({variant:'warning',title:'Async result after route change'});
 let newSnapshot;const stop=store.subscribe(()=>{newSnapshot=store.getSnapshot();});
 assert.equal(store.getSnapshot().length,2);store.push({variant:'error',title:'Later result'});
 assert.equal(updates,1);assert.equal(newSnapshot.length,3);stop();
});
test('queue stays bounded without duplicate timer leaks',()=>{
 const c=clock();const store=createToastStore(c);for(let i=0;i<8;i++)store.push({variant:'info',title:`item ${i}`});
 assert.equal(store.getSnapshot().length,4);assert.equal(c.jobs.size,4);
});
test('ALLOW, WARN and BLOCK describe assessments, never payment success',()=>{
 try{for(const decision of ['ALLOW','WARN','BLOCK']){
   notifyDecision(decision,`fixture-decision-${decision}`);
   const result=toastStore.getSnapshot().at(-1);
   assert.equal(result.variant,decision==='ALLOW'?'success':decision==='WARN'?'warning':'error');
   assert(!/Payment successful|Payment sent|Payment confirmed/.test(result.title));
 }}finally{clear();}
});
test('unconfirmed existing proposal is sticky and links to activity, unavailable runner is distinct',()=>{
 try{
   notifyError('API error 502: {"code":"AGENT_RUN_UNAVAILABLE","proposalCreated":false}','fixture-runner');
   let result=toastStore.getSnapshot().at(-1);assert.equal(result.variant,'error');assert(result.description.includes('No proposal was confirmed'));
   notifyProposal('PROPOSAL_CREATED_DISPATCH_UNCONFIRMED',{id:'fixture-partial',decision:'ALLOW'});
   result=toastStore.getSnapshot().at(-1);assert.equal(result.variant,'warning');assert.equal(result.remaining,0);assert.equal(result.href,'#payments');
   assert(!result.description.includes('try again'));assert(result.title.includes('Proposal saved'));
 }finally{clear();}
});
test('Attempted never becomes a settlement success; only verified chain reconciliation can confirm',()=>{
 try{
   notifyProposal('AUTONOMOUS_PROPOSAL',{id:'fixture-attempt',decision:'ALLOW',status:'Attempted'});
   let result=toastStore.getSnapshot().at(-1);assert.equal(result.variant,'info');assert(result.title.includes('attempt'));
   notifyReconciliation({paymentId:'fixture-unverified',status:'Settled',chainObserved:false});
   result=toastStore.getSnapshot().at(-1);assert.notEqual(result.variant,'success');
   notifyReconciliation({paymentId:'fixture-settled',status:'Settled',chainObserved:true});
   result=toastStore.getSnapshot().at(-1);assert.equal(result.title,'Payment confirmed on NEAR.');assert.equal(result.variant,'success');
   notifyReconciliation({paymentId:'fixture-failed',status:'Failed',chainObserved:true});
   result=toastStore.getSnapshot().at(-1);assert.equal(result.title,'Payment failed on NEAR.');assert.equal(result.variant,'error');
 }finally{clear();}
});
test('verified terminal reconciliation removes an earlier unconfirmed toast for that payment only',()=>{
 try{
   notifyProposal('PROPOSAL_CREATED_DISPATCH_UNCONFIRMED',{id:'fixture-resolved',decision:'ALLOW'});
   notifyReconciliation({paymentId:'fixture-resolved',status:'Settled',chainObserved:true});
   assert(!toastStore.getSnapshot().some(item=>item.paymentId==='fixture-resolved'&&item.variant==='warning'));
   assert(toastStore.getSnapshot().some(item=>item.title==='Payment confirmed on NEAR.'));
 }finally{clear();}
});
test('allowance success requires a matching observed chain state, never a wallet request alone',()=>{
 const expected={id:'fixture-allowance',agent:'agent.testnet',max_payment:'100',budget:'500',daily_limit:'300',expires_at_ns:'1799999999999999999',recipients:['recipient.testnet']};
 const observation={id:expected.id,state:{agent:expected.agent,maxPayment:'100',budget:'500',dailyLimit:'300',expiresAtNs:expected.expires_at_ns,recipients:['recipient.testnet'],revoked:false}};
 const wallet={success:true,pendingConfirmation:false};
 assert.equal(allowanceConfirmed(wallet,undefined,expected,'create'),false);
 assert.equal(allowanceConfirmed({...wallet,pendingConfirmation:true},observation,expected,'create'),false);
 assert.equal(allowanceConfirmed(wallet,observation,expected,'create'),true);
 assert.equal(allowanceConfirmed(wallet,{...observation,state:{...observation.state,recipients:['other.testnet']}},expected,'create'),false);
 assert.equal(allowanceConfirmed(wallet,observation,expected,'revoke'),false);
 assert.equal(allowanceConfirmed(wallet,{...observation,state:{...observation.state,revoked:true}},expected,'revoke'),true);
});
test('one provider includes one viewport and does not execute child actions',async()=>{
 const {NotificationProvider}=await import(await loadSource('components/Notifications',{'./ui':await loadSource('components/ui')}));
 const html=renderToStaticMarkup(React.createElement(NotificationProvider,null,React.createElement('p',null,'Fixture page')));
 assert.equal((html.match(/id="sentinel-notifications"/g)??[]).length,1);assert(html.includes('Fixture page'));
});
