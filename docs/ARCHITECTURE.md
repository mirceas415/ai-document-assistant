# AI Document Assistant Architecture

## Overview

AI Document Assistant is a modular monolith with an ASP.NET Core 10 backend, a React and TypeScript frontend, PostgreSQL persistence through Entity Framework Core and pgvector, OpenAI embedding and answer generation, local document storage, and selective local OCR. The completed core MVP supports authenticated, user-owned projects, PDF and DOCX ingestion, metadata-aware hybrid search across a project, grounded answers with authoritative document citations, and persistent project conversations.

The two main flows are:

```text
INGESTION
Upload -> Technical PDF Analysis -> OCR-aware Extract -> Normalize -> Document Understanding
       -> Chunk -> Embed -> PostgreSQL + pgvector

QUERY
Question -> deterministic preparation + one query embedding
         -> ownership-filtered vector + lexical candidates + Ready M10 metadata signal
         -> RRF unique Top-K chunks -> bounded untrusted context -> OpenAI answer -> citations

CONVERSATION
Persisted conversation -> current question -> hybrid retrieval
  -> bounded non-authoritative recent history + authoritative document context
  -> grounded answer -> persisted assistant message + source snapshots
```

The application deliberately does not include cloud OCR, vision/image understanding, custom computer vision, BM25 tuning, reranking, agents, Semantic Kernel, cloud storage, streaming, or a durable message broker. Milestone 13 adds deterministic retrieval fusion only: M10/M11/M12 ingestion semantics and answer/citation behavior remain unchanged.

## Components

- `AI.DocumentAssistant.Server` hosts the JSON API, cookie authentication, static frontend output, EF Core data access, file storage, ingestion pipeline, in-memory background queue, hybrid retrieval, bounded RAG context construction, and narrow OpenAI embedding and answer adapters.
- `ai.documentassistant.client` is a React 19 and TypeScript single-page application built with Vite.
- `AI.DocumentAssistant.Server.Tests` contains xUnit tests using EF Core's in-memory provider, fakes for every OpenAI boundary, generated PDF and DOCX fixtures, deterministic retrieval evaluation cases, and SQL/model-shape checks for PostgreSQL vector and full-text queries.
- PostgreSQL is the production relational database. The pgvector extension stores embeddings through the provider's first-class `Vector` type; vectors are not manually serialized.
- The local `Uploads` directory stores uploaded PDF and DOCX files under generated filenames. Stored paths and uploaded files are not exposed through the API.

## Entities and Relationships

```text
ApplicationUser 1 ── * Project 1 ── * Document
                                      ├── * DocumentTextSection
                                      ├── * DocumentChunk
                                      ├── 0..1 DocumentUnderstanding 1 ── * DocumentMetadataEntry
                                      └── 0..1 DocumentTechnicalAnalysis 1 ── * DocumentPageTechnicalAnalysis
                         └── * Conversation 1 ── * ConversationMessage
                                                   └── * ConversationMessageSource
```

### ApplicationUser

ASP.NET Core Identity user with a GUID key, display name, email, authentication data, creation time, and owned projects.

### Project

A user-owned workspace with a name, optional description, and timestamps. Deleting a user deletes owned projects; deleting a project deletes its document and conversation database rows.

`Project` remains the backend knowledge, ownership, security, document, and retrieval boundary. The frontend uses **Workspace** as the user-facing term, but routes, API contracts, entity names, relationships, and database tables continue to use `Project`/`ProjectId`.

### Document

Stores the project relationship, safe original filename, generated storage filename, MIME type, size, processing status, extraction, normalization, chunking, and aggregate embedding metadata, plus bounded safe public errors.

Important chunking fields are:

- `ChunkCount`
- `ChunkedAtUtc`
- `ChunkingError`

Embedding aggregates are `EmbeddedChunkCount`, `EmbeddingModel`, `EmbeddingDimensions`, `EmbeddedAtUtc`, and `EmbeddingError`. They cheaply exclude legacy or configuration-mismatched documents without returning vectors. For aggregate candidates, list/detail reads verify every chunk's vector presence, model, dimensions, timestamp, and SHA-256 against its exact current content before returning `EmbeddingsAreCurrent`; the aggregate is not treated as a substitute for chunk-row validation.

Document understanding, technical PDF intelligence, and OCR processing are deliberately not stored as sets of unrelated fields on `Document`. Separate nullable one-to-one relationships keep their lifecycles independent from the document's ingestion/RAG status.

### DocumentTextSection

The ordered extraction representation. PDF sections normally correspond to pages. DOCX sections correspond to heading groups. `Content` is immutable raw extraction for verification and debugging. `ExtractionMethod` records `NativePdf`, `Ocr`, `Docx`, or legacy-compatible `Unknown` provenance. Nullable `NormalizedContent` is the retrieval-oriented derivative, with `NormalizationChanged`, `RemovedCharacterCount`, and `NormalizedAtUtc` providing traceability. Existing rows from before Milestone 5.1 have null normalized content until normalization is rebuilt; existing rows receive `Unknown` provenance through the database default.

The document row stores useful aggregates: normalized character count, removed character count, changed-section count, completion time, and a bounded safe normalization error. Raw content is never overwritten by normalization or chunk rebuilding.

### DocumentChunk

The ordered retrieval unit derived from one or more source sections. It stores content, exact tokenizer count, character count, page range, heading, source-section range, creation time, and nullable embedding data. `(DocumentId, ChunkIndex)` is unique. Document deletion cascades to chunks, so the vector disappears with its owning chunk and no external vector-store cleanup is needed.

The embedding fields are:

- `Embedding`: the provider `Vector` value mapped to PostgreSQL `vector(1536)`;
- `EmbeddingModel` and `EmbeddingDimensions`: the exact configuration that produced it;
- `EmbeddingContentHash`: uppercase hexadecimal SHA-256 of the exact UTF-8 `DocumentChunk.Content` sent for embedding; and
- `EmbeddedAtUtc`: the successful generation time.

The hash is deterministic integrity/staleness metadata, not a security mechanism. An embedding is current only when the vector exists, model and dimensions match current configuration, and the stored hash matches the exact current chunk content. Chunk replacement always creates and embeds new rows; embeddings are never copied by chunk index, page number, or section index.

### DocumentUnderstanding and DocumentMetadataEntry

`DocumentUnderstanding` uses `DocumentId` as both its primary key and foreign key, so a document has at most one current understanding aggregate. Its independent status is `Pending`, `Processing`, `Ready`, `Failed`, or `Skipped`; the absence of a row represents a legacy document that has not been analyzed. A document can therefore remain `Ready` and fully retrievable while understanding is `Failed`.

The aggregate stores the controlled document type, optional short subtype, classification confidence, normalized primary language code and confidence, detected title, subject, requested model identifier, prompt version, uppercase full-normalized-content SHA-256, analysis time, and a bounded safe error/reason. The controlled type taxonomy is `Unknown`, `Contract`, `Invoice`, `Receipt`, `Report`, `Policy`, `Procedure`, `Manual`, `CourseMaterial`, `ResearchPaper`, `FinancialDocument`, `Form`, `Letter`, `Resume`, `TechnicalDocument`, and `Other`.

