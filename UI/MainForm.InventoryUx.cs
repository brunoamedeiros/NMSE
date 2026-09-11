using NMSE.Core;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Panels;

namespace NMSE.UI;

public partial class MainFormResources
{
    private readonly InventoryEditHistory _inventoryHistory = new();
    private readonly HashSet<int> _otherDirtyTabs = new();
    private JsonObject? _inventoryUxRoot;
    private bool _refreshingInventoryUx;
    private ToolStripMenuItem _undoInventoryMenu = null!;
    private ToolStripMenuItem _redoInventoryMenu = null!;
    private ToolStripButton _undoInventoryButton = null!;
    private ToolStripButton _redoInventoryButton = null!;
    private ToolStripLabel _saveStateLabel = null!;

    private void InitializeInventoryUx()
    {
        _undoInventoryMenu = new ToolStripMenuItem("", null, (_, _) => UndoInventory(false)) { ShortcutKeyDisplayString = "Ctrl+Z" };
        _redoInventoryMenu = new ToolStripMenuItem("", null, (_, _) => UndoInventory(true)) { ShortcutKeyDisplayString = "Ctrl+Y" };
        var edit = (ToolStripMenuItem)_menuStrip.Items[1];
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.AddRange([_undoInventoryMenu, _redoInventoryMenu]);
        _undoInventoryButton = new ToolStripButton("", null, (_, _) => UndoInventory(false));
        _redoInventoryButton = new ToolStripButton("", null, (_, _) => UndoInventory(true));
        _saveStateLabel = new ToolStripLabel();
        _toolStrip2.Items.Add(new ToolStripSeparator());
        _toolStrip2.Items.AddRange([_undoInventoryButton, _redoInventoryButton]);
        _statusStrip.Items.Insert(2, _saveStateLabel);
        _statusStrip.ShowItemToolTips = true;
        RefreshInventoryUx();
    }

    private void CaptureInventoryUxBaseline()
    {
        if (_currentSaveData == null) return;
        var inventories = InventorySearchLogic.Enumerate(_currentSaveData).Select(x => x.Inventory).ToArray();
        if (!ReferenceEquals(_inventoryUxRoot, _currentSaveData))
            _inventoryHistory.Reset(inventories);
        else
            _inventoryHistory.MarkSaved(inventories);
        _inventoryUxRoot = _currentSaveData;
        _otherDirtyTabs.Clear();
        _hasUnsavedChanges = false;
        RefreshInventoryUx();
    }

    private void OnEditorDataModified(object? sender, EventArgs e)
    {
        if (_refreshingInventoryUx || _currentSaveData == null || !ReferenceEquals(_inventoryUxRoot, _currentSaveData)) return;
        _hasUnsavedChanges = true;
        int tab = sender switch
        {
            MainStatsPanel => 0, ExosuitPanel => 1, MultitoolPanel => 2, StarshipPanel => 3,
            FleetPanel => 4, ExocraftPanel => 5, CompanionPanel => 6, BasePanel => 7,
            CataloguePanel => 8, MilestonePanel => 9, SettlementPanel => 10, ByteBeatPanel => 11,
            AccountPanel => 12, RawJsonPanel => 14, _ => _tabControl.SelectedIndex
        };
        bool inventoryChanged = _inventoryHistory.Record(InventorySearchLogic.Enumerate(_currentSaveData).Select(x => x.Inventory),
            UiStrings.Get("inventory_ux.inventory_edit"));
        if ((!inventoryChanged || sender is RawJsonPanel or CompanionPanel or MainStatsPanel) && tab >= 0) _otherDirtyTabs.Add(tab);
        _rawJsonPanel.NotifyDataChanged();
        RefreshInventoryUx();
    }

    private void NotifyExternalInventoryEdit(string label)
    {
        if (_currentSaveData == null) return;
        _hasUnsavedChanges = true;
        _inventoryHistory.Record(InventorySearchLogic.Enumerate(_currentSaveData).Select(x => x.Inventory), label);
        RefreshInventoryGrids();
        var report = BuildChangeSummary();
        _hasUnsavedChanges = report.Changes.Count > 0 || report.Truncated;
        if (!_hasUnsavedChanges) _otherDirtyTabs.Clear();
        else if (_tabControl.SelectedIndex >= 0) _otherDirtyTabs.Add(_tabControl.SelectedIndex);
        RefreshInventoryUx();
    }

    private void ResetInventoryUxForImport()
    {
        if (_currentSaveData == null) return;
        _inventoryHistory.Reset(InventorySearchLogic.Enumerate(_currentSaveData).Select(x => x.Inventory));
        _inventoryUxRoot = _currentSaveData;
        _otherDirtyTabs.Clear();
        _otherDirtyTabs.Add(14);
        _hasUnsavedChanges = true;
        RefreshInventoryUx();
    }

