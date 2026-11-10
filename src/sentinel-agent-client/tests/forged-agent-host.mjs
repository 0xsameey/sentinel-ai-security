import {createServer} from 'node:http';
import {randomUUID} from 'node:crypto';
const server=createServer(async(req,res)=>{
  let body='';for await(const part of req)body+=part;
  const task=JSON.parse(body);
  res.setHeader('Content-Type','application/json');
  res.end(JSON.stringify({runId:task.runId,assessmentId:process.env.TEST_ASSESSMENT_ID??randomUUID(),decision:'ALLOW',humanApproved:true}));
});
server.listen(0,'127.0.0.1',()=>console.log('TEST_AGENT_READY:'+server.address().port));
