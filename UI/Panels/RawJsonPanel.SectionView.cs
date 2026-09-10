using NMSE.Core;
using NMSE.Data;
using NMSE.UI.Controls;

namespace NMSE.UI.Panels;

public partial class RawJsonPanel
{
    /// <summary>
    /// Displays a snapshot of one tree value without changing the save or the
    /// full-document text editor. Objects, arrays and scalar values are supported.
    /// </summary>
    private void ShowSelectedJson()
    {
        if (_treeView.SelectedNode is not { Tag: NodeTag } node) return;
        using var dialog = CreateSelectedJsonDialog(node);
        dialog.ShowDialog(this);
    }

    private Form CreateSelectedJsonDialog(TreeNode node)
    {
        var tag = (NodeTag)node.Tag!;
        string json = RawJsonLogic.SerializeValue(tag.Value);
        var pathParts = new List<string>();
        for (TreeNode? current = node; current != null; current = current.Parent)
            pathParts.Add(current.Tag is NodeTag currentTag ? currentTag.Key ?? "Root" : "Root");
        pathParts.Reverse();

        var dialog = new Form
        {
            Text = UiStrings.Get("raw_json.selected_title"),
            Size = new Size(900, 650),
            MinimumSize = new Size(500, 350),
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            ShowInTaskbar = false
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var path = new TextBox
        {
            Text = string.Join(" / ", pathParts), Dock = DockStyle.Fill, ReadOnly = true,
            BorderStyle = BorderStyle.None, Margin = new Padding(3, 5, 3, 8), TabStop = false
        };
        var viewer = new JsonSyntaxTextBox { Dock = DockStyle.Fill, ReadOnly = true, JsonText = json };
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true, Padding = new Padding(0, 5, 0, 0)
        };
        var close = new Button
        {
            Text = UiStrings.Get("common.close"), AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, DialogResult = DialogResult.Cancel
        };
        var copy = new Button
        {
            Text = UiStrings.Get("common.copy"), AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(viewer.JsonText); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, UiStrings.Get("common.error"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        footer.Controls.Add(close);
        footer.Controls.Add(copy);
        footer.Controls.Add(new Label
        {
            Text = UiStrings.Get("raw_json.selected_read_only"), AutoSize = true,
            Margin = new Padding(3, 7, 10, 3)
        });
        layout.Controls.Add(path, 0, 0);
        layout.Controls.Add(viewer, 0, 1);
        layout.Controls.Add(footer, 0, 2);
        dialog.Controls.Add(layout);
        dialog.CancelButton = close;
        dialog.Shown += (_, _) => viewer.Focus();
        dialog.FormClosed += (_, _) => viewer.ClearContent();
        return dialog;
    }
}
