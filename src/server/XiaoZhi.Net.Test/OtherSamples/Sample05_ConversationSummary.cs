using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Text.RegularExpressions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Test.Plugins;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class Sample05_ConversationSummary
    {
        public static async Task Run()
        {
            await TestConversationSummary();
        }

        static async Task TestConversationSummary()
        {
            string endPoint = "https://open.bigmodel.cn/api/paas/v4/";
            string apiKey = Environment.GetEnvironmentVariable("OPEN_AI_API_KEY", EnvironmentVariableTarget.User)!;
            string chatModel = "glm-4-flash";
            OpenAIClientOptions options = new OpenAIClientOptions
            {
                Endpoint = new Uri(endPoint),
                ProjectId = "Xiao Zhi Test"
            };
            OpenAIClient openAIClient = new OpenAIClient(new ApiKeyCredential(apiKey), options);

            IKernelBuilder builder = Kernel.CreateBuilder();

            builder.AddOpenAIChatCompletion(chatModel, openAIClient);

            Kernel kernel = builder.Build();

            var plugin = kernel.ImportPluginFromType<ConversationSummaryPlugin>("ConversationSummary");


            var chatCompletionOptions = new OpenAIPromptExecutionSettings
            {
                Temperature = 0.5f,
                MaxTokens = 80,
                ResponseFormat = ChatResponseFormat.CreateTextFormat(),
                //FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(plugin)
            };


            IChatCompletionService chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

            ChatHistory chatHistory = new ChatHistory();
            chatHistory.AddUserMessage("我最近想去四川旅游，能推荐一些好玩的地方吗？");
            chatHistory.AddAssistantMessage("当然可以！四川有很多著名的旅游景点，比如成都的宽窄巷子、都江堰、乐山大佛、九寨沟和峨眉山等。你对自然风光还是历史文化更感兴趣呢？");
            chatHistory.AddUserMessage("我两个都感兴趣，另外我还想品尝一些四川的特色美食。");
            chatHistory.AddAssistantMessage("那你一定要试试成都的火锅、麻辣烫、兔头、钵钵鸡，还有乐山的豆腐脑和峨眉山的素斋。四川的小吃种类丰富，味道麻辣鲜香，非常有特色。");
            chatHistory.AddUserMessage("听起来很棒！你能帮我规划一个简单的行程吗？");
            chatHistory.AddAssistantMessage("当然可以。你可以先到成都，游览宽窄巷子、春熙路，品尝当地小吃。然后去都江堰和青城山感受历史文化。接着前往乐山参观大佛，最后去九寨沟欣赏自然风光。每个地方都能体验到不同的美食和风景。");
            chatHistory.AddUserMessage("你觉得几月份去四川旅游最合适？");
            chatHistory.AddAssistantMessage("一般来说，春秋两季是去四川旅游的最佳时节，气候宜人，风景优美。");
            chatHistory.AddUserMessage("四川有哪些值得带回去的特产？");
            chatHistory.AddAssistantMessage("四川的特产有腊肠、豆瓣酱、张飞牛肉、竹叶青酒、川茶等，都是很受欢迎的伴手礼。");
            chatHistory.AddUserMessage("九寨沟门票需要提前预订吗？");
            chatHistory.AddAssistantMessage("建议提前在官网或正规平台预订九寨沟门票，尤其是旅游旺季。");
            chatHistory.AddUserMessage("成都有哪些适合亲子游的景点？");
            chatHistory.AddAssistantMessage("成都大熊猫繁育研究基地、欢乐谷、金沙遗址博物馆等都很适合亲子游。");
            chatHistory.AddUserMessage("四川的交通方便吗？");
            chatHistory.AddAssistantMessage("四川的交通较为便利，成都有地铁和高铁，景区之间有大巴和旅游专线。");
            chatHistory.AddUserMessage("去峨眉山需要准备什么？");
            chatHistory.AddAssistantMessage("建议带上舒适的登山鞋、防晒用品和雨具，山上气温较低要注意保暖。");
            chatHistory.AddUserMessage("四川有哪些有趣的民俗活动？");
            chatHistory.AddAssistantMessage("四川有变脸、火锅宴、灯会、川剧等丰富的民俗活动。");
            chatHistory.AddUserMessage("乐山大佛附近有好吃的吗？");
            chatHistory.AddAssistantMessage("乐山豆腐脑、跷脚牛肉、甜皮鸭等都是乐山的美食，值得一试。");
            chatHistory.AddUserMessage("九寨沟适合几天游玩？");
            chatHistory.AddAssistantMessage("一般建议安排2天游玩九寨沟，可以更好地欣赏美景。");
            chatHistory.AddUserMessage("四川有哪些适合拍照的地方？");
            chatHistory.AddAssistantMessage("九寨沟、稻城亚丁、都江堰、宽窄巷子等都是拍照的好地方。");
            chatHistory.AddUserMessage("四川的辣会不会太辣？");
            chatHistory.AddAssistantMessage("四川菜以麻辣著称，但大多数餐厅可以根据口味调整辣度。");
            chatHistory.AddUserMessage("成都有哪些有特色的咖啡馆？");
            chatHistory.AddAssistantMessage("成都有许多文艺小众的咖啡馆，比如宽窄巷子附近的独立咖啡店。");
            chatHistory.AddUserMessage("四川有哪些世界遗产？");
            chatHistory.AddAssistantMessage("四川有九寨沟、黄龙、都江堰、青城山、乐山大佛等世界遗产。");
            chatHistory.AddUserMessage("去四川旅游安全吗？");
            chatHistory.AddAssistantMessage("四川整体治安良好，旅游景区管理规范，是安全的旅游目的地。");
            chatHistory.AddUserMessage("四川有哪些适合自驾游的路线？");
            chatHistory.AddAssistantMessage("成都-都江堰-青城山-乐山-峨眉山-九寨沟是一条经典自驾路线。");
            chatHistory.AddUserMessage("四川的住宿条件怎么样？");
            chatHistory.AddAssistantMessage("四川各大城市和景区都有多种住宿选择，从经济型到高端酒店都有。");
            chatHistory.AddUserMessage("四川有哪些适合夜游的地方？");
            chatHistory.AddAssistantMessage("成都锦里、宽窄巷子、春熙路夜景都很美，适合夜游。");
            chatHistory.AddUserMessage("四川有哪些适合购物的地方？");
            chatHistory.AddAssistantMessage("成都春熙路、太古里、IFS等都是购物的好去处。");
            chatHistory.AddUserMessage("四川有哪些著名的温泉？");
            chatHistory.AddAssistantMessage("青城山温泉、海螺沟温泉、峨眉山温泉都很有名。");
            chatHistory.AddUserMessage("四川有哪些适合徒步的地方？");
            chatHistory.AddAssistantMessage("稻城亚丁、四姑娘山、九寨沟等都是徒步爱好者的天堂。");

            var clientResult = await chatCompletionService.GetChatMessageContentAsync(chatHistory, chatCompletionOptions, kernel);
            string result = MarkdownCleaner.CleanMarkdown(Regex.Replace(Regex.Unescape(clientResult.Content), @"<think>.*?</think>", "", RegexOptions.Singleline));
            Console.WriteLine(result);
        }
    }
}
