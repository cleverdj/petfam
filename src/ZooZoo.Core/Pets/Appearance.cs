namespace ZooZoo.Core.Pets;

/// <summary>
/// Appearance states that stack on the base sprite (plan §4.5). Each is an intensity 0..1.
/// MVP: Wet and Mud. Sand/Snow/Dust/Grass arrive in v0.5.
/// </summary>
public sealed class Appearance
{
    public float Wet { get; private set; }
    public float Mud { get; private set; }

    /// <summary>True once mud has dried; dried mud flakes off on its own after a day.</summary>
    public bool MudDried { get; private set; }

    public const float DaytimeDrySeconds = 20 * 60;
    public const float NighttimeDrySeconds = 40 * 60;
    public const float DriedMudFlakeSeconds = 24 * 3600;

    private float _mudDryTimer;

    public bool IsClean => Wet <= 0.01f && Mud <= 0.01f;

    public void Tick(float dtSeconds, bool isDaytime, bool inSunZone)
    {
        if (Wet > 0f)
        {
            var seconds = isDaytime ? DaytimeDrySeconds : NighttimeDrySeconds;
            if (inSunZone) seconds *= 0.5f;
            Wet = System.Math.Max(0f, Wet - dtSeconds / seconds);
        }

        if (Mud > 0f)
        {
            if (!MudDried)
            {
                // Mud dries when the fur does.
                if (Wet <= 0.01f)
                {
                    _mudDryTimer += dtSeconds;
                    if (_mudDryTimer >= DaytimeDrySeconds) MudDried = true;
                }
            }
            else
            {
                Mud = System.Math.Max(0f, Mud - dtSeconds / DriedMudFlakeSeconds);
                if (Mud <= 0f) ResetMud();
            }
        }
    }

    /// <summary>Entering water: washes mud off, soaks the fur.</summary>
    public void EnterWater(float amount = 1f)
    {
        ResetMud();
        Wet = System.Math.Clamp(Wet + amount, 0f, 1f);
    }

    /// <summary>Rolling in mud, or lying on dirt while wet.</summary>
    public void AddMud(float amount)
    {
        Mud = System.Math.Clamp(Mud + amount, 0f, 1f);
        MudDried = false;
        _mudDryTimer = 0f;
    }

    /// <summary>Shake-off: halves wetness instantly.</summary>
    public void Shake() => Wet *= 0.5f;

    /// <summary>Towel: fully dries, removes half the mud.</summary>
    public void Towel()
    {
        Wet = 0f;
        Mud *= 0.5f;
        if (Mud <= 0.01f) ResetMud();
    }

    /// <summary>Grooming tick (cats): slowly removes mud.</summary>
    public void Groom(float dtSeconds)
    {
        Mud = System.Math.Max(0f, Mud - dtSeconds / 3600f);
        if (Mud <= 0f) ResetMud();
    }

    public void Restore(float wet, float mud, bool mudDried)
    {
        Wet = System.Math.Clamp(wet, 0f, 1f);
        Mud = System.Math.Clamp(mud, 0f, 1f);
        MudDried = mudDried;
    }

    private void ResetMud()
    {
        Mud = 0f;
        MudDried = false;
        _mudDryTimer = 0f;
    }
}
