import {test} from "node:test";
import assert from "node:assert/strict";
import {createServer} from "node:http";
import type {AddressInfo} from "node:net";
import {createExecutor,validateExecution} from "../src/execution.js";
import {requireRestrictedKey} from "../src/keyPermissions.js";
const make=()=>({receiverId:"treasury.testnet",methodName:"execute_delegated_payment",gas:"30000000000000",deposit:"0",args:{id:"p",delegation_id:"d",recipient:"merchant.testnet",amount_yocto:"1",expires_at_ns:(BigInt(Date.now()+60000)*1000000n).toString()},security:{permitId:"11111111-1111-4111-8111-111111111111",intentHash:"a".repeat(64),expiresAtUtc:new Date(Date.now()+20000).toISOString(),signerPublicKey:"ed25519:test"}});
test("delegated method requires explicit execution mode and exact arguments",()=>{
 const c=make();assert.throws(()=>validateExecution(c,"p",c.receiverId));assert.equal(validateExecution(c,"p",c.receiverId,true),c);
 for(const args of [{...c.args,extra:true},{...c.args,id:"q"},{...c.args,amount_yocto:"0"},{...c.args,expires_at_ns:"1"},{...c.args,expires_at_ns:(BigInt(Date.now()+86401000)*1000000n).toString()},{...c.args,recipient:"attacker/near"}])assert.throws(()=>validateExecution({...c,args},"p",c.receiverId,true));
});
test("delegated restricted key cannot include management or owner-approved methods",()=>{
 const permission=(methods:string[])=>({FunctionCall:{receiver_id:"treasury.testnet",method_names:methods,allowance:"100"}});
 requireRestrictedKey(permission(["execute_delegated_payment"]),"treasury.testnet",1n,"execute_delegated_payment");
 for(const names of [[],["execute_payment"],["execute_delegated_payment","create_delegation"],["execute_delegated_payment","execute_payment"]])assert.throws(()=>requireRestrictedKey(permission(names),"treasury.testnet",1n,"execute_delegated_payment"));
});
for(const changed of ["delegation_id","recipient","amount_yocto","expires_at_ns"])test(`consumption changing ${changed} never signs`,async()=>{
 const call=make();let signed=0;
 const server=createServer((req,res)=>{res.setHeader("Content-Type","application/json");
   if(req.url?.endsWith("prepare-execution"))res.end(JSON.stringify(call));
   else res.end(JSON.stringify({...call.security,paymentId:"p",decision:"ALLOW",humanApproved:false,transaction:{...call,args:{...call.args,[changed]:changed==="amount_yocto"?"2":changed==="expires_at_ns"?(BigInt(Date.now()+120000)*1000000n).toString():"different.testnet"}}}));
 });await new Promise<void>(resolve=>server.listen(0,"127.0.0.1",resolve));
 try {const execute=createExecutor({apiUrl:`http://127.0.0.1:${(server.address() as AddressInfo).port}`,workerKey:"fixture",contractId:call.receiverId,delegated:true,expectedSignerPublicKey:"ed25519:test",submit:async()=>{signed++;return {status:{SuccessValue:""}};}});await assert.rejects(execute("p"));assert.equal(signed,0);}
 finally {server.closeAllConnections();await new Promise<void>(resolve=>server.close(()=>resolve()));}
});