`DocumentMetadataEntry` is the bounded generic child model rather than a wide set of nullable business columns. Its controlled kinds are `Organization`, `Person`, `Identifier`, `Date`, `MonetaryAmount`, `Jurisdiction`, `Topic`, and `Other`. Each entry stores a semantic lower-snake-case label, sanitized original value, optional deterministic normalized value, optional validated confidence, and stable sequence. At most 50 validated entries are accepted. Successful rebuilds replace all entries atomically; document deletion cascades through understanding to metadata. Conversation source snapshots have no foreign key to these rows and retain their historical behavior.

### DocumentTechnicalAnalysis and DocumentPageTechnicalAnalysis

`DocumentTechnicalAnalysis` uses `DocumentId` as its primary key and foreign key. Its independent status is `NotAnalyzed`, `Processing`, `Ready`, `Failed`, or `Skipped`; absence of a row is valid for pre-Milestone-11 documents. It stores the controlled `TechnicalType`, page totals by type, uppercase SHA-256 of the original file bytes, analyzer version, completion time, and a bounded safe error. `TechnicalType` values mean:

- `TextBased`: a meaningful text layer is the primary useful representation;
- `Scanned`: little or no meaningful text exists and a raster image covers at least 80% of the visible page, which is structurally consistent with a scan;
- `ImageBased`: little or no meaningful text exists and raster images are present, but none meets the scan threshold;
- `Mixed`: meaningful text and substantial raster content coexist, or materially different useful page types coexist; and
- `Unknown`: structural evidence is insufficient for a safe classification.

`DocumentPageTechnicalAnalysis` has the composite key `(DocumentTechnicalAnalysisId, PageNumber)` and stores type, alphanumeric text-character count, useful-word count, non-mask raster-image count, largest-image coverage, meaningful-text and page-sized-image signals. Deleting a document cascades through its technical aggregate to every page diagnostic. These rows are metadata only and never become document text or RAG context.

### DocumentOcrAnalysis and DocumentPageOcrResult

`DocumentOcrAnalysis` is a separate nullable one-to-one document concern. Its status is `NotAnalyzed`, `Processing`, `Ready`, `Partial`, `Failed`, or `Skipped`. It stores bounded aggregate counts, engine name/version, configured languages and rendering limits, source-file hash, routing version/hash, configuration hash, completion time, and a safe bounded error. No tessdata path, native-library path, rendered image, raw exception, or duplicate OCR text is persisted.

`DocumentPageOcrResult` has the composite key `(DocumentOcrAnalysisId, PageNumber)` and exists only for M11 `Scanned` candidates. It stores `Ready`, `Empty`, `Failed`, or `SkippedLimit`, the source technical type, recognized character/word counts, optional mean confidence, effective render DPI and dimensions, optional duration, whether the recognized text was used, and a bounded error. OCR text used by ingestion lives once as the raw `DocumentTextSection.Content` for that page. Document deletion cascades through the OCR aggregate to diagnostics.

### Conversation, ConversationMessage, and ConversationMessageSource

`Conversation` belongs to one project and stores a GUID, a title, and UTC creation/update timestamps. `(ProjectId, UpdatedAtUtc)` supports the newest-first project history. `ConversationMessage` belongs to one conversation, stores only the constrained `User` or `Assistant` role, text, UTC creation time, and sequence; `(ConversationId, Sequence)` is unique. System/developer prompts, vectors, and provider configuration are never stored.

`ConversationMessageSource` belongs to an assistant message and snapshots the validated source ID, nullable document/chunk identifiers, safe document name, chunk index, pages, heading, and at most 500 excerpt characters. `(ConversationMessageId, SourceIndex)` is unique. Its document and chunk identifiers are intentionally scalar snapshots rather than foreign keys. Therefore deleting a document deletes its text, chunks, and vectors but does not erase old answers or their bounded citation displays. Deleting a conversation cascades to its messages and source snapshots; it never deletes documents.

## Authentication and Ownership

ASP.NET Core Identity uses GUID user keys and an HTTP-only application cookie. API authentication failures return JSON `401` or `403` responses instead of redirects.

All project, document, Search, Ask, and conversation endpoints require authentication. Queries include the current user's ID through `Project.OwnerId`. Conversation queries enforce `Conversation -> Project -> OwnerId` in SQL and also require the route project ID, so both cross-user IDs and conversation/project mismatches return not found. Search and Ask verify project ownership before creating a query embedding, then repeat owner and project filters inside the vector SQL query. Consequently, another user's conversation or document text cannot be retrieved or sent to the answer model. Read-only EF queries use `AsNoTracking` where change tracking is unnecessary.

## Upload Flow

1. The authenticated user uploads a document to an owned project.
2. The API validates the 20 MB limit, filename, extension, declared content type, and file signature.
3. The file is saved locally with a generated `.pdf` or `.docx` filename.
4. A `Document` row is created with status `Uploaded`, a separate understanding row with status `Pending`, a technical-analysis row with `NotAnalyzed` for PDF or `Skipped` for DOCX, and an OCR row with `NotAnalyzed` for PDF or `Skipped` for DOCX.
5. The document ID is offered to the process-local background queue.
6. The API returns document metadata; it never returns the storage filename or path.

## Extraction Flow

1. The worker changes an Uploaded or Failed document to `Processing` and clears prior public errors and counts.
2. The technical-analysis service inspects the original stored file. It analyzes PDF locally and marks DOCX `Skipped`; failure is caught at this independent boundary.
3. The processor selects the registered extractor by MIME type and stored extension.
4. PDF extraction first reads native page text in order with PdfPig. M11 page diagnostics then route only `Scanned` pages through PDFium rendering and local Tesseract OCR; successful OCR text replaces that page's ineffective native extraction. Other page types retain existing native extraction behavior.
5. DOCX extraction reads paragraphs and tables in order, grouping Heading 1–3 content and retaining the heading as section metadata.
6. Native and OCR-derived sections are merged in original page order and remain in memory as one raw source representation while normalization runs.
7. Document understanding receives a deterministic bounded input derived only from the normalized sections. Its result or safe failure state is persisted independently.
8. Chunks are generated from normalized sections in memory even when technical analysis or understanding failed.
9. The exact generated chunk contents are embedded and the complete result is validated in memory.
10. Only then does a database transaction replace sections and chunks, attach their vectors and metadata, update document aggregates, and set the document to `Ready`.

The complete flow is:

```text
Upload -> Technical PDF Analysis -> OCR-aware Extract -> Normalize -> Document Understanding -> Chunk -> Embed -> Persist -> Ready
```

Document `Processing` still covers the main extraction-through-embedding flow, while technical analysis and document understanding have independent lifecycles. An extraction, normalization, chunking, or embedding failure during initial/retry processing leaves no partial new authoritative sections, chunks, or embeddings, sets the document to `Failed`, stores a bounded stage-safe error, and keeps the uploaded file for retry. A technical-analysis failure records technical status `Failed`, but extraction and every normal downstream stage still run. Conversely, extraction failure does not delete already completed technical diagnostics. An understanding timeout, rate limit, configuration error, malformed response, or other provider failure records understanding `Failed` but is caught at its stage boundary; chunking and embedding continue and the document can become `Ready`. Retrying reopens the existing uploaded file and runs the complete current pipeline. Technical exceptions are logged without document text, physical paths, vectors, credentials, authorization headers, or provider response bodies.

