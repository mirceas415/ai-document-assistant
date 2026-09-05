# Source Verification Audit

This file records the source-level audit for every entry in `ace-bibtex.bib`.
Bibliographic metadata was checked against a primary publisher, standards body,
official documentation site, or official project page. Web documentation was
accessed on **5 September 2026**. `N/A` means that a field does not apply or was
deliberately omitted because the authoritative page did not provide it.

## Academic papers

### Lewis2020RAG

- Citation key: `Lewis2020RAG`
- Source type: Peer-reviewed conference paper
- Verified title: Retrieval-Augmented Generation for Knowledge-Intensive NLP Tasks
- Verified author/organization: Patrick Lewis; Ethan Perez; Aleksandra Piktus; Fabio Petroni; Vladimir Karpukhin; Naman Goyal; Heinrich Küttler; Mike Lewis; Wen-tau Yih; Tim Rocktäschel; Sebastian Riedel; Douwe Kiela
- Verified year: 2020
- Verified venue/publisher: Advances in Neural Information Processing Systems 33 (NeurIPS 2020), pp. 9459--9474
- DOI: N/A
- Canonical URL: https://proceedings.neurips.cc/paper/2020/hash/6b493230-Abstract.html
- Verification source: Official NeurIPS proceedings record and paper
- Used in thesis sections: Chapter 1 context; Chapter 2 Retrieval-Augmented Generation; Chapter 3 retrieval and question-answering architecture; Chapter 5 RAG context construction
- Notes: Cited for the RAG paradigm, not as evidence for this project's configuration or measured quality.

### Cormack2009RRF

- Citation key: `Cormack2009RRF`
- Source type: Peer-reviewed conference paper
- Verified title: Reciprocal Rank Fusion Outperforms Condorcet and Individual Rank Learning Methods
- Verified author/organization: Gordon V. Cormack; Charles L. A. Clarke; Stefan Büttcher
- Verified year: 2009
- Verified venue/publisher: Proceedings of the 32nd International ACM SIGIR Conference on Research and Development in Information Retrieval, ACM, pp. 758--759
- DOI: 10.1145/1571941.1572114
- Canonical URL: https://doi.org/10.1145/1571941.1572114
- Verification source: ACM Digital Library DOI record
- Used in thesis sections: Chapter 2 Reciprocal Rank Fusion; Chapter 3 retrieval architecture; Chapter 5 Hybrid Retrieval and Weighted Reciprocal Rank Fusion
- Notes: Supports rank-based fusion. The application's channel weights and metadata contribution remain project-specific.

### Karpukhin2020DPR

- Citation key: `Karpukhin2020DPR`
- Source type: Peer-reviewed conference paper
- Verified title: Dense Passage Retrieval for Open-Domain Question Answering
- Verified author/organization: Vladimir Karpukhin; Barlas Oğuz; Sewon Min; Patrick Lewis; Ledell Wu; Sergey Edunov; Danqi Chen; Wen-tau Yih
- Verified year: 2020
- Verified venue/publisher: Proceedings of the 2020 Conference on Empirical Methods in Natural Language Processing, Association for Computational Linguistics, pp. 6769--6781
- DOI: 10.18653/v1/2020.emnlp-main.550
- Canonical URL: https://aclanthology.org/2020.emnlp-main.550/
- Verification source: ACL Anthology publication record and BibTeX metadata
- Used in thesis sections: Chapter 2 chunking and vector embeddings; Chapter 4 document chunking; Chapter 5 semantic retrieval
- Notes: Supports the general dense passage-retrieval setting, not the thesis application's exact chunk sizes or embedding model.

### Luan2021SparseDense

