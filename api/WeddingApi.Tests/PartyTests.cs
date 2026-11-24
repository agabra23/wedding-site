namespace WeddingApi.Tests;

/// <summary>The Party rules, in-process: no host, no emulator, no fake clock.</summary>
public class PartyTests
{
    static readonly DateTime Now = new(2027, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    static Party Seed(params string[] firstNames) => Party.New(firstNames.Select(f => Guest.New(f, "Smith")));

    static RsvpAnswer Answer(Guest g, string? rsvp) => new(g.Id, rsvp);

    // Guests answering

    [Fact]
    public void Answers_update_only_the_named_guests_and_stamp_them()
    {
        var p = Seed("A", "B", "C");
        var after = p.AnswerAsGuest([Answer(p.Guests[0], Rsvps.Attending), Answer(p.Guests[1], Rsvps.Declined)], Now, null).Value;

        Assert.Equal(p.Id, after.Id);
        Assert.Equal([Rsvps.Attending, Rsvps.Declined, Rsvps.Pending], after.Guests.Select(g => g.Rsvp));
        Assert.All(after.Guests.Take(2), g => Assert.Equal((Now, ChangedBy.Guest), (g.RsvpChangedAt, g.RsvpChangedBy)));
        Assert.Null(after.Guests[2].RsvpChangedAt);
        Assert.Null(after.Guests[2].RsvpChangedBy);
    }

    [Fact]
    public void An_existing_answer_can_be_changed()
    {
        var p = Seed("A");
        p = p.AnswerAsGuest([Answer(p.Guests[0], Rsvps.Attending)], Now, null).Value;
        p = p.AnswerAsGuest([Answer(p.Guests[0], Rsvps.Declined)], Now.AddHours(1), null).Value;
        Assert.Equal((Rsvps.Declined, Now.AddHours(1)), (p.Guests[0].Rsvp, p.Guests[0].RsvpChangedAt));
    }

    [Fact]
    public void When_a_guest_is_listed_twice_the_last_answer_wins()
    {
        var p = Seed("A");
        var after = p.AnswerAsGuest([Answer(p.Guests[0], Rsvps.Attending), Answer(p.Guests[0], Rsvps.Declined)], Now, null).Value;
        Assert.Equal(Rsvps.Declined, after.Guests[0].Rsvp);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("maybe")]
    [InlineData("")]
    [InlineData(null)]
    public void Guests_may_only_answer_attending_or_declined(string? value)
    {
        var p = Seed("A");
        var refusal = Assert.IsType<Refusal.InvalidRsvp>(p.AnswerAsGuest([Answer(p.Guests[0], value)], Now, null).Refusal);
        Assert.Equal([Rsvps.Attending, Rsvps.Declined], refusal.Allowed);
    }

    [Fact]
    public void A_guest_from_another_party_rejects_every_answer()
    {
        var p = Seed("A");
        var other = Seed("B");
        var result = p.AnswerAsGuest([Answer(p.Guests[0], Rsvps.Attending), Answer(other.Guests[0], Rsvps.Attending)], Now, null);
        Assert.IsType<Refusal.GuestNotInParty>(result.Refusal);
    }

    [Fact]
    public void An_answer_without_a_guest_is_refused()
    {
        var p = Seed("A");
        Assert.IsType<Refusal.GuestNotInParty>(p.AnswerAsGuest([new RsvpAnswer(null, Rsvps.Attending)], Now, null).Refusal);
    }

    [Fact]
    public void Answers_are_accepted_up_to_and_at_the_deadline()
    {
        var p = Seed("A");
        Assert.Null(p.AnswerAsGuest([Answer(p.Guests[0], Rsvps.Attending)], Now, Now.AddDays(1)).Refusal);
        Assert.Null(p.AnswerAsGuest([Answer(p.Guests[0], Rsvps.Attending)], Now, Now).Refusal);
    }

    [Fact]
    public void Answers_are_refused_after_the_deadline()
    {
        var p = Seed("A");
        var result = p.AnswerAsGuest([Answer(p.Guests[0], Rsvps.Attending)], Now, Now.AddTicks(-1));
        Assert.IsType<Refusal.DeadlinePassed>(result.Refusal);
    }

    // Admins setting an RSVP (there is no deadline to pass in)

    [Theory]
    [InlineData("attending")]
    [InlineData("declined")]
    [InlineData("pending")]
    public void Admin_can_set_any_status_and_it_is_stamped(string status)
    {
        var p = Seed("A", "B");
        var after = p.SetRsvpAsAdmin(p.Guests[0].Id, status, Now).Value;
        Assert.Equal((status, Now, ChangedBy.Admin), (after.Guests[0].Rsvp, after.Guests[0].RsvpChangedAt, after.Guests[0].RsvpChangedBy));
        Assert.Equal(p.Guests[1], after.Guests[1]);
    }

    [Theory]
    [InlineData("maybe")]
    [InlineData("")]
    [InlineData(null)]
    public void Admin_cannot_set_an_unknown_status(string? status)
    {
        var p = Seed("A");
        var refusal = Assert.IsType<Refusal.InvalidRsvp>(p.SetRsvpAsAdmin(p.Guests[0].Id, status, Now).Refusal);
        Assert.Equal([Rsvps.Pending, Rsvps.Attending, Rsvps.Declined], refusal.Allowed);
    }

    [Fact]
    public void Admin_setting_a_guest_outside_the_party_is_not_found()
    {
        Assert.IsType<Refusal.NotFound>(Seed("A").SetRsvpAsAdmin("nobody", Rsvps.Attending, Now).Refusal);
    }

    // Removing a Guest

    [Fact]
    public void Removing_a_guest_updates_the_party()
    {
        var p = Seed("A", "B");
        var change = Assert.IsType<PartyChange.Updated>(p.Remove(p.Guests[0].Id).Value);
        Assert.Equal(p.Id, change.Party.Id);
        Assert.Equal([p.Guests[1]], change.Party.Guests);
    }

    [Fact]
    public void Removing_the_last_guest_means_the_party_is_gone()
    {
        var p = Seed("A");
        var change = Assert.IsType<PartyChange.Gone>(p.Remove(p.Guests[0].Id).Value);
        Assert.Equal(p.Id, change.Party.Id);
    }

    [Fact]
    public void Removing_a_guest_outside_the_party_is_not_found()
    {
        Assert.IsType<Refusal.NotFound>(Seed("A").Remove("nobody").Refusal);
    }

    // Moving a Guest

    [Fact]
    public void Moving_into_another_party_keeps_the_guest_and_their_rsvp()
    {
        var a = Seed("A", "B");
        a = a.SetRsvpAsAdmin(a.Guests[0].Id, Rsvps.Attending, Now).Value;
        var b = Seed("C");

        var move = a.Move(a.Guests[0].Id, b).Value;

        var from = Assert.IsType<PartyChange.Updated>(move.From);
        Assert.Equal([a.Guests[1]], from.Party.Guests);
        var to = Assert.IsType<PartyChange.Updated>(move.To);
        Assert.Equal(b.Id, to.Party.Id);
        Assert.Equal([b.Guests[0], a.Guests[0]], to.Party.Guests);
    }

    [Fact]
    public void Moving_without_a_party_creates_a_new_one_for_the_guest()
    {
        var a = Seed("A", "B");
        var move = a.Move(a.Guests[0].Id, null).Value;

        Assert.IsType<PartyChange.Updated>(move.From);
        var to = Assert.IsType<PartyChange.Created>(move.To);
        Assert.NotEqual(a.Id, to.Party.Id);
        Assert.Equal([a.Guests[0]], to.Party.Guests);
    }

    [Fact]
    public void Moving_the_only_guest_means_the_old_party_is_gone()
    {
        var a = Seed("A");
        var move = a.Move(a.Guests[0].Id, Seed("B")).Value;
        Assert.Equal(a.Id, Assert.IsType<PartyChange.Gone>(move.From).Party.Id);
    }

    [Fact]
    public void Moving_to_the_current_party_changes_nothing()
    {
        var a = Seed("A", "B");
        var move = a.Move(a.Guests[0].Id, a).Value;
        Assert.Same(a, Assert.IsType<PartyChange.Unchanged>(move.From).Party);
        Assert.Same(a, Assert.IsType<PartyChange.Unchanged>(move.To).Party);
    }

    [Fact]
    public void Moving_a_guest_outside_the_party_is_not_found()
    {
        Assert.IsType<Refusal.NotFound>(Seed("A").Move("nobody", null).Refusal);
    }
}
