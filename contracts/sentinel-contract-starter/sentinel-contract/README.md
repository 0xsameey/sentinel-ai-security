# Sentinel contract — testnet security prototype

Owner authorizes immutable recipient, amount, unique ID and future expiry no more
than 24 hours away. Only the agent executes. Authorization and execution enforce
pause, recipient permissions and per-payment/daily budgets. Available balance
reserves storage and 0.2 NEAR for gas. A private callback tracks outcome; failed
or cancelled IDs cannot be reused. Authorization quota is never refunded.

Run `cmd /d /c tests\run-rust-tests.cmd` from the repository root. Prepare a local
review artifact with `tests/prepare-milestone5a-contract.ps1`: Rust 1.99.1, locked
offline build and reviewed wasm-opt 0.116.1 helper. The script writes provenance
and hashes; it never deploys or signs.

See [Milestone 5A](../../../docs/milestone-5a.md) for approval gates, state
compatibility and rollback considerations. The expiry-only upgrade changes no
state layout. Do not call `new` or legacy `migrate` on the compatible current
state. Legacy migration only supports the old five-field layout, requires pause
and zero pending transfers, and initializes policy/allowlist to deny by default.

Unaudited prototype: unit tests do not establish live protocol enforcement or
asynchronous receipt behavior. There is no withdrawal/recovery or owner rotation.
Use testnet only. Keep keys in the isolated signer, outside model context.
