import { packager } from '@electron/packager';
import path from 'node:path';
await import('./build.mjs');
const workspace=process.cwd();
const out=path.resolve(workspace,'release');
if(!out.startsWith(path.resolve(workspace)+path.sep))throw new Error('Package output must remain inside the workspace.');
const result=await packager({
  dir:workspace,name:'MuWorld',platform:'win32',arch:'x64',
  icon:path.join(workspace,'assets','icon.ico'),out,overwrite:true,asar:true,
  ignore:[/^\/(tests|scripts|artifacts|release|src|docs|\.git)(\/|$)/],
  win32metadata:{CompanyName:'MuWorld Studio',FileDescription:'MuWorld Rhythm Studio',ProductName:'MuWorld',InternalName:'MuWorld'},
});
console.log('Packaged:',result.join('\n'));
