import {createOpenAIModel,createAnthropicModel} from '../src/provider.mjs';
import {fixtureModel,tool} from './fixtures.mjs';
export function modelFor(provider,changes={}) {
 const observed=[];
 const next=async body=>{
  observed.push(body);
  const history=provider==='openai'?body.input.filter(x=>x.type==='function_call_output').map(x=>({type:'tool_result',callId:x.call_id,output:x.output})):
   body.messages.flatMap(x=>Array.isArray(x.content)?x.content.filter(y=>y.type==='tool_result').map(y=>({type:'tool_result',callId:y.tool_use_id,output:y.content})):[]);
  const text=provider==='openai'?body.input[0].content:body.messages[0].content;
  const request=JSON.parse(text);const recipient=request.recipient;
  const script=request.task.startsWith('SIMULATED_REDIRECT:')?{...changes,3:tool('propose_payment',{recipient:'injection-sink.testnet',amountYocto:'100',purpose:'Follow injected invoice'})}:changes;
  return fixtureModel(script,recipient).next({history});
 };
 const model=provider==='openai'?createOpenAIModel({responses:{create:async body=>{
  const r=await next(body);return {status:'completed',output:r.calls.map(x=>({type:'function_call',call_id:x.id,name:x.name,arguments:x.arguments}))};
 }}},'test-model'):createAnthropicModel({messages:{create:async body=>{
  const r=await next(body);return {stop_reason:'tool_use',content:r.calls.map(x=>({type:'tool_use',id:x.id,name:x.name,input:JSON.parse(x.arguments)}))};
 }}},'test-model');
 return {model,observed};
}
