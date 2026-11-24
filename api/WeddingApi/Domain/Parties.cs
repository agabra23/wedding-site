using System.Globalization;
using System.Text;

// The rules for Guests, Parties and RSVPs (see GLOSSARY.md). Nothing here touches HTTP, Cosmos or the clock:
// each operation takes the current state and returns either the new state or a Refusal.

public static class Rsvps
{
    public const string Pending = "pending";
    public const string Attending = "attending";
    public const string Declined = "declined";
}

/// <summary>Who last changed a Guest's RSVP.</summary>
public static class ChangedBy
{
    public const string Guest = "guest";
    public const string Admin = "admin";
}

public record Guest(
    string Id,
    string FirstName,
    string LastName,
    string FirstNameNormalized,
    string LastNameNormalized,
    string Rsvp,
    DateTime? RsvpChangedAt,
    string? RsvpChangedBy)
{
    /// <summary>A new, Pending Guest. Names are stored trimmed.</summary>
    public static Guest New(string firstName, string lastName)
    {
        var (first, last) = (firstName.Trim(), lastName.Trim());
        return new(NewId(), first, last, Names.Normalize(first), Names.Normalize(last), Rsvps.Pending, null, null);
    }

    internal Guest Renamed(string firstName, string lastName)
    {
        var (first, last) = (firstName.Trim(), lastName.Trim());
        return this with
        {
            FirstName = first,
            LastName = last,
            FirstNameNormalized = Names.Normalize(first),
            LastNameNormalized = Names.Normalize(last),
        };
    }

    internal bool HasSameNameAs(Guest other) =>
        FirstNameNormalized == other.FirstNameNormalized && LastNameNormalized == other.LastNameNormalized;

    internal Guest Answered(string rsvp, DateTime now, string by) =>
        this with { Rsvp = rsvp, RsvpChangedAt = now, RsvpChangedBy = by };

    internal static string NewId() => Guid.NewGuid().ToString("N");
}

public record Party(string Id, List<Guest> Guests)
{
    // ADR-0001: every document shares one partition key value
    public string Pk { get; init; } = Partitioning.Value;

    static readonly string[] GuestAnswers = [Rsvps.Attending, Rsvps.Declined];
    static readonly string[] AdminAnswers = [Rsvps.Pending, Rsvps.Attending, Rsvps.Declined];

    public static Party New(IEnumerable<Guest> guests) => new(Guest.NewId(), guests.ToList());

    /// <summary>
    /// Guests give or change RSVPs for anyone in their Party, until the RSVP Deadline passes (none means open).
    /// One bad answer refuses them all. If a Guest is answered twice, the last answer wins.
    /// </summary>
    public Result<Party> AnswerAsGuest(IReadOnlyCollection<RsvpAnswer> answers, DateTime now, DateTime? deadline)
    {
        if (answers.Any(a => !GuestAnswers.Contains(a.Rsvp)))
            return new Refusal.InvalidRsvp(GuestAnswers);
        if (deadline is { } d && now > d)
            return new Refusal.DeadlinePassed();
        if (answers.Any(a => a.GuestId is null || !Has(a.GuestId)))
            return new Refusal.GuestNotInParty();

        var byGuest = answers.GroupBy(a => a.GuestId!).ToDictionary(g => g.Key, g => g.Last().Rsvp!);
        return WithGuests(Guests.Select(g => byGuest.TryGetValue(g.Id, out var r) ? g.Answered(r, now, ChangedBy.Guest) : g));
    }

    /// <summary>Admins can set any Guest's RSVP, including back to Pending, whatever the RSVP Deadline.</summary>
    public Result<Party> SetRsvpAsAdmin(string guestId, string? rsvp, DateTime now)
    {
        if (!AdminAnswers.Contains(rsvp))
            return new Refusal.InvalidRsvp(AdminAnswers);
        if (!Has(guestId))
            return new Refusal.NotFound();
        return WithGuests(Guests.Select(g => g.Id == guestId ? g.Answered(rsvp!, now, ChangedBy.Admin) : g));
    }

    public Result<PartyChange> Remove(string guestId)
    {
        if (!Has(guestId)) return new Refusal.NotFound();
        return Without(guestId);
    }

    /// <summary>
    /// Moves a Guest, with their RSVP, into another Party, or into a new Party of their own when <paramref name="into"/> is null.
    /// </summary>
    public Result<GuestMove> Move(string guestId, Party? into)
    {
        if (!Has(guestId)) return new Refusal.NotFound();
        if (into?.Id == Id) return new GuestMove(new PartyChange.Unchanged(this), new PartyChange.Unchanged(this));

        var guest = Guests.Single(g => g.Id == guestId);
        PartyChange to = into is null
            ? new PartyChange.Created(New([guest]))
            : new PartyChange.Updated(into.WithGuests([.. into.Guests, guest]));
        return new GuestMove(Without(guestId), to);
    }

