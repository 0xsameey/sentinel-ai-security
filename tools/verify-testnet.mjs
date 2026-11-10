// Read-only verification. No transaction or key-management RPC is implemented.
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { assessTrialPolicy, inspectSignerPermission, crossCheckSignerPermission } from './verification-checks.mjs';
const require = createRequire(new URL('../src/sentinel-signer/package.json', import.meta.url));
const { parse } = require('dotenv');
const { KeyPairSigner } = require('near-api-js');
const root = resolve(fileURLToPath(new URL('..', import.meta.url)));
const contractDir = resolve(root, 'contracts/sentinel-contract-starter/sentinel-contract');
const sha = data => createHash('sha256').update(data).digest();
function base58(bytes) {
  const alphabet = '123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz';
  let n = BigInt('0x' + bytes.toString('hex')); let out = '';
  while (n) { out = alphabet[Number(n % 58n)] + out; n /= 58n; }
  for (const b of bytes) { if (b !== 0) break; out = '1' + out; }
  return out;
}
const expected = ['new','migrate','get_config','get_payment','get_policy','is_recipient_allowed',
  'get_daily_usage','get_available_balance','set_agent','set_paused','set_policy',
  'set_recipient_allowed','authorize_payment','cancel_payment','execute_payment','on_payment_complete'];
