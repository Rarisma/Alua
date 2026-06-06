namespace Alua.Services.ViewModels;

/// <summary>How a library card shows achievement progress.</summary>
public enum CardProgressStyle
{
    /// A thin progress bar (default).
    Bar,
    /// A filled-background indicator behind the card content.
    FilledBackground,
    /// No progress indicator.
    None
}

/// <summary>Horizontal alignment of the text on a library card.</summary>
public enum CardTextAlignment
{
    Left,
    Center,
    Right
}

/// <summary>How a merged (multi-edition) card reports its completion.</summary>
public enum MergedCompletionMode
{
    /// Show the most-progressed edition's completion (the group's representative).
    Best,
    /// Aggregate completion across all editions in the group.
    Aggregate
}
