using System.Drawing;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace ModbusServer;

/// <summary>Ovládá jednu Snap7 roli a u serveru zobrazuje živý obsah DB.</summary>
public sealed class Snap7Form : Form
{
    private readonly bool _serverMode;
    private readonly Snap7Transport _transport = new();
    private readonly TextBox _host = new() { Width = 140 };
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Value = 102, Width = 75 };
    private readonly NumericUpDown _rack = new() { Minimum = 0, Maximum = 7, Width = 55 };
    private readonly NumericUpDown _slot = new() { Minimum = 0, Maximum = 31, Value = 1, Width = 55 };
    private readonly NumericUpDown _db = new() { Minimum = 1, Maximum = 65535, Value = 1, Width = 75 };
    private readonly NumericUpDown _size = new() { Minimum = 4, Maximum = 65536, Value = 256, Width = 75 };
    private readonly TextBox _address = new() { Text = "10.2", Width = 145 };
    private readonly ComboBox _type = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 115 };
    private readonly TextBox _value = new() { Text = "0", Width = 140 };
    private readonly Button _start = new() { AutoSize = true };
    private readonly Button _stop = new() { Text = "Zastavit", AutoSize = true };
    private readonly Button _read = new() { Text = "Vyčíst", AutoSize = true };
    private readonly Button _write = new() { Text = "Zapsat", AutoSize = true };
    private readonly GroupBox _dbGroup = new() { Text = "Obsah DB", Dock = DockStyle.Fill };
    private readonly NumericUpDown _viewOffset = new() { Minimum = 0, Maximum = 65535, Increment = 256, Width = 85 };
    private readonly TextBox _dbView = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = false,
        ScrollBars = ScrollBars.Both, Font = new Font(FontFamily.GenericMonospace, 9),
        Text = "Spusťte server pro zobrazení DB."
    };
    private readonly ListBox _log = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Label _status = new() { Text = "Zastaveno", AutoSize = true };
    private readonly System.Windows.Forms.Timer _events = new() { Interval = 150 };
    private bool _busy;
    private bool _checkingConnection;

    public Snap7Form(bool serverMode)
    {
        _serverMode = serverMode;
        if (!serverMode) _events.Interval = 2000;
        Text = serverMode ? "Snap7 server" : "Snap7 klient";
        MinimumSize = serverMode ? new Size(730, 520) : new Size(660, 420);
        Size = serverMode ? new Size(900, 700) : new Size(880, 570);
        _host.Text = "127.0.0.1";
        _start.Text = serverMode ? "Spustit" : "Připojit";
        _type.Items.AddRange(Enum.GetNames<Snap7ValueType>());
        _type.SelectedIndex = 0;
        BuildLayout();
        _start.Click += async (_, _) => await RunAsync(() =>
        {
            var host = _host.Text.Trim();
            if (!IPAddress.TryParse(host, out _)) throw new ArgumentException("Zadejte platnou IP adresu.");
            var port = (int)_port.Value;
            var db = (int)_db.Value;
            var size = (int)_size.Value;
            var rack = (int)_rack.Value;
            var slot = (int)_slot.Value;
            return () =>
            {
                if (_serverMode) _transport.StartServer(host, port, db, size);
                else _transport.Connect(host, port, rack, slot);
            };
        }, () =>
        {
            _status.Text = _serverMode ? $"Server běží: {_host.Text}:{_port.Value}, DB{_db.Value}" : $"Připojeno: {_host.Text}:{_port.Value}";
            Log(_status.Text);
            if (_serverMode)
            {
                _dbGroup.Text = $"Obsah DB{_db.Value} ({_size.Value} bajtů)";
                _viewOffset.Maximum = (int)_size.Value - 1;
                _viewOffset.Value = 0;
                RefreshDbView();
            }
            _events.Start();
        });
        _stop.Click += (_, _) =>
        {
            _events.Stop();
            _transport.Stop();
            _status.Text = "Zastaveno";
            if (_serverMode) _dbView.Text = "Server je zastaven.";
            Log("Spojení ukončeno.");
            SetButtons();
        };
        _read.Click += async (_, _) =>
        {
            string? value = null;
            Snap7Address address = default;
            await RunAsync(() =>
            {
                var type = CurrentType;
                address = CurrentAddress(type);
                return () => value = Snap7Codec.Decode(_transport.Read(address, type), type, address.Bit);
            }, () => { _value.Text = value; Log($"Čtení DB{address.Db}, byte {address.Byte}: {value}"); });
        };
        _write.Click += async (_, _) => await RunAsync(() =>
        {
            var type = CurrentType;
            var address = CurrentAddress(type);
            var value = _value.Text;
            var data = Snap7Codec.Encode(value, type);
            return () => _transport.Write(address, type, data);
        }, () => { Log("Hodnota zapsána."); if (_serverMode) RefreshDbView(); });
        _viewOffset.ValueChanged += (_, _) => { if (_serverMode && _transport.IsServer) RefreshDbView(); };
        _events.Tick += async (_, _) =>
        {
            if (_serverMode)
            {
                try
                {
                    foreach (var message in _transport.PickEvents()) Log(message);
                    RefreshDbView();
                }
                catch (Exception ex) { _events.Stop(); Log($"Chyba logu: {ex.Message}"); }
            }
            else if (!_busy && !_checkingConnection)
            {
                _checkingConnection = true;
                SetButtons();
                try
                {
                    var connected = await Task.Run(_transport.CheckConnection);
                    if (!IsDisposed && _events.Enabled && !connected)
                    {
                        _events.Stop();
                        _status.Text = "Odpojeno";
                        Log("Spojení se Snap7 serverem bylo ukončeno.");
                    }
                }
                catch (Exception ex) { if (!IsDisposed) Log($"Chyba kontroly spojení: {ex.Message}"); }
                finally { _checkingConnection = false; if (!IsDisposed) SetButtons(); }
            }
        };
        FormClosing += (_, _) => { _events.Stop(); _transport.Dispose(); _events.Dispose(); };
        SetButtons();
    }

    private Snap7ValueType CurrentType => (Snap7ValueType)_type.SelectedIndex;
    private Snap7Address CurrentAddress(Snap7ValueType type) => Snap7Codec.Parse(_address.Text, (int)_db.Value, type);

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = _serverMode ? 5 : 4 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, _serverMode ? 55 : 100));
        if (_serverMode) root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var endpoint = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        AddField(endpoint, _serverMode ? "Naslouchat na IP:" : "IP PLC:", _host);
        AddField(endpoint, "Port:", _port);
        if (_serverMode) AddField(endpoint, "Velikost DB (byte):", _size);
        else { AddField(endpoint, "Rack:", _rack); AddField(endpoint, "Slot:", _slot); }
        endpoint.Controls.Add(_start);
        endpoint.Controls.Add(_stop);
        root.Controls.Add(endpoint, 0, 0);

        var data = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(0, 14, 0, 14) };
        AddField(data, "Výchozí DB:", _db);
        AddField(data, "Adresa:", _address);
        AddField(data, "Typ:", _type);
        AddField(data, "Hodnota:", _value);
        data.Controls.Add(_read);
        data.Controls.Add(_write);
        root.Controls.Add(data, 0, 1);

        var logRow = 2;
        if (_serverMode)
        {
            var viewLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6), RowCount = 2, ColumnCount = 1 };
            viewLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            viewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var viewHeader = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
            AddField(viewHeader, "Od bajtu:", _viewOffset);
            viewHeader.Controls.Add(new Label { Text = "256 bajtů na stránku · hex | ASCII", AutoSize = true, Margin = new Padding(8, 8, 0, 0) });
            viewLayout.Controls.Add(viewHeader, 0, 0);
            viewLayout.Controls.Add(_dbView, 0, 1);
            _dbGroup.Controls.Add(viewLayout);
            root.Controls.Add(_dbGroup, 0, logRow++);
        }

        var group = new GroupBox { Text = "Log komunikace", Dock = DockStyle.Fill };
        group.Controls.Add(_log);
        root.Controls.Add(group, 0, logRow);
        root.Controls.Add(_status, 0, logRow + 1);
        Controls.Add(root);
    }

    private void RefreshDbView()
    {
        if (!_transport.IsServer) return;
        var offset = (int)_viewOffset.Value;
        var bytes = _transport.ReadServerBytes(offset, Math.Min(256, (int)_size.Value - offset));
        var text = FormatDbBytes(bytes, offset);
        if (_dbView.Text != text) _dbView.Text = text;
    }

    private static string FormatDbBytes(byte[] bytes, int offset)
    {
        var result = new StringBuilder();
        for (var i = 0; i < bytes.Length; i += 16)
        {
            var count = Math.Min(16, bytes.Length - i);
            result.Append((offset + i).ToString("X4")).Append("  ");
            for (var j = 0; j < 16; j++) result.Append(j < count ? bytes[i + j].ToString("X2") + " " : "   ");
            result.Append(" |");
            for (var j = 0; j < count; j++)
                result.Append(bytes[i + j] is >= 32 and <= 126 ? (char)bytes[i + j] : '.');
            result.AppendLine("|");
        }
        return result.ToString();
    }

    private static void AddField(FlowLayoutPanel panel, string name, Control control)
    {
        panel.Controls.Add(new Label { Text = name, AutoSize = true, Margin = new Padding(8, 8, 3, 0) });
        control.Margin = new Padding(0, 3, 8, 3);
        panel.Controls.Add(control);
    }

    private async Task RunAsync(Func<Action> prepare, Action onSuccess)
    {
        if (_busy) return;
        _busy = true;
        SetButtons();
        try { var operation = prepare(); await Task.Run(operation); if (!IsDisposed) onSuccess(); }
        catch (Exception ex) { Log($"Chyba: {ex.Message}"); }
        finally { _busy = false; if (!IsDisposed) SetButtons(); }
    }

    private void SetButtons()
    {
        if (IsDisposed) return;
        var running = _transport.IsRunning;
        var ready = !_busy && !_checkingConnection;
        _start.Enabled = ready && !running;
        _stop.Enabled = ready && running;
        _read.Enabled = ready && running;
        _write.Enabled = ready && running;
        _host.Enabled = _port.Enabled = _rack.Enabled = _slot.Enabled = _size.Enabled = ready && !running;
        _db.Enabled = ready && (!running || !_serverMode);
        _viewOffset.Enabled = _serverMode && ready && running;
    }

    private void Log(string message)
    {
        if (IsDisposed) return;
        _log.Items.Add($"{DateTime.Now:HH:mm:ss.fff}  {UiText.TranslateLog(message)}");
        if (_log.Items.Count > 500) _log.Items.RemoveAt(0);
        _log.TopIndex = _log.Items.Count - 1;
    }
}
