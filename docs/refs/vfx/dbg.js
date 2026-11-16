const { chromium } = require('playwright');
(async () => {
  const b = await chromium.launch();
  const p = await b.newPage();
  const errs = [];
  p.on('console', m => { if (m.type()==='error') errs.push(m.text()); });
  p.on('pageerror', e => errs.push('PAGEERROR: ' + e.message));
  await p.goto('file://' + process.cwd() + '/dust.html');
  await p.waitForTimeout(1500);
  console.log(errs.length ? errs.join('\n') : 'no errors; title=' + await p.title());
  await b.close();
})();
