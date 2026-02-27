using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.SchemaDefinitions;
using System.Diagnostics.CodeAnalysis;

namespace CS2.EntityFix.SwiftlyS2;

public static class Extensions
{
    public static bool IsPlayerAlive(this CCSPlayerPawn? pawn)
    {
        if (!pawn.Valid())
            return false;
        return (LifeState_t)pawn.LifeState == LifeState_t.LIFE_ALIVE;
    }

    public static bool Valid([NotNullWhen(true)] this CBasePlayerPawn? pawn)
    {
        try
        {
            if (pawn is null)
                return false;
            if (!pawn.IsValid)
                return false;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
