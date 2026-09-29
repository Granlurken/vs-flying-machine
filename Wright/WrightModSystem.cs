using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Wright;

public class WrightModSystem : ModSystem
{
    WrightHud? hud;

    public override void Start(ICoreAPI api)
    {
        api.RegisterEntity("EntityWright", typeof(EntityWright));
        api.RegisterEntity("EntityWrightConstruction", typeof(EntityWrightConstruction));
        api.RegisterItemClass("ItemWrightTrestles", typeof(ItemWrightTrestles));
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        hud = new WrightHud(api);
        api.Event.RegisterGameTickListener(_ => hud.Update(), 100);

        // Høyreklikk på flyet med væskebeholder: fang klikket FØR gjenstanden i hånda får det
        // (ellers drikker/heller bøtta, og EntityWright.OnInteract blir aldri kalt)
        api.Input.InWorldAction += (EnumEntityAction action, bool on, ref EnumHandling handled) =>
        {
            if (action != EnumEntityAction.InWorldRightMouseDown || !on) return;
            var player = api.World.Player;
            if (player.CurrentEntitySelection?.Entity is not EntityWright plane) return;
            if (player.Entity.MountedOn == plane || player.Entity.Controls.ShiftKey) return;
            if (player.InventoryManager.ActiveHotbarSlot?.Itemstack?.Collectible is not ILiquidSource) return;

            plane.OpenFuelDialog(api);
            handled = EnumHandling.PreventDefault;
        };
    }
}
