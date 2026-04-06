namespace RhythmGame;

/// <summary>
/// WAV 분석 결과로부터 BMS 채보 파일을 난이도별로 자동 생성하는 클래스.
/// Easy: 초당 1노트, Normal: 초당 6노트, Hard: 초당 9노트.
/// </summary>
internal static class ChartGenerator
{
    private const int LaneCount = 4;
    private const string ChartFolderName = "NoteLane";

    /// <summary>
    /// Songs/InGameBGM 폴더의 모든 WAV 파일을 분석하여 채보를 생성한다.
    /// 이미 생성된 채보가 있으면 건너뛴다.
    /// </summary>
    public static void GenerateAllCharts()
    {
        string bgmDir = Path.Combine(AppContext.BaseDirectory, "Songs", "InGameBGM", "Original");
        if (!Directory.Exists(bgmDir))
            return;

        string chartDir = Path.Combine(AppContext.BaseDirectory, ChartFolderName);
        Directory.CreateDirectory(chartDir);

        string[] wavFiles = Directory.GetFiles(bgmDir, "*.wav", SearchOption.TopDirectoryOnly);

        foreach (string wavPath in wavFiles)
        {
            string songName = Path.GetFileNameWithoutExtension(wavPath);

            for (int difficulty = 0; difficulty < 3; difficulty++)
            {
                string chartFile = Path.Combine(chartDir, GetChartFileName(songName, difficulty));

                if (File.Exists(chartFile))
                    continue;

                var beats = WavAnalyzer.Analyze(wavPath);
                if (beats.Count == 0)
                    continue;

                string bmsContent = GenerateBms(songName, beats, difficulty);
                File.WriteAllText(chartFile, bmsContent);
            }
        }
    }

    /// <summary>
    /// 특정 곡의 채보 파일 이름을 반환한다 (0=easy, 1=normal, 2=hard).
    /// </summary>
    public static string GetChartFileName(string songName, int difficultyIndex)
    {
        string prefix = difficultyIndex switch { 0 => "easy", 1 => "normal", _ => "hard" };
        string safeName = NormalizeSongFileName(songName);
        return $"{prefix}_{safeName}.bms";
    }

    /// <summary>
    /// 난이도별 초당 노트 수를 반환한다.
    /// </summary>
    private static float GetNotesPerSecond(int difficulty)
    {
        return difficulty switch
        {
            0 => 1f,    // Easy: 초당 1노트
            1 => 6f,    // Normal: 초당 6노트
            _ => 9f,    // Hard: 초당 9노트
        };
    }

    /// <summary>
    /// 곡 길이와 난이도를 바탕으로 목표 노트 수를 계산한다.
    /// </summary>
    private static int GetTargetNoteCount(int difficulty, float songDuration)
    {
        float nps = GetNotesPerSecond(difficulty);
        return Math.Max(10, (int)MathF.Round(nps * songDuration));
    }

    /// <summary>
    /// 난이도별 박자 세분화(subdivision) 수를 반환한다.
    /// </summary>
    private static int GetSubdivision(int difficulty)
    {
        return difficulty switch
        {
            0 => 8,   // 8분음표 (초당 3~4 지원)
            1 => 16,  // 16분음표 (초당 8~9 지원)
            _ => 24,  // 24분음표 (초당 12~13 지원)
        };
    }

    private static string GenerateBms(string songName, List<WavAnalyzer.BeatInfo> beats, int difficulty)
    {
        int subdivision = GetSubdivision(difficulty);

        // BPM 추정
        float estimatedBpm = EstimateBpm(beats);
        float secondsPerMeasure = 240f / estimatedBpm;

        // 곡 길이 추정 (마지막 비트 기준)
        float songDuration = beats.Count > 0 ? beats[^1].Time + 2f : 60f;
        int totalMeasures = Math.Max(4, (int)MathF.Ceiling(songDuration / secondsPerMeasure));
        int targetNotes = GetTargetNoteCount(difficulty, songDuration);

        // 1단계: 박자 그리드에 맞는 모든 가능한 위치 생성
        var gridPositions = new List<(int measure, float offset, float time, float energy)>();
        var beatLookup = new Dictionary<int, float>();

        foreach (var beat in beats)
        {
            int gridKey = (int)MathF.Round(beat.Time * subdivision * 2);
            beatLookup.TryAdd(gridKey, beat.Energy);
        }

        for (int m = 0; m < totalMeasures; m++)
        {
            for (int s = 0; s < subdivision; s++)
            {
                float offset = s / (float)subdivision;
                float time = (m + offset) * secondsPerMeasure;

                if (time > songDuration)
                    break;

                int gridKey = (int)MathF.Round(time * subdivision * 2);
                float energy = beatLookup.TryGetValue(gridKey, out float e) ? e : 0f;

                gridPositions.Add((m, offset, time, energy));
            }
        }

        if (gridPositions.Count == 0)
            return BuildBmsString(songName, estimatedBpm, []);

        // 2단계: 위치 선택 (에너지 우선 + 균등 분배)
        var rng = new Random(HashCode.Combine(songName.GetHashCode(), difficulty));
        var selectedPositions = SelectNotePositions(gridPositions, targetNotes, rng);

        // 3단계: 패턴 기반 레인 배정 (동시 노트 없이, 리듬게임 패턴 기술 활용)
        var notes = AssignLanesWithPatterns(selectedPositions, difficulty, estimatedBpm, secondsPerMeasure, rng);

        return BuildBmsString(songName, estimatedBpm, notes);
    }

