using System.IO;
using System.Windows;
using System.Windows.Controls;
using TxtConverter.Core.Models;

namespace TxtConverter.Views;

public partial class SelectionWindow : Window
{
    private List<string> _allFiles;
    private HashSet<string> _initialSelection;
    private string _rootPath;
    private List<FileTreeNode> _rootNodes = new();

    public HashSet<string>? Result { get; private set; }

    public SelectionWindow(List<string> allFiles, HashSet<string> currentSelection, string rootPath)
    {
        InitializeComponent();
        _allFiles = allFiles;
        _initialSelection = new HashSet<string>(currentSelection);
        _rootPath = rootPath;

        ViewModeCombo.SelectedIndex = 0; // Default triggers SelectionChanged logic

        // Ensure tree is built if event didn't fire due to init order
        if (FileTree.ItemsSource == null)
        {
            BuildTree(true);
            UpdateStats();
        }
    }

    private void ViewMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded && _rootNodes.Count == 0) return;

        bool byType = ViewModeCombo.SelectedIndex == 0;

        // Save state before switch
        if (_rootNodes.Count > 0)
        {
            SnapshotSelection();
        }

        BuildTree(byType);
        UpdateStats();
    }

    private void BuildTree(bool byType)
    {
        _rootNodes.Clear();
        if (byType)
        {
            BuildByType();
        }
        else
        {
            BuildByFolder();
        }
        FileTree.ItemsSource = null;
        FileTree.ItemsSource = _rootNodes;
    }

    private void BuildByType()
    {
        var grouped = _allFiles.GroupBy(f => Path.GetExtension(f).TrimStart('.').ToLower())
                               .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            string ext = string.IsNullOrEmpty(group.Key) ? "No Extension" : group.Key.ToUpper();
            var groupNode = new FileTreeNode { Name = $"{ext} ({group.Count()})", IsFile = false };

            foreach (var file in group)
            {
                var fileNode = new FileTreeNode
                {
                    Name = Path.GetRelativePath(_rootPath, file),
                    FullPath = file,
                    IsFile = true,
                    Parent = groupNode,
                    IsChecked = _initialSelection.Contains(file)
                };
                fileNode.PropertyChanged += FileNode_PropertyChanged;
                groupNode.Children.Add(fileNode);
            }
            groupNode.RecalculateState();
            _rootNodes.Add(groupNode);
        }
    }

    private void BuildByFolder()
    {
        var folderCache = new Dictionary<string, FileTreeNode>();

        foreach (var file in _allFiles)
        {
            string relPath = Path.GetRelativePath(_rootPath, file);
            string[] parts = relPath.Split(Path.DirectorySeparatorChar);

            FileTreeNode? currentParent = null;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                string part = parts[i];
                string pathKey = string.Join(Path.DirectorySeparatorChar, parts.Take(i + 1));

                if (!folderCache.TryGetValue(pathKey, out var folderNode))
                {
                    folderNode = new FileTreeNode
                    {
                        Name = part,
                        IsFile = false,
                        Parent = currentParent
                    };
                    folderCache[pathKey] = folderNode;

                    if (currentParent == null) _rootNodes.Add(folderNode);
                    else currentParent.Children.Add(folderNode);
                }
                currentParent = folderNode;
            }

            var fileNode = new FileTreeNode
            {
                Name = parts.Last(),
                FullPath = file,
                IsFile = true,
                Parent = currentParent,
                IsChecked = _initialSelection.Contains(file)
            };
            fileNode.PropertyChanged += FileNode_PropertyChanged;

            if (currentParent == null) _rootNodes.Add(fileNode);
            else currentParent.Children.Add(fileNode);
        }

        foreach (var root in _rootNodes) RecalculateRecursive(root);
    }

    private void RecalculateRecursive(FileTreeNode node)
    {
        if (node.Children.Count > 0)
        {
            foreach (var child in node.Children) RecalculateRecursive(child);
            node.RecalculateState();
        }
    }

    private void FileNode_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileTreeNode.IsChecked))
        {
            UpdateStats();
        }
    }

    private void UpdateStats()
    {
        int count = GetSelectedCount(_rootNodes);
        InfoLabel.Text = $"{count} of {_allFiles.Count}";
    }

    private int GetSelectedCount(IEnumerable<FileTreeNode> nodes)
    {
        int count = 0;
        foreach (var node in nodes)
        {
            if (node.IsFile && node.IsChecked == true) count++;
            count += GetSelectedCount(node.Children);
        }
        return count;
    }

    private void SnapshotSelection()
    {
        _initialSelection.Clear();
        CollectSelected(_rootNodes);
    }

    private void CollectSelected(IEnumerable<FileTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsFile && node.IsChecked == true) _initialSelection.Add(node.FullPath);
            CollectSelected(node.Children);
        }
    }

    // --- Actions ---

    private void SelectAll_Click(object sender, RoutedEventArgs e) => SetCheckedState(true);
    private void SelectNone_Click(object sender, RoutedEventArgs e) => SetCheckedState(false);

    private void ExpandAll_Click(object sender, RoutedEventArgs e) => SetExpandedState(true);
    private void CollapseAll_Click(object sender, RoutedEventArgs e) => SetExpandedState(false);

    private void SetCheckedState(bool state)
    {
        foreach (var node in _rootNodes) node.IsChecked = state;
        UpdateStats();
    }

    private void SetExpandedState(bool isExpanded)
    {
        foreach (var node in _rootNodes)
        {
            SetExpandedRecursive(node, isExpanded);
        }
    }

    private void SetExpandedRecursive(FileTreeNode node, bool isExpanded)
    {
        node.IsExpanded = isExpanded;
        foreach (var child in node.Children)
        {
            SetExpandedRecursive(child, isExpanded);
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        SnapshotSelection();
        Result = _initialSelection;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left) DragMove();
    }
}