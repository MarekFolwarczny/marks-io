using System.Windows.Forms;

namespace ModbusServer;

/// <summary>Po potvrzení volby spustí jediné hlavní okno vybraného protokolu.</summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var selection = new ModeSelectionForm();
        if (selection.ShowDialog() != DialogResult.OK) return;
        Form form = selection.SelectedMode switch
        {
            CommunicationMode.ModbusServer => new MainForm(),
            CommunicationMode.ModbusClient => new ModbusClientForm(),
            CommunicationMode.TcpServer => new TcpTextForm(serverMode: true),
            CommunicationMode.TcpClient => new TcpTextForm(serverMode: false),
            CommunicationMode.Snap7Server => new Snap7Form(serverMode: true),
            CommunicationMode.Snap7Client => new Snap7Form(serverMode: false),
            CommunicationMode.OpcUaServer => new OpcUaForm(serverMode: true),
            CommunicationMode.OpcUaClient => new OpcUaForm(serverMode: false),
            _ => throw new ArgumentOutOfRangeException()
        };
        BrandTheme.Apply(form);
        Application.Run(form);
    }
}
