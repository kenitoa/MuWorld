namespace RhythmGame;

public sealed partial class GameForm
{
    private string _libraryGenre = string.Empty;
    private string _librarySource = string.Empty;
    private bool _libraryRecent;
    private int _libraryMinLevel = 1;
    private int _libraryMaxLevel = 15;
    private SongEntry[]? _filteredSource;
    private string _filteredKey = string.Empty;
    private SongEntry[] _filteredResult = [];
    private readonly Dictionary<(string SongId, int Difficulty, int Lanes), int> _libraryLevels = [];
    private static readonly Dictionary<string, string> LibraryStatus = new(StringComparer.Ordinal);

    private void OpenLibraryFilters()
    {
        SongEntry[] songs = GetCurrentSongs();
        using var dialog = new Form { Text = "곡 필터", ClientSize = new Size(480, 380),
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false, MaximizeBox = false };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 7, Padding = new Padding(16) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        ComboBox Choice(string name, IEnumerable<string> values, string selected)
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, AccessibleName = name };
            box.Items.Add("전체");
            box.Items.AddRange(values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).Order().Cast<object>().ToArray());
            box.SelectedIndex = string.IsNullOrEmpty(selected) ? 0 : Math.Max(0, box.Items.IndexOf(selected));
            return box;
        }
        var genre = Choice("장르", songs.Select(s => s.Genre), _libraryGenre);
        var source = Choice("출처", songs.Select(s => s.Source), _librarySource);
        var favorite = new CheckBox { Text = "즐겨찾기만", Checked = _songFavoritesOnly, AutoSize = true };
        var recent = new CheckBox { Text = "최근 30일 플레이", Checked = _libraryRecent, AutoSize = true };
        var min = new NumericUpDown { Minimum = 1, Maximum = 15, Value = _libraryMinLevel, AccessibleName = "최소 레벨" };
        var max = new NumericUpDown { Minimum = 1, Maximum = 15, Value = _libraryMaxLevel, AccessibleName = "최대 레벨" };
        void Row(string name, Control control, int row)
        {
            layout.Controls.Add(new Label { Text = name, AutoSize = true }, 0, row);
            layout.Controls.Add(control, 1, row);
        }
        Row("장르", genre, 0); Row("출처", source, 1); Row("즐겨찾기", favorite, 2);
        Row("최근 기록", recent, 3); Row("최소 레벨", min, 4); Row("최대 레벨", max, 5);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var apply = new Button { Text = "적용", AutoSize = true };
        var reset = new Button { Text = "초기화", AutoSize = true };
        var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.AddRange([apply, reset, cancel]);
        layout.Controls.Add(buttons, 0, 6); layout.SetColumnSpan(buttons, 2);
        dialog.Controls.Add(layout); dialog.AcceptButton = apply; dialog.CancelButton = cancel;
        reset.Click += (_, _) => { genre.SelectedIndex = source.SelectedIndex = 0; favorite.Checked = recent.Checked = false; min.Value = 1; max.Value = 15; };
        apply.Click += (_, _) =>
        {
            if (min.Value > max.Value) { MessageBox.Show(dialog, "최소 레벨이 최대 레벨보다 큽니다."); return; }
            dialog.DialogResult = DialogResult.OK;
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string? selected = GetSelectedSong()?.SongId;
        _libraryGenre = genre.SelectedIndex == 0 ? string.Empty : genre.Text;
        _librarySource = source.SelectedIndex == 0 ? string.Empty : source.Text;
        _songFavoritesOnly = favorite.Checked; _libraryRecent = recent.Checked;
        _libraryMinLevel = (int)min.Value; _libraryMaxLevel = (int)max.Value;
        SongEntry[] filtered = GetFilteredSongs();
        _songSelectSelectedIndex = Math.Max(0, Array.FindIndex(filtered, s => s.SongId == selected));
        _songSelectPageIndex = _songSelectSelectedIndex / SongRowsPerPage;
        _previewSongKey = string.Empty;
        _accessibleScreenKey = string.Empty;
        Invalidate();
    }

    private static string InspectLibraryAssets(string audioPath, SongMetadata metadata)
    {
        var status = new List<string>();
        if (!File.Exists(Path.ChangeExtension(audioPath, ".json"))) status.Add("NO METADATA");
        if (!string.IsNullOrEmpty(metadata.CoverPath) && !File.Exists(metadata.CoverPath)) status.Add("COVER MISSING");
        if (!string.IsNullOrEmpty(metadata.BgaPath))
        {
            if (!File.Exists(metadata.BgaPath)) status.Add("BGA MISSING");
            else if (!new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(metadata.BgaPath).ToLowerInvariant()))
                status.Add("IMAGE BGA ONLY");
        }
        if (!ChartGenerator.HasAllPrecomputedCharts(metadata.Title)) status.Add("CHARTS INCOMPLETE");
        return string.Join(" | ", status);
    }
}
