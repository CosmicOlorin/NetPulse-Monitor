# Security policy

## Supported versions

Security fixes are made for the latest published release and the current `main`
branch. Older releases may be used for comparison but should not be assumed to
receive security updates.

## Report a vulnerability privately

Do not open a public issue for a suspected vulnerability. Use **Security →
Report a vulnerability** in this GitHub repository so the report and discussion
remain private.

Include the NetPulse version, Windows version, MR600 hardware/firmware version
when relevant, a concise impact description and reproducible steps. Attach only
the minimum evidence required. Remove or mask passwords, cookies, session tokens,
phone numbers, SMS content, SIM identifiers, full cell identifiers, public IP
addresses, router exports and unrelated log entries.

The project will make a best-effort acknowledgement within 72 hours and provide
an initial assessment within 14 days. These are targets, not a service-level
agreement. Please allow a reasonable remediation period before public disclosure.

## Security boundaries

High-value areas include credential handling, local-router authentication,
unauthorised Cell Lock changes, SMS privacy/sending, unsafe external navigation,
log disclosure and bypasses of the private-LAN router restriction.

Testing must use equipment and accounts you own or are authorised to test. Do not
disrupt mobile networks, third-party speed-test services, other users or public
infrastructure. Social engineering, denial-of-service traffic and reports that
only expose already-public version information are outside the intended scope.

## Operational guidance

- Download releases only from this repository and verify the published SHA-256.
- Keep Windows and router firmware updated.
- Do not expose the router administration interface directly to the internet.
- Close NetPulse before opening the MR600 web interface because the router allows
  only one local management session.
- Review CSV logs and LTE history before sharing them with another person.
