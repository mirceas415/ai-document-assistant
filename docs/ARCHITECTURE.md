# AI Document Assistant Architecture

## Overview

AI Document Assistant is a modular monolith with an ASP.NET Core 10 backend, a React and TypeScript frontend, PostgreSQL persistence through Entity Framework Core, and local document storage. The application currently supports authenticated, user-owned projects, PDF and DOCX upload, background text extraction, deterministic retrieval chunk generation, and inspection or rebuilding of stored chunks.

Milestone 5 deliberately stops before embeddings or retrieval. There are no OpenAI API calls, embedding columns, vector extensions, vector searches, chat features, OCR, cloud storage, external message queues, or microservices.

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

The ordered, extracted source representation. PDF sections normally correspond to pages. DOCX sections correspond to heading groups. These rows remain the source of truth for chunk rebuilding and are not replaced by chunks.

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
5. Existing sections and chunks are removed transactionally.
6. New ordered `DocumentTextSection` rows and extraction counts are stored while the document remains `Processing`.
7. Chunk generation begins from the stored sections.

An extraction failure removes stale sections and chunks, sets the document to `Failed`, stores a bounded safe `ProcessingError`, keeps the uploaded file for retry, and logs the technical exception.

## Chunking Flow

1. Stored text sections are read in `SectionIndex` order with `AsNoTracking`.
2. The generator prefers boundaries in this order: titled section/heading, paragraph, sentence, then tokenizer boundary.
3. The default configuration targets 700 tokens, allows at most 900, repeats up to 100 tokens of overlap, and aims for a 100-token minimum. Values are configured in `appsettings.json` and validated during startup.
4. Sentence detection protects common Romanian abbreviations including `art.`, `nr.`, and `dl.`.
5. Oversized sentences use tokenizer character indexes and backtrack to whitespace where possible, avoiding broken words and UTF-16 surrogate pairs.
6. The short final chunk is merged or rebalanced where the maximum allows. A short document produces one chunk.
7. Overlap copies useful whole trailing paragraphs or sentences when possible. If a unit is too large, a tokenizer-bounded suffix is used. The generator never copies an entire previous chunk as overlap.
8. Generated chunks replace previous rows inside a transaction. Chunk metadata is updated and the document becomes `Ready` only after the transaction commits.

Chunk generation uses Microsoft's `Microsoft.ML.Tokenizers` with the `cl100k_base` tiktoken BPE data. Counts are real tokenizer counts, not word-based estimates. Romanian, English, mixed-language, and other Unicode content remain .NET strings and PostgreSQL `text` without ASCII conversion.

A chunking failure removes chunks, retains stored source sections, clears chunk metadata, sets the document to `Failed`, stores a bounded safe `ChunkingError`, keeps the uploaded file, and logs the technical exception.

## Chunk Rebuild Flow

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
- Ready: View extracted text, View chunks, Rebuild chunks, and Delete. Process and Retry are never rendered.

Chunk rebuild requires browser confirmation. The chunk viewer handles loading, error, empty, content, and close states.

## Completed Milestones

- Milestone 1: Identity registration, login, logout, current user, and cookie authentication.
- Milestone 2: User-owned project CRUD and ownership authorization.
- Milestone 3: PDF and DOCX upload, validation, metadata, local storage, and deletion.
- Milestone 4: PDF/DOCX extraction, background processing, retries, source-section storage, extracted-text viewer, and backend tests.
- Milestone 5: Deterministic multilingual chunking, exact token counts, overlap, chunk persistence, rebuild/list APIs, frontend viewer/rebuild controls, tests, and documentation.

## Remaining Roadmap

The planned later milestones are embeddings, PostgreSQL vector search, AI chat, and deployment. Each remains separate from the chunking implementation; see `ROADMAP.md`.
