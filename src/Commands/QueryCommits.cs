using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace SourceGit.Commands
{
    public class QueryCommits : Command
    {
        private List<Models.Commit> _commits = new List<Models.Commit>();
        private Models.Commit _current = null;
        private bool _findFirstMerged = false;
        private bool _isHeadFound = false;
        private string _fileNameFilter = string.Empty;
        private Models.CommitSearchMethod _method;
        private int _totalLogCount = 0; // 记录原始查询的日志数量

        public int TotalLogCount => _totalLogCount;

        public QueryCommits(string repo, string limits, bool needFindHead = true)
        {
            WorkingDirectory = repo;
            Context = repo;
            Args = $"log --no-show-signature --decorate=full --format=%H%n%P%n%D%n%aN±%aE%n%at%n%cN±%cE%n%ct%n%s {limits}";
            _findFirstMerged = needFindHead;
        }

        public QueryCommits(string repo, string filter, Models.CommitSearchMethod method, bool onlyCurrentBranch, int skip = 0)
        {
            string search = onlyCurrentBranch ? string.Empty : "--branches --remotes ";
            _method = method;
            
            if (method == Models.CommitSearchMethod.ByAuthor)
            {
                search += $"-i --author={filter.Quoted()}";
            }
            else if (method == Models.CommitSearchMethod.ByCommitter)
            {
                search += $"-i --committer={filter.Quoted()}";
            }
            else if (method == Models.CommitSearchMethod.ByMessage)
            {
                var argsBuilder = new StringBuilder();
                argsBuilder.Append(search);

                var words = filter.Split([' ', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries);
                foreach (var word in words)
                    argsBuilder.Append("--grep=").Append(word.Trim().Quoted()).Append(' ');
                argsBuilder.Append("--all-match -i");

                search = argsBuilder.ToString();
            }
            else if (method == Models.CommitSearchMethod.ByPath)
            {
                search += $"-- {filter.Quoted()}";
            }
            else if (method == Models.CommitSearchMethod.ByFileName)
            {
                // 使用 --name-only 来获取文件列表，使用自定义格式
                search += $"--name-only --format=%H%n%P%n%D%n%aN±%aE%n%at%n%cN±%cE%n%ct%n%s%n";
                _fileNameFilter = filter;
            }
            else
            {
                search = $"-G{filter.Quoted()}";
            }

            WorkingDirectory = repo;
            Context = repo;
            
            // 为byFileName方法添加skip参数支持
            if (method == Models.CommitSearchMethod.ByFileName && skip > 0)
            {
                Args = $"log -1000 --skip={skip} --date-order --no-show-signature --decorate=full --format=%H%n%P%n%D%n%aN±%aE%n%at%n%cN±%cE%n%ct%n%s {search}";
            }
            else
            {
                Args = $"log -1000 --date-order --no-show-signature --decorate=full --format=%H%n%P%n%D%n%aN±%aE%n%at%n%cN±%cE%n%ct%n%s {search}";
            }
            
            _findFirstMerged = false;
        }

        public async Task<List<Models.Commit>> GetResultAsync()
        {
            var rs = await ReadToEndAsync().ConfigureAwait(false);
            if (!rs.IsSuccess)
                return _commits;

            if (_method == Models.CommitSearchMethod.ByFileName)
            {
                return ParseWithNameOnlyAsync(rs.StdOut);
            }

            var nextPartIdx = 0;
            var start = 0;
            var end = rs.StdOut.IndexOf('\n', start);
            while (end > 0)
            {
                var line = rs.StdOut.Substring(start, end - start);
                switch (nextPartIdx)
                {
                    case 0:
                        _current = new Models.Commit() { SHA = line };
                        _commits.Add(_current);
                        break;
                    case 1:
                        ParseParent(line, _current);
                        break;
                    case 2:
                        _current.ParseDecorators(line);
                        if (_current.IsMerged && !_isHeadFound)
                            _isHeadFound = true;
                        break;
                    case 3:
                        _current.Author = Models.User.FindOrAdd(line);
                        break;
                    case 4:
                        _current.AuthorTime = ulong.Parse(line);
                        break;
                    case 5:
                        _current.Committer = Models.User.FindOrAdd(line);
                        break;
                    case 6:
                        _current.CommitterTime = ulong.Parse(line);
                        break;
                    case 7:
                        _current.Subject = line;
                        nextPartIdx = -1;
                        break;
                }

                nextPartIdx++;

                start = end + 1;
                end = rs.StdOut.IndexOf('\n', start);
            }

            if (start < rs.StdOut.Length)
                _current.Subject = rs.StdOut.Substring(start);

            if (_findFirstMerged && !_isHeadFound && _commits.Count > 0)
                await MarkFirstMergedAsync().ConfigureAwait(false);

            return _commits;
        }

        private void ParseParent(string data, Models.Commit commit)
        {
            if (data.Length < 8)
                return;

            commit.Parents.AddRange(data.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        private async Task MarkFirstMergedAsync()
        {
            Args = $"log --since={_commits[^1].CommitterTimeStr.Quoted()} --format=\"%H\"";

            var rs = await ReadToEndAsync().ConfigureAwait(false);
            var shas = rs.StdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (shas.Length == 0)
                return;

            var set = new HashSet<string>(shas);

            foreach (var c in _commits)
            {
                if (set.Contains(c.SHA))
                {
                    c.IsMerged = true;
                    break;
                }
            }
        }

        private List<Models.Commit> ParseWithNameOnlyAsync(string output)
        {
            var commits = new List<Models.Commit>();
            var lines = output.Split('\n');
            Models.Commit currentCommit = new Models.Commit();
            int nextPartIdx = 0;
            bool hasMatchingFile = false;
            bool inCommitInfo = false;
            _totalLogCount = 0; 

            foreach (var line in lines)
            {
                // 新的 commit 开始
                if (!inCommitInfo && line.Length == 40 && Regex.IsMatch(line, "^[a-fA-F0-9]+$"))
                {
                    _totalLogCount++; // 统计原始日志数量
                    
                    if (currentCommit != null && hasMatchingFile)
                    {
                        commits.Add(currentCommit);
                        currentCommit = new Models.Commit();
                    }

                    currentCommit.SHA = line;
                    currentCommit.Parents.Clear();
                    currentCommit.Decorators.Clear();

                    nextPartIdx = 1;
                    hasMatchingFile = false;
                    inCommitInfo = true;
                    continue;
                }

                // 填充 commit 字段
                if (inCommitInfo && nextPartIdx < 8)
                {
                    switch (nextPartIdx)
                    {
                        case 1: ParseParent(line, currentCommit); break;
                        case 2: currentCommit.ParseDecorators(line); break;
                        case 3: currentCommit.Author = Models.User.FindOrAdd(line); break;
                        case 4: currentCommit.AuthorTime = ulong.Parse(line); break;
                        case 5: currentCommit.Committer = Models.User.FindOrAdd(line); break;
                        case 6: currentCommit.CommitterTime = ulong.Parse(line); break;
                        case 7: currentCommit.Subject = line; inCommitInfo = false; break;
                    }
                    nextPartIdx++;
                }
                else
                {
                    if (!string.IsNullOrEmpty(line) && currentCommit != null && !string.IsNullOrEmpty(_fileNameFilter))
                    {
                        string fileName = Path.GetFileName(line);
                        if (fileName.Contains(_fileNameFilter, StringComparison.OrdinalIgnoreCase))
                            hasMatchingFile = true;
                    }
                }
            }

            // 处理最后一个 commit
            if (currentCommit != null && hasMatchingFile)
                commits.Add(currentCommit);

            return commits;
        }
    }
}
