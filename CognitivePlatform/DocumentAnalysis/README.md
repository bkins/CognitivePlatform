# Development document analysis

Optional read-only endpoints for Axiom's explicit document snapshots:

- `GET /api/document-analysis/capabilities`
- `POST /api/document-analysis/embeddings`
- `POST /api/document-analysis/insights`

All routes require `X-Document-Analysis-Key`. Configure `DocumentAnalysis:Enabled=true` and `DocumentAnalysis:ClientKeySha256` as the hexadecimal SHA-256 of a dedicated random credential containing 32–256 characters, through Development user-secrets/environment configuration. The raw credential belongs only in Axiom's session entry; never put it in Markdown, logs, source or committed settings. Rotate/revoke the configured hash or disable the feature. These routes fail closed outside Development. HTTP is accepted only for a loopback caller; remote callers require HTTPS. Existing health/Admin credentials are not accepted.

The initial supported insight provider is CP's existing Ollama client. Embeddings use the existing `IEmbeddingService`; disconnected, unsupported and simulated states remain explicit. No cloud provider activation, full CP deployment, Production change or model download is part of this change. Capabilities distinguish configuration from catalog readiness; missing catalog digest does not establish a usable model. Embedding responses compare catalog model digests before/after execution; changed digests return 409. Axiom does not reuse persistent vectors without an identified digest.

Version 1 POST requests contain `protocolVersion`, `clientRequestId`, `documents` (1–16 objects with `referenceKey`, `sourceHash`, `text`), and optional `question`. Hashes are SHA-256 of UTF-8 submitted text. Total text is bounded to 200,000 UTF-16 characters, question to 4,000 characters, request body to 1 MiB. References are distinct and echoed in order. Insights are separate derived text; the server neither edits nor stores domain documents. Provider exceptions and response bodies are not exposed in errors. The dedicated concurrency gate returns 429 while an operation remains active, including an uncooperative provider after a bounded caller timeout. Request cancellation propagates to providers. Timeout is configurable from 1–120 seconds.

The controller has no conversation orchestrator, action registry, agent-job or persistent-domain dependencies. The isolated integration fixture in Axiom's `POCs/CpAnalysisIntegration` registers only these endpoints, provider clients and the dedicated gate: it exercises real HTTP with installed local Ollama models and synthetic text, without starting the normal CP hosted jobs/stores. The standard Development server remains disabled until an operator supplies its dedicated credential configuration. Tests and fixtures do not authorize Production deployment or establish human relevance/grounding acceptance.

Verification: the existing CP regression suite and real Kestrel auth/revocation/non-Development/hash/error/timeout tests pass. Build with `-p:SkipKillRunningProcesses=true` when using an isolated checkout so the existing server is preserved. Baseline repository compiler warnings remain separate from the newly added endpoint logic.
