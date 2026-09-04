import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { DragEvent, FormEvent, KeyboardEvent, ReactNode } from 'react';
import {
    ApiRequestError,
    apiRequest,
    getErrorMessage,
} from './api';
import type {
    Conversation,
    ConversationMessage,
    ConversationMessageSource,
    ConversationSummary,
    CurrentUser,
    DocumentSummary,
    ProjectDetails,
    SemanticSearchResponse,
} from './api';
import { ConfirmDialog, Icon, Skeleton } from './Ui';
import { useToast } from './toast-context';
import {
    DOCUMENT_FILE_ACCEPT,
    hasSupportedDocumentDrag,
    uploadWorkspaceDocument,
    validateDocumentFile,
} from './document-upload';
import './ChatWorkspace.css';

type Navigate = (path: string, replace?: boolean) => void;

type RecentUploadStatus = 'Uploading' | DocumentSummary['status'];

interface RecentUpload {
    key: string;
    documentId?: string;
    fileName: string;
    status: RecentUploadStatus;
    error?: string;
}

function reconcileRecentUploads(uploads: RecentUpload[], documents: DocumentSummary[]) {
    return uploads.map((upload) => {
        if (!upload.documentId) return upload;
        const document = documents.find((item) => item.id === upload.documentId);
        if (!document || document.status === upload.status) return upload;
        return {
            ...upload,
            status: document.status,
            error: document.status === 'Failed' ? 'Document processing failed.' : undefined,
        };
    });
}

interface ProjectChatWorkspaceProps {
    user: CurrentUser;
    projectId: string;
    conversationId?: string;
    onNavigate: Navigate;
    onSignedOut: () => void;
}

