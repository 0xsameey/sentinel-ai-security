import test from 'node:test';
import assert from 'node:assert/strict';
import {requireAgentIsolation} from '../src/runtimeBoundary.mjs';
test('model runner refuses signing and internal worker credentials without displaying values',()=>{
 for(const name of ['NEAR_AGENT_PRIVATE_KEY','NEAR_PRIVATE_KEY','NEAR_SEED_PHRASE','SENTINEL_WORKER_KEY','SENTINEL_DISPATCH_KEY','DelegatedDispatcher__Key','AgentWorker__ApiKey']) {
  assert.throws(()=>requireAgentIsolation({[name]:'SECRET_SENTINEL_TEST_VALUE'}),e=>!e.message.includes('SECRET_SENTINEL_TEST_VALUE'));
 }
 requireAgentIsolation({ANTHROPIC_API_KEY:'provider-only',SENTINEL_AGENT_API_KEY:'proposal-only'});
});
