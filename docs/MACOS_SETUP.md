# macOS Local Development Setup

This guide is derived from the repository as of the .NET 10/Vite 8 application. It supports both Apple Silicon and Intel Homebrew installations and does not hardcode a Homebrew prefix.

## A. First-time macOS setup

### Prerequisites already expected

- macOS with Homebrew, Git, VS Code, and the .NET 10 SDK
- this repository cloned and checked out

From the repository root, run the setup script:

```zsh
./scripts/setup-macos.sh
```

The script is idempotent and does the following in order:

1. verifies macOS, Homebrew, Git, and .NET 10;
2. installs `node`, `postgresql@18`, `pgvector`, `tesseract`, and `tesseract-lang` with Homebrew;
3. verifies the `eng` and `ron` Tesseract language files under `$(brew --prefix)/share/tessdata`;
4. starts the per-user PostgreSQL service;
5. creates the `ai_document_assistant` database when it does not already exist, using the current macOS user and Homebrew's local trust authentication;
6. stores the local connection string and tessdata path in .NET user-secrets;
7. restores .NET tools/packages, runs `npm ci`, builds the frontend and solution, applies EF Core migrations, and creates an ASP.NET development certificate.

The script does not store an OpenAI key. Add it without putting the value in shell history:

```zsh
read -s "OPENAI_KEY?OpenAI API key: "
echo
dotnet user-secrets set \
  --project AI.DocumentAssistant.Server/AI.DocumentAssistant.Server.csproj \
  "OpenAI:ApiKey" "$OPENAI_KEY"
unset OPENAI_KEY
```

The exact environment-variable equivalent is `OpenAI__ApiKey`, but .NET user-secrets is recommended for local development. The key is read only by the ASP.NET backend; the React client has no OpenAI key or other `VITE_*` configuration.

Trust the development HTTPS certificate once:

```zsh
dotnet dev-certs https --trust
```

macOS may show a Keychain confirmation. Approve it. The Vite development server exports a PEM copy of that development certificate beneath `~/.aspnet/https` when first started.

### Configuration reference

| Configuration key | Environment variable | Required | Sensitive | Purpose | Recommended macOS configuration |
|---|---|---:|---:|---|---|
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | Yes | Usually | PostgreSQL connection used by EF Core and retrieval | Set automatically by `setup-macos.sh` in .NET user-secrets. The local value has no password. |
| `OpenAI:ApiKey` | `OpenAI__ApiKey` | Yes for upload embeddings, document understanding, reranking, and answers | Yes | Authenticates backend OpenAI API calls | Set manually with the no-history `read -s` command above. Never expose it as `VITE_*`. |
| `Ocr:TessDataPath` | `Ocr__TessDataPath` | Yes for the Homebrew OCR setup | No | Directory containing `eng.traineddata` and `ron.traineddata` | Set automatically to `$(brew --prefix)/share/tessdata` in .NET user-secrets. |

These values have checked-in defaults and do not need local configuration: `OpenAI:EmbeddingModel`, `OpenAI:EmbeddingDimensions`, `OpenAI:BatchSize`, `OpenAI:AnswerModel`, `OpenAI:RerankingModel`, `OpenAI:DocumentUnderstandingModel`, the remaining answer/retrieval limits, and all `Ocr` options other than the machine-specific tessdata path. Environment-variable overrides use the normal double-underscore mapping, for example `Ocr__Languages=ron+eng`.

The `https` launch profile supplies `ASPNETCORE_ENVIRONMENT=Development` and `ASPNETCORE_HOSTINGSTARTUPASSEMBLIES=Microsoft.AspNetCore.SpaProxy`. SpaProxy supplies the backend URL/port information consumed by Vite. No CORS setting or frontend `.env` file is required.

### Local data locations

- Database cluster: `$(brew --prefix)/var/postgresql@18`
- OCR language data: `$(brew --prefix)/share/tessdata`
- Uploaded files: `AI.DocumentAssistant.Server/Uploads` (created on first upload and ignored by Git)
- .NET user-secrets: managed outside the repository under the project `UserSecretsId`

## B. Daily startup

From the repository root:

```zsh
brew services start postgresql@18
dotnet run \
  --project AI.DocumentAssistant.Server/AI.DocumentAssistant.Server.csproj \
  --launch-profile https
```

Then open `https://localhost:56235`. Vite serves the React frontend there and proxies `/api` to the ASP.NET backend at `https://localhost:7189`. The backend also listens on `http://localhost:5297`, but the HTTPS profile is the normal development workflow.

Stop the application with `Control-C` in its terminal. PostgreSQL may remain running between sessions. To stop it explicitly:

```zsh
brew services stop postgresql@18
```

If PostgreSQL was stopped, the first daily command starts it again. `brew services start` is safe when the service is already running.

## Useful verification commands

```zsh
node --version
npm --version
"$(brew --prefix postgresql@18)/bin/pg_isready" -h localhost -p 5432
tesseract --list-langs | grep -E '^(eng|ron)$'
dotnet test AI.DocumentAssistant.slnx --no-build
npm --prefix ai.documentassistant.client run lint
```

To inspect configured user-secret names without copying their values into a report, run `dotnet user-secrets list --project AI.DocumentAssistant.Server/AI.DocumentAssistant.Server.csproj` locally and keep the output private.

## OCR/PDF behavior on macOS

`PDFtoImage` supplies `osx-arm64` and `osx-x64` PDFium assets plus a universal SkiaSharp native library through NuGet. No Homebrew PDF renderer is required. The `TesseractOCR` NuGet package supplies Windows-native binaries, so the application retains that in-process implementation on Windows and uses the Homebrew `tesseract` CLI on macOS/Unix. Both implementations honor the same `Ocr` configuration and produce the same application-level OCR result contract.

See [OCR_SETUP.md](OCR_SETUP.md) for the manual document acceptance scenarios.
