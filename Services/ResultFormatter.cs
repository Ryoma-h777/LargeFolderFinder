using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace LargeFolderFinder
{
    public class ResultFormatter
    {
        public int CalculateMaxLineLength(
            FolderInfo node,
            ConcurrentDictionary<FolderInfo, List<FolderInfo>> filterCache,
            int indentLen,
            bool isRoot,
            bool isLast,
            long thresholdBytes,
            bool includeFiles)
        {
            if (node == null ||
                (!isRoot && node.Size < thresholdBytes) ||
                (!isRoot && node.IsFile && !includeFiles)) return 0;

            int prefixLen = isRoot ? 0 : GetStringWidth(isLast ? AppConstants.TreeLastBranch : AppConstants.TreeBranch);
            int currentLen = indentLen + prefixLen + GetStringWidth(node.Name);
            int max = currentLen;

            // インデント幅の統一: Lastの場合もスペース3つ分
            int childIndentLen = indentLen + (isRoot
                ? 0
                : GetStringWidth(isLast
                    ? AppConstants.TreeSpace + AppConstants.TreeSpace + AppConstants.TreeSpace
                    : AppConstants.TreeVertical + AppConstants.TreeSpace));

            if (node.Children != null)
            {
                List<FolderInfo>? list = null;
                if (filterCache.TryGetValue(node, out var cachedList))
                {
                    list = cachedList;
                }
                else
                {
                    lock (node.Children)
                    {
                        list = node.Children.Where(c => c.Size >= thresholdBytes && (includeFiles || !c.IsFile)).ToList();
                    }
                }

                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        // IsExpanded チェックを追加
                        if (!node.IsExpanded && !node.IsFile) continue;

                        int childMax = CalculateMaxLineLength(
                            list[i],
                            filterCache,
                            childIndentLen,
                            isRoot: false,
                            isLast: i == list.Count - 1,
                            thresholdBytes,
                            includeFiles);
                        if (childMax > max) max = childMax;
                    }
                }
            }
            return max;
        }

        public void PrintTreeRecursive(
            StringBuilder sb,
            FolderInfo node,
            ConcurrentDictionary<FolderInfo, List<FolderInfo>> filterCache,
            string indent,
            bool isLast,
            bool isRoot,
            int targetColumn,
            bool useSpaces,
            int tabWidth,
            long thresholdBytes,
            AppConstants.SizeUnit unit,
            bool includeFiles,
            CancellationToken token = default)
        {
            if (token.IsCancellationRequested) return;

            if (node == null ||
                (!isRoot && node.Size < thresholdBytes) ||
                (!isRoot && node.IsFile && !includeFiles))
                return;

            if (isRoot && node.Size < thresholdBytes)
            {
                sb.Append(AppConstants.TreeLastBranch).AppendLine(LocalizationManager.Instance.GetText(LanguageKey.NotFoundMessage));
                return;
            }

            string sizeStr = $"{(double)node.Size / AppConstants.GetBytesPerUnit(unit):N0} {unit}".PadLeft(AppConstants.BaseSizeLength);
            string line = isRoot
                ? node.Name
                : indent + (isLast ? AppConstants.TreeLastBranch : AppConstants.TreeBranch) + node.Name;
            sb.Append(line);

            int curLen = GetStringWidth(line);
            // if (useSpaces)
            //     sb.Append(' ', targetColumn - curLen);
            // else
            //     //                sb.Append('\t', 1 + (((tabWidth-1)+targetColumn) / tabWidth) - (((tabWidth-1)+curLen) / tabWidth) );
            //     sb.Append('\t', (targetColumn - curLen) / tabWidth);
            while (curLen < targetColumn)
            {
                if (useSpaces)
                    sb.Append(' ');
                else
                    sb.Append('\t');
                curLen += (useSpaces ? 1 : tabWidth - (curLen % tabWidth));
            }

            sb.Append(sizeStr).AppendLine();

            if (node.Children != null)
            {
                List<FolderInfo>? list = null;
                if (filterCache.TryGetValue(node, out var cachedList))
                {
                    list = cachedList;
                }
                else
                {
                    lock (node.Children)
                    {
                        list = node.Children.Where(c => c.Size >= thresholdBytes && (includeFiles || !c.IsFile)).ToList();
                    }
                }

                if (list != null)
                {
                    string childIndent = indent + (isRoot
                        ? ""
                        : (isLast
                            ? AppConstants.TreeSpace + AppConstants.TreeSpace + AppConstants.TreeSpace
                            : AppConstants.TreeVertical + AppConstants.TreeSpace));

                    for (int i = 0; i < list.Count; i++)
                    {
                        // IsExpanded チェックを追加
                        if (!node.IsExpanded && !node.IsFile) continue;

                        PrintTreeRecursive(
                            sb,
                            node: list[i],
                            filterCache,
                            childIndent,
                            isLast: i == list.Count - 1,
                            isRoot: false,
                            targetColumn,
                            useSpaces,
                            tabWidth,
                            thresholdBytes,
                            unit,
                            includeFiles,
                            token);
                    }
                }
            }
        }

        public IEnumerable<FolderRowItem> GenerateListItemsRecursive(
            FolderInfo node,
            ConcurrentDictionary<FolderInfo, List<FolderInfo>> filterCache,
            string indent,
            bool isLast,
            bool isRoot,
            int targetColumn,
            bool useSpaces,
            int tabWidth,
            long thresholdBytes,
            AppConstants.SizeUnit unit,
            bool includeFiles)
        {
            if (node == null ||
                (!isRoot && node.Size < thresholdBytes) ||
                (!isRoot && node.IsFile && !includeFiles))
                yield break;

            if (isRoot && node.Size < thresholdBytes)
            {
                yield return new FolderRowItem
                (
                    node: node,
                    displayText: AppConstants.TreeLastBranch + LocalizationManager.Instance.GetText(LanguageKey.NotFoundMessage),
                    sizeText: "",
                    indentedName: AppConstants.TreeLastBranch + LocalizationManager.Instance.GetText(LanguageKey.NotFoundMessage),
                    displayType: ""
                );
                yield break;
            }

            string sizeStr = $"{(double)node.Size / AppConstants.GetBytesPerUnit(unit):N0} {unit}".PadLeft(AppConstants.BaseSizeLength);
            string baseLine = isRoot
                ? node.Name
                : indent + (isLast ? AppConstants.TreeLastBranch : AppConstants.TreeBranch) + node.Name;

            // Padding calculation for DisplayText (Legacy/Clipboard support)
            string paddedLine = baseLine;
            int curLen = GetStringWidth(baseLine);
            int paddingNeeded = targetColumn - curLen;
            if (paddingNeeded > 0)
            {
                if (useSpaces)
                {
                    paddedLine += new string(' ', paddingNeeded);
                }
                else
                {
                    var sb = new StringBuilder();
                    while (curLen < targetColumn)
                    {
                        if (useSpaces) sb.Append(' '); else sb.Append('\t');
                        curLen += (useSpaces ? 1 : tabWidth - (curLen % tabWidth));
                    }
                    paddedLine += sb.ToString();
                }
            }

            string displayType = node.IsFile ? System.IO.Path.GetExtension(node.Name) : "";

            yield return new FolderRowItem
            (
                node: node,
                displayText: paddedLine + sizeStr,
                sizeText: sizeStr,
                indentedName: baseLine,
                displayType: displayType
            );

            if (node.Children != null)
            {
                // IsExpanded チェック
                if (!node.IsExpanded && !node.IsFile) yield break;

                List<FolderInfo>? list = null;
                if (filterCache.TryGetValue(node, out var cachedList))
                {
                    list = cachedList;
                }
                else
                {
                    lock (node.Children)
                    {
                        list = node.Children.Where(c => c.Size >= thresholdBytes && (includeFiles || !c.IsFile)).ToList();
                    }
                }

                if (list != null)
                {
                    string childIndent = indent + (isRoot
                        ? ""
                        : (isLast
                            ? AppConstants.TreeSpace + AppConstants.TreeSpace + AppConstants.TreeSpace
                            : AppConstants.TreeVertical + AppConstants.TreeSpace));

                    for (int i = 0; i < list.Count; i++)
                    {
                        foreach (var item in GenerateListItemsRecursive(
                            list[i],
                            filterCache,
                            childIndent,
                            isLast: i == list.Count - 1,
                            isRoot: false,
                            targetColumn,
                            useSpaces,
                            tabWidth,
                            thresholdBytes,
                            unit,
                            includeFiles))
                        {
                            yield return item;
                        }
                    }
                }
            }
        }

        // Updated BuildFilterCache (Signature changed to return bool for recursion check)
        public bool BuildFilterCache(
            FolderInfo node,
            ConcurrentDictionary<FolderInfo, List<FolderInfo>> cache,
            long thresholdBytes,
            bool includeFiles,
            AppConstants.SortTarget sortTarget,
            AppConstants.SortDirection sortDirection,
            CancellationToken token,
            TreeFilter? filter = null)
        {
            if (token.IsCancellationRequested || node == null) return false;

            bool isSelfMatch = true;
            if (filter != null && !filter.IsEmpty)
            {
                // Root is always matched? No, we might want to filter root too if it were a list of roots.
                // But typically root folder (the one scanned) name is whatever.
                // If the user searches for "foo", and root is "C:\", root doesn't match effectively.
                // BUT we need to traverse down.
                // Usually root is kept if it has matching descendants.
                isSelfMatch = filter.IsMatch(node.Name);
            }

            bool hasMatchingDescendant = false;

            if (node.Children != null)
            {
                List<FolderInfo> visible = new List<FolderInfo>();
                List<FolderInfo> candidates;

                lock (node.Children)
                {
                    var query = node.Children.Where(c => c.Size >= thresholdBytes && (includeFiles || !c.IsFile));

                    switch (sortTarget)
                    {
                        case AppConstants.SortTarget.Size:
                            query = sortDirection == AppConstants.SortDirection.Ascending
                                ? query.OrderBy(c => c.Size)
                                : query.OrderByDescending(c => c.Size);
                            break;
                        case AppConstants.SortTarget.Name:
                            query = sortDirection == AppConstants.SortDirection.Ascending
                                ? query.OrderBy(c => c.Name)
                                : query.OrderByDescending(c => c.Name);
                            break;
                        case AppConstants.SortTarget.Date:
                            query = sortDirection == AppConstants.SortDirection.Ascending
                                ? query.OrderBy(c => c.LastModified)
                                : query.OrderByDescending(c => c.LastModified);
                            break;
                        case AppConstants.SortTarget.Type:
                            query = sortDirection == AppConstants.SortDirection.Ascending
                                ? query.OrderBy(c => c.IsFile ? System.IO.Path.GetExtension(c.Name) : "")
                                : query.OrderByDescending(c => c.IsFile ? System.IO.Path.GetExtension(c.Name) : "");
                            break;
                    }
                    candidates = query.ToList();
                }

                // Recursively check children
                // Parallel Processing for children
                var results = new bool[candidates.Count];

                if (candidates.Count > 0)
                {
                    // Use Parallel.ForEach to process children concurrently
                    Parallel.ForEach(Partitioner.Create(0, candidates.Count), range =>
                    {
                        for (int i = range.Item1; i < range.Item2; i++)
                        {
                            if (token.IsCancellationRequested) return;
                            results[i] = BuildFilterCache(candidates[i], cache, thresholdBytes, includeFiles, sortTarget, sortDirection, token, filter);
                        }
                    });
                }

                if (token.IsCancellationRequested) return false;

                for (int i = 0; i < candidates.Count; i++)
                {
                    if (results[i])
                    {
                        visible.Add(candidates[i]);
                        hasMatchingDescendant = true;
                    }
                }

                if (hasMatchingDescendant || (filter != null && !filter.IsEmpty && isSelfMatch))
                {
                    // If we have matching descendants, we keep this node.
                    // If we match ourselves, we ALSO keep this node. 
                    // Note: If ONLY self matches, 'visible' might be empty (no children match), which is fine. (Leaf match)
                }

                cache[node] = visible;
            }
            else
            {
                // No children.
                cache[node] = new List<FolderInfo>();
            }

            if (filter == null || filter.IsEmpty) return true; // No filter -> Everything matches

            return isSelfMatch || hasMatchingDescendant;
        }

        private int GetStringWidth(string str)
        {
            return TextMeasurer.GetStringWidth(str);
        }

        public string FormatDuration(TimeSpan ts)
        {
            var lm = LocalizationManager.Instance;
            return ts switch
            {
                TimeSpan t when t.TotalHours > 1 => $"{(int)ts.TotalHours}{lm.GetText(LanguageKey.UnitHour)}{ts.Minutes}{lm.GetText(LanguageKey.UnitMinute)}",
                TimeSpan t when t.TotalMinutes > 1 => $"{(int)ts.TotalMinutes}{lm.GetText(LanguageKey.UnitMinute)}{ts.Seconds}{lm.GetText(LanguageKey.UnitSecond)}",
                TimeSpan t when t.TotalSeconds > 1 => $"{(int)ts.TotalSeconds}{lm.GetText(LanguageKey.UnitSecond)}",
                _ => $"{ts.Milliseconds}{lm.GetText(LanguageKey.UnitMillisecond)}"
            };
        }
    }
}
