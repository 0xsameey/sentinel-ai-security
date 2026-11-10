import OpenAI from 'openai';
export function createClient(apiKey) {
  return new OpenAI({apiKey,baseURL:'https://api.openai.com/v1',maxRetries:0,timeout:30000});
}