export function ProjectChatWorkspace({
    user,
    projectId,
    conversationId,
    onNavigate,
    onSignedOut,
}: ProjectChatWorkspaceProps) {
    const [project, setProject] = useState<ProjectDetails | null>(null);
    const [documents, setDocuments] = useState<DocumentSummary[]>([]);
    const [conversations, setConversations] = useState<ConversationSummary[]>([]);
    const [conversation, setConversation] = useState<Conversation | null>(null);
    const [isLoading, setIsLoading] = useState(true);
    const [isCreating, setIsCreating] = useState(false);
    const [isAsking, setIsAsking] = useState(false);
    const [error, setError] = useState('');
    const [question, setQuestion] = useState('');
    const [failedQuestion, setFailedQuestion] = useState('');
    const [failedMessageId, setFailedMessageId] = useState<string | null>(null);
    const [sourceToView, setSourceToView] = useState<ConversationMessageSource | null>(null);
    const [confirmDelete, setConfirmDelete] = useState(false);
    const [isDeleting, setIsDeleting] = useState(false);
    const [recentUploads, setRecentUploads] = useState<RecentUpload[]>([]);
    const [uploadError, setUploadError] = useState('');
    const [isDraggingFiles, setIsDraggingFiles] = useState(false);
    const composerRef = useRef<HTMLTextAreaElement>(null);
    const composerDraftRef = useRef('');
    const isAskingRef = useRef(false);
    const uploadInputRef = useRef<HTMLInputElement>(null);
    const messagesEndRef = useRef<HTMLDivElement>(null);
    const shouldFollowMessagesRef = useRef(true);
    const dragDepthRef = useRef(0);
    const uploadingFileKeysRef = useRef(new Set<string>());
    const showToast = useToast();
    const readyDocumentCount = useMemo(
        () => documents.filter((document) => document.status === 'Ready').length,
        [documents],
    );
    const processingUploads = useMemo(
        () => recentUploads.filter((upload) => upload.status === 'Uploading' || upload.status === 'Uploaded' || upload.status === 'Processing'),
        [recentUploads],
    );
    const shouldPollDocuments = documents.some(
        (document) => document.status === 'Uploaded' || document.status === 'Processing',
    );

    const loadConversations = useCallback(async () => {
        const response = await apiRequest<ConversationSummary[]>(
            `/api/projects/${projectId}/conversations`,
        );
        setConversations(response);
        return response;
    }, [projectId]);

    const loadConversation = useCallback(async (id: string) => {
        const response = await apiRequest<Conversation>(
            `/api/projects/${projectId}/conversations/${id}`,
        );
        setConversation(response);
        return response;
    }, [projectId]);

    useEffect(() => {
        let active = true;

        const load = async () => {
            try {
                const [projectResponse, documentsResponse, conversationResponse] =
                    await Promise.all([
                        apiRequest<ProjectDetails>(`/api/projects/${projectId}`),
                        apiRequest<DocumentSummary[]>(`/api/projects/${projectId}/documents`),
                        apiRequest<ConversationSummary[]>(`/api/projects/${projectId}/conversations`),
                    ]);
                if (!active) return;
                setError('');
                setProject(projectResponse);
                setDocuments(documentsResponse);
                setConversations(conversationResponse);

                if (conversationId) {
                    const detail = await apiRequest<Conversation>(
                        `/api/projects/${projectId}/conversations/${conversationId}`,
                    );
                    if (active) setConversation(detail);
                } else if (conversationResponse.length > 0) {
                    const detail = await apiRequest<Conversation>(
                        `/api/projects/${projectId}/conversations/${conversationResponse[0].id}`,
                    );
                    if (active) setConversation(detail);
                } else {
                    setConversation(null);
                }
            } catch (requestError) {
                if (!active) return;
                setError(requestError instanceof ApiRequestError && requestError.status === 404
                    ? 'This workspace or conversation is not available to your account.'
                    : getErrorMessage(requestError));
            } finally {
                if (active) setIsLoading(false);
            }
        };

        void load();
        return () => { active = false; };
    }, [projectId, conversationId]);

    useEffect(() => {
        if (!shouldPollDocuments) return;

        let active = true;
        const refreshDocuments = async () => {
            try {
                const response = await apiRequest<DocumentSummary[]>(
                    `/api/projects/${projectId}/documents`,
                );
                if (active) {
                    setDocuments(response);
                    setRecentUploads((current) => reconcileRecentUploads(current, response));
                    setUploadError('');
                }
            } catch (requestError) {
                if (active) setUploadError(getErrorMessage(requestError));
            }
        };
        const timer = window.setInterval(() => void refreshDocuments(), 2_500);
        return () => {
            active = false;
            window.clearInterval(timer);
        };
    }, [projectId, shouldPollDocuments]);

    useEffect(() => {
        if (conversation && !isAsking) composerRef.current?.focus();
    }, [conversation, isAsking]);

    useEffect(() => {
        if (shouldFollowMessagesRef.current) {
            messagesEndRef.current?.scrollIntoView({ behavior: isAsking ? 'smooth' : 'auto' });
        }
    }, [conversation?.messages.length, isAsking]);

    useEffect(() => {
        const textarea = composerRef.current;
        if (!textarea) return;
        textarea.style.height = 'auto';
        textarea.style.height = `${Math.min(textarea.scrollHeight, 144)}px`;
    }, [question]);

    const createConversation = async () => {
        setError('');
        setIsCreating(true);
        try {
            const created = await apiRequest<Conversation>(
                `/api/projects/${projectId}/conversations`,
                { method: 'POST' },
            );
            setConversation(created);
            composerDraftRef.current = '';
            setQuestion('');
            setFailedQuestion('');
            setFailedMessageId(null);
            await loadConversations();
            onNavigate(`/projects/${projectId}/chats/${created.id}`);
        } catch (requestError) {
            setError(getErrorMessage(requestError));
        } finally {
            setIsCreating(false);
        }
    };

    const askQuestion = async (event?: FormEvent) => {
        event?.preventDefault();
        if (!conversation || isAskingRef.current) return;

        const submittedText = question.trim();
        if (!submittedText) {
            setError('Enter a question about documents in this workspace.');
            return;
        }
        if (submittedText.length > 2_000) {
            setError('The question cannot exceed 2,000 characters.');
            return;
        }

        setError('');
        setFailedQuestion('');
        const retryMessageId = failedQuestion === submittedText
            ? failedMessageId
            : null;
        setFailedMessageId(null);
        composerDraftRef.current = '';
        setQuestion('');
        isAskingRef.current = true;
        setIsAsking(true);
        window.requestAnimationFrame(() => {
            const textarea = composerRef.current;
            if (!textarea) return;
            textarea.style.height = 'auto';
            textarea.focus();
        });
        shouldFollowMessagesRef.current = true;
        const optimisticMessage: ConversationMessage = {
            id: `pending-${Date.now()}`,
            role: 'User',
            content: submittedText,
            createdAtUtc: new Date().toISOString(),
            sequence: (conversation.messages.at(-1)?.sequence ?? 0) + 1,
            sources: [],
        };
        setConversation((current) => current
            ? { ...current, messages: [...current.messages, optimisticMessage] }
            : current);

        try {
            await apiRequest<ConversationMessage>(
                `/api/projects/${projectId}/conversations/${conversation.id}/messages`,
                {
                    method: 'POST',
                    body: JSON.stringify({
                        question: submittedText,
                        ...(retryMessageId ? { retryMessageId } : {}),
                    }),
                },
            );
            await Promise.all([
                loadConversation(conversation.id),
                loadConversations(),
            ]);
        } catch (requestError) {
            const canRestoreSubmittedText = !composerDraftRef.current.trim();
            if (canRestoreSubmittedText) {
                composerDraftRef.current = submittedText;
                setQuestion(submittedText);
                setFailedQuestion(submittedText);
            }
            setError(getErrorMessage(requestError));
            try {
                const [reloaded] = await Promise.all([
                    loadConversation(conversation.id),
                    loadConversations(),
                ]);
                const lastMessage = reloaded.messages.at(-1);
                if (!composerDraftRef.current.trim() || composerDraftRef.current.trim() === submittedText) {
                    setFailedMessageId(
                        lastMessage?.role === 'User' && lastMessage.content === submittedText
                            ? lastMessage.id
                            : null,
                    );
                }
            } catch {
                // Preserve the original safe provider error and local retry text.
            }
        } finally {
            isAskingRef.current = false;
            setIsAsking(false);
        }
    };

    const handleComposerKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
        if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing) {
            event.preventDefault();
            void askQuestion();
        }
    };

    const uploadFiles = async (files: File[]) => {
        for (const file of files) {
            const validationError = validateDocumentFile(file);
            if (validationError) {
                const message = `${file.name}: ${validationError}`;
                setUploadError(message);
                showToast({ message, tone: 'info' });
                continue;
            }

            const key = `${file.name}-${file.size}-${file.lastModified}`;
            if (uploadingFileKeysRef.current.has(key)) continue;

            uploadingFileKeysRef.current.add(key);
            setUploadError('');
            const pendingUpload: RecentUpload = { key, fileName: file.name, status: 'Uploading' };
            setRecentUploads((current) => [
                pendingUpload,
                ...current.filter((upload) => upload.key !== key),
            ].slice(0, 4));

            try {
                const uploadedDocument = await uploadWorkspaceDocument(projectId, file);
                setDocuments((current) => [
                    uploadedDocument,
                    ...current.filter((document) => document.id !== uploadedDocument.id),
                ]);
                setRecentUploads((current) => current.map((upload) => upload.key === key
                    ? {
                        ...upload,
                        documentId: uploadedDocument.id,
                        fileName: uploadedDocument.originalFileName,
                        status: uploadedDocument.status,
                    }
                    : upload));
                showToast({ message: `${uploadedDocument.originalFileName} added to ${project?.name ?? 'this workspace'}.` });
            } catch (requestError) {
                const message = getErrorMessage(requestError);
                setUploadError(`${file.name}: ${message}`);
                setRecentUploads((current) => current.map((upload) => upload.key === key
                    ? { ...upload, status: 'Failed', error: message }
                    : upload));
                showToast({ message: `${file.name} could not be uploaded.`, tone: 'info' });
            } finally {
                uploadingFileKeysRef.current.delete(key);
            }
        }
    };

    const containsFiles = (dataTransfer: DataTransfer) =>
        Array.from(dataTransfer.types).includes('Files');

    const handleDragEnter = (event: DragEvent<HTMLElement>) => {
        if (!containsFiles(event.dataTransfer)) return;
        event.preventDefault();
        if (!hasSupportedDocumentDrag(event.dataTransfer)) return;
        dragDepthRef.current += 1;
        setIsDraggingFiles(true);
    };

    const handleDragOver = (event: DragEvent<HTMLElement>) => {
        if (!containsFiles(event.dataTransfer)) return;
        event.preventDefault();
        event.dataTransfer.dropEffect = 'copy';
    };

    const handleDragLeave = (event: DragEvent<HTMLElement>) => {
        if (!containsFiles(event.dataTransfer)) return;
        dragDepthRef.current = Math.max(0, dragDepthRef.current - 1);
        if (dragDepthRef.current === 0) setIsDraggingFiles(false);
    };

    const handleDrop = (event: DragEvent<HTMLElement>) => {
        if (!containsFiles(event.dataTransfer)) return;
        event.preventDefault();
        dragDepthRef.current = 0;
        setIsDraggingFiles(false);
        const files = Array.from(event.dataTransfer.files);
        if (files.length > 0) void uploadFiles(files);
    };

    const renameConversation = async (title: string) => {
        if (!conversation) return;
        const renamed = await apiRequest<Conversation>(
            `/api/projects/${projectId}/conversations/${conversation.id}`,
            { method: 'PATCH', body: JSON.stringify({ title }) },
        );
        setConversation(renamed);
        await loadConversations();
        showToast({ message: 'Conversation renamed.' });
    };

    const deleteConversation = async () => {
        if (!conversation) return;

        setIsDeleting(true);
        try {
            await apiRequest<void>(
                `/api/projects/${projectId}/conversations/${conversation.id}`,
                { method: 'DELETE' },
            );
            const remaining = await loadConversations();
            setConversation(null);
            setConfirmDelete(false);
            showToast({ message: 'Conversation deleted.' });
            onNavigate(remaining.length > 0
                ? `/projects/${projectId}/chats/${remaining[0].id}`
                : `/projects/${projectId}`);
        } catch (requestError) {
            setError(getErrorMessage(requestError));
            setConfirmDelete(false);
        } finally {
            setIsDeleting(false);
        }
    };

    if (isLoading) {
        return <WorkspaceSkeleton />;
    }

    if (!project) {
        return (
            <div className="chat-fatal-state">
                <h1>Workspace unavailable</h1>
                <p>{error || 'The workspace could not be loaded.'}</p>
                <button className="primary-button" type="button" onClick={() => onNavigate('/projects')}>
                    Return to workspaces
                </button>
            </div>
        );
    }

    return (
        <div className="chat-workspace">
            <WorkspaceSidebar
                user={user}
                project={project}
                documentCount={documents.length}
                isCreating={isCreating}
                onNewChat={() => void createConversation()}
                onNavigate={onNavigate}
                onSignedOut={onSignedOut}
            />
            <ConversationHistory
                projectId={projectId}
                activeId={conversation?.id}
                conversations={conversations}
                onNavigate={onNavigate}
            />
            <main
                className="chat-main"
                onDragEnter={handleDragEnter}
                onDragOver={handleDragOver}
                onDragLeave={handleDragLeave}
                onDrop={handleDrop}
            >
                <ChatHeader
                    conversation={conversation}
                    projectName={project.name}
                    readyDocumentCount={readyDocumentCount}
                    onRename={renameConversation}
                    onDelete={() => setConfirmDelete(true)}
                />

                {error && <div className="chat-error" role="alert">{error}</div>}

                {!conversation ? (
                    <section className="chat-empty-state">
                        <div className="chat-empty-mark" aria-hidden="true">AI</div>
                        <p className="empty-project-name">Current workspace · {project.name}</p>
                        <h1>Ask your documents</h1>
                        <p>
                            {documents.length === 0
                                ? 'Upload a PDF or DOCX to get started.'
                                : 'Start a chat to ask questions grounded in this workspace.'}
                        </p>
                        <button className="primary-button" type="button" onClick={() => void createConversation()} disabled={isCreating}>
                            {isCreating ? 'Creating…' : 'Start a new chat'}
                        </button>
                    </section>
                ) : (
                    <>
                        <section
                            className="message-scroll"
                            aria-live="polite"
                            onScroll={(event) => {
                                const element = event.currentTarget;
                                shouldFollowMessagesRef.current = element.scrollHeight - element.scrollTop - element.clientHeight < 120;
                            }}
                        >
                            {conversation.messages.length === 0 ? (
                                <ConversationEmptyState
                                    projectName={project.name}
                                    documentCount={documents.length}
                                    readyDocumentCount={readyDocumentCount}
                                />
                            ) : conversation.messages.map((message) => (
                                <ChatMessage
                                    key={message.id}
                                    message={message}
                                    onViewSource={setSourceToView}
                                    onCopied={() => showToast({ message: 'Answer copied.' })}
                                />
                            ))}
                            {isAsking && (
                                <div className="assistant-thinking">
                                    <span className="thinking-dot" />
                                    <span className="thinking-dot" />
                                    <span className="thinking-dot" />
                                    <span>Generating answer…</span>
                                </div>
                            )}
                            <div ref={messagesEndRef} />
                        </section>

                        <div className="composer-region">
                            <UploadStatusChips
                                uploads={recentUploads}
                                onDismiss={(key) => setRecentUploads((current) => current.filter((upload) => upload.key !== key))}
                            />
                            {uploadError && <div className="composer-upload-error" role="alert">{uploadError}</div>}
                            {processingUploads.length > 0 && (
                                <p className="composer-processing-note" role="status">
                                    {processingUploads.length === 1
                                        ? `${processingUploads[0].fileName} is still processing and may not be included yet.`
                                        : `${processingUploads.length} recent uploads are still processing and may not be included yet.`}
                                </p>
                            )}
                            {readyDocumentCount === 0 && (
                                <p className="composer-processing-note zero-ready" role="status">
                                    A document needs to finish processing before grounded answers are available.
                                </p>
                            )}
                            {failedQuestion && (
                                <button
                                    className="retry-message-button"
                                    type="button"
                                    disabled={isAsking}
                                    onClick={() => void askQuestion()}
                                >
                                    Retry this question
                                </button>
                            )}
                            <form className="chat-composer" onSubmit={(event) => void askQuestion(event)}>
                                <button
                                    className="composer-attachment"
                                    type="button"
                                    aria-label="Upload document"
                                    title="Upload PDF or DOCX"
                                    onClick={() => uploadInputRef.current?.click()}
                                >
                                    <Icon name="attachment" size={18} />
                                </button>
                                <textarea
                                    ref={composerRef}
                                    value={question}
                                    rows={1}
                                    maxLength={2_000}
                                    placeholder="Ask about documents in this workspace…"
                                    aria-label="Question"
                                    aria-describedby="composer-scope composer-shortcut"
                                    onKeyDown={handleComposerKeyDown}
                                    onChange={(event) => {
                                        composerDraftRef.current = event.target.value;
                                        setQuestion(event.target.value);
                                        if (event.target.value.trim() !== failedQuestion) {
                                            setFailedQuestion('');
                                            setFailedMessageId(null);
                                        }
                                    }}
                                />
                                <button type="submit" aria-label="Send question" disabled={isAsking || !question.trim()}>
                                    {isAsking ? <span className="send-spinner" aria-hidden="true" /> : <Icon name="send" size={17} />}
                                </button>
                            </form>
                            <div className="composer-caption">
                                <span id="composer-scope">Answers use {readyDocumentCount} ready document{readyDocumentCount === 1 ? '' : 's'} from {project.name}</span>
                                <span id="composer-shortcut">Enter to send · Shift+Enter for a new line</span>
                            </div>
                            <details className="retrieval-details">
                                <summary><Icon name="search" size={11} /> Retrieval details</summary>
                                <RetrievalDebug projectId={projectId} />
                            </details>
                        </div>
                    </>
                )}
                <input
                    ref={uploadInputRef}
                    className="visually-hidden"
                    type="file"
                    accept={DOCUMENT_FILE_ACCEPT}
                    aria-label="Upload document"
                    onChange={(event) => {
                        const input = event.currentTarget;
                        const file = input.files?.[0];
                        input.value = '';
                        if (file) void uploadFiles([file]);
                    }}
                />
                {isDraggingFiles && (
                    <div className="chat-drop-overlay" aria-hidden="true">
                        <div>
                            <Icon name="upload" size={24} />
                            <strong>Drop documents to add them to this workspace</strong>
                            <span>PDF or DOCX · up to 20 MB</span>
                        </div>
                    </div>
                )}
            </main>

            {sourceToView && (
                <SourceModal source={sourceToView} onClose={() => setSourceToView(null)} />
            )}
            <ConfirmDialog
                open={confirmDelete}
                title="Delete conversation?"
                description={<>This permanently deletes <strong>{conversation?.title}</strong> and its messages. Workspace documents remain unchanged.</>}
                confirmLabel="Delete conversation"
                busy={isDeleting}
                onCancel={() => setConfirmDelete(false)}
                onConfirm={() => void deleteConversation()}
            />
        </div>
    );
}

