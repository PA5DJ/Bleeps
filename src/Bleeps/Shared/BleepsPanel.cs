using System.Drawing.Drawing2D;
using ShackAssistant.Helpers;

namespace ShackAssistant.Forms;

/// <summary>
/// The middle of Mode Master in Bleeps (PLAN-BLEEPS §6, PA5DJ 16 November 2026): a ROOMY receive window - the chat - and
/// ONE line to send. Knows nothing of sound cards, PTT or the receiver: the window feeds it <see cref="Show"/> and sends
/// through <see cref="SendRequested"/>. No quick log in this mode (nothing is logged, §9.5).
/// </summary>
internal sealed class BleepsPanel : UserControl
{
    /// <summary>Send this text (the window queues it when it is busy).</summary>
    public Action<string>? SendRequested;
    public Action? StopRequested;
    /// <summary>Free spot: the receiver's quietest place, tone 0 in Hz; null = none known yet.</summary>
    public Func<double?>? FreeSpotSource;
    /// <summary>The TX frequency (tone 0) changed - the window moves its marker.</summary>
    public Action<double>? TxHzChanged;
    /// <summary>Bleeps Settings changed something - the window takes it over (threads).</summary>
    public Action? OptionsChanged;

    private readonly AppSettings _settings;
    private readonly ChatView _chat;
    private readonly TextBox _txt;
    private readonly Button _btnSend, _btnStop, _btnFree, _btnSettings, _btnNew, _btnEmoji;
    private ToolStripDropDown? _emojiDrop;
    // the picker's own tooltip: ShowAlways, because a drop-down never becomes the active window and a ToolTip only shows in
    // an active one (v1.0.739, PA5DJ: "then the tooltips don't work")
    private readonly ToolTip _emojiTips = new() { ShowAlways = true, InitialDelay = 250, ReshowDelay = 50, AutoPopDelay = 10000 };
    private Label? _emojiInfo;
    private readonly Label _lblTx, _lblCount, _lblStatus;
    private readonly ProgressBar _bar;
    private readonly ComboBox _cboSpeed;
    private readonly CheckBox _chkAuto;
    private readonly ToolTip _tips = new() { AutoPopDelay = 15000, InitialDelay = 400, ReshowDelay = 100 };
    private double _txHz;
    private int _queued;

