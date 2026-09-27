using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Wright;

public class WrightModSystem : ModSystem
{
    WrightHud? hud;

    public override void Start(ICoreAPI api)
    {
        api.RegisterEntity("EntityWright", typeof(EntityWright));
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        hud = new WrightHud(api);
        api.Event.RegisterGameTickListener(_ => hud.Update(), 100);
    }
}
