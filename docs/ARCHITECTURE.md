# AI Document Assistant Architecture

## Overview

AI Document Assistant is a modular monolith with an ASP.NET Core 10 backend, a React and TypeScript frontend, PostgreSQL persistence through Entity Framework Core and pgvector, OpenAI embedding generation, and local document storage. The application supports authenticated, user-owned projects, PDF and DOCX upload, background text extraction, conservative extraction normalization, deterministic retrieval chunk generation, embedding persistence, and explicit rebuild or inspection operations.

Milestone 6 ends at embedding generation and storage. There is no vector retrieval, nearest-neighbor query, query embedding, vector index, semantic search, hybrid search, reranking, chat, RAG answering, classification, OCR, cloud storage, external message queue, or generic AI orchestration framework.

## Components

- `AI.DocumentAssistant.Server` hosts the JSON API, cookie authentication, static frontend output, EF Core data access, file storage, extraction pipeline, in-memory background queue, chunking services, the application embedding abstraction, and the OpenAI adapter.
- `ai.documentassistant.client` is a React 19 and TypeScript single-page application built with Vite.
- `AI.DocumentAssistant.Server.Tests` contains xUnit tests using EF Core's in-memory provider plus generated PDF and DOCX fixtures.
- PostgreSQL is the production relational database. The pgvector extension stores embeddings through the provider's first-class `Vector` type; vectors are not manually serialized.
- The local `Uploads` directory stores uploaded PDF and DOCX files under generated filenames. Stored paths and uploaded files are not exposed through the API.

## Entities and Relationships

```text
ApplicationUser 1 ── * Project 1 ── * Document
                                      ├── * DocumentTextSection
                                      └── * DocumentChunk
```

### ApplicationUser

ASP.NET Core Identity user with a GUID key, display name, email, authentication data, creation time, and owned projects.

### Project

A user-owned workspace with a name, optional description, and timestamps. Deleting a user deletes owned projects; deleting a project deletes its document database rows.

### Document

Stores the project relationship, safe original filename, generated storage filename, MIME type, size, processing status, extraction, normalization, chunking, and aggregate embedding metadata, plus bounded safe public errors.

Important chunking fields are:

- `ChunkCount`
- `ChunkedAtUtc`
- `ChunkingError`

Embedding aggregates are `EmbeddedChunkCount`, `EmbeddingModel`, `EmbeddingDimensions`, `EmbeddedAtUtc`, and `EmbeddingError`. They cheaply exclude legacy or configuration-mismatched documents without returning vectors. For aggregate candidates, list/detail reads verify every chunk's vector presence, model, dimensions, timestamp, and SHA-256 against its exact current content before returning `EmbeddingsAreCurrent`; the aggregate is not treated as a substitute for chunk-row validation.

### DocumentTextSection

The ordered extraction representation. PDF sections normally correspond to pages. DOCX sections correspond to heading groups. `Content` is immutable raw extraction for verification and debugging. Nullable `NormalizedContent` is the retrieval-oriented derivative, with `NormalizationChanged`, `RemovedCharacterCount`, and `NormalizedAtUtc` providing traceability. Existing rows from before Milestone 5.1 have null normalized content until normalization is rebuilt.

The document row stores useful aggregates: normalized character count, removed character count, changed-section count, completion time, and a bounded safe normalization error. Raw content is never overwritten by normalization or chunk rebuilding.

### DocumentChunk

The ordered retrieval unit derived from one or more source sections. It stores content, exact tokenizer count, character count, page range, heading, source-section range, creation time, and nullable embedding data. `(DocumentId, ChunkIndex)` is unique. Document deletion cascades to chunks, so the vector disappears with its owning chunk and no external vector-store cleanup is needed.

The embedding fields are:

- `Embedding`: the provider `Vector` value mapped to PostgreSQL `vector(1536)`;
- `EmbeddingModel` and `EmbeddingDimensions`: the exact configuration that produced it;
- `EmbeddingContentHash`: uppercase hexadecimal SHA-256 of the exact UTF-8 `DocumentChunk.Content` sent for embedding; and
- `EmbeddedAtUtc`: the successful generation time.

The hash is deterministic integrity/staleness metadata, not a security mechanism. An embedding is current only when the vector exists, model and dimensions match current configuration, and the stored hash matches the exact current chunk content. Chunk replacement always creates and embeds new rows; embeddings are never copied by chunk index, page number, or section index.

