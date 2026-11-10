// Local fallback for restricted environments where Postman cannot initialize
// ~/.postman dataset storage. Executes the SAME handler, with in-memory pm.state.
const http = require('node:http');
const state = new Map();
global.pm = { state: { get: key => state.get(key), set: (key, value) => state.set(key, value) } };
const createServer = http.createServer;
http.createServer = handler => createServer(async (req, res) => {
  if (req.url !== '/near' && !req.url.startsWith('/near/')) { res.writeHead(501); res.end(); return; }
  req.url = req.url.slice('/near'.length);
  let body = '';
  for await (const chunk of req) body += chunk;
  try { req.body = body ? JSON.parse(body) : {}; await handler(req, res); }
  catch { res.writeHead(500); res.end('{}'); }
});
require('../postman/mocks/near/default.js');
