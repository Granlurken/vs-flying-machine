using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace FlyingMachine;

/// <summary>
/// Flyet. Arver seter, sving og kollisjon fra vanilla-båten, men har egen flymodell.
///
/// Enheter: fart er i blokker per 1/60 sekund (samme som Entity.Pos.Motion), så 0.25 = 15 blokker/s.
/// Vinkler i radianer.
///
/// Kontroller:
///   W/S    = gass opp/ned (blir liggende). Hold S på tomgang på bakken = hjulbrems (med lyd).
///   Space  = nesa opp, Ctrl = nesa ned. Nesevinkelen blir liggende når du slipper (trim).
///   A/D    = krengning i lufta (svingen følger krengningen), halesporstyring på bakken.
///
/// Flymodell: flyet går dit nesa peker. Tyngdekraften virker langs banen, så stigning spiser fart og
/// dykk gir fart. Under stallfart bærer ikke vingene: flyet synker og nesa faller av seg selv.
/// </summary>
public class EntityFlyingMachine : EntityBoat, ISeatInstSupplier
{
    // Tuning — leses fra entity-JSON (attributes.flight), dette er fallbackene
    double maxSpeed = 0.28;       // toppfart ved full gass
    double stallSpeed = 0.15;     // under dette bærer ikke vingene flyet
    double thrustAccel = 0.018;   // fart/s ved full gass fra stillestående — lavt = lang rullebane. Luftmotstanden (∝ fart²) tar resten, så toppfarten blir maxSpeed
    double rollingFriction = 0.002; // fart/s tapt på bakken (hjul i gress)
    int safeDismountHeight = 3;   // Shift stiger bare av når det er noe å stå på (fast mark eller vann) innen så mange blokker under
    double throttleRate = 0.4;    // gass-endring per sekund med W/S holdt inne
    double brakeRate = 0.6;       // ekstra retardasjon på bakken med S på tomgang
    double gravityAlongPath = 0.12; // fart tapt/vunnet per sekund per sin(banevinkel)
    double maxSinkRate = 0.2;     // fritt fall uten løft
    double verticalResponse = 3;  // hvor fort vertikalfarten følger banen

    double pitchRate = 0.7;       // rad/s med Space/Ctrl
    double pitchResponse = 4;     // hvor fort nesa kommer i gang / stopper (1/s) — lavt = tyngre nese
    double maxPitchUp = 0.6;      // ~35°
    double maxPitchDown = 0.8;    // ~45°
    double rotateLift = 0.8;      // løftandel der nesa kan løftes på bakken (rotasjon før avgang)
    double stallNoseDrop = 1.2;   // rad/s nesa faller når vingene ikke bærer

    double maxBank = 0.7;         // ~40°
    double bankRate = 1.5;        // maks rullehastighet, rad/s
    double bankStiffness = 9;     // fjær mot ønsket krengning — høyt = kvikkere
    double bankDamping = 3.3;     // demping — lavt = mer oversving/gynging (2·√stiffness = ingen oversving)
    double airTurnRate = 1.2;     // rad/s sving ved full krengning
    double groundTurnRate = 0.8;  // rad/s halesporstyring
    double turnResponse = 3;      // hvor fort svingraten følger ønsket verdi

    // Skrens: banen (retningen flyet faktisk beveger seg) henger etter nesa
    double airGrip = 1.5;         // 1/s: hvor fort banen retter seg etter nesa i lufta — lavt = mer skrens
    double groundSettle = 5;      // 1/s: hvor fort flyet legger seg på hjulene etter landing (nese/krengning) — lavt = mykere
    double groundGrip = 8;        // hjulene holder banen stramt på bakken
    double slipDrag = 0.4;        // andel fart tapt per sekund ved 90° skrens
    double trackYaw = double.NaN; // retningen flyet beveger seg
    double bankVelocity, pitchVelocity;
    readonly EntityPos trackPos = new();

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

    // Oppstart: fra motoren er av til den tar (sekunder). Så lenge den sveiver er gassen låst til CrankThrottle
    double engineStartSeconds = 2;
    const double CrankThrottle = 0.01;
    bool engineOn;
    double startupLeft;

    // Visuelt: propell, hjul og eksos (attributes.visuals i JSON)
    double propMaxRevs = 6;       // omdreininger/s ved full gass (holdes lavt, ellers «står» propellen på skjermen)
    double propIdleRevs = 1.5;    // tomgang når motoren går
    float propSpinSign = 1, wheelSpinSign = -1;
    double wheelRadius = 0.38;
    Vec3d[] exhausts = { new(2.38, 1.95, 0.335), new(2.84, 1.95, 0.335) }; // design-ramme: +X nese, +Y opp, +Z høyre
    double propAngle, wheelAngle, wheelRate, propRevs;
    double propCrankTime = -1;   // sekunder inn i sveivingen (klient), −1 = ikke i gang
    FlyingMachineBrakeSound? brakeSound;
    Vec3d? lastRenderPos;

    // Instrumentbordet (attributes.instruments i JSON). Viserne leses av posisjonsendring, så de virker for alle
    float needleSign = 1;         // +1 = viserne går med klokka slik piloten ser dem
    double speedDialMax = 25;     // blokker/s ved fullt utslag (270°)
    double lowFuelFraction = 0.15;
    double altLongPerTurn = 100, altShortPerTurn = 1000; // blokker over havnivå per runde
    // Variometer: ±varioMax blokker/s over ±varioSpan grader fra klokka 9, log-skala (varioCurve) — må stemme med dash_textures.js
    double varioMax = 10, varioSpan = 175, varioCurve = 4, varioLag = 1.5;
    double gaugeSpeed, gaugeFuel, gaugeVario;
    Vec3d? lastGaugePos;
    SimpleParticleProperties? smoke;
    FlyingMachineEngineSound? engineSound;

