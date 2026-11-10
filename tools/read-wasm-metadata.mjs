// Runs only the public metadata export in a local WASM sandbox. All host calls
// except value_return trap; no network, keys, storage or blockchain is exposed.
import { readFile, writeFile } from 'node:fs/promises';
import { Worker } from 'node:worker_threads';
const paths = process.argv.slice(2);
if (!paths.length) throw new Error('Provide WASM artifact paths.');
const results = [];
for (const path of paths) {
  const bytes = await readFile(path);
  const metadata = await new Promise((resolve, reject) => {
    const worker = new Worker(`
      const { parentPort, workerData } = require('node:worker_threads');
      try {
        const module = new WebAssembly.Module(workerData);
        const imports = {};
        for (const item of WebAssembly.Module.imports(module)) {
          if (item.kind !== 'function') throw Error('Unsupported import');
          (imports[item.module] ??= {})[item.name] = () => { throw Error('Unexpected host call: ' + item.name); };
        }
        let instance, metadata;
        imports.env.value_return = (length, pointer) => {
          if (Number(length) > 100000) throw Error('Metadata too large');
          metadata = JSON.parse(new TextDecoder().decode(new Uint8Array(instance.exports.memory.buffer, Number(pointer), Number(length))));
        };
        instance = new WebAssembly.Instance(module, imports);
        instance.exports.contract_source_metadata();
        parentPort.postMessage(metadata);
      } catch (error) { parentPort.postMessage({ error: error.message }); }
    `, { eval: true, workerData: bytes });
    const timer = setTimeout(() => { worker.terminate(); reject(new Error('Metadata execution timed out')); }, 10000);
    worker.once('message', value => { clearTimeout(timer); worker.terminate(); resolve(value); });
    worker.once('error', error => { clearTimeout(timer); worker.terminate(); reject(error); });
  });
  results.push({ path, metadata });
}
await writeFile('.test-artifacts/milestone1b/wasm-metadata.json', JSON.stringify(results, null, 2));
console.log(JSON.stringify(results, null, 2));
