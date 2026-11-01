const { app, BrowserWindow, Menu, session, ipcMain } = require('electron');
const path = require('node:path');
const fs = require('node:fs');
app.setAppUserModelId('studio.muworld.rhythm');
if (process.env.MUWORLD_DATA_DIR) app.setPath('userData', process.env.MUWORLD_DATA_DIR);
let win;
if (process.env.MUWORLD_TEST !== '1' && !app.requestSingleInstanceLock()) app.quit();
app.on('second-instance', () => { if(win) { if(win.isMinimized())win.restore();win.focus(); } });
app.whenReady().then(() => {
  Menu.setApplicationMenu(null);
  session.defaultSession.setPermissionRequestHandler((_wc, _permission, callback) => callback(false));
  session.defaultSession.webRequest.onBeforeRequest({ urls: ['http://*/*', 'https://*/*', 'ws://*/*', 'wss://*/*'] }, (_details, callback) => callback({ cancel: true }));
  win = new BrowserWindow({
    width: 1500, height: 1000, minWidth: 1080, minHeight: 740,
    show: process.env.MUWORLD_TEST !== '1', backgroundColor: '#12161e',
    title: 'MuWorld — Find your rhythm', icon: path.join(__dirname, '..', 'assets', 'icon.ico'),
    titleBarStyle: 'hidden', titleBarOverlay: { color: '#12161e', symbolColor: '#a6afbf', height: 36 },
    autoHideMenuBar: true,
    webPreferences: { nodeIntegration: false, contextIsolation: true, sandbox: true, backgroundThrottling: false, devTools: true, preload: path.join(__dirname, 'preload.cjs') }
  });
  win.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  win.webContents.on('will-navigate', (event) => event.preventDefault());
  const allowed = new Map([['muworld:v1','library.json'],['muworld:v1:backup','library.backup.json'],['muworld:favorites','favorites.json'],['muworld:arcade','arcade.json'],['muworld:profiles','profiles.json'],['muworld:profiles:backup','profiles.backup.json']]);
  ipcMain.on('muworld-storage', (event, operation, key, value) => {
    try {
      if(event.sender !== win.webContents || !allowed.has(key)) throw new Error('허용되지 않는 데이터 접근입니다.');
      const directory = path.join(app.getPath('userData'), 'library');
      const filename = path.join(directory, allowed.get(key));
      if(operation === 'get') event.returnValue = {ok:true,value:fs.existsSync(filename)?fs.readFileSync(filename,'utf8'):null};
      else if(operation === 'set') {
        if(typeof value !== 'string' || Buffer.byteLength(value,'utf8')>60000000) throw new Error('저장할 데이터가 너무 큽니다.');
        fs.mkdirSync(directory,{recursive:true});
        fs.writeFileSync(filename+'.tmp',value,'utf8');
        fs.renameSync(filename+'.tmp',filename);
        event.returnValue={ok:true};
      } else throw new Error('지원하지 않는 저장 동작입니다.');
    } catch(error) { event.returnValue={ok:false,error:error.message}; }
  });
  win.loadFile(path.join(__dirname, '..', 'dist', 'index.html'), process.env.MUWORLD_TEST === '1' ? {query:{qa:'1'}} : {});
});
app.on('window-all-closed', () => app.quit());
