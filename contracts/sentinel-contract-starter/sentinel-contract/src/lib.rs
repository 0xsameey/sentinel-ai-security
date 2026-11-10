mod delegation;
use near_sdk::borsh::{BorshDeserialize, BorshSerialize};
use near_sdk::collections::{LookupMap, UnorderedMap};
use near_sdk::json_types::{U128, U64};
use near_sdk::{env, near, require, AccountId, Gas, NearToken, PanicOnDefault, Promise, PromiseError};

const CALLBACK_GAS: Gas = Gas::from_tgas(15);
const DAY_NS: u64 = 86_400_000_000_000;
const GAS_RESERVE_YOCTO: u128 = 200_000_000_000_000_000_000_000; // 0.2 NEAR
const STORAGE_BUFFER_BYTES: u128 = 4_096;

// Keep the original variant order: it is part of the stored Borsh format.
#[near(serializers = [json, borsh])]
#[derive(Clone, PartialEq, Eq, Debug)]
pub enum PaymentStatus {
    Authorized,
    Executing,
    Completed,
    Failed,
    Cancelled,
}

// Keep these original five fields in exactly this order for migration.
#[near(serializers = [json, borsh])]
#[derive(Clone)]
pub struct Payment {
    pub id: String,
    pub recipient: AccountId,
    pub amount_yocto: U128,
    pub expires_at_ns: U64,
    pub status: PaymentStatus,
}

#[near(serializers = [json, borsh])]
#[derive(Clone, Debug)]
pub struct SpendingPolicy {
    pub max_per_transaction: U128,
    pub daily_limit: U128,
}

impl Default for SpendingPolicy {
    fn default() -> Self {
        Self { max_per_transaction: U128(0), daily_limit: U128(0) }
    }
}

// Exact Borsh layout of the original deployed contract state.
#[derive(BorshDeserialize, BorshSerialize)]
pub struct OldSentinel {
    owner: AccountId,
    agent: AccountId,
    paused: bool,
    payments: UnorderedMap<String, Payment>,
    pending_yocto: u128,
}

#[near(contract_state)]
#[derive(PanicOnDefault)]
pub struct Sentinel {
    // Keep original fields first and in their original order.
    owner: AccountId,
    agent: AccountId,
    paused: bool,
    payments: UnorderedMap<String, Payment>,
    pending_yocto: u128,

    policy: SpendingPolicy,
    allowed_recipients: LookupMap<AccountId, bool>,

    // Authorization-day quota; never refunded on failure/cancellation.
    authorization_day: u64,
    authorized_today_yocto: u128,

    // Execution-day quota: completed + currently in flight.
    execution_day: u64,
    executed_today_yocto: u128,
    executing_today_yocto: u128,
    payment_execution_days: LookupMap<String, u64>,
}

#[near]
impl Sentinel {
    #[init]
    pub fn new(owner: AccountId, agent: AccountId) -> Self {
        require!(!env::state_exists(), "Already initialized");
        let day = Self::today();
        Self {
            owner,
            agent,
            paused: false,
            payments: UnorderedMap::new(b"p"),
            pending_yocto: 0,
            policy: SpendingPolicy::default(),
            allowed_recipients: LookupMap::new(b"a"),
            authorization_day: day,
            authorized_today_yocto: 0,
            execution_day: day,
            executed_today_yocto: 0,
            executing_today_yocto: 0,
            payment_execution_days: LookupMap::new(b"d"),
        }
    }

    // Deploy upgraded code first, then call migrate from the contract account.
    // Test this on a separate account before upgrading existing funds/state.
    #[private]
    #[init(ignore_state)]
    pub fn migrate() -> Self {
        let old: OldSentinel = env::state_read()
            .expect("Previous contract state not found");
        // Require a paused legacy contract with no executing payment.
        // Existing authorizations should be audited before upgrade.
        require!(old.paused, "Pause old contract before migration");
        require!(old.pending_yocto == 0, "Pending transfers must finish before migration");
        let day = Self::today();
        Self {
            owner: old.owner,
            agent: old.agent,
            paused: true,
            payments: old.payments,
            pending_yocto: 0,
            policy: SpendingPolicy::default(),
            allowed_recipients: LookupMap::new(b"a"),
            authorization_day: day,
            authorized_today_yocto: 0,
            execution_day: day,
            executed_today_yocto: 0,
            executing_today_yocto: 0,
            payment_execution_days: LookupMap::new(b"d"),
        }
    }

