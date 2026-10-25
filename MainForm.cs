using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Drawing;
using System.Windows.Forms;

namespace ModbusServer;

/// <summary>Editor Modbus serveru; definice jsou zde, živá data zůstávají v RegisterBank.</summary>
public sealed class MainForm : Form
{
    // Konfigurace patří do uživatelského profilu, aby EXE šlo spustit odkudkoli.
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ModbusPlcSimulator", "config.json");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly RegisterBank _bank = new();
    private readonly ModbusTcpServer _server;
    private readonly List<Variable> _variables;
    private readonly DataGridView _grid = new();
    private readonly ListBox _log = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 100 };
    private readonly Queue<string> _pendingLogs = new();
    private int _gridDirty;
    private readonly TextBox _ip = new() { Width = 120 };
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Width = 75 };
    private readonly Button _start = new() { Text = "Spustit", AutoSize = true };
    private readonly Button _stop = new() { Text = "Zastavit", AutoSize = true, Enabled = false };
    private readonly TextBox _name = new();
    private readonly NumericUpDown _address = new() { Minimum = 0, Maximum = 65535 };
    private readonly TextBox _bit = new() { PlaceholderText = "prázdné" };
    private readonly ComboBox _type = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _length = new() { Minimum = 1, Maximum = 125, Value = 1 };
    private readonly TextBox _value = new() { Text = "0" };
    private readonly Label _status = new() { AutoSize = true, Text = "Zastaveno" };
    // Obnovení tabulky mění výběr; při něm se editor nesmí načíst znovu.
    private bool _refreshing;
    // Vadný konfigurační soubor se nesmí automaticky přepsat jen validním zbytkem.
    private bool _canAutoSave = true;
    // Pokud uživatel hodnotu neupravil, změna názvu/adresy použije aktuální data PLC.
    private bool _valueEdited;

    public MainForm()
    {
        Text = "Modbus TCP server – simulátor registrů";
        MinimumSize = new Size(850, 550);
        Size = new Size(1080, 700);
        var config = LoadConfig(out _canAutoSave);
        _variables = [];
        _server = new ModbusTcpServer(_bank);
        // Události serveru přicházejí i z vláken klientů; ovládací prvky mění jen UI vlákno.
        _server.Status += message => PostToUi(() =>
        {
            // Stav tlačítek se řídí serverem i při nečekaném ukončení přijímání spojení.
            var running = _server.Port != 0;
            _ip.Enabled = _port.Enabled = _start.Enabled = !running;
            _stop.Enabled = running;
            _status.Text = message;
            AppendLog(message);
        });
        // Síť může zapisovat rychleji než UI kreslí. Držíme jen poslední události a jednu značku změny tabulky.
        _server.Log += message =>
        {
            lock (_pendingLogs)
            {
                if (_pendingLogs.Count == 500) _pendingLogs.Dequeue();
                _pendingLogs.Enqueue(message);
            }
        };
        _bank.Changed += () => Interlocked.Exchange(ref _gridDirty, 1);
        _ip.Text = config.ListenAddress;
        _port.Value = Math.Clamp(config.Port, 1, 65535);

        BuildLayout();
        _uiTimer.Tick += (_, _) =>
        {
            FlushPendingLogs();
            if (Interlocked.Exchange(ref _gridDirty, 0) != 0) RefreshGrid();
        };
        _type.DataSource = Enum.GetValues<VariableType>();
        _type.SelectedIndexChanged += (_, _) => UpdateTypeFields();
        _value.TextChanged += (_, _) => _valueEdited = true;
        _grid.SelectionChanged += (_, _) => { if (!_refreshing) LoadSelectedVariable(); };
        _start.Click += (_, _) => StartServer();
        _stop.Click += (_, _) => StopServer();
        FormClosing += (_, _) => { _uiTimer.Stop(); _uiTimer.Dispose(); _server.Stop(); SaveConfigSilently(); };

        foreach (var variable in config.Variables ?? new List<Variable>())
        {
            try
            {
                if (variable is null) throw new ArgumentException("Záznam proměnné je prázdný.");
                VariableCodec.Validate(variable, _variables);
                _variables.Add(variable);
                _bank.SetVariable(variable);
            }
            catch (Exception ex)
            {
                _canAutoSave = false;
                UiText.Show($"Proměnnou {variable?.Name ?? "(bez názvu)"} nelze načíst: {ex.Message}", "Konfigurace",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        _bank.Restore(_variables, config.RegisterValues);
        RefreshGrid();
        Interlocked.Exchange(ref _gridDirty, 0);
        _uiTimer.Start();
    }

    private void BuildLayout()
    {
        // Tabulka a log se roztahují s oknem; ovládání a editor zůstávají kompaktní.
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 70));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var top = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        top.Controls.Add(new Label { Text = "IP:", AutoSize = true, Margin = new Padding(3, 8, 3, 0) });
        top.Controls.Add(_ip);
        top.Controls.Add(new Label { Text = "Port:", AutoSize = true, Margin = new Padding(12, 8, 3, 0) });
        top.Controls.Add(_port);
        top.Controls.Add(_start);
        top.Controls.Add(_stop);
        var save = new Button { Text = "Uložit konfiguraci", AutoSize = true };
        save.Click += (_, _) => SaveConfigWithMessage();
        top.Controls.Add(save);
        root.Controls.Add(top, 0, 0);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.RowHeadersVisible = false;
        _grid.Columns.Add("name", "Název");
        _grid.Columns.Add("address", "Adresa");
        _grid.Columns.Add("bit", "Bit");
        _grid.Columns.Add("type", "Typ");
        _grid.Columns.Add("length", "Registry");
        _grid.Columns.Add("value", "Hodnota");
        _grid.Columns.Add("raw", "Raw (hex)");
        foreach (DataGridViewColumn column in _grid.Columns)
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        root.Controls.Add(_grid, 0, 1);

        var logPanel = new GroupBox { Text = "Log serveru", Dock = DockStyle.Fill };
        logPanel.Controls.Add(_log);
        root.Controls.Add(logPanel, 0, 2);

        var editor = new TableLayoutPanel { Dock = DockStyle.Fill, Height = 105, ColumnCount = 6, RowCount = 3, Margin = new Padding(0, 10, 0, 0) };
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        foreach (var width in new[] { 18, 17, 9, 16, 10, 30 })
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
        var labels = new[] { "Název", "Adresa (0-based)", "Bit 0–15", "Typ", "Délka", "Hodnota" };
        Control[] fields = [_name, _address, _bit, _type, _length, _value];
        for (var i = 0; i < fields.Length; i++)
        {
            editor.Controls.Add(new Label { Text = labels[i], AutoSize = true }, i, 0);
            fields[i].Dock = DockStyle.Fill;
            editor.Controls.Add(fields[i], i, 1);
        }
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        var add = new Button { Text = "Přidat", AutoSize = true };
        var update = new Button { Text = "Uložit změnu", AutoSize = true };
        var delete = new Button { Text = "Smazat", AutoSize = true };
        add.Click += (_, _) => AddVariable();
        update.Click += (_, _) => UpdateVariable();
        delete.Click += (_, _) => DeleteVariable();
        buttons.Controls.AddRange([add, update, delete]);
        editor.Controls.Add(buttons, 0, 2);
        editor.SetColumnSpan(buttons, 6);
        root.Controls.Add(editor, 0, 3);

        root.Controls.Add(_status, 0, 4);
        UpdateTypeFields();
    }

    private void AppendLog(string message)
    {
        // Paměť logu je omezená i při dlouhém běhu simulátoru.
        _log.Items.Add($"{DateTime.Now:HH:mm:ss.fff}  {UiText.TranslateLog(message)}");
        if (_log.Items.Count > 500) _log.Items.RemoveAt(0);
        _log.TopIndex = _log.Items.Count - 1;
    }

    private void FlushPendingLogs()
    {
        string[] messages;
        lock (_pendingLogs)
        {
            messages = _pendingLogs.ToArray();
            _pendingLogs.Clear();
        }
        if (messages.Length == 0) return;
        _log.BeginUpdate();
        try
        {
            foreach (var message in messages) _log.Items.Add($"{DateTime.Now:HH:mm:ss.fff}  {UiText.TranslateLog(message)}");
            while (_log.Items.Count > 500) _log.Items.RemoveAt(0);
            _log.TopIndex = _log.Items.Count - 1;
        }
        finally { _log.EndUpdate(); }
    }

    private void PostToUi(Action action)
    {
        // Při zavírání okna už nemusí existovat handle pro BeginInvoke.
        try { if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(() => { if (!IsDisposed) action(); })); }
        catch (InvalidOperationException) { }
    }

    private void UpdateTypeFields()
    {
        // Bit a délka mají význam jen pro odpovídající typy proměnných.
        var type = _type.SelectedItem is VariableType selected ? selected : VariableType.Bool;
        _bit.Enabled = type == VariableType.Bool;
        if (!_bit.Enabled) _bit.Clear();
        _length.Enabled = type is VariableType.String or VariableType.RegisterAscii;
    }

    private Variable ReadEditor()
    {
        int? bit = null;
        if (!string.IsNullOrWhiteSpace(_bit.Text))
        {
            if (!int.TryParse(_bit.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                throw new ArgumentException("Bit musí být číslo 0 až 15.");
            bit = parsed;
        }
        return new Variable
        {
            Name = _name.Text.Trim(), Address = (int)_address.Value,
            Type = (VariableType)_type.SelectedItem!, Bit = bit,
            Length = (int)_length.Value, Value = _value.Text
        };
    }

    private void AddVariable()
    {
        try
        {
            var variable = ReadEditor();
            VariableCodec.Validate(variable, _variables);
            _variables.Add(variable);
            _bank.SetVariable(variable);
            RefreshGrid(_variables.Count - 1);
            LoadSelectedVariable();
            SaveConfigSilently();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void UpdateVariable()
    {
        var index = SelectedIndex();
        if (index < 0) return;
        try
        {
            var variable = ReadEditor();
            var old = _variables[index];
            ushort[]? originalWords = null;
            var preserveWords = false;
            if (!_valueEdited)
            {
                // Zápis klienta mezitím mohl změnit hodnotu od výběru řádku.
                originalWords = _bank.Read(old.Address, old.RegisterCount);
                preserveWords = old.RegisterCount == variable.RegisterCount && old.Bit is null && variable.Bit is null;
                // Změna typu se stejnou šířkou mění výklad bitů, například UInt32 0xFFFFFFFF na Int32 -1.
                variable.Value = VariableCodec.Decode(preserveWords ? variable : old, originalWords);
            }
            VariableCodec.Validate(variable, _variables.Where((_, i) => i != index));
            _bank.ClearVariable(old);
            _variables[index] = variable;
            _bank.SetVariable(variable);
            // Bez úpravy hodnoty se syrová slova nepřebalují ani při změně interpretace typu.
            if (preserveWords && originalWords is not null)
                _bank.Write(variable.Address, originalWords);
            RefreshGrid(index);
            LoadSelectedVariable();
            SaveConfigSilently();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void DeleteVariable()
    {
        var index = SelectedIndex();
        if (index < 0) return;
        _bank.ClearVariable(_variables[index]);
        _variables.RemoveAt(index);
        RefreshGrid(-1);
        _name.Clear();
        _address.Value = 0;
        _type.SelectedItem = VariableType.Bool;
        _bit.Clear();
        _length.Value = 1;
        _value.Text = "0";
        _valueEdited = false;
        SaveConfigSilently();
    }

    private int SelectedIndex() => _grid.SelectedRows.Count == 0 ? -1 : _grid.SelectedRows[0].Index;

    private void LoadSelectedVariable()
    {
        var index = SelectedIndex();
        if (index < 0 || index >= _variables.Count) return;
        var variable = _variables[index];
        _name.Text = variable.Name;
        _address.Value = variable.Address;
        _type.SelectedItem = variable.Type;
        _bit.Text = variable.Bit?.ToString(CultureInfo.InvariantCulture) ?? "";
        _length.Value = Math.Clamp(variable.Length, 1, 125);
        _value.Text = VariableCodec.Decode(variable, _bank.Read(variable.Address, variable.RegisterCount));
        _valueEdited = false;
    }

    private void RefreshGrid(int? select = null)
    {
        // Dekódujeme jeden snímek každé proměnné, aby hodnota souhlasila s Raw.
        var selected = select ?? SelectedIndex();
        _refreshing = true;
        try
        {
            _grid.Rows.Clear();
            foreach (var variable in _variables)
            {
                var words = _bank.Read(variable.Address, variable.RegisterCount);
                var value = VariableCodec.Decode(variable, words);
                _grid.Rows.Add(variable.Name, variable.Address, variable.Bit?.ToString() ?? "",
                    variable.Type, variable.RegisterCount, value,
                    string.Join(" ", words.Select(w => w.ToString("X4", CultureInfo.InvariantCulture))));
            }
            _grid.ClearSelection();
            if (selected >= 0 && selected < _grid.Rows.Count) _grid.Rows[selected].Selected = true;
        }
        finally { _refreshing = false; }
    }

    private void StartServer()
    {
        try
        {
            if (!IPAddress.TryParse(_ip.Text.Trim(), out var address))
                throw new ArgumentException("Zadejte platnou IPv4 nebo IPv6 adresu.");
            _server.Start(address, (int)_port.Value);
            _ip.Enabled = _port.Enabled = _start.Enabled = false;
            _stop.Enabled = true;
            SaveConfigSilently();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void StopServer()
    {
        _server.Stop();
        _ip.Enabled = _port.Enabled = _start.Enabled = true;
        _stop.Enabled = false;
    }

    private static SimulatorConfig LoadConfig(out bool canAutoSave)
    {
        try
        {
            canAutoSave = true;
            return File.Exists(ConfigPath)
                ? JsonSerializer.Deserialize<SimulatorConfig>(File.ReadAllText(ConfigPath), JsonOptions) ?? new SimulatorConfig()
                : new SimulatorConfig();
        }
        catch (Exception ex)
        {
            canAutoSave = false;
            UiText.Show($"Konfiguraci nelze načíst: {ex.Message}", "Konfigurace", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return new SimulatorConfig();
        }
    }

    private void SaveConfig()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        // Jediný snímek zachová i syrová slova, která nelze beze ztráty převést na text.
        var registers = _bank.Capture(_variables);
        foreach (var variable in _variables)
            variable.Value = VariableCodec.Decode(variable,
                Enumerable.Range(variable.Address, variable.RegisterCount).Select(address => registers[address]).ToArray());
        var config = new SimulatorConfig
        {
            ListenAddress = _ip.Text.Trim(), Port = (int)_port.Value,
            Variables = _variables, RegisterValues = registers
        };
        var tempPath = ConfigPath + ".tmp";
        // Přesun v jedné složce zabrání částečně zapsanému JSON při pádu procesu.
        File.WriteAllText(tempPath, JsonSerializer.Serialize(config, JsonOptions));
        File.Move(tempPath, ConfigPath, true);
    }

    private void SaveConfigSilently()
    {
        if (!_canAutoSave) { AppendLog("Konfiguraci je nutné opravit a uložit ručně."); return; }
        try { SaveConfig(); }
        catch (Exception ex) { AppendLog($"Konfiguraci nelze uložit: {ex.Message}"); }
    }

    private void SaveConfigWithMessage()
    {
        try { SaveConfig(); _canAutoSave = true; AppendLog($"Uloženo: {ConfigPath}"); }
        catch (Exception ex) { ShowError(ex); }
    }

    private static void ShowError(Exception ex) => UiText.Show(ex.Message, "Modbus server", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
