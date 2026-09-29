using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace Wright;

/// <summary>
/// Byggeplassen for flyet. Utdrag av vanilla <c>EntityBoatConstruction</c>: <c>RightClickConstruction</c> gjør selve
/// stegene (krever stacks i hotbaren, viser nye shape-grupper). Forskjellen er slutten: siste steg ruller ut flyet
/// (<c>attributes.spawnsEntity</c>) og byggeplassen forsvinner — den blir ikke stående igjen som båtens rullestokker.
///
/// Steg 0 (bare bukker) kan tas ned igjen med Shift + tom hånd, og gir tilbake <c>attributes.returnsItem</c>.
/// </summary>
public class EntityWrightConstruction : Entity
{
    readonly RightClickConstruction rcc = new();

    int CurrentStage
    {
        get => WatchedAttributes.GetInt("currentStage");
        set => WatchedAttributes.SetInt("currentStage", value);
    }

    bool OnLastStage => rcc.CurrentCompletedStage >= rcc.Stages.Length - 1;

    public override double FrustumSphereRadius => base.FrustumSphereRadius * 4;

    public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
    {
        WatchedAttributes.RegisterModifiedListener("currentStage", OnStageChanged);
        WatchedAttributes.RegisterModifiedListener("wildcards", () => rcc.FromTreeAttributes(WatchedAttributes));
        base.Initialize(properties, api, InChunkIndex3d);

        var stages = properties.Attributes["stages"].AsArray<ConstructionStage>(null, "game");
        rcc.LateInit(stages, api, () => Pos.XYZ, "entity " + Code);
        CurrentStage = rcc.CurrentCompletedStage;
    }

    void OnStageChanged()
    {
        if (World.Side != EnumAppSide.Client) return;
        rcc.CurrentCompletedStage = CurrentStage;
        MarkShapeModified();
    }

    public override void OnTesselation(ref Shape entityShape, string shapePathForLogging)
    {
        if (Properties.Client.Renderer is EntityShapeRenderer esr)
        {
            esr.OverrideSelectiveElements = rcc.getShapeElements();
        }
        // Kroppen får tresorten fra første byggesteg
        if (Api is ICoreClientAPI && rcc.StoredWildCards.TryGetValue("wood", out var wood))
        {
            SetTexture("debarked", new AssetLocation($"game:block/wood/debarked/{wood}"));
            SetTexture("planks", new AssetLocation($"game:block/wood/planks/{wood}1"));
        }
        base.OnTesselation(ref entityShape, shapePathForLogging);
    }

    void SetTexture(string code, AssetLocation loc)
    {
        var capi = (ICoreClientAPI)Api;
        var tex = new CompositeTexture(loc);
        Properties.Client.Textures[code] = tex;
        capi.EntityTextureAtlas.GetOrInsertTexture(tex, out int subId, out _);
        tex.Baked.TextureSubId = subId;
    }

    public override void OnInteract(EntityAgent byEntity, ItemSlot handslot, Vec3d hitPosition, EnumInteractMode mode)
    {
        base.OnInteract(byEntity, handslot, hitPosition, mode);
        if (mode != EnumInteractMode.Interact || OnLastStage) return;

        // Bare bukker: Shift + tom hånd tar dem ned igjen
        if (rcc.CurrentCompletedStage == 0 && handslot.Empty && byEntity.Controls.ShiftKey)
        {
            if (World.Side == EnumAppSide.Server)
            {
                string code = Properties.Attributes["returnsItem"].AsString("wright:trestles");
                var item = World.GetItem(new AssetLocation(code));
                if (item != null && !byEntity.TryGiveItemStack(new ItemStack(item)))
                {
                    World.SpawnItemEntity(new ItemStack(item), Pos.XYZ);
                }
                Die(EnumDespawnReason.Removed);
            }
            return;
        }

        if (!rcc.OnInteract(byEntity, handslot)) return;

        CurrentStage = rcc.CurrentCompletedStage;
        rcc.ToTreeAttributes(WatchedAttributes);
        WatchedAttributes.MarkPathDirty("wildcards");
        WatchedAttributes.MarkPathDirty("currentStage");

        if (OnLastStage && World.Side == EnumAppSide.Server)
        {
            RollOut(byEntity);
        }
    }

    void RollOut(EntityAgent byEntity)
    {
        string code = Properties.Attributes["spawnsEntity"].AsString("wright:wright");
        var type = World.GetEntityType(new AssetLocation(code));
        if (type == null)
        {
            World.Logger.Error("[wright] Byggeplassen fant ikke entity-typen {0}", code);
            return;
        }

        var plane = World.ClassRegistry.CreateEntity(type);
        plane.Pos.SetFrom(Pos);
        plane.Pos.Yaw = Pos.Yaw + Properties.Attributes["spawnYawOffset"].AsFloat(0);
        plane.Pos.Y += 0.1;
        if (byEntity is EntityPlayer ep && ep.Player != null)
        {
            plane.WatchedAttributes.SetString("createdByPlayername", ep.Player.PlayerName);
            plane.WatchedAttributes.SetString("createdByPlayerUID", ep.Player.PlayerUID);
        }
        World.SpawnEntity(plane);
        Die(EnumDespawnReason.Removed);
    }

    public override WorldInteraction[] GetInteractionHelp(IClientWorldAccessor world, EntitySelection es, IClientPlayer player)
    {
        var wis = base.GetInteractionHelp(world, es, player);
        var next = rcc.GetInteractionHelp(world, player);
        if (next == null) return wis;
        wis = wis.Append(next);
        if (rcc.CurrentCompletedStage == 0)
        {
            wis = wis.Append(new WorldInteraction
            {
                HotKeyCode = "shift",
                RequireFreeHand = true,
                MouseButton = EnumMouseButton.Right,
                ActionLangCode = "wright:trestles-takedown"
            });
        }
        return wis;
    }

    public override void ToBytes(System.IO.BinaryWriter writer, bool forClient)
    {
        rcc.ToTreeAttributes(WatchedAttributes);
        base.ToBytes(writer, forClient);
    }

    public override void FromBytes(System.IO.BinaryReader reader, bool isSync)
    {
        base.FromBytes(reader, isSync);
        rcc.FromTreeAttributes(WatchedAttributes);
    }

    public override string GetInfoText()
    {
        string text = base.GetInfoText() + "\r\n";
        if (rcc.StoredWildCards.TryGetValue("wood", out var wood))
        {
            text += Lang.Get("Material: {0}", Lang.Get("material-" + wood)) + "\r\n";
        }
        // Siste steg er selve utrullingen, så det telles ikke som et byggesteg
        return text + Lang.Get("build-stage-current-and-total", rcc.CurrentCompletedStage + 1, rcc.Stages.Length - 1);
    }
}
