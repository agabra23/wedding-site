# Local dev runs on a separate Cosmos account

Local development uses its own serverless Cosmos account (`cosmos-wedding-thegabras2027-dev`) with the same database and container names as prod, so the connection string is the only thing that differs. Previously dev used a `parties-dev` container inside the prod account, which meant the local API held the prod read-write key and one config slip could write to real guest data. Serverless costs cents at dev traffic; it can't be free tier because prod already holds the subscription's one free-tier account.

## Consequences

- In `Development`, the API throws at startup unless `CosmosConnection` points at `localhost`/`127.0.0.1` (the emulator) or `DevCosmosHost`. Pasting the prod connection string into user-secrets fails fast.
- Which database is used depends only on the environment name and the machine's user-secrets, never on the git branch.
- Tests run as `Testing` against the emulator, so the guard doesn't apply to them.
