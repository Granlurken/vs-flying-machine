using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace Wright;

/// <summary>
/// Sete i flyet. Som vanilla-båtsetet, men Shift (stig av) virker ikke høyt oppe i lufta —
/// Shift ligger rett ved Ctrl (nesa ned), og et feiltrykk ville sendt piloten rett i bakken.
/// Bare tastetrykket sperres: død, teleport og utlogging går fortsatt via TryUnmount som vanlig.
/// </summary>
public class EntityWrightSeat : EntityBoatSeat
{
    long lastWarnMs;

    public EntityWrightSeat(IMountable mountable, string seatId, SeatConfig config) : base(mountable, seatId, config)
    {
        var vanilla = Controls.OnAction;
        Controls.OnAction = (EnumEntityAction action, bool on, ref EnumHandling handled) =>
        {
            if (action == EnumEntityAction.Sneak && on && Entity is EntityWright plane && plane.DismountBlocked())
            {
                Warn(plane);
                return;
            }
            vanilla(action, on, ref handled);
        };
    }

    void Warn(EntityWright plane)
    {
        if (plane.Api is not ICoreClientAPI capi || capi.World.Player?.Entity != Passenger) return;
        if (capi.World.ElapsedMilliseconds - lastWarnMs < 2000) return;
        lastWarnMs = capi.World.ElapsedMilliseconds;
        capi.TriggerIngameError(this, "wright-nodismount", Lang.Get("wright:nodismount-air"));
    }
}
