using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public class RevisionFileTreeNodeToggleButton : ToggleButton
    {
        protected override Type StyleKeyOverride => typeof(ToggleButton);

        protected override async void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
                DataContext is ViewModels.RevisionFileTreeNode { IsFolder: true } node)
            {
                var tree = this.FindAncestorOfType<RevisionFileTreeView>();
                await tree?.ToggleNodeIsExpandedAsync(node);
            }

            e.Handled = true;
        }
    }

    public class RevisionTreeNodeIcon : UserControl
    {
        public static readonly StyledProperty<ViewModels.RevisionFileTreeNode> NodeProperty =
            AvaloniaProperty.Register<RevisionTreeNodeIcon, ViewModels.RevisionFileTreeNode>(nameof(Node));

        public ViewModels.RevisionFileTreeNode Node
        {
            get => GetValue(NodeProperty);
            set => SetValue(NodeProperty, value);
        }

        public static readonly StyledProperty<bool> IsExpandedProperty =
            AvaloniaProperty.Register<RevisionTreeNodeIcon, bool>(nameof(IsExpanded));

        public bool IsExpanded
        {
            get => GetValue(IsExpandedProperty);
            set => SetValue(IsExpandedProperty, value);
        }

        static RevisionTreeNodeIcon()
        {
            NodeProperty.Changed.AddClassHandler<RevisionTreeNodeIcon>((icon, _) => icon.UpdateContent());
            IsExpandedProperty.Changed.AddClassHandler<RevisionTreeNodeIcon>((icon, _) => icon.UpdateContent());
        }

        private void UpdateContent()
        {
            var node = Node;
            if (node?.Backend == null)
            {
                Content = null;
                return;
            }

            var obj = node.Backend;
            switch (obj.Type)
            {
                case Models.ObjectType.Blob:
                    CreateContent("Icons.File", new Thickness(0, 0, 0, 0));
                    break;
                case Models.ObjectType.Commit:
                    CreateContent("Icons.Submodule", new Thickness(0, 0, 0, 0));
                    break;
                default:
                    CreateContent(node.IsExpanded ? "Icons.Folder.Open" : "Icons.Folder", new Thickness(0, 2, 0, 0), Brushes.Goldenrod);
                    break;
            }
        }

        private void CreateContent(string iconKey, Thickness margin, IBrush fill = null)
        {
            if (this.FindResource(iconKey) is not StreamGeometry geo)
                return;

            var icon = new Avalonia.Controls.Shapes.Path()
            {
                Width = 14,
                Height = 14,
                Margin = margin,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Data = geo,
            };

            if (fill != null)
                icon.Fill = fill;

            Content = icon;
        }
    }

    public class RevisionFileRowsListBox : ListBox
    {
        protected override Type StyleKeyOverride => typeof(ListBox);

        protected override async void OnKeyDown(KeyEventArgs e)
        {
            if (SelectedItem is ViewModels.RevisionFileTreeNode node)
            {
                if (node.IsFolder &&
                    e.KeyModifiers == KeyModifiers.None &&
                    (node.IsExpanded && e.Key == Key.Left) || (!node.IsExpanded && e.Key == Key.Right))
                {
                    var tree = this.FindAncestorOfType<RevisionFileTreeView>();
                    await tree?.ToggleNodeIsExpandedAsync(node);
                    e.Handled = true;
                }
                else if (e.Key == Key.C &&
                    e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control))
                {
                    var detailView = this.FindAncestorOfType<CommitDetail>();
                    if (detailView is { DataContext: ViewModels.CommitDetail detail })
                    {
                        var path = node.Backend?.Path ?? string.Empty;
                        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                            path = detail.GetAbsPath(path);

                        await App.CopyTextAsync(path);
                        e.Handled = true;
                    }
                }
                else if (node.Backend is { Type: Models.ObjectType.Blob } file &&
                    e.Key == Key.S &&
                    e.KeyModifiers == ((OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control) | KeyModifiers.Shift))
                {
                    var detailView = this.FindAncestorOfType<CommitDetail>();
                    if (detailView is { DataContext: ViewModels.CommitDetail detail })
                    {
                        await detail.SaveRevisionFile(file);
                        e.Handled = true;
                    }
                }
            }

            if (!e.Handled)
                base.OnKeyDown(e);
        }
    }

    public partial class RevisionFileTreeView : UserControl
    {
        public static readonly StyledProperty<string> RevisionProperty =
            AvaloniaProperty.Register<RevisionFileTreeView, string>(nameof(Revision));

        public string Revision
        {
            get => GetValue(RevisionProperty);
            set => SetValue(RevisionProperty, value);
        }

        public AvaloniaList<ViewModels.RevisionFileTreeNode> Rows { get; } = [];

        public RevisionFileTreeView()
        {
            InitializeComponent();
        }

        public async Task SetSearchResultAsync(string file)
        {
            Rows.Clear();
            _searchResult.Clear();

            var rows = new List<ViewModels.RevisionFileTreeNode>();
            if (string.IsNullOrEmpty(file))
            {
                MakeRows(rows, _tree, 0);
            }
            else
            {
                var vm = DataContext as ViewModels.CommitDetail;
                if (vm?.Commit == null)
                    return;

                var objects = await vm.GetRevisionFilesUnderFolderAsync(file);
                if (objects is not { Count: 1 })
                    return;

                var routes = file.Split('/');
                if (routes.Length == 1)
                {
                    _searchResult.Add(new ViewModels.RevisionFileTreeNode
                    {
                        Backend = objects[0]
                    });
                }
                else
                {
                    var last = _searchResult;
                    var prefix = string.Empty;
                    for (var i = 0; i < routes.Length - 1; i++)
                    {
                        var folder = new ViewModels.RevisionFileTreeNode
                        {
                            Backend = new Models.Object
                            {
                                Type = Models.ObjectType.Tree,
                                Path = prefix + routes[i],
                            },
                            IsExpanded = true,
                        };

                        last.Add(folder);
                        last = folder.Children;
                        prefix = folder.Backend + "/";
                    }

                    last.Add(new ViewModels.RevisionFileTreeNode
                    {
                        Backend = objects[0]
                    });
                }

                MakeRows(rows, _searchResult, 0);
            }

            Rows.AddRange(rows);
            GC.Collect();
        }

        public async Task ToggleNodeIsExpandedAsync(ViewModels.RevisionFileTreeNode node)
        {
            _disableSelectionChangingEvent = true;
            node.IsExpanded = !node.IsExpanded;

            var depth = node.Depth;
            var idx = Rows.IndexOf(node);
            if (idx == -1)
                return;

            if (node.IsExpanded)
            {
                var subtree = await GetChildrenOfTreeNodeAsync(node);
                if (subtree is { Count: > 0 })
                {
                    var subrows = new List<ViewModels.RevisionFileTreeNode>();
                    MakeRows(subrows, subtree, depth + 1);
                    Rows.InsertRange(idx + 1, subrows);
                }
            }
            else
            {
                var removeCount = 0;
                for (int i = idx + 1; i < Rows.Count; i++)
                {
                    var row = Rows[i];
                    if (row.Depth <= depth)
                        break;

                    removeCount++;
                }
                Rows.RemoveRange(idx + 1, removeCount);
            }

            _disableSelectionChangingEvent = false;
        }

        protected override async void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == RevisionProperty)
            {
                _tree.Clear();
                _searchResult.Clear();

                var vm = DataContext as ViewModels.CommitDetail;
                if (vm?.Commit == null)
                {
                    Rows.Clear();
                    GC.Collect();
                    return;
                }

                var objects = await vm.GetRevisionFilesUnderFolderAsync(null);
                if (objects == null || objects.Count == 0)
                {
                    Rows.Clear();
                    GC.Collect();
                    return;
                }

                foreach (var obj in objects)
                    _tree.Add(new ViewModels.RevisionFileTreeNode { Backend = obj });

                SortNodes(_tree);

                var topTree = new List<ViewModels.RevisionFileTreeNode>();
                MakeRows(topTree, _tree, 0);

                Rows.Clear();
                Rows.AddRange(topTree);
                GC.Collect();
            }
        }

        private void OnTreeNodeContextRequested(object sender, ContextRequestedEventArgs e)
        {
            if (DataContext is ViewModels.CommitDetail vm &&
                sender is Grid { DataContext: ViewModels.RevisionFileTreeNode { Backend: { } obj } } grid)
            {
                var menu = vm.CreateRevisionFileContextMenu(obj);
                menu.Open(grid);
            }

            e.Handled = true;
        }

        private async void OnTreeNodeDoubleTapped(object sender, TappedEventArgs e)
        {
            if (sender is Grid { DataContext: ViewModels.RevisionFileTreeNode { IsFolder: true } node })
            {
                var posX = e.GetPosition(this).X;
                if (posX < node.Depth * 16 + 16)
                    return;

                await ToggleNodeIsExpandedAsync(node);
            }
        }

        private async void OnRowsSelectionChanged(object sender, SelectionChangedEventArgs _)
        {
            if (_disableSelectionChangingEvent || DataContext is not ViewModels.CommitDetail vm)
                return;

            if (sender is ListBox { SelectedItem: ViewModels.RevisionFileTreeNode { IsFolder: false } node })
                await vm.ViewRevisionFileAsync(node.Backend);
            else
                await vm.ViewRevisionFileAsync(null);
        }

        private async Task<List<ViewModels.RevisionFileTreeNode>> GetChildrenOfTreeNodeAsync(ViewModels.RevisionFileTreeNode node)
        {
            if (!node.IsFolder)
                return null;

            if (node.Children.Count > 0)
                return node.Children;

            if (DataContext is not ViewModels.CommitDetail vm)
                return null;

            var objects = await vm.GetRevisionFilesUnderFolderAsync(node.Backend.Path + "/");
            if (objects == null || objects.Count == 0)
                return null;

            foreach (var obj in objects)
                node.Children.Add(new ViewModels.RevisionFileTreeNode() { Backend = obj });

            SortNodes(node.Children);
            return node.Children;
        }

        private void MakeRows(List<ViewModels.RevisionFileTreeNode> rows, List<ViewModels.RevisionFileTreeNode> nodes, int depth)
        {
            foreach (var node in nodes)
            {
                node.Depth = depth;
                rows.Add(node);

                if (!node.IsExpanded || !node.IsFolder)
                    continue;

                MakeRows(rows, node.Children, depth + 1);
            }
        }

        private void SortNodes(List<ViewModels.RevisionFileTreeNode> nodes)
        {
            nodes.Sort((l, r) =>
            {
                if (l.IsFolder == r.IsFolder)
                    return Models.NumericSort.Compare(l.Name, r.Name);
                return l.IsFolder ? -1 : 1;
            });
        }

        private List<ViewModels.RevisionFileTreeNode> _tree = [];
        private bool _disableSelectionChangingEvent = false;
        private List<ViewModels.RevisionFileTreeNode> _searchResult = [];
    }
}
