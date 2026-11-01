import { _electron as electron } from 'playwright';
import { readFile, writeFile, mkdir, mkdtemp } from 'node:fs/promises';
import path from 'node:path';
await mkdir('artifacts',{recursive:true});const profile=await mkdtemp(path.join(process.cwd(),'artifacts','icon-profile-'));
const env={...process.env,MUWORLD_TEST:'1',MUWORLD_DATA_DIR:profile};delete env.ELECTRON_RUN_AS_NODE;
const app=await electron.launch({args:['.'],cwd:process.cwd(),env});
try{const page=await app.firstWindow();await page.setViewportSize({width:256,height:256});const svg=await readFile('assets/icon.svg','utf8');await page.setContent(`<html><head><style>html,body{margin:0;background:transparent}svg{display:block;width:256px;height:256px}</style></head><body>${svg}</body></html>`);const png=await page.locator('svg').screenshot({omitBackground:true,scale:'css'});await writeFile('assets/icon.png',png);const header=Buffer.alloc(22);header.writeUInt16LE(1,2);header.writeUInt16LE(1,4);header.writeUInt16LE(1,10);header.writeUInt16LE(32,12);header.writeUInt32LE(png.length,14);header.writeUInt32LE(22,18);await writeFile('assets/icon.ico',Buffer.concat([header,png]));console.log('Created app icon');}finally{await app.close();}
