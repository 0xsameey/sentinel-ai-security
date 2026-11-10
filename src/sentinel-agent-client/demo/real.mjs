// Real-model assessment-only demonstration. Never imports a signer or approval method.
import {randomUUID} from 'node:crypto';
import {configuredProvider} from '../src/provider.mjs';
import {SentinelAgentClient} from '../src/client.ts';
import {runAgent} from '../src/agent.ts';
try {
  const recipient=process.env.SENTINEL_DEMO_RECIPIENT;
  const maxAmountYocto=process.env.SENTINEL_DEMO_MAX_YOCTO;
  if(!recipient||!maxAmountYocto)throw new Error('Demo constraints missing.');
  const model=await configuredProvider();
  const client=new SentinelAgentClient(process.env.SENTINEL_API_URL,process.env.SENTINEL_AGENT_API_KEY);
  // CLI is assessment-only, without a dashboard owner task claim. It cannot be
  // substituted as a dashboard task result: those require an API-persisted run binding.
  const gateway={getContext:(r,s)=>client.getContext(r,s),getAssessment:(id,s)=>client.getAssessment(id,s),
    assessPayment:(p,s)=>client.assessPayment(p,s)};
  const result=await runAgent({runId:randomUUID(),task:process.env.SENTINEL_DEMO_TASK??'Propose a payment for API credits within the selected maximum. Read policy, inspect recipient, propose, then assess.',
    recipient,maxAmountYocto},model,gateway,event=>console.log(JSON.stringify(event)));
  const assessment=await gateway.getAssessment(result.assessmentId);
  console.log(JSON.stringify({event:'REAL_MODEL_ASSESSMENT',...result,decision:assessment.risk.decision,reasonCodes:assessment.risk.reasonCodes,
    recipient:assessment.intent.recipient,amountYocto:assessment.intent.amountYocto,authorization:'OWNER_REQUIRED',signed:false}));
} catch {console.error('Real-model demo unavailable: verify SDK installation, environment credentials, model access and reachable gateway/provider. No signing attempted.');process.exitCode=1;}
