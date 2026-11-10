import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createAgentServer,listenAgentServer} from '../src/server.mjs';
import {gateway,fixtureModel,task} from './fixtures.mjs';
test('isolated HTTP runner authenticates, rejects replay, and never returns approval or signing capability',async()=>{
  const key='synthetic-runner-key-'.repeat(3);const server=createAgentServer({key,model:fixtureModel(),gateway:gateway('WARN')});
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const url=`http://127.0.0.1:${server.address().port}/tasks`;
  const send=(body,credential=key)=>fetch(url,{method:'POST',headers:{'Content-Type':'application/json','X-Sentinel-Runner-Key':credential},body:JSON.stringify(body)});
  try{
    assert.equal((await send(task(),'forged')).status,401);
    const t=task();const first=await send(t);assert.equal(first.status,200);assert.deepEqual(Object.keys(await first.json()).sort(),['assessmentId','runId']);
    assert.equal((await send(t)).status,409);
    assert.equal((await send({...task(),humanApproved:true})).status,422);
  }finally{server.closeAllConnections();await new Promise(resolve=>server.close(resolve));}
});

test('occupied runner port rejects startup without stopping existing runner',async()=>{
 const key='synthetic-runner-key-'.repeat(3);
 const first=createAgentServer({key,model:fixtureModel(),gateway:gateway()});
 const second=createAgentServer({key,model:fixtureModel(),gateway:gateway()});
 await listenAgentServer(first,0);
 try {
   await assert.rejects(listenAgentServer(second,first.address().port),error=>error.code==='EADDRINUSE');
   assert(first.listening);assert(!second.listening);
 }finally{first.closeAllConnections();await new Promise(resolve=>first.close(resolve));}
});