    pub fn get_config(&self) -> (AccountId, AccountId, bool) {
        (self.owner.clone(), self.agent.clone(), self.paused)
    }

    pub fn get_payment(&self, id: String) -> Option<Payment> {
        self.payments.get(&id)
    }

    pub fn get_policy(&self) -> SpendingPolicy {
        self.policy.clone()
    }

    pub fn is_recipient_allowed(&self, recipient: AccountId) -> bool {
        self.allowed_recipients.get(&recipient).unwrap_or(false)
    }

    pub fn get_daily_usage(&self) -> (U64, U128, U128, U128) {
        let today = Self::today();
        (
            U64(today),
            U128(if self.authorization_day == today { self.authorized_today_yocto } else { 0 }),
            U128(if self.execution_day == today { self.executed_today_yocto } else { 0 }),
            U128(if self.execution_day == today { self.executing_today_yocto } else { 0 }),
        )
    }

    pub fn get_available_balance(&self) -> U128 {
        let storage_bytes = (env::storage_usage() as u128)
            .saturating_add(STORAGE_BUFFER_BYTES);
        let storage_cost = storage_bytes
            .saturating_mul(env::storage_byte_cost().as_yoctonear());
        U128(env::account_balance().as_yoctonear()
            .saturating_sub(storage_cost)
            .saturating_sub(GAS_RESERVE_YOCTO)
            .saturating_sub(self.pending_yocto))
    }

    pub fn set_agent(&mut self, agent: AccountId) {
        self.assert_owner();
        self.agent = agent;
    }

    pub fn set_paused(&mut self, paused: bool) {
        self.assert_owner();
        self.paused = paused;
    }

    pub fn set_policy(&mut self, max_per_transaction: U128, daily_limit: U128) {
        self.assert_owner();
        require!(max_per_transaction.0 > 0, "Maximum transaction amount must be positive");
        require!(daily_limit.0 >= max_per_transaction.0, "Daily limit must cover maximum transaction");
        self.policy = SpendingPolicy { max_per_transaction, daily_limit };
        env::log_str("Spending policy updated");
    }

    pub fn set_recipient_allowed(&mut self, recipient: AccountId, allowed: bool) {
        self.assert_owner();
        require!(recipient != env::current_account_id(), "Treasury cannot be a recipient");
        if allowed {
            self.allowed_recipients.insert(&recipient, &true);
        } else {
            self.allowed_recipients.remove(&recipient);
        }
        env::log_str("Recipient permission updated");
    }

    pub fn authorize_payment(
        &mut self,
        id: String,
        recipient: AccountId,
        amount_yocto: U128,
        expires_at_ns: U64,
    ) {
        self.assert_owner();
        self.authorize_payment_inner(id, recipient, amount_yocto, expires_at_ns);
    }

    fn authorize_payment_inner(&mut self, id: String, recipient: AccountId, amount_yocto: U128, expires_at_ns: U64) {
        require!(!self.paused, "Paused");
        require!(!id.is_empty() && id.len() <= 64, "Invalid payment ID");
        require!(self.payments.get(&id).is_none(), "Payment ID already used");
        require!(amount_yocto.0 > 0, "Amount must be positive");
        require!(expires_at_ns.0 > env::block_timestamp(), "Authorization already expired");
        const MAX_PAYMENT_LIFETIME_NS: u64 = 86_400_000_000_000;

        require!(
            expires_at_ns.0 <=
                env::block_timestamp()
                    .saturating_add(MAX_PAYMENT_LIFETIME_NS),
            "Payment authorization exceeds 24 hours"
        );
        require!(recipient != env::current_account_id(), "Treasury cannot pay itself");
        require!(self.is_recipient_allowed(recipient.clone()), "Recipient is not approved");
        require!(amount_yocto.0 <= self.policy.max_per_transaction.0, "Transaction exceeds per-payment limit");

        let today = Self::today();
        let authorized = if self.authorization_day == today {
            self.authorized_today_yocto
        } else {
            0
        };
        let next = authorized.checked_add(amount_yocto.0)
            .expect("Authorization quota overflow");
        require!(next <= self.policy.daily_limit.0, "Daily authorization limit exceeded");
        self.authorization_day = today;
        self.authorized_today_yocto = next;

        let payment = Payment {
            id: id.clone(),
            recipient,
            amount_yocto,
            expires_at_ns,
            status: PaymentStatus::Authorized,
        };
        self.payments.insert(&id, &payment);
        env::log_str("Payment authorized");
    }

