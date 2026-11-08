using Avalonia.Media;
using PetFam.Core.Pets;

namespace PetFam.Desktop.Rendering;

public enum EarKind { CatPointed, CatSmall, CatFolded, DogFloppy, DogLongFloppy, DogErect }

public enum TailKind { CatLong, CatPlume, DogCurl, DogPlume, DogPompom, DogStub, DogThin, DogFeather }

public enum FaceKind { Kitten, Flat, Button, Fox }

/// <summary>
/// The coat's shape, beyond its colour: a big neck ruff (Pomeranian, Husky), tight curls (Poodle, Bichon), long silky
/// hair falling to the floor (Maltese), a long-haired cat's ruff and britches (Persian, Maine Coon, Ragdoll), or a
/// retriever's feathering under the chest and belly.
/// </summary>
public enum CoatKind { Short, Fluffy, Curly, Silky, Long, Feathered }

/// <summary>
/// How a breed is drawn: coat colours (RGB hex), cel-shading tints, proportions relative to the species' base body,
/// and the parts that make the breed recognisable (ears, face, tail, coat pattern).
/// </summary>
public sealed record BreedLook(
    uint Fur, uint Belly, uint Dark, uint Nose, uint EarInner, uint Paw, uint Saddle, uint Chest, uint Leg, uint Iris,
    (float R, float G, float B) Shade, (float R, float G, float B) StripeTint, uint Line,
    bool Stripes, EarKind Ears, TailKind Tail, FaceKind Face,
    float BodyLen = 1f, float BodyR = 1f, float Legs = 1f, float Head = 1f, float EarSize = 1f,
    uint? Point = null, bool Blaze = false, float HeadFluff = 0f,
    bool EarTufts = false, uint? TailTip = null, bool Hairless = false,
    float Size = 1f, float Snout = 1f, CoatKind Coat = CoatKind.Short, bool PaleSnout = true, bool Cap = false,
    bool Spots = false, float Cheeks = 1f, bool PointLegs = false,
    float MuzzleW = 1f, float MuzzleH = 1f, float Jowls = 0f, bool Brows = false, uint? MuzzleColor = null,
    float EyeSize = 1f, uint? Bow = null)
{
    public static Color C(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static readonly (float, float, float) Cool = (0.90f, 0.92f, 0.98f);
    private static readonly (float, float, float) Warm = (0.98f, 0.86f, 0.80f);
    private static readonly (float, float, float) Plain = (1f, 1f, 1f);

    private static readonly Dictionary<string, BreedLook> Looks = new()
    {
        // ---------------------------------------------------------------- dogs
        ["pomeranian"] = new(0xEA953C, 0xFBE6C0, 0x5A3414, 0x1E1612, 0xF2B888, 0xF4D4A0, 0xB45E20, 0xFCE6BE, 0xF2C686, 0x3A2214,
            Warm, (0.88f, 0.66f, 0.46f), 0x6A3814, Stripes: false, EarKind.DogErect, TailKind.DogCurl, FaceKind.Fox,
            Size: 0.85f, EarSize: 0.7f, Coat: CoatKind.Fluffy, Snout: 0.8f, MuzzleW: 0.8f, MuzzleH: 0.8f, EyeSize: 1.05f),
        ["maltese"] = new(0xF8F6F2, 0xFFFFFF, 0x3A3330, 0x1A1614, 0xF2DCD4, 0xFFFFFF, 0xF0ECE4, 0xFFFFFF, 0xF8F6F2, 0x2A1C14,
            Cool, Plain, 0x847A74, Stripes: false, EarKind.DogLongFloppy, TailKind.DogPlume, FaceKind.Button,
            BodyLen: 0.95f, Legs: 0.9f, HeadFluff: 0.3f,
            Size: 0.85f, Coat: CoatKind.Silky, Snout: 0.8f, MuzzleW: 0.85f, MuzzleH: 0.85f, EyeSize: 1.05f, Bow: 0xF0508C),
        ["poodle"] = new(0xC9773E, 0xD98E54, 0x4A2412, 0x3A2014, 0xD08A5A, 0xC9773E, 0xB8682E, 0xD4864C, 0xC9773E, 0x3A2010,
            Warm, Plain, 0x5A2C10, Stripes: false, EarKind.DogLongFloppy, TailKind.DogPompom, FaceKind.Button,
            BodyLen: 0.9f, Legs: 1.15f, HeadFluff: 0.7f,
            Size: 0.95f, Coat: CoatKind.Curly, Snout: 1.1f, MuzzleW: 0.7f, MuzzleH: 0.75f, EyeSize: 1.05f),
        ["bichon"] = new(0xFAF8F3, 0xFFFFFF, 0x2E2824, 0x161210, 0xF0DCD0, 0xFFFFFF, 0xF2EEE6, 0xFFFFFF, 0xFAF8F3, 0x241812,
            Cool, Plain, 0x847A74, Stripes: false, EarKind.DogFloppy, TailKind.DogPlume, FaceKind.Button,
            Head: 1.05f, EarSize: 0.85f, HeadFluff: 1f,
            Size: 0.9f, Coat: CoatKind.Curly, Snout: 0.75f, MuzzleW: 0.9f, EyeSize: 1.05f),
        ["shiba"] = new(0xD98A45, 0xF6E8D2, 0x4E2A12, 0x1E1612, 0xEAC8A8, 0xF6E8D2, 0xC47434, 0xF8ECD8, 0xE3A466, 0x2E1A0E,
            Warm, Plain, 0x6A3410, Stripes: false, EarKind.DogErect, TailKind.DogCurl, FaceKind.Fox,
            Coat: CoatKind.Fluffy, MuzzleW: 0.85f, MuzzleH: 0.8f, Brows: true, EyeSize: 0.82f),
        ["corgi"] = new(0xDD8B3E, 0xFFFDF8, 0x4E2A12, 0x1E1612, 0xF0C0A0, 0xFFFFFF, 0xC9752C, 0xFFFFFF, 0xFFFDF8, 0x3A2010,
            Warm, Plain, 0x6A3410, Stripes: false, EarKind.DogErect, TailKind.DogStub, FaceKind.Fox,
            BodyLen: 1.3f, BodyR: 1.05f, Legs: 0.55f, EarSize: 1.35f, Blaze: true,
            Snout: 0.95f, MuzzleW: 0.85f, MuzzleH: 0.85f, EyeSize: 0.9f),
        ["golden-retriever"] = new(0xE0A85A, 0xF6DCAE, 0x5A3618, 0x2A1C14, 0xE8BC88, 0xF0CC94, 0xC98E44, 0xF4D8A8, 0xE6B470, 0x3A2412,
            Warm, Plain, 0x6E4418, Stripes: false, EarKind.DogFloppy, TailKind.DogFeather, FaceKind.Fox,
            BodyLen: 1.15f, BodyR: 1.1f, Legs: 1.15f, Head: 1.05f, EarSize: 1.1f,
            Size: 1.35f, Snout: 1.25f, Coat: CoatKind.Feathered, PaleSnout: false, MuzzleW: 1.15f, MuzzleH: 1.05f, Jowls: 0.25f, EyeSize: 1.08f),
        ["french-bulldog"] = new(0x5E4E44, 0xEDE6DE, 0x2A2220, 0x1E1A18, 0xC89A90, 0x6A5A50, 0x4A3C34, 0xF2EEE8, 0x5E4E44, 0x2E1E14,
            Cool, Plain, 0x2E2420, Stripes: false, EarKind.DogErect, TailKind.DogStub, FaceKind.Flat,
            BodyLen: 0.9f, BodyR: 1.15f, Legs: 0.8f, Head: 1.1f, EarSize: 1.5f,
            Size: 0.95f, Snout: 0.4f, MuzzleW: 1.4f, MuzzleH: 1.05f, Jowls: 0.55f, MuzzleColor: 0x2E2622, EyeSize: 1.1f),
        ["dachshund"] = new(0xC8622A, 0xD98A50, 0x4A2410, 0x2A1810, 0xD9906A, 0xC8622A, 0xA84E20, 0xD27A42, 0xC8622A, 0x3A1C0E,
            Warm, Plain, 0x5A2C10, Stripes: false, EarKind.DogLongFloppy, TailKind.DogThin, FaceKind.Fox,
            BodyLen: 1.45f, Legs: 0.5f, Head: 0.95f,
            Snout: 1.35f, PaleSnout: false, MuzzleW: 0.72f, MuzzleH: 0.75f, EyeSize: 1.05f),
        ["siberian-husky"] = new(0x8E949E, 0xF6F6F4, 0x2E3238, 0x1E1E20, 0xE2C8C4, 0xF6F6F4, 0x4E545E, 0xFAFAF8, 0xF2F2F0, 0x5AA2E8,
            Cool, Plain, 0x3A3E46, Stripes: false, EarKind.DogErect, TailKind.DogCurl, FaceKind.Fox,
            BodyLen: 1.1f, Legs: 1.15f, EarSize: 1.1f, Blaze: true,
            Size: 1.3f, Snout: 1.15f, Coat: CoatKind.Fluffy, Cap: true, MuzzleW: 0.9f, MuzzleH: 0.85f, Brows: true, EyeSize: 0.82f),
        ["chihuahua"] = new(0xE8C28E, 0xF6E2C4, 0x4A3220, 0x3A2A22, 0xF0B8A8, 0xF2D6AE, 0xD8A86C, 0xF6E0C0, 0xE8C28E, 0x2A1A10,
            Warm, Plain, 0x6A4A2A, Stripes: false, EarKind.DogErect, TailKind.DogThin, FaceKind.Button,
            BodyLen: 0.8f, BodyR: 0.85f, Legs: 0.95f, Head: 1.15f, EarSize: 1.6f,
            Size: 0.72f, Snout: 0.65f, MuzzleW: 0.7f, MuzzleH: 0.7f, EyeSize: 1.2f),
        ["beagle"] = new(0xD29550, 0xFFFDF8, 0x3A2414, 0x1E1612, 0xE0A880, 0xFFFFFF, 0x2E2622, 0xFFFFFF, 0xFFFDF8, 0x3A2010,
            Warm, Plain, 0x4A2C14, Stripes: false, EarKind.DogLongFloppy, TailKind.DogThin, FaceKind.Fox,
            BodyLen: 1.05f, Legs: 0.95f, Blaze: true, TailTip: 0xFFFFFF,
            Size: 1.05f, Snout: 1.15f, MuzzleW: 1.05f, Jowls: 0.45f, EyeSize: 1.05f),
        ["jindo"] = new(0xD9A55E, 0xF5E6CA, 0x4E3218, 0x241A14, 0xE8C8A0, 0xF2E2C4, 0xC89048, 0xF5E8D0, 0xE4BC80, 0x2E1C10,
            Warm, Plain, 0x6A4418, Stripes: false, EarKind.DogErect, TailKind.DogCurl, FaceKind.Fox,
            BodyLen: 1.05f, Legs: 1.15f,
            Size: 1.15f, Snout: 1.1f, Coat: CoatKind.Fluffy, MuzzleW: 0.9f, MuzzleH: 0.85f, Brows: true, EyeSize: 0.82f),

        // ---------------------------------------------------------------- cats
        ["korean-shorthair"] = new(0xA9AFB8, 0xF3F3F1, 0x3E434C, 0xE08E98, 0xF2B4B6, 0xF3F3F1, 0x747B87, 0xF5F5F3, 0xA9AFB8, 0x4E7A34,
            Cool, (0.58f, 0.60f, 0.66f), 0x454A55, Stripes: true, EarKind.CatPointed, TailKind.CatLong, FaceKind.Kitten),
        ["russian-blue"] = new(0x8C98A8, 0xA9B3C0, 0x3A414C, 0x6E7684, 0xB8A6B8, 0x9CA6B4, 0x808C9C, 0xA2ACBA, 0x8C98A8, 0x3FA34E,
            (0.90f, 0.93f, 1.0f), Plain, 0x3E4652, Stripes: false, EarKind.CatPointed, TailKind.CatLong, FaceKind.Kitten,
            BodyR: 0.92f, Legs: 1.1f, EarSize: 1.15f,
            Size: 0.95f, Snout: 1.05f),
        ["british-shorthair"] = new(0x7F8A99, 0x98A2B0, 0x383E48, 0x5E6672, 0xB0A0AE, 0x8C96A4, 0x77818F, 0x8E98A6, 0x7F8A99, 0xD9871C,
            Cool, Plain, 0x383E48, Stripes: false, EarKind.CatSmall, TailKind.CatLong, FaceKind.Kitten,
            BodyR: 1.15f, Legs: 0.9f, Head: 1.12f,
            Size: 1.1f, Cheeks: 1.35f, MuzzleW: 1.15f),
        ["scottish-fold"] = new(0xE9CBA0, 0xFFF8EE, 0x7A5232, 0xE9A0A0, 0xF0C0B0, 0xFFF8EE, 0xD9AE78, 0xFFF8EE, 0xE9CBA0, 0xD99A2B,
            Warm, (0.88f, 0.78f, 0.66f), 0x6E4A2E, Stripes: true, EarKind.CatFolded, TailKind.CatLong, FaceKind.Kitten,
            Head: 1.08f,
            Cheeks: 1.2f),
        ["persian"] = new(0xF4F1EC, 0xFFFFFF, 0x6A6460, 0xEAA2A8, 0xF4C4C8, 0xFAF8F4, 0xECE6DE, 0xFFFFFF, 0xF4F1EC, 0x4F86D6,
            (0.92f, 0.93f, 0.98f), Plain, 0x7A7470, Stripes: false, EarKind.CatSmall, TailKind.CatPlume, FaceKind.Flat,
            BodyR: 1.1f, Legs: 0.85f, HeadFluff: 0.35f,
            Size: 1.05f, Coat: CoatKind.Long, Cheeks: 1.2f),
        ["ragdoll"] = new(0xF1E9DA, 0xFFFDF8, 0x4E3A30, 0xD9A0A0, 0xE2B8B0, 0xFFFFFF, 0xE4D6C0, 0xFFFFFF, 0xF1E9DA, 0x4C8FE0,
            Warm, Plain, 0x5E4638, Stripes: false, EarKind.CatPointed, TailKind.CatPlume, FaceKind.Kitten,
            BodyLen: 1.1f, BodyR: 1.1f, Head: 1.05f, Point: 0x7A5A48,
            Size: 1.2f, Coat: CoatKind.Long, PointLegs: true),
        ["siamese"] = new(0xF2E6D2, 0xFBF5EA, 0x3A2A22, 0x6A4A42, 0xD8A8A0, 0x5A4034, 0xE6D6BC, 0xFBF5EA, 0xF2E6D2, 0x3E7FD8,
            Warm, Plain, 0x5A4034, Stripes: false, EarKind.CatPointed, TailKind.CatLong, FaceKind.Kitten,
            BodyR: 0.9f, Legs: 1.1f, EarSize: 1.25f, Point: 0x5A4034,
            Size: 0.95f, PointLegs: true, Snout: 1.3f, MuzzleW: 0.85f),
        ["maine-coon"] = new(0xB08454, 0xF2E6D6, 0x3E2A18, 0xC88078, 0xE8B8A0, 0xF2E6D6, 0x6E4A2C, 0xF4EADC, 0xB08454, 0xD9A23A,
            Warm, (0.62f, 0.5f, 0.4f), 0x4E3420, Stripes: true, EarKind.CatPointed, TailKind.CatPlume, FaceKind.Kitten,
            BodyLen: 1.2f, BodyR: 1.15f, Head: 1.1f, EarSize: 1.2f, HeadFluff: 0.3f, EarTufts: true,
            Size: 1.3f, Coat: CoatKind.Long, Snout: 1.1f, MuzzleW: 1.25f, MuzzleH: 1.15f),
        ["bengal"] = new(0xE2A85A, 0xF8E8C8, 0x3A2412, 0xC88070, 0xE8B090, 0xF6E4C4, 0x5A3A1E, 0xFAEED6, 0xE2A85A, 0x6DAA3A,
            Warm, (0.55f, 0.4f, 0.28f), 0x5A3418, Stripes: false, EarKind.CatPointed, TailKind.CatLong, FaceKind.Kitten,
            Legs: 1.1f, EarSize: 0.95f,
            Spots: true, Snout: 1.08f),
        ["sphynx"] = new(0xE8C2B0, 0xF2D2C4, 0x5A3A34, 0xD98A8A, 0xE9A9A0, 0xEECAB8, 0xDDB2A0, 0xF0CEC0, 0xE8C2B0, 0x4FA36A,
            Warm, Plain, 0x7A4E44, Stripes: false, EarKind.CatPointed, TailKind.CatLong, FaceKind.Kitten,
            BodyR: 0.9f, Legs: 1.05f, EarSize: 1.5f, Hairless: true,
            Size: 0.95f, Snout: 1.1f),
        ["abyssinian"] = new(0xC9874A, 0xE8C49A, 0x4A2A14, 0xC87870, 0xE0A890, 0xE6BC90, 0xA86A34, 0xEACCA4, 0xC9874A, 0xC9A030,
            Warm, Plain, 0x5A3418, Stripes: false, EarKind.CatPointed, TailKind.CatLong, FaceKind.Kitten,
            BodyR: 0.9f, Legs: 1.1f, EarSize: 1.3f, Snout: 1.12f),
        ["munchkin"] = new(0xF0B46A, 0xFFF4E2, 0x7A4A1E, 0xE8939A, 0xF4BCA8, 0xFFF4E2, 0xDB8A3E, 0xFFF6E8, 0xF0B46A, 0xD8A030,
            (0.98f, 0.88f, 0.82f), (0.85f, 0.66f, 0.48f), 0x7A4A20, Stripes: true, EarKind.CatPointed, TailKind.CatLong, FaceKind.Kitten,
            Legs: 0.55f,
            Size: 0.9f),
    };

    public static BreedLook Of(Pet pet) => Of(pet.Breed);

    public static BreedLook Of(Breed breed) =>
        Looks.TryGetValue(breed.Id, out var look) ? look : Looks[Breed.Default(breed.Species).Id];
}
