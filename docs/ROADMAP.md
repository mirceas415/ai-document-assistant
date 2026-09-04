# AI Document Assistant Roadmap

## Core MVP

Status: Complete

Milestones 1 through 11 provide the complete ingestion, grounded Ask Your Documents, persistent conversation experience, generic document understanding, and deterministic PDF-structure foundation. Deployment remains a separate operations milestone; later retrieval milestones are listed independently and are not implemented by Milestone 11.

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

## Milestone 6 — Embedding Infrastructure + pgvector

Status: Complete

- Official OpenAI .NET SDK behind a batch-oriented application abstraction
- Configurable `text-embedding-3-small`, 1536 dimensions, and bounded sequential batching
- First-class pgvector/Npgsql/EF Core integration with nullable `vector(1536)` chunk storage
- Per-chunk model, dimensions, time, and exact-content SHA-256 metadata
- Safe document-level embedding aggregates and legacy-document detection
- Atomic Extract → Normalize → Chunk → Embed → Persist → Ready processing
- Fresh embeddings for normalization and chunk rebuilds without partial authoritative replacement
- Ownership-protected explicit embedding generation/rebuild from persisted chunk content
- SDK-level transient retries, cancellation, structured safe logging, and cost controls
- Frontend embedding status and confirmed Generate/Rebuild actions without vector exposure
- EF Core migration, fake-based automated tests, and architecture documentation
- Milestone 6 intentionally stopped before vector retrieval, query embeddings, semantic search, and RAG; those capabilities are completed by Milestones 7 and 8 below

## Milestone 7 — Semantic Retrieval/Search

Status: Complete

- Authenticated `POST /api/projects/{projectId}/search`
- Required trimmed query up to 2,000 characters and Top-K default 8, maximum 20
- Exactly one transient query embedding through the existing `ITextEmbeddingService`
- PostgreSQL/pgvector cosine-distance ordering with smaller distance meaning closer match
- Database-side owner, project, Ready status, model, dimensions, timestamp, vector, aggregate, and SHA-256 content-freshness filters
- Safe ranked document/page/chunk/heading/content DTOs without vectors or storage paths
- Shared `ISemanticRetrievalService` and small PostgreSQL vector-query abstraction
- Exact filtered search for MVP correctness; HNSW and IVFFlat deferred pending representative measurement
- Semantic retrieval/debug UI with loading, error, and ranked result states
- Fake-based automated tests plus deterministic/manual retrieval evaluation guidance

## Milestone 8 — RAG / Ask Your Documents

Status: Complete

- Authenticated `POST /api/projects/{projectId}/ask` with ownership verified before any AI request
- Reuse of Milestone 7 retrieval with server-configured Top-K 8
- Deterministic context construction bounded to a 6,000-token `cl100k_base` estimate
- Stable `[S1]`, `[S2]`, ... source identifiers and untrusted-document delimiters
- Explicit prompt-injection defense and documents-as-data instruction hierarchy
- Grounded Romanian/English answers using the existing configurable `gpt-5.6-luna`
- Official OpenAI .NET Responses API with low reasoning, 700 output-token cap, and provider storage disabled
- Backend-validated citations mapped only to retrieved authoritative chunks
- Bounded authoritative source excerpts with document/page/chunk/heading metadata
- Safe localized no-evidence behavior without an unnecessary answer-model call
- Non-streaming question flow with one embedding plus at most one answer request
- Ask Your Documents UI with answer, safe error, and inspectable source states
- Fake-based RAG, citation, Unicode, prompt-injection, ownership, and provider-failure tests

## Milestone 9.1 — Conversation UX and Persistent Chat History

Status: Complete