    pub fn cancel_payment(&mut self, id: String) {
        self.assert_owner();
        let mut payment = self.payments.get(&id).expect("Unknown payment");
        require!(payment.status == PaymentStatus::Authorized, "Only authorized payments can be cancelled");
        payment.status = PaymentStatus::Cancelled;
        self.payments.insert(&id, &payment);
        env::log_str("Payment cancelled");
    }

    pub fn execute_payment(&mut self, id: String) -> Promise {
        require!(env::predecessor_account_id() == self.agent, "Only agent");
        require!(!self.paused, "Paused");
        let mut p = self.payments.get(&id).expect("Unknown payment");
        require!(p.status == PaymentStatus::Authorized, "Already executed or in progress");
        require!(env::block_timestamp() < p.expires_at_ns.0, "Expired");
        require!(self.is_recipient_allowed(p.recipient.clone()), "Recipient approval has been revoked");
        require!(p.amount_yocto.0 <= self.policy.max_per_transaction.0, "Payment exceeds current transaction limit");
        require!(self.policy.daily_limit.0 > 0, "Spending policy not configured");

        let today = Self::today();
        let completed = if self.execution_day == today { self.executed_today_yocto } else { 0 };
        let in_flight = if self.execution_day == today { self.executing_today_yocto } else { 0 };
        let next_in_flight = in_flight.checked_add(p.amount_yocto.0)
            .expect("Execution quota overflow");
        let total = completed.checked_add(next_in_flight)
            .expect("Execution quota overflow");
        require!(total <= self.policy.daily_limit.0, "Daily execution limit exceeded");
        require!(p.amount_yocto.0 <= self.get_available_balance().0, "Insufficient available funds");

        self.pending_yocto = self.pending_yocto.checked_add(p.amount_yocto.0)
            .expect("Pending balance overflow");
        self.execution_day = today;
        self.executed_today_yocto = completed;
        self.executing_today_yocto = next_in_flight;
        self.payment_execution_days.insert(&id, &today);
        p.status = PaymentStatus::Executing;
        self.payments.insert(&id, &p);

        Promise::new(p.recipient)
            .transfer(NearToken::from_yoctonear(p.amount_yocto.0))
            .then(Self::ext(env::current_account_id())
                .with_static_gas(CALLBACK_GAS)
                .on_payment_complete(id))
    }

    #[private]
    pub fn on_payment_complete(&mut self, id: String, #[callback_result] result: Result<(), PromiseError>) {
        let mut p = self.payments.get(&id).expect("Unknown payment");
        require!(p.status == PaymentStatus::Executing, "Invalid callback state");
        let amount = p.amount_yocto.0;
        self.pending_yocto = self.pending_yocto.checked_sub(amount)
            .expect("Pending balance underflow");

        // A callback after midnight belongs to the day the transfer began.
        if let Some(payment_day) = self.payment_execution_days.get(&id) {
            if payment_day == self.execution_day {
                self.executing_today_yocto = self.executing_today_yocto
                    .checked_sub(amount).expect("Execution counter underflow");
                if result.is_ok() {
                    self.executed_today_yocto = self.executed_today_yocto
                        .checked_add(amount).expect("Execution counter overflow");
                }
            }
        }
        self.payment_execution_days.remove(&id);
        p.status = if result.is_ok() { PaymentStatus::Completed } else { PaymentStatus::Failed };
        self.payments.insert(&id, &p);
        env::log_str(if result.is_ok() { "Payment completed" } else { "Payment failed" });
    }
}

