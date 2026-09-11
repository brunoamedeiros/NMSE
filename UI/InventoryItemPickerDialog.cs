using System.Globalization;
using NMSE.Data;

namespace NMSE.UI;

/// <summary>Choose one compatible item and its amount before changing an inventory slot.</summary>
internal sealed class InventoryItemPickerDialog : ToolsDialog
{
    internal GameItem? SelectedItem { get; private set; }
    internal int Amount { get; private set; }

    internal InventoryItemPickerDialog(string title, IEnumerable<GameItem> items,
        Func<GameItem, (int Minimum, int Maximum, int Initial)> amountLimits) : base(title)
    {
        var available = items.OrderBy(i => i.Name).ToArray();
        var search = new TextBox
        {
            Dock = DockStyle.Top,
            PlaceholderText = UiStrings.Get("inventory.search_placeholder"),
            AccessibleName = UiStrings.Get("inventory.label_search")
        };
        var count = new Label { Dock = DockStyle.Top, Height = 35, Padding = new Padding(0, 8, 0, 0) };
        var list = MakeList(("item_picker.col_name", 380), ("item_picker.col_category", 220), ("item_picker.col_id", 220));
        Controls.Add(list);
        Controls.Add(count);
        Controls.Add(search);

        var quantity = new NumericUpDown
        {
            Minimum = 1, Maximum = 1, Value = 1, Width = 120,
            Enabled = false, AccessibleName = UiStrings.Get("inventory.amount"),
            Margin = new Padding(8, 6, 8, 0)
        };
        CancelButton = AddButton("common.cancel", result: DialogResult.Cancel);
        var add = AddButton(title, (_, _) =>
        {
            if (list.SelectedItems.Count != 1) return;
            SelectedItem = (GameItem)list.SelectedItems[0].Tag!;
            Amount = (int)quantity.Value;
            DialogResult = DialogResult.OK;
        });
        add.Enabled = false;
        AcceptButton = add;
        Footer.Controls.Add(quantity);
        Footer.Controls.Add(new Label
        {
            Text = UiStrings.Get("inventory.amount"), AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        });

        list.SelectedIndexChanged += (_, _) =>
        {
            add.Enabled = quantity.Enabled = list.SelectedItems.Count == 1;
            if (!add.Enabled) return;
            var limits = amountLimits((GameItem)list.SelectedItems[0].Tag!);
            quantity.Minimum = 0;
            quantity.Maximum = limits.Maximum;
            quantity.Minimum = limits.Minimum;
            quantity.Value = Math.Clamp(limits.Initial, limits.Minimum, limits.Maximum);
        };

        void RefreshResults()
        {
            string query = search.Text.Trim().TrimStart('^').Trim();
            bool Matches(string value) => CultureInfo.CurrentCulture.CompareInfo.IndexOf(
                value, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var item in available.Where(i => Matches(i.Name) || Matches(i.Id) || Matches(i.Category)))
                list.Items.Add(new ListViewItem([item.Name, item.Category, item.Id]) { Tag = item });
            list.EndUpdate();
            add.Enabled = quantity.Enabled = false;
            count.Text = list.Items.Count == 0
                ? UiStrings.Get("inventory.picker_no_matches")
                : UiStrings.Format("inventory.picker_match_count", list.Items.Count);
        }
        search.TextChanged += (_, _) => RefreshResults();
        RefreshResults();
        Shown += (_, _) => search.Focus();
    }
}