function ConversationEmptyState({
    projectName,
    documentCount,
    readyDocumentCount,
}: {
    projectName: string;
    documentCount: number;
    readyDocumentCount: number;
}) {
    return (
        <div className="conversation-empty">
            <div className="chat-empty-mark" aria-hidden="true">AI</div>
            <p className="empty-project-name">Current workspace · {projectName}</p>
            <h2>Ask your documents</h2>
            <p>{documentCount === 0
                ? 'Upload a PDF or DOCX to get started.'
                : readyDocumentCount === 0
                    ? 'Your documents are processing. You can ask once one is ready.'
                    : 'Ask questions and get answers grounded in the documents inside this workspace.'}</p>
        </div>
    );
}

function UploadStatusChips({ uploads, onDismiss }: { uploads: RecentUpload[]; onDismiss: (key: string) => void }) {
    if (uploads.length === 0) return null;

    return (
        <div className="upload-status-chips" aria-live="polite" aria-label="Recent document uploads">
            {uploads.map((upload) => {
                const statusLabel = upload.status === 'Uploaded' ? 'Queued' : upload.status;
                const complete = upload.status === 'Ready' || upload.status === 'Failed';
                return (
                    <span className={`upload-status-chip status-${upload.status.toLowerCase()}`} key={upload.key} title={upload.error}>
                        {!complete && <span className="upload-chip-spinner" aria-hidden="true" />}
                        <span className="upload-chip-copy"><strong>{upload.fileName}</strong> · {statusLabel}</span>
                        {complete && (
                            <button type="button" aria-label={`Dismiss ${upload.fileName} upload status`} onClick={() => onDismiss(upload.key)}>
                                <Icon name="close" size={12} />
                            </button>
                        )}
                    </span>
                );
            })}
        </div>
    );
}

