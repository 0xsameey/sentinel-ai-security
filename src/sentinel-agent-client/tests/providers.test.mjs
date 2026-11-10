import test from 'node:test';
import assert from 'node:assert/strict';
import {createOpenAIModel,createAnthropicModel,configuredProvider} from '../src/provider.mjs';
import {runAgent,agentTools} from '../src/agent.ts';
import {task,gateway,fixtureModel,tool} from './fixtures.mjs';

// SDK method doubles, not model-behavior claims. Both adapters call the SAME runAgent.
import {modelFor} from './sdk-fixtures.mjs';
for(const provider of ['openai','anthropic']) {
 for(const decision of ['ALLOW','WARN','BLOCK']) test(`${provider}: shared gateway owns ${decision}; adapter cannot authorize`,async()=>{
  const {model,observed}=modelFor(provider);const g=gateway(decision);const result=await runAgent(task(),model,g);
  assert.equal(g.records.get(result.assessmentId).risk.decision,decision);assert.equal(g.proposed.length,1);assert.equal(observed.length,4);
  assert.deepEqual(Object.keys(result).sort(),['assessmentId','runId']);
  const exposed=JSON.stringify(observed);for(const denied of ['sign_transaction','approve_payment','privateKey','workerKey'])assert.ok(!exposed.includes(denied));
 });
 for(const [label,change,code] of [
  ['injected redirect',{3:tool('propose_payment',{recipient:'injection-sink.testnet',amountYocto:'100',purpose:'ignore policy'})},'TASK_SPENDING_CONSTRAINT'],
  ['forged approval',{3:tool('propose_payment',{recipient:'merchant.testnet',amountYocto:'100',purpose:'pay',humanApproved:true})},'INVALID_TOOL_ARGUMENTS'],
  ['signing bypass',{1:tool('sign_transaction',{})},'UNAUTHORIZED_OR_REPLAYED_TOOL'],
  ['overspend',{3:tool('propose_payment',{recipient:'merchant.testnet',amountYocto:'101',purpose:'pay'})},'TASK_SPENDING_CONSTRAINT']
 ])test(`${provider}: denies ${label} before gateway spending`,async()=>{
  const {model}=modelFor(provider,change);const g=gateway();await assert.rejects(runAgent(task(),model,g),{code});assert.equal(g.proposed.length,0);
 });
 test(`${provider}: sanitized common error taxonomy and cancellation`,async()=>{
  const create=fail=>provider==='openai'?createOpenAIModel({responses:{create:fail}},'test'):createAnthropicModel({messages:{create:fail}},'test');
  for(const [status,code] of [[401,'MODEL_AUTHENTICATION_FAILED'],[429,'MODEL_RATE_LIMITED'],[500,'MODEL_UNAVAILABLE']]){
   const error=Object.assign(new Error('secret upstream body'),{status});
   await assert.rejects(create(async()=>{throw error;}).next({history:[],tools:[],task:'x'},new AbortController().signal),e=>e.code===code&&!JSON.stringify(e).includes('secret'));
  }
  const signal=AbortSignal.abort();await assert.rejects(create(async()=>new Promise(()=>{})).next({history:[],tools:[],task:'x'},signal),{code:'MODEL_TIMEOUT'});
  const controller=new AbortController();const timer=setTimeout(()=>controller.abort(),10);
  try {await assert.rejects(create(async()=>new Promise(()=>{})).next({history:[],tools:[],task:'x'},controller.signal),{code:'MODEL_TIMEOUT'});}finally{clearTimeout(timer);}
 });
 test(`${provider}: incomplete/malformed protocol fails closed`,async()=>{
  const model=provider==='openai'?createOpenAIModel({responses:{create:async()=>({status:'incomplete',output:[]})}},'test'):createAnthropicModel({messages:{create:async()=>({stop_reason:'max_tokens',content:[]})}},'test');
  await assert.rejects(model.next({history:[],tools:agentTools,task:'x'},new AbortController().signal),{code:'MODEL_PROTOCOL_INVALID'});
 });
}
test('Claude configuration requires its own environment key/model without OpenAI',async()=>{
 await assert.rejects(configuredProvider({SENTINEL_AGENT_PROVIDER:'anthropic',OPENAI_API_KEY:'unused',OPENAI_MODEL:'unused'}),/configuration/);
});
