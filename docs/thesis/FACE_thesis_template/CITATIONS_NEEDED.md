# Citations Needed

No bibliography entry has been accepted as verified in this first draft. The official template's sample references were removed because they do not establish sources for the AI Document Assistant. During the dedicated bibliography pass, verify every entry directly against a primary, official, or otherwise authoritative source before replacing a visible `\citationneeded{...}` marker.

Prefer original publications for named academic methods, standards for file formats, official project/vendor documentation for implementation behavior, and a respected information-retrieval textbook or peer-reviewed survey for general definitions. Do not use a search-result snippet, generated bibliography, or unverified secondary blog as bibliographic evidence.

## Chapter 1 — Introduction

### Heterogeneous document retrieval and grounded language-model applications

- **Why a citation is needed:** supports the general motivation that information must be found across heterogeneous document collections and that generated answers require grounding and provenance. Project-specific problem statements and implementation objectives do not need external attribution.
- **Preferred source type:** authoritative information-retrieval textbook/survey plus a foundational or peer-reviewed grounded-generation/RAG source.

## Chapter 2 — Background and Fundamental Concepts

### Information-retrieval foundations

- **Why a citation is needed:** defines ranked retrieval, relevance, precision, recall, and the distinction between collection items and retrieval units.
- **Preferred source type:** established information-retrieval textbook or peer-reviewed survey.

### PDF and Office Open XML representation

- **Why a citation is needed:** supports the conceptual difference between page-description PDF content and structurally represented DOCX paragraphs, styles, and tables.
- **Preferred source type:** official PDF specification and ECMA/ISO Office Open XML specification or Microsoft Open XML documentation.

### OCR and Tesseract

- **Why a citation is needed:** defines optical character recognition, typical quality limitations, and identifies the established Tesseract engine.
- **Preferred source type:** authoritative OCR textbook/survey and official Tesseract project/documentation.

### Text normalization and conservative de-hyphenation

- **Why a citation is needed:** supports normalization as an information-retrieval preprocessing step and the risks of destructive cleanup.
- **Preferred source type:** peer-reviewed document-processing/IR source or authoritative textbook.

### `cl100k_base` tokenization

- **Why a citation is needed:** supports the statement that model tokens differ from words/characters and documents the tokenizer used for exact project budgets.
- **Preferred source type:** official tokenizer/model-provider documentation; official Microsoft ML Tokenizers documentation for the implementation package.

### Document chunking for semantic retrieval

- **Why a citation is needed:** supports the general small-versus-large passage and overlap trade-offs. The concrete algorithm and values are implementation facts and need no external source.
- **Preferred source type:** peer-reviewed retrieval study or authoritative technical publication that evaluates passage/chunk size and overlap.

### Text embeddings and the configured embedding model

- **Why a citation is needed:** defines vector text embeddings and verifies intended model behavior and configurable dimensions.
- **Preferred source type:** original/peer-reviewed embedding source for the concept and official OpenAI model/API documentation for `text-embedding-3-small`.

### Cosine similarity and the vector-space model

- **Why a citation is needed:** supports Equations 2.1--2.2 and their use as a ranking relation rather than a calibrated confidence probability.
- **Preferred source type:** established information-retrieval or linear-algebra source; pgvector documentation for operator semantics.

### PostgreSQL and pgvector

- **Why a citation is needed:** supports native vector persistence, cosine-distance operators, relational filtering, and the distinction between exact and approximate search.
- **Preferred source type:** official PostgreSQL and pgvector documentation.

### Retrieval-Augmented Generation

- **Why a citation is needed:** attributes the established retrieval-plus-generation architecture and its limitations.
- **Preferred source type:** foundational/original RAG academic paper, supplemented only where necessary by a peer-reviewed survey.

### Lexical retrieval and PostgreSQL full-text search

- **Why a citation is needed:** supports lexical matching concepts and the documented behavior of `tsvector`, `tsquery`, the `simple` configuration, `websearch_to_tsquery`, ranking, and GIN indexes.
- **Preferred source type:** information-retrieval textbook plus official PostgreSQL full-text search documentation.

### Hybrid lexical and semantic retrieval

- **Why a citation is needed:** supports the established motivation for combining channels with complementary failure modes.
- **Preferred source type:** peer-reviewed hybrid-retrieval paper or survey.

### Reciprocal Rank Fusion

- **Why a citation is needed:** attributes the existing RRF method and supports the generic rank-fusion equation. The weighted metadata-aware implementation remains the student's engineering work.
- **Preferred source type:** original RRF academic publication.

### Second-stage/model-based reranking