## Authentication and Ownership

ASP.NET Core Identity uses GUID user keys and an HTTP-only application cookie. API authentication failures return JSON `401` or `403` responses instead of redirects.

All project and document endpoints require authentication. Queries include the current user's ID through `Project.OwnerId`. A resource belonging to another user is returned as not found so its existence is not disclosed. Read-only queries use `AsNoTracking` where change tracking is unnecessary.

## Upload Flow

1. The authenticated user uploads a document to an owned project.
2. The API validates the 20 MB limit, filename, extension, declared content type, and file signature.
3. The file is saved locally with a generated `.pdf` or `.docx` filename.
4. A `Document` row is created with status `Uploaded`.
5. The document ID is offered to the process-local background queue.
6. The API returns document metadata; it never returns the storage filename or path.

## Extraction Flow

1. The worker changes an Uploaded or Failed document to `Processing` and clears prior public errors and counts.
2. The processor selects the registered extractor by MIME type and stored extension.
3. PDF extraction reads pages in order and records page numbers.
4. DOCX extraction reads paragraphs and tables in order, grouping Heading 1–3 content and retaining the heading as section metadata.
5. Extracted sections remain in memory as raw source data while normalization runs.
6. Chunks are generated from normalized sections in memory.
7. The exact generated chunk contents are embedded and the complete result is validated in memory.
8. Only then does a database transaction replace sections and chunks, attach their vectors and metadata, update document aggregates, and set the document to `Ready`.

The complete flow is:

```text
Upload -> Extract -> Normalize -> Chunk -> Embed -> Persist -> Ready
```

`Processing` covers extraction through embedding; no separate status is needed. An extraction, normalization, chunking, or embedding failure during initial/retry processing leaves no partial new authoritative sections, chunks, or embeddings, sets the document to `Failed`, stores a bounded stage-safe error, and keeps the uploaded file for retry. Retrying reopens the existing uploaded file and runs the complete current pipeline. Technical exceptions are logged without document text, physical paths, vectors, credentials, authorization headers, or provider response bodies.

## Normalization Flow

Normalization is pure, synchronous CPU work that does not use the database, network, AI, dictionaries, or language models. It runs in this fixed order:

```text
raw sections
  -> line-ending and horizontal-whitespace normalization
  -> repeated PDF header/footer removal
  -> isolated PDF page-number removal
  -> conservative cross-line word-break repair
  -> blank-line collapse and final trimming
  -> normalized sections
```

Line endings become `\n`; lines are trimmed; tabs and repeated horizontal whitespace collapse to one space; and at most one empty line is retained between paragraphs. Unicode stays in .NET and PostgreSQL strings, so Romanian diacritics, English text, mixed-language content, emoji, and surrogate pairs are not transliterated.

### PDF boilerplate detection

PDF extraction creates one section per non-empty page. Milestone 5.1 compared independent normalized lines, which was safe but missed long edge blocks when physical wrapping changed, when the block exceeded eight extracted lines, or when a varying page counter interrupted otherwise stable content. Milestone 5.2 retains exact-line detection close to the edge and adds bounded contiguous block detection.

For each page, the normalizer obtains up to the configured first and last 15 non-empty lines as separate header and footer regions. On pages where those windows would overlap, they are proportionally reduced and leave a middle body line uninspected. Exact-line matching is limited to the first or last three lines; longer content is handled by blocks.

Block generation considers contiguous spans of at least two lines within a region. A span must start within two lines of the header edge or end within two lines of the footer edge. With a 15-line window this produces at most 39 spans per region before per-page canonical deduplication, rather than arbitrary page-wide subsets.

Canonical block comparison is separate from stored content. It:

- normalizes Unicode compatibility forms for comparison only;
- collapses horizontal whitespace;
- removes harmless spaces immediately around opening or closing punctuation;
- compares using invariant uppercase;
- joins physical lines so different wrapping yields the same logical sequence;
- applies the existing conservative line-break hyphen repair for comparison; and
- omits a standalone page counter only when it agrees with PDF page metadata.

Dates, versions, amounts, document references, phone numbers, and other numbers are not generalized or replaced. Materially different blocks therefore keep different keys. Confirmed keys are mapped back to their original line indexes; raw `Content` and the canonical key are never persisted or logged.

