# Sentinel dashboard redesign

Follow-up: notifications are standardized and the font is now DM Sans. See
[notification audit](dashboard-notifications.md) for the current 41-test frontend
verification and notification-specific changes.

Implemented 2026-11-09. This change is confined to React presentation, component
state for navigation/feedback, frontend tests and documentation. No Rust, API
contract, backend rule, signing service, credential, database schema or NEAR
permission was changed. No live transaction was triggered during verification.

## Navigation and layout

The dashboard now has five primary pages, with hash navigation and browser
back/forward support:

| Page | Purpose |
|---|---|
| Overview | Treasury, contract protection, maximum payment, latest observed payment, review attention, spending setup and shortcuts |
| Risk assessment | Recipient/amount/purpose, read-only assessment, readable reasons and the existing owner authorization flow |
| AI payments | Owner-approved or autonomous mode, four-step workflow, proposal review, existing approval/execute actions and verification |
| Transactions | Search/filter observed delegation payments, responsive activity table/cards, expanded evidence and existing payment-ID lookup |
| Security & delegations | Keyboard-accessible tabs for global policies and spending allowances; compact creation and revocation controls |

Pages remain mounted while hidden so navigating does not discard a proposal or
review state. Navigation/mode changes do not submit a task, sign, authorize or
dispatch a transaction. The owner-approved mode remains the default.

The white-first design uses one DM Sans family (system fallback), emerald actions,
charcoal headings, neutral surfaces, subtle borders and restrained shadows.
Spacing/control/radius tokens unify the application. The sidebar is 224px on
desktop and becomes a keyboard-accessible overlay below 900px. Layouts adapt at
1180px, 900px and 620px; tables become cards on narrow screens. Technical values
wrap safely, with copy controls and fixed-host NEAR explorer links.

## Copy and state semantics

- ALLOW: **Payment meets your security rules.** This does not authorize or execute.
- WARN: **This payment needs your review.** Assessment approval remains separate
  from wallet authorization.
- BLOCK: **Payment blocked by your security rules.** It cannot be overridden.
- Payment status is displayed independently from the risk decision.
- Proposed stays neutral. Attempted/pending confirmation use blue. Settled alone
  uses settlement green. Failed/blocked use red and expired uses gray.
- Terminal verified states do not become Expired merely because their original
  authorization time has passed. A hash alone never becomes settlement.
- A valid, recorded WARN review no longer appears as an unreviewed payment; the
  underlying WARN decision remains visible separately.
- Known error/reason codes receive plain-English messages. Original codes and
  evidence remain in expandable technical details. Uncertain execution advises
  verification, not another submission. Errors remain visible until dismissed
  or replaced, rather than disappearing after a short timer.
- Old claims that live signing was blocked by a missing expiry guard were
  removed. No replacement claim of deployment verification was invented.
- Allowance labels explain maximum payment, total attempt budget, daily limit
  and expiry. Failed attempts may still consume budget. Owner policy changes
  and revocation have explicit review dialogs; creation has explicit review
  confirmation before the existing wallet call.

## Real data and preserved integrations

The overview/transaction list use existing delegation list and inspection APIs.
They show the available observations, not invented totals. At most 20 listed
delegations are loaded; each existing inspection endpoint has its own record
limit. The transaction view displays up to 200 matching loaded records and says
that search is not full account history. No new indexing API was introduced.
Owner-approved payments remain available through the existing ID lookup, since
the current API does not provide a general owner-payment list.

The existing authentication hook, wallet provider, risk API, prepare/approve/
dispatch/reconcile handlers and signing functions are retained. New data fetching
is observational: it uses the existing inspection calls. Verification buttons
call the existing reconciliation endpoint; no automatic execution was added.
Existing delegated run IDs are retained on task retry. Failed actions refresh
observed payment activity without retrying execution.

No provider/signing credentials were introduced into frontend configuration.
The inspected `.env.local` exposes only `VITE_API_URL` and
`VITE_NEAR_CONTRACT_ID`. Existing wallet/JWT behavior was not reimplemented.

## Files changed

