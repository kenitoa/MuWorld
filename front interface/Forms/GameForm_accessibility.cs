namespace RhythmGame;

public sealed partial class GameForm
{
    private sealed class AccessibleUiNode
    {
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required Rectangle Bounds { get; init; }
        public AccessibleRole Role { get; init; } = AccessibleRole.PushButton;
        public bool ClientCoordinates { get; init; }
        public Action? Invoke { get; init; }
        public Action<int>? Adjust { get; init; }
    }

    private readonly List<AccessibleUiNode> _accessibleNodes = [];
    private int _keyboardFocusIndex = -1;
    private string _accessibleScreenKey = string.Empty;

    protected override AccessibleObject CreateAccessibilityInstance()
    {
        return new GameFormAccessibleObject(this);
    }

    private sealed class GameFormAccessibleObject(GameForm owner) : Control.ControlAccessibleObject(owner)
    {
        public override int GetChildCount()
        {
            owner.RebuildAccessibleNodes();
            return owner._accessibleNodes.Count;
        }

        public override AccessibleObject? GetChild(int index)
        {
            owner.RebuildAccessibleNodes();
            return index >= 0 && index < owner._accessibleNodes.Count
                ? new AccessibleNodeObject(owner, index)
                : null;
        }

        public override AccessibleObject? GetFocused()
        {
            owner.RebuildAccessibleNodes();
            return owner._keyboardFocusIndex >= 0 &&
                owner._keyboardFocusIndex < owner._accessibleNodes.Count &&
                owner.IsAccessibleNodeInteractive(owner._accessibleNodes[owner._keyboardFocusIndex])
                ? new AccessibleNodeObject(owner, owner._keyboardFocusIndex)
                : base.GetFocused();
        }

        public override AccessibleObject? HitTest(int x, int y)
        {
            owner.RebuildAccessibleNodes();
            Point client = owner.PointToClient(new Point(x, y));
            for (int i = 0; i < owner._accessibleNodes.Count; i++)
            {
                if (owner.GetAccessibleNodeClientBounds(owner._accessibleNodes[i]).Contains(client))
                    return new AccessibleNodeObject(owner, i);
            }

            return base.HitTest(x, y);
        }
    }

    private sealed class AccessibleNodeObject(GameForm owner, int index) : AccessibleObject
    {
        private AccessibleUiNode? Node => index >= 0 && index < owner._accessibleNodes.Count ? owner._accessibleNodes[index] : null;

        public override string? Name
        {
            get => Node?.Name ?? string.Empty;
            set { }
        }

        public override string? Description => Node?.Description ?? string.Empty;
        public override AccessibleRole Role => Node?.Role ?? AccessibleRole.PushButton;
        public override AccessibleStates State
        {
            get
            {
                if (!owner.IsAccessibleNodeInteractive(Node))
                    return AccessibleStates.None;

                AccessibleStates state = AccessibleStates.Focusable;
                if (owner._keyboardFocusIndex == index)
                    state |= AccessibleStates.Focused;
                return state;
            }
        }

        public override Rectangle Bounds
        {
            get
            {
                AccessibleUiNode? node = Node;
                if (node is null)
                    return Rectangle.Empty;

                Rectangle bounds = owner.GetAccessibleNodeClientBounds(node);
                Point screen = owner.PointToScreen(bounds.Location);
                return new Rectangle(screen, bounds.Size);
            }
        }

        public override void DoDefaultAction()
        {
            if (!owner.IsAccessibleNodeInteractive(Node))
                return;

            owner.FocusAccessibleNode(index, announce: false);
            owner.InvokeAccessibleNode(index);
        }

        public override void Select(AccessibleSelection flags)
        {
            if ((flags & AccessibleSelection.TakeFocus) != 0 && owner.IsAccessibleNodeInteractive(Node))
                owner.FocusAccessibleNode(index, announce: false);
        }

        public override string DefaultAction => Node switch
        {
            { Adjust: not null } => "Adjust",
            { Invoke: not null } => "Press",
            _ => string.Empty,
        };
    }