function WorkspaceSkeleton() {
    return (
        <div className="chat-workspace workspace-skeleton" aria-label="Loading workspace" aria-busy="true">
            <aside className="workspace-sidebar">
                <Skeleton className="skeleton-brand" />
                <Skeleton className="skeleton-new-chat" />
                <Skeleton className="skeleton-nav" />
                <Skeleton className="skeleton-nav" />
                <Skeleton className="skeleton-nav" />
            </aside>
            <aside className="conversation-history">
                <div className="history-heading"><strong>Chats</strong></div>
                <div className="history-skeleton-list">
                    {[0, 1, 2, 3, 4].map((item) => <Skeleton className="skeleton-history-row" key={item} />)}
                </div>
            </aside>
            <main className="chat-main">
                <div className="chat-header"><Skeleton className="skeleton-chat-title" /></div>
                <div className="conversation-loading-skeleton">
                    <Skeleton className="skeleton-answer-wide" />
                    <Skeleton className="skeleton-answer" />
                    <Skeleton className="skeleton-answer-short" />
                </div>
            </main>
        </div>
    );
}

interface WorkspaceSidebarProps {
    user: CurrentUser;
    project: ProjectDetails;
    documentCount: number;
    isCreating: boolean;
    onNewChat: () => void;
    onNavigate: Navigate;
    onSignedOut: () => void;
}