    public BleepsPanel(AppSettings settings)
    {
        _settings = settings;
        _txHz = Math.Clamp(settings.GetInt("bleeps", "tx_hz", 1500), 200, 2800);

        _chat = new ChatView(settings) { Dock = DockStyle.Fill };
        _chat.CallClicked = call => { _txt.Text = call + ": " + _txt.Text.TrimStart(); _txt.Focus(); _txt.SelectionStart = _txt.TextLength; };
        _chat.TxNear = hz => SetTxHz(hz + 100);
        _chat.NewBelow = n => { _btnNew!.Text = $"▼ {n} new"; _btnNew.Visible = n > 0; };
        _chat.RemoveWaiting = i => RemoveQueued?.Invoke(i);

        _btnNew = new Button { Text = "▼ new", AutoSize = true, Visible = false, Anchor = AnchorStyles.Bottom | AnchorStyles.Right, FlatStyle = FlatStyle.System };
        _btnNew.Click += (_, _) => _chat.ScrollToEnd();
        _tips.SetToolTip(_btnNew, "New messages below - jump to the newest.");
        var rxHost = new Panel { Dock = DockStyle.Fill };
        rxHost.Controls.Add(_btnNew);
        rxHost.Controls.Add(_chat);
        rxHost.Resize += (_, _) => _btnNew.Location = new Point(rxHost.ClientSize.Width - _btnNew.Width - 22, rxHost.ClientSize.Height - _btnNew.Height - 6);
        _btnNew.BringToFront();

        // ── the TX line: one line, Enter = send, Esc = stop ──
        _txt = new TextBox { Dock = DockStyle.Fill, Font = new Font("Arial", 11), MaxLength = 1000 };
        _txt.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Send(); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; StopRequested?.Invoke(); }
        };
        _txt.TextChanged += (_, _) => { EmojiWhileTyping(); UpdateCount(); };
        _tips.SetToolTip(_txt, "Type a message and press Enter to send it. Esc stops sending. While one goes out you can type the next.");
        // emoji (v1.0.736): the picker; typing :) or :beer: works too, and the chat shows the emoji
        _btnEmoji = new Button { AutoSize = false, Size = new Size(LogicalToDeviceUnits(34), LogicalToDeviceUnits(26)), Anchor = AnchorStyles.Left,
                                 FlatStyle = FlatStyle.Standard, UseMnemonic = false, ImageAlign = ContentAlignment.MiddleCenter,
                                 Image = Helpers.Audio.Bleeps.BleepsEmoji.Picture("🙂", LogicalToDeviceUnits(18)) };
        if (_btnEmoji.Image == null) { _btnEmoji.Text = "🙂"; _btnEmoji.Font = new Font("Segoe UI Emoji", 10); }
        _btnEmoji.Click += (_, _) => ShowEmoji();
        _tips.SetToolTip(_btnEmoji, "Emoji: click one to put it in the TX line. You can also type :) ;) :D <3 or a code like :beer: - hover an emoji to see its code. Each goes out in its shortest form.");
        _btnSend = new Button { Text = "Send", AutoSize = true, FlatStyle = FlatStyle.System };
        _btnSend.Click += (_, _) => Send();
        _tips.SetToolTip(_btnSend, "Send the line (Enter).");
        _btnStop = new Button { Text = "Stop", AutoSize = true, FlatStyle = FlatStyle.System, Enabled = false };
        _btnStop.Click += (_, _) => StopRequested?.Invoke();
        _tips.SetToolTip(_btnStop, "Stop sending now (Esc). The others see the message end with ⚠.");

        _lblTx = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Font = new Font("Arial", 9, FontStyle.Bold) };
        _tips.SetToolTip(_lblTx, "Where you send: the lowest of the four tones. Click in the waterfall to move it.");
        _btnFree = new Button { Text = "Free spot", AutoSize = true, FlatStyle = FlatStyle.System };
        _btnFree.Click += (_, _) =>
        {
            if (FreeSpotSource?.Invoke() is { } hz) { SetTxHz(hz); SetStatus($"Free spot: {hz:0} Hz"); }
            else SetStatus("Free spot: listening first - try again in a few seconds");
        };
        _tips.SetToolTip(_btnFree, "Move TX to the quietest place in the passband, away from strong stations.");
        _chkAuto = new CheckBox { Text = "Auto", AutoSize = true, Anchor = AnchorStyles.Left, Checked = settings.GetBool("bleeps", "auto_free_spot", true) };   // on by default (PA5DJ: "turn this option on by default")
        _chkAuto.CheckedChanged += (_, _) => _settings.Set("bleeps", "auto_free_spot", _chkAuto.Checked);
        _tips.SetToolTip(_chkAuto, "Auto free spot: at every Send the program checks your place first. Still free: it stays (so the others find you). Taken or busy: it moves to the quietest place, then sends.");
        _lblCount = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        _tips.SetToolTip(_lblCount, $"How much of a message is used: at most {Helpers.Audio.Bleeps.BleepsCode.MaxTextBytes} bytes (an accent or € takes 2-3).");
        _btnSettings = new Button { Text = "Settings…", AutoSize = true, FlatStyle = FlatStyle.System };
        _btnSettings.Click += (_, _) =>
        {
            using var f = new BleepsSettingsForm(_settings);
            if (f.ShowDialog(FindForm()) == DialogResult.OK) { _chat.ReloadOptions(); OptionsChanged?.Invoke(); }
        };
        _tips.SetToolTip(_btnSettings, "Bleeps Settings: where Hz and dB stand, colours or mono, decoder threads.");
        _bar = new ProgressBar { Width = 140, Height = 14, Anchor = AnchorStyles.Left, Visible = false };
        // the speed is a choice when SENDING - receiving always hears both (PLAN-BLEEPS §10)
        _cboSpeed = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70, Anchor = AnchorStyles.Left };
        foreach (var s in Helpers.Audio.Bleeps.BleepsSpeed.All) _cboSpeed.Items.Add(s.Name);
        _cboSpeed.SelectedItem = Helpers.Audio.Bleeps.BleepsSpeed.ByName(settings.Get("bleeps", "tx_speed", "Normal")).Name;
        _cboSpeed.SelectedIndexChanged += (_, _) =>
        {
            _settings.Set("bleeps", "tx_speed", Speed.Name);
            UpdateTxLabel(); TxHzChanged?.Invoke(_txHz);
        };
        _tips.SetToolTip(_cboSpeed, "How fast you send. Normal: ~3 characters/s, ~83 Hz wide, the most robust. Fast: twice as fast, ~166 Hz wide. Both are always received.");
        _lblStatus = new Label { AutoSize = false, Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };

        var line1 = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 5, Padding = new Padding(0, 4, 0, 0) };
        line1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        line1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        line1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        line1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        line1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        line1.Controls.Add(new Label { Text = "TX", AutoSize = true, Anchor = AnchorStyles.Left, Font = new Font("Arial", 9, FontStyle.Bold) }, 0, 0);
        line1.Controls.Add(_txt, 1, 0);
        line1.Controls.Add(_btnEmoji, 2, 0);
        line1.Controls.Add(_btnSend, 3, 0);
        line1.Controls.Add(_btnStop, 4, 0);

        // the second line holds still: fixed columns, the status text ends in "..." (the lesson of v1.0.688)
        var line2 = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 8 };
        for (int i = 0; i < 7; i++) line2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        line2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        line2.Controls.Add(_lblTx, 0, 0);
        line2.Controls.Add(_cboSpeed, 1, 0);
        line2.Controls.Add(_btnFree, 2, 0);
        line2.Controls.Add(_chkAuto, 3, 0);
        line2.Controls.Add(_lblCount, 4, 0);
        line2.Controls.Add(_bar, 5, 0);
        line2.Controls.Add(_btnSettings, 6, 0);
        line2.Controls.Add(_lblStatus, 7, 0);
        line2.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        var tx = new Panel { Dock = DockStyle.Bottom, Height = 70 };
        tx.Controls.Add(line2);
        tx.Controls.Add(line1);
        tx.Layout += (_, _) => tx.Height = line1.Height + line2.Height + 4;

        Controls.Add(rxHost);
        Controls.Add(tx);
        UpdateTxLabel(); UpdateCount();
        UITheme.Apply(this);
    }

    public double TxHz => _txHz;
    /// <summary>Auto free spot: choose the place at every Send.</summary>
    public bool AutoFreeSpot => _chkAuto.Checked;
    /// <summary>The speed to send at.</summary>
    public Helpers.Audio.Bleeps.BleepsSpeed Speed => Helpers.Audio.Bleeps.BleepsSpeed.ByName(_cboSpeed?.SelectedItem as string);

    public void SetTxHz(double hz)
    {
        _txHz = Math.Round(Math.Clamp(hz, 200, 2800));
        _settings.Set("bleeps", "tx_hz", (int)_txHz);
        UpdateTxLabel();
        TxHzChanged?.Invoke(_txHz);
    }

    private void UpdateTxLabel() => _lblTx.Text = $"TX {_txHz:0} Hz";

    private bool _completing;
    /// <summary>:rain: becomes 🌧 as soon as it is typed, :) at the space after it (v1.0.740).</summary>
    private void EmojiWhileTyping()
    {
        if (_completing) return;
        if (!Helpers.Audio.Bleeps.BleepsEmoji.CompleteAt(_txt.Text, _txt.SelectionStart, out var t, out int caret)) return;
        _completing = true;
        try { _txt.Text = t; _txt.SelectionStart = caret; _txt.SelectionLength = 0; }
        finally { _completing = false; }
    }

    private void UpdateCount()
    {
        int n = System.Text.Encoding.UTF8.GetByteCount(Helpers.Audio.Bleeps.BleepsEmoji.ToWire(_txt.Text));   // what goes on the air
        int max = Helpers.Audio.Bleeps.BleepsCode.MaxTextBytes;
        _lblCount.Text = $"{n} / {max} bytes" + (_queued > 0 ? $"  ({_queued} waiting)" : "");
        _lblCount.ForeColor = n > max ? Color.Red : ForeColor;
    }

    private void Send()
    {
        var t = Helpers.Audio.Bleeps.BleepsEmoji.ToWire(_txt.Text.Trim());   // each emoji in its shortest form (v1.0.736)
        if (t.Length == 0) return;
        if (System.Text.Encoding.UTF8.GetByteCount(t) > Helpers.Audio.Bleeps.BleepsCode.MaxTextBytes)
        { SetStatus($"Too long: at most {Helpers.Audio.Bleeps.BleepsCode.MaxTextBytes} bytes"); return; }
        if (t.Contains('\u0004')) { SetStatus("The text contains a character that cannot be sent"); return; }
        _txt.Clear();
        SendRequested?.Invoke(t);
    }

    /// <summary>What is waiting behind the message on the air: the count next to the bytes, and the lines themselves greyed
    /// at the bottom of the chat (v1.0.734, PA5DJ: "I want to see what the queue holds").</summary>
    public void SetQueued(IReadOnlyList<string> waiting) { _queued = waiting.Count; _chat.SetWaiting(waiting); UpdateCount(); }

    /// <summary>Right-click on a waiting line: take it out of the queue (its place in the queue, 0 = next).</summary>
    public Action<int>? RemoveQueued;

    public void SetSending(bool on) { _btnStop.Enabled = on; _bar.Visible = on; if (!on) _bar.Value = 0; }

    public void SetProgress(double played, double total)
    {
        _bar.Visible = true;
        _bar.Value = total <= 0 ? 0 : (int)Math.Clamp(played / total * 100, 0, 100);
        SetStatus($"Sending {played:0} / {total:0} s");
    }

    public void SetStatus(string s) => _lblStatus.Text = s;

    /// <summary>The receiver's messages as they are now (new, growing, done) - the window calls this on the UI thread.</summary>
    public void Show(IReadOnlyList<ChatView.Entry> entries) => _chat.Upsert(entries);

    /// <summary>Our own message, at the moment it goes out.</summary>
    public long AddOwn(string call, string text, double hz, string speed) => _chat.AddOwn(call, text, hz, speed);
    /// <summary>Our own message heard back on the receiver (full duplex): its mark on our line.</summary>
    public void HeardBack(long id, bool finished, bool complete, double db) => _chat.HeardBack(id, finished, complete, db);

    /// <summary>The colour of a station (the waterfall marker uses the same).</summary>
    public Color ColourOf(string? call) => _chat.ColourOf(call);

    public void FocusInput() => _txt.Focus();

    // ── the emoji picker (v1.0.736) ──

    /// <summary>Put an emoji in the TX line where the caret stands.</summary>
    public void InsertEmoji(string emoji)
    {
        int at = _txt.SelectionStart;
        _txt.SelectedText = emoji;
        _txt.SelectionStart = at + emoji.Length; _txt.SelectionLength = 0;
        _txt.Focus();
    }

    /// <summary>The picker: the set in its groups, a button per emoji with its code as tooltip. Click = in the TX line and
    /// close; Shift+click = in the TX line and stay open (for more than one).</summary>
    private void ShowEmoji()
    {
        var drop = EmojiDrop;
        drop.Show(_btnEmoji, new Point(_btnEmoji.Width - drop.PreferredSize.Width, -drop.PreferredSize.Height));
    }

    /// <summary>Built once, on first use (a harness builds it without showing it).</summary>
    private ToolStripDropDown EmojiDrop
    {
        get
        {
            if (_emojiDrop != null) return _emojiDrop;
            int D(int v) => LogicalToDeviceUnits(v);
            var flow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight,
                                             MaximumSize = new Size(D(16 * 34 + 8), 0), Padding = new Padding(D(4)), BackColor = AppTheme.Field };
            var font = new Font("Segoe UI Emoji", 13);
            const string idle = "Point at an emoji to see its code - type that, or click the emoji";
            _emojiInfo = new Label { Text = idle, AutoSize = true, Font = new Font("Arial", 9, FontStyle.Bold), UseMnemonic = false,
                                     ForeColor = AppTheme.IsDark ? Color.Gainsboro : Color.FromArgb(40, 40, 40), Margin = new Padding(D(2), D(6), 0, D(2)) };
            var head = new Font("Arial", 8, FontStyle.Bold);
            foreach (var g in Helpers.Audio.Bleeps.BleepsEmoji.Groups)
            {
                var lbl = new Label { Text = g, AutoSize = true, Font = head, ForeColor = AppTheme.IsDark ? Color.Silver : Color.DimGray, Margin = new Padding(D(2), D(3), 0, D(1)) };
                flow.Controls.Add(lbl); flow.SetFlowBreak(lbl, true);
                Control? last = null;
                foreach (var it in Helpers.Audio.Bleeps.BleepsEmoji.All.Where(i => i.Group == g))
                {
                    var pic = Helpers.Audio.Bleeps.BleepsEmoji.Picture(it.Emoji, D(24));   // Twemoji in colour (v1.0.737)
                    var b = new Button { Text = pic == null ? it.Emoji : "", Image = pic, ImageAlign = ContentAlignment.MiddleCenter, Font = font,
                                         Size = new Size(D(34), D(34)), Margin = new Padding(0), FlatStyle = FlatStyle.Flat,
                                         TextAlign = ContentAlignment.MiddleCenter, Padding = new Padding(0), Tag = it, UseMnemonic = false,
                                         ForeColor = AppTheme.IsDark ? Color.Gainsboro : Color.Black };
                    b.FlatAppearance.BorderSize = 0;
                    b.FlatAppearance.MouseOverBackColor = AppTheme.IsDark ? Color.FromArgb(70, 80, 95) : Color.FromArgb(215, 230, 250);
                    b.Click += (_, _) =>
                    {
                        InsertEmoji(it.Emoji);
                        if ((ModifierKeys & Keys.Shift) == 0) _emojiDrop?.Close();
                    };
                    _emojiTips.SetToolTip(b, it.Hint);
                    // and always in the line under the grid, tooltip or not
                    b.MouseEnter += (_, _) => { if (_emojiInfo != null) _emojiInfo.Text = it.Emoji + "   " + it.Hint; };
                    b.MouseLeave += (_, _) => { if (_emojiInfo != null) _emojiInfo.Text = idle; };
                    flow.Controls.Add(b); last = b;
                }
                if (last != null) flow.SetFlowBreak(last, true);
            }
            var credit = new Label { Text = Helpers.Audio.Bleeps.BleepsEmoji.Credit, AutoSize = true, Font = new Font("Arial", 7),
                                     ForeColor = AppTheme.IsDark ? Color.Silver : Color.DimGray, Margin = new Padding(D(2), D(6), 0, D(2)) };
            flow.Controls.Add(_emojiInfo); flow.SetFlowBreak(_emojiInfo, true);
            flow.Controls.Add(credit);
            var host = new ToolStripControlHost(flow) { AutoSize = true, Margin = Padding.Empty, Padding = Padding.Empty };
            _emojiDrop = new ToolStripDropDown { Padding = Padding.Empty, AutoClose = true };
            _emojiDrop.Items.Add(host);
            return _emojiDrop;
        }
    }
}

