import {friendlyError,humanReason} from "./presentation";
export type ToastVariant="success"|"warning"|"error"|"info";
export type ToastInput={title:string;description?:string;variant:ToastVariant;details?:string;key?:string;duration?:number;href?:string;actionLabel?:string;paymentId?:string};
export type Toast=ToastInput&{id:number;remaining:number;started:number};
const defaults:Record<ToastVariant,number>={success:4500,info:5000,warning:9000,error:10000};
export function createToastStore(clock:{now:()=>number;set:(fn:()=>void,ms:number)=>unknown;clear:(id:unknown)=>void}={now:()=>Date.now(),set:(fn,ms)=>setTimeout(fn,ms),clear:id=>clearTimeout(id as ReturnType<typeof setTimeout>)}){
 let items:Toast[]=[];let sequence=0;let paused=false;
 const listeners=new Set<()=>void>();const timers=new Map<number,unknown>();const seen=new Map<string,{at:number;signature:string}>();
 const emit=()=>listeners.forEach(listener=>listener());
 const dismiss=(id:number)=>{if(timers.has(id))clock.clear(timers.get(id));timers.delete(id);items=items.filter(item=>item.id!==id);emit();};
 const schedule=(item:Toast)=>{if(item.remaining>0&&!paused)timers.set(item.id,clock.set(()=>dismiss(item.id),item.remaining));};
 return {
   getSnapshot:()=>items,
   subscribe:(listener:()=>void)=>{listeners.add(listener);return()=>{listeners.delete(listener);};},
   dismiss,
   push:(input:ToastInput)=>{
     const now=clock.now();const key=input.key??`${input.variant}:${input.title}:${input.description??""}`;
     const signature=JSON.stringify([input.variant,input.title,input.description,input.href]);
     const existing=items.find(item=>(item.key??`${item.variant}:${item.title}:${item.description??""}`)===key);
     const previous=seen.get(key);
     if(previous?.signature===signature&&(existing||now-previous.at<6000))return;
     seen.set(key,{at:now,signature});for(const [old,value] of seen)if(now-value.at>60000)seen.delete(old);
     if(existing){
       if(timers.has(existing.id))clock.clear(timers.get(existing.id));timers.delete(existing.id);
       const updated={...input,id:existing.id,remaining:input.duration??defaults[input.variant],started:now};
       items=items.map(item=>item.id===existing.id?updated:item);schedule(updated);emit();return;
     }
     const item={...input,id:++sequence,remaining:input.duration??defaults[input.variant],started:now};
     // Bounded visual stack. Critical outcomes remain in the page and payment record.
     if(items.length>=4)dismiss(items[0].id);
     items=[...items,item];schedule(item);emit();
   },
   pause:()=>{if(paused)return;paused=true;const now=clock.now();items=items.map(item=>{if(timers.has(item.id)){clock.clear(timers.get(item.id));timers.delete(item.id);return {...item,remaining:Math.max(1,item.remaining-(now-item.started))};}return item;});emit();},
   resume:()=>{if(!paused)return;paused=false;items=items.map(item=>({...item,started:clock.now()}));items.forEach(schedule);emit();},
 };
}
export const toastStore=createToastStore();
export function notify(input:ToastInput){toastStore.push(input);}
export function dismissNotification(key:string){for(const item of toastStore.getSnapshot())if(item.key===key)toastStore.dismiss(item.id);}
type AllowanceObservation={id:string;state:{agent:string;maxPayment:string;budget:string;dailyLimit:string;expiresAtNs:string;recipients:string[];revoked:boolean}|null};
export function allowanceConfirmed(wallet:{success:boolean;pendingConfirmation:boolean},observation:AllowanceObservation|undefined,expected:Record<string,unknown>,action:'create'|'revoke'):boolean{
 if(!wallet.success||wallet.pendingConfirmation||!observation?.state||observation.id!==expected.id)return false;
 const state=observation.state;if(action==='revoke')return state.revoked;
 const recipients=expected.recipients;
 return !state.revoked&&state.agent===expected.agent&&state.maxPayment===expected.max_payment&&state.budget===expected.budget&&state.dailyLimit===expected.daily_limit&&state.expiresAtNs===expected.expires_at_ns&&Array.isArray(recipients)&&recipients.length===state.recipients.length&&recipients.every(recipient=>state.recipients.includes(recipient));
}
export function notifyError(raw:string,key?:string){
 let partial=false;let unavailable=false;let scope='';let paymentId:string|undefined;
 try{const start=raw.indexOf('{');if(start>=0){const response=JSON.parse(raw.slice(start));partial=response.proposalCreated===true;unavailable=response.code==='AGENT_RUN_UNAVAILABLE'&&response.proposalCreated===false;const id=response.paymentId??response.runId;if(typeof id==='string'&&/^[A-Za-z0-9_-]{1,64}$/.test(id)){scope=id;if(response.paymentId)paymentId=id;}}}catch{/* Preserve raw codes only in details. */}
 const uncertain=partial||/unconfirmed|confirmation.*pending|ALREADY_CLAIMED|ALREADY_PROCESSED|DUPLICATE|PERMIT_(?:ALREADY|USED)|RECONCILE|reporting failed|reporting pending/i.test(raw);
 const expired=/EXPIRED|expired/.test(raw);
 const limit=/DELEGATION_BUDGET_EXHAUSTED|DELEGATION_DAILY_LIMIT|TASK_SPENDING_CONSTRAINT/.test(raw);
 notify({variant:uncertain||expired||limit?"warning":"error",title:partial?'Proposal saved. Execution is unconfirmed.':uncertain?"Outcome is unconfirmed":unavailable?"The AI agent couldn't complete this request.":expired?"Authorization expired":limit?"Spending limit reached":"Request could not be completed",description:partial?'Review the existing payment record before requesting another payment.':unavailable?'No proposal was confirmed for this run. Check the agent connection and review payment activity before another request.':limit?'This request exceeds an owner-defined spending boundary. Review the amount and available allowance.':friendlyError(raw),details:raw,paymentId,key:key??`error:${scope}:${friendlyError(raw)}`,duration:uncertain?0:undefined,...(uncertain?{href:'#payments',actionLabel:'Review payment activity'}:{})});
}
export function notifyDecision(decision:string,key?:string){notify({key,variant:decision==='ALLOW'?'success':decision==='WARN'?'warning':'error',title:decision==='ALLOW'?'Payment passed security checks.':decision==='WARN'?'This payment needs your review.':'Payment blocked by your security rules.',description:decision==='ALLOW'?'This assessment does not authorize or execute the payment.':decision==='WARN'?'Review this exact assessment before proceeding.':'Review the payment reasons and your spending rules.'});}
export function notifyReconciliation(result:{status?:string;chainObserved?:boolean;paymentId?:string}){
 const key=`reconcile:${result.paymentId??'payment'}:${result.status}:${result.chainObserved}`;
 if(result.paymentId&&result.chainObserved&&(result.status==='Settled'||result.status==='Failed'))for(const item of toastStore.getSnapshot())if(item.paymentId===result.paymentId)toastStore.dismiss(item.id);
 if(result.chainObserved&&result.status==='Settled')notify({variant:'success',title:'Payment confirmed on NEAR.',description:'The matching on-chain payment has completed.',key});
 else if(result.chainObserved&&result.status==='Failed')notify({variant:'error',title:'Payment failed on NEAR.',description:'The matching on-chain payment is marked failed.',key});
 else notify({variant:'info',title:'On-chain status checked.',description:result.chainObserved?'Settlement is still pending. Do not submit this payment again.':'No matching on-chain payment was observed. Review the existing record before taking another action.',key,href:'#payments',actionLabel:'Review payment activity'});
}
export function notifyProposal(status:string,payment?:{id:string;decision:string;status?:string;decisionJson?:string}){
 const key=`proposal:${payment?.id??status}:${status}`;
 let reason:string|undefined;
 try{reason=(JSON.parse(payment?.decisionJson??'{}').reasonCodes as string[]|undefined)?.find(code=>code==='DELEGATION_BUDGET_EXHAUSTED'||code==='DELEGATION_DAILY_LIMIT');}catch{/* Raw data never controls notification authority. */}
 if(status==='PROPOSAL_CREATED_DISPATCH_UNCONFIRMED'||status==='DispatchUnconfirmed')notify({variant:'warning',title:'Proposal saved. Execution is unconfirmed.',description:'Review the existing payment record before requesting another payment.',paymentId:payment?.id,key,duration:0,href:'#payments',actionLabel:'Review existing payment'});
 else if(payment?.decision==='BLOCK'||status==='BLOCKED')notify({variant:'error',title:'Proposal blocked by your security rules.',description:reason?humanReason(reason):'The proposal remains available in payment activity.',key});
 else if(payment?.decision==='WARN'||status==='HUMAN_APPROVAL_REQUIRED')notify({variant:'warning',title:'AI proposal needs your review.',description:'Approve the exact warning assessment before execution.',key});
 else if(status==='OWNER_AUTHORIZATION_REQUIRED')notify({variant:'info',title:'AI payment proposal is ready.',description:'It passed security checks. Owner authorization is still required.',key});
 else if(payment?.status==='Attempted')notify({variant:'info',title:'An execution attempt was recorded.',description:'A permit was consumed. Transaction submission and settlement still need verification.',paymentId:payment.id,key,href:'#payments',actionLabel:'Review payment activity'});
 else notify({variant:'info',title:'AI payment proposal is ready.',description:'Check its execution and verification status in payment activity.',key});
}
