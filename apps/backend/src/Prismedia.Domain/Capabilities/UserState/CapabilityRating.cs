namespace Prismedia.Domain.Capabilities;

/// <summary>
/// Mutable rating capability for entities that support user ratings.
/// </summary>
public sealed class CapabilityRating : EntityCapability {
    /// <summary>
    /// Creates a rating capability.
    /// </summary>
    /// <param name="value">Optional zero-through-five rating value.</param>
    public CapabilityRating(int? value = null) {
        Value = value is null ? null : Scale.Normalize(value.Value);
    }

    /// <summary>
    /// Prismedia's shared zero-through-five rating scale and its normalization rule.
    /// </summary>
    public static class Scale {
        /// <summary>Lowest rating value supported by Prismedia.</summary>
        public const int MinValue = 0;

        /// <summary>Highest rating value supported by Prismedia.</summary>
        public const int MaxValue = 5;

        /// <summary>Clamps a value onto the supported zero-through-five scale.</summary>
        /// <param name="value">Raw rating value.</param>
        /// <returns>The clamped rating value.</returns>
        public static int Normalize(int value) => Math.Clamp(value, MinValue, MaxValue);
    }

    /// <summary>Current normalized rating value, or null when unrated.</summary>
    public int? Value { get; private set; }

    /// <summary>
    /// Sets the rating value.
    /// </summary>
    /// <param name="value">Rating value clamped onto the shared zero-through-five scale.</param>
    public void Rate(int value) {
        Value = Scale.Normalize(value);
    }

    /// <summary>Clears the current rating.</summary>
    public void Clear() {
        Value = null;
    }
}
