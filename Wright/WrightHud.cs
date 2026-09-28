using Vintagestory.API.Client;

namespace Wright;

/// <summary>Enkel instrumentlinje mens du sitter i flyet: gass, fart, høyde, løft.</summary>
public class WrightHud : HudElement
{
    public WrightHud(ICoreClientAPI capi) : base(capi)
    {
        var bounds = ElementBounds.Fixed(EnumDialogArea.CenterBottom, 0, -110, 520, 30);
        var textBounds = ElementBounds.Fixed(0, 0, 520, 30);
        SingleComposer = capi.Gui.CreateCompo("wright-hud", bounds)
            .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), textBounds, "status")
            .Compose();
    }

    public void Update()
    {
        var plane = capi.World.Player?.Entity?.MountedOn?.Entity as EntityWright;
        if (plane == null)
        {
            if (IsOpened()) TryClose();
            return;
        }
        if (!IsOpened()) TryOpen();

        double blocksPerSecond = plane.ForwardSpeed * 60;
        string state = plane.Stalling ? "STALL" : plane.Flying ? "AIRBORNE" : (plane.LiftRatio >= 0.8 ? "ROTATE - hold Space" : "ground");
        double pitchDeg = plane.PitchAngle * 180 / System.Math.PI;
        SingleComposer.GetDynamicText("status").SetNewText(
            $"Throttle {plane.Throttle * 100:0}%   Speed {blocksPerSecond:0.0} b/s   Lift {plane.LiftRatio * 100:0}%   Pitch {pitchDeg:+0;-0;0}°   Alt {plane.Pos.Y:0}   {state}");
    }

    public override bool Focusable => false;
}
