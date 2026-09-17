namespace Dami.Gui;

/// <summary>A readable section of a reply, retaining the exact code payload.</summary>
public sealed record ReplyBlock(string Kind, string Text, string Language = "");
