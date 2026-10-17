using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Helpers
{
    internal static class EmotionTagParser
    {
        private static readonly Regex LEADING_TAG_REGEX = new Regex(@"^\s*\[(?<tag>[^\]\r\n]+)\]\s*", RegexOptions.Compiled);

        private static readonly IReadOnlyDictionary<string, Emotion> EMOTION_ALIASES = BuildEmotionAliases();

        public static ParsedEmotionSegment Parse(string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return new ParsedEmotionSegment(string.Empty, Emotion.Neutral);
            }

            string normalizedContent = content.Trim();
            Match match = LEADING_TAG_REGEX.Match(normalizedContent);
            if (!match.Success)
            {
                return new ParsedEmotionSegment(normalizedContent, Emotion.Neutral);
            }

            string tag = match.Groups["tag"].Value.Trim();
            string cleanContent = normalizedContent.Substring(match.Length).TrimStart();
            Emotion emotion = ParseEmotion(tag);

            return new ParsedEmotionSegment(cleanContent, emotion);
        }

        public static Emotion ParseEmotion(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Emotion.Neutral;
            }

            string normalizedValue = value.Trim();
            if (Enum.TryParse(normalizedValue, ignoreCase: true, out Emotion emotion))
            {
                return emotion;
            }

            return EMOTION_ALIASES.TryGetValue(normalizedValue, out Emotion mappedEmotion)
                ? mappedEmotion
                : Emotion.Neutral;
        }

        private static IReadOnlyDictionary<string, Emotion> BuildEmotionAliases()
        {
            Dictionary<string, Emotion> aliases = new Dictionary<string, Emotion>(StringComparer.OrdinalIgnoreCase);

            foreach (Emotion emotion in Enum.GetValues<Emotion>())
            {
                aliases[emotion.ToString()] = emotion;

                MemberInfo memberInfo = typeof(Emotion).GetMember(emotion.ToString())[0];
                DescriptionAttribute? descriptionAttribute = memberInfo.GetCustomAttribute<DescriptionAttribute>();
                if (!string.IsNullOrWhiteSpace(descriptionAttribute?.Description))
                {
                    aliases[descriptionAttribute.Description] = emotion;
                }
            }

            return aliases;
        }

        internal readonly record struct ParsedEmotionSegment(string Content, Emotion Emotion);
    }
}