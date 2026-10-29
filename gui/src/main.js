'use strict';
const {app, BrowserWindow, ipcMain, dialog, clipboard, shell} = require('electron');
const path = require('node:path');
const fs = require('node:fs/promises');
const {pathToFileURL} = require('node:url');
const {Backend} = require('./backend');
app.setPath('userData', path.join(app.getPath('appData'), process.argv.includes('--smoke-test') ? `BluetoothLab-Test-${process.pid}` : process.argv.includes('--field-test') ? 'BluetoothLab-Field' : 'BluetoothAdapterLab'));

let win, backend, closing = false, lastExport, exitCode = 0;
const page = pathToFileURL(path.join(__dirname, 'index.html')).href;
const allowed = new Set(['refresh', 'select', 'probe', 'start', 'stop', 'snapshot', 'note', 'map', 'trial-start', 'trial-confirm', 'trial-cancel']);
function assertLocal(event) {
  if (!win || event.sender !== win.webContents || event.senderFrame !== win.webContents.mainFrame || event.senderFrame.url !== page)
    throw new Error('허용되지 않은 요청입니다.');
}
function send(message) { if (win && !win.isDestroyed()) win.webContents.send('engine:event', message); }
function register() {
  ipcMain.handle('engine:command', async (event, command, data) => {
    assertLocal(event);
    if (!allowed.has(command)) throw new Error('지원하지 않는 명령입니다.');
    return backend.request(command, data);
  });
  ipcMain.handle('report:save', async event => {
    assertLocal(event);
    const chosen = await dialog.showOpenDialog(win, {title: '진단 결과를 저장할 폴더 선택', properties: ['openDirectory', 'createDirectory']});
    if (chosen.canceled) return {canceled: true};
    const answer = await backend.request('export', {directory: chosen.filePaths[0]});
    if (answer.ok) lastExport = answer.result;
    return answer;
  });
  ipcMain.handle('report:reveal', event => {
    assertLocal(event);
    if (lastExport) shell.showItemInFolder(path.join(lastExport, 'report.json'));
  });
  ipcMain.handle('clipboard:write', (event, text) => {
    assertLocal(event);
    if (typeof text !== 'string' || text.length > 4000) throw new Error('복사할 값이 올바르지 않습니다.');
    clipboard.writeText(text);
  });
  ipcMain.on('window:control', (event, action) => {
    assertLocal(event);
    if (action === 'minimize') win.minimize();
    if (action === 'maximize') win.isMaximized() ? win.unmaximize() : win.maximize();
    if (action === 'close') win.close();
  });
}
app.whenReady().then(async () => {
  win = new BrowserWindow({width: 1440, height: 1000, minWidth: 900, minHeight: 650, frame: false,
    backgroundColor: '#f4f6f8', title: 'Bluetooth Adapter Lab',
    webPreferences: {preload: path.join(__dirname, 'preload.js'), contextIsolation: true, nodeIntegration: false, sandbox: true}});
  win.setMenu(null);
  backend = new Backend(path.join(app.isPackaged ? process.resourcesPath : path.join(__dirname, '..'), app.isPackaged ? 'backend' : 'backend-dev', 'BluetoothDiagnostics.exe'));
  backend.on('message', send);
  backend.on('diagnostic', detail => send({type: 'diagnostic', detail}));
  backend.on('offline', detail => send({type: 'offline', detail}));
  win.webContents.setWindowOpenHandler(() => ({action: 'deny'}));
  win.webContents.on('will-navigate', (event, url) => { if (url !== page) event.preventDefault(); });
  win.webContents.session.setPermissionRequestHandler((_contents, _permission, callback) => callback(false));
  register();
  win.on('close', event => {
    if (closing) return;
    event.preventDefault();
    closing = true;
    send({type: 'closing'});
    backend.close().finally(() => { win.destroy(); app.exit(exitCode); });
  });
  await win.loadFile(path.join(__dirname, 'index.html'));
  if (process.argv.includes('--field-test')) {
    const {startFieldCapture} = require('./field-test');
    await startFieldCapture(win, backend, path.join(__dirname, '..', 'test-results', 'field-test'));
  }
  if (process.argv.includes('--smoke-test')) {
    const {runSmoke} = require('./smoke');
    const directory = app.isPackaged ? path.join(app.getPath('temp'), 'BluetoothLab-ui-test') : path.join(__dirname, '..', 'test-results');
    try { await runSmoke(win, backend, directory); }
    catch (error) {
      console.error(error);
      exitCode = 1;
      await fs.mkdir(directory, {recursive: true});
      await fs.writeFile(path.join(directory, 'summary.json'), JSON.stringify({passed: false, generatedAt: new Date().toISOString(), error: error.message}, null, 2));
    }
    finally { win.close(); }
  }
});
app.on('window-all-closed', () => { if (!closing) app.exit(exitCode); });
