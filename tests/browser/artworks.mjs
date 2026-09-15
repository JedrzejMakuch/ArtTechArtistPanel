import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';

export async function artworks(page, panel, api, exhibition) {
    const url = path => new URL(path, panel).href;
    const list = `exhibitions/${exhibition.id}/artworks`;
    await page.goto(url(`exhibitions/${exhibition.id}`));
    assert.equal(await page.locator('form').count(), 0);
    await page.getByRole('heading', { name: 'No artworks yet' }).waitFor();
    await page.getByRole('link', { name: 'Add artwork', exact: true }).click();
    await page.getByLabel('Title', { exact: true }).fill('Browser painting');
    await page.getByLabel('Description (optional)', { exact: true }).fill('Artwork <script>plain text</script>');
    await page.getByLabel('Creation year').fill('2025');
    await page.getByLabel('Width (cm)').fill('80.25');
    await page.getByLabel('Height (cm)').fill('60.5');
    await page.getByLabel('Image URL').fill(new URL('dev-assets/artworks/morning-forest.jpg', api).href);
    await page.getByLabel('Display order', { exact: true }).fill('12');
    const response = page.waitForResponse(r => r.url().endsWith(`/api/artist/${list}`) && r.request().method() === 'POST');
    await page.getByRole('button', { name: 'Create artwork', exact: true }).click();
    const created = await (await response).json();
    assert.equal(created.widthCm, 80.25);
    await page.waitForURL(url(`exhibitions/${exhibition.id}`));
    await page.getByRole("link", { name: "Edit artwork", exact: true }).click();
    await page.getByLabel('Title', { exact: true }).fill('Edited painting');
    await page.getByLabel('Width (cm)').fill('42.12');
    await page.getByLabel('Display order', { exact: true }).fill('2');
    await page.getByRole('button', { name: 'Save changes', exact: true }).click();
    await page.waitForURL(url(`exhibitions/${exhibition.id}`));
    await page.getByRole("link", { name: "Edited painting", exact: true }).waitFor();
    const publicUrl = new URL(`api/artworks/${created.id}`, api).href;
    const visible = await (await page.request.get(publicUrl)).json();
    assert.equal(visible.title, 'Edited painting'); assert.equal(visible.widthCm, 42.12);
    assert.equal(visible.imageUrl, created.imageUrl);
    const publicExhibition = await (await page.request.get(new URL(`api/exhibitions/${exhibition.exhibitionCode}`, api).href)).json();
    assert.equal(publicExhibition.artworks[0].sortOrder, 2);
    assert.equal((await page.request.get(created.imageUrl)).status(), 200);
    await page.reload();
    await page.getByRole('link', { name: 'Edit artwork', exact: true }).click();
    await page.getByLabel('Title', { exact: true }).waitFor();
    assert.equal(await page.getByLabel('Title', { exact: true }).inputValue(), 'Edited painting');
    // A real invalid field is rejected locally without losing the rest of the form.
    await page.getByLabel('Width (cm)').fill('0');
    await page.getByRole('button', { name: 'Save changes', exact: true }).click();
    assert.equal(await page.getByLabel('Title', { exact: true }).inputValue(), 'Edited painting');
    await page.getByLabel('Width (cm)').fill('42.12');
    for (const width of [1280, 390, 320]) {
        await page.setViewportSize({ width, height: 900 });
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `Artwork editor overflow ${width}`);
        await page.screenshot({ path: fileURLToPath(new URL(`../../artifacts/artwork-editor-${width}.png`, import.meta.url)), fullPage: true });
    }
    await page.getByRole('link', { name: 'Back to exhibition', exact: true }).click();
    await page.getByRole('link', { name: 'Edited painting', exact: true }).waitFor();
    assert.equal(await page.locator('.exhibition-card script').count(), 0);
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
    for (const width of [320, 390, 1280]) {
        await page.setViewportSize({ width, height: 900 });
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `Populated exhibition details overflow ${width}`);
        await page.screenshot({ path: fileURLToPath(new URL(`../../artifacts/exhibition-details-${width}.png`, import.meta.url)), fullPage: true });
    }
    await page.getByRole('link', { name: 'Edited painting', exact: true }).click();
    await page.getByRole('button', { name: 'Delete artwork', exact: true }).click();
    await page.getByRole('button', { name: 'Cancel', exact: true }).click();
    assert.equal((await page.request.get(publicUrl)).status(), 200);
    await page.getByRole('button', { name: 'Delete artwork', exact: true }).click();
    await page.getByRole('button', { name: 'Confirm deletion', exact: true }).click();
    await page.waitForURL(url(`exhibitions/${exhibition.id}`));
    await page.getByRole('heading', { name: 'No artworks yet' }).waitFor();
    assert.equal((await page.request.get(publicUrl)).status(), 404);
    assert.equal((await (await page.request.get(new URL(`api/exhibitions/${exhibition.exhibitionCode}`, api).href)).json()).artworks.length, 0);
    await page.setViewportSize({ width: 1280, height: 900 });
    console.log('PASS real artwork create/edit/order/public metadata/image/delete, reload, escaped text and responsive editor');
}
