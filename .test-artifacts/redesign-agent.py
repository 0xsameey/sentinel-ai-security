from pathlib import Path
p=Path('src/sentinel-dashboard/src/components/AgentTaskPanel.tsx')
s=p.read_text(encoding='utf-8').replace('import type {GatewayAssessment}', 'import {Steps,Feedback,CopyValue,RiskDecisionCard,TechnicalDetails,StatusBadge} from "./ui";\nimport type {GatewayAssessment}',1)
start=s.index('  return <section')
s=s[:start]+'''  return <section className="panel agent-task-panel" id="agent" aria-labelledby="agent-title">
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
'''
p.write_text(s,encoding='utf-8')
