namespace RhythmGame;

/// <summary>Shared night-console palette and native control styling.</summary>
internal static class InterfaceTheme
{
    internal static readonly Color Background = Color.FromArgb(8, 12, 22);
    internal static readonly Color Surface = Color.FromArgb(16, 24, 39);
    internal static readonly Color Raised = Color.FromArgb(24, 35, 56);
    internal static readonly Color Border = Color.FromArgb(43, 57, 82);
    internal static readonly Color Text = Color.FromArgb(237, 242, 252);
    internal static readonly Color Muted = Color.FromArgb(167, 180, 202);
    internal static readonly Color Accent = Color.FromArgb(140, 175, 255);

    internal static void StyleControls(Control parent)
    {
        bool contrast = SystemInformation.HighContrast;
        parent.BackColor = contrast ? SystemColors.Window : Background;
        parent.ForeColor = contrast ? SystemColors.WindowText : Text;
        foreach (Control child in parent.Controls)
        {
            StyleControls(child);
            if (child is Button button)
            {
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = contrast ? SystemColors.WindowText : Border;
                button.FlatAppearance.MouseOverBackColor = contrast ? SystemColors.Highlight : Raised;
                button.BackColor = contrast ? SystemColors.Control : Surface;
                button.Padding = new Padding(10, 6, 10, 6);
                button.MinimumSize = new Size(0, 42);
            }
            else if (child is ComboBox or NumericUpDown or TextBox)
                child.BackColor = contrast ? SystemColors.Window : Surface;
        }
    }
}
