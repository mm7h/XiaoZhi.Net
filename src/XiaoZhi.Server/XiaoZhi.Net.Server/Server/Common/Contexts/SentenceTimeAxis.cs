using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class SentenceTimeAxis
    {
        private readonly Channel<Func<Task>> _sendSentenceAction;
        private readonly SemaphoreSlim _sendSentenceActionSlim = new SemaphoreSlim(1, 1);

        public SentenceTimeAxis()
        {
#if DEBUG
            int capacity = 100;
#else
            int capacity = 1000;
#endif
            BoundedChannelOptions boundedChannelOptions = new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = false,
                SingleReader = true
            };

            this._sendSentenceAction = Channel.CreateBounded<Func<Task>>(boundedChannelOptions);
            _ = this.HandleSendSentenceAction();
        }


        private async Task HandleSendSentenceAction()
        {
            await foreach (Func<Task> func in this._sendSentenceAction.Reader.ReadAllAsync())
            {
                try
                {
                    await this._sendSentenceActionSlim.WaitAsync();
                    await func();
                }
                finally
                {
                    this._sendSentenceActionSlim.Release();
                }
            }
        }

        public async Task AddSendSentenceActionAsync(Func<Task> func, CancellationToken token)
        {
            await this._sendSentenceAction.Writer.WriteAsync(func, token);
        }

        public void Reset()
        {
        }

        public void Release()
        {
            this._sendSentenceAction.Writer.Complete();
        }
    }
}