impl Sentinel {
    fn assert_owner(&self) {
        require!(env::predecessor_account_id() == self.owner, "Only owner");
    }

    fn today() -> u64 {
        env::block_timestamp() / DAY_NS
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use near_sdk::test_utils::VMContextBuilder;
    use near_sdk::testing_env;

    fn account(id: &str) -> AccountId { id.parse().unwrap() }

    fn context(predecessor: &str, timestamp: u64) {
        let mut builder = VMContextBuilder::new();
        builder.current_account_id(account("sentinel.testnet"))
            .predecessor_account_id(account(predecessor))
            .account_balance(NearToken::from_near(10))
            .block_timestamp(timestamp);
        testing_env!(builder.build());
    }

    fn setup() -> Sentinel {
        context("owner.testnet", DAY_NS);
        let mut c = Sentinel::new(account("owner.testnet"), account("agent.testnet"));
        c.set_policy(U128(NearToken::from_near(1).as_yoctonear()),
                     U128(NearToken::from_near(2).as_yoctonear()));
        c.set_recipient_allowed(account("merchant.testnet"), true);
        c
    }

    fn authorize(c: &mut Sentinel, id: &str, amount: u128, expiry: u64) {
        c.authorize_payment(id.into(), account("merchant.testnet"), U128(amount), U64(expiry));
    }

    #[test]
    fn authorization_at_24_hour_boundary_is_allowed() {
        let mut c = setup();
        authorize(&mut c, "boundary", 1, 2 * DAY_NS);
        assert_eq!(c.get_payment("boundary".into()).unwrap().expires_at_ns.0, 2 * DAY_NS);
    }

    #[test]
    #[should_panic(expected = "Payment authorization exceeds 24 hours")]
    fn authorization_beyond_24_hours_is_blocked() {
        let mut c = setup();
        authorize(&mut c, "too-long", 1, 2 * DAY_NS + 1);
    }

    #[test]
    #[should_panic(expected = "Expired")]
    fn execution_at_expiry_is_blocked() {
        let mut c = setup();
        authorize(&mut c, "expired", 1, DAY_NS + 1);
        context("agent.testnet", DAY_NS + 1);
        let _ = c.execute_payment("expired".into());
    }

    #[test]
    #[should_panic(expected = "Only agent")]
    fn unrelated_caller_cannot_execute() {
        let mut c = setup();
        authorize(&mut c, "wrong-caller", 1, 2 * DAY_NS);
        context("attacker.testnet", DAY_NS);
        let _ = c.execute_payment("wrong-caller".into());
    }

    #[test]
    #[should_panic(expected = "Paused")]
    fn pause_blocks_agent_execution() {
        let mut c = setup();
        authorize(&mut c, "paused", 1, 2 * DAY_NS);
        c.set_paused(true);
        context("agent.testnet", DAY_NS);
        let _ = c.execute_payment("paused".into());
    }

    #[test]
    fn approved_payment_succeeds() {
        let mut c = setup();
        authorize(&mut c, "p1", NearToken::from_millinear(500).as_yoctonear(), DAY_NS + 60_000_000_000);
        assert_eq!(c.get_payment("p1".into()).unwrap().status, PaymentStatus::Authorized);
    }

    #[test]
    #[should_panic(expected = "Recipient is not approved")]
    fn unknown_recipient_is_blocked() {
        let mut c = setup();
        c.authorize_payment("p1".into(), account("attacker.testnet"),
            U128(NearToken::from_millinear(500).as_yoctonear()), U64(DAY_NS + 60_000_000_000));
    }

    #[test]
    #[should_panic(expected = "Transaction exceeds per-payment limit")]
    fn oversized_payment_is_blocked() {
        let mut c = setup();
        authorize(&mut c, "p1", NearToken::from_near(2).as_yoctonear(), DAY_NS + 60_000_000_000);
    }

    #[test]
    #[should_panic(expected = "Daily authorization limit exceeded")]
    fn authorization_quota_is_enforced() {
        let mut c = setup();
        for i in 0..3 {
            authorize(&mut c, &format!("p{i}"), NearToken::from_near(1).as_yoctonear(), DAY_NS + 60_000_000_000);
        }
    }

    #[test]
    #[should_panic(expected = "Only owner")]
    fn non_owner_cannot_change_policy() {
        let mut c = setup();
        context("agent.testnet", DAY_NS);
        c.set_policy(U128(1), U128(2));
    }

    #[test]
    #[should_panic(expected = "Recipient approval has been revoked")]
    fn revocation_blocks_execution() {
        let mut c = setup();
        authorize(&mut c, "p1", NearToken::from_millinear(500).as_yoctonear(), DAY_NS + 60_000_000_000);
        c.set_recipient_allowed(account("merchant.testnet"), false);
        context("agent.testnet", DAY_NS);
        c.execute_payment("p1".into());
    }

    #[test]
    #[should_panic(expected = "Already executed or in progress")]
    fn cancelled_payment_cannot_execute() {
        let mut c = setup();
        authorize(&mut c, "p1", NearToken::from_millinear(500).as_yoctonear(), DAY_NS + 60_000_000_000);
        c.cancel_payment("p1".into());
        context("agent.testnet", DAY_NS);
        c.execute_payment("p1".into());
    }

    #[test]
    #[should_panic(expected = "Daily execution limit exceeded")]
    fn cross_day_authorizations_cannot_exceed_execution_limit() {
        let mut c = setup();
        let amount = NearToken::from_near(1).as_yoctonear();
        context("owner.testnet", DAY_NS + DAY_NS / 2);
        authorize(&mut c, "p1", amount, 2 * DAY_NS + DAY_NS / 2);
        authorize(&mut c, "p2", amount, 2 * DAY_NS + DAY_NS / 2);
        context("owner.testnet", 2 * DAY_NS);
        authorize(&mut c, "p3", amount, 3 * DAY_NS);
        context("agent.testnet", 2 * DAY_NS);
        c.execute_payment("p1".into());
        c.execute_payment("p2".into());
        c.execute_payment("p3".into());
    }

    
#[test]
fn successful_payment_updates_execution_usage() {
    let mut c = setup();
    let amount = NearToken::from_millinear(500).as_yoctonear();

    authorize(&mut c, "p1", amount, 2 * DAY_NS);

    context("agent.testnet", DAY_NS);
    c.execute_payment("p1".into());

    assert_eq!(
        c.get_payment("p1".into()).unwrap().status,
        PaymentStatus::Executing
    );

    context("sentinel.testnet", DAY_NS);
    c.on_payment_complete("p1".into(), Ok(()));

    assert_eq!(
        c.get_payment("p1".into()).unwrap().status,
        PaymentStatus::Completed
    );

    assert_eq!(c.pending_yocto, 0);
    assert_eq!(c.executing_today_yocto, 0);
    assert_eq!(c.executed_today_yocto, amount);
}

#[test]
fn callback_after_midnight_keeps_original_execution_day() {
    let mut c = setup();
    let amount = NearToken::from_millinear(500).as_yoctonear();

    authorize(&mut c, "p1", amount, 2 * DAY_NS);

    context("agent.testnet", 2 * DAY_NS - 1);
    c.execute_payment("p1".into());

    // The callback arrives after midnight.
    context("sentinel.testnet", 2 * DAY_NS + 1);
    c.on_payment_complete("p1".into(), Ok(()));

    assert_eq!(
        c.get_payment("p1".into()).unwrap().status,
        PaymentStatus::Completed
    );

    assert_eq!(c.pending_yocto, 0);
    assert_eq!(c.executing_today_yocto, 0);
    assert_eq!(c.executed_today_yocto, amount);

    // Yesterday's spending must not count against today.
    let usage = c.get_daily_usage();
    assert_eq!(usage.2.0, 0);
    assert_eq!(usage.3.0, 0);
}

}


#[cfg(test)]
mod storage_rehearsal;
