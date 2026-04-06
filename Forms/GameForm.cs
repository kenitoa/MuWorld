using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RhythmGame;

internal enum UiScreen
{
    Splash,
    MainMenu,
    Settings,
    SongSelect,
    Achievement,
    AchievementDetail,
    Analyze
}

internal enum SettingsSlider
{
    None,
    Bgm,
    Sfx,
    LaneBrightness
}

internal enum DisplayMode
{
    Windowed,
    Fullscreen
}

public sealed partial class GameForm : Form
{
    private const float DesignWidth = 1152f;
    private const float DesignHeight = 768f;

    // ── 엔진 & 타이머 ─────────────────────────────────────────────────────────
    private readonly GameEngine _engine = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 8 }; // ~120 fps
    private readonly System.Diagnostics.Stopwatch _frameStopwatch = System.Diagnostics.Stopwatch.StartNew();
    private int _hoverMenuIndex = -1;
    private int _hoverSongPlayIndex = -1;
    private int _songSelectDifficultyIndex = 1;

    private int _songSelectPageIndex;
    private int _songSelectSelectedIndex;
    private string _songSearchQuery = string.Empty;
    private bool _isSongSearchFocused;
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
    private bool _isMenuRestartHovered;
    private bool _isExitHovered;
    private UiScreen _screen = UiScreen.Splash;
    private DateTime _splashStartTime = DateTime.Now;
    private readonly System.Windows.Forms.Timer _splashTimer = new() { Interval = 16 };
    private SettingsSlider _draggedSlider;
    private DisplayMode _displayMode;
    private readonly AudioManager _audio = new();
    private readonly AchievementProgressStore _achievementStore = new();
    private readonly Random _uiRandom = new();
    private bool _isCountdownActive;
    private DateTime _countdownStartTime;
    private int _countdownSeconds;
    private float _layoutScale = 1f;
    private float _layoutOffsetX;
    private float _layoutOffsetY;
    private bool _isApplyingDisplayMode;
    private Rectangle _windowedBounds;
    private IReadOnlyList<LaneNote> _selectedChartNotes = [];

    // Analyze screen state
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
    private bool _isAnalyzeOkHovered;
    private DateTime _chartCompleteTime;
    private bool _chartCompleteWaiting;

    private int _bgmVolume = 80;
    private int _sfxVolume = 60;
    private int _themeColorIndex;
    private int _laneBrightness = 70;
    private int _frameRateMode = 2; // 0=30, 1=60, 2=120, 3=144, 4=240
    private bool _vsyncEnabled;
    private bool _darkModeEnabled;
    private static readonly int[] FrameRateIntervals = [33, 16, 8, 7, 4]; // ms per frame
    private static readonly string[] FrameRateLabels = ["30", "60", "120", "144", "240"];

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    private static readonly Color[] ThemeColors =
    [
        Color.FromArgb(72, 126, 216),
        Color.FromArgb(149, 116, 225),
        Color.FromArgb(68, 206, 130),
        Color.FromArgb(248, 151, 69),
    ];

    // ── 판정 피드백 ───────────────────────────────────────────────────────────
    private string?  _feedback;
    private DateTime _feedbackTime;

    // ── HUD 캐시 (불필요한 문자열 할당 방지) ────────────────────────────────────
    private string _cachedStatsText = string.Empty;
    private int _cachedStatsPerfect, _cachedStatsGreat, _cachedStatsBetter;
    private int _cachedStatsGood, _cachedStatsBad, _cachedStatsMiss;
    private string _cachedComboText = string.Empty;
    private int _cachedComboValue;

    // ── 레인 설정 ─────────────────────────────────────────────────────────────
    private const int LaneCount = 4;
    private int LaneWidth => ClientSize.Width / LaneCount;

    private static readonly Color[] LaneColors =
    [
        Color.FromArgb(255,  80,  80),   // D — 빨강
        Color.FromArgb( 80, 210,  80),   // F — 초록
        Color.FromArgb( 80, 120, 255),   // J — 파랑
        Color.FromArgb(255, 210,  50),   // K — 노랑
    ];

    private static readonly Keys[]   LaneKeys   = [Keys.D, Keys.F, Keys.J, Keys.K];
    private static readonly string[] LaneLabels = ["D", "F", "J", "K"];
    private readonly bool[] _lanePressed = new bool[LaneCount];

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
    {
        Text            = "Rhythm Game";
        BackColor       = Color.White;
        DoubleBuffered  = true;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize     = new Size(960, 640);
        MaximizeBox     = true;
        StartPosition   = FormStartPosition.CenterScreen;

        // 현재 모니터 해상도의 75%로 초기 창 크기 설정 (DesignWidth:DesignHeight 비율 유지)
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        float fitScale = Math.Min(screen.Width * 0.75f / DesignWidth, screen.Height * 0.75f / DesignHeight);
        int initW = (int)(DesignWidth * fitScale);
        int initH = (int)(DesignHeight * fitScale);
        ClientSize = new Size(Math.Max(initW, 960), Math.Max(initH, 640));
        KeyPreview      = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        _timer.Tick += OnTick;
        MouseMove += OnMenuMouseMove;
        MouseLeave += OnMenuMouseLeave;
        MouseDown += OnMenuMouseDown;
        MouseUp += OnMenuMouseUp;
        Resize += OnGameFormResize;

        UpdateLayoutMetrics();
        ApplySettingsToRuntime();
        _playerProgress = _achievementStore.Load();

        // WAV 파일 분석 및 채보 자동 생성
        ChartGenerator.GenerateAllCharts();

        _audio.PlayMainScreenBgm();

        _splashTimer.Tick += (_, _) => Invalidate();
        _splashTimer.Start();
    }

    private void PlaySelectedSongBgm(SongEntry? song)
    {
        if (song is null)
            return;

        string wavPath = Path.Combine(AppContext.BaseDirectory, "Songs", "InGameBGM", "Original", song.Title + ".wav");
        if (File.Exists(wavPath))
            _audio.PlayInGameBgm(wavPath);
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

        if (_isCountdownActive)
        {
            if ((now - _countdownStartTime).TotalSeconds >= _countdownSeconds)
            {
                _isCountdownActive = false;
                _engine.Start(ClientSize.Height, _selectedChartNotes);
                SongEntry? selectedSong = _screen == UiScreen.SongSelect ? GetSelectedSong() : null;
                PlaySelectedSongBgm(selectedSong);
            }

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

        _engine.Update(dt);

        if (_engine.IsChartComplete)
        {
            if (!_chartCompleteWaiting)
            {
                _chartCompleteWaiting = true;
                _chartCompleteTime = DateTime.Now;
            }
            else if ((DateTime.Now - _chartCompleteTime).TotalSeconds >= 3.0)
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

            if (_screen == UiScreen.SongSelect)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    e.SuppressKeyPress = true;
                    _screen = UiScreen.MainMenu;
                    Invalidate();
                    return;
                }

                if (e.KeyCode == Keys.Back && _isSongSearchFocused)
                {
                    ApplySongSearchInput(removeLast: true);
                    Invalidate();
                    return;
                }

                return;
            }

            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; BeginGame(); }
            return;
        }

        if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; _chartCompleteWaiting = false; EndGame(); return; }

        // 배속 조절: 1키 증가, 2키 감소
        if (e.KeyCode == Keys.D1) { e.SuppressKeyPress = true; IncreaseSpeed(); Invalidate(); return; }
        if (e.KeyCode == Keys.D2) { e.SuppressKeyPress = true; DecreaseSpeed(); Invalidate(); return; }

        // 모드 전환: 3키 다음, 4키 이전
        if (e.KeyCode == Keys.D3) { e.SuppressKeyPress = true; CycleGameModeForward(); Invalidate(); return; }
        if (e.KeyCode == Keys.D4) { e.SuppressKeyPress = true; CycleGameModeBackward(); Invalidate(); return; }

        for (int i = 0; i < LaneKeys.Length; i++)
        {
            if (e.KeyCode != LaneKeys[i]) continue;
            e.SuppressKeyPress = true;
            if (_lanePressed[i]) break;   // 키 반복 방지
            _lanePressed[i] = true;
            string? fb = _engine.TryHit(i);
            if (fb is not null)
            {
                _feedback = fb;
                _feedbackTime = DateTime.Now;

                bool isPerfectOrGreat = fb[0] is 'P' or 'G' && fb[^1] == '!';
                _audio.PlayHit(_sfxVolume, isPerfectOrGreat, _audio.IsInGameBgmPlaying);
            }
            // Invalidate()는 타이머(~8ms)가 이미 매 프레임 호출하므로 생략
            break;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        for (int i = 0; i < LaneKeys.Length; i++)
        {
            if (e.KeyCode == LaneKeys[i])
            {
                _lanePressed[i] = false;
                // 게임 중에는 타이머가 Invalidate()를 호출하므로 메뉴에서만 호출
                if (!_engine.IsRunning) Invalidate();
                break;
            }
        }
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

    private void BeginGame()
    {
        _feedback = null;
        ApplySettingsToRuntime();
        _audio.StopAllSounds();
        SongEntry? selectedSong = _screen == UiScreen.SongSelect ? GetSelectedSong() : null;
        _selectedChartNotes = NoteLane.LoadNotes(selectedSong?.Title, selectedSong?.Artist, _songSelectDifficultyIndex);

        // 시작 후 3초간 노트 없이 준비 시간 확보
        const float startDelay = 3f;
        _selectedChartNotes = _selectedChartNotes
            .Select(n => new LaneNote(n.Time + startDelay, n.Lane))
            .ToList();

        _countdownSeconds = 3;

        _isCountdownActive = true;
        _countdownStartTime = DateTime.Now;

        _timer.Start();
    }

    private void EndGame()
    {
        // Capture results before stopping engine
        _analyzeScore = _engine.Score.Score;
        _analyzeMaxCombo = _engine.Score.MaxCombo;
        _analyzePerfectCount = _engine.Score.PerfectCount;
        _analyzeGreatCount = _engine.Score.GreatCount;
        _analyzeBetterCount = _engine.Score.BetterCount;
        _analyzeGoodCount = _engine.Score.GoodCount;
        _analyzeBadCount = _engine.Score.BadCount;
        _analyzeMissCount = _engine.Score.MissCount;
        // Compute miss streak approximation
        int totalHits = _engine.Score.PerfectCount + _engine.Score.GreatCount + _engine.Score.BetterCount + _engine.Score.GoodCount + _engine.Score.BadCount;
        _analyzeMissStreak = _engine.Score.MissCount > 0 ? Math.Max(1, _engine.Score.MissCount / Math.Max(1, totalHits) + 1) : 0;

        // Store song info
        SongEntry? song = GetSelectedSong();
        _analyzeSongTitle = song?.Title ?? "Unknown";
        _analyzeSongArtist = song?.Artist ?? "Unknown";
        _analyzeSongArtworkStyle = song?.ArtworkStyle ?? 0;
        _analyzeHighestScore = _playerProgress.HighestScore;

        RecordAchievementProgress();
        _engine.Stop();
        _isCountdownActive = false;

        // Update highest score after recording
        _analyzeHighestScore = Math.Max(_analyzeHighestScore, _analyzeScore);

        _isAnalyzeOkHovered = false;
        _screen = UiScreen.Analyze;
        _audio.StopAllSounds();
        if (!_timer.Enabled && HasPendingAchievementToast())
            _timer.Start();
        else if (!HasPendingAchievementToast())
            _timer.Stop();
        Invalidate();
    }

    private void RecordAchievementProgress()
    {
        GameSessionSummary session = new(
            _engine.Score.Score,
            _engine.Score.MaxCombo,
            _engine.Score.PerfectCount,
            _engine.Score.GreatCount,
            _engine.Score.BetterCount,
            _engine.Score.GoodCount,
            _engine.Score.BadCount,
            _engine.Score.MissCount);

        if (!session.HasPlayableData)
            return;

        List<AchievementDefinition> unlocked = AchievementCatalog.ApplySession(_playerProgress, session);
        _achievementStore.Save(_playerProgress);
        EnqueueAchievementToasts(unlocked);
    }

    private void CancelCountdown()
    {
        _isCountdownActive = false;
        _audio.StopAllSounds();
        _audio.PlayMainScreenBgm();
        _timer.Stop();
        _screen = UiScreen.MainMenu;
    }

    // ── 렌더링 ────────────────────────────────────────────────────────────────
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        bool inGame = _engine.IsRunning || _isCountdownActive;
        if (inGame)
        {
            g.SmoothingMode = SmoothingMode.HighSpeed;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighSpeed;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SystemDefault;
        }
        else
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
        }
        g.Clear(inGame ? Color.FromArgb(10, 14, 24) : ClearColor);

        if (_screen == UiScreen.Splash)
        {
            DrawSplash(g);
            return;
        }

        if (_isCountdownActive)
        {
            DrawCountdown(g);
            return;
        }

        if (_engine.IsRunning)
        {
            DrawGame(g);
            return;
        }

        var state = g.Save();
        g.TranslateTransform(_layoutOffsetX, _layoutOffsetY);
        if (_screen == UiScreen.Settings) DrawSettings(g);
        else if (_screen == UiScreen.SongSelect) DrawSongSelect(g);
        else if (_screen == UiScreen.AchievementDetail) DrawAchievementDetail(g);
        else if (_screen == UiScreen.Achievement) DrawAchievement(g);
        else if (_screen == UiScreen.Analyze) DrawAnalyze(g);
        else if (_screen == UiScreen.MainMenu) DrawMenu(g);

        if (_vsyncEnabled)
        {
            try { DwmFlush(); } catch { /* DWM not available */ }
        }
        g.Restore(state);

        if (_activeAchievementToast is not null)
            DrawAchievementToast(g, _activeAchievementToast);
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
        float slide = (1f - fadeIn) * 18f;

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
            Color.FromArgb(255, 255, 255),
            Color.FromArgb(240, 242, 252)))
        {
            g.FillRectangle(bgBrush, 0, 0, w, h);
        }

        // 웨이브 영역 중심 Y (화면 하단 58% 부근)
        float waveCenterY = h * 0.58f;

        // 여러 겹의 동적 웨이브 라인
        DrawSplashWaves(g, w, h, waveCenterY, elapsed);

        // 빛나는 파티클
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
        using var titleBrush = new SolidBrush(Color.FromArgb(52, 120, 210));
        g.DrawString(title1, titleFont, titleBrush, titleX - sz1.Width / 2f, titleY1);
        g.DrawString(title2, titleFont, titleBrush, titleX - sz2.Width / 2f, titleY2);

        // 하단 안내 텍스트 (깜빡임)
        float blink = (float)(Math.Sin(elapsed * 3.0) * 0.5 + 0.5);
        int alpha = (int)(80 + 175 * blink);
        using var hintFont = new Font("Segoe UI", Math.Max(10f, h * 0.018f), FontStyle.Regular);
        using var hintBrush = new SolidBrush(Color.FromArgb(alpha, 100, 130, 170));
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
            Color.FromArgb(0,  255, 255, 255),
            Color.FromArgb(30, 200, 210, 250),
            Color.FromArgb(50, 220, 200, 255),
            Color.FromArgb(30, 200, 210, 250),
            Color.FromArgb(0,  255, 255, 255),
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
        int centerX = (int)ScaleX(DesignWidth / 2f);
        Color accent = GetAccentColor();

        using var titleFont = new Font("Segoe UI", Math.Max(12f, ScaleY(31f)), FontStyle.Bold);
        using var labelFont = new Font("Segoe UI", Math.Max(9f, ScaleY(15f)), FontStyle.Regular);
        using var menuButtonFont = new Font("Segoe UI", Math.Max(10f, ScaleY(24f)), FontStyle.Bold);

        var titleBrush = new SolidBrush(accent);
        string title = "RHYTHM GAME";
        var titleSize = g.MeasureString(title, titleFont);
        float noteGap = ScaleX(12f);
        float noteWidth = ScaleX(28f);
        float groupWidth = titleSize.Width + noteGap + noteWidth;
        float titleX = centerX - groupWidth / 2f;
        float titleY = ScaleY(58f);
        g.DrawString(title, titleFont, titleBrush, titleX, titleY);
        DrawTitleNote(g, titleX + titleSize.Width + noteGap, titleY + ScaleY(8f), accent);
        titleBrush.Dispose();

        for (int i = 0; i < 3; i++)
        {
            var buttonBounds = GetMenuActionButtonBounds(i);
            DrawMenuActionButton(g, buttonBounds, GetMenuActionLabel(i), i == _hoverMenuIndex, menuButtonFont);
        }

        var exitBounds = GetMenuBottomButtonBounds(isRestart: false);
        var restartBounds = GetMenuBottomButtonBounds(isRestart: true);
        DrawExitButton(g, exitBounds, _isExitHovered, labelFont);
        DrawRestartButton(g, restartBounds, _isMenuRestartHovered, labelFont);
    }

    // ── 인게임 화면 ───────────────────────────────────────────────────────────
    private void DrawGame(Graphics g)
    {
        var state = g.Save();

        var playArea = GetPlayAreaBounds();
        int w    = ClientSize.Width;
        int h    = ClientSize.Height;
        int hitY = h - (int)GameEngine.HitZoneOffset;
        int laneWidth = playArea.Width / LaneCount;

        // ── 배경: 어두운 그라데이션 ──
        using (var bgBrush = new LinearGradientBrush(
            new Point(0, 0), new Point(0, h),
            Color.FromArgb(18, 18, 30), Color.FromArgb(10, 10, 20)))
            g.FillRectangle(bgBrush, 0, 0, w, h);

        // ── 플레이 영역 외부: 장식 프레임 ──
        DrawGameFrame(g, playArea);

        // ── 레인 배경: 세련된 그라데이션 ──
        for (int i = 0; i < LaneCount; i++)
        {
            int laneX = playArea.Left + i * laneWidth;
            Rectangle laneBounds = new(laneX, playArea.Top, laneWidth, playArea.Height);

            // 레인 배경: 항상 어두운 톤
            {
                int shade = 22 + i * 3;
                using var laneFill = new LinearGradientBrush(laneBounds,
                    Color.FromArgb(shade, shade, shade + 8),
                    Color.FromArgb(shade + 6, shade + 6, shade + 14),
                    LinearGradientMode.Vertical);
                g.FillRectangle(laneFill, laneBounds);
            }

            // 레인 구분선: 캐시된 펜
            g.DrawLine(_dividerPen, laneX, 0, laneX, h);
        }
        g.DrawLine(_dividerPen, playArea.Right, 0, playArea.Right, h);

        // ── 가이드 라인 (세로 중앙선) ──
        for (int i = 0; i < LaneCount; i++)
        {
            int cx = playArea.Left + i * laneWidth + laneWidth / 2;
            g.DrawLine(_guidePen, cx, 0, cx, hitY - 30);
        }

        // ── 히트존 글로우 이펙트 ──
        DrawHitZoneGlow(g, playArea, hitY);

        // ── 피아노 키 스타일 히트존 ──
        DrawPianoKeys(g, playArea, hitY, laneWidth);

        // ── 노트 그리기 (for loop — 열거자 할당 방지) ──
        var notes = _engine.Notes;
        for (int i = 0; i < notes.Count; i++)
        {
            var note = notes[i];
            if (note.State != NoteState.Active) continue;
            DrawStyledNote(g, note, playArea.Left, laneWidth);
        }

        // ── 게임 모드 효과 (블라인드/안개) ──
        ApplyGameModeEffect(g, playArea, hitY);

        // ── 콤보 & 정확도 HUD ──
        DrawGameHUD(g, playArea, hitY);

        // ── 배속/모드 인디케이터 ──
        DrawSpeedIndicator(g, playArea);
        DrawGameModeIndicator(g, playArea);

        // ── 판정 피드백 ──
        if (_feedback is not null)
        {
            float fbElapsed = (float)(DateTime.Now - _feedbackTime).TotalMilliseconds;
            if (fbElapsed < 600f)
            {
                float prog  = fbElapsed / 600f;
                int   alpha = (int)(255 * (1f - prog));
                float rise  = prog * 20f;
                // 첫 글자로 빠르게 분기 (문자열 비교 제거)
                Color fc = _feedback[0] switch
                {
                    'P' => Color.FromArgb(alpha, 0, 230, 255),
                    'G' when _feedback.Length > 4 => Color.FromArgb(alpha, 100, 255, 200), // GREAT!
                    'G' => Color.FromArgb(alpha, 140, 255, 160), // GOOD
                    'B' when _feedback[1] == 'E' => Color.FromArgb(alpha, 180, 255, 100), // BETTER
                    'B' => Color.FromArgb(alpha, 255, 160, 80), // BAD
                    _   => Color.FromArgb(alpha, 200, 200, 200),
                };
                _reusableFbBrush.Color = fc;
                DrawCentered(g, _feedback, _fbFont, _reusableFbBrush, playArea.Left + playArea.Width / 2, hitY - 90 - (int)rise);
            }
        }

        g.Restore(state);
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

    private void DrawHitZoneGlow(Graphics g, Rectangle playArea, int hitY)
    {
        // 글로우 배경 (히트존 주변)
        int glowHeight = 80;
        Rectangle glowRect = new(playArea.Left, hitY - glowHeight / 2, playArea.Width, glowHeight);
        using var glowBrush = new LinearGradientBrush(glowRect,
            Color.FromArgb(0, 255, 180, 50),
            Color.FromArgb(60, 255, 160, 30),
            LinearGradientMode.Vertical);
        g.FillRectangle(glowBrush, glowRect);

        // 히트존 아래 강한 글로우
        Rectangle bottomGlow = new(playArea.Left, hitY - 4, playArea.Width, 40);
        using var bottomGlowBrush = new LinearGradientBrush(bottomGlow,
            Color.FromArgb(120, 255, 180, 50),
            Color.FromArgb(0, 255, 140, 20),
            LinearGradientMode.Vertical);
        g.FillRectangle(bottomGlowBrush, bottomGlow);

        // 판정선 (밝은 오렌지-골드) — 캐시된 펜 사용
        g.DrawLine(_hitPen1, playArea.Left, hitY, playArea.Right, hitY);
        g.DrawLine(_hitPen2, playArea.Left, hitY - 1, playArea.Right, hitY - 1);
    }

    private void DrawPianoKeys(Graphics g, Rectangle playArea, int hitY, int laneWidth)
    {
        int keyAreaTop = hitY + 4;
        int keyAreaHeight = ClientSize.Height - keyAreaTop;

        for (int i = 0; i < LaneCount; i++)
        {
            int kx = playArea.Left + i * laneWidth;
            Rectangle keyRect = new(kx + 2, keyAreaTop, laneWidth - 4, keyAreaHeight - 4);

            // 피아노 키 배경: 누르면 흰색, 안 누르면 검은색
            bool pressed = _lanePressed[i];
            Color keyTop = pressed ? Color.FromArgb(200, 210, 220) : Color.FromArgb(55, 60, 70);
            Color keyBot = pressed ? Color.FromArgb(170, 180, 195) : Color.FromArgb(35, 40, 50);

            using var keyBrush = new LinearGradientBrush(keyRect, keyTop, keyBot, LinearGradientMode.Vertical);
            using var keyPath = CreateRoundedRect(keyRect, 4f);
            g.FillPath(keyBrush, keyPath);

            // 키 레이블 — 캐시된 폰트/브러시 사용
            DrawCentered(g, LaneLabels[i], _keyLabelFont, pressed ? _keyLabelPressedBrush : _keyLabelReleasedBrush,
                kx + laneWidth / 2, keyAreaTop + keyAreaHeight / 2 - 8);
        }
    }

    private void DrawStyledNote(Graphics g, Note note, int playAreaLeft, int laneWidth)
    {
        int nx = playAreaLeft + note.Lane * laneWidth + 6;
        int ny = (int)note.Y;
        int nw = laneWidth - 12;
        int nh = (int)Note.Height;

        // 노트 글로우 (뒤쪽) — 캐시된 브러시 사용
        Rectangle glowRect = new(nx - 3, ny - 2, nw + 6, nh + 4);
        g.FillRectangle(_noteGlowBrushes[note.Lane], glowRect);

        // 노트 본체 (둥근 바)
        Rectangle noteRect = new(nx, ny, nw, nh);
        using var notePath = CreateRoundedRect(noteRect, 5f);

        // 그라데이션 — 캐시된 색상 사용
        using var noteBrush = new LinearGradientBrush(noteRect, _noteTopColors[note.Lane], _noteBotColors[note.Lane], LinearGradientMode.Vertical);
        g.FillPath(noteBrush, notePath);

        // 노트 하이라이트 (상단 밝은 줄) — 캐시된 브러시
        Rectangle highlightRect = new(nx + 2, ny + 1, nw - 4, nh / 3);
        g.FillRectangle(_noteHighlightBrush, highlightRect);

        // 노트 테두리 — 캐시된 펜
        g.DrawPath(_noteBorderPen, notePath);
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
        int totalNotes = score.PerfectCount + score.GreatCount + score.BetterCount + score.GoodCount + score.BadCount + score.MissCount;
        if (totalNotes > 0)
        {
            float accuracy = (score.PerfectCount * 100f + score.GreatCount * 90f + score.BetterCount * 75f + score.GoodCount * 50f + score.BadCount * 25f) / totalNotes;
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
        if (_engine.IsRunning) return;

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

        if (_screen == UiScreen.Analyze)
        {
            bool okHover = GetAnalyzeOkButtonBounds().Contains(logicalPoint);
            if (okHover != _isAnalyzeOkHovered)
            {
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
        bool restartHovered = GetMenuBottomButtonBounds(isRestart: true).Contains(logicalPoint);

        if (exitHovered != _isExitHovered || restartHovered != _isMenuRestartHovered || hoverIndex != _hoverMenuIndex)
        {
            _isExitHovered = exitHovered;
            _isMenuRestartHovered = restartHovered;
            _hoverMenuIndex = hoverIndex;
            Cursor = exitHovered || restartHovered || hoverIndex >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    private void OnMenuMouseLeave(object? sender, EventArgs e)
    {
        if (_engine.IsRunning) return;

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
            if (_isAnalyzeOkHovered)
            {
                _isAnalyzeOkHovered = false;
                Cursor = Cursors.Default;
                Invalidate();
            }
            return;
        }

        if (_isExitHovered || _isMenuRestartHovered || _hoverMenuIndex != -1)
        {
            _isExitHovered = false;
            _isMenuRestartHovered = false;
            _hoverMenuIndex = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }
    }

    private void OnMenuMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (_engine.IsRunning) return;

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

        if (GetMenuBottomButtonBounds(isRestart: true).Contains(logicalPoint))
        {
            RestartApplicationViaRunBat();
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
            Invalidate();
        }
    }

    private void OnMenuMouseUp(object? sender, MouseEventArgs e)
    {
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
    }

    private int GetHoveredMenuIndex(Point point)
    {
        for (int i = 0; i < 3; i++)
            if (GetMenuActionButtonBounds(i).Contains(point))
                return i;

        return -1;
    }

    private Rectangle GetMenuActionButtonBounds(int index)
    {
        float width = 365f;
        float height = 76f;
        float gap = 22f;
        float startY = 250f;
        float y = startY + index * (height + gap);
        return Rectangle.Round(new RectangleF(ScaleX((DesignWidth - width) / 2f), ScaleY(y), ScaleX(width), ScaleY(height)));
    }

    private static string GetMenuActionLabel(int index)
    {
        return index switch
        {
            0 => "SETTINGS",
            1 => "SONG SELECT",
            _ => "ACHIEVEMENT",
        };
    }

    private void DrawMenuActionButton(Graphics g, Rectangle bounds, string label, bool hovered, Font font)
    {
        Color accent = GetAccentColor();
        var shadowBounds = bounds;
        shadowBounds.Offset(0, (int)ScaleY(8f));
        using (var shadowPath = CreateRoundedRect(shadowBounds, shadowBounds.Height / 2f))
        using (var shadowBrush = new SolidBrush(Color.FromArgb(44, 44, 83, 135)))
        {
            g.FillPath(shadowBrush, shadowPath);
        }

        var drawBounds = bounds;
        if (hovered)
            drawBounds.Offset(0, -(int)ScaleY(3f));

        using var outerPath = CreateRoundedRect(drawBounds, drawBounds.Height / 2f);
        using var fillBrush = new LinearGradientBrush(drawBounds, Color.FromArgb(180, accent), Color.FromArgb(115, accent), LinearGradientMode.Vertical);
        using var glossBrush = new LinearGradientBrush(new Rectangle(drawBounds.X, drawBounds.Y, drawBounds.Width, drawBounds.Height / 2), Color.FromArgb(90, 255, 255, 255), Color.FromArgb(8, 255, 255, 255), LinearGradientMode.Vertical);
        using var outerPen = new Pen(Color.FromArgb(140, accent), 3f);
        using var innerPen = new Pen(Color.FromArgb(180, 210, 233, 255), 2f);
        using var textBrush = new SolidBrush(Color.White);

        g.FillPath(fillBrush, outerPath);
        using (var glossPath = CreateRoundedRect(new Rectangle(drawBounds.X + 4, drawBounds.Y + 4, drawBounds.Width - 8, drawBounds.Height / 2), (drawBounds.Height / 2f) - 4f))
        {
            g.FillPath(glossBrush, glossPath);
        }

        g.DrawPath(outerPen, outerPath);
        using (var innerPath = CreateRoundedRect(new Rectangle(drawBounds.X + 5, drawBounds.Y + 5, drawBounds.Width - 10, drawBounds.Height - 10), (drawBounds.Height - 10) / 2f))
        {
            g.DrawPath(innerPen, innerPath);
        }

        DrawCentered(g, label, font, textBrush, drawBounds.Left + drawBounds.Width / 2, drawBounds.Top + (int)ScaleY(16f));
    }

    private Rectangle GetStartButtonBounds()
    {
        int width = (int)ScaleX(365f);
        int height = (int)ScaleY(94f);
        int x = (int)ScaleX((DesignWidth - 365f) / 2f);
        int y = (int)ScaleY(306f);
        return new Rectangle(x, y, width, height);
    }

    private Rectangle GetExitButtonBounds()
    {
        return GetMenuBottomButtonBounds(isRestart: false);
    }

    private Rectangle GetMenuBottomButtonBounds(bool isRestart)
    {
        float width = 190f;
        float height = 52f;
        float gap = 22f;
        float totalWidth = width * 2f + gap;
        float startX = (DesignWidth - totalWidth) / 2f;
        float x = isRestart ? startX + width + gap : startX;
        float y = 538f;
        return Rectangle.Round(new RectangleF(ScaleX(x), ScaleY(y), ScaleX(width), ScaleY(height)));
    }

    private Rectangle GetMenuCircleBounds(int index)
    {
        int diameter = (int)ScaleX(98f);
        int spacing = (int)ScaleX(220f);
        int centerY = (int)ScaleY(592f);
        int centerX = (int)ScaleX(DesignWidth / 2f) + (index - 1) * spacing;
        return new Rectangle(centerX - diameter / 2, centerY - diameter / 2, diameter, diameter);
    }

    private Rectangle GetPlayAreaBounds()
    {
        float designPlayWidth = 560f;
        float designMargin = 60f;
        int scaledWidth = (int)Math.Min(designPlayWidth * _layoutScale, ClientSize.Width - 2 * designMargin * _layoutScale);
        scaledWidth = Math.Max(scaledWidth, 200);
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
        _layoutOffsetX = (ClientSize.Width - DesignWidth * _layoutScale) / 2f;
        _layoutOffsetY = (ClientSize.Height - DesignHeight * _layoutScale) / 2f;
    }

    private void ApplySettingsToRuntime()
    {
        _engine.NoteSpeedMultiplier = _speedMultiplier;

        _audio.SetBgmVolume(_bgmVolume);
        ApplyDisplayMode();
        ApplyFrameRate();
    }

    private void ApplyFrameRate()
    {
        int idx = Math.Clamp(_frameRateMode, 0, FrameRateIntervals.Length - 1);
        _timer.Interval = FrameRateIntervals[idx];
    }

    private void ApplyDisplayMode()
    {
        if (_isApplyingDisplayMode || IsDisposed)
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
                if (FormBorderStyle == FormBorderStyle.Sizable && WindowState != FormWindowState.Maximized)
                    return;

                WindowState = FormWindowState.Normal;
                FormBorderStyle = FormBorderStyle.Sizable;
                if (_windowedBounds != Rectangle.Empty)
                {
                    Bounds = _windowedBounds;
                }
                else if (Width < 980 || Height < 700)
                {
                    var scr = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
                    float fs = Math.Min(scr.Width * 0.75f / DesignWidth, scr.Height * 0.75f / DesignHeight);
                    ClientSize = new Size(Math.Max((int)(DesignWidth * fs), 960), Math.Max((int)(DesignHeight * fs), 640));
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
        int index = Math.Clamp(_themeColorIndex, 0, ThemeColors.Length - 1);
        return ThemeColors[index];
    }

    // ── Dark Mode 색상 헬퍼 ──────────────────────────────────────────────────
    private Color BgColor1 => _darkModeEnabled ? Color.FromArgb(24, 26, 33) : Color.FromArgb(250, 250, 252);
    private Color BgColor2 => _darkModeEnabled ? Color.FromArgb(18, 20, 26) : Color.FromArgb(243, 244, 247);
    private Color CardFill => _darkModeEnabled ? Color.FromArgb(36, 39, 48) : Color.FromArgb(252, 252, 253);
    private Color CardBorder => _darkModeEnabled ? Color.FromArgb(58, 62, 72) : Color.FromArgb(221, 223, 228);
    private Color CardShadow => _darkModeEnabled ? Color.FromArgb(30, 0, 0, 0) : Color.FromArgb(15, 86, 95, 112);
    private Color SeparatorColor => _darkModeEnabled ? Color.FromArgb(52, 56, 65) : Color.FromArgb(228, 230, 234);
    private Color LabelColor => _darkModeEnabled ? Color.FromArgb(180, 185, 195) : Color.FromArgb(104, 109, 118);
    private Color SubTextColor => _darkModeEnabled ? Color.FromArgb(140, 148, 165) : Color.FromArgb(125, 142, 175);
    private Color PrimaryTextColor => _darkModeEnabled ? Color.FromArgb(220, 225, 235) : Color.FromArgb(43, 84, 158);
    private Color SecondaryTextColor => _darkModeEnabled ? Color.FromArgb(160, 170, 185) : Color.FromArgb(108, 125, 156);
    private Color SliderTrackColor => _darkModeEnabled ? Color.FromArgb(55, 60, 72) : Color.FromArgb(230, 232, 236);
    private Color ToggleOffColor => _darkModeEnabled ? Color.FromArgb(65, 70, 82) : Color.FromArgb(217, 220, 225);
    private Color SegmentBg => _darkModeEnabled ? Color.FromArgb(32, 36, 45) : Color.FromArgb(251, 251, 252);
    private Color SegmentBorder => _darkModeEnabled ? Color.FromArgb(55, 60, 72) : Color.FromArgb(212, 215, 220);
    private Color SegmentText => _darkModeEnabled ? Color.FromArgb(155, 162, 178) : Color.FromArgb(109, 113, 120);
    private Color SegmentDivider => _darkModeEnabled ? Color.FromArgb(50, 55, 66) : Color.FromArgb(226, 228, 232);
    private Color ValuePillBg => _darkModeEnabled ? Color.FromArgb(40, 44, 54) : Color.FromArgb(250, 250, 251);
    private Color ValuePillBorder => _darkModeEnabled ? Color.FromArgb(60, 65, 78) : Color.FromArgb(213, 216, 221);
    private Color ValuePillText => _darkModeEnabled ? Color.FromArgb(175, 182, 195) : Color.FromArgb(100, 104, 111);
    private Color SliderKnobShadow => _darkModeEnabled ? Color.FromArgb(40, 0, 0, 0) : Color.FromArgb(24, 70, 96, 146);
    private Color BackBtnFill => _darkModeEnabled ? Color.FromArgb(40, 44, 54) : Color.FromArgb(250, 250, 251);
    private Color BackBtnBorder => _darkModeEnabled ? Color.FromArgb(60, 65, 78) : Color.FromArgb(204, 206, 212);
    private Color BackBtnArrow => _darkModeEnabled ? Color.FromArgb(160, 170, 185) : Color.FromArgb(101, 110, 126);
    private Color IconColor => _darkModeEnabled ? Color.FromArgb(120, 128, 145) : Color.FromArgb(128, 133, 143);
    private Color ThemeRingColor => _darkModeEnabled ? Color.FromArgb(70, 75, 88) : Color.FromArgb(221, 224, 230);
    private Color HazeTint => _darkModeEnabled ? Color.FromArgb(12, 80, 120, 200) : Color.FromArgb(28, 255, 255, 255);
    private Color ClearColor => _darkModeEnabled ? Color.FromArgb(20, 22, 28) : Color.FromArgb(248, 249, 252);
    private Color PanelFill1 => _darkModeEnabled ? Color.FromArgb(32, 36, 44) : Color.FromArgb(249, 250, 253);
    private Color PanelBorder => _darkModeEnabled ? Color.FromArgb(52, 58, 70) : Color.FromArgb(205, 214, 228);
    private Color PanelDivider => _darkModeEnabled ? Color.FromArgb(48, 54, 65) : Color.FromArgb(221, 226, 235);
    private Color SearchFill1 => _darkModeEnabled ? Color.FromArgb(30, 34, 44) : Color.FromArgb(236, 240, 248);
    private Color SearchFill2 => _darkModeEnabled ? Color.FromArgb(26, 30, 38) : Color.FromArgb(225, 230, 240);
    private Color SearchBorder => _darkModeEnabled ? Color.FromArgb(50, 56, 68) : Color.FromArgb(207, 214, 227);
    private Color SearchIconColor => _darkModeEnabled ? Color.FromArgb(100, 110, 130) : Color.FromArgb(160, 171, 193);
    private Color SearchActiveText => _darkModeEnabled ? Color.FromArgb(180, 190, 210) : Color.FromArgb(94, 108, 138);
    private Color TabFill1 => _darkModeEnabled ? Color.FromArgb(34, 38, 48) : Color.FromArgb(245, 248, 253);
    private Color TabFill2 => _darkModeEnabled ? Color.FromArgb(28, 32, 40) : Color.FromArgb(232, 237, 246);
    private Color TabBorder => _darkModeEnabled ? Color.FromArgb(50, 56, 68) : Color.FromArgb(182, 194, 215);
    private Color TabText => _darkModeEnabled ? Color.FromArgb(130, 140, 160) : Color.FromArgb(122, 137, 164);
    private Color SelectedRowFill1 => _darkModeEnabled ? Color.FromArgb(38, 48, 68) : Color.FromArgb(230, 239, 253);
    private Color SelectedRowFill2 => _darkModeEnabled ? Color.FromArgb(32, 42, 60) : Color.FromArgb(215, 227, 246);
    private Color SelectedRowBorder => _darkModeEnabled ? Color.FromArgb(55, 75, 110) : Color.FromArgb(173, 196, 233);
    private Color RowCircleFill => _darkModeEnabled ? Color.FromArgb(42, 46, 58) : Color.FromArgb(238, 241, 247);
    private Color RowCircleBorder => _darkModeEnabled ? Color.FromArgb(60, 66, 80) : Color.FromArgb(191, 201, 218);
    private Color SelectedCircleFill => _darkModeEnabled ? Color.FromArgb(50, 42, 68) : Color.FromArgb(233, 224, 252);
    private Color SelectedCircleBorder => _darkModeEnabled ? Color.FromArgb(80, 68, 110) : Color.FromArgb(187, 168, 228);
    private Color ScrollTrackColor => _darkModeEnabled ? Color.FromArgb(40, 44, 55) : Color.FromArgb(227, 232, 240);
    private Color ScrollHandleColor => _darkModeEnabled ? Color.FromArgb(70, 78, 95) : Color.FromArgb(184, 194, 211);
    private Color DotColor => _darkModeEnabled ? Color.FromArgb(70, 78, 92) : Color.FromArgb(197, 204, 218);
    private Color DotBorder => _darkModeEnabled ? Color.FromArgb(55, 62, 75) : Color.FromArgb(159, 170, 193);
    private Color ArrowBtnFill1 => _darkModeEnabled ? Color.FromArgb(38, 42, 52) : Color.FromArgb(236, 240, 247);
    private Color ArrowBtnFill2 => _darkModeEnabled ? Color.FromArgb(32, 36, 46) : Color.FromArgb(223, 230, 241);
    private Color ArrowBtnBorder => _darkModeEnabled ? Color.FromArgb(55, 62, 75) : Color.FromArgb(175, 188, 209);
    private Color ArrowColor => _darkModeEnabled ? Color.FromArgb(120, 132, 155) : Color.FromArgb(128, 145, 177);
    private Color CloseBtnFill1 => _darkModeEnabled ? Color.FromArgb(38, 42, 52) : Color.FromArgb(238, 243, 251);
    private Color CloseBtnFill2 => _darkModeEnabled ? Color.FromArgb(32, 36, 46) : Color.FromArgb(225, 232, 245);
    private Color CloseBtnBorder => _darkModeEnabled ? Color.FromArgb(55, 62, 78) : Color.FromArgb(178, 192, 213);
    private Color CloseBtnX => _darkModeEnabled ? Color.FromArgb(140, 155, 185) : Color.FromArgb(75, 112, 179);
    private Color AchCardFill1 => _darkModeEnabled ? Color.FromArgb(34, 40, 52) : Color.FromArgb(248, 250, 253);
    private Color AchCardFill2 => _darkModeEnabled ? Color.FromArgb(28, 34, 44) : Color.FromArgb(237, 242, 249);
    private Color AchCardBorder => _darkModeEnabled ? Color.FromArgb(50, 58, 72) : Color.FromArgb(201, 212, 229);
    private Color AchCardText => _darkModeEnabled ? Color.FromArgb(145, 170, 210) : Color.FromArgb(105, 139, 193);
    private Color AchDotPen => _darkModeEnabled ? Color.FromArgb(80, 100, 135) : Color.FromArgb(160, 186, 214);
    private Color ChevronColor => _darkModeEnabled ? Color.FromArgb(90, 115, 160) : Color.FromArgb(110, 143, 196);
    private Color ExitBtnFill1 => _darkModeEnabled ? Color.FromArgb(34, 38, 48) : Color.FromArgb(250, 252, 255);
    private Color ExitBtnFill2 => _darkModeEnabled ? Color.FromArgb(28, 32, 42) : Color.FromArgb(237, 243, 252);
    private Color ExitBtnHoverFill1 => _darkModeEnabled ? Color.FromArgb(38, 42, 54) : Color.FromArgb(242, 247, 255);
    private Color ExitBtnHoverFill2 => _darkModeEnabled ? Color.FromArgb(32, 36, 48) : Color.FromArgb(223, 233, 248);
    private Color AnalyzeBg1 => _darkModeEnabled ? Color.FromArgb(22, 26, 38) : Color.FromArgb(235, 240, 252);
    private Color AnalyzeBg2 => _darkModeEnabled ? Color.FromArgb(16, 20, 30) : Color.FromArgb(218, 228, 248);
    private Color AnalyzeTitle => _darkModeEnabled ? Color.FromArgb(140, 165, 220) : Color.FromArgb(72, 96, 168);
    private Color AnalyzePanelFill1 => _darkModeEnabled ? Color.FromArgb(32, 36, 48) : Color.FromArgb(252, 253, 255);
    private Color AnalyzePanelFill2 => _darkModeEnabled ? Color.FromArgb(26, 30, 40) : Color.FromArgb(238, 243, 253);
    private Color AnalyzePanelBorder => _darkModeEnabled ? Color.FromArgb(48, 55, 72) : Color.FromArgb(195, 210, 235);
    private Color AnalyzeRowAlt1 => _darkModeEnabled ? Color.FromArgb(30, 35, 48) : Color.FromArgb(240, 244, 252);
    private Color AnalyzeRowAlt2 => _darkModeEnabled ? Color.FromArgb(34, 40, 52) : Color.FromArgb(246, 248, 254);
    private Color AnalyzeRowBorder => _darkModeEnabled ? Color.FromArgb(48, 55, 70) : Color.FromArgb(210, 220, 238);
    private Color AnalyzeLabelColor => _darkModeEnabled ? Color.FromArgb(160, 175, 210) : Color.FromArgb(55, 75, 130);
    private Color AnalyzeValueColor => _darkModeEnabled ? Color.FromArgb(140, 165, 215) : Color.FromArgb(60, 85, 150);
    private Color AnalyzeSongTitle => _darkModeEnabled ? Color.FromArgb(170, 185, 220) : Color.FromArgb(50, 70, 120);
    private Color AnalyzeSongArtist => _darkModeEnabled ? Color.FromArgb(130, 150, 185) : Color.FromArgb(120, 140, 175);
    private Color AnalyzeStatLabel => _darkModeEnabled ? Color.FromArgb(140, 160, 200) : Color.FromArgb(100, 120, 160);
    private Color AnalyzeStatValue => _darkModeEnabled ? Color.FromArgb(160, 175, 215) : Color.FromArgb(55, 75, 135);
    private Color AchDetailTabFill1 => _darkModeEnabled ? Color.FromArgb(34, 38, 48) : Color.FromArgb(246, 249, 253);
    private Color AchDetailTabFill2 => _darkModeEnabled ? Color.FromArgb(28, 32, 40) : Color.FromArgb(229, 237, 248);
    private Color AchDetailTabBorder => _darkModeEnabled ? Color.FromArgb(48, 55, 68) : Color.FromArgb(200, 214, 232);
    private Color AchDetailSelectedFill => _darkModeEnabled ? Color.FromArgb(42, 48, 60) : Color.FromArgb(255, 255, 255);
    private Color AchDetailSelectedFill2 => _darkModeEnabled ? Color.FromArgb(36, 42, 54) : Color.FromArgb(242, 247, 253);
    private Color AchDetailSelectedBorder => _darkModeEnabled ? Color.FromArgb(58, 68, 85) : Color.FromArgb(210, 223, 238);
    private Color AchDetailSelectedText => _darkModeEnabled ? Color.FromArgb(145, 170, 210) : Color.FromArgb(98, 130, 184);
    private Color AchDetailUnselectedText => _darkModeEnabled ? Color.FromArgb(110, 130, 160) : Color.FromArgb(142, 165, 193);

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
            drawBounds.Offset(0, -(int)ScaleY(2f));

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
        float diameter = radius * 2f;
        var path = new GraphicsPath();

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
            _timer.Dispose();
            _audio.Dispose();
        }
        base.Dispose(disposing);
    }
}
