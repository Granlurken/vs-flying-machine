using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace FlyingMachine.Tests;

/// <summary>The mod loads cleanly: everything it ships is registered, resolves, and has English text.</summary>
[AtlasWorld]
public class ContentScenarios : AtlasScenarioBase
{
    private ICoreAPI Api => World.Api;

    [AtlasScenario]
    public void Boot_logs_no_warnings_from_the_mod()
    {
        var ours = World.BootDiagnostics
            .Where(d => d.Source == "flyingmachine" || d.Message.Contains("flyingmachine"))
            .Select(d => d.DescribeSource() + ": " + d.Message);
        Assert.Empty(ours);
    }

    [AtlasScenario]
    public void Every_item_file_is_registered()
    {
        var shipped = Api.Assets.GetMany("itemtypes/", "flyingmachine", loadAsset: false)
            .Select(a => a.Location.GetName()[..^".json".Length])
            .Order();
        var registered = Api.World.Items
            .Where(i => i.Code?.Domain == "flyingmachine")
            .Select(i => i.FirstCodePart())
            .Distinct()
            .Order();
        Assert.Equal(shipped, registered);
    }

    [AtlasScenario]
    public void Entities_use_the_mod_classes()
    {
        Assert.Equal("EntityFlyingMachine", Api.World.GetEntityType(new AssetLocation("flyingmachine:flyingmachine"))?.Class);
        Assert.Equal("EntityFlyingMachineConstruction",
            Api.World.GetEntityType(new AssetLocation("flyingmachine:flyingmachineconstruction"))?.Class);
        Assert.IsType<ItemFlyingMachineTrestles>(Api.World.GetItem(new AssetLocation("flyingmachine:trestles")));
    }

    [AtlasScenario]
    public void Every_part_except_the_plane_itself_can_be_made()
    {
        var grid = Api.World.GridRecipes
            .Select(r => r.Output.ResolvedItemStack?.Collectible?.Code?.ToString());
        var smithing = Api.GetSmithingRecipes()
            .Select(r => r.Output.ResolvedItemStack?.Collectible?.Code?.ToString());
        var made = grid.Concat(smithing).Where(c => c != null).ToHashSet();

        // The plane is rolled out of the building site, never crafted
        var parts = Api.World.Items
            .Where(i => i.Code?.Domain == "flyingmachine" && i.Code.Path != "flyingmachine")
            .Select(i => i.Code.ToString());
        Assert.All(parts, code => Assert.Contains(code, made));
    }

    [AtlasScenario]
    public void Every_recipe_ingredient_resolves()
    {
        var ours = Api.World.GridRecipes
            .Where(r => r.Output.ResolvedItemStack?.Collectible?.Code?.Domain == "flyingmachine").ToList();
        Assert.NotEmpty(ours);
        Assert.All(ours, r => Assert.All(r.ResolvedIngredients.Where(i => i != null),
            i => Assert.True(i.Code.Path.Contains('*') || i.ResolvedItemStack != null, $"{r.Name}: {i.Code} did not resolve")));
    }

    [AtlasScenario]
    public void Every_item_and_entity_has_an_english_name()
    {
        var items = Api.World.Items.Where(i => i.Code?.Domain == "flyingmachine")
            .Select(i => "flyingmachine:item-" + i.Code.Path);
        var entities = new[] { "flyingmachine:item-creature-flyingmachine", "flyingmachine:item-creature-flyingmachineconstruction" };
        Assert.All(items.Concat(entities), key => Assert.True(Lang.HasTranslation(key), $"missing lang key {key}"));
    }
}
