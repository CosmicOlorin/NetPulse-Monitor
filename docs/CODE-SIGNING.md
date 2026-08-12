# Code signing and publisher identity

NetPulse release files should be Authenticode-signed with a trusted code-signing
certificate before they are published. A certificate authority validates the
certificate subject. Windows displays that validated subject as the publisher;
the application cannot choose a different trusted publisher label.

For an individual certificate, use the legal name accepted by the certificate
provider. `CosmicOlorin` can remain the product brand, repository owner, author
and copyright identity. It should be used as the signed Windows publisher only
if a certificate provider has validated and issued the certificate to that exact
registered identity. A registered company or trade entity can instead obtain an
organisation certificate in its verified legal name.

## Safe local signing workflow

1. Obtain an Authenticode code-signing certificate from a trusted provider.
2. Install it in the current Windows user's certificate store. Keep private-key
   material and provider credentials outside this repository.
3. Find the certificate's 40-character SHA-1 thumbprint in `certmgr.msc`.
4. Build the release.
5. Run:

   ```powershell
   .\sign-release.ps1 `
     -Files .\artifacts\publish\win-x64\NetPulseMonitor.exe `
     -CertificateThumbprint YOUR_CERTIFICATE_THUMBPRINT
   ```

The script applies SHA-256 Authenticode signing, requests a trusted timestamp and
then runs Windows policy verification. It never accepts or stores a private key,
PFX file or password in the repository.

The release builder also accepts `-SigningCertificateThumbprint`. If Inno Setup
6 is installed, `-BuildInstaller` creates the per-user installer from
`installer/NetPulseMonitor.iss`; the builder signs both the application and the
installer when a thumbprint is supplied.

Never publish an installer that merely claims to be signed. If no trusted
certificate is available, publish it explicitly as an unsigned build with its
SHA-256 hash and keep the application's trust card accurate.
