# Sentinel notification audit and standardization

Implemented 2026-11-09, alongside the dashboard redesign and DM Sans font update.
No notification dependency was installed. No backend, wallet permissions,
contract, signing configuration or transaction workflow was replaced. No live
wallet signing, AI task or payment was triggered during testing.

## Existing implementation reused

Sentinel used a custom React implementation: App-level `notice`/`error` state,
the `Feedback` component and earlier fixed-position `.message` styling. There
was no toast library/provider. After the redesign those messages were inline;
agent/delegation pages also kept local feedback, while policy controls used
their existing `onNotify` callback. Clipboard feedback was local to `CopyValue`.

This implementation extends that same `Feedback` component with four variants,
secondary text, dismiss controls and expandable technical details. There is now
**one** `NotificationProvider` in `main.tsx`, containing **one** toast viewport
for both the real application and the legacy simulation page. Inline errors and
critical financial feedback remain in their original page context; they are not
a second toast stack. Routine success notices no longer duplicate the toast as
an extra page banner.

`notifications.ts` provides the shared queue and semantic message helpers. The
queue lives outside page component state, so route changes/subscriber unmounts
do not discard asynchronous results. It retains at most four visible messages,
suppresses identical action-key/message replays, and updates an existing keyed
toast when its outcome changes rather than retaining contradictory text.

The source audit found **no `alert()` calls** in `src/sentinel-dashboard/src`;
none needed replacing.

## Coverage and semantics

| Area | Notifications and safeguards |
|---|---|
| Wallet/authentication | Connected, verified, local session closed, operation errors and expiry. Expiry uses the existing JWT expiry claim for display only; it does not validate/renew/grant authentication. Sign in remains an explicit wallet action. |
| Risk assessment | Separate ALLOW success, WARN warning and BLOCK error messages explicitly describe assessment, not payment settlement. |
| Owner AI proposal | Proposal ready/authorization required, WARN review, BLOCK, errors, approval completed, unsigned preparation and assessment expiry. |
| Delegation | Explicit inspection, creation, revocation, expiry, failed refresh, policy/budget failure and warning approval. Creation/revocation success requires a matching observed on-chain state as well as a successful non-pending wallet result. |
| Autonomous execution | Attempt recorded, dispatch accepted, outcome unconfirmed, duplicate claims and reconciliation. Submission acceptance never becomes settlement success. |
| Transactions | Explicit reconciliation uses backend `chainObserved` and status; only a matching verified Settled/Failed result receives terminal payment copy. |
| General | Explicit contract refresh, API/input errors and clipboard success/failure. Background activity refresh remains inline and does not repeatedly toast. |
| Simulation page | Uses the same provider with explicitly simulation-scoped messages; never claims a real payment occurred. |

`PROPOSAL_CREATED_DISPATCH_UNCONFIRMED` gets a persistent warning: the proposal
exists, execution is unconfirmed, and the user should review the existing payment
record. Its action opens Transactions; it never retries execution.

`AGENT_RUN_UNAVAILABLE` with `proposalCreated:false` says no proposal was confirmed
for that run and suggests checking the connection and existing activity. It does
not claim no payment could exist elsewhere or encourage a blind resubmission.

Consumed permits/Attempted remain non-final. A hash or successful dispatch response
does not produce “Payment confirmed.” Verified reconciliation does. Terminal
reconciliation removes older pending warnings for that payment. Successful
authentication clears stale session/auth-error toasts. Rechecked confirmed
allowances clear their earlier pending confirmation messages.

Critical outcome errors remain visible inline and are not exclusive to toasts.
Pending inline messages no longer auto-expire on a short success timer. Raw
technical error codes are available in details, not the primary notification.

## Design, reliability and accessibility

- DM Sans is the single app font, with the existing system fallback. Technical
  values retain a monospace treatment. Both global font entry points were updated.
- Emerald success, amber warning, red error and neutral/blue info, using the
  existing shared feedback layout and Lucide icons. Text identifies the state.
