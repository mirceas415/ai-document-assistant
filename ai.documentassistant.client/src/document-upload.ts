import { apiRequest } from './api';
import type { DocumentDetails } from './api';

export const DOCUMENT_FILE_ACCEPT = '.pdf,.docx,application/pdf,application/vnd.openxmlformats-officedocument.wordprocessingml.document';
const SUPPORTED_CONTENT_TYPES = new Set([
    'application/pdf',
    'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
]);

export function hasSupportedDocumentDrag(dataTransfer: DataTransfer) {
    if (!Array.from(dataTransfer.types).includes('Files')) return false;
    const fileItems = Array.from(dataTransfer.items).filter((item) => item.kind === 'file');
    return fileItems.length === 0 || fileItems.some((item) =>
        !item.type || SUPPORTED_CONTENT_TYPES.has(item.type.toLowerCase()));
}

export function validateDocumentFile(file: File) {
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

export async function uploadWorkspaceDocument(projectId: string, file: File) {
    const formData = new FormData();
    formData.append('file', file);

    return apiRequest<DocumentDetails>(
        `/api/projects/${projectId}/documents`,
        {
            method: 'POST',
            body: formData,
        },
    );
}
