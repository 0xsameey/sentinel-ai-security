use super::*;

// Separate, versioned namespace: no changes to Sentinel or Payment Borsh layouts.
#[near(serializers = [json, borsh])]
#[derive(Clone)]
pub struct Delegation {
    pub agent: AccountId,
    pub max_payment: U128,
    pub budget: U128,
    pub daily_limit: U128,
    pub expires_at_ns: U64,
    pub recipients: Vec<AccountId>,
    pub revoked: bool,
    pub spent: U128,
    pub day: U64,
    pub spent_today: U128,
}

fn key(id: &str) -> Vec<u8> { [b"sentinel:delegation:v1:".as_slice(), id.as_bytes()].concat() }

#[near]
impl Sentinel {
    pub fn get_payment_delegation(&self, id: String) -> Option<String> {
        env::storage_read(&[b"sentinel:payment-delegation:v1:".as_slice(), id.as_bytes()].concat())
            .map(|bytes| String::from_utf8(bytes).expect("Invalid payment delegation"))
    }

    pub fn get_delegation(&self, id: String) -> Option<Delegation> {
        env::storage_read(&key(&id)).map(|bytes| Delegation::try_from_slice(&bytes).expect("Invalid delegation state"))
    }

    pub fn create_delegation(&mut self, id: String, agent: AccountId, max_payment: U128,
        budget: U128, daily_limit: U128, expires_at_ns: U64, recipients: Vec<AccountId>) {
        self.assert_owner();
        require!(!self.paused, "Paused");
        require!(!id.is_empty() && id.len() <= 64, "Invalid delegation ID");
        require!(self.get_delegation(id.clone()).is_none(), "Delegation ID already used");
        require!(agent == self.agent, "Delegation must use configured agent");
        require!(max_payment.0 > 0 && max_payment.0 <= budget.0 && max_payment.0 <= daily_limit.0,
            "Invalid delegation limits");
        require!(max_payment.0 <= self.policy.max_per_transaction.0 && daily_limit.0 <= self.policy.daily_limit.0,
            "Delegation exceeds global policy");
        require!(expires_at_ns.0 > env::block_timestamp() && expires_at_ns.0 <= env::block_timestamp().saturating_add(DAY_NS),
            "Delegation must expire within 24 hours");
        require!(!recipients.is_empty() && recipients.len() <= 16, "Explicit recipients required (maximum 16)");
        for recipient in &recipients { require!(self.is_recipient_allowed(recipient.clone()), "Recipient is not approved"); }
        let d = Delegation { agent, max_payment, budget, daily_limit, expires_at_ns, recipients,
            revoked: false, spent: U128(0), day: U64(Self::today()), spent_today: U128(0) };
        env::storage_write(&key(&id), &borsh::to_vec(&d).expect("Serialize delegation"));
    }

    pub fn revoke_delegation(&mut self, id: String) {
        self.assert_owner();
        let mut d = self.get_delegation(id.clone()).expect("Unknown delegation");
        d.revoked = true;
        env::storage_write(&key(&id), &borsh::to_vec(&d).expect("Serialize delegation"));
    }