/// <summary>
/// The chat: a line per message, growing while it comes in. Time (UTC, never a date), the call in the station's own colour,
/// the text in the same colour, ✓ or ⚠ at the end. Hz and dB where Bleeps Settings puts them - by default only on
/// mouse-over. Click a call: it goes into the TX line. Right-click: TX next to him, or another colour.
/// </summary>
internal sealed class ChatView : Control
{
    public sealed class Entry
    {
        public required long Id;                 // the receiver's message (or a negative number for our own)
        public DateTime Utc;
        public string? Call;                     // null = came in halfway ('?')
        public string Text = "";
        public bool Complete, Finished, Mine, Late;
        public double Hz, SnrDb;
        /// <summary>Own message only: heard back on our own receiver (full duplex, QO-100) - null = not (yet).</summary>
        public bool? Echo;              // false = coming back, true = done
        public bool EchoComplete;
        public double EchoDb = double.NaN;
        public TimeSpan Length;
        public string Speed = "Normal";
        /// <summary>A line in the send queue, not on the air yet: its place (1 = next).</summary>
        public int Waiting;
    }

    public Action<string>? CallClicked;
    public Action<double>? TxNear;
    public Action<int>? NewBelow;
    public Action<int>? RemoveWaiting;

    private readonly AppSettings _settings;
    private readonly List<Entry> _entries = new();
    // the lines still in the send queue, drawn greyed under everything else - never part of _entries (v1.0.734)
    private readonly List<Entry> _waiting = new();
    private readonly VScrollBar _sb = new() { Dock = DockStyle.Right };
    private readonly ToolTip _tip = new() { InitialDelay = 300, AutoPopDelay = 15000 };
    private readonly Dictionary<string, Color> _colours = new(StringComparer.OrdinalIgnoreCase);
    private long _own = -1;
    private int _newBelow;
    private Font _font = new("Arial", 10), _bold = new("Arial", 10, FontStyle.Bold);
    // layout, per entry: its lines (start char), y and height
    private readonly List<(Entry E, int Y, int H, Rectangle CallRect, List<string> Lines)> _layout = new();
    private int _total;
    private object? _tipFor;
    private string _showHz = "hidden", _showDb = "hidden", _fmtHz = "hz", _fmtDb = "db";
    private bool _mono;

