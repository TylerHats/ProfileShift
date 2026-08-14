using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using ProfileShift.Core;
using ProfileShift.Models;
using ProfileShift.Utilities;

namespace ProfileShift.UI.Views
{
    public enum FolderNodeType
    {
        UserRoot,
        C_RootGroup,
        TopLevelFolder,
        SubFolder
    }

    public class FolderNode : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _fullPath = string.Empty;
        private bool? _isChecked = true;
        private bool _isExpanded = false;
        private string _displayIcon = "📁";
        private FolderNodeType _nodeType = FolderNodeType.SubFolder;
        private string _ownerUsername = string.Empty;

        public FolderNode? Parent { get; set; }
        public ObservableCollection<FolderNode> Children { get; set; } = new ObservableCollection<FolderNode>();

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }

        public string FullPath
        {
            get => _fullPath;
            set { _fullPath = value; OnPropertyChanged(nameof(FullPath)); }
        }

        public string DisplayIcon
        {
            get => _displayIcon;
            set { _displayIcon = value; OnPropertyChanged(nameof(DisplayIcon)); }
        }

        public FolderNodeType NodeType
        {
            get => _nodeType;
            set { _nodeType = value; OnPropertyChanged(nameof(NodeType)); }
        }

        public string OwnerUsername
        {
            get => _ownerUsername;
            set { _ownerUsername = value; OnPropertyChanged(nameof(OwnerUsername)); }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(nameof(IsExpanded)); }
        }

        public bool? IsChecked
        {
            get => _isChecked;
            set => SetIsChecked(value, true, true);
        }

        public void SetIsChecked(bool? value, bool updateChildren, bool updateParent)
        {
            if (_isChecked == value) return;

            _isChecked = value;
            OnPropertyChanged(nameof(IsChecked));

            if (updateChildren && _isChecked.HasValue)
            {
                foreach (var child in Children)
                {
                    child.SetIsChecked(_isChecked, true, false);
                }
            }

            if (updateParent && Parent != null)
            {
                Parent.VerifyCheckState();
            }
        }

        public void VerifyCheckState()
        {
            if (Children.Count == 0) return;

            bool allTrue = true;
            bool allFalse = true;

            foreach (var child in Children)
            {
                if (child.IsChecked == true)
                {
                    allFalse = false;
                }
                else if (child.IsChecked == false)
                {
                    allTrue = false;
                }
                else
                {
                    allTrue = false;
                    allFalse = false;
                    break;
                }
            }

            bool? newState;
            if (allTrue) newState = true;
            else if (allFalse) newState = false;
            else newState = null; // Indeterminate

            if (_isChecked != newState)
            {
                _isChecked = newState;
                OnPropertyChanged(nameof(IsChecked));
                Parent?.VerifyCheckState();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }

    public partial class FolderPickerModal : Window
    {
        public Dictionary<string, List<string>> UserFoldersMap { get; private set; } = new Dictionary<string, List<string>>();
        public Dictionary<string, List<string>> UserExcludedFoldersMap { get; private set; } = new Dictionary<string, List<string>>();
        public List<string> SelectedRootFolders { get; private set; } = new List<string>();
        public List<string> ExcludedRootFolders { get; private set; } = new List<string>();

        public List<string> SelectedFolderPaths { get; private set; } = new List<string>();

        private ObservableCollection<FolderNode> _rootNodes = new ObservableCollection<FolderNode>();

        public FolderPickerModal(
            List<UserProfile> selectedUsers,
            bool includeRootData,
            Dictionary<string, List<string>>? currentCustomUserFolders = null,
            Dictionary<string, List<string>>? currentCustomUserExcludedFolders = null,
            List<string>? currentCustomRootFolders = null,
            List<string>? currentCustomExcludedRootFolders = null)
        {
            InitializeComponent();
            Loaded += FolderPickerModal_Loaded;
            BuildTree(selectedUsers, includeRootData, currentCustomUserFolders, currentCustomUserExcludedFolders, currentCustomRootFolders, currentCustomExcludedRootFolders);
        }

        public FolderPickerModal(List<string> initialFolderPaths)
        {
            InitializeComponent();
            Loaded += FolderPickerModal_Loaded;
            BuildTreeFromPaths(initialFolderPaths);
        }

        private void FolderPickerModal_Loaded(object sender, RoutedEventArgs e)
        {
            DwmHelper.EnableDarkModeTitleBar(this);
        }

        private void BuildTree(
            List<UserProfile> selectedUsers,
            bool includeRootData,
            Dictionary<string, List<string>>? currentCustomUserFolders,
            Dictionary<string, List<string>>? currentCustomUserExcludedFolders,
            List<string>? currentCustomRootFolders,
            List<string>? currentCustomExcludedRootFolders)
        {
            _rootNodes.Clear();

            foreach (var user in selectedUsers)
            {
                if (!user.IsSelected) continue;

                var userNode = new FolderNode
                {
                    Name = $"User: {user.Username}",
                    FullPath = user.ProfilePath,
                    DisplayIcon = "👤",
                    NodeType = FolderNodeType.UserRoot,
                    OwnerUsername = user.Username,
                    IsExpanded = true
                };

                var standardFolders = FolderScanner.GetUserSelectableFolders(user.ProfilePath);
                var customFoldersForUser = currentCustomUserFolders != null && currentCustomUserFolders.TryGetValue(user.Username, out var cf) ? cf : null;
                var customExcludedForUser = currentCustomUserExcludedFolders != null && currentCustomUserExcludedFolders.TryGetValue(user.Username, out var ce) ? ce : null;

                foreach (var folderPath in standardFolders)
                {
                    if (Directory.Exists(folderPath))
                    {
                        var folderNode = CreateFolderHierarchy(folderPath, user.Username, FolderNodeType.TopLevelFolder, 0, 2);
                        folderNode.Parent = userNode;

                        if (customFoldersForUser != null)
                        {
                            bool isIncluded = customFoldersForUser.Contains(folderPath, StringComparer.OrdinalIgnoreCase);
                            if (!isIncluded)
                            {
                                folderNode.SetIsChecked(false, true, false);
                            }
                        }

                        if (customExcludedForUser != null && customExcludedForUser.Count > 0)
                        {
                            ApplySubfolderExclusions(folderNode, customExcludedForUser);
                        }

                        userNode.Children.Add(folderNode);
                    }
                }

                userNode.VerifyCheckState();
                _rootNodes.Add(userNode);
            }

            if (includeRootData)
            {
                var rootFolders = FolderScanner.GetRootDriveFolders();
                if (rootFolders.Count > 0)
                {
                    var rootGroupNode = new FolderNode
                    {
                        Name = "C:\\ Root Folders",
                        FullPath = @"C:\",
                        DisplayIcon = "💾",
                        NodeType = FolderNodeType.C_RootGroup,
                        IsExpanded = true
                    };

                    foreach (var rf in rootFolders)
                    {
                        if (Directory.Exists(rf))
                        {
                            var rfNode = CreateFolderHierarchy(rf, string.Empty, FolderNodeType.TopLevelFolder, 0, 1);
                            rfNode.Parent = rootGroupNode;

                            if (currentCustomRootFolders != null)
                            {
                                bool isIncluded = currentCustomRootFolders.Contains(rf, StringComparer.OrdinalIgnoreCase);
                                if (!isIncluded)
                                {
                                    rfNode.SetIsChecked(false, true, false);
                                }
                            }

                            if (currentCustomExcludedRootFolders != null && currentCustomExcludedRootFolders.Count > 0)
                            {
                                ApplySubfolderExclusions(rfNode, currentCustomExcludedRootFolders);
                            }

                            rootGroupNode.Children.Add(rfNode);
                        }
                    }

                    rootGroupNode.VerifyCheckState();
                    _rootNodes.Add(rootGroupNode);
                }
            }

            TvFolders.ItemsSource = _rootNodes;
        }

        private void BuildTreeFromPaths(List<string> initialFolderPaths)
        {
            _rootNodes.Clear();
            foreach (var path in initialFolderPaths)
            {
                if (Directory.Exists(path))
                {
                    var node = CreateFolderHierarchy(path, string.Empty, FolderNodeType.TopLevelFolder, 0, 1);
                    _rootNodes.Add(node);
                }
            }
            TvFolders.ItemsSource = _rootNodes;
        }

        private FolderNode CreateFolderHierarchy(string path, string username, FolderNodeType nodeType, int currentDepth, int maxDepth)
        {
            string displayName = Path.GetFileName(path);
            if (string.IsNullOrEmpty(displayName)) displayName = path;

            var node = new FolderNode
            {
                Name = displayName,
                FullPath = path,
                DisplayIcon = "📁",
                NodeType = nodeType,
                OwnerUsername = username,
                IsExpanded = currentDepth < 1
            };

            if (currentDepth < maxDepth)
            {
                try
                {
                    var subDirs = Directory.GetDirectories(path);
                    foreach (var sub in subDirs)
                    {
                        try
                        {
                            var di = new DirectoryInfo(sub);
                            if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        }
                        catch { }

                        string subName = Path.GetFileName(sub);
                        if (ExclusionFilter.ShouldExcludeDirectory(sub)) continue;

                        var childNode = CreateFolderHierarchy(sub, username, FolderNodeType.SubFolder, currentDepth + 1, maxDepth);
                        childNode.Parent = node;
                        node.Children.Add(childNode);
                    }
                }
                catch { }
            }

            return node;
        }

        private void ApplySubfolderExclusions(FolderNode node, List<string> excludedPaths)
        {
            if (excludedPaths.Contains(node.FullPath, StringComparer.OrdinalIgnoreCase))
            {
                node.SetIsChecked(false, true, false);
                return;
            }

            foreach (var child in node.Children)
            {
                ApplySubfolderExclusions(child, excludedPaths);
            }
            node.VerifyCheckState();
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var root in _rootNodes)
            {
                root.SetIsChecked(true, true, false);
            }
        }

        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var root in _rootNodes)
            {
                root.SetIsChecked(false, true, false);
            }
        }

        private void BtnExpandAll_Click(object sender, RoutedEventArgs e)
        {
            ExpandAllNodes(_rootNodes, true);
        }

        private void ExpandAllNodes(IEnumerable<FolderNode> nodes, bool expand)
        {
            foreach (var node in nodes)
            {
                node.IsExpanded = expand;
                ExpandAllNodes(node.Children, expand);
            }
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            UserFoldersMap.Clear();
            UserExcludedFoldersMap.Clear();
            SelectedRootFolders.Clear();
            ExcludedRootFolders.Clear();
            SelectedFolderPaths.Clear();

            foreach (var rootNode in _rootNodes)
            {
                if (rootNode.NodeType == FolderNodeType.UserRoot)
                {
                    string username = rootNode.OwnerUsername;
                    var includedFolders = new List<string>();
                    var excludedFolders = new List<string>();

                    foreach (var topLevel in rootNode.Children)
                    {
                        if (topLevel.IsChecked == true)
                        {
                            includedFolders.Add(topLevel.FullPath);
                            SelectedFolderPaths.Add(topLevel.FullPath);
                        }
                        else if (topLevel.IsChecked == null)
                        {
                            includedFolders.Add(topLevel.FullPath);
                            SelectedFolderPaths.Add(topLevel.FullPath);
                            CollectExcludedDescendants(topLevel, excludedFolders);
                        }
                    }

                    UserFoldersMap[username] = includedFolders;
                    UserExcludedFoldersMap[username] = excludedFolders;
                }
                else if (rootNode.NodeType == FolderNodeType.C_RootGroup)
                {
                    foreach (var topLevel in rootNode.Children)
                    {
                        if (topLevel.IsChecked == true)
                        {
                            SelectedRootFolders.Add(topLevel.FullPath);
                            SelectedFolderPaths.Add(topLevel.FullPath);
                        }
                        else if (topLevel.IsChecked == null)
                        {
                            SelectedRootFolders.Add(topLevel.FullPath);
                            SelectedFolderPaths.Add(topLevel.FullPath);
                            CollectExcludedDescendants(topLevel, ExcludedRootFolders);
                        }
                    }
                }
            }

            DialogResult = true;
            Close();
        }

        private void CollectExcludedDescendants(FolderNode node, List<string> excludedList)
        {
            foreach (var child in node.Children)
            {
                if (child.IsChecked == false)
                {
                    excludedList.Add(child.FullPath);
                }
                else if (child.IsChecked == null)
                {
                    CollectExcludedDescendants(child, excludedList);
                }
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

