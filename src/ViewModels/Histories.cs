﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Avalonia.Controls;
using Avalonia.Platform.Storage;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class Histories : ObservableObject, IDisposable
    {
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public List<Models.Commit> Commits
        {
            get => _commits;
            set
            {
                var lastSelected = AutoSelectedCommit;
                if (SetProperty(ref _commits, value))
                {
                    if (value.Count > 0 && lastSelected != null)
                        AutoSelectedCommit = value.Find(x => x.SHA == lastSelected.SHA);
                }
            }
        }

        public Models.CommitGraph Graph
        {
            get => _graph;
            set => SetProperty(ref _graph, value);
        }

        public Models.Commit AutoSelectedCommit
        {
            get => _autoSelectedCommit;
            set => SetProperty(ref _autoSelectedCommit, value);
        }

        public long NavigationId
        {
            get => _navigationId;
            private set => SetProperty(ref _navigationId, value);
        }

        public IDisposable DetailContext
        {
            get => _detailContext;
            set => SetProperty(ref _detailContext, value);
        }

        public Models.Bisect Bisect
        {
            get => _bisect;
            private set => SetProperty(ref _bisect, value);
        }

        public GridLength LeftArea
        {
            get => _leftArea;
            set => SetProperty(ref _leftArea, value);
        }

        public GridLength RightArea
        {
            get => _rightArea;
            set => SetProperty(ref _rightArea, value);
        }

        public GridLength TopArea
        {
            get => _topArea;
            set => SetProperty(ref _topArea, value);
        }

        public GridLength BottomArea
        {
            get => _bottomArea;
            set => SetProperty(ref _bottomArea, value);
        }

        public Histories(Repository repo)
        {
            _repo = repo;
        }

        public void Dispose()
        {
            Commits = [];
            _repo = null;
            _graph = null;
            _autoSelectedCommit = null;
            _detailContext?.Dispose();
            _detailContext = null;
        }

        public Models.BisectState UpdateBisectInfo()
        {
            var test = Path.Combine(_repo.GitDir, "BISECT_START");
            if (!File.Exists(test))
            {
                Bisect = null;
                return Models.BisectState.None;
            }

            var info = new Models.Bisect();
            var dir = Path.Combine(_repo.GitDir, "refs", "bisect");
            if (Directory.Exists(dir))
            {
                var files = new DirectoryInfo(dir).GetFiles();
                foreach (var file in files)
                {
                    if (file.Name.StartsWith("bad"))
                        info.Bads.Add(File.ReadAllText(file.FullName).Trim());
                    else if (file.Name.StartsWith("good"))
                        info.Goods.Add(File.ReadAllText(file.FullName).Trim());
                }
            }

            Bisect = info;

            if (info.Bads.Count == 0 || info.Goods.Count == 0)
                return Models.BisectState.WaitingForRange;
            else
                return Models.BisectState.Detecting;
        }

        public void NavigateTo(string commitSHA)
        {
            var commit = _commits.Find(x => x.SHA.StartsWith(commitSHA, StringComparison.Ordinal));
            if (commit == null)
            {
                AutoSelectedCommit = null;
                commit = new Commands.QuerySingleCommit(_repo.FullPath, commitSHA).GetResultAsync().Result;
            }
            else
            {
                AutoSelectedCommit = commit;
                NavigationId = _navigationId + 1;
            }

            if (commit != null)
            {
                if (_detailContext is CommitDetail detail)
                {
                    detail.Commit = commit;
                }
                else
                {
                    var commitDetail = new CommitDetail(_repo, true);
                    commitDetail.Commit = commit;
                    DetailContext = commitDetail;
                }
            }
            else
            {
                DetailContext = null;
            }
        }

        public void Select(IList commits)
        {
            if (commits.Count == 0)
            {
                _repo.SelectedSearchedCommit = null;
                DetailContext = null;
            }
            else if (commits.Count == 1)
            {
                var commit = (commits[0] as Models.Commit)!;
                if (_repo.SelectedSearchedCommit == null || _repo.SelectedSearchedCommit.SHA != commit.SHA)
                    _repo.SelectedSearchedCommit = _repo.SearchedCommits.Find(x => x.SHA == commit.SHA);

                AutoSelectedCommit = commit;
                NavigationId = _navigationId + 1;

                if (_detailContext is CommitDetail detail)
                {
                    detail.Commit = commit;
                }
                else
                {
                    var commitDetail = new CommitDetail(_repo, true);
                    commitDetail.Commit = commit;
                    DetailContext = commitDetail;
                }
            }
            else if (commits.Count == 2)
            {
                _repo.SelectedSearchedCommit = null;

                var end = commits[0] as Models.Commit;
                var start = commits[1] as Models.Commit;
                DetailContext = new RevisionCompare(_repo.FullPath, start, end);
            }
            else
            {
                _repo.SelectedSearchedCommit = null;
                DetailContext = new Models.Count(commits.Count);
            }
        }

        public bool CheckoutBranchByDecorator(Models.Decorator decorator)
        {
            if (decorator == null)
                return false;

            if (decorator.Type == Models.DecoratorType.CurrentBranchHead ||
                decorator.Type == Models.DecoratorType.CurrentCommitHead)
                return true;

            if (decorator.Type == Models.DecoratorType.LocalBranchHead)
            {
                var b = _repo.Branches.Find(x => x.Name == decorator.Name);
                if (b == null)
                    return false;

                _repo.CheckoutBranch(b);
                return true;
            }

            if (decorator.Type == Models.DecoratorType.RemoteBranchHead)
            {
                var rb = _repo.Branches.Find(x => x.FriendlyName == decorator.Name);
                if (rb == null)
                    return false;

                var lb = _repo.Branches.Find(x => x.IsLocal && x.Upstream == rb.FullName);
                if (lb == null || lb.TrackStatus.Ahead.Count > 0)
                {
                    if (_repo.CanCreatePopup())
                        _repo.ShowPopup(new CreateBranch(_repo, rb));
                }
                else if (lb.TrackStatus.Behind.Count > 0)
                {
                    if (_repo.CanCreatePopup())
                        _repo.ShowPopup(new CheckoutAndFastForward(_repo, lb, rb));
                }
                else if (!lb.IsCurrent)
                {
                    _repo.CheckoutBranch(lb);
                }

                return true;
            }

            return false;
        }

        public void CheckoutBranchByCommit(Models.Commit commit)
        {
            if (commit.IsCurrentHead)
                return;

            Models.Branch firstRemoteBranch = null;
            foreach (var d in commit.Decorators)
            {
                if (d.Type == Models.DecoratorType.LocalBranchHead)
                {
                    var b = _repo.Branches.Find(x => x.Name == d.Name);
                    if (b == null)
                        continue;

                    _repo.CheckoutBranch(b);
                    return;
                }

                if (d.Type == Models.DecoratorType.RemoteBranchHead)
                {
                    var rb = _repo.Branches.Find(x => x.FriendlyName == d.Name);
                    if (rb == null)
                        continue;

                    var lb = _repo.Branches.Find(x => x.IsLocal && x.Upstream == rb.FullName);
                    if (lb is { TrackStatus.Ahead.Count: 0 })
                    {
                        if (_repo.CanCreatePopup())
                            _repo.ShowPopup(new CheckoutAndFastForward(_repo, lb, rb));
                        return;
                    }

                    firstRemoteBranch ??= rb;
                }
            }

            if (_repo.CanCreatePopup())
            {
                if (firstRemoteBranch != null)
                    _repo.ShowPopup(new CreateBranch(_repo, firstRemoteBranch));
                else if (!_repo.IsBare)
                    _repo.ShowPopup(new CheckoutCommit(_repo, commit));
            }
        }

        public ContextMenu CreateContextMenuForSelectedCommits(List<Models.Commit> selected, Action<Models.Commit> onAddSelected)
        {
            var current = _repo.CurrentBranch;
            if (current == null)
                return null;

            if (selected.Count > 1)
            {
                var canCherryPick = true;
                var canMerge = true;

                foreach (var c in selected)
                {
                    if (c.IsMerged)
                    {
                        canMerge = false;
                        canCherryPick = false;
                    }
                    else if (c.Parents.Count > 1)
                    {
                        canCherryPick = false;
                    }
                }

                var multipleMenu = new ContextMenu();

                if (!_repo.IsBare)
                {
                    if (canCherryPick)
                    {
                        var cherryPickMultiple = new MenuItem();
                        cherryPickMultiple.Header = App.Text("CommitCM.CherryPickMultiple");
                        cherryPickMultiple.Icon = App.CreateMenuIcon("Icons.CherryPick");
                        cherryPickMultiple.Click += (_, e) =>
                        {
                            if (_repo.CanCreatePopup())
                                _repo.ShowPopup(new CherryPick(_repo, selected));
                            e.Handled = true;
                        };
                        multipleMenu.Items.Add(cherryPickMultiple);
                    }

                    if (canMerge)
                    {
                        var mergeMultiple = new MenuItem();
                        mergeMultiple.Header = App.Text("CommitCM.MergeMultiple");
                        mergeMultiple.Icon = App.CreateMenuIcon("Icons.Merge");
                        mergeMultiple.Click += (_, e) =>
                        {
                            if (_repo.CanCreatePopup())
                                _repo.ShowPopup(new MergeMultiple(_repo, selected));
                            e.Handled = true;
                        };
                        multipleMenu.Items.Add(mergeMultiple);
                    }

                    if (canCherryPick || canMerge)
                        multipleMenu.Items.Add(new MenuItem() { Header = "-" });
                }

                var saveToPatchMultiple = new MenuItem();
                saveToPatchMultiple.Icon = App.CreateMenuIcon("Icons.Diff");
                saveToPatchMultiple.Header = App.Text("CommitCM.SaveAsPatch");
                saveToPatchMultiple.Click += async (_, e) =>
                {
                    var storageProvider = App.GetStorageProvider();
                    if (storageProvider == null)
                        return;

                    var options = new FolderPickerOpenOptions() { AllowMultiple = false };
                    CommandLog log = null;
                    try
                    {
                        var picker = await storageProvider.OpenFolderPickerAsync(options);
                        if (picker.Count == 1)
                        {
                            log = _repo.CreateLog("Save as Patch");

                            var folder = picker[0];
                            var folderPath = folder is { Path: { IsAbsoluteUri: true } path } ? path.LocalPath : folder.Path.ToString();
                            var succ = false;
                            for (var i = 0; i < selected.Count; i++)
                            {
                                var saveTo = GetPatchFileName(folderPath, selected[i], i);
                                succ = await new Commands.FormatPatch(_repo.FullPath, selected[i].SHA, saveTo).Use(log).ExecAsync();
                                if (!succ)
                                    break;
                            }

                            if (succ)
                                App.SendNotification(_repo.FullPath, App.Text("SaveAsPatchSuccess"));
                        }
                    }
                    catch (Exception exception)
                    {
                        App.RaiseException(_repo.FullPath, $"Failed to save as patch: {exception.Message}");
                    }

                    log?.Complete();
                    e.Handled = true;
                };
                multipleMenu.Items.Add(saveToPatchMultiple);
                multipleMenu.Items.Add(new MenuItem() { Header = "-" });

                var copyMultipleSHAs = new MenuItem();
                copyMultipleSHAs.Header = App.Text("CommitCM.CopySHA");
                copyMultipleSHAs.Icon = App.CreateMenuIcon("Icons.Fingerprint");
                copyMultipleSHAs.Click += async (_, e) =>
                {
                    var builder = new StringBuilder();
                    foreach (var c in selected)
                        builder.AppendLine(c.SHA);

                    await App.CopyTextAsync(builder.ToString());
                    e.Handled = true;
                };

                var copyMultipleInfo = new MenuItem();
                copyMultipleInfo.Header = App.Text("CommitCM.CopySHA") + " - " + App.Text("CommitCM.CopySubject");
                copyMultipleInfo.Icon = App.CreateMenuIcon("Icons.Info");
                copyMultipleInfo.Tag = OperatingSystem.IsMacOS() ? "⌘+C" : "Ctrl+C";
                copyMultipleInfo.Click += async (_, e) =>
                {
                    var builder = new StringBuilder();
                    foreach (var c in selected)
                        builder.Append(c.SHA.AsSpan(0, 10)).Append(" - ").AppendLine(c.Subject);

                    await App.CopyTextAsync(builder.ToString());
                    e.Handled = true;
                };

                var copyMultiple = new MenuItem();
                copyMultiple.Header = App.Text("Copy");
                copyMultiple.Icon = App.CreateMenuIcon("Icons.Copy");
                copyMultiple.Items.Add(copyMultipleSHAs);
                copyMultiple.Items.Add(copyMultipleInfo);
                multipleMenu.Items.Add(copyMultiple);

                return multipleMenu;
            }

            var commit = selected[0];
            var menu = new ContextMenu();
            var tags = new List<Models.Tag>();

            if (commit.HasDecorators)
            {
                foreach (var d in commit.Decorators)
                {
                    switch (d.Type)
                    {
                        case Models.DecoratorType.CurrentBranchHead:
                            FillCurrentBranchMenu(menu, current);
                            break;
                        case Models.DecoratorType.LocalBranchHead:
                            var lb = _repo.Branches.Find(x => x.IsLocal && d.Name == x.Name);
                            FillOtherLocalBranchMenu(menu, lb, current, commit.IsMerged);
                            break;
                        case Models.DecoratorType.RemoteBranchHead:
                            var rb = _repo.Branches.Find(x => !x.IsLocal && d.Name == x.FriendlyName);
                            FillRemoteBranchMenu(menu, rb, current, commit.IsMerged);
                            break;
                        case Models.DecoratorType.Tag:
                            var t = _repo.Tags.Find(x => x.Name == d.Name);
                            if (t != null)
                                tags.Add(t);
                            break;
                    }
                }

                if (menu.Items.Count > 0)
                    menu.Items.Add(new MenuItem() { Header = "-" });
            }

            if (tags.Count > 0)
            {
                foreach (var tag in tags)
                    FillTagMenu(menu, tag, current, commit.IsMerged);
                menu.Items.Add(new MenuItem() { Header = "-" });
            }

            var createBranch = new MenuItem();
            createBranch.Icon = App.CreateMenuIcon("Icons.Branch.Add");
            createBranch.Header = App.Text("CreateBranch");
            createBranch.Tag = OperatingSystem.IsMacOS() ? "⌘+⇧+B" : "Ctrl+Shift+B";
            createBranch.Click += (_, e) =>
            {
                if (_repo.CanCreatePopup())
                    _repo.ShowPopup(new CreateBranch(_repo, commit));
                e.Handled = true;
            };
            menu.Items.Add(createBranch);

            var createTag = new MenuItem();
            createTag.Icon = App.CreateMenuIcon("Icons.Tag.Add");
            createTag.Header = App.Text("CreateTag");
            createTag.Tag = OperatingSystem.IsMacOS() ? "⌘+⇧+T" : "Ctrl+Shift+T";
            createTag.Click += (_, e) =>
            {
                if (_repo.CanCreatePopup())
                    _repo.ShowPopup(new CreateTag(_repo, commit));
                e.Handled = true;
            };
            menu.Items.Add(createTag);
            menu.Items.Add(new MenuItem() { Header = "-" });

            if (!_repo.IsBare)
            {
                var target = commit.GetFriendlyName();

                if (current.Head != commit.SHA)
                {
                    var reset = new MenuItem();
                    reset.Header = App.Text("CommitCM.Reset", current.Name, target);
                    reset.Icon = App.CreateMenuIcon("Icons.Reset");
                    reset.Click += (_, e) =>
                    {
                        if (_repo.CanCreatePopup())
                            _repo.ShowPopup(new Reset(_repo, current, commit));
                        e.Handled = true;
                    };
                    menu.Items.Add(reset);
                }
                else
                {
                    var reword = new MenuItem();
                    reword.Header = App.Text("CommitCM.Reword");
                    reword.Icon = App.CreateMenuIcon("Icons.Edit");
                    reword.Click += (_, e) =>
                    {
                        if (_repo.CanCreatePopup())
                            _repo.ShowPopup(new Reword(_repo, commit));
                        e.Handled = true;
                    };
                    menu.Items.Add(reword);

                    var squash = new MenuItem();
                    squash.Header = App.Text("CommitCM.Squash");
                    squash.Icon = App.CreateMenuIcon("Icons.SquashIntoParent");
                    squash.IsEnabled = commit.Parents.Count == 1;
                    squash.Click += (_, e) =>
                    {
                        if (commit.Parents.Count == 1)
                        {
                            var parent = _commits.Find(x => x.SHA == commit.Parents[0]);
                            if (parent != null && _repo.CanCreatePopup())
                                _repo.ShowPopup(new Squash(_repo, parent, commit.SHA));
                        }

                        e.Handled = true;
                    };
                    menu.Items.Add(squash);
                }

                if (!commit.IsMerged)
                {
                    var rebase = new MenuItem();
                    rebase.Header = App.Text("CommitCM.Rebase", current.Name, target);
                    rebase.Icon = App.CreateMenuIcon("Icons.Rebase");
                    rebase.Click += (_, e) =>
                    {
                        if (_repo.CanCreatePopup())
                            _repo.ShowPopup(new Rebase(_repo, current, commit));
                        e.Handled = true;
                    };
                    menu.Items.Add(rebase);

                    if (!commit.HasDecorators)
                    {
                        var merge = new MenuItem();
                        merge.Header = App.Text("CommitCM.Merge", current.Name);
                        merge.Icon = App.CreateMenuIcon("Icons.Merge");
                        merge.Click += (_, e) =>
                        {
                            if (_repo.CanCreatePopup())
                                _repo.ShowPopup(new Merge(_repo, commit, current.Name));

                            e.Handled = true;
                        };
                        menu.Items.Add(merge);
                    }

                    var cherryPick = new MenuItem();
                    cherryPick.Header = App.Text("CommitCM.CherryPick");
                    cherryPick.Icon = App.CreateMenuIcon("Icons.CherryPick");
                    cherryPick.Click += async (_, e) =>
                    {
                        if (_repo.CanCreatePopup())
                        {
                            if (commit.Parents.Count <= 1)
                            {
                                _repo.ShowPopup(new CherryPick(_repo, [commit]));
                            }
                            else
                            {
                                var parents = new List<Models.Commit>();
                                foreach (var sha in commit.Parents)
                                {
                                    var parent = _commits.Find(x => x.SHA == sha);
                                    if (parent == null)
                                        parent = await new Commands.QuerySingleCommit(_repo.FullPath, sha)
                                            .GetResultAsync();

                                    if (parent != null)
                                        parents.Add(parent);
                                }

                                _repo.ShowPopup(new CherryPick(_repo, commit, parents));
                            }
                        }

                        e.Handled = true;
                    };
                    menu.Items.Add(cherryPick);
                }
                else
                {
                    var revert = new MenuItem();
                    revert.Header = App.Text("CommitCM.Revert");
                    revert.Icon = App.CreateMenuIcon("Icons.Undo");
                    revert.Click += (_, e) =>
                    {
                        if (_repo.CanCreatePopup())
                            _repo.ShowPopup(new Revert(_repo, commit));
                        e.Handled = true;
                    };
                    menu.Items.Add(revert);
                }

                if (current.Head != commit.SHA)
                {
                    var checkoutCommit = new MenuItem();
                    checkoutCommit.Header = App.Text("CommitCM.Checkout");
                    checkoutCommit.Icon = App.CreateMenuIcon("Icons.Detached");
                    checkoutCommit.Click += (_, e) =>
                    {
                        if (_repo.CanCreatePopup())
                            _repo.ShowPopup(new CheckoutCommit(_repo, commit));
                        e.Handled = true;
                    };
                    menu.Items.Add(checkoutCommit);

                    if (commit.IsMerged && commit.Parents.Count > 0)
                    {
                        var interactiveRebase = new MenuItem();
                        interactiveRebase.Header = App.Text("CommitCM.InteractiveRebase");
                        interactiveRebase.Icon = App.CreateMenuIcon("Icons.InteractiveRebase");

                        var manually = new MenuItem();
                        manually.Header = App.Text("CommitCM.InteractiveRebase.Manually", current.Name, target);
                        manually.Click += async (_, e) =>
                        {
                            await App.ShowDialog(new InteractiveRebase(_repo, commit));
                            e.Handled = true;
                        };

                        var reword = new MenuItem();
                        reword.Header = App.Text("CommitCM.InteractiveRebase.Reword");
                        reword.Click += async (_, e) =>
                        {
                            var prefill = new InteractiveRebasePrefill(commit.SHA, Models.InteractiveRebaseAction.Reword);
                            var on = await new Commands.QuerySingleCommit(_repo.FullPath, $"{commit.SHA}~").GetResultAsync();
                            await App.ShowDialog(new InteractiveRebase(_repo, on, prefill));
                            e.Handled = true;
                        };

                        var edit = new MenuItem();
                        edit.Header = App.Text("CommitCM.InteractiveRebase.Edit");
                        edit.Click += async (_, e) =>
                        {
                            var prefill = new InteractiveRebasePrefill(commit.SHA, Models.InteractiveRebaseAction.Edit);
                            var on = await new Commands.QuerySingleCommit(_repo.FullPath, $"{commit.SHA}~").GetResultAsync();
                            await App.ShowDialog(new InteractiveRebase(_repo, on, prefill));
                            e.Handled = true;
                        };

                        var squash = new MenuItem();
                        squash.Header = App.Text("CommitCM.InteractiveRebase.Squash");
                        squash.Click += async (_, e) =>
                        {
                            var prefill = new InteractiveRebasePrefill(commit.SHA, Models.InteractiveRebaseAction.Squash);
                            var on = await new Commands.QuerySingleCommit(_repo.FullPath, $"{commit.SHA}~~").GetResultAsync();
                            if (on != null)
                                await App.ShowDialog(new InteractiveRebase(_repo, on, prefill));
                            else
                                App.RaiseException(_repo.FullPath, $"Can not squash current commit into parent!");

                            e.Handled = true;
                        };

                        var fixup = new MenuItem();
                        fixup.Header = App.Text("CommitCM.InteractiveRebase.Fixup");
                        fixup.Click += async (_, e) =>
                        {
                            var prefill = new InteractiveRebasePrefill(commit.SHA, Models.InteractiveRebaseAction.Fixup);
                            var on = await new Commands.QuerySingleCommit(_repo.FullPath, $"{commit.SHA}~~").GetResultAsync();
                            if (on != null)
                                await App.ShowDialog(new InteractiveRebase(_repo, on, prefill));
                            else
                                App.RaiseException(_repo.FullPath, $"Can not fixup current commit into parent!");

                            e.Handled = true;
                        };

                        var drop = new MenuItem();
                        drop.Header = App.Text("CommitCM.InteractiveRebase.Drop");
                        drop.Click += async (_, e) =>
                        {
                            var prefill = new InteractiveRebasePrefill(commit.SHA, Models.InteractiveRebaseAction.Drop);
                            var on = await new Commands.QuerySingleCommit(_repo.FullPath, $"{commit.SHA}~").GetResultAsync();
                            await App.ShowDialog(new InteractiveRebase(_repo, on, prefill));
                            e.Handled = true;
                        };

                        interactiveRebase.Items.Add(manually);
                        interactiveRebase.Items.Add(new MenuItem() { Header = "-" });
                        interactiveRebase.Items.Add(reword);
                        interactiveRebase.Items.Add(edit);
                        interactiveRebase.Items.Add(squash);
                        interactiveRebase.Items.Add(fixup);
                        interactiveRebase.Items.Add(drop);

                        menu.Items.Add(new MenuItem() { Header = "-" });
                        menu.Items.Add(interactiveRebase);
                    }
                    else
                    {
                        var interactiveRebase = new MenuItem();
                        interactiveRebase.Header = App.Text("CommitCM.InteractiveRebase.Manually", current.Name, target);
                        interactiveRebase.Icon = App.CreateMenuIcon("Icons.InteractiveRebase");
                        interactiveRebase.Click += async (_, e) =>
                        {
                            await App.ShowDialog(new InteractiveRebase(_repo, commit));
                            e.Handled = true;
                        };

                        menu.Items.Add(new MenuItem() { Header = "-" });
                        menu.Items.Add(interactiveRebase);
                    }
                }

                menu.Items.Add(new MenuItem() { Header = "-" });
            }

            if (current.Head != commit.SHA)
            {
                if (current.TrackStatus.Ahead.Contains(commit.SHA))
                {
                    var upstream = _repo.Branches.Find(x => x.FullName.Equals(current.Upstream, StringComparison.Ordinal));
                    var pushRevision = new MenuItem();
                    pushRevision.Header = App.Text("CommitCM.PushRevision", commit.SHA.Substring(0, 10), upstream.FriendlyName);
                    pushRevision.Icon = App.CreateMenuIcon("Icons.Push");
                    pushRevision.Click += (_, e) =>
                    {
                        if (_repo.CanCreatePopup())
                            _repo.ShowPopup(new PushRevision(_repo, commit, upstream));
                        e.Handled = true;
                    };
                    menu.Items.Add(pushRevision);
                    menu.Items.Add(new MenuItem() { Header = "-" });
                }

                var compareWithHead = new MenuItem();
                compareWithHead.Header = App.Text("CommitCM.CompareWithHead");
                compareWithHead.Icon = App.CreateMenuIcon("Icons.Compare");
                compareWithHead.Click += async (_, e) =>
                {
                    var head = _commits.Find(x => x.SHA == current.Head);
                    if (head == null)
                    {
                        _repo.SelectedSearchedCommit = null;
                        head = await new Commands.QuerySingleCommit(_repo.FullPath, current.Head).GetResultAsync();
                        if (head != null)
                            DetailContext = new RevisionCompare(_repo.FullPath, commit, head);
                    }
                    else
                    {
                        onAddSelected?.Invoke(head);
                    }

                    e.Handled = true;
                };
                menu.Items.Add(compareWithHead);

                if (_repo.LocalChangesCount > 0)
                {
                    var compareWithWorktree = new MenuItem();
                    compareWithWorktree.Header = App.Text("CommitCM.CompareWithWorktree");
                    compareWithWorktree.Icon = App.CreateMenuIcon("Icons.Compare");
                    compareWithWorktree.Click += (_, e) =>
                    {
                        DetailContext = new RevisionCompare(_repo.FullPath, commit, null);
                        e.Handled = true;
                    };
                    menu.Items.Add(compareWithWorktree);
                }

                menu.Items.Add(new MenuItem() { Header = "-" });
            }

            var saveToPatch = new MenuItem();
            saveToPatch.Icon = App.CreateMenuIcon("Icons.Diff");
            saveToPatch.Header = App.Text("CommitCM.SaveAsPatch");
            saveToPatch.Click += async (_, e) =>
            {
                var storageProvider = App.GetStorageProvider();
                if (storageProvider == null)
                    return;

                var options = new FolderPickerOpenOptions() { AllowMultiple = false };
                CommandLog log = null;
                try
                {
                    var selected = await storageProvider.OpenFolderPickerAsync(options);
                    if (selected.Count == 1)
                    {
                        log = _repo.CreateLog("Save as Patch");

                        var folder = selected[0];
                        var folderPath = folder is { Path: { IsAbsoluteUri: true } path } ? path.LocalPath : folder.Path.ToString();
                        var saveTo = GetPatchFileName(folderPath, commit);
                        var succ = await new Commands.FormatPatch(_repo.FullPath, commit.SHA, saveTo).Use(log).ExecAsync();
                        if (succ)
                            App.SendNotification(_repo.FullPath, App.Text("SaveAsPatchSuccess"));
                    }
                }
                catch (Exception exception)
                {
                    App.RaiseException(_repo.FullPath, $"Failed to save as patch: {exception.Message}");
                }

                log?.Complete();
                e.Handled = true;
            };
            menu.Items.Add(saveToPatch);

            var archive = new MenuItem();
            archive.Icon = App.CreateMenuIcon("Icons.Archive");
            archive.Header = App.Text("Archive");
            archive.Click += (_, e) =>
            {
                if (_repo.CanCreatePopup())
                    _repo.ShowPopup(new Archive(_repo, commit));
                e.Handled = true;
            };
            menu.Items.Add(archive);
            menu.Items.Add(new MenuItem() { Header = "-" });

            var actions = _repo.GetCustomActions(Models.CustomActionScope.Commit);
            if (actions.Count > 0)
            {
                var custom = new MenuItem();
                custom.Header = App.Text("CommitCM.CustomAction");
                custom.Icon = App.CreateMenuIcon("Icons.Action");

                foreach (var action in actions)
                {
                    var (dup, label) = action;
                    var item = new MenuItem();
                    item.Icon = App.CreateMenuIcon("Icons.Action");
                    item.Header = label;
                    item.Click += (_, e) =>
                    {
                        _repo.ExecCustomAction(dup, commit);
                        e.Handled = true;
                    };

                    custom.Items.Add(item);
                }

                menu.Items.Add(custom);
                menu.Items.Add(new MenuItem() { Header = "-" });
            }

            var copySHA = new MenuItem();
            copySHA.Header = App.Text("CommitCM.CopySHA");
            copySHA.Icon = App.CreateMenuIcon("Icons.Fingerprint");
            copySHA.Click += async (_, e) =>
            {
                await App.CopyTextAsync(commit.SHA);
                e.Handled = true;
            };

            var copySubject = new MenuItem();
            copySubject.Header = App.Text("CommitCM.CopySubject");
            copySubject.Icon = App.CreateMenuIcon("Icons.Subject");
            copySubject.Click += async (_, e) =>
            {
                await App.CopyTextAsync(commit.Subject);
                e.Handled = true;
            };

            var copyInfo = new MenuItem();
            copyInfo.Header = App.Text("CommitCM.CopySHA") + " - " + App.Text("CommitCM.CopySubject");
            copyInfo.Icon = App.CreateMenuIcon("Icons.Info");
            copyInfo.Tag = OperatingSystem.IsMacOS() ? "⌘+C" : "Ctrl+C";
            copyInfo.Click += async (_, e) =>
            {
                await App.CopyTextAsync($"{commit.SHA.AsSpan(0, 10)} - {commit.Subject}");
                e.Handled = true;
            };

            var copyMessage = new MenuItem();
            copyMessage.Header = App.Text("CommitCM.CopyCommitMessage");
            copyMessage.Icon = App.CreateMenuIcon("Icons.Info");
            copyMessage.Click += async (_, e) =>
            {
                var message = await new Commands.QueryCommitFullMessage(_repo.FullPath, commit.SHA).GetResultAsync();
                await App.CopyTextAsync(message);
                e.Handled = true;
            };

            var copyAuthor = new MenuItem();
            copyAuthor.Header = App.Text("CommitCM.CopyAuthor");
            copyAuthor.Icon = App.CreateMenuIcon("Icons.User");
            copyAuthor.Click += async (_, e) =>
            {
                await App.CopyTextAsync(commit.Author.ToString());
                e.Handled = true;
            };

            var copyCommitter = new MenuItem();
            copyCommitter.Header = App.Text("CommitCM.CopyCommitter");
            copyCommitter.Icon = App.CreateMenuIcon("Icons.User");
            copyCommitter.Click += async (_, e) =>
            {
                await App.CopyTextAsync(commit.Committer.ToString());
                e.Handled = true;
            };

            var copy = new MenuItem();
            copy.Header = App.Text("Copy");
            copy.Icon = App.CreateMenuIcon("Icons.Copy");
            copy.Items.Add(copySHA);
            copy.Items.Add(copySubject);
            copy.Items.Add(copyInfo);
            copy.Items.Add(copyMessage);
            copy.Items.Add(copyAuthor);
            copy.Items.Add(copyCommitter);
            menu.Items.Add(copy);

            return menu;
        }

        private void FillCurrentBranchMenu(ContextMenu menu, Models.Branch current)
        {
            var submenu = new MenuItem();
            submenu.Icon = App.CreateMenuIcon("Icons.Branch");
            submenu.Header = current.Name;

            var visibility = new MenuItem();
            visibility.Classes.Add("filter_mode_switcher");
            visibility.Header = new FilterModeInGraph(_repo, current);
            submenu.Items.Add(visibility);
            submenu.Items.Add(new MenuItem() { Header = "-" });

            if (!string.IsNullOrEmpty(current.Upstream))
            {
                var upstream = current.Upstream.Substring(13);

                var fastForward = new MenuItem();
                fastForward.Header = App.Text("BranchCM.FastForward", upstream);
                fastForward.Icon = App.CreateMenuIcon("Icons.FastForward");
                fastForward.IsEnabled = current.TrackStatus.Ahead.Count == 0;
                fastForward.Click += (_, e) =>
                {
                    var b = _repo.Branches.Find(x => x.FriendlyName == upstream);
                    if (b == null)
                        return;

                    if (_repo.CanCreatePopup())
                        _repo.ShowAndStartPopup(new Merge(_repo, b, current.Name, true));

                    e.Handled = true;
                };
                submenu.Items.Add(fastForward);

                var pull = new MenuItem();
                pull.Header = App.Text("BranchCM.Pull", upstream);
                pull.Icon = App.CreateMenuIcon("Icons.Pull");
                pull.Click += (_, e) =>
                {
                    if (_repo.CanCreatePopup())
                        _repo.ShowPopup(new Pull(_repo, null));
                    e.Handled = true;
                };
                submenu.Items.Add(pull);
            }

            var push = new MenuItem();
            push.Header = App.Text("BranchCM.Push", current.Name);
            push.Icon = App.CreateMenuIcon("Icons.Push");
            push.IsEnabled = _repo.Remotes.Count > 0;
            push.Click += (_, e) =>
            {
                if (_repo.CanCreatePopup())
                    _repo.ShowPopup(new Push(_repo, current));
                e.Handled = true;
            };
            submenu.Items.Add(push);

            var rename = new MenuItem();
            rename.Header = App.Text("BranchCM.Rename", current.Name);
            rename.Icon = App.CreateMenuIcon("Icons.Rename");
            rename.Click += (_, e) =>
            {
                if (_repo.CanCreatePopup())
                    _repo.ShowPopup(new RenameBranch(_repo, current));
                e.Handled = true;
            };
            submenu.Items.Add(rename);
            submenu.Items.Add(new MenuItem() { Header = "-" });

            if (!_repo.IsBare)
            {
                var type = _repo.GetGitFlowType(current);
                if (type != Models.GitFlowBranchType.None)
                {
                    var finish = new MenuItem();
                    finish.Header = App.Text("BranchCM.Finish", current.Name);
                    finish.Icon = App.CreateMenuIcon("Icons.GitFlow");
                    finish.Click += (_, e) =>
                    {
                        if (_repo.CanCreatePopup())
                            _repo.ShowPopup(new GitFlowFinish(_repo, current, type));
                        e.Handled = true;
                    };
                    submenu.Items.Add(finish);
                    submenu.Items.Add(new MenuItem() { Header = "-" });
                }
            }

            var copy = new MenuItem();
            copy.Header = App.Text("BranchCM.CopyName");
            copy.Icon = App.CreateMenuIcon("Icons.Copy");
            copy.Click += async (_, e) =>
            {
                await App.CopyTextAsync(current.Name);
                e.Handled = true;
            };
            submenu.Items.Add(copy);

            menu.Items.Add(submenu);
        }

        private void FillOtherLocalBranchMenu(ContextMenu menu, Models.Branch branch, Models.Branch current, bool merged)
        {
            var submenu = new MenuItem();
            submenu.Icon = App.CreateMenuIcon("Icons.Branch");
            submenu.Header = branch.Name;

            var visibility = new MenuItem();
            visibility.Classes.Add("filter_mode_switcher");
            visibility.Header = new FilterModeInGraph(_repo, branch);
            submenu.Items.Add(visibility);
            submenu.Items.Add(new MenuItem() { Header = "-" });

            if (!_repo.IsBare)
            {
                var checkout = new MenuItem();
                checkout.Header = App.Text("BranchCM.Checkout", branch.Name);
                checkout.Icon = App.CreateMenuIcon("Icons.Check");
                checkout.Click += (_, e) =>
                {
                    _repo.CheckoutBranch(branch);
                    e.Handled = true;
                };
                submenu.Items.Add(checkout);

                var merge = new MenuItem();
                merge.Header = App.Text("BranchCM.Merge", branch.Name, current.Name);
                merge.Icon = App.CreateMenuIcon("Icons.Merge");
                merge.IsEnabled = !merged;
                merge.Click += (_, e) =>
                {
                    if (_repo.CanCreatePopup())
                        _repo.ShowPopup(new Merge(_repo, branch, current.Name, false));
                    e.Handled = true;
                };
                submenu.Items.Add(merge);
            }

            var rename = new MenuItem();
            rename.Header = App.Text("BranchCM.Rename", branch.Name);
            rename.Icon = App.CreateMenuIcon("Icons.Rename");
            rename.Click += (_, e) =>
            {
                if (_repo.CanCreatePopup())
                    _repo.ShowPopup(new RenameBranch(_repo, branch));
                e.Handled = true;
            };
            submenu.Items.Add(rename);

            var delete = new MenuItem();
            delete.Header = App.Text("BranchCM.Delete", branch.Name);
            delete.Icon = App.CreateMenuIcon("Icons.Clear");
            delete.Click += (_, e) =>
            {
                if (_repo.CanCreatePopup())
                    _repo.ShowPopup(new DeleteBranch(_repo, branch));
                e.Handled = true;
            };
            submenu.Items.Add(delete);
            submenu.Items.Add(new MenuItem() { Header = "-" });

            if (!_repo.IsBare)
            {
                var type = _repo.GetGitFlowType(branch);
                if (type != Models.GitFlowBranchType.None)
                {
                    var finish = new MenuItem();
                    finish.Header = App.Text("BranchCM.Finish", branch.Name);
                    finish.Icon = App.CreateMenuIcon("Icons.GitFlow");
                    finish.Click += (_, e) =>
                    {
                        if (_repo.CanCreatePopup())
                            _repo.ShowPopup(new GitFlowFinish(_repo, branch, type));
                        e.Handled = true;
                    };
                    submenu.Items.Add(finish);
                    submenu.Items.Add(new MenuItem() { Header = "-" });
                }
            }

            var copy = new MenuItem();
            copy.Header = App.Text("BranchCM.CopyName");
            copy.Icon = App.CreateMenuIcon("Icons.Copy");
            copy.Click += async (_, e) =>
            {
                await App.CopyTextAsync(branch.Name);
                e.Handled = true;
            };
            submenu.Items.Add(copy);

            menu.Items.Add(submenu);
        }

        private void FillRemoteBranchMenu(ContextMenu menu, Models.Branch branch, Models.Branch current, bool merged)
        {
            var name = branch.FriendlyName;

            var submenu = new MenuItem();
            submenu.Icon = App.CreateMenuIcon("Icons.Branch");
            submenu.Header = name;

            var visibility = new MenuItem();
            visibility.Classes.Add("filter_mode_switcher");
            visibility.Header = new FilterModeInGraph(_repo, branch);
            submenu.Items.Add(visibility);
            submenu.Items.Add(new MenuItem() { Header = "-" });

            var checkout = new MenuItem();
            checkout.Header = App.Text("BranchCM.Checkout", name);
            checkout.Icon = App.CreateMenuIcon("Icons.Check");
            checkout.Click += (_, e) =>
            {
                _repo.CheckoutBranch(branch);
                e.Handled = true;
            };
            submenu.Items.Add(checkout);

            var merge = new MenuItem();
            merge.Header = App.Text("BranchCM.Merge", name, current.Name);
            merge.Icon = App.CreateMenuIcon("Icons.Merge");
            merge.IsEnabled = !merged;
            merge.Click += (_, e) =>
            {
                if (_repo.CanCreatePopup())
                    _repo.ShowPopup(new Merge(_repo, branch, current.Name, false));
                e.Handled = true;
            };

            submenu.Items.Add(merge);

            var delete = new MenuItem();
            delete.Header = App.Text("BranchCM.Delete", name);
            delete.Icon = App.CreateMenuIcon("Icons.Clear");
            delete.Click += (_, e) =>
            {
                if (_repo.CanCreatePopup())
                    _repo.ShowPopup(new DeleteBranch(_repo, branch));
                e.Handled = true;
            };
            submenu.Items.Add(delete);
            submenu.Items.Add(new MenuItem() { Header = "-" });

            var copy = new MenuItem();
            copy.Header = App.Text("BranchCM.CopyName");
            copy.Icon = App.CreateMenuIcon("Icons.Copy");
            copy.Click += async (_, e) =>
            {
                await App.CopyTextAsync(name);
                e.Handled = true;
            };
            submenu.Items.Add(copy);

            menu.Items.Add(submenu);
        }

        private void FillTagMenu(ContextMenu menu, Models.Tag tag, Models.Branch current, bool merged)
        {
            var submenu = new MenuItem();
            submenu.Header = tag.Name;
            submenu.Icon = App.CreateMenuIcon("Icons.Tag");
            submenu.MinWidth = 200;

            var visibility = new MenuItem();
            visibility.Classes.Add("filter_mode_switcher");
            visibility.Header = new FilterModeInGraph(_repo, tag);
            submenu.Items.Add(visibility);
            submenu.Items.Add(new MenuItem() { Header = "-" });

            var push = new MenuItem();
            push.Header = App.Text("TagCM.Push", tag.Name);
            push.Icon = App.CreateMenuIcon("Icons.Push");
            push.IsEnabled = _repo.Remotes.Count > 0;
            push.Click += (_, e) =>
            {
                if (_repo.CanCreatePopup())
                    _repo.ShowPopup(new PushTag(_repo, tag));
                e.Handled = true;
            };
            submenu.Items.Add(push);

            if (!_repo.IsBare && !merged)
            {
                var merge = new MenuItem();
                merge.Header = App.Text("TagCM.Merge", tag.Name, current.Name);
                merge.Icon = App.CreateMenuIcon("Icons.Merge");
                merge.Click += (_, e) =>
                {
                    if (_repo.CanCreatePopup())
                        _repo.ShowPopup(new Merge(_repo, tag, current.Name));
                    e.Handled = true;
                };
                submenu.Items.Add(merge);
            }

            var delete = new MenuItem();
            delete.Header = App.Text("TagCM.Delete", tag.Name);
            delete.Icon = App.CreateMenuIcon("Icons.Clear");
            delete.Click += (_, e) =>
            {
                if (_repo.CanCreatePopup())
                    _repo.ShowPopup(new DeleteTag(_repo, tag));
                e.Handled = true;
            };
            submenu.Items.Add(delete);
            submenu.Items.Add(new MenuItem() { Header = "-" });

            var copy = new MenuItem();
            copy.Header = App.Text("TagCM.Copy");
            copy.Icon = App.CreateMenuIcon("Icons.Copy");
            copy.Click += async (_, e) =>
            {
                await App.CopyTextAsync(tag.Name);
                e.Handled = true;
            };
            submenu.Items.Add(copy);

            menu.Items.Add(submenu);
        }

        private string GetPatchFileName(string dir, Models.Commit commit, int index = 0)
        {
            var ignore_chars = new HashSet<char> { '/', '\\', ':', ',', '*', '?', '\"', '<', '>', '|', '`', '$', '^', '%', '[', ']', '+', '-' };
            var builder = new StringBuilder();
            builder.Append(index.ToString("D4"));
            builder.Append('-');

            var chars = commit.Subject.ToCharArray();
            var len = 0;
            foreach (var c in chars)
            {
                if (!ignore_chars.Contains(c))
                {
                    if (c == ' ' || c == '\t')
                        builder.Append('-');
                    else
                        builder.Append(c);

                    len++;

                    if (len >= 48)
                        break;
                }
            }
            builder.Append(".patch");

            return Path.Combine(dir, builder.ToString());
        }

        private Repository _repo = null;
        private bool _isLoading = true;
        private List<Models.Commit> _commits = new List<Models.Commit>();
        private Models.CommitGraph _graph = null;
        private Models.Commit _autoSelectedCommit = null;
        private long _navigationId = 0;
        private IDisposable _detailContext = null;

        private Models.Bisect _bisect = null;

        private GridLength _leftArea = new GridLength(1, GridUnitType.Star);
        private GridLength _rightArea = new GridLength(1, GridUnitType.Star);
        private GridLength _topArea = new GridLength(1, GridUnitType.Star);
        private GridLength _bottomArea = new GridLength(1, GridUnitType.Star);
    }
}
