using System.Reflection;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Tests;

public sealed class CodecCompletenessTests {
    private static IEnumerable<Type> CodeBearingEnums() =>
        typeof(EntityKind).Assembly.GetTypes()
            .Where(type => type.IsEnum)
            .Where(type => type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Any(field => field.GetCustomAttribute<CodeAttribute>() is not null));

    [Fact]
    public void CodecEnumsAreDiscovered() {
        // Guards against the discovery query silently returning nothing.
        Assert.True(CodeBearingEnums().Count() >= 15);
    }

    [Fact]
    public void EveryCodeBearingEnumIsFullyAnnotatedAndRoundTrips() {
        foreach (var enumType in CodeBearingEnums()) {
            var fields = enumType.GetFields(BindingFlags.Public | BindingFlags.Static);

            // Self-documenting contract: if an enum opts into codes, every member must carry one.
            Assert.All(fields, field =>
                Assert.True(
                    field.GetCustomAttribute<CodeAttribute>() is not null,
                    $"{enumType.Name}.{field.Name} is missing a [Code] attribute."));

            Assert.True(CodecRegistry.TryGet(enumType, out var codec));

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in Enum.GetValues(enumType)) {
                var code = codec!.EncodeObject(value!);
                Assert.False(string.IsNullOrWhiteSpace(code));
                Assert.True(seen.Add(code), $"Duplicate code '{code}' on {enumType.Name}.");
                Assert.Equal(value, codec.DecodeObject(code));
            }
        }
    }
}
