namespace SyncClipboard.Core.Utilities;

public delegate void MessageHandler<TMessage>(TMessage config);
public abstract class MessageDispatcher
{
    public abstract void Invoke(object config);
    public sealed class For<TMessage> : MessageDispatcher
    {
        private readonly MessageHandler<TMessage> _handler;
        public For(MessageHandler<TMessage> handler)
        {
            _handler = handler;
        }

        public override void Invoke(object config)
        {
            _handler((TMessage)config);
        }
    }
}
