# Read-only companion browser fixture

Run only against a fresh evidence directory:

```powershell
dotnet run --project POCs/CompanionBrowserFixture -c Release -p:SkipKillRunningProcesses=true -- <fresh-evidence-directory>
```

The fixture creates two synthetic Markdown files and a synthetic schema-9 metadata database. It binds a random loopback port recorded in `launch.json`. No normal CP host, domain stores, user workspace or Production data is registered. The credential is the fixture-only constant in Program.cs, never a real account credential.

Open the recorded endpoint's `/companion` page and authenticate. Verify reading, literal search, source view, refresh and disconnect at desktop and phone widths. `one.md` shows Project, Cedar, Tree and an outgoing references relationship; markup in the synthetic tag must remain literal text. `notes/two.md` remains readable without enrollment or metadata. Disconnect must clear source and metadata. Compare synthetic database/source hashes before and after browsing; no writes should occur. Stop only this fixture's process afterward.

Browser simulation does not prove a physical phone, HTTPS deployment, production authorization or human acceptance. Keep those checks pending.
