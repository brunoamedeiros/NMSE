using NMSE.Data;

namespace NMSE.UI;

internal sealed class InventorySlotEditDialog : ToolsDialog
{
    internal int Amount { get; private set; }
    internal bool ReplaceRequested { get; private set; }

    internal InventorySlotEditDialog(string name, int amount, int maximum, bool rechargeable) : base("inventory_ux.edit")
    {
        Size = new Size(560, 270);
        MinimumSize = new Size(450, 250);
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(8) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var heading = new Label { Text = name, AutoSize = true, MaximumSize = new Size(470, 0), Margin = new Padding(0, 0, 0, 18) };
        layout.Controls.Add(heading, 0, 0); layout.SetColumnSpan(heading, 2);
        layout.Controls.Add(new Label { Text = UiStrings.Get("inventory.amount"), AutoSize = true, Margin = new Padding(0, 6, 12, 0) }, 0, 1);
        var quantity = new NumericUpDown
        {
            Minimum = Math.Min(0, amount), Maximum = Math.Max(Math.Max(amount, maximum), 1), Value = amount,
            Dock = DockStyle.Top, ThousandsSeparator = true, AccessibleName = UiStrings.Get("inventory.amount")
        };
        layout.Controls.Add(quantity, 1, 1);
        Controls.Add(layout);
        CancelButton = AddButton("common.cancel", result: DialogResult.Cancel);
        AcceptButton = AddButton("inventory_ux.apply", (_, _) => { Amount = (int)quantity.Value; DialogResult = DialogResult.OK; });
        AddButton("inventory.ctx_replace_item", (_, _) => { ReplaceRequested = true; DialogResult = DialogResult.OK; });
        if (rechargeable) AddButton("inventory_ux.recharge", (_, _) => quantity.Value = Math.Max(0, maximum));
        Shown += (_, _) => { quantity.Focus(); quantity.Select(0, quantity.Text.Length); };
    }
}
