using System;
using System.Text.RegularExpressions;

namespace LargeFolderFinder
{
    public class TreeFilter
    {
        private readonly Regex? _regex;
        private readonly bool _isEmpty;

        public TreeFilter(string pattern, bool isRegex)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                _isEmpty = true;
                return;
            }

            try
            {
                if (isRegex)
                {
                    _regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                }
                else
                {
                    // Convert wildcard to regex
                    string regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                    _regex = new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                }
            }
            catch
            {
                // 意図して無視: 入力途中の不正な式は打ち間違いとして扱い、記録しない。_regex を空にして何にも一致させない
                _regex = null;
            }
        }

        public bool IsEmpty => _isEmpty;

        public bool IsMatch(string text)
        {
            if (_isEmpty) return true;
            if (_regex == null) return false; // Invalid pattern scenario
            return _regex.IsMatch(text);
        }
    }
}
