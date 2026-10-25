using System.Drawing;
using System.Windows.Forms;

namespace ModbusServer;

public enum CommunicationMode
{
    ModbusServer, ModbusClient, TcpServer, TcpClient,
    Snap7Server, Snap7Client, OpcUaServer, OpcUaClient
}

/// <summary>Jednorázová volba role; každý běh aplikace vytváří právě jedno komunikační okno.</summary>
public sealed class ModeSelectionForm : Form
{
    public CommunicationMode SelectedMode { get; private set; }
    private readonly List<(Button Button, string English, string Czech)> _choices = [];

    public ModeSelectionForm()
    {
        Text = $"Marks IO {BrandTheme.VersionText} – Select mode";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScroll = true;
        ClientSize = new Size(650, 740);
        MinimumSize = new Size(420, 480);
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, Height = 730, Padding = new Padding(12), RowCount = 11, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        for (var i = 0; i < 8; i++) layout.RowStyles.Add(new RowStyle(SizeType.Percent, 12.5f));

        var hero = new Panel
        {
            Dock = DockStyle.Fill,
            BackgroundImage = BrandTheme.LoadArtwork("marks-io-header.png"),
            BackgroundImageLayout = ImageLayout.Stretch,
            Margin = new Padding(0, 0, 0, 6)
        };
        var logo = new PictureBox
        {
            Image = BrandTheme.LoadArtwork("marks-io-logo.png"),
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(20, 26), Size = new Size(135, 95),
            BackColor = Color.Transparent
        };
        var heading = new Label
        {
            Text = "MARKS IO", Font = new Font("Segoe UI", 18, FontStyle.Bold),
            Location = new Point(165, 45), Size = new Size(360, 38),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.Transparent, ForeColor = BrandTheme.Text
        };
        var subtitle = new Label
        {
            Text = $"Industrial communication · {BrandTheme.VersionText}", Font = new Font("Segoe UI", 9),
            Location = new Point(168, 87), Size = new Size(350, 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.Transparent, ForeColor = BrandTheme.Muted
        };
        hero.Controls.AddRange([logo, heading, subtitle]);
        layout.Controls.Add(hero, 0, 0);

        var languageRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var languageLabel = new Label { Text = "Language:", AutoSize = true, Margin = new Padding(2, 11, 8, 0) };
        var english = new RadioButton { Text = "English", AutoSize = true, Margin = new Padding(0, 9, 14, 0) };
        var czech = new RadioButton { Text = "Čeština", AutoSize = true, Margin = new Padding(0, 9, 0, 0) };
        languageRow.Controls.AddRange([languageLabel, english, czech]);
        layout.Controls.Add(languageRow, 0, 1);

        var instruction = new Label
        {
            Text = "Choose one mode. Restart the app to change it.",
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(2, 0, 0, 0)
        };
        layout.Controls.Add(instruction, 0, 2);
        AddChoice(layout, 3, "Modbus TCP server", "Modbus TCP server", CommunicationMode.ModbusServer);
        AddChoice(layout, 4, "Modbus TCP client", "Modbus TCP klient", CommunicationMode.ModbusClient);
        AddChoice(layout, 5, "TCP/IP server", "TCP/IP server", CommunicationMode.TcpServer);
        AddChoice(layout, 6, "TCP/IP client", "TCP/IP klient", CommunicationMode.TcpClient);
        AddChoice(layout, 7, "Snap7 server", "Snap7 server", CommunicationMode.Snap7Server);
        AddChoice(layout, 8, "Snap7 client", "Snap7 klient", CommunicationMode.Snap7Client);
        AddChoice(layout, 9, "OPC UA server", "OPC UA server", CommunicationMode.OpcUaServer);
        AddChoice(layout, 10, "OPC UA client", "OPC UA klient", CommunicationMode.OpcUaClient);
        Controls.Add(layout);
        BrandTheme.Apply(this);
        logo.BackColor = heading.BackColor = subtitle.BackColor = Color.Transparent;
        heading.ForeColor = BrandTheme.Text;
        subtitle.ForeColor = BrandTheme.Muted;
        void UpdateLanguage()
        {
            UiText.Language = czech.Checked ? UiLanguage.Czech : UiLanguage.English;
            Text = UiText.Language == UiLanguage.Czech
                ? $"Marks IO {BrandTheme.VersionText} – výběr režimu"
                : $"Marks IO {BrandTheme.VersionText} – Select mode";
            subtitle.Text = UiText.Language == UiLanguage.Czech
                ? $"Průmyslová komunikace · {BrandTheme.VersionText}"
                : $"Industrial communication · {BrandTheme.VersionText}";
            languageLabel.Text = UiText.Language == UiLanguage.Czech ? "Jazyk:" : "Language:";
            instruction.Text = UiText.Language == UiLanguage.Czech
                ? "Zvolte jeden režim. Pro změnu aplikaci spusťte znovu."
                : "Choose one mode. Restart the app to change it.";
            foreach (var choice in _choices)
                choice.Button.Text = UiText.Language == UiLanguage.Czech ? choice.Czech : choice.English;
        }
        english.CheckedChanged += (_, _) => UpdateLanguage();
        czech.CheckedChanged += (_, _) => UpdateLanguage();
        english.Checked = true;
    }

    private void AddChoice(TableLayoutPanel layout, int row, string english, string czech, CommunicationMode mode)
    {
        var button = new Button
        {
            Text = english, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 3, 0, 3)
        };
        button.Click += (_, _) => { SelectedMode = mode; DialogResult = DialogResult.OK; };
        layout.Controls.Add(button, 0, row);
        _choices.Add((button, english, czech));
    }
}