## Technical PDF Analysis Flow

`IDocumentTechnicalAnalysisService` owns source-file hashing, idempotency, status transitions, and persistence. `IPdfTechnicalAnalyzer` is the narrow, local PDF-inspection boundary implemented by `PdfPigPdfTechnicalAnalyzer`. It uses the already-installed PdfPig package to read each page's text, crop-box dimensions, non-mask raster images, and image placement bounds. It never decodes pixels, rasterizes a page, performs OCR, calls OpenAI, creates embeddings, or uses an external service.

Text measurement counts Unicode letters and digits plus contiguous alphanumeric runs of at least two characters as useful words. A page has meaningful text when it contains at least 40 alphanumeric characters **or** at least 8 useful words. This prevents a page number, small footer, watermark, or a few OCR artifacts from establishing a useful text layer.

For every non-mask raster image with positive sample dimensions, coverage is the intersection of its axis-aligned placement bounds with the visible crop box divided by crop-box area. Every ratio is finite and clamped to `[0, 1]`. The persisted page coverage is the maximum single-image ratio rather than a sum: this is a conservative lower bound on union coverage and cannot double-count overlapping placements. A page-sized image is one with coverage at least 0.80. Image content is substantial at coverage at least 0.30.

Page classification is fixed and ordered:

1. meaningful text plus coverage at least 0.30 → `Mixed`;
2. other meaningful text → `TextBased`;
3. no meaningful text plus a page-sized image (at least 0.80) → `Scanned`;
4. no meaningful text plus one or more raster images → `ImageBased`; and
5. no meaningful text and no raster-image evidence → `Unknown`.

A scan-like page with an existing meaningful hidden/searchable OCR text layer is therefore `Mixed`, not `Scanned`; routing can see both `HasMeaningfulText` and `HasPageSizedImage`. M11 itself performs no OCR. M12 consumes only per-page `Scanned` as an automatic OCR candidate; `TextBased`, `Mixed`, `ImageBased`, and `Unknown` pages are not automatically OCRed.

Document aggregation ignores `Unknown` pages only within a bounded blank-page tolerance: `max(1, floor(total pages × 0.20))`. More unknown pages make the document `Unknown`. Among remaining useful pages, a type representing at least 80% wins. Otherwise, two or more useful types make the document `Mixed`; without enough evidence it remains `Unknown`. Thus one decorative outlier cannot make a 100-page homogeneous document mixed, while a material 70/30 split does. All thresholds belong to `pdf-technical-analysis-v1`; changing their meaning requires a new analyzer version.

The service computes SHA-256 from the original uploaded PDF bytes. Automatic analysis reuses only a `Ready` result whose `SourceFileHash` and `AnalyzerVersion` both match. Manual rebuild always forces inspection and atomic page replacement. No migration/startup backfill or filesystem scan occurs; legacy PDFs return `NotAnalyzed` until explicitly rebuilt or normally reprocessed. DOCX returns `Skipped`/not applicable without opening or inspecting DOCX internals.

The ownership-protected APIs are:

- `GET /api/projects/{projectId}/documents/{documentId}/technical-analysis`, which explicitly returns document aggregates and ordered page diagnostics; and
- `POST /api/projects/{projectId}/documents/{documentId}/technical-analysis/rebuild`, which forces local analysis of the original PDF and rejects DOCX with a safe not-applicable response.

Both routes constrain document ID, route project ID, and `Project.OwnerId`; neither returns a storage name, path, exception, content, secret, or credential.

## Local OCR Routing and Extraction Flow

Milestone 11 answers **“Does this page structurally need OCR?”** Milestone 12 answers **“Recover text locally from pages identified as scans.”** The persisted M11 page diagnostics are the sole routing authority; OCR does not duplicate image-coverage or meaningful-text heuristics. The centralized `ocr-routing-v1` policy is exact:

- `Scanned` -> render that page and run local OCR;
- `TextBased` -> preserve PdfPig native text;
- `Mixed` -> preserve meaningful PdfPig native text and do not OCR;
- `ImageBased` -> preserve existing extraction behavior and do not OCR; and
- `Unknown` -> preserve existing extraction behavior and do not OCR.

Routing is page-level, never based only on the document aggregate. A PDF with native pages 1 and 3 and scanned pages 2 and 4 becomes one ordered sequence: page 1 `NativePdf`, page 2 `Ocr`, page 3 `NativePdf`, page 4 `Ocr`. There is no separate OCR chunk store or OCR-specific RAG path. OCR output is minimally line-ending-cleaned, stored as raw section content, normalized by the existing normalizer, and then enters the unchanged Document Understanding, chunking, embedding, retrieval, and RAG pipeline.

`IPdfPageRenderer` isolates rendering from ingestion and is implemented by `PdfiumPdfPageRenderer` using PDFtoImage/PDFium and SkiaSharp. `IOcrService` isolates recognition and is implemented by `TesseractOcrService` using the TesseractOCR managed/native wrapper with the modern default engine. The processor renders and recognizes one selected page at a time, uses in-memory PNG buffers, disposes bitmaps, streams, OCR pages, and pixel buffers promptly, and reuses one engine only inside its scoped, serialized document-processing lifetime. It never invokes `tesseract.exe`, creates an external process, or writes a rendered page to disk.

Default OCR configuration is enabled, `ron+eng`, 300 DPI, at most 200 candidate pages per document, and at most 25,000,000 rendered pixels per page. Before rendering, page dimensions are converted to requested pixels. Pages exceeding the pixel budget are scaled down proportionally while preserving aspect ratio, and effective dimensions/DPI are recorded. When the candidate maximum is exceeded, aggregate failed counts include every omitted page while one bounded representative diagnostic records the first omitted page as `SkippedLimit`; work and diagnostic-row growth remain bounded. The aggregate is `Partial` when any eligible page succeeded or `Failed` when none did.

Status semantics are independent of `Document.Status`: `Skipped` means non-PDF/not applicable or no scanned candidates; `Ready` means every candidate succeeded; `Partial` means at least one candidate succeeded and at least one did not; and `Failed` means OCR was required but no candidate succeeded or infrastructure was unavailable. An empty/whitespace OCR result becomes an `Empty` page result and contributes no placeholder or error string to searchable content. A failed candidate never removes successful OCR text from other pages. Useful native pages can still take a document to `Ready`; a fully scanned document with no recognized or native text honestly fails normal extraction.

Automatic reuse requires a successful complete OCR analysis matching the original-file SHA-256, engine name/version and traineddata fingerprint, normalized language configuration, render DPI and limits, `ocr-routing-v1`, and the deterministic SHA-256 routing fingerprint built from ordered `PageNumber:TechnicalType` candidates. It also requires corresponding persisted OCR sections. Manual rebuild bypasses reuse. New PDFs run M11 before OCR-aware extraction; a zero-candidate PDF never invokes the renderer or OCR engine. Historical documents are neither backfilled nor scanned at startup and remain valid with no OCR row until normal reprocessing or explicit rebuild.

