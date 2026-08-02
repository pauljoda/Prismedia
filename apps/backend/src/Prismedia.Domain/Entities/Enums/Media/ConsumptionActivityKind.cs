namespace Prismedia.Domain.Entities;

/// <summary>Closed set of active entity-consumption modes recorded in daily duration buckets.</summary>
public enum ConsumptionActivityKind {
    /// <summary>Time actively spent watching moving-image media.</summary>
    [Code("viewing")]
    Viewing,

    /// <summary>Time actively spent listening to audio media.</summary>
    [Code("listening")]
    Listening,

    /// <summary>Time actively spent reading text, pages, comics, EPUBs, or PDFs.</summary>
    [Code("reading")]
    Reading
}
