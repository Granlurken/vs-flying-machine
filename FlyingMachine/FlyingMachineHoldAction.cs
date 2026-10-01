using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace FlyingMachine;

/// <summary>
/// «Hold inne for å gjøre»: et tannhjul snurrer ved trådkorset med en ring som fylles, og skralle-klikk spilles.
/// Avbrytes hvis spilleren slipper høyre museknapp eller Shift, ser bort fra entiteten eller bytter verktøy.
/// Ferdig → <c>onDone</c> (som sender en pakke til serveren; serveren sjekker alt på nytt).
/// Kun klient. Tuning i entitetens <c>attributes.disassembly</c>: seconds, tickSound, tickInterval, doneSound.
/// </summary>
public class FlyingMachineHoldAction : IRenderer
{
    static FlyingMachineHoldAction? current;

    readonly ICoreClientAPI capi;
    readonly Entity target;
    readonly JsonObject toolAttributes;
    readonly Action onDone;
    readonly double seconds;
    readonly AssetLocation? tickSound, doneSound;
    readonly double tickInterval;
    double elapsed, nextTick;
    LoadedTexture? tex;
    const int Size = 72;

    public double RenderOrder => 1;
    public int RenderRange => 0;

    FlyingMachineHoldAction(ICoreClientAPI capi, Entity target, JsonObject attributes, Action onDone)
    {
        this.capi = capi;
        this.target = target;
        this.onDone = onDone;
        toolAttributes = attributes;
        var cfg = attributes["disassembly"];
        seconds = cfg["seconds"].AsDouble(5);
        tickInterval = cfg["tickInterval"].AsDouble(0.6);
        string? tick = cfg["tickSound"].AsString("game:sounds/effect/latch");
        string? done = cfg["doneSound"].AsString("game:sounds/player/build");
        tickSound = string.IsNullOrEmpty(tick) ? null : new AssetLocation(tick);
        doneSound = string.IsNullOrEmpty(done) ? null : new AssetLocation(done);
    }

    /// <summary>Start holdingen mot <paramref name="target"/>, med mindre den allerede går.</summary>
    public static void Start(ICoreClientAPI capi, Entity target, JsonObject attributes, Action onDone)
    {
        if (current != null) return;
        current = new FlyingMachineHoldAction(capi, target, attributes, onDone);
        capi.Event.RegisterRenderer(current, EnumRenderStage.Ortho, "flyingmachinehold");
    }

    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        var player = capi.World.Player;
        bool holding = capi.Input.InWorldMouseButton.Right
            && player.Entity.Controls.ShiftKey
            && player.CurrentEntitySelection?.Entity == target
            && target.Alive
            && FlyingMachineMaterials.IsDisassemblyTool(player.InventoryManager.ActiveHotbarSlot?.Itemstack, toolAttributes);
        if (!holding)
        {
            Stop();
            return;
        }

        elapsed += dt;
        if (tickSound != null && elapsed >= nextTick)
        {
            capi.World.PlaySoundAt(tickSound, target, null, true, 16, 0.6f);
            nextTick += tickInterval;
        }
        if (elapsed >= seconds)
        {
            if (doneSound != null) capi.World.PlaySoundAt(doneSound, target, null, true, 16, 1f);
            Stop();
            onDone();
            return;
        }

        Draw();
        float size = (float)GuiElement.scaled(Size);
        float x = capi.Render.FrameWidth / 2f - size / 2f;
        float y = capi.Render.FrameHeight / 2f - size / 2f;
        capi.Render.Render2DTexturePremultipliedAlpha(tex!.TextureId, x, y, size, size);
    }

    void Draw()
    {
        int px = (int)GuiElement.scaled(Size);
        using var surface = new ImageSurface(Format.Argb32, px, px);
        using var ctx = new Context(surface);
        double c = px / 2.0;
        double progress = Math.Min(1, elapsed / seconds);

        // Ring: mørk bakgrunn, messingfarget fremdrift fra klokka 12 og med klokka
        ctx.LineWidth = px * 0.07;
        ctx.SetSourceRGBA(0, 0, 0, 0.45);
        ctx.Arc(c, c, px * 0.43, 0, 2 * Math.PI);
        ctx.Stroke();
        ctx.SetSourceRGBA(0.85, 0.68, 0.32, 0.95);
        ctx.Arc(c, c, px * 0.43, -Math.PI / 2, -Math.PI / 2 + progress * 2 * Math.PI);
        ctx.Stroke();

        // Tannhjul som snurrer
        const int teeth = 8;
        double outer = px * 0.30, inner = px * 0.23, hole = px * 0.09;
        double rot = elapsed * 2.5;
        ctx.NewPath();
        for (int i = 0; i < teeth * 4; i++)
        {
            // Hver tann: to punkter ute, to inne
            double a = rot + i * Math.PI * 2 / (teeth * 4);
            double r = (i % 4 == 1 || i % 4 == 2) ? outer : inner;
            double vx = c + Math.Cos(a) * r, vy = c + Math.Sin(a) * r;
            if (i == 0) ctx.MoveTo(vx, vy); else ctx.LineTo(vx, vy);
        }
        ctx.ClosePath();
        ctx.NewSubPath();
        ctx.Arc(c, c, hole, 0, 2 * Math.PI);
        ctx.FillRule = FillRule.EvenOdd;
        ctx.SetSourceRGBA(0.78, 0.76, 0.72, 0.95);
        ctx.FillPreserve();
        ctx.LineWidth = px * 0.025;
        ctx.SetSourceRGBA(0.15, 0.13, 0.1, 0.9);
        ctx.Stroke();

        tex ??= new LoadedTexture(capi);
        capi.Gui.LoadOrUpdateCairoTexture(surface, true, ref tex);
    }

    void Stop()
    {
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Ortho);
        Dispose();
        if (current == this) current = null;
    }

    public void Dispose()
    {
        tex?.Dispose();
        tex = null;
    }
}
