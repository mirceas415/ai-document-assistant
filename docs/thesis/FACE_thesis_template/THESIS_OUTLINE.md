# Thesis Outline and Page Budget

Working English title: **Intelligent Assistant for Semantic Document Analysis and Querying**

This outline is written for the official 2026 FACE template. Page estimates include prose, focused tables, algorithms, and figure placeholders at the template's current page size and spacing. They are planning ranges rather than promises because the final bibliography, rendered diagrams, screenshots, and evaluation results will change pagination.

## Overall page budget

| Part | Estimated final pages | Notes |
|---|---:|---|
| Official preliminary matter | 15--18 | Cover, title page, originality declaration, project form, supervisor remarks, bilingual summaries, optional administrative pages, contents, and lists |
| Chapter 1 -- Introduction | 3--4 | Accessible problem framing and contribution |
| Chapter 2 -- Background and Fundamental Concepts | 5--6 | Only theory needed to understand the implementation |
| Chapter 3 -- Requirements and System Architecture | 5--6 | Requirements, boundaries, components, and domain model |
| Chapter 4 -- Document Ingestion and Understanding | 8--10 | First central technical chapter |
| Chapter 5 -- Retrieval and Grounded Question Answering | 8--10 | Second central technical chapter |
| Chapter 6 -- Application Implementation and User Experience | 5--6 | Practical implementation and UI walkthrough |
| Chapter 7 -- Testing and Evaluation | 5--6 | Verified automated tests plus pending experimental evaluation |
| Conclusions and Future Work | 2--3 | Results stated without invented measurements; limitations and future work |
| Bibliography and official appendices | 2--3 | Expected to grow during the verified bibliography and final-submission passes |
| **Estimated total** | **58--65** | Within the requested 55--65-page target after final assets are inserted |

The main technical content is expected to occupy approximately 41--51 pages, and therefore remains the largest part of the thesis.

## Preliminary matter

### Official cover and title page

- **Purpose:** identify the institution, diploma-project type, title, candidate, supervisor, and submission session while preserving the official Romanian layout.
- **Key material:** confirmed English working title; visible placeholders for the Romanian title, candidate, supervisor, programme, and date.
- **Estimated pages:** 2.
- **Figures:** official University and Faculty logos already supplied by the template.
- **Tables:** none.
- **Citations needed:** none.

### Originality declaration, project timetable, and supervisor remarks

- **Purpose:** preserve the official administrative forms without retaining sample personal data.
- **Key material:** Romanian form wording and clearly marked fields for manual completion and signatures.
- **Estimated pages:** 5--7.
- **Figures:** none.
- **Tables:** official form tables.
- **Citations needed:** none; administrative wording must instead be checked against Faculty requirements.

### Project summaries

- **Purpose:** provide faithful Romanian and English summaries of the same completed engineering project.
- **Key material:** problem, integrated ingestion/retrieval solution, contribution, testing status, limitations, and keywords. The English technical account is authoritative; the Romanian translation requires final manual language review.
- **Estimated pages:** 2.
- **Figures/tables:** none.
- **Citations needed:** none in the summaries.

### Dedication, acknowledgements, and foreword

- **Purpose:** retain official optional-page architecture without presenting template examples as student-authored content.
- **Key material:** visible completion/omission placeholders.
- **Estimated pages:** 2--3 if retained in the submitted version.
- **Figures/tables/citations:** none.

### Contents and lists

- **Purpose:** provide the official Table of Contents, List of Figures, List of Tables, and a List of Algorithms for the included pseudocode.
- **Estimated pages:** 4--5.
- **Figures/tables/citations:** generated automatically.

## Chapter 1 -- Introduction

### 1.1 Context

- **Purpose:** establish the practical difficulty of locating precise information in growing, heterogeneous document collections.
- **Key material:** native digital documents, scanned pages, mixed structure, and provenance requirements.
- **Estimated pages:** 0.4--0.5.
- **Figures/tables:** none.
- **Citations needed:** growth and use of digital-document information, if a quantitative claim is added.

### 1.2 Motivation