A block is confirmed when it occurs on at least `ceil(PDF sections × occurrence ratio)` pages (60% by default). A second conservative rule supports multi-template PDFs: an exact canonical block of at least 160 characters may also qualify when at least three occurrences form a local page range whose density still meets 60%. This allows a long repeated template used in one consecutive part of a document without lowering the global ratio for short candidates.

False-positive safeguards include PDF-only application, separate header/footer keys, bounded and non-overlapping edge regions, the two-line boundary offset, a 40-character minimum block key, exact canonical equality rather than fuzzy similarity, a three-page minimum, local-density eligibility only for long blocks, and maximal-occurrence mapping only at original edge positions. Numbered headings are never removed even if they participate in a matched block. A body occurrence outside the edge region remains. If removal would empty the entire meaningful document, the normalizer falls back to whitespace-normalized content.

A one-line page cannot establish a candidate. Header/footer inference is never applied to DOCX sections. The current PDF extractor omits blank pages, so occurrence calculations use stored non-empty PDF page sections rather than physical blank pages.

For `P` pages and edge size `N`, line counting is `O(PN)`, while contiguous block generation is `O(PN²L)` for bounded canonical text length `L`. Defaults fix `N` at 15 and cap a canonical candidate at 4,000 characters, so work remains localized and predictable under the existing 20 MB upload limit. Candidate comparison uses dictionary grouping by exact canonical key rather than pairwise fuzzy comparisons.

### Page numbers and word breaks

Standalone PDF header/footer lines matching `N`, `Page N`, `Pagina N`, `N / M`, or `N/M` are removed only when `N` equals the section's page number. The same rule can omit a counter from block comparison without hiding stable numbers. Amounts, dates, phone numbers, identifiers, contract numbers, and numeric text inside body lines are untouched.

Cross-line hyphen repair requires at least four Unicode letters before a trailing hyphen and at least three lowercase-starting Unicode letters on the next line. Numeric continuations, double hyphens, blank-line boundaries, and continuations containing another hyphen are rejected. Existing in-line compounds such as `well-known` remain unchanged. This rule repairs common extraction splits such as `furnizo-\nrului`, but deliberately does not attempt spelling or general language correction; rare meaningful compounds split exactly at a line boundary may remain ambiguous.

The transformation is deterministic and idempotent with respect to output content. Per-section and document counts make changes inspectable without exposing matching internals or document contents in logs.

## Chunking Flow

1. Stored text sections are read in `SectionIndex` order with `AsNoTracking`; chunking uses `NormalizedContent ?? Content` for compatibility with pre-normalization rows.
2. The generator prefers boundaries in this order: titled section/heading, paragraph, sentence, then tokenizer boundary.
3. The default configuration targets 700 tokens, allows at most 900, repeats up to 100 tokens of overlap, and aims for a 100-token minimum. Values are configured in `appsettings.json` and validated during startup.
4. Sentence detection protects common Romanian abbreviations including `art.`, `nr.`, and `dl.`.
5. Oversized sentences use tokenizer character indexes and backtrack to whitespace where possible, avoiding broken words and UTF-16 surrogate pairs.
6. The short final chunk is merged or rebalanced where the maximum allows. A short document produces one chunk.
7. Overlap copies useful whole trailing paragraphs or sentences when possible. If a unit is too large, a tokenizer-bounded suffix is used. The generator never copies an entire previous chunk as overlap.
8. Generated chunks are embedded before replacement; after all vectors pass validation, chunks and their embeddings replace previous rows inside one database transaction. Overlap is derived only from the same normalized source passed to the generator, so removed boilerplate cannot be reintroduced.

Chunk generation uses Microsoft's `Microsoft.ML.Tokenizers` with the `cl100k_base` tiktoken BPE data. Counts are real tokenizer counts, not word-based estimates. Romanian, English, mixed-language, and other Unicode content remain .NET strings and PostgreSQL `text` without ASCII conversion.

A chunking failure in initial processing produces `Failed` as described above. A failure during an explicit chunk rebuild preserves the previous authoritative chunks and embeddings and returns the document to `Ready` with a bounded safe stage error.

## Embedding Flow

