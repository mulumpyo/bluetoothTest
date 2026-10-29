'use strict';
const {spawn} = require('node:child_process');
const readline = require('node:readline');
const {EventEmitter} = require('node:events');

class Backend extends EventEmitter {
  constructor(executable) {
    super();
    this.pending = new Map();
    this.sequence = 0;
    this.child = spawn(executable, ['--ipc'], {windowsHide: true, stdio: ['pipe', 'pipe', 'pipe']});
    this.child.stdin.on('error', error => this.fail(error));
    this.child.on('error', error => this.fail(error));
    this.child.on('exit', (code, signal) => {
      this.exited = true;
      this.fail(new Error(`진단 엔진 종료 (code=${code}, signal=${signal})`));
    });
    this.child.stderr.on('data', data => this.emit('diagnostic', String(data)));
    readline.createInterface({input: this.child.stdout}).on('line', line => this.accept(line));
  }
  accept(line) {
    let message;
    try { message = JSON.parse(line.replace(/^\uFEFF/, '')); }
    catch { this.emit('diagnostic', `엔진 응답 형식 오류: ${line.slice(0, 240)}`); return; }
    if (message.type === 'response') {
      const request = this.pending.get(message.id);
      if (!request) return;
      clearTimeout(request.timer);
      this.pending.delete(message.id);
      // A rejected hardware command can still change selection/server state.
      request.resolve(message);
    } else this.emit('message', message);
  }
  fail(error) {
    for (const p of this.pending.values()) { clearTimeout(p.timer); p.reject(error); }
    this.pending.clear();
    this.emit('diagnostic', error.message);
    this.emit('offline', error.message);
  }
  request(command, data = {}) {
    if (this.exited) return Promise.reject(new Error('진단 엔진이 종료되었습니다. 프로그램을 다시 실행하세요.'));
    const id = String(++this.sequence);
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error('진단 응답 시간 초과. 진행 중인 하드웨어 작업은 취소되지 않았습니다. 잠시 후 상태를 다시 확인하세요.'));
      }, 120000);
      this.pending.set(id, {resolve, reject, timer});
      this.child.stdin.write(JSON.stringify({id, command, data}) + '\n');
    });
  }
  async close() {
    if (this.exited) return;
    const force = setTimeout(() => {
      if (this.exited) return;
      const killer = spawn('taskkill.exe', ['/pid', String(this.child.pid), '/T', '/F'], {windowsHide: true, stdio: 'ignore'});
      killer.on('error', () => this.child.kill());
    }, 6000);
    try {
      await Promise.race([this.request('quit'), new Promise(resolve => this.child.once('exit', resolve))]);
    } catch { /* The process may have already exited. */ }
    finally { clearTimeout(force); this.child.stdin.end(); }
  }
}
module.exports = {Backend};
