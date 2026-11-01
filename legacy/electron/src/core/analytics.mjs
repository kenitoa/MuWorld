const WEIGHTS = Object.freeze({ PERFECT: 1, GREAT: 0.9, GOOD: 0.6, BAD: 0.2, MISS: 0 });
const SEGMENT_US = 10_000_000;
const finite = value => typeof value === 'number' && Number.isFinite(value);
const mean = values => values.length ? values.reduce((sum, value) => sum + value, 0) / values.length : null;
const median = values => {
  if (!values.length) return null;
  const sorted = [...values].sort((a, b) => a - b);
  const middle = Math.floor(sorted.length / 2);
  return sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
};
const percent = (part, total) => total ? part / total * 100 : null;
const eventTime = judgment => finite(judgment.targetUs) ? judgment.targetUs : judgment.timeUs;
const validJudgment = judgment => judgment && Object.hasOwn(WEIGHTS, judgment.grade) && Number.isInteger(judgment.lane) && judgment.lane >= 0 && judgment.lane < 4;
const isTimed = judgment => judgment.kind !== 'hold' && judgment.grade !== 'MISS' && finite(judgment.deltaUs);

/** All timing statistics use corrected real-time deltas, never MISS or sustain units. */
export function analyzeRun(summary = {}, chart = {}) {
  const judgments = Array.isArray(summary.judgments) ? summary.judgments.filter(validJudgment) : [];
  const heads = judgments.filter(judgment => judgment.kind !== 'hold');
  const timedHeads = heads.filter(isTimed);
  const times = timedHeads.map(judgment => judgment.deltaUs / 1000);
  const center = mean(times);
  const midpoint = median(times);
  const absolutes = times.map(Math.abs).sort((a, b) => a - b);
  const timing = {
    count: times.length,
    meanMs: center,
    medianMs: midpoint,
    maeMs: mean(absolutes),
    stdMs: times.length ? Math.sqrt(mean(times.map(value => (value - center) ** 2))) : null,
    madMs: times.length ? median(times.map(value => Math.abs(value - midpoint))) : null,
    p95Ms: absolutes.length ? absolutes[Math.ceil(absolutes.length * 0.95) - 1] : null,
    early: times.filter(value => value < 0).length,
    late: times.filter(value => value > 0).length,
    exact: times.filter(value => value === 0).length,
  };
  const missCount = heads.filter(judgment => judgment.grade === 'MISS').length;
  const lanes = Array.from({ length: 4 }, (_, lane) => {
    const events = heads.filter(judgment => judgment.lane === lane);
    const deltas = events.filter(isTimed).map(judgment => judgment.deltaUs / 1000);
    const misses = events.filter(judgment => judgment.grade === 'MISS').length;
    return { lane, count: events.length, timingCount: deltas.length, misses, missRate: percent(misses, events.length), meanMs: mean(deltas), maeMs: mean(deltas.map(Math.abs)) };
  });
  const patternGroups = new Map();
  for (const judgment of heads) {
    const pattern = typeof judgment.pattern === 'string' && judgment.pattern ? judgment.pattern : 'unknown';
    if (!patternGroups.has(pattern)) patternGroups.set(pattern, []);
    patternGroups.get(pattern).push(judgment);
  }
  const patterns = [...patternGroups].map(([pattern, events]) => {
    const misses = events.filter(judgment => judgment.grade === 'MISS').length;
    const deltas = events.filter(isTimed).map(judgment => judgment.deltaUs / 1000);
    return { pattern, count: events.length, timingCount: deltas.length, misses, missRate: percent(misses, events.length), meanMs: mean(deltas) };
  }).sort((a, b) => b.count - a.count || a.pattern.localeCompare(b.pattern));

  const finalTarget = judgments.reduce((maximum, judgment) => Math.max(maximum, finite(eventTime(judgment)) ? eventTime(judgment) + 1 : 0), 0);
  const durationUs = Math.max(finite(chart.durationUs) ? chart.durationUs : 0, finalTarget);
  const segments = [];
  for (let startUs = 0; startUs < durationUs; startUs += SEGMENT_US) {
    const endUs = Math.min(startUs + SEGMENT_US, durationUs);
    const events = judgments.filter(judgment => eventTime(judgment) >= startUs && eventTime(judgment) < endUs);
    const misses = events.filter(judgment => judgment.grade === 'MISS').length;
    const weighted = events.reduce((total, judgment) => total + WEIGHTS[judgment.grade], 0);
    segments.push({ startUs, endUs, count: events.length, misses, accuracy: events.length ? weighted / events.length * 100 : null });
  }

  const maximumDelta = absolutes.length ? absolutes.at(-1) : 0;
  const extent = Math.max(120, Math.ceil(maximumDelta / 5) * 5);
  const histogram = Array.from({ length: extent * 2 / 5 }, (_, index) => ({ fromMs: -extent + index * 5, toMs: -extent + (index + 1) * 5, count: 0 }));
  for (const delta of times) {
    const index = Math.min(histogram.length - 1, Math.max(0, Math.floor((delta + extent) / 5)));
    histogram[index].count++;
  }

  const chordGroups = new Map();
  for (const judgment of heads) {
    if (!finite(judgment.targetUs)) continue;
    if (!chordGroups.has(judgment.targetUs)) chordGroups.set(judgment.targetUs, []);
    chordGroups.get(judgment.targetUs).push(judgment);
  }
  const allChords = [...chordGroups.values()].filter(events => new Set(events.map(event => event.lane)).size >= 2);
  const spreads = allChords.filter(events => events.every(isTimed)).map(events => {
    const deltas = events.map(event => event.deltaUs / 1000);
    return Math.max(...deltas) - Math.min(...deltas);
  });
  const chords = { count: allChords.length, measuredCount: spreads.length, medianSpreadMs: median(spreads) };
  const chartHolds = new Set((Array.isArray(chart.notes) ? chart.notes : []).filter(note => finite(note.endUs)).map(note => note.id));
  const holdHeads = heads.filter(judgment => chartHolds.has(judgment.noteId));
  const sustains = judgments.filter(judgment => judgment.kind === 'hold');
  const broken = sustains.filter(judgment => judgment.grade === 'MISS').length;
  const holds = { total: Math.max(holdHeads.length, sustains.length), evaluated: sustains.length, broken, successRate: percent(sustains.length - broken, sustains.length) };
  const weakestSegment = segments.filter(segment => segment.count >= 4).sort((a, b) => a.accuracy - b.accuracy || b.misses - a.misses || a.startUs - b.startUs)[0] ?? null;
  const recommendations = [];
  if (weakestSegment && weakestSegment.accuracy < 95) {
    recommendations.push({ title: '실수가 모인 구간 반복', body: `${Math.floor(weakestSegment.startUs / 1_000_000)}–${Math.ceil(weakestSegment.endUs / 1_000_000)}초 구간의 판정 ${weakestSegment.count}개 중 ${weakestSegment.misses}개가 MISS였습니다. 낮은 속도로 확인한 뒤 원래 속도로 돌아오세요.`, fromUs: Math.max(0, weakestSegment.startUs - 2_000_000), toUs: Math.min(durationUs, weakestSegment.endUs + 2_000_000) });
  }
  if (timing.count >= 12 && Math.abs(timing.medianMs) >= 8 && timing.madMs <= 15) {
    recommendations.push({ title: '한 방향으로 치우친 타이밍 확인', body: `판정된 입력 ${timing.count}개의 중앙값이 ${timing.medianMs.toFixed(1)}ms입니다. 연주 습관과 현재 장치·보정 설정을 함께 확인하세요. 이 값만으로 장치 지연을 확정할 수는 없습니다.`, fromUs: 0, toUs: Math.min(durationUs, 20_000_000) });
  }
  if (recommendations.length < 2 && holds.broken > 0) {
    const firstBreak = sustains.find(judgment => judgment.grade === 'MISS');
    const position = Math.max(0, eventTime(firstBreak) || 0);
    recommendations.push({ title: '홀드 유지 연습', body: `확인된 홀드 ${holds.evaluated}개 중 ${holds.broken}개가 끊겼습니다. 홀드를 유지한 채 다른 레인을 누르는 구간을 확인하세요.`, fromUs: Math.max(0, position - 5_000_000), toUs: Math.min(durationUs, position + 5_000_000) });
  }
  if (!recommendations.length && heads.length) {
    recommendations.push({ title: '같은 조건으로 한 번 더 확인', body: '같은 채보와 속도에서 다시 연주해 정확도와 타이밍 분산이 얼마나 일정한지 확인하세요.', fromUs: 0, toUs: Math.min(durationUs, 20_000_000) });
  }
  return { timing, lanes, patterns, segments, histogram, chords, holds, recommendations: recommendations.slice(0, 2), headCount: heads.length, missCount, missRate: percent(missCount, heads.length), accuracy: finite(summary.accuracy) ? summary.accuracy : (judgments.length ? judgments.reduce((sum, judgment) => sum + WEIGHTS[judgment.grade], 0) / judgments.length * 100 : null), weakestSegment };
}

