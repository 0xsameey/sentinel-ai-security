// Names only; never include values in errors or audit logs.
export function requireAgentIsolation(env) {
  for(const name of ['NEAR_AGENT_PRIVATE_KEY','NEAR_PRIVATE_KEY','NEAR_SEED_PHRASE','SENTINEL_WORKER_KEY','SENTINEL_DISPATCH_KEY','DelegatedDispatcher__Key','AgentWorker__ApiKey']) {
    if(env[name])throw new Error('Agent process contains forbidden signing or worker configuration. Use separate process environments.');
  }
}
