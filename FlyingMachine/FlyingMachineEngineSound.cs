using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace FlyingMachine;

/// <summary>
/// Motorlyden (bare klient). Følger EngineThrottle, som alle klienter ser likt — piloten, passasjeren og tilskuere.
///
///   Av → på:  startlyden spilles (sveiv + tenning), tomgangsløkka toner inn mot slutten av den.
///   Går:      tomgangsløkka (ping-pong, 20 s) — tonehøyde og volum følger gassen.
///   På → av:  løkka spinner ned (tonehøyde og volum synker), så stopp.
///   Lite drivstoff: hosting med tilfeldige mellomrom, oftere jo tommere; løkka dupper samtidig.
///
/// Tuning i entity-JSON: attributes.sound.
/// </summary>
public class FlyingMachineEngineSound : IDisposable
{
    readonly ICoreClientAPI capi;
    readonly EntityFlyingMachine plane;

    // Tuning (attributes.sound)
    float pitchIdle = 1f, pitchFull = 1.5f;
    float volIdle = 0.5f, volFull = 1f;
    float startVolume = 1f, coughVolume = 1f;
    float range = 64f;
    float startBlend = 1.5f;       // lengden på kryssfadingen startlyd → løkke (sekunder)
    float startTail = 1f;          // uttoningen som er bakt inn i slutten av start.ogg — kryssfadingen er ferdig før den
    float spinDownSeconds = 1.2f;
    float coughIntervalLow = 7f;   // sekunder mellom hosting ved grensa for lite drivstoff
    float coughIntervalEmpty = 1.5f; // … og nesten tom

    readonly ILoadedSound? idle;
    ILoadedSound? start;
    bool running;
    double runTime;       // sekunder siden motoren startet
    double startLength;   // lengden på startlyden (for inntoning av løkka)
    double spinDown;      // 1 → 0 mens motoren stopper
    double coughTimer;    // sekunder til neste hosting
    double coughDip;      // 1 = løkka helt nede, siger tilbake mot 0

    public FlyingMachineEngineSound(ICoreClientAPI capi, EntityFlyingMachine plane, JsonObject cfg)
    {
        this.capi = capi;
        this.plane = plane;
        if (cfg.Exists)
        {
            pitchIdle = cfg["pitchIdle"].AsFloat(pitchIdle);
            pitchFull = cfg["pitchFull"].AsFloat(pitchFull);
            volIdle = cfg["volumeIdle"].AsFloat(volIdle);
            volFull = cfg["volumeFull"].AsFloat(volFull);
            startVolume = cfg["volumeStart"].AsFloat(startVolume);
            coughVolume = cfg["volumeCough"].AsFloat(coughVolume);
            range = cfg["range"].AsFloat(range);
            startBlend = cfg["startBlendSeconds"].AsFloat(startBlend);
            startTail = cfg["startTailSeconds"].AsFloat(startTail);
            spinDownSeconds = cfg["spinDownSeconds"].AsFloat(spinDownSeconds);
            coughIntervalLow = cfg["coughIntervalLow"].AsFloat(coughIntervalLow);
            coughIntervalEmpty = cfg["coughIntervalEmpty"].AsFloat(coughIntervalEmpty);
        }

        idle = capi.World.LoadSound(new SoundParams
        {
            Location = new AssetLocation("flyingmachine", "sounds/engine/idle.ogg"),
            ShouldLoop = true,
            DisposeOnFinish = false,
            Position = SoundPos(),
            Range = range,
            SoundType = EnumSoundType.Entity,
            Volume = 0
        });
    }

    Vec3f SoundPos() => plane.Pos.XYZ.Add(0, 1.5, 0).ToVec3f();

