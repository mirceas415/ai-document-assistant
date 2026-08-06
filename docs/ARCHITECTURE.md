# AI Document Assistant Architecture

## Overview

AI Document Assistant is a modular monolith with an ASP.NET Core 10 backend, a React and TypeScript frontend, PostgreSQL persistence through Entity Framework Core, and local document storage. The application currently supports authenticated, user-owned projects, PDF and DOCX upload, background text extraction, conservative extraction normalization, deterministic retrieval chunk generation, and inspection or rebuilding of normalized content and stored chunks.

Milestone 5.2 deliberately stops before embeddings or retrieval. There are no OpenAI API calls, embedding columns, vector extensions, vector searches, semantic search, classification, chat features, OCR, cloud storage, external message queues, or microservices.

## Components

- `AI.DocumentAssistant.Server` hosts the JSON API, cookie authentication, static frontend output, EF Core data access, file storage, extraction pipeline, in-memory background queue, and chunking services.
- `ai.documentassistant.client` is a React 19 and TypeScript single-page application built with Vite.
- `AI.DocumentAssistant.Server.Tests` contains xUnit tests using EF Core's in-memory provider plus generated PDF and DOCX fixtures.
- PostgreSQL is the production relational database.
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

Stores the project relationship, safe original filename, generated storage filename, MIME type, size, processing status, extraction metadata, chunking metadata, and safe public errors.

Important chunking fields are:

- `ChunkCount`
- `ChunkedAtUtc`
- `ChunkingError`

### DocumentTextSection

The ordered extraction representation. PDF sections normally correspond to pages. DOCX sections correspond to heading groups. `Content` is immutable raw extraction for verification and debugging. Nullable `NormalizedContent` is the retrieval-oriented derivative, with `NormalizationChanged`, `RemovedCharacterCount`, and `NormalizedAtUtc` providing traceability. Existing rows from before Milestone 5.1 have null normalized content until normalization is rebuilt.

The document row stores useful aggregates: normalized character count, removed character count, changed-section count, completion time, and a bounded safe normalization error. Raw content is never overwritten by normalization or chunk rebuilding.

### DocumentChunk

The ordered retrieval unit derived from one or more source sections. It stores content, exact tokenizer count, character count, page range, heading, source-section range, and creation time. `(DocumentId, ChunkIndex)` is unique. Document deletion cascades to chunks.

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
6. Chunks are generated from normalized sections.
7. Existing sections and chunks are replaced together in one transaction, with raw and normalized values stored on each section.
8. The document becomes `Ready` only after extraction, normalization, chunk generation, and persistence all succeed.

An extraction, normalization, or chunk generation failure leaves no partial new sections or chunks, sets the document to `Failed`, stores the bounded safe error in the stage-specific metadata, keeps the uploaded file for retry, and logs the technical exception without logging document text or physical paths.

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
8. Generated chunks replace previous rows inside a transaction. Overlap is derived only from the same normalized source passed to the generator, so removed boilerplate cannot be reintroduced.

Chunk generation uses Microsoft's `Microsoft.ML.Tokenizers` with the `cl100k_base` tiktoken BPE data. Counts are real tokenizer counts, not word-based estimates. Romanian, English, mixed-language, and other Unicode content remain .NET strings and PostgreSQL `text` without ASCII conversion.

A chunking failure removes chunks, retains stored source sections, clears chunk metadata, sets the document to `Failed`, stores a bounded safe `ChunkingError`, keeps the uploaded file, and logs the technical exception.

## Normalization and Chunk Rebuild Flow

`POST /api/projects/{projectId}/documents/{documentId}/normalization/rebuild` authenticates the user, filters ownership in SQL, requires stored raw sections, and atomically changes the document to `Processing` before work begins. This prevents concurrent duplicate rebuilds. It reruns normalization from `DocumentTextSection.Content`, regenerates chunks from normalized results, and transactionally replaces normalized fields and chunk rows without opening the uploaded file. A failure clears partial normalized/chunk data, preserves raw content, and marks the document `Failed` with a safe error.

`GET /api/projects/{projectId}/documents/{documentId}/text?view=raw|normalized` returns ordered DTOs with section/page/title data and raw versus normalized character statistics. A normalized request returns conflict until normalization exists. It never returns EF entities, storage names, or physical paths.

`POST /api/projects/{projectId}/documents/{documentId}/chunks/rebuild` authenticates the user, verifies ownership, requires stored text sections, and synchronously regenerates chunks from those rows. It does not open the uploaded file or rerun extraction. Replacement and metadata updates are transactional.

`GET /api/projects/{projectId}/documents/{documentId}/chunks` returns ordered chunk content and metadata for a Ready document.

## Background Queue

The queue is a bounded in-memory `Channel<Guid>` with capacity 100 and duplicate scheduling protection. One hosted worker reads document IDs, creates a dependency-injection scope for each job, calls the processing service, logs the outcome, and releases the scheduling marker.

The queue is intentionally not durable. An application restart can lose pending IDs, so Uploaded and Failed documents can be queued manually from the UI. A durable message broker is outside the current milestone.

## Frontend State

The project detail page polls while a document is Uploaded or Processing. A successful refresh replaces local document metadata and clears stale action errors. Available actions are status-specific:

- Uploaded: Process and Delete.
- Processing: state only; no document action buttons.
- Failed: Retry and Delete.
- Ready: inspect raw/normalized text, view chunks, rebuild normalization, rebuild chunks, and delete. Process and Retry are never rendered.

Normalization and chunk rebuilds require browser confirmation. Document cards show concise raw/normalized/removed/changed statistics. Normalized text is fetched only while the text viewer is open and the normalized tab is selected. Successful normalization refreshes document metadata and an open chunk viewer.

## Completed Milestones

- Milestone 1: Identity registration, login, logout, current user, and cookie authentication.
- Milestone 2: User-owned project CRUD and ownership authorization.
- Milestone 3: PDF and DOCX upload, validation, metadata, local storage, and deletion.
- Milestone 4: PDF/DOCX extraction, background processing, retries, source-section storage, extracted-text viewer, and backend tests.
- Milestone 5: Deterministic multilingual chunking, exact token counts, overlap, chunk persistence, rebuild/list APIs, frontend viewer/rebuild controls, tests, and documentation.
- Milestone 5.1: Raw-preserving extraction normalization, conservative PDF boilerplate/page-number cleanup, safe word-break repair, normalized chunk sources, inspection/rebuild APIs, frontend controls, migration, tests, and documentation.
- Milestone 5.2: General-purpose long page-edge block detection, line-wrap-tolerant canonical comparison, dense local template support, stricter body/heading safeguards, stress tests, and aggregate-only real-PDF verification.

## Remaining Roadmap

The next planned milestone is embeddings and PostgreSQL vector support. Semantic retrieval, AI chat, and deployment remain later and separate; see `ROADMAP.md`.
