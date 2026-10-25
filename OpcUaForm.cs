using System.Drawing;
using System.Windows.Forms;

namespace ModbusServer;

/// <summary>Společné ruční ovládání OPC UA serveru a klienta s logem a živým přehledem uzlů.</summary>
public sealed class OpcUaForm : Form
{
    private readonly bool _serverMode;
    private readonly TextBox _endpoint = new() { Width = 360, Text = "opc.tcp://127.0.0.1:4840" };
    private readonly TextBox _profile = new() { Width = 360 };
    private readonly TextBox _applicationUri = new() { Width = 310 };
    private readonly TextBox _namespaceUri = new() { Width = 310, Text = "urn:example:line1" };
    private readonly TextBox _name = new() { Width = 110, Text = "Trigger" };
    private readonly TextBox _identifier = new() { Width = 150, Text = "s=Trigger" };
    private readonly ComboBox _type = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 95 };
    private readonly TextBox _value = new() { Width = 170, Text = "false" };
    private readonly ListBox _items = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly ListBox _log = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Label _status = new() { Text = "Zastaveno", AutoSize = true };
    private readonly Button _start = new() { Text = "Spustit", AutoSize = true };
    private readonly Button _stop = new() { Text = "Zastavit", AutoSize = true };
    private readonly Button _read = new() { Text = "Vyčíst", AutoSize = true };
    private readonly Button _write = new() { Text = "Zapsat", AutoSize = true };
    private readonly Button _add = new() { Text = "Přidat uzel", AutoSize = true };
    private readonly Button _remove = new() { Text = "Odebrat", AutoSize = true };
    private readonly List<OpcUaItem> _definitions = new();
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 500 };
    private OpcUaServer? _server;
    private OpcUaClient? _client;
    private bool _busy;

    public OpcUaForm(bool serverMode)
    {
        _serverMode = serverMode;
        Text = serverMode ? "OPC UA server" : "OPC UA klient";
        MinimumSize = new Size(760, 590);
        Size = new Size(1000, 740);
        _applicationUri.Text = serverMode ? "urn:example:marks-io:server" : "urn:example:marks-io:client";
        _type.Items.AddRange(Enum.GetNames<OpcUaValueType>());
        _type.SelectedIndex = 0;
        _start.Text = serverMode ? "Spustit" : "Připojit";
        BuildLayout();
        _start.Click += async (_, _) => await RunAsync(StartAsync);
        _stop.Click += (_, _) => Stop();
        _add.Click += (_, _) => AddItem();
        _remove.Click += (_, _) => RemoveItem();
        _read.Click += async (_, _) => await RunAsync(ReadAsync);
        _write.Click += async (_, _) => await RunAsync(WriteAsync);
        _items.SelectedIndexChanged += (_, _) => SelectItem();
        _refresh.Tick += (_, _) => RefreshItems();
        _refresh.Start();
        FormClosing += (_, _) => { _refresh.Stop(); Stop(); _refresh.Dispose(); };
        SetButtons();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 6 };
        for (var i = 0; i < 3; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, _serverMode ? 35 : 0));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, _serverMode ? 65 : 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var endpoint = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        AddField(endpoint, "Endpoint:", _endpoint);
        AddField(endpoint, "Profil certifikátů:", _profile);
        var browse = new Button { Text = "…", AutoSize = true };
        browse.Click += (_, _) => { using var dialog = new FolderBrowserDialog(); if (dialog.ShowDialog(this) == DialogResult.OK) _profile.Text = dialog.SelectedPath; };
        endpoint.Controls.Add(browse);
        root.Controls.Add(endpoint, 0, 0);
        var identity = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        AddField(identity, "Application URI:", _applicationUri);
        AddField(identity, "Namespace URI:", _namespaceUri);
        identity.Controls.Add(_start);
        identity.Controls.Add(_stop);
        root.Controls.Add(identity, 0, 1);
        var values = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(0, 12, 0, 12) };
        if (_serverMode) AddField(values, "Název:", _name);
        AddField(values, "NodeId:", _identifier);
        AddField(values, "Typ:", _type);
        AddField(values, "Hodnota:", _value);
        if (_serverMode) { values.Controls.Add(_add); values.Controls.Add(_remove); }
        values.Controls.Add(_read);
        values.Controls.Add(_write);
        root.Controls.Add(values, 0, 2);
        if (_serverMode)
        {
            var group = new GroupBox { Text = "Uzly serveru (změna seznamu vyžaduje restart serveru)", Dock = DockStyle.Fill };
            group.Controls.Add(_items);
            root.Controls.Add(group, 0, 3);
        }
        var logGroup = new GroupBox { Text = "Log", Dock = DockStyle.Fill };
        logGroup.Controls.Add(_log);
        root.Controls.Add(logGroup, 0, 4);
        root.Controls.Add(_status, 0, 5);
        Controls.Add(root);
    }

    private static void AddField(FlowLayoutPanel panel, string label, Control control)
    {
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(8, 8, 3, 0) });
        control.Margin = new Padding(0, 3, 8, 3);
        panel.Controls.Add(control);
    }

    private void AddItem()
    {
        try
        {
            var name = _name.Text.Trim();
            if (name.Length == 0) throw new ArgumentException("Zadejte název uzlu.");
            var id = _identifier.Text.Trim();
            OpcUaValue.Node(id, 2);
            if (_definitions.Any(x => x.Identifier == id || x.Name == name))
                throw new ArgumentException("Název a NodeId musí být jedinečné.");
            var type = (OpcUaValueType)_type.SelectedIndex;
            var item = new OpcUaItem(name, id, type, OpcUaValue.Parse(type, _value.Text));
            _definitions.Add(item);
            _items.Items.Add($"{item.Name}   {item.Identifier}   {item.Type}   {item.Value}");
            _items.SelectedIndex = _items.Items.Count - 1;
            Log($"Přidán uzel {id} ({type}).");
        }
        catch (Exception ex) { Log($"Chyba: {ex.Message}"); }
        SetButtons();
    }

    private void RemoveItem()
    {
        var index = _items.SelectedIndex;
        if (index < 0 || _server is not null) return;
        _definitions.RemoveAt(index);
        _items.Items.RemoveAt(index);
        SetButtons();
    }

    private void SelectItem()
    {
        if (_items.SelectedIndex < 0) return;
        var item = _definitions[_items.SelectedIndex];
        _name.Text = item.Name;
        _identifier.Text = item.Identifier;
        _type.SelectedItem = item.Type.ToString();
        _value.Text = OpcUaValue.Format(_server?.Items?.ReadLocal(item.Identifier) ?? item.Value);
    }

    // Přepisujeme pouze přehled; editor by při psaní uživatele neměl dostat cizí hodnotu.
    private void RefreshItems()
    {
        if (_server?.Items is null) return;
        for (var i = 0; i < _definitions.Count; i++)
        {
            var item = _definitions[i];
            var value = _server.Items.ReadLocal(item.Identifier);
            var label = $"{item.Name}   {item.Identifier}   {item.Type}   {value}";
            if (!Equals(_items.Items[i], label)) _items.Items[i] = label;
        }
    }

    private async Task StartAsync()
    {
        var uri = new Uri(_endpoint.Text.Trim());
        if (uri.Scheme != "opc.tcp" || uri.Port is < 1 or > 65535)
            throw new ArgumentException("Endpoint musí mít tvar opc.tcp://host:port[/cesta].");
        var profile = _profile.Text.Trim();
        var applicationUri = _applicationUri.Text.Trim();
        var namespaceUri = _namespaceUri.Text.Trim();
        if (!Uri.TryCreate(namespaceUri, UriKind.Absolute, out _) || namespaceUri.Length == 0)
            throw new ArgumentException("Zadejte platný namespace URI.");
        if (_serverMode)
        {
            if (_definitions.Count == 0) throw new ArgumentException("Přidejte alespoň jeden uzel.");
            var (config, _) = await OpcUaSecurity.LoadAsync(profile, applicationUri, true, uri.ToString());
            var writer = OpcUaSecurity.PinnedCertificate(profile, "writer.der");
            _server = new OpcUaServer(namespaceUri, _definitions.ToArray(), writer.RawData, Log);
            try { await _server.StartAsync(config, CancellationToken.None); }
            catch { _server.Dispose(); _server = null; throw; }
            _status.Text = $"Server běží: {uri}";
        }
        else
        {
            _client = new OpcUaClient();
            try { await _client.ConnectAsync(uri.ToString(), profile, applicationUri); }
            catch { _client.Dispose(); _client = null; throw; }
            _status.Text = $"Připojeno: {uri}";
        }
        Log(_status.Text);
    }

    private void Stop()
    {
        if (_server is not null) Task.Run(async () => await _server.StopAsync(CancellationToken.None)).GetAwaiter().GetResult();
        _server?.Dispose();
        _server = null;
        _client?.Dispose();
        _client = null;
        _status.Text = "Zastaveno";
        Log("Spojení ukončeno.");
        SetButtons();
    }

    private async Task ReadAsync()
    {
        var identifier = _identifier.Text.Trim();
        var value = _serverMode
            ? _server!.Items!.ReadLocal(identifier)
            : await _client!.ReadAsync(_namespaceUri.Text.Trim(), identifier);
        _value.Text = OpcUaValue.Format(value);
        Log($"Čtení {identifier}: {_value.Text} ({value.GetType().Name})");
    }

    private async Task WriteAsync()
    {
        var identifier = _identifier.Text.Trim();
        var value = OpcUaValue.Parse((OpcUaValueType)_type.SelectedIndex, _value.Text);
        if (_serverMode) _server!.Items!.WriteLocal(identifier, value);
        else await _client!.WriteAsync(_namespaceUri.Text.Trim(), identifier, value);
        Log($"Zápis {identifier}: {value}");
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        SetButtons();
        try { await action(); }
        catch (Exception ex) { Log($"Chyba: {ex.Message}"); }
        finally { _busy = false; if (!IsDisposed) SetButtons(); }
    }

    private void SetButtons()
    {
        if (IsDisposed) return;
        var active = _server is not null || _client?.Connected == true;
        _start.Enabled = !_busy && !active;
        _stop.Enabled = !_busy && active;
        _add.Enabled = _remove.Enabled = _serverMode && !_busy && !active;
        _read.Enabled = _write.Enabled = !_busy && active;
        _endpoint.Enabled = _profile.Enabled = _applicationUri.Enabled = !_busy && !active;
        _namespaceUri.Enabled = !_busy && !active;
    }

    private void Log(string message)
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(() => Log(message)); }
            catch (InvalidOperationException) { } // Okno může během ukončování spojení zaniknout.
            return;
        }
        _log.Items.Add($"{DateTime.Now:HH:mm:ss.fff} {UiText.TranslateLog(message)}");
        if (_log.Items.Count > 500) _log.Items.RemoveAt(0);
        _log.TopIndex = _log.Items.Count - 1;
    }
}
