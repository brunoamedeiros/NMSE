using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.UI.Panels;

public partial class MilestonePanel
{
    private readonly Dictionary<string, NumericUpDown> _localFields = new();
    private readonly Dictionary<string, int> _localOriginal = new();
    private readonly List<Label> _localRequirements = [];
    private TabPage _localTab = null!;
    private Label _localSystem = null!, _localHelp = null!, _localStatus = null!;
    private JsonObject? _localPlayer;
    private long? _localAddress;

    private void InitializeLocalStanding()
    {
        _localTab = new TabPage { AutoScroll = true };
        var table = new TableLayoutPanel
        {
            AutoSize = true, Dock = DockStyle.Top, ColumnCount = 3, Padding = new Padding(16),
            RowCount = 3
        };
        for (int i = 0; i < 3; i++) table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _localSystem = new Label { AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
        _localHelp = new Label { AutoSize = true, MaximumSize = new Size(650, 0), Margin = new Padding(0, 0, 0, 12) };
        _localStatus = new Label { AutoSize = true, MaximumSize = new Size(650, 0), Margin = new Padding(0, 0, 0, 12) };
        table.Controls.Add(_localSystem, 0, 0); table.SetColumnSpan(_localSystem, 3);
        table.Controls.Add(_localHelp, 0, 1); table.SetColumnSpan(_localHelp, 3);
        table.Controls.Add(_localStatus, 0, 2); table.SetColumnSpan(_localStatus, 3);
        foreach (var field in LocalStandingLogic.Fields)
        {
            int row = table.RowCount++;
            var label = new Label { Text = UiStrings.Get(field.LabelKey), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 20, 6) };
            _localisedLabels.Add((label, field.LabelKey));
            var input = new NumericUpDown
            {
                Minimum = int.MinValue, Maximum = int.MaxValue, ThousandsSeparator = true,
                Width = 140, Enabled = false, Margin = new Padding(0, 4, 12, 4), AccessibleName = label.Text
            };
            input.ValueChanged += (_, _) => { if (!_loading) DataModified?.Invoke(this, EventArgs.Empty); };
            var requirement = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
            _localRequirements.Add(requirement);
            _localFields.Add(field.Id, input);
            table.Controls.Add(label, 0, row);
            table.Controls.Add(input, 1, row);
            table.Controls.Add(requirement, 2, row);
        }
        _localTab.Controls.Add(table);
        _tabControl.TabPages.Add(_localTab);
        LocaliseLocalStanding();
    }

    private void LoadLocalStanding(JsonObject saveData)
    {
        _localOriginal.Clear();
        _localPlayer = saveData.GetObject("PlayerStateData");
        _localAddress = _localPlayer != null && LocalStandingLogic.TryCurrentSystem(_localPlayer, out long address) ? address : null;
        foreach (var field in LocalStandingLogic.Fields)
        {
            var input = _localFields[field.Id];
            input.Enabled = false;
            input.Value = 0;
            if (_localPlayer != null && _localAddress is { } system && LocalStandingLogic.TryRead(_localPlayer, system, field.Id, out int value))
            {
                _localOriginal[field.Id] = value;
                input.Value = value;
                input.Enabled = true;
            }
        }
        LocaliseLocalStanding();
    }

    private void SaveLocalStanding(JsonObject saveData)
    {
        if (_localPlayer == null || _localAddress is not { } address) return;
        // Retain the displayed system when another panel edits coordinates before this panel is flushed.
        var edited = _localOriginal.Keys.ToDictionary(id => id, id => (int)_localFields[id].Value);
        if (edited.All(pair => pair.Value == _localOriginal[pair.Key])) return;
        if (!ReferenceEquals(_localPlayer, saveData.GetObject("PlayerStateData")) ||
            !LocalStandingLogic.TryWriteAll(_localPlayer, address, _localOriginal, edited))
            throw new InvalidOperationException(UiStrings.Get("local_standing.stale"));
    }

    private void LocaliseLocalStanding()
    {
        _localTab.Text = UiStrings.Get("local_standing.title");
        _localHelp.Text = UiStrings.Get("local_standing.help");
        _localStatus.Text = UiStrings.Get(_localOriginal.Count == LocalStandingLogic.Fields.Length ? "local_standing.ready" : "local_standing.unavailable");
        _localSystem.Text = UiStrings.Get("local_standing.no_system");
        if (_localAddress is { } address)
        {
            var location = SpacePoiLogic.UnpackUniverseAddress(address);
            _localSystem.Text = UiStrings.Format("local_standing.system", location.RealityIndex + 1,
                CoordinateHelper.VoxelToSignalBooster(location.VoxelX, location.VoxelY, location.VoxelZ, location.SolarSystemIndex));
        }
        for (int i = 0; i < LocalStandingLogic.Fields.Length; i++)
        {
            var field = LocalStandingLogic.Fields[i];
            _localRequirements[i].Text = UiStrings.Format("local_standing.requirement", field.Requirement);
            _localFields[field.Id].AccessibleName = UiStrings.Get(field.LabelKey);
        }
    }
}
