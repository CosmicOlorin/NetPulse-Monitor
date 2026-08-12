## Summary

Describe the user-visible change and why it is needed.

## Validation

- [ ] `dotnet format NetPulseMonitor.sln --verify-no-changes`
- [ ] `dotnet build NetPulseMonitor.sln -c Release -warnaserror`
- [ ] Protocol tests pass when router or LTE behaviour changes
- [ ] UI remains readable at the supported minimum size and Windows scaling
- [ ] No credentials, phone numbers, SMS content, tokens, private logs or generated build artifacts are included

## Safety and privacy

Describe any change to network destinations, stored data, router writes, SMS,
Cell Lock, logging or credential handling. Write “None” when not applicable.