- **Why a citation is needed:** supports the recall-oriented first stage versus precision-oriented reranking distinction.
- **Preferred source type:** original or well-established peer-reviewed passage-reranking/learning-to-rank publication.

### Grounding and citation evaluation

- **Why a citation is needed:** supports the distinction between source-identifier validity, evidence support, and final answer correctness.
- **Preferred source type:** peer-reviewed grounded-generation or citation-evaluation work.

### Indirect prompt injection

- **Why a citation is needed:** defines instruction-like text arriving through untrusted retrieved documents and supports defense-in-depth/residual-risk language.
- **Preferred source type:** recognized security research publication and/or authoritative security guidance from OWASP, NIST, or the relevant model provider.

## Chapter 3 — Requirements and System Architecture

### ASP.NET Core controllers, dependency injection, and hosted services

- **Why a citation is needed:** supports framework terminology used to describe the API composition and background worker.
- **Preferred source type:** official Microsoft ASP.NET Core documentation.

### Entity Framework Core and Npgsql

- **Why a citation is needed:** supports framework/provider roles in relational mapping, transactions, and PostgreSQL access.
- **Preferred source type:** official Microsoft EF Core and Npgsql provider documentation.

### React, TypeScript, and Vite

- **Why a citation is needed:** verifies the frontend technology roles rather than supporting a claim of superiority.
- **Preferred source type:** official React, TypeScript, and Vite documentation.

### Generated full-text columns and GIN indexes

- **Why a citation is needed:** supports the database concepts used in the persistence architecture.
- **Preferred source type:** official PostgreSQL documentation.

### pgvector data type and distance operators

- **Why a citation is needed:** verifies the native vector column and cosine operator described architecturally.
- **Preferred source type:** official pgvector documentation.

### ASP.NET Core Identity and cookie authentication

- **Why a citation is needed:** supports framework authentication concepts and cookie properties. Concrete ownership predicates are repository facts.
- **Preferred source type:** official Microsoft Identity and cookie-authentication documentation.

### RAG, RRF, reranking, and prompt injection

- **Why a citation is needed:** Chapter 3 briefly connects these established concepts to the architecture.
- **Preferred source type:** reuse the verified primary sources selected for the corresponding Chapter 2 topics rather than adding redundant references.

## Chapter 4 — Document Ingestion and Understanding

### PdfPig and PDF text extraction

- **Why a citation is needed:** identifies the extraction library and provides external context for content-order text extraction and page geometry.
- **Preferred source type:** official PdfPig repository/documentation plus the PDF specification where necessary.

### Open XML SDK and WordprocessingML

- **Why a citation is needed:** verifies the library and the structured paragraph/style/table concepts used by the DOCX extractor.
- **Preferred source type:** official Microsoft Open XML SDK documentation and ECMA/ISO specification.

### Text normalization

- **Why a citation is needed:** supports general normalization/de-hyphenation concepts; exact thresholds and safety rules are repository facts.
- **Preferred source type:** peer-reviewed document-processing source or authoritative IR text.

### PDF text and raster-image representation

- **Why a citation is needed:** supports the structural basis for inspecting text layers, image placements, crop boxes, and scan-like pages.
- **Preferred source type:** official PDF specification and PdfPig documentation.

### PDFium and PDFtoImage

- **Why a citation is needed:** verifies the local page-rendering technology and its role before Tesseract recognition.
- **Preferred source type:** official PDFium documentation/source and official PDFtoImage project documentation.

### Tesseract OCR

- **Why a citation is needed:** attributes the recognition engine and verifies language-data/runtime concepts.
- **Preferred source type:** official Tesseract documentation/project repository.

### OpenAI Responses API structured output

- **Why a citation is needed:** supports provider-level strict JSON schema and stored-output configuration used by Document Understanding.
- **Preferred source type:** official OpenAI API documentation current at submission time.

### Chunking and tokenizer implementation dependencies

- **Why a citation is needed:** supports general chunking practice and verifies `cl100k_base`/Microsoft ML Tokenizers behavior.
- **Preferred source type:** peer-reviewed retrieval/chunking source plus official tokenizer documentation.

### Embedding model and dimensions

- **Why a citation is needed:** verifies the configured model's embedding interface and requested dimension behavior.
- **Preferred source type:** official OpenAI embeddings API/model documentation.

### pgvector persistence

- **Why a citation is needed:** verifies `vector(1536)` storage and distance semantics.
- **Preferred source type:** official pgvector documentation.

## Chapter 5 — Retrieval and Grounded Question Answering

### Vector embeddings, cosine retrieval, and pgvector

