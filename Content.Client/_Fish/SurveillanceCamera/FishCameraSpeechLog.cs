using System.Numerics;
using Content.Client.Viewport;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.SurveillanceCamera.UI;

/// <summary>
/// Облачки речи над говорящими поверх вида камеры в мониторе.
/// </summary>
public sealed class FishCameraSpeechLog : Control
{
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly TimeSpan BubbleLifetime = TimeSpan.FromSeconds(6);
    private const float BubbleMaxWidth = 200f;
    private const float HeadOffset = 24f;

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

        var label = new RichTextLabel { MaxWidth = BubbleMaxWidth, Margin = new Thickness(4) };
        label.SetMessage(FormattedMessage.FromUnformatted(message));

        var bubble = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = Color.Black.WithAlpha(0.7f) },
            MouseFilter = MouseFilterMode.Ignore,
        };
        bubble.AddChild(label);
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
            bubble.Measure(Vector2.Max(finalSize, new Vector2(BubbleMaxWidth, BubbleMaxWidth)));

            var screen = _viewport.WorldToScreen(transform.GetWorldPosition(speaker));
            var local = (screen - GlobalPixelPosition) / UIScale;
            var size = bubble.DesiredSize;

            bubble.Arrange(UIBox2.FromDimensions(local - new Vector2(size.X / 2f, size.Y + HeadOffset), size));
        }

        return finalSize;
    }
}