function WorkspaceSidebar({
    user,
    project,
    documentCount,
    isCreating,
    onNewChat,
    onNavigate,
    onSignedOut,
}: WorkspaceSidebarProps) {
    const [isLoggingOut, setIsLoggingOut] = useState(false);
    const [error, setError] = useState('');

    const logout = async () => {
        setIsLoggingOut(true);
        setError('');
        try {
            await apiRequest<void>('/api/auth/logout', { method: 'POST' });
            onSignedOut();
        } catch (requestError) {
            setError(getErrorMessage(requestError));
        } finally {
            setIsLoggingOut(false);
        }
    };

    return (
        <aside className="workspace-sidebar">
            <button className="workspace-brand" type="button" onClick={() => onNavigate('/projects')}>
                <span>AI</span>
                <strong>Document Assistant</strong>
            </button>
            <button className="new-chat-button" type="button" onClick={onNewChat} disabled={isCreating}>
                <Icon name="plus" size={17} /> {isCreating ? 'Creating…' : 'New chat'}
            </button>
            <nav className="workspace-nav" aria-label="Workspace navigation">
                <button type="button" onClick={() => onNavigate('/projects')}><Icon name="folder" /> Workspaces</button>
                <button type="button" onClick={() => onNavigate(`/projects/${project.id}/documents`)}><Icon name="document" /> Documents</button>
            </nav>
            <button className="current-project-block" type="button" onClick={() => onNavigate('/projects')} aria-label={`Manage or switch workspace. Current workspace: ${project.name}`}>
                <span>Current workspace</span>
                <strong title={project.name}>{project.name}</strong>
                <small>{documentCount} document{documentCount === 1 ? '' : 's'}</small>
            </button>
            <div className="workspace-account">
                <div className="account-avatar">{initials(user.displayName)}</div>
                <div><strong>{user.displayName}</strong><span>Signed in</span></div>
                <button type="button" onClick={() => void logout()} disabled={isLoggingOut} aria-label="Sign out" title="Sign out">
                    {isLoggingOut ? '…' : <Icon name="logout" size={17} />}
                </button>
            </div>
            {error && <p className="sidebar-error" role="alert">{error}</p>}
        </aside>
    );
}

