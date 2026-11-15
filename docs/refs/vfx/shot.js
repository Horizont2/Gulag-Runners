const { chromium } = require('playwright');
(async () => {
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 1800, height: 300 }, deviceScaleFactor: 2 });
  const errs = [];
  p.on('console', m => { if (m.type() === 'error') errs.push(m.text()); });
  p.on('pageerror', e => errs.push(String(e)));
  await p.goto('file://' + process.cwd() + '/preview.html');
  await p.waitForFunction("document.title === 'ready'", { timeout: 8000 });
  const c = await p.$('#strip');
  await c.screenshot({ path: 'hit.png' });
  await b.close();
  console.log(errs.length ? 'console errors:\n' + errs.join('\n') : 'no console errors');
})();
