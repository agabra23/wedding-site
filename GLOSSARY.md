# Wedding RSVP

The guest list for the wedding and the guests' replies to their invitation.

## Language

**Guest**:
A single person invited to the wedding, identified to guests by a first name and last name that no other Guest shares. Every Guest belongs to exactly one Party.
_Avoid_: Invitee, attendee, person

**Party**:
A group of Guests who are invited together and may RSVP on each other's behalf.
_Avoid_: Household, invitation, group, family

**Guest List**:
Every Guest invited to the wedding, across all Parties. No two Guests on the Guest List share a full name.
_Avoid_: Invite list, roster

**RSVP**:
A Guest's reply to the invitation: attending or declining. Each Guest has their own RSVP, which any member of their Party may give or change.
_Avoid_: Response, reply

**Pending**:
The state of a Guest whose RSVP has not been given yet.
_Avoid_: Unanswered, no response

**RSVP Deadline**:
The moment, set by the Admins, after which Guests can no longer give or change RSVPs. Admins can still change them.
_Avoid_: Cutoff

**Event Password**:
The single shared secret the couple gives to everyone invited, which lets them into the site.
_Avoid_: Master password, site password

**Admin Password**:
The shared secret known only to the couple, which grants Admin access.
_Avoid_: Master password

**Admin**:
One of the couple, signed in with the Admin Password, who manages Guests, Parties and the RSVP Deadline.
_Avoid_: Owner, host

## Relationships

- A **Party** has one or more **Guests**; a **Guest** belongs to exactly one **Party**. A **Party** with no **Guests** ceases to exist
- A **Party** has no name of its own; it is just its **Guests**
- Each **Guest** has exactly one **RSVP**, which starts **Pending**
- There is a single event; an **RSVP** is to the wedding as a whole
- Finding any **Guest** by full name reveals their whole **Party**
