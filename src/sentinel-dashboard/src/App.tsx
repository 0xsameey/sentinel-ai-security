import { useCallback, useEffect, useMemo, useState, useRef } from "react";
import {notify,notifyError,notifyDecision,notifyReconciliation,dismissNotification} from "./notifications";
import PolicyManager from "./components/PolicyManager";
import AgentTaskPanel from "./components/AgentTaskPanel";
import DelegationManager from "./components/DelegationManager";
import {PaymentActivity} from "./components/PaymentActivity";
import {CopyValue,EmptyState,Feedback,PaymentStatus,RiskDecisionCard,StatusBadge,TechnicalDetails,ExplorerLink} from "./components/ui";
import {usePaymentActivity} from "./hooks/usePaymentActivity";
import {nearDisplay,lifecycle} from "./presentation";
import {signContractCall,MANUAL_TESTING,type NearContractCall} from "./services/nearTransactions";
import {useWalletAuth} from "./hooks/useWalletAuth";
import {Activity,ArrowRight,ArrowUpRight,Check,ChevronRight,Fingerprint,Gauge,LayoutDashboard,LockKeyhole,Menu,RefreshCw,Shield,ShieldAlert,ShieldCheck,Sparkles,Wallet,X} from "lucide-react";
import {delegatedApi,sentinelApi,type ContractConfig,type SpendingPolicy,type RiskAssessment,type Payment,type PreparedPayment} from "./api";
import "./App.css";

function nearToYocto(value: string): string {
  const v = value.trim();
  if (!/^\d+(\.\d{1,24})?$/.test(v))
    throw new Error("Enter a valid NEAR amount (up to 24 decimal places).");
  const [whole, fraction = ""] = v.split(".");
  const result = BigInt(whole) * 10n ** 24n + BigInt(fraction.padEnd(24, "0"));
  if (result <= 0n) throw new Error("Amount must be greater than zero.");
  return result.toString();
}

type NavId = "overview" | "assessment" | "payments" | "policies" | "agent";
const navigation: { id: NavId; label: string; Icon: typeof LayoutDashboard }[] =
  [
    { id: "overview", label: "Overview", Icon: LayoutDashboard },
    { id: "assessment", label: "Risk assessment", Icon: ShieldAlert },
    { id: "agent", label: "AI payments", Icon: Sparkles },
    { id: "payments", label: "Transactions", Icon: ArrowUpRight },
    { id: "policies", label: "Security & delegations", Icon: LockKeyhole },
  ];

function MetricCard({
  label,
  value,
  detail,
  Icon,
  accent = "blue",
  loading = false,
}: {
  label: string;
  value: string;
  detail: string;
  Icon: typeof Wallet;
  accent?: "blue" | "green" | "purple" | "amber";
  loading?: boolean;
}) {
  return (
    <div className="metric-card">
      <div className="metric-head">
        <span>{label}</span>
        <span className={`metric-icon ${accent}`}>
          <Icon size={19} strokeWidth={1.9} />
        </span>
      </div>
      <div className="metric-value">
        {loading ? <span className="skeleton width-medium" /> : value}
      </div>
      <div className="metric-detail">
        <span className={`tiny-dot ${accent}`} />
        {detail}
      </div>
    </div>
  );
}