    internal bool Has(string guestId) => Guests.Any(g => g.Id == guestId);

    internal Party WithGuests(IEnumerable<Guest> guests) => this with { Guests = guests.ToList() };

    // A Party with no Guests ceases to exist
    PartyChange Without(string guestId)
    {
        var remaining = Guests.Where(g => g.Id != guestId).ToList();
        return remaining.Count == 0 ? new PartyChange.Gone(this) : new PartyChange.Updated(WithGuests(remaining));
    }
}

/// <summary>Every Guest, across all Parties. No two Guests share a full name.</summary>
public sealed class GuestList(IEnumerable<Party> parties)
{
    readonly List<Guest> guests = parties.SelectMany(p => p.Guests).ToList();

    /// <summary>Adds Guests into <paramref name="into"/>, or into a new Party when it is null.</summary>
    public Result<PartyChange> Add(IReadOnlyCollection<GuestName> names, Party? into)
    {
        if (names.Any(n => string.IsNullOrWhiteSpace(n.FirstName) || string.IsNullOrWhiteSpace(n.LastName)))
            return new Refusal.BlankName();

        var added = new List<Guest>();
        foreach (var guest in names.Select(n => Guest.New(n.FirstName!, n.LastName!)))
        {
            if (guests.Concat(added).Any(guest.HasSameNameAs))
                return new Refusal.NameTaken(guest.FirstName, guest.LastName);
            added.Add(guest);
        }

        return into is null
            ? new PartyChange.Created(Party.New(added))
            : new PartyChange.Updated(into.WithGuests([.. into.Guests, .. added]));
    }

    /// <summary>Renames a Guest in <paramref name="party"/>. A null name part is left as it is.</summary>
    public Result<Party> Rename(Party party, string guestId, string? firstName, string? lastName)
    {
        if (!party.Has(guestId)) return new Refusal.NotFound();
        if ((firstName is not null && string.IsNullOrWhiteSpace(firstName))
            || (lastName is not null && string.IsNullOrWhiteSpace(lastName)))
            return new Refusal.BlankName();

        var current = party.Guests.Single(g => g.Id == guestId);
        var renamed = current.Renamed(firstName ?? current.FirstName, lastName ?? current.LastName);
        if (guests.Any(g => g.Id != guestId && g.HasSameNameAs(renamed)))
            return new Refusal.NameTaken(renamed.FirstName, renamed.LastName);

        return party.WithGuests(party.Guests.Select(g => g.Id == guestId ? renamed : g));
    }
}

/// <summary>What happened to a Party document: the caller creates, replaces, deletes or leaves it.</summary>
public abstract record PartyChange(Party Party)
{
    public sealed record Created(Party Party) : PartyChange(Party);
    public sealed record Updated(Party Party) : PartyChange(Party);
    /// <summary>The Party as it was before its last Guest left; it ceases to exist.</summary>
    public sealed record Gone(Party Party) : PartyChange(Party);
    public sealed record Unchanged(Party Party) : PartyChange(Party);
}

public sealed record GuestMove(PartyChange From, PartyChange To);

/// <summary>Why an operation was refused. <see cref="RefusalResults"/> maps each one to an HTTP response.</summary>
public abstract record Refusal
{
    public sealed record NotFound : Refusal;
    public sealed record NameTaken(string FirstName, string LastName) : Refusal;
    public sealed record DeadlinePassed : Refusal;
    public sealed record GuestNotInParty : Refusal;
    public sealed record InvalidRsvp(IReadOnlyList<string> Allowed) : Refusal;
    public sealed record BlankName : Refusal;
}

/// <summary>Either a value or the <see cref="Refusal"/> that stopped it.</summary>
public readonly struct Result<T>
{
    readonly T? value;

    Result(T value) => this.value = value;
    Result(Refusal refusal) => Refusal = refusal;

    public Refusal? Refusal { get; }

    public T Value => Refusal is null ? value! : throw new InvalidOperationException($"Refused: {Refusal}");

    public static implicit operator Result<T>(T value) => new(value);
    public static implicit operator Result<T>(Refusal refusal) => new(refusal);
}

public static class Names
{
    /// <summary>Trims, ignores capitals and removes accents (ñ becomes n).</summary>
    public static string Normalize(string s)
    {
        var decomposed = s.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }
}