    private void RebuildAccessibleNodes()
    {
        int countdownRemainingSeconds = GetAccessibleCountdownRemainingSeconds();
        string screenKey = _engine.IsRunning
            ? _isGamePaused
                ? $"Pause:{_engine.Score.Score}:{_engine.Score.TotalJudgedNotes}"
                : $"InGame:{_engine.Score.Score}:{_engine.Score.TotalJudgedNotes}"
            : _isCountdownActive
                ? $"Countdown:{countdownRemainingSeconds}"
                : _screen switch
                {
                    UiScreen.Settings => $"{_screen}:{_settingsTabIndex}",
                    UiScreen.SongSelect => $"{_screen}:{_songSelectPageIndex}:{_songSelectSelectedIndex}:{_songSelectDifficultyIndex}:{_laneModeIndex}:{_songFavoritesOnly}:{_songSearchQuery}",
                    UiScreen.KeyBindings => $"{_screen}:{_keyBindingModeIndex}",
                    UiScreen.AchievementDetail => $"{_screen}:{_achievementDetailTabIndex}:{_achievementDetailPageIndex}",
                    UiScreen.Analyze => $"{_screen}:{_analyzeScore}:{_analyzeAccuracy:F2}:{_analyzeReplayStatus}",
                    _ => _screen.ToString(),
                };
        screenKey += $":{ClientSize.Width}x{ClientSize.Height}";

        if (screenKey == _accessibleScreenKey && _accessibleNodes.Count > 0)
            return;

        _accessibleScreenKey = screenKey;
        _accessibleNodes.Clear();
        _keyboardFocusIndex = -1;

        if (_engine.IsRunning && _isGamePaused)
        {
            for (int i = 0; i < PauseActionLabels.Length; i++)
            {
                int action = i;
                AddAccessibleNode(PauseActionLabels[i], i == 3 ? "Settings are locked while paused." : $"Activate {PauseActionLabels[i]}.", GetPauseActionButtonBounds(i), AccessibleRole.PushButton, () => HandlePauseOverlayMouseDown(GetPauseActionButtonBounds(action).Center()), clientCoordinates: true);
            }
            return;
        }

        if (_engine.IsRunning)
        {
            AddAccessibleNode(
                "Gameplay",
                $"Playing {LaneCount} key chart. Score {_engine.Score.Score}. Accuracy {_engine.Score.Accuracy:F2} percent. Press Escape or P to pause.",
                new Rectangle(Point.Empty, ClientSize),
                AccessibleRole.Graphic,
                null,
                clientCoordinates: true);
            return;
        }

        if (_isCountdownActive)
        {
            string countdownUnit = countdownRemainingSeconds == 1 ? "second" : "seconds";
            AddAccessibleNode(
                "Game countdown",
                $"Chart starts in {countdownRemainingSeconds} {countdownUnit}. Press Escape to cancel.",
                new Rectangle(Point.Empty, ClientSize),
                AccessibleRole.StaticText,
                null,
                clientCoordinates: true);
            return;
        }

        switch (_screen)
        {
            case UiScreen.MainMenu:
                AddAccessibleNode("Play", "Open song select.", GetMenuActionButtonBounds(1), AccessibleRole.PushButton, () => OpenMainMenuAction(1));
                AddAccessibleNode("Settings", "Open settings.", GetMenuTopSettingsButtonBounds(), AccessibleRole.PushButton, () => OpenMainMenuAction(0));
                AddAccessibleNode("Statistics", "Open player statistics.", GetMenuPlayerBadgeBounds(), AccessibleRole.PushButton, () => OpenMainMenuAction(2));
                AddAccessibleNode("Restart", "Restart the game application.", GetMenuActionButtonBounds(3), AccessibleRole.PushButton, () => OpenMainMenuAction(3));
                AddAccessibleNode("Exit", "Close the game.", GetExitButtonBounds(), AccessibleRole.PushButton, Close);
                break;

            case UiScreen.Settings:
                AddSettingsAccessibleNodes();
                break;

            case UiScreen.SongSelect:
                AddSongSelectAccessibleNodes();
                break;

            case UiScreen.SongDetail:
                AddAccessibleNode("Back", "Return to song select.", GetSongDetailBackButtonBounds(), AccessibleRole.PushButton, () => HandleSongDetailMouseDown(GetSongDetailBackButtonBounds().Center()));
                AddAccessibleNode("Favorite", "Toggle favorite for the selected song.", GetSongDetailFavoriteButtonBounds(), AccessibleRole.PushButton, () => HandleSongDetailMouseDown(GetSongDetailFavoriteButtonBounds().Center()));
                break;

            case UiScreen.Achievement:
                AddAccessibleNode("Home", "Return to main menu.", GetStatisticsHomeButtonBounds(), AccessibleRole.PushButton, () => HandleAchievementMouseDown(GetStatisticsHomeButtonBounds().Center()));
                AddAccessibleNode("Settings", "Open settings.", GetStatisticsSettingsButtonBounds(), AccessibleRole.PushButton, () => HandleAchievementMouseDown(GetStatisticsSettingsButtonBounds().Center()));
                AddAccessibleNode("Back", "Return to main menu.", GetAchievementBackButtonBounds(), AccessibleRole.PushButton, () => HandleAchievementMouseDown(GetAchievementBackButtonBounds().Center()));
                break;

            case UiScreen.AchievementDetail:
                AddAccessibleNode("All achievements tab", "Show locked and unlocked achievements.", GetAchievementDetailTabBounds(0), AccessibleRole.PageTab, () => HandleAchievementDetailMouseDown(GetAchievementDetailTabBounds(0).Center()));
                AddAccessibleNode("Completed achievements tab", "Show completed achievements.", GetAchievementDetailTabBounds(1), AccessibleRole.PageTab, () => HandleAchievementDetailMouseDown(GetAchievementDetailTabBounds(1).Center()));
                Rectangle inner = Rectangle.Inflate(GetAchievementDetailPanelBounds(), -(int)ScaleX(24f), -(int)ScaleY(22f));
                AddAccessibleNode("Previous page", "Move to previous achievement page.", GetAchievementPageArrowBounds(inner, 0), AccessibleRole.PushButton, () => HandleAchievementDetailMouseDown(GetAchievementPageArrowBounds(inner, 0).Center()));
                AddAccessibleNode("Next page", "Move to next achievement page.", GetAchievementPageArrowBounds(inner, 1), AccessibleRole.PushButton, () => HandleAchievementDetailMouseDown(GetAchievementPageArrowBounds(inner, 1).Center()));
                AddAccessibleNode("Back", "Return to achievements.", GetAchievementBackButtonBounds(), AccessibleRole.PushButton, () => HandleAchievementDetailMouseDown(GetAchievementBackButtonBounds().Center()));
                break;

            case UiScreen.Analyze:
                Rectangle resultBounds = GetAnalyzeContentBounds();
                resultBounds.Height = Math.Max(1, resultBounds.Height - (int)ScaleY(130f));
                string replayStatus = string.IsNullOrWhiteSpace(_analyzeReplayStatus)
                    ? string.Empty
                    : $"{_analyzeReplayStatus}. ";
                AddAccessibleNode(
                    $"Result summary. Score {_analyzeScore}. Accuracy {_analyzeAccuracy:F2} percent. Grade {ScoreManager.FormatGrade(_analyzeGrade)}. {ScoreManager.FormatClearType(_analyzeClearType)}.",
                    $"{replayStatus}Max combo {_analyzeMaxCombo}. Max miss streak {_analyzeMissStreak}. {_analyzeFeedback.TimingLabel}. {_analyzeFeedback.FailureLabel}. {_analyzeFeedback.NextGoal}.",
                    resultBounds,
                    AccessibleRole.StaticText,
                    null);
                AddAccessibleNode("Retry", "Play the selected song again.", GetAnalyzeActionButtonBounds(0), AccessibleRole.PushButton, () => HandleAnalyzeMouseDown(GetAnalyzeActionButtonBounds(0).Center()));
                AddAccessibleNode("Song Select", "Return to song selection.", GetAnalyzeActionButtonBounds(1), AccessibleRole.PushButton, () => HandleAnalyzeMouseDown(GetAnalyzeActionButtonBounds(1).Center()));
                if (CanPlayNextSong())
                    AddAccessibleNode("Next", "Play the next song.", GetAnalyzeActionButtonBounds(2), AccessibleRole.PushButton, () => HandleAnalyzeMouseDown(GetAnalyzeActionButtonBounds(2).Center()));
                break;

            case UiScreen.InputCalibration:
                AddAccessibleNode("Back", "Return to settings without saving.", GetCalibrationBackButtonBounds(), AccessibleRole.PushButton, () => HandleInputCalibrationMouseDown(GetCalibrationBackButtonBounds().Center()));
                AddAccessibleNode("Start calibration", "Start or restart input latency calibration.", GetCalibrationStartButtonBounds(), AccessibleRole.PushButton, () => HandleInputCalibrationMouseDown(GetCalibrationStartButtonBounds().Center()));
                break;

            case UiScreen.KeyBindings:
                AddAccessibleNode("Back", "Return to settings.", GetKeyBindingBackButtonBounds(), AccessibleRole.PushButton, () => HandleKeyBindingsMouseDown(GetKeyBindingBackButtonBounds().Center()));
                AddAccessibleNode("Lane mode tabs", "Choose 4K, 5K, 6K, or 7K key binding set.", GetKeyBindingModeTabBounds(), AccessibleRole.PageTab, () => HandleKeyBindingsMouseDown(GetKeyBindingModeTabBounds().Center()));
                for (int lane = 0; lane < LaneModes[_keyBindingModeIndex].Count; lane++)
                {
                    int capturedLane = lane;
                    AddAccessibleNode($"Lane {lane + 1} key", "Press Enter, then press a key to bind this lane.", GetKeyBindingLaneBounds(lane), AccessibleRole.PushButton, () => HandleKeyBindingsMouseDown(GetKeyBindingLaneBounds(capturedLane).Center()));
                }
                AddAccessibleNode("Reset key bindings", "Reset the selected lane mode bindings.", GetKeyBindingResetButtonBounds(), AccessibleRole.PushButton, () => HandleKeyBindingsMouseDown(GetKeyBindingResetButtonBounds().Center()));
                AddAccessibleNode("Done", "Save and return to settings.", GetKeyBindingDoneButtonBounds(), AccessibleRole.PushButton, () => HandleKeyBindingsMouseDown(GetKeyBindingDoneButtonBounds().Center()));
                break;

            case UiScreen.ChartEditor:
                string[] actions = ["Back", "Save", "Undo", "Type", "BPM down", "BPM up", "Time down", "Time up", "Preview"];
                for (int i = 0; i < actions.Length; i++)
                {
                    int action = i;
                    AddAccessibleNode(actions[i], $"Chart editor {actions[i]} action.", GetChartEditorActionBounds(i), AccessibleRole.PushButton, () => HandleChartEditorMouseDown(GetChartEditorActionBounds(action).Center(), MouseButtons.Left));
                }
                AddAccessibleNode("Chart grid", "Chart editing grid. Use keyboard shortcuts for detailed editing.", GetChartEditorGridBounds(), AccessibleRole.Graphic, null);
                break;
        }
    }

