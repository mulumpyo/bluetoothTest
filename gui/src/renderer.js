'use strict';
const $ = id => document.getElementById(id);
const vm = window.labView;
let state = null, busy = false, offline = false, logs = [], hiddenBefore = 0, toastTimer;
function diagnostics() { return state?.gatt?.diagnostics; }
function element(tag, text, className) {
  const node = document.createElement(tag);
  if (text !== undefined) node.textContent = String(text);
  if (className) node.className = className;
  return node;
}
function selected() { return state?.adapters?.find(a => a.deviceId === state.selectedDeviceId); }
function toast(message) {
  $('toast').textContent = message;
  $('toast').hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { $('toast').hidden = true; }, 3000);
}
function error(message) {
  $('errorBanner').textContent = message;
  $('errorBanner').hidden = false;
}
function log(entry) {
  logs.push(entry);
  renderLogs();
  renderEvidence();
}
function renderLogs() {
  const filter = $('logFilter').value;
  const visible = logs.slice(hiddenBefore).filter(entry => filter === 'all' || (filter === 'error' ? /오류|실패/.test(entry.event + entry.detail) : /GATT|READ|WRITE|Advertising|세션|연결/.test(entry.event)));
  $('log').replaceChildren(...visible.slice(-400).map(entry => {
    const time = new Date(entry.time).toLocaleTimeString('ko-KR', {hour12: false});
    return element('div', `[${time}] ${entry.event}  ${entry.detail}`, 'line ' + (/오류|실패/.test(entry.event) ? 'fail' : /READ|WRITE|Advertising/.test(entry.event) ? 'good' : 'info'));
  }));
  if (!visible.length) $('log').append(element('div', '표시할 로그가 없습니다. 전체 기록은 내보내기에 포함됩니다.', 'line info'));
  $('log').scrollTop = $('log').scrollHeight;
}
function renderEvidence() {
  const current = diagnostics()?.sessions || [];
  const read = current.some(s => s.readCount > 0);
  const write = current.some(s => s.commandSuccessCount > 0);
  $('readEvidence').textContent = read ? 'PC 요청 관측' : '요청 대기';
  $('writeEvidence').textContent = write ? '명령 수신' : '요청 대기';
  $('readEvidence').classList.toggle('confirmed', read);
  $('writeEvidence').classList.toggle('confirmed', write);
  const scanned = state?.observations?.some(o => o.stage === '검색' && o.outcome === '성공');
  $('scanEvidence').textContent = scanned ? '사용자 성공 기록' : '사용자 확인';
  $('scanEvidence').classList.toggle('confirmed', Boolean(scanned));
}
function updateControls() {
  const a = selected();
  document.querySelectorAll('[data-operation]').forEach(button => { button.disabled = busy || offline || !state; });
  $('refresh').disabled = busy || offline;
  $('probe').disabled = busy || offline || !a;
  $('adapterSelect').disabled = busy || offline || !state?.adapters?.length;
  $('startServer').disabled = busy || offline || !vm.canStart(a, state);
  $('stopServer').disabled = busy || offline || !state?.gatt?.isCreated;
  const d = diagnostics();
  const trial = d?.trials?.at(-1);
  const running = ['진행 중', 'PC 요청 조건 충족'].includes(trial?.status);
  const aliases = ['A', 'B', 'C'].slice(0, Number($('trialCount').value));
  const mapped = aliases.every(alias => d?.sessions?.some(s => s.active && s.phoneAlias === alias));
  $('mapPhone').disabled = busy || offline || running || !state?.gatt?.connections?.length;
  $('mapConnection').disabled = busy || offline || running || !state?.gatt?.connections?.length;
  $('mapAlias').disabled = busy || offline || running;
  $('beginTrial').disabled = busy || offline || !state?.gatt?.isCreated || running || !mapped || !$('identitiesConfirmed').checked;
  $('confirmTrial').disabled = busy || offline || trial?.status !== 'PC 요청 조건 충족' || !$('responsesConfirmed').checked;
  $('cancelTrial').disabled = busy || offline || !running;
  $('progress').hidden = !busy;
}
function renderDiagnostics() {
  const d = diagnostics();
  if (!d) return;
  $('runLabel').textContent = '시험 ' + d.runId.slice(0, 8);
  $('archiveInfo').textContent = `이전 시험 ${state.archivedRuns?.length ?? 0}건 보존 · 모든 판정/활동/시험 구간이 JSON/CSV에 포함됩니다.`;
  $('stageResults').replaceChildren(...d.stages.map(stage => {
    const row = element('tr');
    row.dataset.stage = stage.key;
    const result = element('td');
    result.append(element('span', stage.status, 'resultBadge ' + (stage.status === '성공' ? 'success' : stage.status === '실패' ? 'failure' : '')));
    const evidence = element('td', stage.detail);
    evidence.append(element('small', `${stage.source} · ${stage.time ? new Date(stage.time).toLocaleString('ko-KR') : '미시험'}`));
    row.append(element('td', stage.name), result, evidence);
    return row;
  }));
  const select = $('mapConnection');
  const signature = JSON.stringify((state.gatt.connections || []).map(c => [c.connectionId, c.phoneAlias]));
  if (select.dataset.signature !== signature) {
    const previous = select.value;
    select.replaceChildren(new Option('활성 세션 선택', ''));
    for (const c of state.gatt.connections || []) select.append(new Option(`${c.phoneAlias || '미매핑'} · ${c.id}`, c.connectionId));
    if ([...select.options].some(o => o.value === previous)) select.value = previous;
    select.dataset.signature = signature;
  }
  $('sessionHistory').replaceChildren(...d.sessions.map(s => {
    const row = element('tr');
    const name = element('td', s.phoneAlias ? `스마트폰 ${s.phoneAlias}` : '폰 미매핑');
    name.append(element('code', s.deviceId), element('code', s.connectionId));
    row.append(name, element('td', `${new Date(s.firstObserved).toLocaleTimeString('ko-KR')} / ${new Date(s.lastActivityAt).toLocaleTimeString('ko-KR')}`),
      element('td', `${s.readCount} / ${s.writeCount} / ${s.commandSuccessCount} / ${s.errorCount}`),
      element('td', s.active ? '관측 활성' : `${s.endedAt ? new Date(s.endedAt).toLocaleTimeString('ko-KR') : ''} ${s.endReason || '추적 종료'}`));
    return row;
  }));
  $('activityHistory').replaceChildren(...d.activities.slice(-100).reverse().map(a => {
    const row = element('tr');
    row.append(element('td', `${new Date(a.time).toLocaleTimeString('ko-KR')} / ${a.phoneAlias || '미매핑'}`), element('td', a.operation), element('td', `${a.success ? '처리' : '오류'} · ${a.detail}`));
    return row;
  }));
  const trial = d.trials.at(-1);
  $('trialStatus').textContent = trial ? `${trial.status} · 수신: ${trial.receivedFrom.join(', ') || '없음'} · 남은 시간 ${Math.max(0, Math.ceil((new Date(trial.deadline) - Date.now()) / 1000))}초 · ${trial.detail}` : '미시험 · 폰을 A/B/C에 매핑한 후 실제 폰 식별 확인란을 체크하세요. 세션 개수만으로 성공 처리하지 않습니다.';
}
function renderGatt() {
  const g = state?.gatt;
  const adv = vm.advertising(g);
  $('advertisingBadge').textContent = adv.text;
  $('advertisingBadge').className = 'status ' + adv.tone;
  $('advertisingDetail').textContent = g?.status || '아직 광고를 시작하지 않았습니다.';
  $('currentSessions').textContent = g ? g.connections.length : '—';
  $('peakSessions').textContent = g?.peakObservedSessions ?? '—';
  $('peakPhones').textContent = g?.peakSuccessfulPhones ?? '—';
  $('sessionList').replaceChildren();
  for (const connection of g?.connections || []) {
    const row = element('div', undefined, 'sessionRow');
    const info = element('div', undefined, 'sessionInfo');
    info.append(element('b', connection.phoneAlias ? `스마트폰 ${connection.phoneAlias}` : '폰 미매핑'), element('code', connection.id),
      element('span', `최근 ${new Date(connection.lastActivityAt).toLocaleTimeString('ko-KR', {hour12: false})} · Read ${connection.readCount} / Write ${connection.writeCount} / 정상 명령 ${connection.commandSuccessCount}`));
    row.append(element('span', '●', 'sessionDot'), info, element('span', `${Math.floor(connection.observedSeconds)}초`, 'sessionTime'));
    $('sessionList').append(row);
  }
  if (!g?.connections.length) $('sessionList').append(element('div', '관측된 활성 세션 없음 · 스마트폰에서 Read / Write를 실행하세요.', 'emptyState'));
  $('step3').classList.toggle('complete', adv.success);
  $('serverPrerequisite').textContent = vm.startBlockers(selected(), state).join(' ') || '시작 조건 확인 완료. 선택한 동글에서 GATT 서버를 시작할 수 있습니다.';
  $('step4').classList.toggle('complete', vm.multiSucceeded(diagnostics()));
  renderDiagnostics();
  renderEvidence();
  if (!busy && !offline) {
    const a = selected();
    $('statusBadge').textContent = adv.success ? 'GATT 광고 중' : a ? '어댑터 확인됨' : '어댑터 선택 필요';
    $('statusBadge').className = 'status ' + (adv.success || a ? 'good' : '');
  }
  updateControls();
}
function renderState(next, replaceLogs = false) {
  if (state?.runId !== next.runId) { $('identitiesConfirmed').checked = false; $('responsesConfirmed').checked = false; }
  state = next;
  if (replaceLogs) { logs = next.logs || []; hiddenBefore = Math.min(hiddenBefore, logs.length); renderLogs(); }
  const a = selected();
  $('adapterSelect').replaceChildren();
  $('adapterSelect').append(new Option(state.adapters.length ? '검사할 어댑터를 선택하세요' : '사용 가능한 WinRT 어댑터 없음', ''));
  for (const adapter of state.adapters) $('adapterSelect').append(new Option(`${adapter.name}${adapter.isBarrot ? ' · BARROT' : ''}${adapter.isDefault ? ' · 기본' : ''} · ${vm.present(adapter.pnp?.present)}`, adapter.deviceId));
  $('adapterSelect').value = state.selectedDeviceId || '';
  $('sideName').textContent = a?.name || '선택된 장치 없음';
  $('sideDevice').textContent = a ? `${a.isBarrot ? 'USB 33FA : 0012' : '사용자 선택 어댑터'} · ${vm.present(a.pnp?.present)}` : '어댑터 목록을 확인하세요';
  $('deviceDetails').replaceChildren();
  if (a) {
    const tags = element('div', undefined, 'deviceTags');
    tags.append(element('span', vm.present(a.pnp?.present), 'chip ' + (a.pnp?.present ? 'green' : '')), element('span', `PnP ${a.pnp?.status ?? '미확인'}`, 'chip'), element('span', a.isDefault ? '기본 어댑터' : '기본 아님', 'chip'));
    $('deviceDetails').append(tags);
    const mac = element('div', undefined, 'macAddressBox');
    const addressText = element('div');
    addressText.append(element('span', 'Bluetooth MAC · Windows 어댑터 주소'));
    const addressValue = element('code', a.bluetoothAddress || a.bluetoothAddressState || '미확인');
    addressValue.id = 'bluetoothMac';
    addressText.append(addressValue);
    const copyMac = element('button', 'MAC 복사', 'btn ghost');
    copyMac.id = 'copyMac';
    copyMac.disabled = !a.bluetoothAddress;
    copyMac.onclick = () => window.bluetooth.copy(a.bluetoothAddress).then(() => toast('Bluetooth MAC을 복사했습니다.')).catch(ex => error(ex.message));
    mac.append(addressText, copyMac);
    $('deviceDetails').append(mac);
    $('deviceDetails').append(element('p', a.bluetoothAddressError || 'nRF Connect는 MAC 입력 없이 스캔된 광고를 선택해 연결할 수 있습니다. 광고 주소는 프라이버시 주소 사용 등에 따라 위 어댑터 주소와 다를 수 있으므로 Service UUID도 확인하세요. 광고가 Aborted이면 먼저 광고 문제를 해결해야 합니다.', 'smallNote'));
    for (const [label, value] of [['Instance ID', a.instanceId || '미확인'], ['Device ID', a.deviceId], ['존재 확인', a.pnp?.presenceSource || '미확인'], ['드라이버', `${a.pnp?.provider || '미확인'} / ${a.pnp?.version || '미확인'}`]]) {
      const row = element('div', undefined, 'detailRow');
      row.append(element('span', label), element('code', value));
      $('deviceDetails').append(row);
    }
  } else $('deviceDetails').append(element('p', 'BARROT가 유일하게 확인되지 않으면 자동 선택하지 않습니다. 목록에서 장치를 선택하세요.', 'smallNote'));
  for (const [prefix, key, card] of [['peripheral', 'IsPeripheralRoleSupported', 'capPeripheral'], ['ble', 'IsLowEnergySupported', 'capBle'], ['central', 'IsCentralRoleSupported', 'capCentral'], ['extended', 'IsExtendedAdvertisingSupported', 'capExtended']]) {
    const view = vm.capability(a?.capabilities?.[key]);
    $(prefix + 'Value').textContent = view.text;
    $(prefix + 'Detail').textContent = view.detail;
    $(prefix + 'Detail').title = view.detail;
    $(card).classList.remove('supported', 'unsupported', 'unknown');
    $(card).classList.add(view.tone);
  }
  $('capabilityNote').textContent = a?.error ? `조회 실패 / 어댑터 미인식: ${a.error}` : '지원 True는 실제 광고 및 스마트폰 연결 성공과 별개입니다. Extended Advertising=False여도 일반 GATT 광고를 시험할 수 있습니다.';
  $('pnpCount').textContent = `(${state.pnpDevices.length}개)`;
  $('pnpBody').replaceChildren();
  for (const p of state.pnpDevices) {
    const tr = element('tr');
    const name = element('td');
    name.append(element('b', p.name), element('code', p.instanceId));
    tr.append(name, element('td', vm.present(p.present)), element('td', p.status), element('td', `${p.provider || '미확인'} ${p.version || ''}`));
    if (p.present === false) tr.className = 'historical';
    $('pnpBody').append(tr);
  }
  if (!state.pnpDevices.length) { const tr = element('tr'); const cell = element('td', 'PnP 정보 없음 · 조회 오류는 로그를 확인하세요.'); cell.colSpan = 4; tr.append(cell); $('pnpBody').append(tr); }
  $('serverPrerequisite').textContent = vm.canStart(a, state) ? '선택한 동글의 Peripheral=True를 확인했습니다. 서버를 시작할 수 있습니다.' : state.gatt.isCreated ? '서버 동작 중 · 재시작 시 현재 판정·이력을 보존하고 새 시험을 만듭니다.' : 'Peripheral=True, Present=True, 선택 장치가 유일한 기본 라디오여야 합니다. 다른 Bluetooth 라디오를 비활성화한 뒤 새로 조회하세요.';
  $('step1').classList.toggle('complete', Boolean(a));
  $('step2').classList.toggle('complete', a?.capabilities?.IsPeripheralRoleSupported?.value === true);
  $('observations').replaceChildren();
  for (const note of state.observations || []) {
    const row = element('div', undefined, 'observationRow');
    row.append(element('span', new Date(note.time).toLocaleTimeString('ko-KR', {hour12: false})), element('b', note.phone), element('span', `${note.stage} · ${note.outcome}`, note.outcome === '실패' ? 'failureText' : ''), element('span', note.detail));
    $('observations').append(row);
  }
  if (!state.observations?.length) $('observations').append(element('span', '아직 기록된 사용자 실측이 없습니다.', 'smallNote'));
  renderEvidence();
  renderGatt();
  $('engineDot').classList.add('online');
  $('engineLabel').textContent = 'C# 진단 엔진 연결됨';
  // A stable signal for the desktop UI integration check.
  document.body.dataset.loaded = 'true';
}
async function operation(label, action) {
  if (busy) return;
  busy = true;
  $('errorBanner').hidden = true;
  $('progressText').textContent = label;
  $('statusBadge').textContent = '진단 진행 중';
  $('statusBadge').className = 'status busy';
  updateControls();
  try {
    const answer = await action();
    if (answer?.state) renderState(answer.state, true);
    if (answer?.ok === false) throw new Error(answer.error);
    return answer;
  } catch (ex) { error(ex.message); }
  finally { busy = false; renderGatt(); }
}
function command(name, data) { return window.bluetooth.command(name, data); }
$('mappingForm').onsubmit = event => {
  event.preventDefault();
  operation('활성 세션과 실제 스마트폰을 매핑합니다.', () => command('map', {connectionId: $('mapConnection').value, alias: $('mapAlias').value}));
};
$('beginTrial').onclick = () => {
  $('responsesConfirmed').checked = false;
  return operation('30초 다중 폰 시험 구간을 시작합니다.', () => command('trial-start', {count: Number($('trialCount').value), identitiesConfirmed: $('identitiesConfirmed').checked}));
};
$('confirmTrial').onclick = () => operation('PC 명령 수신 및 폰 응답 확인을 검증합니다.', () => command('trial-confirm', {responsesConfirmed: $('responsesConfirmed').checked}));
$('cancelTrial').onclick = () => operation('현재 시험을 취소합니다.', () => command('trial-cancel'));
for (const name of ['trialCount', 'identitiesConfirmed', 'responsesConfirmed']) $(name).onchange = updateControls;
$('refresh').onclick = () => operation('장치 및 PnP 정보를 조회하고 있습니다. 최대 45초 걸릴 수 있습니다.', () => command('refresh'));
$('probe').onclick = () => operation('선택한 어댑터의 Windows Bluetooth API를 확인하고 있습니다.', () => command('probe'));
$('adapterSelect').onchange = () => { if ($('adapterSelect').value) operation('어댑터를 선택하고 있습니다.', () => command('select', {deviceId: $('adapterSelect').value})); };
$('startServer').onclick = () => operation('동글을 다시 확인하고 GATT 서버를 시작합니다. 최대 45초 걸릴 수 있습니다.', () => command('start'));
$('stopServer').onclick = () => operation('광고를 종료하고 GATT 요청 핸들러를 해제합니다.', () => command('stop'));
$('observationForm').onsubmit = event => {
  event.preventDefault();
  operation('사용자 실측 결과를 기록합니다.', () => command('note', {phone: $('phoneAlias').value.trim(), stage: $('testStage').value, outcome: $('testOutcome').value, detail: $('observationDetail').value}));
};
$('saveReport').onclick = () => operation('저장 폴더를 선택하고 진단 결과를 내보냅니다.', async () => {
  const answer = await window.bluetooth.saveReport();
  if (answer?.ok) { $('exportPath').textContent = answer.result; $('revealReport').hidden = false; toast('JSON / CSV 저장 완료'); }
  return answer;
});
$('revealReport').onclick = () => window.bluetooth.revealReport().catch(ex => error(ex.message));
$('clearLog').onclick = () => { hiddenBefore = logs.length; renderLogs(); };
$('logFilter').onchange = renderLogs;
document.querySelectorAll('[data-copy]').forEach(button => { button.onclick = () => window.bluetooth.copy($(button.dataset.copy).textContent).then(() => toast('클립보드에 복사했습니다.')).catch(ex => error(ex.message)); });
document.querySelectorAll('[data-window]').forEach(button => { button.onclick = () => window.bluetooth.windowControl(button.dataset.window); });
document.querySelectorAll('[data-section]').forEach(button => { button.onclick = () => {
  const content = document.querySelector('.content');
  content.scrollTo({top: $(button.dataset.section).getBoundingClientRect().top - content.getBoundingClientRect().top + content.scrollTop - 20, behavior: 'smooth'});
}; });
window.bluetooth.onEvent(message => {
  if (message.type === 'log') log(message.entry);
  if (message.type === 'gatt' && state && message.gatt?.diagnostics?.runId === state.runId) { state.gatt = message.gatt; renderGatt(); }
  if (message.type === 'diagnostic') log({time: new Date().toISOString(), event: '엔진 진단', detail: message.detail});
  if (message.type === 'offline') {
    offline = true;
    error(message.detail + ' 프로그램을 다시 실행하세요.');
    $('engineDot').classList.remove('online');
    $('engineLabel').textContent = '진단 엔진 연결 끊김';
    $('statusBadge').textContent = '엔진 종료';
    $('statusBadge').className = 'status bad';
    updateControls();
  }
  if (message.type === 'closing') { busy = true; $('progressText').textContent = '광고를 종료하고 프로그램을 닫습니다…'; updateControls(); }
});
operation('Windows Bluetooth 어댑터를 조회합니다. PnP 상세 조회는 최대 45초 걸릴 수 있습니다.', () => command('refresh'));
