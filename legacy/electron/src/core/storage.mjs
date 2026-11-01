const VERSION = 1;
const PRIMARY_KEY = 'muworld:v1';
const BACKUP_KEY = 'muworld:v1:backup';
const MAX_RECORDS = 500;
const MAX_BACKUP_BYTES = 20_000_000;
const BOOLEAN_SETTINGS = new Set(['showTiming', 'showCombo', 'highContrast', 'metronome', 'autoStep']);
const RANGES = Object.freeze({ inputOffsetUs: [-500_000, 500_000, true], visualOffsetUs: [-500_000, 500_000, true], approachMs: [300, 3000], volume: [0, 1], effectVolume: [0, 1], laneWidth: [60, 180], noteBrightness: [10, 150], backgroundBrightness: [0, 150], effects: [0, 100], uiScale: [75, 150], practiceRate: [0.5, 1], practiceFromSec: [0, 3599], practiceToSec: [0.001, 3600], windowScale: [0.25, 2] });
export const DEFAULT_SETTINGS = Object.freeze({ keys: Object.freeze(['KeyD', 'KeyF', 'KeyJ', 'KeyK']), inputOffsetUs: 0, visualOffsetUs: 0, approachMs: 1200, volume: 0.7, effectVolume: 0.3, laneWidth: 100, noteBrightness: 90, backgroundBrightness: 100, effects: 25, showTiming: false, showCombo: true, highContrast: false, uiScale: 100, preset: 'comfort', metronome: false, practiceRate: 0.8, practiceFromSec: 0, practiceToSec: 20, autoStep: false, windowScale: 1 });
let fallback = null;
const clone = value => JSON.parse(JSON.stringify(value));
const plain = value => value !== null && typeof value === 'object' && !Array.isArray(value) && (Object.getPrototypeOf(value) === Object.prototype || Object.getPrototypeOf(value) === null);
const finite = value => typeof value === 'number' && Number.isFinite(value);
const safeString = (value, max = 300) => typeof value === 'string' && value.length > 0 && value.length <= max;
function fail(message) { throw new RangeError(message); }

function validateSettings(input, base = DEFAULT_SETTINGS) {
  if (!plain(input)) fail('설정은 객체여야 합니다.');
  const merged = { ...base };
  for (const [key, value] of Object.entries(input)) {
    if (!Object.hasOwn(DEFAULT_SETTINGS, key)) fail(`알 수 없는 설정: ${key}`);
    if (key === 'keys') {
      if (!Array.isArray(value) || value.length !== 4 || value.some(code => typeof code !== 'string' || !/^[A-Za-z][A-Za-z0-9]{0,39}$/.test(code) || ['Escape', 'Tab', 'MetaLeft', 'MetaRight'].includes(code)) || new Set(value).size !== 4) fail('서로 다른 네 개의 유효한 키가 필요합니다.');
      merged.keys = [...value];
    } else if (BOOLEAN_SETTINGS.has(key)) {
      if (typeof value !== 'boolean') fail(`${key} 설정은 참/거짓이어야 합니다.`);
      merged[key] = value;
    } else if (key === 'preset') {
      if (!['comfort', 'focus', 'custom', 'highContrast'].includes(value)) fail('알 수 없는 화면 프리셋입니다.');
      merged[key] = value;
    } else {
      const [minimum, maximum, integer] = RANGES[key];
      if (!finite(value) || value < minimum || value > maximum || (integer && !Number.isSafeInteger(value))) fail(`${key} 설정이 허용 범위를 벗어났습니다.`);
      merged[key] = value;
    }
  }
  if (!(merged.practiceToSec > merged.practiceFromSec)) fail('연습 종료는 시작보다 뒤여야 합니다.');
  return clone(merged);
}

function validateJSON(value, depth = 0) {
  if (depth > 12) fail('기록 구조가 지나치게 깊습니다.');
  if (value == null || typeof value === 'boolean') return;
  if (typeof value === 'number') { if (!Number.isFinite(value)) fail('유효하지 않은 숫자가 있습니다.'); return; }
  if (typeof value === 'string') { if (value.length > 20_000) fail('문자열이 너무 깁니다.'); return; }
  if (Array.isArray(value)) { if (value.length > 100_000) fail('기록 항목이 너무 많습니다.'); value.forEach(item => validateJSON(item, depth + 1)); return; }
  if (!plain(value)) fail('기록에는 JSON 데이터만 저장할 수 있습니다.');
  for (const [key, item] of Object.entries(value)) {
    if (['__proto__', 'prototype', 'constructor'].includes(key)) fail('허용되지 않는 속성이 있습니다.');
    validateJSON(item, depth + 1);
  }
}

