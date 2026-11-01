import { getSong } from './content.mjs';

const clamp = (value, min, max) => Math.min(max, Math.max(min, value));
const midi = note => 440 * 2 ** ((note - 69) / 12);
const now = () => globalThis.performance.now();

function seededRandom(seed) {
  let state = seed >>> 0;
  return () => {
    state = (Math.imul(state, 1664525) + 1013904223) >>> 0;
    return state / 4294967296;
  };
}

/** A new tempo is rendered from the score: pitch is preserved without time-stretch artifacts. */
export async function renderSong(songId, rate = 1, sampleRate = 48_000) {
  const song = getSong(songId);
  if (!Number.isFinite(rate) || rate < .5 || rate > 1.5) throw new RangeError('Playback rate must be between 0.5 and 1.5');
  const OfflineContext = globalThis.OfflineAudioContext ?? globalThis.webkitOfflineAudioContext;
  if (!OfflineContext) throw new Error('이 환경에서는 오디오 합성을 지원하지 않습니다. MuWorld 데스크톱 앱을 사용해 주세요.');
  const duration = song.durationSec / rate;
  const context = new OfflineContext(2, Math.ceil(duration * sampleRate), sampleRate);
  // Direct PCM voices avoid thousands of scheduled nodes and keep loading bounded.
  const synthRate = 24_000, length = Math.ceil(duration * synthRate);
  const pcm = context.createBuffer(2, length, synthRate);
  const left = pcm.getChannelData(0), right = pcm.getChannelData(1);
  const beat = 60 / song.bpm / rate, totalBeats = song.bars * 4;
  const random = seededRandom(9317 + song.art * 77);

  const addTone = (frequency, at, span, level, pan = 0, pad = false, harmonic = 0) => {
    const start = Math.round(at * synthRate), count = Math.min(Math.round(span * synthRate), length - start);
    if (count <= 0) return;
    const omega = 2 * Math.PI * frequency / synthRate, coefficient = 2 * Math.cos(omega);
    const harmonicCoefficient = 2 * Math.cos(omega * 2);
    let previous = 0, current = Math.sin(omega), previousH = 0, currentH = Math.sin(omega * 2);
    const gainL = level * Math.sqrt((1 - pan) / 2), gainR = level * Math.sqrt((1 + pan) / 2);
    const attack = Math.min(count * .2, synthRate * (pad ? .2 : .006));
    const releaseStart = count * .65, decay = Math.exp(-6 / count);
    let envelope = 1;
    for (let i = 0; i < count; i++) {
      const next = coefficient * current - previous; previous = current; current = next;
      const nextH = harmonicCoefficient * currentH - previousH; previousH = currentH; currentH = nextH;
      const shape = i < attack ? i / attack : pad ? i > releaseStart ? (count - i) / (count - releaseStart) : 1 : envelope;
      const value = (current + currentH * harmonic) * shape;
      left[start + i] += value * gainL; right[start + i] += value * gainR;
      envelope *= decay;
    }
  };
  const addKick = (at, level) => {
    const start = Math.round(at * synthRate), count = Math.min(Math.round(.32 * synthRate), length - start);
    let frequencyDelta = 90, amplitude = level, phase = 0;
    const pitchDecay = Math.exp(-25 / synthRate), gainDecay = Math.exp(-19 / synthRate);
    for (let i = 0; i < count; i++) {
      phase += 2 * Math.PI * (45 + frequencyDelta) / synthRate;
      const value = Math.sin(phase) * amplitude * Math.min(1, i / 50);
      left[start + i] += value; right[start + i] += value;
      frequencyDelta *= pitchDecay; amplitude *= gainDecay;
    }
  };
  const addNoise = (at, span, level, snare = false, pan = 0) => {
    const start = Math.round(at * synthRate), count = Math.min(Math.round(span * synthRate), length - start);
    const decay = Math.exp(-6 / Math.max(1, count));
    let previous = 0, filtered = 0, amplitude = level;
    for (let i = 0; i < count; i++) {
      const white = random() * 2 - 1;
      const high = white - previous; previous = white;
      filtered = filtered * .65 + white * .35;
      const value = (snare ? filtered : high * .5) * amplitude * Math.min(1, i / 12);
      left[start + i] += value * (1 - pan); right[start + i] += value * (1 + pan);
      amplitude *= decay;
    }
    if (snare) addTone(178, at, .09, level * .3);
  };

  for (let b = 0; b < totalBeats; b += 8) {
    const chord = song.chords[Math.floor(b / 8) % song.chords.length];
    chord.forEach((note, voice) => addTone(midi(note), b * beat, 8.5 * beat, .018, (voice - 1.5) * .25, true, .07));
  }
  for (let b = 0; b < totalBeats; b += .5) {
    const chord = song.chords[Math.floor(b / 8) % song.chords.length];
    const step = Math.round(b * 2), bar = Math.floor(b / 4), local = b % 4;
    const intro = b < 8, outro = b >= totalBeats - 4, breakdown = bar % 16 === 12 || bar % 16 === 13;
    const phraseLevel = intro ? .65 : outro ? .6 : breakdown ? .75 : 1;
    const octave = step % 16 === 11 || song.art === 4 && step % 8 === 6 ? 24 : 12;
    addTone(midi(chord[song.melody[step % 8]] + octave), b * beat, beat * 1.7, .085 * phraseLevel, Math.sin(step * .61) * .3, false, song.art === 0 || song.art === 4 ? .22 : .12);
    if (b % 2 === 0) addTone(midi(chord[0] - 12), b * beat, beat * 1.8, .14 * phraseLevel, 0, false, .14);
    if (intro) continue;
    const isKick = song.groove === 'velvet' ? b % 1 === 0 : song.groove === 'dawn' ? local === 0 || local === 1.5 || local === 2.5 : local === 0 || local === 2;
    if (isKick) addKick(b * beat, (breakdown ? .17 : .28) * (outro ? .7 : 1));
    if (local === 1 || local === 3) addNoise(b * beat, .16, breakdown ? .1 : .17, true, -.05);
    addNoise(b * beat, local % 1 === .5 ? .075 : .045, local % 1 === .5 ? .024 : .016, false, .2);
    addNoise((b + .25) * beat, .025, .01, false, -.25);
  }
  for (const note of song.chords[0]) addTone(midi(note + 12), totalBeats * beat, 3.8 * beat, .022, 0, true);
  // A quiet cross-channel delay provides space without a complex audio-node graph.
  const delaySamples = Math.round(beat * .75 * synthRate);
  for (let i = delaySamples; i < length; i++) {
    const echoL = left[i - delaySamples] * .1, echoR = right[i - delaySamples] * .1;
    left[i] += echoR; right[i] += echoL;
  }
  const fadeIn = Math.round(synthRate * .015), fadeOut = Math.round(synthRate * .8);
  let peak = 0;
  for (let i = 0; i < length; i++) peak = Math.max(peak, Math.abs(left[i]), Math.abs(right[i]));
  const normalization = Math.min(2.1, .84 / Math.max(.001, peak));
  for (let i = 0; i < length; i++) {
    const fade = Math.min(1, i / fadeIn, (length - 1 - i) / fadeOut) * normalization;
    left[i] *= fade; right[i] *= fade;
  }
  const source = context.createBufferSource(); source.buffer = pcm; source.connect(context.destination); source.start();
  return context.startRendering();
}