export default function App({initialPage="overview"}:{initialPage?:NavId}={}) {
  const [revision,setRevision]=useState(0);
  const [policyTab,setPolicyTab]=useState<"global"|"delegations">("global");
  const [selectedDelegation,setSelectedDelegation]=useState("");
  const [paymentMode,setPaymentMode]=useState<"owner"|"delegated">("owner");
  const [authorizationHash, setAuthorizationHash] = useState("");
  const [executing, setExecuting] = useState(false);
  const [authorizing, setAuthorizing] = useState(false);
  const [currentNav, setCurrentNav] = useState<NavId>(initialPage);
  const [menuOpen, setMenuOpen] = useState(false);
  const [mobile,setMobile]=useState(false);
  const [,setDisplayClock]=useState(Date.now);
  useEffect(()=>{const tick=setInterval(()=>setDisplayClock(Date.now()),15000);return()=>clearInterval(tick);},[]);
  useEffect(()=>{
    const media=window.matchMedia('(max-width: 900px)');const update=()=>setMobile(media.matches);
    update();media.addEventListener('change',update);return()=>media.removeEventListener('change',update);
  },[]);
  useEffect(()=>{if(mobile&&menuOpen)requestAnimationFrame(()=>document.querySelector<HTMLButtonElement>('.sidebar .nav-item.active')?.focus());},[mobile,menuOpen]);
  useEffect(()=>{
    const sync=()=>{const page=window.location.hash.slice(1);if(navigation.some(item=>item.id===page))setCurrentNav(page as NavId);};
    sync();window.addEventListener('popstate',sync);window.addEventListener('hashchange',sync);
    return()=>{window.removeEventListener('popstate',sync);window.removeEventListener('hashchange',sync);};
  },[]);
  const {
    accountId,
    token,
    loading: walletLoading,
    error: walletError,
    connect,
    authenticate,
    disconnect,
  } = useWalletAuth();
  const observed=usePaymentActivity(token,revision);
  const [config, setConfig] = useState<ContractConfig | null>(null);
  const [policy, setPolicy] = useState<SpendingPolicy | null>(null);
  const [balance, setBalance] = useState("0");
  const [recipient, setRecipient] = useState("");
  const [amount, setAmount] = useState("0.1");
  const [purpose, setPurpose] = useState("");
  const [risk, setRisk] = useState<RiskAssessment | null>(null);
  const [assessedFingerprint, setAssessedFingerprint] = useState("");
  const [prepared, setPrepared] = useState<PreparedPayment | null>(null);
  const [paymentId, setPaymentId] = useState("");
  const [payment, setPayment] = useState<Payment | null>(null);
  const [error, setErrorState] = useState("");
  const setError=useCallback((message:string)=>{setErrorState(message);if(message)notifyError(message);},[]);
  const [notice, setNotice] = useState("");
  const [sessionExpired,setSessionExpired]=useState(false);
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [lastSync, setLastSync] = useState<Date | null>(null);

  const walletPrevious=useRef({account:accountId,token,error:walletError});
  useEffect(()=>{
    const previous=walletPrevious.current;
    if(accountId!==previous.account){dismissNotification('session-expired');dismissNotification('wallet-auth-error');dismissNotification('wallet-verified');if(previous.account)dismissNotification(`wallet-connected:${previous.account}`);}
    if(accountId&&accountId!==previous.account)notify({variant:'info',title:'Wallet connected.',description:'Sign in to verify access to this workspace.',key:`wallet-connected:${accountId}`});
    if(!accountId&&previous.account)notify({variant:'info',title:'Wallet session closed.',description:'Your existing payment records have not changed.',key:'wallet-disconnected'});
    if(token&&token!==previous.token){dismissNotification('session-expired');dismissNotification('wallet-auth-error');if(accountId)dismissNotification(`wallet-connected:${accountId}`);notify({variant:'success',title:'Wallet verified successfully.',key:'wallet-verified'});}
    if(walletError&&walletError!==previous.error)notifyError(walletError,'wallet-auth-error');
    walletPrevious.current={account:accountId,token,error:walletError};
  },[accountId,token,walletError]);
  useEffect(()=>{
    setSessionExpired(false);
    if(!token)return;
    let timer:ReturnType<typeof setTimeout>|undefined;
    try {
      const data=JSON.parse(atob(token.split('.')[1].replace(/-/g,'+').replace(/_/g,'/'))) as {exp?:number};
      if(typeof data.exp==='number'&&Number.isFinite(data.exp)){
        const expires=data.exp*1000;
        const check=()=>{const remaining=expires-Date.now();if(remaining>0){timer=setTimeout(check,Math.min(remaining,2147483647));return;}setSessionExpired(true);setErrorState('Your sign-in session has expired. Sign in again to continue; existing payments remain unchanged.');notify({variant:'warning',title:'Your session has expired.',description:'Sign in again before requesting another action.',key:'session-expired',duration:0});};check();
      }
    }catch{/* A display timer never validates, refreshes or grants authentication. */}
    return()=>{if(timer)clearTimeout(timer);};
  },[token]);
  const refresh = useCallback(async (announce=false) => {
    setError("");
    setLoading(true);
    try {
      const [cfg, bal] = await Promise.all([
        sentinelApi.getConfig(),
        sentinelApi.getBalance(),
      ]);
      setConfig(cfg);
      setBalance(bal.availableBalanceYocto);
      if (token) {
        setPolicy(await sentinelApi.getPolicy(token));
      } else {
        setPolicy(null);
      }
      setLastSync(new Date());
      if(announce)notify({variant:'success',title:'Contract data refreshed.'});
    } catch (e) {
      setErrorState((e as Error).message);
      if(announce)notifyError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, [token]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  useEffect(() => {
    if (!token) {
      setPolicy(null);
      setRisk(null);
      setPrepared(null);
      setAuthorizationHash("");
      setPayment(null);
    }
  }, [token]);

  // Automatically dismiss success notifications.
  useEffect(() => {
    if (!notice) return;
    if(/pending|unconfirmed|needs verification|submitted.*verify/i.test(notice))return;

    const timeout = window.setTimeout(() => {
      setNotice("");
    }, 4500);

    return () => window.clearTimeout(timeout);
  }, [notice]);

  const fingerprint = `${recipient}\n${amount}\n${purpose}`;
  const freshRisk = risk !== null && assessedFingerprint === fingerprint;
  const assessmentAllows = freshRisk && risk.decision === "ALLOW";
  const pct = useMemo(() => {
    if (
      !policy ||
      !/^\d+$/.test(policy.maxPerTransactionYocto) ||
      !/^\d+$/.test(policy.dailyLimitYocto)
    )
      return 0;
    const total = BigInt(policy.dailyLimitYocto);
    return total === 0n
      ? 0
      : Number((BigInt(policy.maxPerTransactionYocto) * 100n) / total);
  }, [policy]);

  function navigate(id: NavId) {
    setCurrentNav(id);
    setMenuOpen(false);
    if(window.location.hash!==`#${id}`)window.history.pushState(null,"",`#${id}`);
    window.scrollTo({top:0,behavior:"instant"});
    requestAnimationFrame(()=>document.getElementById("page-content")?.focus({preventScroll:true}));
  }

  function resetAssessment() {
    dismissNotification('risk-assessment');
    setRisk(null);
    setPrepared(null);
    setAuthorizationHash("");
    setNotice("");
  }

  async function assess() {
    dismissNotification('risk-assessment');
    setBusy(true);
    setError("");
    setNotice("");
    setRisk(null);
    setPrepared(null);
    try {
      if (!token)
        throw new Error("Connect and authenticate your NEAR wallet first.");
      if (!purpose.trim()) throw new Error("Enter a transaction purpose.");
      const data = {
        recipient: recipient.trim(),
        amountYocto: nearToYocto(amount),
        purpose: purpose.trim(),
      };
      const result = await sentinelApi.assessRisk(token, data);
      setRisk(result);
      setAssessedFingerprint(fingerprint);
      setNotice("Assessment completed against current on-chain policy.");
      notifyDecision(result.decision,'risk-assessment');
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  async function prepare() {
    setBusy(true);
    setError("");
    setNotice("");
    try {
      if (!token) throw new Error("Owner authentication is required.");
      if (!assessmentAllows)
        throw new Error(
          "Assess this exact payment and obtain an ALLOW decision first.",
        );
      // Temporary numeric expiry to match current ASP.NET API model. Replace with decimal string in wallet phase.
      const expiresAtNs = (
        BigInt(Date.now() + 60 * 60 * 1000) * 1_000_000n
      ).toString();
      const result = await sentinelApi.preparePayment(
        token.trim(),
        {
          recipient: recipient.trim(),
          amountYocto: nearToYocto(amount),
          purpose: purpose.trim(),
          expiresAtNs,
        },
        crypto.randomUUID(),
      );
      setPrepared(result);
      setPaymentId(result.paymentId);
      setAuthorizationHash("");
      setNotice(
        "Authorization prepared. Sign with your owner wallet to authorize it.",
      );
      notify({variant:'info',title:'Owner authorization prepared.',description:'A separate wallet signature is required. No payment has been executed.'});
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  async function authorizePayment() {
    if (!prepared || !accountId || !token) {
      setError("Authenticate your owner wallet first.");
      return;
    }

    setAuthorizing(true);
    setError("");
    setNotice("");

    const currentPayment = prepared;

    try {
      // Recheck the current input so an old assessment
      // cannot accidentally authorize a different payment.
      if (!assessmentAllows) {
        throw new Error("Reassess the transaction before authorization.");
      }

      const call = currentPayment.transaction as NearContractCall;

      if (call.methodName !== "authorize_payment") {
        throw new Error("Unexpected payment method.");
      }

      if (call.args.id !== currentPayment.paymentId) {
        throw new Error("Payment ID mismatch.");
      }

      if (call.args.recipient !== recipient.trim()) {
        throw new Error("Recipient changed after preparation.");
      }

      if (call.args.amount_yocto !== nearToYocto(amount)) {
        throw new Error("Amount changed after preparation.");
      }

      const result = await signContractCall(call, accountId);

      if (result.pendingConfirmation) {
        setNotice(
          "Wallet confirmation is pending. Check the wallet and NEAR Explorer.",
        );
        notify({variant:'warning',title:'Wallet confirmation is pending.',description:'Check the existing authorization before requesting another signature.',duration:0,key:`authorization-pending:${currentPayment.paymentId}`});
        return;
      }

      if (!result.hash) {
        throw new Error("Authorization completed without a transaction hash.");
      }

      setAuthorizationHash(result.hash);

      let reportingPending=false;
      try {
        await sentinelApi.reportPaymentTransaction(
          token,
          currentPayment.paymentId,
          result.hash,
        );
      } catch (reportError) {
        reportingPending=true;
        console.error("Transaction reporting failed", reportError);

        setError("Authorization submitted, but reporting failed. Verify the existing payment record before taking another action.");
      }

      try {
        await sentinelApi.syncPayment(token, currentPayment.paymentId);
      } catch (syncError) {
        reportingPending=true;
        console.error("Reconciliation failed", syncError);
        setError("Authorization submitted; verification is unconfirmed. Check the existing payment record before requesting another signature.");
      }

      setPayment(await sentinelApi.getPayment(currentPayment.paymentId));

      if(!reportingPending){setNotice("Owner authorization recorded. Payment execution is separate.");notify({variant:'info',title:'Owner authorization recorded.',description:'This is not payment settlement.',key:`authorization:${currentPayment.paymentId}`});}
    } catch (error) {
      setError((error as Error).message);
    } finally {
      setAuthorizing(false);
    }
  }

  async function executePayment() {
    if (!token || !paymentId.trim()) {
      setError("Authenticate and select a payment.");
      return;
    }

    setExecuting(true);
    setError("");
    setNotice("");

    try {
      const id = paymentId.trim();

      // Ask backend to refresh its on-chain status first.
      await sentinelApi.syncPayment(token, id);

      const latest = await sentinelApi.getPayment(id);
      setPayment(latest);

      if (latest.status !== "Authorized") {
        throw new Error(
          `Payment must be Authorized. Current status: ${latest.status}`,
        );
      }

      const result = await sentinelApi.dispatchPayment(token, id);

      setNotice("Execution requested. Final payment status still needs verification.");
      notify({variant:'info',title:'Execution request accepted.',description:'This response does not prove transaction submission or settlement.',details:result.message,key:`dispatch:${id}`,paymentId:id});

      // Reconciliation is independent of transaction submission.
      await sentinelApi.syncPayment(token, id);

      const reconciled=await sentinelApi.getPayment(id);setPayment(reconciled);
      if(reconciled.status==='Completed'||reconciled.status==='Failed')setNotice('');
      notifyReconciliation({paymentId:id,status:reconciled.status==='Completed'?'Settled':reconciled.status==='Executing'?'Reserved':reconciled.status,chainObserved:true});
    } catch (error) {
      setError((error as Error).message);
    } finally {
      setExecuting(false);
    }
  }

  async function lookupPayment() {
    setBusy(true);
    setError("");
    setNotice("");
    try {
      setPayment(await sentinelApi.getPayment(paymentId.trim()));notify({variant:"info",title:"Payment record loaded.",description:"Review its current network status below."});
    } catch (e) {
      setError((e as Error).message);
      setPayment(null);
    } finally {
      setBusy(false);
    }
  }

  const pageTitle=navigation.find(item=>item.id===currentNav)?.label??"Overview";
  const subtitles:Record<NavId,string>={overview:"Your AI payments, protected and under control.",assessment:"Understand a payment's risk before taking the next step.",agent:"Give your agent a task. Keep control of how it pays.",payments:"Follow each payment from proposal to verified settlement.",policies:"Set the boundaries your agent must work within."};
  const pending=observed.payments.filter(p=>lifecycle(p.status,p.decision,JSON.parse(p.intentJson).ExpiresAtNs,Date.now(),!!p.transactionHash,p.approvalExpiresAtUtc).label==="Needs review");
  const latest=observed.payments[0];
  const activeView=observed.views.find(v=>v.id===selectedDelegation);
  const refreshActivity=()=>{setRevision(value=>value+1);void refresh();};
  return <div className="app-shell">
    <a className="skip-link" href="#page-content">Skip to content</a>
    <aside className={`sidebar ${menuOpen?"open":""}`} inert={mobile&&!menuOpen} role={mobile?"dialog":undefined} aria-modal={mobile&&menuOpen?true:undefined} aria-label="Workspace navigation" onKeyDown={event=>{if(!mobile||!menuOpen)return;if(event.key==="Escape"){setMenuOpen(false);requestAnimationFrame(()=>document.getElementById("navigation-toggle")?.focus());}if(event.key==="Tab"){const items=event.currentTarget.querySelectorAll<HTMLButtonElement>('button');const first=items[0];const last=items[items.length-1];if(event.shiftKey&&document.activeElement===first){event.preventDefault();last?.focus();}else if(!event.shiftKey&&document.activeElement===last){event.preventDefault();first?.focus();}}}}>
      <button className="icon-button mobile-menu mobile-close" aria-label="Close navigation" onClick={()=>{setMenuOpen(false);requestAnimationFrame(()=>document.getElementById("navigation-toggle")?.focus());}}><X size={18}/></button><div className="logo"><div className="logo-symbol"><ShieldCheck size={25}/></div><div><span className="logo-name">sentinel<span className="logo-dot">.</span></span><span className="logo-caption">AI PAYMENT SECURITY</span></div></div>
      <div className="sidebar-label">WORKSPACE</div>
      <nav className="side-nav" aria-label="Main navigation">{navigation.map(({id,label,Icon})=><button key={id} className={`nav-item ${currentNav===id?"active":""}`} aria-current={currentNav===id?"page":undefined} onClick={()=>navigate(id)}><Icon size={18} strokeWidth={1.8}/><span>{label}</span></button>)}</nav>
      <div className="sidebar-bottom"><div className="sidebar-note"><Shield size={17}/><p>Owner-defined rules.<br/>Enforced on NEAR.</p></div><span className="sidebar-footer">Sentinel · Testnet workspace</span></div>
    </aside>
    {menuOpen&&<button className="mobile-scrim" aria-label="Close navigation" onClick={()=>setMenuOpen(false)}/>}
    <div className="workspace" inert={mobile&&menuOpen}>
      <header className="topbar"><div className="top-left"><button id="navigation-toggle" className="icon-button mobile-menu" aria-label={menuOpen?"Close navigation":"Open navigation"} aria-expanded={menuOpen} onClick={()=>setMenuOpen(!menuOpen)}>{menuOpen?<X size={19}/>:<Menu size={19}/>}</button><span className="breadcrumb">Workspace</span><ChevronRight size={14}/><span className="breadcrumb-current">{pageTitle}</span></div>
        <div className="top-right"><span className="environment-pill">NEAR Testnet</span><div className="top-wallet">{!accountId?<button className="primary-button" onClick={()=>void connect()} disabled={walletLoading}><Wallet size={15}/>{walletLoading?"Connecting…":"Connect wallet"}</button>:<><span className="wallet-account" title={accountId}>{accountId.length>26?`${accountId.slice(0,12)}…${accountId.slice(-9)}`:accountId}</span>{!token||sessionExpired?<button className="primary-button" disabled={walletLoading} onClick={()=>void authenticate()}><Fingerprint size={15}/>{walletLoading?"Signing in…":"Sign in"}</button>:<span className="auth-status"><Check size={13}/>Signed in</span>}<button className="text-button" onClick={()=>void disconnect().catch(error=>setError(error instanceof Error?error.message:"Wallet disconnect could not be confirmed."))}>Disconnect</button></>}</div></div>
      </header>
      <main className="content" id="page-content" tabIndex={-1}>
        <div className="page-intro"><div><span className="eyebrow">SENTINEL WORKSPACE</span><h1>{pageTitle}</h1><p>{subtitles[currentNav]}</p></div><button className="outline-button sync-button" disabled={loading} onClick={()=>{void refresh(true);setRevision(value=>value+1);}}><RefreshCw size={15} className={loading?"spin":""}/>{loading?"Refreshing…":"Refresh"}</button></div>
        {(error||walletError)&&<Feedback message={error||walletError||""} error onDismiss={error?()=>setError(""):undefined}/>}
        {notice&&/pending|unconfirmed|needs verification|submitted.*verify/i.test(notice)&&<Feedback message={notice} onDismiss={()=>setNotice("")}/>}
        {MANUAL_TESTING&&<div className="feedback">Inspection mode: wallet transactions and payment dispatch are disabled.</div>}

        <div hidden={currentNav!=="overview"} className="page-view" aria-label="Overview">
          <div className="overview-intro"><div><span className="section-kicker">ON-CHAIN SPENDING CONTROL</span><h2>AI payments. Your rules.</h2><p>Agents propose and pay within your boundaries. Sentinel checks the risk, enforces spending limits and keeps the evidence.</p></div><button className="primary-button" onClick={()=>navigate("agent")}>Open AI payments<ArrowRight size={15}/></button></div>
          <section className="metrics" aria-label="Observed security metrics">
            <MetricCard label="Available treasury" value={config?`${nearDisplay(balance)} NEAR`:"Unavailable"} detail="Available contract funds" Icon={Wallet} loading={loading}/>
            <MetricCard label="Contract protection" value={config?(config.paused?"Paused":"Active"):"Unavailable"} detail={config?(config.paused?"Execution paused by owner":"Spending rules enforced on chain"):"Refresh to check network status"} Icon={ShieldCheck} accent={config?.paused?"amber":"green"} loading={loading}/>
            <MetricCard label="Maximum payment" value={policy?`${nearDisplay(policy.maxPerTransactionYocto)} NEAR`:"Sign in to view"} detail="Global per-payment limit" Icon={Gauge}/>
            <MetricCard label="Latest observed payment" value={latest?lifecycle(latest.status,latest.decision,JSON.parse(latest.intentJson).ExpiresAtNs).label:"No activity loaded"} detail="From loaded delegation records" Icon={Activity} loading={observed.loading}/>
          </section>
          <div className="overview-grid"><section className="panel"><div className="panel-heading"><div><h2>Recent payment activity</h2><p>Risk decisions and payment outcomes, kept separate.</p></div><button className="text-button" onClick={()=>navigate("payments")}>View activity<ArrowUpRight size={14}/></button></div>{observed.error&&<Feedback message={observed.error}/>}<PaymentActivity payments={observed.payments} compact onOpenAgent={()=>navigate("agent")}/></section>
          <div className="summary-stack"><section className="panel"><span className="section-kicker">NEEDS ATTENTION</span><h2>{pending.length?`${pending.length} observed payment${pending.length===1?"":"s"} need review`:observed.loading?"Checking activity…":!token||observed.error?"Activity not fully available":"No pending reviews observed"}</h2><p>{pending.length?"Review the security reasons before approving an assessment.":"Reviews appear here from the delegation records available to your wallet."}</p><button className="outline-button" onClick={()=>navigate("agent")}>Review AI payments</button></section>
          <section className="panel"><h2>Spending setup</h2><dl className="summary-list"><div><dt>Current mode</dt><dd>{paymentMode==="owner"?"Owner approved":"Autonomous"}</dd></div><div><dt>Daily policy limit</dt><dd>{policy?`${nearDisplay(policy.dailyLimitYocto)} NEAR`:"Sign in to view"}</dd></div><div><dt>Selected delegation</dt><dd>{paymentMode==="delegated"?(activeView?.id??"None selected"):"Not used in this mode"}</dd></div>{paymentMode==="delegated"&&activeView?.state&&<><div><dt>Remaining attempt budget</dt><dd>{nearDisplay(activeView.remainingBudget)} NEAR</dd></div><div><dt>Expires</dt><dd>{new Date(Number(BigInt(activeView.state.expiresAtNs)/1000000n)).toLocaleString()}</dd></div></>}</dl><button className="text-button" onClick={()=>navigate("policies")}>Manage security & delegations<ArrowRight size={14}/></button></section></div></div>
          <div className="quick-links"><button onClick={()=>navigate("assessment")}><ShieldAlert size={18}/><span><strong>Check a payment</strong><small>Assess risk before authorization</small></span><ArrowRight size={16}/></button><button onClick={()=>navigate("payments")}><Activity size={18}/><span><strong>Verify an outcome</strong><small>Inspect payment and network evidence</small></span><ArrowRight size={16}/></button></div>
        </div>

        <div hidden={currentNav!=="agent"} className="page-view" aria-label="AI payments">
          <div className="mode-switch"><div><strong>Payment mode</strong><p>{paymentMode==="owner"?"Every payment requires separate owner authorization.":"An active delegation permits execution within owner-defined limits."}</p></div><label><span className="sr-only">Payment mode</span><select value={paymentMode} onChange={e=>setPaymentMode(e.target.value as "owner"|"delegated")}><option value="owner">Owner approved</option><option value="delegated" disabled={MANUAL_TESTING}>Autonomous payments</option></select></label></div>
          <div hidden={paymentMode!=="owner"}><AgentTaskPanel token={token} isOwner={Boolean(token&&config&&accountId===config.owner)}/></div>
          <div hidden={paymentMode!=="delegated"}><DelegationManager token={token} accountId={accountId} agent={config?.agent??""} isOwner={Boolean(token&&config&&accountId===config.owner)} section="payments" selectedId={selectedDelegation} onSelectId={setSelectedDelegation} onUpdated={refreshActivity}/></div>
        </div>

        <div hidden={currentNav!=="assessment"} className="page-view assessment-layout" aria-label="Risk assessment"><section className="panel risk-panel"><div className="panel-heading"><div><h2>Assess a payment</h2><p>A read-only security assessment. No funds will move.</p></div></div><div className="field-grid"><label className="field">Recipient<input value={recipient} onChange={e=>{setRecipient(e.target.value);resetAssessment();}} placeholder="recipient.testnet" autoComplete="off"/></label><label className="field">Amount (NEAR)<input value={amount} inputMode="decimal" onChange={e=>{setAmount(e.target.value);resetAssessment();}} placeholder="0.001"/></label></div><label className="field">Payment purpose<textarea rows={3} value={purpose} onChange={e=>{setPurpose(e.target.value);resetAssessment();}} placeholder="What is this payment for?"/></label><div className="action-row"><button className="primary-button" disabled={busy} onClick={()=>void assess()}>{busy?"Assessing…":"Assess payment"}</button><button className="outline-button" disabled={busy||!assessmentAllows||MANUAL_TESTING} onClick={()=>void prepare()}>Prepare owner authorization</button></div>{!token&&<p className="helper-text">Connect your wallet and sign in to assess a payment.</p>}
          {risk&&freshRisk&&<RiskDecisionCard decision={risk.decision} reasons={risk.reasons}><p className="next-action">{risk.decision==="ALLOW"?"Next: prepare the payment for owner authorization. ALLOW does not authorize or execute it.":risk.decision==="WARN"?"Next: open AI payments and review the corresponding proposal. This assessment screen cannot approve WARN decisions.":"This payment cannot proceed. Review the reasons and your policy."}</p><TechnicalDetails label="Assessment context"><p>Risk score: {risk.riskScore}/100</p>{risk.aiExplanation&&<p>{risk.aiExplanation}</p>}</TechnicalDetails></RiskDecisionCard>}
          {prepared&&<section className="prepared-result"><h3>Ready for owner authorization</h3><p>A wallet signature authorizes this intent. It does not prove that the recipient has been paid.</p><CopyValue value={prepared.paymentId}/>{!authorizationHash?<button className="primary-button" disabled={authorizing||!accountId||!token||MANUAL_TESTING} onClick={()=>void authorizePayment()}>{authorizing?"Waiting for wallet…":"Authorize in wallet"}</button>:<><StatusBadge tone="blue">Authorization submitted</StatusBadge><ExplorerLink hash={authorizationHash}/></>}<TechnicalDetails><pre>{JSON.stringify(prepared.transaction,null,2)}</pre></TechnicalDetails></section>}
        </section><aside className="panel context-panel"><span className="section-kicker">HOW SENTINEL DECIDES</span><h2>One payment. Independent checks.</h2><p>Sentinel evaluates the recipient, available funds and your spending rules.</p><dl className="decision-guide"><div><StatusBadge tone="green">ALLOW</StatusBadge><p>Meets the current rules. Authorization or delegated authority is still required.</p></div><div><StatusBadge tone="amber">WARN</StatusBadge><p>Needs authenticated owner review and revalidation.</p></div><div><StatusBadge tone="red">BLOCK</StatusBadge><p>Cannot proceed. A warning approval cannot override a block.</p></div></dl></aside></div>

        <div hidden={currentNav!=="policies"} className="page-view" aria-label="Security and delegations"><div className="tab-list" role="tablist" aria-label="Security settings" onKeyDown={event=>{if(!['ArrowLeft','ArrowRight','Home','End'].includes(event.key))return;event.preventDefault();setPolicyTab(event.key==='Home'?'global':event.key==='End'?'delegations':policyTab==='global'?'delegations':'global');requestAnimationFrame(()=>document.querySelector<HTMLButtonElement>('.tab-list [aria-selected="true"]')?.focus());}}><button id="policy-tab-global" aria-controls="policy-panel-global" role="tab" tabIndex={policyTab==="global"?0:-1} aria-selected={policyTab==="global"} onClick={()=>setPolicyTab("global")}>Global policies</button><button id="policy-tab-delegations" aria-controls="policy-panel-delegations" role="tab" tabIndex={policyTab==="delegations"?0:-1} aria-selected={policyTab==="delegations"} onClick={()=>setPolicyTab("delegations")}>Spending delegations</button></div>
          <section hidden={policyTab!=="global"} id="policy-panel-global" aria-labelledby="policy-tab-global" className="panel policy-panel" role="tabpanel" aria-label="Global policies"><div className="panel-heading"><div><h2>Global security policies</h2><p>Every payment remains subject to these contract rules.</p></div><StatusBadge tone={config?(config.paused?"amber":"green"):"neutral"}>{config?(config.paused?"Paused":"Protection active"):"Status unavailable"}</StatusBadge></div><div className="policy-summary-grid"><div><span>Maximum payment</span><strong>{policy?nearDisplay(policy.maxPerTransactionYocto):"—"}<small> NEAR</small></strong></div><div><span>Daily limit</span><strong>{policy?nearDisplay(policy.dailyLimitYocto):"—"}<small> NEAR</small></strong></div></div>{policy&&<div className="policy-visual"><span>Per-payment limit as a share of daily allowance</span><div className="policy-bar"><div style={{width:`${Math.min(100,pct)}%`}}/></div></div>}<p className="helper-text">Changes require an owner wallet transaction. Verify the values in your wallet before signing.</p><PolicyManager currentMaximum={policy?nearDisplay(policy.maxPerTransactionYocto):undefined} currentDaily={policy?nearDisplay(policy.dailyLimitYocto):undefined} accountId={accountId} authenticated={!!token} paused={config?.paused??false} onUpdated={refresh} onNotify={(type,message)=>{if(type==="error"){setNotice("");setError(message);}else{setError("");setNotice(message);notify({variant:/pending|unconfirmed/i.test(message)?'warning':type==='success'?'success':'info',title:message,duration:/pending|unconfirmed/i.test(message)?0:undefined});}}}/></section>
          <div hidden={policyTab!=="delegations"} id="policy-panel-delegations" aria-labelledby="policy-tab-delegations" role="tabpanel" aria-label="Spending delegations"><DelegationManager token={token} accountId={accountId} agent={config?.agent??""} isOwner={Boolean(token&&config&&accountId===config.owner)} section="management" selectedId={selectedDelegation} onSelectId={setSelectedDelegation} onUpdated={refreshActivity}/></div>
        </div>

        <div hidden={currentNav!=="payments"} className="page-view" aria-label="Transactions"><section className="panel"><div className="panel-heading"><div><h2>Payment activity</h2><p>Observed delegation records. A transaction report alone is not settlement evidence.</p></div>{observed.loading&&<span className="helper-text">Loading…</span>}</div>{observed.error&&<Feedback message={observed.error}/>}<PaymentActivity payments={observed.payments} onOpenAgent={()=>navigate("agent")} onVerify={token?async id=>{const result=await delegatedApi.reconcile(token,id);notifyReconciliation(result as {status?:string;chainObserved?:boolean;paymentId?:string});refreshActivity();}:undefined}/><p className="table-caption">Showing up to 200 matching records from 20 loaded delegations. Search covers loaded records, not your full account history.</p></section>
          <section className="panel transactions-panel"><div className="panel-heading"><div><h2>Look up a payment</h2><p>Check any known Sentinel payment ID, including owner-approved payments.</p></div></div><label className="field"><span className="sr-only">Payment ID</span><div className="payment-search"><input placeholder="Enter payment ID" value={paymentId} onChange={e=>setPaymentId(e.target.value)} onKeyDown={e=>{if(e.key==="Enter"&&paymentId.trim())void lookupPayment();}}/><button className="primary-button" disabled={busy||!paymentId.trim()} onClick={()=>void lookupPayment()}>{busy?"Checking…":"Check payment"}</button></div></label>{payment?<div className="payment-result"><div className="panel-heading"><div><h3>{nearDisplay(payment.amountYocto)} NEAR</h3><p>To {payment.recipient}</p></div><PaymentStatus status={payment.status} expiry={payment.expiresAtNs}/></div><CopyValue value={payment.id}/><p className="helper-text">Status read from the contract. Authorized is not settled; Executing is pending confirmation.</p>{payment.status==="Authorized"&&<button className="primary-button" disabled={executing||!token||MANUAL_TESTING} onClick={()=>void executePayment()}>{executing?"Requesting execution…":"Request agent execution"}</button>}{!token&&<p className="helper-text">Sign in to request execution. A payment lookup does not sign a transaction.</p>}</div>:<EmptyState title="Check the network record">Paste a payment ID to see its current contract status.</EmptyState>}</section>
        </div>
        <footer className="page-footer"><span>{lastSync?`Contract data refreshed ${lastSync.toLocaleTimeString([], {hour:"2-digit",minute:"2-digit"})}`:"Contract data not yet available"}</span><span>Policy checks → Owner authority → Restricted signing → Network verification</span></footer>
      </main>
    </div>
  </div>;
}