    /// <summary>Gass slik alle klienter ser den: egen verdi for piloten, ellers synket fra serveren.</summary>
    public double EngineThrottle => ThrottleSeenBy((Api as ICoreClientAPI)?.World.Player.Entity);

    public double ThrottleSeenBy(EntityAgent? viewer) => IsPilot(viewer) ? Throttle : WatchedAttributes.GetFloat("throttle", 0);

    /// <summary>Sitter i førersetet i dette flyet. MountedOn er setet, og MountSupplier sete-behavioren, så sammenlign setets Entity.</summary>
    bool IsPilot(EntityAgent? viewer) => viewer?.MountedOn is IMountableSeat seat && seat.Entity == this && seat.CanControl;

    public bool Flying { get; private set; }
    /// <summary>Hjulbremsen er på (S holdt på tomgang, på bakken, flyet ruller).</summary>
    public bool Braking { get; private set; }

    /// <summary>Bremsen slik alle klienter ser den: egen verdi for piloten, ellers synket fra serveren.</summary>
    public bool BrakeActive => BrakeSeenBy((Api as ICoreClientAPI)?.World.Player.Entity);

    public bool BrakeSeenBy(EntityAgent? viewer) => IsPilot(viewer) ? Braking : WatchedAttributes.GetBool("braking", false);

    /// <summary>Nesevinkel, positiv = nesa opp.</summary>
    public double PitchAngle { get; private set; }
    /// <summary>Krengning, positiv = venstre vinge ned.</summary>
    public double BankAngle { get; private set; }
    public bool Stalling { get; private set; }

    public override bool ApplyGravity => !Flying;

    /// <summary>
    /// Kun ved nyfødsel (ikke ved lasting). Fly fra byggeplassen starter tomme; satt ut med kreativ-itemet
    /// (eller /entity spawn) får de full tank, så man kan prøvefly uten å destillere først.
    /// </summary>
    public override void OnEntitySpawn()
    {
        base.OnEntitySpawn();
        if (Api.Side == EnumAppSide.Server && !WatchedAttributes.GetBool(BuiltOnSiteKey)) FuelLitres = fuelCapacity;
    }

    public const string BuiltOnSiteKey = "builtOnSite";

    public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
    {
        base.Initialize(properties, api, InChunkIndex3d);
        InitFuelInventory(api);
        if (api is ICoreClientAPI capiInit)
        {
            engineSound = new FlyingMachineEngineSound(capiInit, this, properties.Attributes["sound"]);
            brakeSound = new FlyingMachineBrakeSound(capiInit, this, properties.Attributes["sound"]["brake"]);
        }

        var fuel = properties.Attributes["fuel"];
        if (fuel.Exists)
        {
            fuelCode = fuel["code"].AsString(fuelCode);
            fuelCapacity = fuel["capacityLitres"].AsDouble(fuelCapacity);
            fuelBurnRate = fuel["litresPerMinuteFullThrottle"].AsDouble(fuelBurnRate * 60) / 60;
        }

        var vis = properties.Attributes["visuals"];
        if (vis.Exists)
        {
            propMaxRevs = vis["propMaxRevsPerSec"].AsDouble(propMaxRevs);
            propIdleRevs = vis["propIdleRevsPerSec"].AsDouble(propIdleRevs);
            propSpinSign = vis["propSpinSign"].AsFloat(propSpinSign);
            wheelSpinSign = vis["wheelSpinSign"].AsFloat(wheelSpinSign);
            wheelRadius = vis["wheelRadius"].AsDouble(wheelRadius);
            var ex = vis["exhausts"].AsArray<double[]>(null);
            if (ex != null) exhausts = Array.ConvertAll(ex, e => new Vec3d(e[0], e[1], e[2]));
        }

        var ins = properties.Attributes["instruments"];
        if (ins.Exists)
        {
            needleSign = ins["needleSign"].AsFloat(needleSign);
            speedDialMax = ins["speedDialMax"].AsDouble(speedDialMax);
            lowFuelFraction = ins["lowFuelFraction"].AsDouble(lowFuelFraction);
            altLongPerTurn = ins["altLongPerTurn"].AsDouble(altLongPerTurn);
            altShortPerTurn = ins["altShortPerTurn"].AsDouble(altShortPerTurn);
            varioMax = ins["varioMax"].AsDouble(varioMax);
            varioSpan = ins["varioSpan"].AsDouble(varioSpan);
            varioCurve = ins["varioCurve"].AsDouble(varioCurve);
            varioLag = ins["varioLag"].AsDouble(varioLag);
        }

        var f = properties.Attributes["flight"];
        if (!f.Exists) return;
        maxSpeed = f["maxSpeed"].AsDouble(maxSpeed);
        engineStartSeconds = f["engineStartSeconds"].AsDouble(engineStartSeconds);
        stallSpeed = f["stallSpeed"].AsDouble(stallSpeed);
        thrustAccel = f["thrustAccel"].AsDouble(thrustAccel);
        rollingFriction = f["rollingFriction"].AsDouble(rollingFriction);
        safeDismountHeight = f["safeDismountHeight"].AsInt(safeDismountHeight);
        throttleRate = f["throttleRate"].AsDouble(throttleRate);
        brakeRate = f["brakeRate"].AsDouble(brakeRate);
        gravityAlongPath = f["gravityAlongPath"].AsDouble(gravityAlongPath);
        maxSinkRate = f["maxSinkRate"].AsDouble(maxSinkRate);
        verticalResponse = f["verticalResponse"].AsDouble(verticalResponse);
        pitchRate = f["pitchRate"].AsDouble(pitchRate);
        pitchResponse = f["pitchResponse"].AsDouble(pitchResponse);
        maxPitchUp = f["maxPitchUp"].AsDouble(maxPitchUp);
        maxPitchDown = f["maxPitchDown"].AsDouble(maxPitchDown);
        rotateLift = f["rotateLift"].AsDouble(rotateLift);
        stallNoseDrop = f["stallNoseDrop"].AsDouble(stallNoseDrop);
        maxBank = f["maxBank"].AsDouble(maxBank);
        bankRate = f["bankRate"].AsDouble(bankRate);
        bankStiffness = f["bankStiffness"].AsDouble(bankStiffness);
        bankDamping = f["bankDamping"].AsDouble(bankDamping);
        airTurnRate = f["airTurnRate"].AsDouble(airTurnRate);
        groundTurnRate = f["groundTurnRate"].AsDouble(groundTurnRate);
        turnResponse = f["turnResponse"].AsDouble(turnResponse);
        airGrip = f["airGrip"].AsDouble(airGrip);
        groundGrip = f["groundGrip"].AsDouble(groundGrip);
        groundSettle = f["groundSettle"].AsDouble(groundSettle);
        slipDrag = f["slipDrag"].AsDouble(slipDrag);
        visualPitchSign = f["visualPitchSign"].AsFloat(visualPitchSign);
        visualRollSign = f["visualRollSign"].AsFloat(visualRollSign);
    }