    public void Update(float dt, double throttle, double fuelFraction, double lowFuelFraction)
    {
        if (idle == null) return;
        bool on = throttle > 0;
        var pos = SoundPos();

        if (on && !running) StartEngine(pos);
        else if (!on && running) { running = false; spinDown = 1; start?.FadeOutAndStop(0.3f); start = null; }
        if (start != null && (start.HasStopped || start.IsDisposed)) start = null;
        start?.SetPosition(pos);
        idle.SetPosition(pos);

        if (running)
        {
            runTime += dt;
            // Løkka toner inn mens startlyden går over i jevn gange
            // Equal-power kryssfading (sin/cos): summen holder seg jevn, ingen dupp midt i overgangen.
            // Ferdig før startlydens egen uttoning, så løkka har tatt over helt når startlyden stilner
            double blendEnd = startLength - startTail;
            double x = startLength > 0 && startBlend > 0
                ? GameMath.Clamp((runTime - (blendEnd - startBlend)) / startBlend, 0, 1)
                : 1;
            double fadeIn = Math.Sin(x * Math.PI / 2);
            if (start != null)
            {
                start.SetVolume(startVolume * (float)Math.Cos(x * Math.PI / 2));
                if (x >= 1) { start.Stop(); if (!start.IsDisposed) start.Dispose(); start = null; }
            }
            UpdateCough(dt, fuelFraction, lowFuelFraction, pos);
            float t = (float)GameMath.Clamp(throttle, 0, 1);
            float pitch = GameMath.Lerp(pitchIdle, pitchFull, t);
            float vol = GameMath.Lerp(volIdle, volFull, t) * (float)fadeIn * (float)(1 - 0.75 * coughDip);
            idle.SetPitch(pitch * (float)(1 - 0.1 * coughDip));
            idle.SetVolume(vol);
            start?.SetPitch(pitch);
        }
        else if (spinDown > 0)
        {
            spinDown = Math.Max(0, spinDown - dt / spinDownSeconds);
            idle.SetPitch(GameMath.Lerp(pitchIdle * 0.5f, pitchIdle, (float)spinDown));
            idle.SetVolume(volIdle * (float)(spinDown * spinDown));
            if (spinDown <= 0) idle.Stop();
        }
    }

    void StartEngine(Vec3f pos)
    {
        running = true;
        runTime = 0;
        coughDip = 0;
        coughTimer = coughIntervalLow * (0.5 + capi.World.Rand.NextDouble());

        // Startlyden bare hvis motoren sto helt stille — spinner den fortsatt ned, tar den bare opp igjen
        startLength = 0;
        if (spinDown <= 0)
        {
            start = capi.World.LoadSound(new SoundParams
            {
                Location = new AssetLocation("flyingmachine", "sounds/engine/start.ogg"),
                ShouldLoop = false,
                DisposeOnFinish = true,
                Position = pos,
                Range = range,
                SoundType = EnumSoundType.Entity,
                Volume = startVolume
            });
            if (start != null)
            {
                start.Start();
                startLength = start.SoundLengthSeconds;
            }
        }
        spinDown = 0;
        if (!idle!.IsPlaying) { idle.SetVolume(0); idle.Start(); }
    }

    void UpdateCough(float dt, double fuelFraction, double lowFuelFraction, Vec3f pos)
    {
        coughDip = Math.Max(0, coughDip - dt / 0.5);
        if (fuelFraction >= lowFuelFraction || lowFuelFraction <= 0) return;
        if ((coughTimer -= dt) > 0) return;

        // Tommere tank = hyppigere hosting
        double empty = 1 - GameMath.Clamp(fuelFraction / lowFuelFraction, 0, 1);
        double interval = GameMath.Lerp(coughIntervalLow, coughIntervalEmpty, (float)empty);
        coughTimer = interval * (0.6 + 0.8 * capi.World.Rand.NextDouble());
        coughDip = 1;
        var cough = capi.World.LoadSound(new SoundParams
        {
            Location = new AssetLocation("flyingmachine", "sounds/engine/cough.ogg"),
            DisposeOnFinish = true,
            Position = pos,
            Range = range,
            SoundType = EnumSoundType.Entity,
            Volume = coughVolume,
            Pitch = 0.9f + 0.2f * (float)capi.World.Rand.NextDouble()
        });
        cough?.Start();
    }

    public void Dispose()
    {
        start?.Stop();
        start?.Dispose();
        idle?.Stop();
        idle?.Dispose();
    }
}
