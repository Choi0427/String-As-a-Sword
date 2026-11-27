const { chromium } = require('playwright');
const [dir, scale, outDir, ...names] = process.argv.slice(2);
(async () => {
  const b = await chromium.launch({ args: ['--allow-file-access-from-files'] });
  const p = await b.newPage({ viewport: { width: 1500, height: 1200 }, deviceScaleFactor: Number(scale) });
  for (const n of names) {
    const errs = []; p.removeAllListeners('pageerror'); p.on('pageerror', e => errs.push(e.message));
    await p.goto('file://' + dir + '/' + n + '.html'); await p.evaluate(() => document.fonts.ready); await p.waitForTimeout(300);
    await p.locator('#fig').screenshot({ path: outDir + '/' + n + '.png' });
    console.log(n, errs.length ? 'ERR ' + errs.join(';') : 'ok');
  }
  await b.close();
})();
