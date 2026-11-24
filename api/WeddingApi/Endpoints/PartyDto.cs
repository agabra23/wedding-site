public record GuestDto(string Id, string FirstName, string LastName, string Rsvp, DateTime? RsvpChangedAt, string? RsvpChangedBy);

public record PartyDto(string Id, List<GuestDto> Guests)
{
    public static PartyDto From(Party p) => new(p.Id,
        p.Guests.Select(g => new GuestDto(g.Id, g.FirstName, g.LastName, g.Rsvp, g.RsvpChangedAt, g.RsvpChangedBy)).ToList());
}
