/** Deterministic, offline rhythm judgment. All public song times are integer µs. */
export const RULES = Object.freeze({
  windows: Object.freeze({ PERFECT: 20_000, GREAT: 45_000, GOOD: 80_000, BAD: 120_000 }),
  weights: Object.freeze({ PERFECT: 1, GREAT: 0.9, GOOD: 0.6, BAD: 0.2, MISS: 0 }),
  maxWindowUs: 120_000,
});

const GRADES = ['PERFECT', 'GREAT', 'GOOD', 'BAD', 'MISS'];
const POINTS = { PERFECT: 100, GREAT: 90, GOOD: 60, BAD: 20, MISS: 0 };
const integer = (value, name) => {
  if (!Number.isSafeInteger(value)) throw new TypeError(`${name} must be a safe integer in microseconds`);
  return value;
};
const positive = (value, name) => {
  if (!Number.isFinite(value) || value <= 0) throw new RangeError(`${name} must be positive and finite`);
  return value;
};

// Decimal rate/scale settings are compared as exact fractions, without an epsilon.
// This prevents e.g. 0.8x from changing a boundary through binary float rounding.
function fraction(value) {
  const [coefficient, exponentText = '0'] = String(value).toLowerCase().split('e');
  const [whole, fractional = ''] = coefficient.split('.');
  let numerator = BigInt(whole + fractional);
  let denominator = 10n ** BigInt(fractional.length);
  const exponent = Number(exponentText);
  if (exponent >= 0) numerator *= 10n ** BigInt(exponent);
  else denominator *= 10n ** BigInt(-exponent);
  return [numerator, denominator];
}

function floorDiv(a, b) {
  const q = a / b;
  return a < 0n && a % b !== 0n ? q - 1n : q;
}

function safeTime(value) {
  return integer(Number(value), 'derived deadline');
}

export class RhythmEngine {
  constructor(chart, { inputOffsetUs = 0, rate = 1, windowScale = 1, fromUs = 0, toUs = chart.durationUs } = {}) {
    if (!chart || !Array.isArray(chart.notes)) throw new TypeError('A chart with notes is required');
    integer(chart.durationUs, 'chart.durationUs');
    this.inputOffsetUs = integer(inputOffsetUs, 'inputOffsetUs');
    this.rate = positive(rate, 'rate');
    this.windowScale = positive(windowScale, 'windowScale');
    this.fromUs = integer(fromUs, 'fromUs');
    this.toUs = integer(toUs, 'toUs');
    if (fromUs < 0 || toUs < fromUs || toUs > chart.durationUs) throw new RangeError('Invalid practice interval');
    [this._rateNum, this._rateDen] = fraction(rate);
    [this._scaleNum, this._scaleDen] = fraction(windowScale);
    this._offset = BigInt(inputOffsetUs);
    const ids = new Set();
    this._baseNotes = chart.notes.map((note) => {
      integer(note.timeUs, 'note.timeUs');
      if (!Number.isInteger(note.lane) || note.lane < 0 || note.lane > 3) throw new RangeError('Invalid note lane');
      if (note.id === undefined || ids.has(note.id)) throw new TypeError('Note IDs must be unique');
      ids.add(note.id);
      if (note.endUs !== undefined) {
        integer(note.endUs, 'note.endUs');
        if (note.endUs <= note.timeUs) throw new RangeError('Hold end must follow its head');
      }
      return { ...note };
    }).filter((note) => note.timeUs >= fromUs && note.timeUs < toUs && (note.endUs === undefined || note.endUs <= toUs))
      .sort((a, b) => a.timeUs - b.timeUs || a.lane - b.lane);
    this._noteOrder = new Map(this._baseNotes.map((note, index) => [note.id, index]));
    this.totalUnits = this._baseNotes.reduce((sum, note) => sum + (note.endUs === undefined ? 1 : 2), 0);
    this._events = [];
    this._serial = 0;
    this._cursorUs = -Infinity;
    this._finished = false;
    this._resetState();
  }

