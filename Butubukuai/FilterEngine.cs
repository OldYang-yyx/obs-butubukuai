using System;
using System.Collections.Generic;
using System.Linq;

namespace Butubukuai
{
    public class FilterEngine
    {
        private List<string> _bannedWords = new List<string>();

        public void LoadWords(string commaSeparatedWords)
        {
            if (string.IsNullOrWhiteSpace(commaSeparatedWords))
            {
                _bannedWords.Clear();
                return;
            }

            // 按逗号断句并过滤空字符串
            _bannedWords = commaSeparatedWords.Split(new[] { ',', '，', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                              .Select(w => w.Trim())
                                              .Where(w => !string.IsNullOrEmpty(w))
                                              .ToList();
        }

        public bool Check(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || _bannedWords.Count == 0) return false;

            foreach (var word in _bannedWords)
            {
                if (text.Contains(word, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        
        public int BannedWordCount => _bannedWords.Count;
    }
}