- **Purpose:** explain why manual browsing, filename search, keyword-only search, and ungrounded language-model responses are insufficient.
- **Key material:** semantic mismatch, exact-identifier queries, verification effort, and unsupported answers.
- **Estimated pages:** 0.5.
- **Figures/tables:** none.
- **Citations needed:** document retrieval and language-model grounding.

### 1.3 Problem statement

- **Purpose:** state the bounded engineering problem and its security/provenance constraints.
- **Key material:** an authenticated user must ingest a project-scoped collection and receive evidence-backed answers traceable to real source locations.
- **Estimated pages:** 0.4.
- **Figures/tables:** none.
- **Citations needed:** none for the project-specific formulation.

### 1.4 Objectives

- **Purpose:** define verifiable project objectives without marketing claims.
- **Key material:** heterogeneous ingestion, selective OCR, normalization, understanding, token-aware chunking, searchable persistence, hybrid retrieval, reranking, grounded citations, conversation history, ownership, and failure isolation.
- **Estimated pages:** 0.5.
- **Figures/tables:** concise objective list only if it reads more clearly than prose.
- **Citations needed:** none for project objectives.

### 1.5 Proposed solution

- **Purpose:** introduce the two connected system flows without low-level implementation detail.
- **Key material:** document-to-knowledge flow and question-to-grounded-answer flow.
- **Estimated pages:** 0.6.
- **Figures:** reference to the high-level architecture figure in Chapter 3.
- **Tables:** none.
- **Citations needed:** Retrieval-Augmented Generation (RAG) concept.

### 1.6 Personal contribution

- **Purpose:** distinguish established techniques from the student's engineering design, implementation, integration, and validation work.
- **Key material:** extraction through citation delivery, secure ownership boundaries, diagnostics, failure isolation, and automated tests; no claim of inventing OCR, embeddings, RRF, or reranking.
- **Estimated pages:** 0.5.
- **Figures/tables:** none.
- **Citations needed:** none for implementation contribution.

### 1.7 Thesis organization

- **Purpose:** guide the reader through Chapters 2--7 and the conclusions.
- **Estimated pages:** 0.3.
- **Figures/tables/citations:** none.

## Chapter 2 -- Background and Fundamental Concepts

### 2.1 Information retrieval for documents

- **Purpose:** introduce collections, queries, candidate retrieval, ranking, relevance, precision, and recall.
- **Key material:** ranked retrieval rather than exact file lookup; provenance as an application requirement.
- **Estimated pages:** 0.5.
- **Figures/tables:** retrieval-channel comparison table later in the chapter.
- **Citations needed:** authoritative information-retrieval textbook or survey.

### 2.2 Text extraction from structured documents

- **Purpose:** distinguish logical structure in DOCX from page-oriented and sometimes geometrically encoded PDF text.
- **Estimated pages:** 0.4.
- **Citations needed:** PDF specification and Open XML documentation.

### 2.3 Optical Character Recognition

- **Purpose:** explain why rasterized text requires recognition and why OCR output is uncertain.
- **Estimated pages:** 0.4.
- **Citations needed:** OCR overview and official Tesseract documentation/project source.

### 2.4 Text normalization

- **Purpose:** explain conservative cleanup, retrieval consistency, and the risk of deleting valid text.
- **Estimated pages:** 0.3.
- **Citations needed:** text normalization or information-retrieval preprocessing.

### 2.5 Tokenization

- **Purpose:** explain model-oriented tokens and why character counts do not reliably enforce model budgets.
- **Estimated pages:** 0.3.
- **Citations needed:** tokenizer/model-provider documentation.

### 2.6 Document chunking

- **Purpose:** motivate bounded, overlapping evidence units and the small-versus-large trade-off.
- **Estimated pages:** 0.5.
- **Citations needed:** retrieval chunking literature or authoritative technical guidance.

### 2.7 Vector embeddings

- **Purpose:** provide an intuitive account of mapping text to numerical vectors in a shared semantic space.
- **Estimated pages:** 0.5.
- **Citations needed:** vector embeddings and the configured embedding model.

### 2.8 Semantic similarity

- **Purpose:** present cosine similarity/distance with variables explained and relate it to nearest-neighbour retrieval.
- **Estimated pages:** 0.4.
- **Citations needed:** vector-space retrieval/cosine similarity.