function ConversationHistory({
    projectId,
    activeId,
    conversations,
    onNavigate,
}: {
    projectId: string;
    activeId?: string;
    conversations: ConversationSummary[];
    onNavigate: Navigate;
}) {
    const [filter, setFilter] = useState('');
    const filtered = useMemo(() => {
        const normalizedFilter = filter.trim().toLocaleLowerCase();
        return normalizedFilter
            ? conversations.filter((item) => item.title.toLocaleLowerCase().includes(normalizedFilter))
            : conversations;
    }, [conversations, filter]);
    const groups = useMemo(() => groupConversations(filtered), [filtered]);

    return (
        <aside className="conversation-history">
            <div className="history-heading"><strong>Chats</strong><span>{conversations.length}</span></div>
            <label className="history-search">
                <Icon name="search" size={15} />
                <input value={filter} aria-label="Search conversations" placeholder="Search chats" onChange={(event) => setFilter(event.target.value)} />
            </label>
            <div className="history-groups">
                {groups.map((group) => (
                    <section key={group.label}>
                        <h2>{group.label}</h2>
                        {group.items.map((item) => (
                            <button
                                className={item.id === activeId ? 'active' : ''}
                                type="button"
                                key={item.id}
                                onClick={() => onNavigate(`/projects/${projectId}/chats/${item.id}`)}
                            >
                                <strong>{item.title}</strong>
                                <span>
                                    {formatHistoryTime(item.updatedAtUtc)}
                                    {item.sourceCount > 0 ? ` · ${item.sourceCount} source${item.sourceCount === 1 ? '' : 's'}` : ''}
                                </span>
                            </button>
                        ))}
                    </section>
                ))}
                {filtered.length === 0 && (
                    <div className="history-empty">
                        <Icon name={filter ? 'search' : 'chat'} size={18} />
                        <strong>{filter ? 'No matching chats' : 'No conversations yet'}</strong>
                        <span>{filter ? 'Try a different title.' : 'Start a new chat to ask your documents.'}</span>
                    </div>
                )}
            </div>
        </aside>
    );
}

function ChatHeader({
    conversation,
    projectName,
    readyDocumentCount,
    onRename,
    onDelete,
}: {
    conversation: Conversation | null;
    projectName: string;
    readyDocumentCount: number;
    onRename: (title: string) => Promise<void>;
    onDelete: () => void;
}) {
    const [editing, setEditing] = useState(false);
    const [title, setTitle] = useState('');
    const [error, setError] = useState('');

    const save = async () => {
        const normalized = title.trim();
        if (!normalized || normalized.length > 80) {
            setError('Use a title between 1 and 80 characters.');
            return;
        }
        try {
            await onRename(normalized);
            setEditing(false);
            setError('');
        } catch (requestError) {
            setError(getErrorMessage(requestError));
        }
    };

    return (
        <header className="chat-header">
            <div>
                {conversation && editing ? (
                    <div className="rename-row">
                        <input
                            value={title}
                            maxLength={80}
                            autoFocus
                            aria-label="Conversation title"
                            onChange={(event) => setTitle(event.target.value)}
                            onKeyDown={(event) => {
                                if (event.key === 'Enter') void save();
                                if (event.key === 'Escape') { setEditing(false); setTitle(conversation.title); }
                            }}
                        />
                        <button type="button" onClick={() => void save()}>Save</button>
                        <button type="button" onClick={() => { setEditing(false); setTitle(conversation.title); }}>Cancel</button>
                    </div>
                ) : <h1>{conversation?.title ?? 'Workspace chat'}</h1>}
                <p>{projectName} · {readyDocumentCount} ready document{readyDocumentCount === 1 ? '' : 's'}</p>
                {error && <span className="rename-error">{error}</span>}
            </div>
            {conversation && (
                <ConversationActionsMenu
                    onRename={() => { setTitle(conversation.title); setEditing(true); }}
                    onDelete={onDelete}
                />
            )}
        </header>
    );
}

function ConversationActionsMenu({ onRename, onDelete }: { onRename: () => void; onDelete: () => void }) {
    const [open, setOpen] = useState(false);
    const wrapperRef = useRef<HTMLDivElement>(null);
    const triggerRef = useRef<HTMLButtonElement>(null);

    useEffect(() => {
        if (!open) return;

        const firstItem = wrapperRef.current?.querySelector<HTMLButtonElement>('[role="menuitem"]');
        firstItem?.focus();
        const close = (event: globalThis.KeyboardEvent | PointerEvent) => {
            if (event instanceof globalThis.KeyboardEvent && event.key === 'Escape') {
                setOpen(false);
                triggerRef.current?.focus();
                return;
            }
            if (event instanceof PointerEvent && !wrapperRef.current?.contains(event.target as Node)) {
                setOpen(false);
            }
        };
        document.addEventListener('keydown', close);
        document.addEventListener('pointerdown', close);
        return () => {
            document.removeEventListener('keydown', close);
            document.removeEventListener('pointerdown', close);
        };
    }, [open]);

    const handleMenuKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
        if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) return;
        event.preventDefault();
        const items = Array.from(event.currentTarget.querySelectorAll<HTMLButtonElement>('[role="menuitem"]'));
        const currentIndex = items.indexOf(document.activeElement as HTMLButtonElement);
        const nextIndex = event.key === 'Home'
            ? 0
            : event.key === 'End'
                ? items.length - 1
                : event.key === 'ArrowDown'
                    ? (currentIndex + 1) % items.length
                    : (currentIndex - 1 + items.length) % items.length;
        items[nextIndex]?.focus();
    };

    return (
        <div className="conversation-actions" ref={wrapperRef}>
            <button
                ref={triggerRef}
                className="conversation-actions-trigger"
                type="button"
                aria-label="Conversation actions"
                aria-haspopup="menu"
                aria-expanded={open}
                onClick={() => setOpen((value) => !value)}
            >
                <Icon name="more" size={19} />
            </button>
            {open && (
                <div className="conversation-actions-menu" role="menu" aria-label="Conversation actions" onKeyDown={handleMenuKeyDown}>
                    <button type="button" role="menuitem" onClick={() => { setOpen(false); onRename(); }}>
                        <Icon name="edit" size={14} /> Rename
                    </button>
                    <button className="delete-chat" type="button" role="menuitem" onClick={() => { triggerRef.current?.focus(); setOpen(false); onDelete(); }}>
                        <Icon name="delete" size={14} /> Delete chat
                    </button>
                </div>
            )}
        </div>
    );
}

