// Read-only account model discovery. No gateway, wallet or signer imports.
import {smokeFailure} from './smoke-errors.mjs';
let stage='configuration';
try {
  if(!process.env.ANTHROPIC_API_KEY?.trim()) throw new Error('configuration');
  stage='provider_setup';
  const {createClient}=await import('../providers/anthropic/runtime.mjs');
  const client=createClient(process.env.ANTHROPIC_API_KEY);
  stage='request';
  const page=await client.models.list({limit:100},{signal:AbortSignal.timeout(30000)});
  const models=page.data.map(item=>item.id);
  const selected=process.env.ANTHROPIC_MODEL;
  if(selected) await client.models.retrieve(selected,{}, {signal:AbortSignal.timeout(30000)});
  console.log(JSON.stringify({event:'CLAUDE_MODEL_DISCOVERY',models,hasMore:page.has_more,selectedModelVerified:!!selected,signed:false}));
} catch(error) {
  console.error(JSON.stringify(smokeFailure(error,stage,false,'anthropic')));
  process.exitCode=1;
}