    // --- Fylling: høyreklikk med væskebeholder åpner fylle-vinduet (slot → pil → tank) ---
    const int PacketOpenFuel = 1001;
    const int PacketCloseFuel = 1003; // server → klient: lukk vinduet (opptatt, gått for langt unna)
    const double PourLitresPerSecond = 1;
    const double FuelReach = 8;
    double fuelUserCheck;

    /// <summary>
    /// Spilleren som har fylle-vinduet åpent, eller null. Bare én om gangen: sloten er felles for alle,
    /// så med to åpne kunne den ene lukke og få den andres bøtte.
    /// </summary>
    string? FuelUser
    {
        get { string? uid = WatchedAttributes.GetString("fuelUser"); return string.IsNullOrEmpty(uid) ? null : uid; }
        set { if (value == null) WatchedAttributes.RemoveAttribute("fuelUser"); else WatchedAttributes.SetString("fuelUser", value); }
    }
    InventoryGeneric? fuelInv;
    GuiDialogFlyingMachineFuel? fuelDialog;
    double pourCarry; // brøkdel av væske-item som ikke er helt ennå

    public InventoryGeneric FuelInventory => fuelInv!;

    void InitFuelInventory(ICoreAPI api)
    {
        fuelInv = new InventoryGeneric(1, "flyingmachinefuel-" + EntityId, api, (_, inv) => new ItemSlotFlyingMachineFuel(inv));
        if (api.Side == EnumAppSide.Server)
        {
            // Lukker du vinduet, får du beholderen tilbake (sloten lagres ikke med flyet)
            fuelInv.OnInventoryClosed += player =>
            {
                if (player?.PlayerUID != FuelUser) return;
                ReturnFuelSlot(player);
                FuelUser = null;
            };
        }
    }

    /// <summary>
    /// Server, ca. hvert sekund: har fylleren logget ut, gått for langt unna eller lukket uten at vi fikk beskjed,
    /// lukkes vinduet og beholderen går tilbake (eller ligger ved flyet hvis spilleren er borte).
    /// </summary>
    void CheckFuelUser(float dt)
    {
        if (FuelUser == null || (fuelUserCheck += dt) < 1) return;
        fuelUserCheck = 0;
        var player = World.PlayerByUid(FuelUser) as IServerPlayer;
        bool online = player?.Entity != null && player.ConnectionState == EnumClientState.Playing;
        if (online && fuelInv!.HasOpened(player) && player!.Entity.Pos.DistanceTo(Pos) < FuelReach) return;

        if (online)
        {
            ((ICoreServerAPI)Api).Network.SendEntityPacket(player, EntityId, PacketCloseFuel, null);
            player!.InventoryManager.CloseInventory(fuelInv); // → OnInventoryClosed gir beholderen tilbake
        }
        if (FuelUser != null)
        {
            ReturnFuelSlot(online ? player : null);
            FuelUser = null;
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

    /// <summary>Tilstanden til fylle-sloten, som lang-nøkkel (uten «flyingmachine:»).</summary>
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
        if (Api.Side != EnumAppSide.Server) return;
        if (fuelInv != null) { CheckFuelUser(dt); PourTick(dt); }
        // Gassen synkes til klientene (propell + røyk hos tilskuere og passasjerer)
        float synced = WatchedAttributes.GetFloat("throttle", 0);
        if (Math.Abs(synced - Throttle) > 0.02 || (Throttle <= 0) != (synced <= 0))
        {
            WatchedAttributes.SetFloat("throttle", (float)Throttle);
        }
        if (WatchedAttributes.GetBool("braking", false) != Braking) WatchedAttributes.SetBool("braking", Braking);
    }

    public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode)
    {
        if (mode == EnumInteractMode.Interact && byEntity.Controls.ShiftKey
            && FlyingMachineMaterials.IsDisassemblyTool(itemslot?.Itemstack, Properties.Attributes))
        {
            // Klienten holder (tannhjul i 5 s) og sender pakken; serveren gjør ingenting på selve klikket
            if (Api is ICoreClientAPI capi0 && byEntity == capi0.World.Player.Entity)
            {
                string? error = DisassembleError(byEntity);
                if (error != null) FlyingMachineMaterials.Error(this, byEntity, error);
                else FlyingMachineHoldAction.Start(capi0, this, Properties.Attributes,
                    () => capi0.Network.SendEntityPacket(EntityId, PacketDisassemble, null));
            }
            return;
        }
        if (mode == EnumInteractMode.Interact && !byEntity.Controls.ShiftKey
            && itemslot?.Itemstack?.Collectible is ILiquidSource
            && byEntity is EntityPlayer ep && IsFuelBox(ep.EntitySelection?.SelectionBoxIndex ?? -1))
        {
            if (Api is ICoreClientAPI capi && byEntity == capi.World.Player.Entity) OpenFuelDialog(capi);
            return; // ikke sett deg inn
        }
        // Kreativ snarvei: Shift + høyreklikk med tom hånd plukker opp flyet (vanilla rightClickPickup er av,
        // ellers kunne man stappe et bygd fly i sekken i overlevelse)
        if (mode == EnumInteractMode.Interact && byEntity.Controls.ShiftKey && itemslot?.Empty != false
            && (byEntity as EntityPlayer)?.Player?.WorldData.CurrentGameMode == EnumGameMode.Creative
            && DisassembleError(byEntity) == null)
        {
            if (World.Side == EnumAppSide.Server)
            {
                var stack = new ItemStack(World.GetItem(Code));
                if (!byEntity.TryGiveItemStack(stack)) World.SpawnItemEntity(stack, Pos.XYZ);
                Die(EnumDespawnReason.Removed);
            }
            return;
        }
        base.OnInteract(byEntity, itemslot, hitPosition, mode);
    }

