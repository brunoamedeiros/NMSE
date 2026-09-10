using NMSE.Core;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.UI;

internal sealed class HotkeyDialog : ToolsDialog
{
    internal JsonArray Draft { get; private set; }
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private sealed record Choice(string Label, HotkeyLogic.ActionOption? Option)
    {
        public override string ToString() => Label;
    }

    internal HotkeyDialog(JsonArray draft) : base("tools_plus.hotkeys")
    {
        Draft = draft.DeepClone();
        Controls.Add(_tabs);
        Controls.Add(new Label { Text = UiStrings.Get("hotkeys.help"), Dock = DockStyle.Top, Height = 64 });
        CancelButton = AddButton("tools_plus.cancel", result: DialogResult.Cancel);
        AcceptButton = AddButton("tools_plus.apply", result: DialogResult.OK);
        AddButton("tools_plus.export", (_, _) => Export());
        AddButton("tools_plus.import", (_, _) => Import());
        Rebuild();
    }

    private void Rebuild()
    {
        int selected = Math.Max(0, _tabs.SelectedIndex);
        while (_tabs.TabPages.Count > 0) { var page = _tabs.TabPages[0]; _tabs.TabPages.Remove(page); page.Dispose(); }
        for (int c = 0; c < 3; c++)
        {
            int context = c;
            var page = new TabPage(UiStrings.Get(new[] { "hotkeys.player", "hotkeys.ship", "hotkeys.vehicle" }[c]));
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2, Padding = new Padding(12) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int s = 0; s < 10; s++)
            {
                int slot = s;
                var entry = HotkeyLogic.Entry(Draft, c, s);
                string action = entry.GetObject("Action")!.GetString("QuickMenuActions")!;
                string actionLabel = HotkeyLogic.Options(c).FirstOrDefault(x => x.Id == action) is { } known
                    ? UiStrings.Get("hotkeys.action." + known.LabelKey) : action;
                var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, AccessibleName = $"{page.Text} {s}" };
                // Always start with Keep current, including unsupported/recharge/emote bindings.
                combo.Items.Add(new Choice(UiStrings.Format("hotkeys.keep", actionLabel), null));
                foreach (var option in HotkeyLogic.Options(c))
                    combo.Items.Add(new Choice(UiStrings.Get("hotkeys.action." + option.LabelKey), option));
                combo.SelectedIndex = 0;
                combo.SelectedIndexChanged += (_, _) =>
                {
                    if (combo.SelectedItem is Choice { Option: { } option }) HotkeyLogic.SetAction(Draft, context, slot, option);
                    else Draft.GetObject(context).GetArray("KeyActions")!.Set(slot, entry.DeepClone());
                };
                // The captured original must not be mutated by a subsequent action selection.
                entry = entry.DeepClone();
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
                table.Controls.Add(new Label { Text = s.ToString(System.Globalization.CultureInfo.InvariantCulture), AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, s);
                table.Controls.Add(combo, 1, s);
            }
            page.Controls.Add(table);
            _tabs.TabPages.Add(page);
        }
        _tabs.SelectedIndex = selected;
    }

    private void Export()
    {
        using var dialog = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "shortcuts.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllText(dialog.FileName, HotkeyLogic.Export(Draft)); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, UiStrings.Get("dialog.error")); }
    }

    private void Import()
    {
        using var dialog = new OpenFileDialog { Filter = "JSON (*.json)|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 1_000_000) throw new InvalidDataException("hotkeys.invalid");
            var imported = HotkeyLogic.Import(File.ReadAllText(dialog.FileName));
            Draft = imported;
            Rebuild();
            ThemeApplicator.ApplyToForm(this);
        }
        catch (Exception) { MessageBox.Show(this, UiStrings.Get("hotkeys.invalid"), UiStrings.Get("dialog.error")); }
    }
}