- ChatGPT-style project workspace with project navigation, conversation history, focused messages, bottom composer, and clear document scope
- Persisted project conversations, ordered user/assistant messages, and authoritative bounded source snapshots
- Ownership enforced in database queries through conversation → project → owner
- Create, list, load, rename, delete, and conversation-scoped message APIs
- Deterministic first-question titles without an extra model request
- Bounded recent-message context kept separate from authoritative retrieved document evidence
- User-message-first failure semantics, retry UX, and no fabricated assistant persistence
- Today, Yesterday, Previous 7 days, and Older local-time history groups
- Compact source cards and authoritative source-snapshot inspection
- Document management on a dedicated project route and Retrieval Debug in a collapsed advanced panel
- `AddConversations` EF Core migration and fake-based persistence, ownership, history, source, Unicode, and failure tests

## Milestone 9.2 — Chat-first UX + Direct Workspace Document Upload

Status: Complete

- Chat-first workspace presentation while `Project` remains the backend ownership, security, document, and retrieval boundary
- Workspaces/Documents primary navigation with the duplicate first-sidebar Chats item removed and conversation history preserved
- Centered Current Workspace empty-state context without generic prompt suggestions
- Human-readable ready-document scope in the chat header and composer
- Accessible rename/delete conversation overflow menu using the existing confirmation dialog
- Composer paperclip and main-chat PDF/DOCX drag/drop through the existing project document upload API
- Shared frontend upload validation and normal extraction, normalization, chunking, and embedding processing
- Compact transient upload status, conditional existing document-state polling, and ask-while-processing guidance
- Documents remain reusable by every conversation in the workspace; no conversation document ownership or schema change
- No new AI call type, model change, package, backend endpoint, or migration

## Milestone 10 — Document Understanding / Ingestion Intelligence

Status: Complete

- Independent `DocumentUnderstanding` lifecycle: Pending, Processing, Ready, Failed, and Skipped
- Generic controlled document-type taxonomy and short optional subtype
- Primary BCP-47-compatible language code with validated confidence
- Bounded generic document metadata entries with controlled kinds, labels, values, optional deterministic normalized values, confidence, and sequence
- Strict OpenAI Responses JSON-schema output behind a focused provider abstraction and the existing backend-only credentials
- Explicit untrusted-document/prompt-injection boundary with no tools or external actions
- Deterministic full-text-or-beginning/middle/end sampling capped at approximately 6,000 `cl100k_base` tokens
- Full normalized-content SHA-256 plus model and `document-understanding-v1` idempotency
- Non-fatal provider/validation failures that leave chunking, embedding, semantic retrieval, and RAG usable
- Atomic successful classification/metadata replacement and safe bounded Failed/Skipped states
- Automatic analysis for new uploads plus ownership-protected GET and forced rebuild endpoints for historical Ready documents
- Document Intelligence UI with statuses, confidence text, metadata, Advanced audit fields, confirmation, retry, and polling
- `AddDocumentUnderstanding` EF Core migration, offline fake-based tests, and architecture documentation
- No OCR, PDF technical classification, metadata-aware retrieval, hybrid search, reranking, or automatic summary

## Milestone 11 — Technical PDF Intelligence / OCR-ready Routing

Status: Complete

- Independent `DocumentTechnicalAnalysis` lifecycle with ordered `DocumentPageTechnicalAnalysis` diagnostics
- Controlled Unknown, TextBased, Scanned, ImageBased, and Mixed page/document taxonomy
- Deterministic meaningful-text thresholds and conservative page-sized raster-image detection through existing PdfPig APIs
- Original-file SHA-256 plus `pdf-technical-analysis-v1` idempotency, with forced manual rebuild
- Automatic pre-extraction analysis that remains non-fatal to extraction and every downstream ingestion stage
- Blank-page tolerance, 80% document majority, OCR-text-layer-safe Mixed handling, and future per-page OCR routing signals
- Ownership-protected read/rebuild APIs, compact status/count UI, and Advanced page diagnostics
- DOCX Skipped/not-applicable behavior, legacy document compatibility, cascade deletion, safe failures, migration, and offline tests
- No OCR, computer vision, rasterization pipeline, OpenAI call, extraction change, Document Understanding change, or retrieval/RAG change

## Milestone 12 — Local OCR-assisted Document Ingestion

