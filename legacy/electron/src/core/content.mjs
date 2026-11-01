/** Original, deterministic scores. All chart timestamps are integer microseconds. */
export const DIFFICULTIES = Object.freeze([
  Object.freeze({ id: 'easy', label: '입문', name: 'EASY', description: '박자를 느끼며 천천히' }),
  Object.freeze({ id: 'normal', label: '기본', name: 'NORMAL', description: '리듬의 흐름을 따라' }),
  Object.freeze({ id: 'hard', label: '도전', name: 'HARD', description: '조금 더 섬세한 연주' }),
]);

const definitions = [
  {
    id: 'glasshouse', title: 'Glasshouse', subtitle: '빛이 머무는 공간', bpm: 100, bars: 32,
    color: '#b3a6db', accent: '#d2c9ed', genre: 'AMBIENT ELECTRONICA',
    tags: ['잔잔한 박자', '교대', '입문 추천'], description: '유리 위로 번지는 빛처럼. 부드러운 건반과 맑은 소리를 따라 첫 박자를 시작해 보세요.',
    level: { easy: 1, normal: 4, hard: 7 }, art: 0,
    chords: [[50, 57, 60, 64], [46, 53, 57, 60], [53, 60, 64, 69], [48, 55, 58, 62]],
    melody: [0, 2, 1, 3, 2, 1, 3, 2], groove: 'glass',
  },
  {
    id: 'afterglow', title: 'Afterglow', subtitle: '하루의 마지막 온도', bpm: 112, bars: 36,
    color: '#cb9865', accent: '#f0c390', genre: 'LO-FI GROOVE',
    tags: ['따뜻한 질감', '엇박', '홀드'], description: '천천히 식어가는 오후의 색. 따뜻한 베이스와 여유로운 그루브 사이에서 나만의 흐름을 찾아보세요.',
    level: { easy: 2, normal: 5, hard: 8 }, art: 1,
    chords: [[45, 52, 55, 59], [41, 48, 52, 57], [48, 55, 59, 64], [43, 50, 53, 57]],
    melody: [0, 1, 3, 2, 1, 3, 2, 0], groove: 'warm',
  },
  {
    id: 'paper-moon', title: 'Paper Moon', subtitle: '접어 둔 밤의 조각', bpm: 124, bars: 36,
    color: '#8da995', accent: '#bdd2bc', genre: 'DREAM GARAGE',
    tags: ['섬세한 리듬', '교대', '동시치기'], description: '작은 종이달 아래 겹쳐지는 소리. 여백 사이에 놓인 리듬을 섬세하게 읽어 보세요.',
    level: { easy: 2, normal: 6, hard: 9 }, art: 2,
    chords: [[52, 59, 62, 66], [48, 55, 59, 62], [55, 62, 66, 71], [50, 57, 60, 64]],
    melody: [2, 0, 3, 1, 2, 3, 1, 0], groove: 'paper',
  },
  {
    id: 'velvet-current', title: 'Velvet Current', subtitle: '고요 속의 움직임', bpm: 136, bars: 40,
    color: '#ba827d', accent: '#e2aaa0', genre: 'DEEP HOUSE',
    tags: ['단단한 비트', '연타', '홀드 복합'], description: '깊고 고른 비트 위에 흐르는 부드러운 선율. 흔들림 없는 타격으로 긴 흐름을 이어가 보세요.',
    level: { easy: 3, normal: 7, hard: 10 }, art: 3,
    chords: [[45, 52, 55, 59], [48, 55, 59, 62], [41, 48, 52, 55], [43, 50, 53, 57]],
    melody: [0, 2, 3, 1, 0, 3, 2, 1], groove: 'velvet',
  },
  {
    id: 'daybreak', title: 'Daybreak', subtitle: '다시, 새로운 리듬', bpm: 148, bars: 40,
    color: '#83a9bf', accent: '#b8d7e7', genre: 'MELODIC BREAKS',
    tags: ['경쾌한 선율', '동시치기', '복합 패턴'], description: '수평선 너머부터 조금씩 밝아지는 아침. 맑은 멜로디와 함께 한 단계 더 나아가세요.',
    level: { easy: 3, normal: 8, hard: 12 }, art: 4,
    chords: [[50, 57, 61, 66], [47, 54, 57, 61], [43, 50, 54, 59], [45, 52, 55, 59]],
    melody: [0, 3, 2, 1, 3, 2, 0, 1], groove: 'dawn',
  },
];

export const SONGS = Object.freeze(definitions.map(song => Object.freeze({
  ...song, artist: 'MUWORLD ORIGINALS', durationSec: (song.bars * 4 + 4) * 60 / song.bpm,
  level: Object.freeze(song.level), tags: Object.freeze(song.tags),
})));

export function getSong(songId) {
  const song = SONGS.find(item => item.id === songId);
  if (!song) throw new RangeError(`Unknown song: ${songId}`);
  return song;
}

export function beatToUs(song, beat) { return Math.round(beat * 60_000_000 / song.bpm); }

