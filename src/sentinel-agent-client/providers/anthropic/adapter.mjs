import {guarded,validateModel} from '../../src/provider-common.mjs';
export function createAnthropicModel(client,model) {
  validateModel(model);
  return {next:(input,signal)=>guarded(async bounded=>{
    const messages=[{role:'user',content:input.task}];
    for(const item of input.history) {
      if(item.type==='provider_continuation'&&item.provider==='anthropic') messages.push({role:'assistant',content:item.items});
      else if(item.type==='tool_result') messages.push({role:'user',content:[{type:'tool_result',tool_use_id:item.callId,content:item.output}]});
    }
    const response=await client.messages.create({model,system:input.instructions,messages,max_tokens:1200,
      tools:input.tools.map(x=>({name:x.name,description:x.description,input_schema:x.parameters})),tool_choice:{type:'any',disable_parallel_tool_use:true}},{signal:bounded});
    if(response.stop_reason!=='tool_use'||!Array.isArray(response.content)) throw new Error('protocol');
    return {calls:response.content.filter(x=>x.type==='tool_use').map(x=>({id:x.id,name:x.name,arguments:JSON.stringify(x.input)})),continuation:[{type:'provider_continuation',provider:'anthropic',items:response.content}]};
  },signal)};
}
