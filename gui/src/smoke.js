'use strict';
const fs = require('node:fs/promises');
const path = require('node:path');
const assert = require('node:assert/strict');

async function runSmoke(win, backend, directory) {
  const failures = [];
  win.webContents.on('console-message', (_event, ...args) => {
    if (args.some(arg => typeof arg === 'string' && /Uncaught|ReferenceError|SyntaxError/.test(arg))) failures.push(args.join(' '));
  });
  const evaluate = code => win.webContents.executeJavaScript(code);
  async function capture(name) {
    // Capture the current compositor frame even when the test window is behind another app.
    if (!win.webContents.debugger.isAttached()) win.webContents.debugger.attach('1.3');
    const shot = await win.webContents.debugger.sendCommand('Page.captureScreenshot', {format: 'png', fromSurface: true});
    await fs.writeFile(path.join(directory, name), Buffer.from(shot.data, 'base64'));
  }
  async function waitFor(code, timeout = 90000) {
    const start = Date.now();
    while (Date.now() - start < timeout) {
      if (backend.exited) throw new Error('진단 엔진 종료로 GUI 시험을 진행할 수 없습니다.');
      if (await evaluate(code)) return;
      await new Promise(resolve => setTimeout(resolve, 250));
    }
    await capture('failure.png');
    await fs.writeFile(path.join(directory, 'failure-state.json'), JSON.stringify(await evaluate("({error: document.getElementById('errorBanner').textContent, progress: document.getElementById('progressText').textContent, log: document.getElementById('log').textContent})"), null, 2));
    throw new Error(`GUI state timed out: ${code}`);
  }
  await fs.mkdir(directory, {recursive: true});
  await fs.writeFile(path.join(directory, 'summary.json'), JSON.stringify({passed: false, status: '진행 중', generatedAt: new Date().toISOString()}, null, 2));
  await waitFor("document.body.dataset.loaded === 'true' && document.getElementById('progress').hidden");
  const initial = await backend.request('snapshot');
  assert.equal(initial.ok, true);
  assert.equal(initial.state.gatt.diagnostics.stages.length, 6);
  assert.equal(await evaluate("document.querySelectorAll('#stageResults tr').length"), 6);
  assert.equal(initial.state.gatt.physicalConnectionCount, '확인되지 않음');
  assert.equal(initial.state.gatt.peakSuccessfulPhones, 0);
  assert.equal(await evaluate("document.getElementById('step4').classList.contains('complete')"), false);
  assert.equal(await evaluate("typeof window.require"), 'undefined');
  assert.equal(await evaluate("document.documentElement.scrollWidth <= innerWidth"), true);
  assert.equal(await evaluate("document.querySelector('.content').scrollWidth <= document.querySelector('.content').clientWidth"), true);
  const adapter = initial.state.adapters.find(a => a.deviceId === initial.state.selectedDeviceId);
  if (process.argv.includes('--smoke-gatt')) assert.ok(adapter, '실제 GATT 시험에는 선택 가능한 어댑터가 필요합니다.');
  if (adapter) {
    assert.ok(adapter.pnp?.presenceSource?.includes('CfgMgr32'), '현재 어댑터는 Native 장치 존재 확인 출처가 있어야 함');
    if (adapter.capabilities.IsPeripheralRoleSupported?.value === true && adapter.isDefault && adapter.pnp?.present === true && initial.state.adapters.length === 1)
      assert.equal(await evaluate("document.getElementById('startServer').disabled"), false, '지원/존재/기본 라디오 확인 후 시작 버튼이 활성화되어야 함');
    if (adapter.bluetoothAddress) {
      assert.match(adapter.bluetoothAddress, /^([0-9A-F]{2}:){5}[0-9A-F]{2}$/);
      assert.equal(await evaluate("document.getElementById('bluetoothMac').textContent"), adapter.bluetoothAddress);
      assert.equal(await evaluate("document.getElementById('copyMac').disabled"), false);
    }
    const expected = adapter.capabilities.IsPeripheralRoleSupported;
    assert.equal(await evaluate("document.getElementById('peripheralValue').textContent"), expected.value === null ? expected.state : String(expected.value).replace(/^t/, 'T').replace(/^f/, 'F'));
    await evaluate("document.getElementById('probe').click()");
    await waitFor("document.getElementById('progress').hidden");
    assert.equal(await evaluate("document.getElementById('errorBanner').hidden"), true);
  }
  await evaluate("document.getElementById('phoneAlias').value = 'GUI 동작 점검'; document.getElementById('testOutcome').value = '미확인'; document.getElementById('observationDetail').value = '스마트폰 시험 아님 · 입력 및 저장 확인'; document.getElementById('observationForm').requestSubmit()");
  await waitFor("document.getElementById('progress').hidden");
  assert.equal(await evaluate("document.getElementById('observations').textContent.includes('GUI 동작 점검')"), true);
  const saved = await backend.request('export', {directory});
  assert.equal(saved.ok, true);
  const json = JSON.parse((await fs.readFile(path.join(saved.result, 'report.json'), 'utf8')).replace(/^\uFEFF/, ''));
  assert.equal(json.state.observations.at(-1).outcome, '미확인');
  assert.equal(json.state.gatt.diagnostics.stages.find(s => s.key === 'phoneDiscovery').status, '확인되지 않음');
  assert.equal(json.state.gatt.diagnostics.stages.find(s => s.key === 'commandReceived').status, '미시험');
  const csv = await fs.readFile(path.join(saved.result, 'report.csv'), 'utf8');
  assert.ok(csv.includes('"Stage"'));
  await fs.access(path.join(saved.result, 'report.csv'));
  await evaluate("window.scrollTo(0,0); document.querySelector('.content').scrollTop = 0");
  await new Promise(resolve => setTimeout(resolve, 500));
  assert.equal(await evaluate("document.querySelector('.content').scrollTop"), 0);
  await capture('dashboard.png');
  await evaluate("document.querySelector('[data-section=connectionsPanel]').click()");
  await new Promise(resolve => setTimeout(resolve, 600));
  assert.equal(await evaluate("window.scrollY"), 0);
  await capture('connections.png');
  // Exercise a real GATT start/stop only when explicitly requested by the developer test command.
  if (process.argv.includes('--smoke-gatt') && adapter) {
    await evaluate("document.getElementById('startServer').click()");
    await waitFor("document.getElementById('progress').hidden");
    await new Promise(resolve => setTimeout(resolve, 2000));
    const started = await backend.request('snapshot');
    await fs.writeFile(path.join(directory, 'gatt-start.json'), JSON.stringify(started.state.gatt, null, 2));
    assert.equal(started.state.gatt.advertisementStatus, 'Started');
    assert.equal(started.state.gatt.advertisementError, 'Success');
    const d = started.state.gatt.diagnostics;
    assert.equal(d.stages.find(s => s.key === 'advertising').status, '성공');
    assert.ok(d.stages.filter(s => ['phoneDiscovery', 'gattConnection', 'commandReceived', 'multiPhone'].includes(s.key)).every(s => s.status === '미시험'));
    assert.equal(d.actualAdvertisingPacketFormat, '확인되지 않음');
    assert.equal(d.legacyForced, false);
    assert.ok(started.state.archivedRuns.some(a => a.observations.some(o => o.phone === 'GUI 동작 점검')));
    assert.equal(await evaluate("document.getElementById('advertisingBadge').textContent"), '광고 중');
    await evaluate("window.scrollTo(0,0); document.querySelector('.content').scrollTop = 0");
    await capture('advertising.png');
    await evaluate("document.getElementById('stopServer').click()");
    await waitFor("document.getElementById('progress').hidden");
    const stopped = await backend.request('snapshot');
    assert.equal(stopped.state.gatt.isCreated, false);
    assert.equal(stopped.state.gatt.diagnostics.stages.find(s => s.key === 'advertising').status, '성공');
    const finalReport = await backend.request('export', {directory});
    assert.equal(finalReport.ok, true);
    await fs.writeFile(path.join(directory, 'gatt-final-state.json'), JSON.stringify(stopped.state, null, 2));
  }
  win.setSize(900, 700);
  await evaluate("document.querySelector('.content').scrollTop = 0; window.scrollTo(0,0)");
  await new Promise(resolve => setTimeout(resolve, 300));
  assert.equal(await evaluate("document.querySelector('.content').scrollWidth <= document.querySelector('.content').clientWidth"), true);
  await capture('compact.png');
  assert.deepEqual(failures, []);
  await fs.writeFile(path.join(directory, 'summary.json'), JSON.stringify({passed: true, generatedAt: new Date().toISOString(), adapterCount: initial.state.adapters.length, gattTested: process.argv.includes('--smoke-gatt'), report: saved.result}, null, 2));
  console.log('GUI smoke test passed:', directory);
}
module.exports = {runSmoke};
