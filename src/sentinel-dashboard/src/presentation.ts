// Display only. These labels never grant authority or change a backend decision.
export type Tone = "neutral" | "green" | "amber" | "red" | "blue";
export const reasonCopy: Record<string,string> = {
  POLICY_AND_EVIDENCE_PASS:"This payment meets your current security rules.",
  DELEGATION_BUDGET_EXHAUSTED:"Your agent has used its available spending budget.",
  DELEGATION_DAILY_LIMIT:"Your agent has reached today's spending limit.",
  DELEGATION_PAYMENT_LIMIT:"This amount exceeds the delegation's maximum payment.",
  DELEGATION_RECIPIENT_NOT_ALLOWED:"This recipient is outside the agent's spending allowance.",
  DELEGATION_REVOKED:"The owner has revoked this spending allowance.",
  DELEGATION_EXPIRED:"This spending allowance has expired.",
  PAYMENT_OUTLIVES_DELEGATION:"The payment would remain valid after its allowance expires.",
  DELEGATION_AGENT_MISMATCH:"This allowance belongs to a different agent.",
  DELEGATION_LIMIT_OR_AUTHORITY:"The payment does not meet its delegation's rules.",
  RECIPIENT_NOT_ALLOWED:"This recipient is not approved by your security policy.",
  RECIPIENT_NOT_FOUND:"This recipient account could not be found on the network.",
  MALFORMED_RPC_POLICY:"Network policy data could not be verified. Execution remains blocked.",
  COUNTERPARTY_BLOCKLISTED:"This recipient is on the security blocklist.",
  PER_TRANSACTION_LIMIT:"This amount exceeds your per-payment limit.",
  DAILY_AUTHORIZATION_QUOTA:"This payment would exceed your daily authorization limit.",
  DAILY_EXECUTION_QUOTA:"This payment would exceed your daily execution limit.",
  INSUFFICIENT_TREASURY:"The treasury does not have enough available funds.",
  NEAR_TRANSACTION_LIMIT:"This payment is close to your per-payment limit.",
  INDEXED_HISTORY_UNAVAILABLE:"Recent recipient history could not be verified.",
  NO_INDEXED_HISTORY:"No recent recipient history is available.",
  HIGH_RECENT_FAILURE_RATE:"Several recent payments to this recipient have failed.",
  RECIPIENT_HAS_CONTRACT:"This recipient is a smart contract and needs extra review.",
  CONTRACT_PAUSED:"Payment execution is paused by the owner.",
  CRITICAL_RPC_UNAVAILABLE:"The network could not be checked. Execution remains blocked.",
};
export function humanReason(value:string):string {
  return reasonCopy[value] ?? (/^[A-Z][A-Z_0-9]+$/.test(value)?"A security check needs attention. See technical details.":value);
}
export function nearDisplay(value:string):string {
  if(!/^\d+$/.test(value))return "Unavailable";
  const digits=value.padStart(25,"0");const fraction=digits.slice(-24).replace(/0+$/,"");
  return digits.slice(0,-24)+(fraction?"."+fraction:"");
}
export function lifecycle(status:string,decision?:string,expiry?:string,now=Date.now(),reported=false,approvalExpiry?:string|null):{label:string;tone:Tone} {
  // Terminal chain states take precedence over later expiry or older risk decisions.
  if(status==="Settled"||status==="Completed")return {label:"Settled",tone:"green"};
  if(status==="Failed")return {label:"Failed",tone:"red"};
  if(status==="Cancelled")return {label:"Cancelled",tone:"neutral"};
  if(status==="Expired")return {label:"Expired",tone:"neutral"};
  if(status==="Reserved"||status==="Executing")return {label:"Pending confirmation",tone:"blue"};
  if(status==="Attempted")return {label:reported?"Pending confirmation":"Execution attempted",tone:"blue"};
  if(decision==="BLOCK")return {label:"Blocked",tone:"red"};
  if(expiry&&/^\d+$/.test(expiry)&&BigInt(expiry)<=BigInt(now)*1000000n)return {label:"Expired",tone:"neutral"};
  if(status==="Authorized")return {label:"Authorized · not executed",tone:"neutral"};
  if(decision==="WARN"&&!(approvalExpiry&&Date.parse(approvalExpiry)>now))return {label:"Needs review",tone:"amber"};
  return {label:status==="Proposed"?"Proposed":"Status unavailable",tone:"neutral"};
}
const errorCopy:Record<string,string>={
  AGENT_RUN_UNAVAILABLE:"The agent could not complete this request. Review payment activity and check the agent connection before starting another request.",
  AGENT_RUNNER_UNCONFIGURED:"The AI agent is not connected to this workspace. Check its service configuration before starting another request.",
  AGENT_RUN_ALREADY_CLAIMED:"This request already exists. Check its payment activity instead of creating a duplicate.",
  PAYMENT_EXPIRED:"This payment authorization has expired. Review existing activity before creating a fresh proposal.",
  PAYMENT_EXPIRY_EXCEEDS_24_HOURS:"Payment authorization must expire within 24 hours.",
  DISPATCH_TRANSPORT_UNCONFIRMED:"Execution could not be confirmed. Verify the payment's on-chain status before taking any further action.",
  EXECUTION_ALREADY_CLAIMED_RECONCILE_REQUIRED:"Execution has already been requested. Verify on-chain status; do not submit it again.",
  EXECUTION_ALREADY_CLAIMED:"A signing permit has already been issued. Verify on-chain status before taking another action.",
  SECURITY_BLOCK:"Sentinel blocked this payment. Review the security reasons; this decision cannot be overridden.",
  HUMAN_APPROVAL_REQUIRED:"This assessment needs the owner's review before it can proceed.",
  ASSESSMENT_CHANGED_REASSESS_REQUIRED:"Security conditions have changed. Run a fresh assessment before proceeding.",
  PROPOSAL_CREATED_DISPATCH_UNCONFIRMED:"Your proposal was saved, but execution is unconfirmed. Verify its on-chain status before taking another action.",
  AUTONOMOUS_PROPOSAL:"Your proposal was saved. Check payment activity for execution and verification status.",
  HUMAN_APPROVAL_REQUIRED_STATUS:"Your proposal needs the owner's review.",
  BLOCKED:"Sentinel blocked this proposal. Review its security reasons before taking another action.",
};
export function friendlyError(raw:string):string {
  const code=Object.keys(errorCopy).find(code=>raw.includes(code));if(code)return errorCopy[code];
  if(raw.includes("DispatchUnconfirmed")||raw.includes("RECONCILE_BEFORE_RETRY"))return errorCopy.DISPATCH_TRANSPORT_UNCONFIRMED;
  if(raw.includes("Failed to fetch")||raw.includes("fetch failed"))return "We couldn't reach Sentinel. Check your local connection. Review existing payment activity before submitting again.";
  if(raw.startsWith("API error"))return "This request could not be completed. Review payment activity before taking another action; technical details are available below.";
  if(/^[A-Z][A-Z0-9_]+(?: \(HTTP \d+\))?$/.test(raw))return "This request could not be completed. Review the technical details before taking another action.";
  return raw;
}
export function executionHint(input:{signedIn:boolean;owner:boolean;busy:boolean;revoked:boolean;delegationExpired:boolean;paymentExpired:boolean;decision:string;claimed:boolean;approvalValid:boolean}):string|null {
  if(input.claimed)return "Execution has already been requested. Verify on-chain status; do not submit this payment again.";
  if(input.decision==="BLOCK")return "Blocked payments cannot be executed or approved.";
  if(input.paymentExpired)return "This payment authorization has expired. Check its activity before creating a fresh proposal.";
  if(input.revoked)return "This spending allowance has been revoked.";
  if(input.delegationExpired)return "This spending allowance has expired.";
  if(!input.signedIn||!input.owner)return "Sign in with the owner wallet to request this action.";
  if(input.busy)return "A request is already in progress.";
  if(input.decision==="WARN"&&!input.approvalValid)return "Approve this exact warning assessment before requesting execution.";
  return null;
}
