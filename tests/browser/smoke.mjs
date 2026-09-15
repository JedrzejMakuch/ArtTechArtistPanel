import { chromium } from 'playwright';
import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
import { exhibitions } from './exhibitions.mjs';

// Use only an isolated/disposable backend: this test registers two unique accounts.
const panel = process.env.ARTTECH_PANEL_URL;
const api = process.env.ARTTECH_API_URL;
if (!panel || !api) throw new Error('Set ARTTECH_PANEL_URL and ARTTECH_API_URL for an isolated test environment.');
const base = new URL(panel);
const backend = new URL(api);
const browser = await chromium.launch({ headless: true, channel: process.env.ARTTECH_BROWSER_CHANNEL || 'chrome' });
const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
const page = await context.newPage();
page.setDefaultTimeout(20000);
const errors = [];
page.on('pageerror', e => errors.push(e.message));
let refreshes = 0, publicReads = 0;
let profile404s = 0;
page.on("response", r => { if (new URL(r.url()).pathname === "/api/artist/profile" && r.request().method() === "GET" && r.status() === 404) profile404s++; });
page.on('request', r => {
    const url = new URL(r.url());
    if (url.origin !== backend.origin) return;
    if (url.pathname.endsWith('/refresh')) refreshes++;
    if (url.pathname.includes('/api/profiles/')) {
        publicReads++;
        if (r.headers().authorization) errors.push('Public request unexpectedly included Authorization');
    }
});
const url = path => new URL(path, base).href;
const waitHeading = text => page.getByRole('heading', { name: text, exact: true }).waitFor();
const password = 'Smoke-Only-Password-123!';
const email = `smoke-${Date.now()}@example.test`;
async function register(address) {
    await page.goto(url('register'));
    await page.getByLabel('Email', { exact: true }).fill(address);
    await page.getByLabel('Password', { exact: true }).fill(password);
    await page.getByRole('button', { name: 'Create account', exact: true }).click();
    await waitHeading('Introduce yourself');
}
async function login(address, secret = password) {
    await page.getByLabel('Email', { exact: true }).fill(address);
    await page.getByLabel('Password', { exact: true }).fill(secret);
    await page.getByRole('button', { name: 'Log in', exact: true }).click();
}
try {
    await page.goto(url('profile'));
    await waitHeading('Welcome back');
    console.log('PASS protected route redirects anonymous browser');
    await register(email);
    await page.getByLabel('Display name', { exact: true }).fill('  Browser Artist  ');
    await page.getByLabel('Biography', { exact: false }).fill('First biography');
    await page.getByRole('button', { name: 'Create profile', exact: true }).click();
    await page.locator('.public-preview h3').filter({ hasText: 'Browser Artist' }).waitFor();
    assert.equal(await page.locator('form').count(), 0); assert.equal(profile404s, 1);
    assert.equal(await page.locator('.public-preview .biography').textContent(), 'First biography');
    const beforeEdit = publicReads;
    await page.getByRole('button', { name: 'Edit profile', exact: true }).click();
    await page.locator('#bio').fill('Updated biography <script>plain text</script>');
    await page.getByRole('button', { name: 'Save changes', exact: true }).click();
    await page.locator('.public-preview .biography').filter({ hasText: 'Updated biography' }).waitFor();
    assert.ok(publicReads > beforeEdit);
    assert.equal(await page.locator('.public-preview script').count(), 0);
    console.log('PASS real registration/login, onboarding POST, editing PUT, fresh anonymous verification');
    const beforeReload = refreshes;
    await page.reload();
    await page.locator('.public-preview h3').waitFor();
    assert.ok(refreshes > beforeReload);
    assert.equal(await page.locator('.profile-details .biography').textContent(), 'Updated biography <script>plain text</script>'); assert.equal(await page.locator('form').count(), 0); assert.equal(profile404s, 1);
    console.log('PASS reload restores session through real refresh endpoint');

    // Fault injection only for this check: the retry and /refresh still hit the real API.
    let injected = false;
    await page.route('**/api/artist/profile', async route => {
        if (!injected && route.request().method() === 'PUT') {
            injected = true;
            await route.fulfill({ status: 401, contentType: 'application/problem+json', body: '{"title":"Expired test access token"}' });
        } else await route.continue();
    });
    const before401 = refreshes;
    await page.getByRole('button', { name: 'Edit profile', exact: true }).click();
    await page.locator('#bio').fill('Saved after one auth retry');
    await page.getByRole('button', { name: 'Save changes', exact: true }).click();
    await page.locator('.public-preview .biography').filter({ hasText: 'Saved after one auth retry' }).waitFor();
    assert.equal(refreshes, before401 + 1);
    await page.unroute('**/api/artist/profile');
    console.log('PASS injected 401 causes one real refresh and successful write retry');

    const exhibitionId = await exhibitions(page, base, backend);

    await page.setViewportSize({ width: 390, height: 844 });
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
    await mkdir(new URL('../../artifacts/', import.meta.url), { recursive: true });
    await page.screenshot({ path: new URL('../../artifacts/profile-mobile.png', import.meta.url).pathname.replace(/^\/(\w:)/, '$1'), fullPage: true });
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.screenshot({ path: new URL('../../artifacts/profile-desktop.png', import.meta.url).pathname.replace(/^\/(\w:)/, '$1'), fullPage: true });
    await page.getByRole('button', { name: 'Log out', exact: true }).click();
    await waitHeading('Welcome back');
    assert.equal(await page.evaluate(() => Object.keys(sessionStorage).filter(x => x.startsWith('arttech.refresh:')).length), 0);
    await page.goto(url('profile'));
    await waitHeading('Welcome back');
    for (const path of ['exhibitions', 'exhibitions/new', `exhibitions/${exhibitionId}`, `exhibitions/${exhibitionId}/artworks`, `exhibitions/${exhibitionId}/artworks/new`, `exhibitions/${exhibitionId}/artworks/00000000-0000-0000-0000-000000000001`]) {
        await page.goto(url(path));
        await waitHeading('Welcome back');
    }
    await login(email, 'Incorrect-password-123!');
    await page.getByRole('alert').waitFor();
    await login(email);
    await page.locator('.public-preview h3').waitFor();
    assert.equal(await page.locator('form').count(), 0); assert.equal(profile404s, 1);
    console.log('PASS existing profile has no 404s on reload/login/navigation');
    console.log('PASS logout clears storage, protected navigation, invalid and existing-user login');
    await page.getByRole('button', { name: 'Log out', exact: true }).click();
    await waitHeading('Welcome back');
    await register(`second-${Date.now()}@example.test`);
    assert.equal(await page.locator('#display-name').inputValue(), '');
    console.log('PASS second account has separate onboarding');
    await page.goto(url('exhibitions'));
    await page.getByRole('link', { name: 'Set up my profile', exact: true }).waitFor();
    await page.goto(url(`exhibitions/${exhibitionId}`));
    await page.getByRole('alert').filter({ hasText: 'Exhibition not found' }).waitFor();
    assert.equal(await page.locator('form').count(), 0);
    console.log('PASS missing-profile guidance and cross-owner exhibition rejection');
    for (const suffix of ['', '/new', '/00000000-0000-0000-0000-000000000001']) {
        await page.goto(url(`exhibitions/${exhibitionId}/artworks${suffix}`));
        await page.getByRole('alert').filter({ hasText: 'Exhibition not found' }).waitFor();
        assert.equal(await page.locator('form').count(), 0);
    }
    console.log('PASS protected artwork routes and cross-owner rejection');
    await page.evaluate(() => {
        for (const key of Object.keys(sessionStorage)) if (key.startsWith('arttech.refresh:')) sessionStorage.setItem(key, 'invalid');
    });
    await page.reload();
    await waitHeading('Welcome back');
    assert.equal(await page.evaluate(() => Object.keys(sessionStorage).filter(x => x.startsWith('arttech.refresh:')).length), 0);
    console.log('PASS rejected refresh clears session');
    assert.deepEqual(errors, []);
    console.log('PASS no browser runtime errors; desktop/mobile screenshots saved in artifacts');
} catch (error) {
    console.error('Browser errors:', errors);
    console.error('Visible page:', (await page.locator('body').innerText()).slice(0, 3000));
    throw error;
} finally {
    await browser.close();
}
