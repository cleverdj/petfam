using PetFam.Core.Behavior.Behaviors;
using PetFam.Core.Persistence;
using PetFam.Core.Pets;
using PetFam.Core.World;

namespace PetFam.Core.Tests;

public class BreedTests
{
    private static readonly DateTime T0 = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Every_breed_has_a_full_voice_in_both_languages()
    {
        Assert.Equal(17, Breed.Of(PetSpecies.Dog).Count());
        Assert.Equal(16, Breed.Of(PetSpecies.Cat).Count());
        Assert.Equal(Breed.All.Count, Breed.All.Select(b => b.Id).Distinct().Count());
        foreach (var b in Breed.All)
        {
            var v = b.Voice;
            foreach (var lines in new[] { v.Greeting, v.Idle, v.Happy, v.Petted, v.Hungry, v.Sleepy, v.Held, v.Rain, v.Snow, v.Called })
            {
                Assert.NotEmpty(lines);
                Assert.All(lines, l => { Assert.False(string.IsNullOrWhiteSpace(l.En)); Assert.False(string.IsNullOrWhiteSpace(l.Ko)); });
            }
            Assert.NotEmpty(b.Names);
            Assert.False(string.IsNullOrWhiteSpace(b.Persona));
        }
    }

    [Fact]
    public void A_pet_is_born_with_its_breeds_temperament()
    {
        var rng = new Random(1);
        for (var i = 0; i < 20; i++)
        {
            var shiba = new Pet(PetSpecies.Dog, "Shiro", T0, rng, Breed.Shiba);
            var bichon = new Pet(PetSpecies.Dog, "Cotton", T0, rng, Breed.Bichon);
            Assert.True(shiba.Personality.Tidiness > bichon.Personality.Tidiness);
            Assert.True(bichon.Personality.Playfulness > shiba.Personality.Playfulness - 0.05f);
        }
        var blue = new Pet(PetSpecies.Cat, "Blue", T0, rng, Breed.RussianBlue);
        Assert.True(blue.Personality.Shyness >= 0.65f);
        Assert.Equal(TravelMode.Free, new Pet(PetSpecies.Dog, "Shiro", T0, rng, Breed.Shiba).Travel);       // independent
        Assert.Equal(TravelMode.Follow, new Pet(PetSpecies.Cat, "Dolly", T0, rng, Breed.Ragdoll).Travel); // dog-like cat
    }

    [Fact]
    public void Breeds_live_differently()
    {
        var rng = new Random(2);
        Pet Adult(Breed b)
        {
            var p = new Pet(b.Species, "x", T0, rng, b) { Growth = new Growth(T0, Growth.AdultAt) };
            return p;
        }
        var corgi = Adult(Breed.Corgi);
        var pom = Adult(Breed.Pomeranian);
        Assert.True(corgi.Traits.HungerDecay > pom.Traits.HungerDecay);   // the foodie
        Assert.True(corgi.Traits.Leap < pom.Traits.Leap);                  // short legs
        Assert.True(Adult(Breed.Persian).Traits.Speed < Adult(Breed.KoreanShorthair).Traits.Speed);

        // A Maltese hates even a drizzle; a Jindo shrugs off a shower.
        var drizzle = WeatherNow.Pretend(WeatherKind.Rain, 0.25f);
        Assert.True(ShelterBehavior.Dislike(Adult(Breed.Maltese), drizzle) > 0.2f);
        Assert.Equal(0f, ShelterBehavior.Dislike(Adult(Breed.Jindo), WeatherNow.Pretend(WeatherKind.Rain, 0.6f)));
    }

    [Fact]
    public void Some_breeds_warm_up_faster_than_others()
    {
        var maltese = new Pet(PetSpecies.Dog, "Cloud", T0, new Random(3), Breed.Maltese);
        var shiba = new Pet(PetSpecies.Dog, "Shiro", T0, new Random(3), Breed.Shiba);
        for (var i = 0; i < 5; i++)
        {
            maltese.Bond.Feed(T0);
            shiba.Bond.Feed(T0);
        }
        Assert.True(maltese.Bond.Value > shiba.Bond.Value);
    }

    [Fact]
    public void Each_breed_talks_in_its_own_voice()
    {
        var shiba = new Pet(PetSpecies.Dog, "Shiro", T0, new Random(4), Breed.Shiba) { Growth = new Growth(T0, Growth.AdultAt) };
        shiba.SayAs(v => v.Held, new Random(1));
        Assert.Contains(shiba.Bubble, Breed.Shiba.Voice.Held.Select(l => l.Text));
    }

    [Fact]
    public void The_breed_is_saved_and_old_pets_keep_their_look()
    {
        var pet = new Pet(PetSpecies.Cat, "Dolly", T0, new Random(5), Breed.Ragdoll);
        var save = SaveStore.ToSave(new[] { pet }, T0);
        Assert.Same(Breed.Ragdoll, SaveStore.Restore(save.Pets[0], T0, new Random(6)).Breed);

        save.Pets[0].Breed = null;                                 // a save from before breeds
        Assert.Same(Breed.KoreanShorthair, SaveStore.Restore(save.Pets[0], T0, new Random(6)).Breed);
        save.Pets[0].Species = PetSpecies.Dog;
        Assert.Same(Breed.Pomeranian, SaveStore.Restore(save.Pets[0], T0, new Random(6)).Breed);
    }
}
