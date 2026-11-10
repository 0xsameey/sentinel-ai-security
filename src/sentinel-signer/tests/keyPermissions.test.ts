import { test } from "node:test";
import assert from "node:assert/strict";
import { requireRestrictedKey } from "../src/keyPermissions.js";
const permission = { FunctionCall: { receiver_id: "treasury.testnet", method_names: ["execute_payment"], allowance: "1000" } };
test("exact restricted key with enough gas allowance is accepted", () => assert.doesNotThrow(() => requireRestrictedKey(permission,"treasury.testnet",100n)));
for (const [name, value] of Object.entries({ fullAccess:"FullAccess", missing:null,
  allMethods:{ FunctionCall:{...permission.FunctionCall,method_names:[]} },
  extraMethod:{ FunctionCall:{...permission.FunctionCall,method_names:["execute_payment","set_policy"]} },
  wrongContract:{ FunctionCall:{...permission.FunctionCall,receiver_id:"evil.testnet"} },
  unlimited:{ FunctionCall:{...permission.FunctionCall,allowance:null} },
  insufficient:{ FunctionCall:{...permission.FunctionCall,allowance:"99"} },
})) test(`signer rejects ${name}`,()=>assert.throws(()=>requireRestrictedKey(value,"treasury.testnet",100n)));