  _resetState() {
    this.notes = this._baseNotes.map((note) => ({ ...note, state: 'pending', ...(note.endUs === undefined ? {} : { holdState: 'pending' }) }));
    this.judgments = [];
    this.inputs = [];
    this.combo = 0;
    this.maxCombo = 0;
    this.extraInputs = 0;
    this._pressed = [false, false, false, false];
    this._activeHolds = [null, null, null, null];
    this._unitKeys = new Set();
    this._actions = [];
    for (let index = 0; index < this.notes.length; index++) {
      const note = this.notes[index];
      const numerator = BigInt(note.timeUs) * this._rateDen * this._scaleDen
        + this._offset * this._rateNum * this._scaleDen
        + BigInt(RULES.maxWindowUs) * this._rateNum * this._scaleNum;
      // A head at exactly the late BAD boundary is valid; miss begins strictly after it.
      const missUs = safeTime(floorDiv(numerator, this._rateDen * this._scaleDen) + 1n);
      this._actions.push({ timeUs: missUs, index, kind: 'miss' });
      if (note.endUs !== undefined) {
        const tailNum = BigInt(note.endUs) * this._rateDen + this._offset * this._rateNum;
        const tailUs = safeTime(-floorDiv(-tailNum, this._rateDen));
        this._actions.push({ timeUs: tailUs, index, kind: 'tail' });
      }
    }
    this._actions.sort((a, b) => a.timeUs - b.timeUs || a.index - b.index || (a.kind === 'miss' ? -1 : 1));
    this._actionCursor = 0;
  }

  _deltaNumerator(timeUs, targetUs) {
    return (BigInt(timeUs) - BigInt(targetUs)) * this._rateDen - this._offset * this._rateNum;
  }

  _grade(timeUs, targetUs) {
    const numerator = this._deltaNumerator(timeUs, targetUs);
    const distance = (numerator < 0n ? -numerator : numerator) * this._scaleDen;
    for (const grade of GRADES.slice(0, -1)) {
      if (distance <= BigInt(RULES.windows[grade]) * this._rateNum * this._scaleNum) return grade;
    }
    return null;
  }

  _key(noteId, kind) { return `${typeof noteId}:${String(noteId)}:${kind}`; }

  _judgmentCompare(a, b) {
    return a.timeUs - b.timeUs || this._noteOrder.get(a.noteId) - this._noteOrder.get(b.noteId)
      || (a.kind === b.kind ? 0 : a.kind === 'head' ? -1 : 1);
  }

  _emit(note, grade, kind, timeUs, deltaUs = null) {
    const key = this._key(note.id, kind);
    if (this._unitKeys.has(key)) return;
    this._unitKeys.add(key);
    const judgment = { noteId: note.id, lane: note.lane, timeUs, targetUs: kind === 'hold' ? note.endUs : note.timeUs, deltaUs, grade, kind, pattern: note.pattern ?? 'basic' };
    const previous = this.judgments.at(-1);
    this.judgments.push(judgment);
    if (previous && this._judgmentCompare(previous, judgment) > 0) {
      // A canonical tie order makes combo independent of render-frame boundaries.
      this.judgments.sort((a, b) => this._judgmentCompare(a, b));
      this.combo = 0;
      this.maxCombo = 0;
      for (const item of this.judgments) {
        this.combo = item.grade === 'MISS' ? 0 : this.combo + 1;
        this.maxCombo = Math.max(this.maxCombo, this.combo);
      }
    } else {
      this.combo = grade === 'MISS' ? 0 : this.combo + 1;
      this.maxCombo = Math.max(this.maxCombo, this.combo);
    }
  }

  _resolveHold(note, success, timeUs) {
    note.holdState = success ? 'complete' : 'broken';
    if (this._activeHolds[note.lane] === note) this._activeHolds[note.lane] = null;
    this._emit(note, success ? 'PERFECT' : 'MISS', 'hold', timeUs);
  }

  _runActions(timeUs, inclusive = true) {
    while (this._actionCursor < this._actions.length) {
      const action = this._actions[this._actionCursor];
      if (action.timeUs > timeUs || (!inclusive && action.timeUs === timeUs)) break;
      this._actionCursor++;
      const note = this.notes[action.index];
      if (action.kind === 'miss' && note.state === 'pending') {
        note.state = 'miss';
        this._emit(note, 'MISS', 'head', action.timeUs);
        if (note.endUs !== undefined) this._resolveHold(note, false, action.timeUs);
      } else if (action.kind === 'tail' && note.holdState === 'holding') {
        this._resolveHold(note, this._pressed[note.lane], action.timeUs);
      }
    }
  }

