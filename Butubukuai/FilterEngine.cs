using System;
using System.Collections.Generic;
using System.Linq;

namespace Butubukuai
{
    public class FilterEngine
    {
        private List<string> _bannedWords = new List<string>();
        private string _lastProcessedText = "";
        private int _lastBanTriggerIndex = -1;

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

        public (bool isMatch, int durationMs) CheckAndGetDuration(RecognizedTextEventArgs args)
        {
            if (string.IsNullOrWhiteSpace(args.Text) || _bannedWords.Count == 0) return (false, 0);

            // 新句重置：预留微调容错
            if (args.Text.Length < _lastProcessedText.Length - 2)
            {
                _lastBanTriggerIndex = -1;
            }

            _lastProcessedText = args.Text;

            int maxMatchedIndex = -1;
            int finalDuration = 0;

            foreach (var word in _bannedWords)
            {
                int index = args.Text.LastIndexOf(word, StringComparison.OrdinalIgnoreCase);

                // 核心拦截：只有当找到的违禁词 index 严格大于 _lastBanTriggerIndex 时才认为新出现
                if (index > _lastBanTriggerIndex)
                {
                    if (index > maxMatchedIndex)
                    {
                        maxMatchedIndex = index;
                    }

                    int exactDuration = 0;
                    
                    // 尝试根据词级时间戳定位命中词的范围
                    if (args.Words != null && args.Words.Count > 0)
                    {
                        var matchedWords = args.Words.Where(w => 
                            word.Contains(w.Text, StringComparison.OrdinalIgnoreCase) || 
                            w.Text.Contains(word, StringComparison.OrdinalIgnoreCase)).ToList();

                        if (matchedWords.Any())
                        {
                            int minBegin = matchedWords.Min(w => w.BeginTime);
                            int maxEnd = matchedWords.Max(w => w.EndTime);
                            exactDuration = maxEnd - minBegin;
                        }
                    }

                    // 优先使用查到的精确时长，如果没有则退回到句子的时长/默认时长
                    int currentDuration = exactDuration > 0 ? exactDuration : args.DurationMs;
                    
                    // 安全缓冲算法 (+300ms)
                    currentDuration += 300;

                    if (currentDuration > finalDuration)
                    {
                        finalDuration = currentDuration;
                    }
                }
            }

            if (maxMatchedIndex > _lastBanTriggerIndex)
            {
                _lastBanTriggerIndex = maxMatchedIndex;
                return (true, finalDuration);
            }

            return (false, 0);
        }
        
        public int BannedWordCount => _bannedWords.Count;
    }
}
