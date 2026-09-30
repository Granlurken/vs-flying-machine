using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Wright;

/// <summary>
/// Hjulbrems-lyden (bare klient): treklosser mot hjulene. Løkke som spiller mens bremsen er på og flyet ruller;
/// volum og tonehøyde følger farten, så lyden dør ut når flyet stanser. Slipper du, toner den kjapt ut.
///
/// Tuning i entity-JSON: attributes.sound.brake.
/// </summary>
public class WrightBrakeSound : IDisposable
{
    readonly EntityWright plane;

    string location = "game:sounds/effect/woodgrind.ogg";
    float volume = 0.8f;
    float pitchSlow = 0.7f, pitchFast = 1.1f;
    float fullSpeed = 8f;      // blokker/s der lyden er på sitt sterkeste
    float fadeSeconds = 0.25f;
    float range = 32f;

    readonly ILoadedSound? loop;
    double level;              // 0..1, glattet av/på

    public WrightBrakeSound(ICoreClientAPI capi, EntityWright plane, JsonObject cfg)
    {
        this.plane = plane;
        if (cfg.Exists)
        {
            location = cfg["sound"].AsString(location);
            volume = cfg["volume"].AsFloat(volume);
            pitchSlow = cfg["pitchSlow"].AsFloat(pitchSlow);
            pitchFast = cfg["pitchFast"].AsFloat(pitchFast);
            fullSpeed = cfg["fullSpeed"].AsFloat(fullSpeed);
            fadeSeconds = cfg["fadeSeconds"].AsFloat(fadeSeconds);
            range = cfg["range"].AsFloat(range);
        }

        loop = capi.World.LoadSound(new SoundParams
        {
            Location = new AssetLocation(location),
            ShouldLoop = true,
            DisposeOnFinish = false,
            Position = SoundPos(),
            Range = range,
            SoundType = EnumSoundType.Entity,
            Volume = 0
        });
    }

    Vec3f SoundPos() => plane.Pos.XYZ.Add(0, 0.4, 0).ToVec3f(); // nede ved hjulene

    /// <param name="speed">Faktisk fart i blokker/s (glattet).</param>
    public void Update(float dt, bool braking, double speed)
    {
        if (loop == null) return;
        double speedFactor = GameMath.Clamp(speed / fullSpeed, 0, 1);
        double target = braking ? speedFactor : 0;
        double step = fadeSeconds > 0 ? dt / fadeSeconds : 1;
        level = target > level ? Math.Min(target, level + step) : Math.Max(target, level - step);

        if (level <= 0.001)
        {
            if (loop.IsPlaying) loop.Stop();
            return;
        }
        if (!loop.IsPlaying) { loop.SetVolume(0); loop.Start(); }
        loop.SetPosition(SoundPos());
        loop.SetVolume(volume * (float)level);
        loop.SetPitch(GameMath.Lerp(pitchSlow, pitchFast, (float)speedFactor));
    }

    public void Dispose()
    {
        loop?.Stop();
        loop?.Dispose();
    }
}
