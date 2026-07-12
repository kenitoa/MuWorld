using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RhythmGame;

internal enum UiScreen
{
    Splash,
    MainMenu,
    Settings,
    SongSelect,
    SongDetail,
    Achievement,
    AchievementDetail,
    Analyze,
    InputCalibration,
    KeyBindings,
    ChartEditor
}

internal enum SettingsSlider
{
    None,
    Bgm,
    Preview,
    Sfx,
    NoteSpeed,
    AudioOffset,
    LaneBrightness,
    TextScale,
    SplashDuration
}

internal enum DisplayMode
{
    Windowed,
    Fullscreen
}

internal enum PlayMode
{
    Normal,
    Practice,
    Auto
}

public sealed partial class GameForm : Form
{
    private const float DesignWidth = 1152f;
    private const float DesignHeight = 768f;
    private const float MainMenuDesignWidth = 1680f;
    private const float MainMenuDesignHeight = 944f;

    // ── 엔진 & 타이머 ─────────────────────────────────────────────────────────
    private readonly GameEngine _engine = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 8 }; // ~120 fps
    private readonly System.Diagnostics.Stopwatch _frameStopwatch = System.Diagnostics.Stopwatch.StartNew();
    private int _hoverMenuIndex = -1;
    private int _hoverSongPlayIndex = -1;
    private int _songSelectDifficultyIndex = 1;

    private int _songSelectPageIndex;
    private int _songSelectSelectedIndex;
    private int _songSortModeIndex;
    private bool _songFavoritesOnly;
    private int _hoverPauseAction = -1;
    private string _songSearchQuery = string.Empty;
    private bool _isSongSearchFocused;
    private int _playModeIndex;
    private float _grooveGauge = 70f;
    private bool _gameFailedByGauge;
    private int _hoverAchievementCardIndex = -1;
    private bool _isAchievementBackHovered;
    private int _selectedAchievementCardIndex;
    private int _achievementDetailTabIndex;
    private int _achievementDetailPageIndex;
    private int _hoverAchievementDetailTabIndex = -1;
    private int _hoverAchievementDetailPageArrow = -1; // 0=left, 1=right
    private bool _isAchievementDetailBackHovered;
    private PlayerProgress _playerProgress = new();
    private readonly Queue<AchievementDefinition> _achievementToastQueue = new();
    private AchievementDefinition? _activeAchievementToast;
    private DateTime _achievementToastStartTime;
    private DateTime _achievementToastUntil;
    private bool _isExitHovered;
    private UiScreen _screen = UiScreen.Splash;
    private DateTime _splashStartTime = DateTime.Now;
    private readonly System.Windows.Forms.Timer _splashTimer = new() { Interval = 16 };
    private SettingsSlider _draggedSlider;
    private DisplayMode _displayMode;
    private readonly AudioManager _audio = new();
    private readonly AchievementProgressStore _achievementStore = new();
    private readonly UserSettingsStore _settingsStore = new();
    private readonly InputLogStore _inputLogStore = new();
    private readonly ReplayStore _replayStore = new();
    private readonly RenderResourceCache _renderResources = new();
    private readonly GdiResourceMonitor _gdiMonitor = new();
    private readonly Random _uiRandom = new();
    private readonly object _settingsSaveLock = new();
    private readonly object _settingsDiskSaveLock = new();
    private readonly bool _selfTestMode;
    private UserSettings? _latestSettingsSnapshot;
    private bool _settingsSaveWorkerRunning;
    private bool _isCountdownActive;
    private DateTime _countdownStartTime;
    private int _countdownSeconds;
    private float _layoutScale = 1f;
    private float _layoutOffsetX;
    private float _layoutOffsetY;
    private bool _isApplyingDisplayMode;
    private Rectangle _windowedBounds;
    private IReadOnlyList<LaneNote> _selectedChartNotes = [];
    private bool _isGamePaused;
    private bool _isReplayPlayback;
    private ReplayRecord? _activeReplay;
    private int _replayEventIndex;
    private string _previewSongKey = string.Empty;
    private DateTime _previewStartedAt;
    private ChartDifficultyInfo? _songPreviewDifficulty;
    private IReadOnlyList<LaneNote> _songPreviewNotes = [];
    private string _songPreviewStatus = string.Empty;
    private List<LaneNote> _chartEditorNotes = [];
    private readonly Stack<List<LaneNote>> _chartEditorUndo = new();
    private int _chartEditorSelectedIndex = -1;
    private int _hoverChartEditorAction = -1;
    private NoteType _chartEditorInsertType = NoteType.Tap;
    private float _chartEditorCursorTime;
    private float _chartEditorBpm = 120f;
    private float _chartEditorSongDuration = 60f;
    private string _chartEditorSongTitle = string.Empty;
    private string _chartEditorStatus = string.Empty;
    private string _chartEditorPath = string.Empty;
    private ChartDifficultyInfo? _chartEditorDifficulty;

    // Analyze screen state
    private string _analyzeSongId = string.Empty;
    private string _analyzeSongTitle = string.Empty;
    private string _analyzeSongArtist = string.Empty;
    private int _analyzeSongArtworkStyle;
    private int _analyzeScore;
    private int _analyzeHighestScore;
    private int _analyzeMaxCombo;
    private int _analyzePerfectCount;
    private int _analyzeGreatCount;
    private int _analyzeBetterCount;
    private int _analyzeGoodCount;
    private int _analyzeBadCount;
    private int _analyzeMissCount;
    private int _analyzeMissStreak;
    private int _analyzeEarlyCount;
    private int _analyzeLateCount;
    private int _analyzeAverageTimingMs;
    private float _analyzeAccuracy;
    private float _analyzeGrooveGauge;
    private float _analyzeGaugeClearThreshold;
    private string _analyzePlayMode = "NORMAL";
    private ResultGrade _analyzeGrade = ResultGrade.F;
    private ClearType _analyzeClearType = ClearType.Failed;
    private bool _isAnalyzeOkHovered;
    private int _hoverAnalyzeAction = -1;
    private bool _analyzeIsNewRecord;
    private ResultFeedbackSummary _analyzeFeedback = new("NO TIMING DATA", "NEXT: KEEP THE COMBO STABLE", "NO MISS BREAKS", "NO MISS", [], 0);
    private string _analyzeReplayStatus = string.Empty;
    private DateTime _chartCompleteTime;
    private bool _chartCompleteWaiting;

    private int _bgmVolume = 80;
    private int _previewVolume = 45;
    private int _sfxVolume = 60;
    private string _hitSoundSkin = "SYNTH";
    private string _visualSkinName = VisualSkin.DefaultName;
    private VisualSkin _visualSkin = VisualSkin.Load(VisualSkin.DefaultName);
    private int _hitSoundSkinIndex;
    private int _hitSoundPitch; // -1 low, 0 normal, 1 high
    private bool _hitSoundMuted;
    private int _audioOffsetMs;
    private int _themeColorIndex;
    private int _laneBrightness = 70;
    private int _frameRateMode = 2; // 0=30, 1=60, 2=120, 3=144, 4=240
    private bool _vsyncEnabled;
    private bool _darkModeEnabled;
    private int _splashDurationMs = 1600;
    private bool _highContrastEnabled;
    private int _colorVisionMode;
    private bool _reducedMotionEnabled;
    private int _textScalePercent = 100;
    private int _renderQualityMode = 1;
    private static readonly int[] FrameRateIntervals = [33, 16, 8, 7, 4]; // ms per frame
    private static readonly string[] FrameRateLabels = ["30", "60", "120", "144", "240"];
    private static readonly string[] RenderQualityLabels = ["FAST", "BAL", "HIGH"];
    private static readonly string[] ColorVisionLabels = ["OFF", "DEUT", "PROT", "TRIT"];
    private static readonly string[] PlayModeLabels = ["NORMAL", "PRACTICE", "AUTO"];
    private static readonly string[] HitSoundSkinLabels = ["CLASSIC", "EDM", "LO-FI"];
    private static readonly string[] HitSoundPitchLabels = ["LOW", "MID", "HIGH"];
    private static readonly string[] SongSortLabels = ["TITLE", "ARTIST", "BPM", "LENGTH", "SCORE", "RECENT", "LEVEL", "FAV"];
    private static readonly string[] PauseActionLabels = ["RESUME", "RESTART", "SONG SELECT", "SETTINGS", "EXIT TO MENU"];
    private static readonly Size[] WindowResolutionPresets =
    [
        new(1280, 720),
        new(1366, 768),
        new(1600, 900),
        new(1680, 944),
        new(1920, 1080),
    ];
    private string _gameBgaPath = string.Empty;
    private Bitmap? _gameBgaImage;
    private Bitmap? _gameBackgroundCache;
    private string _gameBackgroundCacheKey = string.Empty;
    private const float GameStartDelaySeconds = 3f;
    private string _comboMilestoneText = string.Empty;
    private DateTime _comboMilestoneTime;
    private int _lastComboMilestone;
    private long _gameDrawFrameCount;
    private long _lastGameDrawSampleFrame;
    private long _lastGameDrawAllocatedBytes;
    private float _lastPlaybackPositionSeconds;
    private DateTime _lastAllocationLogTime;

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint period);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint period);

    private static readonly Color[] ThemeColors =
    [
        Color.FromArgb(72, 126, 216),
        Color.FromArgb(149, 116, 225),
        Color.FromArgb(68, 206, 130),
        Color.FromArgb(248, 151, 69),
    ];

    private readonly record struct GaugeRule(
        float Start,
        float ClearThreshold,
        float PerfectGain,
        float GreatGain,
        float BetterGain,
        float GoodGain,
        float BadLoss,
        float MissLoss);

    // ── 판정 피드백 ───────────────────────────────────────────────────────────
    private string?  _feedback;
    private string?  _feedbackTiming;
    private Judgment? _feedbackJudgment;
    private DateTime _feedbackTime;

    private readonly Stopwatch _calibrationStopwatch = new();
    private readonly List<float> _calibrationOffsets = [];
    private readonly List<float> _calibrationTargetTimes = [];
    private readonly HashSet<int> _calibrationHitTargets = [];
    private float _nextCalibrationBeatSeconds;
    private float _lastCalibrationOffsetSeconds;
    private float _lastCalibrationHitRate;
    private float _autoSyncEstimateSeconds;
    private float _autoSyncJitterSeconds;
    private float _autoSyncConfidence;
    private int _autoSyncValidSampleCount;
    private int _calibrationBeatCount;
    private int _calibrationPreviousTimerInterval;
    private bool _calibrationLowLatencyModeActive;
    private bool _gameLowLatencyModeActive;
    private int _gamePreviousTimerInterval;
    private bool _calibrationSaved;
    private bool _isCalibrationBackHovered;
    private bool _isCalibrationStartHovered;
    private int _keyBindingModeIndex;
    private int _keyBindingCaptureLane = -1;
    private int _hoverKeyBindingLane = -1;
    private int _hoverKeyBindingAction = -1;
    private string _keyBindingStatus = "SELECT A LANE";
    private readonly bool[] _keyTestPressed = new bool[7];
    private int _mouseHeldLane = -1;
    private readonly List<InputLogEvent> _inputLogEvents = [];
    private string _replayRecordingInvalidReason = string.Empty;
    private string _sessionAudioFingerprint = string.Empty;
    private Task<string>? _sessionAudioFingerprintTask;
    private CancellationTokenSource? _sessionAudioFingerprintCancellation;
    private int _replayLoadGeneration;
    private CancellationTokenSource? _replayLoadCancellation;

    // ── HUD 캐시 (불필요한 문자열 할당 방지) ────────────────────────────────────
    private string _cachedStatsText = string.Empty;
    private int _cachedStatsPerfect, _cachedStatsGreat, _cachedStatsBetter;
    private int _cachedStatsGood, _cachedStatsBad, _cachedStatsMiss;
    private string _cachedComboText = string.Empty;
    private int _cachedComboValue;

    // ── 레인 설정 ─────────────────────────────────────────────────────────────
    private sealed record LaneModeConfig(int Count, Keys[] Keys, string[] Labels);

    private static readonly LaneModeConfig[] LaneModes =
    [
        new(4, [Keys.D, Keys.F, Keys.J, Keys.K], ["D", "F", "J", "K"]),
        new(5, [Keys.D, Keys.F, Keys.Space, Keys.J, Keys.K], ["D", "F", "Space", "J", "K"]),
        new(6, [Keys.S, Keys.D, Keys.F, Keys.J, Keys.K, Keys.L], ["S", "D", "F", "J", "K", "L"]),
        new(7, [Keys.S, Keys.D, Keys.F, Keys.Space, Keys.J, Keys.K, Keys.L], ["S", "D", "F", "Space", "J", "K", "L"]),
    ];

    private int _laneModeIndex;
    private readonly Keys[][] _laneKeyBindings = LaneModes.Select(mode => mode.Keys.ToArray()).ToArray();
    private LaneModeConfig ActiveLaneMode => LaneModes[_laneModeIndex];
    private int LaneCount => ActiveLaneMode.Count;
    private Keys[] LaneKeys => _laneKeyBindings[_laneModeIndex];
    private string[] LaneLabels => _laneKeyBindings[_laneModeIndex].Select(FormatKeyLabel).ToArray();
    private int LaneWidth => ClientSize.Width / LaneCount;
    private ReplaySettingsSnapshot? ActiveReplaySettings => _isReplayPlayback ? _activeReplay?.Settings : null;
    private float EffectiveSpeedMultiplier => ActiveReplaySettings is { } settings
        ? Math.Clamp(settings.NoteSpeedPercent / 100f, 0.1f, 5f)
        : _speedMultiplier;
    private int EffectiveAudioOffsetMs => ActiveReplaySettings is { } settings
        ? Math.Clamp(settings.AudioOffsetMs, -150, 150)
        : _audioOffsetMs;
    private int EffectivePlayModeIndex => ActiveReplaySettings is { } settings
        ? Math.Clamp(settings.PlayModeIndex, 0, PlayModeLabels.Length - 1)
        : _playModeIndex;

    private static readonly Color[] LaneColors =
    [
        Color.FromArgb(255,  80,  80),   // D — 빨강
        Color.FromArgb( 80, 210,  80),   // F — 초록
        Color.FromArgb( 80, 120, 255),   // J — 파랑
        Color.FromArgb(255, 210,  50),   // K — 노랑
        Color.FromArgb(255, 120, 210),   // 5K center / 7K accent
        Color.FromArgb( 80, 220, 230),   // 7K cyan
        Color.FromArgb(210, 140, 255),   // 7K violet
    ];

    private readonly bool[] _lanePressed = new bool[7];
    private readonly bool[] _pauseHeldLaneAwaitingKeyUp = new bool[7];

    // ── 캐시된 GDI+ 객체 (게임 렌더링 성능 최적화) ──────────────────────────────
    private static readonly SolidBrush[] _noteGlowBrushes = LaneColors
        .Select(c => new SolidBrush(Color.FromArgb(50, c))).ToArray();
    private static readonly Color[] _noteTopColors = LaneColors
        .Select(c => Color.FromArgb(240, ControlPaint.Light(c, 0.3f))).ToArray();
    private static readonly Color[] _noteBotColors = LaneColors
        .Select(c => Color.FromArgb(220, c)).ToArray();
    private static readonly SolidBrush _noteHighlightBrush = new(Color.FromArgb(80, 255, 255, 255));
    private static readonly Pen _noteBorderPen = new(Color.FromArgb(100, 255, 255, 255), 1f);
    private static readonly Font _comboLabelFont = new("Segoe UI", 11, FontStyle.Bold);
    private static readonly Font _comboNumFont = new("Segoe UI", 40, FontStyle.Bold);
    private static readonly SolidBrush _comboLabelBrush = new(Color.FromArgb(180, 200, 215, 240));
    private static readonly SolidBrush _comboNumBrush = new(Color.White);
    private static readonly Font _fbFont = new("Segoe UI", 24, FontStyle.Bold);
    private static readonly Font _scoreFont = new("Segoe UI", 12, FontStyle.Bold);
    private static readonly SolidBrush _scoreBrush = new(Color.FromArgb(200, 220, 230, 250));
    private static readonly Font _statFont = new("Segoe UI", 9);
    private static readonly SolidBrush _statBrush = new(Color.FromArgb(140, 180, 190, 210));
    private static readonly Font _accFont = new("Segoe UI", 28, FontStyle.Bold);
    private static readonly Font _maxFont = new("Segoe UI", 22, FontStyle.Bold);
    private static readonly SolidBrush _maxBrush = new(Color.FromArgb(255, 220, 160));
    private static readonly Pen _dividerPen = new(Color.FromArgb(40, 180, 190, 210), 1f);
    private static readonly Pen _guidePen = new(Color.FromArgb(12, 255, 255, 255), 1f);
    private static readonly Font _keyLabelFont = new("Segoe UI", 13, FontStyle.Bold);

    // ── 오브젝트 풀링: 게임 프레임 / 히트존 / 카운트다운 / 판정 피드백 ─────
    // DrawGameFrame
    private static readonly Pen _framePenOuter = new(Color.FromArgb(80, 160, 175, 200), 3f);
    private static readonly Pen _framePenInner = new(Color.FromArgb(40, 200, 215, 240), 1.5f);
    private static readonly SolidBrush _frameCornerBrush = new(Color.FromArgb(60, 180, 200, 230));

    // DrawHitZoneGlow
    private static readonly Pen _hitPen1 = new(Color.FromArgb(220, 255, 200, 80), 3f);
    private static readonly Pen _hitPen2 = new(Color.FromArgb(140, 255, 240, 180), 1.5f);

    // DrawPianoKeys — 눌림/해제 상태 캐시
    private static readonly SolidBrush _keyLabelPressedBrush = new(Color.FromArgb(140, 80, 90, 110));
    private static readonly SolidBrush _keyLabelReleasedBrush = new(Color.FromArgb(140, 180, 190, 210));

    // DrawCountdown — 캐시된 폰트/브러시
    private static readonly Font _countdownTitleFont = new("Segoe UI", 18, FontStyle.Bold);
    private static readonly Font _countdownNumFont = new("Segoe UI", 96, FontStyle.Bold);
    private static readonly SolidBrush _countdownTitleBrush = new(Color.FromArgb(190, 220, 230, 250));
    private static readonly SolidBrush _countdownNumBrush = new(Color.White);

    // DrawStyledNote — 재사용 가능한 브러시 (Color를 매 프레임 갱신)
    private readonly SolidBrush _reusableFbBrush = new(Color.White);

    // ApplyGameModeEffect — 블라인드/안개 효과
    private static readonly SolidBrush _blindBrush = new(Color.FromArgb(250, 10, 10, 20));
    private static readonly SolidBrush _fogBrush1 = new(Color.FromArgb(140, 15, 18, 30));

    // DrawSplashWaves — 캐시된 펜 배열
    private static readonly Pen[] _splashWavePens;
    private static readonly (float amp, float freq, float speed, Color color, float thickness, float yOff)[] _splashWaveParams =
    [
        (35f, 0.008f,  1.8f, Color.FromArgb(60,  180, 210, 245), 2.5f, -40f),
        (45f, 0.006f,  1.2f, Color.FromArgb(80,  160, 190, 240), 3.0f, -20f),
        (55f, 0.005f,  0.9f, Color.FromArgb(100, 190, 170, 240), 3.5f,   0f),
        (50f, 0.007f,  1.5f, Color.FromArgb(90,  210, 180, 245), 2.8f,  15f),
        (40f, 0.009f,  2.0f, Color.FromArgb(70,  200, 200, 250), 2.2f,  30f),
        (30f, 0.011f,  2.5f, Color.FromArgb(50,  170, 200, 240), 1.8f,  45f),
        (48f, 0.0055f, 1.0f, Color.FromArgb(90,  200, 160, 230), 3.2f, -10f),
        (38f, 0.0075f, 1.6f, Color.FromArgb(70,  220, 180, 250), 2.5f,  20f),
        (55f, 0.0045f, 0.7f, Color.FromArgb(60,  180, 150, 220), 3.8f,   5f),
        (42f, 0.0065f, 1.3f, Color.FromArgb(55,  230, 180, 230), 2.0f, -30f),
        (35f, 0.010f,  2.2f, Color.FromArgb(45,  240, 200, 240), 1.6f,  35f),
    ];

    // DrawSplashParticles — 재사용 가능한 브러시
    private static readonly SolidBrush _particleBrush = new(Color.White);
    private static readonly SolidBrush _particleGlowBrush = new(Color.White);

    static GameForm()
    {
        // 스플래시 웨이브 펜 프리캐시 (스케일 1.0 기준)
        _splashWavePens = new Pen[_splashWaveParams.Length];
        for (int i = 0; i < _splashWaveParams.Length; i++)
        {
            _splashWavePens[i] = new Pen(_splashWaveParams[i].color, Math.Max(1f, _splashWaveParams[i].thickness));
            _splashWavePens[i].StartCap = LineCap.Round;
            _splashWavePens[i].EndCap = LineCap.Round;
        }
    }

    // ── 생성자 ────────────────────────────────────────────────────────────────
    public GameForm()
        : this(selfTestMode: false)
    {
    }

    internal GameForm(bool selfTestMode)
    {
        _selfTestMode = selfTestMode;
        Text            = "Rhythm Game";
        AccessibleName = "Rhythm Game";
        AccessibleRole = AccessibleRole.Application;
        BackColor       = Color.White;
        DoubleBuffered  = true;
        FormBorderStyle = FormBorderStyle.None;
        MinimumSize     = new Size(960, 540);
        MaximizeBox     = false;
        StartPosition   = FormStartPosition.CenterScreen;

        // 현재 모니터 해상도의 75%로 초기 창 크기 설정 (DesignWidth:DesignHeight 비율 유지)
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        float fitScale = Math.Min(screen.Width * 0.75f / MainMenuDesignWidth, screen.Height * 0.75f / MainMenuDesignHeight);
        int initW = (int)(MainMenuDesignWidth * fitScale);
        int initH = (int)(MainMenuDesignHeight * fitScale);
        ClientSize = new Size(Math.Max(initW, 960), Math.Max(initH, 540));
        KeyPreview      = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        _timer.Tick += OnTick;
        MouseMove += OnMenuMouseMove;
        MouseLeave += OnMenuMouseLeave;
        MouseDown += OnMenuMouseDown;
        MouseUp += OnMenuMouseUp;
        Resize += OnGameFormResize;

        LoadUserSettings();
        UpdateLayoutMetrics();
        ApplySettingsToRuntime();
        _playerProgress = _achievementStore.Load();

        // WAV 파일 분석 및 채보 자동 생성
        if (!selfTestMode)
        {
            ChartGenerator.BeginGenerateAllChartsAsync();
            StartSongFolderWatcher();
            _audio.PlayMainScreenBgm();
            _splashTimer.Tick += OnSplashTick;
            _splashTimer.Start();
        }
    }

    private void OnSplashTick(object? sender, EventArgs e)
    {
        if (_splashDurationMs > 0 && (DateTime.Now - _splashStartTime).TotalMilliseconds >= _splashDurationMs)
        {
            TransitionFromSplash();
            return;
        }

        Invalidate();
    }

    private void PlaySelectedSongBgm(SongEntry? song)
    {
        if (song is null)
            return;

        if (File.Exists(song.FilePath))
            _audio.PlayInGameBgm(song.FilePath);
    }

    private void LoadBgaForSong(SongEntry? song)
    {
        string path = song?.BgaPath ?? string.Empty;
        if (string.Equals(_gameBgaPath, path, StringComparison.Ordinal))
            return;

        _gameBgaImage?.Dispose();
        _gameBgaImage = null;
        _gameBgaPath = path;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        string extension = Path.GetExtension(path);
        if (!string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".bmp", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            using var source = new Bitmap(path);
            _gameBgaImage = new Bitmap(source);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to load BGA image {path}.", ex);
            _gameBgaImage = null;
        }
    }

    // ── 게임 루프 ─────────────────────────────────────────────────────────────
    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        double elapsedMs = _frameStopwatch.Elapsed.TotalMilliseconds;
        _frameStopwatch.Restart();
        float dt = (float)(elapsedMs / 1000.0);
        dt = Math.Min(dt, 0.05f); // cap at 50ms to avoid jumps
        bool toastVisible = UpdateAchievementToast(now);

        if (_screen == UiScreen.InputCalibration)
        {
            UpdateInputCalibration();
            Invalidate();
            return;
        }

        if (_isCountdownActive)
        {
            if ((now - _countdownStartTime).TotalSeconds >= _countdownSeconds)
            {
                _isCountdownActive = false;
                _engine.Start(ClientSize.Height, _selectedChartNotes, LaneCount);
                SongEntry? selectedSong = _screen == UiScreen.SongSelect ? GetSelectedSong() : null;
                PlaySelectedSongBgm(selectedSong);
            }

            Invalidate();
            return;
        }

        if (_isGamePaused)
        {
            Invalidate();
            return;
        }

        if (!_engine.IsRunning)
        {
            if (toastVisible)
            {
                Invalidate();
            }
            else
            {
                _timer.Stop();
            }
            return;
        }

        MaintainAutoPlayLaneHolds();
        float? playbackPosition = _audio.GetInGameBgmPositionSeconds();
        if (playbackPosition.HasValue && float.IsFinite(playbackPosition.GetValueOrDefault()))
            _lastPlaybackPositionSeconds = Math.Max(0f, playbackPosition.GetValueOrDefault());
        if (_isReplayPlayback)
            UpdateReplayPlayback(dt, playbackPosition);
        else
            _engine.Update(dt, playbackPosition);

        if (!_engine.IsRunning)
            return;

        ProcessAutoPlayMode();
        ConsumeEngineGaugeEvents();
        UpdateComboMilestone();
        _gdiMonitor.Sample(_isReplayPlayback ? "replay" : "game");

        if (!IsNoFailPlayMode && _gameFailedByGauge)
        {
            EndGame();
            return;
        }

        if (!_engine.IsChartComplete && _audio.IsInGameBgmPlaying && _audio.IsInGameBgmFinished())
        {
            if (!_chartCompleteWaiting)
            {
                _chartCompleteWaiting = true;
                _chartCompleteTime = DateTime.Now;
            }
            else if ((DateTime.Now - _chartCompleteTime).TotalSeconds >= 1.2)
            {
                _chartCompleteWaiting = false;
                EndGame();
                return;
            }
        }
        else if (!_engine.IsChartComplete)
        {
            _chartCompleteWaiting = false;
        }

        if (_engine.IsChartComplete)
        {
            if (!_chartCompleteWaiting)
            {
                _chartCompleteWaiting = true;
                _chartCompleteTime = DateTime.Now;
            }
            else if (ShouldEndAfterChartComplete())
            {
                _chartCompleteWaiting = false;
                EndGame();
                return;
            }
        }

        Invalidate();
    }

    // ── 키 입력 ───────────────────────────────────────────────────────────────
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (HandleAccessibilityKeyDown(e))
            return;

        if (!_engine.IsRunning)
        {
            if (_screen == UiScreen.Splash)
            {
                TransitionFromSplash();
                return;
            }

            if (_isCountdownActive)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    e.SuppressKeyPress = true;
                    CancelCountdown();
                    Invalidate();
                }
                return;
            }

            if (_screen == UiScreen.InputCalibration)
            {
                e.SuppressKeyPress = true;
                if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Back)
                {
                    StopInputCalibration(save: false);
                    _screen = UiScreen.Settings;
                    Invalidate();
                    return;
                }

                if (e.KeyCode == Keys.Enter)
                {
                    StartInputCalibration();
                    Invalidate();
                    return;
                }

                if (e.KeyCode == Keys.Space || LaneKeys.Contains(e.KeyCode))
                {
                    CaptureInputCalibrationHit();
                    Invalidate();
                }

                return;
            }

            if (_screen == UiScreen.KeyBindings)
            {
                e.SuppressKeyPress = true;
                HandleKeyBindingsKeyDown(e.KeyCode);
                Invalidate();
                return;
            }

            if (_screen == UiScreen.ChartEditor)
            {
                e.SuppressKeyPress = true;
                HandleChartEditorKeyDown(e.KeyCode);
                Invalidate();
                return;
            }

            if (_screen == UiScreen.Settings)
            {
                if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Back)
                {
                    e.SuppressKeyPress = true;
                    _screen = UiScreen.MainMenu;
                    Invalidate();
                }
                return;
            }

            if (_screen == UiScreen.Achievement)
            {
                if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Back)
                {
                    e.SuppressKeyPress = true;
                    _screen = UiScreen.MainMenu;
                    Invalidate();
                }
                return;
            }

            if (_screen == UiScreen.AchievementDetail)
            {
                if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Back)
                {
                    e.SuppressKeyPress = true;
                    _screen = UiScreen.Achievement;
                    Invalidate();
                }
                return;
            }

            if (_screen == UiScreen.SongDetail)
            {
                if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Back)
                {
                    e.SuppressKeyPress = true;
                    _screen = UiScreen.SongSelect;
                    Invalidate();
                    return;
                }

                if (e.KeyCode == Keys.F)
                {
                    e.SuppressKeyPress = true;
                    ToggleSelectedSongFavorite();
                    Invalidate();
                    return;
                }

                return;
            }

            if (_screen == UiScreen.Analyze)
            {
                e.SuppressKeyPress = true;
                if (e.KeyCode is Keys.Escape or Keys.Back or Keys.Enter or Keys.Space)
                {
                    ActivateAnalyzeAction(1);
                    return;
                }

                if (e.KeyCode == Keys.R)
                {
                    ActivateAnalyzeAction(0);
                    return;
                }

                if (e.KeyCode is Keys.N or Keys.Right)
                {
                    ActivateAnalyzeAction(2);
                    return;
                }

                return;
            }

            if (_screen == UiScreen.SongSelect)
            {
                if (e.KeyCode != Keys.L)
                    CancelPendingReplayLoad();

                if (e.KeyCode == Keys.Escape)
                {
                    e.SuppressKeyPress = true;
                    _screen = UiScreen.MainMenu;
                    _audio.StopSongPreview();
                    _audio.PlayMainScreenBgm();
                    Invalidate();
                    return;
                }

                if (!_isSongSearchFocused && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space))
                {
                    e.SuppressKeyPress = true;
                    if (GetSelectedSong() is not null)
                        BeginGame();
                    return;
                }

                if (!_isSongSearchFocused && e.KeyCode == Keys.Up)
                {
                    e.SuppressKeyPress = true;
                    MoveSongSelection(-1);
                    Invalidate();
                    return;
                }

                if (!_isSongSearchFocused && e.KeyCode == Keys.Down)
                {
                    e.SuppressKeyPress = true;
                    MoveSongSelection(1);
                    Invalidate();
                    return;
                }

                if (!_isSongSearchFocused && e.KeyCode == Keys.PageUp)
                {
                    e.SuppressKeyPress = true;
                    SelectSongPage(_songSelectPageIndex - 1);
                    Invalidate();
                    return;
                }

                if (!_isSongSearchFocused && e.KeyCode == Keys.PageDown)
                {
                    e.SuppressKeyPress = true;
                    SelectSongPage(_songSelectPageIndex + 1);
                    Invalidate();
                    return;
                }

                if (!_isSongSearchFocused && e.KeyCode == Keys.Left)
                {
                    e.SuppressKeyPress = true;
                    ChangeSongDifficulty(-1);
                    Invalidate();
                    return;
                }

                if (!_isSongSearchFocused && e.KeyCode == Keys.Right)
                {
                    e.SuppressKeyPress = true;
                    ChangeSongDifficulty(1);
                    Invalidate();
                    return;
                }

                if (e.KeyCode == Keys.Back && _isSongSearchFocused)
                {
                    ApplySongSearchInput(removeLast: true);
                    Invalidate();
                    return;
                }

                if (e.KeyCode == Keys.D5 || e.KeyCode == Keys.NumPad5)
                {
                    e.SuppressKeyPress = true;
                    SwitchLaneModeToCount(5);
                    Invalidate();
                    return;
                }

                if (e.KeyCode == Keys.D6 || e.KeyCode == Keys.NumPad6)
                {
                    e.SuppressKeyPress = true;
                    SwitchLaneModeToCount(6);
                    Invalidate();
                    return;
                }

                if (e.KeyCode == Keys.D7 || e.KeyCode == Keys.NumPad7)
                {
                    e.SuppressKeyPress = true;
                    SwitchLaneModeToCount(7);
                    Invalidate();
                    return;
                }

                if (e.KeyCode == Keys.E && !_isSongSearchFocused)
                {
                    e.SuppressKeyPress = true;
                    OpenSelectedChartForEditing();
                    return;
                }

                if (e.KeyCode == Keys.D && !_isSongSearchFocused)
                {
                    e.SuppressKeyPress = true;
                    OpenSelectedSongDetail();
                    return;
                }

                if (e.KeyCode == Keys.F && !_isSongSearchFocused)
                {
                    e.SuppressKeyPress = true;
                    ToggleSelectedSongFavorite();
                    Invalidate();
                    return;
                }

                if (e.KeyCode == Keys.L && !_isSongSearchFocused)
                {
                    e.SuppressKeyPress = true;
                    StartReplayForSelectedSong();
                    return;
                }

                if (e.KeyCode == Keys.R && !_isSongSearchFocused)
                {
                    e.SuppressKeyPress = true;
                    RescanSongs();
                    return;
                }

                return;
            }

            if (_screen == UiScreen.MainMenu)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    e.SuppressKeyPress = true;
                    Close();
                    return;
                }

                if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    _screen = UiScreen.SongSelect;
                    Invalidate();
                    return;
                }
            }

            if (e.KeyCode == Keys.D5 || e.KeyCode == Keys.NumPad5) { e.SuppressKeyPress = true; SwitchLaneModeToCount(5); Invalidate(); return; }
            if (e.KeyCode == Keys.D6 || e.KeyCode == Keys.NumPad6) { e.SuppressKeyPress = true; SwitchLaneModeToCount(6); Invalidate(); return; }
            if (e.KeyCode == Keys.D7 || e.KeyCode == Keys.NumPad7) { e.SuppressKeyPress = true; SwitchLaneModeToCount(7); Invalidate(); return; }

            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; BeginGame(); }
            return;
        }

        if (_isGamePaused)
        {
            int pausedLane = GetLaneForKey(e.KeyCode);
            if (!_isReplayPlayback && pausedLane >= 0)
            {
                // A held key can keep producing KeyDown repeats over the pause
                // overlay. Require its physical KeyUp before accepting a fresh
                // reacquire press after resume.
                _pauseHeldLaneAwaitingKeyUp[pausedLane] = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.P)
            {
                e.SuppressKeyPress = true;
                ResumeGame();
                return;
            }

            if (e.KeyCode == Keys.Back)
            {
                e.SuppressKeyPress = true;
                _chartCompleteWaiting = false;
                EndGame();
                return;
            }

            return;
        }

        if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.P) { e.SuppressKeyPress = true; PauseGame(); return; }

        if (_isReplayPlayback)
        {
            e.SuppressKeyPress = true;
            _feedback = "INPUT LOCKED";
            _feedbackTiming = "REPLAY";
            _feedbackJudgment = null;
            _feedbackTime = DateTime.Now;
            Invalidate();
            return;
        }

        // 배속 조절: 1키 증가, 2키 감소
        if (e.KeyCode == Keys.D1) { e.SuppressKeyPress = true; IncreaseSpeed(); Invalidate(); return; }
        if (e.KeyCode == Keys.D2) { e.SuppressKeyPress = true; DecreaseSpeed(); Invalidate(); return; }
        if (e.KeyCode == Keys.D6 || e.KeyCode == Keys.NumPad6) { e.SuppressKeyPress = true; SwitchLaneModeToCount(6); Invalidate(); return; }
        if (e.KeyCode == Keys.D7 || e.KeyCode == Keys.NumPad7) { e.SuppressKeyPress = true; SwitchLaneModeToCount(7); Invalidate(); return; }

        // 모드 전환: 3키 다음, 4키 이전
        if (e.KeyCode == Keys.D3) { e.SuppressKeyPress = true; CycleGameModeForward(); Invalidate(); return; }
        if (e.KeyCode == Keys.D4) { e.SuppressKeyPress = true; CycleGameModeBackward(); Invalidate(); return; }

        int boundLane = GetLaneForKey(e.KeyCode);
        if (boundLane >= 0 && _pauseHeldLaneAwaitingKeyUp[boundLane])
        {
            e.SuppressKeyPress = true;
            return;
        }
        if (boundLane >= 0)
        {
            e.SuppressKeyPress = true;
            BeginLaneInput(boundLane, FormatKeyLabel(e.KeyCode), "keyboard");
            return;
        }

        for (int i = 0; i < LaneKeys.Length; i++)
        {
            if (e.KeyCode != LaneKeys[i]) continue;
            e.SuppressKeyPress = true;
            if (_lanePressed[i]) break;   // 키 반복 방지
            _lanePressed[i] = true;
            _engine.SetLaneHeld(i, true);
            GameEngine.HitResult? hit = _engine.TryHit(i);
            if (hit is not null)
            {
                _feedback = hit.Value.Label;
                _feedbackTiming = hit.Value.TimingLabel;
                _feedbackJudgment = hit.Value.Judgment;
                _feedbackTime = DateTime.Now;

            }
            // Invalidate()는 타이머(~8ms)가 이미 매 프레임 호출하므로 생략
            break;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (!_engine.IsRunning && _screen == UiScreen.KeyBindings)
        {
            HandleKeyBindingsKeyUp(e.KeyCode);
            Invalidate();
            return;
        }

        int boundLane = GetLaneForKey(e.KeyCode);
        if (_isReplayPlayback)
            return;

        if (boundLane >= 0 && _pauseHeldLaneAwaitingKeyUp[boundLane])
        {
            // Consume the first physical release after a pause without turning it
            // into an early Long/Slide release. The following KeyDown is the
            // explicit reacquire input covered by resume grace.
            _pauseHeldLaneAwaitingKeyUp[boundLane] = false;
            _lanePressed[boundLane] = false;
            _engine.SetLaneHeld(boundLane, false);
            return;
        }

        if (_isGamePaused && boundLane >= 0)
        {
            // A key released on the pause overlay must not resolve a Long/Slide
            // note against the frozen audio clock. Resume grants a short window
            // in which the player can press required lanes again.
            _lanePressed[boundLane] = false;
            _engine.SetLaneHeld(boundLane, false);
            return;
        }

        if (boundLane >= 0)
        {
            EndLaneInput(boundLane, FormatKeyLabel(e.KeyCode), "keyboard");
            if (!_engine.IsRunning) Invalidate();
            return;
        }

        for (int i = 0; i < LaneKeys.Length; i++)
        {
            if (e.KeyCode == LaneKeys[i])
            {
                _lanePressed[i] = false;
                _engine.SetLaneHeld(i, false);
                // 게임 중에는 타이머가 Invalidate()를 호출하므로 메뉴에서만 호출
                if (!_engine.IsRunning) Invalidate();
                break;
            }
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        if (!_engine.IsRunning && _screen == UiScreen.SongSelect)
            ScrollSongSelect(e.Delta);
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);

        if (_engine.IsRunning || _screen != UiScreen.SongSelect || !_isSongSearchFocused)
            return;

        if (!char.IsControl(e.KeyChar))
        {
            ApplySongSearchInput(appendChar: e.KeyChar);
            Invalidate();
            e.Handled = true;
        }
    }

    private void BeginGame(
        bool replayPlayback = false,
        IReadOnlyList<LaneNote>? validatedReplayChart = null,
        string? validatedAudioFingerprint = null)
    {
        if (!replayPlayback)
            CancelPendingReplayLoad();

        if (!replayPlayback && _screen == UiScreen.SongSelect)
        {
            SongEntry? requestedSong = GetSelectedSong();
            ChartGenerator.ChartGenerationSnapshot generation = ChartGenerator.GetStatus();
            if (requestedSong is not null && generation.IsRunning && !ChartGenerator.HasAllPrecomputedCharts(requestedSong.Title))
            {
                _feedback = "CHART PREPARING";
                _feedbackTiming = $"{generation.ProcessedSongs}/{generation.TotalSongs}";
                _feedbackJudgment = null;
                _feedbackTime = DateTime.Now;
                Invalidate();
                return;
            }
        }

        _feedback = null;
        _feedbackTiming = null;
        _feedbackJudgment = null;
        _comboMilestoneText = string.Empty;
        _lastComboMilestone = 0;
        _inputLogEvents.Clear();
        _isReplayPlayback = replayPlayback;
        _replayEventIndex = 0;
        if (!replayPlayback)
        {
            _activeReplay = null;
            _replayRecordingInvalidReason = string.Empty;
        }
        _isGamePaused = false;
        _gameFailedByGauge = false;
        _grooveGauge = GetGaugeRule(_songSelectDifficultyIndex).Start;
        _gameDrawFrameCount = 0;
        _lastGameDrawSampleFrame = 0;
        _lastGameDrawAllocatedBytes = 0;
        _lastPlaybackPositionSeconds = 0f;
        _lastAllocationLogTime = DateTime.Now;
        _gdiMonitor.Start(replayPlayback ? "replay" : "game");
        Array.Clear(_lanePressed);
        Array.Clear(_pauseHeldLaneAwaitingKeyUp);
        _mouseHeldLane = -1;
        ApplySettingsToRuntime();
        _audio.StopAllSounds();
        SongEntry? selectedSong = _screen == UiScreen.SongSelect ? GetSelectedSong() : null;
        CancelSessionAudioFingerprint();
        _sessionAudioFingerprint = validatedAudioFingerprint ?? string.Empty;
        if (selectedSong is not null && string.IsNullOrWhiteSpace(_sessionAudioFingerprint))
        {
            string audioPath = selectedSong.FilePath;
            string songId = selectedSong.SongId;
            _sessionAudioFingerprintCancellation = new CancellationTokenSource();
            CancellationToken cancellationToken = _sessionAudioFingerprintCancellation.Token;
            // Hash during the three-second countdown so large WAV files do not
            // freeze the UI. SaveReplayRecord joins this task at session end.
            _sessionAudioFingerprintTask = BuildSessionAudioFingerprintAsync(audioPath, songId, cancellationToken);
        }
        LoadBgaForSong(selectedSong);
        _selectedChartNotes = replayPlayback && validatedReplayChart is not null
            ? validatedReplayChart
            : LoadShiftedChartForCurrentLaneMode(selectedSong);

        // 시작 후 3초간 노트 없이 준비 시간 확보
        _countdownSeconds = 3;

        _isCountdownActive = true;
        _countdownStartTime = DateTime.Now;

        EnterGameLowLatencyMode();
        _timer.Start();
    }

    private IReadOnlyList<LaneNote> LoadShiftedChartForCurrentLaneMode(SongEntry? selectedSong)
    {
        return NoteLane.LoadNotes(selectedSong?.Title, selectedSong?.Artist, _songSelectDifficultyIndex, LaneCount)
            .Select(n => new LaneNote(n.Time + GameStartDelaySeconds, n.Lane, n.Type, n.Duration, n.EndLane))
            .ToList();
    }

    private async void StartReplayForSelectedSong()
    {
        SongEntry? song = GetSelectedSong();
        if (song is null)
            return;

        CancelPendingReplayLoad();
        _replayLoadCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _replayLoadCancellation.Token;
        int requestGeneration = _replayLoadGeneration;
        string requestedSongId = song.SongId;
        int requestedDifficultyIndex = _songSelectDifficultyIndex;
        int requestedLaneCount = LaneCount;
        _feedback = "CHECKING REPLAY";
        _feedbackTime = DateTime.Now;
        Invalidate();

        IReadOnlyList<ReplayRecord> replayCandidates;
        try
        {
            replayCandidates = await Task.Run(() =>
                _replayStore.LoadCandidates(requestedSongId, requestedDifficultyIndex, requestedLaneCount, cancellationToken),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            if (!IsReplayRequestCurrent(requestGeneration, requestedSongId, requestedDifficultyIndex, requestedLaneCount))
                return;

            _feedback = "REPLAY CHECK FAILED";
            _feedbackTime = DateTime.Now;
            AppLogger.Error($"Failed to prepare replay for {requestedSongId}.", ex);
            Invalidate();
            return;
        }

        if (!IsReplayRequestCurrent(requestGeneration, requestedSongId, requestedDifficultyIndex, requestedLaneCount))
            return;

        if (replayCandidates.Count == 0)
        {
            _feedback = "NO REPLAY";
            _feedbackTime = DateTime.Now;
            Invalidate();
            return;
        }

        string currentAudioFingerprint;
        try
        {
            currentAudioFingerprint = await ReplayCompatibility.BuildAudioFingerprintAsync(song.FilePath, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            if (!IsReplayRequestCurrent(requestGeneration, requestedSongId, requestedDifficultyIndex, requestedLaneCount))
                return;

            _feedback = "REPLAY AUDIO CHECK FAILED";
            _feedbackTime = DateTime.Now;
            AppLogger.Error($"Failed to fingerprint replay audio for {requestedSongId}.", ex);
            Invalidate();
            return;
        }

        if (!IsReplayRequestCurrent(requestGeneration, requestedSongId, requestedDifficultyIndex, requestedLaneCount))
            return;

        ReplayRecord? replay = null;
        ReplayValidationResult validation = default;
        ReplayValidationResult newestFailure = default;
        bool hasNewestFailure = false;
        foreach (ReplayRecord candidate in replayCandidates)
        {
            validation = ReplayCompatibility.ValidateForPlayback(
                candidate,
                requestedSongId,
                requestedDifficultyIndex,
                requestedLaneCount,
                currentAudioFingerprint);
            if (!validation.CanPlay)
            {
                if (!hasNewestFailure)
                {
                    newestFailure = validation;
                    hasNewestFailure = true;
                }
                continue;
            }

            replay = candidate;
            break;
        }

        if (replay is null)
        {
            validation = hasNewestFailure ? newestFailure : validation;
            _feedback = validation.UserMessage;
            _feedbackTime = DateTime.Now;
            AppLogger.Info($"Replay blocked for {requestedSongId}: {validation.UserMessage}");
            Invalidate();
            return;
        }

        _activeReplay = replay;
        _replayEventIndex = 0;
        CancelPendingReplayLoad();
        BeginGame(
            replayPlayback: true,
            validatedReplayChart: replay.Chart,
            validatedAudioFingerprint: currentAudioFingerprint);
    }

    private bool IsReplayRequestCurrent(int generation, string songId, int difficultyIndex, int laneCount)
    {
        if (IsDisposed || generation != _replayLoadGeneration || _screen != UiScreen.SongSelect ||
            _isCountdownActive || _engine.IsRunning ||
            _songSelectDifficultyIndex != difficultyIndex || LaneCount != laneCount)
        {
            return false;
        }

        SongEntry? selectedSong = GetSelectedSong();
        return selectedSong is not null && string.Equals(selectedSong.SongId, songId, StringComparison.Ordinal);
    }

    private void CancelPendingReplayLoad()
    {
        _replayLoadGeneration++;
        _replayLoadCancellation?.Cancel();
        _replayLoadCancellation?.Dispose();
        _replayLoadCancellation = null;
    }

    private void CancelSessionAudioFingerprint()
    {
        _sessionAudioFingerprintCancellation?.Cancel();
        _sessionAudioFingerprintCancellation?.Dispose();
        _sessionAudioFingerprintCancellation = null;
        _sessionAudioFingerprintTask = null;
    }

    private static async Task<string> BuildSessionAudioFingerprintAsync(
        string audioPath,
        string songId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ReplayCompatibility.BuildAudioFingerprintAsync(audioPath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return string.Empty;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to fingerprint session audio for {songId}.", ex);
            return string.Empty;
        }
    }

    private bool ShouldEndAfterChartComplete()
    {
        if (_audio.IsInGameBgmFinished())
            return true;

        float waitSeconds = _audio.GetInGameBgmDurationSeconds().HasValue ? 1.2f : 1.8f;
        return (DateTime.Now - _chartCompleteTime).TotalSeconds >= waitSeconds;
    }

    private void UpdateReplayPlayback(float deltaTime, float? playbackPositionSeconds)
    {
        if (!_isReplayPlayback || _activeReplay is null)
            return;

        float startChartTime = _engine.CurrentChartTime;
        float targetChartTime = playbackPositionSeconds.HasValue && float.IsFinite(playbackPositionSeconds.GetValueOrDefault())
            ? Math.Max(startChartTime, playbackPositionSeconds.GetValueOrDefault() - _engine.AudioOffsetSeconds)
            : startChartTime + Math.Max(0f, deltaTime);
        List<InputLogEvent> events = _activeReplay.Events;
        while (_replayEventIndex < events.Count && events[_replayEventIndex].Time <= targetChartTime)
        {
            InputLogEvent input = events[_replayEventIndex++];
            if (input.Lane < 0 || input.Lane >= LaneCount)
                continue;

            // Advance only to the recorded event time first. Advancing to the
            // current audio sample before this input could auto-miss or complete
            // a note and make an otherwise deterministic replay frame-dependent.
            _engine.Update(0f, input.Time + _engine.AudioOffsetSeconds);
            if (!_engine.IsRunning)
                return;

            if (input.KeyDown)
                BeginLaneInput(input.Lane, input.Input, "replay", input.Time);
            else
                EndLaneInput(input.Lane, input.Input, "replay", input.Time);

            if (!_engine.IsRunning)
                return;
        }

        if (playbackPositionSeconds.HasValue && float.IsFinite(playbackPositionSeconds.GetValueOrDefault()))
            _engine.Update(deltaTime, playbackPositionSeconds);
        else
            _engine.Update(Math.Max(0f, targetChartTime - _engine.CurrentChartTime), null);
    }

    private void EndGame()
    {
        ReplayRecord? completedReplay = _isReplayPlayback ? _activeReplay : null;
        _gdiMonitor.Stop(_isReplayPlayback ? "replay" : "game");
        // Capture results before stopping engine
        _analyzeScore = _engine.Score.Score;
        _analyzeMaxCombo = _engine.Score.MaxCombo;
        _analyzePerfectCount = _engine.Score.PerfectCount;
        _analyzeGreatCount = _engine.Score.GreatCount;
        _analyzeBetterCount = _engine.Score.BetterCount;
        _analyzeGoodCount = _engine.Score.GoodCount;
        _analyzeBadCount = _engine.Score.BadCount;
        _analyzeMissCount = _engine.Score.MissCount;
        _analyzeMissStreak = _engine.Score.MaxMissStreak;
        _analyzeEarlyCount = _engine.Score.EarlyCount;
        _analyzeLateCount = _engine.Score.LateCount;
        _analyzeAverageTimingMs = (int)MathF.Round(_engine.Score.AverageTimingOffsetSeconds * 1000f);
        _analyzeAccuracy = _engine.Score.TotalJudgedNotes > 0 ? _engine.Score.Accuracy : 0f;
        _analyzeGrooveGauge = _grooveGauge;
        _analyzeGaugeClearThreshold = GetGaugeRule(_songSelectDifficultyIndex).ClearThreshold;
        _analyzePlayMode = PlayModeLabels[Math.Clamp(EffectivePlayModeIndex, 0, PlayModeLabels.Length - 1)];
        _analyzeClearType = GetSessionClearType();
        _analyzeGrade = ScoreManager.CalculateGrade(_analyzeAccuracy, _engine.Score.MissCount, _engine.Score.MaxCombo, _analyzeClearType);
        float chartStartTime = _selectedChartNotes.Count > 0 ? _selectedChartNotes.Min(note => note.Time) : 0f;
        float chartEndTime = _selectedChartNotes.Count > 0 ? _selectedChartNotes.Max(note => note.Time + Math.Max(0f, note.Duration)) : Math.Max(1f, _engine.CurrentChartTime);
        _analyzeFeedback = ResultFeedbackSummary.Create(
            _engine.Score,
            _engine.JudgmentHistory,
            chartStartTime,
            chartEndTime);
        if (_analyzeFeedback.RecordedMissCount != _engine.Score.MissCount)
            AppLogger.Info($"Judgment history mismatch: score misses={_engine.Score.MissCount}, recorded misses={_analyzeFeedback.RecordedMissCount}");
        _analyzeReplayStatus = completedReplay is null
            ? string.Empty
            : ReplayCompatibility.CompareResult(completedReplay, _engine.Score, _analyzeGrade, _analyzeClearType, _engine.JudgmentHistory);

        // Store song info
        SongEntry? song = GetSelectedSong();
        _analyzeSongId = song?.SongId ?? string.Empty;
        _analyzeSongTitle = song?.Title ?? "Unknown";
        _analyzeSongArtist = song is null ? "Unknown" : BuildSongMetadata(song, includeBest: true);
        _analyzeSongArtworkStyle = song?.ArtworkStyle ?? 0;
        int previousSongHighScore = song is null
            ? _playerProgress.HighestScore
            : SongData.TryGetScore(song.SongId)?.HighestScore ?? 0;
        _analyzeHighestScore = previousSongHighScore;
        _analyzeIsNewRecord = _analyzeScore > previousSongHighScore;

        SongScoreRecord? songScore = null;
        if (!_isReplayPlayback)
        {
            string replayPath = string.Empty;
            try
            {
                replayPath = SaveReplayRecord(song);
            }
            catch (Exception ex)
            {
                _analyzeReplayStatus = "REPLAY SAVE FAILED";
                AppLogger.Error("Failed to save replay during game cleanup.", ex);
            }

            try
            {
                songScore = RecordSongScore(song, replayPath);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to save song score during game cleanup.", ex);
            }

            try
            {
                RecordAchievementProgress(song);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to save achievement progress during game cleanup.", ex);
            }

            try
            {
                _inputLogStore.Save(_inputLogEvents);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to save input log during game cleanup.", ex);
            }
        }
        _engine.Stop();
        _isCountdownActive = false;
        _isGamePaused = false;
        _isReplayPlayback = false;
        _activeReplay = null;
        _replayEventIndex = 0;
        _sessionAudioFingerprint = string.Empty;
        CancelSessionAudioFingerprint();
        _gameBgaImage?.Dispose();
        _gameBgaImage = null;
        _gameBgaPath = string.Empty;
        Array.Clear(_lanePressed);
        Array.Clear(_pauseHeldLaneAwaitingKeyUp);
        _mouseHeldLane = -1;

        // Update highest score after recording
        _analyzeHighestScore = songScore?.HighestScore ?? Math.Max(_analyzeHighestScore, _analyzeScore);

        _isAnalyzeOkHovered = false;
        _screen = UiScreen.Analyze;
        _audio.StopAllSounds();
        ExitGameLowLatencyMode();
        if (!_timer.Enabled && HasPendingAchievementToast())
            _timer.Start();
        else if (!HasPendingAchievementToast())
            _timer.Stop();
        Invalidate();
    }

    private string SaveReplayRecord(SongEntry? song)
    {
        if (_isReplayPlayback || song is null || _inputLogEvents.Count == 0)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(_replayRecordingInvalidReason))
        {
            _analyzeReplayStatus = $"REPLAY NOT SAVED - {_replayRecordingInvalidReason}";
            AppLogger.Info($"Replay recording skipped for {song.SongId}: {_replayRecordingInvalidReason}");
            return string.Empty;
        }

        if (_playModeIndex == 2)
        {
            _analyzeReplayStatus = "REPLAY NOT SAVED - AUTO PLAY";
            AppLogger.Info($"Replay recording skipped for {song.SongId}: Auto play is timer-driven rather than input-driven.");
            return string.Empty;
        }

        if (_selectedChartNotes.Count == 0)
        {
            _analyzeReplayStatus = "REPLAY NOT SAVED - NO FIXED CHART";
            AppLogger.Info($"Replay recording skipped for {song.SongId}: no fixed chart was played.");
            return string.Empty;
        }

        string difficulty = GetDifficultyLabel(_songSelectDifficultyIndex);
        if (string.IsNullOrWhiteSpace(_sessionAudioFingerprint) && _sessionAudioFingerprintTask is not null)
        {
            try
            {
                _sessionAudioFingerprint = _sessionAudioFingerprintTask.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Failed to fingerprint session audio for {song.SongId}.", ex);
            }
        }

        if (string.IsNullOrWhiteSpace(_sessionAudioFingerprint))
        {
            _analyzeReplayStatus = "REPLAY NOT SAVED - AUDIO CHECK FAILED";
            AppLogger.Info($"Replay recording skipped for {song.SongId}: audio fingerprint was unavailable at session start.");
            return string.Empty;
        }

        return _replayStore.Save(new ReplayRecord
        {
            ReplayVersion = ReplayCompatibility.CurrentReplayVersion,
            GameVersion = ReplayCompatibility.CurrentGameVersion,
            ChartVersion = BuildChartVersion(song),
            AudioFingerprint = _sessionAudioFingerprint,
            SongId = song.SongId,
            SongTitle = song.Title,
            Artist = song.Artist,
            DifficultyIndex = _songSelectDifficultyIndex,
            Difficulty = difficulty,
            LaneCount = LaneCount,
            AudioOffsetMs = _audioOffsetMs,
            SpeedMultiplier = _speedMultiplier,
            PlayedUtc = DateTime.UtcNow.ToString("O"),
            Score = _analyzeScore,
            Accuracy = _analyzeAccuracy,
            Grade = ScoreManager.FormatGrade(_analyzeGrade),
            ClearType = ScoreManager.FormatClearType(_analyzeClearType),
            Settings = new ReplaySettingsSnapshot
            {
                AudioOffsetMs = _audioOffsetMs,
                NoteSpeedPercent = (int)MathF.Round(_speedMultiplier * 100f),
                PlayModeIndex = _playModeIndex,
                GameModeIndex = (int)_gameMode,
                LaneCount = LaneCount,
            },
            Result = new ReplayResultSnapshot
            {
                Score = _analyzeScore,
                Accuracy = _analyzeAccuracy,
                Grade = ScoreManager.FormatGrade(_analyzeGrade),
                ClearType = ScoreManager.FormatClearType(_analyzeClearType),
                PerfectCount = _analyzePerfectCount,
                GreatCount = _analyzeGreatCount,
                BetterCount = _analyzeBetterCount,
                GoodCount = _analyzeGoodCount,
                BadCount = _analyzeBadCount,
                MissCount = _analyzeMissCount,
                MaxCombo = _analyzeMaxCombo,
                MaxMissStreak = _analyzeMissStreak,
            },
            Chart = _selectedChartNotes.ToList(),
            Events = _inputLogEvents.ToList(),
            Judgments = _engine.JudgmentHistory.ToList(),
        });
    }

    private string BuildChartVersion(SongEntry song, IReadOnlyList<LaneNote>? chartNotes = null)
    {
        return ReplayCompatibility.BuildChartVersion(
            song.SongId,
            _songSelectDifficultyIndex,
            LaneCount,
            chartNotes ?? _selectedChartNotes);
    }

    private SongScoreRecord? RecordSongScore(SongEntry? song, string replayPath)
    {
        if (song is null)
            return null;

        SongScoreRecord record = SongData.RecordScore(
            song.ToMetadata(),
            _songSelectDifficultyIndex,
            _analyzeScore,
            _analyzeMaxCombo,
            _analyzeAccuracy,
            _analyzeGrade,
            _analyzeClearType,
            _analyzeMissStreak,
            LaneCount,
            _analyzePerfectCount,
            _analyzeGreatCount,
            _analyzeBetterCount,
            _analyzeGoodCount,
            _analyzeBadCount,
            _analyzeMissCount,
            replayPath);
        InvalidateSongCache();
        return record;
    }

    private void RecordAchievementProgress(SongEntry? song)
    {
        GameSessionSummary session = new(
            _engine.Score.Score,
            _engine.Score.MaxCombo,
            _engine.Score.PerfectCount,
            _engine.Score.GreatCount,
            _engine.Score.BetterCount,
            _engine.Score.GoodCount,
            _engine.Score.BadCount,
            _engine.Score.MissCount,
            _engine.Score.MaxMissStreak,
            _analyzeAccuracy,
            _analyzeGrade,
            _analyzeClearType,
            song?.SongId ?? string.Empty,
            _songSelectDifficultyIndex,
            LaneCount,
            song?.Bpm ?? 0f,
            song?.DurationSeconds ?? 0f,
            DateTime.Now.ToString("yyyy-MM-dd"));

        if (!session.HasPlayableData)
            return;

        List<AchievementDefinition> unlocked = AchievementCatalog.ApplySession(_playerProgress, session);
        _achievementStore.Save(_playerProgress);
        EnqueueAchievementToasts(unlocked);
    }

    private GaugeRule GetGaugeRule(int difficultyIndex)
    {
        return difficultyIndex switch
        {
            0 => new GaugeRule(78f, 30f, 2.2f, 1.8f, 1.2f, 0.7f, 4.0f, 8.0f),
            2 => new GaugeRule(64f, 70f, 1.3f, 1.0f, 0.7f, 0.2f, 8.0f, 16.0f),
            _ => new GaugeRule(70f, 50f, 1.8f, 1.4f, 1.0f, 0.4f, 6.0f, 12.0f),
        };
    }

    private bool IsPracticeMode => EffectivePlayModeIndex == (int)PlayMode.Practice;

    private bool IsAutoPlayMode => EffectivePlayModeIndex == (int)PlayMode.Auto;

    private bool IsNoFailPlayMode => IsPracticeMode || IsAutoPlayMode;

    private bool ShouldFailByGauge()
    {
        return !IsNoFailPlayMode && (_gameFailedByGauge || _grooveGauge < GetGaugeRule(_songSelectDifficultyIndex).ClearThreshold);
    }

    private ClearType GetSessionClearType()
    {
        ClearType baseClearType = _engine.Score.ClearType;
        if (IsNoFailPlayMode)
            return baseClearType == ClearType.Failed && _engine.Score.TotalJudgedNotes > 0
                ? ClearType.Clear
                : baseClearType;

        return ShouldFailByGauge() ? ClearType.Failed : baseClearType;
    }

    private bool IsGaugeDanger()
    {
        if (IsNoFailPlayMode || !_engine.IsRunning)
            return false;

        GaugeRule rule = GetGaugeRule(_songSelectDifficultyIndex);
        return _grooveGauge <= Math.Max(22f, rule.ClearThreshold + 10f);
    }

    private void ApplyGaugeForHitResult(GameEngine.HitResult hit)
    {
        if (hit.Label.StartsWith("MISS", StringComparison.OrdinalIgnoreCase))
            return;

        ApplyGaugeJudgment(hit.Judgment);
    }

    private void ConsumeEngineGaugeEvents()
    {
        foreach (Judgment judgment in _engine.ConsumePendingAutoJudgments())
            ApplyGaugeJudgment(judgment);

        int misses = _engine.ConsumePendingMisses();
        for (int i = 0; i < misses; i++)
            ApplyGaugeMiss();
    }

    private void UpdateComboMilestone()
    {
        int combo = _engine.Score.Combo;
        if (combo <= 0)
        {
            _lastComboMilestone = 0;
            return;
        }

        int milestone = combo switch
        {
            >= 200 when combo % 100 == 0 => combo,
            100 => 100,
            50 => 50,
            _ => 0,
        };

        if (milestone <= 0 || milestone == _lastComboMilestone)
            return;

        _lastComboMilestone = milestone;
        _comboMilestoneText = $"{milestone} COMBO";
        _comboMilestoneTime = DateTime.Now;
    }

    private void ApplyGaugeJudgment(Judgment judgment)
    {
        GaugeRule rule = GetGaugeRule(_songSelectDifficultyIndex);
        float delta = judgment switch
        {
            Judgment.Perfect => rule.PerfectGain,
            Judgment.Great => rule.GreatGain,
            Judgment.Better => rule.BetterGain,
            Judgment.Good => rule.GoodGain,
            Judgment.Bad => -rule.BadLoss,
            _ => 0f,
        };

        ApplyGaugeDelta(delta);
    }

    private void ApplyGaugeMiss()
    {
        ApplyGaugeDelta(-GetGaugeRule(_songSelectDifficultyIndex).MissLoss);
    }

    private void ApplyGaugeDelta(float delta)
    {
        _grooveGauge = Math.Clamp(_grooveGauge + delta, 0f, 100f);
        if (!IsNoFailPlayMode && _grooveGauge <= 0f)
            _gameFailedByGauge = true;
    }

    private void MaintainAutoPlayLaneHolds()
    {
        if (!IsAutoPlayMode || !_engine.IsRunning || _isReplayPlayback)
            return;

        Span<bool> held = stackalloc bool[7];
        float chartTime = _engine.CurrentChartTime;
        for (int i = 0; i < _engine.Notes.Count; i++)
        {
            Note note = _engine.Notes[i];
            if (note.State != NoteState.Holding)
                continue;

            int lane = GetAutoPlayRequiredLane(note, chartTime);
            if (lane >= 0 && lane < held.Length)
                held[lane] = true;
        }

        for (int lane = 0; lane < LaneCount; lane++)
        {
            _lanePressed[lane] = held[lane];
            _engine.SetLaneHeld(lane, held[lane]);
        }
    }

    private void ProcessAutoPlayMode()
    {
        if (!IsAutoPlayMode || !_engine.IsRunning || _isReplayPlayback)
            return;

        float chartTime = _engine.CurrentChartTime;
        Note[] dueNotes = _engine.Notes
            .Where(note => note.State == NoteState.Active &&
                           chartTime >= note.TargetTime &&
                           chartTime - note.TargetTime <= GameEngine.BadWindow)
            .OrderBy(note => note.TargetTime)
            .ThenBy(note => note.Lane)
            .ToArray();

        foreach (Note note in dueNotes)
        {
            int lane = Math.Clamp(note.Lane, 0, LaneCount - 1);
            _lanePressed[lane] = true;
            _engine.SetLaneHeld(lane, true);

            GameEngine.HitResult? hit = _engine.TryHit(lane);
            if (hit is not null)
            {
                _feedback = FormatFeedback(hit.Value);
                _feedbackTiming = hit.Value.TimingLabel;
                _feedbackJudgment = hit.Value.Judgment;
                _feedbackTime = DateTime.Now;
                ApplyGaugeForHitResult(hit.Value);
            }
        }

        MaintainAutoPlayLaneHolds();
    }

    private static int GetAutoPlayRequiredLane(Note note, float chartTime)
    {
        if (note.Type != NoteType.Slide || note.Duration <= 0f)
            return note.Lane;

        float progress = Math.Clamp((chartTime - note.TargetTime) / note.Duration, 0f, 1f);
        return progress < 0.5f ? note.Lane : note.EndLane;
    }

    private void CancelCountdown()
    {
        _isCountdownActive = false;
        _isGamePaused = false;
        ClearReplayPlaybackState();
        Array.Clear(_lanePressed);
        Array.Clear(_pauseHeldLaneAwaitingKeyUp);
        _mouseHeldLane = -1;
        _audio.StopAllSounds();
        _audio.PlayMainScreenBgm();
        ExitGameLowLatencyMode();
        _timer.Stop();
        _screen = UiScreen.MainMenu;
    }

    private void ClearReplayPlaybackState()
    {
        _isReplayPlayback = false;
        _activeReplay = null;
        _replayEventIndex = 0;
        _sessionAudioFingerprint = string.Empty;
        CancelSessionAudioFingerprint();
    }

    private void PauseGame()
    {
        if (!_engine.IsRunning || _isGamePaused)
            return;

        if (!_audio.PauseInGameBgm())
        {
            _feedback = "PAUSE FAILED";
            _feedbackTiming = "AUDIO DEVICE";
            _feedbackJudgment = null;
            _feedbackTime = DateTime.Now;
            return;
        }

        _isGamePaused = true;
        _frameStopwatch.Restart();
        if (!_isReplayPlayback)
        {
            for (int i = 0; i < _pauseHeldLaneAwaitingKeyUp.Length; i++)
                _pauseHeldLaneAwaitingKeyUp[i] = _lanePressed[i];
            Array.Clear(_lanePressed);
            _mouseHeldLane = -1;
            for (int i = 0; i < 7; i++)
                _engine.SetLaneHeld(i, false);
        }
        Invalidate();
    }

    private void ResumeGame()
    {
        if (!_engine.IsRunning || !_isGamePaused)
            return;

        if (!_audio.ResumeInGameBgm())
        {
            _feedback = "RESUME FAILED";
            _feedbackTiming = "AUDIO DEVICE";
            _feedbackJudgment = null;
            _feedbackTime = DateTime.Now;
            Invalidate();
            return;
        }

        _isGamePaused = false;
        _frameStopwatch.Restart();
        if (!_isReplayPlayback)
            _engine.GrantHoldResumeGrace();
        Invalidate();
    }

    private void CycleLaneModeForward()
    {
        if (_engine.IsRunning || _isCountdownActive)
            return;

        _laneModeIndex = (_laneModeIndex + 1) % LaneModes.Length;
        SaveUserSettings();
    }

    private void CycleLaneModeBackward()
    {
        if (_engine.IsRunning || _isCountdownActive)
            return;

        _laneModeIndex = (_laneModeIndex - 1 + LaneModes.Length) % LaneModes.Length;
        SaveUserSettings();
    }

    private void SwitchLaneModeToCount(int laneCount)
    {
        int targetIndex = Array.FindIndex(LaneModes, mode => mode.Count == laneCount);
        if (targetIndex < 0 || targetIndex == _laneModeIndex)
            return;

        if (_isReplayPlayback)
        {
            _feedback = "LANE LOCKED";
            _feedbackTiming = "REPLAY";
            _feedbackJudgment = null;
            _feedbackTime = DateTime.Now;
            return;
        }

        if (_engine.IsRunning)
            InvalidateReplayRecording("LANE MODE CHANGED");

        _laneModeIndex = targetIndex;
        Array.Clear(_lanePressed);
        _mouseHeldLane = -1;
        for (int i = 0; i < 7; i++)
            _engine.SetLaneHeld(i, false);

        SongEntry? selectedSong = _screen == UiScreen.SongSelect ? GetSelectedSong() : null;
        if (_engine.IsRunning)
        {
            _selectedChartNotes = LoadShiftedChartForCurrentLaneMode(selectedSong);
            _engine.SwitchLaneMode(LaneCount, _selectedChartNotes);
            _feedback = $"{LaneCount}K";
            _feedbackTiming = "LANE MODE";
            _feedbackJudgment = null;
            _feedbackTime = DateTime.Now;
        }
        else if (_isCountdownActive)
        {
            _selectedChartNotes = LoadShiftedChartForCurrentLaneMode(selectedSong);
        }

        SaveUserSettings();
    }

    private void InvalidateReplayRecording(string reason)
    {
        if (!_engine.IsRunning || _isReplayPlayback || !string.IsNullOrWhiteSpace(_replayRecordingInvalidReason))
            return;

        _replayRecordingInvalidReason = reason;
        AppLogger.Info($"Replay recording invalidated: {reason}");
    }

    // ── 렌더링 ────────────────────────────────────────────────────────────────
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        bool inGame = _engine.IsRunning || _isCountdownActive;
        UpdateAccessibleContext(inGame);
        ApplyRenderingQuality(g, inGame);
        g.Clear(inGame ? Color.FromArgb(10, 14, 24) : ClearColor);

        if (_screen == UiScreen.Splash)
        {
            DrawSplash(g);
            return;
        }

        if (_isCountdownActive)
        {
            DrawCountdown(g);
            FlushDwmIfEnabled();
            return;
        }

        if (_engine.IsRunning)
        {
            DrawGame(g);
            FlushDwmIfEnabled();
            return;
        }

        var state = g.Save();
        g.TranslateTransform(_layoutOffsetX, _layoutOffsetY);
        if (_screen == UiScreen.Settings) DrawSettings(g);
        else if (_screen == UiScreen.SongSelect) DrawSongSelect(g);
        else if (_screen == UiScreen.SongDetail) DrawSongDetail(g);
        else if (_screen == UiScreen.AchievementDetail) DrawAchievementDetail(g);
        else if (_screen == UiScreen.Achievement) DrawAchievement(g);
        else if (_screen == UiScreen.Analyze) DrawAnalyze(g);
        else if (_screen == UiScreen.InputCalibration) DrawInputCalibration(g);
        else if (_screen == UiScreen.KeyBindings) DrawKeyBindings(g);
        else if (_screen == UiScreen.ChartEditor) DrawChartEditor(g);
        else if (_screen == UiScreen.MainMenu) DrawMenu(g);
        DrawKeyboardFocus(g, clientCoordinates: false);

        FlushDwmIfEnabled();
        g.Restore(state);

        if (_activeAchievementToast is not null)
            DrawAchievementToast(g, _activeAchievementToast);
    }

    private void ApplyRenderingQuality(Graphics g, bool inGame)
    {
        if (_renderQualityMode == 0 || inGame)
        {
            g.SmoothingMode = SmoothingMode.HighSpeed;
            g.InterpolationMode = InterpolationMode.Low;
            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
            g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SystemDefault;
            return;
        }

        if (_renderQualityMode == 2)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.AssumeLinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SystemDefault;
    }

    private void UpdateAccessibleContext(bool inGame)
    {
        string screenName = inGame ? "In Game" : _screen.ToString();
        AccessibleName = $"Rhythm Game - {screenName}";
        AccessibleDescription = screenName switch
        {
            "Splash" => "Splash screen. Press any key or click to start.",
            "Settings" => "Settings screen. Press Tab to move between controls, Enter or Space to activate, Left or Right to adjust sliders and segmented controls.",
            "InputCalibration" => "Input latency calibration screen. Press keys to match the metronome.",
            "KeyBindings" => "Key binding screen. Assign lane keys and test simultaneous input.",
            "SongSelect" => "Song selection screen. Press Tab to move through search, songs, difficulty, controls, and play.",
            "Analyze" => "Results screen. Review score, timing, miss causes, and choose Retry, Song Select, or Next.",
            "Achievement" => "Player statistics screen. Review recent performance and press Tab to open details, settings, or return home.",
            "AchievementDetail" => "Achievement detail screen. Press Tab to choose tabs, pages, or back.",
            "In Game" => "Gameplay screen. Use lane keys to hit notes. Press Escape or P to pause.",
            _ => "Main menu. Open settings, song select, or achievements.",
        };
    }

    private void EnqueueAchievementToasts(IEnumerable<AchievementDefinition> unlockedAchievements)
    {
        foreach (AchievementDefinition unlocked in unlockedAchievements)
            _achievementToastQueue.Enqueue(unlocked);

        if (_activeAchievementToast is null && _achievementToastQueue.Count > 0)
            StartNextAchievementToast(DateTime.Now);
    }

    private bool UpdateAchievementToast(DateTime now)
    {
        if (_activeAchievementToast is null)
        {
            if (_achievementToastQueue.Count == 0)
                return false;

            StartNextAchievementToast(now);
            return true;
        }

        if (now < _achievementToastUntil)
            return true;

        if (_achievementToastQueue.Count > 0)
        {
            StartNextAchievementToast(now);
            return true;
        }

        _activeAchievementToast = null;
        return false;
    }

    private void StartNextAchievementToast(DateTime now)
    {
        _activeAchievementToast = _achievementToastQueue.Dequeue();
        _achievementToastStartTime = now;
        _achievementToastUntil = now.AddSeconds(3.2);
    }

    private bool HasPendingAchievementToast()
    {
        return _activeAchievementToast is not null || _achievementToastQueue.Count > 0;
    }

    private void DrawAchievementToast(Graphics g, AchievementDefinition achievement)
    {
        float elapsed = (float)(DateTime.Now - _achievementToastStartTime).TotalSeconds;
        float total = (float)(_achievementToastUntil - _achievementToastStartTime).TotalSeconds;
        float fadeIn = Math.Clamp(elapsed / 0.28f, 0f, 1f);
        float fadeOut = (float)Math.Clamp((_achievementToastUntil - DateTime.Now).TotalSeconds / 0.35, 0d, 1d);
        float opacity = Math.Min(fadeIn, fadeOut <= 0f ? 1f : fadeOut);
        float slide = _reducedMotionEnabled ? 0f : (1f - fadeIn) * 18f;

        int width = (int)Math.Round(ScaleX(292f));
        int height = (int)Math.Round(ScaleY(86f));
        int x = ClientSize.Width - width - (int)Math.Round(ScaleX(28f));
        int y = (int)Math.Round(_layoutOffsetY + ScaleY(28f) - slide);
        Rectangle bounds = new(x, y, width, height);

        int alpha = (int)Math.Round(255 * opacity);
        using var shadowPath = CreateRoundedRect(new Rectangle(bounds.X, bounds.Y + (int)Math.Round(ScaleY(7f)), bounds.Width, bounds.Height), ScaleY(24f));
        using var shadowBrush = new SolidBrush(Color.FromArgb((int)Math.Round(28 * opacity), 42, 74, 120));
        using var cardPath = CreateRoundedRect(bounds, ScaleY(24f));
        using var fillBrush = new LinearGradientBrush(bounds, Color.FromArgb(alpha, 255, 255, 255), Color.FromArgb(alpha, 236, 244, 253), LinearGradientMode.Vertical);
        using var borderPen = new Pen(Color.FromArgb((int)Math.Round(190 * opacity), GetAccentColor()), Math.Max(1.2f, ScaleY(1.5f)));
        using var titleFont = new Font("Segoe UI", Math.Max(9.5f, ScaleY(16f)), FontStyle.Bold);
        using var bodyFont = new Font("Malgun Gothic", Math.Max(7.5f, ScaleY(11f)), FontStyle.Bold);
        using var titleBrush = new SolidBrush(Color.FromArgb(alpha, 60, 104, 164));
        using var bodyBrush = new SolidBrush(Color.FromArgb(alpha, 112, 133, 162));

        g.FillPath(shadowBrush, shadowPath);
        g.FillPath(fillBrush, cardPath);
        g.DrawPath(borderPen, cardPath);

        Rectangle iconBounds = new(bounds.Left + (int)Math.Round(ScaleX(18f)), bounds.Top + (int)Math.Round(ScaleY(18f)), (int)Math.Round(ScaleX(44f)), (int)Math.Round(ScaleY(44f)));
        DrawAchievementBadge(g, iconBounds, achievement, true, opacity);
        g.DrawString("업적 해제", bodyFont, bodyBrush, bounds.Left + ScaleX(76f), bounds.Top + ScaleY(16f));

        // Title text - fit within toast bounds
        float titleLeft = bounds.Left + ScaleX(76f);
        float titleTop = bounds.Top + ScaleY(36f);
        float titleMaxWidth = bounds.Right - titleLeft - ScaleX(12f);
        RectangleF titleRect = new(titleLeft, titleTop, titleMaxWidth, bounds.Bottom - titleTop - ScaleY(6f));
        using var titleFormat = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        g.DrawString(achievement.Title, titleFont, titleBrush, titleRect, titleFormat);
    }

    // ── 스플래시 화면 ─────────────────────────────────────────────────────────
    private void TransitionFromSplash()
    {
        _screen = UiScreen.MainMenu;
        _splashTimer.Stop();
        Invalidate();
    }

    private void DrawSplash(Graphics g)
    {
        int w = ClientSize.Width;
        int h = ClientSize.Height;
        float elapsed = (float)(DateTime.Now - _splashStartTime).TotalSeconds;

        // 배경: 밝은 흰색 그라데이션
        using (var bgBrush = new LinearGradientBrush(
            new Point(0, 0), new Point(0, h),
            Color.FromArgb(6, 10, 20),
            Color.FromArgb(16, 24, 42)))
        {
            g.FillRectangle(bgBrush, 0, 0, w, h);
        }

        // 웨이브 영역 중심 Y (화면 하단 58% 부근)
        float waveCenterY = h * 0.58f;

        // 여러 겹의 동적 웨이브 라인
        if (!_reducedMotionEnabled)
            DrawSplashWaves(g, w, h, waveCenterY, elapsed);

        // 빛나는 파티클
        if (!_reducedMotionEnabled)
            DrawSplashParticles(g, w, h, waveCenterY, elapsed);

        // "RHYTHM BEAT" 제목
        float titleFontSize = Math.Max(28f, Math.Min(w, h) * 0.062f);
        using var titleFont = new Font("Segoe UI", titleFontSize, FontStyle.Bold);
        string title1 = "RHYTHM";
        string title2 = "BEAT";
        var sz1 = g.MeasureString(title1, titleFont);
        var sz2 = g.MeasureString(title2, titleFont);
        float titleX = w / 2f;
        float titleY1 = h * 0.33f;
        float titleY2 = titleY1 + sz1.Height * 0.85f;
        using var titleBrush = new SolidBrush(GetAccentColor());
        g.DrawString(title1, titleFont, titleBrush, titleX - sz1.Width / 2f, titleY1);
        g.DrawString(title2, titleFont, titleBrush, titleX - sz2.Width / 2f, titleY2);

        // 하단 안내 텍스트 (깜빡임)
        float blink = _reducedMotionEnabled ? 0.8f : (float)(Math.Sin(elapsed * 3.0) * 0.5 + 0.5);
        int alpha = (int)(80 + 175 * blink);
        using var hintFont = new Font("Segoe UI", Math.Max(10f, h * 0.018f), FontStyle.Regular);
        using var hintBrush = new SolidBrush(Color.FromArgb(alpha, 170, 190, 225));
        string hint = "Press any key or click to start";
        var hintSz = g.MeasureString(hint, hintFont);
        g.DrawString(hint, hintFont, hintBrush, w / 2f - hintSz.Width / 2f, h * 0.82f);
    }

    private static void DrawSplashWaves(Graphics g, int w, int h, float centerY, float time)
    {
        float scale = Math.Min(w, h) / 768f;

        for (int wi = 0; wi < _splashWaveParams.Length; wi++)
        {
            var (amp, freq, speed, _, thickness, yOff) = _splashWaveParams[wi];
            float a = amp * scale;
            float t = thickness * scale;

            // 캐시된 펜의 두께만 갱신 (스케일 변경 시)
            var pen = _splashWavePens[wi];
            pen.Width = Math.Max(1f, t);

            var points = new PointF[w / 3 + 2];
            for (int i = 0; i < points.Length; i++)
            {
                float x = i * 3f;
                float phase = time * speed;
                float y = centerY + yOff * scale
                    + (float)(Math.Sin(x * freq + phase) * a)
                    + (float)(Math.Sin(x * freq * 1.7 + phase * 0.8 + 1.2) * a * 0.35)
                    + (float)(Math.Sin(x * freq * 0.5 + phase * 1.3 + 2.8) * a * 0.2);
                points[i] = new PointF(x, y);
            }

            if (points.Length >= 2)
                g.DrawCurve(pen, points, 0.4f);
        }

        // 웨이브 영역에 반투명 글로우
        using var glowBrush = new LinearGradientBrush(
            new PointF(0, centerY - 80 * scale),
            new PointF(0, centerY + 80 * scale),
            Color.FromArgb(0, 255, 255, 255),
            Color.FromArgb(0, 255, 255, 255));

        var blend = new ColorBlend(5);
        blend.Positions = [0f, 0.3f, 0.5f, 0.7f, 1f];
        blend.Colors =
        [
            Color.FromArgb(0,  0, 180, 255),
            Color.FromArgb(28, 0, 220, 255),
            Color.FromArgb(48, 255, 230, 90),
            Color.FromArgb(28, 0, 220, 255),
            Color.FromArgb(0,  0, 180, 255),
        ];
        glowBrush.InterpolationColors = blend;
        g.FillRectangle(glowBrush, 0, centerY - 80 * scale, w, 160 * scale);
    }

    private static void DrawSplashParticles(Graphics g, int w, int h, float centerY, float time)
    {
        int particleCount = 28;
        float scale = Math.Min(w, h) / 768f;

        for (int i = 0; i < particleCount; i++)
        {
            double hash1 = Math.Sin(i * 127.1 + 311.7) * 43758.5453;
            double hash2 = Math.Sin(i * 269.5 + 183.3) * 43758.5453;
            double hash3 = Math.Sin(i * 419.2 + 371.9) * 43758.5453;

            float px = (float)((hash1 - Math.Floor(hash1)) * w);
            float py = centerY + (float)((hash2 - Math.Floor(hash2)) - 0.5) * 160 * scale;
            float baseSize = (float)((hash3 - Math.Floor(hash3)) * 4 + 2) * scale;

            float twinkle = (float)(Math.Sin(time * (1.5 + i * 0.3) + i * 0.7) * 0.5 + 0.5);
            int pAlpha = (int)(40 + 160 * twinkle);
            float size = baseSize * (0.6f + 0.4f * twinkle);

            // 재사용 브러시 — Color만 갱신
            _particleBrush.Color = Color.FromArgb(pAlpha, 255, 255, 255);
            g.FillEllipse(_particleBrush, px - size / 2f, py - size / 2f, size, size);

            if (twinkle > 0.7f)
            {
                float glowSize = size * 3f;
                int glowAlpha = (int)(20 * twinkle);
                _particleGlowBrush.Color = Color.FromArgb(glowAlpha, 200, 220, 255);
                g.FillEllipse(_particleGlowBrush, px - glowSize / 2f, py - glowSize / 2f, glowSize, glowSize);
            }
        }
    }

    // ── 메뉴 화면 ─────────────────────────────────────────────────────────────
    private void DrawMenu(Graphics g)
    {
        Rectangle layoutRect = MenuRect(0f, 0f, MainMenuDesignWidth, MainMenuDesignHeight);
        DrawMainMenuBackground(g, layoutRect);

        Color accent = UseHighContrast ? Color.White : Color.FromArgb(105, 166, 255);
        using var brandFont = new Font("Segoe UI", Math.Max(8f, MenuS(15f)), FontStyle.Regular);
        using var titleFont = new Font("Segoe UI Light", Math.Max(40f, MenuS(76f)), FontStyle.Regular);
        using var tagFont = new Font("Segoe UI", Math.Max(8.5f, MenuS(17f)), FontStyle.Regular);
        using var menuFont = new Font("Segoe UI", Math.Max(13f, MenuS(26f)), FontStyle.Regular);
        using var smallFont = new Font("Segoe UI", Math.Max(7.5f, MenuS(13f)), FontStyle.Regular);
        using var textBrush = new SolidBrush(Color.FromArgb(236, 242, 255));
        using var dimBrush = new SolidBrush(Color.FromArgb(182, 190, 210));
        using var blueBrush = new SolidBrush(accent);

        DrawRhythmBrand(g, MenuX(32f), MenuY(30f), brandFont, dimBrush);
        DrawMenuGearButton(g, GetMenuTopSettingsButtonBounds(), _hoverMenuIndex == 0, accent);

        float centerX = MenuX(MainMenuDesignWidth / 2f);
        DrawGlowingSpacedText(g, "MuWorld", titleFont, textBrush, centerX, MenuY(205f), MenuS(17f));
        DrawMenuTagline(g, centerX, MenuY(315f) + 5f, tagFont, dimBrush, accent);

        DrawPlayMenuButton(g, GetMenuActionButtonBounds(1), _hoverMenuIndex == 1, menuFont, accent);
        DrawSecondaryMenuRow(g, GetMenuActionButtonBounds(3), "RESTART", _hoverMenuIndex == 3, menuFont, accent, DrawRestartGlyph);
        DrawPlayerBadge(g, _hoverMenuIndex == 2, smallFont, dimBrush, blueBrush);
        DrawQuitHint(g, GetExitButtonBounds(), _isExitHovered, smallFont, dimBrush);
    }

    private void DrawMenuSummary(Graphics g, Font labelFont, Font bodyFont, Brush textBrush)
    {
        SongEntry[] songs = DiscoverSongs();
        int bestScore = songs.Length == 0 ? 0 : songs.Max(s => s.HighestScore);
        string selected = songs.Length == 0
            ? "No songs loaded"
            : $"{songs.Length} songs  |  Best {bestScore:N0}  |  Lane {LaneCount}K";
        ChartGenerator.ChartGenerationSnapshot chartStatus = ChartGenerator.GetStatus();
        if (chartStatus.IsRunning)
            selected += $"  |  Charts {chartStatus.ProcessedSongs}/{chartStatus.TotalSongs}";
        else if (chartStatus.GeneratedCharts > 0 || chartStatus.SkippedSongs > 0)
            selected += $"  |  {chartStatus.LastMessage}";
        DrawCentered(g, selected, bodyFont, textBrush, (int)ScaleX(DesignWidth / 2f), (int)ScaleY(710f));
        DrawCentered(g, "Start opens Song Select. Detailed song records stay inside Song Select.", labelFont, textBrush, (int)ScaleX(DesignWidth / 2f), (int)ScaleY(732f));
    }

    // ── 인게임 화면 ───────────────────────────────────────────────────────────
    private void DrawGame(Graphics g)
    {
        long allocationStart = GC.GetAllocatedBytesForCurrentThread();
        var state = g.Save();
        if (_renderQualityMode > 0)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        }

        var playArea = GetPlayAreaBounds();
        int w    = ClientSize.Width;
        int h    = ClientSize.Height;
        int hitY = (int)MathF.Round(GameEngine.GetHitZoneY(h));
        int laneWidth = playArea.Width / LaneCount;

        // ── 배경: 어두운 그라데이션 ──
        DrawGameplayBackground(g, w, h, playArea);
        DrawPerspectiveLaneHighlights(g, playArea, hitY);

        // ── 피아노 키 스타일 히트존 ──
        DrawPianoKeys(g, playArea, hitY, laneWidth);

        // ── 노트 그리기 (for loop — 열거자 할당 방지) ──
        var notes = _engine.Notes;
        for (int i = 0; i < notes.Count; i++)
        {
            var note = notes[i];
            if (note.State is not (NoteState.Active or NoteState.Holding or NoteState.Hit or NoteState.Miss)) continue;
            DrawStyledNote(g, note, playArea, hitY);
        }

        // ── 게임 모드 효과 (블라인드/안개) ──
        ApplyGameModeEffect(g, playArea, hitY);

        // ── 콤보 & 정확도 HUD ──
        DrawGameHudDeck(g, playArea, hitY);
        DrawComboMilestone(g, playArea, hitY);

        // ── 배속/모드 인디케이터 ──
        DrawGaugeDangerOverlay(g, playArea, hitY);

        // ── 판정 피드백 ──
        if (_feedback is not null)
        {
            float fbElapsed = (float)(DateTime.Now - _feedbackTime).TotalMilliseconds;
            float feedbackDuration = GetJudgmentFeedbackDuration(_feedbackJudgment);
            if (fbElapsed < feedbackDuration)
            {
                float prog  = fbElapsed / feedbackDuration;
                int   alpha = (int)(255 * (1f - prog));
                float rise  = prog * (_feedbackJudgment == Judgment.Bad ? 8f : 24f);
                // 첫 글자로 빠르게 분기 (문자열 비교 제거)
                int shake = _feedbackJudgment == Judgment.Bad && !_reducedMotionEnabled
                    ? (int)(MathF.Sin(prog * 42f) * ScaleX(5f) * (1f - prog))
                    : 0;
                Color baseFeedback = GetJudgmentAccessibleColor(_feedbackJudgment);
                Color fc = Color.FromArgb(alpha, baseFeedback);
                _reusableFbBrush.Color = fc;
                Font feedbackFont = _renderResources.Font("Segoe UI", Math.Max(12f, GetJudgmentFeedbackFontSize(_feedbackJudgment) * _layoutScale), FontStyle.Bold);
                Point feedbackAnchor = GetJudgmentFeedbackAnchor(playArea, hitY);
                DrawCentered(g, _feedback, feedbackFont, _reusableFbBrush, feedbackAnchor.X + shake, feedbackAnchor.Y - (int)rise);
                if (!string.IsNullOrWhiteSpace(_feedbackTiming))
                {
                    Font timingFont = _renderResources.Font("Segoe UI", Math.Max(8f, 13f * _layoutScale), FontStyle.Bold);
                    SolidBrush timingBrush = _renderResources.Brush(Color.FromArgb(alpha, 220, 228, 248));
                    DrawCentered(g, _feedbackTiming, timingFont, timingBrush, feedbackAnchor.X + shake, feedbackAnchor.Y + (int)ScaleY(34f) - (int)rise);
                }
            }
        }

        if (_isGamePaused)
            DrawPauseOverlay(g);

        g.Restore(state);
        TrackGameFrameAllocations(allocationStart);
    }

    private void TrackGameFrameAllocations(long allocationStart)
    {
        _gameDrawFrameCount++;
        long allocated = Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - allocationStart);
        _lastGameDrawAllocatedBytes = allocated;
        double sampleSeconds = (DateTime.Now - _lastAllocationLogTime).TotalSeconds;
        if (sampleSeconds < 5)
            return;

        long sampleFrames = _gameDrawFrameCount - _lastGameDrawSampleFrame;
        float sampledFps = sampleSeconds > 0d ? (float)(sampleFrames / sampleSeconds) : 0f;
        _lastGameDrawSampleFrame = _gameDrawFrameCount;
        _lastAllocationLogTime = DateTime.Now;
        AppLogger.Info($"Game draw performance sample: frame={_gameDrawFrameCount}, fps={sampledFps:F1}, lastFrameBytes={allocated}, gdi={GdiResourceMonitor.GetCurrentGdiObjectCount()}");
    }

    private void DrawGameplayBackground(Graphics g, int width, int height, Rectangle playArea)
    {
        DrawCachedGameplayBackground(g, width, height, playArea);

        float position = _lastPlaybackPositionSeconds > 0f ? _lastPlaybackPositionSeconds : _engine.CurrentChartTime;
        float motion = _reducedMotionEnabled ? 0f : position;
        float groove = Math.Clamp(_grooveGauge / 100f, 0.2f, 1f);
        Color accent = GetAccentColor();
        using var bandBrush = new SolidBrush(Color.FromArgb((int)(10 + groove * 26), accent));
        using var starBrush = new SolidBrush(Color.White);
        int starCount = _renderQualityMode switch { 0 => 42, 1 => 78, _ => 110 };
        for (int i = 0; i < starCount; i++)
        {
            int hash = i * 1103515245 + 12345;
            float x = Math.Abs(hash % 10000) / 10000f * width;
            float y = Math.Abs((hash / 97) % 10000) / 10000f * height * 0.82f;
            float twinkle = _reducedMotionEnabled ? 0.6f : 0.48f + MathF.Sin(motion * (0.6f + i % 5 * 0.09f) + i) * 0.28f;
            int alpha = Math.Clamp((int)(38f + twinkle * 82f), 24, 130);
            starBrush.Color = Color.FromArgb(alpha, 190, 205, 255);
            float size = (i % 11 == 0 ? 2.1f : 1.2f) * GameScale;
            g.FillEllipse(starBrush, x, y, size, size);
        }

        float horizon = height * 0.70f;
        for (int i = 0; i < 4; i++)
        {
            float phase = motion * (0.7f + i * 0.12f) + i * 1.4f;
            float y = horizon - 120f * GameScale + i * 48f * GameScale + MathF.Sin(phase) * 10f * GameScale;
            RectangleF band = new(0, y, width, Math.Max(1f, 2f * GameScale));
            g.FillRectangle(bandBrush, band);
        }
    }

    private void FlushDwmIfEnabled()
    {
        if (!_vsyncEnabled)
            return;

        try { DwmFlush(); } catch { /* DWM not available */ }
    }

    private void DrawCachedGameplayBackground(Graphics g, int width, int height, Rectangle playArea)
    {
        Color accent = GetAccentColor();
        string cacheKey = $"{width}x{height}|{playArea.X},{playArea.Y},{playArea.Width},{playArea.Height}|{LaneCount}|{accent.ToArgb()}|{_gameBgaPath}|{_darkModeEnabled}|{_highContrastEnabled}|{_laneBrightness}|{_colorVisionMode}|{_visualSkinName}";
        if (_gameBackgroundCache is null || !string.Equals(_gameBackgroundCacheKey, cacheKey, StringComparison.Ordinal))
        {
            _gameBackgroundCache?.Dispose();
            _gameBackgroundCache = new Bitmap(Math.Max(1, width), Math.Max(1, height), PixelFormat.Format32bppPArgb);
            using Graphics backgroundGraphics = Graphics.FromImage(_gameBackgroundCache);
            backgroundGraphics.SmoothingMode = SmoothingMode.HighSpeed;
            backgroundGraphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            backgroundGraphics.CompositingQuality = CompositingQuality.HighSpeed;
            DrawGameplayBackgroundBase(backgroundGraphics, width, height, playArea, accent);
            int hitY = (int)MathF.Round(GameEngine.GetHitZoneY(height));
            DrawPerspectivePlayfieldBase(backgroundGraphics, playArea, hitY);
            DrawHitZoneGlow(backgroundGraphics, playArea, hitY);
            _gameBackgroundCacheKey = cacheKey;
        }

        g.DrawImageUnscaled(_gameBackgroundCache, 0, 0);
    }

    private void DrawGameplayBackgroundBase(Graphics g, int width, int height, Rectangle playArea, Color accent)
    {
        using (var bgBrush = new LinearGradientBrush(
            new Point(0, 0), new Point(0, height),
            Color.FromArgb(2, 4, 10), Color.FromArgb(8, 10, 22)))
            g.FillRectangle(bgBrush, 0, 0, width, height);

        if (_gameBgaImage is not null)
        {
            Rectangle dest = GetCoverDestination(new Rectangle(0, 0, width, height), _gameBgaImage.Width, _gameBgaImage.Height);
            DrawImageAlpha(g, _gameBgaImage, dest, 92);
            using var shade = new SolidBrush(Color.FromArgb(158, 2, 4, 12));
            g.FillRectangle(shade, 0, 0, width, height);
        }

        float horizon = height * 0.70f;
        using (var aura = new LinearGradientBrush(
            new RectangleF(0, horizon - 170f * GameScale, width, 240f * GameScale),
            Color.FromArgb(0, accent),
            Color.FromArgb(58, accent),
            LinearGradientMode.Vertical))
            g.FillRectangle(aura, 0, horizon - 170f * GameScale, width, 240f * GameScale);

        using var mountainBrush = new SolidBrush(Color.FromArgb(205, 4, 7, 15));
        PointF[] leftMountains =
        [
            new(0, height),
            new(0, horizon + 30f * GameScale),
            new(width * 0.12f, horizon - 10f * GameScale),
            new(width * 0.25f, horizon + 54f * GameScale),
            new(width * 0.38f, horizon + 18f * GameScale),
            new(width * 0.50f, height),
        ];
        PointF[] rightMountains =
        [
            new(width * 0.50f, height),
            new(width * 0.64f, horizon + 28f * GameScale),
            new(width * 0.78f, horizon - 18f * GameScale),
            new(width * 0.91f, horizon + 48f * GameScale),
            new(width, horizon + 12f * GameScale),
            new(width, height),
        ];
        g.FillPolygon(mountainBrush, leftMountains);
        g.FillPolygon(mountainBrush, rightMountains);

        using var roadPen = new Pen(Color.FromArgb(36, accent), Math.Max(1f, GameScale));
        for (int i = -6; i <= 6; i++)
        {
            float startX = playArea.Left + playArea.Width / 2f + i * 12f * GameScale;
            float endX = width / 2f + i * width * 0.12f;
            g.DrawLine(roadPen, startX, horizon, endX, height);
        }
    }

    private static Rectangle GetCoverDestination(Rectangle bounds, int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
            return bounds;

        float scale = Math.Max(bounds.Width / (float)imageWidth, bounds.Height / (float)imageHeight);
        int width = (int)MathF.Ceiling(imageWidth * scale);
        int height = (int)MathF.Ceiling(imageHeight * scale);
        return new Rectangle(bounds.Left + (bounds.Width - width) / 2, bounds.Top + (bounds.Height - height) / 2, width, height);
    }

    private static void DrawImageAlpha(Graphics g, Image image, Rectangle bounds, int alpha)
    {
        alpha = Math.Clamp(alpha, 0, 255);
        if (alpha >= 255)
        {
            g.DrawImage(image, bounds);
            return;
        }

        using var attributes = new ImageAttributes();
        float a = alpha / 255f;
        var matrix = new ColorMatrix
        {
            Matrix00 = 1f,
            Matrix11 = 1f,
            Matrix22 = 1f,
            Matrix33 = a,
            Matrix44 = 1f,
        };
        attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        g.DrawImage(image, bounds, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
    }

    private static float GetJudgmentFeedbackDuration(Judgment? judgment)
    {
        return judgment switch
        {
            Judgment.Perfect => 520f,
            Judgment.Great => 560f,
            Judgment.Better => 620f,
            Judgment.Good => 680f,
            Judgment.Bad => 820f,
            _ => 600f,
        };
    }

    private static float GetJudgmentFeedbackFontSize(Judgment? judgment)
    {
        return judgment switch
        {
            Judgment.Perfect => 28f,
            Judgment.Great => 26f,
            Judgment.Better => 24f,
            Judgment.Good => 23f,
            Judgment.Bad => 30f,
            _ => 24f,
        };
    }

    private Point GetJudgmentFeedbackAnchor(Rectangle playArea, int hitY)
    {
        int x = playArea.Left + playArea.Width / 2;
        int y = hitY - (int)(210f * GameScale);
        return new Point(x, y);
    }

    private Color GetJudgmentAccessibleColor(Judgment? judgment)
    {
        if (UseHighContrast)
        {
            return judgment == Judgment.Bad
                ? Color.FromArgb(255, 90, 90)
                : Color.White;
        }

        if (_colorVisionMode > 0)
        {
            return judgment switch
            {
                Judgment.Perfect => Color.FromArgb(0, 114, 178),
                Judgment.Great => Color.FromArgb(0, 158, 115),
                Judgment.Better => Color.FromArgb(230, 159, 0),
                Judgment.Good => Color.FromArgb(86, 180, 233),
                Judgment.Bad => Color.FromArgb(213, 94, 0),
                _ => Color.FromArgb(200, 200, 200),
            };
        }

        return judgment switch
        {
            Judgment.Perfect => Color.FromArgb(0, 230, 255),
            Judgment.Great => Color.FromArgb(100, 255, 200),
            Judgment.Better => Color.FromArgb(180, 255, 100),
            Judgment.Good => Color.FromArgb(140, 255, 160),
            Judgment.Bad => Color.FromArgb(255, 90, 76),
            _ => Color.FromArgb(200, 200, 200),
        };
    }

    private void DrawPauseOverlay(Graphics g)
    {
        using var dimBrush = new SolidBrush(Color.FromArgb(205, 3, 5, 12));
        g.FillRectangle(dimBrush, 0, 0, ClientSize.Width, ClientSize.Height);

        Rectangle panel = GetPauseOverlayPanelBounds();
        Rectangle menu = GetPauseMenuBounds();
        Color accent = GetAccentColor();
        using var titleFont = new Font("Segoe UI Light", Math.Max(19f, 34f * GameScale), FontStyle.Regular);
        using var subFont = new Font("Segoe UI", Math.Max(7.5f, 11f * GameScale), FontStyle.Regular);
        using var itemFont = new Font("Segoe UI", Math.Max(12f, 18f * GameScale), FontStyle.Regular);
        using var keyFont = new Font("Segoe UI", Math.Max(7.5f, 10.5f * GameScale), FontStyle.Bold);
        using var titleBrush = new SolidBrush(Color.FromArgb(246, 248, 255));
        using var subBrush = new SolidBrush(Color.FromArgb(205, 204, 215, 242));
        using var panelPath = CreateRoundedRect(panel, 14f * GameScale);
        using var panelFill = new LinearGradientBrush(
            panel,
            Color.FromArgb(170, 8, 10, 25),
            Color.FromArgb(214, 4, 5, 13),
            LinearGradientMode.Vertical);
        using var panelBorder = new Pen(Color.FromArgb(205, 166, 174, 255), Math.Max(1f, 1.25f * GameScale));
        using var panelSoftBorder = new Pen(Color.FromArgb(80, accent), Math.Max(3f, 3.4f * GameScale));

        Rectangle shadow = new(panel.Left - (int)(18f * GameScale), panel.Top + (int)(18f * GameScale), panel.Width + (int)(36f * GameScale), panel.Height);
        using (var shadowPath = CreateRoundedRect(shadow, 18f * GameScale))
        using (var shadowBrush = new SolidBrush(Color.FromArgb(118, 0, 0, 0)))
            g.FillPath(shadowBrush, shadowPath);

        g.FillPath(panelFill, panelPath);
        g.DrawPath(panelSoftBorder, panelPath);
        g.DrawPath(panelBorder, panelPath);

        float centerX = panel.Left + panel.Width / 2f;
        float titleY = panel.Top + 50f * GameScale;
        DrawSpacedString(g, "PAUSED", titleFont, titleBrush, centerX, titleY, 12f * GameScale, centered: true);
        DrawPauseHeaderLines(g, centerX, titleY + 25f * GameScale, panel.Width * 0.23f, accent);
        DrawSpacedString(g, "ESC MENU", subFont, subBrush, centerX, panel.Top + 104f * GameScale, 5f * GameScale, centered: true);

        using (var menuPath = CreateRoundedRect(menu, 5f * GameScale))
        using (var menuFill = new SolidBrush(Color.FromArgb(92, 6, 7, 18)))
        using (var menuBorder = new Pen(Color.FromArgb(72, 130, 145, 210), Math.Max(1f, GameScale)))
        {
            g.FillPath(menuFill, menuPath);
            g.DrawPath(menuBorder, menuPath);
        }

        for (int i = 0; i < PauseActionLabels.Length; i++)
        {
            bool enabled = i != 3;
            bool active = _hoverPauseAction == i || (_hoverPauseAction < 0 && i == 0);
            DrawPauseOverlayActionButton(g, GetPauseActionButtonBounds(i), PauseActionLabels[i], active, enabled, itemFont, keyFont);
        }

        int dividerY = menu.Bottom + (int)(20f * GameScale);
        using var dividerPen = new Pen(Color.FromArgb(70, 120, 135, 185), Math.Max(1f, GameScale));
        g.DrawLine(dividerPen, panel.Left + 76f * GameScale, dividerY, panel.Right - 76f * GameScale, dividerY);

        Rectangle escPill = new(
            (int)(centerX - 104f * GameScale),
            dividerY + (int)(23f * GameScale),
            (int)(58f * GameScale),
            (int)(28f * GameScale));
        DrawPauseKeyPill(g, escPill, "ESC", keyFont, enabled: true);
        DrawSpacedString(g, "BACK / CLOSE", subFont, subBrush, centerX - 2f * GameScale, escPill.Top + 5f * GameScale, 5f * GameScale, centered: false);
        DrawKeyboardFocus(g, clientCoordinates: true);
    }

    private void DrawPauseHeaderLines(Graphics g, float centerX, float y, float width, Color accent)
    {
        using var linePen = new Pen(Color.FromArgb(118, accent), Math.Max(1f, GameScale))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        using var dotBrush = new SolidBrush(Color.FromArgb(226, 238, 242, 255));
        float gap = 148f * GameScale;
        g.DrawLine(linePen, centerX - gap - width, y, centerX - gap * 0.55f, y);
        g.DrawLine(linePen, centerX + gap * 0.55f, y, centerX + gap + width, y);
        g.FillEllipse(dotBrush, centerX - gap * 0.54f - 2f * GameScale, y - 2f * GameScale, 4f * GameScale, 4f * GameScale);
        g.FillEllipse(dotBrush, centerX + gap * 0.54f - 2f * GameScale, y - 2f * GameScale, 4f * GameScale, 4f * GameScale);
    }

    private Rectangle GetPauseOverlayPanelBounds()
    {
        float s = GameScale;
        int width = Math.Min((int)(570f * s), ClientSize.Width - (int)(64f * s));
        int height = Math.Min((int)(540f * s), ClientSize.Height - (int)(72f * s));
        width = Math.Max(360, width);
        height = Math.Max(420, height);
        return new Rectangle((ClientSize.Width - width) / 2, (ClientSize.Height - height) / 2, width, height);
    }

    private Rectangle GetPauseMenuBounds()
    {
        Rectangle panel = GetPauseOverlayPanelBounds();
        int x = panel.Left + (int)(68f * GameScale);
        int y = panel.Top + (int)(146f * GameScale);
        int width = panel.Width - (int)(136f * GameScale);
        int height = (int)(298f * GameScale);
        return new Rectangle(x, y, width, height);
    }

    private Rectangle GetPauseActionButtonBounds(int index)
    {
        Rectangle menu = GetPauseMenuBounds();
        int rowHeight = Math.Max(42, menu.Height / PauseActionLabels.Length);
        return new Rectangle(menu.Left, menu.Top + index * rowHeight, menu.Width, rowHeight);
    }

    private int GetPauseActionAt(Point location)
    {
        for (int i = 0; i < PauseActionLabels.Length; i++)
            if (GetPauseActionButtonBounds(i).Contains(location))
                return i;

        return -1;
    }

    private void DrawPauseOverlayActionButton(Graphics g, Rectangle bounds, string text, bool active, bool enabled, Font font, Font keyFont)
    {
        Color accent = enabled ? GetAccentColor() : Color.FromArgb(88, 100, 116);
        Rectangle inner = Rectangle.Inflate(bounds, active ? 0 : -1, active ? -1 : -2);
        if (active && enabled)
        {
            using var glowPath = CreateRoundedRect(Rectangle.Inflate(inner, 4, 4), 5f * GameScale);
            using var glowBrush = new SolidBrush(Color.FromArgb(60, accent));
            g.FillPath(glowBrush, glowPath);
        }

        using var path = CreateRoundedRect(inner, 4f * GameScale);
        using var fill = new LinearGradientBrush(
            inner,
            active && enabled ? Color.FromArgb(104, accent) : Color.FromArgb(enabled ? 20 : 12, 255, 255, 255),
            active && enabled ? Color.FromArgb(38, accent) : Color.FromArgb(enabled ? 8 : 5, 255, 255, 255),
            LinearGradientMode.Vertical);
        using var border = new Pen(active && enabled ? Color.FromArgb(235, 205, 210, 255) : Color.FromArgb(enabled ? 32 : 20, 120, 135, 185), Math.Max(1f, 1.2f * GameScale));
        using var textBrush = new SolidBrush(enabled ? Color.FromArgb(242, 246, 255) : Color.FromArgb(112, 130, 144, 166));
        g.FillPath(fill, path);
        g.DrawPath(border, path);

        DrawSpacedString(g, text, font, textBrush, bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height * 0.27f, 6f * GameScale, centered: true);
    }

    private void DrawPauseKeyPill(Graphics g, Rectangle bounds, string text, Font font, bool enabled)
    {
        Color color = enabled ? Color.FromArgb(214, 214, 218, 255) : Color.FromArgb(120, 140, 150, 170);
        using var path = CreateRoundedRect(bounds, 4f * GameScale);
        using var fill = new SolidBrush(Color.FromArgb(enabled ? 42 : 20, 12, 12, 28));
        using var border = new Pen(color, Math.Max(1f, GameScale));
        using var textBrush = new SolidBrush(color);
        g.FillPath(fill, path);
        g.DrawPath(border, path);
        DrawCentered(g, text, font, textBrush, bounds.Left + bounds.Width / 2, bounds.Top + (int)(bounds.Height * 0.20f));
    }

    private void HandlePauseOverlayMouseMove(Point location)
    {
        int hover = GetPauseActionAt(location);
        if (hover != _hoverPauseAction)
        {
            _hoverPauseAction = hover;
            Cursor = hover >= 0 && hover != 3 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        else
        {
            Cursor = hover >= 0 && hover != 3 ? Cursors.Hand : Cursors.Default;
        }
    }

    private void HandlePauseOverlayMouseDown(Point location)
    {
        int action = GetPauseActionAt(location);
        if (action < 0)
            return;

        _hoverPauseAction = action;
        switch (action)
        {
            case 0:
                ResumeGame();
                break;
            case 1:
                _chartCompleteWaiting = false;
                _isCountdownActive = false;
                _engine.Stop();
                _audio.StopAllSounds();
                BeginGame(
                    replayPlayback: _isReplayPlayback,
                    validatedReplayChart: _isReplayPlayback ? _selectedChartNotes : null,
                    validatedAudioFingerprint: _isReplayPlayback ? _activeReplay?.AudioFingerprint : null);
                break;
            case 2:
                _chartCompleteWaiting = false;
                _isCountdownActive = false;
                _isGamePaused = false;
                _engine.Stop();
                _audio.StopAllSounds();
                ClearReplayPlaybackState();
                Array.Clear(_lanePressed);
                _mouseHeldLane = -1;
                ExitGameLowLatencyMode();
                _screen = UiScreen.SongSelect;
                _previewSongKey = string.Empty;
                Invalidate();
                break;
            case 4:
                EndGame();
                break;
        }
    }

    private const float GameplayDesignWidth = 1920f;
    private const float GameplayDesignHeight = 1080f;

    private float GameScale => Math.Max(0.52f, Math.Min(ClientSize.Width / GameplayDesignWidth, ClientSize.Height / GameplayDesignHeight));

    private float PlayfieldTopY => Math.Max(84f * GameScale, ClientSize.Height * 0.13f);

    private float PerspectiveProgress(float y, int hitY)
    {
        float top = PlayfieldTopY;
        return Math.Clamp((y - top) / Math.Max(1f, hitY - top), 0f, 1f);
    }

    private float PerspectiveWidth(Rectangle playArea, float y, int hitY)
    {
        return Math.Max(LaneCount * 96f * GameScale, playArea.Width * 0.94f);
    }

    private float PerspectiveLaneX(Rectangle playArea, int laneBoundary, float y, int hitY)
    {
        float width = PerspectiveWidth(playArea, y, hitY);
        float left = playArea.Left + playArea.Width / 2f - width / 2f;
        return left + width * laneBoundary / LaneCount;
    }

    private PointF PerspectivePoint(Rectangle playArea, int laneBoundary, float y, int hitY) =>
        new(PerspectiveLaneX(playArea, laneBoundary, y, hitY), y);

    private RectangleF GetPerspectiveLaneNoteRect(Rectangle playArea, int lane, float y, int hitY)
    {
        y = Math.Clamp(y, PlayfieldTopY - 32f * GameScale, hitY + 18f * GameScale);
        float left = PerspectiveLaneX(playArea, lane, y, hitY);
        float right = PerspectiveLaneX(playArea, lane + 1, y, hitY);
        float laneWidth = Math.Max(20f * GameScale, right - left);
        float noteWidth = laneWidth * 0.62f;
        float noteHeight = Math.Max(12f * GameScale, 17f * GameScale);
        return new RectangleF(left + (laneWidth - noteWidth) / 2f, y - noteHeight / 2f, noteWidth, noteHeight);
    }

    private GraphicsPath CreatePerspectiveLaneClip(Rectangle playArea, int lane, float topY, float bottomY, int hitY)
    {
        topY = Math.Clamp(topY, PlayfieldTopY - 40f * GameScale, hitY + 24f * GameScale);
        bottomY = Math.Clamp(bottomY, PlayfieldTopY - 40f * GameScale, hitY + 24f * GameScale);
        if (bottomY < topY)
            (topY, bottomY) = (bottomY, topY);

        var path = new GraphicsPath();
        path.AddPolygon(
        [
            PerspectivePoint(playArea, lane, topY, hitY),
            PerspectivePoint(playArea, lane + 1, topY, hitY),
            PerspectivePoint(playArea, lane + 1, bottomY, hitY),
            PerspectivePoint(playArea, lane, bottomY, hitY),
        ]);
        return path;
    }

    private void DrawPerspectivePlayfieldBase(Graphics g, Rectangle playArea, int hitY)
    {
        float topY = PlayfieldTopY;
        float horizonY = topY - 8f * GameScale;
        float bottomY = hitY;
        PointF topLeft = PerspectivePoint(playArea, 0, topY, hitY);
        PointF topRight = PerspectivePoint(playArea, LaneCount, topY, hitY);
        PointF bottomLeft = PerspectivePoint(playArea, 0, bottomY, hitY);
        PointF bottomRight = PerspectivePoint(playArea, LaneCount, bottomY, hitY);
        Color accent = GetAccentColor();

        using var deckPath = new GraphicsPath();
        deckPath.AddPolygon([topLeft, topRight, bottomRight, bottomLeft]);
        using (var deckFill = new PathGradientBrush(deckPath)
        {
            CenterPoint = new PointF(playArea.Left + playArea.Width / 2f, hitY - 70f * GameScale),
            CenterColor = Color.FromArgb(74, 28, 33, 58),
            SurroundColors = [Color.FromArgb(18, 4, 7, 15)],
        })
            g.FillPath(deckFill, deckPath);

        using (var shade = new LinearGradientBrush(
            new RectangleF(playArea.Left, topY, playArea.Width, Math.Max(1f, bottomY - topY)),
            Color.FromArgb(6, 255, 255, 255),
            Color.FromArgb(34, accent),
            LinearGradientMode.Vertical))
            g.FillPath(shade, deckPath);

        for (int lane = 0; lane < LaneCount; lane++)
        {
            Color laneColor = GetAccessibleLaneColor(lane);
            int alpha = lane % 2 == 0 ? 16 : 9;
            using var lanePath = new GraphicsPath();
            lanePath.AddPolygon([
                PerspectivePoint(playArea, lane, topY, hitY),
                PerspectivePoint(playArea, lane + 1, topY, hitY),
                PerspectivePoint(playArea, lane + 1, bottomY, hitY),
                PerspectivePoint(playArea, lane, bottomY, hitY),
            ]);
            using var laneBrush = new LinearGradientBrush(
                new RectangleF(playArea.Left, topY, playArea.Width, bottomY - topY),
                Color.FromArgb(Math.Max(4, alpha / 3), laneColor),
                Color.FromArgb(alpha, laneColor),
                LinearGradientMode.Vertical);
            g.FillPath(laneBrush, lanePath);
        }

        using var outerPen = new Pen(Color.FromArgb(184, 188, 205, 255), Math.Max(1.2f, 1.7f * GameScale));
        using var innerPen = new Pen(Color.FromArgb(72, 190, 210, 255), Math.Max(0.8f, 1f * GameScale));
        using var centerPen = new Pen(Color.FromArgb(110, accent), Math.Max(0.8f, 1.2f * GameScale));

        for (int lane = 0; lane <= LaneCount; lane++)
        {
            PointF start = PerspectivePoint(playArea, lane, topY, hitY);
            PointF end = PerspectivePoint(playArea, lane, bottomY, hitY);
            bool edge = lane == 0 || lane == LaneCount;
            g.DrawLine(edge ? outerPen : innerPen, start, end);
        }

        g.DrawLine(centerPen, playArea.Left + playArea.Width / 2f, horizonY, playArea.Left + playArea.Width / 2f, hitY);
        using var horizonPen = new Pen(Color.FromArgb(52, accent), Math.Max(0.8f, 1f * GameScale));
        g.DrawLine(horizonPen, playArea.Left - playArea.Width * 0.12f, horizonY, playArea.Right + playArea.Width * 0.12f, horizonY);
    }

    private void DrawPerspectiveLaneHighlights(Graphics g, Rectangle playArea, int hitY)
    {
        Span<bool> holdingLanes = stackalloc bool[7];
        for (int noteIndex = 0; noteIndex < _engine.Notes.Count; noteIndex++)
        {
            Note note = _engine.Notes[noteIndex];
            if (note.State != NoteState.Holding)
                continue;

            if (note.Lane >= 0 && note.Lane < holdingLanes.Length)
                holdingLanes[note.Lane] = true;
            if (note.EndLane >= 0 && note.EndLane < holdingLanes.Length)
                holdingLanes[note.EndLane] = true;
        }

        float topY = PlayfieldTopY;
        float bottomY = hitY;
        for (int lane = 0; lane < LaneCount; lane++)
        {
            int alpha = _lanePressed[lane] ? 60 : holdingLanes[lane] ? 42 : 0;
            if (alpha == 0)
                continue;

            Color laneColor = GetAccessibleLaneColor(lane);
            using var lanePath = new GraphicsPath();
            lanePath.AddPolygon([
                PerspectivePoint(playArea, lane, topY, hitY),
                PerspectivePoint(playArea, lane + 1, topY, hitY),
                PerspectivePoint(playArea, lane + 1, bottomY, hitY),
                PerspectivePoint(playArea, lane, bottomY, hitY),
            ]);
            using var laneBrush = new LinearGradientBrush(
                new RectangleF(playArea.Left, topY, playArea.Width, Math.Max(1f, bottomY - topY)),
                Color.FromArgb(Math.Max(4, alpha / 3), laneColor),
                Color.FromArgb(alpha, laneColor),
                LinearGradientMode.Vertical);
            g.FillPath(laneBrush, lanePath);
        }
    }

    private void DrawGameFrame(Graphics g, Rectangle playArea)
    {
        int w = ClientSize.Width;
        int h = ClientSize.Height;

        // 프레임 영역 (플레이 영역 바깥)
        int frameInset = 8;
        Rectangle outerFrame = new(
            playArea.Left - frameInset, 0,
            playArea.Width + frameInset * 2, h);

        // 좌측 장식
        using (var leftGrad = new LinearGradientBrush(
            new Rectangle(0, 0, playArea.Left, h),
            Color.FromArgb(12, 14, 22), Color.FromArgb(22, 26, 38),
            LinearGradientMode.Horizontal))
            g.FillRectangle(leftGrad, 0, 0, playArea.Left, h);

        // 우측 장식
        using (var rightGrad = new LinearGradientBrush(
            new Rectangle(playArea.Right, 0, w - playArea.Right, h),
            Color.FromArgb(22, 26, 38), Color.FromArgb(12, 14, 22),
            LinearGradientMode.Horizontal))
            g.FillRectangle(rightGrad, playArea.Right, 0, w - playArea.Right, h);

        // 프레임 테두리 (메탈릭 느낌) — 캐시된 펜 사용
        g.DrawRectangle(_framePenOuter, outerFrame);
        g.DrawRectangle(_framePenInner, Rectangle.Inflate(outerFrame, -3, -3));

        // 코너 장식 (작은 원) — 캐시된 브러시 사용
        int cornerSize = 10;
        g.FillEllipse(_frameCornerBrush, outerFrame.Left - cornerSize / 2, outerFrame.Top + 20, cornerSize, cornerSize);
        g.FillEllipse(_frameCornerBrush, outerFrame.Right - cornerSize / 2, outerFrame.Top + 20, cornerSize, cornerSize);
        g.FillEllipse(_frameCornerBrush, outerFrame.Left - cornerSize / 2, outerFrame.Bottom - 30, cornerSize, cornerSize);
        g.FillEllipse(_frameCornerBrush, outerFrame.Right - cornerSize / 2, outerFrame.Bottom - 30, cornerSize, cornerSize);
    }

    private void DrawPerspectiveHitZoneGlow(Graphics g, Rectangle playArea, int hitY)
    {
        Color hitGlow = _visualSkin.HitGlow ?? Color.FromArgb(102, 166, 255);
        Color hitGlowBottom = _visualSkin.HitGlowBottom ?? Color.FromArgb(168, 105, 255);
        Color hitLine = _visualSkin.HitLine ?? Color.FromArgb(216, 194, 255);
        float left = PerspectiveLaneX(playArea, 0, hitY, hitY);
        float right = PerspectiveLaneX(playArea, LaneCount, hitY, hitY);
        float width = right - left;
        RectangleF glowRect = new(left - width * 0.12f, hitY - 56f * GameScale, width * 1.24f, 116f * GameScale);

        using (var glowBrush = new LinearGradientBrush(glowRect,
            Color.FromArgb(0, hitGlow),
            Color.FromArgb(92, hitGlowBottom),
            LinearGradientMode.Vertical))
            g.FillEllipse(glowBrush, glowRect);

        using var bloomPen = new Pen(Color.FromArgb(155, hitGlowBottom), Math.Max(7f, 10f * GameScale))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        using var corePen = new Pen(Color.FromArgb(246, hitLine), Math.Max(2f, 3f * GameScale))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        using var whitePen = new Pen(Color.FromArgb(220, 255, 255, 255), Math.Max(1f, 1.2f * GameScale))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };

        g.DrawLine(bloomPen, left, hitY, right, hitY);
        g.DrawLine(corePen, left, hitY, right, hitY);
        g.DrawLine(whitePen, left, hitY - Math.Max(1f, GameScale), right, hitY - Math.Max(1f, GameScale));

        using var flareBrush = new SolidBrush(Color.FromArgb(150, 255, 255, 255));
        for (int lane = 0; lane <= LaneCount; lane++)
        {
            float x = PerspectiveLaneX(playArea, lane, hitY, hitY);
            g.FillEllipse(flareBrush, x - 8f * GameScale, hitY - 8f * GameScale, 16f * GameScale, 16f * GameScale);
        }
    }

    private void DrawHitZoneGlow(Graphics g, Rectangle playArea, int hitY)
    {
        if (ClientSize.Width >= 0)
        {
            DrawPerspectiveHitZoneGlow(g, playArea, hitY);
            return;
        }
        // 글로우 배경 (히트존 주변)
        int glowHeight = 80;
        Rectangle glowRect = new(playArea.Left, hitY - glowHeight / 2, playArea.Width, glowHeight);
        Color hitGlow = _visualSkin.HitGlow ?? Color.FromArgb(0, 170, 255);
        Color hitGlowBottom = _visualSkin.HitGlowBottom ?? Color.FromArgb(255, 230, 70);
        Color hitLine = _visualSkin.HitLine ?? Color.FromArgb(255, 200, 80);
        using var glowBrush = new LinearGradientBrush(glowRect,
            Color.FromArgb(0, hitGlow),
            Color.FromArgb(70, hitGlow),
            LinearGradientMode.Vertical);
        g.FillRectangle(glowBrush, glowRect);

        // 히트존 아래 강한 글로우
        Rectangle bottomGlow = new(playArea.Left, hitY - 4, playArea.Width, 40);
        using var bottomGlowBrush = new LinearGradientBrush(bottomGlow,
            Color.FromArgb(150, hitGlowBottom),
            Color.FromArgb(0, hitGlow),
            LinearGradientMode.Vertical);
        g.FillRectangle(bottomGlowBrush, bottomGlow);

        // 판정선 (밝은 오렌지-골드) — 캐시된 펜 사용
        if (_visualSkin.HitLine is null)
        {
            g.DrawLine(_hitPen1, playArea.Left, hitY, playArea.Right, hitY);
            g.DrawLine(_hitPen2, playArea.Left, hitY - 1, playArea.Right, hitY - 1);
        }
        else
        {
            using var hitPen1 = new Pen(Color.FromArgb(220, hitLine), Math.Max(2f, 3f * _layoutScale));
            using var hitPen2 = new Pen(Color.FromArgb(140, ControlPaint.Light(hitLine, 0.35f)), Math.Max(1f, 1.5f * _layoutScale));
            g.DrawLine(hitPen1, playArea.Left, hitY, playArea.Right, hitY);
            g.DrawLine(hitPen2, playArea.Left, hitY - 1, playArea.Right, hitY - 1);
        }
    }

    private void DrawFloatingLaneKeys(Graphics g, Rectangle playArea, int hitY)
    {
        Font font = _renderResources.Font("Segoe UI", Math.Max(14f, 24f * GameScale), FontStyle.Regular);
        SolidBrush textBrush = _renderResources.Brush(Color.FromArgb(236, 242, 255));
        SolidBrush dimBrush = _renderResources.Brush(Color.FromArgb(150, 178, 190, 222));

        for (int i = 0; i < LaneCount; i++)
        {
            RectangleF keyRect = GetFloatingLaneKeyBounds(playArea, hitY, i);
            float center = keyRect.Left + keyRect.Width / 2f;
            bool pressed = _lanePressed[i];
            Color laneColor = GetAccessibleLaneColor(i);
            using var path = CreateRoundedRect(Rectangle.Round(keyRect), 8f * GameScale);
            using var fill = new LinearGradientBrush(
                keyRect,
                pressed ? Color.FromArgb(118, laneColor) : Color.FromArgb(34, 16, 20, 36),
                pressed ? Color.FromArgb(76, laneColor) : Color.FromArgb(18, 4, 7, 16),
                LinearGradientMode.Vertical);
            using var border = new Pen(pressed ? Color.FromArgb(235, laneColor) : Color.FromArgb(172, 190, 198, 255), Math.Max(1.2f, 1.7f * GameScale));
            g.FillPath(fill, path);
            g.DrawPath(border, path);
            DrawCentered(g, LaneLabels[i], font, pressed ? textBrush : dimBrush, (int)center, (int)(keyRect.Top + keyRect.Height * 0.2f));
        }

        Rectangle pauseKey = Rectangle.Round(GetPauseKeyBounds());
        using var pausePath = CreateRoundedRect(pauseKey, 6f * GameScale);
        using var pauseFill = new SolidBrush(Color.FromArgb(34, 255, 255, 255));
        using var pauseBorder = new Pen(Color.FromArgb(130, 220, 226, 255), Math.Max(1f, GameScale));
        g.FillPath(pauseFill, pausePath);
        g.DrawPath(pauseBorder, pausePath);
        DrawCentered(g, "II", font, dimBrush, pauseKey.Left + pauseKey.Width / 2, pauseKey.Top + (int)(pauseKey.Height * 0.12f));
        Font hintFont = _renderResources.Font("Segoe UI", Math.Max(8f, 14f * GameScale), FontStyle.Regular);
        DrawSpacedString(g, "ESC PAUSE", hintFont, dimBrush, pauseKey.Right + 28f * GameScale, pauseKey.Top + 11f * GameScale, 4f * GameScale, centered: false);
    }

    private RectangleF GetFloatingLaneKeyBounds(Rectangle playArea, int hitY, int lane)
    {
        float left = PerspectiveLaneX(playArea, lane, hitY, hitY);
        float right = PerspectiveLaneX(playArea, lane + 1, hitY, hitY);
        float center = (left + right) / 2f;
        float size = Math.Clamp((right - left) * 0.34f, 42f * GameScale, 72f * GameScale);
        return new RectangleF(center - size / 2f, hitY + 38f * GameScale, size, size * 0.82f);
    }

    private RectangleF GetPauseKeyBounds()
    {
        float s = GameScale;
        return new RectangleF(38f * s, ClientSize.Height - 78f * s, 46f * s, 40f * s);
    }

    private void DrawPianoKeys(Graphics g, Rectangle playArea, int hitY, int laneWidth)
    {
        if (ClientSize.Width >= 0)
        {
            DrawFloatingLaneKeys(g, playArea, hitY);
            return;
        }

        int keyAreaTop = hitY + 4;
        int keyAreaHeight = ClientSize.Height - keyAreaTop;

        for (int i = 0; i < LaneCount; i++)
        {
            int kx = playArea.Left + i * laneWidth;
            Rectangle keyRect = new(kx + 2, keyAreaTop, laneWidth - 4, keyAreaHeight - 4);

            // 피아노 키 배경: 누르면 흰색, 안 누르면 검은색
            bool pressed = _lanePressed[i];
            Color keyTop = pressed
                ? _visualSkin.KeyPressedTop ?? Color.FromArgb(200, 210, 220)
                : _visualSkin.KeyTop ?? Color.FromArgb(55, 60, 70);
            Color keyBot = pressed
                ? _visualSkin.KeyPressedBottom ?? Color.FromArgb(170, 180, 195)
                : _visualSkin.KeyBottom ?? Color.FromArgb(35, 40, 50);

            using var keyBrush = new LinearGradientBrush(keyRect, keyTop, keyBot, LinearGradientMode.Vertical);
            using var keyPath = CreateRoundedRect(keyRect, 4f);
            g.FillPath(keyBrush, keyPath);

            // 키 레이블 — 캐시된 폰트/브러시 사용
            DrawCentered(g, LaneLabels[i], _keyLabelFont, pressed ? _keyLabelPressedBrush : _keyLabelReleasedBrush,
                kx + laneWidth / 2, keyAreaTop + keyAreaHeight / 2 - 8);
        }
    }

    private Color GetAccessibleLaneColor(int lane)
    {
        if (UseHighContrast)
            return lane % 2 == 0 ? Color.White : Color.FromArgb(255, 230, 0);

        int index = Math.Clamp(lane, 0, LaneColors.Length - 1);
        if (_colorVisionMode <= 0)
            return _visualSkin.GetLaneColor(index, LaneColors[index]);

        Color[] palette =
        [
            Color.FromArgb(0, 114, 178),
            Color.FromArgb(230, 159, 0),
            Color.FromArgb(0, 158, 115),
            Color.FromArgb(204, 121, 167),
            Color.FromArgb(86, 180, 233),
            Color.FromArgb(213, 94, 0),
            Color.FromArgb(240, 228, 66),
        ];
        return palette[index % palette.Length];
    }

    private void DrawStyledPerspectiveNote(Graphics g, Note note, Rectangle playArea, int hitY)
    {
        RectangleF noteRect = GetPerspectiveLaneNoteRect(playArea, note.Lane, note.Y, hitY);
        Color laneColor = GetAccessibleLaneColor(note.Lane);
        float resolvedAge = note.State is NoteState.Hit or NoteState.Miss
            ? Math.Max(0f, _engine.CurrentChartTime - note.ResolvedTime)
            : 0f;
        int alpha = note.State is NoteState.Hit or NoteState.Miss
            ? Math.Clamp((int)(255f * (1f - resolvedAge / 0.55f)), 35, 255)
            : 255;

        if (note.State == NoteState.Miss)
            noteRect.X += (float)(Math.Sin(resolvedAge * 80f) * Math.Max(1f, 7f * GameScale * (1f - Math.Min(1f, resolvedAge / 0.45f))));

        if (note.Type != NoteType.Tap)
        {
            int endLane = Math.Clamp(note.EndLane, 0, LaneCount - 1);
            RectangleF endRect = GetPerspectiveLaneNoteRect(playArea, endLane, note.EndY, hitY);
            using var tailPen = new Pen(Color.FromArgb(note.State == NoteState.Miss ? Math.Min(alpha, 150) : 122, note.State == NoteState.Miss ? Color.FromArgb(255, 82, 92) : laneColor), Math.Max(5f, noteRect.Width * 0.20f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            g.DrawLine(tailPen,
                noteRect.Left + noteRect.Width / 2f,
                noteRect.Top + noteRect.Height / 2f,
                endRect.Left + endRect.Width / 2f,
                endRect.Top + endRect.Height / 2f);
        }

        RectangleF glowRect = RectangleF.Inflate(noteRect, noteRect.Width * 0.16f, noteRect.Height * 0.75f);
        using GraphicsPath laneClip = CreatePerspectiveLaneClip(playArea, note.Lane, glowRect.Top, glowRect.Bottom, hitY);
        GraphicsState clipState = g.Save();
        g.SetClip(laneClip, CombineMode.Intersect);
        try
        {
            using (var glowBrush = new SolidBrush(Color.FromArgb(note.State == NoteState.Miss ? Math.Min(130, alpha) : 64, note.State == NoteState.Miss ? Color.FromArgb(255, 74, 86) : laneColor)))
                g.FillEllipse(glowBrush, glowRect);

            using var path = CreateRoundedRect(noteRect, Math.Max(5f, 6f * GameScale));
            Color top = note.State == NoteState.Miss ? Color.FromArgb(alpha, 255, 92, 108) : Color.FromArgb(alpha, ControlPaint.Light(laneColor, 0.54f));
            Color bottom = note.State == NoteState.Miss ? Color.FromArgb(alpha, 130, 22, 38) : Color.FromArgb(alpha, laneColor);
            using var fill = new LinearGradientBrush(noteRect, top, bottom, LinearGradientMode.Vertical);
            using var border = new Pen(Color.FromArgb(Math.Min(245, alpha), 235, 242, 255), Math.Max(1f, 1.4f * GameScale));
            g.FillPath(fill, path);
            g.DrawPath(border, path);

            using var shinePen = new Pen(Color.FromArgb(Math.Min(220, alpha), 255, 255, 255), Math.Max(1f, GameScale))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            g.DrawLine(shinePen, noteRect.Left + noteRect.Width * 0.13f, noteRect.Top + noteRect.Height * 0.35f, noteRect.Right - noteRect.Width * 0.13f, noteRect.Top + noteRect.Height * 0.35f);

            if (note.State == NoteState.Miss)
            {
                using var missPen = new Pen(Color.FromArgb(alpha, 255, 225, 230), Math.Max(1.4f, 2f * GameScale));
                g.DrawLine(missPen, noteRect.Left, noteRect.Top, noteRect.Right, noteRect.Bottom);
                g.DrawLine(missPen, noteRect.Right, noteRect.Top, noteRect.Left, noteRect.Bottom);
            }
        }
        finally
        {
            g.Restore(clipState);
        }
    }

    private void DrawStyledNote(Graphics g, Note note, Rectangle playArea, int hitY)
    {
        int playAreaLeft = playArea.Left;
        int laneWidth = playArea.Width / LaneCount;
        if (ClientSize.Width >= 0)
        {
            DrawStyledPerspectiveNote(g, note, playArea, hitY);
            return;
        }

        int nx = playAreaLeft + note.Lane * laneWidth + 6;
        int ny = (int)note.Y;
        int nw = laneWidth - 12;
        int nh = (int)Note.Height;
        Color laneColor = GetAccessibleLaneColor(note.Lane);
        Color noteTop = UseHighContrast ? Color.White : Color.FromArgb(240, ControlPaint.Light(laneColor, 0.3f));
        Color noteBottom = UseHighContrast ? Color.Black : Color.FromArgb(220, laneColor);
        float resolvedAge = note.State is NoteState.Hit or NoteState.Miss
            ? Math.Max(0f, _engine.CurrentChartTime - note.ResolvedTime)
            : 0f;
        int resolvedAlpha = note.State is NoteState.Hit or NoteState.Miss
            ? Math.Clamp((int)(255f * (1f - resolvedAge / 0.55f)), 35, 255)
            : 255;
        if (note.State == NoteState.Miss)
            nx += (int)(Math.Sin(resolvedAge * 80f) * Math.Max(1f, 7f * (1f - Math.Min(1f, resolvedAge / 0.45f))));

        if (note.Type != NoteType.Tap)
        {
            int endLane = Math.Clamp(note.EndLane, 0, LaneCount - 1);
            int endX = playAreaLeft + endLane * laneWidth + 6;
            int endY = (int)note.EndY;
            int bodyTop = Math.Min(ny, endY) + nh / 2;
            int bodyHeight = Math.Max(8, Math.Abs(endY - ny));
            Rectangle bodyBounds = new(Math.Min(nx, endX), bodyTop, Math.Abs(endX - nx) + nw, bodyHeight);

            using var bodyBrush = new LinearGradientBrush(
                bodyBounds,
                note.State == NoteState.Miss ? Color.FromArgb(120, 255, 70, 72) : Color.FromArgb(note.State == NoteState.Holding ? 130 : 90, noteTop),
                note.State == NoteState.Miss ? Color.FromArgb(30, 255, 70, 72) : Color.FromArgb(35, noteBottom),
                LinearGradientMode.Vertical);

            if (note.Type == NoteType.Slide && endLane != note.Lane)
            {
                using var bodyPen = new Pen(bodyBrush, Math.Max(8f, laneWidth * 0.18f))
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                };
                g.DrawLine(bodyPen, nx + nw / 2, ny + nh / 2, endX + nw / 2, endY + nh / 2);
                DrawSlideArrowSkin(g, nx + nw / 2, ny + nh / 2, endX + nw / 2, endY + nh / 2, laneWidth, resolvedAlpha);
            }
            else
            {
                Rectangle bodyRect = new(nx + nw / 3, bodyTop, Math.Max(8, nw / 3), bodyHeight);
                using var bodyPath = CreateRoundedRect(bodyRect, 6f);
                g.FillPath(bodyBrush, bodyPath);
                if (_visualSkin.LongTail is not null)
                    DrawImageAlpha(g, _visualSkin.LongTail, bodyRect, note.State == NoteState.Miss ? Math.Min(160, resolvedAlpha) : 210);
                if (note.State == NoteState.Holding && note.Duration > 0f)
                {
                    int fillHeight = Math.Max(2, (int)(bodyHeight * note.HoldProgress));
                    Rectangle fillRect = new(bodyRect.Left, bodyRect.Bottom - fillHeight, bodyRect.Width, fillHeight);
                    using var fillBrush = new SolidBrush(Color.FromArgb(125, 255, 255, 255));
                    g.FillRectangle(fillBrush, fillRect);
                }
            }
        }

        // 노트 글로우 (뒤쪽) — 캐시된 브러시 사용
        Rectangle glowRect = new(nx - 3, ny - 2, nw + 6, nh + 4);
        if (note.State == NoteState.Miss)
        {
            using var missGlow = new SolidBrush(Color.FromArgb(Math.Min(180, resolvedAlpha), 255, 54, 64));
            g.FillRectangle(missGlow, glowRect);
        }
        else
        {
            using var glowBrush = new SolidBrush(Color.FromArgb(UseHighContrast ? 130 : 50, laneColor));
            g.FillRectangle(glowBrush, glowRect);
        }

        // 노트 본체 (둥근 바)
        Rectangle noteRect = new(nx, ny, nw, nh);
        using var notePath = CreateRoundedRect(noteRect, 5f);

        // 그라데이션 — 캐시된 색상 사용
        Color top = note.State == NoteState.Miss ? Color.FromArgb(resolvedAlpha, 255, 86, 92) : Color.FromArgb(resolvedAlpha, noteTop);
        Color bottom = note.State == NoteState.Miss ? Color.FromArgb(resolvedAlpha, 145, 24, 36) : Color.FromArgb(resolvedAlpha, noteBottom);
        using var noteBrush = new LinearGradientBrush(noteRect, top, bottom, LinearGradientMode.Vertical);
        if (_visualSkin.NoteBody is not null && note.State != NoteState.Miss)
            DrawImageAlpha(g, _visualSkin.NoteBody, noteRect, resolvedAlpha);
        else
            g.FillPath(noteBrush, notePath);

        // 노트 하이라이트 (상단 밝은 줄) — 캐시된 브러시
        Rectangle highlightRect = new(nx + 2, ny + 1, nw - 4, nh / 3);
        g.FillRectangle(_noteHighlightBrush, highlightRect);

        if (note.State == NoteState.Holding)
        {
            float pulse = (float)(Math.Sin(DateTime.Now.TimeOfDay.TotalSeconds * 18.0) * 0.5 + 0.5);
            using var holdPen = new Pen(Color.FromArgb((int)(130 + pulse * 90), 255, 255, 255), Math.Max(1.5f, 2f * _layoutScale));
            g.DrawPath(holdPen, notePath);
        }
        else if (note.State == NoteState.Miss)
        {
            using var missPen = new Pen(Color.FromArgb(resolvedAlpha, 255, 210, 210), Math.Max(2f, 3f * _layoutScale));
            g.DrawLine(missPen, noteRect.Left + 4, noteRect.Top + 4, noteRect.Right - 4, noteRect.Bottom - 4);
            g.DrawLine(missPen, noteRect.Right - 4, noteRect.Top + 4, noteRect.Left + 4, noteRect.Bottom - 4);
        }

        if (note.State == NoteState.Hit && _visualSkin.HitBurst is not null)
            DrawImageAlpha(g, _visualSkin.HitBurst, Rectangle.Inflate(noteRect, nw / 3, nh), Math.Min(220, resolvedAlpha));
        if (note.State == NoteState.Miss && _visualSkin.MissEffect is not null)
            DrawImageAlpha(g, _visualSkin.MissEffect, Rectangle.Inflate(noteRect, nw / 4, nh / 2), Math.Min(230, resolvedAlpha));

        // 노트 테두리 — 캐시된 펜
        if (_colorVisionMode > 0 || UseHighContrast)
        {
            Font labelFont = _renderResources.Font("Segoe UI", Math.Max(7f, laneWidth * 0.08f), FontStyle.Bold);
            SolidBrush labelBrush = _renderResources.Brush(UseHighContrast ? Color.Black : Color.White);
            string noteLabel = LaneLabels[Math.Min(note.Lane, LaneLabels.Length - 1)];
            DrawCentered(g, noteLabel, labelFont, labelBrush, nx + nw / 2, ny + Math.Max(1, nh / 8));
        }

        g.DrawPath(_noteBorderPen, notePath);
    }

    private void DrawSlideArrowSkin(Graphics g, int startX, int startY, int endX, int endY, int laneWidth, int alpha)
    {
        float dx = endX - startX;
        float dy = endY - startY;
        if (Math.Abs(dx) + Math.Abs(dy) < 1f)
            return;

        float centerX = (startX + endX) / 2f;
        float centerY = (startY + endY) / 2f;
        float angle = MathF.Atan2(dy, dx) * 180f / MathF.PI;
        int size = Math.Max(20, (int)(laneWidth * 0.32f));
        Rectangle bounds = new((int)centerX - size / 2, (int)centerY - size / 2, size, size);

        var state = g.Save();
        g.TranslateTransform(centerX, centerY);
        g.RotateTransform(angle);
        g.TranslateTransform(-centerX, -centerY);

        if (_visualSkin.SlideArrow is not null)
        {
            DrawImageAlpha(g, _visualSkin.SlideArrow, bounds, Math.Min(230, alpha));
        }
        else
        {
            using var arrowBrush = new SolidBrush(Color.FromArgb(Math.Min(190, alpha), 255, 255, 255));
            Point[] arrow =
            [
                new(bounds.Right, bounds.Top + bounds.Height / 2),
                new(bounds.Left, bounds.Top),
                new(bounds.Left + bounds.Width / 3, bounds.Top + bounds.Height / 2),
                new(bounds.Left, bounds.Bottom),
            ];
            g.FillPolygon(arrowBrush, arrow);
        }

        g.Restore(state);
    }

    private void DrawRhythmStyleHud(Graphics g, Rectangle playArea, int hitY)
    {
        float s = GameScale;
        SongEntry? song = GetSelectedSong();
        var score = _engine.Score;
        Color accent = GetAccentColor();
        SolidBrush titleBrush = _renderResources.Brush(Color.FromArgb(244, 248, 255));
        SolidBrush accentBrush = _renderResources.Brush(Color.FromArgb(230, accent));

        Font logoFont = _renderResources.Font("Segoe UI Light", Math.Max(24f, 42f * s), FontStyle.Regular);
        Font songFont = _renderResources.Font("Segoe UI", Math.Max(12f, 24f * s), FontStyle.Regular);
        Font artistFont = _renderResources.Font("Segoe UI", Math.Max(8f, 14f * s), FontStyle.Regular);
        Font labelFont = _renderResources.Font("Segoe UI", Math.Max(7f, 12f * s), FontStyle.Regular);

        DrawSpacedString(g, "MuWorld", logoFont, titleBrush, ClientSize.Width / 2f, 28f * s, 10f * s, centered: true);

        Rectangle art = Rectangle.Round(GetSongArtworkBounds());
        DrawSongArtwork(g, art, song);
        using (var artBorder = new Pen(Color.FromArgb(145, 225, 232, 255), Math.Max(1f, s)))
            g.DrawRectangle(artBorder, art);
        g.DrawString(song?.Title ?? "Starlight Drive", songFont, titleBrush, art.Right + 20f * s, 38f * s);
        g.DrawString(song?.Artist ?? "Aureon", artistFont, accentBrush, art.Right + 20f * s, 72f * s);

        float current = _lastPlaybackPositionSeconds > 0f ? _lastPlaybackPositionSeconds : _engine.CurrentChartTime;
        float duration = song?.DurationSeconds > 0f ? song.DurationSeconds : _audio.GetInGameBgmDurationSeconds() ?? Math.Max(1f, _selectedChartNotes.Count > 0 ? _selectedChartNotes.Max(n => n.Time + n.Duration) : 138f);
        RectangleF progressRail = GetProgressRailBounds();
        DrawProgressRail(g, current, duration, progressRail.Left, progressRail.Top, progressRail.Width);

        Rectangle difficulty = Rectangle.Round(GetDifficultyBadgeBounds());
        using (var path = CreateRoundedRect(difficulty, 5f * s))
        using (var border = new Pen(Color.FromArgb(190, accent), Math.Max(1f, 1.3f * s)))
        using (var fill = new SolidBrush(Color.FromArgb(22, accent)))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }
        DrawFittedSpacedString(
            g,
            GetDifficultyLabel(_songSelectDifficultyIndex).ToUpperInvariant(),
            labelFont,
            titleBrush,
            new RectangleF(difficulty.Left + 6f * s, difficulty.Top, difficulty.Width - 12f * s, difficulty.Height),
            difficulty.Top + 7f * s,
            3.5f * s,
            centered: true);

        DrawSpeedPanel(g, GetSpeedPanelBounds());
        DrawScorePanel(g, GetScorePanelBounds(), score);

        if (score.Combo > 0)
        {
            if (score.Combo != _cachedComboValue)
            {
                _cachedComboValue = score.Combo;
                _cachedComboText = score.Combo.ToString();
            }

            Font comboLabel = _renderResources.Font("Segoe UI", Math.Max(10f, 18f * s), FontStyle.Regular);
            Font comboValue = _renderResources.Font("Segoe UI Light", Math.Max(48f, 82f * s), FontStyle.Regular);
            DrawSpacedString(g, "COMBO", comboLabel, titleBrush, playArea.Left + playArea.Width / 2f, hitY - 405f * s, 7f * s, centered: true);
            DrawSpacedString(g, _cachedComboText, comboValue, titleBrush, playArea.Left + playArea.Width / 2f, hitY - 372f * s, 10f * s, centered: true);
        }

        if (_feedback is not null)
        {
            // DrawGame renders the animated feedback later; this keeps the center clear for it.
        }

        if (score.PerfectCount != _cachedStatsPerfect || score.GreatCount != _cachedStatsGreat ||
            score.BetterCount != _cachedStatsBetter || score.GoodCount != _cachedStatsGood ||
            score.BadCount != _cachedStatsBad || score.MissCount != _cachedStatsMiss)
        {
            _cachedStatsPerfect = score.PerfectCount;
            _cachedStatsGreat = score.GreatCount;
            _cachedStatsBetter = score.BetterCount;
            _cachedStatsGood = score.GoodCount;
            _cachedStatsBad = score.BadCount;
            _cachedStatsMiss = score.MissCount;
            _cachedStatsText = $"P {score.PerfectCount}  GR {score.GreatCount}  G {score.GoodCount}  B {score.BadCount}  M {score.MissCount}";
        }
    }

    private RectangleF GetSongArtworkBounds()
    {
        float s = GameScale;
        return new RectangleF(32f * s, 28f * s, 70f * s, 70f * s);
    }

    private RectangleF GetProgressRailBounds()
    {
        float s = GameScale;
        float left = 32f * s;
        float right = Math.Min(ClientSize.Width * 0.74f, GetScorePanelBounds().Left - 36f * s);
        return new RectangleF(left, 124f * s, Math.Max(120f * s, right - left), 1f);
    }

    private RectangleF GetDifficultyBadgeBounds()
    {
        float s = GameScale;
        return new RectangleF(ClientSize.Width - 136f * s, 32f * s, 92f * s, 31f * s);
    }

    private RectangleF GetScorePanelBounds()
    {
        float s = GameScale;
        return new RectangleF(ClientSize.Width - 270f * s, 124f * s, 230f * s, 220f * s);
    }

    private void DrawProgressRail(Graphics g, float current, float duration, float x, float y, float width)
    {
        float s = GameScale;
        float ratio = Math.Clamp(current / Math.Max(1f, duration), 0f, 1f);
        using var railPen = new Pen(Color.FromArgb(68, 165, 182, 225), Math.Max(1f, s));
        using var fillPen = new Pen(Color.FromArgb(210, 126, 154, 255), Math.Max(2f, 2.5f * s))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        g.DrawLine(railPen, x, y, x + width, y);
        g.DrawLine(fillPen, x, y, x + width * ratio, y);
        using var knob = new SolidBrush(Color.FromArgb(245, 238, 242, 255));
        g.FillEllipse(knob, x + width * ratio - 5f * s, y - 5f * s, 10f * s, 10f * s);
        Font timeFont = _renderResources.Font("Segoe UI", Math.Max(8f, 13f * s), FontStyle.Regular);
        SolidBrush timeBrush = _renderResources.Brush(Color.FromArgb(190, 235, 238, 250));
        g.DrawString(FormatSongDuration(current), timeFont, timeBrush, x, y + 14f * s);
        string total = FormatSongDuration(duration);
        SizeF totalSize = g.MeasureString(total, timeFont);
        g.DrawString(total, timeFont, timeBrush, x + width - totalSize.Width, y + 14f * s);
    }

    private void DrawSpeedPanel(Graphics g, RectangleF bounds)
    {
        float s = GameScale;
        using var path = CreateRoundedRect(Rectangle.Round(bounds), 12f * s);
        using var fill = new SolidBrush(Color.FromArgb(74, 7, 9, 20));
        using var border = new Pen(Color.FromArgb(124, 158, 144, 230), Math.Max(1f, 1.2f * s));
        g.FillPath(fill, path);
        g.DrawPath(border, path);

        Font label = _renderResources.Font("Segoe UI", Math.Max(8f, 14f * s), FontStyle.Regular);
        Font value = _renderResources.Font("Segoe UI Light", Math.Max(26f, 42f * s), FontStyle.Regular);
        SolidBrush title = _renderResources.Brush(Color.FromArgb(234, 242, 245, 255));
        DrawSpacedString(g, "HI-SPEED", label, title, bounds.Left + bounds.Width / 2f, bounds.Top + 22f * s, 5f * s, centered: true);
        RectangleF valueBounds = GetSpeedValueBounds(bounds);
        DrawCentered(g, $"{EffectiveSpeedMultiplier:F1}x", value, title, valueBounds.Left + valueBounds.Width / 2f, valueBounds.Top);

        RectangleF minusButton = GetSpeedMinusButtonBounds(bounds);
        RectangleF plusButton = GetSpeedPlusButtonBounds(bounds);
        DrawMiniSpeedButton(g, minusButton, "-");
        DrawMiniSpeedButton(g, plusButton, "+");

        using var tickPen = new Pen(Color.FromArgb(128, 215, 224, 255), Math.Max(1f, s));
        RectangleF railBounds = GetSpeedRailBounds(bounds);
        float railLeft = railBounds.Left;
        float railRight = railBounds.Right;
        float railY = railBounds.Top + railBounds.Height / 2f;
        for (int i = 0; i <= 12; i++)
        {
            float x = railLeft + (railRight - railLeft) * i / 12f;
            float tick = i == 6 ? 13f * s : 7f * s;
            g.DrawLine(tickPen, x, railY - tick / 2f, x, railY + tick / 2f);
        }
        using var knobPen = new Pen(Color.FromArgb(220, GetAccentColor()), Math.Max(2f, 2.4f * s));
        float knobX = railLeft + (railRight - railLeft) * Math.Clamp((EffectiveSpeedMultiplier - 0.5f) / 2.0f, 0f, 1f);
        g.DrawLine(knobPen, knobX, railY - 14f * s, knobX, railY + 14f * s);
    }

    private RectangleF GetSpeedPanelBounds()
    {
        float s = GameScale;
        return new RectangleF(42f * s, ClientSize.Height * 0.37f, 300f * s, 198f * s);
    }

    private RectangleF GetSpeedValueBounds(RectangleF panel)
    {
        float s = GameScale;
        return new RectangleF(panel.Left + 72f * s, panel.Top + 58f * s, panel.Width - 144f * s, 52f * s);
    }

    private RectangleF GetSpeedMinusButtonBounds(RectangleF panel)
    {
        float s = GameScale;
        return new RectangleF(panel.Left + 22f * s, panel.Bottom - 66f * s, 48f * s, 44f * s);
    }

    private RectangleF GetSpeedPlusButtonBounds(RectangleF panel)
    {
        float s = GameScale;
        return new RectangleF(panel.Right - 70f * s, panel.Bottom - 66f * s, 48f * s, 44f * s);
    }

    private RectangleF GetSpeedRailBounds(RectangleF panel)
    {
        float s = GameScale;
        return new RectangleF(panel.Left + 92f * s, panel.Bottom - 53f * s, panel.Width - 184f * s, 24f * s);
    }

    private void DrawMiniSpeedButton(Graphics g, RectangleF bounds, string text)
    {
        using var path = CreateRoundedRect(Rectangle.Round(bounds), 6f * GameScale);
        using var fill = new SolidBrush(Color.FromArgb(34, 110, 92, 190));
        using var border = new Pen(Color.FromArgb(170, 170, 154, 255), Math.Max(1f, GameScale));
        Font font = _renderResources.Font("Segoe UI", Math.Max(18f, 26f * GameScale), FontStyle.Regular);
        SolidBrush brush = _renderResources.Brush(Color.White);
        g.FillPath(fill, path);
        g.DrawPath(border, path);
        DrawCentered(g, text, font, brush, bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f - g.MeasureString(text, font).Height / 2f);
    }

    private void DrawScorePanel(Graphics g, RectangleF bounds, ScoreManager score)
    {
        float s = GameScale;
        Font label = _renderResources.Font("Segoe UI", Math.Max(7f, 12f * s), FontStyle.Regular);
        Font value = _renderResources.Font("Segoe UI Light", Math.Max(18f, 38f * s), FontStyle.Regular);
        Font smallValue = _renderResources.Font("Segoe UI", Math.Max(12f, 18f * s), FontStyle.Regular);
        SolidBrush labelBrush = _renderResources.Brush(Color.FromArgb(176, 214, 220, 238));
        SolidBrush valueBrush = _renderResources.Brush(Color.White);

        DrawSpacedString(g, "SCORE", label, labelBrush, bounds.Left, bounds.Top, 4f * s, centered: false);
        DrawFittedSpacedString(
            g,
            score.Score.ToString().PadLeft(7, '0'),
            value,
            valueBrush,
            new RectangleF(bounds.Left, bounds.Top, bounds.Width, bounds.Height),
            bounds.Top + 28f * s,
            6f * s,
            centered: false);
        DrawSpacedString(g, "ACCURACY", label, labelBrush, bounds.Left, bounds.Top + 94f * s, 4f * s, centered: false);
        g.DrawString($"{score.Accuracy:F2}%", smallValue, valueBrush, bounds.Left, bounds.Top + 124f * s);
        DrawSpacedString(g, "LIFE", label, labelBrush, bounds.Left, bounds.Top + 166f * s, 4f * s, centered: false);

        GaugeRule rule = GetGaugeRule(_songSelectDifficultyIndex);
        float ratio = Math.Clamp(_grooveGauge / 100f, 0f, 1f);
        RectangleF rail = new(bounds.Left, bounds.Top + 196f * s, 150f * s, 5f * s);
        using var bg = new SolidBrush(Color.FromArgb(66, 80, 84, 110));
        using var fill = new LinearGradientBrush(rail, Color.FromArgb(230, 95, 122, 255), Color.FromArgb(230, 168, 111, 255), LinearGradientMode.Horizontal);
        g.FillRectangle(bg, rail);
        g.FillRectangle(fill, rail.Left, rail.Top, rail.Width * ratio, rail.Height);
        Font lifeFont = _renderResources.Font("Segoe UI", Math.Max(8f, 13f * s), FontStyle.Regular);
        SolidBrush lifeBrush = _renderResources.Brush(IsGaugeDanger() ? Color.FromArgb(255, 220, 220) : Color.FromArgb(220, 238, 240, 255));
        g.DrawString($"♡ {_grooveGauge:F0}%", lifeFont, lifeBrush, rail.Right + 16f * s, rail.Top - 9f * s);
        if (!IsNoFailPlayMode)
        {
            float thresholdX = rail.Left + rail.Width * Math.Clamp(rule.ClearThreshold / 100f, 0f, 1f);
            using var threshold = new Pen(Color.FromArgb(180, 255, 238, 156), Math.Max(1f, s));
            g.DrawLine(threshold, thresholdX, rail.Top - 4f * s, thresholdX, rail.Bottom + 4f * s);
        }
    }

    private void DrawGameHudDeck(Graphics g, Rectangle playArea, int hitY)
    {
        if (ClientSize.Width >= 0)
        {
            DrawRhythmStyleHud(g, playArea, hitY);
            return;
        }

        int centerX = playArea.Left + playArea.Width / 2;
        var score = _engine.Score;
        float accuracy = score.Accuracy;

        Rectangle topBar = new(playArea.Left, 0, playArea.Width, (int)Math.Round(82f * _layoutScale));
        using (var topFill = new LinearGradientBrush(topBar, Color.FromArgb(205, 8, 13, 25), Color.FromArgb(80, 8, 13, 25), LinearGradientMode.Vertical))
            g.FillRectangle(topFill, topBar);

        Font labelFont = _renderResources.Font("Segoe UI", Math.Max(8f, 12f * _layoutScale), FontStyle.Bold);
        Font valueFont = _renderResources.Font("Segoe UI", Math.Max(12f, 22f * _layoutScale), FontStyle.Bold);
        Font comboFont = _renderResources.Font("Segoe UI", Math.Max(24f, 56f * _layoutScale), FontStyle.Bold);
        SolidBrush labelBrush = _renderResources.Brush(Color.FromArgb(190, 150, 165, 195));
        SolidBrush valueBrush = _renderResources.Brush(Color.White);
        SolidBrush accentBrush = _renderResources.Brush(GetAccentColor());

        g.DrawString("SCORE", labelFont, labelBrush, playArea.Left + ScaleX(16f), ScaleY(10f));
        g.DrawString(score.Score.ToString("N0"), valueFont, valueBrush, playArea.Left + ScaleX(14f), ScaleY(26f));

        string accText = $"{accuracy:F1}%";
        SizeF accSize = g.MeasureString(accText, valueFont);
        g.DrawString("SYNC", labelFont, labelBrush, playArea.Right - accSize.Width - ScaleX(18f), ScaleY(10f));
        g.DrawString(accText, valueFont, accentBrush, playArea.Right - accSize.Width - ScaleX(16f), ScaleY(26f));

        DrawGrooveGauge(g, playArea, topBar, labelFont);
        DrawHudMetaRow(g, playArea, topBar, labelFont);

        if (score.Combo > 0)
        {
            if (score.Combo != _cachedComboValue)
            {
                _cachedComboValue = score.Combo;
                _cachedComboText = score.Combo.ToString();
            }

            DrawCentered(g, _cachedComboText, comboFont, valueBrush, centerX, (int)ScaleY(92f));
            DrawCentered(g, "COMBO", labelFont, accentBrush, centerX, (int)ScaleY(158f));
        }

        if (score.PerfectCount != _cachedStatsPerfect || score.GreatCount != _cachedStatsGreat ||
            score.BetterCount != _cachedStatsBetter || score.GoodCount != _cachedStatsGood ||
            score.BadCount != _cachedStatsBad || score.MissCount != _cachedStatsMiss)
        {
            _cachedStatsPerfect = score.PerfectCount;
            _cachedStatsGreat = score.GreatCount;
            _cachedStatsBetter = score.BetterCount;
            _cachedStatsGood = score.GoodCount;
            _cachedStatsBad = score.BadCount;
            _cachedStatsMiss = score.MissCount;
            _cachedStatsText = $"P {score.PerfectCount}   GR {score.GreatCount}   G {score.GoodCount}   B {score.BadCount}   M {score.MissCount}";
        }

        SizeF statsSize = g.MeasureString(_cachedStatsText, _statFont);
        RectangleF statsRect = new(centerX - statsSize.Width / 2f - ScaleX(12f), hitY + ScaleY(40f), statsSize.Width + ScaleX(24f), statsSize.Height + ScaleY(8f));
        SolidBrush statsBg = _renderResources.Brush(Color.FromArgb(150, 8, 13, 25));
        g.FillRectangle(statsBg, statsRect);
        g.DrawString(_cachedStatsText, _statFont, _statBrush, statsRect.Left + ScaleX(12f), statsRect.Top + ScaleY(4f));
    }

    private void DrawComboMilestone(Graphics g, Rectangle playArea, int hitY)
    {
        if (string.IsNullOrWhiteSpace(_comboMilestoneText))
            return;

        float elapsed = (float)(DateTime.Now - _comboMilestoneTime).TotalMilliseconds;
        const float duration = 1100f;
        if (elapsed >= duration)
            return;

        float progress = elapsed / duration;
        int alpha = (int)(230 * (1f - progress));
        float lift = _reducedMotionEnabled ? 0f : progress * ScaleY(16f);
        Color accent = GetAccentColor();
        int width = Math.Min((int)ScaleX(230f), playArea.Width - 24);
        Rectangle bounds = Rectangle.Round(new RectangleF(
            playArea.Left + (playArea.Width - width) / 2f,
            hitY - ScaleY(160f) - lift,
            width,
            ScaleY(34f)));

        using var path = CreateRoundedRect(bounds, ScaleY(8f));
        using var fill = new SolidBrush(Color.FromArgb(Math.Clamp(alpha / 2, 0, 120), accent));
        using var border = new Pen(Color.FromArgb(Math.Clamp(alpha, 0, 220), accent), Math.Max(1f, ScaleY(1.4f)));
        using var textBrush = new SolidBrush(Color.FromArgb(Math.Clamp(alpha, 0, 255), Color.White));
        using var font = new Font("Segoe UI", Math.Max(8f, 14f * _layoutScale), FontStyle.Bold);
        g.FillPath(fill, path);
        g.DrawPath(border, path);
        DrawCentered(g, _comboMilestoneText, font, textBrush, bounds.Left + bounds.Width / 2, bounds.Top + (int)ScaleY(7f));
    }

    private void DrawHudMetaRow(Graphics g, Rectangle playArea, Rectangle topBar, Font font)
    {
        (string label, string value)[] chips =
        [
            ("SPD", $"x{EffectiveSpeedMultiplier:F1}"),
            ("MODE", GetGameModeLabel()),
            ("LANE", $"{LaneCount}K"),
            ("PLAY", PlayModeLabels[Math.Clamp(EffectivePlayModeIndex, 0, PlayModeLabels.Length - 1)]),
        ];

        int gap = Math.Max(3, (int)ScaleX(4f));
        int chipWidth = Math.Max(58, (playArea.Width - gap * (chips.Length - 1)) / chips.Length);
        int chipHeight = Math.Max(17, (int)ScaleY(19f));
        int y = topBar.Bottom - chipHeight - Math.Max(3, (int)ScaleY(4f));
        SolidBrush labelBrush = _renderResources.Brush(Color.FromArgb(170, 170, 188, 220));
        SolidBrush valueBrush = _renderResources.Brush(Color.FromArgb(230, 244, 248, 255));
        Pen border = _renderResources.Pen(Color.FromArgb(90, 115, 145, 205), Math.Max(1f, ScaleY(1f)));
        SolidBrush fill = _renderResources.Brush(Color.FromArgb(82, 12, 18, 34));

        for (int i = 0; i < chips.Length; i++)
        {
            Rectangle bounds = new(playArea.Left + i * (chipWidth + gap), y, chipWidth, chipHeight);
            using var path = CreateRoundedRect(bounds, ScaleY(5f));
            g.FillPath(fill, path);
            g.DrawPath(border, path);

            string text = $"{chips[i].label} {chips[i].value}";
            SizeF textSize = g.MeasureString(text, font);
            Brush brush = i == 1 && EffectiveGameMode != GameMode.Normal ? valueBrush : labelBrush;
            g.DrawString(text, font, brush, bounds.Left + (bounds.Width - textSize.Width) / 2f, bounds.Top + (bounds.Height - textSize.Height) / 2f);
        }
    }

    private void DrawGrooveGauge(Graphics g, Rectangle playArea, Rectangle topBar, Font labelFont)
    {
        GaugeRule rule = GetGaugeRule(_songSelectDifficultyIndex);
        float ratio = Math.Clamp(_grooveGauge / 100f, 0f, 1f);
        bool danger = IsGaugeDanger();
        int gaugeWidth = (int)Math.Min(ScaleX(310f), playArea.Width * 0.34f);
        Rectangle gauge = Rectangle.Round(new RectangleF(
            playArea.Left + (playArea.Width - gaugeWidth) / 2f,
            topBar.Top + ScaleY(20f),
            gaugeWidth,
            ScaleY(13f)));

        Color fillColor = IsNoFailPlayMode
            ? IsAutoPlayMode ? Color.FromArgb(118, 235, 186) : Color.FromArgb(112, 178, 255)
            : danger
                ? Color.FromArgb(255, 82, 72)
                : GetAccentColor();

        SolidBrush bg = _renderResources.Brush(Color.FromArgb(120, 20, 26, 42));
        Pen border = _renderResources.Pen(Color.FromArgb(155, 145, 165, 205), Math.Max(1f, ScaleY(1.1f)));
        g.FillRectangle(bg, gauge);
        g.DrawRectangle(border, gauge);

        Rectangle fill = new(gauge.Left + 1, gauge.Top + 1, Math.Max(0, (int)((gauge.Width - 2) * ratio)), Math.Max(1, gauge.Height - 2));
        if (fill.Width > 0)
        {
            using var fillBrush = new LinearGradientBrush(fill, Color.FromArgb(230, fillColor), Color.FromArgb(145, fillColor), LinearGradientMode.Vertical);
            g.FillRectangle(fillBrush, fill);
        }

        int thresholdX = gauge.Left + (int)(gauge.Width * Math.Clamp(rule.ClearThreshold / 100f, 0f, 1f));
        Pen thresholdPen = _renderResources.Pen(Color.FromArgb(230, 255, 235, 135), Math.Max(1f, ScaleY(1.4f)));
        g.DrawLine(thresholdPen, thresholdX, gauge.Top - 2, thresholdX, gauge.Bottom + 2);

        string mode = PlayModeLabels[Math.Clamp(EffectivePlayModeIndex, 0, PlayModeLabels.Length - 1)];
        string label = IsNoFailPlayMode
            ? $"{mode}  {_grooveGauge:F0}%"
            : $"GROOVE  {_grooveGauge:F0}% / CLEAR {rule.ClearThreshold:F0}%";
        SolidBrush textBrush = _renderResources.Brush(danger ? Color.FromArgb(255, 230, 225) : Color.FromArgb(218, 232, 255));
        DrawCentered(g, label, labelFont, textBrush, gauge.Left + gauge.Width / 2, gauge.Bottom + (int)ScaleY(5f));
    }

    private void DrawGaugeDangerOverlay(Graphics g, Rectangle playArea, int hitY)
    {
        if (!IsGaugeDanger())
            return;

        float pulse = _reducedMotionEnabled
            ? 0.65f
            : 0.55f + MathF.Sin((float)DateTime.Now.TimeOfDay.TotalSeconds * 8f) * 0.18f;
        int alpha = Math.Clamp((int)(95f * pulse), 30, 120);
        using var edgePen = new Pen(Color.FromArgb(alpha + 85, 255, 72, 68), Math.Max(2f, ScaleY(4f)));
        using var hitBrush = new SolidBrush(Color.FromArgb(alpha, 255, 40, 36));
        using var textBrush = new SolidBrush(Color.FromArgb(230, 255, 228, 228));
        Font warningFont = _renderResources.Font("Segoe UI", Math.Max(9f, 15f * _layoutScale), FontStyle.Bold);

        g.DrawRectangle(edgePen, Rectangle.Inflate(playArea, -(int)ScaleX(2f), -(int)ScaleY(2f)));
        g.FillRectangle(hitBrush, playArea.Left, hitY - (int)ScaleY(8f), playArea.Width, (int)ScaleY(18f));
        DrawCentered(g, "LOW GROOVE", warningFont, textBrush, playArea.Left + playArea.Width / 2, hitY - (int)ScaleY(34f));
    }

    private void DrawGameHUD(Graphics g, Rectangle playArea, int hitY)
    {
        int centerX = playArea.Left + playArea.Width / 2;
        var score = _engine.Score;

        // ── COMBO 표시 (중앙 상단) — 값이 변경될 때만 문자열 재생성 ──
        if (score.Combo > 0)
        {
            DrawCentered(g, "COMBO", _comboLabelFont, _comboLabelBrush, centerX, 20);
            if (score.Combo != _cachedComboValue)
            {
                _cachedComboValue = score.Combo;
                _cachedComboText = score.Combo.ToString();
            }
            DrawCentered(g, _cachedComboText, _comboNumFont, _comboNumBrush, centerX, 35);
        }

        // ── 정확도 표시 (하단 중앙, 노란색 그라데이션) ──
        if (score.TotalJudgedNotes > 0)
        {
            float accuracy = score.Accuracy;
            string accText = $"{accuracy:F2}%";
            SizeF accSize = g.MeasureString(accText, _accFont);
            float accX = centerX - accSize.Width / 2f;
            float accY = playArea.Bottom - accSize.Height - 18f;
            RectangleF accRect = new(accX, accY, accSize.Width, accSize.Height);
            using var accBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                accRect,
                Color.FromArgb(255, 255, 230, 80),
                Color.FromArgb(255, 255, 180, 40),
                System.Drawing.Drawing2D.LinearGradientMode.Vertical);
            g.DrawString(accText, _accFont, accBrush, accX, accY);

            // MAX 표시 (정확도가 100%일 때)
            if (score.MissCount == 0 && score.BadCount == 0 && score.GoodCount == 0 && score.BetterCount == 0)
            {
                DrawCentered(g, "MAX 100%", _maxFont, _maxBrush, centerX, (int)(accY - 40));
            }
        }

        // ── 점수 (좌상단 작게) ──
        g.DrawString($"Score: {score.Score:N0}", _scoreFont, _scoreBrush, playArea.Left + 8, 10);

        // ── 우측 상단 통계 — 값이 변경될 때만 문자열 재생성 ──
        if (score.PerfectCount != _cachedStatsPerfect || score.GreatCount != _cachedStatsGreat ||
            score.BetterCount != _cachedStatsBetter || score.GoodCount != _cachedStatsGood ||
            score.BadCount != _cachedStatsBad || score.MissCount != _cachedStatsMiss)
        {
            _cachedStatsPerfect = score.PerfectCount;
            _cachedStatsGreat = score.GreatCount;
            _cachedStatsBetter = score.BetterCount;
            _cachedStatsGood = score.GoodCount;
            _cachedStatsBad = score.BadCount;
            _cachedStatsMiss = score.MissCount;
            _cachedStatsText = $"P {score.PerfectCount}  Gr {score.GreatCount}  Bt {score.BetterCount}  G {score.GoodCount}  B {score.BadCount}  M {score.MissCount}";
        }
        SizeF ssz = g.MeasureString(_cachedStatsText, _statFont);
        g.DrawString(_cachedStatsText, _statFont, _statBrush, playArea.Right - ssz.Width - 8, 10);
    }

    private void DrawCountdown(Graphics g)
    {
        int remain = _countdownSeconds - (int)(DateTime.Now - _countdownStartTime).TotalSeconds;
        remain = Math.Max(1, remain);

        DrawCentered(g, "Get Ready", _countdownTitleFont, _countdownTitleBrush, ClientSize.Width / 2, ClientSize.Height / 2 - 120);
        DrawCentered(g, remain.ToString(), _countdownNumFont, _countdownNumBrush, ClientSize.Width / 2, ClientSize.Height / 2 - 40);
    }

    private void OnMenuMouseMove(object? sender, MouseEventArgs e)
    {
        if (_engine.IsRunning)
        {
            if (_isGamePaused)
                HandlePauseOverlayMouseMove(e.Location);
            return;
        }

        if (_isCountdownActive)
        {
            Cursor = Cursors.Default;
            return;
        }

        Point logicalPoint = ToLogicalPoint(e.Location);

        if (_screen == UiScreen.Settings)
        {
            if (_draggedSlider != SettingsSlider.None)
            {
                UpdateSliderFromPoint(_draggedSlider, logicalPoint.X);
                Invalidate();
                return;
            }

            Cursor = IsSettingsInteractive(logicalPoint) ? Cursors.Hand : Cursors.Default;
            return;
        }

        if (_screen == UiScreen.SongSelect)
        {
            int hoverCode = GetSongSelectHoverCode(logicalPoint);
            bool searchHover = IsSongSearchBoxHit(logicalPoint);
            if (hoverCode != _hoverSongPlayIndex)
            {
                _hoverSongPlayIndex = hoverCode;
                Cursor = searchHover ? Cursors.IBeam : (IsSongSelectInteractive(logicalPoint) ? Cursors.Hand : Cursors.Default);
                Invalidate();
            }
            else
            {
                Cursor = searchHover ? Cursors.IBeam : (IsSongSelectInteractive(logicalPoint) ? Cursors.Hand : Cursors.Default);
            }
            return;
        }

        if (_screen == UiScreen.Achievement)
        {
            bool backHover = IsAchievementBackButtonHit(logicalPoint);
            int cardHover = GetHoveredAchievementCardIndex(logicalPoint);
            if (backHover != _isAchievementBackHovered || cardHover != _hoverAchievementCardIndex)
            {
                _isAchievementBackHovered = backHover;
                _hoverAchievementCardIndex = cardHover;
                Cursor = (backHover || cardHover >= 0) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            else
            {
                Cursor = (backHover || cardHover >= 0) ? Cursors.Hand : Cursors.Default;
            }
            return;
        }

        if (_screen == UiScreen.AchievementDetail)
        {
            bool backHover = IsAchievementDetailBackButtonHit(logicalPoint);
            int tabHover = GetHoveredAchievementDetailTabIndex(logicalPoint);
            int arrowHover = GetHoveredAchievementPageArrow(logicalPoint);
            if (backHover != _isAchievementDetailBackHovered || tabHover != _hoverAchievementDetailTabIndex || arrowHover != _hoverAchievementDetailPageArrow)
            {
                _isAchievementDetailBackHovered = backHover;
                _hoverAchievementDetailTabIndex = tabHover;
                _hoverAchievementDetailPageArrow = arrowHover;
                Cursor = (backHover || tabHover >= 0 || arrowHover >= 0) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            else
            {
                Cursor = (backHover || tabHover >= 0 || arrowHover >= 0) ? Cursors.Hand : Cursors.Default;
            }
            return;
        }

        if (_screen == UiScreen.InputCalibration)
        {
            bool backHover = GetCalibrationBackButtonBounds().Contains(logicalPoint);
            bool startHover = GetCalibrationStartButtonBounds().Contains(logicalPoint);
            if (backHover != _isCalibrationBackHovered || startHover != _isCalibrationStartHovered)
            {
                _isCalibrationBackHovered = backHover;
                _isCalibrationStartHovered = startHover;
                Cursor = (backHover || startHover) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            else
            {
                Cursor = (backHover || startHover) ? Cursors.Hand : Cursors.Default;
            }
            return;
        }

        if (_screen == UiScreen.InputCalibration)
        {
            if (_isCalibrationBackHovered || _isCalibrationStartHovered)
            {
                _isCalibrationBackHovered = false;
                _isCalibrationStartHovered = false;
                Cursor = Cursors.Default;
                Invalidate();
            }
            return;
        }

        if (_screen == UiScreen.KeyBindings)
        {
            bool changed = UpdateKeyBindingsHover(logicalPoint);
            Cursor = IsKeyBindingsInteractive(logicalPoint) ? Cursors.Hand : Cursors.Default;
            if (changed)
                Invalidate();
            return;
        }

        if (_screen == UiScreen.ChartEditor)
        {
            bool changed = UpdateChartEditorHover(logicalPoint);
            Cursor = IsChartEditorInteractive(logicalPoint) ? Cursors.Hand : Cursors.Default;
            if (changed)
                Invalidate();
            return;
        }

        if (_screen == UiScreen.KeyBindings)
        {
            if (_hoverKeyBindingLane != -1 || _hoverKeyBindingAction != -1)
            {
                _hoverKeyBindingLane = -1;
                _hoverKeyBindingAction = -1;
                Cursor = Cursors.Default;
                Invalidate();
            }
            return;
        }

        if (_screen == UiScreen.Analyze)
        {
            int hover = GetAnalyzeActionAt(logicalPoint);
            bool okHover = hover >= 0;
            if (hover != _hoverAnalyzeAction || okHover != _isAnalyzeOkHovered)
            {
                _hoverAnalyzeAction = hover;
                _isAnalyzeOkHovered = okHover;
                Cursor = okHover ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            else
            {
                Cursor = okHover ? Cursors.Hand : Cursors.Default;
            }
            return;
        }

        int hoverIndex = GetHoveredMenuIndex(logicalPoint);
        bool exitHovered = GetExitButtonBounds().Contains(logicalPoint);

        if (exitHovered != _isExitHovered || hoverIndex != _hoverMenuIndex)
        {
            _isExitHovered = exitHovered;
            _hoverMenuIndex = hoverIndex;
            Cursor = exitHovered || hoverIndex >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    private void OnMenuMouseLeave(object? sender, EventArgs e)
    {
        if (_engine.IsRunning)
        {
            if (_hoverPauseAction != -1)
            {
                _hoverPauseAction = -1;
                Cursor = Cursors.Default;
                Invalidate();
            }
            return;
        }

        if (_screen == UiScreen.Settings)
        {
            if (_draggedSlider == SettingsSlider.None)
                Cursor = Cursors.Default;
            return;
        }

        if (_screen == UiScreen.SongSelect)
        {
            if (_hoverSongPlayIndex != -1)
            {
                _hoverSongPlayIndex = -1;
                Cursor = Cursors.Default;
                Invalidate();
            }
            return;
        }

        if (_screen == UiScreen.Achievement)
        {
            if (_isAchievementBackHovered || _hoverAchievementCardIndex != -1)
            {
                _isAchievementBackHovered = false;
                _hoverAchievementCardIndex = -1;
                Cursor = Cursors.Default;
                Invalidate();
            }
            return;
        }

        if (_screen == UiScreen.AchievementDetail)
        {
            if (_isAchievementDetailBackHovered || _hoverAchievementDetailTabIndex != -1 || _hoverAchievementDetailPageArrow != -1)
            {
                _isAchievementDetailBackHovered = false;
                _hoverAchievementDetailTabIndex = -1;
                _hoverAchievementDetailPageArrow = -1;
                Cursor = Cursors.Default;
                Invalidate();
            }
            return;
        }

        if (_screen == UiScreen.Analyze)
        {
            if (_isAnalyzeOkHovered || _hoverAnalyzeAction != -1)
            {
                _isAnalyzeOkHovered = false;
                _hoverAnalyzeAction = -1;
                Cursor = Cursors.Default;
                Invalidate();
            }
            return;
        }

        if (_screen == UiScreen.ChartEditor)
        {
            if (_hoverChartEditorAction != -1)
            {
                _hoverChartEditorAction = -1;
                Cursor = Cursors.Default;
                Invalidate();
            }
            return;
        }

        if (_isExitHovered || _hoverMenuIndex != -1)
        {
            _isExitHovered = false;
            _hoverMenuIndex = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }
    }

    private void OnMenuMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (_engine.IsRunning)
        {
            if (_isGamePaused)
            {
                HandlePauseOverlayMouseDown(e.Location);
                return;
            }

            if (_isReplayPlayback)
                return;

            HandleGameplayMouseDown(e.Location);
            return;
        }

        if (_isCountdownActive)
            return;

        if (_screen == UiScreen.Splash)
        {
            TransitionFromSplash();
            return;
        }

        Point logicalPoint = ToLogicalPoint(e.Location);

        if (_screen == UiScreen.Settings)
        {
            HandleSettingsMouseDown(logicalPoint);
            return;
        }

        if (_screen == UiScreen.SongSelect)
        {
            HandleSongSelectMouseDown(logicalPoint);
            return;
        }

        if (_screen == UiScreen.SongDetail)
        {
            HandleSongDetailMouseDown(logicalPoint);
            return;
        }

        if (_screen == UiScreen.Achievement)
        {
            HandleAchievementMouseDown(logicalPoint);
            return;
        }

        if (_screen == UiScreen.AchievementDetail)
        {
            HandleAchievementDetailMouseDown(logicalPoint);
            return;
        }

        if (_screen == UiScreen.InputCalibration)
        {
            HandleInputCalibrationMouseDown(logicalPoint);
            return;
        }

        if (_screen == UiScreen.KeyBindings)
        {
            HandleKeyBindingsMouseDown(logicalPoint);
            return;
        }

        if (_screen == UiScreen.ChartEditor)
        {
            HandleChartEditorMouseDown(logicalPoint, e.Button);
            return;
        }

        if (_screen == UiScreen.Analyze)
        {
            HandleAnalyzeMouseDown(logicalPoint);
            return;
        }

        if (GetExitButtonBounds().Contains(logicalPoint))
        {
            Close();
            return;
        }

        int hoverIndex = GetHoveredMenuIndex(logicalPoint);
        if (hoverIndex >= 0)
        {
            if (hoverIndex == 0)
                _screen = UiScreen.Settings;
            else if (hoverIndex == 1)
                _screen = UiScreen.SongSelect;
            else if (hoverIndex == 2)
                _screen = UiScreen.Achievement;
            else if (hoverIndex == 3)
                RestartApplicationViaRunBat();
            Invalidate();
        }
    }

    private void OnMenuMouseUp(object? sender, MouseEventArgs e)
    {
        if (_engine.IsRunning)
        {
            if (!_isReplayPlayback)
                HandleGameplayMouseUp();
            return;
        }

        _draggedSlider = SettingsSlider.None;

        if (!_engine.IsRunning && _screen == UiScreen.Settings)
            Cursor = IsSettingsInteractive(ToLogicalPoint(e.Location)) ? Cursors.Hand : Cursors.Default;

        if (!_engine.IsRunning && _screen == UiScreen.SongSelect)
            Cursor = IsSongSelectInteractive(ToLogicalPoint(e.Location)) ? Cursors.Hand : Cursors.Default;

        if (!_engine.IsRunning && _screen == UiScreen.Achievement)
        {
            Point logicalPoint = ToLogicalPoint(e.Location);
            Cursor = (IsAchievementBackButtonHit(logicalPoint) || GetHoveredAchievementCardIndex(logicalPoint) >= 0)
                ? Cursors.Hand
                : Cursors.Default;
        }

        if (!_engine.IsRunning && _screen == UiScreen.AchievementDetail)
        {
            Point logicalPoint = ToLogicalPoint(e.Location);
            Cursor = (IsAchievementDetailBackButtonHit(logicalPoint) || GetHoveredAchievementDetailTabIndex(logicalPoint) >= 0)
                ? Cursors.Hand
                : Cursors.Default;
        }

        if (!_engine.IsRunning && _screen == UiScreen.InputCalibration)
        {
            Point logicalPoint = ToLogicalPoint(e.Location);
            Cursor = (GetCalibrationBackButtonBounds().Contains(logicalPoint) || GetCalibrationStartButtonBounds().Contains(logicalPoint))
                ? Cursors.Hand
                : Cursors.Default;
        }

        if (!_engine.IsRunning && _screen == UiScreen.KeyBindings)
            Cursor = IsKeyBindingsInteractive(ToLogicalPoint(e.Location)) ? Cursors.Hand : Cursors.Default;
    }

    private int GetHoveredMenuIndex(Point point)
    {
        if (GetMenuTopSettingsButtonBounds().Contains(point))
            return 0;

        if (GetMenuActionButtonBounds(1).Contains(point))
            return 1;

        if (GetMenuActionButtonBounds(3).Contains(point))
            return 3;

        if (GetMenuPlayerBadgeBounds().Contains(point))
            return 2;

        return -1;
    }

    private Rectangle GetMenuActionButtonBounds(int index)
    {
        return index switch
        {
            1 => MenuRect(584f, 412f, 512f, 100f),
            3 => MenuRect(615f, 552f, 450f, 58f),
            _ => Rectangle.Empty,
        };
    }

    private Rectangle GetMenuPlayerBadgeBounds()
    {
        return MenuRect(24f, 842f, 220f, 74f);
    }

    private Rectangle GetMenuTopSettingsButtonBounds()
    {
        return MenuRect(1590f, 30f, 44f, 44f);
    }

    private void DrawMainMenuBackground(Graphics g, Rectangle bounds)
    {
        using (var baseBrush = new LinearGradientBrush(bounds, Color.FromArgb(3, 6, 12), Color.FromArgb(8, 12, 26), LinearGradientMode.Vertical))
            g.FillRectangle(baseBrush, bounds);

        using (var vignette = new GraphicsPath())
        {
            vignette.AddEllipse(MenuX(-72f), MenuY(-84f), MenuS(1824f), MenuS(1094f));
            using var shade = new PathGradientBrush(vignette)
            {
                CenterColor = Color.FromArgb(0, 0, 0, 0),
                SurroundColors = [Color.FromArgb(128, 0, 0, 0)]
            };
            g.FillRectangle(shade, bounds);
        }

        using (var hazeBrush = new LinearGradientBrush(
            MenuRect(0f, 780f, MainMenuDesignWidth, 118f),
            Color.FromArgb(0, 20, 35, 80),
            Color.FromArgb(60, 58, 70, 180),
            LinearGradientMode.Vertical))
        {
            g.FillRectangle(hazeBrush, MenuX(0f), MenuY(780f), MenuS(MainMenuDesignWidth), MenuS(118f));
        }

        DrawMenuTexture(g, bounds);
        DrawMenuBottomGlow(g);
    }

    private void DrawMenuTexture(Graphics g, Rectangle bounds)
    {
        using var verticalPen = new Pen(Color.FromArgb(7, 110, 130, 180), Math.Max(1f, MenuS(1f)));
        for (float x = 34f; x < MainMenuDesignWidth; x += 64f)
        {
            float wobble = (float)Math.Sin(x * 0.09f) * MenuS(5f);
            g.DrawLine(verticalPen, MenuX(x) + wobble, bounds.Top, MenuX(x - 18f), bounds.Bottom);
        }

        using var scratchPen = new Pen(Color.FromArgb(10, 220, 230, 255), Math.Max(1f, MenuS(1f)));
        for (int i = 0; i < 42; i++)
        {
            float seed = i * 37.31f;
            float x = MenuX((seed * 13f) % MainMenuDesignWidth);
            float y = MenuY(90f + (seed * 7.7f) % 690f);
            float length = MenuS(26f + (i % 5) * 14f);
            g.DrawLine(scratchPen, x, y, x + MenuS((i % 3) - 1), y + length);
        }
    }

    private void DrawMenuBottomGlow(Graphics g)
    {
        float y = MenuY(887f);
        using var linePen = new Pen(Color.FromArgb(52, 70, 118, 210), Math.Max(1f, MenuS(1f)));
        g.DrawLine(linePen, MenuX(86f), y, MenuX(1544f), y);

        RectangleF glow = new(MenuX(610f), MenuY(846f), MenuS(460f), MenuS(92f));
        using var path = new GraphicsPath();
        path.AddEllipse(glow);
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(136, 150, 115, 255),
            SurroundColors = [Color.FromArgb(0, 44, 62, 170)]
        };
        g.FillPath(brush, path);

        using var corePen = new Pen(Color.FromArgb(156, 255, 214, 170), Math.Max(1f, MenuS(1.2f)));
        g.DrawLine(corePen, MenuX(724f), y, MenuX(956f), y);
    }

    private void DrawRhythmBrand(Graphics g, float x, float y, Font font, Brush brush)
    {
        using var pen = new Pen(Color.FromArgb(220, 232, 238, 252), Math.Max(1f, MenuS(1.5f)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        float barX = x;
        float barY = y + MenuS(3f);
        float[] heights = [MenuS(22f), MenuS(34f), MenuS(48f), MenuS(34f), MenuS(22f)];
        for (int i = 0; i < heights.Length; i++)
        {
            float bx = barX + MenuS(i * 7.2f);
            g.DrawLine(pen, bx, barY + (MenuS(48f) - heights[i]) / 2f, bx, barY + (MenuS(48f) + heights[i]) / 2f);
        }

        DrawSpacedString(g, "RHYTHM GAME", font, brush, x + MenuS(52f), y + MenuS(11f), MenuS(8.6f), centered: false);
    }

    private void DrawGlowingSpacedText(Graphics g, string text, Font font, Brush brush, float centerX, float y, float spacing)
    {
        using var glowBrush = new SolidBrush(Color.FromArgb(52, 112, 150, 255));
        for (int i = 5; i >= 1; i--)
            DrawSpacedString(g, text, font, glowBrush, centerX + MenuS(i * 0.6f), y + MenuS(i * 0.2f), spacing, centered: true);

        DrawSpacedString(g, text, font, brush, centerX, y, spacing, centered: true);
    }

    private void DrawMenuTagline(Graphics g, float centerX, float y, Font font, Brush brush, Color accent)
    {
        using var pen = new Pen(Color.FromArgb(170, accent), Math.Max(1f, MenuS(1.2f)));
        g.DrawLine(pen, centerX - MenuS(264f), y + MenuS(11f), centerX - MenuS(198f), y + MenuS(11f));
        g.DrawLine(pen, centerX + MenuS(198f), y + MenuS(11f), centerX + MenuS(264f), y + MenuS(11f));
    }

    private void DrawPlayMenuButton(Graphics g, Rectangle bounds, bool hovered, Font font, Color accent)
    {
        Rectangle drawBounds = bounds;
        if (hovered)
            drawBounds.Offset(0, -MenuMotionOffsetY(2f));

        using var path = CreateRoundedRect(drawBounds, MenuS(7f));
        using var fill = new LinearGradientBrush(drawBounds,
            Color.FromArgb(hovered ? 38 : 24, 38, 52, 78),
            Color.FromArgb(hovered ? 20 : 10, 4, 8, 18),
            LinearGradientMode.Vertical);
        using var border = new Pen(hovered ? Color.White : Color.FromArgb(235, 118, 184, 255), Math.Max(1.2f, MenuS(1.4f)));
        using var glowPen = new Pen(Color.FromArgb(75, accent), Math.Max(5f, MenuS(6f)));
        g.FillPath(fill, path);
        g.DrawPath(glowPen, path);
        g.DrawPath(border, path);

        DrawPlayGlyph(g, new Rectangle(drawBounds.Left + (int)MenuS(68f), drawBounds.Top + (int)MenuS(34f), (int)MenuS(28f), (int)MenuS(32f)), Color.White);
        using var textBrush = new SolidBrush(Color.FromArgb(238, 244, 255));
        DrawSpacedString(g, "PLAY", font, textBrush, drawBounds.Left + drawBounds.Width / 2f, drawBounds.Top + MenuS(35f), MenuS(18f), centered: true);
    }

    private void DrawSecondaryMenuRow(Graphics g, Rectangle bounds, string text, bool hovered, Font font, Color accent, Action<Graphics, Rectangle, Color> drawIcon)
    {
        Rectangle drawBounds = bounds;
        if (hovered)
            drawBounds.Offset(0, -MenuMotionOffsetY(1f));

        if (hovered)
        {
            using var hoverPath = CreateRoundedRect(drawBounds, MenuS(5f));
            using var hoverBrush = new SolidBrush(Color.FromArgb(24, 80, 118, 170));
            g.FillPath(hoverBrush, hoverPath);
        }

        Color color = hovered ? Color.White : Color.FromArgb(218, 224, 238);
        Rectangle iconBounds = new(drawBounds.Left + (int)MenuS(26f), drawBounds.Top + (int)MenuS(15f), (int)MenuS(28f), (int)MenuS(30f));
        drawIcon(g, iconBounds, color);

        using var textBrush = new SolidBrush(color);
        DrawSpacedString(g, text, font, textBrush, drawBounds.Left + MenuS(280f), drawBounds.Top + MenuS(13f), MenuS(12f), centered: true);
    }

    private void DrawSecondaryMenuDivider(Graphics g, float x, float y, float width)
    {
        using var pen = new Pen(Color.FromArgb(48, 210, 222, 255), Math.Max(1f, MenuS(1f)));
        g.DrawLine(pen, x, y, x + width, y);
    }

    private void DrawMenuGearButton(Graphics g, Rectangle bounds, bool hovered, Color accent)
    {
        if (hovered)
        {
            using var glow = new SolidBrush(Color.FromArgb(28, accent));
            g.FillEllipse(glow, Rectangle.Inflate(bounds, (int)MenuS(9f), (int)MenuS(9f)));
        }

        DrawOutlineGearGlyph(g, bounds, hovered ? Color.White : Color.FromArgb(230, 232, 238, 248));
    }

    private void DrawPlayerBadge(Graphics g, bool hovered, Font font, Brush textBrush, Brush accentBrush)
    {
        Rectangle bounds = GetMenuPlayerBadgeBounds();
        if (hovered)
        {
            using var glow = new SolidBrush(Color.FromArgb(28, 105, 166, 255));
            using var path = CreateRoundedRect(bounds, MenuS(8f));
            g.FillPath(glow, path);
        }

        Rectangle icon = MenuRect(32f, 852f, 54f, 54f);
        using var pen = new Pen(Color.FromArgb(hovered ? 235 : 170, 225, 232, 244), Math.Max(1f, MenuS(1f)));
        g.DrawEllipse(pen, icon);
        g.DrawEllipse(pen, icon.Left + MenuS(19f), icon.Top + MenuS(13f), MenuS(16f), MenuS(16f));
        g.DrawArc(pen, icon.Left + MenuS(13f), icon.Top + MenuS(31f), MenuS(28f), MenuS(24f), 204f, 132f);

        DrawSpacedString(g, "PLAYER STATS", font, textBrush, MenuX(104f), MenuY(861f), MenuS(5f), centered: false);
        g.DrawString($"{_playerProgress.TotalGamesPlayed} PLAYS", font, accentBrush, MenuX(105f), MenuY(891f));

        if (hovered)
        {
            using var line = new Pen(Color.FromArgb(170, 105, 166, 255), Math.Max(1f, MenuS(1f)));
            g.DrawLine(line, bounds.Left + MenuS(82f), bounds.Bottom - MenuS(12f), bounds.Right - MenuS(18f), bounds.Bottom - MenuS(12f));
        }
    }

    private void DrawQuitHint(Graphics g, Rectangle bounds, bool hovered, Font font, Brush textBrush)
    {
        Rectangle keyBounds = new(bounds.Left, bounds.Top, (int)MenuS(46f), (int)MenuS(28f));
        using var keyPath = CreateRoundedRect(keyBounds, MenuS(4f));
        using var keyFill = new SolidBrush(Color.FromArgb(hovered ? 46 : 24, 225, 232, 244));
        using var keyPen = new Pen(Color.FromArgb(hovered ? 230 : 130, 225, 232, 244), Math.Max(1f, MenuS(1f)));
        g.FillPath(keyFill, keyPath);
        g.DrawPath(keyPen, keyPath);

        using var keyBrush = new SolidBrush(Color.FromArgb(hovered ? 255 : 190, 225, 232, 244));
        DrawCentered(g, "ESC", font, keyBrush, keyBounds.Left + keyBounds.Width / 2, keyBounds.Top + (int)MenuS(5f));
        DrawSpacedString(g, "QUIT", font, textBrush, bounds.Left + MenuS(62f), bounds.Top + MenuS(6f), MenuS(8f), centered: false);
    }

    private static void DrawPlayGlyph(Graphics g, Rectangle bounds, Color color)
    {
        using var brush = new SolidBrush(color);
        PointF[] points =
        [
            new(bounds.Left, bounds.Top),
            new(bounds.Right, bounds.Top + bounds.Height / 2f),
            new(bounds.Left, bounds.Bottom)
        ];
        g.FillPolygon(brush, points);
    }

    private static void DrawStatsGlyph(Graphics g, Rectangle bounds, Color color)
    {
        using var pen = new Pen(color, 1.7f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        float baseY = bounds.Bottom;
        float barW = bounds.Width / 6f;
        float[] heights = [bounds.Height * 0.42f, bounds.Height * 0.76f, bounds.Height * 0.56f];
        for (int i = 0; i < heights.Length; i++)
        {
            float x = bounds.Left + bounds.Width * (0.18f + i * 0.27f);
            g.DrawRectangle(pen, x, baseY - heights[i], barW, heights[i]);
        }
    }

    private static void DrawSmallGearGlyph(Graphics g, Rectangle bounds, Color color)
    {
        DrawOutlineGearGlyph(g, bounds, color);
    }

    private static void DrawRestartGlyph(Graphics g, Rectangle bounds, Color color)
    {
        using var pen = new Pen(color, 1.8f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        RectangleF arc = new(
            bounds.Left + bounds.Width * 0.13f,
            bounds.Top + bounds.Height * 0.10f,
            bounds.Width * 0.72f,
            bounds.Height * 0.72f);
        g.DrawArc(pen, arc, 35f, 285f);

        PointF head = new(bounds.Left + bounds.Width * 0.30f, bounds.Top + bounds.Height * 0.11f);
        g.DrawLine(pen, head, new PointF(head.X + bounds.Width * 0.06f, head.Y + bounds.Height * 0.23f));
        g.DrawLine(pen, head, new PointF(head.X - bounds.Width * 0.20f, head.Y + bounds.Height * 0.15f));
    }

    private static void DrawOutlineGearGlyph(Graphics g, Rectangle bounds, Color color)
    {
        float cx = bounds.Left + bounds.Width / 2f;
        float cy = bounds.Top + bounds.Height / 2f;
        float outer = Math.Min(bounds.Width, bounds.Height) * 0.36f;
        float inner = outer * 0.44f;
        using var pen = new Pen(color, Math.Max(1.4f, bounds.Width * 0.055f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        g.DrawEllipse(pen, cx - outer, cy - outer, outer * 2f, outer * 2f);
        for (int i = 0; i < 8; i++)
        {
            double angle = i * Math.PI / 4.0;
            float x1 = cx + (float)Math.Cos(angle) * (outer + bounds.Width * 0.04f);
            float y1 = cy + (float)Math.Sin(angle) * (outer + bounds.Width * 0.04f);
            float x2 = cx + (float)Math.Cos(angle) * (outer + bounds.Width * 0.18f);
            float y2 = cy + (float)Math.Sin(angle) * (outer + bounds.Width * 0.18f);
            g.DrawLine(pen, x1, y1, x2, y2);
        }

        g.DrawEllipse(pen, cx - inner, cy - inner, inner * 2f, inner * 2f);
    }

    private static void DrawSpacedString(Graphics g, string text, Font font, Brush brush, float x, float y, float spacing, bool centered)
    {
        float width = MeasureSpacedString(g, text, font, spacing);
        float cursor = centered ? x - width / 2f : x;
        foreach (char ch in text)
        {
            string part = ch.ToString();
            g.DrawString(part, font, brush, cursor, y);
            cursor += g.MeasureString(part, font).Width + spacing;
        }
    }

    private void DrawFittedSpacedString(
        Graphics g,
        string text,
        Font preferredFont,
        Brush brush,
        RectangleF bounds,
        float y,
        float spacing,
        bool centered)
    {
        float preferredWidth = MeasureSpacedString(g, text, preferredFont, spacing);
        Font fittedFont = preferredFont;
        float fittedSpacing = spacing;
        if (preferredWidth > bounds.Width && preferredWidth > 0f)
        {
            float ratio = Math.Clamp(bounds.Width / preferredWidth * 0.96f, 0.35f, 1f);
            fittedFont = _renderResources.Font(preferredFont.FontFamily.Name, Math.Max(7f, preferredFont.Size * ratio), preferredFont.Style);
            fittedSpacing *= ratio;
        }

        float x = centered ? bounds.Left + bounds.Width / 2f : bounds.Left;
        DrawSpacedString(g, text, fittedFont, brush, x, y, fittedSpacing, centered);
    }

    private static float MeasureSpacedString(Graphics g, string text, Font font, float spacing)
    {
        float width = 0f;
        foreach (char ch in text)
            width += g.MeasureString(ch.ToString(), font).Width + spacing;

        return text.Length == 0 ? 0f : width - spacing;
    }

    private Rectangle GetExitButtonBounds()
    {
        return MenuRect(1526f, 865f, 130f, 30f);
    }

    private Rectangle GetMenuBottomButtonBounds(bool isRestart)
    {
        return GetExitButtonBounds();
    }

    private Rectangle GetPlayAreaBounds()
    {
        float safeMargin = Math.Max(18f, 36f * GameScale);
        float sideReserve = ClientSize.Width >= 1180 ? 360f * GameScale : 110f * GameScale;
        float minLaneWidth = LaneCount >= 7 ? 72f * GameScale : LaneCount == 5 ? 82f * GameScale : 96f * GameScale;
        float minPlayWidth = LaneCount * minLaneWidth;
        float targetWidth = ClientSize.Width * (ClientSize.Width >= 1180 ? 0.46f : 0.72f);
        float maxPlayWidth = Math.Max(minPlayWidth, ClientSize.Width - safeMargin * 2f - sideReserve * 2f);
        int scaledWidth = (int)Math.Min(Math.Max(targetWidth, minPlayWidth), maxPlayWidth);
        return new Rectangle((ClientSize.Width - scaledWidth) / 2, 0, scaledWidth, ClientSize.Height);
    }

    private void RestartApplicationViaRunBat()
    {
        string? runBatPath = TryFindRunBatPath();
        try
        {
            if (runBatPath is not null)
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = runBatPath,
                    WorkingDirectory = Path.GetDirectoryName(runBatPath) ?? AppContext.BaseDirectory,
                    UseShellExecute = true,
                };
                Process.Start(startInfo);
            }
            else
            {
                var fallback = new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    WorkingDirectory = AppContext.BaseDirectory,
                    UseShellExecute = true,
                };
                Process.Start(fallback);
            }
        }
        catch
        {
            // Ignore restart launch errors and keep current app running.
            return;
        }

        Close();
    }

    private static string? TryFindRunBatPath()
    {
        DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && current is not null; i++)
        {
            string candidate = Path.Combine(current.FullName, "run.bat");
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        return null;
    }

    private float ScaleX(float value) => value * _layoutScale;

    private float ScaleY(float value) => value * _layoutScale;

    private float ScaleTextY(float value) => value * _layoutScale * (_textScalePercent / 100f);

    private int MotionOffsetY(float value) => _reducedMotionEnabled ? 0 : (int)ScaleY(value);

    private float MainMenuScale => Math.Max(0.35f, Math.Min(ClientSize.Width / MainMenuDesignWidth, ClientSize.Height / MainMenuDesignHeight));

    private float MainMenuOffsetX => (ClientSize.Width - MainMenuDesignWidth * MainMenuScale) / 2f - _layoutOffsetX;

    private float MainMenuOffsetY => (ClientSize.Height - MainMenuDesignHeight * MainMenuScale) / 2f - _layoutOffsetY;

    private float MenuS(float value) => value * MainMenuScale;

    private float MenuX(float value) => MainMenuOffsetX + value * MainMenuScale;

    private float MenuY(float value) => MainMenuOffsetY + value * MainMenuScale;

    private Rectangle MenuRect(float x, float y, float width, float height)
    {
        return Rectangle.Round(new RectangleF(MenuX(x), MenuY(y), MenuS(width), MenuS(height)));
    }

    private int MenuMotionOffsetY(float value) => _reducedMotionEnabled ? 0 : (int)MenuS(value);

    private Point ToLogicalPoint(Point point)
    {
        return new Point(
            (int)Math.Round(point.X - _layoutOffsetX),
            (int)Math.Round(point.Y - _layoutOffsetY));
    }

    private void UpdateLayoutMetrics()
    {
        float sx = ClientSize.Width / DesignWidth;
        float sy = ClientSize.Height / DesignHeight;
        _layoutScale = Math.Max(0.35f, Math.Min(sx, sy));
        _layoutOffsetX = Math.Max(0f, (ClientSize.Width - DesignWidth * _layoutScale) / 2f);
        _layoutOffsetY = Math.Max(0f, (ClientSize.Height - DesignHeight * _layoutScale) / 2f);
        _accessibleScreenKey = string.Empty;
    }

    private void ApplySettingsToRuntime()
    {
        ApplySpeedToEngine();
        _engine.AudioOffsetSeconds = EffectiveAudioOffsetMs / 1000f;

        _audio.SetBgmVolume(_bgmVolume);
        _audio.SetPreviewVolume(_previewVolume);
        _audio.ConfigureHitSound(_hitSoundSkin, _hitSoundPitch, _hitSoundMuted);
        _visualSkin = VisualSkin.Load(_visualSkinName);
        ApplyDisplayMode();
        ApplyFrameRate();
    }

    private void LoadUserSettings()
    {
        UserSettings settings = _settingsStore.Load();
        _bgmVolume = Math.Clamp(settings.BgmVolume, 0, 100);
        _previewVolume = Math.Clamp(settings.PreviewVolume, 0, 100);
        _sfxVolume = Math.Clamp(settings.SfxVolume, 0, 100);
        _hitSoundSkin = NormalizeHitSoundSkin(settings.HitSoundSkin);
        _visualSkinName = string.IsNullOrWhiteSpace(settings.VisualSkin) ? VisualSkin.DefaultName : settings.VisualSkin;
        _hitSoundPitch = Math.Clamp(settings.HitSoundPitch, -1, 1);
        _hitSoundMuted = settings.HitSoundMuted;
        _themeColorIndex = Math.Clamp(settings.ThemeColorIndex, 0, ThemeColors.Length - 1);
        _laneBrightness = Math.Clamp(settings.LaneBrightness, 0, 100);
        _frameRateMode = Math.Clamp(settings.FrameRateMode, 0, FrameRateIntervals.Length - 1);
        _vsyncEnabled = settings.VSyncEnabled;
        _darkModeEnabled = settings.DarkModeEnabled;
        _audioOffsetMs = Math.Clamp(settings.AudioOffsetMs, -150, 150);
        _laneModeIndex = Math.Clamp(settings.LaneModeIndex, 0, LaneModes.Length - 1);
        _speedMultiplier = Math.Clamp(settings.NoteSpeedPercent <= 0 ? 1f : settings.NoteSpeedPercent / 100f, 0.5f, 2.5f);
        _displayMode = settings.DisplayMode == (int)DisplayMode.Fullscreen ? DisplayMode.Fullscreen : DisplayMode.Windowed;
        if (!_selfTestMode && settings.WindowWidth >= 960 && settings.WindowHeight >= 540)
        {
            ClientSize = new Size(settings.WindowWidth, settings.WindowHeight);
            _windowedBounds = new Rectangle(Location, Size);
        }
        LoadKeyBindingsFromSettings(settings);
        _splashDurationMs = Math.Clamp(settings.SplashDurationMs, 600, 5000);
        _highContrastEnabled = settings.HighContrastEnabled;
        _colorVisionMode = Math.Clamp(settings.ColorVisionMode, 0, ColorVisionLabels.Length - 1);
        _reducedMotionEnabled = settings.ReducedMotionEnabled;
        _textScalePercent = Math.Clamp(settings.TextScalePercent <= 0 ? 100 : settings.TextScalePercent, 90, 140);
        _renderQualityMode = Math.Clamp(settings.RenderQualityMode, 0, RenderQualityLabels.Length - 1);
        _playModeIndex = Math.Clamp(settings.PlayModeIndex, 0, PlayModeLabels.Length - 1);
    }

    private void SaveUserSettings()
    {
        UserSettings snapshot = CreateUserSettingsSnapshot();
        lock (_settingsSaveLock)
        {
            _latestSettingsSnapshot = snapshot;
            if (_settingsSaveWorkerRunning)
                return;

            _settingsSaveWorkerRunning = true;
        }

        _ = Task.Run(SaveUserSettingsWorkerAsync);
    }

    private UserSettings CreateUserSettingsSnapshot()
    {
        Size windowSize = GetSavedWindowSize();
        return new UserSettings
        {
            BgmVolume = _bgmVolume,
            PreviewVolume = _previewVolume,
            SfxVolume = _sfxVolume,
            HitSoundSkin = _hitSoundSkin,
            VisualSkin = _visualSkinName,
            HitSoundPitch = _hitSoundPitch,
            HitSoundMuted = _hitSoundMuted,
            ThemeColorIndex = _themeColorIndex,
            LaneBrightness = _laneBrightness,
            FrameRateMode = _frameRateMode,
            VSyncEnabled = _vsyncEnabled,
            DarkModeEnabled = _darkModeEnabled,
            AudioOffsetMs = _audioOffsetMs,
            LaneModeIndex = _laneModeIndex,
            NoteSpeedPercent = (int)Math.Round(_speedMultiplier * 100f),
            DisplayMode = (int)_displayMode,
            WindowWidth = windowSize.Width,
            WindowHeight = windowSize.Height,
            KeyBindings4K = SerializeKeyBindings(0),
            KeyBindings5K = SerializeKeyBindings(1),
            KeyBindings6K = SerializeKeyBindings(2),
            KeyBindings7K = SerializeKeyBindings(3),
            SplashDurationMs = _splashDurationMs,
            HighContrastEnabled = _highContrastEnabled,
            ColorVisionMode = _colorVisionMode,
            ReducedMotionEnabled = _reducedMotionEnabled,
            TextScalePercent = _textScalePercent,
            RenderQualityMode = _renderQualityMode,
            PlayModeIndex = _playModeIndex,
        };
    }

    private async Task SaveUserSettingsWorkerAsync()
    {
        while (true)
        {
            await Task.Delay(120).ConfigureAwait(false);

            UserSettings? snapshot;
            lock (_settingsSaveLock)
            {
                snapshot = _latestSettingsSnapshot;
                _latestSettingsSnapshot = null;
            }

            if (snapshot is not null)
                SaveUserSettingsSnapshot(snapshot);

            lock (_settingsSaveLock)
            {
                if (_latestSettingsSnapshot is null)
                {
                    _settingsSaveWorkerRunning = false;
                    return;
                }
            }
        }
    }

    private void FlushPendingUserSettingsSave()
    {
        UserSettings? snapshot;
        lock (_settingsSaveLock)
        {
            snapshot = _latestSettingsSnapshot;
            _latestSettingsSnapshot = null;
        }

        if (snapshot is not null)
            SaveUserSettingsSnapshot(snapshot);
    }

    private void SaveUserSettingsSnapshot(UserSettings snapshot)
    {
        try
        {
            lock (_settingsDiskSaveLock)
            {
                _settingsStore.Save(snapshot);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to save user settings.", ex);
        }
    }

    private void ApplyFrameRate()
    {
        int idx = Math.Clamp(_frameRateMode, 0, FrameRateIntervals.Length - 1);
        _timer.Interval = FrameRateIntervals[idx];
    }

    private void EnterGameLowLatencyMode()
    {
        if (_gameLowLatencyModeActive)
            return;

        _gamePreviousTimerInterval = _timer.Interval;
        try { timeBeginPeriod(1); } catch { /* Timer precision is best-effort. */ }
        _gameLowLatencyModeActive = true;
    }

    private void ExitGameLowLatencyMode()
    {
        if (!_gameLowLatencyModeActive)
            return;

        try { timeEndPeriod(1); } catch { /* Timer precision is best-effort. */ }
        if (_gamePreviousTimerInterval > 0)
            _timer.Interval = _gamePreviousTimerInterval;
        _gameLowLatencyModeActive = false;
    }

    private void ApplyDisplayMode()
    {
        if (_isApplyingDisplayMode || IsDisposed || _selfTestMode)
            return;

        _isApplyingDisplayMode = true;
        try
        {
            SuspendLayout();
            if (_displayMode == DisplayMode.Fullscreen)
            {
                if (FormBorderStyle == FormBorderStyle.None && WindowState == FormWindowState.Maximized)
                    return;

                if (WindowState == FormWindowState.Normal)
                    _windowedBounds = Bounds;

                var screenBounds = Screen.FromControl(this).Bounds;
                WindowState = FormWindowState.Normal;
                FormBorderStyle = FormBorderStyle.None;
                Bounds = screenBounds;
                WindowState = FormWindowState.Maximized;
            }
            else
            {
                if (FormBorderStyle == FormBorderStyle.None && WindowState == FormWindowState.Normal)
                    return;

                WindowState = FormWindowState.Normal;
                FormBorderStyle = FormBorderStyle.None;
                if (_windowedBounds != Rectangle.Empty)
                {
                    Bounds = _windowedBounds;
                }
                else if (Width < 980 || Height < 560)
                {
                    var scr = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
                    float fs = Math.Min(scr.Width * 0.75f / MainMenuDesignWidth, scr.Height * 0.75f / MainMenuDesignHeight);
                    ClientSize = new Size(Math.Max((int)(MainMenuDesignWidth * fs), 960), Math.Max((int)(MainMenuDesignHeight * fs), 540));
                }
            }
        }
        finally
        {
            ResumeLayout(true);
            _isApplyingDisplayMode = false;
            UpdateLayoutMetrics();
            Invalidate();
        }
    }

    private void OnGameFormResize(object? sender, EventArgs e)
    {
        if (!_isApplyingDisplayMode)
        {
            if (_displayMode == DisplayMode.Windowed && WindowState == FormWindowState.Normal)
                _windowedBounds = Bounds;
            UpdateLayoutMetrics();
            Invalidate();
        }
    }

    private Color GetAccentColor()
    {
        if (UseHighContrast)
            return Color.FromArgb(255, 230, 0);

        int index = Math.Clamp(_themeColorIndex, 0, ThemeColors.Length - 1);
        if (_colorVisionMode > 0)
        {
            Color[] accessibleThemeColors =
            [
                Color.FromArgb(0, 114, 178),
                Color.FromArgb(213, 94, 0),
                Color.FromArgb(0, 158, 115),
                Color.FromArgb(204, 121, 167),
            ];
            return accessibleThemeColors[index];
        }

        return ThemeColors[index];
    }

    private Size GetSavedWindowSize()
    {
        if (_displayMode == DisplayMode.Windowed && WindowState == FormWindowState.Normal)
            return ClientSize;

        if (_windowedBounds.Width >= 960 && _windowedBounds.Height >= 540)
            return _windowedBounds.Size;

        return ClientSize;
    }

    private bool UseHighContrast => _highContrastEnabled || SystemInformation.HighContrast;

    // ── Dark Mode 색상 헬퍼 ──────────────────────────────────────────────────
    private Color BgColor1 => UseHighContrast ? Color.Black : Color.FromArgb(8, 12, 22);
    private Color BgColor2 => UseHighContrast ? Color.Black : Color.FromArgb(18, 25, 42);
    private Color CardFill => UseHighContrast ? Color.Black : Color.FromArgb(17, 25, 40);
    private Color CardBorder => UseHighContrast ? Color.White : (_darkModeEnabled ? Color.FromArgb(58, 62, 72) : Color.FromArgb(221, 223, 228));
    private Color CardShadow => _darkModeEnabled ? Color.FromArgb(30, 0, 0, 0) : Color.FromArgb(15, 86, 95, 112);
    private Color SeparatorColor => UseHighContrast ? Color.White : Color.FromArgb(54, 68, 96);
    private Color LabelColor => UseHighContrast ? Color.White : Color.FromArgb(170, 184, 212);
    private Color SubTextColor => UseHighContrast ? Color.White : Color.FromArgb(134, 151, 184);
    private Color PrimaryTextColor => UseHighContrast ? Color.White : Color.FromArgb(244, 248, 255);
    private Color SecondaryTextColor => UseHighContrast ? Color.White : Color.FromArgb(155, 173, 205);
    private Color SliderTrackColor => UseHighContrast ? Color.White : (_darkModeEnabled ? Color.FromArgb(55, 60, 72) : Color.FromArgb(230, 232, 236));
    private Color ToggleOffColor => _darkModeEnabled ? Color.FromArgb(65, 70, 82) : Color.FromArgb(217, 220, 225);
    private Color SegmentBg => UseHighContrast ? Color.Black : (_darkModeEnabled ? Color.FromArgb(32, 36, 45) : Color.FromArgb(251, 251, 252));
    private Color SegmentBorder => UseHighContrast ? Color.White : (_darkModeEnabled ? Color.FromArgb(55, 60, 72) : Color.FromArgb(212, 215, 220));
    private Color SegmentText => UseHighContrast ? Color.White : (_darkModeEnabled ? Color.FromArgb(155, 162, 178) : Color.FromArgb(109, 113, 120));
    private Color SegmentDivider => UseHighContrast ? Color.White : (_darkModeEnabled ? Color.FromArgb(50, 55, 66) : Color.FromArgb(226, 228, 232));
    private Color ValuePillBg => UseHighContrast ? Color.Black : (_darkModeEnabled ? Color.FromArgb(40, 44, 54) : Color.FromArgb(250, 250, 251));
    private Color ValuePillBorder => UseHighContrast ? Color.White : (_darkModeEnabled ? Color.FromArgb(60, 65, 78) : Color.FromArgb(213, 216, 221));
    private Color ValuePillText => UseHighContrast ? Color.White : (_darkModeEnabled ? Color.FromArgb(175, 182, 195) : Color.FromArgb(100, 104, 111));
    private Color SliderKnobShadow => _darkModeEnabled ? Color.FromArgb(40, 0, 0, 0) : Color.FromArgb(24, 70, 96, 146);
    private Color BackBtnFill => _darkModeEnabled ? Color.FromArgb(40, 44, 54) : Color.FromArgb(250, 250, 251);
    private Color BackBtnBorder => _darkModeEnabled ? Color.FromArgb(60, 65, 78) : Color.FromArgb(204, 206, 212);
    private Color BackBtnArrow => _darkModeEnabled ? Color.FromArgb(160, 170, 185) : Color.FromArgb(101, 110, 126);
    private Color IconColor => _darkModeEnabled ? Color.FromArgb(120, 128, 145) : Color.FromArgb(128, 133, 143);
    private Color ThemeRingColor => _darkModeEnabled ? Color.FromArgb(70, 75, 88) : Color.FromArgb(221, 224, 230);
    private Color HazeTint => _darkModeEnabled ? Color.FromArgb(12, 80, 120, 200) : Color.FromArgb(28, 255, 255, 255);
    private Color ClearColor => Color.FromArgb(8, 12, 22);
    private Color PanelFill1 => UseHighContrast ? Color.Black : Color.FromArgb(17, 25, 40);
    private Color PanelBorder => UseHighContrast ? Color.White : Color.FromArgb(72, 100, 150);
    private Color PanelDivider => UseHighContrast ? Color.White : Color.FromArgb(45, 59, 86);
    private Color SearchFill1 => UseHighContrast ? Color.Black : _darkModeEnabled ? Color.FromArgb(30, 34, 44) : Color.FromArgb(236, 240, 248);
    private Color SearchFill2 => UseHighContrast ? Color.Black : _darkModeEnabled ? Color.FromArgb(26, 30, 38) : Color.FromArgb(225, 230, 240);
    private Color SearchBorder => UseHighContrast ? Color.White : _darkModeEnabled ? Color.FromArgb(50, 56, 68) : Color.FromArgb(207, 214, 227);
    private Color SearchIconColor => UseHighContrast ? Color.White : _darkModeEnabled ? Color.FromArgb(100, 110, 130) : Color.FromArgb(160, 171, 193);
    private Color SearchActiveText => UseHighContrast ? Color.White : _darkModeEnabled ? Color.FromArgb(180, 190, 210) : Color.FromArgb(94, 108, 138);
    private Color TabFill1 => UseHighContrast ? Color.Black : _darkModeEnabled ? Color.FromArgb(34, 38, 48) : Color.FromArgb(245, 248, 253);
    private Color TabFill2 => UseHighContrast ? Color.Black : _darkModeEnabled ? Color.FromArgb(28, 32, 40) : Color.FromArgb(232, 237, 246);
    private Color TabBorder => UseHighContrast ? Color.White : _darkModeEnabled ? Color.FromArgb(50, 56, 68) : Color.FromArgb(182, 194, 215);
    private Color TabText => UseHighContrast ? Color.White : _darkModeEnabled ? Color.FromArgb(130, 140, 160) : Color.FromArgb(122, 137, 164);
    private Color SelectedRowFill1 => UseHighContrast ? Color.FromArgb(40, 40, 40) : _darkModeEnabled ? Color.FromArgb(38, 48, 68) : Color.FromArgb(230, 239, 253);
    private Color SelectedRowFill2 => UseHighContrast ? Color.Black : _darkModeEnabled ? Color.FromArgb(32, 42, 60) : Color.FromArgb(215, 227, 246);
    private Color SelectedRowBorder => UseHighContrast ? Color.FromArgb(255, 230, 0) : _darkModeEnabled ? Color.FromArgb(55, 75, 110) : Color.FromArgb(173, 196, 233);
    private Color RowCircleFill => UseHighContrast ? Color.Black : _darkModeEnabled ? Color.FromArgb(42, 46, 58) : Color.FromArgb(238, 241, 247);
    private Color RowCircleBorder => UseHighContrast ? Color.White : _darkModeEnabled ? Color.FromArgb(60, 66, 80) : Color.FromArgb(191, 201, 218);
    private Color SelectedCircleFill => UseHighContrast ? Color.Black : _darkModeEnabled ? Color.FromArgb(50, 42, 68) : Color.FromArgb(233, 224, 252);
    private Color SelectedCircleBorder => UseHighContrast ? Color.FromArgb(255, 230, 0) : _darkModeEnabled ? Color.FromArgb(80, 68, 110) : Color.FromArgb(187, 168, 228);
    private Color ScrollTrackColor => UseHighContrast ? Color.Black : _darkModeEnabled ? Color.FromArgb(40, 44, 55) : Color.FromArgb(227, 232, 240);
    private Color ScrollHandleColor => UseHighContrast ? Color.White : _darkModeEnabled ? Color.FromArgb(70, 78, 95) : Color.FromArgb(184, 194, 211);
    private Color DotColor => UseHighContrast ? Color.White : _darkModeEnabled ? Color.FromArgb(70, 78, 92) : Color.FromArgb(197, 204, 218);
    private Color DotBorder => UseHighContrast ? Color.White : _darkModeEnabled ? Color.FromArgb(55, 62, 75) : Color.FromArgb(159, 170, 193);
    private Color ArrowBtnFill1 => UseHighContrast ? Color.Black : _darkModeEnabled ? Color.FromArgb(38, 42, 52) : Color.FromArgb(236, 240, 247);
    private Color ArrowBtnFill2 => UseHighContrast ? Color.Black : _darkModeEnabled ? Color.FromArgb(32, 36, 46) : Color.FromArgb(223, 230, 241);
    private Color ArrowBtnBorder => UseHighContrast ? Color.White : _darkModeEnabled ? Color.FromArgb(55, 62, 75) : Color.FromArgb(175, 188, 209);
    private Color ArrowColor => UseHighContrast ? Color.White : _darkModeEnabled ? Color.FromArgb(120, 132, 155) : Color.FromArgb(128, 145, 177);
    private Color CloseBtnFill1 => UseHighContrast ? Color.Black : _darkModeEnabled ? Color.FromArgb(38, 42, 52) : Color.FromArgb(238, 243, 251);
    private Color CloseBtnFill2 => UseHighContrast ? Color.Black : _darkModeEnabled ? Color.FromArgb(32, 36, 46) : Color.FromArgb(225, 232, 245);
    private Color CloseBtnBorder => UseHighContrast ? Color.White : _darkModeEnabled ? Color.FromArgb(55, 62, 78) : Color.FromArgb(178, 192, 213);
    private Color CloseBtnX => UseHighContrast ? Color.White : _darkModeEnabled ? Color.FromArgb(140, 155, 185) : Color.FromArgb(75, 112, 179);
    private Color AchCardFill1 => UseHighContrast ? Color.Black : _darkModeEnabled ? Color.FromArgb(34, 40, 52) : Color.FromArgb(248, 250, 253);
    private Color AchCardFill2 => UseHighContrast ? Color.Black : _darkModeEnabled ? Color.FromArgb(28, 34, 44) : Color.FromArgb(237, 242, 249);
    private Color AchCardBorder => UseHighContrast ? Color.White : _darkModeEnabled ? Color.FromArgb(50, 58, 72) : Color.FromArgb(201, 212, 229);
    private Color AchCardText => UseHighContrast ? Color.White : _darkModeEnabled ? Color.FromArgb(145, 170, 210) : Color.FromArgb(105, 139, 193);
    private Color AchDotPen => UseHighContrast ? Color.White : _darkModeEnabled ? Color.FromArgb(80, 100, 135) : Color.FromArgb(160, 186, 214);
    private Color ChevronColor => UseHighContrast ? Color.White : _darkModeEnabled ? Color.FromArgb(90, 115, 160) : Color.FromArgb(110, 143, 196);
    private Color ExitBtnFill1 => _darkModeEnabled ? Color.FromArgb(34, 38, 48) : Color.FromArgb(250, 252, 255);
    private Color ExitBtnFill2 => _darkModeEnabled ? Color.FromArgb(28, 32, 42) : Color.FromArgb(237, 243, 252);
    private Color ExitBtnHoverFill1 => _darkModeEnabled ? Color.FromArgb(38, 42, 54) : Color.FromArgb(242, 247, 255);
    private Color ExitBtnHoverFill2 => _darkModeEnabled ? Color.FromArgb(32, 36, 48) : Color.FromArgb(223, 233, 248);
    private Color AnalyzeBg1 => UseHighContrast ? Color.Black : Color.FromArgb(8, 12, 22);
    private Color AnalyzeBg2 => UseHighContrast ? Color.Black : Color.FromArgb(18, 25, 42);
    private Color AnalyzeTitle => UseHighContrast ? Color.White : Color.FromArgb(235, 245, 255);
    private Color AnalyzePanelFill1 => UseHighContrast ? Color.Black : Color.FromArgb(18, 26, 42);
    private Color AnalyzePanelFill2 => UseHighContrast ? Color.Black : Color.FromArgb(12, 19, 33);
    private Color AnalyzePanelBorder => UseHighContrast ? Color.White : Color.FromArgb(72, 100, 150);
    private Color AnalyzeRowAlt1 => UseHighContrast ? Color.Black : Color.FromArgb(20, 30, 48);
    private Color AnalyzeRowAlt2 => UseHighContrast ? Color.FromArgb(20, 20, 20) : Color.FromArgb(15, 23, 38);
    private Color AnalyzeRowBorder => UseHighContrast ? Color.White : Color.FromArgb(44, 60, 88);
    private Color AnalyzeLabelColor => UseHighContrast ? Color.White : Color.FromArgb(170, 188, 220);
    private Color AnalyzeValueColor => UseHighContrast ? Color.White : Color.FromArgb(235, 245, 255);
    private Color AnalyzeSongTitle => UseHighContrast ? Color.White : Color.FromArgb(240, 248, 255);
    private Color AnalyzeSongArtist => UseHighContrast ? Color.White : Color.FromArgb(150, 170, 205);
    private Color AnalyzeStatLabel => UseHighContrast ? Color.White : Color.FromArgb(145, 165, 200);
    private Color AnalyzeStatValue => UseHighContrast ? Color.White : Color.FromArgb(235, 245, 255);
    private Color AchDetailTabFill1 => UseHighContrast ? Color.Black : _darkModeEnabled ? Color.FromArgb(34, 38, 48) : Color.FromArgb(246, 249, 253);
    private Color AchDetailTabFill2 => UseHighContrast ? Color.Black : _darkModeEnabled ? Color.FromArgb(28, 32, 40) : Color.FromArgb(229, 237, 248);
    private Color AchDetailTabBorder => UseHighContrast ? Color.White : _darkModeEnabled ? Color.FromArgb(48, 55, 68) : Color.FromArgb(200, 214, 232);
    private Color AchDetailSelectedFill => UseHighContrast ? Color.FromArgb(35, 35, 35) : _darkModeEnabled ? Color.FromArgb(42, 48, 60) : Color.FromArgb(255, 255, 255);
    private Color AchDetailSelectedFill2 => UseHighContrast ? Color.Black : _darkModeEnabled ? Color.FromArgb(36, 42, 54) : Color.FromArgb(242, 247, 253);
    private Color AchDetailSelectedBorder => UseHighContrast ? Color.FromArgb(255, 230, 0) : _darkModeEnabled ? Color.FromArgb(58, 68, 85) : Color.FromArgb(210, 223, 238);
    private Color AchDetailSelectedText => UseHighContrast ? Color.White : _darkModeEnabled ? Color.FromArgb(145, 170, 210) : Color.FromArgb(98, 130, 184);
    private Color AchDetailUnselectedText => UseHighContrast ? Color.White : _darkModeEnabled ? Color.FromArgb(110, 130, 160) : Color.FromArgb(142, 165, 193);

    private void DrawExitButton(Graphics g, Rectangle bounds, bool hovered, Font font)
    {
        Color accent = GetAccentColor();

        var shadowBounds = bounds;
        shadowBounds.Offset(0, (int)ScaleY(4f));
        using (var shadowPath = CreateRoundedRect(shadowBounds, shadowBounds.Height / 2f))
        using (var shadowBrush = new SolidBrush(Color.FromArgb(22, 40, 62, 98)))
        {
            g.FillPath(shadowBrush, shadowPath);
        }

        var drawBounds = bounds;
        if (hovered)
            drawBounds.Offset(0, -MotionOffsetY(2f));

        using var outerPath = CreateRoundedRect(drawBounds, drawBounds.Height / 2f);
        using var fillBrush = new LinearGradientBrush(
            drawBounds,
            hovered ? ExitBtnHoverFill1 : ExitBtnFill1,
            hovered ? ExitBtnHoverFill2 : ExitBtnFill2,
            LinearGradientMode.Vertical);
        using var borderPen = new Pen(Color.FromArgb(120, accent), Math.Max(1.4f, ScaleY(1.8f)));
        using var textBrush = new SolidBrush(Color.FromArgb(140, accent));

        g.FillPath(fillBrush, outerPath);
        g.DrawPath(borderPen, outerPath);
        DrawCentered(g, "EXIT", font, textBrush, drawBounds.Left + drawBounds.Width / 2, drawBounds.Top + (int)ScaleY(13f));
    }

    private void DrawRestartButton(Graphics g, Rectangle bounds, bool hovered, Font font)
    {
        Color accent = GetAccentColor();
        Rectangle shadowBounds = bounds;
        shadowBounds.Offset(0, 4);
        using (var shadowPath = CreateRoundedRect(shadowBounds, shadowBounds.Height / 2f))
        using (var shadowBrush = new SolidBrush(Color.FromArgb(26, 40, 62, 98)))
        {
            g.FillPath(shadowBrush, shadowPath);
        }

        Rectangle drawBounds = bounds;
        if (hovered)
            drawBounds.Offset(0, -2);

        using var outerPath = CreateRoundedRect(drawBounds, drawBounds.Height / 2f);
        using var fillBrush = new LinearGradientBrush(
            drawBounds,
            hovered ? ExitBtnHoverFill1 : ExitBtnFill1,
            hovered ? ExitBtnHoverFill2 : ExitBtnFill2,
            LinearGradientMode.Vertical);
        using var borderPen = new Pen(Color.FromArgb(150, accent), 1.8f);
        using var textBrush = new SolidBrush(Color.FromArgb(120, accent));

        g.FillPath(fillBrush, outerPath);
        g.DrawPath(borderPen, outerPath);
        DrawCentered(g, "RESTART", font, textBrush, drawBounds.Left + drawBounds.Width / 2, drawBounds.Top + (int)ScaleY(13f));
    }

    private static void DrawTitleNote(Graphics g, float x, float y, Color color)
    {
        using var pen = new Pen(color, 4.5f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        using var brush = new SolidBrush(color);

        g.DrawLine(pen, x + 8f, y + 2f, x + 8f, y + 32f);
        g.DrawLine(pen, x + 8f, y + 2f, x + 28f, y + 8f);
        g.FillEllipse(brush, x - 4f, y + 25f, 18f, 14f);
    }

    private static void DrawGearGlyph(Graphics g, RectangleF bounds, Color color, Color holeColor)
    {
        float cx = bounds.Left + bounds.Width / 2f;
        float cy = bounds.Top + bounds.Height / 2f;
        float outerRadius = bounds.Width * 0.34f;
        float toothWidth = bounds.Width * 0.15f;
        float toothHeight = bounds.Height * 0.14f;

        using var brush = new SolidBrush(color);
        using var cutBrush = new SolidBrush(holeColor);

        g.FillEllipse(brush, cx - outerRadius, cy - outerRadius, outerRadius * 2f, outerRadius * 2f);

        GraphicsState state = g.Save();
        g.TranslateTransform(cx, cy);
        for (int i = 0; i < 8; i++)
        {
            g.RotateTransform(45f);
            g.FillRectangle(brush, -toothWidth / 2f, -outerRadius - toothHeight / 2f, toothWidth, toothHeight);
        }
        g.Restore(state);

        float innerRadius = bounds.Width * 0.14f;
        g.FillEllipse(cutBrush, cx - innerRadius, cy - innerRadius, innerRadius * 2f, innerRadius * 2f);
    }

    private static void DrawSoundIcon(Graphics g, Rectangle bounds, Color color)
    {
        using var brush = new SolidBrush(color);
        using var pen = new Pen(color, 2.8f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        float x = bounds.Left;
        float y = bounds.Top;
        float w = bounds.Width;
        float h = bounds.Height;
        PointF[] speaker =
        [
            new PointF(x + w * 0.08f, y + h * 0.38f),
            new PointF(x + w * 0.28f, y + h * 0.38f),
            new PointF(x + w * 0.48f, y + h * 0.18f),
            new PointF(x + w * 0.48f, y + h * 0.82f),
            new PointF(x + w * 0.28f, y + h * 0.62f),
            new PointF(x + w * 0.08f, y + h * 0.62f),
        ];
        g.FillPolygon(brush, speaker);
        g.DrawArc(pen, x + w * 0.45f, y + h * 0.20f, w * 0.26f, h * 0.60f, -55f, 110f);
        g.DrawArc(pen, x + w * 0.56f, y + h * 0.08f, w * 0.30f, h * 0.82f, -55f, 110f);
    }

    private static void DrawPaletteIcon(Graphics g, Rectangle bounds, Color color)
    {
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, bounds.Left + bounds.Width * 0.12f, bounds.Top + bounds.Height * 0.12f, bounds.Width * 0.76f, bounds.Height * 0.76f);
        using var holeBrush = new SolidBrush(Color.FromArgb(252, 252, 253));
        g.FillEllipse(holeBrush, bounds.Left + bounds.Width * 0.48f, bounds.Top + bounds.Height * 0.48f, bounds.Width * 0.26f, bounds.Height * 0.22f);

        float dotSize = bounds.Width * 0.09f;
        g.FillEllipse(holeBrush, bounds.Left + bounds.Width * 0.30f, bounds.Top + bounds.Height * 0.22f, dotSize, dotSize);
        g.FillEllipse(holeBrush, bounds.Left + bounds.Width * 0.18f, bounds.Top + bounds.Height * 0.40f, dotSize, dotSize);
        g.FillEllipse(holeBrush, bounds.Left + bounds.Width * 0.35f, bounds.Top + bounds.Height * 0.47f, dotSize, dotSize);
        g.FillEllipse(holeBrush, bounds.Left + bounds.Width * 0.52f, bounds.Top + bounds.Height * 0.28f, dotSize, dotSize);
    }

    private static void DrawLaneBrightnessIcon(Graphics g, Rectangle bounds, Color color)
    {
        using var pen = new Pen(color, 2.6f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        for (int i = 0; i < 4; i++)
        {
            float x = bounds.Left + bounds.Width * (0.18f + i * 0.18f);
            g.DrawLine(pen, x, bounds.Top + bounds.Height * 0.18f, x - bounds.Width * 0.06f, bounds.Bottom - bounds.Height * 0.06f);
        }

        g.DrawLine(pen, bounds.Left + bounds.Width * 0.12f, bounds.Top + bounds.Height * 0.28f, bounds.Left + bounds.Width * 0.12f, bounds.Top + bounds.Height * 0.18f);
        g.DrawLine(pen, bounds.Left + bounds.Width * 0.34f, bounds.Top + bounds.Height * 0.18f, bounds.Left + bounds.Width * 0.34f, bounds.Top + bounds.Height * 0.08f);
        g.DrawLine(pen, bounds.Left + bounds.Width * 0.56f, bounds.Top + bounds.Height * 0.22f, bounds.Left + bounds.Width * 0.56f, bounds.Top + bounds.Height * 0.12f);
        g.DrawLine(pen, bounds.Left + bounds.Width * 0.78f, bounds.Top + bounds.Height * 0.30f, bounds.Left + bounds.Width * 0.78f, bounds.Top + bounds.Height * 0.20f);
    }

    private static void DrawMonitorIcon(Graphics g, Rectangle bounds, Color color)
    {
        using var pen = new Pen(color, 2.6f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        Rectangle screen = Rectangle.Round(new RectangleF(bounds.Left + bounds.Width * 0.08f, bounds.Top + bounds.Height * 0.16f, bounds.Width * 0.84f, bounds.Height * 0.58f));
        using (var path = CreateRoundedRect(screen, 4f))
            g.DrawPath(pen, path);

        g.DrawLine(pen, bounds.Left + bounds.Width * 0.44f, bounds.Top + bounds.Height * 0.76f, bounds.Left + bounds.Width * 0.56f, bounds.Top + bounds.Height * 0.76f);
        g.DrawLine(pen, bounds.Left + bounds.Width * 0.32f, bounds.Top + bounds.Height * 0.92f, bounds.Left + bounds.Width * 0.68f, bounds.Top + bounds.Height * 0.92f);
    }

    private static GraphicsPath CreateRoundedRect(Rectangle bounds, float radius)
    {
        return CreateRoundedRect(new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height), radius);
    }

    private static GraphicsPath CreateRoundedRect(RectangleF bounds, float radius)
    {
        float diameter = radius * 2f;
        var path = new GraphicsPath();

        diameter = Math.Min(diameter, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 0f)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }

    // ── 유틸 ──────────────────────────────────────────────────────────────────
    private static void DrawCentered(Graphics g, string text, Font font, Brush brush, int cx, int y)
    {
        DrawCentered(g, text, font, brush, (float)cx, y);
    }

    private static void DrawCentered(Graphics g, string text, Font font, Brush brush, float cx, float y)
    {
        var sz = g.MeasureString(text, font);
        g.DrawString(text, font, brush, cx - sz.Width / 2f, y);
    }

    private static void DrawLeftCentered(Graphics g, string text, Font font, Brush brush, float x, int centerY)
    {
        var sz = g.MeasureString(text, font);
        g.DrawString(text, font, brush, x, centerY - sz.Height / 2f);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelPendingReplayLoad();
            CancelSessionAudioFingerprint();
            ExitCalibrationLowLatencyMode();
            ExitGameLowLatencyMode();
            FlushPendingUserSettingsSave();
            _timer.Dispose();
            _splashTimer.Dispose();
            _songFolderWatcher?.Dispose();
            _songGenerationDebounceTimer?.Dispose();
            _audio.Dispose();
            _renderResources.Dispose();
            _gameBgaImage?.Dispose();
            _gameBackgroundCache?.Dispose();
            _songSelectPhoto?.Dispose();
        }
        base.Dispose(disposing);
    }
}
