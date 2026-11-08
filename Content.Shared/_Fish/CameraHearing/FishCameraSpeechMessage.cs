using Robust.Shared.Serialization;

namespace Content.Shared._Fish.CameraHearing;

/// <summary>
/// Речь, услышанная камерой, для показа на экране монитора камер.
/// </summary>
[Serializable, NetSerializable]
public sealed class FishCameraSpeechMessage(string speaker, string message) : BoundUserInterfaceMessage
{
    public string Speaker { get; } = speaker;
    public string Message { get; } = message;
}