function addNote(notes, occupied, song, beat, lane, pattern, endBeat = null) {
  const timeUs = beatToUs(song, beat);
  // A held lane remains reserved through its endpoint. The next press needs a gap.
  if (timeUs <= (occupied[lane] ?? -Infinity)) return false;
  const note = { id: `n${notes.length}`, lane, timeUs, pattern };
  if (endBeat !== null) note.endUs = beatToUs(song, endBeat);
  notes.push(note);
  occupied[lane] = note.endUs ?? timeUs;
  return true;
}

function chartNotes(song, difficulty) {
  const notes = [], occupied = [-1, -1, -1, -1];
  const lastBeat = song.bars * 4 - 4;
  const laneOrder = [0, 2, 1, 3, 1, 2, 0, 3];
  let serial = 0;
  for (let barBeat = 8; barBeat < lastBeat; barBeat += 4) {
    const bar = barBeat / 4;
    // Each eighth-bar phrase ends with a small breathing space.
    const phraseEnd = bar % 8 === 7;
    if (difficulty === 'easy') {
      const positions = phraseEnd ? [0] : [0, 2];
      for (const pos of positions) {
        const lane = laneOrder[serial++ % laneOrder.length];
        const isHold = bar >= 6 && bar % 8 === 6 && pos === 0;
        addNote(notes, occupied, song, barBeat + pos, lane, isHold ? '홀드' : '교대', isHold ? barBeat + pos + 1.5 : null);
      }
    } else if (difficulty === 'normal') {
      const positions = phraseEnd ? [0, 1, 2] : bar % 4 === 2 ? [0, 1, 1.5, 2, 3, 3.5] : [0, 1, 2, 3];
      for (const pos of positions) {
        const lane = laneOrder[(serial++ + song.art) % laneOrder.length];
        const isHold = bar % 6 === 4 && pos === 0;
        const isChord = bar % 4 === 0 && pos === 2;
        addNote(notes, occupied, song, barBeat + pos, lane, isHold ? '홀드' : isChord ? '동시치기' : pos % 1 ? '엇박' : '교대', isHold ? barBeat + 1.5 : null);
        if (isChord) addNote(notes, occupied, song, barBeat + pos, (lane + 2) % 4, '동시치기');
      }
    } else {
      const positions = phraseEnd ? [0, .5, 1, 2, 2.5, 3] : bar % 4 === 2 ? [0, .5, 1, 1.5, 2, 2.25, 2.5, 2.75, 3, 3.5] : [0, .5, 1, 1.5, 2, 2.5, 3, 3.5];
      for (const pos of positions) {
        let lane = laneOrder[(serial++ + song.art) % laneOrder.length];
        const isJack = bar % 7 === 3 && pos >= 2;
        if (isJack) lane = (bar + song.art) % 4;
        const isHold = bar % 5 === 1 && pos === 0;
        const hasHold = bar % 5 === 1 && pos > 0 && pos < 3;
        const isChord = bar % 3 === 0 && (pos === 0 || pos === 2);
        const pattern = isHold ? '홀드' : hasHold ? '홀드 복합' : isChord ? '동시치기' : isJack ? '연타' : pos % .5 ? '연타' : '교대';
        addNote(notes, occupied, song, barBeat + pos, lane, pattern, isHold ? barBeat + 3 : null);
        if (isChord) addNote(notes, occupied, song, barBeat + pos, (lane + 2) % 4, '동시치기');
      }
    }
  }
  return notes.sort((a, b) => a.timeUs - b.timeUs || a.lane - b.lane).map((note, index) => ({ ...note, id: `${song.id}-${difficulty}-${index}` }));
}

const charts = new Map();
export function getChart(songId, difficulty = 'normal') {
  const song = getSong(songId);
  if (!DIFFICULTIES.some(item => item.id === difficulty)) throw new RangeError(`Unknown difficulty: ${difficulty}`);
  const key = `${songId}:${difficulty}`;
  if (!charts.has(key)) {
    const notes = chartNotes(song, difficulty);
    const chart = {
      id: `${songId}-${difficulty}-v1`, songId, difficulty, version: '1', bpm: song.bpm,
      durationUs: Math.round(song.durationSec * 1_000_000), notes,
    };
    const errors = validateChart(chart);
    if (errors.length) throw new Error(errors.join('; '));
    charts.set(key, chart);
  }
  const chart = charts.get(key);
  return { ...chart, notes: chart.notes.map(note => ({ ...note })) };
}

