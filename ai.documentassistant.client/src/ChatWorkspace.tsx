import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { FormEvent, KeyboardEvent, ReactNode } from 'react';
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
import './ChatWorkspace.css';

type Navigate = (path: string, replace?: boolean) => void;

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
    const composerRef = useRef<HTMLTextAreaElement>(null);
    const messagesEndRef = useRef<HTMLDivElement>(null);
    const shouldFollowMessagesRef = useRef(true);
    const showToast = useToast();
    const readyDocumentCount = useMemo(
        () => documents.filter((document) => document.status === 'Ready').length,
        [documents],
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
                } else {
                    setConversation(null);
                }
            } catch (requestError) {
                if (!active) return;
                setError(requestError instanceof ApiRequestError && requestError.status === 404
                    ? 'This project or conversation is not available to your account.'
                    : getErrorMessage(requestError));
            } finally {
                if (active) setIsLoading(false);
            }
        };

        void load();
        return () => { active = false; };
    }, [projectId, conversationId]);

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
        if (!conversation || isAsking) return;

        const normalized = question.trim();
        if (!normalized) {
            setError('Enter a question about documents in this project.');
            return;
        }
        if (normalized.length > 2_000) {
            setError('The question cannot exceed 2,000 characters.');
            return;
        }

        setError('');
        setFailedQuestion('');
        const retryMessageId = failedQuestion === normalized
            ? failedMessageId
            : null;
        setFailedMessageId(null);
        setIsAsking(true);
        shouldFollowMessagesRef.current = true;
        const optimisticMessage: ConversationMessage = {
            id: `pending-${Date.now()}`,
            role: 'User',
            content: normalized,
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
                        question: normalized,
                        ...(retryMessageId ? { retryMessageId } : {}),
                    }),
                },
            );
            setQuestion('');
            await Promise.all([
                loadConversation(conversation.id),
                loadConversations(),
            ]);
        } catch (requestError) {
            setFailedQuestion(normalized);
            setQuestion(normalized);
            setError(getErrorMessage(requestError));
            try {
                const [reloaded] = await Promise.all([
                    loadConversation(conversation.id),
                    loadConversations(),
                ]);
                const lastMessage = reloaded.messages.at(-1);
                setFailedMessageId(
                    lastMessage?.role === 'User' && lastMessage.content === normalized
                        ? lastMessage.id
                        : null,
                );
            } catch {
                // Preserve the original safe provider error and local retry text.
            }
        } finally {
            setIsAsking(false);
        }
    };

    const handleComposerKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
        if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing) {
            event.preventDefault();
            void askQuestion();
        }
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
                <p>{error || 'The project could not be loaded.'}</p>
                <button className="primary-button" type="button" onClick={() => onNavigate('/projects')}>
                    Return to projects
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
            <main className="chat-main">
                <ChatHeader
                    conversation={conversation}
                    projectName={project.name}
                    onRename={renameConversation}
                    onDelete={() => setConfirmDelete(true)}
                />

                {error && <div className="chat-error" role="alert">{error}</div>}

                {!conversation ? (
                    <section className="chat-empty-state">
                        <div className="chat-empty-mark" aria-hidden="true">AI</div>
                        <h1>Ask your project documents</h1>
                        <p>
                            Start a chat with <strong>{project.name}</strong>. Answers search all eligible
                            embedded documents in this project and show their supporting sources.
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
                                    onSelectPrompt={(prompt) => {
                                        setQuestion(prompt);
                                        window.requestAnimationFrame(() => composerRef.current?.focus());
                                    }}
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
                                <textarea
                                    ref={composerRef}
                                    value={question}
                                    rows={1}
                                    maxLength={2_000}
                                    placeholder="Ask about documents in this project…"
                                    disabled={isAsking}
                                    aria-label="Question"
                                    aria-describedby="composer-scope composer-shortcut"
                                    onKeyDown={handleComposerKeyDown}
                                    onChange={(event) => {
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
                                <summary>Advanced: retrieval details</summary>
                                <RetrievalDebug projectId={projectId} />
                            </details>
                        </div>
                    </>
                )}
            </main>

            {sourceToView && (
                <SourceModal source={sourceToView} onClose={() => setSourceToView(null)} />
            )}
            <ConfirmDialog
                open={confirmDelete}
                title="Delete conversation?"
                description={<>This permanently deletes <strong>{conversation?.title}</strong> and its messages. Project documents remain unchanged.</>}
                confirmLabel="Delete conversation"
                busy={isDeleting}
                onCancel={() => setConfirmDelete(false)}
                onConfirm={() => void deleteConversation()}
            />
        </div>
    );
}

