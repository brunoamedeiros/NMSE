using NMSE.Core;
using NMSE.Data;

namespace NMSE.UI;

internal sealed class ChangeSummaryDialog : ToolsDialog
{
    internal enum ReviewAction { None, Open, Revert }
    internal ReviewAction RequestedAction { get; private set; }
    internal ChangeSummaryLogic.Change? SelectedChange { get; private set; }

    internal ChangeSummaryDialog(ChangeSummaryLogic.Report report, bool beforeSave) : base("tools_plus.summary")
    {
        var list = MakeList(("summary.area", 165), ("summary.field", 380), ("summary.before", 170), ("summary.after", 170));
        foreach (var change in report.Changes)
            list.Items.Add(new ListViewItem([change.Area, change.Field, change.Before, change.After]) { ToolTipText = change.Path, Tag = change });
        var detail = new TextBox
        {
            Dock = DockStyle.Bottom, Height = 135, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical, Text = UiStrings.Get("summary.action_hint")
        };
        Controls.Add(list);
        Controls.Add(detail);
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
        var open = AddButton("summary.open", (_, _) => Request(ReviewAction.Open));
        var revert = AddButton("summary.revert", (_, _) => Request(ReviewAction.Revert));
        open.Enabled = revert.Enabled = false;
        list.SelectedIndexChanged += (_, _) =>
        {
            SelectedChange = list.SelectedItems.Count == 1 ? list.SelectedItems[0].Tag as ChangeSummaryLogic.Change : null;
            open.Enabled = revert.Enabled = SelectedChange?.Tokens.Count > 0;
            detail.Text = SelectedChange is { } selected
                ? selected.Path + Environment.NewLine + Environment.NewLine +
                  UiStrings.Get("summary.before") + ": " + Preview(selected.BeforeValue, selected.HadBefore) + Environment.NewLine +
                  UiStrings.Get("summary.after") + ": " + Preview(selected.AfterValue, selected.HasAfter)
                : UiStrings.Get("summary.action_hint");
        };
        list.ItemActivate += (_, _) => Request(ReviewAction.Open);

        void Request(ReviewAction action)
        {
            if (SelectedChange is not { Tokens.Count: > 0 } selected) return;
            if (action == ReviewAction.Revert && MessageBox.Show(this,
                UiStrings.Format("summary.revert_confirm", selected.Field), UiStrings.Get("summary.revert"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            RequestedAction = action;
            // Opening or reverting exits the current review. A pending save is
            // cancelled so its next attempt always reviews the updated data.
            DialogResult = DialogResult.Retry;
            Close();
        }
    }

    private static string Preview(object? value, bool exists)
    {
        if (!exists) return UiStrings.Get("summary.missing");
        string text = RawJsonLogic.SerializeValue(value);
        const int limit = 20000;
        return text.Length <= limit ? text : text[..limit] + Environment.NewLine + UiStrings.Get("summary.preview_truncated");
    }
}
