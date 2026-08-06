import { useEffect, useState } from 'react';
import type { FormEvent, ReactNode } from 'react';
import {
    ApiRequestError,
    apiRequest,
    getErrorMessage,
} from './api';
import type {
    CurrentUser,
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
                const response = await apiRequest<ProjectDetails>(`/api/projects/${projectId}`);
                if (isActive) setProject(response);
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

                        <section className="next-milestone-card">
                            <div aria-hidden="true">＋</div>
                            <h2>Documents</h2>
                            <p>Documents will be added in the next milestone.</p>
                        </section>
                    </>
                ) : null}
            </main>
        </AuthenticatedLayout>
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

function isGuid(value: string) {
    return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value);
}
