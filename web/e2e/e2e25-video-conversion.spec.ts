import { test, expect } from '@playwright/test';
import { Api, signIn, state, unique, users, shot } from './support';

/** Gap 23: an uploaded video shows its playback-copy status; without a configured converter it waits with the reason and the original stays playable and downloadable. */
test.describe('E2E 25: video playback copies', () => {
  test('video in the content library shows conversion status, the configuration requirement and the original download', async ({ page }) => {
    const author = await Api.as(users.author);
    const ncr = await author.post('/api/ncrs', { title: unique('Video status NCR'), description: 'Setup', source: 'In-process', siteId: state().siteId, detectedOn: new Date().toISOString().slice(0, 10), severity: 1 });
    const name = `${unique('press-setup').replace(/\s+/g, '-')}.mov`;
    await author.upload('Ncr', ncr.id, name, 'bytes of a phone recording '.repeat(40), 'video/quicktime');
    const st = await author.get(`/api/files/search?kind=Video&q=${encodeURIComponent(name)}`);
    const id = st.items[0].id as string;
    const status = await author.get(`/api/files/${id}/renditions`);
    expect(status.renditions[0].status).toBe(0); // queued, never reported as converted

    await signIn(page, users.author);
    await page.goto('/documents/content');
    await page.getByRole('button', { name: /Videos/ }).click();
    await page.getByText(name).click();
    const panel = page.getByTestId('video-status');
    await expect(panel).toContainText('Playing the original file.');
    await expect(panel).toContainText('MP4: Queued');
    await expect(panel.getByRole('link', { name: 'Download original' })).toBeVisible();
    if (!status.converterAvailable) await expect(page.getByText(/Playback copies are not being made: .*Video:FfmpegPath/)).toBeVisible();
    await shot(page, '23-video-conversion-status');
  });
});
