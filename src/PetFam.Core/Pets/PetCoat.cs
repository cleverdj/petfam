namespace PetFam.Core.Pets;

/// <summary>
/// Colours the user picked for a pet (RGB, 0xRRGGBB), over its breed's own: the main coat, the light parts (belly,
/// chest, paws), the markings (back saddle, tabby stripes, spots, colourpoints) and the eyes. Null: the breed's colour.
/// </summary>
public sealed record PetCoat(uint? Fur = null, uint? Light = null, uint? Marking = null, uint? Eyes = null)
{
    public bool IsEmpty => Fur is null && Light is null && Marking is null && Eyes is null;

    /// <summary>Null when nothing is set, so "back to the breed's colours" stores nothing.</summary>
    public PetCoat? OrNull() => IsEmpty ? null : this;
}
