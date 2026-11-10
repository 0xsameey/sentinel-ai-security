using System.Numerics;
using Sentinel.Application.Security;

namespace Sentinel.UnitTests;
public sealed class DelegationConstraintTests
{
    private static readonly DateTimeOffset Now=DateTimeOffset.FromUnixTimeMilliseconds(1794155611052);
    private static string Ns(DateTimeOffset value)=>(new BigInteger(value.ToUnixTimeMilliseconds())*1_000_000).ToString();
    private static GatewayPaymentRequest Intent()=>new("agent.testnet","100",Ns(Now.AddMinutes(15)),"Test");
    private static DelegationState State()=>new("agent.testnet","100","100","100",Ns(Now.AddMinutes(15)),["agent.testnet"],false,"0",(Now.ToUnixTimeMilliseconds()/86400000).ToString(),"0");
    [Fact] public void Exact_payment_budget_daily_and_expiry_boundaries_are_allowed()=>Assert.Empty(DelegationConstraints.Failures(State(),"agent.testnet",Intent(),Now));
    [Theory]
    [InlineData("revoked","DELEGATION_REVOKED")]
    [InlineData("agent","DELEGATION_AGENT_MISMATCH")]
    [InlineData("expired","DELEGATION_EXPIRED")]
    [InlineData("recipient","DELEGATION_RECIPIENT_NOT_ALLOWED")]
    [InlineData("payment","DELEGATION_PAYMENT_LIMIT")]
    [InlineData("budget","DELEGATION_BUDGET_EXHAUSTED")]
    [InlineData("daily","DELEGATION_DAILY_LIMIT")]
    [InlineData("outlives","PAYMENT_OUTLIVES_DELEGATION")]
    public void Every_condition_has_a_specific_diagnostic(string change,string reason) {
        var d=State();d=change switch {
            "revoked"=>d with{Revoked=true},"agent"=>d with{Agent="other.testnet"},"expired"=>d with{ExpiresAtNs=Ns(Now)},
            "recipient"=>d with{Recipients=["other.testnet"]},"payment"=>d with{MaxPayment="99"},"budget"=>d with{Spent="1"},
            "daily"=>d with{SpentToday="1"},"outlives"=>d with{ExpiresAtNs=Ns(Now.AddMinutes(14))},_=>d};
        Assert.Contains(reason,DelegationConstraints.Failures(d,"agent.testnet",Intent(),Now));
    }
    [Fact] public void Previous_days_usage_does_not_consume_todays_quota()=>Assert.Empty(DelegationConstraints.Failures(State() with{Day="0",SpentToday="100"},"agent.testnet",Intent(),Now));
    [Fact] public void Correct_nanoseconds_and_24_hour_boundaries_are_enforced() {
        Assert.Equal("1794155611052000000",Ns(Now));
        Intent().Validate(Now);
        (Intent() with{ExpiresAtNs=Ns(Now.AddHours(24))}).Validate(Now);
        Assert.Equal("PAYMENT_EXPIRED",Assert.Throws<PaymentExpiryException>(()=>(Intent() with{ExpiresAtNs=Ns(Now)}).Validate(Now)).Code);
        Assert.Equal("PAYMENT_EXPIRED",Assert.Throws<PaymentExpiryException>(()=>(Intent() with{ExpiresAtNs=Now.ToUnixTimeMilliseconds().ToString()}).Validate(Now)).Code);
        Assert.Equal("PAYMENT_EXPIRY_EXCEEDS_24_HOURS",Assert.Throws<PaymentExpiryException>(()=>(Intent() with{ExpiresAtNs=(BigInteger.Parse(Ns(Now.AddHours(24)))+1).ToString()}).Validate(Now)).Code);
    }
}
