using NMSE.Data;

namespace NMSE.UI;

internal class ToolsDialog : Form
{
    internal readonly FlowLayoutPanel Footer;
    internal ToolsDialog(string title)
    {
        Text = UiStrings.Get(title);
        Size = new Size(1000, 660);
        MinimumSize = new Size(700, 460);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        Padding = new Padding(12);
        Footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
        Controls.Add(Footer);
    }

    internal Button AddButton(string key, EventHandler? action = null, DialogResult result = DialogResult.None)
    {
        var button = new Button { Text = UiStrings.Get(key), AutoSize = true, MinimumSize = new Size(95, 32), DialogResult = result };
        if (action != null) button.Click += action;
        Footer.Controls.Add(button);
        return button;
    }

    internal static ListView MakeList(params (string Key, int Width)[] columns)
    {
        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, ShowItemToolTips = true };
        foreach (var column in columns) list.Columns.Add(UiStrings.Get(column.Key), column.Width);
        return list;
    }

    protected override void OnShown(EventArgs e)
    {
        Footer.SendToBack();
        ThemeApplicator.ApplyToForm(this);
        base.OnShown(e);
    }
}
