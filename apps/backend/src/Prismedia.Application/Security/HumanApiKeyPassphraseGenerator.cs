using System.Security.Cryptography;

namespace Prismedia.Application.Security;

/// <summary>
/// Generates human-enterable API keys as three short, lowercase, hyphen-separated
/// Prismedia-owned pseudo-words.
/// </summary>
public static class HumanApiKeyPassphraseGenerator {
    private static readonly string[] Onsets =
    [
        "b", "c", "d", "f", "g", "h", "j", "k", "l", "m", "n", "p", "r", "s", "t", "v",
        "w", "z", "br", "cr", "dr", "fr", "gr", "pr", "tr", "st", "sk", "sl", "cl", "fl", "gl", "pl"
    ];

    private static readonly string[] Rhymes =
    [
        "aba", "abe", "abi", "abo", "abu", "ada", "ade", "adi",
        "ado", "adu", "aga", "age", "agi", "ago", "agu", "aka",
        "ake", "aki", "ako", "aku", "ala", "ale", "ali", "alo",
        "alu", "ama", "ame", "ami", "amo", "amu", "ana", "ane",
        "ani", "ano", "anu", "apa", "ape", "api", "apo", "apu",
        "ara", "are", "ari", "aro", "aru", "asa", "ase", "asi",
        "aso", "asu", "ata", "ate", "ati", "ato", "atu", "ava",
        "ave", "avi", "avo", "avu", "aya", "ayo", "eza", "izo"
    ];

    /// <summary>All possible generated key words. The list size is exactly 2048.</summary>
    public static IReadOnlyList<string> Words { get; } =
        Onsets.SelectMany(onset => Rhymes.Select(rhyme => onset + rhyme)).Take(2048).ToArray();

    /// <summary>Generates a new three-word API key phrase.</summary>
    public static string Generate() =>
        string.Join('-', Enumerable.Range(0, 3).Select(_ => Words[RandomNumberGenerator.GetInt32(Words.Count)]));
}
