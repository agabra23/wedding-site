# All documents live in a single Cosmos partition

Every document (Party documents, which embed their Guests, and the single Settings document) shares one constant partition key value. The guest list is a few hundred records, far below a logical partition's size limit, and keeping everything in one partition lets us save atomically across Parties (moving a Guest between Parties is one transactional batch), check unique names and look up names with single-partition queries, and store Settings next to the data it governs. Partitioning by Party ID would have made moving a Guest two separate writes that can't be made atomic.

## Consequences

- The partition key looks pointless, because it never varies. That's deliberate; don't "fix" it.
- A container's partition key can't be changed in place, so the prod and dev `parties` containers were recreated (while still empty) when we made this switch.
- The unique-name check reads the whole Guest List, but it isn't atomic across Parties: two concurrent adds or renames could create a duplicate. We accept this because only Admins write names, so we don't use name-claim documents.
