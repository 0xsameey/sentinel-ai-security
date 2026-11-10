import { test } from 'node:test';
import assert from 'node:assert/strict';
import { assessTrialPolicy, inspectSignerPermission, crossCheckSignerPermission } from '../tools/verification-checks.mjs';

const contract = 'treasury.testnet';
const permission = { FunctionCall: { receiver_id: contract, method_names: ['execute_payment'], allowance: '1000' } };
const publicKey = 'ed25519:test-public-key';
const list = [{ public_key: publicKey, access_key: { permission } }];
test('matching direct and listed permissions are confirmed', () =>
  assert.equal(crossCheckSignerPermission({ permission }, list, publicKey).confirmed, true));
test('missing direct permission stays unverified despite a valid list entry', () =>
  assert.equal(crossCheckSignerPermission({}, list, publicKey).confirmed, false));
test('conflicting direct and listed permissions fail closed', () =>
  assert.equal(crossCheckSignerPermission({ permission: 'FullAccess' }, list, publicKey).confirmed, false));
test('another key on the same account cannot establish signer permission', () =>
  assert.equal(crossCheckSignerPermission({ permission }, list, 'ed25519:different').confirmed, false));
test('provider result.error for a missing key is explicitly a query failure', () => {
  const result = crossCheckSignerPermission({ error: 'access key does not exist while viewing', logs: [] }, [], publicKey);
  assert.equal(result.queryFailed, true);
  assert.equal(result.confirmed, false);
  assert.equal(result.listedKey, undefined);
});
test('provider errors fail closed even if permissions are also present', () =>
  assert.equal(crossCheckSignerPermission({ permission, error: 'query failed' }, list, publicKey).confirmed, false));
test('exact execution permission and adequate gas allowance pass', () => {
  assert.deepEqual(inspectSignerPermission(permission, contract, 100n),
    { restricted: true, finiteGasAllowance: true, allowanceSufficientAtObservedPrice: true });
});
for (const [name, value] of [
  ['full access', 'FullAccess'],
  ['wrong receiver', { FunctionCall: { ...permission.FunctionCall, receiver_id: 'other.testnet' } }],
  ['all methods', { FunctionCall: { ...permission.FunctionCall, method_names: [] } }],
  ['extra method', { FunctionCall: { ...permission.FunctionCall, method_names: ['execute_payment','set_policy'] } }],
]) test(`reject ${name}`, () => assert.equal(inspectSignerPermission(value, contract, 100n).restricted, false));
test('unlimited or insufficient gas allowance does not pass the allowance gate', () => {
  for (const allowance of [null, '0', '99'])
    assert.equal(inspectSignerPermission({ FunctionCall: { ...permission.FunctionCall, allowance } }, contract, 100n).allowanceSufficientAtObservedPrice, false);
});
const trial = { policy: { max_per_transaction: '1000', daily_limit: '10000' },
  usage: ['1','0','0','0'], availableBalance: '10000', recipientAllowed: true, amount: 100n };
test('small allowlisted payment fits budgets and treasury', () => {
  assert.deepEqual(assessTrialPolicy(trial), { policyPermits: true, sufficientBalance: true, policyPreviewDecision: 'ALLOW' });
});
test('unapproved recipient blocks the preview', () => assert.equal(assessTrialPolicy({ ...trial, recipientAllowed: false }).policyPreviewDecision, 'BLOCK'));
test('authorization or in-flight execution usage consumes the daily budget', () => {
  for (const usage of [['1','9950','0','0'], ['1','0','9900','50']])
    assert.equal(assessTrialPolicy({ ...trial, usage }).policyPermits, false);
});
test('near-limit amount requires review', () => assert.equal(assessTrialPolicy({ ...trial, amount: 800n }).policyPreviewDecision, 'REVIEW'));
test('insufficient treasury is independently detected', () => assert.equal(assessTrialPolicy({ ...trial, availableBalance: '99' }).sufficientBalance, false));
test('malformed RPC amounts fail closed', () => {
  for (const balance of ['-1','1.2','340282366920938463463374607431768211456'])
    assert.throws(() => assessTrialPolicy({ ...trial, availableBalance: balance }));
});
