import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { artworks } from './artworks.mjs';

// Called after real registration and profile onboarding by smoke.mjs.
export async function exhibitions(page, panel, api) {
    const url = path => new URL(path, panel).href;
    const status = text => page.locator('.exhibition-status').filter({ hasText: new RegExp(`^${text}$`) }).waitFor();
    await page.getByRole('link', { name: 'Exhibitions', exact: true }).click();
    await page.getByRole('heading', { name: 'No exhibitions yet', exact: true }).waitFor();
    await page.getByRole('link', { name: 'New exhibition', exact: true }).click();
    await page.getByLabel('Title', { exact: true }).fill('Browser exhibition');
    await page.getByLabel('Description', { exact: false }).fill('A real draft');
    await page.getByLabel('Display order', { exact: true }).fill('3');
    const createdResponse = page.waitForResponse(r => r.url().endsWith('/api/artist/exhibitions') && r.request().method() === 'POST');
    await page.getByRole('button', { name: 'Create draft', exact: true }).click();
    const created = await (await createdResponse).json();
    assert.equal(created.status, 'draft');
    await page.waitForURL(url(`exhibitions/${created.id}`));
    await status('Draft');
    assert.equal((await page.request.get(new URL(`api/exhibitions/${created.exhibitionCode}`, api).href)).status(), 404);
    assert.equal(await page.locator('form').count(), 0);
    await page.getByRole('button', { name: 'Edit exhibition', exact: true }).click();
    await page.getByLabel('Title', { exact: true }).fill('Edited browser exhibition');
    await page.getByLabel('Description', { exact: false }).fill('Description <script>plain text</script>');
    await page.getByRole('button', { name: 'Save changes', exact: true }).click();
    await page.getByText('Exhibition saved.', { exact: true }).waitFor();
    await page.getByRole('button', { name: 'Publish', exact: true }).click();
    await status('Published');
    assert.ok((await page.locator('.exhibition-code').innerText()).includes(created.exhibitionCode));
    const publicResponse = await page.request.get(new URL(`api/exhibitions/${created.exhibitionCode}`, api).href);
    assert.equal(publicResponse.status(), 200);
    assert.equal((await publicResponse.json()).title, 'Edited browser exhibition');
    await page.reload();
    await status('Published');
    assert.equal(await page.locator('.exhibition-details h2').textContent(), 'Edited browser exhibition'); assert.equal(await page.locator('form').count(), 0);
    await page.getByRole('button', { name: 'Deactivate', exact: true }).click();
    await page.getByRole('button', { name: 'Cancel', exact: true }).click();
    await status('Published');
    await page.getByRole('button', { name: 'Deactivate', exact: true }).click();
    await page.getByRole('button', { name: 'Confirm deactivation', exact: true }).click();
    await status('Deactivated');
    assert.equal((await page.request.get(new URL(`api/exhibitions/${created.exhibitionCode}`, api).href)).status(), 404);
    await page.getByRole('button', { name: 'Republish', exact: true }).click();
    await status('Published');
    assert.equal((await page.request.get(new URL(`api/exhibitions/${created.exhibitionCode}`, api).href)).status(), 200);
    console.log('PASS real exhibition draft/create/edit/publish/deactivate/republish, stable code, anonymous visibility and reload');

    await page.getByRole('link', { name: 'Exhibitions', exact: true }).click();
    await status('Published');
    assert.equal(await page.locator('.exhibition-description').textContent(), 'Description <script>plain text</script>');
    assert.equal(await page.locator('.exhibition-card script').count(), 0);
    await page.getByRole('button', { name: 'Deactivate', exact: true }).click();
    await page.getByRole('button', { name: 'Confirm deactivation', exact: true }).click();
    await status('Deactivated');
    // Simulate another tab publishing first. Both requests reach the real backend;
    // the UI's request receives a genuine 409 and must re-fetch the list.
    const publishRoute = `**/api/artist/exhibitions/${created.id}/publish`;
    await page.route(publishRoute, async route => {
        const concurrent = await route.fetch();
        assert.equal(concurrent.status(), 200);
        await route.continue();
    });
    await page.getByRole('button', { name: 'Republish', exact: true }).click();
    await page.getByRole('alert').filter({ hasText: 'Invalid exhibition transition' }).waitFor();
    await status('Published');
    await page.unroute(publishRoute);
    console.log('PASS list lifecycle controls and genuine backend 409 with authoritative re-fetch');

    await mkdir(new URL('../../artifacts/', import.meta.url), { recursive: true });
    for (const width of [1280, 390, 320]) {
        await page.setViewportSize({ width, height: 900 });
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `List overflow at ${width}px`);
        await page.screenshot({ path: fileURLToPath(new URL(`../../artifacts/exhibitions-${width}.png`, import.meta.url)), fullPage: true });
    }
    await page.getByRole('link', { name: 'Open exhibition', exact: true }).click();
    await status('Published');
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
    await page.screenshot({ path: fileURLToPath(new URL('../../artifacts/exhibition-editor-320.png', import.meta.url)), fullPage: true });
    await page.setViewportSize({ width: 1280, height: 900 });
    await artworks(page, panel, api, created);
    await page.getByRole('link', { name: 'My profile', exact: true }).click();
    await page.locator('.public-preview h3').waitFor();
    return created.id;
}
