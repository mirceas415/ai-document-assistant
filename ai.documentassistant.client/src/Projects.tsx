import { useEffect, useState } from 'react';
import type { Dispatch, FormEvent, ReactNode, SetStateAction } from 'react';
import {
    ApiRequestError,
    apiRequest,
    getErrorMessage,
} from './api';
import type {
    CurrentUser,
    DocumentDetails,
    DocumentSummary,
    ExtractedTextSection,
    ProjectDetails,
    ProjectSummary,
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
        setProjects((currentProjects) => {
            const remainingProjects = currentProjects.filter((item) => item.id !== project.id);
            return [project, ...remainingProjects]
                .sort((left, right) => Date.parse(right.updatedAtUtc) - Date.parse(left.updatedAtUtc));
        });
        setEditor(null);
    };

    const deleteProject = async (project: ProjectSummary) => {
        const confirmed = window.confirm(
            `Delete “${project.name}”? This action cannot be undone.`,
        );

        if (!confirmed) return;

        setError('');
        setDeletingId(project.id);

        try {
            await apiRequest<void>(`/api/projects/${project.id}`, { method: 'DELETE' });
            setProjects((currentProjects) =>
                currentProjects.filter((item) => item.id !== project.id));
        } catch (requestError) {
            setError(getErrorMessage(requestError));
        } finally {
            setDeletingId(null);
        }
    };

    return (
        <AuthenticatedLayout user={user} onNavigate={onNavigate} onSignedOut={onSignedOut}>
            <main className="page-content">
                <div className="page-heading">
                    <div>
                        <p className="eyebrow">Your workspace</p>
                        <h1>My Projects</h1>
                        <p className="page-description">Create and organize your document workspaces.</p>
                    </div>
                    <button className="primary-button" type="button" onClick={() => setEditor('create')}>
                        <span aria-hidden="true">＋</span> New project
                    </button>
                </div>

                {error && <div className="alert page-alert" role="alert">{error}</div>}

                {isLoading ? (
                    <section className="content-state" aria-live="polite">
                        <div className="spinner" aria-hidden="true" />
                        <p>Loading projects…</p>
                    </section>
                ) : projects.length === 0 ? (
                    <section className="content-state empty-state">
                        <div className="empty-icon" aria-hidden="true">▦</div>
                        <h2>No projects yet</h2>
                        <p>Create your first workspace to get started.</p>
                        <button className="primary-button" type="button" onClick={() => setEditor('create')}>
                            Create a project
                        </button>
                    </section>
                ) : (
                    <section className="project-grid" aria-label="Projects">
                        {projects.map((project) => (
                            <article className="project-card" key={project.id}>
                                <div className="project-card-heading">
                                    <div className="project-icon" aria-hidden="true">P</div>
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
                                        Open
                                    </button>
                                    <button className="secondary-button compact-button" type="button" onClick={() => setEditor(project)}>
                                        Edit
                                    </button>
                                    <button
                                        className="danger-button compact-button"
                                        type="button"
                                        disabled={deletingId === project.id}
                                        onClick={() => void deleteProject(project)}
                                    >
                                        {deletingId === project.id ? 'Deleting…' : 'Delete'}
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
                    ← Back to projects
                </button>

                {isLoading ? (
                    <section className="content-state" aria-live="polite">
                        <div className="spinner" aria-hidden="true" />
                        <p>Loading project…</p>
                    </section>
                ) : notFound ? (
                    <section className="content-state empty-state">
                        <div className="empty-icon" aria-hidden="true">?</div>
                        <h1>Project not found</h1>
                        <p>The project does not exist or is not available to your account.</p>
                        <button className="primary-button" type="button" onClick={() => onNavigate('/projects')}>
                            Return to projects
                        </button>
                    </section>
                ) : error ? (
                    <div className="alert page-alert" role="alert">{error}</div>
                ) : project ? (
                    <>
                        <section className="details-card">
                            <div className="project-icon large-project-icon" aria-hidden="true">P</div>
                            <p className="eyebrow">Project</p>
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
    const [viewerDocument, setViewerDocument] = useState<DocumentSummary | null>(null);

    const shouldPoll = documents.some(
        (document) => document.status === 'Uploaded' || document.status === 'Processing',
    );

    useEffect(() => {
        if (!shouldPoll) return;

        let isActive = true;

        const refreshDocuments = async () => {
            try {
                const response = await apiRequest<DocumentSummary[]>(
                    `/api/projects/${projectId}/documents`,
                );

                if (isActive) onDocumentsChanged(response);
            } catch (requestError) {
                if (isActive) setError(getErrorMessage(requestError));
            }
        };

        const timer = window.setInterval(() => void refreshDocuments(), 2_500);

        return () => {
            isActive = false;
            window.clearInterval(timer);
        };
    }, [projectId, shouldPoll, onDocumentsChanged]);

    const uploadDocument = async (file: File) => {
        setError('');

        const validationError = validateDocumentFile(file);
        if (validationError) {
            setError(validationError);
            return;
        }

        setIsUploading(true);

        try {
            const formData = new FormData();
            formData.append('file', file);

            const uploadedDocument = await apiRequest<DocumentDetails>(
                `/api/projects/${projectId}/documents`,
                {
                    method: 'POST',
                    body: formData,
                },
            );

            onDocumentsChanged((currentDocuments) => [
                uploadedDocument,
                ...currentDocuments.filter((document) => document.id !== uploadedDocument.id),
            ]);
        } catch (requestError) {
            setError(getErrorMessage(requestError));
        } finally {
            setIsUploading(false);
        }
    };

    const deleteDocument = async (document: DocumentSummary) => {
        const confirmed = window.confirm(
            `Delete “${document.originalFileName}”? This removes the uploaded file permanently.`,
        );

        if (!confirmed) return;

        setError('');
        setDeletingId(document.id);

        try {
            await apiRequest<void>(
                `/api/projects/${projectId}/documents/${document.id}`,
                { method: 'DELETE' },
            );
            onDocumentsChanged((currentDocuments) =>
                currentDocuments.filter((item) => item.id !== document.id));
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
            onDocumentsChanged((currentDocuments) => currentDocuments.map((item) =>
                item.id === document.id
                    ? { ...item, status: 'Uploaded', processingError: null }
                    : item));
        } catch (requestError) {
            setError(getErrorMessage(requestError));
        } finally {
            setRetryingId(null);
        }
    };

    return (
        <section className="documents-card" aria-labelledby="documents-heading">
            <div className="documents-heading">
                <div>
                    <p className="eyebrow">Project files</p>
                    <h2 id="documents-heading">Documents</h2>
                    <p>PDF and DOCX files up to 20 MB.</p>
                </div>

                <label className={`primary-button upload-button${isUploading ? ' disabled-upload' : ''}`} htmlFor="document-upload">
                    {isUploading
                        ? 'Uploading…'
                        : documents.length > 0
                            ? 'Upload another document'
                            : 'Upload document'}
                </label>
                <input
                    id="document-upload"
                    className="visually-hidden"
                    type="file"
                    accept=".pdf,.docx,application/pdf,application/vnd.openxmlformats-officedocument.wordprocessingml.document"
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

            {documents.length === 0 && !isUploading ? (
                <div className="document-empty-state">
                    <div className="document-file-icon" aria-hidden="true">DOC</div>
                    <h3>No documents uploaded yet.</h3>
                    <p>Upload a PDF or DOCX file to this project.</p>
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
                                    <p className="processing-note ready-note">
                                        {document.extractedSectionCount} {document.extractedSectionCount === 1 ? 'section' : 'sections'}
                                        {' · '}{document.extractedCharacterCount.toLocaleString()} characters
                                        {document.processedAtUtc && ` · Completed ${formatDate(document.processedAtUtc)}`}
                                    </p>
                                )}
                                {document.status === 'Failed' && (
                                    <p className="processing-note failed-note">
                                        {document.processingError || 'Text extraction failed. You can retry processing.'}
                                    </p>
                                )}
                                {document.status === 'Uploaded' && (
                                    <p className="processing-note">Waiting for text extraction.</p>
                                )}
                            </div>
                            <span className={`status-pill status-${document.status.toLowerCase()}`}>
                                {document.status}
                            </span>
                            <div className="document-actions">
                                {document.status === 'Ready' && (
                                    <button
                                        className="secondary-button compact-button"
                                        type="button"
                                        onClick={() => setViewerDocument(document)}
                                    >
                                        View extracted text
                                    </button>
                                )}
                                {(document.status === 'Failed' || document.status === 'Uploaded') && (
                                    <button
                                        className="secondary-button compact-button"
                                        type="button"
                                        disabled={retryingId === document.id}
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
                                    disabled={deletingId === document.id}
                                    onClick={() => void deleteDocument(document)}
                                >
                                    {deletingId === document.id ? 'Deleting…' : 'Delete'}
                                </button>
                            </div>
                        </article>
                    ))}
                </div>
            )}

            {viewerDocument && (
                <ExtractedTextViewer
                    projectId={projectId}
                    document={viewerDocument}
                    onClose={() => setViewerDocument(null)}
                />
            )}
        </section>
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
    const [sections, setSections] = useState<ExtractedTextSection[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState('');

    useEffect(() => {
        let isActive = true;

        const loadText = async () => {
            try {
                const response = await apiRequest<ExtractedTextSection[]>(
                    `/api/projects/${projectId}/documents/${document.id}/text`,
                );
                if (isActive) setSections(response);
            } catch (requestError) {
                if (isActive) setError(getErrorMessage(requestError));
            } finally {
                if (isActive) setIsLoading(false);
            }
        };

        void loadText();
        return () => { isActive = false; };
    }, [projectId, document.id]);

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
                    <button className="icon-button" type="button" aria-label="Close extracted text" onClick={onClose}>×</button>
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

    const submit = async (event: FormEvent<HTMLFormElement>) => {
        event.preventDefault();
        setError('');

        const normalizedName = name.trim();
        const normalizedDescription = description.trim();

        if (!normalizedName) {
            setError('Project name is required.');
            return;
        }

        if (normalizedName.length > 100) {
            setError('Project name cannot exceed 100 characters.');
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
                        <h2 id="project-editor-title">{project ? 'Edit project' : 'Create project'}</h2>
                    </div>
                    <button className="icon-button" type="button" aria-label="Close" onClick={onCancel} disabled={isSubmitting}>×</button>
                </div>

                {error && <div className="alert" role="alert">{error}</div>}

                <form onSubmit={submit}>
                    <label htmlFor="project-name">Project name</label>
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
                            {isSubmitting ? 'Saving…' : project ? 'Save changes' : 'Create project'}
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
                        {isLoggingOut ? 'Signing out…' : 'Sign out'}
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

function formatFileSize(bytes: number) {
    if (bytes < 1_024) return `${bytes} B`;
    if (bytes < 1_024 * 1_024) return `${(bytes / 1_024).toFixed(1)} KB`;
    return `${(bytes / (1_024 * 1_024)).toFixed(1)} MB`;
}

function getFileExtensionLabel(fileName: string) {
    const extension = fileName.split('.').pop()?.toUpperCase();
    return extension === 'PDF' || extension === 'DOCX' ? extension : 'DOC';
}

function validateDocumentFile(file: File) {
    const maxFileSizeBytes = 20 * 1_024 * 1_024;
    const fileName = file.name.toLowerCase();
    const isPdf = fileName.endsWith('.pdf');
    const isDocx = fileName.endsWith('.docx');

    if (!isPdf && !isDocx) {
        return 'Only PDF and DOCX files are supported.';
    }

    if (file.size === 0) {
        return 'The selected file is empty.';
    }

    if (file.size > maxFileSizeBytes) {
        return 'The file cannot exceed 20 MB.';
    }

    const expectedContentType = isPdf
        ? 'application/pdf'
        : 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';

    if (file.type.toLowerCase() !== expectedContentType) {
        return `The content type does not match the ${isPdf ? 'PDF' : 'DOCX'} file type.`;
    }

    return '';
}

function isGuid(value: string) {
    return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value);
}