    /// <summary>Colours for the stations: readable on light, grey and dark (checked against each theme's text field).</summary>
    public static readonly Color[] LightPalette =
    {
        Color.FromArgb(0, 92, 175), Color.FromArgb(176, 40, 30), Color.FromArgb(15, 105, 45), Color.FromArgb(130, 50, 160),
        Color.FromArgb(140, 75, 0), Color.FromArgb(0, 104, 110), Color.FromArgb(170, 30, 110), Color.FromArgb(70, 85, 20),
        Color.FromArgb(40, 60, 140), Color.FromArgb(120, 60, 40),
    };
    public static readonly Color[] DarkPalette =
    {
        Color.FromArgb(110, 180, 255), Color.FromArgb(255, 130, 115), Color.FromArgb(120, 220, 130), Color.FromArgb(205, 150, 255),
        Color.FromArgb(255, 190, 90), Color.FromArgb(90, 215, 215), Color.FromArgb(255, 130, 200), Color.FromArgb(200, 215, 110),
        Color.FromArgb(150, 170, 255), Color.FromArgb(225, 175, 140),
    };
    public static Color OwnLight = Color.FromArgb(60, 60, 60), OwnDark = Color.FromArgb(200, 200, 200);

    public ChatView(AppSettings settings)
    {
        _settings = settings;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Controls.Add(_sb);
        _sb.Scroll += (_, _) => { if (AtEnd) { _newBelow = 0; NewBelow?.Invoke(0); } Invalidate(); };
        MouseWheel += (_, e) => ScrollBy(-e.Delta / 120 * 3 * LineH);
        ReloadOptions();
    }

