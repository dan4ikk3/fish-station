using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.SurveillanceCamera.UI;

/// <summary>
/// Строки речи поверх вида камеры в мониторе, исчезают через несколько секунд.
/// </summary>
public sealed class FishCameraSpeechLog : RichTextLabel
{
    [Dependency] private IGameTiming _timing = default!;

    private static readonly TimeSpan LineLifetime = TimeSpan.FromSeconds(8);

    private readonly List<(string Text, TimeSpan Expire)> _lines = new();

    public FishCameraSpeechLog()
    {
        IoCManager.InjectDependencies(this);
    }

    public void AddLine(string speaker, string message)
    {
        _lines.Add(($"{FormattedMessage.EscapeText(speaker)}: {FormattedMessage.EscapeText(message)}", _timing.RealTime + LineLifetime));
        Refresh();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_lines.RemoveAll(line => line.Expire <= _timing.RealTime) > 0)
            Refresh();
    }

    private void Refresh()
    {
        var message = new FormattedMessage();

        for (var i = 0; i < _lines.Count; i++)
        {
            if (i > 0)
                message.PushNewline();

            message.AddMarkupPermissive(_lines[i].Text);
        }

        SetMessage(message);
    }
}
