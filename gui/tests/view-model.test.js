'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('../src/view-model');

test('unsupported is distinct from unsupported API, error and missing data', () => {
  assert.equal(vm.capability({value: false, state: '조회 성공'}).text, 'False');
  assert.equal(vm.capability({value: null, state: 'API 미지원'}).text, 'API 미지원');
  assert.equal(vm.capability({value: null, state: '조회 실패', detail: '0x80070005'}).detail, '0x80070005');
  assert.equal(vm.capability(undefined).text, '미확인');
});
test('only a successful advertising event counts as advertising success', () => {
  assert.equal(vm.advertising({isCreated: true}).success, false);
  assert.equal(vm.advertising({isCreated: true, advertisementStatus: 'Started', advertisementError: 'Success'}).success, true);
  assert.equal(vm.advertising({isCreated: true, advertisementStatus: 'Started', advertisementError: 'NotSupported'}).success, false);
  assert.equal(vm.advertising({isCreated: true, advertisementStatus: 'StartedWithoutAllAdvertisementData', advertisementError: 'Success'}).success, false);
  assert.equal(vm.advertising({isCreated: false, advertisementStatus: 'Started', advertisementError: 'Success'}).success, false);
});
test('GATT start requires a unique default, present peripheral adapter', () => {
  const adapter = {isDefault: true, pnp: {present: true}, capabilities: {IsPeripheralRoleSupported: {value: true}}};
  const state = {adapters: [adapter], gatt: {isCreated: false}};
  assert.equal(vm.canStart(adapter, state), true);
  assert.equal(vm.canStart({...adapter, isDefault: false}, state), false);
  assert.equal(vm.canStart({...adapter, pnp: null}, state), false);
  assert.equal(vm.canStart(adapter, {...state, adapters: [adapter, {}]}), false);
  assert.equal(vm.canStart(adapter, {...state, gatt: {isCreated: true}}), false);
  assert.equal(vm.canStart({...adapter, capabilities: {}}, state), false);
});
test('historical presence and unknown presence are kept distinct', () => {
  assert.equal(vm.present(true), '현재 연결');
  assert.equal(vm.present(false), '현재 부재 / 과거');
  assert.equal(vm.present(null), '미확인');
});
test('observed session peak never auto-completes the multi-phone step', () => {
  assert.equal(vm.multiSucceeded({peakObservedSessions: 3, stages: [{key: 'multiPhone', status: '미시험'}]}), false);
  assert.equal(vm.multiSucceeded({peakObservedSessions: 3, stages: [{key: 'multiPhone', status: '확인되지 않음'}]}), false);
  assert.equal(vm.multiSucceeded({stages: [{key: 'multiPhone', status: '성공'}]}), true);
});
test('start blockers explain missing selection, unknown presence and stale GATT separately', () => {
  assert.ok(vm.startBlockers(null, {adapters:[{}]}).some(s => s.includes('선택된 어댑터')));
  const a = {isDefault:true, pnp:{present:null}, capabilities:{IsPeripheralRoleSupported:{value:true}}};
  assert.ok(vm.startBlockers(a, {adapters:[a]}).some(s => s.includes('현재 존재')));
  assert.ok(vm.startBlockers({...a,pnp:{present:true}}, {adapters:[a],gatt:{isCreated:true}}).some(s => s.includes('기존 GATT')));
});
