import {useState,useEffect,useRef,type ReactNode} from "react";
import {Check,Copy,ChevronRight,ExternalLink,X,Info,TriangleAlert,CircleAlert} from "lucide-react";
import {notify,notifyError,type ToastVariant} from "../notifications";
import {friendlyError,humanReason,lifecycle,type Tone} from "../presentation";

export function StatusBadge({children,tone="neutral"}:{children:ReactNode;tone?:Tone}){return <span className={`status-badge tone-${tone}`}>{children}</span>;}
export function PaymentStatus({status,decision,expiry,reported=false,approvalExpiry}:{status:string;decision?:string;expiry?:string;reported?:boolean;approvalExpiry?:string|null}){
 const value=lifecycle(status,decision,expiry,Date.now(),reported,approvalExpiry);return <StatusBadge tone={value.tone}>{value.label}</StatusBadge>;
}
export function TechnicalDetails({children,label="Technical details"}:{children:ReactNode;label?:string}){return <details className="technical-details"><summary>{label}<ChevronRight size={14}/></summary><div>{children}</div></details>;}
export function Feedback({message,error=false,onDismiss,description,variant,technical,action}:{message:string;error?:boolean;onDismiss?:()=>void;description?:string;variant?:ToastVariant;technical?:string;action?:ReactNode}){
 const tone=variant??(error?'error':'info');const Icon=tone==='success'?Check:tone==='warning'?TriangleAlert:tone==='error'?CircleAlert:Info;
 const details=technical??(friendlyError(message)!==message?message:undefined);
 return <div className={`feedback feedback-${tone}`} role={tone==='error'?'alert':'status'} aria-atomic="true"><div className="feedback-heading"><Icon size={17} aria-hidden="true"/><div><p>{friendlyError(message)}</p>{description&&<small>{description}</small>}</div>{onDismiss&&<button className="icon-button" aria-label="Dismiss notification" onClick={onDismiss}><X size={14}/></button>}</div>{action&&<div className="notification-action">{action}</div>}{details&&<TechnicalDetails><pre>{details}</pre></TechnicalDetails>}</div>;
}
export function CopyValue({value,label="Copy ID"}:{value:string;label?:string}){
 const [copied,setCopied]=useState(false);const [failed,setFailed]=useState(false);
 async function copy(){try{await navigator.clipboard.writeText(value);setCopied(true);setFailed(false);notify({variant:'success',title:'Copied to clipboard.'});setTimeout(()=>setCopied(false),2000);}catch{setFailed(true);notifyError('Clipboard access was denied. Select and copy the value manually.');}}
 return <span className="copy-value"><code title={value}>{value}</code><button type="button" className="icon-button" onClick={()=>void copy()} title={label} aria-label={copied?"Copied":label}>{copied?<Check size={14}/>:<Copy size={14}/>}</button>{(copied||failed)&&<span className="copy-feedback">{failed?"Select and copy the value manually.":"Copied"}</span>}</span>;
}
export function EmptyState({title,children,action}:{title:string;children:ReactNode;action?:ReactNode}){return <div className="empty-state"><strong>{title}</strong><p>{children}</p>{action}</div>;}
export function Decision({decision}:{decision:string}){return <StatusBadge tone={decision==="ALLOW"?"green":decision==="WARN"?"amber":decision==="BLOCK"?"red":"neutral"}>{decision}</StatusBadge>;}
export function RiskDecisionCard({decision,reasons,children}:{decision:string;reasons:string[];children?:ReactNode}){
 return <div className={`decision-card decision-${decision.toLowerCase()}`}><div className="panel-heading"><h3>{decision==="ALLOW"?"Payment meets your security rules.":decision==="WARN"?"This payment needs your review.":"Payment blocked by your security rules."}</h3><Decision decision={decision}/></div><ul className="reason-list">{Array.from(new Set(reasons.map(humanReason))).map(reason=><li key={reason}>{reason}</li>)}</ul>{children}<TechnicalDetails><ul>{reasons.map(code=><li key={code}><code>{code}</code></li>)}</ul></TechnicalDetails></div>;
}
export function ExplorerLink({hash}:{hash:string}){return <a className="text-link" href={`https://testnet.nearblocks.io/txns/${encodeURIComponent(hash)}`} target="_blank" rel="noopener noreferrer">View transaction <ExternalLink size={13}/></a>;}
export function Steps({autonomous=false}:{autonomous?:boolean}){return <ol className="workflow-steps" aria-label="Payment workflow">{["Describe payment","Review decision",autonomous?"Permitted execution":"Owner authorization","Verify result"].map((text,index)=><li key={text}><span>{index+1}</span>{text}</li>)}</ol>;}
export function ConfirmationDialog({open,title,children,onClose,onConfirm,busy=false}:{open:boolean;title:string;children:ReactNode;onClose:()=>void;onConfirm:()=>void;busy?:boolean}){
 const dialog=useRef<HTMLDialogElement>(null);
 useEffect(()=>{const node=dialog.current;if(!node)return;if(open&&!node.open)node.showModal();else if(!open&&node.open)node.close();},[open]);
 return <dialog ref={dialog} className="confirmation-dialog" aria-label={title} onCancel={event=>{event.preventDefault();if(!busy)onClose();}}><h2>{title}</h2><div>{children}</div><div className="action-row"><button className="outline-button" disabled={busy} onClick={onClose}>Cancel</button><button className="primary-button" disabled={busy} onClick={onConfirm}>{busy?"Waiting for wallet…":"Continue to wallet"}</button></div></dialog>;
}