    private void AddSettingsAccessibleNodes()
    {
        for (int i = 0; i < SettingsTabLabels.Length; i++)
        {
            int tab = i;
            AddAccessibleNode(SettingsTabLabels[i], $"Open {SettingsTabLabels[i]} settings.", GetSettingsTabBounds(i), AccessibleRole.PageTab, () =>
            {
                _settingsTabIndex = tab;
                _draggedSlider = SettingsSlider.None;
            });
        }

        AddAccessibleNode("Back", "Return to main menu.", GetBackButtonBounds(), AccessibleRole.PushButton, () => HandleSettingsMouseDown(GetBackButtonBounds().Center()));
        AddSliderNode("BGM volume", "Adjust background music volume.", SettingsSlider.Bgm);
        AddSliderNode("Preview volume", "Adjust song preview volume.", SettingsSlider.Preview);
        AddSliderNode("SFX volume", "Adjust hit sound volume.", SettingsSlider.Sfx);
        AddSliderNode("Note speed", "Adjust note scroll speed.", SettingsSlider.NoteSpeed);
        AddSliderNode("Audio offset", "Adjust audio offset in milliseconds.", SettingsSlider.AudioOffset);
        AddAccessibleNode("Hit sound skin", "Cycle hit sound skin.", GetSettingsSegmentBounds("hitskin"), AccessibleRole.ComboBox, () => CycleHitSoundSkin(1), delta => CycleHitSoundSkin(delta));
        AddAccessibleNode("Hit sound mute", "Toggle hit sound mute.", GetSettingsToggleBounds("hitmute"), AccessibleRole.CheckButton, () => HandleSettingsMouseDown(GetSettingsToggleBounds("hitmute").Center()));
        AddAccessibleNode("Hit pitch", "Cycle hit sound pitch.", GetSettingsSegmentBounds("hitpitch"), AccessibleRole.ComboBox, () => CycleHitPitch(1), delta => CycleHitPitch(delta));
        AddAccessibleNode("Lane mode", "Choose 4K, 5K, 6K, or 7K lane mode.", GetSettingsSegmentBounds("lanemode"), AccessibleRole.ComboBox, () => CycleLaneModeSetting(1), delta => CycleLaneModeSetting(delta));
        AddAccessibleNode("Calibration", "Open input latency calibration.", GetCalibrationEntryButtonBounds(), AccessibleRole.PushButton, () => HandleSettingsMouseDown(GetCalibrationEntryButtonBounds().Center()));
        AddAccessibleNode("Key bindings", "Open key binding settings.", GetKeyBindingEntryButtonBounds(), AccessibleRole.PushButton, () => HandleSettingsMouseDown(GetKeyBindingEntryButtonBounds().Center()));
        AddAccessibleNode("Resolution", "Cycle window resolution.", GetSettingsSegmentBounds("display"), AccessibleRole.ComboBox, () => CycleResolutionSetting(1), delta => CycleResolutionSetting(delta));
        AddAccessibleNode("Frame rate", "Cycle frame rate limit.", GetSettingsSegmentBounds("framerate"), AccessibleRole.ComboBox, () => CycleFrameRate(1), delta => CycleFrameRate(delta));
        AddAccessibleNode("Render quality", "Cycle render quality.", GetSettingsSegmentBounds("render"), AccessibleRole.ComboBox, () => CycleRenderQuality(1), delta => CycleRenderQuality(delta));
        AddAccessibleNode("V Sync", "Toggle V Sync.", GetSettingsToggleBounds("vsync"), AccessibleRole.CheckButton, () => HandleSettingsMouseDown(GetSettingsToggleBounds("vsync").Center()));
        AddAccessibleNode("Dark mode", "Toggle dark mode.", GetSettingsToggleBounds("darkmode"), AccessibleRole.CheckButton, () => HandleSettingsMouseDown(GetSettingsToggleBounds("darkmode").Center()));
        AddAccessibleNode("High contrast", "Toggle high contrast.", GetSettingsToggleBounds("highcontrast"), AccessibleRole.CheckButton, () => HandleSettingsMouseDown(GetSettingsToggleBounds("highcontrast").Center()));
        AddAccessibleNode("Color vision", "Cycle color vision mode.", GetSettingsSegmentBounds("colorvision"), AccessibleRole.ComboBox, () => CycleColorVision(1), delta => CycleColorVision(delta));
        AddAccessibleNode("Reduced motion", "Toggle reduced motion.", GetSettingsToggleBounds("reducedmotion"), AccessibleRole.CheckButton, () => HandleSettingsMouseDown(GetSettingsToggleBounds("reducedmotion").Center()));
        AddAccessibleNode("Play mode", "Cycle normal, practice, or auto play mode.", GetSettingsSegmentBounds("playmode"), AccessibleRole.ComboBox, () => CyclePlayMode(1), delta => CyclePlayMode(delta));
        AddSliderNode("Text size", "Adjust result, settings, and song list text size.", SettingsSlider.TextScale);
        AddSliderNode("Splash time", "Adjust splash screen duration.", SettingsSlider.SplashDuration);
        AddAccessibleNode("Reset settings", "Reset all settings to defaults.", GetResetButtonBounds(), AccessibleRole.PushButton, () => HandleSettingsMouseDown(GetResetButtonBounds().Center()));
        AddAccessibleNode("System reset", "Reset all settings to defaults.", GetSettingsSystemResetButtonBounds(), AccessibleRole.PushButton, () => HandleSettingsMouseDown(GetSettingsSystemResetButtonBounds().Center()));
        AddAccessibleNode("Cancel", "Return to main menu.", GetSettingsCancelButtonBounds(), AccessibleRole.PushButton, () => HandleSettingsMouseDown(GetSettingsCancelButtonBounds().Center()));
        AddAccessibleNode("Apply", "Apply and save settings.", GetSettingsApplyButtonBounds(), AccessibleRole.PushButton, () => HandleSettingsMouseDown(GetSettingsApplyButtonBounds().Center()));
    }

