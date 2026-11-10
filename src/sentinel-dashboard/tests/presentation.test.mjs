import test from 'node:test';
import assert from 'node:assert/strict';
import React from 'react';
import {renderToStaticMarkup} from 'react-dom/server';
import {loadSource} from './render-helper.mjs';
const {lifecycle,humanReason,nearDisplay,friendlyError,executionHint}=await import(await loadSource('presentation'));
const {PaymentStatus,RiskDecisionCard,Feedback}=await import(await loadSource('components/ui'));
for(const [raw,label,tone] of [['Proposed','Proposed','neutral'],['Attempted','Execution attempted','blue'],['Reserved','Pending confirmation','blue'],['Settled','Settled','green'],['Failed','Failed','red']])test(`${raw} has truthful lifecycle semantics`,()=>{
 assert.deepEqual(lifecycle(raw,'ALLOW'),{label,tone});
});
test('decision, expiry and submission cannot manufacture settlement',()=>{
 assert.equal(lifecycle('Proposed','WARN').label,'Needs review');
 assert.equal(lifecycle('Proposed','BLOCK').label,'Blocked');
 assert.equal(lifecycle('Proposed','ALLOW','1').label,'Expired');
 assert.equal(lifecycle('Attempted','ALLOW','1',Date.now(),true).label,'Pending confirmation');
 assert.equal(lifecycle('Settled','ALLOW','1').label,'Settled');
 assert.equal(lifecycle('Reserved','ALLOW','1').label,'Pending confirmation');
 assert.equal(lifecycle('Authorized').tone,'neutral');
 assert.equal(lifecycle('Expired').label,'Expired');
 assert.equal(lifecycle('unrecognized-state').label,'Status unavailable');
 assert.equal(lifecycle('Proposed','WARN',undefined,Date.now(),false,new Date(Date.now()+60000).toISOString()).label,'Proposed');
});
test('disabled execution explains uncertainty, expiry, approval and owner requirements',()=>{
 const valid={signedIn:true,owner:true,busy:false,revoked:false,delegationExpired:false,paymentExpired:false,decision:'ALLOW',claimed:false,approvalValid:false};
 assert.equal(executionHint(valid),null);
 assert(executionHint({...valid,claimed:true,paymentExpired:true}).includes('do not submit'));
 assert(executionHint({...valid,decision:'WARN'}).includes('Approve this exact'));
 assert.equal(executionHint({...valid,decision:'WARN',approvalValid:true}),null);
 assert(executionHint({...valid,signedIn:false}).includes('owner wallet'));
 assert(executionHint({...valid,revoked:true}).includes('revoked'));
 assert(executionHint({...valid,delegationExpired:true}).includes('expired'));
 assert(executionHint({...valid,decision:'BLOCK'}).includes('cannot'));
});
test('amount display preserves all yocto precision',()=>{
 assert.equal(nearDisplay('1000000000000000000000'),'0.001');
 assert.equal(nearDisplay('1000000000000000000'),'0.000001');
 assert.equal(nearDisplay('1'),'0.000000000000000000000001');
});
test('budget and quota reasons are readable while raw codes remain available',()=>{
 assert.equal(humanReason('DELEGATION_BUDGET_EXHAUSTED'),'Your agent has used its available spending budget.');
 assert(humanReason('DELEGATION_DAILY_LIMIT').includes("today's spending limit"));
 const html=renderToStaticMarkup(React.createElement(RiskDecisionCard,{decision:'BLOCK',reasons:['DELEGATION_BUDGET_EXHAUSTED']}));
 assert(html.indexOf('Your agent has used')<html.indexOf('DELEGATION_BUDGET_EXHAUSTED'));
 assert(html.includes('<details'));assert(html.includes('Payment blocked by your security rules.'));
});
test('uncertain outcomes advise verification rather than another execution',()=>{
 const raw='API error 409: {"status":"DispatchUnconfirmed"}';
 assert(friendlyError(raw).includes('Verify'));assert(!friendlyError(raw).includes('try again'));
 const html=renderToStaticMarkup(React.createElement(Feedback,{message:raw,error:true}));
 assert(html.includes('role="alert"'));assert(html.indexOf('Verify')<html.indexOf('API error 409'));
});
test('status always includes a text label, independent of its color',()=>{
 const html=renderToStaticMarkup(React.createElement(PaymentStatus,{status:'Attempted',decision:'ALLOW'}));
 assert(html.includes('Execution attempted'));assert(!html.includes('tone-green'));
});