### 2.9 Vector databases and pgvector

- **Purpose:** explain native vector persistence and database-side similarity search at an undergraduate level.
- **Estimated pages:** 0.4.
- **Citations needed:** PostgreSQL and pgvector official documentation.

### 2.10 Retrieval-Augmented Generation

- **Purpose:** explain retrieval, bounded evidence context, generation, and why retrieval quality constrains answer quality.
- **Estimated pages:** 0.5.
- **Citations needed:** original or foundational RAG publication.

### 2.11 Lexical/full-text retrieval

- **Purpose:** explain token/term matching and why it complements semantic retrieval for identifiers and exact wording.
- **Estimated pages:** 0.4.
- **Citations needed:** PostgreSQL full-text search documentation and IR source.

### 2.12 Hybrid retrieval

- **Purpose:** motivate combining retrieval channels that fail differently.
- **Estimated pages:** 0.3.
- **Tables:** semantic, lexical, and metadata channel comparison.
- **Citations needed:** hybrid retrieval.

### 2.13 Reciprocal Rank Fusion

- **Purpose:** introduce rank-based fusion and its scale-independence; show the generic RRF equation.
- **Estimated pages:** 0.5.
- **Citations needed:** original RRF paper.

### 2.14 Reranking

- **Purpose:** distinguish recall-oriented candidate generation from precision-oriented second-stage ranking.
- **Estimated pages:** 0.4.
- **Citations needed:** learning-to-rank or model-based reranking source.

### 2.15 Grounding and citations

- **Purpose:** explain evidence attribution, source identifiers, and the difference between generated text and authoritative source mapping.
- **Estimated pages:** 0.4.
- **Citations needed:** grounded generation and citation evaluation.

### 2.16 Prompt injection in document-based systems

- **Purpose:** define indirect prompt injection and motivate defense in depth without claiming complete prevention.
- **Estimated pages:** 0.5.
- **Citations needed:** authoritative prompt-injection/security guidance.

## Chapter 3 -- Requirements and System Architecture

### 3.1 Functional requirements

- **Purpose:** translate the problem into testable system capabilities.
- **Key material:** authentication, workspaces, upload, processing, diagnostics, conversations, retrieval, answers, citations, and rebuild operations.
- **Estimated pages:** 0.7.
- **Tables:** functional requirements with identifiers and verification approach.
- **Citations needed:** none.

### 3.2 Non-functional requirements

- **Purpose:** define ownership isolation, traceability, bounded resource use, deterministic stages, failure isolation, maintainability, and usability.
- **Estimated pages:** 0.6.
- **Tables:** compact non-functional requirements table.
- **Citations needed:** optionally secure-design guidance; otherwise project requirements need no source.

### 3.3 High-level system architecture

- **Purpose:** present the modular-monolith deployment and the complete ingress/query paths.
- **Estimated pages:** 0.8.
- **Figures:** DIA-01 high-level architecture.
- **Tables:** none.
- **Citations needed:** none for the implemented architecture.

### 3.4 Backend architecture

- **Purpose:** explain API/controllers, application services, bounded provider adapters, background worker, persistence, and local file storage.
- **Estimated pages:** 0.5.
- **Citations needed:** ASP.NET Core and Entity Framework Core official documentation.

### 3.5 Frontend architecture

- **Purpose:** explain the React/TypeScript SPA, API boundary, state organization, and chat-first shell without enumerating components.
- **Estimated pages:** 0.4.
- **Citations needed:** React and TypeScript official documentation.

### 3.6 Persistence and domain model

- **Purpose:** explain the principal entities, ownership relationships, processing artifacts, and immutable citation snapshots.
- **Estimated pages:** 0.9.
- **Figures:** DIA-02 compact domain/workspace model.
- **Tables:** processing/status separation appears in Chapter 4.
- **Citations needed:** PostgreSQL and pgvector official documentation.

### 3.7 Authentication and workspace ownership

- **Purpose:** identify the user/project boundary and show how ownership filters are enforced through each read path.
- **Estimated pages:** 0.5.
- **Citations needed:** ASP.NET Core Identity/cookie authentication official documentation.

### 3.8 Document-processing architecture