    private void UndoInventory(bool redo)
    {
        if (_currentSaveData == null) return;
        if (!PreservePendingRawText()) return;
        SyncAllPanelData();
        var affected = redo ? _inventoryHistory.Redo() : _inventoryHistory.Undo();
        if (affected.Count == 0) { RefreshInventoryUx(); return; }
        _refreshingInventoryUx = true;
        try
        {
            RefreshInventoryGrids();
            _rawJsonPanel.NotifyDataChanged();
            if (_loadedTabIndices.Contains(14)) _rawJsonPanel.RefreshTree(_currentSaveData);
            var report = BuildChangeSummary();
            _hasUnsavedChanges = report.Changes.Count > 0 || report.Truncated;
            if (!_hasUnsavedChanges) _otherDirtyTabs.Clear();
            RefreshInventoryUx();
            _statusLabel.Text = UiStrings.Get(redo ? "inventory_ux.redone" : "inventory_ux.undone");
        }
        finally { _refreshingInventoryUx = false; }
    }

    private bool PreservePendingRawText()
    {
        if (!_rawJsonPanel.HasPendingTextEdits) return true;
        MessageBox.Show(this, UiStrings.Get("inventory_ux.pending_raw_text"), UiStrings.Get("raw_json.title"),
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private void RefreshInventoryGrids()
    {
        // Restore keeps inventory roots alive; only their grids need reloading.
        foreach (var grid in Descendants(_tabControl).OfType<InventoryGridPanel>())
            grid.ReloadInventoryView();
    }

    private void RefreshInventoryUx()
    {
        if (_undoInventoryMenu == null) return;
        var locations = _currentSaveData == null ? [] : InventorySearchLogic.Enumerate(_currentSaveData);
        var dirtyTabs = new HashSet<int>(_otherDirtyTabs);
        var nestedTabs = Descendants(_tabControl).OfType<TabPage>().Where(p => p.Parent != _tabControl)
            .ToDictionary(p => p, _ => false);
        foreach (var location in locations)
            if (_inventoryHistory.HasChanges(location.Inventory)) dirtyTabs.Add(location.Tab);
        foreach (var grid in Descendants(_tabControl).OfType<InventoryGridPanel>())
        {
            var location = locations.FirstOrDefault(x => grid.HasInventory(x.Inventory));
            if (location == null) continue;
            var positions = _inventoryHistory.GetChangedPositions(location.Inventory);
            grid.UpdateEditIndicators(positions, location.Label);
            for (Control? c = grid.Parent; c != null && c != _tabControl; c = c.Parent)
                if (c is TabPage page && page.Parent != _tabControl)
                    nestedTabs[page] = nestedTabs.GetValueOrDefault(page) || _inventoryHistory.HasChanges(location.Inventory);
        }
        foreach (var (page, nestedDirty) in nestedTabs) SetDirtyTab(page, nestedDirty);
        for (int i = 0; i < _tabControl.TabCount; i++) SetDirtyTab(_tabControl.TabPages[i], dirtyTabs.Contains(i));
        _undoInventoryMenu.Text = UiStrings.Get("inventory_ux.undo");
        _redoInventoryMenu.Text = UiStrings.Get("inventory_ux.redo");
        _undoInventoryButton.Text = UiStrings.Get("inventory_ux.undo_short");
        _redoInventoryButton.Text = UiStrings.Get("inventory_ux.redo_short");
        _undoInventoryMenu.Enabled = _undoInventoryButton.Enabled = _currentSaveData != null && _inventoryHistory.CanUndo;
        _redoInventoryMenu.Enabled = _redoInventoryButton.Enabled = _currentSaveData != null && _inventoryHistory.CanRedo;
        _undoInventoryButton.ToolTipText = UiStrings.Get("inventory_ux.undo") + " (Ctrl+Z)";
        _redoInventoryButton.ToolTipText = UiStrings.Get("inventory_ux.redo") + " (Ctrl+Y)";
        bool dirty = dirtyTabs.Count > 0 || _hasUnsavedChanges;
        _saveStateLabel.Text = UiStrings.Get(_currentSaveData == null ? "inventory_ux.no_save" : dirty ? "inventory_ux.not_saved" : "inventory_ux.saved");
        _saveStateLabel.ToolTipText = UiStrings.Get("inventory_ux.marker_hint");
        bool dark = ThemeManager.Effective == AppTheme.Dark;
        _saveStateLabel.ForeColor = dirty ? (dark ? Color.Gold : Color.FromArgb(128, 64, 0)) : (dark ? Color.Gainsboro : SystemColors.ControlText);
    }

    private static void SetDirtyTab(TabPage page, bool dirty)
    {
        const string marker = " •";
        string title = page.Text.EndsWith(marker, StringComparison.Ordinal) ? page.Text[..^marker.Length] : page.Text;
        page.Text = title + (dirty ? marker : "");
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is (Keys.Control | Keys.Z) or (Keys.Control | Keys.Y))
        {
            // Raw JSON owns a separate undo history and handles these keys in its tree.
            if (_rawJsonPanel.ContainsFocus) return base.ProcessCmdKey(ref msg, keyData);
            Control? focused = Descendants(this).FirstOrDefault(c => c.Focused);
            if (focused is not TextBoxBase && focused is not NumericUpDown
                && (focused is not ComboBox combo || combo.DropDownStyle == ComboBoxStyle.DropDownList))
            { UndoInventory(keyData == (Keys.Control | Keys.Y)); return true; }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