/** Pure mapping helper: outputTimestamp already contains output latency. Never subtract it twice. */
export function mapOutputTimestamp(timestamp, performanceTimeMs) {
  return timestamp.contextTime + (performanceTimeMs - timestamp.performanceTime) / 1000;
}

export class AudioSystem {
  constructor() {
    this.context = null;
    this.musicGain = null;
    this.effectGain = null;
    this.volume = .7;
    this.effectVolume = .3;
    this._cache = new Map();
    this._pending = new Map();
    this._generation = 0;
    this._source = null;
    this._clicks = [];
    this._hitBuffers = [];
    this._state = 'idle';
    this._clockSource = 'uninitialized';
    this._pausedUs = 0;
    this._config = null;
    this._ctxStart = 0;
    this._fromUs = 0;
    this._toUs = 0;
    this._rate = 1;
    this._lastClock = null;
  }

  async init() {
    if (!this.context) {
      const Context = globalThis.AudioContext ?? globalThis.webkitAudioContext;
      if (!Context) throw new Error('오디오 장치를 사용할 수 없습니다.');
      this.context = new Context({ latencyHint: 'interactive', sampleRate: 48_000 });
      this.musicGain = this.context.createGain();
      this.effectGain = this.context.createGain();
      this.musicGain.gain.value = this.volume;
      this.effectGain.gain.value = this.effectVolume;
      this.musicGain.connect(this.context.destination);
      this.effectGain.connect(this.context.destination);
      this._hitBuffers = Array.from({ length: 4 }, (_, lane) => {
        const length = Math.round(this.context.sampleRate * .045);
        const buffer = this.context.createBuffer(1, length, this.context.sampleRate), data = buffer.getChannelData(0);
        const frequency = [520, 620, 720, 820][lane];
        for (let i = 0; i < length; i++) {
          const t = i / this.context.sampleRate;
          data[i] = Math.sin(2 * Math.PI * frequency * t) * Math.exp(-t * 110) * Math.min(1, t * 2000) * .16;
        }
        return buffer;
      });
    }
    if (this.context.state === 'suspended') await this.context.resume();
    return this.getInfo();
  }

