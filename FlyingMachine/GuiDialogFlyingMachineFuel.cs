using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace FlyingMachine;

/// <summary>
/// Fylle-vinduet: [beholder-slot] → [pil] → [tank-måler]. Samme oppsett som kokegropa.
/// Serveren heller væsken; vinduet leser bare slot + tankmengde og tegner.
/// </summary>
public class GuiDialogFlyingMachineFuel : GuiDialog
{
    readonly EntityFlyingMachine plane;
    long tickListener;

    // Oppsett (GUI-enheter, uskalert). Innhold starter under tittellinja.
    const double SlotY = 60;
    const double ArrowX = 63;
    const double GaugeX = 165, GaugeY = 40, GaugeW = 34, GaugeH = 90;

    public override string? ToggleKeyCombinationCode => null;
    public override bool PrefersUngrabbedMouse => false;

    public GuiDialogFlyingMachineFuel(ICoreClientAPI capi, EntityFlyingMachine plane) : base(capi)
    {
        this.plane = plane;
        Compose();
    }

    void Compose()
    {
        var drawBounds = ElementBounds.Fixed(0, 0, 230, 140);
        var slotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, SlotY, 1, 1);
        var litresBounds = ElementBounds.Fixed(GaugeX + GaugeW / 2 - 50, GaugeY + GaugeH + 6, 100, 20);
        var statusBounds = ElementBounds.Fixed(0, 160, 230, 36);

        var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;
        bgBounds.WithChildren(drawBounds, statusBounds);

        var dialogBounds = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);

        SingleComposer = capi.Gui.CreateCompo("flyingmachine-fuel-" + plane.EntityId, dialogBounds)
            .AddShadedDialogBG(bgBounds, true)
            .AddDialogTitleBar(Lang.Get("flyingmachine:fuel-title"), () => TryClose())
            .BeginChildElements(bgBounds)
                .AddDynamicCustomDraw(drawBounds, OnDraw, "symbols")
                .AddItemSlotGrid(plane.FuelInventory, SendInvPacket, 1, slotBounds, "fuelslot")
                .AddDynamicText("", CairoFont.WhiteDetailText().WithOrientation(EnumTextOrientation.Center), litresBounds, "litres")
                .AddDynamicText("", CairoFont.WhiteDetailText(), statusBounds, "status")
            .EndChildElements()
            .Compose();
    }

    void SendInvPacket(object packet)
    {
        capi.Network.SendEntityPacketWithOffset(plane.EntityId, 0, packet);
    }

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        capi.World.Player.InventoryManager.OpenInventory(plane.FuelInventory);
        tickListener = capi.Event.RegisterGameTickListener(_ => Refresh(), 100);
        Refresh();
    }

    public override void OnGuiClosed()
    {
        base.OnGuiClosed();
        capi.Event.UnregisterGameTickListener(tickListener);
        SingleComposer.GetSlotGrid("fuelslot")?.OnGuiClosed(capi);
        capi.World.Player.InventoryManager.CloseInventoryAndSync(plane.FuelInventory);
    }

    public override void OnFinalizeFrame(float dt)
    {
        base.OnFinalizeFrame(dt);
        if (!plane.Alive || !capi.World.Player.IsInInteractionRangeOf(plane, 0.25f))
        {
            capi.Event.EnqueueMainThreadTask(() => TryClose(), "closeflyingmachinefuel");
        }
    }

    void Refresh()
    {
        if (!IsOpened()) return;
        SingleComposer.GetDynamicText("litres").SetNewText($"{plane.FuelLitres:0.0} / {plane.FuelCapacity:0} L");
        SingleComposer.GetDynamicText("status").SetNewText(Lang.Get("flyingmachine:" + plane.FuelSlotState()));
        SingleComposer.GetCustomDraw("symbols").Redraw();
    }

    void OnDraw(Context ctx, ImageSurface surface, ElementBounds bounds)
    {
        bool pouring = plane.FuelSlotState() == "fuel-pouring";

        // --- Pil: grå kontur, grønn fylling som feier mens det renner (som kokegropa) ---
        ctx.Save();
        var m = ctx.Matrix;
        m.Translate(GuiElement.scaled(ArrowX), GuiElement.scaled(SlotY + 2));
        m.Scale(GuiElement.scaled(0.6), GuiElement.scaled(0.6));
        ctx.Matrix = m;
        capi.Gui.Icons.DrawArrowRight(ctx, 2, true, true);
        if (pouring)
        {
            double t = (capi.ElapsedMilliseconds % 1200) / 1200.0;
            ctx.Rectangle(5, 0, 125 * t, 100);
            ctx.Clip();
            var grad = new LinearGradient(0, 0, 200, 0);
            grad.AddColorStop(0, new Color(0, 0.4, 0, 1));
            grad.AddColorStop(1, new Color(0.2, 0.6, 0.2, 1));
            ctx.SetSource(grad);
            capi.Gui.Icons.DrawArrowRight(ctx, 0, false, false);
            grad.Dispose();
        }
        ctx.Restore();

        // --- Tank-måler ---
        double x = GuiElement.scaled(GaugeX), y = GuiElement.scaled(GaugeY);
        double w = GuiElement.scaled(GaugeW), h = GuiElement.scaled(GaugeH);

        ctx.SetSourceRGBA(0, 0, 0, 0.35);
        ctx.Rectangle(x, y, w, h);
        ctx.Fill();

        double frac = plane.FuelCapacity > 0 ? Math.Clamp(plane.FuelLitres / plane.FuelCapacity, 0, 1) : 0;
        if (frac > 0)
        {
            double fillH = h * frac;
            var liquid = new LinearGradient(x, 0, x + w, 0);
            liquid.AddColorStop(0, new Color(0.78, 0.66, 0.38, 0.95));
            liquid.AddColorStop(0.5, new Color(0.92, 0.82, 0.55, 0.95));
            liquid.AddColorStop(1, new Color(0.72, 0.60, 0.34, 0.95));
            ctx.SetSource(liquid);
            ctx.Rectangle(x, y + h - fillH, w, fillH);
            ctx.Fill();
            liquid.Dispose();
        }

        // Skalastreker per 2 L + ramme
        ctx.SetSourceRGBA(0.1, 0.07, 0.04, 0.8);
        ctx.LineWidth = GuiElement.scaled(1);
        for (int i = 1; i < 5; i++)
        {
            double ly = y + h - h * i / 5;
            ctx.MoveTo(x, ly);
            ctx.LineTo(x + w * 0.3, ly);
        }
        ctx.Stroke();
        ctx.LineWidth = GuiElement.scaled(2);
        ctx.SetSourceRGBA(0.35, 0.27, 0.18, 1);
        ctx.Rectangle(x, y, w, h);
        ctx.Stroke();
    }
}

/// <summary>Fylle-sloten tar bare én væskebeholder om gangen.</summary>
public class ItemSlotFlyingMachineFuel : ItemSlot
{
    public ItemSlotFlyingMachineFuel(InventoryBase inventory) : base(inventory)
    {
        MaxSlotStackSize = 1;
    }

    public override bool CanHold(ItemSlot sourceSlot)
    {
        return sourceSlot?.Itemstack?.Collectible is Vintagestory.GameContent.ILiquidSource && base.CanHold(sourceSlot);
    }
}
