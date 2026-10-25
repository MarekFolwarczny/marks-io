using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ModbusServer;

/// <summary>Ruční čtení a zápis jedné typované hodnoty na vzdáleném Modbus serveru.</summary>
public sealed class ModbusClientForm : Form
{
    private readonly ModbusTcpClient _client = new();
    private readonly CancellationTokenSource _closing = new();
    private readonly TextBox _host = new() { Text = "127.0.0.1", Width = 140 };
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Value = 1502, Width = 75 };
    private readonly NumericUpDown _unit = new() { Minimum = 0, Maximum = 255, Value = 1, Width = 55 };
    private readonly Button _connect = new() { Text = "Připojit", AutoSize = true };
    private readonly Button _disconnect = new() { Text = "Odpojit", AutoSize = true };
    private readonly NumericUpDown _address = new() { Minimum = 0, Maximum = 65535 };
    private readonly ComboBox _type = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _bit = new() { PlaceholderText = "prázdné" };
    private readonly NumericUpDown _length = new() { Minimum = 1, Maximum = 125, Value = 1 };
    private readonly TextBox _value = new() { Text = "0" };
    private readonly TextBox _raw = new() { ReadOnly = true };
    private readonly Button _read = new() { Text = "Vyčíst", AutoSize = true };
    private readonly Button _write = new() { Text = "Zapsat", AutoSize = true };
    private readonly ListBox _log = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Label _status = new() { Text = "Odpojeno", AutoSize = true };
    private readonly System.Windows.Forms.Timer _connectionCheck = new() { Interval = 1000 };
    private bool _busy;

    public ModbusClientForm()
    {
        Text = "Modbus TCP klient";
        MinimumSize = new Size(800, 500);
        Size = new Size(950, 600);
        BuildLayout();
        _type.DataSource = Enum.GetValues<VariableType>();
        _type.SelectedIndexChanged += (_, _) => UpdateTypeFields();
        _client.Status += message => { _status.Text = message; AppendLog(message); SetButtons(); };
        _connect.Click += async (_, _) => await RunOperation(async token =>
            await _client.ConnectAsync(_host.Text.Trim(), (int)_port.Value, token));
        _disconnect.Click += (_, _) => { _client.Disconnect(); SetButtons(); };
        _read.Click += async (_, _) => await RunOperation(ReadAsync);
        _write.Click += async (_, _) => await RunOperation(WriteAsync);
        _connectionCheck.Tick += (_, _) =>
        {
            if (_busy || _client.IsConnected) return;
            _client.Disconnect();
            SetButtons();
        };
        _connectionCheck.Start();
        FormClosing += (_, _) => { _connectionCheck.Stop(); _closing.Cancel(); _client.Dispose(); };
        UpdateTypeFields();
        SetButtons();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var endpoint = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        endpoint.Controls.Add(new Label { Text = "Host/IP:", AutoSize = true, Margin = new Padding(3, 8, 3, 0) });
        endpoint.Controls.Add(_host);
        endpoint.Controls.Add(new Label { Text = "Port:", AutoSize = true, Margin = new Padding(12, 8, 3, 0) });
        endpoint.Controls.Add(_port);
        endpoint.Controls.Add(new Label { Text = "Unit ID:", AutoSize = true, Margin = new Padding(12, 8, 3, 0) });
        endpoint.Controls.Add(_unit);
        endpoint.Controls.Add(_connect);
        endpoint.Controls.Add(_disconnect);
        root.Controls.Add(endpoint, 0, 0);

        var editor = new TableLayoutPanel { Dock = DockStyle.Fill, Height = 145, ColumnCount = 5, RowCount = 4, Margin = new Padding(0, 14, 0, 10) };
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        foreach (var width in new[] { 16, 22, 13, 13, 36 })
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
        var labels = new[] { "Adresa (0-based)", "Typ", "Bit 0–15", "Délka", "Hodnota" };
        Control[] fields = [_address, _type, _bit, _length, _value];
        for (var i = 0; i < fields.Length; i++)
        {
            editor.Controls.Add(new Label { Text = labels[i], AutoSize = true }, i, 0);
            fields[i].Dock = DockStyle.Fill;
            editor.Controls.Add(fields[i], i, 1);
        }
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        actions.Controls.AddRange([_read, _write]);
        editor.Controls.Add(actions, 0, 2);
        editor.SetColumnSpan(actions, 5);
        editor.Controls.Add(new Label { Text = "Registry (hex):", AutoSize = true }, 0, 3);
        editor.Controls.Add(_raw, 1, 3);
        editor.SetColumnSpan(_raw, 4);
        _raw.Dock = DockStyle.Fill;
        root.Controls.Add(editor, 0, 1);

        var logPanel = new GroupBox { Text = "Log komunikace", Dock = DockStyle.Fill };
        logPanel.Controls.Add(_log);
        root.Controls.Add(logPanel, 0, 2);
        root.Controls.Add(_status, 0, 3);
    }

    private void UpdateTypeFields()
    {
        var type = _type.SelectedItem is VariableType selected ? selected : VariableType.Bool;
        _bit.Enabled = type == VariableType.Bool;
        if (!_bit.Enabled) _bit.Clear();
        _length.Enabled = type is VariableType.String or VariableType.RegisterAscii;
    }

    private Variable ReadEditor(bool forWrite)
    {
        int? bit = null;
        if (!string.IsNullOrWhiteSpace(_bit.Text))
        {
            if (!int.TryParse(_bit.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                throw new ArgumentException("Bit musí být číslo 0 až 15.");
            bit = parsed;
        }
        var variable = new Variable
        {
            Name = "Hodnota", Address = (int)_address.Value, Type = (VariableType)_type.SelectedItem!,
            Bit = bit, Length = (int)_length.Value, Value = forWrite ? _value.Text : "0"
        };
        VariableCodec.Validate(variable, []);
        return variable;
    }

    private async Task ReadAsync(CancellationToken token)
    {
        var variable = ReadEditor(forWrite: false);
        var words = await _client.ReadHoldingRegistersAsync((byte)_unit.Value, variable.Address, variable.RegisterCount, token);
        _raw.Text = FormatWords(words);
        _value.Text = VariableCodec.Decode(variable, words);
        AppendLog($"FC03 čtení HR {variable.Address}–{variable.Address + words.Length - 1}: {_raw.Text} → {_value.Text}");
    }

    private async Task WriteAsync(CancellationToken token)
    {
        var variable = ReadEditor(forWrite: true);
        var words = VariableCodec.Encode(variable);
        if (variable.Bit is int bit)
        {
            // FC22 mění bit přímo na serveru bez závodu mezi čtením a zápisem.
            await _client.MaskWriteRegisterAsync((byte)_unit.Value, variable.Address, bit, words[0] != 0, token);
            words = await _client.ReadHoldingRegistersAsync((byte)_unit.Value, variable.Address, 1, token);
        }
        else
        {
            await _client.WriteHoldingRegistersAsync((byte)_unit.Value, variable.Address, words, token);
        }
        _raw.Text = FormatWords(words);
        AppendLog($"Zápis HR {variable.Address}–{variable.Address + words.Length - 1}: {_raw.Text}");
    }

    private async Task RunOperation(Func<CancellationToken, Task> action)
    {
        if (_busy) return;
        _busy = true;
        SetButtons();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try { await action(timeout.Token); }
        catch (OperationCanceledException) when (!_closing.IsCancellationRequested) { AppendLog("Chyba: vypršel časový limit komunikace."); }
        catch (Exception ex) { AppendLog($"Chyba: {ex.Message}"); }
        finally
        {
            _busy = false;
            if (!IsDisposed) SetButtons();
        }
    }

    private void SetButtons()
    {
        var connected = _client.IsConnected;
        _host.Enabled = _port.Enabled = _unit.Enabled = _connect.Enabled = !_busy && !connected;
        _disconnect.Enabled = !_busy && connected;
        _read.Enabled = _write.Enabled = !_busy && connected;
    }

    private void AppendLog(string message)
    {
        if (IsDisposed) return;
        _log.Items.Add($"{DateTime.Now:HH:mm:ss.fff}  {UiText.TranslateLog(message)}");
        if (_log.Items.Count > 500) _log.Items.RemoveAt(0);
        _log.TopIndex = _log.Items.Count - 1;
    }

    private static string FormatWords(ushort[] words) =>
        string.Join(" ", words.Select(word => word.ToString("X4", CultureInfo.InvariantCulture)));
}
