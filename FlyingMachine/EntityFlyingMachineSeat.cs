using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace FlyingMachine;

/// <summary>
/// Sete i flyet. Som vanilla-båtsetet, men Shift (stig av) virker ikke høyt oppe i lufta —
/// Shift ligger rett ved Ctrl (nesa ned), og et feiltrykk ville sendt piloten rett i bakken.
/// Bare tastetrykket sperres: død, teleport og utlogging går fortsatt via TryUnmount som vanlig.
/// </summary>
public class EntityFlyingMachineSeat : EntityBoatSeat
{
    long lastWarnMs;

    public EntityFlyingMachineSeat(IMountable mountable, string seatId, SeatConfig config) : base(mountable, seatId, config)
    {
        var vanilla = Controls.OnAction;
        Controls.OnAction = (EnumEntityAction action, bool on, ref EnumHandling handled) =>
        {
            if (action == EnumEntityAction.Sneak && on && Entity is EntityFlyingMachine plane && plane.DismountBlocked())
            {
                Warn(plane);
                return;
            }
            vanilla(action, on, ref handled);
        };
    }

    void Warn(EntityFlyingMachine plane)
    {
        if (plane.Api is not ICoreClientAPI capi || capi.World.Player?.Entity != Passenger) return;
        if (capi.World.ElapsedMilliseconds - lastWarnMs < 2000) return;
        lastWarnMs = capi.World.ElapsedMilliseconds;
        capi.TriggerIngameError(this, "flyingmachine-nodismount", Lang.Get("flyingmachine:nodismount-air"));
    }
}