    // ── 패턴 종류 ─────────────────────────────────────────────────────────────
    private enum ChartPattern
    {
        Stream,     // 순차 흐름: 0→1→2→3→2→1 (물 흐르듯 자연스럽게)
        Stair,      // 계단: 0→1→2→3 또는 3→2→1→0 (한 방향)
        Trill,      // 트릴: 두 레인 빠른 교차 (예: 1↔3, 0↔2)
        Jack,       // 잭: 같은 레인 연타
        Zigzag,     // 지그재그: 양 끝을 오가며 (0→3→1→2 등)
        Swing,      // 스윙: 인접 레인 왕복 (1→2→1→2)
        Spread,     // 스프레드: 중앙→외곽 또는 외곽→중앙
    }

    /// <summary>
    /// 난이도에 따른 패턴 가중치 테이블.
    /// Easy는 단순 패턴 위주, Hard는 다양한 패턴을 적극 활용.
    /// </summary>
    private static (ChartPattern pattern, float weight)[] GetPatternWeights(int difficulty)
    {
        return difficulty switch
        {
            0 => // Easy: 단순하고 예측 가능
            [
                (ChartPattern.Stream, 0.50f),
                (ChartPattern.Stair, 0.30f),
                (ChartPattern.Swing, 0.20f),
            ],
            1 => // Normal: 다양한 패턴 혼합
            [
                (ChartPattern.Stream, 0.25f),
                (ChartPattern.Stair, 0.20f),
                (ChartPattern.Trill, 0.15f),
                (ChartPattern.Jack, 0.10f),
                (ChartPattern.Zigzag, 0.15f),
                (ChartPattern.Swing, 0.10f),
                (ChartPattern.Spread, 0.05f),
            ],
            _ => // Hard: 어려운 패턴 적극 활용
            [
                (ChartPattern.Stream, 0.15f),
                (ChartPattern.Stair, 0.15f),
                (ChartPattern.Trill, 0.20f),
                (ChartPattern.Jack, 0.15f),
                (ChartPattern.Zigzag, 0.15f),
                (ChartPattern.Swing, 0.10f),
                (ChartPattern.Spread, 0.10f),
            ],
        };
    }

    /// <summary>
    /// 패턴 한 구간의 길이 (노트 수). 난이도가 높을수록 짧은 구간도 사용.
    /// </summary>
    private static int GetPatternLength(int difficulty, Random rng)
    {
        return difficulty switch
        {
            0 => rng.Next(6, 12),   // Easy: 긴 구간 (안정적)
            1 => rng.Next(4, 10),   // Normal: 중간 구간
            _ => rng.Next(3, 8),    // Hard: 짧은 구간 (빈번한 전환)
        };
    }

    private static ChartPattern PickPattern(Random rng, (ChartPattern pattern, float weight)[] weights)
    {
        float roll = rng.NextSingle();
        float cumulative = 0f;
        foreach (var (pattern, weight) in weights)
        {
            cumulative += weight;
            if (roll <= cumulative)
                return pattern;
        }
        return weights[^1].pattern;
    }