    private int LineH => (int)Math.Ceiling(_font.GetHeight()) + 2;
    private bool AtEnd => _sb.Value >= Math.Max(0, _sb.Maximum - _sb.LargeChange + 1) - 2;

    public void ReloadOptions()
    {
        _showHz = _settings.Get("bleeps", "show_hz", "hidden"); _showDb = _settings.Get("bleeps", "show_db", "hidden");
        _fmtHz = _settings.Get("bleeps", "fmt_hz", "hz"); _fmtDb = _settings.Get("bleeps", "fmt_db", "db");
        _mono = _settings.Get("bleeps", "colours", "colours").Equals("mono", StringComparison.OrdinalIgnoreCase);
        Relayout(); Invalidate();
    }

    /// <summary>A station keeps its colour: the one stored for it, or the next of the palette, remembered.</summary>
    public Color ColourOf(string? call)
    {
        bool dark = AppTheme.IsDark;
        if (_mono) return ForeColor;
        if (string.IsNullOrEmpty(call)) return dark ? Color.FromArgb(170, 170, 170) : Color.FromArgb(110, 110, 110);
        int idx;
        var stored = _settings.Get("bleeps", "colour_" + call.ToUpperInvariant(), "");
        if (!int.TryParse(stored, out idx))
        {
            idx = _settings.GetInt("bleeps", "next_colour", 0) % LightPalette.Length;
            _settings.Set("bleeps", "colour_" + call.ToUpperInvariant(), idx);
            _settings.Set("bleeps", "next_colour", idx + 1);
        }
        var pal = dark ? DarkPalette : LightPalette;
        return pal[((idx % pal.Length) + pal.Length) % pal.Length];
    }

