// LOCAL TEST FIXTURE ONLY. This is not a NEAR VM or a cryptographic verifier.
const http = require('http');
const historyTime = (BigInt(Date.now() - 86400000) * 1000000n).toString();
const reviewed = Buffer.from('34d9ad95449b50a9ad4005d14a950b9d79b2b677ac5588aefc8640f61d15784f','hex');
let n=BigInt('0x'+reviewed.toString('hex')), reviewedHash='';
while(n){ reviewedHash='123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz'[Number(n%58n)]+reviewedHash; n/=58n; }

http.createServer(async (req, res) => {
  const reply = (status, body) => { res.writeHead(status, { 'Content-Type': 'application/json' }); res.end(JSON.stringify(body)); };
  let state = pm.state.get('sentinelNear') || { payments: {}, delegations: {}, paymentDelegations: {}, transactions: {}, paused: false, calls: [] };
  const body = req.body || {};
  if (req.url === '/health') return reply(200, { ok: true });
  if (req.url === '/control' && req.method === 'POST') {
    if (body.payment) state.payments[body.payment.id] = body.payment;
    if (body.delegation) { state.delegations ||= {}; state.delegations[body.delegation.id] = body.delegation.state; }
    if (body.paymentDelegation) { state.paymentDelegations ||= {}; state.paymentDelegations[body.paymentDelegation.id] = body.paymentDelegation.delegationId; }
    if (body.transaction) state.transactions[body.transaction.transaction.hash] = body.transaction;
    if (typeof body.paused === 'boolean') state.paused = body.paused;
    for (const key of ['historyUnavailable','recipientAllowed','keyMissing','keyFullAccess','rpcUnavailable','policy','usage','contractCodeHash','balance','keyMethods','agent']) {
      if (key in body) state[key] = body[key];
    }
    pm.state.set('sentinelNear', state); return reply(200, { ok: true });
  }
  if (req.url === '/calls') return reply(200, state.calls);
  if (req.method === 'GET' && req.url.startsWith('/v3/accounts/')) {
    state.calls.push({ method: 'GET', route: 'nearblocks-v3-history' }); pm.state.set('sentinelNear', state);
    if (state.historyUnavailable) return reply(503,{data:null,errors:[{message:'fixture outage'}]});
    return reply(200,{data:[{transaction_hash:'11111111111111111111111111111111',block:{block_timestamp:historyTime},outcomes:{status:true}}],meta:{}});
  }
  if (req.method !== 'POST' || !body.jsonrpc) return reply(501, { error: 'Not modeled' });
  if (state.rpcUnavailable) return reply(503,{error:'fixture RPC outage'});
  const p = body.params || {};
  state.calls.push({ method: body.method, request_type: p.request_type, method_name: p.method_name });
  pm.state.set('sentinelNear', state);
  const result = value => reply(200, { jsonrpc: '2.0', id: body.id, result: value });
  if (body.method === 'status') return result({chain_id:'testnet'});
  if (body.method === 'block') return result({header:{hash:'11111111111111111111111111111111',height:1,timestamp_nanosec:(BigInt(Date.now())*1000000n).toString()}});
  if (body.method === 'gas_price') return result({gas_price:'100000000'});
  if (body.method === 'query' && p.request_type === 'view_account') return result({amount:'1000000000000000000000000',code_hash:p.account_id==='treasury.testnet'?(state.contractCodeHash||reviewedHash):'11111111111111111111111111111111'});
  if (body.method === 'query' && p.request_type === 'view_access_key') {
    if (state.keyMissing) return result({error:'access key does not exist while viewing'});
    return result({permission:state.keyFullAccess?'FullAccess':{FunctionCall:{receiver_id:'treasury.testnet',method_names:state.keyMethods||['execute_payment'],allowance:'10000000000000000000000'}}});
  }
  if (body.method === 'tx') {
    const tx = state.transactions[p.tx_hash];
    return tx ? result(tx) : reply(200, { jsonrpc: '2.0', id: body.id, error: { cause: { name: 'UNKNOWN_TRANSACTION' } } });
  }
  if (body.method !== 'query' || p.request_type !== 'call_function') return reply(501, { error: 'Not modeled' });
  const args = JSON.parse(Buffer.from(p.args_base64, 'base64').toString());
  let value;
  switch (p.method_name) {
    case 'get_config': value = ['owner.testnet', state.agent || 'agent.testnet', state.paused]; break;
    case 'get_policy': value = state.policy || { max_per_transaction: '1000', daily_limit: '10000' }; break;
    case 'get_daily_usage': value = state.usage || ['1', '0', '0', '0']; break;
    case 'is_recipient_allowed': value = state.recipientAllowed ?? (args.recipient === 'merchant.testnet'); break;
    case 'get_available_balance': value = state.balance || '1000000'; break;
    case 'get_delegation': value = (state.delegations || {})[args.id] || null; break;
    case 'get_payment_delegation': value = (state.paymentDelegations || {})[args.id] || null; break;
    case 'get_payment': value = state.payments[args.id] || null; break;
    default: return reply(501, { error: 'Not modeled' });
  }
  return result({ result: Array.from(Buffer.from(JSON.stringify(value))), logs: [] });
}).listen(process.env.PORT,'127.0.0.1');
