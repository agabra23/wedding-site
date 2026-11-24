/// <summary>The one place a <see cref="Refusal"/> becomes an HTTP response.</summary>
public static class RefusalResults
{
    public static IResult ToHttp(this Refusal refusal) => refusal switch
    {
        Refusal.NotFound => Results.NotFound(),
        Refusal.NameTaken t => Results.Conflict($"A guest named {t.FirstName} {t.LastName} already exists."),
        Refusal.DeadlinePassed => Results.Json("The RSVP deadline has passed.", statusCode: StatusCodes.Status403Forbidden),
        Refusal.GuestNotInParty => Results.BadRequest("A guest does not belong to this party."),
        Refusal.InvalidRsvp r => Results.BadRequest($"RSVP must be {OneOf(r.Allowed)}."),
        Refusal.BlankName => Results.BadRequest("Every guest needs a first and last name."),
        Refusal.ChangedElsewhere => Results.Conflict("This party was just changed elsewhere. Please try again."),
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, null),
    };

    static string OneOf(IReadOnlyList<string> options) =>
        options.Count == 1 ? options[0] : $"{string.Join(", ", options.SkipLast(1))} or {options[^1]}";
}
