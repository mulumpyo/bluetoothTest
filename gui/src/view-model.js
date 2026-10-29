'use strict';
// Pure presentation rules: never coerce an absent/error capability to false.
(function(root) {
  const api = {
    capability(value) {
      if (value?.value === true) return {text: 'True', tone: 'supported', detail: '지원 · Windows API 확인'};
      if (value?.value === false) return {text: 'False', tone: 'unsupported', detail: '미지원 · Windows API 확인'};
      return {text: value?.state || '미확인', tone: 'unknown', detail: value?.detail || '조회 결과 없음'};
    },
    advertising(gatt) {
      if (!gatt) return {text: '미시험', tone: '', success: false};
      const success = gatt.isCreated && gatt.advertisementStatus === 'Started' && gatt.advertisementError === 'Success';
      if (success) return {text: '광고 중', tone: 'good', success: true};
      if (gatt.advertisementStatus === 'Aborted') return {text: '광고 중단', tone: 'bad', success: false};
      if (gatt.advertisementStatus === 'StartedWithoutAllAdvertisementData') return {text: '광고 데이터 일부 누락', tone: 'busy', success: false};
      if (gatt.advertisementError && gatt.advertisementError !== 'Success') return {text: '광고 오류', tone: 'bad', success: false};
      if (gatt.advertisementStatus === 'Stopped') return {text: '광고 종료', tone: '', success: false};
      if (gatt.isCreated) return {text: '시작 확인 중', tone: 'busy', success: false};
      return {text: gatt.advertisementStatus === 'Stopped' ? '광고 종료' : '미시험', tone: '', success: false};
    },
    canStart(adapter, state) {
      return api.startBlockers(adapter, state).length === 0;
    },
    startBlockers(adapter, state) {
      const reasons = [];
      if (state?.gatt?.isCreated) reasons.push('기존 GATT 서버 객체가 남아 있습니다. 서버 종료 또는 장치 새로 조회를 누르세요.');
      if (!adapter) reasons.push('선택된 어댑터가 없습니다. 장치 새로 조회 후 목록에서 새 동글을 선택하세요.');
      else {
        if (adapter.capabilities?.IsPeripheralRoleSupported?.value !== true) reasons.push('선택 동글의 Peripheral=True가 확인되지 않았습니다. 지원 기능을 다시 검사하세요.');
        if (adapter.pnp?.present !== true) reasons.push('선택 동글의 현재 존재 여부가 True로 확인되지 않았습니다. 장치 새로 조회를 누르세요.');
        if (!adapter.isDefault) reasons.push('선택한 동글이 기본 Bluetooth 라디오가 아닙니다.');
      }
      if (state?.adapters?.length !== 1) reasons.push('WinRT 라디오가 정확히 1개여야 합니다. 다른 라디오를 비활성화한 후 새로 조회하세요.');
      return reasons;
    },
    present(value) { return value === true ? '현재 연결' : value === false ? '현재 부재 / 과거' : '미확인'; },
    multiSucceeded(diagnostics) { return diagnostics?.stages?.find(s => s.key === 'multiPhone')?.status === '성공'; }
  };
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.labView = Object.freeze(api);
})(typeof window === 'object' ? window : globalThis);