    private void AddSongSelectAccessibleNodes()
    {
        Rectangle panel = GetSongSelectPanelBounds();
        AddAccessibleNode("Close song select", "Return to main menu.", GetSongSelectCloseButtonBounds(), AccessibleRole.PushButton, () => HandleSongSelectMouseDown(GetSongSelectCloseButtonBounds().Center()));
        AddAccessibleNode("Search", "Focus song search box.", GetSongSearchBounds(panel), AccessibleRole.Text, () => { _isSongSearchFocused = true; Invalidate(); });
        AddAccessibleNode("Easy difficulty", "Select Easy difficulty.", new Rectangle(GetSongDifficultyBounds(panel).Left, GetSongDifficultyBounds(panel).Top, GetSongDifficultyBounds(panel).Width / 3, GetSongDifficultyBounds(panel).Height), AccessibleRole.PageTab, () => SelectSongDifficulty(0));
        AddAccessibleNode("Normal difficulty", "Select Normal difficulty.", new Rectangle(GetSongDifficultyBounds(panel).Left + GetSongDifficultyBounds(panel).Width / 3, GetSongDifficultyBounds(panel).Top, GetSongDifficultyBounds(panel).Width / 3, GetSongDifficultyBounds(panel).Height), AccessibleRole.PageTab, () => SelectSongDifficulty(1));
        AddAccessibleNode("Hard difficulty", "Select Hard difficulty.", new Rectangle(GetSongDifficultyBounds(panel).Left + 2 * GetSongDifficultyBounds(panel).Width / 3, GetSongDifficultyBounds(panel).Top, GetSongDifficultyBounds(panel).Width / 3, GetSongDifficultyBounds(panel).Height), AccessibleRole.PageTab, () => SelectSongDifficulty(2));
        AddAccessibleNode("Sort", "Cycle song sort mode.", GetSongSortButtonBounds(panel), AccessibleRole.PushButton, () => HandleSongSelectMouseDown(GetSongSortButtonBounds(panel).Center()));
        AddAccessibleNode("Favorites filter", "Toggle favorites filter.", GetSongFavoriteFilterBounds(panel), AccessibleRole.CheckButton, () => HandleSongSelectMouseDown(GetSongFavoriteFilterBounds(panel).Center()));
        AddAccessibleNode("Rescan songs", "Rescan song library.", GetSongRescanButtonBounds(panel), AccessibleRole.PushButton, () => HandleSongSelectMouseDown(GetSongRescanButtonBounds(panel).Center()));
        AddAccessibleNode("Song detail", "Open selected song detail.", GetSongDetailButtonBounds(panel), AccessibleRole.PushButton, () => HandleSongSelectMouseDown(GetSongDetailButtonBounds(panel).Center()));
        AddAccessibleNode("Replay latest compatible", "Play the newest compatible saved replay for the selected song, difficulty, lane mode, chart snapshot, audio file, and game version.", GetSongPlayButtonBounds(panel).WithOffset(0, -(int)ScaleY(68f)), AccessibleRole.PushButton, StartReplayForSelectedSong);

        Rectangle list = GetSongListBounds(panel);
        SongEntry[] songs = GetFilteredSongs();
        for (int i = 0; i < SongRowsPerPage; i++)
        {
            int songIndex = _songSelectPageIndex * SongRowsPerPage + i;
            if (songIndex >= songs.Length)
                continue;
            int captured = songIndex;
            Rectangle row = GetSongRowBounds(list, i);
            AddAccessibleNode($"Song {songs[songIndex].Title}", BuildSongMetadata(songs[songIndex], includeBest: true), row, AccessibleRole.ListItem, () => SelectSongByIndex(captured));
        }

        AddAccessibleNode("Previous song page", "Move to previous song page.", GetSongPrevButtonBounds(panel), AccessibleRole.PushButton, () => HandleSongSelectMouseDown(GetSongPrevButtonBounds(panel).Center()));
        AddAccessibleNode("Next song page", "Move to next song page.", GetSongNextButtonBounds(panel), AccessibleRole.PushButton, () => HandleSongSelectMouseDown(GetSongNextButtonBounds(panel).Center()));
        AddAccessibleNode("Play selected song", "Start the selected chart.", GetSongPlayButtonBounds(panel), AccessibleRole.PushButton, () => HandleSongSelectMouseDown(GetSongPlayButtonBounds(panel).Center()));
    }

