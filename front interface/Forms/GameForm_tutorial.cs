namespace RhythmGame;

public sealed partial class GameForm
{
    private int _tutorialPromptVersion;
    private bool _tutorialCompleted;
    private Rectangle GetTutorialBounds() => MenuRect(584f, 540f, 512f, 64f);

    private void OpenTutorial()
    {
        _audio.StopAllSounds();
        try
        {
            using var tutorial = new TutorialForm(_laneKeyBindings[0], _audioOffsetMs);
            tutorial.FormBorderStyle = FormBorderStyle.None;
            tutorial.StartPosition = FormStartPosition.Manual;
            tutorial.Bounds = RectangleToScreen(ClientRectangle);
            tutorial.ShowInTaskbar = false;
            tutorial.ShowDialog(this);
            _tutorialPromptVersion = 1;
            _tutorialCompleted |= tutorial.Completed;
            SaveUserSettings();
            _screen = tutorial.CalibrateRequested ? UiScreen.InputCalibration : UiScreen.SongSelect;
            if (tutorial.CalibrateRequested) PrepareInputCalibrationScreen();
            _previewSongKey = string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.Error("Tutorial audio could not be prepared.", ex);
            MessageBox.Show(this, "연습 오디오를 준비하지 못했습니다. 임시 폴더의 쓰기 권한을 확인하세요.", "MuWorld");
        }
        Invalidate();
    }
}
