# Password transport and verification (issue #37)

Question: `POST /auth` sends the raw password the user typed in a JSON body. Is that secure, and is it how passwords are normally sent?

## Verdict

Yes. Sending the plaintext password in the body of a POST over TLS is the standard, recommended pattern. Hashing or encrypting it in the browser first adds no real security and can make things worse. The server-side comparison here (SHA-256 of both sides, then `CryptographicOperations.FixedTimeEquals`, generic 401, per-IP rate limit) is sound for a deliberate shared-secret gate (ADR-0002). The gaps are small hardening items, not a design flaw:

- No HSTS anywhere
- HTTPS-only is not stated explicitly in Terraform
- Rate limiting is per-IP only
- Failed attempts are not logged
- The shared passwords' strength is not enforced

Where a claim below is my own reasoning rather than a quote from a source, it is marked **(inference)**.

## Findings

### 1. Plaintext password in an HTTPS POST body is standard and secure

- OWASP: "The login page and all subsequent authenticated pages must be exclusively accessed over TLS or other strong transport." Without TLS, an attacker can change the form action and redirect submitted credentials. https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html (section "Transmit Passwords Only Over TLS or Other Strong Transport")
- NIST SP 800-63B: "Communication between the claimant and verifier SHALL occur via one or more authenticated protected channels." The protection it requires is the channel, not a client-side transform. https://pages.nist.gov/800-63-4/sp800-63b.html (Sec. 2, AAL1)
- Our `request()` in `src/api/client.ts` sends the password via `fetch` with a JSON body, which matches this pattern. The password is never in the URL or query string (see 3e).

### 2. Client-side hashing or encryption: not beneficial, can be harmful

- The cheat sheets and NIST do not recommend client-side hashing. The OWASP Password Storage Cheat Sheet does not discuss it. NIST's required protection is a protected channel plus server-side salted hashing. https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html and https://pages.nist.gov/800-63-4/sp800-63b.html. I could not read the entire NIST document (the fetch was truncated), so "no mention" is limited to what I read.
- **(inference) Pass-the-hash.** If the browser sends `H(password)` and the server accepts it, then `H(password)` is now the password. Anyone who sees it (a log, a proxy, an XSS payload, a leaked config) can replay it without knowing the original. Nothing is gained, because TLS already protects the transit and the attacker model that remains (XSS, malicious extension) can read the hash as easily as the password.
- **(inference)** It would not protect against a malicious or compromised server either. It would also push you into designing a custom protocol, where mistakes are likely. If you ever wanted real protection beyond TLS, the correct tool is a password-authenticated key exchange (for example OPAQUE), which is overkill for a wedding site.
- OWASP's warnings about pre-hashing (null bytes with bcrypt, "password shucking" when a leaked unsalted fast hash can be worked back to the password) are for server-side storage but show that ad-hoc hashing layers create new attack surface. https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html

### 3. Server-side comparison, storage, throttling, logging

a. **Storage of user passwords.** OWASP says use Argon2id (minimum 19 MiB memory, 2 iterations, parallelism 1), scrypt, bcrypt, or PBKDF2 (600,000+ iterations, HMAC-SHA-256), with a unique salt per password. NIST requires salted and hashed storage resistant to offline attacks (Sec. 3.1.1.2). https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html ; https://pages.nist.gov/800-63-4/sp800-63b.html
   - **(inference)** This guidance targets databases of many users' passwords, where a leak enables offline cracking. Here there are two shared secrets held as Azure Key Vault secrets and injected as env vars (`infra/main.tf`), read only by the API's managed identity. They are not in the database or in source. For this threat model, plaintext in a secret store is acceptable. Storing Argon2id hashes would be a defensible hardening but gives little gain, and the real protection is a long random passphrase (3c).

b. **Constant-time comparison.** Microsoft: `FixedTimeEquals` "compares the contents from two buffers for equality in a way that doesn't leak timing information", and it short-circuits only when lengths differ. https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cryptographicoperations.fixedtimeequals OWASP also lists constant-time returns for comparisons. https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html (section "Compare Password Hashes Using Safe Functions")
   - `TokenService.RoleFor` in `api/WeddingApi/Auth.cs` hashes both sides with SHA-256 first, so the lengths are always equal (no length leak), and it evaluates both the guest and admin secrets every time. This is correct.

c. **Generic errors.** OWASP: respond with a generic error "regardless of" the failure reason. https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html (section "Authentication Responses"). `/auth` returns a bare 401 for any wrong password. OK.

d. **Rate limiting and lockout.**
   - NIST: verifiers must "effectively limit" failed attempts (Sec. 3.1.1.2) and cap consecutive failures (Sec. 3.2.2, no more than 100). https://pages.nist.gov/800-63-4/sp800-63b.html
   - OWASP: throttling and lockout exist to stop guessing, but lockout "can be used to cause a denial of service by locking out other users' accounts", and lockout counters should be tied to the account rather than the source IP. https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html (sections "Login Throttling", "Account Lockout")
   - Microsoft: ASP.NET Core rate limiting counters live in memory per instance, so limits multiply across replicas. Partitioning by client IP is vulnerable to source-address spoofing. Rate limiting does not replace DDoS protection. https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit
   - Here: `AddRateLimiter` partitions by `RemoteIpAddress`, fixed window, 5 per minute (`Auth.cs`). The Container App is pinned to `max_replicas = 1`, so the in-memory counter is accurate today. That is about 7,200 guesses per IP per day, with no cap across IPs. With a strong passphrase this is fine. With a weak one, a distributed guesser is not stopped. A hard global lockout would let anyone lock all guests out (the OWASP DoS warning), so prefer a global soft limit or backoff.

