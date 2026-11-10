# Exact AI payment amount regression

2026-11-07: The dashboard converts NEAR using 24 decimal places correctly.
The prior agent enforced only a maximum ceiling. A smaller model-generated
amount could therefore pass deterministic policy assessment even when the
owner task explicitly requested an exact amount. ALLOW validated that smaller
intent against policy; it did not prove fidelity to the requested amount.
The particular historical run was not fetched or changed during this fix.

The shared Claude/OpenAI workflow now deterministically reads explicit
`exactly <decimal> NEAR` amounts from the submitted task using integer arithmetic.
It supplies `exactAmountYocto` to the model, constrains the proposal tool's amount
to that single value, and independently rejects any mismatch before assessment.
This only narrows the separately supplied ceiling. Conflicting exact amounts,
invalid precision, over-ceiling requests and policy failures fail closed.
Tasks without an explicit exact-amount request retain ceiling-based behavior.
No model response, recipient data or tool output can change this constraint.

Regression: exactly 0.001 NEAR becomes `1000000000000000000000` yoctoNEAR;
`1000000000000000000` (0.000001 NEAR) is rejected as `EXACT_AMOUNT_MISMATCH`.
The proposal-only owner workflow never dispatches. Dashboard placeholder now
shows an exact-amount request instead of suggesting minimum-amount selection.

Verification: 78 deterministic agent tests pass with zero failures/skips;
workspace TypeScript checking passes. These are local tests, not a live model
or blockchain run. No payment, approval, signing key or database record changed.

Restart the Claude runner to load the fix:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/start-real-agent.ps1 -HttpApi
```

Submit a fresh task using the original exact request, select the intended
recipient and set maximum NEAR to at least 0.001. An old assessment keeps its
original immutable amount; do not prepare or authorize it as the new request.
