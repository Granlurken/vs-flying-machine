using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Wright;

/// <summary>
/// Bukkene som starter en byggeplass. Høyreklikk på bakken setter ut <c>attributes.spawnsEntity</c> midt på en
/// kvadratisk plass (<c>attributes.siteSize</c>) som må være flat og solid, med <c>attributes.clearance</c>
/// blokker fri luft over. Planter og annet uten kollisjon teller som fri luft.
/// Plassen vender samme vei som spilleren ser (rundet til nærmeste 90°).
/// </summary>
public class ItemWrightTrestles : Item
{
    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
    {
        if (blockSel == null || !firstEvent || byEntity is not EntityPlayer ep || ep.Player == null)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
            return;
        }
        handling = EnumHandHandling.PreventDefault;

        var world = byEntity.World;
        if (!world.Claims.TryAccess(ep.Player, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak)) return;

        int size = Attributes?["siteSize"].AsInt(9) ?? 9;
        int clearance = Attributes?["clearance"].AsInt(5) ?? 5;
        string? error = CheckSite(world.BlockAccessor, blockSel.Position, size, clearance);
        if (error != null)
        {
            (api as ICoreClientAPI)?.TriggerIngameError(this, "wrightsite", Lang.Get(error, size, clearance));
            return;
        }

        if (world.Side != EnumAppSide.Server) return;

        string code = Attributes?["spawnsEntity"].AsString("wright:wrightconstruction") ?? "wright:wrightconstruction";
        var type = world.GetEntityType(new AssetLocation(code));
        if (type == null)
        {
            world.Logger.Error("[wright] Bukkene fant ikke entity-typen {0}", code);
            return;
        }

        var site = world.ClassRegistry.CreateEntity(type);
        site.Pos.SetPos(blockSel.Position.ToVec3d().Add(0.5, 1, 0.5));
        const float quarter = GameMath.PIHALF;
        site.Pos.Yaw = (float)Math.Round(byEntity.Pos.Yaw / quarter) * quarter;
        WrightMaterials.SetOwner(site, ep.Player);
        world.SpawnEntity(site);

        slot.TakeOut(1);
        slot.MarkDirty();
    }

    /// <summary>Null hvis plassen er god, ellers en lang-nøkkel.</summary>
    static string? CheckSite(IBlockAccessor ba, BlockPos ground, int size, int clearance)
    {
        int half = size / 2;
        var pos = new BlockPos(ground.dimension);
        for (int dx = -half; dx <= half; dx++)
        {
            for (int dz = -half; dz <= half; dz++)
            {
                pos.Set(ground.X + dx, ground.Y, ground.Z + dz);
                var block = ba.GetBlock(pos);
                if (!block.SideIsSolid(ba, pos, BlockFacing.UP.Index)) return "wright:site-notflat";

                for (int dy = 1; dy <= clearance; dy++)
                {
                    pos.Set(ground.X + dx, ground.Y + dy, ground.Z + dz);
                    var boxes = ba.GetBlock(pos).GetCollisionBoxes(ba, pos);
                    if (boxes != null && boxes.Length > 0) return "wright:site-notclear";
                }
            }
        }
        return null;
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
    {
        return new[]
        {
            new WorldInteraction { ActionLangCode = "wright:trestles-place", MouseButton = EnumMouseButton.Right }
        };
    }
}
