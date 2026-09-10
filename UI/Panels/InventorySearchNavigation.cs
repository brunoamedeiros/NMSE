using NMSE.Core;
using NMSE.Models;

namespace NMSE.UI.Panels;

public partial class InventoryGridPanel
{
    internal bool HasInventory(JsonObject inventory) => ReferenceEquals(_currentInventory, inventory);
    internal bool FocusSearchSlot(JsonObject inventory, int x, int y)
    {
        if (!HasInventory(inventory)) return false;
        var cell = _cells.FirstOrDefault(c => c.GridX == x && c.GridY == y);
        if (cell == null) return false;
        SelectCell(cell);
        _gridContainer.ScrollControlIntoView(cell);
        cell.Focus();
        return true;
    }
}

public partial class StarshipPanel
{
    internal void SelectSearchShip(int index)
    {
        for (int i = 0; i < _shipSelector.Items.Count; i++)
            if (_shipSelector.Items[i] is StarshipLogic.ShipListItem item && item.DataIndex == index)
            { _shipSelector.SelectedIndex = i; return; }
    }
}

public partial class MultitoolPanel
{
    internal void SelectSearchTool(int index)
    {
        for (int i = 0; i < _toolSelector.Items.Count; i++)
            if (_toolSelector.Items[i] is MultitoolLogic.ToolListItem item && item.DataIndex == index)
            { _toolSelector.SelectedIndex = i; return; }
    }
}

public partial class ExocraftPanel
{
    internal void SelectSearchVehicle(int index)
    {
        int selected = _addedVehicleIndices.IndexOf(index);
        if (selected >= 0) _vehicleSelector.SelectedIndex = selected;
    }
}
