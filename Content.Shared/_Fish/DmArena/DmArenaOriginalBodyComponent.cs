using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Fish.DmArena;

[RegisterComponent]
public sealed partial class DmArenaOriginalBodyComponent : Component
{
    [DataField]
    public NetUserId Owner;
}
