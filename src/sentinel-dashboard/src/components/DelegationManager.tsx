import {useEffect,useState} from "react";
import {delegatedApi,type DelegationView,type DelegatedPayment} from "../api";
import {executionHint} from "../presentation";
import {notify,notifyError,notifyProposal,notifyDecision,notifyReconciliation,dismissNotification,allowanceConfirmed} from "../notifications";
import {signContractCall} from "../services/nearTransactions";
import {Steps,StatusBadge,Decision,PaymentStatus,RiskDecisionCard,Feedback,CopyValue,ExplorerLink,TechnicalDetails,EmptyState,ConfirmationDialog} from "./ui";

function yocto(s:string):string {
  if(!/^\d+(\.\d{1,24})?$/.test(s))throw new Error("Enter NEAR with up to 24 decimal places.");
  const [a,b=""]=s.split(".");return (BigInt(a)*10n**24n+BigInt(b.padEnd(24,"0"))).toString();
}
function near(s:string):string {const d=s.padStart(25,"0");return `${d.slice(0,-24)}.${d.slice(-24).replace(/0+$/,"")||"0"}`;}
export default function DelegationManager({token,accountId,agent,isOwner,section="all",selectedId,onSelectId,onUpdated}:{token:string|null;accountId:string|null;agent:string;isOwner:boolean;section?:"all"|"management"|"payments";selectedId?:string;onSelectId?:(id:string)=>void;onUpdated?:()=>void}) {
  const [id,setId]=useState("");const [ids,setIds]=useState<string[]>([]);const [view,setView]=useState<DelegationView|null>(null);
  const [createConfirmed,setCreateConfirmed]=useState(false);const [revokeOpen,setRevokeOpen]=useState(false);
  useEffect(()=>{if(selectedId!==undefined){setId(selectedId);setView(null);setConfirmed(null);}},[selectedId]);
  const [max,setMax]=useState("0.001");const [budget,setBudget]=useState("0.005");const [daily,setDaily]=useState("0.005");
  const [minutes,setMinutes]=useState("60");const [recipients,setRecipients]=useState("");const [task,setTask]=useState("");
  const [recipient,setRecipient]=useState("");const [ceiling,setCeiling]=useState("0.001");
  const [runId,setRunId]=useState<string|null>(null);
  useEffect(()=>setRunId(null),[id,task,recipient,ceiling,token]);
  const [busy,setBusy]=useState(false);const [message,setMessage]=useState("");const [confirmed,setConfirmed]=useState<string|null>(null);
  const [now,setNow]=useState(Date.now());const enabled=Boolean(token&&accountId&&isOwner&&!busy);
  useEffect(()=>{const t=setInterval(()=>setNow(Date.now()),1000);return()=>clearInterval(t);},[]);
  useEffect(()=>{setView(null);setIds([]);setConfirmed(null);if(token)void delegatedApi.list(token).then(x=>setIds(x.map(r=>r.id))).catch(()=>setMessage("Spending allowances could not be loaded. Check your connection and owner sign-in."));},[token]);
  async function refresh(selected=id){if(token&&selected){const result=await delegatedApi.inspect(token,selected);if(result.state)dismissNotification(`delegation-create-pending:${selected}`);if(result.state?.revoked)dismissNotification(`revoke-pending:${selected}`);setView(result);onUpdated?.();return result;}}
  async function act(fn:()=>Promise<unknown>){setBusy(true);setMessage("");try{await fn();}catch(e){const message=e instanceof Error?e.message:"Request failed.";setMessage(message);notifyError(message);try{await refresh();}catch{/* Keep the original error; never retry execution. */}}finally{setBusy(false);}}
  async function create(){if(!token||!accountId)return;const mins=Number(minutes);if(!Number.isInteger(mins)||mins<1||mins>1440)throw new Error("Expiry must be within 24 hours.");
    const call=await delegatedApi.create(token,{id,agent,maxPayment:yocto(max),budget:yocto(budget),dailyLimit:yocto(daily),
      expiresAtNs:(BigInt(Date.now()+mins*60000)*1000000n).toString(),recipients:recipients.split(",").map(x=>x.trim()).filter(Boolean)});
    const result=await signContractCall(call,accountId);setMessage(result.pendingConfirmation?"Wallet confirmation pending.":"Allowance submitted. Check its network state before using it.");
    setIds(x=>Array.from(new Set([...x,id])));
    try {
      const observed=await refresh();
      if(allowanceConfirmed(result,observed,call.args,'create'))notify({variant:'success',title:'Spending allowance created.',description:'The allowance is visible on chain.',key:`delegation-created:${id}`});
      else notify({variant:'warning',title:'Allowance confirmation is pending.',description:'Check the existing allowance before creating another request.',duration:0,key:`delegation-create-pending:${id}`});
    }catch{setMessage('Allowance submitted; the network refresh failed. Check this existing allowance before requesting another transaction.');notifyError('Allowance submission is unconfirmed. Review the existing allowance before taking another action.');}
  }
  useEffect(()=>setCreateConfirmed(false),[id,max,budget,daily,minutes,recipients]);
  const s=view?.state;const expired=s?BigInt(s.expiresAtNs)<=BigInt(now)*1000000n:true;
  const status=!s?"Not observed on chain":s.revoked?"Revoked":expired?"Expired":"Active";
  useEffect(()=>{if(view?.state&&expired&&!view.state.revoked)notify({variant:'warning',title:'Spending allowance expired.',description:`The agent can no longer use allowance ${view.id}.`,key:`delegation-expired:${view.id}:${view.state.expiresAtNs}`});},[view,expired]);
  async function approve(p:DelegatedPayment){if(token&&confirmed===p.id){await delegatedApi.approve(token,p);setConfirmed(null);notify({variant:'success',title:'Assessment approved.',description:'Execution still requires fresh policy checks.',key:`delegated-review:${p.id}`});await refresh();}}
  const management=section!=="payments";const payments=section!=="management";
  const titleId=`${section}-delegations-title`;
  return <section className="panel agent-task-panel delegation-workspace" aria-labelledby={titleId}>
    <div className="panel-heading"><div><h2 id={titleId}>{management?"Spending delegations":"Autonomous payments"}</h2><p>{management?"Give your agent a bounded spending allowance. You can revoke it at any time.":"Your agent can execute eligible payments within an active owner-created allowance."}</p></div></div>
    {payments&&<Steps autonomous/>}
    {!isOwner&&<Feedback message="Sign in with the contract owner wallet to manage allowances, request payments or approve a review."/>}
    {message&&<Feedback message={message} error={message.startsWith("API error")}/>}
    <div className="delegation-picker"><label>Delegation ID<input value={id} maxLength={64} placeholder="Select or enter a delegation" onChange={e=>{setId(e.target.value);onSelectId?.(e.target.value);setView(null);setConfirmed(null);}} list={`${section}-delegation-ids`}/></label><datalist id={`${section}-delegation-ids`}>{ids.map(value=><option key={value} value={value}/>)}</datalist><button className="outline-button" disabled={!token||!id||busy} onClick={()=>void act(async()=>{await refresh();notify({variant:"info",title:"Spending allowance checked.",description:"Review its current on-chain state below."});})}>{busy?"Checking…":"Check delegation"}</button></div>
    {!view&&<p className="helper-text">Check a delegation to load its limits and current network state. This action does not sign a transaction.</p>}
    {management&&<details className="create-delegation"><summary>Create spending allowance</summary><form onSubmit={e=>{e.preventDefault();void act(create);}}>
      <p className="helper-text">Owner wallet signature required. Agent: <strong>{agent||"Not available"}</strong></p>
      <div className="field-grid">{[["Maximum payment (NEAR)","Most the agent can send in one payment.",max,setMax],["Total budget (NEAR)","Maximum amount this allowance can attempt to spend.",budget,setBudget],["Daily limit (NEAR)","Maximum attempted spending allowed per day.",daily,setDaily],["Expires after (minutes)","The allowance stops working after this period (1–1440).",minutes,setMinutes]].map(([label,hint,value,setter])=><label key={String(label)}>{String(label)}<input value={String(value)} onChange={e=>(setter as (v:string)=>void)(e.target.value)} required inputMode="decimal"/><small>{String(hint)}</small></label>)}</div>
      <label>Allowed recipients<input value={recipients} onChange={e=>setRecipients(e.target.value)} placeholder="account.testnet, another.testnet" required maxLength={1040}/><small>Comma-separated accounts already approved by your global policy.</small></label>
      <div className="budget-notice">Failed payment attempts may still consume the delegation's budget.</div>
      <label className="check-label"><input type="checkbox" checked={createConfirmed} onChange={e=>setCreateConfirmed(e.target.checked)}/>I reviewed the agent, recipients, limits and expiry. Continue with an owner wallet signature.</label>
      <button className="primary-button" disabled={!enabled||!id||!agent||!createConfirmed}>Create allowance in wallet</button>
    </form></details>}
    {view&&<><div className="delegation-summary"><div className="panel-heading"><div><span className="section-kicker">SPENDING ALLOWANCE</span><h3>{view.id}</h3></div><StatusBadge tone={status==="Active"?"green":status==="Revoked"?"red":"neutral"}>{status}</StatusBadge></div>{s?<>
      <dl className="detail-grid"><div><dt>Maximum payment</dt><dd>{near(s.maxPayment)} NEAR</dd></div><div><dt>Remaining budget</dt><dd>{near(view.remainingBudget)} NEAR<small>of {near(s.budget)} NEAR total attempt budget</small></dd></div><div><dt>Daily allowance</dt><dd>{near(s.dailyLimit)} NEAR<small>{near(BigInt(s.day)===BigInt(now)/86400000n?s.spentToday:"0")} NEAR attempted today</small></dd></div><div><dt>Expires</dt><dd>{new Date(Number(BigInt(s.expiresAtNs)/1000000n)).toLocaleString()}</dd></div></dl>
      <p className="helper-text">Allowed recipients: {s.recipients.join(", ")}</p>
      <TechnicalDetails label="Delegation evidence"><p>Agent: {agent}</p><p>Attempt budget charged: {near(s.spent)} NEAR. Failed attempts may consume budget.</p><p>Observed {new Date(view.observedAtUtc).toLocaleString()}</p><CopyValue value={view.blockHash} label="Copy block hash"/></TechnicalDetails>
      {management&&<button className="danger-button" disabled={!enabled||s.revoked} onClick={()=>setRevokeOpen(true)}>Revoke allowance</button>}
    </>:<EmptyState title="No on-chain allowance found">Check the ID or create an owner-signed allowance. A prepared request is not proof of an active delegation.</EmptyState>}</div>
    {s&&payments&&<form className="ai-task-form" onSubmit={e=>{e.preventDefault();void act(async()=>{if(token){const attempt=runId??crypto.randomUUID();setRunId(attempt);const result=await delegatedApi.run(token,{delegationId:id,task,recipient,maxAmountYocto:yocto(ceiling)},attempt);setMessage(result.status);notifyProposal(result.status,result.payment);await refresh();}});}}>
      <div className="section-heading"><h3>Describe the payment</h3><p>ALLOW may execute automatically using this delegation. WARN pauses for your review.</p></div><label>Payment task<textarea value={task} onChange={e=>setTask(e.target.value)} maxLength={2000} placeholder="Pay exactly 0.001 NEAR for Sentinel integration testing." required/></label>
      <div className="field-grid"><label>Recipient<input value={recipient} onChange={e=>setRecipient(e.target.value)} placeholder="account.testnet" required/></label><label>Maximum amount (NEAR)<input value={ceiling} onChange={e=>setCeiling(e.target.value)} required inputMode="decimal"/></label></div>
      <button className="primary-button" disabled={!enabled||expired||s.revoked}>{busy?"Request in progress…":"Ask AI to make a payment"}</button>
      {(expired||s.revoked)&&<p className="helper-text">{s.revoked?"This allowance has been revoked.":"This allowance has expired."} Choose an active delegation before requesting a payment.</p>}
      {runId&&<TechnicalDetails label="Request ID"><CopyValue value={runId}/><p>Repeating an unchanged request reuses this ID. Review payment activity before starting another task.</p></TechnicalDetails>}
    </form>}
    {payments&&<><div className="section-heading"><h3>Review and verify payments</h3><p>A risk decision does not prove execution. Only verified settlement means the payment completed.</p></div>
      {view.payments.length===0&&<EmptyState title="No payment activity">Requests under this delegation will appear here.</EmptyState>}
      <div className="proposal-list">{view.payments.map(p=>{const risk=JSON.parse(p.decisionJson) as {reasonCodes:string[];evidence?:unknown[]};const intent=JSON.parse(p.intentJson) as {Recipient:string;AmountYocto:string;Purpose:string;ExpiresAtNs:string};const paymentExpired=BigInt(intent.ExpiresAtNs)<=BigInt(now)*1000000n;const disabledReason=executionHint({signedIn:!!token,owner:isOwner,busy,revoked:!!s?.revoked,delegationExpired:expired,paymentExpired,decision:p.decision,claimed:!!(p.permitId||p.consumedAtUtc),approvalValid:!!p.approvalExpiresAtUtc&&Date.parse(p.approvalExpiresAtUtc)>now});return <article className="proposal-card" key={p.id}>
        <div className="panel-heading"><div><h3>{near(intent.AmountYocto)} NEAR</h3><p>To {intent.Recipient}</p></div><div className="status-pair"><span>Risk <Decision decision={p.decision}/></span><span>Payment <PaymentStatus status={p.status} decision={p.decision} expiry={intent.ExpiresAtNs} reported={!!p.transactionHash} approvalExpiry={p.approvalExpiresAtUtc}/></span></div></div><p>{intent.Purpose}</p>
        <RiskDecisionCard decision={p.decision} reasons={risk.reasonCodes}/>
        <dl className="detail-grid"><div><dt>Payment ID</dt><dd><CopyValue value={p.id}/></dd></div><div><dt>Authorization expires</dt><dd>{new Date(Number(BigInt(intent.ExpiresAtNs)/1000000n)).toLocaleString()}{paymentExpired&&<small>Expired. Review this payment before creating a fresh proposal.</small>}</dd></div><div><dt>Transaction</dt><dd>{p.transactionHash?<><CopyValue value={p.transactionHash} label="Copy transaction hash"/><ExplorerLink hash={p.transactionHash}/></>:"No transaction reported yet"}</dd></div><div><dt>Human review</dt><dd>{p.approvalExpiresAtUtc?`Approval expires ${new Date(p.approvalExpiresAtUtc).toLocaleString()}`:p.decision==="WARN"?"Owner assessment approval required":"Not required for ALLOW"}</dd></div></dl>
        <TechnicalDetails label="Payment evidence"><pre>{JSON.stringify(risk.evidence??[],null,2)}</pre><p>Attempted means a signing permit was consumed. Reserved means a matching transfer is executing on chain. Settled and Failed come only from chain reconciliation.</p></TechnicalDetails>
        {p.decision==="WARN"&&!p.consumedAtUtc&&!(p.approvalExpiresAtUtc&&Date.parse(p.approvalExpiresAtUtc)>now)&&<div className="agent-review"><label className="check-label"><input type="checkbox" checked={confirmed===p.id} onChange={e=>setConfirmed(e.target.checked?p.id:null)}/>I reviewed this exact payment and its warning. Approve only this assessment.</label><button className="outline-button" disabled={!enabled||confirmed!==p.id||paymentExpired||expired||!!s?.revoked} onClick={()=>void act(()=>approve(p))}>Approve assessment</button></div>}
        <div className="action-row"><button className="primary-button" disabled={disabledReason!==null} onClick={()=>void act(async()=>{if(token){await delegatedApi.dispatch(token,p.id);notify({variant:"info",title:"Execution request accepted.",description:"Submission and final settlement still need verification.",key:`dispatch:${p.id}`,paymentId:p.id});await refresh();}})}>Execute permitted payment</button><button className="outline-button" disabled={!enabled||Boolean(p.consumedAtUtc)} onClick={()=>void act(async()=>{if(token){const fresh=await delegatedApi.reassess(token,p.id);notifyDecision((fresh as DelegatedPayment).decision,`reassess:${p.id}`);setConfirmed(null);await refresh();}})}>Reassess</button><button className="outline-button" disabled={!token||busy} onClick={()=>void act(async()=>{if(token){const result=await delegatedApi.reconcile(token,p.id);notifyReconciliation(result as {status?:string;chainObserved?:boolean;paymentId?:string});await refresh();}})}>Verify on-chain status</button></div>
        {disabledReason&&<p className="helper-text">{disabledReason}</p>}
      </article>;})}</div>
    </>}
    </>}
    <ConfirmationDialog open={revokeOpen} title="Revoke this spending allowance?" busy={busy} onClose={()=>setRevokeOpen(false)} onConfirm={()=>void act(async()=>{if(token&&accountId){const call=await delegatedApi.revoke(token,id);const result=await signContractCall(call,accountId);setRevokeOpen(false);const observed=await refresh();if(allowanceConfirmed(result,observed,call.args,'revoke'))notify({variant:"success",title:"Spending allowance revoked.",key:`delegation-revoked:${id}`});else notify({variant:"warning",title:"Revocation confirmation is pending.",description:"Check this allowance before requesting another transaction.",duration:0,key:`revoke-pending:${id}`});}})}><p>The agent will no longer be able to use <strong>{id}</strong> after revocation is confirmed on chain. Review the transaction in your owner wallet.</p></ConfirmationDialog>
  </section>;
}
