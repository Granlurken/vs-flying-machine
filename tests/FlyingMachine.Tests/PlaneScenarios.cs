using Atlas.XUnit;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FlyingMachine.Tests;

/// <summary>The plane entity on the server: fuel, refuelling, and sitting still when nobody flies it.</summary>
[AtlasWorld]
public class PlaneScenarios : AtlasScenarioBase
{
    private const string Plane = "flyingmachine:flyingmachine";

    private IWorldAccessor W => World.Api.World;

    // Each scenario gets its own patch of the superflat world, well apart
    private static readonly Spots spots = new();

    // The server only simulates entities near a player
    private static bool watching;
    private async Task Watch()
    {
        if (watching) return;
        await World.JoinPlayer("watcher");
        watching = true;
    }
    private BlockPos FreeSpot() => spots.NextGround(W, World.Spawn).Up();

    private async Task<EntityFlyingMachine> SpawnBuiltOnSite(BlockPos pos)
    {
        await Watch();
        var plane = W.ClassRegistry.CreateEntity(W.GetEntityType(new AssetLocation(Plane)));
        plane.Pos.SetPos(pos.ToVec3d().Add(0.5, 0, 0.5));
        plane.WatchedAttributes.SetBool(EntityFlyingMachine.BuiltOnSiteKey, true);
        W.SpawnEntity(plane);
        await World.Ticks(2);
        return (EntityFlyingMachine)plane;
    }

    private ItemStack BucketOf(string liquidCode, int litres)
    {
        var bucket = (BlockLiquidContainerBase)W.GetBlock(new AssetLocation("game:woodbucket"));
        var stack = new ItemStack(bucket);
        var liquid = new ItemStack(W.GetItem(new AssetLocation(liquidCode)));
        var props = BlockLiquidContainerBase.GetContainableProps(liquid)!;
        liquid.StackSize = (int)(litres * props.ItemsPerLitre);
        bucket.SetContent(stack, liquid);
        return stack;
    }

    [AtlasScenario]
    public async Task A_spawned_plane_starts_with_a_full_tank()
    {
        await Watch();
        var plane = (EntityFlyingMachine)World.SpawnEntity(Plane, FreeSpot());
        await World.Ticks(2);

        Assert.Equal(10, plane.FuelCapacity);
        Assert.Equal(plane.FuelCapacity, plane.FuelLitres);
    }

    [AtlasScenario]
    public async Task A_plane_rolled_out_of_the_building_site_starts_empty()
    {
        var plane = await SpawnBuiltOnSite(FreeSpot());
        Assert.Equal(0, plane.FuelLitres);
    }

    [AtlasScenario]
    public async Task Fuel_is_clamped_to_the_tank()
    {
        var plane = await SpawnBuiltOnSite(FreeSpot());
        plane.FuelLitres = 99;
        Assert.Equal(plane.FuelCapacity, plane.FuelLitres);
        plane.FuelLitres = -5;
        Assert.Equal(0, plane.FuelLitres);
    }

    [AtlasScenario]
    public async Task Aqua_vitae_in_the_slot_pours_into_the_tank()
    {
        var plane = await SpawnBuiltOnSite(FreeSpot());
        var slot = plane.FuelInventory[0];
        slot.Itemstack = BucketOf("game:alcoholportion", 5);
        slot.MarkDirty();
        Assert.Equal("fuel-pouring", plane.FuelSlotState());

        // Pours 1 L/s; give it a few seconds of game time
        await World.Until(() => plane.FuelLitres >= 2, 600);
        Assert.InRange(plane.FuelLitres, 2, 5);
    }

    [AtlasScenario]
    public async Task A_full_tank_stops_pouring()
    {
        await Watch();
        var plane = (EntityFlyingMachine)World.SpawnEntity(Plane, FreeSpot());
        await World.Ticks(2);
        var slot = plane.FuelInventory[0];
        slot.Itemstack = BucketOf("game:alcoholportion", 5);
        Assert.Equal("fuel-full", plane.FuelSlotState());

        await World.Ticks(60);
        var bucket = (BlockLiquidContainerBase)slot.Itemstack.Collectible;
        Assert.Equal(5, bucket.GetCurrentLitres(slot.Itemstack), 3);
    }