    const int PacketDisassemble = 1002;

    /// <summary>Null hvis flyet kan tas fra hverandre: ingen om bord, tom lasteplass, står stille på bakken.</summary>
    string? DisassembleError()
    {
        return DisassembleError(null);
    }

    /// <summary>Som over, pluss eierskap når vi vet hvem som spør.</summary>
    string? DisassembleError(EntityAgent? byEntity)
    {
        if (byEntity != null && FlyingMachineMaterials.OwnerError(this, byEntity) is string owner) return owner;
        if (GetBehavior<EntityBehaviorSeatable>()?.AnyMounted() == true) return "flyingmachine:disassemble-occupied";
        if (GetBehavior<EntityBehaviorRideableAccessories>()?.Inventory.Empty == false) return "flyingmachine:disassemble-cargo";
        if (Flying || !WheelsOnGround() || Pos.Motion.Length() > 0.01) return "flyingmachine:disassemble-notlanded";
        return null;
    }

    /// <summary>
    /// Shift + skiftenøkkel holdt i <c>attributes.disassembly.seconds</c> (jern eller bedre): flyet tas fra hverandre
    /// og gir tilbake alt det ble bygd av, bukkene inkludert. Drivstoffet i tanken går tapt. Kun server.
    /// </summary>
    void Disassemble(EntityAgent byEntity)
    {

        string siteCode = Properties.Attributes["constructionEntity"].AsString("flyingmachine:flyingmachineconstruction");
        var stacks = FlyingMachineMaterials.Stored(this) ?? FlyingMachineMaterials.FromStages(World, siteCode);
        var siteType = World.GetEntityType(new AssetLocation(siteCode));
        var trestles = World.GetItem(new AssetLocation(siteType?.Attributes?["returnsItem"].AsString("flyingmachine:trestles") ?? "flyingmachine:trestles"));
        if (trestles != null) stacks.Add(new ItemStack(trestles));

        FlyingMachineMaterials.GiveBack(byEntity, stacks, Pos.XYZ);
        World.Logger.Audit("{0} took apart a flying machine at {1}.", byEntity.GetName(), Pos);
        Die(EnumDespawnReason.Removed);
    }

    /// <summary>
    /// Navnet øverst på skjermen: peker spilleren på en del (sete, motor, lasteplass), vises delen —
    /// lang-nøkkel <c>flyingmachine:part-&lt;AP-kode&gt;</c>. Ellers flyets eget navn.
    /// </summary>
    public override string GetName()
    {
        string? part = HoveredPart();
        return part != null ? Lang.Get("flyingmachine:part-" + part) : base.GetName();
    }

    /// <summary>
    /// Under delenavnet står flyets navn, så man ser hva delen hører til. Vanilla-linja for klikkboksen fjernes:
    /// selectionboxes-behavioren slår opp «creature-flyingmachine:flyingmachine-selectionbox-&lt;AP&gt;» (kolon midt i nøkkelen),
    /// som aldri treffer og vises som rå nøkkel — delenavnet står allerede som tittel.
    /// </summary>
    public override string GetInfoText()
    {
        var lines = new System.Collections.Generic.List<string>(base.GetInfoText().Split('\n'));
        lines.RemoveAll(l => l.Contains("-selectionbox-"));
        string text = string.Join("\n", lines);
        return HoveredPart() != null ? base.GetName() + "\n" + text : text;
    }