- **Why a citation is needed:** supports the standard semantic-retrieval method and database operator. Eligibility predicates, TopK, and tie-breaking are implementation facts.
- **Preferred source type:** reuse verified embedding/vector-space sources plus official pgvector documentation.

### PostgreSQL full-text search

- **Why a citation is needed:** supports generated `tsvector`, `simple` configuration, GIN index, query construction, and rank functions.
- **Preferred source type:** official PostgreSQL documentation.

### Metadata-aware information retrieval

- **Why a citation is needed:** supports using controlled metadata as a ranking signal. The specific score, cap, candidate-union rule, and evidence restriction are implementation facts.
- **Preferred source type:** peer-reviewed metadata-aware retrieval paper or authoritative IR source.

### Weighted Reciprocal Rank Fusion

- **Why a citation is needed:** attributes RRF and supports rank-based fusion across incomparable raw scales.
- **Preferred source type:** original RRF publication; reuse Chapter 2 entry.

### Model-based passage reranking

- **Why a citation is needed:** supports second-stage comparative relevance ranking and its recall/precision framing.
- **Preferred source type:** peer-reviewed passage-reranking publication.

### Structured reranking output and Responses API

- **Why a citation is needed:** verifies strict JSON response formatting, output limits, reasoning settings, and stored-output behavior.
- **Preferred source type:** official OpenAI API documentation current at submission time.

### Retrieval-Augmented Generation and answer API

- **Why a citation is needed:** supports the RAG concept and verifies the configured provider interaction at a non-secret architectural level.
- **Preferred source type:** foundational RAG paper and official OpenAI Responses API documentation.

### Grounded-answer citation correctness

- **Why a citation is needed:** supports evaluating semantic claim support separately from valid backend source membership.
- **Preferred source type:** peer-reviewed citation/grounded-answer evaluation research.

### Conversational RAG

- **Why a citation is needed:** provides context for bounded history and retrieval driven by the current question.
- **Preferred source type:** peer-reviewed conversational retrieval or conversational RAG publication.

### Indirect prompt-injection defense

- **Why a citation is needed:** supports the threat model and cautious defense-in-depth framing.
- **Preferred source type:** authoritative security research/guidance; reuse Chapter 2 source where possible.

## Chapter 6 — Application Implementation and User Experience

### Implemented technology stack

- **Why a citation is needed:** verifies the defined role and official identity of .NET/ASP.NET Core, EF Core, PostgreSQL, pgvector, React, TypeScript, PdfPig, Open XML SDK, PDFium/PDFtoImage, Tesseract, and the OpenAI API.
- **Preferred source type:** one official documentation/project source per technology, consolidated to avoid unnecessary citations.

### ASP.NET Core Identity and cookies

- **Why a citation is needed:** supports the authentication mechanism and security terminology.
- **Preferred source type:** official Microsoft documentation; reuse Chapter 3 sources.

### Accessibility terminology

- **Why a citation is needed:** supports terms such as focus visibility, live regions, reduced motion, and modal semantics without claiming formal compliance.
- **Preferred source type:** official W3C Web Content Accessibility Guidelines and WAI-ARIA Authoring Practices.

## Chapter 7 — Testing and Evaluation

### Software testing strategy

- **Why a citation is needed:** supports distinctions among unit/boundary/integration tests and the limitation of substitutes for external services.
- **Preferred source type:** established software-engineering/testing textbook or recognized standard.

### OCR Character Error Rate

- **Why a citation is needed:** defines the transcription metric and its normalization assumptions before final evaluation.
- **Preferred source type:** authoritative OCR evaluation standard, textbook, or peer-reviewed publication.

### Information-retrieval metrics

- **Why a citation is needed:** supports Top-1 accuracy and Recall@k definitions and interpretation.
- **Preferred source type:** established information-retrieval textbook or evaluation publication.

### Model-provider pricing and usage accounting

- **Why a citation is needed:** any final cost comparison is time-dependent and must use verified prices and billing units at evaluation time.
- **Preferred source type:** official OpenAI pricing and API usage documentation captured/dated during the final evaluation pass.

## Final bibliography verification checklist

- Verify primary-source metadata from the publication or official documentation page.
- Prefer DOI links for academic papers and stable official URLs for software documentation.
- Record access dates for web sources if required by the Faculty/IEEE style.
- Check that every BibTeX key is cited and every in-text citation resolves.
- Remove every visible `CITATION NEEDED` marker only after the replacement citation is present.
- Run BibTeX and inspect warnings after entries have been added; do not consider an automatically generated `.bib` file verified by default.

