namespace Dami.Host;

/// <summary>The runtime surface (D-005). Every response is rendered from durable state.</summary>
public static class RuntimeEndpoints
{
    /// <summary>Maps every endpoint. The CLI's verb families, as routes.</summary>
    public static void MapDamiRuntime(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        SessionEndpoints.Map(app);
        TurnEndpoints.Map(app);
        SurfacingEndpoints.Map(app);
        BeliefEndpoints.Map(app);
        ApprovalEndpoints.Map(app);
        EventEndpoints.Map(app);
        CorpusEndpoints.Map(app);
        FrontierEndpoints.Map(app);
        HealthDomainEndpoints.Map(app);
        TranscriptionEndpoints.Map(app);
        ToolProposalEndpoints.Map(app);
        TaskBoardEndpoints.Map(app);
    }
        DisclosureEndpoints.Map(app);
        DomainEndpoints.Map(app);
    }
}

/// <summary>One interactive turn.</summary>
/// <summary>One interactive turn. <paramref name="Augmented"/> retrieves locally and
/// lets the frontier answer on that context.</summary>
public sealed record TurnRequest(string Message, bool Frontier = false, bool Augmented = false);

/// <summary>A reaction to a surfacing.</summary>
public sealed record FeedbackRequest(string Verdict, string? Note);

/// <summary>Approve or deny one pending approval.</summary>
public sealed record ResolveRequest(bool Approve, string? Note);

/// <summary>Retract one belief.</summary>
public sealed record RetractRequest(string Reason);

/// <summary>Supersede one belief with a corrected statement.</summary>
public sealed record CorrectRequest(string Statement);

/// <summary>Append one observation.</summary>
public sealed record NoteRequest(string Body);

/// <summary>A question for the frontier or a brief draft.</summary>
public sealed record QuestionRequest(string Question);
