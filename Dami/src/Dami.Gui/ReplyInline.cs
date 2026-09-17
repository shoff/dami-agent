namespace Dami.Gui;

/// <summary>A selectable text span with a small, explicit set of display treatments.</summary>
public sealed record ReplyInline(string Text, string Kind = "text");
