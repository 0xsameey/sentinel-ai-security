import {createServer} from 'node:http';
import {timingSafeEqual} from 'node:crypto';
import {pathToFileURL} from 'node:url';
import {runAgent,runDelegatedAgent,validateTask,AgentDenied} from './agent.ts';
import {SentinelAgentClient} from './client.ts';
import {configuredProvider} from './provider.mjs';
import {requireAgentIsolation} from './runtimeBoundary.mjs';

export function createAgentServer({key,model,gateway,audit=()=>{}}) {
  if(typeof key!=='string'||key.length<32) throw new Error('Runner service credential required.');
  const claimed=new Set(); let active=0;
  return createServer(async(req,res)=>{
    res.setHeader('Content-Type','application/json');
    const respond=(status,body)=>{res.writeHead(status);res.end(JSON.stringify(body));};
    const expected=Buffer.from(key), supplied=Buffer.from(req.headers['x-sentinel-runner-key']??'');
    if(expected.length!==supplied.length || !timingSafeEqual(expected,supplied)) return respond(401,{code:'UNAUTHORIZED'});
    if(req.method!=='POST'||!['/tasks','/delegated-tasks'].includes(req.url)) return respond(404,{code:'NOT_FOUND'});
    const controller=new AbortController();
    res.on('close',()=>{if(!res.writableEnded) controller.abort();});
    try {
      let body=''; for await(const part of req){body+=part;if(Buffer.byteLength(body)>8192) return respond(413,{code:'REQUEST_TOO_LARGE'});}
      const raw=JSON.parse(body); const delegated=req.url==='/delegated-tasks';
      const delegationId=delegated?raw.delegationId:undefined;
      if(delegated) {
        if(typeof delegationId!=='string'||!/^[A-Za-z0-9_-]{1,64}$/.test(delegationId)||Object.keys(raw).length!==5) throw new AgentDenied('INVALID_DELEGATION');
        delete raw.delegationId;
      }
      const task=validateTask(raw);
      if(claimed.has(task.runId)) return respond(409,{code:'AGENT_RUN_ALREADY_CLAIMED'});
      if(active>=2||claimed.size>=1000) return respond(429,{code:'AGENT_CAPACITY_LIMIT'});
      claimed.add(task.runId);active++;
      try {respond(200,await (delegated?runDelegatedAgent:runAgent)(task,model,delegated?gateway.delegatedGateway(delegationId):gateway,audit,{signal:controller.signal}));}
      finally {active--;}
    } catch(error) {respond(422,{code:error instanceof AgentDenied ? error.code : 'AGENT_FAILED'});}
  });
}
export async function listenAgentServer(server,port) {
  await new Promise((resolve,reject)=>{
    const failed=error=>{server.off('listening',ready);reject(error);};
    const ready=()=>{server.off('error',failed);resolve();};
    server.once('error',failed);server.once('listening',ready);server.listen(port,'127.0.0.1');
  });
}
if(process.argv[1] && import.meta.url===pathToFileURL(process.argv[1]).href) {
  let stage='isolation';
  try {
    requireAgentIsolation(process.env);
    stage='gateway_configuration';
    const gateway=new SentinelAgentClient(process.env.SENTINEL_API_URL,process.env.SENTINEL_AGENT_API_KEY,fetch,
      {allowLoopbackHttp:process.env.SENTINEL_ALLOW_LOOPBACK_HTTP==='true'});
    stage='provider_setup';const model=await configuredProvider();
    const server=createAgentServer({key:process.env.SENTINEL_RUNNER_KEY,model,gateway,
      audit:event=>console.log(JSON.stringify(event))});
    server.requestTimeout=100000;server.headersTimeout=10000;
    const port=Number(process.env.SENTINEL_AGENT_PORT??8021);
    if(!Number.isInteger(port)||port<1||port>65535) throw new Error('Invalid port.');
    stage='bind';await listenAgentServer(server,port);
    console.log(JSON.stringify({event:'AGENT_LISTENING',port}));
  } catch(error) {
    const code=error?.code==='EADDRINUSE'?'AGENT_PORT_IN_USE':error?.code==='EACCES'?'AGENT_BIND_DENIED':'AGENT_STARTUP_FAILED';
    console.error(JSON.stringify({event:'AGENT_STARTUP_FAILED',stage,code}));process.exitCode=1;
  }
}
