/**
 * SysBot WebAPI CORS Proxy — Cloudflare Worker
 *
 * Forwards requests to your SysBot WebAPI and adds CORS headers
 * so a browser-hosted frontend can talk to it.
 *
 * Setup:
 *   1. Set the SYSBOT_URL secret to your SysBot WebAPI address:
 *      wrangler secret put SYSBOT_URL
 *      (e.g. http://YOUR_IP:5000  or  https://your-ngrok-url.ngrok-free.app)
 *   2. Deploy: wrangler deploy
 */

const CORS = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
  'Access-Control-Allow-Headers': 'Content-Type',
};

export default {
  async fetch(request, env) {
    if (request.method === 'OPTIONS') {
      return new Response(null, { headers: CORS });
    }

    const url = new URL(request.url);
    const target = `${env.SYSBOT_URL}${url.pathname}${url.search}`;

    try {
      const upstream = await fetch(target, {
        method: request.method,
        headers: { 'Content-Type': 'application/json' },
        body: request.method !== 'GET' ? await request.text() : undefined,
      });

      const body = await upstream.text();
      return new Response(body, {
        status: upstream.status,
        headers: {
          'Content-Type': upstream.headers.get('Content-Type') ?? 'application/json',
          ...CORS,
        },
      });
    } catch (err) {
      return new Response(JSON.stringify({ error: 'Bot unreachable', detail: err.message }), {
        status: 502,
        headers: { 'Content-Type': 'application/json', ...CORS },
      });
    }
  },
};