The ownership-protected APIs are:

- `GET /api/projects/{projectId}/documents/{documentId}/ocr`, returning safe aggregate and candidate-page diagnostics; and
- `POST /api/projects/{projectId}/documents/{documentId}/ocr/rebuild`, requiring PDF and forcing OCR-aware extraction followed by normalization, the existing M10 analysis, chunking, and embeddings.

OCR itself is entirely local and adds zero OpenAI calls. A rebuild that changes extracted text deliberately runs the pre-existing M10 and embedding stages, which can make their normal OpenAI requests. No page image is sent to OpenAI or another OCR/vision provider. Missing native dependencies or traineddata do not fail application startup or DOCX/text-based processing; they produce bounded OCR failure diagnostics only when a scanned page actually requires OCR. Local installation, tessdata, and three-PDF acceptance steps are documented in [`OCR_SETUP.md`](OCR_SETUP.md).

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

## Document Understanding Flow

`IDocumentUnderstandingService` owns orchestration and persistence, while `IDocumentUnderstandingClient` is the narrow provider boundary over the already-installed official OpenAI .NET Responses SDK. `OpenAI:DocumentUnderstandingModel` is configurable and falls back to the configured answer model when omitted; the same backend-only `OpenAI:ApiKey` is reused. Responses have provider storage disabled, low reasoning effort, and a strict JSON-schema text format. No alternate HTTP client or AI SDK is used.

Normalized document text is untrusted data. The system instruction limits the task to classification, primary-language detection, title/subject detection, and explicitly supported metadata. It says not to follow instructions embedded in a document, reveal secrets or system prompts, call tools, or perform external actions. The request provides no tools. The strict schema constrains the main classification and metadata kinds, but provider output is still treated as untrusted: local code rejects unsupported enums, malformed/empty responses, non-finite or out-of-range confidence, invalid language codes, excessive counts, overlong values, unexpected nulls, and invalid labels before persistence.

The input builder uses the existing `cl100k_base` tokenizer and normalized sections only. It builds ordered content with page and heading markers where available. If the annotated document fits the 6,000-token content budget, the full text is used. Otherwise a deterministic, non-overlapping representative sample reserves schema/marker safety space and allocates approximately 50% to the beginning, 25% around the middle, and 25% to the end. The complete ordered normalized content—not merely the sample—is hashed with SHA-256, so any source change can invalidate the result. Blank, extremely small, or noisy extraction below the deterministic 20-token/40-alphanumeric-character threshold makes understanding `Skipped` with `Insufficient normalized text`; no OpenAI request is made. OCR and image/PDF technical classification are not performed.

The provider call is never held inside a database transaction. The service first claims the independent understanding state as `Processing`, calls the provider, validates and normalizes the entire response, then atomically replaces the current classification and all metadata entries. Text values receive conservative whitespace normalization; unambiguous supported dates become ISO `yyyy-MM-dd`, while unsafe or ambiguous normalization remains null. Duplicate `(kind, label, normalized-or-original value)` entries are removed deterministically.

Automatic execution reuses a `Ready` or `Skipped` result when full-content hash, resolved model, and `document-understanding-v1` prompt version all match. A manual rebuild forces a new request. A changed normalization, model, or prompt version triggers analysis; successful normalization rebuilds invoke the same non-fatal understanding path after their authoritative normalized text commits. Provider or validation failure stores only a bounded safe `Failed` state and cannot make an otherwise usable document fail chunking, embedding, semantic retrieval, or RAG.

The ownership-protected APIs are:

- `GET /api/projects/{projectId}/documents/{documentId}/understanding` for current safe status, classification, language, metadata, and bounded audit fields;
- `POST /api/projects/{projectId}/documents/{documentId}/understanding/rebuild` for a forced analysis of persisted normalized text.

Both document ID and route project ID are joined through `Project.OwnerId` in database queries. Legacy documents with no understanding row return `NotAnalyzed` and remain valid. Understanding metadata is deliberately not copied to chunks, injected into RAG context, used to rewrite queries, filter retrieval, or alter pgvector ranking in Milestone 10.

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
  "BatchSize": 32,
  "DocumentUnderstandingModel": "gpt-5.6-luna"
}
```

Model and batch size are configurable. Batch size must be between 1 and 128 and defaults to 32; batches are deliberately sequential for predictable ordering, cost, and provider load. Dimensions are represented in configuration and sent explicitly to OpenAI, but the current persistence architecture requires exactly 1536. Startup validation rejects a different value because changing PostgreSQL `vector(1536)` requires an EF migration. Existing chunk sizes of at most 900 `cl100k_base` tokens are comfortably below the selected model's per-input limit, so embedding does not re-chunk or preprocess content. Romanian diacritics, English, mixed-language text, and other Unicode are passed unchanged.

The adapter relies on the official SDK's bounded built-in retry behavior for transient provider and transport failures. There is no second application-level retry loop and no additional resilience package, avoiding stacked retry amplification. The SDK's network-timeout behavior is retained rather than adding a second application timeout layer. Cancellation and permanent configuration or validation failures are not retried indefinitely. A missing API key does not prevent restore, build, tests, migration generation, or EF model inspection; it fails safely only when an embedding request is invoked. The key is read only through backend ASP.NET Core configuration, is not stored in repository configuration or frontend code, and is never logged.

For initial processing and normalization/chunk rebuilds, all OpenAI batches must finish and the full result must pass model, count, order, dimension, and finite-value checks before the database transaction begins. Therefore a failure after one or more successful batches persists none of those partial results. The transaction contains only database replacement and metadata/status updates; it is not held open across OpenAI network calls.

PostgreSQL vector support is registered through Npgsql/EF Core pgvector integration. The EF model enables the `vector` database extension and maps `DocumentChunk.Embedding` to nullable `vector(1536)` for historical compatibility. The migration emits `CREATE EXTENSION IF NOT EXISTS vector`; its rollback removes Milestone 6 columns but deliberately does not drop the pre-existing database-level extension. There is intentionally no HNSW or IVFFlat index and no chosen distance metric in Milestone 6; index, metric, and query shape belong to Milestone 7.

Structured embedding logs contain safe aggregates such as document/project IDs, chunk and batch counts, batch size, model, dimensions, operation, duration, and outcome. They never contain raw or normalized document text, chunk contents, vectors, credentials, secrets, authorization headers, connection strings, or provider response bodies.

Embedding calls occur only for a newly processed upload, explicit processing retry, normalization rebuild, chunk rebuild, or explicit embedding generation/rebuild. GET endpoints, polling, startup, rendering, and recurring background work never create embeddings, and historical documents are not silently backfilled.

## Metadata-aware Hybrid Retrieval Flow

`POST /api/projects/{projectId}/search` accepts a trimmed query and optional `TopK`. The query is required, whitespace-only input is rejected, and the maximum length is 2,000 characters. `TopK` defaults to 8 and is bounded from 1 through 20. The endpoint returns ranked safe chunk metadata plus optional hybrid diagnostics; it never returns an embedding, storage filename, physical path, or owner ID.

`ISemanticRetrievalService` remains the single shared orchestration boundary used by both Search and Ask. It first verifies `(Project.Id, Project.OwnerId)` in the database. Only after that succeeds does deterministic local query preparation run and `ITextEmbeddingService` get called exactly once with a one-item batch containing the trimmed original query. The returned model, count, 1,536 dimensions, and finite values are validated against current embedding configuration. Query embeddings are transient and are never saved, serialized, returned, or logged. M13 adds no query-classification, rewrite, entity-extraction, HyDE, or other LLM request.

The bounded M13 candidate-generation and M14 selection shape is:

```text
Question -> one query embedding -> exact pgvector candidates (default 30)
         -> original text -> PostgreSQL full-text candidates (default 30)
         -> deterministic hints -> Ready M10 document matches (default 20)
         -> weighted reciprocal-rank fusion and ChunkId deduplication
         -> best bounded hybrid candidates for reranking (default 18, maximum 30)
         -> one optional model reranking request
         -> validated unique final TopK chunks (default 8, maximum 20)
         -> existing RagContextBuilder and grounded answer generation