- Citation key: `Luan2021SparseDense`
- Source type: Peer-reviewed journal article
- Verified title: Sparse, Dense, and Attentional Representations for Text Retrieval
- Verified author/organization: Yi Luan; Jacob Eisenstein; Kristina Toutanova; Michael Collins
- Verified year: 2021
- Verified venue/publisher: Transactions of the Association for Computational Linguistics, vol. 9, pp. 329--345, MIT Press
- DOI: 10.1162/tacl_a_00369
- Canonical URL: https://aclanthology.org/2021.tacl-1.20/
- Verification source: ACL Anthology publication record
- Used in thesis sections: Chapter 2 document chunking and hybrid retrieval; Chapter 4 document chunking
- Notes: Used to support the complementarity of sparse and dense representations and passage-level retrieval, not the project's weighted RRF formula.

### Nogueira2019BERT

- Citation key: `Nogueira2019BERT`
- Source type: Academic preprint
- Verified title: Passage Re-ranking with BERT
- Verified author/organization: Rodrigo Nogueira; Kyunghyun Cho
- Verified year: 2019
- Verified venue/publisher: arXiv preprint arXiv:1901.04085
- DOI: N/A
- Canonical URL: https://arxiv.org/abs/1901.04085
- Verification source: arXiv record, cross-checked with DBLP
- Used in thesis sections: Chapter 2 Reranking; Chapter 3 retrieval architecture; Chapter 5 Model-Based Reranking
- Notes: Supports the general two-stage passage-reranking concept. It does not describe the application's model-based JSON grading protocol.

### Smith2007Tesseract

- Citation key: `Smith2007Tesseract`
- Source type: Peer-reviewed conference paper
- Verified title: An Overview of the Tesseract OCR Engine
- Verified author/organization: Ray Smith
- Verified year: 2007
- Verified venue/publisher: Ninth International Conference on Document Analysis and Recognition, vol. 2, IEEE, pp. 629--633
- DOI: 10.1109/ICDAR.2007.4376991
- Canonical URL: https://doi.org/10.1109/ICDAR.2007.4376991
- Verification source: IEEE DOI record, cross-checked with the paper hosted by the Tesseract project
- Used in thesis sections: Chapter 2 Optical Character Recognition; Chapter 4 Selective Local OCR
- Notes: Describes the Tesseract engine historically; current usage details are supported separately by the official user manual.

### Liu2023Verifiability

- Citation key: `Liu2023Verifiability`
- Source type: Peer-reviewed conference paper
- Verified title: Evaluating Verifiability in Generative Search Engines
- Verified author/organization: Nelson F. Liu; Tianyi Zhang; Percy Liang
- Verified year: 2023
- Verified venue/publisher: Findings of the Association for Computational Linguistics: EMNLP 2023, pp. 7001--7025, Association for Computational Linguistics
- DOI: 10.18653/v1/2023.findings-emnlp.467
- Canonical URL: https://aclanthology.org/2023.findings-emnlp.467/
- Verification source: ACL Anthology publication record and paper
- Used in thesis sections: Chapter 1 context; Chapter 2 Grounding and Citations; Chapter 5 Authoritative Citations
- Notes: Supports evaluating whether citations entail associated claims. It is not used to claim that this application has already achieved a measured citation score.

### Abdelnabi2023IndirectPromptInjection

- Citation key: `Abdelnabi2023IndirectPromptInjection`
- Source type: Peer-reviewed workshop paper
- Verified title: Not What You've Signed Up For: Compromising Real-World LLM-Integrated Applications with Indirect Prompt Injection
- Verified author/organization: Sahar Abdelnabi; Kai Greshake; Shailesh Mishra; Christoph Endres; Thorsten Holz; Mario Fritz
- Verified year: 2023
- Verified venue/publisher: Proceedings of the 16th ACM Workshop on Artificial Intelligence and Security, ACM, pp. 79--90
- DOI: 10.1145/3605764.3623985
- Canonical URL: https://doi.org/10.1145/3605764.3623985
- Verification source: ACM publication DOI record, cross-checked with DBLP and the authors' preprint
- Used in thesis sections: Chapter 2 Prompt Injection; Chapter 3 Security Boundaries; Chapter 5 Prompt-Injection Defense
- Notes: Final ACM author order is used. The source motivates indirect prompt-injection risk; the thesis retains explicit residual-risk wording.

