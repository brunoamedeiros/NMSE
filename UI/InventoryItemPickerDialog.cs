using System.Globalization;
using NMSE.Config;
using NMSE.Core;
using NMSE.Data;

namespace NMSE.UI;

/// <summary>Choose one compatible item and its amount before changing an inventory slot.</summary>
internal sealed class InventoryItemPickerDialog : ToolsDialog
{
    internal GameItem? SelectedItem { get; private set; }
    internal int Amount { get; private set; }

    internal InventoryItemPickerDialog(string title, IEnumerable<GameItem> items,
        Func<GameItem, (int Minimum, int Maximum, int Initial)> amountLimits,
        IconManager? icons = null, bool isTechInventory = false, bool isCargoInventory = true) : base(title)
    {
        var available = items.OrderBy(i => i.Name).ToArray();
        var recentItems = ItemPickerRecentItems.GetCompatible(AppConfig.Instance, available);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 166));
        var search = new TextBox
        {
            Dock = DockStyle.Top,
            PlaceholderText = UiStrings.Get("inventory.search_placeholder"),
            AccessibleName = UiStrings.Get("inventory.label_search"),
            Margin = new Padding(0, 0, 0, 6)
        };
        var recentOnly = new CheckBox
        {
            Text = UiStrings.Get("item_picker.recent_only"),
            AutoSize = true, Margin = new Padding(0, 0, 0, 6)
        };
        var compatibility = new Label
        {
            Text = UiStrings.Get(isTechInventory ? "item_picker.compatibility_technology"
                : isCargoInventory ? "item_picker.compatibility_cargo" : "item_picker.compatibility_general"),
            AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6)
        };
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = Padding.Empty };
        var count = new Label { AutoSize = true, Margin = new Padding(16, 1, 0, 6) };
        filters.Controls.Add(recentOnly);
        filters.Controls.Add(count);
        var list = MakeList(("item_picker.col_name", 350), ("item_picker.col_category", 190), ("item_picker.col_id", 180));
        list.Margin = Padding.Empty;
        var preview = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3,
            Padding = new Padding(0, 10, 0, 0), Margin = Padding.Empty
        };
        preview.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        preview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        preview.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        preview.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        preview.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var icon = new PictureBox
        {
            Dock = DockStyle.Top, Height = 96, SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(0, 0, 8, 0), TabStop = false
        };
        var itemName = new Label
        {
            Text = UiStrings.Get("item_picker.select_to_preview"), AutoSize = true,
            Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 4)
        };
        var itemSubtitle = new Label { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 5) };
        var description = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None,
            ScrollBars = ScrollBars.Vertical, WordWrap = true, Margin = Padding.Empty,
            AccessibleName = UiStrings.Get("item_picker.description")
        };
        preview.Controls.Add(icon, 0, 0);
        preview.SetRowSpan(icon, 3);
        preview.Controls.Add(itemName, 1, 0);
        preview.Controls.Add(itemSubtitle, 1, 1);
        preview.Controls.Add(description, 1, 2);
        layout.Controls.Add(search, 0, 0);
        layout.Controls.Add(filters, 0, 1);
        layout.Controls.Add(compatibility, 0, 2);
        layout.Controls.Add(list, 0, 3);
        layout.Controls.Add(preview, 0, 4);
        Controls.Add(layout);

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
            ItemPickerRecentItems.Remember(AppConfig.Instance, SelectedItem.Id);
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

        void ClearSelection()
        {
            add.Enabled = quantity.Enabled = false;
            icon.Image = null;
            itemName.Text = UiStrings.Get("item_picker.select_to_preview");
            itemSubtitle.Text = description.Text = "";
        }

        list.SelectedIndexChanged += (_, _) =>
        {
            add.Enabled = quantity.Enabled = list.SelectedItems.Count == 1;
            if (!add.Enabled)
            {
                ClearSelection();
                return;
            }
            var item = (GameItem)list.SelectedItems[0].Tag!;
            icon.Image = icons?.GetIcon(item.Icon);
            icon.AccessibleName = item.Name;
            itemName.Text = item.Name;
            itemSubtitle.Text = string.IsNullOrWhiteSpace(item.Subtitle) ? item.Category : item.Subtitle;
            description.Text = string.IsNullOrWhiteSpace(item.Description)
                ? UiStrings.Get("item_picker.no_description") : item.Description.ReplaceLineEndings(Environment.NewLine);
            var limits = amountLimits(item);
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
            IEnumerable<GameItem> source = recentOnly.Checked ? recentItems : available;
            foreach (var item in source.Where(i => Matches(i.Name) || Matches(i.Id) || Matches(i.Category)))
                list.Items.Add(new ListViewItem([item.Name, item.Category, item.Id]) { Tag = item });
            list.EndUpdate();
            ClearSelection();
            count.Text = list.Items.Count == 0
                ? UiStrings.Get(recentOnly.Checked && string.IsNullOrEmpty(query)
                    ? "item_picker.no_recent" : "inventory.picker_no_matches")
                : UiStrings.Format("inventory.picker_match_count", list.Items.Count);
        }
        search.TextChanged += (_, _) => RefreshResults();
        recentOnly.CheckedChanged += (_, _) => RefreshResults();
        // The shared IconManager owns these images; closing a picker must not dispose them.
        FormClosed += (_, _) => icon.Image = null;
        RefreshResults();
        Shown += (_, _) => search.Focus();
    }
}