function ChatMessage({
    message,
    onViewSource,
    onCopied,
}: {
    message: ConversationMessage;
    onViewSource: (source: ConversationMessageSource) => void;
    onCopied: () => void;
}) {
    if (message.role === 'User') {
        return <article className="chat-message user-message"><div>{message.content}</div></article>;
    }

    const copyAnswer = async () => {
        try {
            await navigator.clipboard.writeText(message.content);
            onCopied();
        } catch {
            // Clipboard access can be unavailable in an insecure browser context.
        }
    };

    return (
        <article className="chat-message assistant-message">
            <div className="assistant-avatar" aria-hidden="true">AI</div>
            <div className="assistant-body">
                <SafeMessageContent content={message.content} sources={message.sources} onViewSource={onViewSource} />
                <div className="message-actions">
                    <button
                        type="button"
                        onClick={() => void copyAnswer()}
                        aria-label="Copy answer"
                    >
                        <Icon name="copy" size={14} /> Copy answer
                    </button>
                </div>
                {message.sources.length > 0 && (
                    <div className="message-sources">
                        <h3>Sources</h3>
                        <div className="source-card-row">
                            {message.sources.map((source) => (
                                <button className="source-card" type="button" key={`${message.id}-${source.sourceId}`} onClick={() => onViewSource(source)}>
                                    <span className="source-label">{source.sourceId}</span>
                                    <strong>{source.documentName}</strong>
                                    <span>{formatPageRange(source.pageStart, source.pageEnd)} · Chunk {source.chunkIndex + 1}</span>
                                    {source.heading && <small>{source.heading}</small>}
                                    <small className="source-excerpt">{source.excerpt}</small>
                                    <em><Icon name="source" size={12} /> View source</em>
                                </button>
                            ))}
                        </div>
                    </div>
                )}
            </div>
        </article>
    );
}

function SafeMessageContent({
    content,
    sources,
    onViewSource,
}: {
    content: string;
    sources: ConversationMessageSource[];
    onViewSource: (source: ConversationMessageSource) => void;
}) {
    const blocks = content.split(/\n{2,}/).filter(Boolean);
    return (
        <div className="safe-message-content">
            {blocks.map((block, blockIndex) => {
                const lines = block.split('\n');
                const unordered = lines.every((line) => /^\s*[-*]\s+/.test(line));
                const ordered = lines.every((line) => /^\s*\d+[.)]\s+/.test(line));
                if (unordered || ordered) {
                    const List = ordered ? 'ol' : 'ul';
                    return (
                        <List key={blockIndex}>
                            {lines.map((line, lineIndex) => (
                                <li key={lineIndex}>{renderInline(line.replace(/^\s*(?:[-*]|\d+[.)])\s+/, ''), sources, onViewSource)}</li>
                            ))}
                        </List>
                    );
                }
                return <p key={blockIndex}>{lines.map((line, index) => <span key={index}>{renderInline(line, sources, onViewSource)}{index < lines.length - 1 && <br />}</span>)}</p>;
            })}
        </div>
    );
}

function renderInline(
    value: string,
    sources: ConversationMessageSource[],
    onViewSource: (source: ConversationMessageSource) => void,
): ReactNode[] {
    const parts = value.split(/(\*\*[^*]+\*\*|\[S\d+\])/g);
    return parts.filter(Boolean).map((part, index) => {
        if (part.startsWith('**') && part.endsWith('**')) return <strong key={index}>{part.slice(2, -2)}</strong>;
        if (/^\[S\d+\]$/.test(part)) {
            const source = sources.find((item) => `[${item.sourceId}]` === part);
            return source ? (
                <button
                    className="inline-citation"
                    type="button"
                    key={index}
                    title={`View ${source.sourceId}: ${source.documentName}`}
                    aria-label={`View source ${source.sourceId}, ${source.documentName}`}
                    onClick={() => onViewSource(source)}
                >
                    {source.sourceId}
                </button>
            ) : <span className="inline-citation inline-citation-static" key={index}>{part.slice(1, -1)}</span>;
        }
        return part;
    });
}

function SourceModal({ source, onClose }: { source: ConversationMessageSource; onClose: () => void }) {
    const closeRef = useRef<HTMLButtonElement>(null);

    useEffect(() => {
        const previous = document.activeElement as HTMLElement | null;
        closeRef.current?.focus();
        const closeOnEscape = (event: globalThis.KeyboardEvent) => {
            if (event.key === 'Escape') onClose();
        };
        document.addEventListener('keydown', closeOnEscape);
        return () => {
            document.removeEventListener('keydown', closeOnEscape);
            previous?.focus();
        };
    }, [onClose]);

    return (
        <div className="source-modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) onClose(); }}>
            <section className="source-modal" role="dialog" aria-modal="true" aria-labelledby="source-modal-title">
                <button ref={closeRef} className="source-modal-close" type="button" onClick={onClose} aria-label="Close source"><Icon name="close" size={17} /></button>
                <span className="source-label">{source.sourceId}</span>
                <h2 id="source-modal-title">{source.documentName}</h2>
                <p className="source-modal-meta">{formatPageRange(source.pageStart, source.pageEnd)} · Chunk {source.chunkIndex + 1}</p>
                {source.heading && <h3>{source.heading}</h3>}
                <div className="source-snapshot-note"><Icon name="source" size={14} /> Authoritative excerpt saved with this answer</div>
                <p className="source-modal-excerpt">{source.excerpt}</p>
                {!source.documentId && <p className="source-unavailable">The original document is no longer available; this bounded citation snapshot remains.</p>}
            </section>
        </div>
    );
}

