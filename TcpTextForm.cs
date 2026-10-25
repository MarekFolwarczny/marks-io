using System.Drawing;
using System.Net;
using System.Windows.Forms;

namespace ModbusServer;

/// <summary>Společné okno pro odesílání, příjem a logování textového TCP proudu.</summary>
public sealed class TcpTextForm : Form
{
    private readonly bool _serverMode;
    private readonly TcpTextTransport _transport = new();
    private readonly CancellationTokenSource _closing = new();
    private readonly TextBox _host = new() { Width = 150 };
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Value = 5000, Width = 80 };
    private readonly Button _start = new() { AutoSize = true };
    private readonly Button _stop = new() { Text = "Zastavit", AutoSize = true };
    private readonly TextBox _outgoing = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = "STATUS 1\r\n" };
    private readonly TextBox _incoming = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly Button _send = new() { Text = "Odeslat text", AutoSize = true };
    private readonly ListBox _log = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Label _status = new() { Text = "Zastaveno", AutoSize = true };
    private bool _busy;

    public TcpTextForm(bool serverMode)
    {
        _serverMode = serverMode;
        Text = serverMode ? "TCP/IP textový server" : "TCP/IP textový klient";
        MinimumSize = new Size(700, 550);
        Size = new Size(900, 700);
        _host.Text = "127.0.0.1";
        _start.Text = serverMode ? "Spustit" : "Připojit";
        BuildLayout();

        _transport.Status += message => PostToUi(() => { _status.Text = message; AppendLog(message); SetButtons(); });
        _transport.Log += message => PostToUi(() => { AppendLog(message); SetButtons(); });
        _transport.Received += (_, message) => PostToUi(() =>
        {
            _incoming.AppendText(message);
            if (_incoming.TextLength > 20000) _incoming.Text = _incoming.Text[^20000..];
        });
        _start.Click += async (_, _) => await RunOperation(StartAsync);
        _stop.Click += (_, _) => { _transport.Stop(); SetButtons(); };
        _send.Click += async (_, _) => await RunOperation(token => _transport.SendAsync(_outgoing.Text, token));
        FormClosing += (_, _) => { _closing.Cancel(); _transport.Dispose(); };
        SetButtons();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 24));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var endpoint = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        endpoint.Controls.Add(new Label { Text = _serverMode ? "Naslouchat na IP:" : "Host/IP:", AutoSize = true, Margin = new Padding(3, 8, 3, 0) });
        endpoint.Controls.Add(_host);
        endpoint.Controls.Add(new Label { Text = "Port:", AutoSize = true, Margin = new Padding(12, 8, 3, 0) });
        endpoint.Controls.Add(_port);
        endpoint.Controls.Add(_start);
        endpoint.Controls.Add(_stop);
        root.Controls.Add(endpoint, 0, 0);

        var sendPanel = new GroupBox { Text = "Odeslat UTF-8 text", Dock = DockStyle.Fill };
        var sendLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6), RowCount = 3, ColumnCount = 1 };
        sendLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sendLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sendLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sendLayout.Controls.Add(new Label
        {
            Text = "Příklady zpráv: STATUS 1 + nový řádek    |    <STATUS>1</STATUS>",
            AutoSize = true, MaximumSize = new Size(640, 0)
        }, 0, 0);
        sendLayout.Controls.Add(_outgoing, 0, 1);
        sendLayout.Controls.Add(_send, 0, 2);
        sendPanel.Controls.Add(sendLayout);
        root.Controls.Add(sendPanel, 0, 1);

        var receivePanel = new GroupBox { Text = "Přijatý text", Dock = DockStyle.Fill };
        receivePanel.Controls.Add(_incoming);
        root.Controls.Add(receivePanel, 0, 2);

        var logPanel = new GroupBox { Text = "Log komunikace", Dock = DockStyle.Fill };
        logPanel.Controls.Add(_log);
        root.Controls.Add(logPanel, 0, 3);
        root.Controls.Add(_status, 0, 4);
    }

    private async Task StartAsync(CancellationToken token)
    {
        if (_serverMode)
        {
            if (!IPAddress.TryParse(_host.Text.Trim(), out var address))
                throw new ArgumentException("Zadejte platnou IPv4 nebo IPv6 adresu.");
            _transport.StartServer(address, (int)_port.Value);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(_host.Text)) throw new ArgumentException("Zadejte host nebo IP adresu.");
            await _transport.ConnectAsync(_host.Text.Trim(), (int)_port.Value, token);
        }
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
        var running = _transport.IsRunning;
        _host.Enabled = _port.Enabled = _start.Enabled = !_busy && !running;
        _stop.Enabled = !_busy && running;
        _send.Enabled = !_busy && running && _transport.ClientCount > 0;
    }

    private void PostToUi(Action action)
    {
        try { if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(() => { if (!IsDisposed) action(); })); }
        catch (InvalidOperationException) { }
    }

    private void AppendLog(string message)
    {
        if (IsDisposed) return;
        _log.Items.Add($"{DateTime.Now:HH:mm:ss.fff}  {UiText.TranslateLog(message)}");
        if (_log.Items.Count > 500) _log.Items.RemoveAt(0);
        _log.TopIndex = _log.Items.Count - 1;
    }
}
