using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace XiaoZhi.Net.Server.Helpers
{
    internal static class DialogueHelper
    {
        #region emoji ranges

        private static List<Tuple<int, int>> EMOJI_RANGES = new List<Tuple<int, int>>
        {
            // 基本Emoji
            Tuple.Create(0x1F300, 0x1F5FF),  // 杂项符号和象形文字
            Tuple.Create(0x1F600, 0x1F64F),  // 表情符号
            Tuple.Create(0x1F680, 0x1F6FF),  // 交通和地图符号
        
            // 扩展Emoji
            Tuple.Create(0x1F700, 0x1F77F),  // 炼金术符号
            Tuple.Create(0x1F780, 0x1F7FF),  // 几何形状扩展
            Tuple.Create(0x1F800, 0x1F8FF),  // 补充箭头C
            Tuple.Create(0x1F900, 0x1F9FF),  // 补充符号和象形文字
            Tuple.Create(0x1FA00, 0x1FA6F),  // 象棋符号
            Tuple.Create(0x1FA70, 0x1FAFF),  // 符号和象形文字扩展A
            Tuple.Create(0x1FB00, 0x1FBFF),  // 符号和象形文字扩展B
            Tuple.Create(0x1FC00, 0x1FCFF),  // 符号和象形文字扩展C
            Tuple.Create(0x1FD00, 0x1FDFF),  // 符号和象形文字扩展D
        
            // 国旗
            Tuple.Create(0x1F1E6, 0x1F1FF),  // 区域指示符号 (国旗)
        
            // 其他Unicode块中的表情符号
            Tuple.Create(0x2600, 0x26FF),    // 杂项符号
            Tuple.Create(0x2700, 0x27BF),    // 装饰符号
            Tuple.Create(0x2300, 0x23FF),    // 技术符号
            Tuple.Create(0x2B00, 0x2BFF),    // 杂项符号和箭头
        
            // Emoji修饰符和组合
            Tuple.Create(0xFE00, 0xFE0F),    // 变体选择器 (用于Emoji风格)
            Tuple.Create(0x1F3FB, 0x1F3FF),  // 肤色修饰符
        
            // 零宽连接符
            Tuple.Create(0x200D, 0x200D),    // ZWJ (零宽连接符)
        
            // 其他可能用作表情符号的符号
            Tuple.Create(0x203C, 0x203C),    // 双感叹号
            Tuple.Create(0x2049, 0x2049),    // 感叹问号
            Tuple.Create(0x2122, 0x2122),    // 商标符号
            Tuple.Create(0x2139, 0x2139),    // 信息源
            Tuple.Create(0x2194, 0x2199),    // 箭头
            Tuple.Create(0x21A9, 0x21AA),    // 带钩的箭头
            Tuple.Create(0x231A, 0x231B),    // 手表和沙漏
            Tuple.Create(0x2328, 0x2328),    // 键盘
            Tuple.Create(0x23CF, 0x23CF),    // 弹出按钮
            Tuple.Create(0x23E9, 0x23F3),    // 媒体控制按钮
            Tuple.Create(0x23F8, 0x23FA),    // 媒体控制
            Tuple.Create(0x24C2, 0x24C2),    // 圆圈中的M
            Tuple.Create(0x25AA, 0x25AB),    // 小方块
            Tuple.Create(0x25B6, 0x25B6),    // 右指三角形
            Tuple.Create(0x25C0, 0x25C0),    // 左指三角形
            Tuple.Create(0x25FB, 0x25FE),    // 几何形状
            Tuple.Create(0x2614, 0x2615),    // 雨伞和热饮
            Tuple.Create(0x2622, 0x2623),    // 辐射和生物危害
            Tuple.Create(0x262A, 0x262A),    // 星月
            Tuple.Create(0x2638, 0x2640),    // 各种宗教和性别符号
            Tuple.Create(0x2642, 0x2642),    // 男性符号
            Tuple.Create(0x2648, 0x2653),    // 星座
            Tuple.Create(0x265F, 0x2660),    // 象棋和扑克牌
            Tuple.Create(0x2663, 0x2663),    // 梅花
            Tuple.Create(0x2665, 0x2666),    // 红桃和方块
            Tuple.Create(0x2668, 0x2668),    // 温泉
            Tuple.Create(0x267B, 0x267B),    // 回收符号
            Tuple.Create(0x267E, 0x267F),    // 无限和轮椅
            Tuple.Create(0x2692, 0x2697),    // 工具
            Tuple.Create(0x2699, 0x2699),    // 齿轮
            Tuple.Create(0x269B, 0x269C),    // 原子和符号
            Tuple.Create(0x26A0, 0x26A1),    // 警告和高压
            Tuple.Create(0x26A7, 0x26A7),    // 跨性别符号
            Tuple.Create(0x26AA, 0x26AB),    // 中号圆圈
            Tuple.Create(0x26B0, 0x26B1),    // 棺材和墓碑
            Tuple.Create(0x26BD, 0x26BE),    // 足球和棒球
            Tuple.Create(0x26C4, 0x26C5),    // 雪人和部分阴天
            Tuple.Create(0x26C8, 0x26C8),    // 雷暴
            Tuple.Create(0x26CE, 0x26CF),    // 蛇夫座和锄头
            Tuple.Create(0x26D1, 0x26D1),    // 救援工人头盔
            Tuple.Create(0x26D3, 0x26D4),    // 锁链和禁止进入
            Tuple.Create(0x26E9, 0x26EA),    // 塔和教堂
            Tuple.Create(0x26F0, 0x26F5),    // 山和帆船
            Tuple.Create(0x26F7, 0x26FA),    // 滑雪和露营
            Tuple.Create(0x26FD, 0x26FD),    // 加油站
            Tuple.Create(0x2702, 0x2702),    // 剪刀
            Tuple.Create(0x2705, 0x2705),    // 白色重勾
            Tuple.Create(0x2708, 0x2709),    // 飞机和信封
            Tuple.Create(0x270A, 0x270B),    // 举起的拳头和手
            Tuple.Create(0x270C, 0x270D),    // 胜利手势和写作手
            Tuple.Create(0x270F, 0x270F),    // 铅笔
            Tuple.Create(0x2712, 0x2712),    // 黑色尖笔
            Tuple.Create(0x2714, 0x2714),    // 重勾
            Tuple.Create(0x2716, 0x2716),    // 重X
            Tuple.Create(0x271D, 0x271D),    // 拉丁十字
            Tuple.Create(0x2721, 0x2721),    // 星星
            Tuple.Create(0x2728, 0x2728),    // 闪烁
            Tuple.Create(0x2733, 0x2734),    // 八角星
            Tuple.Create(0x2744, 0x2744),    // 雪花
            Tuple.Create(0x2747, 0x2747),    // 闪光
            Tuple.Create(0x274C, 0x274C),    // 交叉标记
            Tuple.Create(0x274E, 0x274E),    // 否定交叉标记
            Tuple.Create(0x2753, 0x2755),    // 问号和感叹号
            Tuple.Create(0x2757, 0x2757),    // 重感叹号
            Tuple.Create(0x2763, 0x2764),    // 心形装饰和红心
            Tuple.Create(0x2795, 0x2797),    // 加号、减号和除号
            Tuple.Create(0x27A1, 0x27A1),    // 右箭头
            Tuple.Create(0x27B0, 0x27B0),    // 卷曲循环
            Tuple.Create(0x27BF, 0x27BF),    // 双循环
            Tuple.Create(0x2934, 0x2935),    // 箭头
            Tuple.Create(0x2B05, 0x2B07),    // 左、上、下箭头
            Tuple.Create(0x2B1B, 0x2B1C),    // 黑白方块
            Tuple.Create(0x2B50, 0x2B50),    // 白色中星
            Tuple.Create(0x2B55, 0x2B55),    // 重圆圈
            Tuple.Create(0x3030, 0x3030),    // 波浪划线
            Tuple.Create(0x303D, 0x303D),    // 替代标记
            Tuple.Create(0x3297, 0x3297),    // 圆圈中的祝贺
            Tuple.Create(0x3299, 0x3299),    // 圆圈中的秘密
        };

        #endregion

        public static Regex SENTENCE_SPLIT_REGEX = new Regex(@"(?<![0-9])[.?!;:](?=\s|$)|[\r\n]+|[。？！；：，]");

        public const string PUNCTUATIONS_PATTERN = @"(?<![0-9])[.?!;:](?![0-9])|[。？！；：]";

        //需要去除的中英文标点（包括全角/半角）
        public static HashSet<char> PUNCTUATION_SET = new HashSet<char>
        {
            '，', ',',  // 中文逗号 + 英文逗号
            '。', '.',  // 中文句号 + 英文句号
            '！', '!',  // 中文感叹号 + 英文感叹号
            '-', '－',  // 英文连字符 + 中文全角横线
            '、'       // 中文顿号
        };

        public static string GetStringNoPunctuationOrEmoji(string s)
        {
            char[] chars = s.ToCharArray();
            // 处理开头的字符
            int start = 0;
            while (start < chars.Length && IsPunctuationOrEmoji(chars[start]))
            {
                start++;
            }
            // 处理结尾的字符
            int end = chars.Length - 1;
            while (end >= start && IsPunctuationOrEmoji(chars[end]))
            {
                end--;
            }
            return new string(chars, start, end - start + 1);
        }

        public static bool IsPunctuationOrEmoji(char c)
        {
            if (char.IsWhiteSpace(c) || PUNCTUATION_SET.Contains(c))
            {
                return true;
            }

            // 检查表情符号
            int codePoint = c;

            return EMOJI_RANGES.Any(range => range.Item1 <= codePoint && codePoint <= range.Item2);
        }

        public static IEnumerable<string> SplitContentByPunctuations(string content)
        {
            string[] rawSegments = Regex.Split(content, PUNCTUATIONS_PATTERN);

            foreach (string segment in rawSegments)
            {
                if (!string.IsNullOrEmpty(segment))
                {
                    yield return segment.Trim();
                }
            }
        }
    }
}
