using System.Globalization;
using System.Text;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.UI;

internal sealed class ChangeSummaryDialog : ToolsDialog
{
    internal enum ReviewAction { None, Open, Revert }
    internal ReviewAction RequestedAction { get; private set; }
    internal ChangeSummaryLogic.Change? SelectedChange { get; private set; }

    private sealed record ReviewEntry(ChangeSummaryLogic.Change Change, ChangePresentationLogic.Presentation Presentation);

    internal ChangeSummaryDialog(ChangeSummaryLogic.Report report, bool beforeSave,
        GameItemDatabase? database = null, JsonObject? saveData = null, JsonObject? accountData = null)
        : base("tools_plus.summary")
    {
        Size = new Size(1120, 760);
        MinimumSize = new Size(850, 620);
        var entries = report.Changes.Select(change => new ReviewEntry(change,
            ChangePresentationLogic.Build(change, database,
                change.Scope == ChangeReviewLogic.DataScope.Account ? accountData : saveData))).ToArray();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 67));
        layout.Controls.Add(new Label
        {
            AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10),
            Text = UiStrings.Format(report.Truncated ? "summary.readable.report_truncated" : "summary.readable.count", entries.Length)
        }, 0, 0);

        var list = MakeList(("summary.area", 170), ("summary.field", 410), ("summary.before", 230), ("summary.after", 230));
        list.AccessibleName = UiStrings.Get("summary.readable.changes");
        list.Margin = Padding.Empty;
        foreach (var entry in entries)
        {
            var view = entry.Presentation;
            list.Items.Add(new ListViewItem([view.Area, view.Title, view.Before, view.After])
            {
                Tag = entry,
                ToolTipText = $"{view.Area} / {view.Title}{Environment.NewLine}{view.Before} → {view.After}"
            });
        }
        list.SizeChanged += (_, _) =>
        {
            int width = Math.Max(400, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6);
            list.Columns[0].Width = width * 17 / 100;
            list.Columns[1].Width = width * 39 / 100;
            list.Columns[2].Width = list.Columns[3].Width = width * 22 / 100;
        };
        layout.Controls.Add(list, 0, 1);

        var details = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0, 10, 0, 0)
        };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var title = new Label { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
        var comparison = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false, AllowUserToOrderColumns = false, RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
            BorderStyle = BorderStyle.FixedSingle, Margin = Padding.Empty,
            ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText,
            AccessibleName = UiStrings.Get("summary.readable.comparison")
        };
        comparison.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        comparison.DefaultCellStyle.Padding = new Padding(8, 6, 8, 6);
        comparison.DefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;
        comparison.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 6, 8, 6);
        comparison.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = UiStrings.Get("summary.readable.detail"), FillWeight = 30, MinimumWidth = 120,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        foreach (string key in new[] { "summary.before", "summary.after" })
            comparison.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = UiStrings.Get(key), FillWeight = 35, MinimumWidth = 150,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        var note = new Label { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 4) };
        var advanced = new CheckBox
        {
            AutoSize = true, Text = UiStrings.Get("summary.readable.advanced"),
            Margin = new Padding(0, 4, 0, 4), Enabled = entries.Length > 0
        };
        var raw = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty, Visible = false
        };
        raw.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        raw.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        raw.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        raw.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var path = new Label { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 6) };
        raw.Controls.Add(path, 0, 0);
        raw.SetColumnSpan(path, 2);
        TextBox RawPane(string key, int column)
        {
            var box = new GroupBox { Text = UiStrings.Get(key), Dock = DockStyle.Fill, Margin = new Padding(column == 0 ? 0 : 4, 0, column == 0 ? 4 : 0, 0) };
            var text = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = false,
                ScrollBars = ScrollBars.Both, AccessibleName = UiStrings.Get(key) + " — JSON"
            };
            box.Controls.Add(text);
            raw.Controls.Add(box, column, 1);
            return text;
        }
        var rawBefore = RawPane("summary.before", 0);
        var rawAfter = RawPane("summary.after", 1);
        var comparisonHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        comparisonHost.Controls.Add(comparison);
        comparisonHost.Controls.Add(raw);
        details.Controls.Add(title, 0, 0);
        details.Controls.Add(comparisonHost, 0, 1);
        details.Controls.Add(note, 0, 2);
        details.Controls.Add(advanced, 0, 3);
        layout.Controls.Add(details, 0, 2);
        Controls.Add(layout);

        CancelButton = AddButton(beforeSave ? "tools_plus.cancel" : "tools_plus.close", result: DialogResult.Cancel);
        if (beforeSave) AcceptButton = AddButton("menu.file.save", result: DialogResult.OK);
        var copy = AddButton("tools_plus.copy", (_, _) =>
        {
            try { Clipboard.SetText(CopySummary(entries, report.Truncated)); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, UiStrings.Get("dialog.error")); }
        });
        copy.Enabled = entries.Length > 0;
        ReviewEntry? selectedEntry = null;
        var open = AddButton("summary.open");
        var revert = AddButton("summary.revert");
        open.Click += (_, _) => Request(ReviewAction.Open);
        revert.Click += (_, _) => Request(ReviewAction.Revert);
        open.Enabled = revert.Enabled = false;
        title.Text = UiStrings.Get(entries.Length == 0 ? "summary.readable.empty" : "summary.readable.select");
        note.Text = UiStrings.Get("summary.readable.save_hint");

        void UpdateRaw()
        {
            // Only serialize on demand. The friendly view never requires JSON.
            if (!advanced.Checked) return;
            path.Text = SelectedChange?.Path ?? "";
            rawBefore.Text = SelectedChange is { } selected ? Preview(selected.BeforeValue, selected.HadBefore) : "";
            rawAfter.Text = SelectedChange is { } current ? Preview(current.AfterValue, current.HasAfter) : "";
        }

        advanced.CheckedChanged += (_, _) =>
        {
            comparisonHost.SuspendLayout();
            comparison.Visible = !advanced.Checked;
            raw.Visible = advanced.Checked;
            UpdateRaw();
            comparisonHost.ResumeLayout(true);
        };
        list.SelectedIndexChanged += (_, _) =>
        {
            selectedEntry = list.SelectedItems.Count == 1 ? list.SelectedItems[0].Tag as ReviewEntry : null;
            SelectedChange = selectedEntry?.Change;
            open.Enabled = revert.Enabled = SelectedChange?.Tokens.Count > 0;
            comparison.Rows.Clear();
            if (selectedEntry?.Presentation is { } view)
            {
                title.Text = $"{view.Area} / {view.Title}".Trim(' ', '/');
                foreach (var row in view.Rows) comparison.Rows.Add(row.Label, row.Before, row.After);
                comparison.ClearSelection();
                revert.Text = UiStrings.Get(view.Grouped ? "summary.readable.revert_group" : "summary.revert");
                note.Text = string.Join(" ", new[]
                {
                    UiStrings.Get(view.Grouped ? "summary.readable.group_hint" : "summary.readable.save_hint"),
                    view.Truncated ? UiStrings.Get("summary.readable.details_truncated") : ""
                }.Where(text => text.Length > 0));
            }
            else
            {
                title.Text = UiStrings.Get("summary.readable.select");
                note.Text = UiStrings.Get("summary.readable.save_hint");
            }
            UpdateRaw();
        };
        list.ItemActivate += (_, _) => Request(ReviewAction.Open);
        Shown += (_, _) =>
        {
            if (list.Items.Count == 0) return;
            list.Items[0].Selected = true;
            list.Items[0].Focused = true;
            list.Select();
        };

        void Request(ReviewAction action)
        {
            if (SelectedChange is not { Tokens.Count: > 0 } || selectedEntry is not { } entry) return;
            string confirmKey = entry.Presentation.Grouped ? "summary.readable.revert_group_confirm" : "summary.revert_confirm";
            if (action == ReviewAction.Revert && MessageBox.Show(this,
                UiStrings.Format(confirmKey, entry.Presentation.Title), revert.Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            RequestedAction = action;
            // Keep the original typed change. Friendly detail rows are not independent revert targets.
            // Opening or reverting cancels a pending save, so the next attempt reviews fresh data.
            DialogResult = DialogResult.Retry;
            Close();
        }
    }

    private static string CopySummary(IEnumerable<ReviewEntry> entries, bool truncated)
    {
        var text = new StringBuilder();
        foreach (var entry in entries)
        {
            var view = entry.Presentation;
            text.AppendLine($"{view.Area} / {view.Title}".Trim(' ', '/'));
            foreach (var row in view.Rows)
                text.AppendLine(CultureInfo.CurrentCulture, $"  {row.Label}: {UiStrings.Get("summary.before")}: {row.Before} → {UiStrings.Get("summary.after")}: {row.After}");
            if (view.Grouped) text.AppendLine(UiStrings.Get("summary.readable.group_hint"));
            if (view.Truncated) text.AppendLine(UiStrings.Get("summary.readable.details_truncated"));
            text.AppendLine();
        }
        if (truncated) text.AppendLine(UiStrings.Get("summary.more"));
        return text.ToString();
    }

    private static string Preview(object? value, bool exists)
    {
        if (!exists) return UiStrings.Get("summary.missing");
        string text = RawJsonLogic.SerializeValue(value);
        const int limit = 20000;
        return text.Length <= limit ? text : text[..limit] + Environment.NewLine + UiStrings.Get("summary.preview_truncated");
    }
}
