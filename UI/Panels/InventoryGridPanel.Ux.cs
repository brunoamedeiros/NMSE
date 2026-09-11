using NMSE.Core;
using NMSE.Data;

namespace NMSE.UI.Panels;

public partial class InventoryGridPanel
{
    private Label _selectionTitle = null!;
    private Label _selectionSummary = null!;
    private Label _selectionDescription = null!;
    private PictureBox _selectionIcon = null!;
    private Button _editSelectedButton = null!;
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
        _editSelectedButton = new Button { AutoSize = true, Dock = DockStyle.Top, MinimumSize = new Size(0, 32) };
        _editSelectedButton.Click += (_, _) => EditSelectedSlot();
        _replaceSelectedButton = new Button { AutoSize = true, Dock = DockStyle.Top, MinimumSize = new Size(0, 32) };
        _replaceSelectedButton.Click += (_, _) => { _contextCell = _selectedCell; OnAddItem(this, EventArgs.Empty); };
        _removeSelectedButton = new Button { AutoSize = true, Dock = DockStyle.Top, MinimumSize = new Size(0, 32) };
        _removeSelectedButton.Click += (_, _) => { _contextCell = _selectedCell; OnRemoveItem(this, EventArgs.Empty); };
        _advancedToggle = new CheckBox { Appearance = Appearance.Button, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 18, 0, 4) };
        _advancedToggle.CheckedChanged += (_, _) => _advancedPanel.Visible = _advancedToggle.Checked;
        foreach (var control in new Control[] { _selectionIcon, _selectionTitle, _selectionSummary,
            _editSelectedButton, _replaceSelectedButton, _removeSelectedButton, _selectionDescription, _advancedToggle, _advancedPanel })
            layout.Controls.Add(control);
        _detailPanel.Controls.Add(layout);
        _actionFeedback = new Label { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6), Visible = false };
        ((SplitContainer)_gridContainer.Parent!.Parent!).Panel1.Controls.Add(_actionFeedback);
        _actionFeedback.BringToFront();
        Disposed += (_, _) => _feedbackTimer?.Dispose();
        RefreshSelectionUx();
    }

    private void RefreshSelectionUx()
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
            ? UiStrings.Format("inventory_ux.amount_summary", cell!.Amount, cell.MaxAmount)
            : UiStrings.Get(usable ? "inventory_ux.empty_hint" : "inventory_ux.select_hint");
        _selectionDescription.Text = hasItem ? System.Text.RegularExpressions.Regex.Replace(_detailDescription.Text, "<[^>]*>", "") : "";
        _editSelectedButton.Text = UiStrings.Get("inventory_ux.edit");
        _editSelectedButton.Visible = hasItem;
        _replaceSelectedButton.Text = UiStrings.Get(hasItem ? "inventory.ctx_replace_item" : "inventory.ctx_add_item");
        _replaceSelectedButton.Enabled = usable;
        _removeSelectedButton.Text = UiStrings.Get("inventory.ctx_remove_item");
        _removeSelectedButton.Visible = hasItem;
        _advancedToggle.Text = UiStrings.Get("inventory_ux.advanced");
    }

    private void EditSelectedSlot()
    {
        var cell = _selectedCell;
        if (cell == null) return;
        if (cell.SlotData == null || cell.IsValidEmpty || string.IsNullOrWhiteSpace(cell.ItemId))
        { _contextCell = cell; OnAddItem(this, EventArgs.Empty); return; }
        SelectCell(cell);
        using var dialog = new InventorySlotEditDialog(cell.DisplayName ?? cell.ItemId, cell.Amount, cell.MaxAmount, cell.IsTechChargeable);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        if (dialog.ReplaceRequested) { _contextCell = cell; OnAddItem(this, EventArgs.Empty); return; }
        _detailAmount.NumericValue = dialog.Amount;
        OnApplyChanges(this, EventArgs.Empty);
        ShowInventoryFeedback(UiStrings.Format("inventory_ux.item_updated", dialog.Amount, cell.DisplayName ?? cell.ItemId, _inventoryLocationLabel));
    }

    private void OnSlotDoubleClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || sender is not SlotCell cell) return;
        SelectCell(cell);
        EditSelectedSlot();
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
