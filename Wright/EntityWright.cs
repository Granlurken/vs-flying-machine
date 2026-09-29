using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace Wright;

/// <summary>
/// Flyet. Arver seter, sving og kollisjon fra vanilla-båten, men har egen flymodell.
///
/// Enheter: fart er i blokker per 1/60 sekund (samme som Entity.Pos.Motion), så 0.25 = 15 blokker/s.
/// Vinkler i radianer.
///
/// Kontroller:
///   W/S    = gass opp/ned (blir liggende). S på tomgang på bakken = brems.
///   Space  = nesa opp, Ctrl = nesa ned. Nesevinkelen blir liggende når du slipper (trim).
///   A/D    = krengning i lufta (svingen følger krengningen), halesporstyring på bakken.
///
/// Flymodell: flyet går dit nesa peker. Tyngdekraften virker langs banen, så stigning spiser fart og
/// dykk gir fart. Under stallfart bærer ikke vingene: flyet synker og nesa faller av seg selv.
/// </summary>
public class EntityWright : EntityBoat
{
    // Tuning — leses fra entity-JSON (attributes.flight), dette er fallbackene
    double maxSpeed = 0.28;       // toppfart ved full gass
    double stallSpeed = 0.15;     // under dette bærer ikke vingene flyet
    double accel = 0.15;          // hvor fort farten nærmer seg målfarten (per sekund) — lavt = seigt
    double throttleRate = 0.4;    // gass-endring per sekund med W/S holdt inne
    double brakeRate = 0.6;       // ekstra retardasjon på bakken med S på tomgang
    double gravityAlongPath = 0.12; // fart tapt/vunnet per sekund per sin(banevinkel)
    double maxSinkRate = 0.2;     // fritt fall uten løft
    double verticalResponse = 3;  // hvor fort vertikalfarten følger banen

    double pitchRate = 0.7;       // rad/s med Space/Ctrl
    double maxPitchUp = 0.6;      // ~35°
    double maxPitchDown = 0.8;    // ~45°
    double rotateLift = 0.8;      // løftandel der nesa kan løftes på bakken (rotasjon før avgang)
    double stallNoseDrop = 1.2;   // rad/s nesa faller når vingene ikke bærer

    double maxBank = 0.7;         // ~40°
    double bankRate = 1.5;        // rad/s inn i krengning
    double bankReturn = 2.0;      // rad/s tilbake til vannrett når A/D slippes
    double airTurnRate = 1.2;     // rad/s sving ved full krengning
    double groundTurnRate = 0.8;  // rad/s halesporstyring
    double turnResponse = 3;      // hvor fort svingraten følger ønsket verdi

    // Visuell tilt — fortegn/akse kan snus i JSON hvis modellen tipper feil vei
    float visualPitchSign = -1;
    float visualRollSign = 1;

    // Drivstoff — Aqua Vitae (vanilla alcoholportion). Mengden bor i WatchedAttributes (server → klient, lagres med flyet)
    string fuelCode = "game:alcoholportion";
    double fuelCapacity = 10;     // liter
    double fuelBurnRate = 1.0 / 60; // liter per sekund ved full gass (1 L/min)

    public double FuelCapacity => fuelCapacity;
    public double FuelLitres
    {
        get => WatchedAttributes.GetDouble("fuelLitres", 0);
        set => WatchedAttributes.SetDouble("fuelLitres", GameMath.Clamp(value, 0, fuelCapacity));
    }

    public double Throttle;
    public bool Flying { get; private set; }
    /// <summary>Nesevinkel, positiv = nesa opp.</summary>
    public double PitchAngle { get; private set; }
    /// <summary>Krengning, positiv = venstre vinge ned.</summary>
    public double BankAngle { get; private set; }
    public bool Stalling { get; private set; }

    public override bool ApplyGravity => !Flying;

