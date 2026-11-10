export {createOpenAIModel} from '../providers/openai/adapter.mjs';
export {createAnthropicModel} from '../providers/anthropic/adapter.mjs';
export async function configuredProvider(env=process.env) {
  const provider=env.SENTINEL_AGENT_PROVIDER??'openai';
  if(!['openai','anthropic'].includes(provider)) throw new Error('Unsupported agent provider.');
  const prefix=provider==='openai'?'OPENAI':'ANTHROPIC';
  if(!env[prefix+'_API_KEY']||!env[prefix+'_MODEL']) throw new Error('Provider environment configuration is required.');
  const {createClient}=await import(`../providers/${provider}/runtime.mjs`);
  const adapter=await import(`../providers/${provider}/adapter.mjs`);
  return adapter[provider==='openai'?'createOpenAIModel':'createAnthropicModel'](createClient(env[prefix+'_API_KEY']),env[prefix+'_MODEL']);
}
