using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Wright;

/// <summary>
/// Flyet. Arver seter, sving og kollisjon fra vanilla-båten, men har egen fart- og løftmodell.
///
/// Enheter: fart er i blokker per 1/60 sekund (samme som Entity.Pos.Motion), så 0.25 = 15 blokker/s.
/// Kontroller: W/S = gass opp/ned (blir liggende), Space = nesa opp, Sprint = nesa ned, A/D = sving.
/// S på tomgang på bakken = brems.
/// </summary>
public class EntityWright : EntityBoat
{
    // Tuning — leses fra entity-JSON (attributes.flight), dette er fallbackene
    double maxSpeed = 0.28;      // toppfart ved full gass
    double stallSpeed = 0.15;    // under dette bærer ikke vingene flyet
    double accel = 0.15;         // hvor fort farten nærmer seg målfarten (per sekund) — lavt = seigt
    double throttleRate = 0.4;   // gass-endring per sekund med W/S holdt inne
    double brakeRate = 0.6;      // ekstra retardasjon på bakken med S på tomgang
    double climbRate = 0.05;     // stigning med Space ved full løft (blokker/tick)
    double diveRate = 0.08;      // synk med Sprint
    double maxSinkRate = 0.2;    // fritt fall uten løft
    double climbCost = 0.2;      // hvor mye fart stigning koster (og dykk gir)
    double verticalResponse = 3; // hvor fort vertikalfarten følger ønsket verdi

    public double Throttle;
    public bool Flying { get; private set; }

    public override bool ApplyGravity => !Flying;

    public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
    {
        base.Initialize(properties, api, InChunkIndex3d);

        var flight = properties.Attributes["flight"];
        if (flight.Exists)
        {
            maxSpeed = flight["maxSpeed"].AsDouble(maxSpeed);
            stallSpeed = flight["stallSpeed"].AsDouble(stallSpeed);
            accel = flight["accel"].AsDouble(accel);
            throttleRate = flight["throttleRate"].AsDouble(throttleRate);
            brakeRate = flight["brakeRate"].AsDouble(brakeRate);
            climbRate = flight["climbRate"].AsDouble(climbRate);
            diveRate = flight["diveRate"].AsDouble(diveRate);
            maxSinkRate = flight["maxSinkRate"].AsDouble(maxSinkRate);
            climbCost = flight["climbCost"].AsDouble(climbCost);
            verticalResponse = flight["verticalResponse"].AsDouble(verticalResponse);
        }
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

    protected override void updateBoatAngleAndMotion(float dt)
    {
        dt = Math.Min(0.5f, dt);

        // Vanilla-seteoppdateringen: setter Controller, animasjoner og gir oss sving (Y)
        var seatMotion = SeatsToMotion(GlobalConstants.PhysicsFrameTime);
        var controls = PilotControls();
        var pos = Pos;

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

        // --- Fart framover ---
        double targetSpeed = Throttle * maxSpeed;
        ForwardSpeed += (targetSpeed - ForwardSpeed) * accel * dt;
        if (OnGround && Throttle <= 0 && controls?.Backward == true)
        {
            ForwardSpeed = Math.Max(0, ForwardSpeed - brakeRate * maxSpeed * dt);
        }
        // Stigning spiser fart, dykk gir fart
        if (Flying) ForwardSpeed -= pos.Motion.Y * climbCost * dt;
        ForwardSpeed = Math.Max(0, ForwardSpeed);
        if (ForwardSpeed < 0.0005 && Throttle <= 0) ForwardSpeed = 0;

        var velocity = pos.GetViewVector().Mul((float)-ForwardSpeed).ToVec3d();
        pos.Motion.X = velocity.X;
        pos.Motion.Z = velocity.Z;

        // --- Løft ---
        double lift = LiftRatio;
        bool pullUp = controls?.Jump == true;
        bool pushDown = controls?.Sprint == true;

        // Vi tar over vertikalen når vingene bærer, eller når vi allerede er i lufta
        Flying = lift >= 1 ? (pullUp || !OnGround) : !OnGround && Flying;

        if (Flying)
        {
            double targetVy = 0;
            if (pullUp && lift >= 1) targetVy += climbRate * Math.Min(lift - 1, 1) + climbRate * 0.25;
            if (pushDown) targetVy -= diveRate;
            targetVy -= maxSinkRate * (1 - Math.Min(lift, 1)); // under stallfart synker vi

            pos.Motion.Y += (targetVy - pos.Motion.Y) * Math.Min(1, verticalResponse * dt);

            // Landet: gi tilbake tyngdekraften
            if (OnGround && pos.Motion.Y <= 0 && !pullUp) Flying = false;
        }

        // --- Sving (vanilla) ---
        AngularVelocity += (seatMotion.Y * SpeedMultiplier - AngularVelocity) * dt;
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
}
