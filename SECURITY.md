# Security policy

Please do not open a public issue for a suspected vulnerability. Share a minimal reproduction privately with the repository owner and avoid including credentials, customer payloads, or Azure resource identifiers.

Never commit `.env`, Azure credentials, SQL passwords, Service Bus connection strings, API keys, or exported production data. GitHub deployment uses federated OIDC credentials and protected environment secrets; local emulator credentials are intentionally limited to local development.
