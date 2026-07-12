namespace RhythmGame;

internal readonly record struct AudioClockSnapshot(
    string SourceFormat,
    int Samples,
    int QueryFailures,
    int BackwardJumps,
    int ForwardJumps,
    int Stalls,
    int Segments,
    float MeanAbsoluteJitterMs,
    float MaximumAbsoluteJitterMs,
    float SegmentDriftMs)
{
    public string ToLogMessage()
    {
        return $"Audio clock summary format={SourceFormat}, samples={Samples}, queryFailures={QueryFailures}, " +
               $"backward={BackwardJumps}, forward={ForwardJumps}, stalls={Stalls}, segments={Segments}, " +
               $"meanJitterMs={MeanAbsoluteJitterMs:F2}, maxJitterMs={MaximumAbsoluteJitterMs:F2}, segmentDriftMs={SegmentDriftMs:F2}";
    }
}

internal sealed class AudioClockDiagnostics
{
    private string _sourceFormat = "unknown";
    private bool _hasBaseline;
    private float _lastPositionSeconds;
    private double _lastWallSeconds;
    private float _segmentStartPositionSeconds;
    private double _segmentStartWallSeconds;
    private double _absoluteJitterSumSeconds;
    private float _maximumAbsoluteJitterSeconds;
    private int _samples;
    private int _queryFailures;
    private int _backwardJumps;
    private int _forwardJumps;
    private int _stalls;
    private int _segments;

    public void Start(string sourceFormat, float positionSeconds = 0f, double wallSeconds = 0d)
    {
        _sourceFormat = string.IsNullOrWhiteSpace(sourceFormat) ? "unknown" : sourceFormat.Trim().TrimStart('.').ToLowerInvariant();
        _hasBaseline = false;
        _absoluteJitterSumSeconds = 0d;
        _maximumAbsoluteJitterSeconds = 0f;
        _samples = 0;
        _queryFailures = 0;
        _backwardJumps = 0;
        _forwardJumps = 0;
        _stalls = 0;
        _segments = 0;
        ResetBaseline(positionSeconds, wallSeconds);
    }

    public void ResetBaseline(float positionSeconds, double wallSeconds)
    {
        if (!float.IsFinite(positionSeconds) || !double.IsFinite(wallSeconds))
            return;

        _hasBaseline = true;
        _lastPositionSeconds = Math.Max(0f, positionSeconds);
        _lastWallSeconds = Math.Max(0d, wallSeconds);
        _segmentStartPositionSeconds = _lastPositionSeconds;
        _segmentStartWallSeconds = _lastWallSeconds;
        _segments++;
    }

    public void Record(float positionSeconds, double wallSeconds)
    {
        if (!float.IsFinite(positionSeconds) || !double.IsFinite(wallSeconds))
        {
            _queryFailures++;
            return;
        }

        positionSeconds = Math.Max(0f, positionSeconds);
        wallSeconds = Math.Max(0d, wallSeconds);
        if (!_hasBaseline)
        {
            ResetBaseline(positionSeconds, wallSeconds);
            return;
        }

        double wallDelta = wallSeconds - _lastWallSeconds;
        if (wallDelta < 0.004d)
            return;

        float positionDelta = positionSeconds - _lastPositionSeconds;
        double jitterSeconds = positionDelta - wallDelta;
        float absoluteJitter = (float)Math.Abs(jitterSeconds);
        _absoluteJitterSumSeconds += absoluteJitter;
        _maximumAbsoluteJitterSeconds = Math.Max(_maximumAbsoluteJitterSeconds, absoluteJitter);
        _samples++;

        if (positionDelta < -0.002f)
            _backwardJumps++;
        if (jitterSeconds > 0.080d)
            _forwardJumps++;
        if (wallDelta >= 0.025d && positionDelta <= 0.001f)
            _stalls++;

        _lastPositionSeconds = positionSeconds;
        _lastWallSeconds = wallSeconds;
    }

    public void RecordQueryFailure()
    {
        _queryFailures++;
    }

    public AudioClockSnapshot Snapshot()
    {
        float meanJitterMs = _samples > 0
            ? (float)(_absoluteJitterSumSeconds / _samples * 1000d)
            : 0f;
        float segmentDriftMs = _hasBaseline
            ? (float)(((_lastPositionSeconds - _segmentStartPositionSeconds) - (_lastWallSeconds - _segmentStartWallSeconds)) * 1000d)
            : 0f;
        return new AudioClockSnapshot(
            _sourceFormat,
            _samples,
            _queryFailures,
            _backwardJumps,
            _forwardJumps,
            _stalls,
            _segments,
            meanJitterMs,
            _maximumAbsoluteJitterSeconds * 1000f,
            segmentDriftMs);
    }
}
