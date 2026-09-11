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
        CaptureInventoryUxBaseline();
    }

    private ChangeSummaryLogic.Report BuildChangeSummary()
    {
        if (_currentSaveData == null || _summarySaveBaseline == null) return new([], false);
        var report = ChangeSummaryLogic.Compare(ChangeSummaryLogic.Restore(_summarySaveBaseline), _currentSaveData);
        if (_summaryAccountBaseline != null && _accountPanel.AccountData != null && !report.Truncated)
        {
            var account = ChangeSummaryLogic.Compare(ChangeSummaryLogic.Restore(_summaryAccountBaseline), _accountPanel.AccountData,
                UiStrings.Get("summary.account"), Math.Max(0, 2000 - report.Changes.Count), ChangeReviewLogic.DataScope.Account);
            report.Changes.AddRange(account.Changes);
            report = new(report.Changes, account.Truncated);
        }
        return report;
    }

    private bool ReviewBeforeSave()
    {
        if (!PreservePendingRawText()) return false;
        var report = BuildChangeSummary();
        if (report.Changes.Count == 0 && !report.Truncated) return true;
        using var dialog = new ChangeSummaryDialog(report, beforeSave: true);
        if (dialog.ShowDialog(this) == DialogResult.OK) return true;
        HandleChangeReviewAction(dialog);
        return false;
    }

    private void OnChangeSummary(object? sender, EventArgs e)
    {
        if (_currentSaveData == null) return;
        if (!PreservePendingRawText()) return;
        try
        {
            ActiveControl = null;
            SyncAllPanelData();
            using var dialog = new ChangeSummaryDialog(BuildChangeSummary(), beforeSave: false);
            dialog.ShowDialog(this);
            HandleChangeReviewAction(dialog);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, UiStrings.Get("dialog.error")); }
    }

    private void HandleChangeReviewAction(ChangeSummaryDialog dialog)
    {
        if (_currentSaveData == null || dialog.SelectedChange is not { } change) return;
        if (!PreservePendingRawText()) return;
        if (dialog.RequestedAction == ChangeSummaryDialog.ReviewAction.Revert)
        {
            var data = change.Scope == ChangeReviewLogic.DataScope.Account ? _accountPanel.AccountData : _currentSaveData;
            if (data == null || !ChangeReviewLogic.TryRevert(data, change, out _))
            {
                MessageBox.Show(this, UiStrings.Get("summary.stale"), UiStrings.Get("tools_plus.summary"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            RefreshLoadedPanels();
            NotifyExternalInventoryEdit(UiStrings.Format("summary.reverted", change.Field));
        }
        if (dialog.RequestedAction is ChangeSummaryDialog.ReviewAction.Open or ChangeSummaryDialog.ReviewAction.Revert)
        {
            if (ChangeReviewLogic.TryFindInventory(_currentSaveData, change, out var location, out int? x, out int? y)
                && location != null && OpenInventoryLocation(location, x, y)) return;
            _isGoToJsonNavigation = true;
            try { _tabControl.SelectedIndex = 14; }
            finally { _isGoToJsonNavigation = false; }
            _rawJsonPanel.NavigateToReviewChange(_currentSaveData, _accountPanel.AccountData, _accountPanel.AccountFilePath, change);
        }
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
                OnEditorDataModified(_mainStatsPanel, EventArgs.Empty);
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
            if (OpenInventoryLocation(result.Location, result.X, result.Y)) return;
            MessageBox.Show(this, UiStrings.Get("tools_plus.slot_unavailable"), UiStrings.Get("tools_plus.search"));
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, UiStrings.Get("dialog.error")); }
    }

    private bool OpenInventoryLocation(InventorySearchLogic.InventoryLocation location, int? x = null, int? y = null)
    {
        _tabControl.SelectedIndex = location.Tab;
        switch (location.Collection)
        {
            case "ShipOwnership": _shipPanel.SelectSearchShip(location.Index); break;
            case "Multitools": _multitoolPanel.SelectSearchTool(location.Index); break;
            case "VehicleOwnership": _vehiclePanel.SelectSearchVehicle(location.Index); break;
        }
        if (location.Tab == 7) _basePanel.PrepareInventorySearch(location.Inventory);
        foreach (var grid in Descendants(GetTabContent(_tabControl.SelectedTab)).OfType<InventoryGridPanel>())
        {
            if (!grid.HasInventory(location.Inventory)) continue;
            var ancestors = new List<TabPage>();
            for (Control? parent = grid.Parent; parent != null; parent = parent.Parent)
                if (parent is TabPage page) ancestors.Add(page);
            ancestors.Reverse();
            foreach (var page in ancestors)
                if (page.Parent is TabControl tabs) tabs.SelectedTab = page;
            if (x.HasValue && y.HasValue) return grid.FocusSearchSlot(location.Inventory, x.Value, y.Value);
            grid.Focus();
            return true;
        }
        return false;
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