    private void AddSliderNode(string name, string description, SettingsSlider slider)
    {
        AddAccessibleNode(name, description, Rectangle.Union(GetSliderTrackBounds(slider), GetSliderValueBounds(slider)), AccessibleRole.Slider, null, delta => AdjustSettingsSlider(slider, delta));
    }

    private void AddAccessibleNode(string name, string description, Rectangle bounds, AccessibleRole role, Action? invoke, Action<int>? adjust = null, bool clientCoordinates = false)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        _accessibleNodes.Add(new AccessibleUiNode
        {
            Name = name,
            Description = description,
            Bounds = bounds,
            Role = role,
            Invoke = invoke,
            Adjust = adjust,
            ClientCoordinates = clientCoordinates,
        });
    }

    private int GetAccessibleCountdownRemainingSeconds()
    {
        if (_countdownStartTime == default)
            return Math.Max(0, _countdownSeconds);

        double elapsedSeconds = Math.Max(0d, (DateTime.Now - _countdownStartTime).TotalSeconds);
        double remainingSeconds = Math.Max(0d, _countdownSeconds - elapsedSeconds);
        return (int)Math.Ceiling(remainingSeconds);
    }

    private bool IsAccessibleNodeInteractive(AccessibleUiNode? node)
    {
        return node?.Invoke is not null || node?.Adjust is not null;
    }

    private bool HandleAccessibilityKeyDown(KeyEventArgs e)
    {
        bool active = !_engine.IsRunning || _isGamePaused;
        if (!active || (_screen == UiScreen.SongSelect && _isSongSearchFocused && e.KeyCode is not (Keys.Tab or Keys.Escape)))
            return false;

        // Screen transitions can happen from the game timer without a keyboard
        // event. Refresh before using a prior focus index so Enter cannot invoke
        // a stale node from the previous screen.
        RebuildAccessibleNodes();

        bool directionalFocus = _screen != UiScreen.SongSelect && e.KeyCode is Keys.Up or Keys.Down;
        if (e.KeyCode == Keys.Tab || directionalFocus)
        {
            if (_accessibleNodes.Count == 0)
                return false;

            int delta = e.Shift || e.KeyCode == Keys.Up ? -1 : 1;
            int nextIndex = FindNextInteractiveAccessibleNode(_keyboardFocusIndex, delta);
            if (nextIndex < 0)
                return false;

            FocusAccessibleNode(nextIndex, announce: true);
            e.SuppressKeyPress = true;
            Invalidate();
            return true;
        }

        if (_keyboardFocusIndex < 0)
            return false;

        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            InvokeAccessibleNode(_keyboardFocusIndex);
            e.SuppressKeyPress = true;
            return true;
        }

        if (e.KeyCode is Keys.Left or Keys.Right)
        {
            int delta = e.KeyCode == Keys.Right ? 1 : -1;
            if (AdjustAccessibleNode(_keyboardFocusIndex, delta))
            {
                e.SuppressKeyPress = true;
                return true;
            }
        }

        return false;
    }

    private int FindNextInteractiveAccessibleNode(int startIndex, int delta)
    {
        if (_accessibleNodes.Count == 0)
            return -1;

        int step = delta < 0 ? -1 : 1;
        int index = startIndex;
        if (index < 0 || index >= _accessibleNodes.Count)
            index = step > 0 ? -1 : 0;

        for (int visited = 0; visited < _accessibleNodes.Count; visited++)
        {
            index = (index + step + _accessibleNodes.Count) % _accessibleNodes.Count;
            if (IsAccessibleNodeInteractive(_accessibleNodes[index]))
                return index;
        }

        return -1;
    }

    private void FocusAccessibleNode(int index, bool announce)
    {
        if (index < 0 || index >= _accessibleNodes.Count || !IsAccessibleNodeInteractive(_accessibleNodes[index]))
            return;

        _keyboardFocusIndex = index;
        if (announce)
            AccessibilityNotifyClients(AccessibleEvents.Focus, index);
    }

    private void InvokeAccessibleNode(int index)
    {
        if (index < 0 || index >= _accessibleNodes.Count || _accessibleNodes[index].Invoke is null)
            return;

        _accessibleNodes[index].Invoke!();
        _accessibleScreenKey = string.Empty;
        Invalidate();
    }

    private bool AdjustAccessibleNode(int index, int delta)
    {
        if (index < 0 || index >= _accessibleNodes.Count || _accessibleNodes[index].Adjust is null)
            return false;

        _accessibleNodes[index].Adjust!(delta);
        Invalidate();
        return true;
    }

    private Rectangle GetAccessibleNodeClientBounds(AccessibleUiNode node)
    {
        if (node.ClientCoordinates)
            return node.Bounds;

        Rectangle bounds = node.Bounds;
        bounds.Offset((int)Math.Round(_layoutOffsetX), (int)Math.Round(_layoutOffsetY));
        return bounds;
    }

    private void DrawKeyboardFocus(Graphics g, bool clientCoordinates)
    {
        RebuildAccessibleNodes();
        if (_keyboardFocusIndex < 0 || _keyboardFocusIndex >= _accessibleNodes.Count)
            return;

        AccessibleUiNode node = _accessibleNodes[_keyboardFocusIndex];
        if (!IsAccessibleNodeInteractive(node) || node.ClientCoordinates != clientCoordinates)
            return;

        Rectangle bounds = Rectangle.Inflate(node.Bounds, (int)ScaleX(4f), (int)ScaleY(4f));
        using var pen = new Pen(Color.FromArgb(245, GetAccentColor()), Math.Max(2f, ScaleY(2.4f)))
        {
            DashStyle = System.Drawing.Drawing2D.DashStyle.Dash,
        };
        g.DrawRectangle(pen, bounds);
    }

    private void OpenMainMenuAction(int index)
    {
        if (index == 3)
        {
            RestartApplicationViaRunBat();
            return;
        }

        _screen = index switch
        {
            0 => UiScreen.Settings,
            1 => UiScreen.SongSelect,
            2 => UiScreen.Achievement,
            _ => _screen,
        };
        Invalidate();
    }

    private void OpenAchievementCard(int card)
    {
        _selectedAchievementCardIndex = card;
        _achievementDetailTabIndex = 0;
        _achievementDetailPageIndex = 0;
        _screen = UiScreen.AchievementDetail;
        Invalidate();
    }

    private void SelectSongDifficulty(int difficulty)
    {
        _songSelectDifficultyIndex = difficulty;
        _songSelectPageIndex = 0;
        _songSelectSelectedIndex = 0;
        _previewSongKey = string.Empty;
        Invalidate();
    }

    private void SelectSongByIndex(int index)
    {
        _songSelectSelectedIndex = index;
        _previewSongKey = string.Empty;
        Invalidate();
    }

    private void AdjustSettingsSlider(SettingsSlider slider, int delta)
    {
        int step = slider switch
        {
            SettingsSlider.AudioOffset => 5,
            SettingsSlider.SplashDuration => 100,
            SettingsSlider.TextScale => 5,
            _ => 5,
        };

        switch (slider)
        {
            case SettingsSlider.Bgm:
                _bgmVolume = Math.Clamp(_bgmVolume + delta * step, 0, 100);
                _audio.SetBgmVolume(_bgmVolume);
                break;
            case SettingsSlider.Preview:
                _previewVolume = Math.Clamp(_previewVolume + delta * step, 0, 100);
                _audio.SetPreviewVolume(_previewVolume);
                break;
            case SettingsSlider.Sfx:
                _sfxVolume = Math.Clamp(_sfxVolume + delta * step, 0, 100);
                _audio.PlayHit(_sfxVolume, Judgment.Great);
                break;
            case SettingsSlider.NoteSpeed:
                _speedMultiplier = Math.Clamp(MathF.Round((_speedMultiplier + delta * 0.05f) * 100f) / 100f, 0.5f, 2.5f);
                ApplySpeedToEngine();
                break;
            case SettingsSlider.AudioOffset:
                _audioOffsetMs = Math.Clamp(_audioOffsetMs + delta * step, -150, 150);
                ApplySettingsToRuntime();
                break;
            case SettingsSlider.LaneBrightness:
                _laneBrightness = Math.Clamp(_laneBrightness + delta * step, 0, 100);
                break;
            case SettingsSlider.TextScale:
                _textScalePercent = Math.Clamp(_textScalePercent + delta * step, 90, 140);
                break;
            case SettingsSlider.SplashDuration:
                _splashDurationMs = Math.Clamp(_splashDurationMs + delta * step, 600, 5000);
                break;
        }
        SaveUserSettings();
    }

    private void CycleHitSoundSkin(int delta)
    {
        string[] labels = GetHitSoundSkinLabels();
        int current = Array.FindIndex(labels, label => string.Equals(label, NormalizeHitSoundSkin(_hitSoundSkin), StringComparison.OrdinalIgnoreCase));
        _hitSoundSkinIndex = (Math.Max(0, current) + Math.Sign(delta) + labels.Length) % labels.Length;
        _hitSoundSkin = labels[_hitSoundSkinIndex];
        ApplySettingsToRuntime();
        SaveUserSettings();
    }

    private void CycleHitPitch(int delta)
    {
        _hitSoundPitch = Math.Clamp(_hitSoundPitch + Math.Sign(delta), -1, 1);
        ApplySettingsToRuntime();
        SaveUserSettings();
    }

    private void CycleLaneModeSetting(int delta)
    {
        _laneModeIndex = (_laneModeIndex + Math.Sign(delta) + LaneModes.Length) % LaneModes.Length;
        _keyBindingModeIndex = _laneModeIndex;
        SaveUserSettings();
    }

    private void CycleFrameRate(int delta)
    {
        _frameRateMode = (_frameRateMode + Math.Sign(delta) + FrameRateLabels.Length) % FrameRateLabels.Length;
        ApplySettingsToRuntime();
        SaveUserSettings();
    }

    private void CycleRenderQuality(int delta)
    {
        _renderQualityMode = (_renderQualityMode + Math.Sign(delta) + RenderQualityLabels.Length) % RenderQualityLabels.Length;
        SaveUserSettings();
    }

    private void CycleColorVision(int delta)
    {
        _colorVisionMode = (_colorVisionMode + Math.Sign(delta) + ColorVisionLabels.Length) % ColorVisionLabels.Length;
        SaveUserSettings();
    }

    private void CyclePlayMode(int delta)
    {
        _playModeIndex = (_playModeIndex + Math.Sign(delta) + PlayModeLabels.Length) % PlayModeLabels.Length;
        SaveUserSettings();
    }
}

internal static class RectangleAccessibilityExtensions
{
    public static Point Center(this Rectangle rectangle)
    {
        return new Point(rectangle.Left + rectangle.Width / 2, rectangle.Top + rectangle.Height / 2);
    }

    public static Rectangle WithOffset(this Rectangle rectangle, int dx, int dy)
    {
        rectangle.Offset(dx, dy);
        return rectangle;
    }
}
