using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.UI.Panels;

public partial class InventoryGridPanel
{
    private Label _selectionTitle = null!;
    private Label _selectionSummary = null!;
    private Label _selectionDescription = null!;
    private PictureBox _selectionIcon = null!;
    private TableLayoutPanel _amountEditor = null!;
    private NumericUpDown _selectionAmount = null!;
    private Button _applyAmountButton = null!;
    private Button _rechargeSelectedButton = null!;
    private bool _refreshingSelectionAmount;
    private SlotCell? _amountEditingCell;
    private JsonObject? _amountEditingSlot;
    private int _loadedSelectionAmount;
    private int _loadedSelectionMaximum;
    private string? _loadedSelectionItemId;
    private Button _replaceSelectedButton = null!;
    private Button _removeSelectedButton = null!;
    private CheckBox _advancedToggle = null!;
    private Panel _advancedPanel = null!;
    private Label _actionFeedback = null!;
    private string _inventoryLocationLabel = "";
    private HashSet<(int X, int Y)> _pendingPositions = new();
    private System.Windows.Forms.Timer? _feedbackTimer;

    private void InitializeInventoryUx()
    {
        var original = _detailPanel.Controls.OfType<TableLayoutPanel>().Single();
        _detailPanel.Controls.Remove(original);
        _advancedPanel = new Panel { Dock = DockStyle.Top, AutoSize = true, Visible = false };
        _advancedPanel.Controls.Add(original);
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(4) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _selectionIcon = new PictureBox { Size = new Size(80, 80), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 6, 0, 8) };
        _selectionTitle = new Label { AutoSize = true, MaximumSize = new Size(250, 0), Margin = new Padding(0, 5, 0, 8) };
        _selectionSummary = new Label { AutoSize = true, MaximumSize = new Size(250, 0), Margin = new Padding(0, 0, 0, 10) };
        _selectionDescription = new Label { AutoSize = true, AutoEllipsis = true, MaximumSize = new Size(250, 160), Margin = new Padding(0, 12, 0, 12) };
        _amountEditor = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 4)
        };
        _amountEditor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _amountEditor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _selectionAmount = new NumericUpDown
        {
            Dock = DockStyle.Fill, ThousandsSeparator = true, Minimum = int.MinValue, Maximum = int.MaxValue,
            Margin = new Padding(0, 4, 6, 4)
        };
        _applyAmountButton = new Button { AutoSize = true, MinimumSize = new Size(70, 28), Margin = Padding.Empty };
        _applyAmountButton.Click += (_, _) => ApplyInlineAmount();
        // Do not parse on every keystroke or write to the save on focus loss.
        _selectionAmount.TextChanged += (_, _) =>
        {
            if (!_refreshingSelectionAmount && _amountEditingCell != null)
                _applyAmountButton.Enabled = true;
        };
        _selectionAmount.ValueChanged += (_, _) =>
        {
            if (!_refreshingSelectionAmount && _amountEditingCell != null)
                _applyAmountButton.Enabled = true;
        };
        _selectionAmount.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && e.Modifiers == Keys.None)
            {
                ApplyInlineAmount();
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape && e.Modifiers == Keys.None)
            {
                RefreshSelectionUx(resetAmount: true);
                e.SuppressKeyPress = true;
            }
        };
        _amountEditor.Controls.Add(_selectionAmount, 0, 0);
        _amountEditor.Controls.Add(_applyAmountButton, 1, 0);
        _rechargeSelectedButton = new Button { AutoSize = true, Dock = DockStyle.Top, MinimumSize = new Size(0, 32) };
        _rechargeSelectedButton.Click += (_, _) =>
        {
            if (_selectedCell is { IsTechChargeable: true } cell && ReferenceEquals(cell, _amountEditingCell))
                _selectionAmount.Value = cell.MaxAmount;
        };
        _replaceSelectedButton = new Button { AutoSize = true, Dock = DockStyle.Top, MinimumSize = new Size(0, 32) };
        _replaceSelectedButton.Click += (_, _) => { _contextCell = _selectedCell; OnAddItem(this, EventArgs.Empty); };
        _removeSelectedButton = new Button { AutoSize = true, Dock = DockStyle.Top, MinimumSize = new Size(0, 32) };
        _removeSelectedButton.Click += (_, _) => { _contextCell = _selectedCell; OnRemoveItem(this, EventArgs.Empty); };
        _advancedToggle = new CheckBox { Appearance = Appearance.Button, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 18, 0, 4) };
        _advancedToggle.CheckedChanged += (_, _) => _advancedPanel.Visible = _advancedToggle.Checked;
        foreach (var control in new Control[] { _selectionIcon, _selectionTitle, _selectionSummary,
            _amountEditor, _rechargeSelectedButton, _replaceSelectedButton, _removeSelectedButton, _selectionDescription, _advancedToggle, _advancedPanel })
            layout.Controls.Add(control);
        _detailPanel.Controls.Add(layout);
        _actionFeedback = new Label { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6), Visible = false };
        ((SplitContainer)_gridContainer.Parent!.Parent!).Panel1.Controls.Add(_actionFeedback);
        _actionFeedback.BringToFront();
        Disposed += (_, _) => _feedbackTimer?.Dispose();
        RefreshSelectionUx();
    }

    private void RefreshSelectionUx(bool resetAmount = false)
    {
        if (_selectionTitle == null) return;
        var cell = _selectedCell;
        bool hasItem = cell?.SlotData != null && !string.IsNullOrEmpty(cell.ItemId) && !cell.IsValidEmpty;
        bool usable = cell != null && (cell.IsActivated || cell.IsValidEmpty || hasItem);
        _selectionTitle.Text = hasItem ? cell!.DisplayName ?? cell.ItemId
            : UiStrings.Get(cell == null ? "inventory_ux.select_slot" : usable ? "inventory.empty_slot" : "inventory_ux.locked_slot");
        _selectionIcon.Image = hasItem ? cell!.IconImage : null;
        _selectionIcon.Visible = hasItem;
        _selectionSummary.Text = hasItem
            ? UiStrings.Format(cell!.IsTechChargeable ? "inventory_ux.charge_summary" : "inventory_ux.amount_summary", cell.Amount, cell.MaxAmount)
            : UiStrings.Get(usable ? "inventory_ux.empty_hint" : "inventory_ux.select_hint");
        _selectionDescription.Text = hasItem ? System.Text.RegularExpressions.Regex.Replace(_detailDescription.Text, "<[^>]*>", "") : "";
        _refreshingSelectionAmount = true;
        try
        {
            bool reloadAmount = resetAmount || !hasItem || !ReferenceEquals(cell, _amountEditingCell)
                || !ReferenceEquals(cell!.SlotData, _amountEditingSlot)
                || !string.Equals(cell.ItemId, _loadedSelectionItemId, StringComparison.Ordinal)
                || cell.Amount != _loadedSelectionAmount || cell.MaxAmount != _loadedSelectionMaximum;
            _amountEditingCell = hasItem ? cell : null;
            _amountEditingSlot = hasItem ? cell!.SlotData : null;
            _amountEditor.Visible = hasItem;
            _selectionAmount.Enabled = hasItem;
            _selectionAmount.AccessibleName = UiStrings.Get(hasItem && cell!.IsTechChargeable ? "inventory.charge" : "inventory.amount");
            if (reloadAmount)
            {
                _loadedSelectionAmount = hasItem ? cell!.Amount : 0;
                _loadedSelectionMaximum = hasItem ? cell!.MaxAmount : 0;
                _loadedSelectionItemId = hasItem ? cell!.ItemId : null;
                _selectionAmount.Minimum = int.MinValue;
                _selectionAmount.Maximum = int.MaxValue;
                _selectionAmount.Value = _loadedSelectionAmount;
                // Preserve existing unusual amounts while keeping ordinary edits within the stack limit.
                _selectionAmount.Minimum = Math.Min(0, _loadedSelectionAmount);
                _selectionAmount.Maximum = Math.Max(Math.Max(_loadedSelectionAmount, _loadedSelectionMaximum), 1);
                _applyAmountButton.Enabled = false;
            }
            _applyAmountButton.Text = UiStrings.Get("common.apply");
            _sharedToolTip.SetToolTip(_selectionAmount, UiStrings.Get("inventory_ux.amount_hint"));
            _sharedToolTip.SetToolTip(_applyAmountButton, UiStrings.Get("inventory_ux.amount_hint"));
        }
        finally { _refreshingSelectionAmount = false; }
        _rechargeSelectedButton.Text = UiStrings.Get("inventory_ux.recharge");
        _rechargeSelectedButton.Visible = hasItem && cell!.IsTechChargeable;
        _replaceSelectedButton.Text = UiStrings.Get(hasItem ? "inventory.ctx_replace_item" : "inventory.ctx_add_item");
        _replaceSelectedButton.Enabled = usable;
        _removeSelectedButton.Text = UiStrings.Get("inventory.ctx_remove_item");
        _removeSelectedButton.Visible = hasItem;
        _advancedToggle.Text = UiStrings.Get("inventory_ux.advanced");
    }

    private void FocusInlineAmount()
    {
        var cell = _selectedCell;
        if (cell == null) return;
        if (cell.SlotData == null || cell.IsValidEmpty || string.IsNullOrWhiteSpace(cell.ItemId))
        { _contextCell = cell; OnAddItem(this, EventArgs.Empty); return; }
        _detailPanel.ScrollControlIntoView(_amountEditor);
        _selectionAmount.Focus();
        _selectionAmount.Select(0, _selectionAmount.Text.Length);
    }

    private void ApplyInlineAmount()
    {
        var cell = _selectedCell;
        if (cell?.SlotData == null || _slots == null || cell.IsValidEmpty || string.IsNullOrWhiteSpace(cell.ItemId)
            || !ReferenceEquals(cell, _amountEditingCell) || !ReferenceEquals(cell.SlotData, _amountEditingSlot)) return;
        // Reading Value commits the NumericUpDown's pending text and applies its bounds.
        int amount = (int)_selectionAmount.Value;
        if (amount == cell.Amount) { RefreshSelectionUx(resetAmount: true); return; }

        // Amount edits must not apply pending Advanced fields or reconstruct binary item IDs.
        RawNumberGuard.SetInt(cell.SlotData, "Amount", amount);
        cell.Amount = amount;
        _detailAmount.NumericValue = amount;
        cell.UpdateDisplay();
        RaiseDataModified();
        string location = _inventoryLocationLabel.Length > 0 ? _inventoryLocationLabel : UiStrings.Get("inventory_ux.inventory");
        ShowInventoryFeedback(UiStrings.Format("inventory_ux.item_updated", amount, cell.DisplayName ?? cell.ItemId, location));
    }

    private void OnSlotDoubleClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || sender is not SlotCell cell) return;
        SelectCell(cell);
        FocusInlineAmount();
    }

    internal void UpdateEditIndicators(IReadOnlySet<(int X, int Y)> positions, string location)
    {
        _pendingPositions = positions.ToHashSet();
        _inventoryLocationLabel = location;
        foreach (var cell in _cells)
        {
            cell.HasPendingEdit = positions.Contains((cell.GridX, cell.GridY));
            cell.Invalidate();
        }
    }

    internal void ReloadInventoryView()
    {
        if (_currentInventory == null) return;
        var position = _selectedCell == null ? ((int X, int Y)?)null : (_selectedCell.GridX, _selectedCell.GridY);
        LoadInventory(_currentInventory);
        if (position is { } p && _cells.FirstOrDefault(c => c.GridX == p.X && c.GridY == p.Y) is { } cell)
            SelectCell(cell);
    }

    internal void ShowInventoryFeedback(string message)
    {
        _actionFeedback.Text = message;
        _actionFeedback.Visible = true;
        if (_selectedCell != null) { _selectedCell.FlashEdit = true; _selectedCell.Invalidate(); }
        _feedbackTimer ??= new System.Windows.Forms.Timer { Interval = 2400 };
        _feedbackTimer.Stop();
        _feedbackTimer.Tick -= EndFeedback;
        _feedbackTimer.Tick += EndFeedback;
        _feedbackTimer.Start();
    }

    private void EndFeedback(object? sender, EventArgs e)
    {
        _feedbackTimer?.Stop();
        foreach (var cell in _cells) { cell.FlashEdit = false; cell.Invalidate(); }
        _actionFeedback.Visible = false;
    }

    private void InventoryUxDataModified()
    {
        RefreshSelectionUx();
        string location = _inventoryLocationLabel.Length > 0 ? _inventoryLocationLabel : UiStrings.Get("inventory_ux.inventory");
        ShowInventoryFeedback(UiStrings.Format("inventory_ux.applied", location));
    }
}
