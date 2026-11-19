using ShackAssistant.Helpers;

namespace ShackAssistant.Forms;

/// <summary>
/// Bleeps Settings (PLAN-BLEEPS §6): where Hz and dB stand in a chat line and how they look (PA5DJ: "Hz and dB on mouse
/// over, or adjustable in settings. where and how you place it"), colours or mono ("every station its own colour
/// (adjustable or mono)"), and the decoder threads ("like WSJT-X" - Auto = cores - 1). A TableLayoutPanel, nothing placed by
/// hand. The wiring (PTT, lead, tail) is in the rig profile, not here.
/// </summary>
internal sealed class BleepsSettingsForm : Form
{
    private readonly AppSettings _s;
    private readonly ComboBox _hzWhere, _hzFmt, _dbWhere, _dbFmt, _colours, _threads;
    private readonly CheckBox _duplex;

    private static readonly (string Key, string Text)[] Where = { ("hidden", "Hidden (mouse-over)"), ("call", "After the call"), ("end", "End of the line") };

    public BleepsSettingsForm(AppSettings s)
    {
        _s = s;
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);      // scales with the screen (v1.0.717: it did not at 150 %)
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Bleeps Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Arial", 9); Padding = new Padding(10);

        // as wide as its longest item, in DESIGN units (the form scales from 96 dpi afterwards) - at a fixed 170 "Colours (each
        // station its own)" was cut off (v1.0.729, seen on the website screenshot)
        ComboBox Box(params string[] items)
        {
            int text = items.Max(i => TextRenderer.MeasureText(i, Font).Width);
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Math.Max(170, (int)Math.Ceiling(text * 96.0 / DeviceDpi) + 30) };
            c.Items.AddRange(items); return c;
        }
        _hzWhere = Box(Where.Select(w => w.Text).ToArray()); _hzFmt = Box("1240 Hz", "[1240]");
        _dbWhere = Box(Where.Select(w => w.Text).ToArray()); _dbFmt = Box("-8 dB", "(-8)");
        _colours = Box("Colours (each station its own)", "Mono");
        _threads = Box(new[] { $"Auto ({Math.Max(1, Environment.ProcessorCount - 1)} of {Environment.ProcessorCount} cores)" }
                       .Concat(Enumerable.Range(1, Environment.ProcessorCount).Select(i => i.ToString())).ToArray());

        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Dock = DockStyle.Top };
        void Row(string label, Control a, Control? b = null)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 8, 3) });
            t.Controls.Add(a);
            if (b != null) t.Controls.Add(b); else t.Controls.Add(new Label { AutoSize = true });
        }
        Row("Frequency (Hz):", _hzWhere, _hzFmt);
        Row("Strength (dB):", _dbWhere, _dbFmt);
        Row("Stations:", _colours);
        Row("Decoder threads:", _threads);
        // full duplex (v1.0.719, PA5DJ): keep receiving while sending
        _duplex = new CheckBox { Text = "Full duplex: keep receiving while sending (QO-100, separate receiver)", AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
        t.Controls.Add(_duplex); t.SetColumnSpan(_duplex, 3);
        var tips = new ToolTip();
        tips.SetToolTip(_hzWhere, "Where the frequency of a message stands. Mouse-over on a message always shows it.");
        tips.SetToolTip(_dbWhere, "Where the strength (SNR in 2500 Hz) stands. Above +15 dB it reads '> +15 dB'.");
        tips.SetToolTip(_colours, "Each station its own colour (right-click a call to change it), or everything in the text colour.");
        tips.SetToolTip(_duplex, "The decoder and the waterfall keep running while a message goes out. On QO-100 you then see your own signal come back: ↩ ✓ on your own line. Leave it off with one HF rig - its receiver is muted while sending.");
        tips.SetToolTip(_threads, "How many processor cores the decoders may use at once. Auto leaves one free for the waterfall and the rest of the program.");

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(cancel); buttons.Controls.Add(ok);
        AcceptButton = ok; CancelButton = cancel;
        var all = new TableLayoutPanel { AutoSize = true, ColumnCount = 1 };
        all.Controls.Add(t); all.Controls.Add(buttons);
        Controls.Add(all);

        int Idx(string key) => Math.Max(0, Array.FindIndex(Where, w => w.Key == s.Get("bleeps", key, "hidden")));
        _hzWhere.SelectedIndex = Idx("show_hz"); _dbWhere.SelectedIndex = Idx("show_db");
        _hzFmt.SelectedIndex = s.Get("bleeps", "fmt_hz", "hz") == "brackets" ? 1 : 0;
        _dbFmt.SelectedIndex = s.Get("bleeps", "fmt_db", "db") == "brackets" ? 1 : 0;
        _colours.SelectedIndex = s.Get("bleeps", "colours", "colours") == "mono" ? 1 : 0;
        _threads.SelectedIndex = Math.Clamp(s.GetInt("bleeps", "threads", 0), 0, Environment.ProcessorCount);
        _duplex.Checked = s.GetBool("bleeps", "full_duplex", false);

        ok.Click += (_, _) => Save();
        ResumeLayout(false);
        PerformLayout();
        UITheme.Apply(this);
    }

    private void Save()
    {
        _s.Set("bleeps", "show_hz", Where[_hzWhere.SelectedIndex].Key);
        _s.Set("bleeps", "show_db", Where[_dbWhere.SelectedIndex].Key);
        _s.Set("bleeps", "fmt_hz", _hzFmt.SelectedIndex == 1 ? "brackets" : "hz");
        _s.Set("bleeps", "fmt_db", _dbFmt.SelectedIndex == 1 ? "brackets" : "db");
        _s.Set("bleeps", "colours", _colours.SelectedIndex == 1 ? "mono" : "colours");
        _s.Set("bleeps", "threads", _threads.SelectedIndex);
        _s.Set("bleeps", "full_duplex", _duplex.Checked);
        _s.Save();
    }
}
