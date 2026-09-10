using NMSE.Core;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Panels;

namespace NMSE.UI;

public partial class MainFormResources
{
    private ToolStripMenuItem _inventorySearchMenu = null!;
    private ToolStripMenuItem _hotkeyMenu = null!;
    private ToolStripMenuItem _summaryMenu = null!;
    private byte[]? _summarySaveBaseline;
    private byte[]? _summaryAccountBaseline;
    private bool _lastSaveSucceeded;

    private void AddPersonalTools(ToolStripMenuItem menu)
    {
        menu.DropDownItems.Add(new ToolStripSeparator());
        _inventorySearchMenu = new ToolStripMenuItem("", null, OnInventorySearch, Keys.Control | Keys.Shift | Keys.F) { Enabled = false };
        _hotkeyMenu = new ToolStripMenuItem("", null, OnHotkeys) { Enabled = false };
        _summaryMenu = new ToolStripMenuItem("", null, OnChangeSummary) { Enabled = false };
        menu.DropDownItems.AddRange([_inventorySearchMenu, _hotkeyMenu, _summaryMenu]);
        LocalisePersonalTools();
    }

    private void LocalisePersonalTools()
    {
        _inventorySearchMenu.Text = UiStrings.Get("tools_plus.search");
        _hotkeyMenu.Text = UiStrings.Get("tools_plus.hotkeys");
        _summaryMenu.Text = UiStrings.Get("tools_plus.summary");
    }

    private void CaptureSummaryBaseline()
    {
        _summarySaveBaseline = _currentSaveData == null ? null : ChangeSummaryLogic.Capture(_currentSaveData);
        _summaryAccountBaseline = _accountPanel.AccountData == null ? null : ChangeSummaryLogic.Capture(_accountPanel.AccountData);
    }

    private ChangeSummaryLogic.Report BuildChangeSummary()
    {
        if (_currentSaveData == null || _summarySaveBaseline == null) return new([], false);
        var report = ChangeSummaryLogic.Compare(ChangeSummaryLogic.Restore(_summarySaveBaseline), _currentSaveData);
        if (_summaryAccountBaseline != null && _accountPanel.AccountData != null && !report.Truncated)
        {
            var account = ChangeSummaryLogic.Compare(ChangeSummaryLogic.Restore(_summaryAccountBaseline), _accountPanel.AccountData,
                UiStrings.Get("summary.account"), Math.Max(0, 2000 - report.Changes.Count));
            report.Changes.AddRange(account.Changes);
            report = new(report.Changes, account.Truncated);
        }
        return report;
    }

    private bool ReviewBeforeSave()
    {
        var report = BuildChangeSummary();
        if (report.Changes.Count == 0 && !report.Truncated) return true;
        using var dialog = new ChangeSummaryDialog(report, beforeSave: true);
        return dialog.ShowDialog(this) == DialogResult.OK;
    }

    private void OnChangeSummary(object? sender, EventArgs e)
    {
        if (_currentSaveData == null) return;
        try
        {
            ActiveControl = null;
            SyncAllPanelData();
            using var dialog = new ChangeSummaryDialog(BuildChangeSummary(), beforeSave: false);
            dialog.ShowDialog(this);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, UiStrings.Get("dialog.error")); }
    }

    private void OnHotkeys(object? sender, EventArgs e)
    {
        if (_currentSaveData == null) return;
        try
        {
            ActiveControl = null;
            SyncAllPanelData();
            using var dialog = new HotkeyDialog(HotkeyLogic.ReadDraft(_currentSaveData));
            if (dialog.ShowDialog(this) == DialogResult.OK && HotkeyLogic.Apply(_currentSaveData, dialog.Draft))
            {
                _hasUnsavedChanges = true;
                _rawJsonPanel.NotifyDataChanged();
                if (_loadedTabIndices.Contains(14)) _rawJsonPanel.RefreshTree(_currentSaveData);
            }
        }
        catch (Exception ex) { MessageBox.Show(this, UiStrings.GetOrNull(ex.Message) ?? ex.Message, UiStrings.Get("dialog.error")); }
    }

    private void OnInventorySearch(object? sender, EventArgs e)
    {
        if (_currentSaveData == null) return;
        try
        {
            ActiveControl = null;
            SyncAllPanelData();
            using var dialog = new InventorySearchDialog(InventorySearchLogic.Index(_currentSaveData, _database));
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedResult is not { } result) return;
            _tabControl.SelectedIndex = result.Location.Tab;
            switch (result.Location.Collection)
            {
                case "ShipOwnership": _shipPanel.SelectSearchShip(result.Location.Index); break;
                case "Multitools": _multitoolPanel.SelectSearchTool(result.Location.Index); break;
                case "VehicleOwnership": _vehiclePanel.SelectSearchVehicle(result.Location.Index); break;
            }
            if (result.Location.Tab == 7) _basePanel.PrepareInventorySearch(result.Location.Inventory);
            var content = GetTabContent(_tabControl.SelectedTab);
            foreach (var grid in Descendants(content).OfType<InventoryGridPanel>())
            {
                if (!grid.HasInventory(result.Location.Inventory)) continue;
                // Reveal nested inventory tabs from outside in before focusing the slot.
                var ancestors = new List<TabPage>();
                for (Control? parent = grid.Parent; parent != null; parent = parent.Parent)
                    if (parent is TabPage page) ancestors.Add(page);
                ancestors.Reverse();
                foreach (var page in ancestors)
                    if (page.Parent is TabControl tabs) tabs.SelectedTab = page;
                if (grid.FocusSearchSlot(result.Location.Inventory, result.X, result.Y)) return;
            }
            MessageBox.Show(this, UiStrings.Get("tools_plus.slot_unavailable"), UiStrings.Get("tools_plus.search"));
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, UiStrings.Get("dialog.error")); }
    }

    private static IEnumerable<Control> Descendants(Control? parent)
    {
        if (parent == null) yield break;
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