```

`ISemanticChunkSearch` is the small PostgreSQL-specific vector-query boundary. `PgvectorSemanticChunkSearch` issues one parameterized command whose essential shape is:

```sql
SELECT safe_chunk_and_document_columns,
       c."Embedding" <=> @query_embedding AS "CosineDistance"
FROM "DocumentChunks" AS c
JOIN "Documents" AS d ON d."Id" = c."DocumentId"
JOIN "Projects" AS p ON p."Id" = d."ProjectId"
WHERE p."Id" = @project_id
  AND p."OwnerId" = @owner_id
  AND d."ProjectId" = @project_id
  AND d."Status" = 'Ready'
  AND d."ChunkCount" > 0
  AND d."EmbeddedChunkCount" = d."ChunkCount"
  AND d."EmbeddingModel" = @embedding_model
  AND d."EmbeddingDimensions" = @embedding_dimensions
  AND d."EmbeddedAtUtc" IS NOT NULL
  AND c."Embedding" IS NOT NULL
  AND c."EmbeddingModel" = @embedding_model
  AND c."EmbeddingDimensions" = @embedding_dimensions
  AND c."EmbeddedAtUtc" = d."EmbeddedAtUtc"
  AND c."EmbeddingContentHash" = upper(
      encode(sha256(convert_to(c."Content", 'UTF8')), 'hex'))
ORDER BY c."Embedding" <=> @query_embedding,
         d."Id", c."ChunkIndex", c."Id"
LIMIT @top_k;
```

The displayed literal `'Ready'` is also supplied as a parameter in the implementation. Project, owner, status, model, dimensions, query vector, and limit are parameters; document text and vector values are not interpolated into SQL. Ownership, project scope, document state, aggregate currency, per-chunk vector metadata, timestamp agreement, and exact SHA-256 hash freshness are therefore enforced before/as part of nearest-neighbor retrieval. PostgreSQL performs cosine ordering; the application never loads vectors to calculate similarity in memory and never globally retrieves chunks before filtering.

Cosine distance was chosen because it compares embedding direction and is the conventional pgvector metric for semantic OpenAI embeddings. The `<=>` operator returns cosine distance: a smaller value means a closer semantic match. It is diagnostic data, not a percentage or confidence score. Romanian, English, mixed-language, and Unicode queries pass unchanged to the multilingual embedding model; there is no translation or document-specific tuning. The embedding model, dimensions, freshness predicates, and exact pgvector search semantics are unchanged.

### Lexical candidates

`DocumentChunk.SearchVector` is a PostgreSQL-generated stored `tsvector` derived automatically from `Content` with the language-neutral `simple` configuration. `AddHybridRetrievalIndexes` adds that column and `IX_DocumentChunks_SearchVector`, a GIN index. Applications never synchronize the vector manually. `ILexicalChunkSearch` uses the indexed `@@` operator and `ts_rank_cd`, with stable document/chunk tie breakers and a bounded default candidate count of 30 (hard maximum 100).

The raw trimmed question is supplied as a parameter to `websearch_to_tsquery('simple', @query_text)`. A second parameter contains only the analyzer's bounded significant terms joined as plain text, which avoids requiring conversational filler words to occur in a chunk while the original query still drives phrase/operator matching. User text is never concatenated into SQL or tsquery syntax. PostgreSQL web-search parsing gives forgiving behavior for quotes, parentheses, punctuation, apostrophes, `OR`/`AND`-like words, identifiers, and Unicode; an effectively empty tsquery simply yields no lexical candidates. The lexical SQL independently joins `Projects`, filters owner and route project, and requires a Ready document before ranking. It does not require a usable embedding, so lexical retrieval can degrade independently while the vector channel retains its existing freshness rules.

### Deterministic metadata document signal

`IRetrievalQueryAnalyzer` performs bounded local Unicode normalization without changing the original text used for embeddings or FTS. It retains identifier forms such as `CN-2026-00491`, `INV/2026/118`, and `AB_9917`; recognizes only unambiguous ISO/dotted dates; and canonicalizes written amount/currency pairs such as `18,500 EUR` and `18500 EUR` for exact comparison. A small explicit alias map recognizes Contract (`contract`, `contractul`, `contracte`), Invoice (`invoice`, `factura`, `factură`, `facturi`), Report/`raport`, Policy/`politică`, Procedure/`procedură`, Manual, Form/`formular`, Letter/`scrisoare`, Resume/CV, Research Paper, and Course Material/`course`/`curs`. These hints are soft signals, not filters or NLP classification.

`IMetadataDocumentSearch` ranks at most 20 documents by default (hard maximum 50). Its one parameterized PostgreSQL query starts from Ready documents in the owned route project, inner-joins only `DocumentUnderstanding.Status == Ready`, and only then examines their `DocumentMetadataEntry` rows. Failed, Processing, Pending, Skipped, absent, or staged-stale understanding is ignored. Exact normalized Identifier matches rank most strongly; conservative Date and MonetaryAmount matches, document type/subtype, Organization and other metadata values, original filename, detected title, and subject contribute bounded deterministic document-ranking signals. Primary language never becomes an automatic filter. M11/M12 technical/OCR classifications are not queried.

Legacy Ready documents with no M10 row remain candidates through vector and lexical retrieval. A metadata match never creates a chunk candidate: it contributes only to vector/lexical candidate chunks that already belong to that document. Debug output contains at most three matched metadata/document signals and never exposes all metadata.

### Rank fusion

Raw cosine distance and `ts_rank_cd` are never added because their scales differ. `IHybridRetrievalFusion` uses weighted reciprocal rank fusion (RRF):

```text
score(chunk) = 1.00 / (60 + vector rank)
             + 1.00 / (60 + lexical rank)
             + 0.35 * exactIdentifierMultiplier / (60 + metadata document rank)

exactIdentifierMultiplier = 1.50 for an exact persisted Identifier match,
                            otherwise 1.00
