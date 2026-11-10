import test from 'node:test';
import assert from 'node:assert/strict';
import React from 'react';
import {renderToStaticMarkup} from 'react-dom/server';
import {loadSource} from './render-helper.mjs';
const {PaymentActivity}=await import(await loadSource('components/PaymentActivity'));
const fixture=(status,decision='ALLOW')=>({id:`fixture-${status}`,delegationId:'fixture-allowance',intentJson:JSON.stringify({Recipient:'recipient.testnet',AmountYocto:'1000000000000000000000',ExpiresAtNs:'1',Purpose:'<script>untrusted purpose</script>'}),decisionJson:JSON.stringify({reasonCodes:['POLICY_AND_EVIDENCE_PASS']}),decision,fingerprint:'fixture',status,transactionHash:'unverified-fixture-hash',approvedAtUtc:null,approvalExpiresAtUtc:null,consumedAtUtc:status==='Attempted'?'2026-11-09T00:00:00Z':null,createdAtUtc:'2026-11-09T00:00:00Z'});
test('payment activity separates risk from settlement and does not imply all reports are verified',()=>{
 const html=renderToStaticMarkup(React.createElement(PaymentActivity,{payments:[fixture('Settled'),fixture('Attempted'),fixture('Failed'),fixture('Proposed','BLOCK')]}));
 for(const value of ['Settled','Pending confirmation','Failed','Blocked','0.001','role="table"','role="columnheader"'])assert(html.includes(value),value);
 assert(!html.includes('<script>'));assert(!html.includes('Verified transaction'));
 assert.equal((html.match(/>ALLOW<\/span>/g)??[]).length,3);
});