    private void SetColour(string call, int idx) { _settings.Set("bleeps", "colour_" + call.ToUpperInvariant(), idx); Invalidate(); }

    public void Upsert(IReadOnlyList<Entry> list)
    {
        bool end = AtEnd, grew = false;
        foreach (var e in list)
        {
            int i = _entries.FindIndex(x => x.Id == e.Id);
            if (i < 0) { _entries.Add(e); grew = true; }
            else _entries[i] = e;
        }
        _entries.Sort((a, b) => a.Utc.CompareTo(b.Utc));
        if (_entries.Count > 1000) _entries.RemoveRange(0, _entries.Count - 1000);
        Relayout();
        if (end) ScrollToEnd();
        else if (grew) { _newBelow++; NewBelow?.Invoke(_newBelow); }
        Invalidate();
    }

    /// <summary>The send queue as it is now (oldest first), shown greyed at the bottom.</summary>
    public void SetWaiting(IReadOnlyList<string> texts)
    {
        bool end = AtEnd;
        _waiting.Clear();
        for (int i = 0; i < texts.Count; i++)
            _waiting.Add(new Entry { Id = long.MinValue + i, Utc = DateTime.UtcNow, Text = texts[i], Mine = true, Complete = true, Finished = true, SnrDb = double.NaN, Waiting = i + 1 });
        _tipFor = null;
        Relayout();
        if (end || texts.Count > 0) ScrollToEnd();
        Invalidate();
    }

    public long AddOwn(string call, string text, double hz, string speed)
    {
        long id = _own--;
        Upsert(new[] { new Entry { Id = id, Utc = DateTime.UtcNow, Call = call, Text = text, Complete = true, Finished = true, Mine = true, Hz = hz, SnrDb = double.NaN, Speed = speed } });
        return id;
    }

    /// <summary>Our own line: heard back (… while it comes back, then ✓ or ⚠), with the strength it came back at.</summary>
    public void HeardBack(long id, bool finished, bool complete, double db)
    {
        var e = _entries.Find(x => x.Id == id);
        if (e == null) return;
        if (e.Echo == true && !finished) return;        // a done mark is not taken back by a straggler
        e.Echo = finished; e.EchoComplete = complete; e.EchoDb = db;
        _tipFor = null;
        bool end = AtEnd; Relayout(); if (end) ScrollToEnd(); Invalidate();
    }

    public void ScrollToEnd()
    {
        _sb.Value = Math.Max(0, _sb.Maximum - _sb.LargeChange + 1);
        _newBelow = 0; NewBelow?.Invoke(0);
        Invalidate();
    }

    private void ScrollBy(int dy)
    {
        _sb.Value = Math.Clamp(_sb.Value + dy, 0, Math.Max(0, _sb.Maximum - _sb.LargeChange + 1));
        if (AtEnd) { _newBelow = 0; NewBelow?.Invoke(0); }
        Invalidate();
    }

    public string HzText(Entry e) => _fmtHz == "brackets" ? $"[{e.Hz:0}]" : $"{e.Hz:0} Hz";
    public string DbText(Entry e) => double.IsNaN(e.SnrDb) ? "" :
        (e.SnrDb > 15 ? (_fmtDb == "brackets" ? "(>15)" : "> +15 dB") : e.SnrDb < -20 ? (_fmtDb == "brackets" ? "(<-20)" : "< -20 dB")
         : _fmtDb == "brackets" ? $"({e.SnrDb:+0;-0})" : $"{e.SnrDb:+0;-0} dB");

    private string AfterCall(Entry e) => string.Join("  ", new[] { _showHz == "call" ? HzText(e) : null, _showDb == "call" ? DbText(e) : null }.Where(s => !string.IsNullOrEmpty(s)));
    private string AtEndOf(Entry e) => string.Join("  ", new[] { _showHz == "end" ? HzText(e) : null, _showDb == "end" ? DbText(e) : null }.Where(s => !string.IsNullOrEmpty(s)));

    private int TimeW => TextRenderer.MeasureText("00:00:00", _font).Width + 6;

    // ── text with Twemoji pictures (v1.0.737): an emoji of the set is a square picture the height of the font ──
    private int EmojiPx => Math.Max(10, (int)Math.Round(_font.GetHeight()));