function stable(value) {
  if (Array.isArray(value)) return `[${value.map(stable).join(',')}]`;
  if (value && typeof value === 'object') return `{${Object.keys(value).sort().map(key => `${JSON.stringify(key)}:${stable(value[key])}`).join(',')}}`;
  return JSON.stringify(value) ?? 'null';
}

function comparisonKey(record) {
  return stable({ songId: record.songId, chartId: record.chartId, chartVersion: record.chartVersion, difficulty: record.difficulty, mode: record.mode, rate: record.rate, inputOffsetUs: record.inputOffsetUs, windowScale: record.settings?.windowScale ?? 1, keys: record.settings?.keys ?? null, environment: record.environment ?? null });
}

/** Only completed, uninterrupted standard runs with identical conditions are comparable. */
export function compareRuns(records = []) {
  const supplied = Array.isArray(records) ? records : [];
  const eligible = supplied.filter(record => record && record.mode === 'standard' && record.completed === true && record.interrupted === false && typeof record.songId === 'string' && typeof record.chartId === 'string' && typeof record.chartVersion === 'string' && typeof record.difficulty === 'string' && finite(record.rate) && Number.isSafeInteger(record.inputOffsetUs) && finite(record.summary?.accuracy) && Number.isFinite(Date.parse(record.createdAt)));
  const grouped = new Map();
  for (const record of eligible) {
    const key = comparisonKey(record);
    if (!grouped.has(key)) grouped.set(key, []);
    grouped.get(key).push(record);
  }
  const difference = (latest, previous) => finite(latest) && finite(previous) ? latest - previous : null;
  const groups = [...grouped].map(([key, entries]) => {
    const ordered = [...entries].sort((a, b) => Date.parse(a.createdAt) - Date.parse(b.createdAt) || String(a.id).localeCompare(String(b.id)));
    const latest = ordered.at(-1);
    const previous = ordered.at(-2) ?? null;
    return { key, label: `${latest.songId} · ${latest.difficulty} · ${latest.rate}×`, records: ordered, count: ordered.length, latest, previous, bestAccuracy: Math.max(...ordered.map(record => record.summary.accuracy)), accuracyDelta: difference(latest.summary.accuracy, previous?.summary?.accuracy), maeDeltaMs: difference(latest.analysis?.timing?.maeMs, previous?.analysis?.timing?.maeMs), missRateDelta: difference(latest.analysis?.missRate, previous?.analysis?.missRate) };
  }).sort((a, b) => Date.parse(b.latest.createdAt) - Date.parse(a.latest.createdAt));
  return { groups, eligibleCount: eligible.length, excludedCount: supplied.length - eligible.length };
}