```

Missing channels contribute zero. The deliberately smaller ordinary metadata weight improves prioritization without hard-excluding stronger chunk evidence; the exact-Identifier multiplier remains smaller than an evidence channel. Candidates are deduplicated by `ChunkId` while retaining every channel rank. Ties are resolved by best individual rank, `DocumentId`, `ChunkIndex`, and `ChunkId`. M13 returns the best bounded unique candidates to M14 when reranking can change final selection; only the requested final TopK enters RAG, so neither candidate pool increases the existing context budget.

### Vector index decision

Milestone 7 intentionally uses an exact filtered scan and adds no HNSW or IVFFlat index. The expected MVP data volume is modest, while owner/project/current-embedding predicates are selective and correctness is more important than approximate recall. With an approximate index, PostgreSQL can apply filters after the index scan and return fewer eligible neighbors unless deployment-specific tuning and iterative-scan behavior are evaluated. Deferring HNSW therefore avoids silently losing project-scoped results. If measured post-MVP volume justifies it, evaluate one `vector_cosine_ops` HNSW index against representative multi-project data and the deployed pgvector version before adding a migration. IVFFlat and HNSW must not be introduced together without evidence.

## Model-based Reranking Flow

M13 remains the recall-oriented candidate generator. M14 adds precision-oriented comparison only after vector/lexical/metadata fusion and `ChunkId` deduplication, and before final TopK selection. `SemanticRetrievalService` remains the single shared Search/Ask orchestration path; `RagContextBuilder`, answer prompting, citation validation, and source mapping are unchanged.

```text
M13 vector + lexical + metadata candidates
  -> weighted RRF and deterministic unique hybrid order
  -> best 18 candidates by default (hard/configurable maximum 30)
  -> one IRetrievalReranker batch request when useful
  -> locally validated model order
  -> omitted candidates appended in original hybrid order
  -> final requested TopK (default 8, maximum 20)
  -> existing bounded RagContextBuilder
  -> existing grounded answer request
```

`Retrieval:Reranking` controls `Enabled`, `CandidateCount` (18), `MaxCandidateCount` (30), `MaxInputTokens` (12,000), `MaxCandidateTokens` (700), and `TimeoutSeconds` (30). The existing tokenizer deterministically accounts for the actual reranking user input. Each candidate is represented by an opaque `C1`…`C30` ID, bounded filename, page label, optional heading, and a transient prefix-truncated copy of `DocumentChunk.Content`; persisted content is never modified. The original current question is sent separately in the same structured payload. Conversation history, assistant answers, vectors, embeddings, RRF scores, secrets, and provider configuration are not supplied.

`IRetrievalReranker` is the provider-independent boundary. `OpenAIRetrievalReranker` uses the installed official OpenAI .NET SDK and Responses API. It resolves `OpenAI:RerankingModel`, falling back to `OpenAI:AnswerModel` only when the reranking model is omitted. The default configuration uses `gpt-5.6-sol`, low reasoning effort, a maximum output of 800 tokens, strict JSON Schema, no tools, and `StoredOutputEnabled = false`. The schema returns only an ordered list of candidate IDs with an integer relevance grade: 4 directly answering/strong support, 3 highly relevant, 2 somewhat relevant, 1 weak, and 0 irrelevant. No rationale, answer, summary, or citation is requested or persisted.

Candidate text is explicitly delimited as untrusted data in a higher-priority instruction that forbids following embedded commands, changing the ranking task, revealing prompts/secrets/configuration, answering the question, or using tools. Provider output is also untrusted. Locally, IDs must map to supplied candidates, first occurrences win, relevance must be 0–4, and list size must remain bounded. Unknown IDs are discarded; omitted or unsent candidates append in original M13 order. An empty, null, overlong, out-of-range, or otherwise unusable result causes complete fallback to M13 ordering. A provider can reorder existing secure candidates but can never create a chunk.

Reranking is skipped when disabled, when there are zero or one candidates, or when the candidate count cannot improve requested TopK selection. There is one logical provider attempt with a 30-second linked timeout and no application-level retry loop. Timeout, rate limit, outage, malformed output, missing configuration, or unexpected provider failure produces a bounded warning and fail-open hybrid results. An actual caller-request cancellation still propagates. Logs contain counts, approximate token use, applied/fallback flags, duration, model name, and exception type/status where available—not questions, chunks, prompts, raw responses, vectors, or secrets.

Search diagnostics add final, hybrid, and reranked ranks, ordinal relevance, plus request-level `RerankingApplied`/`RerankingFallback`; existing vector, lexical, metadata, and fused-score details remain. These are diagnostics only. M10 metadata, filenames, headings, and reranker grades do not become answer evidence or citations. Only selected `DocumentChunk.Content` enters the unchanged RAG context. No M14 database migration or package was added.

## RAG / Ask Your Documents Flow

`POST /api/projects/{projectId}/ask` remains a stateless compatibility endpoint accepting one required, trimmed question of at most 2,000 characters. There is no client-selectable model. `IProjectQuestionAnsweringService` performs the shared grounded-answer sequence:

```text
authenticate and validate
  -> verify owned project and create one query embedding
  -> reuse ISemanticRetrievalService for bounded vector + lexical + metadata fusion
  -> optionally make one bounded batch reranking request over fused candidates
  -> validate/fail open and select unique TopK 8 authoritative chunks
  -> build bounded context from retrieved chunks
  -> call IGroundedAnswerService once
  -> validate cited source IDs
  -> return answer and authoritative sources