function RetrievalDebug({ projectId }: { projectId: string }) {
    const [query, setQuery] = useState('');
    const [response, setResponse] = useState<SemanticSearchResponse | null>(null);
    const [error, setError] = useState('');
    const [loading, setLoading] = useState(false);

    const search = async (event: FormEvent) => {
        event.preventDefault();
        const normalized = query.trim();
        if (!normalized) return setError('Enter a search phrase.');
        setLoading(true);
        setError('');
        try {
            setResponse(await apiRequest<SemanticSearchResponse>(
                `/api/projects/${projectId}/search`,
                { method: 'POST', body: JSON.stringify({ query: normalized, topK: 8 }) },
            ));
        } catch (requestError) {
            setError(getErrorMessage(requestError));
        } finally {
            setLoading(false);
        }
    };

    return (
        <div className="retrieval-debug-panel">
            <p>Inspect hybrid and model-reranked results without generating an answer.</p>
            <form onSubmit={(event) => void search(event)}>
                <input value={query} maxLength={2_000} placeholder="Search phrase" onChange={(event) => setQuery(event.target.value)} />
                <button type="submit" disabled={loading}>{loading ? 'Searching…' : 'Search'}</button>
            </form>
            {error && <div className="retrieval-debug-error">{error}</div>}
            {response?.rerankingFallback && (
                <div className="retrieval-debug-notice">Reranking unavailable — hybrid order used.</div>
            )}
            {response && (
                <ol>
                    {response.results.map((result, index) => (
                        <li key={result.chunkId}>
                            <strong>{result.documentName}</strong>
                            <span>
                                Final rank #{index + 1}
                                {` · Hybrid #${result.hybridRank ?? index + 1}`}
                                {result.rerankRank !== null ? ` · Reranked #${result.rerankRank}` : ''}
                                {result.rerankRelevance !== null ? ` · Relevance: ${formatRerankRelevance(result.rerankRelevance)}` : ''}
                                {result.fusedScore !== null ? ` · score ${result.fusedScore.toFixed(6)}` : ''}
                                {result.vectorRank !== null ? ` · Vector #${result.vectorRank}` : ''}
                                {result.lexicalRank !== null ? ` · Lexical #${result.lexicalRank}` : ''}
                                {result.metadataDocumentRank !== null ? ` · Metadata doc #${result.metadataDocumentRank}` : ''}
                            </span>
                            <span>Chunk {result.chunkIndex + 1} · {formatPageRange(result.pageStart, result.pageEnd)}</span>
                            {(result.matchedMetadata?.length ?? 0) > 0 && (
                                <span>Metadata: {result.matchedMetadata?.map((match) => `${match.field}=${match.value}`).join(' · ')}</span>
                            )}
                            <p>{result.content}</p>
                        </li>
                    ))}
                    {response.results.length === 0 && <li>No eligible chunks found.</li>}
                </ol>
            )}
        </div>
    );
}

function formatRerankRelevance(relevance: number) {
    switch (relevance) {
        case 4: return 'Direct';
        case 3: return 'High';
        case 2: return 'Medium';
        case 1: return 'Low';
        default: return 'Irrelevant';
    }
}

function groupConversations(items: ConversationSummary[]) {
    const now = new Date();
    const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
    const yesterday = new Date(today); yesterday.setDate(yesterday.getDate() - 1);
    const week = new Date(today); week.setDate(week.getDate() - 7);
    const buckets = [
        { label: 'Today', items: [] as ConversationSummary[] },
        { label: 'Yesterday', items: [] as ConversationSummary[] },
        { label: 'Previous 7 days', items: [] as ConversationSummary[] },
        { label: 'Older', items: [] as ConversationSummary[] },
    ];
    for (const item of items) {
        const updated = new Date(item.updatedAtUtc);
        const bucket = updated >= today ? buckets[0]
            : updated >= yesterday ? buckets[1]
                : updated >= week ? buckets[2]
                    : buckets[3];
        bucket.items.push(item);
    }
    return buckets.filter((bucket) => bucket.items.length > 0);
}

function formatHistoryTime(value: string) {
    const date = new Date(value);
    const now = new Date();
    return date.toDateString() === now.toDateString()
        ? new Intl.DateTimeFormat(undefined, { hour: 'numeric', minute: '2-digit' }).format(date)
        : new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' }).format(date);
}

function formatPageRange(start: number | null, end: number | null) {
    if (start === null && end === null) return 'Pages unavailable';
    if (start === end || end === null) return `Page ${start}`;
    if (start === null) return `Page ${end}`;
    return `Pages ${start}–${end}`;
}

function initials(name: string) {
    return name.split(/\s+/).filter(Boolean).slice(0, 2).map((part) => part[0]?.toUpperCase()).join('') || 'U';
}
