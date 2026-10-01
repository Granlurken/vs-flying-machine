using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace FlyingMachine;

public class FlyingMachineModSystem : ModSystem
{
    static ICoreClientAPI? clientApi;
    Harmony? harmony;

    public override void Start(ICoreAPI api)
    {
        api.RegisterEntity("EntityFlyingMachine", typeof(EntityFlyingMachine));
        api.RegisterEntity("EntityFlyingMachineConstruction", typeof(EntityFlyingMachineConstruction));
        api.RegisterItemClass("ItemFlyingMachineTrestles", typeof(ItemFlyingMachineTrestles));
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        clientApi = api;

        // Vanilla spiller flåte-lyder (skvulp + fart gjennom vann) for ALLE båtseter og starter dem på nytt
        // hvert 0,2 s — å stoppe dem utenfra hver frame taper. Patch: sitter vi i et FlyingMachine-fly, oppfør deg som «ikke montert»
        harmony = new Harmony(Mod.Info.ModID);
        harmony.Patch(
            AccessTools.Method(typeof(ModSystemBoatingSoundAndRatlineStamina), nameof(ModSystemBoatingSoundAndRatlineStamina.NowInMotion)),
            prefix: new HarmonyMethod(typeof(FlyingMachineModSystem), nameof(NowInMotionPrefix)));

        // Høyreklikk på flyet med væskebeholder: fang klikket FØR gjenstanden i hånda får det
        // (ellers drikker/heller bøtta, og EntityFlyingMachine.OnInteract blir aldri kalt)
        api.Input.InWorldAction += (EnumEntityAction action, bool on, ref EnumHandling handled) =>
        {
            if (action != EnumEntityAction.InWorldRightMouseDown || !on) return;
            var player = api.World.Player;
            if (player.CurrentEntitySelection?.Entity is not EntityFlyingMachine plane) return;
            if (!plane.IsFuelBox(player.CurrentEntitySelection.SelectionBoxIndex)) return;
            if (player.Entity.MountedOn == plane || player.Entity.Controls.ShiftKey) return;
            if (player.InventoryManager.ActiveHotbarSlot?.Itemstack?.Collectible is not ILiquidSource) return;

            plane.OpenFuelDialog(api);
            handled = EnumHandling.PreventDefault;
        };
    }

    static bool NowInMotionPrefix(ModSystemBoatingSoundAndRatlineStamina __instance)
    {
        if (clientApi?.World.Player?.Entity.MountedOn is not EntityBoatSeat seat || seat.Entity is not EntityFlyingMachine) return true;
        __instance.NotMounted();
        return false;
    }

    public override void Dispose()
    {
        harmony?.UnpatchAll(Mod.Info.ModID);
        clientApi = null;
    }
}
