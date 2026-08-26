using System;
using System.Text;
using TinyPinyin;

namespace AltRunSharp
{
    /// <summary>
    /// 静态匹配工具：支持拼音首字母和英文首字母提取及搜索匹配。
    /// </summary>
    public static class SearchMatcher
    {
        /// <summary>
        /// 拼音首字母：把文本中每个汉字转为拼音首字母（小写），非汉字字母/数字原样保留并小写，其他字符去掉。
        /// 如 "设置" → "sz"，"QQ音乐" → "qqyl"。
        /// </summary>
        public static string GetPinyinInitials(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (PinyinHelper.IsChinese(c))
                {
                    string pinyin = PinyinHelper.GetPinyin(c);
                    if (!string.IsNullOrEmpty(pinyin))
                    {
                        sb.Append(char.ToLowerInvariant(pinyin[0]));
                    }
                }
                else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 英文首字母：按空格分词，取每个词首字符（无视大小写，返回小写拼接）。
        /// 如 "Visual Studio Code" → "vsc"。
        /// </summary>
        public static string GetEnglishInitials(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
                return string.Empty;

            var sb = new StringBuilder(words.Length);
            foreach (string word in words)
            {
                if (!string.IsNullOrEmpty(word))
                {
                    sb.Append(char.ToLowerInvariant(word[0]));
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 首字母匹配：query 归一化（去空格、小写）后，若是 candidate 的拼音首字母串或英文首字母串的子串（Contains）即命中。
        /// query 为空或含中文时直接返回 false（中文走原有子串匹配）。
        /// </summary>
        public static bool MatchesInitials(string candidate, string query)
        {
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(query))
                return false;

            // query 为空或含中文时直接返回 false（中文走原有子串匹配）
            foreach (char c in query)
            {
                if (PinyinHelper.IsChinese(c))
                    return false;
            }

            // query 归一化（去空格、小写）
            var sb = new StringBuilder(query.Length);
            foreach (char c in query)
            {
                if (!char.IsWhiteSpace(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }

            string normQuery = sb.ToString();
            if (normQuery.Length == 0)
                return false;

            string py = GetPinyinInitials(candidate);
            if (py.Contains(normQuery, StringComparison.Ordinal))
                return true;

            string en = GetEnglishInitials(candidate);
            if (en.Contains(normQuery, StringComparison.Ordinal))
                return true;

            return false;
        }
    }
}
