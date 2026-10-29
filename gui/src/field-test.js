'use strict';
const fs = require('node:fs/promises');
const path = require('node:path');

// Developer-invoked real-phone test. No synthetic requests or phone results are injected.
async function startFieldCapture(win, backend, directory) {
  await fs.mkdir(directory, {recursive: true});
  const startedAt = new Date().toISOString();
  let inFlight = false, startRequested = false, stopped = false;
  const capture = async () => {
    if (inFlight || stopped) return;
    inFlight = true;
    try {
      const response = await backend.request('snapshot');
      await fs.writeFile(path.join(directory, 'live-state.json'), JSON.stringify({startedAt, capturedAt: new Date().toISOString(), ...response}, null, 2));
      if (!startRequested && response.state?.selectedDeviceId) {
        const canStart = await win.webContents.executeJavaScript("document.body.dataset.loaded === 'true' && !document.getElementById('startServer').disabled");
        if (canStart) {
          startRequested = true;
          await win.webContents.executeJavaScript("document.getElementById('startServer').click()");
        }
      }
    } catch (error) { console.error('Field capture:', error.message); }
    finally { inFlight = false; }
  };
  const interval = setInterval(capture, 2000);
  win.once('closed', () => { stopped = true; clearInterval(interval); });
  console.log('Real-phone field capture:', directory);
  void capture();
}
module.exports = {startFieldCapture};
