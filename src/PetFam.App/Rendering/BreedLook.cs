using Avalonia.Media;
using PetFam.Core.Pets;

namespace PetFam.Desktop.Rendering;

public enum EarKind { CatPointed, CatSmall, CatFolded, DogFloppy, DogLongFloppy, DogErect }

public enum TailKind { CatLong, CatPlume, DogCurl, DogPlume, DogPompom, DogStub }

public enum FaceKind { Kitten, Flat, Button, Fox }

/// <summary>
/// How a breed is drawn: coat colours (RGB hex), cel-shading tints, proportions relative to the species' base body,
/// and the parts that make the breed recognisable (ears, face, tail, coat pattern).
/// </summary>
public sealed record BreedLook(
    uint Fur, uint Belly, uint Dark, uint Nose, uint EarInner, uint Paw, uint Saddle, uint Chest, uint Leg, uint Iris,
    (float R, float G, float B) Shade, (float R, float G, float B) StripeTint, uint Line,
    bool Stripes, EarKind Ears, TailKind Tail, FaceKind Face,
    float BodyLen = 1f, float BodyR = 1f, float Legs = 1f, float Head = 1f, float EarSize = 1f,
    uint? Point = null, bool Blaze = false, float HeadFluff = 0f)
{
    public static Color C(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static readonly (float, float, float) Cool = (0.90f, 0.92f, 0.98f);
    private static readonly (float, float, float) Warm = (0.98f, 0.86f, 0.80f);
    private static readonly (float, float, float) Plain = (1f, 1f, 1f);

    private static readonly Dictionary<string, BreedLook> Looks = new()
    {
        // ---------------------------------------------------------------- dogs
        ["pomeranian"] = new(0xEA953C, 0xFBE6C0, 0x5A3414, 0x1E1612, 0xF2B888, 0xF4D4A0, 0xB45E20, 0xFCE6BE, 0xF2C686, 0x3A2214,
            Warm, (0.88f, 0.66f, 0.46f), 0x6A3814, Stripes: false, EarKind.DogFloppy, TailKind.DogCurl, FaceKind.Button),
        ["maltese"] = new(0xF8F6F2, 0xFFFFFF, 0x3A3330, 0x1A1614, 0xF2DCD4, 0xFFFFFF, 0xF0ECE4, 0xFFFFFF, 0xF8F6F2, 0x2A1C14,
            Cool, Plain, 0x847A74, Stripes: false, EarKind.DogLongFloppy, TailKind.DogPlume, FaceKind.Button,
            BodyLen: 0.95f, Legs: 0.9f, HeadFluff: 0.3f),
        ["poodle"] = new(0xC9773E, 0xD98E54, 0x4A2412, 0x3A2014, 0xD08A5A, 0xC9773E, 0xB8682E, 0xD4864C, 0xC9773E, 0x3A2010,
            Warm, Plain, 0x5A2C10, Stripes: false, EarKind.DogLongFloppy, TailKind.DogPompom, FaceKind.Button,
            BodyLen: 0.9f, Legs: 1.15f, HeadFluff: 0.7f),
        ["bichon"] = new(0xFAF8F3, 0xFFFFFF, 0x2E2824, 0x161210, 0xF0DCD0, 0xFFFFFF, 0xF2EEE6, 0xFFFFFF, 0xFAF8F3, 0x241812,
            Cool, Plain, 0x847A74, Stripes: false, EarKind.DogFloppy, TailKind.DogPlume, FaceKind.Button,
            Head: 1.05f, EarSize: 0.85f, HeadFluff: 1f),
        ["shiba"] = new(0xD98A45, 0xF6E8D2, 0x4E2A12, 0x1E1612, 0xEAC8A8, 0xF6E8D2, 0xC47434, 0xF8ECD8, 0xE3A466, 0x2E1A0E,
            Warm, Plain, 0x6A3410, Stripes: false, EarKind.DogErect, TailKind.DogCurl, FaceKind.Fox),
        ["corgi"] = new(0xDD8B3E, 0xFFFDF8, 0x4E2A12, 0x1E1612, 0xF0C0A0, 0xFFFFFF, 0xC9752C, 0xFFFFFF, 0xFFFDF8, 0x3A2010,
            Warm, Plain, 0x6A3410, Stripes: false, EarKind.DogErect, TailKind.DogStub, FaceKind.Fox,
            BodyLen: 1.3f, BodyR: 1.05f, Legs: 0.55f, EarSize: 1.35f, Blaze: true),
        ["jindo"] = new(0xD9A55E, 0xF5E6CA, 0x4E3218, 0x241A14, 0xE8C8A0, 0xF2E2C4, 0xC89048, 0xF5E8D0, 0xE4BC80, 0x2E1C10,
            Warm, Plain, 0x6A4418, Stripes: false, EarKind.DogErect, TailKind.DogCurl, FaceKind.Fox,
            BodyLen: 1.05f, Legs: 1.15f),

        // ---------------------------------------------------------------- cats
        ["korean-shorthair"] = new(0xA9AFB8, 0xF3F3F1, 0x3E434C, 0xE08E98, 0xF2B4B6, 0xF3F3F1, 0x747B87, 0xF5F5F3, 0xA9AFB8, 0x4E7A34,
            Cool, (0.58f, 0.60f, 0.66f), 0x454A55, Stripes: true, EarKind.CatPointed, TailKind.CatLong, FaceKind.Kitten),
        ["russian-blue"] = new(0x8C98A8, 0xA9B3C0, 0x3A414C, 0x6E7684, 0xB8A6B8, 0x9CA6B4, 0x808C9C, 0xA2ACBA, 0x8C98A8, 0x3FA34E,
            (0.90f, 0.93f, 1.0f), Plain, 0x3E4652, Stripes: false, EarKind.CatPointed, TailKind.CatLong, FaceKind.Kitten,
            BodyR: 0.92f, Legs: 1.1f, EarSize: 1.15f),
        ["british-shorthair"] = new(0x7F8A99, 0x98A2B0, 0x383E48, 0x5E6672, 0xB0A0AE, 0x8C96A4, 0x77818F, 0x8E98A6, 0x7F8A99, 0xD9871C,
            Cool, Plain, 0x383E48, Stripes: false, EarKind.CatSmall, TailKind.CatLong, FaceKind.Kitten,
            BodyR: 1.15f, Legs: 0.9f, Head: 1.12f),
        ["scottish-fold"] = new(0xE9CBA0, 0xFFF8EE, 0x7A5232, 0xE9A0A0, 0xF0C0B0, 0xFFF8EE, 0xD9AE78, 0xFFF8EE, 0xE9CBA0, 0xD99A2B,
            Warm, (0.88f, 0.78f, 0.66f), 0x6E4A2E, Stripes: true, EarKind.CatFolded, TailKind.CatLong, FaceKind.Kitten,
            Head: 1.08f),
        ["persian"] = new(0xF4F1EC, 0xFFFFFF, 0x6A6460, 0xEAA2A8, 0xF4C4C8, 0xFAF8F4, 0xECE6DE, 0xFFFFFF, 0xF4F1EC, 0x4F86D6,
            (0.92f, 0.93f, 0.98f), Plain, 0x7A7470, Stripes: false, EarKind.CatSmall, TailKind.CatPlume, FaceKind.Flat,
            BodyR: 1.1f, Legs: 0.85f, HeadFluff: 0.35f),
        ["ragdoll"] = new(0xF1E9DA, 0xFFFDF8, 0x4E3A30, 0xD9A0A0, 0xE2B8B0, 0xFFFFFF, 0xE4D6C0, 0xFFFFFF, 0xF1E9DA, 0x4C8FE0,
            Warm, Plain, 0x5E4638, Stripes: false, EarKind.CatPointed, TailKind.CatPlume, FaceKind.Kitten,
            BodyLen: 1.1f, BodyR: 1.1f, Head: 1.05f, Point: 0x7A5A48),
        ["munchkin"] = new(0xF0B46A, 0xFFF4E2, 0x7A4A1E, 0xE8939A, 0xF4BCA8, 0xFFF4E2, 0xDB8A3E, 0xFFF6E8, 0xF0B46A, 0xD8A030,
            (0.98f, 0.88f, 0.82f), (0.85f, 0.66f, 0.48f), 0x7A4A20, Stripes: true, EarKind.CatPointed, TailKind.CatLong, FaceKind.Kitten,
            Legs: 0.55f),
    };

    public static BreedLook Of(Pet pet) => Of(pet.Breed);

    public static BreedLook Of(Breed breed) =>
        Looks.TryGetValue(breed.Id, out var look) ? look : Looks[Breed.Default(breed.Species).Id];
}
