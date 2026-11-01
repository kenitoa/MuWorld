import { _electron as electron } from 'playwright';
import { mkdir, mkdtemp, writeFile } from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
const root=process.cwd(); await mkdir('artifacts',{recursive:true});
const profile=await mkdtemp(path.join(root,'artifacts','qa-profile-'));
const env={...process.env,MUWORLD_TEST:'1',MUWORLD_DATA_DIR:profile};delete env.ELECTRON_RUN_AS_NODE;
const desktop=await electron.launch({args:['.'],cwd:root,env,timeout:60000});
const page=await desktop.firstWindow();const errors=[];page.on('pageerror',error=>errors.push(error.message));
page.on('console',msg=>{if(msg.type()==='error')errors.push(msg.text());});
const screenshot=async name=>{await page.waitForTimeout(200);await page.screenshot({path:`artifacts/${name}.png`});};
try {
 await page.waitForSelector('.library-layout');await page.evaluate(()=>document.fonts.ready);await screenshot('01-library');
 assert.equal(await page.locator('.song-row').count(),5);
 await page.locator('[data-action="select-song"][data-id="paper-moon"]').click();assert.equal(await page.locator('.selected-title h2').textContent(),'Paper Moon');
 await page.locator('.track-panel [data-action="favorite"]').click();
 await page.locator('[data-action="filter"][data-filter="favorites"]').click();assert.equal(await page.locator('.song-row').count(),1);
 await page.locator('.nav-item[data-route="settings"]').click();await page.locator('[data-offset="inputOffsetUs"]').fill('5');await page.locator('[data-offset="inputOffsetUs"]').blur();
 assert.equal(await page.evaluate(()=>__muworld.settings.inputOffsetUs),5000);await screenshot('02-settings');
 await page.locator('.nav-item[data-route="practice"]').click();await screenshot('03-practice');
 assert.equal(await page.locator('.drill-card').count(),12);
 await page.evaluate(()=>{__muworld.selected='glasshouse';__muworld.difficulty='easy';return __muworld.startGame({mode:'practice',rate:1,fromUs:4000000,toUs:9000000});});
 await page.waitForSelector('#game-canvas');await page.waitForTimeout(2100);await screenshot('04-gameplay');
 const events=await page.evaluate(()=>__muworld.session.engine.notes.flatMap(n=>[{time:n.timeUs+5000,lane:n.lane,pressed:true},{time:(n.endUs||n.timeUs+60000)+6000,lane:n.lane,pressed:false}]).sort((a,b)=>a.time-b.time));
 for(const e of events){const remain=await page.evaluate(time=>(time-__muworld.audio.songTimeUs())/1000,e.time);if(remain>0)await page.waitForTimeout(remain);const keys=['d','f','j','k'];if(e.pressed)await page.keyboard.down(keys[e.lane]);else await page.keyboard.up(keys[e.lane]);}
 await page.waitForSelector('.result-hero',{timeout:15000});await screenshot('05-result');
 const result=await page.evaluate(()=>({summary:__muworld.result.summary,records:__muworld.records.length,offset:__muworld.result.inputOffsetUs}));assert.equal(result.records,1);assert.equal(result.offset,5000);assert.ok(result.summary.counts.PERFECT+result.summary.counts.GREAT>0,'actual keyboard events should hit some notes');
 for(const tab of ['timing','patterns','timeline']){await page.locator(`[data-action="result-tab"][data-tab="${tab}"]`).click();await screenshot(`06-result-${tab}`);}
 await page.locator('[data-action="replay"]').click();await page.waitForSelector('.result-hero',{timeout:20000});assert.equal(await page.evaluate(()=>__muworld.records.length),1,'replay must not create a result');
 await page.locator('.nav-item[data-route="records"]').click();await screenshot('07-records');
 await page.locator('.nav-item[data-route="arcade"]').click();await screenshot('08-arcade');
 await page.locator('[data-action="arcade-start"][data-kind="echo"]').click();await screenshot('09-echo');
 await page.locator('.nav-item[data-route="library"]').click();assert.equal(await page.locator('.library-layout').count(),1);
 await page.reload();await page.waitForSelector('.library-layout');assert.equal(await page.evaluate(()=>__muworld.records.length),1,'native persistence survives reload');assert.equal(await page.evaluate(()=>__muworld.settings.inputOffsetUs),5000);
 assert.deepEqual(errors,[]);await writeFile('artifacts/smoke-results.json',JSON.stringify({passed:true,result,errors,profile},null,2));console.log(JSON.stringify({passed:true,recordedHits:result.summary.counts,errors,screenshots:10},null,2));
} finally {await desktop.close();}
