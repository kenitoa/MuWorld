# MuWorld module contracts

All source modules are ESM .mjs. Offline Electron renderer, no backend. Times for judgment are integer microseconds of ORIGINAL song timeline. At rate r, convert song delta to real delta by dividing by r before offset/judgment. UI is Korean, music titles English. No remote assets.

## content.mjs (audio agent)
Exports SONGS (5 songs) and DIFFICULTIES, getChart(songId,difficulty='normal'), getDrills() (12 charts).
Song: {id,title,subtitle,artist,bpm,durationSec,color,accent,genre,tags:string[],description,level:{easy,normal,hard}, art:0..4}.
Chart: {id,songId,difficulty,version:'1',durationUs,notes:[{id,lane:0..3,timeUs,endUs?:number,pattern:string}],bpm}. Notes sorted, no same-lane overlapping holds.
AudioSystem class: async init(); async prepare(songId,rate=1); async start({songId,rate=1,fromUs=0,toUs=null,leadInSec=2,metronome=false}); stop(); pause(); async resume(); songTimeUs(performanceTimeMs=performance.now()); getInfo(); setVolume(number0to1); hit(lane); preview(songId); destroy(). start resolves with start scheduled, songTimeUs negative during lead-in. rate changes synthesized tempo WITHOUT pitch changes. prepare caches; composition and charts share beat grid. Song audio constructed locally with deterministic synthesis, musical arrangement >=60sec per song. Use getOutputTimestamp mapping, fallback output latency once. pause/resume clock continuity. getInfo {sampleRate,baseLatencyMs,outputLatencyMs,clockSource,state}. AudioSystem dispatch not required; root polls song time.

## judge.mjs (judge agent)
Exports RULES, RhythmEngine.
new RhythmEngine(chart,{inputOffsetUs=0,rate=1,windowScale=1,fromUs=0,toUs=chart.durationUs});
engine.input(lane,pressed,timeUs) (integer ORIGINAL song time, convert delta/rate then offset). input returns newly produced judgments array.
engine.advance(timeUs) returns judgments; engine.finish() returns summary; engine.snapshot() returns summary without finishing.
engine.notes is cloned chart notes with state:'pending'|'hit'|'miss', holdState:'pending'|'holding'|'complete'|'broken' optional.
engine.judgments array of {noteId,lane,timeUs,targetUs,deltaUs:number|null,grade:'PERFECT'|'GREAT'|'GOOD'|'BAD'|'MISS',kind:'head'|'hold',pattern}.
engine.inputs array {lane,pressed,timeUs}; engine.combo/maxCombo; snapshot {accuracy:0..100,score:0..1000000,combo,maxCombo,counts:{PERFECT,GREAT,GOOD,BAD,MISS},totalUnits,judgedUnits,extraInputs,judgments,inputs}.
Heads windows abs <=20000/45000/80000/120000 real us. Holds: head and sustain unit. Sustain fails for release strictly before end (corrected for inputOffset), never recovers, head miss fails tail. Tail completion succeeds if held at end. Negative offset requires adjusted deadlines. No FPS dependence, deterministic head candidate, repeat downs ignored, no floating boundary epsilon. Exclude practice interval notes crossing boundaries rather than invent notes. Tests required.

## analytics.mjs and storage.mjs (analytics agent)
analytics exports analyzeRun(summary,chart), compareRuns(records), toCSV(record).
analyzeRun returns {timing:{count,meanMs,medianMs,maeMs,stdMs,madMs,p95Ms,early,late,exact},lanes:[{lane,count,misses,missRate,meanMs,maeMs}],patterns:[{pattern,count,misses,missRate,meanMs}],segments:[{startUs,endUs,count,misses,accuracy}],histogram:[{fromMs,toMs,count}],chords:{count,medianSpreadMs},holds:{total,broken,successRate},recommendations:[{title,body,fromUs,toUs}],...}. Handle empty safely, MISS and sustain excluded from timing. No fabricated performance.
storage exports DEFAULT_SETTINGS, loadSettings(), saveSettings(partial), loadRecords(), saveRecord(record), deleteRecord(id), exportBackup(), importBackup(text), clearRecords(). LocalStorage with validation/version/backups, fallback robust and error handling. DEFAULT_SETTINGS: {keys:['KeyD','KeyF','KeyJ','KeyK'],inputOffsetUs:0,visualOffsetUs:0,approachMs:1200,volume:0.7,effectVolume:0.3,laneWidth:100,noteBrightness:90,backgroundBrightness:100,effects:25,showTiming:false,showCombo:true,highContrast:false,uiScale:100,preset:'comfort',metronome:false,practiceRate:0.8,practiceFromSec:0,practiceToSec:20,autoStep:false,windowScale:1}.
Record supplied root: {id,createdAt,songId,chartId,chartVersion,difficulty,mode:'standard'|'practice'|'echo',rate,inputOffsetUs,settings,summary,analysis,environment,completed,interrupted}.

Agent ownership: judge agent only src/core/judge.mjs & tests/judge.test.mjs; audio agent only src/core/content.mjs & src/core/audio.mjs & tests/content.test.mjs; analytics agent only src/core/analytics.mjs & src/core/storage.mjs & tests/analytics.test.mjs & tests/storage.test.mjs. Root owns all other files including UI/build/docs. Coordinate API changes before writing.
