namespace WeddingApi.Tests;

/// <summary>Adding and renaming Guests on the Guest List, in-process.</summary>
public class GuestListTests
{
    static GuestName Name(string? first, string? last) => new(first, last);

    static GuestList ListWith(params Party[] parties) => new(parties);

    // Adding

    [Fact]
    public void New_guests_are_pending_unstamped_and_stored_trimmed()
    {
        var change = Assert.IsType<PartyChange.Created>(ListWith().Add([Name("  Ada ", " Lovelace  ")], null).Value);
        var g = Assert.Single(change.Party.Guests);
        Assert.False(string.IsNullOrEmpty(g.Id));
        Assert.Equal(("Ada", "Lovelace", "ada", "lovelace"), (g.FirstName, g.LastName, g.FirstNameNormalized, g.LastNameNormalized));
        Assert.Equal(Rsvps.Pending, g.Rsvp);
        Assert.Null(g.RsvpChangedAt);
        Assert.Null(g.RsvpChangedBy);
    }

    [Fact]
    public void Several_guests_without_a_party_make_one_new_party()
    {
        var change = Assert.IsType<PartyChange.Created>(ListWith().Add([Name("A", "S"), Name("B", "S")], null).Value);
        Assert.False(string.IsNullOrEmpty(change.Party.Id));
        Assert.Equal(["A", "B"], change.Party.Guests.Select(g => g.FirstName));
        Assert.Equal(2, change.Party.Guests.Select(g => g.Id).Distinct().Count());
    }

    [Fact]
    public void Adding_into_a_party_appends_to_it()
    {
        var existing = Party.New([Guest.New("A", "S")]);
        var change = Assert.IsType<PartyChange.Updated>(ListWith(existing).Add([Name("B", "S"), Name("C", "S")], existing).Value);
        Assert.Equal(existing.Id, change.Party.Id);
        Assert.Equal(["A", "B", "C"], change.Party.Guests.Select(g => g.FirstName));
    }

    [Theory]
    [InlineData("  JOSE ", "SMITH ")]
    [InlineData("josé", "smith")]
    [InlineData("Jose", "Smith")]
    public void A_name_already_on_the_list_after_normalizing_is_taken(string first, string last)
    {
        var list = ListWith(Party.New([Guest.New("José", "Smith")]));
        var refusal = Assert.IsType<Refusal.NameTaken>(list.Add([Name("Other", "Smith"), Name(first, last)], null).Refusal);
        Assert.Equal((first.Trim(), last.Trim()), (refusal.FirstName, refusal.LastName));
    }

    [Fact]
    public void A_name_in_the_target_party_is_also_taken()
    {
        var existing = Party.New([Guest.New("Nuño", "S")]);
        Assert.IsType<Refusal.NameTaken>(ListWith(existing).Add([Name("nuno", "s")], existing).Refusal);
    }

    [Fact]
    public void The_same_name_twice_in_one_add_is_taken()
    {
        Assert.IsType<Refusal.NameTaken>(ListWith().Add([Name("Sam", "S"), Name(" sam", "S")], null).Refusal);
    }

    [Theory]
    [InlineData("", "Smith")]
    [InlineData("Ada", "  ")]
    [InlineData(null, "Smith")]
    [InlineData("Ada", null)]
    public void Every_guest_needs_a_first_and_last_name(string? first, string? last)
    {
        Assert.IsType<Refusal.BlankName>(ListWith().Add([Name("Good", "Name"), Name(first, last)], null).Refusal);
    }

    // Renaming

    static (GuestList List, Party Party) Seed()
    {
        var party = Party.New([Guest.New("Ann", "Smith"), Guest.New("Bob", "Smith")]);
        return (ListWith(party), party);
    }

    [Fact]
    public void Renaming_changes_only_the_given_part_and_stores_it_trimmed()
    {
        var (list, p) = Seed();
        var after = list.Rename(p, p.Guests[0].Id, "  Anna ", null).Value;
        var g = after.Guests[0];
        Assert.Equal(("Anna", "Smith", "anna", "smith"), (g.FirstName, g.LastName, g.FirstNameNormalized, g.LastNameNormalized));
        Assert.Equal(p.Guests[1], after.Guests[1]);
    }

    [Fact]
    public void Renaming_keeps_the_rsvp()
    {
        var (list, p) = Seed();
        p = p.SetRsvpAsAdmin(p.Guests[0].Id, Rsvps.Attending, DateTime.UtcNow).Value;
        var after = list.Rename(p, p.Guests[0].Id, null, "Jones").Value;
        Assert.Equal((p.Guests[0].Rsvp, p.Guests[0].RsvpChangedAt), (after.Guests[0].Rsvp, after.Guests[0].RsvpChangedAt));
    }

    [Fact]
    public void Renaming_to_another_guests_name_is_taken()
    {
        var (list, p) = Seed();
        var refusal = Assert.IsType<Refusal.NameTaken>(list.Rename(p, p.Guests[0].Id, "  BOB ", null).Refusal);
        Assert.Equal(("BOB", "Smith"), (refusal.FirstName, refusal.LastName));
    }

    [Fact]
    public void Renaming_to_a_name_in_another_party_is_taken()
    {
        var (_, p) = Seed();
        var other = Party.New([Guest.New("Cy", "Jones")]);
        Assert.IsType<Refusal.NameTaken>(ListWith(p, other).Rename(p, p.Guests[0].Id, "Cy", "Jones").Refusal);
    }

    [Theory]
    [InlineData("ANN")]
    [InlineData("Ánn")]
    public void Changing_only_capitals_or_accents_of_your_own_name_is_allowed(string first)
    {
        var (list, p) = Seed();
        Assert.Equal(first, list.Rename(p, p.Guests[0].Id, first, null).Value.Guests[0].FirstName);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData(null, "  ")]
    public void A_given_name_part_cannot_be_blank(string? first, string? last)
    {
        var (list, p) = Seed();
        Assert.IsType<Refusal.BlankName>(list.Rename(p, p.Guests[0].Id, first, last).Refusal);
    }

    [Fact]
    public void Renaming_a_guest_outside_the_party_is_not_found()
    {
        var (list, p) = Seed();
        Assert.IsType<Refusal.NotFound>(list.Rename(p, "nobody", "X", null).Refusal);
    }
}