  async prepare(songId, rate = 1) {
    getSong(songId);
    if (!Number.isFinite(rate) || rate < .5 || rate > 1.5) throw new RangeError('Invalid playback rate');
    const key = `${songId}:${rate.toFixed(4)}`;
    if (this._cache.has(key)) {
      const buffer = this._cache.get(key);
      this._cache.delete(key); this._cache.set(key, buffer);
      return buffer;
    }
    if (this._pending.has(key)) return this._pending.get(key);
    const promise = renderSong(songId, rate).then(buffer => {
      this._cache.set(key, buffer);
      while (this._cache.size > 3) this._cache.delete(this._cache.keys().next().value);
      return buffer;
    }).finally(() => this._pending.delete(key));
    this._pending.set(key, promise);
    return promise;
  }

  async start({ songId, rate = 1, fromUs = 0, toUs = null, leadInSec = 2, metronome = false }) {
    const generation = ++this._generation;
    this._stopNodes();
    this._state = 'preparing';
    const song = getSong(songId);
    const durationUs = Math.round(song.durationSec * 1_000_000);
    fromUs = clamp(Math.round(fromUs), 0, durationUs - 1);
    toUs = toUs == null ? durationUs : clamp(Math.round(toUs), fromUs + 1, durationUs);
    leadInSec = clamp(Number(leadInSec) || 0, 0, 8);
    try {
      await this.init();
      const buffer = await this.prepare(songId, rate);
      if (generation !== this._generation) return null;
      if (this.context.state === 'suspended') await this.context.resume();
      if (generation !== this._generation) return null;
      this._config = { songId, rate, fromUs, toUs, leadInSec, metronome };
      this._fromUs = fromUs; this._toUs = toUs; this._rate = rate;
      this._pausedUs = fromUs;
      this._lastClock = null;
      this._ctxStart = this.context.currentTime + leadInSec + .06;
      const source = this.context.createBufferSource();
      source.buffer = buffer;
      source.connect(this.musicGain);
      source.start(this._ctxStart, fromUs / 1_000_000 / rate, (toUs - fromUs) / 1_000_000 / rate);
      this._source = source;
      this._state = 'running';
      this._scheduleClicks(song, metronome, leadInSec);
      return { startContextTime: this._ctxStart, fromUs, toUs, rate };
    } catch (error) {
      if (generation === this._generation) this._state = 'error';
      throw error;
    }
  }

  _scheduleClicks(song, metronome, leadInSec) {
    const beatSec = 60 / song.bpm / this._rate;
    const count = Math.floor(leadInSec / beatSec);
    for (let index = count; index > 0; index--) this._click(this._ctxStart - index * beatSec, index === count);
    if (metronome) {
      const firstBeat = Math.ceil(this._fromUs / (60_000_000 / song.bpm));
      const lastBeat = Math.floor(this._toUs / (60_000_000 / song.bpm));
      for (let beat = firstBeat; beat <= lastBeat; beat++) {
        const songUs = beat * 60_000_000 / song.bpm;
        this._click(this._ctxStart + (songUs - this._fromUs) / 1_000_000 / this._rate, beat % 4 === 0);
      }
    }
  }

  _click(at, accent) {
    if (at < this.context.currentTime) return;
    const osc = this.context.createOscillator(), envelope = this.context.createGain();
    osc.type = 'sine'; osc.frequency.value = accent ? 1050 : 800;
    envelope.gain.setValueAtTime(.17, at);
    envelope.gain.exponentialRampToValueAtTime(.00001, at + .035);
    osc.connect(envelope).connect(this.effectGain);
    osc.start(at); osc.stop(at + .04);
    this._clicks.push(osc);
  }

