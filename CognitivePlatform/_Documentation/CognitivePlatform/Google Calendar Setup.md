# Google Calendar Setup — Step-by-Step Guide
>This has been done. Kept for prosperity

This guide covers everything needed to connect the Cognitive Platform to Google Calendar.
Complete these steps once before implementing `GoogleCalendarProvider` (see DEFERRED.md item 1).

---

## Prerequisites

- A Google account (personal or Workspace)
- Access to [Google Cloud Console](https://console.cloud.google.com)
- The Cognitive Platform API running locally (for the OAuth callback)

---

## Step 1 — Create a GCP Project

1. Open [https://console.cloud.google.com](https://console.cloud.google.com).
2. Click the project selector in the top bar → **New Project**.
3. Name it (e.g. `CognitivePlatform`) and click **Create**.
4. Wait for the project to provision, then make sure it is selected in the project selector.

---

## Step 2 — Enable the Google Calendar API

1. From the left menu, go to **APIs & Services** → **Library**.
2. Search for **Google Calendar API**.
3. Click it, then click **Enable**.

---

## Step 3 — Configure the OAuth Consent Screen

1. Go to **APIs & Services** → **OAuth consent screen**.
2. Choose **External** (or Internal if using Google Workspace with a single org).
3. Fill in:
   - **App name:** `Cognitive Platform`
   - **User support email:** your email
   - **Developer contact information:** your email
4. Click **Save and Continue**.
5. On the **Scopes** step, click **Add or Remove Scopes**.
   - Add: `https://www.googleapis.com/auth/calendar.readonly`
   - If write support is needed later, also add: `https://www.googleapis.com/auth/calendar.events`
6. Click **Update** → **Save and Continue**.
7. On the **Test Users** step (required for External apps in testing mode), add your
   own Google account as a test user. Click **Save and Continue**.
8. Review and click **Back to Dashboard**.

---

## Step 4 — Create OAuth 2.0 Credentials

1. Go to **APIs & Services** → **Credentials**.
2. Click **Create Credentials** → **OAuth client ID**.
3. Choose **Web application**.
4. Name it (e.g. `CP Local Dev`).
5. Under **Authorised redirect URIs**, add:
   ```
   http://localhost:5273/auth/google/callback
   ```
   (Replace `5273` with whatever port the CP API runs on in your `launchSettings.json`.)
6. Click **Create**.
7. A dialog appears with your **Client ID** and **Client Secret**. Copy both — you will
   need them in the next step.


---

## Step 5 — Store Credentials in the API

Do **not** commit credentials to source control. Use .NET user secrets for local development.

```bash
cd CognitivePlatform
dotnet user-secrets set "GoogleCalendar:ClientId"     "<your-client-id>"
dotnet user-secrets set "GoogleCalendar:ClientSecret" "<your-client-secret>"
```

In `appsettings.json`, add the section (values will be overridden by user secrets in dev):

```json
"GoogleCalendar": {
  "ClientId": "",
  "ClientSecret": "",
  "RedirectUri": "http://localhost:5273/auth/google/callback",
  "TokenStorePartitionKey": "calendar-tokens"
}
```

---

## Step 6 — OAuth Flow (How it Works at Runtime)

The Cognitive Platform uses the **Authorization Code Flow**:

1. User sends a natural language command that requires calendar access (or navigates to
   a "Connect Calendar" URL).
2. The API generates a Google authorisation URL and returns it to the client.
3. The client opens the URL in a browser. The user logs in and grants permission.
4. Google redirects to `http://localhost:5273/auth/google/callback?code=...`.
5. The callback endpoint exchanges the code for an **access token** and **refresh token**.
6. Both tokens are stored in `IObjectStore` (partition key: `calendar-tokens`, id: `default`).
7. Subsequent API calls use the stored refresh token to obtain a fresh access token.

---

## Step 7 — Verify the Setup (Manual Smoke Test)

Once `GoogleCalendarProvider` is implemented:

1. Start the CP API.
2. Call `GET /auth/google/connect` (or trigger it via the conversational UI).
3. Complete the browser OAuth flow.
4. Call `GET /api/tasks/brief` — the Daily Brief should include a "Today's Calendar" section.
5. Or send: `"What's on my calendar today?"` via `/api/converse`.

---

## Troubleshooting

| Symptom | Likely cause |
|---------|-------------|
| `redirect_uri_mismatch` | The redirect URI in GCP doesn't exactly match the API's port/path |
| `access_denied` | Your Google account isn't added as a test user in the OAuth consent screen |
| `invalid_client` | Client ID or Client Secret is wrong or has leading/trailing whitespace |
| Tokens expire / 401 after a day | Refresh token rotation — re-run the OAuth flow once; the updated token will be stored |

---

## Production Notes (for future reference)

- Move the app from **Testing** to **Production** in the OAuth consent screen when
  deploying (requires Google verification for sensitive scopes).
- In production, store tokens encrypted or use a secrets manager rather than the SQLite
  object store.
- Use `https://` redirect URIs only; `http://localhost` is allowed only in dev.

---

_Last updated: 2026-04-11_
