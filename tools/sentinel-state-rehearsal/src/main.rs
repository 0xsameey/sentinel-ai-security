// Offline snapshot decoder only. No RPC, key access, transaction, migration or deployment code.
use near_sdk::{env, AccountId, NearToken, collections::{LookupMap,UnorderedMap}, test_utils::VMContextBuilder};
use near_sdk::borsh::{BorshDeserialize,BorshSerialize};
use sentinel_contract::{Sentinel,Payment,PaymentStatus,SpendingPolicy};
use base64::{Engine,engine::general_purpose::STANDARD};
use std::collections::HashMap;
#[derive(BorshDeserialize,BorshSerialize)]
struct PriorPolicyState {
 owner:AccountId,agent:AccountId,paused:bool,payments:UnorderedMap<String,Payment>,pending_yocto:u128,
 policy:SpendingPolicy,allowed_recipients:LookupMap<AccountId,bool>,authorization_day:u64,authorized_today_yocto:u128,
 execution_day:u64,executed_today_yocto:u128,executing_today_yocto:u128,payment_execution_days:LookupMap<String,u64>,
}
fn main() {
 let a:Vec<String>=std::env::args().collect();
 if a.len()!=4 {println!("Offline only: sentinel-state-rehearsal STATE_JSON BLOCK_TIMESTAMP_NS ACCOUNT_BALANCE_YOCTO");return;}
 let snapshot:serde_json::Value=serde_json::from_slice(&std::fs::read(&a[1]).expect("Read snapshot")).expect("Snapshot JSON");
 let mut storage:HashMap<Vec<u8>,Vec<u8>>=HashMap::new();
 for pair in snapshot["values"].as_array().expect("RPC view_state values array") {
  let k=STANDARD.decode(pair["key"].as_str().unwrap()).unwrap();let v=STANDARD.decode(pair["value"].as_str().unwrap()).unwrap();
  assert!(storage.insert(k,v).is_none(),"Duplicate snapshot storage key");
 }
 let original=storage.get(b"STATE".as_slice()).expect("Missing root STATE").clone();
 let old=PriorPolicyState::try_from_slice(&original).expect("STOP: snapshot is not the reviewed policy-bearing layout");
 assert_eq!(borsh::to_vec(&old).unwrap(),original,"Prior root roundtrip mismatch");
 let m:(Vec<u8>,(u64,Vec<u8>),(u64,Vec<u8>))=borsh::from_slice(&borsh::to_vec(&old.payments).unwrap()).expect("Payment collection metadata");
 assert_eq!(m.0,b"pi");assert_eq!(m.1.1,b"pk");assert_eq!(m.2.1,b"pv");assert_eq!(m.1.0,m.2.0);
 assert_eq!(borsh::to_vec(&old.allowed_recipients).unwrap(),borsh::to_vec(&LookupMap::<AccountId,bool>::new(b"a")).unwrap());
 assert_eq!(borsh::to_vec(&old.payment_execution_days).unwrap(),borsh::to_vec(&LookupMap::<String,u64>::new(b"d")).unwrap());
 let namespace_entries=storage.keys().filter(|k|k.starts_with(b"sentinel:delegation:v1:")||k.starts_with(b"sentinel:payment-delegation:v1:")).count();
 assert_eq!(namespace_entries,0,"STOP: existing delegation namespaces need a separately reviewed upgrade plan");
 let mut ctx=VMContextBuilder::new();ctx.current_account_id("0xsameey-sentinel-v2.testnet".parse().unwrap())
  .predecessor_account_id(old.owner.clone()).block_timestamp(a[2].parse().unwrap())
  .account_balance(NearToken::from_yoctonear(a[3].parse().unwrap()));
 let original_storage=storage.clone();
 let storage_bytes=storage.iter().map(|(k,v)|(k.len()+v.len())as u64).sum();ctx.storage_usage(storage_bytes);
 env::set_blockchain_interface(near_sdk::MockedBlockchain::new(ctx.build(),near_sdk::test_vm_config(),near_sdk::RuntimeFeesConfig::test(),vec![],storage,HashMap::new(),None));
 let upgraded:Sentinel=env::state_read().expect("STOP: proposed code cannot read snapshot");
 assert_eq!(borsh::to_vec(&upgraded).unwrap(),original,"Proposed root bytes differ");
 assert_eq!(upgraded.get_config(),(old.owner.clone(),old.agent.clone(),old.paused));
 let mut inventory=Vec::new();let mut count=0u64;let mut executing=0u128;let mut authorized=0u64;
 for(id,payment)in old.payments.iter(){
  let loaded=upgraded.get_payment(id.clone()).expect("Lost payment");
  assert_eq!(borsh::to_vec(&loaded).unwrap(),borsh::to_vec(&payment).unwrap(),"Changed payment");
  if payment.status==PaymentStatus::Executing {executing=executing.checked_add(payment.amount_yocto.0).unwrap();assert!(old.payment_execution_days.get(&id).is_some(),"Missing execution-day reservation");}
  if payment.status==PaymentStatus::Authorized {authorized+=1;}
  inventory.push(serde_json::to_value(&payment).unwrap());count+=1;
 }
 assert_eq!(executing,old.pending_yocto,"Pending transfer sum mismatch");
 let remaining=near_sdk::mock::with_mocked_blockchain(|b|b.take_storage());
 assert_eq!(original_storage,remaining,"Snapshot storage changed during read-only rehearsal");
 println!("{}",serde_json::json!({"offline":true,"layoutCompatible":true,"rootBytesPreserved":true,"paymentCount":count,"payments":inventory,"allStoragePreserved":true,"authorizedPayments":authorized,"pendingYocto":old.pending_yocto.to_string(),"owner":old.owner,"agent":old.agent,"paused":old.paused,"delegationNamespaceEntries":namespace_entries,"quiescentForUpgrade":executing==0,"note":"Not a WASM VM or live receipt proof. No migration, initializer or callback executed."}));
}