function validateRecord(record) {
  if (!plain(record)) fail('기록은 객체여야 합니다.');
  validateJSON(record);
  for (const field of ['id', 'songId', 'chartId', 'chartVersion', 'difficulty']) if (!safeString(record[field])) fail(`기록의 ${field} 값이 유효하지 않습니다.`);
  if (!safeString(record.createdAt) || !Number.isFinite(Date.parse(record.createdAt))) fail('기록 날짜가 유효하지 않습니다.');
  if (!['standard', 'practice', 'echo'].includes(record.mode)) fail('기록 모드가 유효하지 않습니다.');
  if (!finite(record.rate) || record.rate < 0.25 || record.rate > 2) fail('기록 속도가 유효하지 않습니다.');
  if (!Number.isSafeInteger(record.inputOffsetUs) || Math.abs(record.inputOffsetUs) > 500_000) fail('기록 보정값이 유효하지 않습니다.');
  if (typeof record.completed !== 'boolean' || typeof record.interrupted !== 'boolean') fail('기록 완료 상태가 유효하지 않습니다.');
  const settings = validateSettings(record.settings);
  if (settings.inputOffsetUs !== record.inputOffsetUs) fail('기록과 설정의 입력 보정값이 다릅니다.');
  const summary = record.summary;
  if (!plain(summary) || !finite(summary.accuracy) || summary.accuracy < 0 || summary.accuracy > 100 || !finite(summary.score) || summary.score < 0 || summary.score > 1_000_000) fail('기록 점수가 유효하지 않습니다.');
  for (const field of ['combo', 'maxCombo', 'totalUnits', 'judgedUnits', 'extraInputs']) if (!Number.isSafeInteger(summary[field]) || summary[field] < 0) fail(`기록의 ${field} 값이 유효하지 않습니다.`);
  if (summary.judgedUnits > summary.totalUnits || summary.combo > summary.maxCombo || summary.maxCombo > summary.judgedUnits) fail('기록의 판정 수가 일치하지 않습니다.');
  if (!plain(summary.counts)) fail('판정 집계가 없습니다.');
  let count = 0;
  for (const grade of ['PERFECT', 'GREAT', 'GOOD', 'BAD', 'MISS']) {
    if (!Number.isSafeInteger(summary.counts[grade]) || summary.counts[grade] < 0) fail('판정 집계가 유효하지 않습니다.');
    count += summary.counts[grade];
  }
  if (count !== summary.judgedUnits) fail('판정 집계와 판정 수가 다릅니다.');
  if (!Array.isArray(summary.judgments) || !Array.isArray(summary.inputs) || summary.judgments.length !== summary.judgedUnits) fail('입력 또는 판정 목록이 유효하지 않습니다.');
  const actualCounts = { PERFECT: 0, GREAT: 0, GOOD: 0, BAD: 0, MISS: 0 };
  for (const event of summary.judgments) {
    const validNoteId = event && (safeString(event.noteId) || (Number.isSafeInteger(event.noteId) && event.noteId >= 0));
    if (!plain(event) || !validNoteId || !Number.isInteger(event.lane) || event.lane < 0 || event.lane > 3 || !Number.isSafeInteger(event.timeUs) || !Number.isSafeInteger(event.targetUs) || !['head', 'hold'].includes(event.kind) || !Object.hasOwn(actualCounts, event.grade) || !(event.deltaUs === null || finite(event.deltaUs))) fail('개별 판정이 유효하지 않습니다.');
    if (event.grade === 'MISS' && event.deltaUs !== null) fail('MISS 판정에는 타이밍 편차가 없어야 합니다.');
    if (event.kind === 'head' && event.grade !== 'MISS' && !finite(event.deltaUs)) fail('성공한 입력의 타이밍 편차가 없습니다.');
    actualCounts[event.grade]++;
  }
  if (Object.keys(actualCounts).some(grade => actualCounts[grade] !== summary.counts[grade])) fail('개별 판정과 판정 집계가 다릅니다.');
  for (const event of summary.inputs) if (!plain(event) || !Number.isInteger(event.lane) || event.lane < 0 || event.lane > 3 || typeof event.pressed !== 'boolean' || !Number.isSafeInteger(event.timeUs)) fail('개별 입력이 유효하지 않습니다.');
  if (!plain(record.analysis) || !plain(record.environment)) fail('분석 또는 장치 정보가 유효하지 않습니다.');
  return clone({ ...record, settings });
}