- **Purpose:** connect the upload request, process-local queue, worker, independent analysis stages, and atomic authoritative persistence.
- **Estimated pages:** 0.5.
- **Figures:** forward reference to DIA-03 ingestion pipeline.
- **Citations needed:** none for implementation-specific flow.

### 3.9 Retrieval and question-answering architecture

- **Purpose:** connect query analysis, vector/lexical/metadata candidate generation, fusion, reranking, bounded context, generation, and source validation.
- **Estimated pages:** 0.5.
- **Figures:** forward reference to DIA-07 hybrid retrieval pipeline.
- **Citations needed:** RAG, RRF, reranking.

### 3.10 Security boundaries

- **Purpose:** consolidate authentication, authorization, untrusted document text, model/provider boundaries, secret handling, bounded outputs, and citation authority.
- **Estimated pages:** 0.6.
- **Citations needed:** prompt-injection and secure web-application guidance.

## Chapter 4 -- Document Ingestion and Understanding

### 4.1 Upload and validation

- **Purpose:** explain the authenticated upload path from validation to queued processing.
- **Key material:** verified formats, size/signature checks, generated local filename, ownership, initial statuses, and process-local queue.
- **Estimated pages:** 0.8.
- **Figures:** DIA-03 complete ingestion pipeline.
- **Tables:** accepted formats/validation summary if useful.
- **Citations needed:** file-format specifications; ASP.NET upload guidance if referenced.

### 4.2 PDF and DOCX text extraction

- **Purpose:** explain page-oriented PdfPig extraction and structurally aware Open XML extraction, including table order and heading provenance.
- **Estimated pages:** 0.8.
- **Tables:** PDF versus DOCX extraction characteristics.
- **Citations needed:** PdfPig project, Open XML SDK, PDF/Open XML specifications.

### 4.3 Raw and normalized text

- **Purpose:** justify immutable raw text and describe the verified conservative normalization pipeline and diagnostics.
- **Estimated pages:** 1.0.
- **Tables:** normalization operation/rationale/limitation table.
- **Citations needed:** text-normalization and de-hyphenation sources.

### 4.4 Technical PDF analysis

- **Purpose:** explain versioned page and document classification before OCR.
- **Key material:** controlled types, meaningful-text thresholds, image coverage, ordered page decisions, unknown-page tolerance, and document aggregation.
- **Estimated pages:** 1.2.
- **Figures:** DIA-04 page-classification decision flow.
- **Tables:** technical type definitions and actual thresholds.
- **Algorithms:** deterministic PDF classification.
- **Citations needed:** PDF imaging/text model and PdfPig official/project documentation.

### 4.5 Selective local OCR

- **Purpose:** explain how only pages classified as Scanned are rendered and recognized locally.
- **Key material:** PDFium through PDFtoImage, Tesseract, configured languages/DPI/resource limits, page and aggregate statuses, provenance, mixed-document behaviour, and local-versus-provider boundary.
- **Estimated pages:** 1.2.
- **Figures:** DIA-05 selective OCR flow; UI screenshot later in Chapter 6.
- **Tables:** OCR configuration and status meanings.
- **Algorithms:** OCR routing and bounded page processing.
- **Citations needed:** PDFium/PDFtoImage and Tesseract official/project documentation.

### 4.6 Document Understanding

- **Purpose:** explain bounded structured classification and metadata extraction as a non-fatal enrichment stage.
- **Key material:** type/language/title/subject, controlled metadata, representative deterministic sampling, structured output, allowlists, validation, normalization, confidence and count bounds, hash/model/prompt identity, idempotency, and failure behavior.
- **Estimated pages:** 1.2.
- **Tables:** document types and metadata categories.
- **Citations needed:** structured model output/API documentation; document classification/information extraction background.

### 4.7 Token-aware document chunking

- **Purpose:** give a detailed and approachable explanation of the current deterministic chunk generator.
- **Key material:** ordered normalized sections, exact tokenizer, configured target/hard maximum/overlap/minimum, natural boundaries, oversized-unit splitting, short-tail handling, provenance union, multilingual Unicode behavior, and deterministic rebuilds.
- **Estimated pages:** 1.7--2.0.
- **Figures:** DIA-06 overlap and provenance concept.
- **Tables:** chunking configuration and trade-offs.
- **Algorithms:** token-aware chunking pseudocode accurately matching the implementation.
- **Citations needed:** tokenizer and retrieval chunking sources.