    /// <summary>
    /// 선택된 위치들에 리듬게임 패턴 기술을 적용하여 레인을 배정한다.
    /// 동시 노트(겹침)는 생성하지 않는다.
    /// </summary>
    private static List<(int measure, int lane, float offset)> AssignLanesWithPatterns(
        List<(int measure, float offset, float time, float energy)> positions,
        int difficulty, float bpm, float secondsPerMeasure, Random rng)
    {
        const int MaxSameLane = 2; // 같은 레인 최대 연속 허용 수
        var notes = new List<(int measure, int lane, float offset)>(positions.Count);
        var patternWeights = GetPatternWeights(difficulty);

        int i = 0;
        int prevLane = rng.Next(LaneCount);
        int sameLaneCount = 1;
        ChartPattern currentPattern = PickPattern(rng, patternWeights);
        int patternRemaining = GetPatternLength(difficulty, rng);
        int jackConsecutive = 0;

        // ── 레인 균등 분포 추적 ──
        int[] laneCounts = new int[LaneCount];
        const int BalanceCheckInterval = 8; // 8노트마다 균형 체크

        // 패턴별 상태
        int stairDir = rng.Next(2) == 0 ? 1 : -1;
        int trillLaneA, trillLaneB;
        (trillLaneA, trillLaneB) = PickBalancedPair(laneCounts, rng);
        bool trillFlip = false;
        int zigzagPhase = 0;
        int swingLaneA, swingLaneB;
        (swingLaneA, swingLaneB) = PickBalancedAdjacentPair(laneCounts, rng);
        bool swingFlip = false;
        bool spreadOutward = rng.Next(2) == 0;
        int spreadPhase = 0;

        while (i < positions.Count)
        {
            // 패턴 구간 소진 시 새 패턴 선택
            if (patternRemaining <= 0)
            {
                ChartPattern prevPattern = currentPattern;
                // 같은 패턴이 연속되지 않도록 (가능하면)
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    currentPattern = PickPattern(rng, patternWeights);
                    if (currentPattern != prevPattern || patternWeights.Length <= 2)
                        break;
                }
                patternRemaining = GetPatternLength(difficulty, rng);

                // 새 패턴 상태 초기화 (레인 균형을 고려하여 선택)
                stairDir = rng.Next(2) == 0 ? 1 : -1;
                (trillLaneA, trillLaneB) = PickBalancedPair(laneCounts, rng);
                trillFlip = false;
                zigzagPhase = 0;
                (swingLaneA, swingLaneB) = PickBalancedAdjacentPair(laneCounts, rng);
                swingFlip = false;
                spreadOutward = rng.Next(2) == 0;
                spreadPhase = 0;
                jackConsecutive = 0;
            }

            var pos = positions[i];
            int lane;

            switch (currentPattern)
            {
                case ChartPattern.Stream:
                    // 순차 흐름: 올라갔다 내려오기 (0→1→2→3→2→1→0→…)
                    lane = prevLane + stairDir;
                    if (lane >= LaneCount) { lane = LaneCount - 2; stairDir = -1; }
                    else if (lane < 0) { lane = 1; stairDir = 1; }
                    break;

                case ChartPattern.Stair:
                    // 계단: 한 방향으로만 (끝에 도달하면 반대편에서 재시작)
                    lane = prevLane + stairDir;
                    if (lane >= LaneCount) { lane = 0; }
                    else if (lane < 0) { lane = LaneCount - 1; }
                    break;

                case ChartPattern.Trill:
                    // 트릴: 두 레인 교차
                    lane = trillFlip ? trillLaneB : trillLaneA;
                    trillFlip = !trillFlip;
                    break;

                case ChartPattern.Jack:
                    // 잭: 같은 레인 2연타 후 반드시 이동
                    if (jackConsecutive >= 2)
                    {
                        prevLane = GetLeastUsedLane(laneCounts, prevLane, rng);
                        jackConsecutive = 0;
                    }
                    lane = prevLane;
                    jackConsecutive++;
                    break;

                case ChartPattern.Zigzag:
                    // 지그재그: 0→3→1→2→0→3→… 식 양극단 교차
                    lane = (zigzagPhase % 4) switch
                    {
                        0 => 0,
                        1 => 3,
                        2 => 1,
                        _ => 2,
                    };
                    zigzagPhase++;
                    break;

                case ChartPattern.Swing:
                    // 스윙: 인접 2레인 왕복
                    lane = swingFlip ? swingLaneB : swingLaneA;
                    swingFlip = !swingFlip;
                    break;

                case ChartPattern.Spread:
                    // 스프레드: 중앙→외곽 또는 외곽→중앙
                    int[] outwardOrder = [1, 2, 0, 3];
                    int[] inwardOrder = [0, 3, 1, 2];
                    int[] order = spreadOutward ? outwardOrder : inwardOrder;
                    lane = order[spreadPhase % 4];
                    spreadPhase++;
                    break;

                default:
                    lane = rng.Next(LaneCount);
                    break;
            }

            lane = Math.Clamp(lane, 0, LaneCount - 1);

            // 같은 레인 3연속 이상 방지 (모든 패턴 공통)
            if (lane == prevLane)
            {
                sameLaneCount++;
                if (sameLaneCount > MaxSameLane)
                {
                    lane = GetLeastUsedLane(laneCounts, prevLane, rng);
                    sameLaneCount = 1;
                }
            }
            else
            {
                sameLaneCount = 1;
            }

            // 주기적 균형 보정: 특정 레인이 평균 대비 25% 이상 많으면 가장 적은 레인으로 유도
            if (notes.Count > 0 && notes.Count % BalanceCheckInterval == 0)
            {
                float avg = notes.Count / (float)LaneCount;
                if (laneCounts[lane] > avg * 1.25f && currentPattern != ChartPattern.Trill
                    && currentPattern != ChartPattern.Zigzag && currentPattern != ChartPattern.Spread)
                {
                    lane = GetLeastUsedLane(laneCounts, -1, rng);
                    sameLaneCount = (lane == prevLane) ? sameLaneCount + 1 : 1;
                }
            }

            laneCounts[lane]++;
            notes.Add((pos.measure, lane, pos.offset));
            prevLane = lane;
            patternRemaining--;
            i++;
        }