```

The existing configured answer model is `gpt-5.6-luna`, preserved for its economical multilingual grounded-answer use. `OpenAIResponsesAnswerClient` uses the already-installed official OpenAI .NET SDK's Responses API. Requests use low reasoning effort, cap answer output at 700 tokens, and set `StoredOutputEnabled = false`. The adapter is lazy, uses the API key only through backend configuration, and reduces provider failures to a safe application error. No unofficial SDK, Semantic Kernel, tool loop, hidden self-critique, or second answer call is used. A normal reranked Ask now performs one query-embedding request, one bounded reranking request, and one unchanged answer-generation request; skipped or failed-open reranking does not add a replacement model call.

### Bounded context

`IRagContextBuilder`/`RagContextBuilder` deterministically converts ranked `RetrievedDocumentChunk` values into source blocks identified as `[S1]`, `[S2]`, and so on. Each block carries the safe document name, page range when known, one-based displayed chunk number, heading when known, and authoritative chunk content. Duplicate chunk IDs and exact duplicate contents are omitted. Only retrieved chunks are eligible; whole documents are never added. If the final source would exceed the budget, its content is truncated at a tokenizer boundary and context construction stops.

The default context budget is 6,000 approximate tokens. Counting uses the existing `cl100k_base` tokenizer as a conservative deterministic estimate; the selected answer model may not use that exact tokenizer, so this is explicitly a budget estimate rather than exact provider accounting. Delimiters and source framing count toward the budget:

```text
<BEGIN_UNTRUSTED_DOCUMENT_CONTEXT>
[S1]
Document: ...
Page: ...
Chunk: ...
Heading: ...
Content:
...
---
<END_UNTRUSTED_DOCUMENT_CONTEXT>
```

### Prompt-injection boundary and grounding

Retrieved document content is placed in the user input inside explicit untrusted-data delimiters, never promoted into system/developer instructions. The higher-priority grounding instructions state that document content is data, not instructions; instructions, commands, role changes, or requests found in it must be ignored; commands must not be executed; and system instructions, hidden prompts, secrets, API keys, credentials, authorization data, and internal configuration must never be revealed.

The model is told to answer only from factual evidence in the supplied context, not to fill gaps with world knowledge, and to decline clearly when evidence is insufficient. It answers naturally in the question's language, including Romanian and English. No brittle distance threshold is applied: retrieval supplies ranked evidence and the grounded-answer policy handles relevance. If retrieval returns zero eligible chunks, or no source fits the context budget, the application returns a localized no-information response without calling the answer model.

### Citations and authoritative sources

The model may cite only local IDs such as `[S1]`. The backend retains the authoritative mapping from every supplied source ID to its retrieved database chunk. After generation it extracts citation IDs, normalizes them, discards unknown/hallucinated IDs, and removes unknown citation markers from the answer. Only validated IDs become structured source DTOs. Document IDs, chunk IDs, indexes, pages, headings, and excerpts come from the retrieved database record, never from model-generated metadata. Excerpts preserve Unicode, are bounded to 500 characters by default, and avoid splitting UTF-16 surrogate pairs. Vectors and internal filenames are absent from both answer and source contracts.

### Cost, failure, and privacy behavior

A normal Ask request costs one query-embedding request plus one Responses API request. Search costs only the one query-embedding request. Zero-result Ask skips answer generation. Context, output, Top-K, source-excerpt size, and recent conversation context are bounded. No title or history-summary model call is made.

Structured logs contain project ID, Top-K, result/source counts, model names, dimensions, approximate context tokens, duration, outcome, and safe provider status/type when applicable. They omit the API key, vectors, full question, document/chunk text, prompt/context, generated answer, provider response body, authorization headers, storage paths, and connection strings. Provider or database failures return safe retryable API errors and do not mutate retrieval data or persist a fake answer.

## Conversation UX and Message Flow

The project-scoped conversation API is:

- `GET /api/projects/{projectId}/conversations`: newest-first summaries;
- `POST /api/projects/{projectId}/conversations`: empty `New chat` creation;
- `GET /api/projects/{projectId}/conversations/{conversationId}`: ordered messages and persisted sources;
- `PATCH /api/projects/{projectId}/conversations/{conversationId}`: trimmed title rename, maximum 80 characters;
- `DELETE /api/projects/{projectId}/conversations/{conversationId}`: cascaded chat deletion only; and
- `POST /api/projects/{projectId}/conversations/{conversationId}/messages`: conversation-scoped Ask.

The message flow is:

```text
authenticate -> SQL-filter owned project/conversation -> validate current question
  -> persist User message and deterministic first-question title
  -> current question drives existing ISemanticRetrievalService
  -> build bounded document context + separately bounded recent conversation context
  -> existing IGroundedAnswerService generates one grounded answer
  -> persist Assistant message + backend-authoritative source snapshots
  -> update Conversation.UpdatedAtUtc
