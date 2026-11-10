import test from 'node:test';
import assert from 'node:assert/strict';
import React from 'react';
import {renderToStaticMarkup} from 'react-dom/server';
import {loadSource,inertApi,blockedSigning,dataModule} from './render-helper.mjs';
const signingGuard=dataModule('export const updateSpendingPolicy=()=>{throw Error("SIGNING_FORBIDDEN")}; export const updateRecipient=updateSpendingPolicy; export const updatePauseStatus=updateSpendingPolicy;');
const agent=await loadSource('components/AgentTaskPanel',{'../api':inertApi});
const delegations=await loadSource('components/DelegationManager',{'../api':inertApi,'../services/nearTransactions':blockedSigning});
const policies=await loadSource('components/PolicyManager',{'../services/policyTransactions':signingGuard});
const activity=await loadSource('components/PaymentActivity');
const loggedOut=dataModule('export const useWalletAuth=()=>({accountId:null,token:null,loading:false,error:null,connect:()=>{throw Error("WALLET_TOUCHED")},authenticate:()=>{throw Error("WALLET_TOUCHED")},disconnect:()=>{throw Error("WALLET_TOUCHED")}});');
const emptyActivity=dataModule('export const usePaymentActivity=()=>({views:[],payments:[],loading:false,error:""});');
const overrides={'./components/AgentTaskPanel':agent,'./components/DelegationManager':delegations,'./components/PolicyManager':policies,'./components/PaymentActivity':activity,'./services/nearTransactions':blockedSigning,'./hooks/useWalletAuth':loggedOut,'./hooks/usePaymentActivity':emptyActivity,'./api':inertApi};
const {default:App}=await import(await loadSource('App',overrides));
for(const [page,label] of [['overview','Overview'],['assessment','Risk assessment'],['agent','AI payments'],['payments','Transactions'],['policies','Security &amp; delegations']])test(`${page} renders independently without wallet or transaction side effects`,()=>{
 const html=renderToStaticMarkup(React.createElement(App,{initialPage:page}));
 assert(html.includes(`<h1>${label}</h1>`));
 const pages=[...html.matchAll(/<div[^>]*class="page-view(?: assessment-layout)?"[^>]*>/g)].map(match=>match[0]);
 assert.equal(pages.filter(tag=>!tag.includes('hidden=""')).length,1,'only one primary page visible');
 assert(!html.includes('SIGNING_FORBIDDEN'));assert(!html.includes('WALLET_TOUCHED'));
 assert(html.includes('Skip to content'));assert(html.includes('Connect wallet'));
});
for(const signedIn of [false,true])test(`wallet ${signedIn?'authenticated':'connected but unsigned'} presentation uses only the existing auth actions`,async()=>{
 const auth=dataModule(`export const useWalletAuth=()=>({accountId:'owner.testnet',token:${signedIn?"'synthetic-token'":"null"},loading:false,error:null,connect:()=>{throw Error('WALLET_TOUCHED')},authenticate:()=>{throw Error('WALLET_TOUCHED')},disconnect:()=>{throw Error('WALLET_TOUCHED')}});`);
 const {default:WalletApp}=await import(await loadSource('App',{...overrides,'./hooks/useWalletAuth':auth}));
 const html=renderToStaticMarkup(React.createElement(WalletApp));
 assert(html.includes('owner.testnet'));assert(html.includes('Disconnect'));
 assert(html.includes(signedIn?'Signed in':'Sign in'));
 assert(!html.includes('Connect wallet'));assert(!html.includes('WALLET_TOUCHED'));
});
test('overview visible content is a summary, not a long form',()=>{
 const html=renderToStaticMarkup(React.createElement(App));
 const visible=html.split('aria-label="Overview">')[1].split('<div hidden=""')[0];
 assert(!visible.includes('<form'));assert(!visible.includes('<textarea'));
 assert(visible.includes('AI payments. Your rules.'));assert(visible.includes('Sign in to view'));
 assert(!visible.includes('0.001 NEAR settled'));
});
