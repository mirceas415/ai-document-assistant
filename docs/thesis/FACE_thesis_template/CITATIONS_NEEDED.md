# Citation Verification Audit

This file records the outcome of the dedicated bibliography pass completed on
**5 September 2026**. The original draft contained 58 visible
`\citationneeded{...}` markers. Of these, 49 were replaced by verified
citations, eight were reviewed and removed because they described the
checked-in application rather than external knowledge, and one remains
deliberately unresolved until the final evaluation. Full source metadata and
verification locations are recorded in `SOURCE_VERIFICATION.md`.

## RESOLVED

| Topic | Selected source | Citation key | Thesis locations |
|---|---|---|---|
| Information-retrieval fundamentals and evaluation metrics | Manning, Raghavan, and Schütze, *Introduction to Information Retrieval* | `Manning2008IR` | Chapter 2: Information Retrieval, Text Normalization, Cosine Similarity, Lexical Retrieval; Chapter 5: Semantic and Metadata-Aware Retrieval; Chapter 7: Retrieval Metrics |
| PDF document representation | ISO 32000-2:2020 | `ISO32000PDF` | Chapter 2: Text Extraction; Chapter 4: Technical PDF Analysis |
| Office Open XML and DOCX structure | ECMA-376, 5th edition | `ECMA376` | Chapter 2: Text Extraction; Chapter 4: PDF and DOCX Text Extraction |
| OCR and the Tesseract engine | Smith's Tesseract engine paper and the official Tesseract user manual | `Smith2007Tesseract`, `TesseractDocs` | Chapter 2: Optical Character Recognition; Chapter 4: Selective Local OCR |
| Text normalization | Manning, Raghavan, and Schütze | `Manning2008IR` | Chapter 2: Text Normalization |
| Tokenization and the implemented tokenizer package | Microsoft Learn documentation for Microsoft.ML.Tokenizers | `MicrosoftMLTokenizers` | Chapter 2: Tokenization; Chapter 4: Document Chunking |
| Passage segmentation and chunking context | Dense passage retrieval and sparse/dense retrieval papers | `Karpukhin2020DPR`, `Luan2021SparseDense` | Chapter 2: Document Chunking; Chapter 4: Document Chunking |
| Dense vector retrieval and text embeddings | Dense Passage Retrieval and official model documentation | `Karpukhin2020DPR`, `OpenAIEmbeddingModel` | Chapter 2: Vector Embeddings; Chapter 5: Baseline Semantic Retrieval |
| Cosine similarity in vector-space retrieval | Manning, Raghavan, and Schütze | `Manning2008IR` | Chapter 2: Semantic Similarity; Chapter 5: Baseline Semantic Retrieval |
| Native vector persistence and distance operators | Official pgvector project documentation | `PgvectorDocs` | Chapters 2--5 |
| Retrieval-Augmented Generation | Lewis et al., original RAG paper | `Lewis2020RAG` | Chapter 1; Chapter 2: RAG; Chapter 3: Retrieval Architecture; Chapter 5: Context Construction |
| Lexical retrieval and PostgreSQL full-text search | IR textbook and versioned official PostgreSQL documentation | `Manning2008IR`, `PostgreSQLFTS` | Chapter 2: Lexical Retrieval; Chapter 3: Persistence; Chapter 5: PostgreSQL Lexical Retrieval |
| Hybrid sparse and dense retrieval | Luan et al. | `Luan2021SparseDense` | Chapter 2: Hybrid Retrieval |
| Reciprocal Rank Fusion | Cormack, Clarke, and Büttcher, original RRF paper | `Cormack2009RRF` | Chapter 2: RRF; Chapter 3: Retrieval Architecture; Chapter 5: Hybrid Retrieval and Weighted RRF |
| Second-stage passage reranking | Nogueira and Cho | `Nogueira2019BERT` | Chapter 2: Reranking; Chapter 3: Retrieval Architecture; Chapter 5: Model-Based Reranking |
| Grounding, citation support, and verifiability | Liu, Zhang, and Liang | `Liu2023Verifiability` | Chapter 1; Chapter 2: Grounding and Citations; Chapter 5: Authoritative Citations |
| Indirect prompt injection and defense in depth | Abdelnabi et al. and OWASP guidance | `Abdelnabi2023IndirectPromptInjection`, `OWASPPromptInjection` | Chapter 2: Prompt Injection; Chapter 3: Security Boundaries; Chapter 5: Prompt-Injection Defense |
| Software-testing levels and strategy | Ammann and Offutt | `Ammann2016SoftwareTesting` | Chapter 7: Automated Testing Strategy |
| OCR Character Error Rate | OCR-D quality-assurance specification | `OCRDEvaluation` | Chapter 7: Evaluation Methodology |
| Accessibility terminology | Web Content Accessibility Guidelines 2.2 | `WCAG22` | Chapter 6: Feedback and Accessibility |
| Provider embedding and structured-response interfaces | Official OpenAI model, Embeddings API, and Responses API documentation | `OpenAIEmbeddingModel`, `OpenAIEmbeddingsAPI`, `OpenAIResponsesAPI` | Chapter 2: Embeddings; Chapter 4: Document Understanding and Embedding Generation; Chapter 5: Reranking and Answer Generation |
| Concrete extraction and rendering libraries | Official PdfPig, PDFtoImage, and Microsoft Open XML SDK documentation | `PdfPigDocs`, `PDFtoImageDocs`, `MicrosoftOpenXMLSDK` | Chapter 4: Text Extraction and Selective Local OCR |

### Reviewed markers that did not require an external citation

The following eight markers were removed without adding bibliography entries:

- **Backend framework composition (two markers):** the controller, dependency-injection,
  hosted-service, EF Core, and Npgsql roles are direct descriptions of the
  checked-in solution structure. No claim of framework superiority or general
  behavior remains.
- **React/TypeScript/Vite identity:** Chapter 3 states the frontend stack used by
  this repository. Package-lock and project files are the source of truth.
- **ASP.NET Core Identity configuration:** Chapters 3 and 6 describe the actual
  cookie and ownership configuration. These are implementation facts, not
  external theoretical claims.
- **Conservative de-hyphenation:** Chapter 4 explains the student's engineering
  policy. General normalization is already cited in Chapter 2; no source is
  presented as prescribing the exact project rule.
- **Conversational context selection:** Chapter 5 was minimally reworded to
  describe the implemented distinction between continuity context and retrieval
  evidence. It no longer implies reliance on a separate conversational-RAG
  method.
- **Technology-stack table:** Chapter 6 lists repository technologies and their
  local roles; it does not compare or endorse the frameworks generally.

## UNRESOLVED

### Model-provider pricing and usage accounting at final evaluation time

- **Reason unresolved:** prices, billing units, and applicable model identifiers
  are time-dependent, while the final evaluation has not yet been performed.
  Adding today's price would create a stale or misleading reference for a later
  measurement.
- **Recommended future source/research action:** during the final evaluation,
  record the exact model identifiers and usage fields, then cite the official
  provider pricing and usage documentation as accessed on that evaluation date.
- **Thesis location:** Chapter 7, Evaluation Metrics and Comparison Plan.
- **Current marker:** intentionally retained as
  `\citationneeded{official model-provider pricing and usage accounting at final evaluation time}`.

## Audit totals

- Resolved topic groups: **22**
- Unresolved topic groups: **1**
- Original citation-needed markers: **58**
- Markers replaced with verified citations: **49**
- Markers reclassified as repository facts: **8**
- Markers intentionally remaining: **1**
- Verified bibliography entries: **25**