  _outputTime(performanceTimeMs) {
    if (!this.context) return 0;
    if (typeof this.context.getOutputTimestamp === 'function') {
      const timestamp = this.context.getOutputTimestamp();
      if (timestamp.contextTime > 0 && timestamp.performanceTime > 0 && Number.isFinite(timestamp.contextTime) && Number.isFinite(timestamp.performanceTime)) {
        this._clockSource = 'output-timestamp';
        return mapOutputTimestamp(timestamp, performanceTimeMs);
      }
    }
    this._clockSource = 'latency-estimate';
    const latency = (this.context.baseLatency || 0) + (this.context.outputLatency || 0);
    return this.context.currentTime - latency + (performanceTimeMs - now()) / 1000;
  }

  songTimeUs(performanceTimeMs = now()) {
    if (this._state === 'paused') return this._pausedUs;
    if (this._state !== 'running') return this._pausedUs;
    const outputTime = this._outputTime(performanceTimeMs);
    const songUs = Math.round(this._fromUs + (outputTime - this._ctxStart) * this._rate * 1_000_000);
    // Judgment needs the real trailing time after playback ends; only UI progress is clamped.
    return songUs;
  }

  pause() {
    if (this._state !== 'running') return this._pausedUs;
    // Preserve the trailing judgment window. Only a negative lead-in restarts at its beginning.
    this._pausedUs = Math.max(this._fromUs, this.songTimeUs());
    this._generation++;
    this._stopNodes();
    this._state = 'paused';
    return this._pausedUs;
  }

  async resume() {
    if (this._state !== 'paused' || !this._config) return null;
    if (this._pausedUs >= this._toUs) {
      // The source has ended, but the judge still needs time to expire late-hit windows.
      // A silent clock avoids replaying the source or freezing the game at the endpoint.
      const generation = ++this._generation;
      await this.init();
      if (generation !== this._generation) return null;
      this._fromUs = this._pausedUs;
      this._ctxStart = this._outputTime(now());
      this._state = 'running';
      return { postRoll: true, fromUs: this._fromUs, toUs: this._toUs, rate: this._rate };
    }
    return this.start({ ...this._config, fromUs: this._pausedUs, leadInSec: 1.5 });
  }

  _stopNodes() {
    if (this._source) { try { this._source.stop(); this._source.disconnect(); } catch {} this._source = null; }
    for (const click of this._clicks) { try { click.stop(); click.disconnect(); } catch {} }
    this._clicks = [];
  }

  stop() {
    this._generation++;
    this._stopNodes();
    this._state = 'idle';
    this._pausedUs = 0;
  }

  setVolume(value) {
    this.volume = clamp(Number(value) || 0, 0, 1);
    if (this.musicGain) this.musicGain.gain.setTargetAtTime(this.volume, this.context.currentTime, .015);
  }

  setEffectVolume(value) {
    this.effectVolume = clamp(Number(value) || 0, 0, 1);
    if (this.effectGain) this.effectGain.gain.setTargetAtTime(this.effectVolume, this.context.currentTime, .015);
  }

  hit(lane) {
    if (!this.context || this.context.state !== 'running') return;
    const buffer = this._hitBuffers[clamp(Math.round(lane), 0, 3)];
    if (!buffer) return;
    const source = this.context.createBufferSource();
    source.buffer = buffer; source.connect(this.effectGain); source.start();
  }

  async preview(songId) {
    const song = getSong(songId), fromUs = Math.round(16 * 60_000_000 / song.bpm);
    return this.start({ songId, rate: 1, fromUs, toUs: Math.min(Math.round(song.durationSec * 1_000_000), fromUs + 12_000_000), leadInSec: .05 });
  }

  getInfo() {
    return {
      sampleRate: this.context?.sampleRate ?? 0,
      baseLatencyMs: this.context ? (this.context.baseLatency || 0) * 1000 : null,
      outputLatencyMs: this.context && typeof this.context.outputLatency === 'number' ? this.context.outputLatency * 1000 : null,
      clockSource: this._clockSource,
      state: this._state,
      contextState: this.context?.state ?? 'uninitialized',
      estimated: true,
      rate: this._rate,
    };
  }

  async destroy() {
    this.stop();
    this._cache.clear(); this._pending.clear();
    if (this.context && this.context.state !== 'closed') await this.context.close();
    this.context = null;
  }
}
