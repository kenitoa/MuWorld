import { build } from 'esbuild';
import { mkdir, cp, copyFile } from 'node:fs/promises';
await mkdir('dist', { recursive: true });
await build({ entryPoints: ['src/app.mjs'], bundle: true, outfile: 'dist/app.js', format: 'iife', target: 'chrome130', sourcemap: true, minify: false });
await copyFile('src/styles.css', 'dist/app.css');
await copyFile('index.html', 'dist/index.html');
await cp('assets', 'dist/assets', { recursive: true });
console.log('Built offline app → dist/index.html');
