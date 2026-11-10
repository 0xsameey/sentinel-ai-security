// Test composition only. Production server always loads the official SDK provider.
import {createAgentServer} from '../src/server.mjs';
import {SentinelAgentClient} from '../src/client.ts';
import {fixtureModel} from './fixtures.mjs';
import {modelFor} from './sdk-fixtures.mjs';
const origin='https://gateway.test.invalid';
const gateway=new SentinelAgentClient(origin,process.env.TEST_AGENT_KEY,(url,options)=>{
  if(!String(url).startsWith(origin+'/'))throw new Error('Unexpected fixture target');
  return fetch(process.env.TEST_API_URL+String(url).slice(origin.length),options);
});
if(process.env.TEST_FAIL_AFTER_PROPOSAL==='true') {
 const original=gateway.delegatedGateway.bind(gateway);
 gateway.delegatedGateway=id=>({...original(id),getAssessment:async()=>{throw Error('simulated runner failure after proposal persistence');}});
}
const server=createAgentServer({key:process.env.TEST_RUNNER_KEY,gateway,model:process.env.TEST_PROVIDER?modelFor(process.env.TEST_PROVIDER).model:fixtureModel()});
server.listen(0,'127.0.0.1',()=>console.log('TEST_AGENT_READY:'+server.address().port));