        return notes;
    }

    /// <summary>
    /// 가장 적게 사용된 레인을 반환한다 (excludeLane 제외).
    /// </summary>
    private static int GetLeastUsedLane(int[] laneCounts, int excludeLane, Random rng)
    {
        int minCount = int.MaxValue;
        for (int l = 0; l < LaneCount; l++)
        {
            if (l != excludeLane && laneCounts[l] < minCount)
                minCount = laneCounts[l];
        }

        // 최소 사용량인 레인들 중 랜덤 선택
        Span<int> candidates = stackalloc int[LaneCount];
        int count = 0;
        for (int l = 0; l < LaneCount; l++)
        {
            if (l != excludeLane && laneCounts[l] == minCount)
                candidates[count++] = l;
        }

        return count > 0 ? candidates[rng.Next(count)] : (excludeLane + 1) % LaneCount;
    }

    /// <summary>
    /// 사용량이 적은 레인 2개를 쌍으로 선택한다 (트릴용).
    /// </summary>
    private static (int a, int b) PickBalancedPair(int[] laneCounts, Random rng)
    {
        // 사용량 오름차순으로 정렬된 레인 인덱스
        Span<int> sorted = stackalloc int[LaneCount];
        for (int l = 0; l < LaneCount; l++) sorted[l] = l;
        // 간단한 선택 정렬
        for (int x = 0; x < LaneCount - 1; x++)
            for (int y = x + 1; y < LaneCount; y++)
                if (laneCounts[sorted[y]] < laneCounts[sorted[x]])
                    (sorted[x], sorted[y]) = (sorted[y], sorted[x]);

        // 가장 적게 사용된 2개 선택 (약간의 랜덤성 추가)
        int a = sorted[0];
        int b = sorted[1];
        // 50% 확률로 순서 교체 (시작 레인 다양화)
        if (rng.Next(2) == 0) (a, b) = (b, a);
        return (a, b);
    }

    /// <summary>
    /// 사용량이 적은 인접 레인 쌍을 선택한다 (스윙용).
    /// </summary>
    private static (int a, int b) PickBalancedAdjacentPair(int[] laneCounts, Random rng)
    {
        // 인접 쌍: (0,1), (1,2), (2,3)
        int bestPair = 0;
        int bestSum = int.MaxValue;
        for (int p = 0; p < LaneCount - 1; p++)
        {
            int sum = laneCounts[p] + laneCounts[p + 1];
            if (sum < bestSum || (sum == bestSum && rng.Next(2) == 0))
            {
                bestSum = sum;
                bestPair = p;
            }
        }
        return (bestPair, bestPair + 1);
    }

    /// <summary>
    /// 에너지 우선 + 균등 분배로 목표 노트 수만큼 위치를 선택한다.
    /// </summary>
    private static List<(int measure, float offset, float time, float energy)> SelectNotePositions(
        List<(int measure, float offset, float time, float energy)> gridPositions,
        int targetNotes, Random rng)
    {
        var selectedPositions = new List<(int measure, float offset, float time, float energy)>();

        var energyPositions = gridPositions.Where(p => p.energy > 0).OrderByDescending(p => p.energy).ToList();
        var nonEnergyPositions = gridPositions.Where(p => p.energy == 0).ToList();

        int energyTake = Math.Min(energyPositions.Count, targetNotes);
        selectedPositions.AddRange(energyPositions.Take(energyTake));

        int remaining = targetNotes - selectedPositions.Count;
        if (remaining > 0 && nonEnergyPositions.Count > 0)
        {
            nonEnergyPositions = nonEnergyPositions.OrderBy(p => p.time).ToList();
            int step = Math.Max(1, nonEnergyPositions.Count / remaining);
            for (int i = 0; i < nonEnergyPositions.Count && selectedPositions.Count < targetNotes; i += step)
            {
                selectedPositions.Add(nonEnergyPositions[i]);
            }
        }

        if (selectedPositions.Count < targetNotes && nonEnergyPositions.Count > 0)
        {
            var usedTimes = new HashSet<float>(selectedPositions.Select(p => p.time));
            foreach (var p in nonEnergyPositions)
            {
                if (selectedPositions.Count >= targetNotes) break;
                if (!usedTimes.Contains(p.time))
                {
                    selectedPositions.Add(p);
                    usedTimes.Add(p.time);
                }
            }
        }

        return selectedPositions.OrderBy(p => p.time).ToList();
    }

    private static float EstimateBpm(List<WavAnalyzer.BeatInfo> beats)
    {
        if (beats.Count < 2)
            return 120f;

        var intervals = new List<float>();
        for (int i = 1; i < beats.Count; i++)
        {
            float gap = beats[i].Time - beats[i - 1].Time;
            if (gap > 0.15f && gap < 1.5f)
                intervals.Add(gap);
        }

        if (intervals.Count == 0)
            return 120f;

        intervals.Sort();
        float medianInterval = intervals[intervals.Count / 2];

        // 비트 간격 → BPM (4/4 박자 기준, 1마디 = 4비트)
        float bpm = 60f / medianInterval;

        // 합리적인 범위로 조정
        while (bpm < 80f) bpm *= 2f;
        while (bpm > 200f) bpm /= 2f;

        return MathF.Round(bpm);
    }

    private static string BuildBmsString(string songName, float bpm, List<(int measure, int lane, float offset)> notes)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"#TITLE {songName}");
        sb.AppendLine("#ARTIST AutoGenerated");
        sb.AppendLine($"#BPM {bpm:F0}");

        // 마디별, 레인별 그룹화
        var grouped = notes
            .GroupBy(n => n.measure)
            .OrderBy(g => g.Key);

        foreach (var measureGroup in grouped)
        {
            int measure = measureGroup.Key;

            // 레인별 분리
            var byLane = measureGroup.GroupBy(n => n.lane).OrderBy(g => g.Key);

            foreach (var laneGroup in byLane)
            {
                int lane = laneGroup.Key;
                int channel = 11 + lane; // 11~14

                var offsets = laneGroup.Select(n => n.offset).Distinct().OrderBy(o => o).ToList();

                // 적절한 해상도 결정 (최대 48분할)
                int resolution = DetermineResolution(offsets);
                char[] cells = new char[resolution * 2];
                Array.Fill(cells, '0');

                foreach (float offset in offsets)
                {
                    int cellIndex = (int)MathF.Round(offset * resolution);
                    cellIndex = Math.Clamp(cellIndex, 0, resolution - 1);
                    cells[cellIndex * 2] = '0';
                    cells[cellIndex * 2 + 1] = '1';
                }

                sb.AppendLine($"#{measure:D3}{channel:D2}:{new string(cells)}");
            }
        }

        return sb.ToString();
    }

    private static int DetermineResolution(List<float> offsets)
    {
        // 오프셋들을 잘 표현할 수 있는 최소 해상두
        int[] candidates = [4, 8, 12, 16, 24];

        foreach (int res in candidates)
        {
            bool fits = true;
            foreach (float o in offsets)
            {
                float quantized = MathF.Round(o * res) / res;
                if (MathF.Abs(quantized - o) > 0.03f)
                {
                    fits = false;
                    break;
                }
            }
            if (fits)
                return res;
        }

        return 24;
    }

    private static string NormalizeSongFileName(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char ch in name)
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToLowerInvariant(ch));
            else if (ch == ' ' || ch == '-' || ch == '_')
                sb.Append('_');
        }
        return sb.ToString();
    }
}