Application and processing code depend on the small batch-oriented `ITextEmbeddingService`, not OpenAI SDK response types. `OpenAITextEmbeddingService` validates non-empty inputs, divides them into sequential bounded batches, preserves input/output order across batches, and validates result count, dimensions, and finite vector values. `OpenAISdkEmbeddingClient` is the narrow adapter over the official OpenAI .NET SDK and requests the configured dimensions while propagating `CancellationToken`.

The default configuration is:

```json
"OpenAI": {
  "EmbeddingModel": "text-embedding-3-small",
  "EmbeddingDimensions": 1536,
  "BatchSize": 32
}
```

Model and batch size are configurable. Batch size must be between 1 and 128 and defaults to 32; batches are deliberately sequential for predictable ordering, cost, and provider load. Dimensions are represented in configuration and sent explicitly to OpenAI, but the current persistence architecture requires exactly 1536. Startup validation rejects a different value because changing PostgreSQL `vector(1536)` requires an EF migration. Existing chunk sizes of at most 900 `cl100k_base` tokens are comfortably below the selected model's per-input limit, so embedding does not re-chunk or preprocess content. Romanian diacritics, English, mixed-language text, and other Unicode are passed unchanged.

The adapter relies on the official SDK's bounded built-in retry behavior for transient provider and transport failures. There is no second application-level retry loop and no additional resilience package, avoiding stacked retry amplification. The SDK's network-timeout behavior is retained rather than adding a second application timeout layer. Cancellation and permanent configuration or validation failures are not retried indefinitely. A missing API key does not prevent restore, build, tests, migration generation, or EF model inspection; it fails safely only when an embedding request is invoked. The key is read only through backend ASP.NET Core configuration, is not stored in repository configuration or frontend code, and is never logged.

For initial processing and normalization/chunk rebuilds, all OpenAI batches must finish and the full result must pass model, count, order, dimension, and finite-value checks before the database transaction begins. Therefore a failure after one or more successful batches persists none of those partial results. The transaction contains only database replacement and metadata/status updates; it is not held open across OpenAI network calls.

PostgreSQL vector support is registered through Npgsql/EF Core pgvector integration. The EF model enables the `vector` database extension and maps `DocumentChunk.Embedding` to nullable `vector(1536)` for historical compatibility. The migration emits `CREATE EXTENSION IF NOT EXISTS vector`; its rollback removes Milestone 6 columns but deliberately does not drop the pre-existing database-level extension. There is intentionally no HNSW or IVFFlat index and no chosen distance metric in Milestone 6; index, metric, and query shape belong to Milestone 7.

Structured embedding logs contain safe aggregates such as document/project IDs, chunk and batch counts, batch size, model, dimensions, operation, duration, and outcome. They never contain raw or normalized document text, chunk contents, vectors, credentials, secrets, authorization headers, connection strings, or provider response bodies.

Embedding calls occur only for a newly processed upload, explicit processing retry, normalization rebuild, chunk rebuild, or explicit embedding generation/rebuild. GET endpoints, polling, startup, rendering, and recurring background work never create embeddings, and historical documents are not silently backfilled.

## Normalization, Chunk, and Embedding Rebuild Flow

`POST /api/projects/{projectId}/documents/{documentId}/normalization/rebuild` authenticates the user, filters ownership in SQL, requires a `Ready` document with stored raw sections, and atomically changes the document to `Processing` before work begins. It reruns normalization from `DocumentTextSection.Content`, regenerates chunks, generates fresh embeddings, and replaces normalized fields, chunks, vectors, and aggregate metadata in one transaction without opening the uploaded file. If normalization, chunking, or embedding fails before commit, the prior authoritative normalized content, chunks, and embeddings remain intact; the document returns to `Ready` with a bounded safe stage error.

`GET /api/projects/{projectId}/documents/{documentId}/text?view=raw|normalized` returns ordered DTOs with section/page/title data and raw versus normalized character statistics. A normalized request returns conflict until normalization exists. It never returns EF entities, storage names, or physical paths.

`POST /api/projects/{projectId}/documents/{documentId}/chunks/rebuild` authenticates the user, verifies ownership, requires a `Ready` document with stored text sections, and synchronously regenerates chunks from those rows. It does not open the uploaded file or rerun extraction or normalization. It generates fresh embeddings for every replacement chunk and commits chunks, vectors, and metadata together. A failure preserves the previous authoritative chunk/embedding set.

