namespace Content.Server.Holopad;

/// <summary>
/// Запрещено выдавать вручную!
/// </summary>
[RegisterComponent]
public sealed partial class RemoteHolopadViewerComponent : Component
{
    public EntityUid Receiver;
    public EntityUid? ExitViewActionEntity;
}
