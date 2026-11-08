using Content.Client.Chat.UI;
using Content.Client.Viewport;
using Content.Shared.Chat;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client.SurveillanceCamera.UI;

/// <summary>
/// Обычные облачки речи над говорящими поверх вида камеры в мониторе.
/// </summary>
public sealed class FishCameraSpeechLog : LayoutContainer
{
    [Dependency] private IEntityManager _entMan = default!;

    private readonly Dictionary<EntityUid, SpeechBubble> _bubbles = new();

    public FishCameraSpeechLog()
    {
        IoCManager.InjectDependencies(this);
        MouseFilter = MouseFilterMode.Ignore;
    }

    public void AddLine(ScalingViewport viewport, NetEntity speaker, string name, string message)
    {
        if (!_entMan.TryGetEntity(speaker, out var uid))
            return;

        if (_bubbles.Remove(uid.Value, out var old))
            RemoveChild(old);

        var wrapped = $"[BubbleHeader]{FormattedMessage.EscapeText(name)}[/BubbleHeader][BubbleContent]{FormattedMessage.EscapeText(message)}[/BubbleContent]";
        var chatMessage = new ChatMessage(ChatChannel.Local, message, wrapped, speaker, null);
        var bubble = SpeechBubble.CreateSpeechBubble(SpeechBubble.SpeechType.Say, chatMessage, uid.Value);

        bubble.FishViewport = viewport;
        bubble.MouseFilter = MouseFilterMode.Ignore;
        bubble.OnDied += OnBubbleDied;
        _bubbles[uid.Value] = bubble;
        AddChild(bubble);
    }

    private void OnBubbleDied(EntityUid speaker, SpeechBubble bubble)
    {
        if (_bubbles.TryGetValue(speaker, out var current) && current == bubble)
            _bubbles.Remove(speaker);

        RemoveChild(bubble);
    }
}
