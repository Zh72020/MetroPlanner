using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.International.Converters.PinYinConverter;

namespace MetroPlanner
{
    /// <summary>中文 → 拼音（英语式 / 带声调注音）。</summary>
    public static class PinyinHelper
    {
        public static string ToEnglish(string chinese) => Convert(chinese, false);
        public static string ToZhuyin(string chinese) => Convert(chinese, true);

        /// <summary>生成用于排序的拼音键（无声调、无空格、小写）。</summary>
        public static string ToPinyinKey(string chinese)
        {
            if (string.IsNullOrEmpty(chinese)) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var ch in chinese)
            {
                if (char.IsWhiteSpace(ch)) continue;
                sb.Append(CharPinyin(ch, false).ToLowerInvariant());
            }
            return sb.ToString();
        }

        static string Convert(string s, bool withTone)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var parts = new List<string>();
            foreach (var ch in s)
            {
                if (char.IsWhiteSpace(ch)) continue;
                parts.Add(CharPinyin(ch, withTone));
            }
            return string.Join(" ", parts.Where(p => !string.IsNullOrEmpty(p)));
        }

        // 常见地名多音字的首选读音（覆盖拼音库按字母序排列导致的错误首读音）
        static readonly Dictionary<char, string> Overrides = new()
        {
            ['广'] = "guang3", ['单'] = "dan1", ['行'] = "xing2", ['厦'] = "xia4",
            ['蚌'] = "beng4", ['番'] = "pan1", ['藏'] = "zang4", ['种'] = "zhong3",
            ['蒙'] = "meng3", ['曲'] = "qu3", ['重'] = "chong2", ['乐'] = "le4",
            ['长'] = "chang2", ['都'] = "du1", ['会'] = "hui4", ['大'] = "da4",
            ['相'] = "xiang1", ['兴'] = "xing1", ['济'] = "ji3", ['强'] = "qiang2",
            ['将'] = "jiang1", ['降'] = "jiang4", ['教'] = "jiao4", ['和'] = "he2",
            ['台'] = "tai2", ['丽'] = "li4", ['六'] = "liu4", ['石'] = "shi2",
            ['南'] = "nan2", ['地'] = "di4", ['场'] = "chang3", ['处'] = "chu4",
            ['塔'] = "ta3", ['巷'] = "xiang4", ['厂'] = "chang3", ['岗'] = "gang3", ['区'] = "qu1"
        };

        static string CharPinyin(char ch, bool withTone)
        {
            if (ch < 0x4E00 || ch > 0x9FFF) return char.IsLetterOrDigit(ch) ? ch.ToString() : "";
            try
            {
                string raw;
                if (Overrides.TryGetValue(ch, out var ov)) raw = ov;
                else
                {
                    var cc = new ChineseChar(ch);
                    var pinyins = cc.Pinyins;
                    if (pinyins == null || pinyins.Count == 0) return ch.ToString();
                    raw = pinyins[0] ?? "";
                }
                if (raw.Length == 0) return ch.ToString();
                int toneNum = 0;
                if (char.IsDigit(raw[raw.Length - 1]))
                {
                    toneNum = raw[raw.Length - 1] - '0';
                    raw = raw.Substring(0, raw.Length - 1);
                }
                string basePy = raw.ToLowerInvariant().Replace("v", "ü").Replace("V", "ü");
                string result = withTone && toneNum >= 1 && toneNum <= 4 ? ApplyTone(basePy, toneNum) : basePy;
                if (result.Length > 0) result = char.ToUpperInvariant(result[0]) + result.Substring(1);
                return result;
            }
            catch { return ch.ToString(); }
        }

        static string ApplyTone(string pinyin, int tone)
        {
            int pos = pinyin.IndexOf('a');
            if (pos < 0) pos = pinyin.IndexOf('o');
            if (pos < 0) pos = pinyin.IndexOf('e');
            if (pos < 0)
            {
                pos = -1;
                foreach (var v in "iuü")
                {
                    int lp = pinyin.LastIndexOf(v);
                    if (lp > pos) pos = lp;
                }
            }
            if (pos < 0) return pinyin;
            char c = pinyin[pos];
            char marked = MarkVowel(c, tone);
            return pinyin.Substring(0, pos) + marked + pinyin.Substring(pos + 1);
        }

        static char MarkVowel(char c, int tone)
        {
            string vowels = "aeiouü";
            int i = vowels.IndexOf(char.ToLowerInvariant(c));
            if (i < 0) return c;
            char[][] marks = new[]
            {
                new[]{'a','e','i','o','u','ü'},
                new[]{'ā','ē','ī','ō','ū','ǖ'},
                new[]{'á','é','í','ó','ú','ǘ'},
                new[]{'ǎ','ě','ǐ','ǒ','ǔ','ǚ'},
                new[]{'à','è','ì','ò','ù','ǜ'}
            };
            return marks[tone][i];
        }

        /// <summary>公开的声调标注方法，供手动加声调用。</summary>
        public static char MarkVowelPublic(char c, int tone)
        {
            if (tone < 1 || tone > 4) return c;
            bool upper = char.IsUpper(c);
            char marked = MarkVowel(char.ToLowerInvariant(c), tone);
            return upper ? char.ToUpperInvariant(marked) : marked;
        }
    }
}