    private int Measure(string s)
    {
        int w = 0;
        foreach (var (t, emoji) in Helpers.Audio.Bleeps.BleepsEmoji.Segments(s))
            w += emoji && Helpers.Audio.Bleeps.BleepsEmoji.Picture(t, EmojiPx) != null ? EmojiPx + 2
                 : TextRenderer.MeasureText(t, _font, Size.Empty, TextFormatFlags.NoPadding).Width;
        return w;
    }

    private void DrawRich(Graphics g, string s, Point at, Color col)
    {
        int x = at.X;
        foreach (var (t, emoji) in Helpers.Audio.Bleeps.BleepsEmoji.Segments(s))
        {
            if (emoji && Helpers.Audio.Bleeps.BleepsEmoji.Picture(t, EmojiPx) is { } pic)
            {
                g.DrawImage(pic, new Rectangle(x + 1, at.Y + Math.Max(0, (LineH - 2 - EmojiPx) / 2), EmojiPx, EmojiPx));
                x += EmojiPx + 2;
            }
            else
            {
                TextRenderer.DrawText(g, t, _font, new Point(x, at.Y), col, TextFormatFlags.NoPadding);
                x += TextRenderer.MeasureText(t, _font, Size.Empty, TextFormatFlags.NoPadding).Width;
            }
        }
    }
    private int CallW => TextRenderer.MeasureText("DL/PA5DJ/P", _bold).Width + 8;

    private void Relayout()
    {
        _layout.Clear();
        int y = 0, lh = LineH;
        int textX = TimeW + CallW;
        int width = Math.Max(80, ClientSize.Width - _sb.Width - textX - 8);
        foreach (var e in _entries.Concat(_waiting))
        {
            string body = Helpers.Audio.Bleeps.BleepsEmoji.Render(e.Text);   // :) and :beer: as the emoji (v1.0.736)
            var ac = AfterCall(e); if (ac.Length > 0) body = ac + "  " + body;
            var tail = AtEndOf(e); if (tail.Length > 0) body += "   " + tail;
            body += Mark(e);
            var lines = Wrap(body, width);
            int h = Math.Max(1, lines.Count) * lh + 2;
            var callRect = new Rectangle(TimeW, y, CallW, lh);
            _layout.Add((e, y, h, callRect, lines));
            y += h;
        }
        _total = y;
        _sb.Minimum = 0; _sb.Maximum = Math.Max(0, _total); _sb.LargeChange = Math.Max(1, ClientSize.Height); _sb.SmallChange = lh;
        if (_sb.Value > Math.Max(0, _sb.Maximum - _sb.LargeChange + 1)) _sb.Value = Math.Max(0, _sb.Maximum - _sb.LargeChange + 1);
    }

    private static string Mark(Entry e) =>
        e.Waiting > 0 ? "" :
        e.Mine ? (e.Echo == null ? "" : e.Echo == false ? "  ↩ …" : e.EchoComplete ? "  ↩ ✓" : "  ↩ ⚠")
               : !e.Finished ? " …" : e.Complete ? "  ✓" : "  ⚠";

    private List<string> Wrap(string s, int width)
    {
        var lines = new List<string>();
        foreach (var para in s.Replace("\r", "").Split('\n'))
        {
            var words = para.Split(' ');
            string cur = "";
            foreach (var w in words)
            {
                string tryS = cur.Length == 0 ? w : cur + " " + w;
                if (Measure(tryS) <= width || cur.Length == 0)
                {
                    // a single word wider than the line is cut hard
                    if (cur.Length == 0 && Measure(tryS) > width)
                    {
                        string part = "";
                        foreach (string c in Helpers.Audio.Bleeps.BleepsEmoji.Elements(w))   // an emoji is never cut in half
                        {
                            if (Measure(part + c) > width && part.Length > 0) { lines.Add(part); part = ""; }
                            part += c;
                        }
                        cur = part;
                    }
                    else cur = tryS;
                }
                else { lines.Add(cur); cur = w; }
            }
            lines.Add(cur);
        }
        return lines;
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); bool end = AtEnd; Relayout(); if (end) ScrollToEnd(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Relayout(); }

