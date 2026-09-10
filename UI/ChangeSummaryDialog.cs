using NMSE.Core;
using NMSE.Data;

namespace NMSE.UI;

internal sealed class ChangeSummaryDialog : ToolsDialog
{
    internal ChangeSummaryDialog(ChangeSummaryLogic.Report report, bool beforeSave) : base("tools_plus.summary")
    {
        var list = MakeList(("summary.area", 165), ("summary.field", 380), ("summary.before", 170), ("summary.after", 170));
        foreach (var change in report.Changes)
            list.Items.Add(new ListViewItem([change.Area, change.Field, change.Before, change.After]) { ToolTipText = change.Path });
        Controls.Add(list);
        Controls.Add(new Label
        {
            Dock = DockStyle.Top, Height = 54,
            Text = UiStrings.Format(report.Truncated ? "summary.truncated" : "summary.count", report.Changes.Count)
        });
        CancelButton = AddButton(beforeSave ? "tools_plus.cancel" : "tools_plus.close", result: DialogResult.Cancel);
        if (beforeSave) AcceptButton = AddButton("menu.file.save", result: DialogResult.OK);
        var copy = AddButton("tools_plus.copy", (_, _) =>
        {
            try { Clipboard.SetText(string.Join(Environment.NewLine, report.Changes.Select(c => $"{c.Area} / {c.Field}: {c.Before} → {c.After}")) + (report.Truncated ? Environment.NewLine + UiStrings.Get("summary.more") : "")); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, UiStrings.Get("dialog.error")); }
        });
        copy.Enabled = report.Changes.Count > 0;
    }
}
