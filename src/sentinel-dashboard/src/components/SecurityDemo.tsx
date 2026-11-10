import {notify} from "../notifications";
import {friendlyError} from "../presentation";
import {useState} from "react";
import {ShieldCheck,ShieldAlert,Activity,LockKeyhole} from "lucide-react";
import type {GatewayAssessment} from "../../../sentinel-agent-client/src/client";
import "../App.css";
import "./SecurityDemo.css";

type Audit={id:string;eventType:string;createdAtUtc:string;actorId:string;paymentId:string|null;detailsJson:string};
function auditDetails(value:string){try{return JSON.stringify(JSON.parse(value??"{}"),null,2);}catch{return value;}}
const session=location.hash.slice(1);
if(session) history.replaceState(null,"",location.pathname);
export default function SecurityDemo(){
 const [credential,setCredential]=useState(session);
 const [assessment,setAssessment]=useState<GatewayAssessment|null>(null);
 const [audit,setAudit]=useState<Audit[]>([]);const [activity,setActivity]=useState<string[]>([]);
 const [busy,setBusy]=useState(false);const [error,setError]=useState("");const [confirmed,setConfirmed]=useState(false);
 const [auditError,setAuditError]=useState("");
 const [approved,setApproved]=useState(false);const [expired,setExpired]=useState(false);const [state,setState]=useState("READY");
 const [delegatedWarn,setDelegatedWarn]=useState<string|null>(null);
 const [content,setContent]=useState("");const [proof,setProof]=useState<Record<string,unknown>|null>(null);
 async function request(path:string,method="POST"){
  const response=await fetch("/demo-api/"+path,{method,headers:{Authorization:"Bearer "+credential}});
  const body=await response.json().catch(()=>({code:"DEMO_AUTHENTICATION_REQUIRED"}));
  if(!response.ok)throw new Error(`${body.code??"Request rejected"} (HTTP ${response.status})`);return body;
 }
 async function action(label:string,work:()=>Promise<void>){
  setBusy(true);setError("");try{await work();notify({variant:"info",title:"Simulation action completed.",description:label+" No live transaction was submitted."});setActivity(x=>[`${new Date().toLocaleTimeString()} · ${label}`, ...x]);}
  catch(e){const message=e instanceof Error?e.message:"Request failed";setError(message);notify({variant:"error",title:"Simulation request could not be completed.",description:friendlyError(message),details:message});setActivity(x=>[`${new Date().toLocaleTimeString()} · ${label}: ${message}`,...x]);}
  finally {try{setAudit(await request("audit","GET"));setAuditError("");}catch{setAuditError("Audit refresh failed. Displayed records may be stale; retry Refresh.");}setBusy(false);}
 }
 function accept(result:{assessment?:GatewayAssessment;state:string}){setState(result.state);if(result.assessment){setAssessment(result.assessment);setApproved(false);setConfirmed(false);}}
 async function run(scenario:string){await action(`Scenario ${scenario}`,async()=>{
  setAssessment(null);setProof(null);setContent("");setState("ASSESSING");
  const result=await request("run/"+scenario);accept(result);setContent(result.untrustedContent??"");setProof(result.agentRedirectStatus?{agentRedirectStatus:result.agentRedirectStatus,evidenceSha256:result.evidenceSha256}:null);setExpired(false);setApproved(false);setConfirmed(false);
 });}
 return <main className="security-demo">
  <header className="demo-header"><div className="demo-brand"><ShieldCheck size={32}/><span>Sentinel <small>SECURITY LAB</small></span></div><span className="network-badge">Local simulation · no real funds</span></header>
  <div className="demo-intro"><p className="demo-eyebrow">MILESTONE 5B / ATTACK SIMULATION</p><h1>Untrusted instructions.<br/>Independent protection.</h1><p>Explore the real security gateway with controlled testnet facts. Decisions, approvals and audit records come from Sentinel. Model output and chain state are simulated.</p></div>
  <aside className="demo-notice"><LockKeyhole size={20}/><div><strong>Signing is disabled. No blockchain transactions are submitted.</strong><p>Live execution requires an approved compatible contract upgrade and verified restricted keys. This isolated fixture models those prerequisites.</p></div></aside>
  {!session&&<label>Demo owner session credential<input type="password" value={credential} onChange={e=>setCredential(e.target.value)} placeholder="Use the session link printed by the demo command"/></label>}
  <section className="demo-scenarios" aria-label="Attack scenarios">{[
   ["ALLOW","Routine payment","100 yoctoNEAR to the legitimate merchant, within the current policy."],
   ["WARN","Human review","History is unavailable. Explicit owner approval and fresh validation are required."],
   ["BLOCK","Injected invoice","An adversarial proposal redirects funds to the evidence-backed simulation blocklist."],
   ["LIMIT","Excessive spending","A proposal exceeds the 1,000 yoctoNEAR per-payment ceiling."],
  ].map(([id,title,description])=><button key={id} className={`demo-scenario ${id.toLowerCase()}`} disabled={busy||!credential} onClick={()=>void run(id)}><span>{id}</span><h2>{title}</h2><p>{description}</p></button>)}</section>

  <section className="panel"><h2>Delegated autonomous spending</h2><p>Owner-created limits replace per-payment approval for ALLOW only. Every result below uses the actual local API and isolated PostgreSQL, with simulated chain evidence.</p>
   <div className="demo-actions">{["ALLOW","WARN","BLOCK","BUDGET","REVOKED","FAILED"].map(scenario=><button key={scenario} disabled={busy||!credential} onClick={()=>void action(`Delegated ${scenario}`,async()=>{const result=await request("delegated/"+scenario);setProof(result);setAssessment(null);setState("DELEGATED_"+scenario);setDelegatedWarn(result.payment.decision==="WARN"?result.payment.id:null);setConfirmed(false);})}>{scenario}</button>)}</div>
   {delegatedWarn&&<div className="human-review"><label><input type="checkbox" checked={confirmed} onChange={e=>setConfirmed(e.target.checked)}/>I reviewed the exact delegated WARN proposal and evidence below.</label><button disabled={busy||!confirmed} onClick={()=>void action("Delegated human approval and revalidation",async()=>{setProof(await request("delegated-approve/"+delegatedWarn));setDelegatedWarn(null);})}>Approve delegated WARN</button></div>}
  </section>
  {error&&<p role="alert" className="agent-error">{error}</p>}
  <div className="demo-grid"><section className="panel"><div className="panel-heading"><h2><ShieldAlert size={19}/> Decision and transaction</h2><span className="network-badge">SIMULATED</span></div>
   <p className="demo-state" aria-live="polite">{busy?"Validating…":state.replaceAll("_"," ")}</p>
   {!assessment&&<p>Choose a scenario to inspect the gateway decision, evidence and authorization state.</p>}
   {assessment&&<div className={`risk-result ${assessment.risk.decision.toLowerCase()}`}><h2>{assessment.risk.decision}</h2><p><strong>{assessment.intent.recipient}</strong> · {assessment.intent.amountYocto} yoctoNEAR</p><p>Stage: {assessment.stage} · Assessment expires {new Date(assessment.risk.expiresAtUtc).toLocaleTimeString()}</p>
   <ul>{assessment.risk.reasonCodes.map(code=><li key={code}>{code.replaceAll("_"," ")}</li>)}</ul>
   <details><summary>Gateway evidence ({assessment.risk.evidence.length})</summary>{assessment.risk.evidence.map((item,i)=><div key={i} className="demo-evidence"><strong>{item.code}</strong><p>{item.detail}</p><code>{item.source}</code></div>)}</details>
   {content&&<details open><summary>Untrusted attack content · simulated evidence</summary><pre>{content}</pre><p>The adversarial proposal is deliberately submitted to the independent gateway, even if an agent follows the injection. This does not measure model susceptibility.</p></details>}
   {assessment.risk.decision==="WARN"&&<div className="human-review"><label><input type="checkbox" checked={confirmed} onChange={e=>setConfirmed(e.target.checked)} disabled={busy||expired}/> I reviewed this exact recipient, amount and evidence as the simulated owner.</label><button disabled={busy||!confirmed||expired||approved} onClick={()=>void action("Authenticated owner approval",async()=>{const result=await request("approve/"+assessment.assessmentId);setApproved(true);setProof(result);setState("HUMAN_APPROVED_REVALIDATION_REQUIRED");})}>Approve exact WARN assessment</button></div>}
   <div className="demo-actions"><button disabled={busy||expired||assessment.risk.decision==="BLOCK"||assessment.risk.decision==="WARN"&&!approved} onClick={()=>void action("Revalidate and simulate execution",async()=>{const result=await request((assessment.stage==="Execution"?"finish/":"execute/")+assessment.assessmentId);accept(result);setProof(result);})}>Revalidate & simulate execution</button>
   <button disabled={busy} onClick={()=>void action("Unauthorized approval and gateway bypass probes",async()=>setProof(await request("probes/"+assessment.assessmentId)))}>Attempt security bypasses</button>
   <button disabled={busy||expired||assessment.stage==="Execution"} onClick={()=>void action("Changed policy revalidation",async()=>setProof(await request("revalidate/"+assessment.assessmentId)))}>Change policy & test revalidation</button>
   <button disabled={busy||expired} onClick={()=>void action("Advance simulation clock; test expired authorization",async()=>{await request("expire");setExpired(true);setState("EXPIRED_REASSESSMENT_REQUIRED");await request((assessment.stage==="Execution"?"finish/":"execute/")+assessment.assessmentId);})}>Expire approval & test rejection</button></div>
   {expired&&<p role="status">Simulation clock advanced three minutes. Start a fresh scenario to reassess and approve again.</p>}
   <p>Signed: <strong>No</strong> · Broadcast: <strong>No</strong></p></div>}
   {proof&&<details open><summary>Actual local API outcome</summary><pre>{JSON.stringify(proof,null,2)}</pre></details>}
  </section><section className="panel"><h2><Activity size={19}/> Agent and operator activity</h2><p>Deterministic adversarial proposals; no billable model calls.</p><ol className="demo-activity">{activity.map((line,i)=><li key={i}>{line}</li>)}</ol></section></div>
  <section className="panel demo-audit"><div className="panel-heading"><h2>Persisted security audit</h2><button disabled={busy||!credential} onClick={()=>void action("Refresh audit",async()=>{})}>Refresh</button></div><p>Actual records from this isolated PostgreSQL schema. All payments and upstream facts are simulated.</p>
  {auditError&&<p role="alert">{auditError}</p>}{!audit.length&&!auditError&&<p role="status">No audit records loaded. Run a scenario or refresh.</p>}<div className="demo-table"><table><thead><tr><th>Time</th><th>Security event</th><th>Actor / payment</th><th>Details</th></tr></thead><tbody>{audit.map(row=><tr key={row.id}><td>{new Date(row.createdAtUtc).toLocaleTimeString()}</td><td>{row.eventType}</td><td>{row.actorId}<br/>{row.paymentId}</td><td><details><summary>Inspect record</summary><pre>{auditDetails(row.detailsJson)}</pre></details></td></tr>)}</tbody></table></div>
  </section>
 </main>;
}
