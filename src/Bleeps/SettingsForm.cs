using System.Globalization;
using Microsoft.Win32;
using ShackAssistant.Helpers;

namespace Bleeps;

/// <summary>Settings: Station, Audio, Radio (Hamlib, rigctld, PTT) and Frequencies (the list behind the band buttons).</summary>
internal sealed class SettingsForm : Form
{
    private readonly AppSettings _s;
    private readonly TextBox _call = new() { Width = 120, CharacterCasing = CharacterCasing.Upper };
    private readonly ComboBox _in = Drop(320), _out = Drop(320);
    private readonly ComboBox _model = new() { Width = 320, DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
    private readonly ComboBox _port = new() { Width = 180, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly ComboBox _baud = Drop(90, "1200", "2400", "4800", "9600", "19200", "38400", "57600", "115200");
    private readonly ComboBox _dataBits = Drop(90, "Default", "7", "8"), _stopBits = Drop(90, "Default", "1", "2");
    private readonly ComboBox _hand = Drop(90, "Default", "None", "XONXOFF", "Hardware");
    private readonly ComboBox _dtr = Drop(90, "Default", "High", "Low"), _rts = Drop(90, "Default", "High", "Low");
    private readonly TextBox _civ = new() { Width = 50 };
    private readonly ComboBox _ptt = Drop(90, "VOX", "CAT", "RTS", "DTR");
    private readonly ComboBox _pttPort = new() { Width = 180, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly ComboBox _rigMode = Drop(90, "None", "USB", "Data");
    private readonly NumericUpDown _delay = Num(0, 1000, 10), _tail = Num(0, 3000, 10), _poll = Num(250, 10000, 250);
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = SystemColors.Window,
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
        var fp = new TabPage("Frequencies") { Padding = new Padding(8) };
        fp.Controls.Add(FrequencyPage());
        tabs.TabPages.Add(fp);

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(80, 0) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(80, 0) };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        buttons.Controls.Add(cancel); buttons.Controls.Add(ok);
        AcceptButton = ok; CancelButton = cancel;
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

    // the label of each row, so it goes grey WITH its control (v1.0.7, PA5DJ: "items die niet van toepassing bij een select
    // zoals CAT, grijs maken" - a grey box beside a black label did not show)
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
        return t;
    }

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
        Row(t, "CI-V address:", _civ, "Icom only, in hex (94 for an IC-7300). Empty = the default of the model.");
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
        _civ.Enabled = rig && !net && ((_model.SelectedItem as RigEntry)?.Name.StartsWith("Icom", StringComparison.OrdinalIgnoreCase) ?? false);
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
            if (MessageBox.Show(this, "Replace the whole list with the default Bleeps frequencies?", "Frequencies", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                FillGrid(FrequencyList.Defaults());
        });
        _grid.ContextMenuStrip = menu;
        _tips.SetToolTip(_grid, "The frequencies behind the band buttons and the band list. Right-click: Insert, Delete, Reset. Double-click a cell to edit.");
        FillGrid(Frequencies);

        var add = new Button { Text = "Insert…", AutoSize = true };
        add.Click += (_, _) => Insert();
        var del = new Button { Text = "Delete", AutoSize = true };
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
        if (!Bands.TryParseMHz(text, out var mhz)) { MessageBox.Show(this, "That is not a frequency.", "Frequencies"); return; }
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
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        t.Controls.Add(new Label { Text = prompt, AutoSize = true });
        t.Controls.Add(box); t.Controls.Add(ok);
        f.Controls.Add(t);
        f.AcceptButton = ok;
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
        int i = _s.GetInt("audio", "input", 0), o = _s.GetInt("audio", "output", 0);
        if (_in.Items.Count > 0) _in.SelectedIndex = Math.Clamp(i, 0, _in.Items.Count - 1);
        if (_out.Items.Count > 0) _out.SelectedIndex = Math.Clamp(o, 0, _out.Items.Count - 1);
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
        _s.Set("audio", "input", Math.Max(0, _in.SelectedIndex));
        _s.Set("audio", "output", Math.Max(0, _out.SelectedIndex));
        var model = _model.SelectedItem as RigEntry
                    ?? _model.Items.Cast<RigEntry>().FirstOrDefault(r => r.Name.Equals(_model.Text, StringComparison.OrdinalIgnoreCase));
        var c = new RigConfig
        {
            Model = model?.Model ?? RigConfig.NoRig,
            Port = _port.Text.Trim(),
            Baud = int.TryParse(_baud.Text, out var b) ? b : 9600,
            DataBits = _dataBits.Text, StopBits = _stopBits.Text, Handshake = _hand.Text, Dtr = _dtr.Text, Rts = _rts.Text,
            CivAddress = _civ.Text.Trim(), Ptt = _ptt.Text, PttPort = _pttPort.Text.Trim(), RigMode = _rigMode.Text,
            TxDelayMs = (int)_delay.Value, TxTailMs = (int)_tail.Value, PollMs = (int)_poll.Value,
        };
        c.Save(_s);
        _grid.EndEdit();
        Frequencies = FrequencyList.Sorted(GridEntries());
        _s.Save();
    }
}
