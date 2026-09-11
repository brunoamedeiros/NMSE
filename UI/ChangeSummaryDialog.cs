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
    private readonly record struct ReviewRow(ReviewEntry Entry, ChangePresentationLogic.Row Detail);

    internal ChangeSummaryDialog(ChangeSummaryLogic.Report report, bool beforeSave,
        GameItemDatabase? database = null, JsonObject? saveData = null, JsonObject? accountData = null)
        : base("tools_plus.summary")
    {
        var entries = report.Changes.Select(change => new ReviewEntry(change,
            ChangePresentationLogic.Build(change, database,
                change.Scope == ChangeReviewLogic.DataScope.Account ? accountData : saveData))).ToArray();
        var rows = entries.SelectMany(entry => entry.Presentation.Rows.Select(detail => new ReviewRow(entry, detail))).ToArray();
        MinimumSize = new Size(850, 480);
        Size = new Size(1120, Math.Clamp(280 + Math.Min(rows.Length, 10) * 48, 480, 740));

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        string countKey = report.Truncated ? "summary.readable.report_truncated"
            : entries.Length == 1 ? "summary.readable.count_one" : "summary.readable.count";
        string heading = entries.Length == 0 && !report.Truncated ? UiStrings.Get("summary.readable.empty")
            : UiStrings.Format(countKey, entries.Length);
        if (entries.Any(entry => entry.Presentation.Truncated))
            heading += Environment.NewLine + UiStrings.Get("summary.readable.some_details_omitted");
        layout.Controls.Add(new Label
        {
            AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10), Text = heading
        }, 0, 0);

        var comparison = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false, AllowUserToOrderColumns = false, RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
            BorderStyle = BorderStyle.FixedSingle, Margin = Padding.Empty, VirtualMode = true,
            ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText,
            AccessibleName = UiStrings.Get("summary.readable.comparison")
        };
        comparison.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        comparison.DefaultCellStyle.Padding = new Padding(8, 6, 8, 6);
        comparison.DefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;
        comparison.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 6, 8, 6);
        foreach (var (key, width) in new[] { ("summary.area", 26), ("summary.field", 30), ("summary.before", 22), ("summary.after", 22) })
            comparison.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = UiStrings.Get(key), FillWeight = width, MinimumWidth = 140,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        // Present each detail once. Shared virtual rows avoid allocating/measuring an
        // entire control row for every detail in a large report.
        comparison.CellValueNeeded += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= rows.Length) return;
            var row = rows[e.RowIndex];
            e.Value = e.ColumnIndex switch
            {
                0 => row.Entry.Presentation.Area,
                1 => DescribeRow(row),
                2 => row.Detail.Before,
                3 => row.Detail.After,
                _ => ""
            };
        };
        comparison.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.RowIndex < rows.Length && rows[e.RowIndex].Entry.Presentation.Truncated)
                e.ToolTipText = UiStrings.Get("summary.readable.details_truncated");
        };
        comparison.RowCount = rows.Length;
        layout.Controls.Add(comparison, 0, 1);

        var note = new Label { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 4) };
        var advanced = new CheckBox
        {
            AutoSize = true, Text = UiStrings.Get("summary.readable.advanced"),
            Margin = new Padding(0, 4, 0, 4), Enabled = rows.Length > 0
        };
        var raw = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 4, 0, 0), Visible = false
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
        layout.Controls.Add(note, 0, 2);
        layout.Controls.Add(advanced, 0, 3);
        layout.Controls.Add(raw, 0, 4);
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
        ReviewEntry? rawEntry = null;
        var open = AddButton("summary.open");
        var revert = AddButton("summary.revert");
        open.Click += (_, _) => Request(ReviewAction.Open);
        revert.Click += (_, _) => Request(ReviewAction.Revert);
        open.Enabled = revert.Enabled = false;

        void UpdateRaw()
        {
            // Serialize only on demand, once per original group rather than once per detail row.
            if (!advanced.Checked || ReferenceEquals(rawEntry, selectedEntry)) return;
            rawEntry = selectedEntry;
            path.Text = SelectedChange?.Path ?? "";
            rawBefore.Text = SelectedChange is { } selected ? Preview(selected.BeforeValue, selected.HadBefore) : "";
            rawAfter.Text = SelectedChange is { } current ? Preview(current.AfterValue, current.HasAfter) : "";
        }

        advanced.CheckedChanged += (_, _) =>
        {
            layout.SuspendLayout();
            raw.Visible = advanced.Checked;
            layout.RowStyles[1].Height = advanced.Checked ? 60 : 100;
            layout.RowStyles[4].SizeType = advanced.Checked ? SizeType.Percent : SizeType.Absolute;
            layout.RowStyles[4].Height = advanced.Checked ? 40 : 0;
            UpdateRaw();
            layout.ResumeLayout(true);
        };
        void SelectChange()
        {
            int index = comparison.CurrentCell?.RowIndex ?? -1;
            selectedEntry = index >= 0 && index < rows.Length ? rows[index].Entry : null;
            SelectedChange = selectedEntry?.Change;
            open.Enabled = revert.Enabled = SelectedChange?.Tokens.Count > 0;
            var view = selectedEntry?.Presentation;
            revert.Text = UiStrings.Get(view?.Grouped == true ? "summary.readable.revert_group" : "summary.revert");
            note.Text = view?.Grouped == true
                ? UiStrings.Format("summary.readable.group_selection", view.Title, view.Rows.Count)
                : UiStrings.Get("summary.readable.save_hint");
            if (view?.Truncated == true) note.Text += " " + UiStrings.Get("summary.readable.details_truncated");
            if (beforeSave) note.Text += " " + UiStrings.Get("summary.readable.cancel_save_hint");
            UpdateRaw();
        }
        comparison.CurrentCellChanged += (_, _) => SelectChange();
        comparison.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0) Request(ReviewAction.Open);
        };
        Shown += (_, _) =>
        {
            if (rows.Length > 0)
            {
                comparison.CurrentCell = comparison.Rows[0].Cells[0];
                comparison.Select();
            }
            SelectChange();
        };

        void Request(ReviewAction action)
        {
            if (SelectedChange is not { Tokens.Count: > 0 } || selectedEntry is not { } entry) return;
            string confirmKey = entry.Presentation.Grouped ? "summary.readable.revert_group_confirm" : "summary.revert_confirm";
            if (action == ReviewAction.Revert && MessageBox.Show(this,
                UiStrings.Format(confirmKey, entry.Presentation.Title), revert.Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            RequestedAction = action;
            // Visible detail rows retain their original typed change as the atomic revert target.
            // Opening or reverting cancels a pending save, so the next attempt reviews fresh data.
            DialogResult = DialogResult.Retry;
            Close();
        }
    }

    private static string DescribeRow(ReviewRow row)
    {
        string title = row.Entry.Presentation.Title;
        string detail = row.Detail.Label;
        return detail.Length == 0 || title == detail || title.EndsWith(" / " + detail, StringComparison.Ordinal)
            || title.EndsWith(" · " + detail, StringComparison.Ordinal) ? title : title + " — " + detail;
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
