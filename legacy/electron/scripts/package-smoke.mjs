import { _electron as electron } from 'playwright';
import { mkdtemp, writeFile, access } from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
const workspace=process.cwd(),executablePath=path.join(workspace,'release','MuWorld-win32-x64','MuWorld.exe');
await access(executablePath);
const userData=await mkdtemp(path.join(workspace,'artifacts','packaged-profile-'));
const env={...process.env,MUWORLD_TEST:'1',MUWORLD_DATA_DIR:userData};delete env.ELECTRON_RUN_AS_NODE;
const desktop=await electron.launch({executablePath,args:[],env,timeout:60000});
const page=await desktop.firstWindow(),errors=[],remoteRequests=[];
page.on('pageerror',error=>errors.push(error.message));page.on('request',r=>{if(/^https?:/.test(r.url()))remoteRequests.push(r.url());});
try{
 await page.waitForSelector('.library-layout');await page.evaluate(()=>document.fonts.ready);
 await desktop.evaluate(({BrowserWindow})=>BrowserWindow.getAllWindows()[0].showInactive());
 assert.equal(await desktop.evaluate(({app})=>app.isPackaged),true);
 assert.equal(await page.locator('.song-row').count(),5);
 const play=await page.locator('.play-button').boundingBox(),viewport=await page.evaluate(()=>({height:innerHeight,width:innerWidth}));
 assert.ok(play.y+play.height<viewport.height,'Play button must be visible without scrolling');
 await page.screenshot({path:'artifacts/10-packaged-library.png'});
 await page.locator('.nav-item[data-route="settings"]').click();await page.locator('[data-offset="inputOffsetUs"]').fill('5');await page.locator('[data-offset="inputOffsetUs"]').blur();
 await page.reload();await page.waitForSelector('.library-layout');assert.equal(await page.evaluate(()=>__muworld.settings.inputOffsetUs),5000);
 assert.deepEqual(errors,[]);assert.deepEqual(remoteRequests,[]);
 const result={passed:true,isPackaged:true,remoteRequests,errors,viewport,playButtonVisible:true};await writeFile('artifacts/package-smoke.json',JSON.stringify(result,null,2));console.log(JSON.stringify(result,null,2));
}finally{await desktop.close();}
