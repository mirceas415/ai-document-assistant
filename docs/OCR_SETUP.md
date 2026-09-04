# Local OCR Setup and Acceptance Test

Milestone 12 never downloads OCR models and never sends page images or OCR text to an OCR/vision service. Automated tests use fake renderers and OCR services, so local native setup is needed only to exercise real PDFium/Tesseract recognition.

## Required local files

Use the official Tesseract **tessdata_fast** model family initially. Keep all selected language files from the same family.

For local development from this repository, create:

```text
AI.DocumentAssistant.Server\tessdata\
  eng.traineddata
  ron.traineddata
```

The two required files are available from the official repositories:

- `eng.traineddata`: <https://github.com/tesseract-ocr/tessdata_fast/raw/main/eng.traineddata>
- `ron.traineddata`: <https://github.com/tesseract-ocr/tessdata_fast/raw/main/ron.traineddata>

Do not rename the files and do not place them in a nested `tessdata_fast` directory unless `Ocr:TessDataPath` points there. The architecture also accepts official `tessdata_best` files later; replace both files together and force an OCR rebuild. Their changed content fingerprint invalidates automatic reuse.

For a published deployment, place the same `tessdata` directory under the application's content root, next to the deployed server content, or configure an explicit deployment-local directory. Model binaries are intentionally not committed by Milestone 12.

## Native runtime prerequisite

The project references `TesseractOCR` 5.5.2 and `PDFtoImage` 5.4.0. PDFtoImage brings the supported PDFium/Skia package assets through NuGet. On Windows x64, install the current **Microsoft Visual C++ Redistributable for Visual Studio 2015–2022 (x64)** if it is not already present; the Tesseract native wrapper depends on that runtime. Match the application architecture if publishing for another runtime.

Missing native libraries or traineddata do not prevent ASP.NET Core startup. They are reported only when an M11 `Scanned` page actually requires OCR; text PDFs and DOCX processing remain available.

## Configuration

The checked-in defaults are:

```json
"Ocr": {
  "Enabled": true,
  "TessDataPath": "tessdata",
  "Languages": "ron+eng",
  "RenderDpi": 300,
  "MaxCandidatePages": 200,
  "MaxRenderedPixels": 25000000
}
```

A relative `TessDataPath` is resolved beneath the ASP.NET Core content root. Deployment environment variables use the normal .NET form, for example `Ocr__TessDataPath`, `Ocr__Languages`, and `Ocr__RenderDpi`. Keep `Languages` to installed, plus-separated Tesseract codes. No configuration value is returned by the API if it exposes a local filesystem path.

## Manual acceptance test

Apply the M12 database migration manually first, configure both models, start the application, sign in, and upload the following three representative PDFs to an owned workspace. Use the Documents page's Technical Analysis, OCR, extracted-text, and chunk views; use Ask Your Documents only after ingestion is Ready.

### A. Text-based PDF

1. Upload a PDF with a meaningful native text layer.
2. Confirm Technical Analysis reports `TextBased` pages.
3. Confirm OCR reports `Skipped` / **Not required**, zero candidate pages, and no candidate table.
4. Confirm extracted text retains `NativePdf` provenance and matches existing native extraction behavior.

Expected: PDFium and Tesseract are not invoked; normalization, M10, chunks, embeddings, and RAG work as before.

### B. Fully scanned PDF

1. Upload a raster-scanned Romanian/English PDF with no useful native text layer.
2. Confirm Technical Analysis reports each scan page as `Scanned`.
3. Confirm OCR reports `Ready` (or an honest page-level `Partial` if a page fails), Tesseract, `ron+eng`, candidate diagnostics, and effective DPI/dimensions.
4. Open extracted text and confirm recognized content appears as ordered raw page sections with `Ocr` provenance; confirm no diagnostic/error placeholder appears in content.
5. Confirm normalized text, document understanding, chunks, and embeddings were regenerated and the document becomes Ready when usable text exists.
6. Ask a grounded question whose answer occurs only in the scan and verify its citation.

Expected: every M11 `Scanned` page is rendered individually and recognized locally; OCR text enters the same downstream representation as native text.

### C. Mixed PDF

Use a PDF containing meaningful native pages, full-page scanned pages, and—if available—an `ImageBased` or page-level `Mixed` example.

1. Record the ordered M11 page classifications.
2. Confirm the OCR candidate table contains only pages classified `Scanned`.
3. Confirm `TextBased` and page-level `Mixed` pages retain native PdfPig text, while `ImageBased` and `Unknown` pages are not automatically OCRed.
4. Confirm the raw extraction preserves original page order across alternating `NativePdf` and `Ocr` sections.
5. Confirm RAG can use native and OCR-derived content together.

Expected: routing is page-level even when the document aggregate is `Mixed`.

## Forced rebuild and cost distinction

Under **Advanced**, choose **Rebuild OCR** and accept the application confirmation. This bypasses OCR reuse and reruns OCR-aware extraction plus normalization, existing M10 Document Understanding, chunking, and embeddings.

PDF rendering and Tesseract recognition are entirely local and make zero OpenAI requests. The already-existing M10 and embedding stages may naturally make their configured OpenAI requests after extracted content changes. No OpenAI Vision or other cloud OCR request exists.