let report = { checkedAtUtc: new Date().toISOString(), sourceIdentity: 'unverified', liveVerification: 'unverified' };
const reportDir = resolve(root, '.test-artifacts/milestone1b');
const diagnostics = process.argv.includes('--diagnostics');
const reportName = diagnostics ? 'testnet-diagnostics.json' : 'testnet-readonly.json';
try {
  const env = parse(await readFile(resolve(root, 'src/sentinel-signer/.env')));
  for (const key of ['NEAR_NETWORK','NEAR_CONTRACT_ID','NEAR_AGENT_ACCOUNT_ID','NEAR_AGENT_PRIVATE_KEY','NEAR_RPC_URL']) {
    if (process.env[key] !== undefined) env[key] = process.env[key];
  }
  const contractId = env.NEAR_CONTRACT_ID;
  const agentId = env.NEAR_AGENT_ACCOUNT_ID;
  const recipient = '0xsameey-agent.testnet'; // Explicit user-selected trial recipient.
  const trialAmount = 1_000_000_000_000_000_000_000n;
  if (env.NEAR_NETWORK !== 'testnet' || !contractId?.endsWith('.testnet') || !agentId?.endsWith('.testnet')) throw new Error('InvalidTestnetConfig');
  if (!env.NEAR_AGENT_PRIVATE_KEY) throw new Error('MissingCanonicalSignerKey');
  const publicKey = (await KeyPairSigner.fromSecretKey(env.NEAR_AGENT_PRIVATE_KEY).getPublicKey()).toString();
  const source = await readFile(resolve(contractDir, 'src/lib.rs'));
  const lock = await readFile(resolve(contractDir, 'Cargo.lock'));
  const local = await readFile(resolve(contractDir, 'target/wasm32-unknown-unknown/release/sentinel_contract.wasm'));
  const exports = WebAssembly.Module.exports(new WebAssembly.Module(local)).filter(x => x.kind === 'function').map(x => x.name).sort();
  report = { ...report, contractId, agentId, publicKey, trialRecipient: recipient, trialAmountYocto: trialAmount.toString(),
    legacyKeyDiffers: !!env.AGENT_PRIVATE_KEY && env.AGENT_PRIVATE_KEY !== env.NEAR_AGENT_PRIVATE_KEY,
    sourceSha256: sha(source).toString('hex'), lockSha256: sha(lock).toString('hex'),
    localWasmSha256: sha(local).toString('hex'), localWasmCodeHash: base58(sha(local)),
    localExports: exports, missingLocalMethods: expected.filter(x => !exports.includes(x)) };
  if (process.argv.includes('--local-only')) {
    report.liveVerification = 'not attempted (local-only)';
  } else {
    const endpoint = new URL(env.NEAR_RPC_URL);
    if (endpoint.protocol !== 'https:' || endpoint.username || endpoint.password) throw new Error('InvalidRpcUrl');
    async function rpc(method, params) {
      if (!['status','block','query','gas_price'].includes(method)) throw new Error('ReadOnlyRpcOnly');
      const response = await fetch(endpoint, { method: 'POST', redirect: 'error',
        headers: { 'Content-Type': 'application/json' }, signal: AbortSignal.timeout(15000),
        body: JSON.stringify({ jsonrpc: '2.0', id: 'sentinel-readonly', method, params }) });
      if (!response.ok) throw new Error('RpcHttpFailure');
      const json = await response.json();
      if (json.error) throw new Error('RpcQueryFailure');
      return json.result;
    }
    const status = await rpc('status', []);
    if (status.chain_id !== 'testnet') throw new Error('WrongChain');
    const block = await rpc('block', { finality: 'final' });
    const block_id = block.header.hash;
    const query = (account_id, request_type, extra = {}) => rpc('query', { block_id, account_id, request_type, ...extra });
    const code = await query(contractId, 'view_code');
    const deployed = Buffer.from(code.code_base64, 'base64');
    const deployedHash = base58(sha(deployed));
    if (code.hash && code.hash !== deployedHash) throw new Error('RpcCodeHashMismatch');
    const deployedExports = WebAssembly.Module.exports(new WebAssembly.Module(deployed)).filter(x => x.kind === 'function').map(x => x.name).sort();
    async function view(method_name, args = {}) {
      if (!['get_config','get_policy','get_daily_usage','get_available_balance','is_recipient_allowed'].includes(method_name)) throw new Error('ReadOnlyViewOnly');
      const result = await query(contractId, 'call_function', { method_name, args_base64: Buffer.from(JSON.stringify(args)).toString('base64') });
      return JSON.parse(Buffer.from(result.result).toString());
    }
    const config = await view('get_config');
    const recipientAccount = await query(recipient, 'view_account');
    const recipientAllowed = await view('is_recipient_allowed', { recipient });
    const policy = await view('get_policy');
    const usage = await view('get_daily_usage');
    const availableBalance = await view('get_available_balance');
    const keys = await query(agentId, 'view_access_key_list');
    // Verify the actual signer key directly, independently of list pagination.
    const specificKey = await query(agentId, 'view_access_key', { public_key: publicKey });
    const { listedKey, permission, listedPermission, queryFailed, confirmed: permissionConfirmed } =
      crossCheckSignerPermission(specificKey, keys.keys, publicKey);
    const identical = local.equals(deployed);
    const { policyPermits, sufficientBalance, policyPreviewDecision } = assessTrialPolicy({
      policy, usage, availableBalance, recipientAllowed, amount: trialAmount });
    const price = BigInt((await rpc('gas_price', { block_id })).gas_price);
    const executionGasAtObservedPrice = 30_000_000_000_000n * price;
    const combinedGasAtObservedPrice = executionGasAtObservedPrice * 2n;
    const { restricted, finiteGasAllowance, allowanceSufficientAtObservedPrice } = inspectSignerPermission(permission, contractId, executionGasAtObservedPrice);
    report = { ...report, chainId: status.chain_id, blockHeight: block.header.height, blockHash: block_id,
      deployedCodeHash: deployedHash, deployedExports,
      missingDeployedMethods: expected.filter(x => !deployedExports.includes(x)),
      sourceIdentity: identical ? 'byte-identical to freshly built local WASM' : 'WASM differs; source equivalence not established (build/optimization differences may explain this)',
      config, policy, dailyUsage: usage, availableBalanceYocto: availableBalance,
      recipientChecks: { exists: !!recipientAccount, allowed: recipientAllowed, policyPermits, sufficientBalance, policyPreviewDecision },
      gasChecks: { gasPriceYoctoPerUnit: price.toString(), combinedGasAtObservedPriceYocto: combinedGasAtObservedPrice.toString(),
        allowanceSufficientAtObservedPrice, withinProposedFeeCeiling: combinedGasAtObservedPrice <= 10_000_000_000_000_000_000_000n },
      configuredAgentMatches: config[1] === agentId,
      agentKeys: { count: keys.keys.length, fullAccessCount: keys.keys.filter(x => x.access_key.permission === 'FullAccess').length,
        listMayBeTruncated: !!keys.last_key, signerKeyPresent: !!listedKey, signerPermission: permission ?? null,
        signerQueryFailed: queryFailed,
        listedSignerPermission: listedPermission ?? null, permissionConfirmedByBothQueries: permissionConfirmed,
        restrictedToExecution: restricted, finiteGasAllowance },
      ...(diagnostics ? { keyDiagnostics: { directQueryResult: specificKey, matchingListEntry: listedKey ?? null, keyListResult: keys } } : {}),
      readyForApprovalReview: identical && permissionConfirmed && restricted && allowanceSufficientAtObservedPrice && config[1] === agentId && config[2] === false &&
        recipientAllowed && policyPermits && sufficientBalance && policyPreviewDecision === 'ALLOW' && combinedGasAtObservedPrice <= 10_000_000_000_000_000_000_000n &&
        report.missingLocalMethods.length === 0 && expected.every(x => deployedExports.includes(x)),
      liveVerification: 'read-only RPC completed; no transaction submitted' };
    await mkdir(reportDir, { recursive: true });
    await writeFile(resolve(reportDir, diagnostics ? 'diagnostic-deployed.wasm' : 'deployed.wasm'), deployed);
    if (!report.readyForApprovalReview || report.missingDeployedMethods.length) process.exitCode = 2;
  }
} catch (error) {
  // Never dump SDK errors or provider bodies: either may contain sensitive inputs.
  const code = error?.cause?.code ?? error?.code;
  report = { ...report, liveVerification: 'blocked', blockerType: error?.name ?? 'Error',
    blockerCode: ['EACCES','EPERM','ENETUNREACH','ETIMEDOUT','UND_ERR_CONNECT_TIMEOUT'].includes(code) ? code : null };
  process.exitCode = 1;
} finally {
  await mkdir(reportDir, { recursive: true });
  await writeFile(resolve(reportDir, reportName), JSON.stringify(report, null, 2));
  console.log(JSON.stringify(report, null, 2));
}