### 4.8 Embedding generation

- **Purpose:** explain one vector per exact chunk, batching, validation, native pgvector persistence, hashes, and current/rebuild semantics.
- **Estimated pages:** 0.8.
- **Tables:** embedding configuration and currentness criteria.
- **Citations needed:** configured embedding model/API and pgvector.

### 4.9 Status and failure isolation

- **Purpose:** show why document processing, technical analysis, OCR, and understanding have separate states and how useful retrieval remains possible after non-critical failures.
- **Estimated pages:** 0.7.
- **Tables:** concern/status/effect/retry table.
- **Citations needed:** none for project-specific design.

## Chapter 5 -- Retrieval and Grounded Question Answering

### 5.1 Baseline semantic retrieval

- **Purpose:** explain query embedding, project/owner/readiness/current-vector filtering, pgvector cosine search, and Top-K results.
- **Estimated pages:** 0.8.
- **Tables:** retrieval configuration summary.
- **Citations needed:** embeddings, cosine similarity, pgvector.

### 5.2 PostgreSQL lexical retrieval

- **Purpose:** show why exact identifiers and phrases need a lexical channel and describe the generated simple-configuration search vector, GIN index, and safe web-search query construction.
- **Estimated pages:** 0.8.
- **Citations needed:** official PostgreSQL full-text search documentation.

### 5.3 Metadata-aware retrieval

- **Purpose:** explain query analysis and soft document-level boosts while making clear that metadata is not answer evidence.
- **Estimated pages:** 0.8.
- **Tables:** supported signal types and role in ranking.
- **Citations needed:** metadata-aware retrieval/information extraction.

### 5.4 Hybrid retrieval and weighted RRF

- **Purpose:** provide a detailed account of rank-scale incompatibility, actual formula/configuration, candidate pools, deduplication, stable ordering, and a fictional worked example.
- **Estimated pages:** 1.4.
- **Figures:** DIA-07 hybrid pipeline and DIA-08 RRF contribution concept.
- **Tables:** channel comparison, actual RRF configuration, worked example.
- **Algorithms:** weighted RRF with metadata multiplier.
- **Citations needed:** original RRF publication and hybrid retrieval.

### 5.5 Model-based reranking

- **Purpose:** separate recall-oriented fusion from bounded precision-oriented reranking.
- **Key material:** actual candidate/input/per-candidate/output/timeout limits, opaque candidate IDs, input fields, structured relevance scale, validation, treatment of unknown/duplicate/omitted candidates, skip conditions, and fail-open fallback.
- **Estimated pages:** 1.3.
- **Figures:** DIA-09 reranking and fallback pipeline.
- **Tables:** actual reranking configuration and validation responses.
- **Algorithms:** reranking validation/fallback.
- **Citations needed:** reranking literature and official model structured-output documentation.

### 5.6 RAG context construction

- **Purpose:** explain conversion of final chunks to source-labelled, provenance-rich evidence under a hard token budget.
- **Estimated pages:** 0.7.
- **Tables:** context fields and actual limits.
- **Citations needed:** RAG and tokenizer documentation.

### 5.7 Grounded answer generation

- **Purpose:** explain the model-provider boundary, evidence-only instructions, untrusted-data delimiters, no-evidence behavior, language behavior, and bounded output.
- **Estimated pages:** 0.7.
- **Citations needed:** RAG and official OpenAI API documentation.

### 5.8 Authoritative citations

- **Purpose:** show how backend-assigned S-identifiers are validated and mapped to real chunks, and how immutable source snapshots preserve historical display.
- **Estimated pages:** 0.8.
- **Figures:** DIA-10 grounded citation/source flow.
- **Tables:** model/backend citation responsibility if useful.
- **Citations needed:** grounded citation evaluation.

### 5.9 Conversational context

- **Purpose:** explain persistent messages, bounded recent history, titles, and why past assistant output is not retrieval evidence.
- **Estimated pages:** 0.6.
- **Citations needed:** conversational RAG/background if retained.

