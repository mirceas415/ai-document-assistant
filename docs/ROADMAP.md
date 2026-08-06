# AI Document Assistant Roadmap

## Milestone 1 — Authentication

Status: Complete

- ASP.NET Core Identity with GUID users
- Registration, login, logout, and current-user endpoints
- Secure cookie authentication
- JSON authentication and authorization errors

## Milestone 2 — Projects

Status: Complete

- User-owned projects
- Create, list, view, update, and delete operations
- Ownership enforcement
- Project dashboard and editor UI

## Milestone 3 — Documents

Status: Complete

- PDF and DOCX upload
- Size, type, signature, and filename validation
- Local file storage with generated names
- Document metadata and deletion

## Milestone 4 — Extraction

Status: Complete

- PDF page extraction
- DOCX paragraph, table, and heading extraction
- Bounded background processing queue and hosted worker
- Ordered `DocumentTextSection` persistence
- Processing status, safe errors, and retry
- Extracted-text viewer
- Backend extraction, pipeline, and authorization tests
- Status-specific frontend actions and stale action-error refresh fix

## Milestone 5 — Chunking

Status: Complete

- Ordered `DocumentChunk` persistence with source metadata
- Configurable 700 target, 900 maximum, 100 overlap, and 100 minimum
- Deterministic heading, paragraph, sentence, and tokenizer-boundary strategy
- Exact `cl100k_base` tiktoken counts
- Romanian, English, mixed-language, abbreviation, and Unicode support
- Transactional chunk replacement and safe failure handling
- Authenticated, ownership-protected list and rebuild endpoints
- Chunk count/date UI, chunk viewer, and confirmed rebuild action
- Pipeline, generator, rebuild, metadata, ordering, determinism, language, overlap, and authorization tests

## Milestone 6 — Embeddings

Status: Planned

- Select and configure an embedding provider and model
- Add embedding-specific persistence only in this milestone
- Generate embeddings from existing `DocumentChunk` rows
- Add retry, observability, and cost controls

## Milestone 7 — Vector Search

Status: Planned

- Introduce PostgreSQL vector support
- Store and index chunk embeddings
- Implement ownership-filtered similarity retrieval
- Evaluate retrieval quality and latency

## Milestone 8 — AI Chat

Status: Planned

- Add conversations and messages
- Ground responses in authorized retrieved chunks
- Return document/page citations
- Add prompt-injection and content-safety controls

## Milestone 9 — Deployment

Status: Planned

- Production configuration and secret management
- Managed PostgreSQL and durable file-storage strategy
- HTTPS, monitoring, backups, and recovery
- CI/CD, migrations, health checks, and operational documentation
