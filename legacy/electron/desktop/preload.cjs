const { contextBridge, ipcRenderer } = require('electron');
function request(operation,key,value) {
  const result=ipcRenderer.sendSync('muworld-storage',operation,key,value);
  if(!result?.ok) throw new Error(result?.error||'로컬 데이터에 접근하지 못했습니다.');
  return result.value ?? null;
}
contextBridge.exposeInMainWorld('muworldStorage',{
  getItem:key=>request('get',key),
  setItem:(key,value)=>request('set',key,value)
});
