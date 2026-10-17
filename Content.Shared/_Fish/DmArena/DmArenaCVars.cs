using Robust.Shared.Configuration;

namespace Content.Shared._Fish.DmArena;

[CVarDefs]
public sealed class DmArenaCVars
{
    public static readonly CVarDef<bool> DmArenaEnabled =
        CVarDef.Create("dmarena.enabled", true, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<bool> DmArenaRefill =
        CVarDef.Create("dmarena.refill", true, CVar.SERVER | CVar.REPLICATED);
}
