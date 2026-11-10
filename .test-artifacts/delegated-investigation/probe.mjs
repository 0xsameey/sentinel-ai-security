for(const url of ['https://localhost:7201/health','http://localhost:5156/health','http://127.0.0.1:8021/tasks']){
 try{const r=await fetch(url,{redirect:'manual',signal:AbortSignal.timeout(3000)});console.log(JSON.stringify({url,status:r.status,redirect:r.headers.has('location')}));}
 catch(e){console.log(JSON.stringify({url,code:e.cause?.code??e.code??e.name}));}
}