e. **No password in URL or query.** RFC 9110 Sec. 17.9: "URIs are intended to be shared, not secured", and servers, proxies and user agents log the target URI where third parties may see it. "POST is often preferred in such cases." https://www.rfc-editor.org/rfc/rfc9110.html#section-17.9. The app uses a POST body only. OK.

f. **No logging of bodies.** I found no request-body logging in `Program.cs`, `Auth.cs` or `appsettings.json` (default level Information, `Microsoft.AspNetCore` at Warning). The Container Apps environment has no Log Analytics workspace (`infra/main.tf`), so request logs are not retained anyway. OWASP says to log and review all password failures but does not address excluding passwords from logs. https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html (section "Logging and Monitoring"). The code does not log failed sign-ins at all, so that recommendation is currently unmet.

### 4. HTTPS-only and HSTS in this repo

- **API (Azure Container Apps).** Microsoft: HTTPS endpoints always use TLS 1.2 or 1.3, and "by default, HTTP requests to port 80 are automatically redirected to HTTPS on 443". https://learn.microsoft.com/en-us/azure/container-apps/ingress-overview. `infra/main.tf` sets no `allow_insecure_connections` on the `ingress` block, so the default applies. Result: HTTPS is effectively enforced, but only by default, not by stated config.
- **API HSTS.** `Program.cs` has no `UseHsts()` or `UseHttpsRedirection()`, and I found no HSTS header configuration in infra. OWASP: HSTS tells the browser to always use HTTPS, and API-only endpoints should fail unencrypted requests instead of redirecting (a redirected POST has already leaked the body). https://cheatsheetseries.owasp.org/cheatsheets/Transport_Layer_Security_Cheat_Sheet.html
- **Frontend (Static Web Apps).** `public/staticwebapp.config.json` contains only `navigationFallback`. There are no `globalHeaders`, so no `Strict-Transport-Security` is set by us. Microsoft documents `globalHeaders` for "security policies", and notes they do not affect API responses. https://learn.microsoft.com/en-us/azure/static-web-apps/configuration. Whether SWA adds HSTS by itself is not stated in the pages I read, so treat that as unverified.
- **HSTS mechanics.** RFC 6797: browsers act on the header only when received over a secure connection, and the host must not send it over plain HTTP. https://www.rfc-editor.org/rfc/rfc6797 (Sec. 5, 7.2, 8.1). The API is a separate origin (`api.<domain>`), so HSTS has to be sent by the API host itself, or by the site with `includeSubDomains`, to protect the sign-in POST target. **(inference)** Because the guest typing the password only ever visits the HTTPS site, the real-world exposure is low.
- All DNS is Cloudflare "DNS only, never proxied" (`infra/main.tf`), so no Cloudflare edge setting can add HSTS here.
- CORS allows only the two production origins (`appsettings.json`), which is good.

## Recommended follow-ups (each sized as its own issue)

1. **Set `allow_insecure_connections = false` explicitly on both Container App ingress blocks** (prod and dev) in `infra/main.tf`, so HTTPS-only is documented and cannot drift. Container Apps docs: https://learn.microsoft.com/en-us/azure/container-apps/ingress-overview
2. **Send `Strict-Transport-Security` from the API.** Add `app.UseHsts()` (production only) or a small middleware that sets the header on HTTPS responses, and add a test that `/auth` responses carry it. RFC 6797 https://www.rfc-editor.org/rfc/rfc6797 ; OWASP TLS sheet.
3. **Add `globalHeaders` with `Strict-Transport-Security` (and ideally a CSP) to `public/staticwebapp.config.json`.** Confirm what SWA already sends first. https://learn.microsoft.com/en-us/azure/static-web-apps/configuration
4. **Log failed and rejected sign-ins (without the password), and 429s.** Use `OnRejected` and a log line in `/auth` with IP and outcome only. Needs a decision on log retention given there is no Log Analytics workspace. OWASP "Logging and Monitoring"; https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit
5. **Add a global (not per-IP) soft limit or backoff on `/auth`.** Do not hard-lock (OWASP DoS warning). Also check how `X-Forwarded-For` is trusted: `KnownProxies`/`KnownIPNetworks` are cleared in `Auth.cs`, and Container Apps documents that only the rightmost forwarded IP is trustworthy. https://learn.microsoft.com/en-us/azure/container-apps/ingress-overview
6. **Document a minimum strength for the Event and Admin passwords** (for example a 4+ word random passphrase) next to the `az keyvault secret set` instructions in the README. This matters more than any code change because the shared secret is the only barrier. NIST 800-63B https://pages.nist.gov/800-63-4/sp800-63b.html
7. **Add `Cache-Control: no-store` to the `/auth` response** (it returns a bearer token). OWASP TLS sheet recommends `no-store` for sensitive responses.
8. **Optional / low value: write a short ADR or README note** stating that plaintext-over-TLS is deliberate and client-side hashing was considered and rejected (pass-the-hash), so the question in #37 is not re-opened. Also say that the secrets live in Key Vault, so the Argon2id-at-rest guidance does not apply.

## Not changed

No code was changed for this research.
