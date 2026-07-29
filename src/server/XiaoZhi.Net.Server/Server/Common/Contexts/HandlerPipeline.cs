using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Handlers;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class HandlerPipeline
    {
        private HelloMessageHandler? _helloMessageHandler;
        private TextHandler? _textHandler;
        private AudioReceiveHandler? _audioReceiveHandler;

        private IDictionary<string, IHandler>? _handlerContainer;

        public void InitHelloMessageHandler(HelloMessageHandler helloMessageHandler)
        {
            this._helloMessageHandler = helloMessageHandler ?? throw new ArgumentNullException(nameof(helloMessageHandler));
        }

        public void InitHandlerPipeline(IDictionary<string, IHandler> handlerContainer)
        {
            this._handlerContainer = handlerContainer;
            this._textHandler = handlerContainer[nameof(TextHandler)] as TextHandler ?? throw new ArgumentNullException(nameof(TextHandler));
            this._audioReceiveHandler = handlerContainer[nameof(AudioReceiveHandler)] as AudioReceiveHandler ?? throw new ArgumentNullException(nameof(AudioReceiveHandler));
        }

        public async ValueTask HandleHelloMessage(JsonObject helloMessage)
        {
            if (this._helloMessageHandler is not null)
            {
                await this._helloMessageHandler.HandleAsync(helloMessage);
            }
            else
            {
                throw new InvalidOperationException("HelloMessageHandler is not initialized");
            }
        }

        public void HandleTextMessage(string data)
        {
            if (this._textHandler is not null)
            {
                this._textHandler.HandleAsync(data);
            }
            else
            {
                throw new InvalidOperationException("TextHandler is not initialized");
            }
        }

        public async Task HandleBinaryMessageAsync(byte[] data)
        {
            if (this._audioReceiveHandler is not null)
            {
                await this._audioReceiveHandler.HandleAsync(data);
            }
            else
            {
                throw new InvalidOperationException("AudioReceiveHandler is not initialized");
            }
        }

        public void Release()
        {
            if (this._handlerContainer is null || this._handlerContainer.Count == 0)
            {
                return;
            }
            foreach (IDisposable handler in this._handlerContainer.Values)
            {
                handler.Dispose();
            }
            this._handlerContainer.Clear();
        }


    }
}