`POST /api/projects/{projectId}/documents/{documentId}/embeddings/rebuild` authenticates the user and enforces document ownership in SQL. It requires existing chunks and rejects conflicting active processing. It reads persisted `DocumentChunk.Content` in chunk-index order and does not open the uploaded file, extract, normalize, or re-chunk. It intentionally regenerates all embeddings even when the existing set is current. After complete external generation, it starts a transaction, reloads the document and chunks, verifies IDs, indexes, and contents are unchanged, and atomically replaces only vector values and embedding metadata. A failure preserves every previous vector and leaves the document readable, while returning a safe error.

Historical `Ready` documents remain readable after migration with nullable vectors and zero/null embedding metadata. API metadata identifies them as not embedded, and the frontend offers explicit Generate Embeddings. They are never embedded automatically because doing so would create unexpected provider cost.

`GET /api/projects/{projectId}/documents/{documentId}/chunks` returns ordered chunk content and source metadata for a Ready document. Vector values and provider storage types are never included in chunk, document, section, list, or detail DTOs; the frontend receives only safe aggregate embedding metadata.

## Background Queue

The queue is a bounded in-memory `Channel<Guid>` with capacity 100 and duplicate scheduling protection. One hosted worker reads document IDs, creates a dependency-injection scope for each job, calls the processing service, logs the outcome, and releases the scheduling marker.

The queue is intentionally not durable. An application restart can lose pending IDs, so Uploaded and Failed documents can be queued manually from the UI. A durable message broker is outside the current milestone.

Queue duplicate markers, conditional database status claims, and document consistency rechecks coordinate work. Rebuild and deletion claims serialize conflicting operations through PostgreSQL row updates; document and project deletion refuse active work. The background queue and its duplicate markers remain process-local, however, and there is no durable distributed job lock. Multiple server instances can still race when scheduling full processing unless deployment adds cross-process coordination; Redis, message brokers, and distributed locking remain outside this milestone.

## Frontend State

The project detail page polls while a document is Uploaded or Processing. A successful refresh replaces local document metadata and clears stale action errors. Available actions are status-specific:

- Uploaded: Process and Delete.
- Processing: state only; no document action buttons.
- Failed: Retry and Delete.
- Ready: inspect raw/normalized text, view chunks, rebuild normalization, rebuild chunks, explicitly generate/rebuild embeddings, and delete. Process and Retry are never rendered.

Normalization, chunk, and embedding rebuilds require browser confirmation; the embedding confirmation also makes API-credit usage explicit. Document cards show concise raw/normalized/chunk statistics plus Embedded, Not embedded, or Needs rebuild state, embedded chunk count, model, dimensions, and time. They never display vectors. Action loading state disables conflicting actions for that document, and successful requests refresh metadata. Normalized text is fetched only while the viewer is open and its normalized tab is selected. No React effect or polling path generates embeddings.

## Completed Milestones

- Milestone 1: Identity registration, login, logout, current user, and cookie authentication.
- Milestone 2: User-owned project CRUD and ownership authorization.
- Milestone 3: PDF and DOCX upload, validation, metadata, local storage, and deletion.
- Milestone 4: PDF/DOCX extraction, background processing, retries, source-section storage, extracted-text viewer, and backend tests.
- Milestone 5: Deterministic multilingual chunking, exact token counts, overlap, chunk persistence, rebuild/list APIs, frontend viewer/rebuild controls, tests, and documentation.
- Milestone 5.1: Raw-preserving extraction normalization, conservative PDF boilerplate/page-number cleanup, safe word-break repair, normalized chunk sources, inspection/rebuild APIs, frontend controls, migration, tests, and documentation.
- Milestone 5.2: General-purpose long page-edge block detection, line-wrap-tolerant canonical comparison, dense local template support, stricter body/heading safeguards, stress tests, and aggregate-only real-PDF verification.
- Milestone 6: OpenAI embedding abstraction and adapter, bounded batch generation, pgvector `vector(1536)` persistence, configuration and staleness metadata, atomic pipeline/rebuild integration, legacy-document generation, safe frontend reporting, tests, migration, and documentation.

## Remaining Roadmap

The next planned milestone is Milestone 7, semantic retrieval/search. It will choose the distance metric, ownership-filtered query shape, and vector index strategy. Query embeddings, retrieval, AI chat, and deployment remain later and separate; see `ROADMAP.md`.
