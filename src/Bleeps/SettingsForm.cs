using System.Globalization;
using Microsoft.Win32;
using ShackAssistant.Helpers;
using ShackAssistant.Helpers.Audio.Bleeps;

namespace Bleeps;

/// <summary>Settings: Station, Audio, Radio (Hamlib, rigctld, PTT) and Frequencies (the list behind the band buttons).</summary>
internal sealed class SettingsForm : Form
{
    private readonly AppSettings _s;
    private readonly TextBox _call = new() { Width = 120, CharacterCasing = CharacterCasing.Upper };
    private readonly ComboBox _theme = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly CheckBox _pingReply = new() { Text = "Answer pings automatically", AutoSize = true, Margin = new Padding(3, 3, 3, 6) };
    private readonly NumericUpDown _pingDelay = new() { Minimum = 0, Maximum = 120, Width = 60 };
    private readonly ListBox _black = new() { Width = 220, Height = 220, Sorted = true, SelectionMode = SelectionMode.MultiExtended, IntegralHeight = false };
    private readonly TextBox _blackCall = new() { Width = 220, CharacterCasing = CharacterCasing.Upper };
    private readonly CheckBox _update = new() { Text = "Look for a newer version at start", AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
    private readonly ComboBox _in = Drop(320), _out = Drop(320);
    private readonly ComboBox _model = new() { Width = 320, DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
    private readonly ComboBox _port = new() { Width = 180, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly ComboBox _baud = Drop(90, "1200", "2400", "4800", "9600", "19200", "38400", "57600", "115200");
    private readonly ComboBox _dataBits = Drop(90, "Default", "7", "8"), _stopBits = Drop(90, "Default", "1", "2");
    private readonly ComboBox _hand = Drop(90, "Default", "None", "XONXOFF", "Hardware");
    private readonly ComboBox _dtr = Drop(90, "Default", "High", "Low"), _rts = Drop(90, "Default", "High", "Low");
    private readonly TextBox _civ = new() { Width = 50 };
    /// <summary>The CI-V address we filled in ourselves (the model's default); "" = none.</summary>
    private string _civAuto = "";
    private readonly ComboBox _ptt = Drop(90, "VOX", "CAT", "RTS", "DTR");
    private readonly ComboBox _pttPort = new() { Width = 180, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly ComboBox _rigMode = Drop(90, "None", "USB", "Data");
    private readonly NumericUpDown _delay = Num(0, 1000, 10), _tail = Num(0, 3000, 10), _poll = Num(250, 10000, 250);
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = SystemColors.Window,
        // own header colours: with the visual style the header stays light in dark mode (v1.0.20)
        EnableHeadersVisualStyles = false,
        ColumnHeadersDefaultCellStyle = { BackColor = SystemColors.Control, ForeColor = SystemColors.ControlText,
                                          SelectionBackColor = SystemColors.Control, SelectionForeColor = SystemColors.ControlText },
        GridColor = SystemColors.ControlDark,
    };
    private readonly ToolTip _tips = new() { AutoPopDelay = 20000 };

    public List<FrequencyEntry> Frequencies { get; private set; }

    private static ComboBox Drop(int w, params string[] items)
    {
        var c = new ComboBox { Width = w, DropDownStyle = ComboBoxStyle.DropDownList };
        c.Items.AddRange(items);
        return c;
    }
    private static NumericUpDown Num(int min, int max, int step) => new() { Minimum = min, Maximum = max, Increment = step, Width = 80 };

    public SettingsForm(AppSettings s, List<FrequencyEntry> freqs)
    {
        _s = s;
        Frequencies = freqs.Select(f => new FrequencyEntry { MHz = f.MHz, Region = f.Region, Description = f.Description }).ToList();
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Settings";
        Font = new Font("Segoe UI", 9);
        FormBorderStyle = FormBorderStyle.Sizable; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(620, 660); MinimumSize = new Size(560, 480);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(Page("Station", Station()));
        tabs.TabPages.Add(Page("Audio", AudioPage()));
        tabs.TabPages.Add(Page("Radio", RadioPage()));
        tabs.TabPages.Add(Page("Ping", PingPage()));
        var fp = new TabPage("Frequencies") { Padding = new Padding(8) };
        fp.Controls.Add(FrequencyPage());
        tabs.TabPages.Add(fp);

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(80, 0) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(80, 0) };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        buttons.Controls.Add(cancel); buttons.Controls.Add(ok);
        AcceptButton = ok; CancelButton = cancel; _okButton = ok;
        Controls.Add(tabs);
        Controls.Add(buttons);
        ok.Click += (_, _) => Save();

        FillValues();
        ResumeLayout(false);
        PerformLayout();
        UITheme.Apply(this);
    }

    private static TabPage Page(string title, Control content)
    {
        var p = new TabPage(title) { Padding = new Padding(8), AutoScroll = true };
        p.Controls.Add(content);
        return p;
    }

    private TableLayoutPanel Grid()
    {
        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Top };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return t;
    }

    // the label of each row, so it goes grey WITH its control (v1.0.7, PA5DJ: "grey out items that do not apply to a choice
    // such as CAT" - a grey box beside a black label did not show)
    private readonly Dictionary<Control, Label> _labels = new();

    private void Row(TableLayoutPanel t, string label, Control c, string tip)
    {
        var l = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 10, 3) };
        _labels[c] = l;
        t.Controls.Add(l);
        t.Controls.Add(c);
        _tips.SetToolTip(l, tip); _tips.SetToolTip(c, tip);
    }

    private Control Station()
    {
        var t = Grid();
        Row(t, "Callsign:", _call, "Your callsign. It goes out at the start of every message (the header of Bleeps is the call).");
        _theme.Items.AddRange(AppTheme.Choices);
        Row(t, "Theme:", _theme, "System follows Windows (Settings -> Personalisation -> Colours -> app mode); Light or Dark always. Takes effect when Bleeps starts again.");
        Row(t, "Updates:", _update, "At every start Bleeps asks " + UpdateCheck.Host.Replace("https://", "") + " whether a newer release is out. "
                                  + "Only a newer one gets a message; no internet stays quiet. Help -> Check for updates asks at any time.");
        return t;
    }

    /// <summary>Ping (v1.0.23): answer pings or not, and the blacklist - calls whose pings are never answered - as a list with
    /// Add / Remove (PA5DJ: "the blacklist in a tab of its own, with an Add, Remove structure").</summary>
    private Control PingPage()
    {
        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Top };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _tips.SetToolTip(_pingReply, "When another station pings your call, Bleeps answers by itself with a short pong (how strong it heard the ping). Off = the ping is only shown.");
        t.Controls.Add(_pingReply, 0, 0); t.SetColumnSpan(_pingReply, 2);
        // the countdown before the automatic answer (v1.0.27): a window shows it, the operator can cancel within that time
        var delayRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 12) };
        delayRow.Controls.Add(new Label { Text = "Wait before answering:", AutoSize = true, Margin = new Padding(3, 6, 6, 3) });
        delayRow.Controls.Add(_pingDelay);
        delayRow.Controls.Add(new Label { Text = "s  (0 = answer at once)", AutoSize = true, Margin = new Padding(4, 6, 3, 3) });
        _tips.SetToolTip(_pingDelay, "A ping to you opens a window that counts down; within this time you can click Don't answer. At 0 the answer goes out.");
        t.Controls.Add(delayRow, 0, 1); t.SetColumnSpan(delayRow, 2);
        _pingReply.CheckedChanged += (_, _) => delayRow.Enabled = _pingReply.Checked;
        t.Controls.Add(new Label { Text = "Blacklist - calls whose pings are never answered:", AutoSize = true, Margin = new Padding(3, 3, 3, 4) }, 0, 2);
        t.SetColumnSpan(t.GetControlFromPosition(0, 2)!, 2);

        var add = new Button { Text = "Add", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(80, 0) };
        var remove = new Button { Text = "Remove", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(80, 0), Enabled = false };
        void Add()
        {
            foreach (var c in _blackCall.Text.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var clean = BleepsCode.CleanCall(c, out var why);
                if (clean == null) { Dialogs.Show(this, $"{c}: the call must be {why}.", "Ping blacklist", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                if (!_black.Items.Contains(clean)) _black.Items.Add(clean);
            }
            _blackCall.Clear(); _blackCall.Focus();
        }
        add.Click += (_, _) => Add();
        _blackCall.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Add(); } };   // Enter adds, it does not close Settings
        _blackCall.Enter += (_, _) => AcceptButton = null;
        _blackCall.Leave += (_, _) => AcceptButton = _okButton;
        remove.Click += (_, _) => { foreach (var c in _black.SelectedItems.Cast<object>().ToList()) _black.Items.Remove(c); };
        _black.SelectedIndexChanged += (_, _) => remove.Enabled = _black.SelectedItems.Count > 0;
        _black.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) remove.PerformClick(); };
        _tips.SetToolTip(_blackCall, "A callsign (or several, separated by spaces) - Add or Enter puts it on the list.");
        _tips.SetToolTip(_black, "Pings from these calls are reported but never answered. Select and Remove (or Delete) to take one off.");

        t.Controls.Add(_blackCall, 0, 3); t.Controls.Add(add, 1, 3);
        t.Controls.Add(_black, 0, 4); t.Controls.Add(remove, 1, 4);
        remove.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        return t;
    }

    private Button? _okButton;

    private Control AudioPage()
    {
        var t = Grid();
        _in.Items.AddRange(WaveIn.Devices().ToArray());
        _out.Items.AddRange(WaveOut.Devices().ToArray());
        Row(t, "Input (from the radio):", _in, "The sound card the radio's receive audio comes in on (48 kHz).");
        Row(t, "Output (to the radio):", _out, "The sound card that feeds the radio's transmit audio.");
        return t;
    }

    private Control RadioPage()
    {
        var t = Grid();
        _model.Items.Add(new RigEntry(RigConfig.NoRig, "None (no CAT)"));
        foreach (var r in HamlibNative.RigModels) _model.Items.Add(r);
        foreach (var p in ComPorts()) { _port.Items.Add(p); _pttPort.Items.Add(p); }
        _port.Items.Add("127.0.0.1:4532");
        _pttPort.Items.Insert(0, "");

        Row(t, "Rig:", _model, "The radio, from Hamlib's list. \"Hamlib NET rigctl\" talks to a running rigctld, \"FLRig\" to FLRig. None = no CAT: PTT on RTS/DTR or VOX still works.");
        Row(t, "Port:", _port, "COM port of the radio, or host:port for rigctld (127.0.0.1:4532), FLRig (127.0.0.1:12345) or a radio on the network.");
        Row(t, "Baud rate:", _baud, "Serial speed - the same as in the radio's CAT/CI-V menu.");
        Row(t, "Data bits:", _dataBits, "Default = what Hamlib's backend uses for this radio.");
        Row(t, "Stop bits:", _stopBits, "Default = what Hamlib's backend uses for this radio.");
        Row(t, "Handshake:", _hand, "Default = what Hamlib's backend uses for this radio.");
        Row(t, "DTR:", _dtr, "The DTR line of the CAT port. Some interfaces need it high (power) or low (no PTT).");
        Row(t, "RTS:", _rts, "The RTS line of the CAT port.");
        Row(t, "CI-V address:", _civ, "CI-V radios only (Icom, Xiegu, ...), in hex. Filled in with the model's default from Hamlib (94 for an IC-7300); change it only if you changed it in the radio.");
        Row(t, "PTT:", _ptt, "VOX: the radio keys on the audio. CAT: through Hamlib (also via rigctld). RTS/DTR: a serial line - on the CAT port, or the PTT port below.");
        Row(t, "PTT port:", _pttPort, "For RTS/DTR: a COM port of its own. Empty = the CAT port.");
        Row(t, "Mode:", _rigMode, "What the radio is put in when you pick a band or frequency. Data = PKTUSB (USB-D, DATA-U); None = leave the mode alone.");
        Row(t, "TX delay (ms):", _delay, "Time between PTT and the audio: the relays settle first.");
        Row(t, "PTT tail (ms):", _tail, "Time PTT stays down after the audio.");
        Row(t, "Poll every (ms):", _poll, "How often the frequency is read from the radio.");
        _model.SelectedIndexChanged += (_, _) => UpdateRadio();
        _ptt.SelectedIndexChanged += (_, _) => UpdateRadio();
        return t;
    }

    private void UpdateRadio()
    {
        int model = (_model.SelectedItem as RigEntry)?.Model ?? RigConfig.NoRig;
        bool rig = model != RigConfig.NoRig, net = RigConfig.IsNetworkModel(model);
        _port.Enabled = rig || _ptt.Text is "RTS" or "DTR";
        foreach (var c in new Control[] { _baud, _dataBits, _stopBits, _hand, _dtr, _rts }) c.Enabled = rig && !net;
        // CI-V: filled in with the model's default as soon as Hamlib knows one (v1.0.14). An address of your own stays; one
        // filled in by us follows the model. No default = not a CI-V radio: the box goes grey.
        string civ = rig && !net ? RigConfig.DefaultCivAddress(model) : "";
        _civ.Enabled = civ.Length > 0;
        string cur = _civ.Text.Trim();
        if (cur.Length == 0 || cur.Equals(_civAuto, StringComparison.OrdinalIgnoreCase)) { _civ.Text = civ; _civAuto = civ; }
        _rigMode.Enabled = rig;
        _poll.Enabled = rig;                                   // nothing to read without a radio
        if (!rig && _ptt.Text == "CAT") _ptt.Text = "VOX";
        _pttPort.Enabled = _ptt.Text is "RTS" or "DTR";        // CAT keys through Hamlib, VOX keys on the audio
        _delay.Enabled = _tail.Enabled = _ptt.Text != "VOX";   // "between PTT and the audio" - with VOX there is no PTT
        foreach (var (c, l) in _labels) l.Enabled = c.Enabled;
        if (net && _port.Text.Length == 0) _port.Text = model == 2 ? "127.0.0.1:4532" : "127.0.0.1:12345";
    }

    private Control FrequencyPage()
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Band", HeaderText = "Band", ReadOnly = true, FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Freq", HeaderText = "Frequency (MHz)", FillWeight = 22 });
        var reg = new DataGridViewComboBoxColumn { Name = "Region", HeaderText = "Region", FillWeight = 14, FlatStyle = FlatStyle.Flat };
        reg.Items.AddRange("All", "1", "2", "3");
        _grid.Columns.Add(reg);
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Desc", HeaderText = "Description", FillWeight = 50 });
        _grid.CellValidating += (_, e) =>
        {
            if (e.ColumnIndex == 1 && !Bands.TryParseMHz(e.FormattedValue?.ToString() ?? "", out double _))
            { e.Cancel = true; _grid.Rows[e.RowIndex].ErrorText = "A frequency in MHz, e.g. 14.071"; }
        };
        _grid.CellEndEdit += (_, e) =>
        {
            _grid.Rows[e.RowIndex].ErrorText = "";
            if (e.ColumnIndex == 1 && Bands.TryParseMHz(_grid.Rows[e.RowIndex].Cells[1].Value?.ToString() ?? "", out var mhz))
            {
                _grid.Rows[e.RowIndex].Cells[1].Value = Bands.FormatMHz(mhz);
                _grid.Rows[e.RowIndex].Cells[0].Value = Bands.Of(mhz * 1e6);
            }
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Insert…", null, (_, _) => Insert());
        menu.Items.Add("Delete", null, (_, _) => { foreach (DataGridViewRow r in _grid.SelectedRows) _grid.Rows.Remove(r); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Reset to the defaults", null, (_, _) =>
        {
            if (Dialogs.Show(this, "Replace the whole list with the default Bleeps frequencies?", "Frequencies", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                FillGrid(FrequencyList.Defaults());
        });
        _grid.ContextMenuStrip = menu;
        _tips.SetToolTip(_grid, "The frequencies behind the band buttons and the band list. Right-click: Insert, Delete, Reset. Double-click a cell to edit.");
        FillGrid(Frequencies);

        var add = new Button { Text = "Insert…", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(80, 0) };
        add.Click += (_, _) => Insert();
        var del = new Button { Text = "Delete", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(80, 0) };
        del.Click += (_, _) => { foreach (DataGridViewRow r in _grid.SelectedRows) _grid.Rows.Remove(r); };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        bar.Controls.Add(add); bar.Controls.Add(del);
        var host = new Panel { Dock = DockStyle.Fill };
        host.Controls.Add(_grid);
        host.Controls.Add(bar);
        return host;
    }

    private void Insert()
    {
        string? text = Ask("Insert a frequency", "Frequency in MHz (e.g. 14.071):");
        if (text == null) return;
        if (!Bands.TryParseMHz(text, out var mhz)) { Dialogs.Show(this, "That is not a frequency.", "Frequencies"); return; }
        var list = GridEntries();
        list.Add(new FrequencyEntry { MHz = mhz });
        FillGrid(FrequencyList.Sorted(list));
    }

    private string? Ask(string title, string prompt)
    {
        using var f = new Form { Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                                 MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                 Padding = new Padding(10), Font = Font };
        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 1 };
        var box = new TextBox { Width = 200 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(75, 0) };
        t.Controls.Add(new Label { Text = prompt, AutoSize = true });
        t.Controls.Add(box); t.Controls.Add(ok);
        f.Controls.Add(t);
        f.AcceptButton = ok;
        UITheme.ScaleFromDesign(f);
        UITheme.Apply(f);
        return f.ShowDialog(this) == DialogResult.OK ? box.Text : null;
    }

    private void FillGrid(IEnumerable<FrequencyEntry> list)
    {
        _grid.Rows.Clear();
        foreach (var e in list) _grid.Rows.Add(e.Band, Bands.FormatMHz(e.MHz), e.Region is "1" or "2" or "3" ? e.Region : "All", e.Description);
    }

    private List<FrequencyEntry> GridEntries()
    {
        var list = new List<FrequencyEntry>();
        foreach (DataGridViewRow r in _grid.Rows)
            if (Bands.TryParseMHz(r.Cells[1].Value?.ToString() ?? "", out var mhz))
                list.Add(new FrequencyEntry { MHz = mhz, Region = r.Cells[2].Value?.ToString() ?? "All", Description = r.Cells[3].Value?.ToString() ?? "" });
        return list;
    }

    /// <summary>The serial ports Windows knows, from the registry (no extra package needed).</summary>
    public static List<string> ComPorts()
    {
        var list = new List<string>();
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            if (k != null) foreach (var n in k.GetValueNames()) if (k.GetValue(n) is string p) list.Add(p);
        }
        catch { }
        return list.Distinct().OrderBy(p => p.Length).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void FillValues()
    {
        _call.Text = _s.Get("station", "callsign", "");
        _pingReply.Checked = _s.GetBool("ping", "auto_reply", true);
        _pingDelay.Value = Math.Clamp(_s.GetInt("ping", "answer_delay", 10), 0, 120);
        _black.Items.Clear();
        foreach (var bc in MainForm.PingBlacklist(_s)) _black.Items.Add(bc);
        _update.Checked = _s.GetBool("update", "check_at_start", true);
        _theme.SelectedItem = AppTheme.Choices.FirstOrDefault(c => c.Equals(_s.Get("display", "theme", "System"), StringComparison.OrdinalIgnoreCase)) ?? "System";
        // by NAME first (v1.0.24): a number moves when a USB card comes or goes
        if (_in.Items.Count > 0) _in.SelectedIndex = WaveIn.Resolve(_in.Items.Cast<string>().ToList(), _s.Get("audio", "input_name", ""), _s.GetInt("audio", "input", 0));
        if (_out.Items.Count > 0) _out.SelectedIndex = WaveIn.Resolve(_out.Items.Cast<string>().ToList(), _s.Get("audio", "output_name", ""), _s.GetInt("audio", "output", 0));
        var c = RigConfig.Load(_s);
        _model.SelectedItem = _model.Items.Cast<RigEntry>().FirstOrDefault(r => r.Model == c.Model) ?? _model.Items[0];
        _port.Text = c.Port;
        _baud.Text = c.Baud.ToString(CultureInfo.InvariantCulture);
        if (_baud.SelectedIndex < 0) _baud.SelectedItem = "9600";
        _dataBits.SelectedItem = c.DataBits; _stopBits.SelectedItem = c.StopBits; _hand.SelectedItem = c.Handshake;
        _dtr.SelectedItem = c.Dtr; _rts.SelectedItem = c.Rts;
        foreach (var cb in new[] { _dataBits, _stopBits, _hand, _dtr, _rts }) if (cb.SelectedIndex < 0) cb.SelectedIndex = 0;
        _civ.Text = c.CivAddress;
        _ptt.SelectedItem = c.Ptt; if (_ptt.SelectedIndex < 0) _ptt.SelectedIndex = 0;
        _pttPort.Text = c.PttPort;
        _rigMode.SelectedItem = c.RigMode; if (_rigMode.SelectedIndex < 0) _rigMode.SelectedItem = "Data";
        _delay.Value = c.TxDelayMs; _tail.Value = c.TxTailMs; _poll.Value = Math.Clamp(c.PollMs, 250, 10000);
        UpdateRadio();
    }

    private void Save()
    {
        _s.Set("station", "callsign", _call.Text.Trim().ToUpperInvariant());
        _s.Set("ping", "auto_reply", _pingReply.Checked);
        _s.Set("ping", "answer_delay", (int)_pingDelay.Value);
        _s.Set("ping", "blacklist", string.Join(" ", _black.Items.Cast<string>()));
        _s.Set("update", "check_at_start", _update.Checked);
        string theme = _theme.SelectedItem as string ?? "System";
        if (!theme.Equals(_s.Get("display", "theme", "System"), StringComparison.OrdinalIgnoreCase))
        {
            _s.Set("display", "theme", theme);
            Dialogs.Show(this, $"The theme ({theme}) takes effect when Bleeps starts again.", "Bleeps", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        _s.Set("audio", "input", Math.Max(0, _in.SelectedIndex));
        _s.Set("audio", "output", Math.Max(0, _out.SelectedIndex));
        if (_in.SelectedItem is string inName) _s.Set("audio", "input_name", inName);
        if (_out.SelectedItem is string outName) _s.Set("audio", "output_name", outName);
        var model = _model.SelectedItem as RigEntry
                    ?? _model.Items.Cast<RigEntry>().FirstOrDefault(r => r.Name.Equals(_model.Text, StringComparison.OrdinalIgnoreCase));
        var c = new RigConfig
        {
            Model = model?.Model ?? RigConfig.NoRig,
            Port = _port.Text.Trim(),
            Baud = int.TryParse(_baud.Text, out var b) ? b : 9600,
            DataBits = _dataBits.Text, StopBits = _stopBits.Text, Handshake = _hand.Text, Dtr = _dtr.Text, Rts = _rts.Text,
            CivAddress = _civ.Enabled ? _civ.Text.Trim() : "", Ptt = _ptt.Text, PttPort = _pttPort.Text.Trim(), RigMode = _rigMode.Text,
            TxDelayMs = (int)_delay.Value, TxTailMs = (int)_tail.Value, PollMs = (int)_poll.Value,
        };
        c.Save(_s);
        _grid.EndEdit();
        Frequencies = FrequencyList.Sorted(GridEntries());
        _s.Save();
    }
}
