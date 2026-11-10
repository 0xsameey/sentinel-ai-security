import {test} from 'node:test';
import assert from 'node:assert/strict';
import {smokeFailure} from '../demo/smoke-errors.mjs';

test('smoke diagnostics classify provider failures without exposing exception data',()=>{
  for(const [status,code,category] of [[401,'invalid_api_key','authentication_error'],
    [404,'model_not_found','model_or_endpoint_not_found'],[429,'insufficient_quota','quota_error'],
    [400,'unsupported_parameter','request_error'],[503,'server_error','provider_error']]) {
    const diagnostic=smokeFailure({status,code,message:'SECRET',headers:{authorization:'SECRET'},
      error:{message:'SECRET'},stack:'SECRET'},'request');
    assert.equal(diagnostic.category,category);
    assert.equal(diagnostic.httpStatus,status);
    assert.equal(diagnostic.providerCode,code);
    assert.ok(!JSON.stringify(diagnostic).includes('SECRET'));
  }
  assert.equal(smokeFailure({code:'SECRET'},'request').providerCode,'redacted_unknown_code');
});

test('smoke diagnostics distinguish timeout, network and local failures',()=>{
  assert.equal(smokeFailure(null,'configuration',false,'anthropic').provider,'anthropic');
  assert.equal(smokeFailure(null,'configuration',false,'secret-value').provider,'unsupported');
  assert.equal(smokeFailure({},'request',true).category,'timeout');
  const network=smokeFailure({name:'APIConnectionError',cause:{code:'ENOTFOUND',message:'SECRET'}},'request');
  assert.equal(network.category,'connection_error');
  assert.equal(network.networkCode,'ENOTFOUND');
  assert.equal(network.httpStatus,null);
  assert.equal(smokeFailure(null,'configuration').category,'configuration_error');
  assert.equal(smokeFailure({},'provider_setup').category,'sdk_or_configuration_error');
  assert.equal(smokeFailure({},'validation').category,'invalid_tool_response');
});