  _applyEvent(event) {
    const { lane, pressed, timeUs } = event;
    this._runActions(timeUs, false);
    this.inputs.push({ lane, pressed, timeUs });
    if (this._pressed[lane] === pressed) {
      this._runActions(timeUs);
      return; // Includes every repeat keydown, even if the original down missed.
    }
    this._pressed[lane] = pressed;
    if (!pressed) {
      const hold = this._activeHolds[lane];
      if (hold) this._resolveHold(hold, this._deltaNumerator(timeUs, hold.endUs) >= 0n, timeUs);
    } else {
      const note = this.notes.find((item) => item.lane === lane && item.state === 'pending');
      const grade = note ? this._grade(timeUs, note.timeUs) : null;
      if (!grade) this.extraInputs++;
      else {
        note.state = 'hit';
        const deltaUs = (timeUs - note.timeUs) / this.rate - this.inputOffsetUs;
        this._emit(note, grade, 'head', timeUs, Object.is(deltaUs, -0) ? 0 : deltaUs);
        if (note.endUs !== undefined) {
          const endDelta = this._deltaNumerator(timeUs, note.endUs);
          if (endDelta > 0n) this._resolveHold(note, false, timeUs);
          else if (endDelta === 0n) this._resolveHold(note, true, timeUs);
          else {
            note.holdState = 'holding';
            this._activeHolds[lane] = note;
          }
        }
      }
    }
    this._runActions(timeUs);
  }

  _newJudgments(previous) {
    const old = new Map(previous.map((item) => [this._key(item.noteId, item.kind), item]));
    return this.judgments.filter((item) => {
      const prior = old.get(this._key(item.noteId, item.kind));
      return !prior || prior.grade !== item.grade || prior.timeUs !== item.timeUs || prior.deltaUs !== item.deltaUs;
    }).map((item) => ({ ...item }));
  }

  input(lane, pressed, timeUs) {
    if (!Number.isInteger(lane) || lane < 0 || lane > 3) throw new RangeError('lane must be 0, 1, 2 or 3');
    if (typeof pressed !== 'boolean') throw new TypeError('pressed must be boolean');
    integer(timeUs, 'timeUs');
    if (this._finished) return [];
    const previous = this.judgments.slice();
    const event = { lane, pressed, timeUs, serial: this._serial++ };
    this._events.push(event);
    if (timeUs < this._cursorUs) {
      // Only genuinely late delivery rebuilds state. Its original timestamp wins
      // over an already displayed provisional miss; the hit window never widens.
      const horizon = this._cursorUs;
      this._resetState();
      const ordered = this._events.slice().sort((a, b) => a.timeUs - b.timeUs || a.serial - b.serial);
      for (const item of ordered) this._applyEvent(item);
      this._runActions(horizon);
    } else {
      this._applyEvent(event);
      this._cursorUs = timeUs;
    }
    return this._newJudgments(previous);
  }

  advance(timeUs) {
    integer(timeUs, 'timeUs');
    if (this._finished || timeUs < this._cursorUs) return [];
    // Rendering calls this every frame; do not copy/sort the run's history when
    // no deadline is due. Input scoring is likewise independent of that FPS.
    if (!this._actions[this._actionCursor] || this._actions[this._actionCursor].timeUs > timeUs) {
      this._cursorUs = timeUs;
      return [];
    }
    const previous = this.judgments.slice();
    this._runActions(timeUs);
    this._cursorUs = timeUs;
    return this._newJudgments(previous);
  }

  snapshot() {
    const counts = Object.fromEntries(GRADES.map((grade) => [grade, 0]));
    let points = 0;
    for (const judgment of this.judgments) {
      counts[judgment.grade]++;
      points += POINTS[judgment.grade];
    }
    const judgedUnits = this.judgments.length;
    return {
      accuracy: judgedUnits ? points / judgedUnits : 0,
      score: this.totalUnits ? Math.round(points / (100 * this.totalUnits) * 1_000_000) : 0,
      combo: this.combo,
      maxCombo: this.maxCombo,
      counts,
      totalUnits: this.totalUnits,
      judgedUnits,
      extraInputs: this.extraInputs,
      judgments: this.judgments.map((item) => ({ ...item })),
      inputs: this.inputs.map((item) => ({ ...item })),
    };
  }

  finish() {
    if (!this._finished) {
      const lastActionUs = this._actions.at(-1)?.timeUs ?? this.toUs;
      this._runActions(Math.max(this.toUs, lastActionUs, Number.isFinite(this._cursorUs) ? this._cursorUs : this.toUs));
      this._finished = true;
    }
    return this.snapshot();
  }
}
