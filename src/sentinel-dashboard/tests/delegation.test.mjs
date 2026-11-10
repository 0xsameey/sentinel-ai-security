import test from 'node:test';
import assert from 'node:assert/strict';
import React from 'react';
import {renderToStaticMarkup} from 'react-dom/server';
import {loadSource,inertApi,blockedSigning} from './render-helper.mjs';
const {default:Component}=await import(await loadSource('components/DelegationManager',{'../api':inertApi,'../services/nearTransactions':blockedSigning}));
const props={token:'synthetic-token',accountId:'owner.testnet',agent:'agent.testnet',isOwner:true};
test('delegation management explains limits and requires explicit owner confirmation',()=>{
 const html=renderToStaticMarkup(React.createElement(Component,{...props,section:'management'}));
 for(const text of ['Total budget','Daily limit','Maximum payment','Expires after','Allowed recipients','Create allowance in wallet','Check delegation'])assert(html.includes(text),text);
 assert.match(html,/<button[^>]*disabled=""[^>]*>Create allowance in wallet/);
 assert(html.includes('Failed payment attempts may still consume'));
 assert(!html.includes('Ask AI to make a payment'));
});
test('unauthenticated rendering explains owner requirement and disables creation',()=>{
 const html=renderToStaticMarkup(React.createElement(Component,{...props,token:null,accountId:null,isOwner:false}));
 assert(html.includes('Sign in with the contract owner wallet'));
 assert.match(html,/<button[^>]*disabled=""[^>]*>Create allowance in wallet/);
});
test('AI payment view does not show delegation creation or submit during render',()=>{
 const html=renderToStaticMarkup(React.createElement(Component,{...props,section:'payments'}));
 assert(!html.includes('Create spending allowance'));assert(!html.includes('Signing attempted'));
 assert(html.includes('Permitted execution'));assert(html.includes('Check delegation'));
});
test('untrusted account text remains escaped',()=>{
 const html=renderToStaticMarkup(React.createElement(Component,{...props,agent:'<script>alert(1)</script>'}));
 assert(!html.includes('<script>'));assert(html.includes('&lt;script&gt;'));
});