    [AtlasScenario]
    public async Task Water_is_not_fuel()
    {
        var plane = await SpawnBuiltOnSite(FreeSpot());
        var slot = plane.FuelInventory[0];
        slot.Itemstack = BucketOf("game:waterportion", 5);
        Assert.Equal("fuel-wrong", plane.FuelSlotState());

        await World.Ticks(60);
        Assert.Equal(0, plane.FuelLitres);
    }

    [AtlasScenario]
    public async Task Fuel_slot_state_without_a_full_container()
    {
        var plane = await SpawnBuiltOnSite(FreeSpot());
        var slot = plane.FuelInventory[0];
        Assert.Equal("fuel-insert", plane.FuelSlotState());

        slot.Itemstack = new ItemStack(W.GetBlock(new AssetLocation("game:woodbucket")));
        Assert.Equal("fuel-empty", plane.FuelSlotState());
    }

    [AtlasScenario]
    public async Task An_unmanned_plane_stays_parked()
    {
        await Watch();
        var pos = FreeSpot();
        var plane = (EntityFlyingMachine)World.SpawnEntity(Plane, pos);
        await World.Ticks(20);
        var start = plane.Pos.XYZ;

        await World.Ticks(200);

        Assert.True(plane.Alive);
        Assert.False(plane.Flying);
        Assert.Equal(0, plane.Throttle);
        Assert.True(plane.Pos.XYZ.DistanceTo(start) < 0.05, $"drifted from {start} to {plane.Pos.XYZ}");
        Assert.True(plane.OnGround, "should be resting on the ground");
    }

    [AtlasScenario]
    public async Task Fuel_survives_a_save_and_load()
    {
        var plane = await SpawnBuiltOnSite(FreeSpot());
        plane.FuelLitres = 3.25;

        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) plane.ToBytes(writer, false);
        ms.Position = 0;
        var copy = (EntityFlyingMachine)W.ClassRegistry.CreateEntity(plane.Properties);
        using (var reader = new BinaryReader(ms)) copy.FromBytes(reader, false);

        Assert.Equal(3.25, copy.WatchedAttributes.GetDouble("fuelLitres"), 6);
        Assert.True(copy.WatchedAttributes.GetBool(EntityFlyingMachine.BuiltOnSiteKey));
    }

    [AtlasScenario]
    public void Lift_follows_the_square_of_speed()
    {
        var plane = (EntityFlyingMachine)World.SpawnEntity(Plane, FreeSpot());
        var stall = (double)AccessTools.Field(typeof(EntityFlyingMachine), "stallSpeed").GetValue(plane)!;

        plane.ForwardSpeed = stall;
        Assert.Equal(1, plane.LiftRatio, 6);
        plane.ForwardSpeed = stall / 2;
        Assert.Equal(0.25, plane.LiftRatio, 6);
        plane.ForwardSpeed = 0;
        Assert.Equal(0, plane.LiftRatio, 6);
        plane.Die(EnumDespawnReason.Removed);
    }

    // Holding W (throttle up) must not knock the pilot out of the seated pose: without it the
    // first-person arms and shoulders swing up into the view
    [AtlasScenario]
    public async Task The_pilot_stays_seated_while_holding_the_throttle()
    {
        var pilot = await World.JoinPlayer("armspilot");
        var pos = FreeSpot();
        pilot.TeleportTo(pos);
        var plane = (EntityFlyingMachine)World.SpawnEntity(Plane, pos);
        await World.Ticks(5);

        var seat = plane.GetBehavior<EntityBehaviorSeatable>().Seats.Single(s => s.Config.Controllable);
        Assert.True(pilot.Entity.TryMount(seat), "pilot could not sit down");
        await World.Ticks(5);

        var anims = pilot.Entity.AnimManager.ActiveAnimationsByAnimCode;
        Assert.True(anims.ContainsKey("sitboatidle"), "not seated after mounting");

        seat.Controls.Forward = true;
        for (int i = 0; i < 40; i++)
        {
            await World.Ticks(1);
            Assert.True(anims.ContainsKey("sitboatidle"),
                $"lost the seated pose after {i + 1} ticks of throttle; active: {string.Join(", ", anims.Keys)}");
        }
        Assert.True(plane.Throttle > 0, "the throttle never opened");

        seat.Controls.Forward = false;
        pilot.Entity.TryUnmount();
        plane.Die(EnumDespawnReason.Removed);
    }
}