## Books and textbooks

### Manning2008IR

- Citation key: `Manning2008IR`
- Source type: Textbook
- Verified title: Introduction to Information Retrieval
- Verified author/organization: Christopher D. Manning; Prabhakar Raghavan; Hinrich Schütze
- Verified year: 2008
- Verified venue/publisher: Cambridge University Press
- DOI: N/A
- Canonical URL: https://nlp.stanford.edu/IR-book/
- Verification source: Official Stanford companion site for the Cambridge University Press book
- Used in thesis sections: Chapter 2 information retrieval, normalization, cosine similarity, and lexical retrieval; Chapter 5 semantic and metadata-aware retrieval; Chapter 7 retrieval metrics
- Notes: Used for established IR concepts and evaluation terminology, not project configuration.

### Ammann2016SoftwareTesting

- Citation key: `Ammann2016SoftwareTesting`
- Source type: Textbook
- Verified title: Introduction to Software Testing
- Verified author/organization: Paul Ammann; Jeff Offutt
- Verified year: 2016
- Verified venue/publisher: Cambridge University Press, 2nd edition
- DOI: 10.1017/9781316771273
- Canonical URL: https://doi.org/10.1017/9781316771273
- Verification source: Cambridge University Press book record
- Used in thesis sections: Chapter 7 Automated Testing Strategy
- Notes: Supports general testing-level terminology; the test-suite composition and count come from the repository and the completed test run.

## Official standards

### ISO32000PDF

- Citation key: `ISO32000PDF`
- Source type: International standard
- Verified title: ISO 32000-2:2020, Document management — Portable document format — Part 2: PDF 2.0
- Verified author/organization: International Organization for Standardization
- Verified year: 2020
- Verified venue/publisher: ISO, International Standard, 2nd edition
- DOI: N/A
- Canonical URL: https://www.iso.org/standard/75839.html
- Verification source: Official ISO standards catalogue record
- Used in thesis sections: Chapter 2 structured-document extraction; Chapter 4 Technical PDF Analysis
- Notes: Cited only for general PDF representation, not for the application's page-classification thresholds.

### ECMA376

- Citation key: `ECMA376`
- Source type: Official standard
- Verified title: ECMA-376, Office Open XML File Formats
- Verified author/organization: Ecma International
- Verified year: 2021
- Verified venue/publisher: Ecma International, 5th edition, December 2021
- DOI: N/A
- Canonical URL: https://ecma-international.org/publications-and-standards/standards/ecma-376/
- Verification source: Official Ecma International standard page
- Used in thesis sections: Chapter 2 structured-document extraction; Chapter 4 PDF and DOCX Text Extraction
- Notes: Supports OOXML packaging and WordprocessingML structure. Application-specific extraction behavior is repository-derived.

### WCAG22

- Citation key: `WCAG22`
- Source type: W3C Recommendation
- Verified title: Web Content Accessibility Guidelines (WCAG) 2.2
- Verified author/organization: World Wide Web Consortium
- Verified year: 2024
- Verified venue/publisher: W3C Recommendation, 12 December 2024
- DOI: N/A
- Canonical URL: https://www.w3.org/TR/2024/REC-WCAG22-20241212/
- Verification source: Official dated W3C Recommendation
- Used in thesis sections: Chapter 6 Feedback and Accessibility
- Notes: The dated Recommendation URL is used for stability. The thesis expressly does not claim full WCAG compliance.

## Official and project documentation

### PostgreSQLFTS

- Citation key: `PostgreSQLFTS`
- Source type: Official platform documentation
- Verified title: PostgreSQL 18 Documentation: Chapter 12, Full Text Search
- Verified author/organization: PostgreSQL Global Development Group
- Verified year: N/A
- Verified venue/publisher: PostgreSQL Documentation
- DOI: N/A
- Canonical URL: https://www.postgresql.org/docs/18/textsearch.html
- Verification source: Official PostgreSQL versioned documentation
- Used in thesis sections: Chapter 2 Lexical and Full-Text Retrieval; Chapter 3 persistence model; Chapter 5 PostgreSQL Lexical Retrieval
- Notes: Supports `tsvector`, `tsquery`, configurations, ranking, and GIN indexing. The versioned URL avoids an unstable `current` alias.

