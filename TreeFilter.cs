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
                // Fallback for invalid regex: treat as empty or partial match? 
                // Let's treat as partial match (contains) if regex fails? 
                // Or just match nothing?
                // For safety, let's treat invalid regex as "no match" or just ignore filter?
                // Let's assume no match if invalid.
                _regex = null;
                // Actually, if it's invalid input, maybe we shouldn't filter anything? 
                // But usually that implies user made a mistake.
                // Let's just catch and leave _regex null, effectively 'IsEmpty' false but matches nothing.
                // Or maybe simple string contains?
                // Let's stick to safe defaults: Invalid pattern -> Match nothing (or everything?)
                // User requirement is usually "if I type garbage, show nothing".
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
