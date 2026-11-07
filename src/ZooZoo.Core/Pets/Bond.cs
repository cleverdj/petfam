namespace ZooZoo.Core.Pets;

public enum BondStage
{
    Stranger,   // 0–14  낯섦
    Wary,       // 15–29 경계
    Curious,    // 30–49 호기심
    Familiar,   // 50–69 친숙
    Trusting,   // 70–89 신뢰
    Family,     // 90–100 가족
}

/// <summary>
/// Bond (유대감) 0..100, plan §4.3.
/// Rises through interactions; decays only after 24h without contact. Never punishes beyond "drifting away".
/// </summary>
public sealed class Bond
{
    public const int DailyPetCap = 10;

    public float Value { get; private set; }
    public DateTime LastInteractionUtc { get; private set; }
    public DateTime LastDecayCheckUtc { get; private set; }
    public DateTime LastDailyPresenceUtc { get; private set; }

    /// <summary>Date (UTC) of the current petting counter.</summary>
    public DateOnly PetCounterDate { get; private set; }
    public int PetCountToday { get; private set; }

    public Bond(DateTime nowUtc, float initial = 0f)
    {
        Value = Clamp(initial);
        LastInteractionUtc = nowUtc;
        LastDecayCheckUtc = nowUtc;
        LastDailyPresenceUtc = DateTime.MinValue;
        PetCounterDate = DateOnly.FromDateTime(nowUtc);
    }

    public BondStage Stage => Value switch
    {
        < 15 => BondStage.Stranger,
        < 30 => BondStage.Wary,
        < 50 => BondStage.Curious,
        < 70 => BondStage.Familiar,
        < 90 => BondStage.Trusting,
        _ => BondStage.Family,
    };

    public void Feed(DateTime nowUtc) => Add(2f, nowUtc);

    public void Play(DateTime nowUtc) => Add(2f, nowUtc);

    public void ReactToPeek(DateTime nowUtc) => Add(1f, nowUtc);

    public void Wash(DateTime nowUtc) => Add(3f, nowUtc);

    /// <summary>Petting: +1 per stroke, capped per day so it cannot be farmed.</summary>
    public bool Pet(DateTime nowUtc)
    {
        var today = DateOnly.FromDateTime(nowUtc);
        if (today != PetCounterDate)
        {
            PetCounterDate = today;
            PetCountToday = 0;
        }

        Touch(nowUtc);
        if (PetCountToday >= DailyPetCap)
            return false;

        PetCountToday++;
        Value = Clamp(Value + 1f);
        return true;
    }

    /// <summary>
    /// Called every tick. Handles "+1 for showing up once a day" and the slow decay after neglect.
    /// Merely being present stops the decay (plan: 압박 금지).
    /// </summary>
    public void Tick(DateTime nowUtc, bool userPresent)
    {
        if (userPresent)
        {
            if (nowUtc - LastDailyPresenceUtc >= TimeSpan.FromHours(20))
            {
                LastDailyPresenceUtc = nowUtc;
                Value = Clamp(Value + 1f);
            }

            // Presence counts as light contact: it freezes decay but does not add bond.
            LastInteractionUtc = nowUtc;
        }

        var sinceContact = nowUtc - LastInteractionUtc;
        if (sinceContact < TimeSpan.FromHours(24))
        {
            LastDecayCheckUtc = nowUtc;
            return;
        }

        // Past 24h: -1/day for the first day of neglect, -3/day afterwards.
        // Decay only counts time beyond the 24h grace period.
        var graceEnd = LastInteractionUtc + TimeSpan.FromHours(24);
        var decayFrom = LastDecayCheckUtc > graceEnd ? LastDecayCheckUtc : graceEnd;
        var elapsed = nowUtc - decayFrom;
        if (elapsed <= TimeSpan.Zero)
        {
            LastDecayCheckUtc = nowUtc;
            return;
        }

        var perDay = sinceContact < TimeSpan.FromHours(48) ? 1f : 3f;
        Value = Clamp(Value - perDay * (float)elapsed.TotalDays);
        LastDecayCheckUtc = nowUtc;
    }

    /// <summary>Test/debug only: sets the bond directly and counts it as fresh contact so it does not decay right away.</summary>
    public void DebugSet(float value, DateTime nowUtc)
    {
        Value = Clamp(value);
        Touch(nowUtc);
    }

    public void Restore(float value, DateTime lastInteractionUtc, DateTime lastDailyPresenceUtc, DateOnly petCounterDate, int petCountToday)
    {
        Value = Clamp(value);
        LastInteractionUtc = lastInteractionUtc;
        LastDecayCheckUtc = lastInteractionUtc;
        LastDailyPresenceUtc = lastDailyPresenceUtc;
        PetCounterDate = petCounterDate;
        PetCountToday = petCountToday;
    }

    private void Add(float amount, DateTime nowUtc)
    {
        Touch(nowUtc);
        Value = Clamp(Value + amount);
    }

    private void Touch(DateTime nowUtc)
    {
        LastInteractionUtc = nowUtc;
        LastDecayCheckUtc = nowUtc;
    }

    private static float Clamp(float v) => System.Math.Clamp(v, 0f, 100f);
}
