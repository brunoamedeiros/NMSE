using NMSE.Core;
using NMSE.Models;

namespace NMSE.UI.Panels;

public partial class RawJsonPanel
{
    internal bool HasPendingTextEdits => _viewMode != ViewMode.Tree && _textModifiedSinceSwitch;

    internal void NavigateToReviewChange(JsonObject save, JsonObject? account, string? accountPath,
        ChangeSummaryLogic.Change change)
    {
        _saveData = save;
        SetAccountData(account, accountPath);
        bool showAccount = change.Scope == ChangeReviewLogic.DataScope.Account;
        if (showAccount && account == null) return;
        _fileSelector.SelectedIndex = showAccount ? 1 : 0;
        _isShowingAccount = showAccount;
        var data = showAccount ? account! : save;
        // Deleted fields no longer have a node; reveal their nearest existing
        // parent rather than silently leaving an unrelated field selected.
        var path = change.Tokens.ToList();
        while (path.Count > 0 && !ChangeReviewLogic.TryRead(data, path, out _)) path.RemoveAt(path.Count - 1);
        if (path.Count > 0) NavigateToPath(path.Select(token => token.TreeSegment).ToArray());
        else
        {
            if (_viewMode != ViewMode.Tree) ShowTreeView();
            BuildTree(data);
            if (_treeView.Nodes.Count > 0) _treeView.SelectedNode = _treeView.Nodes[0];
        }
        _treeView.Focus();
    }
}
