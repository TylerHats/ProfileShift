using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using ProfileShift.Utilities;

namespace ProfileShift.UI.Views
{
    public class FolderNode
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public bool IsChecked { get; set; } = true;
        public ObservableCollection<FolderNode> Children { get; set; } = new ObservableCollection<FolderNode>();
    }

    public partial class FolderPickerModal : Window
    {
        public List<string> SelectedFolderPaths { get; private set; } = new List<string>();
        private ObservableCollection<FolderNode> _rootNodes = new ObservableCollection<FolderNode>();

        public FolderPickerModal(List<string> initialFolderPaths)
        {
            InitializeComponent();
            Loaded += FolderPickerModal_Loaded;
            BuildTree(initialFolderPaths);
        }

        private void FolderPickerModal_Loaded(object sender, RoutedEventArgs e)
        {
            DwmHelper.EnableDarkModeTitleBar(this);
        }

        private void BuildTree(List<string> initialFolderPaths)
        {
            foreach (var path in initialFolderPaths)
            {
                if (Directory.Exists(path))
                {
                    var node = new FolderNode
                    {
                        Name = Path.GetFileName(path),
                        FullPath = path,
                        IsChecked = true
                    };

                    try
                    {
                        var subDirs = Directory.GetDirectories(path);
                        foreach (var sub in subDirs)
                        {
                            node.Children.Add(new FolderNode
                            {
                                Name = Path.GetFileName(sub),
                                FullPath = sub,
                                IsChecked = true
                            });
                        }
                    }
                    catch { }

                    _rootNodes.Add(node);
                }
            }

            TvFolders.ItemsSource = _rootNodes;
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            SelectedFolderPaths.Clear();
            CollectSelectedPaths(_rootNodes);
            DialogResult = true;
            Close();
        }

        private void CollectSelectedPaths(IEnumerable<FolderNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.IsChecked)
                {
                    SelectedFolderPaths.Add(node.FullPath);
                }
                CollectSelectedPaths(node.Children);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