### 5.10 Prompt-injection defense

- **Purpose:** describe defense in depth across understanding, reranking, and answering without claiming a complete solution.
- **Estimated pages:** 0.8.
- **Tables:** threat/control/residual-risk summary.
- **Citations needed:** authoritative prompt-injection guidance.

## Chapter 6 -- Application Implementation and User Experience

### 6.1 Technology stack

- **Purpose:** state why each verified technology is used in this application.
- **Estimated pages:** 0.7.
- **Tables:** concise technology/role/rationale table.
- **Citations needed:** official documentation for .NET/ASP.NET Core, EF Core, PostgreSQL, pgvector, React, TypeScript, PdfPig, Open XML SDK, PDFium/PDFtoImage, Tesseract, and OpenAI API.

### 6.2 Authentication and workspaces

- **Purpose:** explain User to Workspace to Documents/Conversations, including Project as backend term and Workspace as UI term; justify reuse of processed documents across chats.
- **Estimated pages:** 0.6.
- **Figures:** reference DIA-02.
- **Citations needed:** ASP.NET Core Identity.

### 6.3 Chat-first interface

- **Purpose:** explain workspace navigation, conversation history, New Chat, composer, upload entry point, and active-workspace context.
- **Estimated pages:** 0.8.
- **Figures:** UI-01 main workspace/empty state if retained in final selection; otherwise UI-02 grounded answer provides the main overview.
- **Citations needed:** none for the implemented UI.

### 6.4 Document management

- **Purpose:** explain upload and visible processing, intelligence, technical-analysis, OCR, extracted-text, diagnostics, and rebuild controls.
- **Estimated pages:** 1.0.
- **Figures:** selected screenshot placeholders for document list/intelligence/OCR diagnostics.
- **Citations needed:** none for implementation-specific screens.

### 6.5 Answers and sources

- **Purpose:** explain assistant messages, inline citation markers, and source-detail provenance.
- **Estimated pages:** 0.8.
- **Figures:** UI-02 grounded answer and UI-03 source details modal.
- **Citations needed:** none for implemented UI; citation background is in Chapters 2 and 5.

### 6.6 Advanced retrieval diagnostics

- **Purpose:** show that rank information is exposed for development, evaluation, debugging, and thesis demonstration rather than normal reading.
- **Estimated pages:** 0.7.
- **Figures:** UI-10 advanced retrieval details.
- **Citations needed:** none.

### 6.7 Feedback and accessibility

- **Purpose:** describe implemented loading states, skeletons, toasts, confirmations, keyboard/Escape/focus handling, reduced motion, and responsive behavior without asserting formal compliance.
- **Estimated pages:** 0.6.
- **Figures/tables:** none unless final screenshots make a state especially clear.
- **Citations needed:** Web Content Accessibility Guidelines only if specific criteria are claimed.

## Chapter 7 -- Testing and Evaluation

### 7.1 Automated testing strategy

- **Purpose:** document the verified current test count and organize tests by responsibility rather than listing methods.
- **Key material:** authorization/ownership, extraction, normalization, chunking, embedding, understanding, technical analysis, OCR, retrieval/RRF/reranking, RAG/citations/history, prompt boundaries, fallback behavior, and PostgreSQL query shape.
- **Estimated pages:** 1.0.
- **Tables:** test-category/scope/representative risk table.
- **Citations needed:** software-testing guidance if a methodological claim is made.

### 7.2 Evaluation methodology

- **Purpose:** define a reproducible, not-yet-executed evaluation using the prepared synthetic corpus.
- **Key material:** labelled expectations for understanding, language, PDF classification, OCR routing/content, three retrieval variants, citations, answer correctness, no-evidence, and prompt injection.
- **Estimated pages:** 1.1.
- **Tables:** corpus task/ground-truth/scoring table.
- **Citations needed:** evaluation methodology and retrieval metrics.

### 7.3 Retrieval and answer metrics

- **Purpose:** define Top-1 accuracy, Top-3 recall, Top-8 recall, citation correctness, answer correctness, and security/no-evidence correctness in plain language.
- **Estimated pages:** 0.8.
- **Tables:** metric definition/interpretation table.
- **Citations needed:** information-retrieval evaluation source and grounded-answer evaluation.