### PgvectorDocs

- Citation key: `PgvectorDocs`
- Source type: Official project documentation
- Verified title: pgvector: Open-Source Vector Similarity Search for PostgreSQL
- Verified author/organization: pgvector contributors
- Verified year: N/A
- Verified venue/publisher: Official pgvector repository and README
- DOI: N/A
- Canonical URL: https://github.com/pgvector/pgvector
- Verification source: Official pgvector project repository
- Used in thesis sections: Chapter 2 Vector Databases and pgvector; Chapter 3 persistence model; Chapter 4 Embedding Generation; Chapter 5 Baseline Semantic Retrieval
- Notes: Supports the native vector type, cosine-distance operator, exact search, and optional approximate indexes. The application's schema and filtering are repository facts.

### TesseractDocs

- Citation key: `TesseractDocs`
- Source type: Official project documentation
- Verified title: Tesseract User Manual
- Verified author/organization: Tesseract OCR
- Verified year: N/A
- Verified venue/publisher: Tesseract OCR documentation
- DOI: N/A
- Canonical URL: https://tesseract-ocr.github.io/tessdoc/
- Verification source: Official Tesseract documentation site
- Used in thesis sections: Chapter 2 Optical Character Recognition; Chapter 4 Selective Local OCR
- Notes: Used for current project identity and language-data concepts, alongside the 2007 engine paper.

### PdfPigDocs

- Citation key: `PdfPigDocs`
- Source type: Official project documentation
- Verified title: PdfPig: Read and Extract Text and Other Content from PDFs in C#
- Verified author/organization: PdfPig contributors
- Verified year: N/A
- Verified venue/publisher: Official PdfPig repository
- DOI: N/A
- Canonical URL: https://github.com/UglyToad/PdfPig
- Verification source: Official project repository and README
- Used in thesis sections: Chapter 4 PDF and DOCX Text Extraction
- Notes: Identifies the extraction library. The application's page-ordering and provenance behavior is verified from its source code, not inferred from this README.

### PDFtoImageDocs

- Citation key: `PDFtoImageDocs`
- Source type: Official project documentation
- Verified title: PDFtoImage: A .NET Library to Render PDF Files into Images
- Verified author/organization: PDFtoImage contributors
- Verified year: N/A
- Verified venue/publisher: Official PDFtoImage repository
- DOI: N/A
- Canonical URL: https://github.com/sungaila/PDFtoImage
- Verification source: Official project repository and README
- Used in thesis sections: Chapter 4 Selective Local OCR
- Notes: Supports the library's use of PDFium for rendering. OCR routing, DPI, and pixel limits are project-specific.

### MicrosoftOpenXMLSDK

- Citation key: `MicrosoftOpenXMLSDK`
- Source type: Official platform documentation
- Verified title: How to: Open and Add Text to a Word Processing Document
- Verified author/organization: Microsoft
- Verified year: N/A
- Verified venue/publisher: Microsoft Learn, Open XML SDK documentation
- DOI: N/A
- Canonical URL: https://learn.microsoft.com/en-us/office/open-xml/word/how-to-open-and-add-text-to-a-word-processing-document
- Verification source: Official Microsoft Learn page
- Used in thesis sections: Chapter 4 PDF and DOCX Text Extraction
- Notes: Supports the SDK's strongly typed access to WordprocessingML elements. ECMA-376 is the format specification.

### MicrosoftMLTokenizers