Status: Complete

- M11 page diagnostics are the routing authority: only `Scanned` pages are automatic OCR candidates
- In-memory, one-page-at-a-time PDFium rendering behind `IPdfPageRenderer`, with proportional DPI reduction under a configurable pixel budget
- Local Tesseract recognition behind `IOcrService`; no cloud OCR, vision request, external process, or runtime model download
- Unified ordered raw extraction with `NativePdf`, `Ocr`, `Docx`, and legacy `Unknown` provenance
- Independent aggregate/page status and bounded diagnostics, plus source/configuration/routing fingerprints and forced manual rebuild
- Page-aware Partial/Failed behavior, empty-result safety, candidate-page limits, DOCX not-applicable behavior, and legacy compatibility
- Ownership-protected read/rebuild APIs, downstream normalization/M10/chunk/embedding regeneration, compact UI, migration, fake-based automated tests, and manual setup guide
- No retrieval, RAG prompt, citation, metadata-search, hybrid-search, reranking, or M11 classification change

## Milestone 13 — Metadata-aware Hybrid Retrieval

Status: Complete

- Preserve exact pgvector cosine retrieval with one transient query embedding and bounded vector candidates
- Add language-neutral PostgreSQL `simple` full-text chunk retrieval with a generated `tsvector` and GIN index
- Use only owned/project-scoped Ready M10 understanding for bounded document-level metadata, title, filename, subtype, and type signals
- Deterministically recognize exact identifiers, conservative dates/amounts, and a small controlled document-type alias set without an LLM call
- Fuse unique vector/lexical chunk candidates with soft metadata document contributions through deterministic weighted reciprocal rank fusion
- Keep metadata out of answer evidence; only final authoritative chunks enter the unchanged bounded RAG context and citation path
- Extend the existing collapsed Advanced Retrieval Details UI and API contract with bounded rank/debug information
- Add focused fusion, behavior, query-safety, ownership-query, regression, and small synthetic evaluation tests
- `AddHybridRetrievalIndexes` EF Core migration; no new package, reranker, ANN index, query-understanding model, or filter UI

## Milestone 14 — Model-based Reranking & Retrieval Quality

Status: Complete

- Keep M13 as bounded recall-oriented candidate generation, then run one optional batch model rerank before final TopK/context selection
- Provider-independent `IRetrievalReranker` with an official OpenAI Responses implementation and deterministic pass-through fake/no-op support
- Strict bounded structured output containing only opaque candidate IDs and 0–4 relevance grades; no generated answers, citations, or free-text rationale
- Reuse the existing tokenizer for an 18-candidate default/30-candidate maximum, 12,000-token total input budget, and 700-token per-candidate cap
- Locally validate all model output, discard unknown IDs, keep first duplicates, append omissions in hybrid order, and prevent creation of new chunks
- Fail open to M13 order on timeout, provider/configuration failure, or malformed output; skip calls that cannot affect selection
- Preserve existing ownership filters, M13 weights, metadata-as-non-evidence semantics, bounded RAG context, answer prompt, and citation path
- Extend Advanced Retrieval Details with final/hybrid/reranked ranks, relevance, and compact fallback state
- Add fake-only orchestration, validation, token-budget, injection-boundary, failure-regression, and hybrid-versus-reranked evaluation tests
- No database migration, package, additional embedding/query-understanding call, answer-generation change, or end-user retrieval controls

## Milestone 15 — Deployment / Production Hardening

Status: Planned

- Production configuration and secret management
- Managed PostgreSQL and durable file-storage strategy
- HTTPS, monitoring, backups, and recovery
- CI/CD, migrations, health checks, and operational documentation

## Post-MVP — Possible Improvements

Status: Not started

- Durable background queue and cross-process job coordination
- Cloud/object storage
- Streaming responses
- Formal retrieval and answer evaluation framework
- Production observability, tracing, and cost dashboards
- Deployment hardening, scaling, backup, and recovery automation
