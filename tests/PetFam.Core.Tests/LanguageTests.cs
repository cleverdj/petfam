using PetFam.Core.Pets;
using PetFam.Core.World;

namespace PetFam.Core.Tests;

/// <summary>Switches the global language, so it must not run alongside the other tests (they expect English).</summary>
[CollectionDefinition("Language", DisableParallelization = true)]
public sealed class LanguageCollection;

[Collection("Language")]
public class LanguageTests
{
    private static readonly DateTime T0 = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void English_is_the_default_and_Korean_is_an_option()
    {
        Assert.Equal(Language.English, L.Current);
        var pet = new Pet(PetSpecies.Dog, "Peanut", T0, new Random(1), Breed.Shiba);
        Assert.Equal("Shiba Inu", pet.Breed.Name.Text);
        Assert.Equal("2 months old", pet.Growth.AgeLabel(pet.Species));
        Assert.Equal("Baby", Growth.StageName(LifeStage.Baby));

        try
        {
            L.Set(Language.Korean);
            Assert.Equal("시바견", pet.Breed.Name.Text);
            Assert.Equal("생후 2개월", pet.Growth.AgeLabel(pet.Species));
            Assert.Equal("새끼", Growth.StageName(LifeStage.Baby));
            Assert.Equal("서울 · 폭우 10°C", WeatherNow.FromWmo(65, 10f, true, "서울", T0).Describe());
        }
        finally
        {
            L.Set(Language.English);
        }
        Assert.Equal("Heavy rain 10°C", WeatherNow.FromWmo(65, 10f, true, "", T0).Describe());
    }

    [Fact]
    public void Babies_mangle_their_words_in_both_languages()
    {
        var kitten = new Pet(PetSpecies.Cat, "Momo", T0, new Random(1));
        kitten.Say("Meow!");
        Assert.Equal("Mew!", kitten.Bubble);
        var puppy = new Pet(PetSpecies.Dog, "Peanut", T0, new Random(1));
        puppy.Say("Woof woof!");
        Assert.Equal("Yip yip!", puppy.Bubble);
        puppy.Say("왈!");
        Assert.Equal("앙!", puppy.Bubble);
    }

    [Fact]
    public void Language_codes_round_trip()
    {
        Assert.Equal(Language.Korean, L.Parse("ko"));
        Assert.Equal(Language.Korean, L.Parse("ko-KR"));
        Assert.Equal(Language.English, L.Parse("en"));
        Assert.Equal(Language.English, L.Parse(null));
        Assert.Equal("ko", L.Code(Language.Korean));
    }
}
