// Screenshot any preview page in this folder, rather than one named in the source.
//
// shot.js only ever opened vent.html, so every new preview arrived with a copy of it that
// differed in one string. This takes the page, where to put the picture, which element to
// frame, and how big the window is, and reports console errors either way -- a preview that
// throws while loading still screenshots, and the picture then looks like a design decision.
//
//   node shoot.js backdrops.html backdrops.png "#out" 1800 900
const { chromium } = require('playwright');

const [page, out, pick = 'body', wide = '1800', high = '600'] = process.argv.slice(2);

if (!page || !out) {
  console.error('usage: node shoot.js <page.html> <out.png> [selector] [width] [height]');
  process.exit(2);
}

(async () => {
  const browser = await chromium.launch();
  const tab = await browser.newPage({
    viewport: { width: Number(wide), height: Number(high) },
    deviceScaleFactor: 2,
  });

  const errs = [];
  tab.on('console', m => { if (m.type() === 'error') errs.push(m.text()); });
  tab.on('pageerror', e => errs.push(String(e)));

  await tab.goto('file://' + process.cwd() + '/' + page);
  try {
    await tab.waitForFunction("document.title === 'ready'", { timeout: 8000 });
  } catch (e) {
    errs.push('the page never said it was ready: ' + String(e).split('\n')[0]);
  }

  const target = (await tab.$(pick)) || (await tab.$('body'));
  await target.screenshot({ path: out });
  await browser.close();

  console.log(errs.length ? 'console errors:\n' + errs.join('\n') : 'no console errors');
})();
