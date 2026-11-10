// Offline comparison only. Never invokes cargo, a signer, or an RPC endpoint.
import { readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';
const root = fileURLToPath(new URL('..', import.meta.url));
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const dir = 'contracts/sentinel-contract-starter/sentinel-contract';
const snapshot = JSON.parse(await readFile(resolve(root, '.test-artifacts/milestone1b/testnet-diagnostics.json'), 'utf8'));
const deployed = await readFile(resolve(root, '.test-artifacts/milestone1b/diagnostic-deployed.wasm'));
const artifacts = [];
for (const path of [
  '.test-artifacts/milestone1b/deployed.wasm',
  '.test-artifacts/milestone1b/diagnostic-deployed.wasm',
  '.test-artifacts/milestone1b/prior-target-near.wasm',
  `${dir}/target/near/sentinel_contract.wasm`,
  `${dir}/target/wasm32-unknown-unknown/release/sentinel_contract.wasm`,
  '.test-artifacts/milestone1b/rebuilt-near/sentinel_contract.wasm',
  '.test-artifacts/milestone1b/current-source-cfg-near.wasm',
  '.test-artifacts/milestone1b/current-source-optimized.wasm',
  '.test-artifacts/milestone1b/expiry-differential-optimized.wasm',
]) {
  try {
    const bytes = await readFile(resolve(root, path));
    artifacts.push({ path, bytes: bytes.length, sha256: sha(bytes), matchesDownloadedDeployed: bytes.equals(deployed) });
  } catch (error) {
    if (error.code !== 'ENOENT') throw error;
    artifacts.push({ path, missing: true });
  }
}
const source = await readFile(resolve(root, `${dir}/src/lib.rs`));
let differential = null;
try {
  const variant = await readFile(resolve(root, '.test-artifacts/milestone1b/expiry-differential/src/lib.rs'));
  const removed = await readFile(resolve(root, '.test-artifacts/milestone1b/expiry-differential-change.txt'), 'utf8');
  const variantLock = await readFile(resolve(root, '.test-artifacts/milestone1b/expiry-differential/Cargo.lock'));
  const optimized = artifacts.find(x => x.path.endsWith('/expiry-differential-optimized.wasm'));
  differential = {
    variantSourceSha256: sha(variant),
    onlyChangeIsRecordedExpiryGuardRemoval: removed.includes('const MAX_PAYMENT_LIFETIME_NS') &&
      removed.includes('Payment authorization exceeds 24 hours') &&
      source.toString().replace(removed, '') === variant.toString(),
    variantLockMatchesCurrent: sha(variantLock) === sha(await readFile(resolve(root, `${dir}/Cargo.lock`))),
    optimizedVariantMatchesDeployed: optimized?.matchesDownloadedDeployed === true,
    removedSource: removed,
  };
} catch (error) { if (error.code !== 'ENOENT') throw error; }
const reproduced = differential?.onlyChangeIsRecordedExpiryGuardRemoval && differential?.variantLockMatchesCurrent && differential?.optimizedVariantMatchesDeployed;
const result = { snapshotBlockHeight: snapshot.blockHeight, snapshotBlockHash: snapshot.blockHash,
  currentSourceSha256: sha(source),
  currentSourceMatchesSnapshot: sha(source) === snapshot.sourceSha256,
  currentLockMatchesSnapshot: sha(await readFile(resolve(root, `${dir}/Cargo.lock`))) === snapshot.lockSha256,
  conclusion: reproduced ? 'Security-relevant difference established: current source without its 24-hour authorization ceiling reproduces deployed bytes exactly.' :
    'Current-source build provenance remains unresolved.',
  differential, artifacts };
await writeFile(resolve(root, '.test-artifacts/milestone1b/artifact-comparison.json'), JSON.stringify(result, null, 2));
console.log(JSON.stringify(result, null, 2));
