// Copyright (c) Microsoft. All rights reserved.

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Text;
using System.ComponentModel;

namespace XiaoZhi.Net.Test.Plugins
{

    /// <summary>
    /// Semantic plugin that enables conversations summarization.
    /// </summary>
    public class ConversationSummaryPlugin
    {
        /// <summary>
        /// The max tokens to process in a single prompt function call.
        /// </summary>
        private const int MaxTokens = 1024;

        private const string SummarizeConversationDefinition =
        @"BEGIN CONTENT TO SUMMARIZE:
{{$INPUT}}

END CONTENT TO SUMMARIZE.

Summarize the conversation in 'CONTENT TO SUMMARIZE', identifying main points of discussion and any conclusions that were reached, in the language that best fits the content.
Do not incorporate other general knowledge.
Summary is in plain text, in complete sentences, with no markup or tags in Chinese.

BEGIN SUMMARY:
";
        private readonly KernelFunction _summarizeConversationFunction;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConversationSummaryPlugin"/> class.
        /// </summary>
        public ConversationSummaryPlugin()
        {
            PromptExecutionSettings settings = new()
            {
                ExtensionData = new Dictionary<string, object>()
            {
                { "Temperature", 0.1 },
                { "TopP", 0.5 },
                { "MaxTokens", MaxTokens }
            }
            };

            this._summarizeConversationFunction = KernelFunctionFactory.CreateFromPrompt(
                ConversationSummaryPlugin.SummarizeConversationDefinition,
                description: "Given a section of a conversation transcript, summarize the part of the conversation.",
                executionSettings: settings);
        }

        /// <summary>
        /// Given a long conversation transcript, summarize the conversation.
        /// </summary>
        /// <param name="input">A long conversation transcript.</param>
        /// <param name="kernel">The <see cref="Kernel"/> containing services, plugins, and other state for use throughout the operation.</param>
        [KernelFunction, Description("Given a long conversation transcript, summarize the conversation.")]
        public Task<string> SummarizeConversation(
            [Description("A long conversation transcript.")] string input,
            Kernel kernel) =>
            ProcessAsync(this._summarizeConversationFunction, input, kernel);



        private static async Task<string> ProcessAsync(KernelFunction func, string input, Kernel kernel)
        {
#pragma warning disable SKEXP0050 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            List<string> lines = TextChunker.SplitPlainTextLines(input, MaxTokens);
            List<string> paragraphs = TextChunker.SplitPlainTextParagraphs(lines, MaxTokens);
#pragma warning restore SKEXP0050 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

            string[] results = new string[paragraphs.Count];

            for (int i = 0; i < results.Length; i++)
            {
                // The first parameter is the input text.
                results[i] = (await func.InvokeAsync(kernel, new() { ["input"] = paragraphs[i] }).ConfigureAwait(false))
                    .GetValue<string>() ?? string.Empty;
            }

            return string.Join("\n", results);
        }
    }
}