### 7.4 Results placeholders

- **Purpose:** provide clearly empty tables for later verified measurements without implying outcomes.
- **Estimated pages:** 1.0.
- **Tables:** retrieval quality; Document Understanding; technical PDF classification; OCR behavior; grounding/citations; security/no-evidence.
- **Citations needed:** none for future project measurements.

### 7.5 Results figure placeholders

- **Purpose:** reserve three locations for final plots while keeping the draft compile-safe.
- **Estimated pages:** 0.8.
- **Figures:** EVAL-01 retrieval comparison; EVAL-02 OCR results; EVAL-03 classification results, all marked WAIT UNTIL FINAL EVALUATION.
- **Citations needed:** none.

### 7.6 Discussion framework

- **Purpose:** provide a disciplined set of questions to answer after results exist, including error analysis, trade-offs, threats to validity, and comparisons among retrieval variants.
- **Estimated pages:** 0.8.
- **Figures/tables:** references to future result tables/plots.
- **Citations needed:** evaluation/threats-to-validity guidance if used.

## Conclusions and Future Work

### Conclusions

- **Purpose:** answer the project problem, summarize the integrated implementation and personal contribution, and state only outcomes evidenced by completed functionality/tests.
- **Key material:** full ingestion and question paths, provenance and ownership, failure isolation, engineering lessons, and pending quantitative evaluation.
- **Estimated pages:** 1.2--1.5.
- **Figures/tables:** none.
- **Citations needed:** normally none; concepts are already cited in earlier chapters.

### Limitations

- **Purpose:** identify verified practical limitations rather than generic disclaimers.
- **Key material to verify before final text:** external model-provider dependency, reranker latency/cost, OCR scan quality, heuristic PDF classification, bounded candidate pools/context, process-local background queue, local demonstration/deployment status.
- **Estimated pages:** 0.6.
- **Citations needed:** none for observed implementation limits.

### Future work

- **Purpose:** separate possible extensions from implemented functionality.
- **Key material:** durable queue, deployment hardening, larger-scale approximate vector indexing, OCR preprocessing, explicit document-scope filters, alternative/local models, and a larger evaluated corpus.
- **Estimated pages:** 0.6.
- **Citations needed:** only if particular future technologies are advocated in detail.

## Official appendices

### Source code

- **Purpose:** state how the code is supplied electronically; optionally show only a concise module map.
- **Key material:** `[PROJECT REPOSITORY URL]` pending confirmation; no large source listings.
- **Estimated pages:** 1.
- **Citations needed:** none.

### Project website

- **Purpose:** preserve the official appendix while avoiding an invented deployment URL.
- **Key material:** visible placeholder or explicit confirmation that no public project site is submitted.
- **Estimated pages:** 0.5.
- **Citations needed:** none.

### Media support

- **Purpose:** preserve the official appendix and identify the Faculty-approved electronic submission medium when known.
- **Key material:** visible placeholder; no invented CD/DVD or link.
- **Estimated pages:** 0.5.
- **Citations needed:** none.

## Planned graphical inventory

Ten Mermaid source diagrams are planned under `diagrams/`:

1. DIA-01 -- high-level system architecture.
2. DIA-02 -- compact domain/workspace model.
3. DIA-03 -- complete ingestion pipeline.
4. DIA-04 -- technical PDF page-classification flow.
5. DIA-05 -- selective OCR flow.
6. DIA-06 -- chunking with overlap and provenance.
7. DIA-07 -- hybrid retrieval pipeline.
8. DIA-08 -- RRF fusion concept.
9. DIA-09 -- reranking and fail-open fallback.
10. DIA-10 -- grounded citation/source flow.

The LaTeX draft will use compile-safe placeholders until these sources are rendered. To keep the final graphical component near the requested range, the first draft plans five essential manual UI screenshots (grounded answer, source details, document states, OCR diagnostics, and advanced retrieval diagnostics) plus three pending evaluation plots. This produces 18 thesis figure placeholders in total: ten conceptual diagrams, five manual screenshots, and three future results figures.

