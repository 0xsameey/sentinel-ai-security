import {guarded,validateModel} from '../../src/provider-common.mjs';
export function createOpenAIModel(client,model) {
  validateModel(model);
  return {next: (input,signal)=>guarded(async bounded=>{
    const history=input.history.flatMap(x=>x.type==='tool_result'?[{type:'function_call_output',call_id:x.callId,output:x.output}]:x.type==='provider_continuation'&&x.provider==='openai'?x.items:[]);
    const response=await client.responses.create({model,instructions:input.instructions,input:[{role:'user',content:input.task},...history],tools:input.tools,tool_choice:'required',parallel_tool_calls:false,max_output_tokens:1200,store:false,include:['reasoning.encrypted_content']},{signal:bounded});
    if(response.status!=='completed'||!Array.isArray(response.output)) throw new Error('protocol');
    return {calls:response.output.filter(x=>x.type==='function_call').map(x=>({id:x.call_id,name:x.name,arguments:x.arguments})),continuation:[{type:'provider_continuation',provider:'openai',items:response.output}]};
  },signal)};
}
