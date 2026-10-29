'use strict';
const {contextBridge, ipcRenderer} = require('electron');
contextBridge.exposeInMainWorld('bluetooth', Object.freeze({
  command: (command, data) => ipcRenderer.invoke('engine:command', command, data),
  saveReport: () => ipcRenderer.invoke('report:save'),
  revealReport: () => ipcRenderer.invoke('report:reveal'),
  copy: text => ipcRenderer.invoke('clipboard:write', text),
  windowControl: action => ipcRenderer.send('window:control', action),
  onEvent: callback => {
    const listener = (_event, message) => callback(message);
    ipcRenderer.on('engine:event', listener);
    return () => ipcRenderer.removeListener('engine:event', listener);
  }
}));
