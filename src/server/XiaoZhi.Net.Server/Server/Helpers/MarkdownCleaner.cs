using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using XiaoZhi.Net.Server.I18n;

namespace XiaoZhi.Net.Server.Helpers
{
    /// <summary>
    /// Markdown清理工具
    /// </summary>
    public class MarkdownCleaner
    {
        // 公式字符正则表达式
        private static readonly Regex NormalFormulaChars = new Regex(@"[a-zA-Z\\^_{}\+\-\(\)\[\]=]", RegexOptions.Compiled);

        // 预编译所有正则表达式（按执行频率排序）
        private static readonly List<(Regex Pattern, MatchEvaluator Replacer)> RegexReplacers = new List<(Regex, MatchEvaluator)>
        {
            (new Regex(@"```.*?```", RegexOptions.Singleline | RegexOptions.Compiled), m => string.Empty), // 代码块
            (new Regex(@"^#+\s*", RegexOptions.Multiline | RegexOptions.Compiled), m => string.Empty), // 标题
            (new Regex(@"(\*\*|__)(.*?)\1", RegexOptions.Compiled), m => m.Groups[2].Value), // 粗体
            (new Regex(@"(\*|_)(?=\S)(.*?)(?<=\S)\1", RegexOptions.Compiled), m => m.Groups[2].Value), // 斜体
            (new Regex(@"!\[.*?\]\(.*?\)", RegexOptions.Compiled), m => string.Empty), // 图片
            (new Regex(@"\[(.*?)\]\(.*?\)", RegexOptions.Compiled), m => m.Groups[1].Value), // 链接
            (new Regex(@"^\s*>+\s*", RegexOptions.Multiline | RegexOptions.Compiled), m => string.Empty), // 引用
            (new Regex(@"(?<table_block>(?:^[^\n]*\|[^\n]*\n)+)", RegexOptions.Multiline | RegexOptions.Compiled), new MatchEvaluator(ReplaceTableBlock)), // 表格
            (new Regex(@"^\s*[*+-]\s*", RegexOptions.Multiline | RegexOptions.Compiled), m => "。"), // 列表
            (new Regex(@"\$\$.*?\$\$", RegexOptions.Singleline | RegexOptions.Compiled), m => string.Empty), // 块级公式
            (new Regex(@"(?<![A-Za-z0-9])\$([^\n$]+)\$(?![A-Za-z0-9])", RegexOptions.Compiled), new MatchEvaluator(ReplaceInlineDollar)), // 行内公式
            (new Regex(@"\n{2,}", RegexOptions.Compiled), m => string.Empty), // 多余空行
        };

        /// <summary>
        /// 清理Markdown文本
        /// </summary>
        /// <param name="text">要清理的Markdown文本</param>
        /// <returns>清理后的文本</returns>
        public static string CleanMarkdown(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            foreach (var (pattern, replacer) in RegexReplacers)
            {
                text = pattern.Replace(text, replacer);
            }

            return text;
        }

        /// <summary>
        /// 处理行内公式
        /// </summary>
        private static string ReplaceInlineDollar(Match match)
        {
            string content = match.Groups[1].Value;
            
            // 如果内部有典型公式字符 => 去掉两侧 $
            // 否则 (纯数字/货币等) => 保留 "$...$"
            if (NormalFormulaChars.IsMatch(content))
            {
                return content;
            }
            else
            {
                return match.Value;
            }
        }

        /// <summary>
        /// 处理表格
        /// </summary>
        private static string ReplaceTableBlock(Match match)
        {
            string blockText = match.Groups["table_block"].Value;
            string[] lines = blockText.Trim('\n').Split('\n');

            List<List<string>> parsedTable = new List<List<string>>();
            
            foreach (string line in lines)
            {
                string lineStripped = line.Trim();
                
                // 跳过分隔行，如 |---|---|---|
                if (Regex.IsMatch(lineStripped, @"^\|\s*[-:]+\s*(\|\s*[-:]+\s*)+\|?$"))
                {
                    continue;
                }
                
                // 提取并处理每一列
                List<string> columns = lineStripped
                    .Split('|')
                    .Where(col => !string.IsNullOrWhiteSpace(col))
                    .Select(col => col.Trim())
                    .ToList();
                
                if (columns.Count > 0)
                {
                    parsedTable.Add(columns);
                }
            }

            if (parsedTable.Count == 0)
            {
                return string.Empty;
            }

            List<string> headers = parsedTable[0];
            List<List<string>> dataRows = parsedTable.Count > 1 ? parsedTable.Skip(1).ToList() : new List<List<string>>();

            List<string> linesForTts = new List<string>();
            
            if (parsedTable.Count == 1)
            {
                // 只有一行
                string onlyLineStr = string.Join(", ", parsedTable[0]);
                linesForTts.Add(string.Format(Lang.MarkdownCleaner_ReplaceTableBlock_SingleLineTable, onlyLineStr));
            }
            else
            {
                linesForTts.Add(string.Format(Lang.MarkdownCleaner_ReplaceTableBlock_TableHeader, string.Join(", ", headers)));
                
                for (int i = 0; i < dataRows.Count; i++)
                {
                    List<string> row = dataRows[i];
                    List<string> rowStrList = new List<string>();
                    
                    for (int colIndex = 0; colIndex < row.Count; colIndex++)
                    {
                        if (colIndex < headers.Count)
                        {
                            rowStrList.Add($"{headers[colIndex]} = {row[colIndex]}");
                        }
                        else
                        {
                            rowStrList.Add(row[colIndex]);
                        }
                    }
                    
                    linesForTts.Add(string.Format(Lang.MarkdownCleaner_ReplaceTableBlock_RowContent, i + 1, string.Join(", ", rowStrList)));
                }
            }

            return string.Join("。", linesForTts);
        }
    }
}