import Anthropic from '@anthropic-ai/sdk';
export function createClient(apiKey) { return new Anthropic({apiKey,baseURL:'https://api.anthropic.com',maxRetries:0,timeout:30000}); }
