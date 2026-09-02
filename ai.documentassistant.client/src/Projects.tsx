import { useCallback, useEffect, useRef, useState } from 'react';
import type { Dispatch, FormEvent, ReactNode, SetStateAction } from 'react';
import {
    ApiRequestError,
    apiRequest,
    getErrorMessage,
} from './api';
import { ConfirmDialog, Icon, Skeleton } from './Ui';
import { useToast } from './toast-context';
import {
    DOCUMENT_FILE_ACCEPT,
    uploadWorkspaceDocument,
    validateDocumentFile,
} from './document-upload';
import type {
    AskProjectResponse,
    CurrentUser,
    DocumentChunk,
    DocumentDetails,
    DocumentSummary,
    DocumentUnderstanding,
    DocumentUnderstandingStatus,
    ExtractedTextSection,
    ProjectDetails,
    ProjectSummary,
    SemanticSearchResponse,
} from './api';

type Navigate = (path: string, replace?: boolean) => void;

interface AuthenticatedPageProps {
    user: CurrentUser;
    onNavigate: Navigate;
    onSignedOut: () => void;
}

export function ProjectsDashboard({ user, onNavigate, onSignedOut }: AuthenticatedPageProps) {
    const [projects, setProjects] = useState<ProjectSummary[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState('');
    const [editor, setEditor] = useState<ProjectSummary | 'create' | null>(null);
    const [deletingId, setDeletingId] = useState<string | null>(null);
    const [projectToDelete, setProjectToDelete] = useState<ProjectSummary | null>(null);
    const showToast = useToast();

    useEffect(() => {
        let isActive = true;

        const loadProjects = async () => {
            try {
                const response = await apiRequest<ProjectSummary[]>('/api/projects');
                if (isActive) setProjects(response);
            } catch (requestError) {
                if (isActive) setError(getErrorMessage(requestError));
            } finally {
                if (isActive) setIsLoading(false);
            }
        };

        void loadProjects();
        return () => { isActive = false; };
    }, []);

    const savedProject = (project: ProjectDetails) => {
        const wasCreated = editor === 'create';
        setProjects((currentProjects) => {
            const remainingProjects = currentProjects.filter((item) => item.id !== project.id);
            return [project, ...remainingProjects]
                .sort((left, right) => Date.parse(right.updatedAtUtc) - Date.parse(left.updatedAtUtc));
        });
        setEditor(null);
        showToast({ message: wasCreated ? 'Workspace created.' : 'Workspace updated.' });
    };

    const deleteProject = async (project: ProjectSummary) => {
        setError('');
        setDeletingId(project.id);

        try {
            await apiRequest<void>(`/api/projects/${project.id}`, { method: 'DELETE' });
            setProjects((currentProjects) =>
                currentProjects.filter((item) => item.id !== project.id));
            setProjectToDelete(null);
            showToast({ message: 'Workspace deleted.' });
        } catch (requestError) {
            setError(getErrorMessage(requestError));
            setProjectToDelete(null);
        } finally {
            setDeletingId(null);
        }
    };

    return (
        <AuthenticatedLayout user={user} onNavigate={onNavigate} onSignedOut={onSignedOut}>
            <main className="page-content">
                <div className="page-heading">
                    <div>
                        <p className="eyebrow">Your document library</p>
                        <h1>Workspaces</h1>
                        <p className="page-description">Create and organize reusable document workspaces.</p>
                    </div>
                    <button className="primary-button" type="button" onClick={() => setEditor('create')}>
                        <Icon name="plus" size={17} /> New workspace
                    </button>
                </div>

                {error && <div className="alert page-alert" role="alert">{error}</div>}

                {isLoading ? (
                    <ProjectListSkeleton />
                ) : projects.length === 0 ? (
                    <section className="content-state empty-state">
                        <div className="empty-icon" aria-hidden="true">▦</div>
                        <h2>No workspaces yet</h2>
                        <p>Create your first workspace to get started.</p>
                        <button className="primary-button" type="button" onClick={() => setEditor('create')}>
                            Create a workspace
                        </button>
                    </section>
                ) : (
                    <section className="project-grid" aria-label="Workspaces">
                        {projects.map((project) => (
                            <article className="project-card" key={project.id}>
                                <div className="project-card-heading">
                                    <div className="project-icon" aria-hidden="true">W</div>
                                    <div>
                                        <h2>{project.name}</h2>
                                        <p className="project-description">
                                            {project.description || 'No description provided.'}
                                        </p>
                                    </div>
                                </div>

                                <dl className="project-dates">
                                    <div>
                                        <dt>Created</dt>
                                        <dd>{formatDate(project.createdAtUtc)}</dd>
                                    </div>
                                    <div>
                                        <dt>Updated</dt>
                                        <dd>{formatDate(project.updatedAtUtc)}</dd>
                                    </div>
                                </dl>

                                <div className="project-actions">
                                    <button className="primary-button compact-button" type="button" onClick={() => onNavigate(`/projects/${project.id}`)}>
                                        <Icon name="chat" size={14} /> Open
                                    </button>
                                    <button className="secondary-button compact-button" type="button" onClick={() => setEditor(project)}>
                                        <Icon name="edit" size={14} /> Edit
                                    </button>
                                    <button
                                        className="danger-button compact-button"
                                        type="button"
                                        disabled={deletingId === project.id}
                                        onClick={() => setProjectToDelete(project)}
                                    >
                                        <Icon name="delete" size={14} /> {deletingId === project.id ? 'Deleting…' : 'Delete'}
                                    </button>
                                </div>
                            </article>
                        ))}
                    </section>
                )}
            </main>

            {editor && (
                <ProjectEditor
                    key={editor === 'create' ? 'create' : editor.id}
                    project={editor === 'create' ? null : editor}
                    onCancel={() => setEditor(null)}
                    onSaved={savedProject}
                />
            )}
            <ConfirmDialog
                open={Boolean(projectToDelete)}
                title="Delete workspace?"
                description={<>This permanently deletes <strong>{projectToDelete?.name}</strong>, including its documents and conversations. This cannot be undone.</>}
                confirmLabel="Delete workspace"
                busy={Boolean(deletingId)}
                onCancel={() => setProjectToDelete(null)}
                onConfirm={() => projectToDelete && void deleteProject(projectToDelete)}
            />
        </AuthenticatedLayout>
    );
}

interface ProjectDetailsPageProps extends AuthenticatedPageProps {
    projectId: string;
}

export function ProjectDetailsPage({
    user,
    projectId,
    onNavigate,
    onSignedOut,
}: ProjectDetailsPageProps) {
    const [project, setProject] = useState<ProjectDetails | null>(null);
    const [documents, setDocuments] = useState<DocumentSummary[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState('');
    const [notFound, setNotFound] = useState(false);

    useEffect(() => {
        let isActive = true;

        const loadProject = async () => {
            if (!isGuid(projectId)) {
                setNotFound(true);
                setIsLoading(false);
                return;
            }

            try {
                const [projectResponse, documentResponse] = await Promise.all([
                    apiRequest<ProjectDetails>(`/api/projects/${projectId}`),
                    apiRequest<DocumentSummary[]>(`/api/projects/${projectId}/documents`),
                ]);

                if (isActive) {
                    setProject(projectResponse);
                    setDocuments(documentResponse);
                }
            } catch (requestError) {
                if (!isActive) return;

                if (requestError instanceof ApiRequestError && requestError.status === 404) {
                    setNotFound(true);
                } else {
                    setError(getErrorMessage(requestError));
                }
            } finally {
                if (isActive) setIsLoading(false);
            }
        };

        void loadProject();
        return () => { isActive = false; };
    }, [projectId]);

    return (
        <AuthenticatedLayout user={user} onNavigate={onNavigate} onSignedOut={onSignedOut}>
            <main className="page-content details-content">
                <button className="back-button" type="button" onClick={() => onNavigate('/projects')}>
                    <Icon name="arrow-left" size={16} /> Back to workspaces
                </button>

                {isLoading ? (
                    <ProjectDetailsSkeleton />
                ) : notFound ? (
                    <section className="content-state empty-state">
                        <div className="empty-icon" aria-hidden="true">?</div>
                        <h1>Workspace not found</h1>
                        <p>The workspace does not exist or is not available to your account.</p>
                        <button className="primary-button" type="button" onClick={() => onNavigate('/projects')}>
                            Return to workspaces
                        </button>
                    </section>
                ) : error ? (
                    <div className="alert page-alert" role="alert">{error}</div>
                ) : project ? (
                    <>
                        <div className="project-view-switcher">
                            <button className="secondary-button" type="button" onClick={() => onNavigate(`/projects/${project.id}`)}>
                                Open chats
                            </button>
                        </div>
                        <section className="details-card">
                            <div className="project-icon large-project-icon" aria-hidden="true">W</div>
                            <p className="eyebrow">Workspace</p>
                            <h1>{project.name}</h1>
                            <p className="details-description">
                                {project.description || 'No description provided.'}
                            </p>

                            <dl className="details-dates">
                                <div>
                                    <dt>Created</dt>
                                    <dd>{formatDate(project.createdAtUtc)}</dd>
                                </div>
                                <div>
                                    <dt>Last updated</dt>
                                    <dd>{formatDate(project.updatedAtUtc)}</dd>
                                </div>
                            </dl>
                        </section>

                        <DocumentsSection
                            key={project.id}
                            projectId={project.id}
                            documents={documents}
                            onDocumentsChanged={setDocuments}
                        />
                    </>
                ) : null}
            </main>
        </AuthenticatedLayout>
    );
}

interface AskDocumentsSectionProps {
    projectId: string;
}

export function AskDocumentsSection({ projectId }: AskDocumentsSectionProps) {
    const [question, setQuestion] = useState('');
    const [response, setResponse] = useState<AskProjectResponse | null>(null);
    const [isAsking, setIsAsking] = useState(false);
    const [error, setError] = useState('');

    const ask = async (event: FormEvent<HTMLFormElement>) => {
        event.preventDefault();
        setError('');

        const normalizedQuestion = question.trim();
        if (!normalizedQuestion) {
            setError('Enter a question about the documents in this workspace.');
            return;
        }

        if (normalizedQuestion.length > 2_000) {
            setError('The question cannot exceed 2,000 characters.');
            return;
        }

        setIsAsking(true);
        setResponse(null);

        try {
            const askResponse = await apiRequest<AskProjectResponse>(
                `/api/projects/${projectId}/ask`,
                {
                    method: 'POST',
                    body: JSON.stringify({ question: normalizedQuestion }),
                },
            );
            setResponse(askResponse);
        } catch (requestError) {
            setError(getErrorMessage(requestError));
        } finally {
            setIsAsking(false);
        }
    };

    return (
        <section className="ask-card" aria-labelledby="ask-documents-heading">
            <div className="ask-heading">
                <div>
                    <p className="eyebrow">Grounded Q&amp;A</p>
                    <h2 id="ask-documents-heading">Ask Your Documents</h2>
                    <p>Ask one question at a time and receive an answer grounded in workspace sources.</p>
                </div>
            </div>

            {error && <div className="alert ask-alert" role="alert">{error}</div>}

            <form className="ask-form" onSubmit={ask}>
                <label htmlFor="ask-documents-question">Question</label>
                <textarea
                    id="ask-documents-question"
                    value={question}
                    maxLength={2_000}
                    rows={3}
                    placeholder="What are the main conditions described in these documents?"
                    onChange={(event) => setQuestion(event.target.value)}
                />
                <div className="ask-form-footer">
                    <p className="character-count">{question.length.toLocaleString()} / 2,000</p>
                    <button className="primary-button" type="submit" disabled={isAsking}>
                        {isAsking ? 'Generating answer…' : 'Ask'}
                    </button>
                </div>
            </form>

            {isAsking ? (
                <div className="ask-progress" aria-live="polite">
                    <div className="spinner small-spinner" aria-hidden="true" />
                    <span>Searching sources and generating a grounded answer…</span>
                </div>
            ) : response ? (
                <div className="ask-response" aria-live="polite">
                    <div className="ask-answer-panel">
                        <p className="eyebrow">Answer</p>
                        <div className="ask-answer">{response.answer}</div>
                    </div>

                    {response.sources.length > 0 && (
                        <div className="ask-sources">
                            <h3>Sources ({response.sources.length})</h3>
                            <ol className="ask-source-list">
                                {response.sources.map((source) => (
                                    <li className="ask-source-card" key={`${source.sourceId}-${source.chunkId}`}>
                                        <div className="ask-source-heading">
                                            <span className="ask-source-id">[{source.sourceId}]</span>
                                            <h4>{source.documentName}</h4>
                                        </div>
                                        <div className="ask-source-meta">
                                            <span>Chunk {source.chunkIndex + 1}</span>
                                            <span>{formatPageRange(source.pageStart, source.pageEnd)}</span>
                                        </div>
                                        {source.heading && <h5>{source.heading}</h5>}
                                        <p className="ask-source-excerpt">{source.excerpt}</p>
                                    </li>
                                ))}
                            </ol>
                        </div>
                    )}
                </div>
            ) : null}
        </section>
    );
}

interface SemanticSearchSectionProps {
    projectId: string;
}

export function SemanticSearchSection({ projectId }: SemanticSearchSectionProps) {
    const [query, setQuery] = useState('');
    const [response, setResponse] = useState<SemanticSearchResponse | null>(null);
    const [isSearching, setIsSearching] = useState(false);
    const [error, setError] = useState('');

    const search = async (event: FormEvent<HTMLFormElement>) => {
        event.preventDefault();
        setError('');

        const normalizedQuery = query.trim();
        if (!normalizedQuery) {
            setError('Enter a question or phrase to search for.');
            return;
        }

        if (normalizedQuery.length > 2_000) {
            setError('The search query cannot exceed 2,000 characters.');
            return;
        }

        setIsSearching(true);
        setResponse(null);

        try {
            const searchResponse = await apiRequest<SemanticSearchResponse>(
                `/api/projects/${projectId}/search`,
                {
                    method: 'POST',
                    body: JSON.stringify({ query: normalizedQuery, topK: 8 }),
                },
            );
            setResponse(searchResponse);
        } catch (requestError) {
            setError(getErrorMessage(requestError));
        } finally {
            setIsSearching(false);
        }
    };

    return (
        <section className="retrieval-card" aria-labelledby="semantic-search-heading">
            <div className="retrieval-heading">
                <div>
                    <p className="eyebrow">Retrieval debug</p>
                    <h2 id="semantic-search-heading">Semantic Search</h2>
                    <p>Find the closest embedded chunks across ready documents in this workspace.</p>
                </div>
            </div>

            {error && <div className="alert retrieval-alert" role="alert">{error}</div>}

            <form className="retrieval-form" onSubmit={search}>
                <label htmlFor="semantic-search-query">Question or search phrase</label>
                <textarea
                    id="semantic-search-query"
                    value={query}
                    maxLength={2_000}
                    rows={3}
                    placeholder="What do the documents say about…?"
                    onChange={(event) => setQuery(event.target.value)}
                />
                <div className="retrieval-form-footer">
                    <p className="character-count">{query.length.toLocaleString()} / 2,000</p>
                    <button className="primary-button" type="submit" disabled={isSearching}>
                        {isSearching ? 'Searching…' : 'Search'}
                    </button>
                </div>
            </form>

            {isSearching ? (
                <div className="retrieval-progress" aria-live="polite">
                    <div className="spinner small-spinner" aria-hidden="true" />
                    <span>Searching workspace documents…</span>
                </div>
            ) : response ? (
                <div className="retrieval-results" aria-live="polite">
                    <div className="retrieval-results-heading">
                        <h3>Ranked results</h3>
                        <span>
                            {response.results.length} of up to {response.topK} chunks · smaller cosine distance is closer
                        </span>
                    </div>

                    {response.results.length === 0 ? (
                        <div className="retrieval-empty-state">
                            No eligible matching chunks were found in this workspace.
                        </div>
                    ) : (
                        <ol className="retrieval-result-list">
                            {response.results.map((result, index) => (
                                <li className="retrieval-result-card" key={result.chunkId}>
                                    <div className="retrieval-result-heading">
                                        <div>
                                            <span className="retrieval-rank">#{index + 1}</span>
                                            <h3>{result.documentName}</h3>
                                        </div>
                                        <span className="retrieval-distance">
                                            Cosine distance {result.cosineDistance.toFixed(4)}
                                        </span>
                                    </div>
                                    <div className="retrieval-result-meta">
                                        <span>Chunk {result.chunkIndex + 1}</span>
                                        <span>{formatPageRange(result.pageStart, result.pageEnd)}</span>
                                    </div>
                                    {result.heading && <h4>{result.heading}</h4>}
                                    <p className="retrieval-result-content">{result.content}</p>
                                </li>
                            ))}
                        </ol>
                    )}
                </div>
            ) : null}
        </section>
    );
}

interface DocumentsSectionProps {
    projectId: string;
    documents: DocumentSummary[];
    onDocumentsChanged: Dispatch<SetStateAction<DocumentSummary[]>>;
}

function DocumentsSection({
    projectId,
    documents,
    onDocumentsChanged,
}: DocumentsSectionProps) {
    const [error, setError] = useState('');
    const [isUploading, setIsUploading] = useState(false);
    const [deletingId, setDeletingId] = useState<string | null>(null);
    const [retryingId, setRetryingId] = useState<string | null>(null);
    const [rebuildingId, setRebuildingId] = useState<string | null>(null);
    const [normalizingId, setNormalizingId] = useState<string | null>(null);
    const [embeddingId, setEmbeddingId] = useState<string | null>(null);
    const [understandingId, setUnderstandingId] = useState<string | null>(null);
    const [understandings, setUnderstandings] = useState<Record<string, DocumentUnderstanding>>({});
    const [understandingLoading, setUnderstandingLoading] = useState<Record<string, boolean>>({});
    const [understandingErrors, setUnderstandingErrors] = useState<Record<string, string>>({});
    const [chunkViewerRefreshKey, setChunkViewerRefreshKey] = useState(0);
    const [textViewerDocument, setTextViewerDocument] = useState<DocumentSummary | null>(null);
    const [chunkViewerDocument, setChunkViewerDocument] = useState<DocumentSummary | null>(null);
    const [confirmation, setConfirmation] = useState<DocumentConfirmation | null>(null);
    const [isDragging, setIsDragging] = useState(false);
    const uploadInputRef = useRef<HTMLInputElement>(null);
    const understandingCacheRef = useRef<Record<string, DocumentUnderstanding>>({});
    const understandingRequestIdsRef = useRef(new Set<string>());
    const showToast = useToast();

    const cacheUnderstanding = useCallback((documentId: string, understanding: DocumentUnderstanding) => {
        const next = { ...understandingCacheRef.current, [documentId]: understanding };
        understandingCacheRef.current = next;
        setUnderstandings(next);
    }, []);

    const discardCachedUnderstanding = useCallback((documentId: string) => {
        if (!understandingCacheRef.current[documentId]) return;
        const next = { ...understandingCacheRef.current };
        delete next[documentId];
        understandingCacheRef.current = next;
        setUnderstandings(next);
    }, []);

    const applyDocumentResponse = useCallback((response: DocumentSummary[]) => {
        let nextCache: Record<string, DocumentUnderstanding> | null = null;

        for (const document of response) {
            const cached = understandingCacheRef.current[document.id];
            const responseStatus = document.understandingStatus ?? 'NotAnalyzed';
            if (cached &&
                !isUnderstandingInProgress(cached.status) &&
                cached.status !== responseStatus) {
                nextCache ??= { ...understandingCacheRef.current };
                delete nextCache[document.id];
            }
        }

        if (nextCache) {
            understandingCacheRef.current = nextCache;
            setUnderstandings(nextCache);
        }

        onDocumentsChanged(response);
    }, [onDocumentsChanged]);

    const loadUnderstanding = useCallback(async (
        documentId: string,
        force = false,
    ): Promise<DocumentUnderstanding | null> => {
        const cached = understandingCacheRef.current[documentId];
        if (!force && cached) return cached;
        if (understandingRequestIdsRef.current.has(documentId)) return cached ?? null;

        understandingRequestIdsRef.current.add(documentId);
        setUnderstandingLoading((current) => ({ ...current, [documentId]: true }));
        setUnderstandingErrors((current) => {
            if (!current[documentId]) return current;
            const next = { ...current };
            delete next[documentId];
            return next;
        });

        try {
            const response = await apiRequest<DocumentUnderstanding>(
                `/api/projects/${projectId}/documents/${documentId}/understanding`,
            );
            cacheUnderstanding(documentId, response);
            onDocumentsChanged((currentDocuments) => currentDocuments.map((document) =>
                document.id === documentId
                    ? { ...document, understandingStatus: response.status }
                    : document));
            return response;
        } catch (requestError) {
            setUnderstandingErrors((current) => ({
                ...current,
                [documentId]: getErrorMessage(requestError),
            }));
            return null;
        } finally {
            understandingRequestIdsRef.current.delete(documentId);
            setUnderstandingLoading((current) => {
                const next = { ...current };
                delete next[documentId];
                return next;
            });
        }
    }, [cacheUnderstanding, onDocumentsChanged, projectId]);

    const shouldPoll = documents.some(
        (document) => document.status === 'Uploaded' ||
            document.status === 'Processing' ||
            isUnderstandingInProgress(document.understandingStatus),
    );

    const visibleDocumentIds = new Set(documents.map((document) => document.id));
    const understandingPollKey = Object.entries(understandings)
        .filter(([documentId, understanding]) =>
            visibleDocumentIds.has(documentId) && isUnderstandingInProgress(understanding.status))
        .map(([documentId]) => documentId)
        .sort()
        .join(',');

    const refreshDocuments = useCallback(async () => {
        const response = await apiRequest<DocumentSummary[]>(
            `/api/projects/${projectId}/documents`,
        );
        applyDocumentResponse(response);
        setError('');
        return response;
    }, [applyDocumentResponse, projectId]);

    useEffect(() => {
        if (!shouldPoll) return;

        let isActive = true;

        const refreshDocuments = async () => {
            try {
                const response = await apiRequest<DocumentSummary[]>(
                    `/api/projects/${projectId}/documents`,
                );

                if (isActive) {
                    applyDocumentResponse(response);
                    setError('');
                }
            } catch (requestError) {
                if (isActive) setError(getErrorMessage(requestError));
            }
        };

        const timer = window.setInterval(() => void refreshDocuments(), 2_500);

        return () => {
            isActive = false;
            window.clearInterval(timer);
        };
    }, [applyDocumentResponse, projectId, shouldPoll]);

    useEffect(() => {
        if (!understandingPollKey) return;
        const documentIds = understandingPollKey.split(',');
        const timer = window.setInterval(() => {
            for (const documentId of documentIds) {
                void loadUnderstanding(documentId, true);
            }
        }, 2_500);

        return () => window.clearInterval(timer);
    }, [loadUnderstanding, understandingPollKey]);

    const uploadDocument = async (file: File) => {
        setError('');

        const validationError = validateDocumentFile(file);
        if (validationError) {
            setError(validationError);
            return;
        }

        setIsUploading(true);

        try {
            const uploadedDocument = await uploadWorkspaceDocument(projectId, file);

            onDocumentsChanged((currentDocuments) => [
                uploadedDocument,
                ...currentDocuments.filter((document) => document.id !== uploadedDocument.id),
            ]);
            setError('');
            showToast({ message: `${uploadedDocument.originalFileName} uploaded.` });
        } catch (requestError) {
            setError(getErrorMessage(requestError));
        } finally {
            setIsUploading(false);
        }
    };

    const deleteDocument = async (document: DocumentSummary) => {
        setError('');
        setDeletingId(document.id);

        try {
            await apiRequest<void>(
                `/api/projects/${projectId}/documents/${document.id}`,
                { method: 'DELETE' },
            );
            onDocumentsChanged((currentDocuments) =>
                currentDocuments.filter((item) => item.id !== document.id));
            discardCachedUnderstanding(document.id);
            setUnderstandingErrors((current) => {
                const next = { ...current };
                delete next[document.id];
                return next;
            });
            setError('');
            showToast({ message: 'Document deleted.' });
        } catch (requestError) {
            setError(getErrorMessage(requestError));
        } finally {
            setDeletingId(null);
        }
    };

    const retryProcessing = async (document: DocumentSummary) => {
        setError('');
        setRetryingId(document.id);

        try {
            await apiRequest<void>(
                `/api/projects/${projectId}/documents/${document.id}/process`,
                { method: 'POST' },
            );
            await refreshDocuments();
        } catch (requestError) {
            try {
                await refreshDocuments();
            } catch {
                setError(getErrorMessage(requestError));
            }
        } finally {
            setRetryingId(null);
        }
    };

    const rebuildChunks = async (document: DocumentSummary) => {
        setError('');
        setRebuildingId(document.id);

        try {
            const rebuiltDocument = await apiRequest<DocumentDetails>(
                `/api/projects/${projectId}/documents/${document.id}/chunks/rebuild`,
                { method: 'POST' },
            );
            onDocumentsChanged((currentDocuments) => currentDocuments.map((item) =>
                item.id === document.id ? rebuiltDocument : item));
            setChunkViewerRefreshKey((value) => value + 1);
            setError('');
            showToast({ message: 'Document chunks rebuilt.' });
        } catch (requestError) {
            try {
                await refreshDocuments();
            } catch {
                setError(getErrorMessage(requestError));
            }
        } finally {
            setRebuildingId(null);
        }
    };

    const rebuildNormalization = async (document: DocumentSummary) => {
        setError('');
        setNormalizingId(document.id);
        discardCachedUnderstanding(document.id);
        onDocumentsChanged((currentDocuments) => currentDocuments.map((item) =>
            item.id === document.id
                ? { ...item, status: 'Processing', understandingStatus: 'Pending' }
                : item));

        try {
            const rebuiltDocument = await apiRequest<DocumentDetails>(
                `/api/projects/${projectId}/documents/${document.id}/normalization/rebuild`,
                { method: 'POST' },
            );
            onDocumentsChanged((currentDocuments) => currentDocuments.map((item) =>
                item.id === document.id ? rebuiltDocument : item));
            setTextViewerDocument((current) =>
                current?.id === document.id ? rebuiltDocument : current);
            setChunkViewerDocument((current) =>
                current?.id === document.id ? rebuiltDocument : current);
            setChunkViewerRefreshKey((value) => value + 1);
            setError('');
            showToast({ message: 'Document normalization rebuilt.' });
        } catch (requestError) {
            const actionError = getErrorMessage(requestError);
            try {
                await refreshDocuments();
            } catch {
                // The rebuild error remains the most useful action-specific message.
            }
            setError(actionError);
        } finally {
            setNormalizingId(null);
        }
    };

    const rebuildEmbeddings = async (document: DocumentSummary) => {
        setError('');
        setEmbeddingId(document.id);

        try {
            const rebuiltDocument = await apiRequest<DocumentDetails>(
                `/api/projects/${projectId}/documents/${document.id}/embeddings/rebuild`,
                { method: 'POST' },
            );
            onDocumentsChanged((currentDocuments) => currentDocuments.map((item) =>
                item.id === document.id ? rebuiltDocument : item));
            setError('');
            showToast({ message: document.embeddedChunkCount > 0 ? 'Embeddings rebuilt.' : 'Embeddings generated.' });
        } catch (requestError) {
            const actionError = getErrorMessage(requestError);
            try {
                await refreshDocuments();
            } catch {
                // The embedding error remains the most useful action-specific message.
            }
            setError(actionError);
        } finally {
            setEmbeddingId(null);
        }
    };

    const rebuildUnderstanding = async (document: DocumentSummary) => {
        setError('');
        setUnderstandingId(document.id);

        const cached = understandingCacheRef.current[document.id];
        if (cached) {
            cacheUnderstanding(document.id, {
                ...cached,
                status: 'Processing',
                lastError: null,
            });
        }
        onDocumentsChanged((currentDocuments) => currentDocuments.map((item) =>
            item.id === document.id
                ? { ...item, understandingStatus: 'Processing' }
                : item));

        try {
            const response = await apiRequest<DocumentUnderstanding>(
                `/api/projects/${projectId}/documents/${document.id}/understanding/rebuild`,
                { method: 'POST' },
            );
            cacheUnderstanding(document.id, response);
            onDocumentsChanged((currentDocuments) => currentDocuments.map((item) =>
                item.id === document.id
                    ? { ...item, understandingStatus: response.status }
                    : item));
            setUnderstandingErrors((current) => {
                if (!current[document.id]) return current;
                const next = { ...current };
                delete next[document.id];
                return next;
            });
            setError('');
            showToast({
                message: response.status === 'Ready'
                    ? 'Document understanding rebuilt.'
                    : 'Document understanding updated.',
                tone: response.status === 'Ready' ? 'success' : 'info',
            });
        } catch (requestError) {
            const actionError = getErrorMessage(requestError);
            const refreshed = await loadUnderstanding(document.id, true);
            if (refreshed) {
                setError('');
                showToast({
                    message: 'Document understanding could not be rebuilt.',
                    tone: 'info',
                });
            } else {
                setError(actionError);
            }
        } finally {
            setUnderstandingId(null);
        }
    };

    const isDocumentActionBusy = (documentId: string) =>
        deletingId === documentId ||
        retryingId === documentId ||
        rebuildingId === documentId ||
        normalizingId === documentId ||
        embeddingId === documentId ||
        understandingId === documentId ||
        Boolean(understandingLoading[documentId]);

    const runConfirmedAction = async () => {
        if (!confirmation) return;
        const { action, document } = confirmation;
        try {
            if (action === 'delete') await deleteDocument(document);
            if (action === 'chunks') await rebuildChunks(document);
            if (action === 'normalization') await rebuildNormalization(document);
            if (action === 'embeddings') await rebuildEmbeddings(document);
            if (action === 'understanding') await rebuildUnderstanding(document);
        } finally {
            setConfirmation(null);
        }
    };

    return (
        <section className="documents-card" aria-labelledby="documents-heading">
            <div className="documents-heading">
                <div>
                    <p className="eyebrow">Workspace files</p>
                    <h2 id="documents-heading">Documents</h2>
                    <p>PDF and DOCX files up to 20 MB.</p>
                </div>

                <label className={`primary-button upload-button${isUploading ? ' disabled-upload' : ''}`} htmlFor="document-upload">
                    <Icon name="upload" size={16} />
                    {isUploading
                        ? 'Uploading…'
                        : documents.length > 0
                            ? 'Upload another document'
                            : 'Upload document'}
                </label>
                <input
                    id="document-upload"
                    ref={uploadInputRef}
                    className="visually-hidden"
                    type="file"
                    accept={DOCUMENT_FILE_ACCEPT}
                    disabled={isUploading}
                    onChange={(event) => {
                        const input = event.currentTarget;
                        const selectedFile = input.files?.[0];
                        input.value = '';
                        if (selectedFile) void uploadDocument(selectedFile);
                    }}
                />
            </div>

            {error && <div className="alert document-alert" role="alert">{error}</div>}

            {isUploading && (
                <div className="upload-progress" aria-live="polite">
                    <div className="spinner small-spinner" aria-hidden="true" />
                    <span>Uploading…</span>
                </div>
            )}

            <div
                className={`document-dropzone${isDragging ? ' is-dragging' : ''}${documents.length === 0 ? ' document-dropzone-empty' : ''}`}
                role="button"
                tabIndex={isUploading ? -1 : 0}
                aria-label="Upload a PDF or DOCX document"
                onClick={() => !isUploading && uploadInputRef.current?.click()}
                onKeyDown={(event) => {
                    if (!isUploading && (event.key === 'Enter' || event.key === ' ')) {
                        event.preventDefault();
                        uploadInputRef.current?.click();
                    }
                }}
                onDragEnter={(event) => { event.preventDefault(); if (!isUploading) setIsDragging(true); }}
                onDragOver={(event) => event.preventDefault()}
                onDragLeave={(event) => { if (!event.currentTarget.contains(event.relatedTarget as Node)) setIsDragging(false); }}
                onDrop={(event) => {
                    event.preventDefault();
                    setIsDragging(false);
                    const file = event.dataTransfer.files[0];
                    if (file && !isUploading) void uploadDocument(file);
                }}
            >
                <span className="dropzone-icon"><Icon name="upload" size={19} /></span>
                <div>
                    <strong>{documents.length === 0 ? 'Drop your first document here' : 'Drop another document here'}</strong>
                    <span>or choose a PDF or DOCX file · up to 20 MB</span>
                </div>
            </div>

            {documents.length === 0 && !isUploading ? (
                <div className="document-empty-state">
                    <h3>No documents yet</h3>
                    <p>Uploaded files will appear here with their processing status.</p>
                </div>
            ) : (
                <div className="document-list">
                    {documents.map((document) => (
                        <article className="document-card" key={document.id}>
                            <div className="document-file-icon" aria-hidden="true">
                                {getFileExtensionLabel(document.originalFileName)}
                            </div>
                            <div className="document-copy">
                                <h3>{document.originalFileName}</h3>
                                <p>{formatFileSize(document.fileSizeBytes)} · Uploaded {formatDate(document.createdAtUtc)}</p>
                                {document.status === 'Processing' && (
                                    <p className="processing-note">
                                        Extraction started {document.processingStartedAtUtc
                                            ? formatDate(document.processingStartedAtUtc)
                                            : 'recently'}.
                                    </p>
                                )}
                                {document.status === 'Ready' && (
                                    <>
                                        <p className="processing-note ready-note">
                                            {document.extractedSectionCount} {document.extractedSectionCount === 1 ? 'section' : 'sections'}
                                            {' · '}{document.extractedCharacterCount.toLocaleString()} raw characters
                                            {' · '}{document.chunkCount} {document.chunkCount === 1 ? 'chunk' : 'chunks'}
                                        </p>
                                        {document.normalizedAtUtc && (
                                            <p className="processing-note normalization-note">
                                                {document.normalizedCharacterCount.toLocaleString()} normalized
                                                {' · '}{document.normalizationRemovedCharacterCount.toLocaleString()} removed
                                                {' · '}{document.normalizationChangedSectionCount} changed {document.normalizationChangedSectionCount === 1 ? 'section' : 'sections'}
                                                {' · '}Normalized {formatDate(document.normalizedAtUtc)}
                                            </p>
                                        )}
                                        <p className={`processing-note embedding-note${document.embeddingsAreCurrent ? ' ready-note' : ' embedding-warning-note'}`}>
                                            {document.embeddingsAreCurrent
                                                ? 'Embedded'
                                                : document.embeddedChunkCount > 0
                                                    ? 'Needs rebuild'
                                                    : 'Not embedded'}
                                            {' · '}{document.embeddedChunkCount}/{document.chunkCount} embedded chunks
                                            {document.embeddingModel && <>{' · '}{document.embeddingModel}</>}
                                            {document.embeddingDimensions !== null && <>{' · '}{document.embeddingDimensions} dimensions</>}
                                            {document.embeddedAtUtc && <>{' · '}Embedded {formatDate(document.embeddedAtUtc)}</>}
                                        </p>
                                        {(document.embeddingError || document.normalizationError || document.chunkingError) && (
                                            <p className="processing-note failed-note">
                                                {document.embeddingError || document.normalizationError || document.chunkingError}
                                            </p>
                                        )}
                                    </>
                                )}
                                {document.status === 'Failed' && (
                                    <p className="processing-note failed-note">
                                        {document.embeddingError || document.normalizationError || document.chunkingError || document.processingError || 'Document processing failed. You can retry processing.'}
                                    </p>
                                )}
                                {document.status === 'Uploaded' && (
                                    <p className="processing-note">Waiting for text extraction.</p>
                                )}
                            </div>
                            <span className={`status-pill status-${document.status.toLowerCase()}`}>
                                <span className="status-dot" aria-hidden="true" />
                                {document.status}
                            </span>
                            <DocumentIntelligenceSection
                                document={document}
                                understanding={understandings[document.id]}
                                isLoading={Boolean(understandingLoading[document.id])}
                                error={understandingErrors[document.id]}
                                isRebuilding={understandingId === document.id}
                                isBusy={isDocumentActionBusy(document.id)}
                                onLoad={loadUnderstanding}
                                onRebuild={() => setConfirmation({ action: 'understanding', document })}
                            />
                            {document.status !== 'Processing' && (
                                <div className="document-actions">
                                    {document.status === 'Ready' && (
                                        <details
                                            className="document-advanced"
                                            onToggle={(event) => {
                                                if (event.currentTarget.open) {
                                                    void loadUnderstanding(document.id);
                                                }
                                            }}
                                        >
                                            <summary><Icon name="more" size={16} /> Advanced</summary>
                                            <div className="document-advanced-actions">
                                            <DocumentUnderstandingAudit
                                                understanding={understandings[document.id]}
                                                isLoading={Boolean(understandingLoading[document.id])}
                                                error={understandingErrors[document.id]}
                                                fallbackStatus={document.understandingStatus}
                                            />
                                            <button
                                                className="secondary-button compact-button"
                                                type="button"
                                                disabled={isDocumentActionBusy(document.id)}
                                                onClick={() => setTextViewerDocument(document)}
                                            >
                                                View extracted text
                                            </button>
                                            <button
                                                className="secondary-button compact-button"
                                                type="button"
                                                disabled={isDocumentActionBusy(document.id)}
                                                onClick={() => setChunkViewerDocument(document)}
                                            >
                                                View chunks
                                            </button>
                                            <button
                                                className="secondary-button compact-button"
                                                type="button"
                                                disabled={isDocumentActionBusy(document.id)}
                                                onClick={() => setConfirmation({ action: 'normalization', document })}
                                            >
                                                {normalizingId === document.id ? 'Normalizing…' : 'Rebuild normalization'}
                                            </button>
                                            <button
                                                className="secondary-button compact-button"
                                                type="button"
                                                disabled={isDocumentActionBusy(document.id)}
                                                onClick={() => setConfirmation({ action: 'chunks', document })}
                                            >
                                                {rebuildingId === document.id ? 'Rebuilding…' : 'Rebuild chunks'}
                                            </button>
                                            <button
                                                className="secondary-button compact-button"
                                                type="button"
                                                disabled={isDocumentActionBusy(document.id)}
                                                onClick={() => setConfirmation({ action: 'embeddings', document })}
                                            >
                                                {embeddingId === document.id
                                                    ? document.embeddedChunkCount > 0
                                                        ? 'Rebuilding embeddings…'
                                                        : 'Generating embeddings…'
                                                    : document.embeddedChunkCount > 0
                                                        ? 'Rebuild embeddings'
                                                        : 'Generate embeddings'}
                                            </button>
                                            <button
                                                className="secondary-button compact-button"
                                                type="button"
                                                disabled={isDocumentActionBusy(document.id)}
                                                onClick={() => setConfirmation({ action: 'understanding', document })}
                                            >
                                                {understandingId === document.id
                                                    ? 'Rebuilding understanding…'
                                                    : 'Rebuild document understanding'}
                                            </button>
                                            </div>
                                        </details>
                                    )}
                                    {(document.status === 'Failed' || document.status === 'Uploaded') && (
                                        <button
                                            className="secondary-button compact-button"
                                            type="button"
                                            disabled={isDocumentActionBusy(document.id)}
                                            onClick={() => void retryProcessing(document)}
                                        >
                                            {retryingId === document.id
                                                ? 'Queueing…'
                                                : document.status === 'Failed'
                                                    ? 'Retry processing'
                                                    : 'Process document'}
                                        </button>
                                    )}
                                    <button
                                        className="danger-button compact-button document-delete-button"
                                        type="button"
                                        disabled={isDocumentActionBusy(document.id)}
                                        aria-label={`Delete ${document.originalFileName}`}
                                        onClick={() => setConfirmation({ action: 'delete', document })}
                                    >
                                        <Icon name="delete" size={14} /> {deletingId === document.id ? 'Deleting…' : 'Delete'}
                                    </button>
                                </div>
                            )}
                        </article>
                    ))}
                </div>
            )}

            {textViewerDocument && (
                <ExtractedTextViewer
                    projectId={projectId}
                    document={textViewerDocument}
                    onClose={() => setTextViewerDocument(null)}
                />
            )}

            {chunkViewerDocument && (
                <DocumentChunkViewer
                    projectId={projectId}
                    document={chunkViewerDocument}
                    refreshKey={chunkViewerRefreshKey}
                    onClose={() => setChunkViewerDocument(null)}
                />
            )}
            <ConfirmDialog
                open={Boolean(confirmation)}
                title={getDocumentConfirmationTitle(confirmation?.action)}
                description={getDocumentConfirmationDescription(confirmation)}
                confirmLabel={getDocumentConfirmationLabel(confirmation?.action)}
                destructive={confirmation?.action === 'delete'}
                busy={confirmation ? isDocumentActionBusy(confirmation.document.id) : false}
                onCancel={() => setConfirmation(null)}
                onConfirm={() => void runConfirmedAction()}
            />
        </section>
    );
}

interface DocumentIntelligenceSectionProps {
    document: DocumentSummary;
    understanding: DocumentUnderstanding | undefined;
    isLoading: boolean;
    error: string | undefined;
    isRebuilding: boolean;
    isBusy: boolean;
    onLoad: (documentId: string, force?: boolean) => Promise<DocumentUnderstanding | null>;
    onRebuild: () => void;
}

function DocumentIntelligenceSection({
    document,
    understanding,
    isLoading,
    error,
    isRebuilding,
    isBusy,
    onLoad,
    onRebuild,
}: DocumentIntelligenceSectionProps) {
    const [isOpen, setIsOpen] = useState(false);
    const status = document.understandingStatus ?? understanding?.status ?? 'NotAnalyzed';
    const canRebuild = document.normalizedAtUtc !== null && document.status === 'Ready';

    useEffect(() => {
        if (isOpen && !understanding && !isLoading && !error) {
            void onLoad(document.id);
        }
    }, [document.id, error, isLoading, isOpen, onLoad, understanding]);

    return (
        <details
            className="document-intelligence"
            onToggle={(event) => setIsOpen(event.currentTarget.open)}
        >
            <summary>
                <span className="document-intelligence-heading">
                    <Icon name="document" size={16} />
                    Document intelligence
                </span>
                <UnderstandingStatusBadge status={status} />
                <Icon name="chevron-down" size={15} />
            </summary>
            <div className="document-intelligence-body">
                {isLoading && !understanding ? (
                    <div className="document-intelligence-state" role="status">
                        <span className="spinner intelligence-spinner" aria-hidden="true" />
                        Loading document intelligence…
                    </div>
                ) : error && !understanding ? (
                    <div className="document-intelligence-state intelligence-error" role="alert">
                        <span>{error}</span>
                        <button
                            className="secondary-button compact-button"
                            type="button"
                            disabled={isBusy}
                            onClick={() => void onLoad(document.id, true)}
                        >
                            Try again
                        </button>
                    </div>
                ) : status === 'NotAnalyzed' ? (
                    <div className="document-intelligence-state">
                        <span>This document has not been analyzed yet.</span>
                        {canRebuild && (
                            <button
                                className="secondary-button compact-button"
                                type="button"
                                disabled={isBusy}
                                onClick={onRebuild}
                            >
                                Analyze document
                            </button>
                        )}
                    </div>
                ) : status === 'Pending' ? (
                    <div className="document-intelligence-state" role="status">
                        <span className="spinner intelligence-spinner" aria-hidden="true" />
                        Document understanding is queued.
                    </div>
                ) : status === 'Processing' ? (
                    <div className="document-intelligence-state" role="status">
                        <span className="spinner intelligence-spinner" aria-hidden="true" />
                        Detecting document type, language, and metadata…
                    </div>
                ) : status === 'Failed' ? (
                    <div className="document-intelligence-state intelligence-error" role="alert">
                        <span>{understanding?.lastError || 'Document understanding could not be completed.'}</span>
                        {canRebuild && (
                            <button
                                className="secondary-button compact-button"
                                type="button"
                                disabled={isBusy}
                                onClick={onRebuild}
                            >
                                {isRebuilding ? 'Retrying…' : 'Retry analysis'}
                            </button>
                        )}
                    </div>
                ) : status === 'Skipped' ? (
                    <div className="document-intelligence-state">
                        {understanding?.lastError || 'There was not enough usable normalized text to analyze this document.'}
                    </div>
                ) : understanding ? (
                    <div className="document-intelligence-ready">
                        <div className="document-intelligence-facts">
                            <div className="document-intelligence-fact">
                                <span>Type</span>
                                <strong>
                                    {formatUnderstandingValue(understanding.documentType ?? 'Unknown')}
                                    <ConfidenceText value={understanding.documentTypeConfidence} />
                                </strong>
                                {understanding.documentSubtype && <small>{understanding.documentSubtype}</small>}
                            </div>
                            <div className="document-intelligence-fact">
                                <span>Language</span>
                                <strong>
                                    {formatLanguageName(understanding.primaryLanguageCode)}
                                    <ConfidenceText value={understanding.languageConfidence} />
                                </strong>
                                {understanding.primaryLanguageCode && understanding.primaryLanguageCode !== 'und' && (
                                    <small>{understanding.primaryLanguageCode}</small>
                                )}
                            </div>
                            {understanding.detectedTitle && (
                                <div className="document-intelligence-fact intelligence-fact-wide">
                                    <span>Detected title</span>
                                    <strong>{understanding.detectedTitle}</strong>
                                </div>
                            )}
                            {understanding.subject && (
                                <div className="document-intelligence-fact intelligence-fact-wide">
                                    <span>Subject</span>
                                    <strong>{understanding.subject}</strong>
                                </div>
                            )}
                        </div>

                        <div className="document-metadata-section">
                            <h4>Metadata</h4>
                            {understanding.metadata.length === 0 ? (
                                <p className="document-metadata-empty">No supported metadata was extracted.</p>
                            ) : (
                                <dl className="document-metadata-list">
                                    {understanding.metadata.map((entry, index) => (
                                        <div key={`${entry.sequence}-${entry.kind}-${index}`}>
                                            <dt>
                                                <strong>{formatMetadataLabel(entry.label)}</strong>
                                                <span>
                                                    {formatUnderstandingValue(entry.kind)}
                                                    <ConfidenceText value={entry.confidence} />
                                                </span>
                                            </dt>
                                            <dd>
                                                {entry.normalizedValue ?? entry.value}
                                                {entry.normalizedValue && entry.normalizedValue !== entry.value && (
                                                    <small>As written: {entry.value}</small>
                                                )}
                                            </dd>
                                        </div>
                                    ))}
                                </dl>
                            )}
                        </div>
                        {error && <p className="document-intelligence-refresh-error" role="alert">{error}</p>}
                    </div>
                ) : (
                    <div className="document-intelligence-state" role="status">
                        <span className="spinner intelligence-spinner" aria-hidden="true" />
                        Loading document intelligence…
                    </div>
                )}
            </div>
        </details>
    );
}

function UnderstandingStatusBadge({ status }: { status: DocumentUnderstandingStatus }) {
    return (
        <span className={`status-pill understanding-status status-${getUnderstandingStatusClass(status)}`}>
            <span className="status-dot" aria-hidden="true" />
            {formatUnderstandingStatus(status)}
        </span>
    );
}

function ConfidenceText({ value }: { value: number | null }) {
    const confidence = formatConfidence(value);
    return confidence ? <span className="intelligence-confidence"> · {confidence}</span> : null;
}

interface DocumentUnderstandingAuditProps {
    understanding: DocumentUnderstanding | undefined;
    isLoading: boolean;
    error: string | undefined;
    fallbackStatus: DocumentUnderstandingStatus | null;
}

function DocumentUnderstandingAudit({
    understanding,
    isLoading,
    error,
    fallbackStatus,
}: DocumentUnderstandingAuditProps) {
    if (isLoading && !understanding) {
        return <p className="document-understanding-audit-state">Loading understanding details…</p>;
    }

    if (error && !understanding) {
        return <p className="document-understanding-audit-state audit-error">{error}</p>;
    }

    const status = understanding?.status ?? fallbackStatus ?? 'NotAnalyzed';
    return (
        <div className="document-understanding-audit">
            <p>Understanding audit</p>
            <dl>
                <div><dt>Status</dt><dd>{formatUnderstandingStatus(status)}</dd></div>
                {understanding?.model && <div><dt>Model</dt><dd>{understanding.model}</dd></div>}
                {understanding?.promptVersion && <div><dt>Prompt version</dt><dd>{understanding.promptVersion}</dd></div>}
                {understanding?.analyzedAtUtc && (
                    <div><dt>Analyzed</dt><dd>{formatDate(understanding.analyzedAtUtc)}</dd></div>
                )}
                {understanding?.sourceContentHash && (
                    <div>
                        <dt>Source content hash</dt>
                        <dd><code>{understanding.sourceContentHash}</code></dd>
                    </div>
                )}
            </dl>
        </div>
    );
}

interface ExtractedTextViewerProps {
    projectId: string;
    document: DocumentSummary;
    onClose: () => void;
}

function ExtractedTextViewer({
    projectId,
    document,
    onClose,
}: ExtractedTextViewerProps) {
    const [view, setView] = useState<'raw' | 'normalized'>('raw');
    const [sectionsByView, setSectionsByView] = useState<Partial<Record<'raw' | 'normalized', ExtractedTextSection[]>>>({});
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState('');
    const closeRef = useRef<HTMLButtonElement>(null);

    useEffect(() => {
        const previous = globalThis.document.activeElement as HTMLElement | null;
        closeRef.current?.focus();
        const closeOnEscape = (event: KeyboardEvent) => { if (event.key === 'Escape') onClose(); };
        globalThis.document.addEventListener('keydown', closeOnEscape);
        return () => { globalThis.document.removeEventListener('keydown', closeOnEscape); previous?.focus(); };
    }, [onClose]);

    useEffect(() => {
        const cachedSections = sectionsByView[view];
        if (cachedSections) {
            return;
        }

        let isActive = true;

        const loadText = async () => {
            try {
                const response = await apiRequest<ExtractedTextSection[]>(
                    `/api/projects/${projectId}/documents/${document.id}/text?view=${view}`,
                );
                if (isActive) {
                    setSectionsByView((current) => ({ ...current, [view]: response }));
                }
            } catch (requestError) {
                if (isActive) setError(getErrorMessage(requestError));
            } finally {
                if (isActive) setIsLoading(false);
            }
        };

        void loadText();
        return () => { isActive = false; };
    }, [projectId, document.id, view, sectionsByView]);

    const sections = sectionsByView[view] ?? [];
    const selectView = (nextView: 'raw' | 'normalized') => {
        setIsLoading(!sectionsByView[nextView]);
        setError('');
        setView(nextView);
    };

    return (
        <div className="modal-backdrop" role="presentation" onMouseDown={(event) => {
            if (event.target === event.currentTarget) onClose();
        }}>
            <section
                className="modal-card extracted-text-modal"
                role="dialog"
                aria-modal="true"
                aria-labelledby="extracted-text-title"
            >
                <div className="modal-heading extracted-text-heading">
                    <div>
                        <p className="eyebrow">Extracted content</p>
                        <h2 id="extracted-text-title">{document.originalFileName}</h2>
                    </div>
                    <button ref={closeRef} className="icon-button" type="button" aria-label="Close extracted text" onClick={onClose}><Icon name="close" size={18} /></button>
                </div>

                <div className="text-view-tabs" role="tablist" aria-label="Text representation">
                    <button
                        className={view === 'raw' ? 'active' : ''}
                        type="button"
                        role="tab"
                        aria-selected={view === 'raw'}
                        onClick={() => selectView('raw')}
                    >
                        Raw extracted text
                    </button>
                    <button
                        className={view === 'normalized' ? 'active' : ''}
                        type="button"
                        role="tab"
                        aria-selected={view === 'normalized'}
                        onClick={() => selectView('normalized')}
                    >
                        Normalized text
                    </button>
                </div>

                {isLoading ? (
                    <div className="extracted-text-state" aria-live="polite">
                        <div className="spinner small-spinner" aria-hidden="true" />
                        <span>Loading extracted text…</span>
                    </div>
                ) : error ? (
                    <div className="alert" role="alert">{error}</div>
                ) : sections.length === 0 ? (
                    <div className="extracted-text-state">No extracted text is available.</div>
                ) : (
                    <div className="extracted-section-list">
                        {sections.map((section) => (
                            <article className="extracted-section" key={section.sectionIndex}>
                                <div className="extracted-section-meta">
                                    <span>Section {section.sectionIndex + 1}</span>
                                    {section.pageNumber && <span>Page {section.pageNumber}</span>}
                                    <span>{section.rawCharacterCount.toLocaleString()} raw</span>
                                    {section.normalizedCharacterCount !== null && (
                                        <span>{section.normalizedCharacterCount.toLocaleString()} normalized</span>
                                    )}
                                    <span>{section.removedCharacterCount.toLocaleString()} removed</span>
                                    <span>{section.normalizationChanged ? 'Changed' : 'Unchanged'}</span>
                                </div>
                                {section.sectionTitle && <h3>{section.sectionTitle}</h3>}
                                <p>{section.content}</p>
                            </article>
                        ))}
                    </div>
                )}

                <div className="modal-actions">
                    <button className="secondary-button" type="button" onClick={onClose}>Close</button>
                </div>
            </section>
        </div>
    );
}

interface DocumentChunkViewerProps {
    projectId: string;
    document: DocumentSummary;
    refreshKey: number;
    onClose: () => void;
}

function DocumentChunkViewer({
    projectId,
    document,
    refreshKey,
    onClose,
}: DocumentChunkViewerProps) {
    const [chunks, setChunks] = useState<DocumentChunk[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState('');
    const closeRef = useRef<HTMLButtonElement>(null);

    useEffect(() => {
        const previous = globalThis.document.activeElement as HTMLElement | null;
        closeRef.current?.focus();
        const closeOnEscape = (event: KeyboardEvent) => { if (event.key === 'Escape') onClose(); };
        globalThis.document.addEventListener('keydown', closeOnEscape);
        return () => { globalThis.document.removeEventListener('keydown', closeOnEscape); previous?.focus(); };
    }, [onClose]);

    useEffect(() => {
        let isActive = true;

        const loadChunks = async () => {
            try {
                const response = await apiRequest<DocumentChunk[]>(
                    `/api/projects/${projectId}/documents/${document.id}/chunks`,
                );
                if (isActive) {
                    setChunks(response);
                    setError('');
                }
            } catch (requestError) {
                if (isActive) setError(getErrorMessage(requestError));
            } finally {
                if (isActive) setIsLoading(false);
            }
        };

        void loadChunks();
        return () => { isActive = false; };
    }, [projectId, document.id, refreshKey]);

    return (
        <div className="modal-backdrop" role="presentation" onMouseDown={(event) => {
            if (event.target === event.currentTarget) onClose();
        }}>
            <section
                className="modal-card extracted-text-modal chunk-viewer-modal"
                role="dialog"
                aria-modal="true"
                aria-labelledby="chunk-viewer-title"
            >
                <div className="modal-heading extracted-text-heading">
                    <div>
                        <p className="eyebrow">Retrieval chunks</p>
                        <h2 id="chunk-viewer-title">{document.originalFileName}</h2>
                    </div>
                    <button ref={closeRef} className="icon-button" type="button" aria-label="Close chunks" onClick={onClose}><Icon name="close" size={18} /></button>
                </div>

                {isLoading ? (
                    <div className="extracted-text-state" aria-live="polite">
                        <div className="spinner small-spinner" aria-hidden="true" />
                        <span>Loading chunks…</span>
                    </div>
                ) : error ? (
                    <div className="alert" role="alert">{error}</div>
                ) : chunks.length === 0 ? (
                    <div className="extracted-text-state">No chunks are available.</div>
                ) : (
                    <div className="extracted-section-list chunk-list">
                        {chunks.map((chunk) => (
                            <article className="extracted-section chunk-card" key={chunk.chunkIndex}>
                                <div className="extracted-section-meta chunk-meta">
                                    <span>Chunk {chunk.chunkIndex + 1}</span>
                                    <span>{chunk.tokenCount.toLocaleString()} tokens</span>
                                    <span>{chunk.characterCount.toLocaleString()} characters</span>
                                    <span>{formatChunkPages(chunk)}</span>
                                </div>
                                {chunk.sectionTitle && <h3>{chunk.sectionTitle}</h3>}
                                <p>{chunk.content}</p>
                            </article>
                        ))}
                    </div>
                )}

                <div className="modal-actions">
                    <button className="secondary-button" type="button" onClick={onClose}>Close</button>
                </div>
            </section>
        </div>
    );
}

interface ProjectEditorProps {
    project: ProjectSummary | null;
    onCancel: () => void;
    onSaved: (project: ProjectDetails) => void;
}

function ProjectEditor({ project, onCancel, onSaved }: ProjectEditorProps) {
    const [name, setName] = useState(project?.name ?? '');
    const [description, setDescription] = useState(project?.description ?? '');
    const [error, setError] = useState('');
    const [isSubmitting, setIsSubmitting] = useState(false);

    useEffect(() => {
        const previous = document.activeElement as HTMLElement | null;
        const closeOnEscape = (event: KeyboardEvent) => {
            if (event.key === 'Escape' && !isSubmitting) onCancel();
        };
        document.addEventListener('keydown', closeOnEscape);
        return () => { document.removeEventListener('keydown', closeOnEscape); previous?.focus(); };
    }, [isSubmitting, onCancel]);

    const submit = async (event: FormEvent<HTMLFormElement>) => {
        event.preventDefault();
        setError('');

        const normalizedName = name.trim();
        const normalizedDescription = description.trim();

        if (!normalizedName) {
            setError('Workspace name is required.');
            return;
        }

        if (normalizedName.length > 100) {
            setError('Workspace name cannot exceed 100 characters.');
            return;
        }

        if (normalizedDescription.length > 1_000) {
            setError('Description cannot exceed 1,000 characters.');
            return;
        }

        setIsSubmitting(true);

        try {
            const response = await apiRequest<ProjectDetails>(
                project ? `/api/projects/${project.id}` : '/api/projects',
                {
                    method: project ? 'PUT' : 'POST',
                    body: JSON.stringify({
                        name: normalizedName,
                        description: normalizedDescription || null,
                    }),
                },
            );
            onSaved(response);
        } catch (requestError) {
            setError(getErrorMessage(requestError));
        } finally {
            setIsSubmitting(false);
        }
    };

    return (
        <div className="modal-backdrop" role="presentation" onMouseDown={(event) => {
            if (event.target === event.currentTarget && !isSubmitting) onCancel();
        }}>
            <section className="modal-card" role="dialog" aria-modal="true" aria-labelledby="project-editor-title">
                <div className="modal-heading">
                    <div>
                        <p className="eyebrow">{project ? 'Edit workspace' : 'New workspace'}</p>
                        <h2 id="project-editor-title">{project ? 'Edit workspace' : 'Create workspace'}</h2>
                    </div>
                    <button className="icon-button" type="button" aria-label="Close" onClick={onCancel} disabled={isSubmitting}><Icon name="close" size={18} /></button>
                </div>

                {error && <div className="alert" role="alert">{error}</div>}

                <form onSubmit={submit}>
                    <label htmlFor="project-name">Workspace name</label>
                    <input
                        id="project-name"
                        type="text"
                        value={name}
                        onChange={(event) => setName(event.target.value)}
                        required
                        maxLength={100}
                        autoFocus
                    />

                    <label htmlFor="project-description">Description <span className="optional-label">Optional</span></label>
                    <textarea
                        id="project-description"
                        value={description}
                        onChange={(event) => setDescription(event.target.value)}
                        maxLength={1_000}
                        rows={5}
                    />
                    <p className="character-count">{description.length} / 1,000</p>

                    <div className="modal-actions">
                        <button className="secondary-button" type="button" onClick={onCancel} disabled={isSubmitting}>Cancel</button>
                        <button className="primary-button" type="submit" disabled={isSubmitting}>
                            {isSubmitting ? 'Saving…' : project ? 'Save changes' : 'Create workspace'}
                        </button>
                    </div>
                </form>
            </section>
        </div>
    );
}

interface AuthenticatedLayoutProps extends AuthenticatedPageProps {
    children: ReactNode;
}

function AuthenticatedLayout({
    user,
    onNavigate,
    onSignedOut,
    children,
}: AuthenticatedLayoutProps) {
    const [isLoggingOut, setIsLoggingOut] = useState(false);
    const [logoutError, setLogoutError] = useState('');

    const logout = async () => {
        setLogoutError('');
        setIsLoggingOut(true);

        try {
            await apiRequest<void>('/api/auth/logout', { method: 'POST' });
            onSignedOut();
        } catch (requestError) {
            setLogoutError(getErrorMessage(requestError));
        } finally {
            setIsLoggingOut(false);
        }
    };

    return (
        <div className="app-layout">
            <header className="topbar">
                <button className="brand-button" type="button" onClick={() => onNavigate('/projects')}>
                    <span className="brand-mark small-brand" aria-hidden="true">AI</span>
                    <span>AI Document Assistant</span>
                </button>

                <div className="account-area">
                    <div className="account-copy">
                        <strong>{user.displayName}</strong>
                        <span>{user.email}</span>
                    </div>
                    <button className="secondary-button logout-button" type="button" onClick={() => void logout()} disabled={isLoggingOut}>
                        {isLoggingOut ? 'Signing out…' : <><Icon name="logout" size={15} /> Sign out</>}
                    </button>
                </div>
            </header>

            {logoutError && <div className="alert global-alert" role="alert">{logoutError}</div>}
            {children}
        </div>
    );
}

function formatDate(value: string) {
    return new Intl.DateTimeFormat(undefined, {
        dateStyle: 'medium',
        timeStyle: 'short',
    }).format(new Date(value));
}

function ProjectListSkeleton() {
    return (
        <section className="project-grid project-skeleton-grid" aria-label="Loading workspaces" aria-busy="true">
            {[0, 1, 2].map((item) => (
                <div className="project-card" key={item}>
                    <Skeleton className="project-skeleton-icon" />
                    <Skeleton className="project-skeleton-title" />
                    <Skeleton className="project-skeleton-copy" />
                    <Skeleton className="project-skeleton-copy short" />
                </div>
            ))}
        </section>
    );
}

function ProjectDetailsSkeleton() {
    return (
        <section className="details-card details-skeleton" aria-label="Loading workspace and documents" aria-busy="true">
            <Skeleton className="project-skeleton-icon" />
            <Skeleton className="project-skeleton-title" />
            <Skeleton className="project-skeleton-copy" />
            <div className="document-skeleton-list">
                {[0, 1, 2].map((item) => <Skeleton className="document-skeleton-row" key={item} />)}
            </div>
        </section>
    );
}

type DocumentConfirmation = {
    action: 'delete' | 'chunks' | 'normalization' | 'embeddings' | 'understanding';
    document: DocumentSummary;
};

function getDocumentConfirmationTitle(action?: DocumentConfirmation['action']) {
    if (action === 'delete') return 'Delete document?';
    if (action === 'chunks') return 'Rebuild chunks?';
    if (action === 'normalization') return 'Rebuild normalized text?';
    if (action === 'embeddings') return 'Rebuild embeddings?';
    if (action === 'understanding') return 'Rebuild document understanding?';
    return 'Confirm action';
}

function getDocumentConfirmationLabel(action?: DocumentConfirmation['action']) {
    if (action === 'delete') return 'Delete document';
    if (action === 'chunks') return 'Rebuild chunks';
    if (action === 'normalization') return 'Rebuild normalization';
    if (action === 'embeddings') return 'Continue';
    if (action === 'understanding') return 'Rebuild understanding';
    return 'Continue';
}

function getDocumentConfirmationDescription(confirmation: DocumentConfirmation | null) {
    if (!confirmation) return '';
    const name = <strong>{confirmation.document.originalFileName}</strong>;
    if (confirmation.action === 'delete') {
        return <>This permanently removes {name}, its extracted content, document intelligence, chunks, and embeddings.</>;
    }
    if (confirmation.action === 'chunks') {
        return <>Rebuild chunks for {name} from its stored extracted text?</>;
    }
    if (confirmation.action === 'normalization') {
        return <>Rebuild normalized text and chunks for {name} from the stored raw extraction?</>;
    }
    if (confirmation.action === 'understanding') {
        return <>Re-analyze {name} and replace its current document intelligence metadata? This uses OpenAI API credits.</>;
    }
    return confirmation.document.embeddedChunkCount > 0
        ? <>Replace the current embedding set for {name}? This uses OpenAI API credits.</>
        : <>Generate embeddings for {name}? This uses OpenAI API credits.</>;
}

function isUnderstandingInProgress(status: DocumentUnderstandingStatus | null | undefined) {
    return status === 'Pending' || status === 'Processing';
}

function formatUnderstandingStatus(status: DocumentUnderstandingStatus) {
    return status === 'NotAnalyzed' ? 'Not analyzed' : status;
}

function getUnderstandingStatusClass(status: DocumentUnderstandingStatus) {
    return status === 'NotAnalyzed' ? 'not-analyzed' : status.toLocaleLowerCase();
}

function formatUnderstandingValue(value: string) {
    const separated = value
        .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
        .replace(/[_-]+/g, ' ')
        .trim();
    if (!separated) return 'Unknown';
    const normalized = separated.toLocaleLowerCase();
    return normalized.charAt(0).toLocaleUpperCase() + normalized.slice(1);
}

function formatMetadataLabel(value: string) {
    return formatUnderstandingValue(value || 'Other');
}

function formatConfidence(value: number | null) {
    if (value === null || !Number.isFinite(value)) return null;
    const bounded = Math.min(1, Math.max(0, value));
    return `${Math.round(bounded * 100)}%`;
}

function formatLanguageName(code: string | null) {
    const normalizedCode = code?.trim();
    if (!normalizedCode || normalizedCode.toLocaleLowerCase() === 'und') return 'Unknown';

    try {
        return new Intl.DisplayNames(undefined, { type: 'language' }).of(normalizedCode) ?? normalizedCode;
    } catch {
        return normalizedCode;
    }
}

function formatFileSize(bytes: number) {
    if (bytes < 1_024) return `${bytes} B`;
    if (bytes < 1_024 * 1_024) return `${(bytes / 1_024).toFixed(1)} KB`;
    return `${(bytes / (1_024 * 1_024)).toFixed(1)} MB`;
}

function formatChunkPages(chunk: DocumentChunk) {
    return formatPageRange(chunk.pageStart, chunk.pageEnd);
}

function formatPageRange(pageStart: number | null, pageEnd: number | null) {
    if (pageStart === null && pageEnd === null) return 'Pages unavailable';
    if (pageStart === pageEnd || pageEnd === null) return `Page ${pageStart}`;
    if (pageStart === null) return `Page ${pageEnd}`;
    return `Pages ${pageStart}–${pageEnd}`;
}

function getFileExtensionLabel(fileName: string) {
    const extension = fileName.split('.').pop()?.toUpperCase();
    return extension === 'PDF' || extension === 'DOCX' ? extension : 'DOC';
}

function isGuid(value: string) {
    return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value);
}
