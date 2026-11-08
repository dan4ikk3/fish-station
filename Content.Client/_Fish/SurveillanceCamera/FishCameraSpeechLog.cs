using System.Numerics;
using Content.Client.Chat.UI;
using Content.Client.Viewport;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.SurveillanceCamera.UI;

/// <summary>
/// Облачки речи над говорящими поверх вида камеры в мониторе, в том же стиле, что и обычные.
/// </summary>
public sealed class FishCameraSpeechLog : Control
{
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    private static readonly TimeSpan BubbleLifetime = TimeSpan.FromSeconds(4);
    private const float HeadOffset = 0.5f;

    private readonly List<(EntityUid Speaker, Control Bubble, TimeSpan Expire)> _bubbles = new();
    private ScalingViewport? _viewport;

    public FishCameraSpeechLog()
    {
        IoCManager.InjectDependencies(this);
        MouseFilter = MouseFilterMode.Ignore;
    }

    public void AddLine(ScalingViewport viewport, NetEntity speaker, string message)
    {
        if (!_entMan.TryGetEntity(speaker, out var uid))
            return;

        _viewport = viewport;
        Remove(uid.Value);

        var label = new RichTextLabel
        {
            MaxWidth = SpeechBubble.SpeechMaxWidth,
            OutlineColorOverride = TextOutline.Default.Color,
        };
        label.SetMessage(FormattedMessage.FromUnformatted(message));

        var bubble = new PanelContainer
        {
            StyleClasses = { "speechBox", "sayBox" },
            Children = { label },
            ModulateSelfOverride = Color.White.WithAlpha(_cfg.GetCVar(CCVars.SpeechBubbleBackgroundOpacity)),
            MouseFilter = MouseFilterMode.Ignore,
        };
        AddChild(bubble);
        _bubbles.Add((uid.Value, bubble, _timing.RealTime + BubbleLifetime));
    }

    private void Remove(EntityUid speaker)
    {
        for (var i = _bubbles.Count - 1; i >= 0; i--)
        {
            if (_bubbles[i].Speaker != speaker)
                continue;

            RemoveChild(_bubbles[i].Bubble);
            _bubbles.RemoveAt(i);
        }
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        for (var i = _bubbles.Count - 1; i >= 0; i--)
        {
            if (_bubbles[i].Expire > _timing.RealTime && !_entMan.Deleted(_bubbles[i].Speaker))
                continue;

            RemoveChild(_bubbles[i].Bubble);
            _bubbles.RemoveAt(i);
        }

        InvalidateArrange();
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        if (_viewport == null)
            return finalSize;

        var transform = _entMan.System<SharedTransformSystem>();

        foreach (var (speaker, bubble, _) in _bubbles)
        {
            bubble.Measure(Vector2Helpers.Infinity);

            var offset = (-_viewport.Eye!.Rotation).ToWorldVec() * -HeadOffset;
            var screen = _viewport.WorldToScreen(transform.GetWorldPosition(speaker) + offset);
            var local = (screen - GlobalPixelPosition) / UIScale;
            var size = bubble.DesiredSize;

            bubble.Arrange(UIBox2.FromDimensions(local - new Vector2(size.X / 2f, size.Y), size));
        }

        return finalSize;
    }
}