```

Titles begin as `New chat` and, on the first question, become a whitespace-normalized Unicode-safe prefix of that question capped at 72 characters. Rename is manual; neither path calls OpenAI.

### Conversation history versus document evidence

At most six recent messages and at most 1,200 approximate `cl100k_base` tokens are supplied by default. Both limits are backend configuration (`RecentConversationMessageCount` maximum 12 and `MaxConversationContextTokens` maximum 4,000). The current question alone remains the retrieval query. Recent messages are wrapped in `<BEGIN_NON_AUTHORITATIVE_CONVERSATION_CONTEXT>` delimiters and may help resolve conversational wording, but the fixed grounding instruction says they are not document evidence, previous assistant answers cannot support factual claims, and history cannot be cited. Retrieved chunks remain the only authoritative evidence in the separate untrusted-document context.

No entire/unbounded conversation, history summary, prior source excerpt, or previous answer is used for retrieval or promoted into the instruction hierarchy. A normal conversation message therefore keeps the existing cost of one query embedding plus at most one answer request.

### Persistence, failure, and privacy

The user message and title/update timestamp commit before retrieval and provider I/O; no database transaction is held across OpenAI calls. The assistant message and all authoritative source snapshots commit together only after successful grounded generation. If retrieval or answer generation fails, the user message remains visible and retryable, while no fake assistant message or partial source rows are created. The UI retains the failed question and exposes one user-triggered retry. Its optional retry ID is accepted only when it identifies the conversation's last user message with the same text, preventing a duplicate user entry; there is no automatic expensive retry loop.

Conversation DTOs omit owner IDs, embeddings, vectors, storage names/paths, prompts, model selection, and secrets. Logs record only safe IDs/counts/timing/model metadata, never full conversation text. Persisted sources come exclusively from the backend-validated RAG mapping, not from frontend input or model-invented metadata.

### Chat workspace

The lightweight History API router supports `/projects/{projectId}`, `/projects/{projectId}/chats/{conversationId}`, and `/projects/{projectId}/documents`. The chat-first workspace uses a compact application sidebar, current-workspace/document count, local-time grouped and filterable history, focused user/assistant messages, anchored composer, loading/error/retry states, an accessible rename/delete overflow menu, and compact source cards with a snapshot modal. Basic paragraphs, lists, bold text, and citation markers are rendered as React nodes without `dangerouslySetInnerHTML` or a Markdown dependency.

The chat composer and main chat drop surface reuse the existing document upload endpoint, frontend PDF/DOCX validation, Toast provider, and project document-status reads. A document uploaded from chat is stored under the current project/workspace, enters the normal extraction → normalization → chunking → embedding pipeline, and is reusable by every conversation in that workspace. `Conversation` does not own documents, there is no conversation/document join, and upload triggers no answer-generation request. Existing Ready documents remain usable while a recent upload processes; compact transient status chips reflect backend document state through the chat workspace's single conditional status-polling loop.

Full document status, raw/normalized text, chunks, embedding generation/rebuild, processing controls, and deletion remain on the Documents route. Hybrid Search diagnostics remain available under a collapsed `Retrieval details` control instead of dominating the normal chat experience. Desktop uses the available three-column width; the history/sidebar progressively collapse on smaller screens.

## Technical Analysis, OCR, Normalization, Understanding, Chunk, and Embedding Rebuild Flow

`POST /api/projects/{projectId}/documents/{documentId}/technical-analysis/rebuild` authenticates the user, enforces the route project/document relationship and owner in SQL, requires PDF, and forces re-analysis from the original uploaded bytes. It does not change the main `Document.Status`, extraction, normalization, understanding, chunks, embeddings, retrieval, or RAG. Its independent failure state is safe and bounded.

`POST /api/projects/{projectId}/documents/{documentId}/ocr/rebuild` applies the same route consistency and ownership rules, requires a completed PDF, and forces the current page-level OCR route even when hashes match. It reuses the normal processing orchestration from technical analysis through OCR-aware extraction, normalization, M10, chunking, and embeddings. Successful replacement makes OCR-derived raw text and all downstream representations current together. For an already Ready document, a downstream rebuild failure preserves the prior authoritative sections/chunks/embeddings and restores Ready with a safe stage error; OCR's independent result still reports the attempted page outcomes.

`POST /api/projects/{projectId}/documents/{documentId}/normalization/rebuild` authenticates the user, filters ownership in SQL, requires a `Ready` document with stored raw sections, and atomically changes the document to `Processing` before work begins. It reruns normalization from `DocumentTextSection.Content`, regenerates chunks, generates fresh embeddings, and replaces normalized fields, chunks, vectors, and aggregate metadata in one transaction without opening the uploaded file. If normalization, chunking, or embedding fails before commit, the prior authoritative normalized content, chunks, and embeddings remain intact; the document returns to `Ready` with a bounded safe stage error.

After a successful normalization commit, the non-fatal understanding path compares the new full normalized-content hash, model, and prompt version. It reuses an identical current result or analyzes the changed source. Understanding failure leaves the successfully rebuilt document, chunks, embeddings, and `Ready` status intact.

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

The project detail page polls while a document is Uploaded or Processing. A successful refresh replaces local document metadata and clears stale action errors. Available document actions are status-specific:

- Uploaded: Process and Delete.
- Processing: state only; no document action buttons.
- Failed: Retry and Delete.
- Ready: inspect raw/normalized text, view chunks, rebuild normalization, rebuild chunks, explicitly generate/rebuild embeddings, and delete. Process and Retry are never rendered.

Technical analysis, OCR, normalization, understanding, chunk, and embedding rebuilds require browser confirmation. AI-backed confirmations make API-credit usage explicit; the OCR confirmation distinguishes local recognition from the existing downstream M10/embedding calls. Document cards show concise raw/normalized/chunk statistics plus Embedded, Not embedded, or Needs rebuild state, embedded chunk count, model, dimensions, and time. They never display vectors. Compact lazy-loaded Technical Analysis and OCR disclosures show their independent states. OCR summarizes pages processed, engine, Romanian + English languages, and target DPI; Advanced shows only candidate pages with status, characters, confidence, source type, and effective DPI. DOCX displays `Not applicable to DOCX`. A separate lazy-loaded Document Intelligence disclosure retains its independent M10 states and business metadata. Action loading state disables conflicting actions for that document, and successful requests refresh metadata. Polling observes statuses but never initiates technical analysis, OCR, or an AI request.

Document management is intentionally separate from the focused chat workspace. The chat UI persists project conversations and renders only backend-authoritative source snapshots. Hybrid Search is retained as a collapsed advanced retrieval inspector with ranked document/chunk/page/heading/content results, fused/vector/lexical ranks, optional cosine/lexical scores, and a bounded matched-metadata summary. These fields explain retrieval only; neither metadata nor filenames become answer evidence. Neither view receives vectors, provider configuration, prompts, storage paths, or secrets.

## Completed Milestones

- Milestone 1: Identity registration, login, logout, current user, and cookie authentication.
- Milestone 2: User-owned project CRUD and ownership authorization.
- Milestone 3: PDF and DOCX upload, validation, metadata, local storage, and deletion.
- Milestone 4: PDF/DOCX extraction, background processing, retries, source-section storage, extracted-text viewer, and backend tests.
- Milestone 5: Deterministic multilingual chunking, exact token counts, overlap, chunk persistence, rebuild/list APIs, frontend viewer/rebuild controls, tests, and documentation.
- Milestone 5.1: Raw-preserving extraction normalization, conservative PDF boilerplate/page-number cleanup, safe word-break repair, normalized chunk sources, inspection/rebuild APIs, frontend controls, migration, tests, and documentation.
- Milestone 5.2: General-purpose long page-edge block detection, line-wrap-tolerant canonical comparison, dense local template support, stricter body/heading safeguards, stress tests, and aggregate-only real-PDF verification.
- Milestone 6: OpenAI embedding abstraction and adapter, bounded batch generation, pgvector `vector(1536)` persistence, configuration and staleness metadata, atomic pipeline/rebuild integration, legacy-document generation, safe frontend reporting, tests, migration, and documentation.
- Milestone 7: Project-scoped semantic retrieval, one transient query embedding, parameterized ownership- and freshness-filtered pgvector cosine search, bounded Top-K, safe results, retrieval debug UI, tests, and exact-search index decision.
- Milestone 8: Single-turn Ask Your Documents, bounded untrusted context, official OpenAI Responses answer generation, grounded multilingual answers, validated authoritative citations, no-evidence behavior, frontend UI, tests, and documentation.
- Milestone 9: Persistent project conversations, bounded non-authoritative recent history, source snapshots, ownership-safe CRUD/message APIs, deterministic titles, failure/retry semantics, focused chat workspace, separate document management, tests, migration, and documentation.
- Milestone 9.2: Chat-first workspace terminology and navigation, simplified empty states, composer and drag/drop workspace upload, backend-backed transient upload status, accessible conversation actions, shared frontend upload validation, and no backend/schema/AI-call changes.
- Milestone 10: Generic document classification, primary-language detection, bounded document metadata extraction, deterministic sampling, strict and locally validated structured output, independent non-fatal status, content/model/prompt idempotency, ownership-safe APIs, Document Intelligence UI, migration, and fake-based tests.
- Milestone 11: Original-file PDF hashing, deterministic PdfPig text/image/page diagnostics, conservative text/scanned/image/mixed classification, OCR-ready page signals, independent non-fatal persistence, ownership-safe read/rebuild APIs, compact UI, migration, and offline tests.
- Milestone 12: Page-selective local PDFium rendering and Tesseract OCR for M11 `Scanned` pages, unified extraction provenance, bounded diagnostics and resources, content/configuration/routing idempotency, ownership-safe read/forced-rebuild APIs, compact UI, migration, offline fake-based tests, and local setup documentation.
- Milestone 13: Language-neutral PostgreSQL FTS, Ready-M10 metadata document signals, deterministic identifier/date/amount/type hints, bounded weighted RRF over vector and lexical chunk candidates, ownership-safe queries, compact advanced diagnostics, migration, and offline regression evaluation.

## Core MVP Status and Limitations

The core bachelor's-project MVP and ingestion-intelligence foundation are complete through Milestone 14. Deployment remains a separate operational milestone. Current intentional limitations include local file storage, a process-local non-durable processing queue, OCR limited to M11 `Scanned` pages and locally installed Romanian/English Tesseract models, no layout/table/image understanding, exact vector search sized for MVP data, deterministic metadata matching, bounded request-time model reranking rather than a trained domain reranker, approximate tokenizer accounting for answer and recent-history context, non-streaming answers, no document-scoped chat filter, no full Markdown engine, and no large formal retrieval/answer benchmark beyond the small deterministic/manual guides.

Broader image/vision policies, durable queues, cloud/object storage, streaming, a larger evaluation framework, observability, and deployment hardening remain future work. M11 still uses only deterministic structural heuristics; M12 consumes its `Scanned` page result without changing classification. Technical PDF intelligence asks “How is this PDF represented?”, local OCR asks “What text can be recovered from pages identified as scans?”, and M10 Document Understanding independently asks “What does this document mean?” M13 consumes only Ready M10 intelligence as a soft retrieval signal; M14 reorders only secure M13 chunk candidates. Neither milestone changes M10/M11/M12 ingestion semantics, the answer prompt, citation semantics, or source mapping; see `ROADMAP.md`.
