using System;
using System.Collections.Generic;
using System.Linq;

namespace Butubukuai
{
    public class FilterEngine
    {
        // 编译后的宏展开查表，Key为平铺的短语，Value为对应的提示音路径
        private readonly Dictionary<string, string> _compiledRules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 写死的内置宏表，用于替换特定标签
        private readonly Dictionary<string, List<string>> _macros = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            { "#城市#", new List<string> { "北京", "上海", "广州", "深圳", "杭州" } }
        };

        private string _lastProcessedText = "";
        private int _lastBanTriggerIndex = -1;

        public void LoadWords(IEnumerable<RuleGroup> groups)
        {
            _compiledRules.Clear();
            if (groups == null) return;

            foreach (var group in groups)
            {
                if (string.IsNullOrWhiteSpace(group.Words)) continue;

                // 拆分短语（支持逗号或换行）
                var tokens = group.Words.Split(new[] { ',', '，', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var token in tokens)
                {
                    string cleanToken = token.Trim();
                    if (string.IsNullOrWhiteSpace(cleanToken)) continue;

                    // 宏替换检查
                    if (_macros.TryGetValue(cleanToken, out var macroWords))
                    {
                        foreach (var w in macroWords)
                        {
                            // AOT平铺覆盖：如果后配置的规则组也包含了该词，后者的音轨覆盖前者
                            _compiledRules[w] = group.SoundPath ?? "";
                        }
                    }
                    else
                    {
                        _compiledRules[cleanToken] = group.SoundPath ?? "";
                    }
                }
            }
        }

        public bool Check(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || _compiledRules.Count == 0) return false;

            foreach (var key in _compiledRules.Keys)
            {
                if (text.Contains(key, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public (bool isMatch, int durationMs, string soundPath) CheckAndGetDuration(RecognizedTextEventArgs args)
        {
            if (string.IsNullOrWhiteSpace(args.Text) || _compiledRules.Count == 0) return (false, 0, "");

            // 新句重置法则：只要长度出现回调或首字突变，立即重置水位线
            if (args.Text.Length <= _lastProcessedText.Length || 
                (_lastProcessedText.Length > 0 && args.Text.Length > 0 && args.Text[0] != _lastProcessedText[0]))
            {
                _lastBanTriggerIndex = -1;
            }

            _lastProcessedText = args.Text;

            int maxMatchedIndex = -1;
            int finalDuration = 0;
            string finalSoundPath = "";
            string maxMatchedWord = ""; // 记录触发的词实体供细化耗时计算使用

            // 遍历平铺验证池搜索
            foreach (var kvp in _compiledRules)
            {
                string word = kvp.Key;
                string soundPath = kvp.Value;

                int index = args.Text.LastIndexOf(word, StringComparison.OrdinalIgnoreCase);

                // 核心拦截：只有当找到的违禁词 index 严格大于 _lastBanTriggerIndex 时才认为新出现
                if (index > _lastBanTriggerIndex)
                {
                    if (index > maxMatchedIndex)
                    {
                        maxMatchedIndex = index;
                        finalSoundPath = soundPath;
                        maxMatchedWord = word;
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

                    // 优先使用查到的精确时长，如果没有则使用动态字长估算算法
                    int currentDuration = exactDuration > 0 ? exactDuration : (word.Length * 250);
                    
                    // 安全缓冲算法 (+100ms) 确保单字限制绝不吞掉下一个字
                    currentDuration += 100;

                    if (currentDuration > finalDuration)
                    {
                        finalDuration = currentDuration;
                    }
                }
            }

            if (maxMatchedIndex > _lastBanTriggerIndex)
            {
                _lastBanTriggerIndex = maxMatchedIndex;
                return (true, finalDuration, finalSoundPath);
            }

            return (false, 0, "");
        }
        
        public int BannedWordCount => _compiledRules.Count;
    }
}