function csvCell(value) {
  let text = value == null ? '' : String(value);
  // Prevent user-authored labels from being interpreted as spreadsheet formulas.
  if (typeof value === 'string' && /^[\t\r ]*[=+\-@]/.test(text)) text = `'${text}`;
  return `"${text.replaceAll('"', '""')}"`;
}

/** Export actual individual judgments, including missing deltas as empty cells. */
export function toCSV(record = {}) {
  const rows = [
    ['MuWorld 기록', '값'],
    ['id', record.id], ['createdAt', record.createdAt], ['songId', record.songId], ['chartId', record.chartId], ['chartVersion', record.chartVersion], ['difficulty', record.difficulty], ['mode', record.mode], ['rate', record.rate], ['inputOffsetUs', record.inputOffsetUs], ['completed', record.completed], ['interrupted', record.interrupted], ['accuracy', record.summary?.accuracy],
    [],
    ['noteId', 'lane', 'kind', 'targetUs', 'timeUs', 'deltaUs', 'grade', 'pattern'],
  ];
  for (const judgment of Array.isArray(record.summary?.judgments) ? record.summary.judgments : []) rows.push([judgment.noteId, judgment.lane, judgment.kind, judgment.targetUs, judgment.timeUs, judgment.deltaUs, judgment.grade, judgment.pattern]);
  return '\uFEFF' + rows.map(row => row.map(csvCell).join(',')).join('\r\n') + '\r\n';
}