    pub fn execute_delegated_payment(&mut self, delegation_id: String, id: String,
        recipient: AccountId, amount_yocto: U128, expires_at_ns: U64) -> Promise {
        require!(!self.paused, "Paused");
        let mut d = self.get_delegation(delegation_id.clone()).expect("Unknown delegation");
        require!(env::predecessor_account_id() == d.agent && d.agent == self.agent, "Only delegated agent");
        require!(!d.revoked, "Delegation revoked");
        require!(env::block_timestamp() < d.expires_at_ns.0, "Delegation expired");
        require!(expires_at_ns.0 <= d.expires_at_ns.0, "Payment outlives delegation");
        require!(d.recipients.contains(&recipient), "Recipient outside delegation");
        require!(amount_yocto.0 > 0 && amount_yocto.0 <= d.max_payment.0, "Delegated payment limit exceeded");
        let spent = d.spent.0.checked_add(amount_yocto.0).expect("Budget overflow");
        let today = Self::today();
        let daily = (if d.day.0 == today { d.spent_today.0 } else { 0 })
            .checked_add(amount_yocto.0).expect("Daily overflow");
        require!(spent <= d.budget.0, "Delegation budget exhausted");
        require!(daily <= d.daily_limit.0, "Delegation daily limit exhausted");
        // Charge attempts permanently, including failed transfers. No refund or retry quota bypass.
        // Receipt rollback is atomic if any subsequent validation fails.
        d.spent = U128(spent); d.day = U64(today); d.spent_today = U128(daily);
        env::storage_write(&key(&delegation_id), &borsh::to_vec(&d).expect("Serialize delegation"));
        self.authorize_payment_inner(id.clone(), recipient, amount_yocto, expires_at_ns);
        env::storage_write(&[b"sentinel:payment-delegation:v1:".as_slice(), id.as_bytes()].concat(), delegation_id.as_bytes());
        self.execute_payment(id)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use near_sdk::{test_utils::VMContextBuilder, testing_env};
    fn context(who: &str) {
        let mut b = VMContextBuilder::new();
        b.current_account_id("treasury.testnet".parse().unwrap())
            .predecessor_account_id(who.parse().unwrap())
            .account_balance(NearToken::from_near(10)).block_timestamp(DAY_NS);
        testing_env!(b.build());
    }
    fn setup() -> Sentinel {
        context("owner.testnet");
        let mut c = Sentinel::new("owner.testnet".parse().unwrap(), "agent.testnet".parse().unwrap());
        c.set_policy(U128(100), U128(1000));
        c.set_recipient_allowed("merchant.testnet".parse().unwrap(), true);
        c.create_delegation("d".into(), "agent.testnet".parse().unwrap(), U128(10), U128(15), U128(15),
            U64(DAY_NS + 100), vec!["merchant.testnet".parse().unwrap()]);
        c
    }
    fn spend(c: &mut Sentinel, id: &str) {
        context("agent.testnet");
        let _ = c.execute_delegated_payment("d".into(), id.into(), "merchant.testnet".parse().unwrap(), U128(10), U64(DAY_NS+99));
    }
    #[test]
    fn spending_reserves_before_callback() {
        let mut c = setup(); spend(&mut c, "p");
        assert_eq!(c.pending_yocto, 10);
        assert_eq!(c.get_delegation("d".into()).unwrap().spent.0, 10);
        assert_eq!(c.get_payment("p".into()).unwrap().status, PaymentStatus::Executing);
    }
    #[test]
    #[should_panic(expected="Delegation budget exhausted")]
    fn concurrent_attempt_cannot_exceed_budget() { let mut c = setup(); spend(&mut c,"p"); spend(&mut c,"q"); }
    #[test]
    #[should_panic(expected="Delegation revoked")]
    fn revoked_is_blocked() { let mut c = setup(); c.revoke_delegation("d".into()); spend(&mut c,"p"); }
    #[test]
    #[should_panic(expected="Only owner")]
    fn agent_cannot_revoke() { let mut c = setup(); context("agent.testnet"); c.revoke_delegation("d".into()); }
    #[test]
    fn failed_callback_keeps_attempt_budget() {
        let mut c = setup(); spend(&mut c,"p"); context("treasury.testnet");
        c.on_payment_complete("p".into(), Err(PromiseError::Failed));
        assert_eq!(c.pending_yocto, 0);
        assert_eq!(c.get_delegation("d".into()).unwrap().spent.0, 10);
        assert_eq!(c.get_payment("p".into()).unwrap().status, PaymentStatus::Failed);
    }
}

#[cfg(test)]
mod adversarial_tests {
    use super::*;
    use near_sdk::{test_utils::VMContextBuilder, testing_env};
    fn ctx(who:&str, time:u64) {
        let mut b=VMContextBuilder::new();
        b.current_account_id("treasury.testnet".parse().unwrap()).predecessor_account_id(who.parse().unwrap())
            .account_balance(NearToken::from_near(10)).block_timestamp(time);testing_env!(b.build());
    }
    fn setup() -> Sentinel {
        ctx("owner.testnet",DAY_NS);
        let mut c=Sentinel::new("owner.testnet".parse().unwrap(),"agent.testnet".parse().unwrap());
        c.set_policy(U128(100),U128(1000));c.set_recipient_allowed("merchant.testnet".parse().unwrap(),true);
        c.create_delegation("d".into(),"agent.testnet".parse().unwrap(),U128(10),U128(100),U128(10),U64(2*DAY_NS),vec!["merchant.testnet".parse().unwrap()]);c
    }
    fn spend(c:&mut Sentinel,id:&str,recipient:&str,amount:u128,expiry:u64) {
        let _=c.execute_delegated_payment("d".into(),id.into(),recipient.parse().unwrap(),U128(amount),U64(expiry));
    }
    #[test] #[should_panic(expected="Delegation daily limit exhausted")]
    fn daily_limit_independent_of_budget() {let mut c=setup();ctx("agent.testnet",DAY_NS);spend(&mut c,"p","merchant.testnet",10,2*DAY_NS);spend(&mut c,"q","merchant.testnet",1,2*DAY_NS);}
    #[test] #[should_panic(expected="Only delegated agent")]
    fn unauthorized_execution() {let mut c=setup();ctx("attacker.testnet",DAY_NS);spend(&mut c,"p","merchant.testnet",1,2*DAY_NS);}
    #[test] #[should_panic(expected="Recipient outside delegation")]
    fn malicious_recipient() {let mut c=setup();ctx("agent.testnet",DAY_NS);spend(&mut c,"p","attacker.testnet",1,2*DAY_NS);}
    #[test] #[should_panic(expected="Payment ID already used")]
    fn replay_is_blocked() {let mut c=setup();ctx("agent.testnet",DAY_NS);spend(&mut c,"p","merchant.testnet",1,2*DAY_NS);spend(&mut c,"p","merchant.testnet",1,2*DAY_NS);}
    #[test] #[should_panic(expected="Delegation expired")]
    fn expiration_boundary() {let mut c=setup();ctx("agent.testnet",2*DAY_NS);spend(&mut c,"p","merchant.testnet",1,2*DAY_NS);}
    #[test] #[should_panic(expected="Only delegated agent")]
    fn rotation_invalidates_authority() {let mut c=setup();c.set_agent("replacement.testnet".parse().unwrap());ctx("agent.testnet",DAY_NS);spend(&mut c,"p","merchant.testnet",1,2*DAY_NS);}
    #[test] #[should_panic(expected="Only owner")]
    fn agent_cannot_create_authority() {let mut c=setup();ctx("agent.testnet",DAY_NS);c.create_delegation("x".into(),"agent.testnet".parse().unwrap(),U128(1),U128(1),U128(1),U64(2*DAY_NS),vec!["merchant.testnet".parse().unwrap()]);}
    #[test] #[should_panic(expected="Delegation ID already used")]
    fn revoked_id_cannot_be_replaced() {let mut c=setup();c.revoke_delegation("d".into());c.create_delegation("d".into(),"agent.testnet".parse().unwrap(),U128(1),U128(1),U128(1),U64(2*DAY_NS),vec!["merchant.testnet".parse().unwrap()]);}
    #[test] #[should_panic(expected="Paused")]
    fn pause_blocks_delegated_execution() {let mut c=setup();c.set_paused(true);ctx("agent.testnet",DAY_NS);spend(&mut c,"p","merchant.testnet",1,2*DAY_NS);}
    #[test] #[should_panic(expected="Recipient is not approved")]
    fn global_recipient_revocation() {let mut c=setup();c.set_recipient_allowed("merchant.testnet".parse().unwrap(),false);ctx("agent.testnet",DAY_NS);spend(&mut c,"p","merchant.testnet",1,2*DAY_NS);}
    #[test] #[should_panic(expected="Authorization already expired")]
    fn payment_expiration_boundary() {let mut c=setup();ctx("agent.testnet",DAY_NS);spend(&mut c,"p","merchant.testnet",1,DAY_NS);}
    #[test]
    fn missing_callback_remains_reserved_and_state_roundtrip_preserves_it() {
        let mut c=setup();ctx("agent.testnet",DAY_NS);spend(&mut c,"p","merchant.testnet",1,2*DAY_NS);
        let bytes=borsh::to_vec(&c).unwrap();env::state_write(&c);let restored:Sentinel=env::state_read().unwrap();
        assert_eq!(borsh::to_vec(&restored).unwrap(),bytes);assert_eq!(restored.pending_yocto,1);
        assert_eq!(restored.get_delegation("d".into()).unwrap().spent.0,1);
        assert_eq!(restored.get_payment("p".into()).unwrap().status,PaymentStatus::Executing);
    }
    #[test]
    fn last_nanosecond_before_expiry_is_allowed() {let mut c=setup();ctx("agent.testnet",2*DAY_NS-1);spend(&mut c,"p","merchant.testnet",1,2*DAY_NS);assert_eq!(c.pending_yocto,1);}
    #[test] #[should_panic(expected="Invalid callback state")]
    fn callback_replay_is_blocked() {let mut c=setup();ctx("agent.testnet",DAY_NS);spend(&mut c,"p","merchant.testnet",1,2*DAY_NS);ctx("treasury.testnet",DAY_NS);c.on_payment_complete("p".into(),Ok(()));c.on_payment_complete("p".into(),Ok(()));}
}
