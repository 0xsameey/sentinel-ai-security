// Never serialize exceptions: SDK messages/bodies can contain credentials or input.
const providerCodes = new Set(['invalid_api_key', 'model_not_found', 'insufficient_quota',
  'rate_limit_exceeded', 'invalid_request_error', 'unsupported_parameter',
  'unsupported_value', 'invalid_value', 'context_length_exceeded', 'server_error',
  'access_terminated', 'account_deactivated', 'billing_hard_limit_reached']);
const networkCodes = new Set(['ENOTFOUND', 'EAI_AGAIN', 'ECONNREFUSED', 'ECONNRESET',
  'ETIMEDOUT', 'CERT_HAS_EXPIRED', 'DEPTH_ZERO_SELF_SIGNED_CERT',
  'SELF_SIGNED_CERT_IN_CHAIN', 'UNABLE_TO_VERIFY_LEAF_SIGNATURE',
  'UNABLE_TO_GET_ISSUER_CERT_LOCALLY', 'UND_ERR_CONNECT_TIMEOUT']);

export function smokeFailure(error, stage, timedOut = false, provider = process.env.SENTINEL_AGENT_PROVIDER ?? 'openai') {
  const status = Number.isInteger(error?.status) && error.status >= 100 && error.status <= 599
    ? error.status : null;
  const code = error?.code ?? error?.error?.code;
  let category = 'unexpected_error';
  if (stage === 'configuration') category = 'configuration_error';
  else if (stage === 'provider_setup') category = 'sdk_or_configuration_error';
  else if (stage === 'validation') category = 'invalid_tool_response';
  else if (timedOut || ['APIConnectionTimeoutError', 'TimeoutError'].includes(error?.name)) category = 'timeout';
  else if (error?.name === 'APIUserAbortError' || error?.name === 'AbortError') category = 'aborted';
  else if (status === 401) category = 'authentication_error';
  else if (status === 403) category = 'permission_error';
  else if (status === 404) category = 'model_or_endpoint_not_found';
  else if (status === 429) category = code === 'insufficient_quota' ? 'quota_error' : 'rate_limit_error';
  else if (status >= 500) category = 'provider_error';
  else if (status) category = 'request_error';
  else if (error?.name === 'APIConnectionError') category = 'connection_error';
  else if (stage === 'request') category = 'sdk_or_model_response_error';
  const networkCode = [error?.cause?.code, error?.cause?.cause?.code, error?.code]
    .find(value => networkCodes.has(value));
  return {event:'REAL_MODEL_SMOKE_FAIL', provider:['openai','anthropic'].includes(provider) ? provider : 'unsupported', stage, category,
    httpStatus:status, providerCode:code == null ? null : providerCodes.has(code) ? code : 'redacted_unknown_code',
    networkCode:networkCode ?? null, signed:false};
}
