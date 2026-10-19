using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace FlyingMachine.Tests;

/// <summary>
/// Separate patches of ground for scenarios that share a world. Kept on a grid inside the loaded spawn chunks:
/// further out, entities are unloaded mid-scenario (and a plane drops whatever is in its fuel slot).
/// </summary>
public class Spots(int spacing = 16, int perRow = 9)
{
    private int next;

    /// <summary>The top solid block of the next patch.</summary>
    public BlockPos NextGround(IWorldAccessor world, BlockPos spawn)
    {
        int i = next++;
        int half = perRow / 2;
        var pos = spawn.AddCopy((i % perRow - half) * spacing, 0, (i / perRow - half) * spacing);
        pos.Y = world.BlockAccessor.GetRainMapHeightAt(pos);
        return pos;
    }
}
