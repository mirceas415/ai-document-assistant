export interface CurrentUser {
    id: string;
    email: string;
    displayName: string;
    createdAtUtc: string;
}

export interface ProjectSummary {
    id: string;
    name: string;
    description: string | null;
    createdAtUtc: string;
    updatedAtUtc: string;
}

export type ProjectDetails = ProjectSummary;

export type DocumentStatus = 'Uploaded' | 'Processing' | 'Ready' | 'Failed';

export interface DocumentSummary {
    id: string;
    originalFileName: string;
    contentType: string;
    fileSizeBytes: number;
    status: DocumentStatus;
    createdAtUtc: string;
    updatedAtUtc: string;
    processingStartedAtUtc: string | null;
    processedAtUtc: string | null;
    extractedSectionCount: number;
    extractedCharacterCount: number;
    processingError: string | null;
    chunkCount: number;
    chunkedAtUtc: string | null;
    chunkingError: string | null;
    normalizedCharacterCount: number;
    normalizationRemovedCharacterCount: number;
    normalizationChangedSectionCount: number;
    normalizedAtUtc: string | null;
    normalizationError: string | null;
    embeddedChunkCount: number;
    embeddingModel: string | null;
    embeddingDimensions: number | null;
    embeddedAtUtc: string | null;
    embeddingError: string | null;
    embeddingsAreCurrent: boolean;
}

export type DocumentDetails = DocumentSummary & {
    projectId: string;
};

export interface ExtractedTextSection {
    sectionIndex: number;
    pageNumber: number | null;
    sectionTitle: string | null;
    content: string;
    rawCharacterCount: number;
    normalizedCharacterCount: number | null;
    removedCharacterCount: number;
    normalizationChanged: boolean;
    normalizedAtUtc: string | null;
}

export interface DocumentChunk {
    chunkIndex: number;
    content: string;
    tokenCount: number;
    characterCount: number;
    pageStart: number | null;
    pageEnd: number | null;
    sectionTitle: string | null;
    sourceSectionStartIndex: number;
    sourceSectionEndIndex: number;
}

export interface SemanticSearchResult {
    documentId: string;
    documentName: string;
    chunkId: string;
    chunkIndex: number;
    content: string;
    pageStart: number | null;
    pageEnd: number | null;
    heading: string | null;
    cosineDistance: number;
}

export interface SemanticSearchResponse {
    topK: number;
    results: SemanticSearchResult[];
}

export interface AskSource {
    sourceId: string;
    documentId: string;
    documentName: string;
    chunkId: string;
    chunkIndex: number;
    pageStart: number | null;
    pageEnd: number | null;
    heading: string | null;
    excerpt: string;
}

export interface AskProjectResponse {
    answer: string;
    sources: AskSource[];
}

export interface ConversationSummary {
    id: string;
    title: string;
    createdAtUtc: string;
    updatedAtUtc: string;
    messageCount: number;
    sourceCount: number;
}

export interface Conversation {
    id: string;
    projectId: string;
    title: string;
    createdAtUtc: string;
    updatedAtUtc: string;
    messages: ConversationMessage[];
}

export type ConversationMessageRole = 'User' | 'Assistant';

export interface ConversationMessage {
    id: string;
    role: ConversationMessageRole;
    content: string;
    createdAtUtc: string;
    sequence: number;
    sources: ConversationMessageSource[];
}

export interface ConversationMessageSource {
    sourceId: string;
    documentId: string | null;
    documentName: string;
    documentChunkId: string | null;
    chunkIndex: number;
    pageStart: number | null;
    pageEnd: number | null;
    heading: string | null;
    excerpt: string;
}

interface ApiError {
    message?: string;
    errors?: Record<string, string[]>;
}

export class ApiRequestError extends Error {
    status: number;

    constructor(status: number, message: string) {
        super(message);
        this.name = 'ApiRequestError';
        this.status = status;
    }
}

export const apiRequest = async <T,>(url: string, options?: RequestInit): Promise<T> => {
    const hasJsonBody = Boolean(options?.body) && !(options?.body instanceof FormData);

    const response = await fetch(url, {
        ...options,
        credentials: 'include',
        headers: {
            ...(hasJsonBody ? { 'Content-Type': 'application/json' } : {}),
            ...options?.headers,
        },
    });

    if (!response.ok) {
        let error: ApiError = {};

        try {
            error = await response.json() as ApiError;
        } catch {
            // Use the status-based fallback when the server did not return JSON.
        }

        const details = error.errors
            ? Object.values(error.errors).flat().join(' ')
            : '';
        const message = [error.message, details].filter(Boolean).join(' ') ||
            'The request could not be completed.';

        throw new ApiRequestError(response.status, message);
    }

    return response.status === 204
        ? undefined as T
        : await response.json() as T;
};

export const getErrorMessage = (error: unknown) => {
    if (!(error instanceof Error)) return 'The request could not be completed.';

    const message = error.message.trim();
    const looksTechnical = message.length > 400 ||
        /(?:System\.|Npgsql|stack trace|\bat [A-Z][\w.]+\()/i.test(message);

    return message && !looksTechnical
        ? message
        : 'The request could not be completed. Please try again.';
};
