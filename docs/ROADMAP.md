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

## Milestone 5.1 — Extraction Normalization

Status: Complete

- Raw `DocumentTextSection.Content` preserved for verification and debugging
- Nullable normalized retrieval content with section and document statistics
- Deterministic whitespace and line normalization
- Conservative exact repeated PDF header/footer detection with configurable safety thresholds
- Standalone Romanian and English page-number cleanup in page-edge regions
- Conservative Unicode-safe line-break hyphen repair
- Chunk generation and overlap from normalized content with raw fallback for existing rows
- Atomic Extract → Normalize → Chunk → Ready processing
- Ownership-protected raw/normalized inspection and normalization rebuild APIs
- Existing-document rebuild path without file extraction or re-upload
- Frontend normalization metadata, raw/normalized viewer, confirmation, loading, and refresh behavior
- EF Core `AddDocumentNormalization` migration, automated tests, and architecture documentation

## Milestone 5.2 — General-Purpose PDF Normalization Improvement

Status: Complete

- Increased configurable PDF header/footer windows from 8 to 15 non-empty lines
- Added bounded contiguous block candidates near page edges
- Added line-wrap-tolerant exact canonical block comparison
- Preserved stable numeric data while omitting metadata-consistent page counters
- Added conservative dense-local detection for long blocks used by a document subsection
- Restricted independent line matching to the immediate three-line edge
- Preserved numbered headings and body occurrences outside edge regions
- Added candidate/confirmed-block structured metrics without logging content
- Added fictional long-footer, wrapping, variation, safety, determinism, idempotence, and chunk stress tests
- Verified an ignored local diagnostic PDF using aggregate metrics only
- No persistence-model or frontend changes required

## Milestone 6 — Embeddings and pgvector

Status: Planned

- Select and configure an embedding provider and model
- Add embedding-specific persistence and PostgreSQL vector support only in this milestone
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
