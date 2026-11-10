// Offline rehearsal of the policy-bearing pre-5B layout. Never calls migrate/new on stored state.
use super::*;
use near_sdk::{test_utils::VMContextBuilder,testing_env};
#[derive(BorshSerialize,BorshDeserialize)]
struct PriorPolicyState {
    owner:AccountId,agent:AccountId,paused:bool,payments:UnorderedMap<String,Payment>,pending_yocto:u128,
    policy:SpendingPolicy,allowed_recipients:LookupMap<AccountId,bool>,authorization_day:u64,authorized_today_yocto:u128,
    execution_day:u64,executed_today_yocto:u128,executing_today_yocto:u128,payment_execution_days:LookupMap<String,u64>,
}
fn ctx(who:&str) {let mut b=VMContextBuilder::new();b.current_account_id("treasury.testnet".parse().unwrap()).predecessor_account_id(who.parse().unwrap()).block_timestamp(DAY_NS).account_balance(NearToken::from_near(10));testing_env!(b.build());}
#[test]
fn policy_state_upgrade_rehearsal_preserves_root_collections_reservations_and_records() {
    ctx("owner.testnet");
    let mut payments=UnorderedMap::new(b"p");
    for (id,status) in [("authorized",PaymentStatus::Authorized),("executing",PaymentStatus::Executing),("completed",PaymentStatus::Completed),("failed",PaymentStatus::Failed),("cancelled",PaymentStatus::Cancelled)] {
        payments.insert(&id.to_string(),&Payment{id:id.to_string(),recipient:"merchant.testnet".parse().unwrap(),amount_yocto:U128(10),expires_at_ns:U64(2*DAY_NS),status});
    }
    let mut recipients=LookupMap::new(b"a");recipients.insert(&"merchant.testnet".parse::<AccountId>().unwrap(),&true);
    let mut days=LookupMap::new(b"d");days.insert(&"executing".to_string(),&1);
    let prior=PriorPolicyState{owner:"owner.testnet".parse().unwrap(),agent:"agent.testnet".parse().unwrap(),paused:true,payments,pending_yocto:10,
        policy:SpendingPolicy{max_per_transaction:U128(100),daily_limit:U128(1000)},allowed_recipients:recipients,
        authorization_day:1,authorized_today_yocto:50,execution_day:1,executed_today_yocto:10,executing_today_yocto:10,payment_execution_days:days};
    let bytes=borsh::to_vec(&prior).unwrap();assert!(OldSentinel::try_from_slice(&bytes).is_err());
    env::storage_write(b"STATE",&bytes);
    let mut upgraded:Sentinel=env::state_read().unwrap();
    assert_eq!(borsh::to_vec(&upgraded).unwrap(),bytes);assert_eq!(upgraded.payments.len(),5);assert_eq!(upgraded.pending_yocto,10);
    assert!(upgraded.get_delegation("new".into()).is_none());
    ctx("treasury.testnet");upgraded.on_payment_complete("executing".into(),Ok(()));assert_eq!(upgraded.pending_yocto,0);
    ctx("owner.testnet");upgraded.set_paused(false);upgraded.create_delegation("new".into(),"agent.testnet".parse().unwrap(),U128(1),U128(2),U128(2),U64(2*DAY_NS),vec!["merchant.testnet".parse().unwrap()]);
    assert_eq!(upgraded.get_payment("authorized".into()).unwrap().status,PaymentStatus::Authorized);
    assert_eq!(upgraded.get_payment("completed".into()).unwrap().status,PaymentStatus::Completed);
    assert_eq!(upgraded.get_payment("failed".into()).unwrap().status,PaymentStatus::Failed);
    assert_eq!(upgraded.get_payment("cancelled".into()).unwrap().status,PaymentStatus::Cancelled);
    env::state_write(&upgraded);let restored:Sentinel=env::state_read().unwrap();assert_eq!(restored.payments.len(),5);
}
