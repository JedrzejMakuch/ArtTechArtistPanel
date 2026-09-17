// Test-only static host. Runs the release output with runtime API configuration.
import http from 'node:http';
import path from 'node:path';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

if (!process.env.ARTTECH_PANEL_URL || !process.env.ARTTECH_API_URL)
    throw new Error('Set ARTTECH_PANEL_URL and ARTTECH_API_URL.');
const address = new URL(process.env.ARTTECH_PANEL_URL);
if (address.protocol !== 'http:') throw new Error('The test-only static host supports loopback HTTP.');
if (!['localhost', '127.0.0.1'].includes(address.hostname)) throw new Error('Use a loopback test origin.');
const root = path.resolve(fileURLToPath(new URL('../../artifacts/publish/wwwroot/', import.meta.url)));
const types = { '.html': 'text/html', '.js': 'text/javascript', '.json': 'application/json',
    '.wasm': 'application/wasm', '.css': 'text/css', '.png': 'image/png', '.svg': 'image/svg+xml', '.dat': 'application/octet-stream' };
http.createServer(async (request, response) => {
    try {
        const pathname = decodeURIComponent(new URL(request.url, address).pathname);
        if (pathname === '/appsettings.json') {
            response.writeHead(200, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' });
            response.end(JSON.stringify({
                Api: { BaseUrl: process.env.ARTTECH_API_URL },
                ShareLinks: { PublicBaseUrl: process.env.ARTTECH_PANEL_URL.replace(/\/$/, '') }
            }));
            return;
        }
        const relative = pathname === '/' || !path.extname(pathname) ? 'index.html' : pathname.slice(1);
        const file = path.resolve(root, relative);
        if (!file.startsWith(root + path.sep)) { response.writeHead(403); response.end(); return; }
        const data = await readFile(file);
        response.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream' });
        response.end(data);
    } catch {
        response.writeHead(404); response.end();
    }
}).listen(Number(address.port), address.hostname, () => console.log('Published panel test host ready.'));