| File | Change |
|---|---|
| `src/sentinel-dashboard/src/App.tsx` | Five-page composition, overview summaries, navigation, shared feedback and state presentation; existing payment handlers retained |
| `src/sentinel-dashboard/src/App.css` | Single entry point for the design system, replacing conflicting accumulated styles |
| `src/sentinel-dashboard/src/design-system.css` | Shared light theme, typography, controls, layouts, responsive behavior, focus states and reduced-motion support |
| `src/sentinel-dashboard/src/index.css` | DM Sans font family and minimal global reset |
| `src/sentinel-dashboard/index.html` | Product title, theme color and description |
| `src/sentinel-dashboard/src/presentation.ts` | Display-only amount/status/reason/error mappings and disabled-action explanations |
| `src/sentinel-dashboard/src/components/ui.tsx` | Status, decision, feedback, copy, empty state, technical details, explorer links, workflow and confirmation dialog components |
| `src/sentinel-dashboard/src/components/PaymentActivity.tsx` | Accessible responsive activity list, filtering, expanded intent/evidence and explicit verification |
| `src/sentinel-dashboard/src/hooks/usePaymentActivity.ts` | Bounded observations through existing APIs, loading/error states, actor-change cleanup |
| `src/sentinel-dashboard/src/components/AgentTaskPanel.tsx` | Clear owner-proposal workflow and copy; existing actions retained |
| `src/sentinel-dashboard/src/components/DelegationManager.tsx` | Separate management/payment presentations, allowance summaries, review confirmations, independent decision/status, evidence and action guidance |
| `src/sentinel-dashboard/src/components/PolicyManager.tsx` | Consistent form controls, current-limit prefilling until edited, owner review dialog before existing signing actions |
| `src/sentinel-dashboard/src/components/SecurityDemo.css` | Legacy simulation view aligned to the light palette; simulation behavior untouched |
| `src/sentinel-dashboard/tests/delegation.test.mjs` | Updated copy assertions, owner confirmation and management/payment separation |
| `src/sentinel-dashboard/tests/render-helper.mjs` | Local component test loader with inert API/signing dependencies |
| `src/sentinel-dashboard/tests/presentation.test.mjs` | Lifecycle boundaries, reason/error copy, exact amount display and disabled-action semantics |
| `src/sentinel-dashboard/tests/pages.test.mjs` | Five-page component integration and mocked wallet-state rendering without signing |
| `src/sentinel-dashboard/tests/activity.test.mjs` | Safe payment fixtures covering independent risk/payment states and escaping |

Existing `manual-testing.test.mjs` remains passing; manual inspection guards were
not removed or weakened.

## Verification

| Check | Result |
|---|---|
| Frontend tests | 26 passed, 0 failed, 0 skipped |
| React TypeScript project check | Passed |
| Production build | Passed |
| Type-aware frontend lint | Passed |
| Component integration | Five page render states and mocked disconnected/connected/authenticated wallet states passed |
| ALLOW/WARN/BLOCK, Proposed/Attempted/Settled/Failed/Expired | Local fixture/status tests passed |
| Navigation/approval safety | Render tests use signing/auth actions that throw if called; no action called by rendering |
| Browser screenshots | **Unavailable**: both browser and native computer-use entry points exited unexpectedly |
| Live wallet/signing/chain tests | Not performed; no live transactions |

Logs: `.test-artifacts/redesign-tests.log`, `redesign-typecheck.log`,
`redesign-build.log`, and `redesign-lint.log`. The build retains existing wallet
dependency warnings about browser-externalized `util` and direct `eval` in a
dependency; the build succeeds. These dependencies were not changed for a UI task.

The component tests are not browser interaction tests. Desktop/tablet/mobile
rules and keyboard handlers were reviewed in source, but actual viewport layout,
font loading, focus trapping, wallet popup behavior, clipboard permissions and
visual contrast still need manual browser QA. No screenshots are fabricated.
Backend/PostgreSQL/Rust tests were not rerun because this change does not alter
their code or API contracts.

## Start the dashboard

From the repository root, with the existing API already running:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/start-real-dashboard.ps1
```

Open **http://localhost:5173**. This default targets API HTTPS 7201. If the existing
API is running only on HTTP 5156, use:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/start-real-dashboard.ps1 -HttpApi
```

Only the frontend needs rebuilding/restarting; no backend, runner or signer
restart is required for this redesign. Do not start duplicate Vite processes on
5173. An existing dev server should pick up these files through hot reload.

Manual, non-transactional QA: inspect all five pages at 1440px, 1024px, 768px and
390px; test keyboard navigation, browser back/forward, tabs, filters, technical
details and copy feedback; inspect existing settled/blocked records; verify
uncertain records remain unverified; open then cancel confirmation dialogs.
Do not submit an AI task in autonomous mode, approve a warning, sign a wallet
transaction or click Execute during visual QA.
