using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Handlers
{
    internal class DialogueHandler : BaseHandler, IInHandler<string, string>, IOutHandler<OutSegment>
    {
        private readonly ObjectPool<Workflow<string>> _stringWorkflowPool;
        private readonly ObjectPool<Workflow<OutSegment>> _outSegmentWorkflowPool;
        private readonly ObjectPool<OutSegment> _outSegmentPool;

        private ILlm? _llm;

        public DialogueHandler(ObjectPool<Workflow<string>> stringWorkflowPool,
            ObjectPool<Workflow<OutSegment>> outSegmentWorkflowPool,
            ObjectPool<OutSegment> outSegmentPool,
            XiaoZhiConfig config,
            ILogger<DialogueHandler> logger) : base(config, logger)
        {
            this._stringWorkflowPool = stringWorkflowPool;
            this._outSegmentWorkflowPool = outSegmentWorkflowPool;
            this._outSegmentPool = outSegmentPool;
        }

        public override string HandlerName => nameof(DialogueHandler);
        public ChannelReader<Workflow<string>> PreviousReader { get; set; } = null!;
        public ChannelReader<Workflow<string>> PreviousReader2 { get; set; } = null!;
        public ChannelWriter<Workflow<OutSegment>> NextWriter { get; set; } = null!;

        public override bool Build(PrivateProvider privateProvider)
        {
            Session session = this.SendOutter.GetSession();
            if (privateProvider.Llm is null)
            {
                this.Logger.LogError(Lang.DialogueHandler_Build_LlmNotConfigured, session.DeviceId);
                return false;
            }

            this._llm = privateProvider.Llm;
            this._llm.OnBeforeTokenGenerate += this.OnBeforeTokenGenerate;
            this._llm.OnTokenGenerating += this.OnTokenGeneratingAsync;
            this._llm.OnTokenGenerated += this.OnTokenGenerated;
            this._llm.RegisterDevice(session.DeviceId, session.SessionId);
            this.RegisterCancellationToken();
            this.Builded = true;
            return true;
        }

        public async Task HandleAsync()
        {
            await foreach (var workflow in this.PreviousReader.ReadAllAsync())
            {
                try
                {
                    await this.HandleAsync(workflow);
                }
                finally
                {
                    this._stringWorkflowPool.Return(workflow);
                }
            }
        }

        public async Task Handle2Async()
        {
            await foreach (var workflow in this.PreviousReader2.ReadAllAsync())
            {
                try
                {
                    await this.HandleAsync(workflow);
                }
                finally
                {
                    this._stringWorkflowPool.Return(workflow);
                }
            }
        }

        public async Task HandleAsync(Workflow<string> workflow)
        {
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }

            if (!this.CheckWorkflowValid(workflow))
            {
                return;
            }

            if (this._llm is null)
            {
                this.Logger.LogError(Lang.DialogueHandler_Handle_LlmNotConfigured, session.DeviceId);
                return;
            }

            if (!session.IsDeviceBinded)
            {
                var outSegment = this._outSegmentPool.Get();
                var notBindWorkflow = this._outSegmentWorkflowPool.Get();

                outSegment.Initialize("NOT_BIND", true, true, Emotion.Neutral);
                notBindWorkflow.Initialize(session, outSegment);
                await this.NextWriter.WriteAsync(notBindWorkflow);
                return;
            }

            try
            {
                // 若 MCP 已启用但工具列表尚未加载完毕，等待就绪信号（最多 5 秒）
                // 避免首次对话因竞态而拿到空工具列表
                Task? mcpReadyTask = session.PrivateProvider.McpClientReadyTask;
                if (mcpReadyTask is not null && !mcpReadyTask.IsCompleted)
                {
                    try
                    {
                        await mcpReadyTask.WaitAsync(TimeSpan.FromSeconds(5), this.HandlerToken);
                    }
                    catch (TimeoutException)
                    {
                        this.Logger.LogWarning(Lang.DialogueHandler_Handle_McpToolsNotReady, session.DeviceId);
                    }
                }

                using (CodeTimer timer = CodeTimer.Create(Lang.DialogueHandler_Handle_LlmCallTime, this.Logger))
                {
                    await this._llm.StartDialogueAsync(workflow.Data, this.HandlerToken);
                }
            }
            catch (OperationCanceledException)
            {
                this.Logger.LogDebug(Lang.DialogueHandler_Handle_Cancelled, session.DeviceId);
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.DialogueHandler_Handle_ProcessFailed, session.DeviceId);
            }
        }

        public async void NoVoiceCloseConnectAsync(Workflow<string> workflow)
        {
            await this.HandleAsync(workflow);
        }

        private void OnBeforeTokenGenerate()
        {
            this.SendOutter.SendLlmMessageAsync(Emotion.Thinking);
            this.SendOutter.SendSttMessageAsync(Lang.DialogueHandler_OnBeforeTokenGenerate_Thinking);
        }

        private async void OnTokenGeneratingAsync(OutSegment outSegment)
        {
            if (this.HandlerToken.IsCancellationRequested)
            {
                return;
            }
            
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }

            var clonedSegment = this._outSegmentPool.Get();
            clonedSegment.Initialize(outSegment.Content, outSegment.IsFirstSegment, outSegment.IsLastSegment, outSegment.Emotion, outSegment.ParagraphId, outSegment.SentenceId);

            var workflow = this._outSegmentWorkflowPool.Get();
            workflow.Initialize(session, clonedSegment);

            try
            {
                await this.NextWriter.WriteAsync(workflow, this.HandlerToken);
            }
            catch (OperationCanceledException)
            {
                this._outSegmentPool.Return(clonedSegment);
                this._outSegmentWorkflowPool.Return(workflow);
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.DialogueHandler_OnTokenGenerating_WriteFailed, session.DeviceId);
                this._outSegmentPool.Return(clonedSegment);
                this._outSegmentWorkflowPool.Return(workflow);
            }
        }

        private void OnTokenGenerated(IEnumerable<OutSegment> outSegments)
        {
            if (this.HandlerToken.IsCancellationRequested)
            {
                // Still need to return segments to pool even if cancelled
                foreach (var seg in outSegments)
                {
                    this._outSegmentPool.Return(seg);
                }
                return;
            }
            
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                foreach (var seg in outSegments)
                {
                    this._outSegmentPool.Return(seg);
                }
                return;
            }

            this.Logger.LogDebug(Lang.DialogueHandler_OnTokenGenerated_ResponseText, string.Join(string.Empty, outSegments.Select(o => o.Content)));
            foreach (var seg in outSegments)
            {
                this._outSegmentPool.Return(seg);
            }
        }

        public override void Dispose()
        {
            if (this._llm is not null)
            {
                this._llm.OnBeforeTokenGenerate -= this.OnBeforeTokenGenerate;
                this._llm.OnTokenGenerating -= this.OnTokenGeneratingAsync;
                this._llm.OnTokenGenerated -= this.OnTokenGenerated;
                Session session = this.SendOutter.GetSession();
                if (session is not null)
                {
                    this._llm.UnregisterDevice(session.DeviceId, session.SessionId);
                }
                this._llm.Dispose();
            }
            this.NextWriter.Complete();
            base.Dispose();
        }
    }
}
