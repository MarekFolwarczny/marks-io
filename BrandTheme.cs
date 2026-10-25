using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace ModbusServer;

/// <summary>Jednotná paleta pro všechny protokolové formuláře.</summary>
internal static class BrandTheme
{
    internal static readonly string VersionText = $"v{Assembly.GetExecutingAssembly().GetName().Version!.ToString(3)}";
    internal static readonly Color Background = Color.FromArgb(20, 23, 27);
    internal static readonly Color Surface = Color.FromArgb(31, 36, 42);
    internal static readonly Color Accent = Color.FromArgb(156, 166, 175);
    internal static readonly Color Text = Color.FromArgb(242, 245, 247);
    internal static readonly Color Muted = Color.FromArgb(184, 193, 201);
    private static readonly Color Border = Color.FromArgb(68, 77, 86);

    internal static Image LoadArtwork(string fileName)
    {
        var name = $"{typeof(BrandTheme).Namespace}.Assets.{fileName}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Grafický podklad {fileName} chybí.");
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    internal static void Apply(Form form)
    {
        form.Font = new Font("Segoe UI", 9F);
        form.Text = UiText.Translate(form.Text);
        if (!form.Text.StartsWith("Marks IO", StringComparison.Ordinal)) form.Text = $"Marks IO {VersionText} · {form.Text}";
        // EXE obsahuje stejnou ikonu i pro Průzkumníka; přiřadíme ji také každému oknu.
        if (Icon.ExtractAssociatedIcon(Application.ExecutablePath) is { } icon) form.Icon = icon;
        Style(form);
    }

    private static void Style(Control control)
    {
        control.ForeColor = Text;
        switch (control)
        {
            case Form or Panel or TableLayoutPanel or FlowLayoutPanel:
                control.BackColor = Background;
                break;
            case GroupBox:
                control.BackColor = Background;
                control.ForeColor = Accent;
                break;
            case Label:
                control.BackColor = Color.Transparent;
                control.ForeColor = Muted;
                break;
            case RadioButton:
                control.BackColor = Background;
                break;
            case TextBox box:
                box.BackColor = Surface;
                box.ForeColor = Text;
                box.BorderStyle = BorderStyle.FixedSingle;
                box.PlaceholderText = UiText.Translate(box.PlaceholderText);
                break;
            case NumericUpDown or ComboBox or ListBox:
                control.BackColor = Surface;
                control.ForeColor = Text;
                if (control is ListBox list)
                    for (var i = 0; i < list.Items.Count; i++)
                        if (list.Items[i] is string item) list.Items[i] = UiText.Translate(item);
                break;
            case Button button:
                button.FlatStyle = FlatStyle.Flat;
                button.UseVisualStyleBackColor = false;
                button.Cursor = Cursors.Hand;
                button.Padding = new Padding(8, 3, 8, 3);
                button.FlatAppearance.BorderColor = Border;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(58, 67, 76);
                button.BackColor = Surface;
                button.EnabledChanged += (_, _) => StyleButtonState(button);
                StyleButtonState(button);
                break;
            case DataGridView grid:
                grid.BackgroundColor = Background;
                grid.BorderStyle = BorderStyle.None;
                grid.GridColor = Border;
                grid.EnableHeadersVisualStyles = false;
                grid.ColumnHeadersDefaultCellStyle.BackColor = Surface;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = Accent;
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Surface;
                grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Accent;
                grid.DefaultCellStyle.BackColor = Surface;
                grid.DefaultCellStyle.ForeColor = Text;
                grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(63, 70, 77);
                grid.DefaultCellStyle.SelectionForeColor = Text;
                grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(38, 44, 51);
                foreach (DataGridViewColumn column in grid.Columns)
                    column.HeaderText = UiText.Translate(column.HeaderText);
                break;
        }

        // Stavové popisky se mění i po startu serveru; překlad proto sleduje jejich Text.
        if (control is Form or Label or GroupBox or Button || control is TextBox { ReadOnly: true })
        {
            control.TextChanged += (_, _) => LocalizeText(control);
            LocalizeText(control);
        }

        foreach (Control child in control.Controls) Style(child);
    }

    private static void LocalizeText(Control control)
    {
        var translated = UiText.Translate(control.Text);
        if (translated != control.Text) control.Text = translated;
    }

    private static void StyleButtonState(Button button)
    {
        var primary = button.Text is "Spustit" or "Připojit" or "Start" or "Connect";
        button.BackColor = !button.Enabled ? Color.FromArgb(76, 84, 92) : primary ? Accent : Surface;
        button.ForeColor = !button.Enabled ? Muted : primary ? Background : Text;
        button.FlatAppearance.BorderColor = primary && button.Enabled ? Accent : Border;
        button.FlatAppearance.MouseOverBackColor = primary
            ? Color.FromArgb(184, 193, 201) : Color.FromArgb(58, 67, 76);
    }
}
