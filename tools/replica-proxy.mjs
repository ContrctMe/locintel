#!/usr/bin/env node
// A round-robin reverse proxy with no dependencies, for the replica stack
// (tools/replica-stack.sh): the load balancer a production topology puts in
// front of N api replicas, reduced to what the fleet suite needs - spread
// requests, stream bodies, and skip an upstream that refuses the connection
// (a replica the suite killed on purpose).
//
//   node tools/replica-proxy.mjs <port> <upstream>...
//
import http from 'node:http';

const [portArg, ...upstreams] = process.argv.slice(2);
if (!portArg || upstreams.length === 0) {
  console.error('usage: node tools/replica-proxy.mjs <port> <upstream>...');
  process.exit(1);
}
const targets = upstreams.map((u) => new URL(u));
// keep-alive per upstream: a new socket per request runs out of ephemeral
// ports at the rates the healthz floor reaches
const agent = new http.Agent({ keepAlive: true, maxSockets: 512 });
let next = 0;

const server = http.createServer((req, res) => {
  const body = [];
  req.on('data', (chunk) => body.push(chunk));
  req.on('end', () => forward(req, res, Buffer.concat(body), targets.length));
});

function forward(req, res, body, attemptsLeft) {
  const target = targets[next++ % targets.length];
  const upstream = http.request(
    {
      host: target.hostname,
      port: target.port,
      method: req.method,
      path: req.url,
      agent,
      headers: { ...req.headers, host: req.headers.host ?? `${target.hostname}:${target.port}` },
    },
    (answer) => {
      res.writeHead(answer.statusCode ?? 502, answer.headers);
      answer.pipe(res);
    },
  );
  upstream.on('error', (error) => {
    // a dead replica is skipped, not reported: the balancer's job
    if (error.code === 'ECONNREFUSED' && attemptsLeft > 1) return forward(req, res, body, attemptsLeft - 1);
    if (!res.headersSent) res.writeHead(502, { 'content-type': 'text/plain' });
    res.end(`proxy: ${error.code ?? error.message}`);
  });
  upstream.end(body);
}

server.listen(Number(portArg), '127.0.0.1', () => {
  console.log(`replica proxy on http://127.0.0.1:${portArg} -> ${upstreams.join(', ')}`);
});
