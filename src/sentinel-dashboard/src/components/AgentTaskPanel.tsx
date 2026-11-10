import {useEffect,useState} from "react";
import {sentinelApi,type PreparedPayment} from "../api";
import {Steps,Feedback,CopyValue,RiskDecisionCard,TechnicalDetails,StatusBadge} from "./ui";
import {notify,notifyError,notifyProposal,dismissNotification} from "../notifications";
import type {GatewayAssessment} from "../../../sentinel-agent-client/src/client";

function yocto(value:string) {
  if(!/^\d+(\.\d{1,24})?$/.test(value)) throw new Error("Enter a positive NEAR ceiling with up to 24 decimal places.");
  const [whole,fraction=""]=value.split(".");
  const n=BigInt(whole)*10n**24n+BigInt(fraction.padEnd(24,"0"));
  if(n<=0n) throw new Error("Ceiling must be positive.");return n.toString();
}
function nearAmount(value:string) {
  const digits=value.padStart(25,"0");const fraction=digits.slice(-24).replace(/0+$/,"");
  return digits.slice(0,-24)+(fraction?"."+fraction:"");
}
export default function AgentTaskPanel({token,isOwner}:{token:string|null;isOwner:boolean}) {
  const [task,setTask]=useState(""); const [recipient,setRecipient]=useState(""); const [ceiling,setCeiling]=useState("0.001");
  const [assessment,setAssessment]=useState<GatewayAssessment|null>(null);
  const [runId,setRunId]=useState("");const [busy,setBusy]=useState(false);const [error,setError]=useState("");
  const [approvalId,setApprovalId]=useState<string|null>(null);const [confirmed,setConfirmed]=useState(false);
  const [approvalExpiry,setApprovalExpiry]=useState<string|null>(null);
  const [activity,setActivity]=useState<{runId:string;status:string;at:string}[]>([]);
  const [prepared,setPrepared]=useState<PreparedPayment|null>(null);const [prepareKey,setPrepareKey]=useState("");
  const [now,setNow]=useState(Date.now());
  useEffect(()=>{const timer=setInterval(()=>setNow(Date.now()),1000);return()=>clearInterval(timer);},[]);
  useEffect(()=>{setAssessment(null);setApprovalId(null);setApprovalExpiry(null);setPrepared(null);setConfirmed(false);setRunId("");},[token,task,recipient,ceiling]);
  useEffect(()=>setActivity([]),[token]);
  const expired=assessment ? Date.parse(assessment.risk.expiresAtUtc)<=now : true;
  const approvalExpired=approvalExpiry!==null&&Date.parse(approvalExpiry)<=now;
  const enabled=Boolean(token&&isOwner&&!busy);
  useEffect(()=>{if(assessment&&expired)notify({variant:'warning',title:'This assessment has expired.',description:'Run a fresh assessment before proceeding.',key:`assessment-expired:${assessment.assessmentId}`});},[assessment,expired]);
  async function action(work:()=>Promise<void>) {setBusy(true);setError("");try{await work();}catch(e){const message=e instanceof Error?e.message:"Request failed.";setError(message);notifyError(message);}finally{setBusy(false);}}
  async function run() {await action(async()=>{
    if(!token) return;if(assessment)dismissNotification(`assessment-expired:${assessment.assessmentId}`);setAssessment(null);setApprovalId(null);setApprovalExpiry(null);setPrepared(null);setConfirmed(false);
    const id=crypto.randomUUID();setRunId(id);setPrepareKey(crypto.randomUUID());
    setActivity(x=>[{runId:id,status:"Running",at:new Date().toISOString()},...x].slice(0,20));
    let result;
    try { result=await sentinelApi.runAgentTask(token,{task,recipient,maxAmountYocto:yocto(ceiling)},id); }
    catch(e) {setActivity(x=>x.map(row=>row.runId===id?{...row,status:"Failed; no authorization granted"}:row));throw e;}
    setAssessment(result.assessment);
    notifyProposal('OWNER_AUTHORIZATION_REQUIRED',{id:result.assessment.assessmentId,decision:result.assessment.risk.decision});
    setActivity(x=>x.map(row=>row.runId===id?{...row,status:result.assessment.risk.decision}:row));
  });}
  async function approve() {await action(async()=>{
    if(!token||!assessment||!confirmed||expired||assessment.risk.decision!=="WARN") return;
    const result=await sentinelApi.approveAssessment(token,assessment.assessmentId,assessment.risk.fingerprint);
    setApprovalId(result.approvalId);
    setApprovalExpiry(result.expiresAtUtc);
    notify({variant:'success',title:'Assessment approved.',description:'Owner wallet authorization is still required.',key:`review:${assessment.assessmentId}`});
  });}
  async function prepare() {await action(async()=>{
    if(!token||!assessment||expired||approvalExpired||assessment.risk.decision==="BLOCK"||assessment.risk.decision==="WARN"&&!approvalId) return;
    setPrepared(await sentinelApi.prepareAssessment(token,assessment.assessmentId,approvalId,prepareKey));
    notify({variant:'info',title:'Owner authorization prepared.',description:'No wallet signature or payment has been submitted.'});
  });}
  return <section className="panel agent-task-panel" id="agent" aria-labelledby="agent-title">
    <div className="panel-heading"><div><h2 id="agent-title">Owner-approved payments</h2><p>Ask AI for a proposal. You retain authority over every payment.</p></div></div><Steps/>
    {!isOwner&&<Feedback message="Connect and sign in with the contract owner wallet to request a proposal or approve a review."/>}
    <form onSubmit={e=>{e.preventDefault();void run();}}>
      <label>Payment task<textarea value={task} onChange={e=>setTask(e.target.value)} maxLength={2000} required disabled={busy} placeholder="Propose a payment of exactly 0.001 NEAR for API credits. Do not execute it."/><small>Describe the amount and purpose. The recipient and maximum below remain fixed boundaries.</small></label>
      <div className="field-grid"><label>Recipient<input value={recipient} onChange={e=>setRecipient(e.target.value)} maxLength={64} required disabled={busy} placeholder="account.testnet"/><small>Choose an account allowed by your policy.</small></label><label>Maximum amount (NEAR)<input value={ceiling} onChange={e=>setCeiling(e.target.value)} required disabled={busy} inputMode="decimal"/><small>The AI cannot propose more than this amount.</small></label></div>
      <div className="action-row"><button className="primary-button" disabled={!enabled||!task.trim()||!recipient}>{busy?"Assessing proposal…":"Propose & assess"}</button><span className="helper-text">No payment is signed or executed by this action.</span></div>
    </form>
    {error&&<Feedback message={error} error/>}
    {runId&&<TechnicalDetails label="Request ID"><CopyValue value={runId}/></TechnicalDetails>}
    {assessment&&<div aria-live="polite"><div className="section-heading"><span className="section-kicker">REVIEW THE PROPOSAL</span><h3>Security assessment</h3></div><dl className="detail-grid"><div><dt>Recipient</dt><dd>{assessment.intent.recipient}</dd></div><div><dt>Proposed amount</dt><dd>{nearAmount(assessment.intent.amountYocto)} NEAR</dd></div><div><dt>Purpose</dt><dd>{assessment.intent.purpose}</dd></div><div><dt>Assessment expires</dt><dd>{new Date(assessment.risk.expiresAtUtc).toLocaleTimeString()}{expired&&<small>Expired. Run a fresh assessment before proceeding.</small>}</dd></div></dl>
      <RiskDecisionCard decision={assessment.risk.decision} reasons={assessment.risk.reasonCodes}><p className="next-action">{assessment.risk.decision==="BLOCK"?"This payment cannot proceed.":assessment.risk.decision==="WARN"?"Review the evidence, then approve this specific assessment. A wallet authorization is still separate.":"You can prepare an owner authorization. ALLOW does not sign or execute the payment."}</p></RiskDecisionCard>
      <TechnicalDetails label="Assessment evidence"><ul>{assessment.risk.evidence.map((item,index)=><li key={index}>{item.code} · {item.source} · {item.observedAtUtc}</li>)}</ul></TechnicalDetails>
      {assessment.risk.decision==="WARN"&&!approvalId&&<div className="agent-review"><label className="check-label"><input type="checkbox" checked={confirmed} onChange={e=>setConfirmed(e.target.checked)} disabled={busy||expired}/>I reviewed this payment, its risk reasons and evidence. Approve this assessment only.</label><button className="outline-button" disabled={!enabled||!confirmed||expired} onClick={()=>void approve()}>Approve assessment</button></div>}
      {approvalId&&<Feedback message={approvalExpired?"Your review approval has expired. Run a fresh assessment.":`Assessment reviewed. Approval expires at ${new Date(approvalExpiry!).toLocaleTimeString()}. Owner wallet authorization is still required.`}/>}
      {assessment.risk.decision!=="BLOCK"&&<div className="action-row"><button className="primary-button" disabled={!enabled||expired||approvalExpired||Boolean(prepared)||assessment.risk.decision==="WARN"&&!approvalId} onClick={()=>void prepare()}>Prepare owner authorization</button><span className="helper-text">Creates an unsigned request; does not move funds.</span></div>}
      {prepared&&<div className="prepared-result"><div className="panel-heading"><h3>Owner authorization pending</h3><StatusBadge>Not signed</StatusBadge></div><p>The authorization is prepared. No wallet signature or payment has been submitted by this screen.</p><CopyValue value={prepared.paymentId}/><TechnicalDetails label="Unsigned authorization"><pre>{JSON.stringify(prepared.transaction,null,2)}</pre></TechnicalDetails></div>}
    </div>}
    {activity.length>0&&<TechnicalDetails label={`Request history this session (${activity.length})`}><ul>{activity.map(row=><li key={row.runId}>{new Date(row.at).toLocaleTimeString()} · {row.status}<CopyValue value={row.runId}/></li>)}</ul><p>Session history only. Canonical assessments remain stored by Sentinel.</p></TechnicalDetails>}
  </section>;
}