- One bottom-right stack, below the header's reserved space and outside sidebar
  layout. Responsive width and safe-area spacing support mobile.
- Wallet modal, native confirmation dialog and open mobile navigation hide the
  stack and pause timers; background tabs, window blur, hover and keyboard focus
  also pause timers. The wallet's existing z-index remains above the stack.
- Success lasts 4.5 seconds, info 5 seconds, warnings 9 seconds and errors 10
  seconds by default. Financial uncertainty and session-expiry warnings persist
  until dismissed or superseded by a verified outcome.
- Error announcements use `role=alert`; other variants use `role=status`.
  Dismiss buttons have accessible labels and focus styling. Details and action
  links are keyboard operable. Clipboard inline feedback avoids an extra live
  announcement of the same copy action.
- Identical notifications are suppressed during display and for a short replay
  window; keys are scoped to public run/payment IDs where available. No JWT or
  credential is used as a toast key or message. No keystroke/navigation toasts
  were added.

## Changed files

| File | Purpose |
|---|---|
| `src/notifications.ts` | Queue, deduplication, timing, shared semantic messages and observed-confirmation guard |
| `src/components/Notifications.tsx` | Single provider/viewport; modal/focus/background pause behavior |
| `src/components/ui.tsx` | Reused Feedback variants and clipboard notifications |
| `src/main.tsx` | Mount the single provider above all app pages |
| `src/App.tsx` | Wallet/session, assessment, preparation, execution, explicit refresh, lookup and policy notifications |
| `src/components/AgentTaskPanel.tsx` | Proposal, owner-review, preparation and assessment-expiry feedback |
| `src/components/DelegationManager.tsx` | Allowance confirmation, expiry, review, dispatch, failures and reconciliation |
| `src/components/PaymentActivity.tsx` | Verification failures use the shared helper while retaining inline details |
| `src/components/PolicyManager.tsx` | Replace premature confirmation wording with submitted/unverified wording |
| `src/components/SecurityDemo.tsx` | Shared, explicitly simulated action feedback |
| `src/presentation.ts` | Friendly fallback for standalone internal codes |
| `src/design-system.css`, `src/index.css` | Toast variants/positioning and DM Sans |
| `tests/notifications.test.mjs`, `tests/render-helper.mjs` | Targeted queue/semantic/confirmation tests and shared module loading |

Paths in this table are relative to `src/sentinel-dashboard/`.

## Verification

- **41 frontend tests passed; zero failures or skips.** Includes existing page,
  wallet-state presentation and lifecycle tests plus four notification variants,
  deduplication/replay, keyed outcome updates, dismissal/timeouts, sticky/pause
  behavior, subscriber/route persistence, bounded timers, partial versus
  unavailable runner, Attempted versus Settled/Failed, observed allowance
  confirmation, stale-warning resolution and single viewport rendering.
- React TypeScript checking, production build and type-aware lint passed.
- Browser capture was attempted; the trusted browser process exited unexpectedly.
  No screenshots or live visual QA are claimed. Actual responsive placement,
  screen-reader announcements, wallet-popup focus interactions and real
  connect/authentication/expiry delivery still need manual browser checks.
- Pure queue/semantic/component tests do not make wallet, provider or chain calls.
  No transaction was triggered, and no signing key was inspected or changed.
- Logs: `.test-artifacts/notifications-tests.log`, `notifications-typecheck.log`,
  `notifications-build.log`, and `notifications-lint.log`.

Remaining intentional limits: the standalone risk endpoint does not expose an
assessment expiry, so no TTL is invented for it. Agent assessment and delegation
expiry are supported where the data exists. Settlement notifications require
explicit verified reconciliation; routine background polling does not repeatedly
announce historic outcomes. A full browser reload resets transient toasts;
canonical payment records and inline financial evidence remain the source of truth.

## Start

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/start-real-dashboard.ps1
```

Default API: HTTPS 7201. Add `-HttpApi` if the existing API is HTTP 5156. Open
http://localhost:5173. Only frontend refresh/rebuild is needed; do not restart a
signer or submit a payment for notification QA.
