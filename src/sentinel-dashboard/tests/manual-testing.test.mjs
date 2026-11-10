import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {transform} from 'rolldown/experimental';

const stub='data:text/javascript,'+encodeURIComponent('export const actionCreators={}; export const CONTRACT_ID="example.testnet"; export function getWalletSelector(){throw Error("WALLET_TOUCHED")}; export const setupWalletSelector=getWalletSelector; export const setupMeteorWallet=getWalletSelector; export const setupModal=getWalletSelector;');
async function load(name){
 let source=await readFile(new URL(`../src/services/${name}.ts`,import.meta.url),'utf8');
 source=source.replaceAll('import.meta.env.VITE_MANUAL_TESTING','"true"').replaceAll('import.meta.env.VITE_NEAR_CONTRACT_ID','"example.testnet"');
 let js=(await transform(`${name}.ts`,source)).code;
 js=js.replace(/import "@near-wallet-selector\/modal-ui\/styles.css";?/g,'');
 js=js.replace(/"(?:@near-wallet-selector\/(?:core|meteor-wallet|modal-ui)|\.\/nearWallet)"/g,JSON.stringify(stub));
 return import('data:text/javascript;base64,'+Buffer.from(js).toString('base64'));
}
test('manual mode rejects transactions before accessing wallet',async()=>{
 const module=await load('nearTransactions');
 assert.equal(module.MANUAL_TESTING,true);
 await assert.rejects(module.signContractCall({},'owner.testnet'),/transaction signing is disabled/);
});
test('manual mode rejects new wallet connection before SDK key generation',async()=>{
 const module=await load('nearWallet');
 await assert.rejects(module.openWalletModal(),/existing wallet sessions only/);
});