    public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
    {
        base.Initialize(properties, api, InChunkIndex3d);
        InitFuelInventory(api);

        var fuel = properties.Attributes["fuel"];
        if (fuel.Exists)
        {
            fuelCode = fuel["code"].AsString(fuelCode);
            fuelCapacity = fuel["capacityLitres"].AsDouble(fuelCapacity);
            fuelBurnRate = fuel["litresPerMinuteFullThrottle"].AsDouble(fuelBurnRate * 60) / 60;
        }

        var f = properties.Attributes["flight"];
        if (!f.Exists) return;
        maxSpeed = f["maxSpeed"].AsDouble(maxSpeed);
        stallSpeed = f["stallSpeed"].AsDouble(stallSpeed);
        accel = f["accel"].AsDouble(accel);
        throttleRate = f["throttleRate"].AsDouble(throttleRate);
        brakeRate = f["brakeRate"].AsDouble(brakeRate);
        gravityAlongPath = f["gravityAlongPath"].AsDouble(gravityAlongPath);
        maxSinkRate = f["maxSinkRate"].AsDouble(maxSinkRate);
        verticalResponse = f["verticalResponse"].AsDouble(verticalResponse);
        pitchRate = f["pitchRate"].AsDouble(pitchRate);
        maxPitchUp = f["maxPitchUp"].AsDouble(maxPitchUp);
        maxPitchDown = f["maxPitchDown"].AsDouble(maxPitchDown);
        rotateLift = f["rotateLift"].AsDouble(rotateLift);
        stallNoseDrop = f["stallNoseDrop"].AsDouble(stallNoseDrop);
        maxBank = f["maxBank"].AsDouble(maxBank);
        bankRate = f["bankRate"].AsDouble(bankRate);
        bankReturn = f["bankReturn"].AsDouble(bankReturn);
        airTurnRate = f["airTurnRate"].AsDouble(airTurnRate);
        groundTurnRate = f["groundTurnRate"].AsDouble(groundTurnRate);
        turnResponse = f["turnResponse"].AsDouble(turnResponse);
        visualPitchSign = f["visualPitchSign"].AsFloat(visualPitchSign);
        visualRollSign = f["visualRollSign"].AsFloat(visualRollSign);
    }

    // --- Fylling: høyreklikk med væskebeholder åpner fylle-vinduet (slot → pil → tank) ---
    const int PacketOpenFuel = 1001;
    const double PourLitresPerSecond = 1;
    InventoryGeneric? fuelInv;
    GuiDialogWrightFuel? fuelDialog;
    double pourCarry; // brøkdel av væske-item som ikke er helt ennå

    public InventoryGeneric FuelInventory => fuelInv!;

    void InitFuelInventory(ICoreAPI api)
    {
        fuelInv = new InventoryGeneric(1, "wrightfuel-" + EntityId, api, (_, inv) => new ItemSlotWrightFuel(inv));
        if (api.Side == EnumAppSide.Server)
        {
            // Lukker du vinduet, får du beholderen tilbake (sloten lagres ikke med flyet)
            fuelInv.OnInventoryClosed += player => ReturnFuelSlot(player);
        }
    }

    void ReturnFuelSlot(IPlayer? player)
    {
        var slot = fuelInv![0];
        if (slot.Empty) return;
        var stack = slot.TakeOutWhole();
        if (player == null || !player.InventoryManager.TryGiveItemstack(stack, true))
        {
            World.SpawnItemEntity(stack, (player?.Entity?.Pos ?? Pos).XYZ);
        }
        slot.MarkDirty();
    }

    /// <summary>Tilstanden til fylle-sloten, som lang-nøkkel (uten «wright:»).</summary>
    public string FuelSlotState()
    {
        var stack = fuelInv?[0].Itemstack;
        if (stack?.Collectible is not ILiquidSource source) return "fuel-insert";
        var content = source.GetContent(stack);
        if (content == null || source.GetContentProps(stack) == null) return "fuel-empty";
        if (!content.Collectible.Code.Equals(new AssetLocation(fuelCode))) return "fuel-wrong";
        if (FuelLitres >= fuelCapacity - 0.01) return "fuel-full";
        return "fuel-pouring";
    }

