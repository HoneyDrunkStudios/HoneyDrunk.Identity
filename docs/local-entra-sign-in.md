# Local Entra sign-in

The API reads development settings from .NET user secrets (`honeydrunk-identity-local`). Configure `Entra:Authority`, the exact discovery `Entra:Issuer`, the API application's client ID as `Entra:Audience`, the public client's ID as `Entra:MobileClientId` (also used for web), and `Entra:ApiScope`.

For Graph account checks on Windows, configure `Entra:Graph:TenantId`, `Entra:Graph:ClientId`, and `Entra:Graph:CertificateThumbprint`. Upload the public certificate to that backend app registration, and keep the private key in the current Windows user's Personal certificate store (`CurrentUser/My`). Run Visual Studio or Aspire as that same Windows user. An expired certificate fails closed; replace it and update the app registration and thumbprint before expiration.

Certificate authentication is used only in Development with an explicit thumbprint. Other environments continue to use system-assigned managed identity. Never put a backend certificate, private key, or client secret in the Expo app.

The backend's initial account lookup requires Microsoft Graph application permission `User.Read.All` with admin consent. This read-only setup does not enable account deletion or session revocation; those lifecycle operations require separate permissions and deployment configuration. Do not claim erasure is ready from a successful sign-in test.

The web client needs a SPA redirect URI of `http://localhost:8081/callback`, delegated permission to the Identity API's `access_as_user` scope with admin consent, and association with a customer sign-up/sign-in user flow. The current development flow uses email one-time passcodes. Social providers require their own setup.
