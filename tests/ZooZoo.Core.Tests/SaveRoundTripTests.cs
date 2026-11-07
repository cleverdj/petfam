using ZooZoo.Core.Persistence;
using ZooZoo.Core.Pets;

namespace ZooZoo.Core.Tests;

public class SaveRoundTripTests
{
    [Fact]
    public void Pet_survives_save_and_restore()
    {
        var now = new DateTime(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);
        var rng = new Random(7);
        var pet = new Pet(PetSpecies.Dog, "콩이", now, rng) { CellId = "a:b", Position = new Core.Geometry.Vec2(100, 900) };
        pet.Needs.Hunger = 33f;
        pet.Bond.Feed(now);
        pet.Appearance.AddMud(0.6f);

        var path = Path.Combine(Path.GetTempPath(), $"zoozoo-test-{Guid.NewGuid():N}.json");
        try
        {
            SaveStore.Write(path, SaveStore.ToSave(new[] { pet }, now));
            var loaded = SaveStore.Read(path);
            Assert.NotNull(loaded);
            var restored = SaveStore.Restore(loaded!.Pets.Single(), now, rng);

            Assert.Equal(pet.Id, restored.Id);
            Assert.Equal("콩이", restored.Name);
            Assert.Equal(PetSpecies.Dog, restored.Species);
            Assert.Equal(33f, restored.Needs.Hunger);
            Assert.Equal(2f, restored.Bond.Value);
            Assert.Equal(0.6f, restored.Appearance.Mud);
            Assert.Equal("a:b", restored.CellId);
            Assert.Equal(100f, restored.Position.X);
            Assert.Equal(pet.Personality.Shyness, restored.Personality.Shyness);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Missing_or_corrupt_file_reads_as_null()
    {
        Assert.Null(SaveStore.Read(Path.Combine(Path.GetTempPath(), "does-not-exist.json")));

        var path = Path.Combine(Path.GetTempPath(), $"zoozoo-bad-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ not json");
        try
        {
            Assert.Null(SaveStore.Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
