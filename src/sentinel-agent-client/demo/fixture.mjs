// Explicit deterministic demo composition, never selected by the production server.
import {runAgent} from '../src/agent.ts';
import {task,fixtureModel,gateway} from '../tests/fixtures.mjs';
for(const decision of ['ALLOW','WARN','BLOCK']) {
  const evidence=gateway(decision);
  const result=await runAgent(task(),fixtureModel(),evidence,event=>console.log(JSON.stringify({...event,source:'fixture'})));
  console.log(JSON.stringify({source:'fixture',...result,decision,authorization:decision==='WARN'?'HUMAN_APPROVAL_REQUIRED':decision==='ALLOW'?'OWNER_AUTHORIZATION_REQUIRED':'REFUSED',signed:false}));
}
