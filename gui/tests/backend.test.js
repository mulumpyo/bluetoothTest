'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');
const {Backend} = require('../src/backend');
const executable = path.resolve(__dirname, '../backend-dev/BluetoothDiagnostics.exe');

test('C# stdio bridge preserves Korean and recovers after an invalid command', {skip: !fs.existsSync(executable), timeout: 20000}, async () => {
  const backend = new Backend(executable);
  try {
    const invalid = await backend.request('not-a-command');
    assert.equal(invalid.ok, false);
    const note = await backend.request('note', {phone: '스마트폰 A', stage: '검색', outcome: '미확인', detail: '따옴표 " / 쉼표, / 줄바꿈\n한글 보존'});
    assert.equal(note.ok, true);
    assert.equal(note.state.observations[0].phone, '스마트폰 A');
    assert.equal(note.state.observations[0].detail, '따옴표 " / 쉼표, / 줄바꿈\n한글 보존');
    assert.equal(note.state.gatt.diagnostics.stages.length, 6);
    assert.equal(note.state.gatt.physicalConnectionCount, '확인되지 않음');
    const claim = await backend.request('note', {phone: 'A/B/C', stage: '동시 연결', outcome: '성공', detail: '검증용 단순 주장 / 실제 폰 아님'});
    assert.equal(claim.state.gatt.diagnostics.stages.find(s => s.key === 'multiPhone').status, '확인되지 않음');
    assert.equal(claim.state.gatt.peakSuccessfulPhones, 0);
    const unmapped = await backend.request('map', {connectionId: 'unknown-session', alias: 'A'});
    assert.equal(unmapped.ok, false);
    const unproven = await backend.request('trial-confirm', {responsesConfirmed: true});
    assert.equal(unproven.ok, false);
    assert.equal((await backend.request('snapshot')).ok, true);
  } finally { await backend.close(); }
});