const examplePrompts = [
    'Summarize the key points',
    'What are the main requirements?',
    'Find information about…',
    'Explain this in simple terms',
];

function ConversationEmptyState({ projectName, onSelectPrompt }: { projectName: string; onSelectPrompt: (prompt: string) => void }) {
    return (
        <div className="conversation-empty">
            <div className="chat-empty-mark" aria-hidden="true">AI</div>
            <p className="empty-project-name">Current project · {projectName}</p>
            <h2>Ask your documents</h2>
            <p>Ask questions and get answers grounded in the documents inside this project.</p>
            <div className="prompt-suggestions" aria-label="Example prompts">
                {examplePrompts.map((prompt) => (
                    <button type="button" key={prompt} onClick={() => onSelectPrompt(prompt)}>{prompt}</button>
                ))}
            </div>
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
            <nav className="workspace-nav" aria-label="Project navigation">
                <button type="button" onClick={() => onNavigate('/projects')}><Icon name="folder" /> Projects</button>
                <button type="button" onClick={() => onNavigate(`/projects/${project.id}/documents`)}><Icon name="document" /> Documents</button>
                <button className="active" type="button" aria-current="page" onClick={() => onNavigate(`/projects/${project.id}`)}><Icon name="chat" /> Chats</button>
            </nav>
            <div className="current-project-block">
                <span>Current project</span>
                <strong title={project.name}>{project.name}</strong>
                <small>{documentCount} document{documentCount === 1 ? '' : 's'}</small>
            </div>
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
    onRename,
    onDelete,
}: {
    conversation: Conversation | null;
    projectName: string;
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
                ) : <h1>{conversation?.title ?? 'Project chats'}</h1>}
                <p>{projectName} · all eligible project documents</p>
                {error && <span className="rename-error">{error}</span>}
            </div>
            {conversation && (
                <div className="chat-header-actions">
                    <button type="button" onClick={() => { setTitle(conversation.title); setEditing(true); }}><Icon name="edit" size={14} /> Rename</button>
                    <button className="delete-chat" type="button" onClick={onDelete}><Icon name="delete" size={14} /> Delete</button>
                </div>
            )}
        </header>
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
            <p>Inspect ranked pgvector results without generating an answer.</p>
            <form onSubmit={(event) => void search(event)}>
                <input value={query} maxLength={2_000} placeholder="Semantic search phrase" onChange={(event) => setQuery(event.target.value)} />
                <button type="submit" disabled={loading}>{loading ? 'Searching…' : 'Search'}</button>
            </form>
            {error && <div className="retrieval-debug-error">{error}</div>}
            {response && (
                <ol>
                    {response.results.map((result) => (
                        <li key={result.chunkId}>
                            <strong>{result.documentName}</strong>
                            <span>Chunk {result.chunkIndex + 1} · {formatPageRange(result.pageStart, result.pageEnd)} · cosine distance {result.cosineDistance.toFixed(4)}</span>
                            <p>{result.content}</p>
                        </li>
                    ))}
                    {response.results.length === 0 && <li>No eligible chunks found.</li>}
                </ol>
            )}
        </div>
    );
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