function validateEnvelope(value) {
  if (!plain(value) || value.version !== VERSION) fail('지원하지 않는 백업 버전입니다.');
  const settings = validateSettings(value.settings);
  if (!Array.isArray(value.records) || value.records.length > MAX_RECORDS) fail(`기록은 ${MAX_RECORDS}개 이하여야 합니다.`);
  const records = value.records.map(validateRecord);
  if (new Set(records.map(record => record.id)).size !== records.length) fail('중복 기록 ID가 있습니다.');
  return { version: VERSION, settings, records };
}

function getStorage() {
  try { return globalThis.muworldStorage ?? globalThis.localStorage ?? null; } catch { return null; }
}
function initial() { return { version: VERSION, settings: clone(DEFAULT_SETTINGS), records: [] }; }
function parseEnvelope(raw) {
  if (typeof raw !== 'string' || raw.length > MAX_BACKUP_BYTES) return null;
  try { return validateEnvelope(JSON.parse(raw)); } catch { return null; }
}
function readState() {
  const storage = getStorage();
  if (!storage) return clone(fallback ?? initial());
  try {
    const state = parseEnvelope(storage.getItem(PRIMARY_KEY)) ?? parseEnvelope(storage.getItem(BACKUP_KEY));
    if (state) { fallback = clone(state); return state; }
  } catch { return clone(fallback ?? initial()); }
  return clone(fallback ?? initial());
}
function commit(next) {
  const validated = validateEnvelope(next);
  const text = JSON.stringify(validated);
  if (text.length > MAX_BACKUP_BYTES) fail('저장할 기록이 너무 큽니다. CSV나 백업을 내보낸 뒤 오래된 기록을 정리하세요.');
  const storage = getStorage();
  if (storage) {
    let previous;
    try { previous = storage.getItem(PRIMARY_KEY); } catch { previous = null; }
    // One envelope makes imports atomic: one primary setItem, never split settings/records.
    try {
      if (parseEnvelope(previous)) {
        try { storage.setItem(BACKUP_KEY, previous); } catch { /* The valid primary remains available until commit. */ }
      }
      storage.setItem(PRIMARY_KEY, text);
    } catch (error) {
      throw new Error('로컬 저장 공간에 기록하지 못했습니다. 기존 데이터는 유지됩니다. 백업을 내보내고 저장 공간을 확인하세요.', { cause: error });
    }
  }
  fallback = clone(validated);
  return validated;
}

export function loadSettings() { return clone(readState().settings); }
export function saveSettings(partial) {
  const current = readState();
  const settings = validateSettings(partial, current.settings);
  commit({ ...current, settings });
  return clone(settings);
}
export function loadRecords() { return clone(readState().records); }
export function saveRecord(record) {
  const validated = validateRecord(record);
  const current = readState();
  const records = current.records.filter(existing => existing.id !== validated.id);
  records.push(validated);
  commit({ ...current, records });
  return clone(validated);
}
export function deleteRecord(id) {
  if (!safeString(id)) fail('삭제할 기록 ID가 유효하지 않습니다.');
  const current = readState();
  const records = current.records.filter(record => record.id !== id);
  if (records.length !== current.records.length) commit({ ...current, records });
  return records.length !== current.records.length;
}
export function clearRecords() {
  const current = readState();
  commit({ ...current, records: [] });
}
export function exportBackup() { return JSON.stringify({ schema: 'muworld.backup', ...readState(), exportedAt: new Date().toISOString() }, null, 2); }
export function importBackup(text) {
  if (typeof text !== 'string' || !text.trim() || text.length > MAX_BACKUP_BYTES) fail('백업 파일이 비어 있거나 너무 큽니다.');
  let parsed;
  try { parsed = JSON.parse(text); } catch { fail('올바른 JSON 백업 파일이 아닙니다.'); }
  if (parsed.schema !== undefined && parsed.schema !== 'muworld.backup') fail('MuWorld 백업 파일이 아닙니다.');
  const validated = validateEnvelope(parsed);
  commit(validated);
  return { importedRecords: validated.records.length, settings: clone(validated.settings) };
}