    void PourTick(float dt)
    {
        if (FuelSlotState() != "fuel-pouring") { pourCarry = 0; return; }
        var slot = fuelInv![0];
        var source = (ILiquidSource)slot.Itemstack!.Collectible;
        var props = source.GetContentProps(slot.Itemstack)!;

        pourCarry += PourLitresPerSecond * dt * props.ItemsPerLitre;
        double room = (fuelCapacity - FuelLitres) * props.ItemsPerLitre;
        int items = (int)Math.Min(Math.Floor(pourCarry), Math.Ceiling(room));
        if (items <= 0) return;
        pourCarry -= items;

        var taken = source.TryTakeContent(slot.Itemstack, items);
        if (taken == null || taken.StackSize <= 0) return;
        FuelLitres += taken.StackSize / (double)props.ItemsPerLitre;
        slot.MarkDirty();
    }

    public override void OnGameTick(float dt)
    {
        base.OnGameTick(dt);
        if (Api.Side == EnumAppSide.Server && fuelInv != null) PourTick(dt);
    }

    public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode)
    {
        if (mode == EnumInteractMode.Interact && !byEntity.Controls.ShiftKey
            && itemslot?.Itemstack?.Collectible is ILiquidSource
            && byEntity is EntityPlayer ep && IsFuelBox(ep.EntitySelection?.SelectionBoxIndex ?? -1))
        {
            if (Api is ICoreClientAPI capi && byEntity == capi.World.Player.Entity) OpenFuelDialog(capi);
            return; // ikke sett deg inn
        }
        base.OnInteract(byEntity, itemslot, hitPosition, mode);
    }

    /// <summary>Sant når klikkboksen er tanklokket (FuelAP). Indeks 0 = hovedhitboksen, AP-boksene starter på 1.</summary>
    public bool IsFuelBox(int selectionBoxIndex)
    {
        var boxes = GetBehavior<EntityBehaviorSelectionBoxes>()?.selectionBoxes;
        int i = selectionBoxIndex - 1;
        return boxes != null && i >= 0 && i < boxes.Length && boxes[i].AttachPoint.Code == "FuelAP";
    }

    public void OpenFuelDialog(ICoreClientAPI capi)
    {
        if (fuelDialog?.IsOpened() == true) return;
        capi.Network.SendEntityPacket(EntityId, PacketOpenFuel, null);
        fuelDialog = new GuiDialogWrightFuel(capi, this);
        fuelDialog.OnClosed += () => fuelDialog = null;
        fuelDialog.TryOpen();
    }

    public override void OnReceivedClientPacket(IServerPlayer player, int packetid, byte[] data)
    {
        base.OnReceivedClientPacket(player, packetid, data);
        if (fuelInv == null) return;
        if (packetid < 1000)
        {
            fuelInv.InvNetworkUtil.HandleClientPacket(player, packetid, data);
            return;
        }
        if (packetid == PacketOpenFuel)
        {
            player.InventoryManager.OpenInventory(fuelInv);
            // Beholderen i hånda hoppper rett i sloten
            var held = player.InventoryManager.ActiveHotbarSlot;
            if (held?.Itemstack?.Collectible is ILiquidSource && fuelInv[0].Empty)
            {
                held.TryPutInto(World, fuelInv[0], 1);
                held.MarkDirty();
            }
        }
    }

    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        if (Api.Side == EnumAppSide.Server && fuelInv != null && !fuelInv[0].Empty)
        {
            World.SpawnItemEntity(fuelInv[0].TakeOutWhole(), Pos.XYZ);
        }
        fuelDialog?.TryClose();
        base.OnEntityDespawn(despawn);
    }

    static WorldInteraction[]? fuelHelp;

    public override WorldInteraction[] GetInteractionHelp(IClientWorldAccessor world, EntitySelection es, IClientPlayer player)
    {
        var help = base.GetInteractionHelp(world, es, player);
        if (player.Entity.MountedOn == this || !IsFuelBox(es.SelectionBoxIndex)) return help;

        fuelHelp ??= new[]
        {
            new WorldInteraction
            {
                ActionLangCode = "wright:fillfuel",
                MouseButton = EnumMouseButton.Right,
                Itemstacks = FuelHelpStacks(world)
            }
        };
        return help == null ? fuelHelp : help.Append(fuelHelp);
    }

    ItemStack[] FuelHelpStacks(IWorldAccessor world)
    {
        var fuel = world.GetItem(new AssetLocation(fuelCode));
        var list = new System.Collections.Generic.List<ItemStack>();
        foreach (var code in new[] { "game:woodbucket", "game:jug-blue-fired" })
        {
            if (fuel == null || world.GetBlock(new AssetLocation(code)) is not BlockLiquidContainerBase block) continue;
            var stack = new ItemStack(block);
            block.SetContent(stack, new ItemStack(fuel, (int)(block.CapacityLitres * 100)));
            list.Add(stack);
        }
        return list.ToArray();
    }

    /// <summary>Kontrollene til den som sitter i førersetet, eller null.</summary>
    EntityControls? PilotControls()
    {
        var seatable = GetBehavior<EntityBehaviorSeatable>();
        if (seatable?.Controller == null) return null;
        foreach (var seat in seatable.Seats)
        {
            if (seat.Passenger == seatable.Controller) return seat.Controls;
        }
        return null;
    }

    public double LiftRatio => stallSpeed > 0 ? Math.Pow(ForwardSpeed / stallSpeed, 2) : 0;

    static double Approach(double value, double target, double step)
    {
        return value < target ? Math.Min(value + step, target) : Math.Max(value - step, target);
    }

    protected override void updateBoatAngleAndMotion(float dt)
    {
        dt = Math.Min(0.5f, dt);

        // Vanilla-seteoppdateringen: setter Controller og animasjoner. Selve svingen regner vi ut selv.
        SeatsToMotion(GlobalConstants.PhysicsFrameTime);
        var controls = PilotControls();
        var pos = Pos;

        bool pullUp = controls?.Jump == true;
        bool pushDown = controls?.Sprint == true;
        int steer = controls == null ? 0 : (controls.Left ? 1 : 0) - (controls.Right ? 1 : 0);

        // --- Gass ---
        if (controls != null)
        {
            if (controls.Forward) Throttle += throttleRate * dt;
            if (controls.Backward) Throttle -= throttleRate * dt;
        }
        else
        {
            Throttle = 0; // ingen pilot = motoren går ikke
        }
        Throttle = GameMath.Clamp(Throttle, 0, 1);

        // --- Drivstoff --- tom tank = motoren stopper, flyet glir
        if (Throttle > 0)
        {
            double fuel = FuelLitres;
            if (fuel <= 0) Throttle = 0;
            else if (Api.Side == EnumAppSide.Server) FuelLitres = fuel - Throttle * fuelBurnRate * dt;
        }

        double lift = LiftRatio;

        // --- Nesevinkel ---
        if (Flying)
        {
            if (pullUp) PitchAngle += pitchRate * dt;
            if (pushDown) PitchAngle -= pitchRate * dt;

            // Stall: vingene bærer ikke, nesa faller til flyet har fått fart igjen
            Stalling = lift < 1;
            if (Stalling) PitchAngle = Approach(PitchAngle, -0.35, stallNoseDrop * (1 - lift) * dt);
        }
        else
        {
            Stalling = false;
            // På bakken: halen står nede. Nesa kan bare løftes (rotasjon) nær avgangsfart.
            if (pullUp && lift >= rotateLift) PitchAngle += pitchRate * dt;
            else PitchAngle = Approach(PitchAngle, 0, pitchRate * 2 * dt);
            PitchAngle = Math.Max(0, PitchAngle);
        }
        PitchAngle = GameMath.Clamp(PitchAngle, -maxPitchDown, maxPitchUp);

        // --- Fart langs banen ---
        double targetSpeed = Throttle * maxSpeed;
        ForwardSpeed += (targetSpeed - ForwardSpeed) * accel * dt;
        if (OnGround && Throttle <= 0 && controls?.Backward == true)
        {
            ForwardSpeed = Math.Max(0, ForwardSpeed - brakeRate * maxSpeed * dt);
        }
        if (Flying) ForwardSpeed -= gravityAlongPath * Math.Sin(PitchAngle) * dt; // stigning spiser fart, dykk gir fart
        ForwardSpeed = Math.Max(0, ForwardSpeed);
        if (ForwardSpeed < 0.0005 && Throttle <= 0) ForwardSpeed = 0;

        // Avgang: vingene bærer og nesa er oppe
        if (!Flying && lift >= 1 && PitchAngle > 0.05) Flying = true;
        if (!OnGround && !Flying && lift >= 1) Flying = true; // rullet utfor en kant med nok fart

        double horizontal = Flying ? ForwardSpeed * Math.Cos(PitchAngle) : ForwardSpeed;
        var velocity = pos.GetViewVector().Mul((float)-horizontal).ToVec3d();
        pos.Motion.X = velocity.X;
        pos.Motion.Z = velocity.Z;

        // --- Vertikal ---
        if (Flying)
        {
            // Krengning koster løft: bare den loddrette delen av løftet bærer flyet
            double bearing = Math.Min(lift, 1) * Math.Cos(BankAngle);
            double targetVy = ForwardSpeed * Math.Sin(PitchAngle) - maxSinkRate * (1 - bearing);
            pos.Motion.Y += (targetVy - pos.Motion.Y) * Math.Min(1, verticalResponse * dt);

            // Landet: gi tilbake tyngdekraften
            if (OnGround && pos.Motion.Y <= 0.001 && PitchAngle <= 0.1)
            {
                Flying = false;
            }
        }

        // --- Krengning og sving ---
        if (Flying)
        {
            if (steer != 0) BankAngle = Approach(BankAngle, steer * maxBank, bankRate * dt);
            else BankAngle = Approach(BankAngle, 0, bankReturn * dt);
        }
        else
        {
            BankAngle = Approach(BankAngle, 0, bankReturn * 2 * dt);
        }

        double targetTurn;
        if (Flying)
        {
            targetTurn = airTurnRate * Math.Sin(BankAngle) / Math.Sin(maxBank);
        }
        else
        {
            // Halespor: styrer bare når flyet ruller, full effekt fra litt fart
            double grip = Math.Min(1, ForwardSpeed / (stallSpeed * 0.3));
            targetTurn = groundTurnRate * steer * Math.Max(0.25, grip);
        }
        // AngularVelocity holdes i vanilla-enheter (yaw per tick * 1/30), så båt-renderer og seter er fornøyde
        double targetAv = targetTurn / 30.0;
        AngularVelocity += (targetAv - AngularVelocity) * Math.Min(1, turnResponse * dt);

        var physics = GetBehavior<EntityBehaviorPassivePhysicsMultiBox>()!;
        bool rotated = true;
        if (AngularVelocity != 0.0)
        {
            float yawDelta = (float)AngularVelocity * dt * 30f;
            if (physics.AdjustCollisionBoxesToYaw(dt, true, pos.Yaw + yawDelta)) pos.Yaw += yawDelta;
            else rotated = false;
        }
        else
        {
            rotated = physics.AdjustCollisionBoxesToYaw(dt, true, pos.Yaw);
        }

        // Sitter fast mot noe: vrikk litt, som vanilla
        if (!rotated)
        {
            if (physics.AdjustCollisionBoxesToYaw(dt, true, pos.Yaw - 0.1f)) pos.Yaw -= 0.0002f;
            else if (physics.AdjustCollisionBoxesToYaw(dt, true, pos.Yaw + 0.1f)) pos.Yaw += 0.0002f;
        }

        pos.Roll = 0f;
    }

    public override void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        base.OnRenderFrame(dt, stage);
        if (Api is not ICoreClientAPI capi || capi.IsGamePaused) return;
        if (Properties.Client.Renderer is not EntityShapeRenderer renderer) return;

        // Overstyr båtens vugging med flyets egen holdning
        float shake = 0;
        if (Stalling || ForwardSpeed > maxSpeed * 0.95)
        {
            double t = capi.InWorldEllapsedMilliseconds / 1000.0;
            shake = (float)(Math.Sin(t * 47) * 0.012 + Math.Sin(t * 31) * 0.008);
        }
        renderer.xangle = visualRollSign * (float)BankAngle + shake;
        renderer.yangle = 0;
        renderer.zangle = visualPitchSign * (float)PitchAngle + shake * 0.5f;
    }
}
