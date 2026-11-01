import test from 'node:test';
import assert from 'node:assert/strict';
import { summarizeCalibration, performanceEventTime, calibrationAudioTime, pickCalibrationBeat } from '../src/ui/calibration.mjs';

test('calibration recommends the raw median with a positive late-correction sign', () => {
  const result = summarizeCalibration(Array.from({ length: 32 }, (_, index) => 5_000 + (index % 3 - 1) * 1_000));
  assert.equal(result.recommendedUs, 5_000);
  assert.equal(result.canApply, true);
  assert.equal(result.reliable, true);
});
test('at least 24 valid taps are required and wide scatter reduces confidence', () => {
  assert.equal(summarizeCalibration(Array(23).fill(5_000)).canApply, false);
  const wide = summarizeCalibration(Array.from({ length: 32 }, (_, index) => index % 2 ? 110_000 : -110_000));
  assert.equal(wide.canApply, true);
  assert.equal(wide.reliable, false);
  assert.equal(wide.madUs, 110_000);
  assert.equal(wide.recommendedUs, 0);
  assert.equal(summarizeCalibration([]).canApply, false);
});
test('input event timestamps preserve the original instant, including epoch-style timestamps', () => {
  assert.equal(performanceEventTime(1234, 1_700_000_000_000, 9000), 1234);
  assert.equal(performanceEventTime(1_700_000_001_234, 1_700_000_000_000, 9000), 1234);
  assert.equal(performanceEventTime(0, 1000, 9000), 0);
});
test('output timestamp mapping includes latency once and keeps a delayed input historical', () => {
  const context = { getOutputTimestamp: () => ({ contextTime: 10, performanceTime: 1000 }), currentTime: 20, baseLatency: .1, outputLatency: .2 };
  assert.equal(calibrationAudioTime(context, 900, 2000).time, 9.9);
  assert.ok(Math.abs(calibrationAudioTime({ currentTime: 10, baseLatency: .01, outputLatency: .02 }, 900, 1000).time - 9.87) < 1e-12);
});
test('each sound cue accepts one tap and off-beat taps cannot shift to an adjacent cue', () => {
  const used = new Set();
  assert.equal(pickCalibrationBeat([1, 1.5, 2], 1.01, used), 0);
  used.add(0);
  assert.equal(pickCalibrationBeat([1, 1.5, 2], 1.02, used), -1);
  assert.equal(pickCalibrationBeat([1, 1.5, 2], 1.25, used), -1);
  assert.equal(pickCalibrationBeat([1, 1.5, 2], 1.52, used), 1);
});
