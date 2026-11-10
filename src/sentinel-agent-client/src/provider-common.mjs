// Sanitized, provider-independent errors. Never retain upstream bodies or credentials.
export class ProviderError extends Error { constructor(code) { super(code); this.code=code; } }
export function validateModel(model) { if(typeof model!=='string'||!model.trim()||model.length>100) throw new ProviderError('MODEL_CONFIGURATION_INVALID'); }
export async function guarded(work,signal) {
  const bounded=AbortSignal.any([signal??new AbortController().signal,AbortSignal.timeout(30000)]);
  let handler;
  try {
    bounded.throwIfAborted();
    return await Promise.race([work(bounded),new Promise((_,reject)=>{handler=()=>reject(new ProviderError('MODEL_TIMEOUT'));bounded.addEventListener('abort',handler,{once:true});})]);
  } catch(error) {
    if(bounded.aborted||/Timeout|Abort/.test(error?.name??'')) throw new ProviderError('MODEL_TIMEOUT');
    throw new ProviderError(error?.status===401||error?.status===403?'MODEL_AUTHENTICATION_FAILED':error?.status===429?'MODEL_RATE_LIMITED':error?.message==='protocol'?'MODEL_PROTOCOL_INVALID':'MODEL_UNAVAILABLE');
  } finally { if(handler) bounded.removeEventListener('abort',handler); }
}
