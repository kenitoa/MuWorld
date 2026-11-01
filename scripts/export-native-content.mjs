import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { SONGS, DIFFICULTIES, getChart, getDrills, validateChart } from '../legacy/electron/src/core/content.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const destination = path.join(root, 'native', 'MuWorld.Native', 'Assets', 'content.json');
const charts = SONGS.flatMap(song => DIFFICULTIES.map(difficulty => getChart(song.id, difficulty.id)));
const drills = getDrills();
for (const chart of [...charts, ...drills]) {
  const errors = validateChart(chart);
  if (errors.length) throw new Error(`${chart.id}: ${errors.join('; ')}`);
}
const content = JSON.stringify({ version: 1, songs: SONGS, difficulties: DIFFICULTIES, charts, drills }, null, 2) + '\n';
if (process.argv.includes('--check')) {
  if (await readFile(destination, 'utf8') !== content) throw new Error('Native content is out of date. Run node scripts/export-native-content.mjs.');
  console.log(`Native content verified: ${SONGS.length} songs, ${charts.length} charts, ${drills.length} drills.`);
} else {
  await mkdir(path.dirname(destination), { recursive: true });
  await writeFile(destination, content, 'utf8');
  console.log(`Exported native content: ${SONGS.length} songs, ${charts.length} charts, ${drills.length} drills. No JavaScript runtime is required by the native app.`);
}
