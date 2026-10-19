using Atlas.Api;
using Atlas.XUnit;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace FlyingMachine.Tests;

/// <summary>The building site: where the trestles may go, who may tear things down, and getting the materials back.</summary>
[AtlasWorld]
public class SiteScenarios : AtlasScenarioBase
{
    private const string Site = "flyingmachine:flyingmachineconstruction";

    private IWorldAccessor W => World.Api.World;

    private static readonly Spots spots = new();
    private BlockPos FreeGround() => spots.NextGround(W, World.Spawn);

    private string? CheckSite(BlockPos ground) =>
        (string?)AccessTools.Method(typeof(ItemFlyingMachineTrestles), "CheckSite")
            .Invoke(null, [W.BlockAccessor, ground, 9, 5]);

    private static int playerCount;
    private async Task<ITestPlayer> Join() => await World.JoinPlayer("pilot" + ++playerCount);

    [AtlasScenario]
    public void Flat_open_ground_is_a_good_site()
    {
        var ground = FreeGround();
        Assert.True(W.BlockAccessor.GetBlock(ground).SideIsSolid(ground, BlockFacing.UP.Index), "spawn should stand on solid ground");
        Assert.Null(CheckSite(ground));
    }

    [AtlasScenario]
    public async Task A_block_in_the_way_spoils_the_site()
    {
        var ground = FreeGround();
        World.SetBlock("game:cobblestone-granite", ground.AddCopy(4, 5, -4)); // far corner, top of the clearance
        await World.Ticks(1);
        Assert.Equal("flyingmachine:site-notclear", CheckSite(ground));
    }

    [AtlasScenario]
    public async Task A_hole_in_the_ground_spoils_the_site()
    {
        var ground = FreeGround();
        World.SetBlock("game:air", ground.AddCopy(-4, 0, 4));
        await World.Ticks(1);
        Assert.Equal("flyingmachine:site-notflat", CheckSite(ground));
    }

    [AtlasScenario]
    public async Task The_owner_may_tear_down_and_a_non_player_may_not()
    {
        var owner = await Join();
        var site = World.SpawnEntity(Site, FreeGround().Up());
        Assert.Null(FlyingMachineMaterials.OwnerError(site, owner.Entity)); // nobody owns it yet

        FlyingMachineMaterials.SetOwner(site, owner.Player);
        Assert.Null(FlyingMachineMaterials.OwnerError(site, owner.Entity));

        var wolf = World.SpawnEntity("game:wolf-eurasian-adult-male", FreeGround().Up());
        Assert.Equal("flyingmachine:disassemble-notowner", FlyingMachineMaterials.OwnerError(site, (EntityAgent)wolf));
        wolf.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario]
    public async Task A_stranger_may_not_tear_down_someone_elses_site()
    {
        var owner = await Join();
        var stranger = await Join();
        World.ExecuteCommand($"/player {stranger.Player.PlayerName} role suplayer");
        await World.Ticks(1);
        Assert.False(stranger.Player.HasPrivilege(Vintagestory.API.Server.Privilege.controlserver), "stranger should not be an admin");

        var site = World.SpawnEntity(Site, FreeGround().Up());
        FlyingMachineMaterials.SetOwner(site, owner.Player);

        Assert.Equal("flyingmachine:disassemble-notowner", FlyingMachineMaterials.OwnerError(site, stranger.Entity));
    }

    [AtlasScenario]
    public async Task What_a_build_step_takes_from_the_hotbar_is_recorded_and_carried_over()
    {
        var player = await Join();
        var site = World.SpawnEntity(Site, FreeGround().Up());
        var hotbar = player.Player.InventoryManager.GetHotbarInventory();
        hotbar[0].Itemstack = new ItemStack(W.GetItem(new AssetLocation("flyingmachine:wingspar")), 4);
        hotbar[1].Itemstack = new ItemStack(W.GetItem(new AssetLocation("flyingmachine:propeller")), 1);

        var before = FlyingMachineMaterials.Snapshot(player.Entity);
        hotbar[0].TakeOut(3);
        hotbar[1].TakeOutWhole();
        FlyingMachineMaterials.RecordConsumed(site, before);

        var stored = FlyingMachineMaterials.Stored(site)!;
        Assert.Equal(2, stored.Count);
        Assert.Contains(stored, s => s.Collectible.Code.Path == "wingspar" && s.StackSize == 3);
        Assert.Contains(stored, s => s.Collectible.Code.Path == "propeller" && s.StackSize == 1);

        var plane = World.SpawnEntity("flyingmachine:flyingmachine", FreeGround().Up());
        FlyingMachineMaterials.CopyTo(site, plane);
        Assert.Equal(stored.Select(s => (s.Collectible.Code, s.StackSize)),
            FlyingMachineMaterials.Stored(plane)!.Select(s => (s.Collectible.Code, s.StackSize)));
    }

    [AtlasScenario]
    public void Every_build_stage_material_resolves()
    {
        var materials = FlyingMachineMaterials.FromStages(W, Site);
        Assert.NotEmpty(materials);

        var stages = W.GetEntityType(new AssetLocation(Site))!.Attributes["stages"].AsArray();
        int required = stages.Sum(s => s["requireStacks"].AsArray()?.Length ?? 0);
        Assert.Equal(required, materials.Count);
    }

    [AtlasScenario]
    public void Only_wrenches_take_things_apart()
    {
        var attrs = W.GetEntityType(new AssetLocation(Site))!.Attributes;
        Assert.True(FlyingMachineMaterials.IsDisassemblyTool(new ItemStack(W.GetItem(new AssetLocation("game:wrench-iron"))), attrs));
        Assert.False(FlyingMachineMaterials.IsDisassemblyTool(new ItemStack(W.GetItem(new AssetLocation("game:hammer-iron"))), attrs));
        Assert.False(FlyingMachineMaterials.IsDisassemblyTool(null, attrs));
        Assert.Equal(3, FlyingMachineMaterials.ToolStacks(W, attrs).Length);
    }
}
