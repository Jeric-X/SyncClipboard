namespace SyncClipboard.Core.Utilities;

public delegate void MessageHandler<TMessage>(TMessage config);
public abstract class MessageDispatcher
{
    public abstract void Invoke(object config);
    public sealed class For<TMessage>(MessageHandler<TMessage> handler) : MessageDispatcher
    {
        public override void Invoke(object config)
        {
            handler((TMessage)config);
        }
    }
}