    /// <summary>AP-koden til klikkboksen den lokale spilleren peker på, eller null (hovedhitboksen / ikke pekt på).</summary>
    string? HoveredPart()
    {
        if (Api is not ICoreClientAPI capi) return null;
        var sel = capi.World.Player?.CurrentEntitySelection;
        if (sel?.Entity != this) return null;
        var boxes = GetBehavior<EntityBehaviorSelectionBoxes>()?.selectionBoxes;
        int i = sel.SelectionBoxIndex - 1;
        if (boxes == null || i < 0 || i >= boxes.Length) return null;
        string code = boxes[i].AttachPoint.Code;
        return Lang.HasTranslation("flyingmachine:part-" + code) ? code : null;
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
        if (FuelUser != null && FuelUser != capi.World.Player.PlayerUID)
        {
            capi.TriggerIngameError(this, "flyingmachine-fuelbusy", Lang.Get("flyingmachine:fuel-busy"));
            return;
        }
        capi.Network.SendEntityPacket(EntityId, PacketOpenFuel, null);
        fuelDialog = new GuiDialogFlyingMachineFuel(capi, this);
        fuelDialog.OnClosed += () => fuelDialog = null;
        fuelDialog.TryOpen();
    }

    public override void OnReceivedClientPacket(IServerPlayer player, int packetid, byte[] data)
    {
        base.OnReceivedClientPacket(player, packetid, data);
        if (packetid == PacketDisassemble)
        {
            // Sjekk alt på nytt: verktøy i hånda, innen rekkevidde, og at flyet fortsatt kan tas fra hverandre
            if (Alive && player.Entity.Pos.DistanceTo(Pos) < 8 && DisassembleError(player.Entity) == null
                && FlyingMachineMaterials.IsDisassemblyTool(player.InventoryManager.ActiveHotbarSlot?.Itemstack, Properties.Attributes))
            {
                Disassemble(player.Entity);
            }
            return;
        }
        if (fuelInv == null) return;
        if (packetid < 1000)
        {
            // Bare den som har vinduet åpent får flytte ting i sloten
            if (player.PlayerUID == FuelUser) fuelInv.InvNetworkUtil.HandleClientPacket(player, packetid, data);
            return;
        }
        if (packetid == PacketOpenFuel)
        {
            // Noen andre fyller allerede (to klikket samtidig): lukk vinduet hos denne
            if ((FuelUser != null && FuelUser != player.PlayerUID) || player.Entity.Pos.DistanceTo(Pos) >= FuelReach)
            {
                ((ICoreServerAPI)Api).Network.SendEntityPacket(player, EntityId, PacketCloseFuel, new byte[] { 1 });
                return;
            }
            FuelUser = player.PlayerUID;
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

    public override void OnReceivedServerPacket(int packetid, byte[] data)
    {
        base.OnReceivedServerPacket(packetid, data);
        if (packetid != PacketCloseFuel || Api is not ICoreClientAPI capi) return;
        fuelDialog?.TryClose();
        if (data != null && data.Length > 0) capi.TriggerIngameError(this, "flyingmachine-fuelbusy", Lang.Get("flyingmachine:fuel-busy"));
    }

    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        if (Api.Side == EnumAppSide.Server && fuelInv != null && !fuelInv[0].Empty)
        {
            World.SpawnItemEntity(fuelInv[0].TakeOutWhole(), Pos.XYZ);
        }
        if (Api.Side == EnumAppSide.Server) FuelUser = null; // ellers lagres «opptatt» med flyet
        fuelDialog?.TryClose();
        engineSound?.Dispose();
        engineSound = null;
        brakeSound?.Dispose();
        brakeSound = null;
        base.OnEntityDespawn(despawn);
    }

    static WorldInteraction[]? fuelHelp;
    static WorldInteraction[]? disassembleHelp;

    public override WorldInteraction[] GetInteractionHelp(IClientWorldAccessor world, EntitySelection es, IClientPlayer player)
    {
        var help = base.GetInteractionHelp(world, es, player);
        if (player.Entity.MountedOn == this) return help;

        disassembleHelp ??= new[]
        {
            new WorldInteraction
            {
                ActionLangCode = "flyingmachine:disassemble",
                HotKeyCode = "shift",
                MouseButton = EnumMouseButton.Right,
                Itemstacks = FlyingMachineMaterials.ToolStacks(world, Properties.Attributes)
            }
        };
        help = help == null ? disassembleHelp : help.Append(disassembleHelp);
        if (!IsFuelBox(es.SelectionBoxIndex)) return help;

        fuelHelp ??= new[]
        {
            new WorldInteraction
            {
                ActionLangCode = "flyingmachine:fillfuel",
                MouseButton = EnumMouseButton.Right,
                Itemstacks = FuelHelpStacks(world)
            }
        };
        return help.Append(fuelHelp);
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

    /// <summary>
    /// Står hjulene på noe? Egen sjekk i stedet for OnGround: på pilotens klient blir OnGround aldri satt for flyet,
    /// så klienten trodde den fortsatt fløy etter landing (nesa ned, ingen brems) mens serveren hadde landet.
    /// Tynn sonde rett under kollisjonsboksen — samme svar på begge sider.
    /// </summary>
    bool WheelsOnGround()
    {
        var box = CollisionBox;
        if (box == null) return OnGround;
        groundProbe.Set(box.X1, box.Y1 - 0.08f, box.Z1, box.X2, box.Y1, box.Z2);
        return World.CollisionTester.IsColliding(World.BlockAccessor, groundProbe, Pos.XYZ, false);
    }
    readonly Cuboidf groundProbe = new();

    // Egne seter: vanilla EntityBoat.CreateSeat er ikke virtual, så grensesnittet implementeres på nytt her
    public new IMountableSeat CreateSeat(IMountable mountable, string seatId, SeatConfig config = null!)
    {
        return new EntityFlyingMachineSeat(mountable, seatId, config);
    }

    /// <summary>
    /// Sant når flyet er så høyt oppe at det er livsfarlig å hoppe av: verken hjul i bakken eller noe å lande på
    /// (fast blokk eller vann) innen safeDismountHeight blokker under. Ren geometri, så klient og server svarer likt.
    /// </summary>
    public bool DismountBlocked()
    {
        if (WheelsOnGround()) return false;
        var ba = World.BlockAccessor;
        var bp = Pos.AsBlockPos;
        for (int i = 0; i <= safeDismountHeight; i++, bp.Y--)
        {
            if (ba.GetBlock(bp, BlockLayersAccess.Fluid).IsLiquid()) return false;
            var boxes = ba.GetBlock(bp).GetCollisionBoxes(ba, bp);
            if (boxes != null && boxes.Length > 0) return false;
        }
        return true;
    }

    public double LiftRatio =>stallSpeed > 0 ? Math.Pow(ForwardSpeed / stallSpeed, 2) : 0;

    static double Approach(double value, double target, double step)
    {
        return value < target ? Math.Min(value + step, target) : Math.Max(value - step, target);
    }

    /// <summary>Korteste vinkel fra «from» til «to», i [-π, π].</summary>
    static double AngleDiff(double from, double to)
    {
        double d = (to - from) % (2 * Math.PI);
        if (d > Math.PI) d -= 2 * Math.PI;
        if (d < -Math.PI) d += 2 * Math.PI;
        return d;
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
        bool grounded = WheelsOnGround();

        // --- Drivstoff --- tom tank = motoren stopper, flyet glir
        if (Throttle > 0)
        {
            double fuel = FuelLitres;
            if (fuel <= 0) Throttle = 0;
            else if (Api.Side == EnumAppSide.Server) FuelLitres = fuel - Throttle * fuelBurnRate * dt;
        }

        // --- Oppstart --- motoren sveiver før den tar (matcher startlyden): gassen holdes nede så flyet står stille
        if (Throttle > 0 && !engineOn) { engineOn = true; startupLeft = engineStartSeconds; }
        if (Throttle <= 0) { engineOn = false; startupLeft = 0; }
        if (startupLeft > 0)
        {
            startupLeft -= dt;
            Throttle = Math.Min(Throttle, CrankThrottle);
        }

        double lift = LiftRatio;

        // --- Nesevinkel ---
        if (Flying)
        {
            // Nesa har treghet: den kommer gradvis i gang og glir litt etter når du slipper
            double pitchInput = (pullUp ? 1 : 0) - (pushDown ? 1 : 0);
            pitchVelocity += (pitchInput * pitchRate - pitchVelocity) * Math.Min(1, pitchResponse * dt);
            PitchAngle += pitchVelocity * dt;

            // Stall: vingene bærer ikke, nesa faller til flyet har fått fart igjen
            Stalling = lift < 1;
            if (Stalling) PitchAngle = Approach(PitchAngle, -0.35, stallNoseDrop * (1 - lift) * dt);
        }
        else
        {
            Stalling = false;
            pitchVelocity = 0;
            // På bakken: halen står nede. Nesa kan bare løftes (rotasjon) nær avgangsfart.
            if (pullUp && lift >= rotateLift) PitchAngle += pitchRate * dt;
            else if (PitchAngle < 0) PitchAngle += -PitchAngle * Math.Min(1, groundSettle * dt); // landet med nesa ned: flyet legger seg mykt ned på hjulene
            else PitchAngle = Approach(PitchAngle, 0, pitchRate * 2 * dt);
        }
        PitchAngle = GameMath.Clamp(PitchAngle, -maxPitchDown, maxPitchUp);

        // --- Fart langs banen ---
        // Skyvekraft minus luftmotstand (∝ fart²). Motstanden er satt så full gass gir nøyaktig maxSpeed.
        // Gir nesten jevn akselerasjon nedover rullebanen, i stedet for et rykk fra stillestående.
        double dragCoeff = maxSpeed > 0 ? thrustAccel / (maxSpeed * maxSpeed) : 0;
        ForwardSpeed += (Throttle * thrustAccel - dragCoeff * ForwardSpeed * ForwardSpeed) * dt;
        if (!Flying && grounded) ForwardSpeed -= rollingFriction * dt;
        // Brems: hold S når gassen er nede. På hjulene = ikke i lufta (OnGround alene er upålitelig for flyet)
        Braking = !Flying && Throttle <= 0 && controls?.Backward == true && ForwardSpeed > 0;
        if (Braking)
        {
            ForwardSpeed = Math.Max(0, ForwardSpeed - brakeRate * maxSpeed * dt);
        }
        if (Flying) ForwardSpeed -= gravityAlongPath * Math.Sin(PitchAngle) * dt; // stigning spiser fart, dykk gir fart
        ForwardSpeed = Math.Max(0, ForwardSpeed);
        if (ForwardSpeed < 0.0005 && Throttle <= 0) ForwardSpeed = 0;

        // Avgang: vingene bærer og nesa er oppe
        if (!Flying && lift >= 1 && PitchAngle > 0.05) Flying = true;
        if (!grounded && !Flying && lift >= 1) Flying = true; // rullet utfor en kant med nok fart

        // --- Skrens --- banen retter seg gradvis etter nesa. I en sving peker nesa innover mens flyet
        // fortsatt glir litt utover; skrensen koster fart.
        if (double.IsNaN(trackYaw) || ForwardSpeed < 0.002) trackYaw = pos.Yaw;
        double slip = AngleDiff(trackYaw, pos.Yaw);
        trackYaw += slip * Math.Min(1, (Flying ? airGrip : groundGrip) * dt);
        ForwardSpeed *= 1 - slipDrag * Math.Abs(Math.Sin(slip)) * dt;

        double horizontal = Flying ? ForwardSpeed * Math.Cos(PitchAngle) : ForwardSpeed;
        trackPos.SetFrom(pos);
        trackPos.Yaw = (float)trackYaw;
        var velocity = trackPos.GetViewVector().Mul((float)-horizontal).ToVec3d();
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
            if (grounded && pos.Motion.Y <= 0.001 && PitchAngle <= 0.1)
            {
                Flying = false;
            }
        }

        // --- Krengning og sving ---
        if (Flying)
        {
            // Fjær + demper: krengningen bygger seg opp, skyter litt over og gynger seg på plass
            double bankAccel = bankStiffness * (steer * maxBank - BankAngle) - bankDamping * bankVelocity;
            bankVelocity = GameMath.Clamp(bankVelocity + bankAccel * dt, -bankRate, bankRate);
            BankAngle = GameMath.Clamp(BankAngle + bankVelocity * dt, -maxBank * 1.25, maxBank * 1.25);
        }
        else
        {
            bankVelocity = 0;
            BankAngle += -BankAngle * Math.Min(1, groundSettle * dt); // retter seg mykt opp etter landing
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
        // Risting: ved steiling, og ved overfart i dykk (over toppfart, sterkere jo mer). Full gass i cruise = rolig
        double overspeed = GameMath.Clamp((ForwardSpeed / maxSpeed - 1.05) / 0.25, 0, 1);
        float shakeAmount = Stalling ? 1 : (float)overspeed;
        float shake = 0;
        if (shakeAmount > 0)
        {
            double t = capi.InWorldEllapsedMilliseconds / 1000.0;
            shake = shakeAmount * (float)(Math.Sin(t * 47) * 0.012 + Math.Sin(t * 31) * 0.008);
        }
        renderer.xangle = visualRollSign * (float)BankAngle + shake;
        renderer.yangle = 0;
        renderer.zangle = visualPitchSign * (float)PitchAngle + shake * 0.5f;

        UpdateSpinAnimations(dt);
        engineSound?.Update(dt, EngineThrottle, fuelCapacity > 0 ? FuelLitres / fuelCapacity : 0, lowFuelFraction);
        brakeSound?.Update(dt, BrakeActive, gaugeSpeed);
    }

    /// <summary>
    /// Propell og hjul: shape-animasjonene «propspin»/«wheelspin» er én omdreining over 30 rammer.
    /// Vi setter rammen direkte hver frame (samme grep som vanilla-værhanen på seilbåten).
    /// </summary>
    void UpdateSpinAnimations(float dt)
    {
        var anim = AnimManager;
        if (anim?.Animator == null) return;

        // Propell: tomgang når motoren går, opp mot maks med gassen. Tom tank = står stille
        double throttle = EngineThrottle;
        double targetRevs = throttle > 0 ? propIdleRevs + (propMaxRevs - propIdleRevs) * throttle : 0;
        // Sveiv: fra stillestående bruker propellen hele oppstartstida på å nå tomgang (når motoren tar).
        // Spinner den fortsatt ned fra forrige gang, tar den bare opp igjen
        if (throttle <= 0) propCrankTime = -1;
        else if (propCrankTime < 0 && propRevs < 0.05) propCrankTime = 0;
        if (propCrankTime >= 0 && propCrankTime < engineStartSeconds)
        {
            propCrankTime += dt;
            double k = Math.Min(1, propCrankTime / engineStartSeconds);
            propRevs = propIdleRevs * k * k; // tungt i starten, tar seg opp mot slutten
        }
        else propRevs += (targetRevs - propRevs) * Math.Min(1, 2 * dt); // spinner opp og ned mykt
        propAngle = GameMath.Mod(propAngle + propSpinSign * propRevs * 360 * dt, 360);

        // Hjul: ruller med faktisk bakkefart (posisjonsendring — virker også hos tilskuere)
        var p = Pos.XYZ;
        if (lastRenderPos != null && dt > 0)
        {
            double dist = Math.Sqrt(Math.Pow(p.X - lastRenderPos.X, 2) + Math.Pow(p.Z - lastRenderPos.Z, 2));
            double rate = dist < 2 ? dist / dt / wheelRadius * GameMath.RAD2DEG : 0; // grader/s (teleport = 0)
            wheelRate = WheelsOnGround() ? rate : wheelRate * Math.Max(0, 1 - 0.5 * dt); // i lufta spinner de ut
        }
        lastRenderPos = p;
        wheelAngle = GameMath.Mod(wheelAngle + wheelSpinSign * wheelRate * dt, 360);

        SetSpinFrame(anim, "propspin", propAngle);
        SetSpinFrame(anim, "wheelspin", wheelAngle);

        UpdateInstruments(dt, anim);
    }

    /// <summary>
    /// Instrumentbordet: visere og kompasskort er shape-animasjoner (én runde over 30 rammer) som stilles direkte,
    /// lampene er animasjoner som skyver et selvlysende glass fram (ramme 30 = på).
    /// Vinkel 0 = rett opp på skiva.
    /// </summary>
    void UpdateInstruments(float dt, IAnimationManager anim)
    {
        // Fart: faktisk forflytning (også stigning/dykk), glattet så viseren ikke skjelver
        var p = Pos.XYZ;
        if (lastGaugePos != null && dt > 0)
        {
            double moved = p.DistanceTo(lastGaugePos);
            double speed = moved < 2 ? moved / dt : 0; // teleport = 0
            gaugeSpeed += (speed - gaugeSpeed) * Math.Min(1, 3 * dt);
            // Variometer: stige-/synkefart, treg som en ekte (trykkbeholder med lekkasje)
            double climb = moved < 2 ? (p.Y - lastGaugePos.Y) / dt : 0;
            gaugeVario += (climb - gaugeVario) * Math.Min(1, dt / Math.Max(0.01, varioLag));
        }
        lastGaugePos = p;
        SetSpinFrame(anim, "needlespeed", needleSign * (-135 + 270 * GameMath.Clamp(gaugeSpeed / speedDialMax, 0, 1)));
        double varioFrac = Math.Log(1 + varioCurve * Math.Min(1, Math.Abs(gaugeVario) / varioMax)) / Math.Log(1 + varioCurve);
        SetSpinFrame(anim, "needlevario", needleSign * (-90 + Math.Sign(gaugeVario) * varioSpan * varioFrac));

        // Drivstoff: E (−90°) til F (+90°), viseren siger rolig
        double fuel = fuelCapacity > 0 ? FuelLitres / fuelCapacity : 0;
        gaugeFuel += (fuel - gaugeFuel) * Math.Min(1, 2 * dt);
        SetSpinFrame(anim, "needlefuel", needleSign * (-90 + 180 * GameMath.Clamp(gaugeFuel, 0, 1)));

        // Høyde over havet: lang viser 100 blokker per runde, kort 1000
        double alt = Math.Max(0, Pos.Y - Api.World.SeaLevel);
        SetSpinFrame(anim, "needlealtlong", needleSign * alt / altLongPerTurn * 360);
        SetSpinFrame(anim, "needlealtshort", needleSign * alt / altShortPerTurn * 360);

        // Kompass: kortet dreier motsatt av kursen, så N alltid peker mot nord (−Z). Forover = −ViewVector
        var view = Pos.GetViewVector();
        double heading = Math.Atan2(-view.X, view.Z) * GameMath.RAD2DEG; // 0 = nord, 90 = øst
        SetSpinFrame(anim, "compasscard", -needleSign * heading);

        // Lamper: STALL blinker, FUEL lyser under grensa og blinker når tanken er tom
        bool blink = (Api.World.ElapsedMilliseconds / 250) % 2 == 0;
        SetLamp(anim, "lampstall", Flying && Stalling && blink);
        SetLamp(anim, "lampfuel", fuel < lowFuelFraction && (fuel > 0 || blink));
    }

    static void SetLamp(IAnimationManager anim, string code, bool on)
    {
        if (!anim.IsAnimationActive(code)) anim.StartAnimation(code);
        var state = anim.GetAnimationState(code);
        if (state == null) return;
        state.CurrentFrame = on ? 30 : 0;
        state.BlendedWeight = 1;
        state.EasingFactor = 1;
    }

    static void SetSpinFrame(IAnimationManager anim, string code, double angle)
    {
        if (!anim.IsAnimationActive(code)) anim.StartAnimation(code);
        var state = anim.GetAnimationState(code);
        if (state == null) return;
        state.CurrentFrame = (float)(GameMath.Mod(angle, 360) / 360 * 30);
        state.BlendedWeight = 1;
        state.EasingFactor = 1;
    }

    /// <summary>
    /// Ingen vannsprut fra båt-klassen. I stedet: sot og røyk fra eksosrørene når motoren går.
    /// </summary>
    public override void OnAsyncParticleTick(float dt, IAsyncParticleManager manager)
    {
        double throttle = EngineThrottle;
        if (throttle <= 0) return;

        smoke ??= new SimpleParticleProperties(
            1, 1, ColorUtil.ToRgba(120, 40, 38, 36), new Vec3d(), new Vec3d(0.08, 0.05, 0.08),
            new Vec3f(-0.15f, 0.4f, -0.15f), new Vec3f(0.15f, 0.9f, 0.15f), 2.5f, -0.02f, 0.4f, 0.9f, EnumParticleModel.Quad)
        {
            SelfPropelled = true,
            WindAffectednes = 0.6f,
            OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -100),
            SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, 1.2f)
        };

        // Forover = motsatt synsvektoren (flyet kjører pos.GetViewVector() * -fart). Høyre = forover dreid 90°
        var view = Pos.GetViewVector();
        double fx = -view.X, fz = -view.Z, len = Math.Sqrt(fx * fx + fz * fz);
        if (len < 1e-6) return;
        fx /= len; fz /= len;
        double rx = -fz, rz = fx;

        // Mer gass = tettere og mørkere røyk. Fartsvinden tar røyken bakover
        float qty = (float)(0.3 + 1.2 * throttle);
        smoke.MinQuantity = qty;
        smoke.AddQuantity = qty;
        smoke.Color = ColorUtil.ToRgba((int)(80 + 90 * throttle), 35, 33, 31);
        var drift = Pos.Motion;
        foreach (var e in exhausts)
        {
            smoke.MinPos.Set(Pos.X + fx * e.X + rx * e.Z - 0.04, Pos.Y + e.Y, Pos.Z + fz * e.X + rz * e.Z - 0.04);
            smoke.MinVelocity.Set((float)(drift.X * 20) - 0.15f, 0.4f, (float)(drift.Z * 20) - 0.15f);
            manager.Spawn(smoke);
        }
    }
}
