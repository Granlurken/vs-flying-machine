using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FlyingMachine.Tests;

/// <summary>
/// Flying above the top of the map (y ≥ MapSizeY, 256 by default), where there are no chunks.
/// Its own class because it unloads a chunk column, which would pull the ground from under other scenarios.
/// </summary>
[AtlasWorld]
public class AltitudeScenarios : AtlasScenarioBase
{
    private const string Plane = "flyingmachine:flyingmachine";

    private IWorldAccessor W => World.Api.World;
    private ICoreServerAPI Sapi => (ICoreServerAPI)World.Api;

    /// <summary>Keeps an unmanned plane at a spot for a while of game time (it would fall otherwise).</summary>
    private async Task HoldAt(Entity plane, Vec3d pos, int ms)
    {
        long start = W.ElapsedMilliseconds;
        for (int i = 0; i < 600 && W.ElapsedMilliseconds - start < ms; i++)
        {
            plane.Pos.SetPos(pos);
            plane.Pos.Motion.Set(0, 0, 0);
            await World.Ticks(1);
        }
    }

    /// <summary>
    /// The client only draws an entity whose own chunk is drawn, unless it opts out (SystemRenderEntities.OnBeforeRender),
    /// and above the map there is no chunk. Atlas has no client to render with, so this pins the opt-out.
    /// </summary>
    [AtlasScenario]
    public void A_plane_is_drawn_outside_loaded_chunks()
    {
        var plane = World.SpawnEntity(Plane, World.Spawn.AddCopy(0, W.BlockAccessor.MapSizeY - World.Spawn.Y + 20, 0));
        Assert.True(plane.AllowOutsideLoadedRange);
        plane.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario]
    public async Task A_plane_above_the_map_survives_its_takeoff_column_unloading()
    {
        await World.JoinPlayer("watcher"); // the server only simulates entities near a player
        int sky = W.BlockAccessor.MapSizeY + 20;
        // Two chunk columns apart, neither of them the spawn column
        var takeoff = World.Spawn.AddCopy(-40, 0, 0);
        var away = World.Spawn.AddCopy(40, 0, 0);
        takeoff.Y = W.BlockAccessor.GetRainMapHeightAt(takeoff) + 1;

        var plane = (EntityFlyingMachine)World.SpawnEntity(Plane, takeoff);
        await World.Ticks(2);

        // Climb out of the map over the takeoff column, then fly on to another column
        await HoldAt(plane, new Vec3d(takeoff.X + 0.5, sky, takeoff.Z + 0.5), 1500);
        await HoldAt(plane, new Vec3d(away.X + 0.5, sky, away.Z + 0.5), 1500);

        // The pilot is long gone from the takeoff column, so the server unloads it
        Sapi.WorldManager.UnloadChunkColumn(takeoff.X / GlobalConstants.ChunkSize, takeoff.Z / GlobalConstants.ChunkSize);
        await World.Ticks(2);

        Assert.NotEqual(EnumEntityState.Despawned, plane.State);
        Assert.Same(plane, W.GetEntityById(plane.EntityId));
    }
}
