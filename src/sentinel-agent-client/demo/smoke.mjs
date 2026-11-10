// Exactly one small real model call; no NEAR/gateway client, approval or signing imports.
import {configuredProvider} from '../src/provider.mjs';
import {agentTools} from '../src/agent.ts';
import {smokeFailure} from './smoke-errors.mjs';
let stage='configuration';
let signal;
try {
  const prefix=process.env.SENTINEL_AGENT_PROVIDER==='anthropic'?'ANTHROPIC':'OPENAI';
  const missing=[prefix+'_API_KEY',prefix+'_MODEL'].filter(name=>!process.env[name]?.trim());
  if(missing.length) {
    console.error(JSON.stringify({...smokeFailure(null,stage),missingEnvironment:missing}));
    process.exitCode=1;
  } else {
  stage='provider_setup';
  const provider=await configuredProvider();
  stage='request';
  signal=AbortSignal.timeout(30000);
  const result=await provider.next({instructions:'Call read_spending_policy with an empty object. Do not propose or execute any payment.',
    task:'Read the spending policy.',tools:agentTools.filter(x=>x.name==='read_spending_policy'),history:[]},signal);
  stage='validation';
  if(result.calls.length!==1||result.calls[0].name!=='read_spending_policy'||JSON.stringify(JSON.parse(result.calls[0].arguments))!=='{}')throw new Error('Unexpected tool call.');
  console.log(JSON.stringify({event:'REAL_MODEL_SMOKE_PASS',provider:process.env.SENTINEL_AGENT_PROVIDER??'openai',tool:'read_spending_policy',signed:false}));
  }
}catch(error){console.error(JSON.stringify(smokeFailure(error,stage,signal?.aborted)));process.exitCode=1;}