/** Returns human-readable errors; useful for an eventual chart editor/importer. */
export function validateChart(chart) {
  const errors = [], lanes = [-Infinity, -Infinity, -Infinity, -Infinity], ids = new Set();
  if (!Number.isSafeInteger(chart.durationUs) || chart.durationUs <= 0) errors.push('Invalid chart duration');
  let previousTime = -Infinity;
  for (const note of chart.notes ?? []) {
    if (ids.has(note.id)) errors.push(`Duplicate note id: ${note.id}`);
    ids.add(note.id);
    if (!Number.isInteger(note.lane) || note.lane < 0 || note.lane > 3) { errors.push(`Invalid lane: ${note.id}`); continue; }
    if (!Number.isSafeInteger(note.timeUs) || note.timeUs < 0 || note.timeUs > chart.durationUs) errors.push(`Invalid note time: ${note.id}`);
    if (note.timeUs < previousTime) errors.push(`Unsorted chart: ${note.id}`);
    if (note.timeUs <= lanes[note.lane]) errors.push(`Overlapping lane: ${note.id}`);
    if (note.endUs !== undefined && (!Number.isSafeInteger(note.endUs) || note.endUs <= note.timeUs || note.endUs > chart.durationUs)) errors.push(`Invalid hold end: ${note.id}`);
    previousTime = note.timeUs;
    lanes[note.lane] = note.endUs ?? note.timeUs;
  }
  return errors;
}

const drillDefinitions = [
  ['steady', '첫 번째 박자', '일정한 4분 박을 편안하게 따라갑니다.', '단타', 0, 1],
  ['alternate', '왼쪽, 오른쪽', '두 손을 번갈아 쓰며 리듬을 이어갑니다.', '교대', 0, 1],
  ['four-lanes', '네 개의 길', '네 레인의 위치를 천천히 익힙니다.', '교대', 0, .5],
  ['offbeat', '박자 사이의 여백', '정박과 엇박을 구분해 들어봅니다.', '엇박', 1, .5],
  ['double', '함께 누르기', '두 키를 같은 순간에 누르는 연습입니다.', '동시치기', 1, 1],
  ['chord-switch', '다른 모양, 같은 박', '동시치기 조합을 바꾸며 일정함을 유지합니다.', '동시치기', 2, 1],
  ['hold', '길게 이어지는 선', '처음 누른 키를 끝까지 유지합니다.', '홀드', 0, 4],
  ['hold-plus', '한 손은 그대로', '한 레인을 유지하며 다른 레인을 연주합니다.', '홀드 복합', 2, .5],
  ['jack', '작은 연타', '같은 키를 누르고 떼는 동작을 고르게 반복합니다.', '연타', 1, .5],
  ['stream', '흐르는 리듬', '8분 박을 따라 네 레인을 연결합니다.', '교대', 3, .5],
  ['burst', '짧은 가속', '짧은 16분 연타 뒤에 충분히 호흡합니다.', '연타', 2, .25],
  ['mixed', '나만의 흐름', '탭, 동시치기, 홀드를 함께 연습합니다.', '복합', 4, .5],
];

export function getDrills() {
  return drillDefinitions.map(([id, title, description, focus, songIndex, spacing], drillIndex) => {
    const song = SONGS[songIndex], notes = [], occupied = [-1, -1, -1, -1];
    const firstBeat = 8, lastBeat = 24;
    for (let beat = firstBeat, index = 0; beat < lastBeat; beat += spacing, index++) {
      const relative = beat - firstBeat;
      if (id === 'burst' && relative % 4 >= 1) continue;
      if (id === 'offbeat' && relative % 2 !== 0 && relative % 2 !== 1.5) continue;
      let lane = id === 'steady' ? 1 : id === 'alternate' ? index % 2 ? 2 : 1 : id === 'jack' || id === 'burst' ? Math.floor(relative / 4) % 4 : [0, 2, 1, 3][index % 4];
      if (id === 'hold') {
        addNote(notes, occupied, song, beat, lane, focus, beat + 3);
      } else if (id === 'hold-plus') {
        if (relative % 4 === 0) addNote(notes, occupied, song, beat, 0, '홀드', beat + 3);
        addNote(notes, occupied, song, beat, [1, 3, 2][index % 3], focus);
      } else if (id === 'double' || id === 'chord-switch') {
        lane = id === 'double' ? 0 : index % 2;
        addNote(notes, occupied, song, beat, lane, focus);
        addNote(notes, occupied, song, beat, lane + 2, focus);
      } else if (id === 'mixed') {
        const isHold = relative % 8 === 0;
        addNote(notes, occupied, song, beat, lane, isHold ? '홀드' : focus, isHold ? beat + 2 : null);
        if (relative % 4 === 2) addNote(notes, occupied, song, beat, (lane + 2) % 4, '동시치기');
      } else {
        addNote(notes, occupied, song, beat, lane, focus);
      }
    }
    notes.sort((a, b) => a.timeUs - b.timeUs || a.lane - b.lane);
    const fromUs = beatToUs(song, 4), toUs = beatToUs(song, 28);
    return {
      id: `drill-${id}-v1`, songId: song.id, difficulty: 'drill', version: '1', title, description, focus,
      bpm: song.bpm, durationUs: Math.round(song.durationSec * 1_000_000), fromUs, toUs,
      durationSec: (toUs - fromUs) / 1_000_000, level: drillIndex < 4 ? '기초' : drillIndex < 8 ? '응용' : '도전',
      notes: notes.map((note, index) => ({ ...note, id: `drill-${id}-${index}` })),
    };
  });
}