- Citation key: `MicrosoftMLTokenizers`
- Source type: Official platform documentation
- Verified title: Use Microsoft.ML.Tokenizers for Text Tokenization
- Verified author/organization: Microsoft
- Verified year: 2026
- Verified venue/publisher: Microsoft Learn
- DOI: N/A
- Canonical URL: https://learn.microsoft.com/en-us/dotnet/ai/how-to/use-tokenizers
- Verification source: Official Microsoft Learn page, last updated 9 April 2026
- Used in thesis sections: Chapter 2 Tokenization; Chapter 4 Document Chunking
- Notes: Supports Tiktoken token counting, encoding and decoding, and character-index operations. The selected `cl100k_base` encoding and consistent-budget policy are repository-derived implementation facts.

### OpenAIEmbeddingModel

- Citation key: `OpenAIEmbeddingModel`
- Source type: Official provider documentation
- Verified title: text-embedding-3-small Model
- Verified author/organization: OpenAI
- Verified year: N/A
- Verified venue/publisher: OpenAI API documentation
- DOI: N/A
- Canonical URL: https://developers.openai.com/api/docs/models/text-embedding-3-small
- Verification source: Official OpenAI model page
- Used in thesis sections: Chapter 2 Vector Embeddings; Chapter 4 Embedding Generation
- Notes: Supports the identity and purpose of the configured embedding model. The application's requested 1,536 dimensions are a repository configuration fact.

### OpenAIEmbeddingsAPI

- Citation key: `OpenAIEmbeddingsAPI`
- Source type: Official provider API reference
- Verified title: Create Embeddings
- Verified author/organization: OpenAI
- Verified year: N/A
- Verified venue/publisher: OpenAI API Reference
- DOI: N/A
- Canonical URL: https://developers.openai.com/api/reference/resources/embeddings/methods/create
- Verification source: Official OpenAI API reference for `POST /embeddings`
- Used in thesis sections: Chapter 4 Embedding Generation
- Notes: Supports batched inputs, returned vectors and indexes, and the configurable dimensions parameter for `text-embedding-3` models. It is not used to prove project batch size.

### OpenAIResponsesAPI

- Citation key: `OpenAIResponsesAPI`
- Source type: Official provider API reference
- Verified title: Create a Model Response
- Verified author/organization: OpenAI
- Verified year: N/A
- Verified venue/publisher: OpenAI API Reference
- DOI: N/A
- Canonical URL: https://developers.openai.com/api/reference/resources/responses/methods/create
- Verification source: Official OpenAI API reference for `POST /responses`
- Used in thesis sections: Chapter 4 Document Understanding; Chapter 5 Model-Based Reranking and Grounded Answer Generation
- Notes: Supports text/JSON outputs, instructions, output limits, and response-storage controls. Specific prompts, schemas, models, and token limits are repository facts.

### OCRDEvaluation

- Citation key: `OCRDEvaluation`
- Source type: Official project specification
- Verified title: Quality Assurance in OCR-D
- Verified author/organization: OCR-D
- Verified year: N/A
- Verified venue/publisher: OCR-D specifications
- DOI: N/A
- Canonical URL: https://ocr-d.de/en/spec/ocrd_eval.html
- Verification source: Official OCR-D specification page
- Used in thesis sections: Chapter 7 Evaluation Methodology
- Notes: Supports Character Error Rate as edit distance normalized by reference length. No OCR result is inferred from this source.

### OWASPPromptInjection

- Citation key: `OWASPPromptInjection`
- Source type: Official security guidance
- Verified title: LLM01:2025 Prompt Injection
- Verified author/organization: OWASP Foundation
- Verified year: 2025
- Verified venue/publisher: OWASP GenAI Security Project
- DOI: N/A
- Canonical URL: https://genai.owasp.org/llmrisk/llm01-prompt-injection/
- Verification source: Official OWASP GenAI Security Project page
- Used in thesis sections: Chapter 2 Prompt Injection; Chapter 3 Security Boundaries; Chapter 5 Prompt-Injection Defense
- Notes: Used as practical defense-in-depth guidance alongside a peer-reviewed attack paper. The thesis does not claim complete prevention.