    protected override void OnPaint(PaintEventArgs pe)
    {
        var g = pe.Graphics;
        g.Clear(AppTheme.Field);
        int top = _sb.Value, lh = LineH, textX = TimeW + CallW;
        var dim = AppTheme.IsDark ? Color.FromArgb(150, 150, 150) : Color.FromArgb(110, 110, 110);
        foreach (var (e, y, h, _, lines) in _layout)
        {
            int yy = y - top;
            if (yy + h < 0 || yy > ClientSize.Height) continue;
            var col = e.Mine ? (AppTheme.IsDark ? OwnDark : OwnLight) : ColourOf(e.Call);
            if (_mono) col = AppTheme.IsDark ? Color.Gainsboro : Color.Black;
            if (e.Waiting > 0)
            {
                // a queued line: greyed, "waiting" where the time stands and its place where the call stands
                TextRenderer.DrawText(g, "waiting", _font, new Point(2, yy), dim, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, $"#{e.Waiting}", _bold, new Point(TimeW, yy), dim, TextFormatFlags.NoPadding);
                for (int i = 0; i < lines.Count; i++)
                    DrawRich(g, lines[i], new Point(textX, yy + i * lh), dim);
                continue;
            }
            TextRenderer.DrawText(g, e.Utc.ToString("HH:mm:ss"), _font, new Point(2, yy), dim, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, e.Call ?? "?", _bold, new Point(TimeW, yy), col, TextFormatFlags.NoPadding);
            for (int i = 0; i < lines.Count; i++)
                DrawRich(g, lines[i], new Point(textX, yy + i * lh), col);
        }
    }

    private (Entry E, Rectangle CallRect, int Y, int H)? Hit(Point p)
    {
        int y = p.Y + _sb.Value;
        foreach (var (e, ey, h, cr, _) in _layout)
            if (y >= ey && y < ey + h) return (e, new Rectangle(cr.X, cr.Y - _sb.Value, cr.Width, cr.Height), ey, h);
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var h = Hit(e.Location);
        object? key = h?.E;
        if (ReferenceEquals(key, _tipFor)) return;
        _tipFor = key;
        if (h is not { } hit) { _tip.SetToolTip(this, null); return; }
        var en = hit.E;
        if (en.Waiting > 0)
        {
            _tip.SetToolTip(this, (en.Waiting == 1 ? "Waiting: goes out as soon as the message on the air is done" : $"Waiting: number {en.Waiting} in the queue") +
                "\nRight-click: take it out of the queue. Stop (Esc) empties the whole queue.");
            Cursor = Cursors.Default;
            return;
        }
        string t = en.Mine ? $"Sent on {HzText(en)}, {en.Speed}" + (en.Echo == null ? "" :
                "\nHeard back on your own receiver (full duplex)" + (en.Echo == false ? ", still coming in" : en.EchoComplete ? ": ✓ complete and checked" : ": ⚠ something was missed") +
                (double.IsNaN(en.EchoDb) ? "" : $", {en.EchoDb:+0;-0} dB")) :
            $"{HzText(en)}" + (DbText(en) is { Length: > 0 } d ? ", " + d : "") + $", {en.Speed}" +
            $"\nStarted {en.Utc:HH:mm:ss} UTC" + (en.Length > TimeSpan.Zero ? $", {en.Length.TotalSeconds:0} s" : "") +
            (en.Late ? "\nCame in halfway: no call, the text cannot be checked" :
             !en.Finished ? "\nStill coming in" : en.Complete ? "\n✓ complete and checked" : "\n⚠ something was missed");
        _tip.SetToolTip(this, t);
        Cursor = hit.CallRect.Contains(e.Location) && en.Call != null && !en.Mine ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (Hit(e.Location) is not { } hit) return;
        var en = hit.E;
        if (en.Waiting > 0)
        {
            if (e.Button != MouseButtons.Right) return;
            var mw = new ContextMenuStrip();
            int k = en.Waiting - 1;
            mw.Items.Add("Remove from the queue", null, (_, _) => RemoveWaiting?.Invoke(k));
            mw.Items.Add("Copy text", null, (_, _) => { if (en.Text.Length > 0) Clipboard.SetText(en.Text); });
            mw.Show(this, e.Location);
            return;
        }
        if (e.Button == MouseButtons.Left && hit.CallRect.Contains(e.Location) && en.Call != null && !en.Mine) CallClicked?.Invoke(en.Call);
        if (e.Button == MouseButtons.Right)
        {
            var m = new ContextMenuStrip();
            m.Items.Add($"TX next to this station ({en.Hz + 100:0} Hz)", null, (_, _) => TxNear?.Invoke(en.Hz));
            if (en.Call != null && !en.Mine)
            {
                var sub = new ToolStripMenuItem("Colour for " + en.Call);
                var pal = AppTheme.IsDark ? DarkPalette : LightPalette;
                for (int i = 0; i < pal.Length; i++)
                {
                    int k = i;
                    var bmp = new Bitmap(16, 16);
                    using (var gg = Graphics.FromImage(bmp)) gg.Clear(pal[i]);
                    sub.DropDownItems.Add($"Colour {i + 1}", bmp, (_, _) => SetColour(en.Call!, k));
                }
                m.Items.Add(sub);
                m.Items.Add($"Copy call {en.Call}", null, (_, _) => Clipboard.SetText(en.Call!));
            }
            m.Items.Add("Copy text", null, (_, _) => { if (en.Text.Length > 0) Clipboard.SetText(Helpers.Audio.Bleeps.BleepsEmoji.Render(en.Text)); });
            m.Show(this, e.Location);
        }
    }
}
