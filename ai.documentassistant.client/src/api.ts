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
    const response = await fetch(url, {
        ...options,
        credentials: 'include',
        headers: {
            ...(options?.body ? { 'Content-Type': 'application/json' } : {}),
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

export const getErrorMessage = (error: unknown) =>
    error instanceof Error
        ? error.message
        : 'The request could not be completed.';
