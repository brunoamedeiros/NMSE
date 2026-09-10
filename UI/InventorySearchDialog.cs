using NMSE.Core;
using NMSE.Data;

namespace NMSE.UI;

internal sealed class InventorySearchDialog : ToolsDialog
{
    internal InventorySearchLogic.Result? SelectedResult { get; private set; }

    internal InventorySearchDialog(List<InventorySearchLogic.Result> index) : base("tools_plus.search")
    {
        var search = new TextBox { Dock = DockStyle.Top, PlaceholderText = UiStrings.Get("tools_plus.search_hint"), AccessibleName = UiStrings.Get("tools_plus.search") };
        var count = new Label { Dock = DockStyle.Top, Height = 35, Padding = new Padding(0, 8, 0, 0) };
        var list = MakeList(("tools_plus.item", 230), ("tools_plus.location", 350), ("tools_plus.amount", 95), ("tools_plus.slot", 110), ("tools_plus.id", 180));
        Controls.Add(list);
        Controls.Add(count);
        Controls.Add(search);
        CancelButton = AddButton("tools_plus.close", result: DialogResult.Cancel);
        var open = AddButton("tools_plus.open_slot", (_, _) => OpenSelected());
        open.Enabled = false;
        AcceptButton = open;
        list.SelectedIndexChanged += (_, _) => open.Enabled = list.SelectedItems.Count > 0;
        list.DoubleClick += (_, _) => OpenSelected();
        void OpenSelected()
        {
            if (list.SelectedItems.Count == 0) return;
            SelectedResult = (InventorySearchLogic.Result)list.SelectedItems[0].Tag!;
            DialogResult = DialogResult.OK;
        }
        void RefreshResults()
        {
            var matches = InventorySearchLogic.Search(index, search.Text).ToList();
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var item in matches)
                list.Items.Add(new ListViewItem([item.Name, item.Location.Label, item.Amount.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), $"{item.X + 1}, {item.Y + 1}", item.ItemId]) { Tag = item });
            list.EndUpdate();
            count.Text = UiStrings.Format("tools_plus.results", matches.Count);
        }
        search.TextChanged += (_, _) => RefreshResults();
        RefreshResults();
        Shown += (_, _) => search.Focus();
    }
}
