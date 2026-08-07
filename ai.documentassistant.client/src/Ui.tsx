import { useCallback, useEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { ToastContext } from './toast-context';
import type { ToastOptions } from './toast-context';

type IconName =
    | 'arrow-left' | 'chat' | 'check' | 'chevron-down' | 'close' | 'copy'
    | 'delete' | 'document' | 'edit' | 'folder' | 'logout' | 'more'
    | 'plus' | 'search' | 'send' | 'source' | 'upload' | 'user';

const iconPaths: Record<IconName, ReactNode> = {
    'arrow-left': <><path d="M19 12H5" /><path d="m12 19-7-7 7-7" /></>,
    chat: <><path d="M21 15a4 4 0 0 1-4 4H8l-5 3V7a4 4 0 0 1 4-4h10a4 4 0 0 1 4 4Z" /><path d="M8 9h8M8 13h5" /></>,
    check: <path d="m5 12 4 4L19 6" />,
    'chevron-down': <path d="m6 9 6 6 6-6" />,
    close: <path d="m6 6 12 12M18 6 6 18" />,
    copy: <><rect width="13" height="13" x="9" y="9" rx="2" /><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" /></>,
    delete: <><path d="M3 6h18M8 6V4h8v2M19 6l-1 14H6L5 6" /><path d="M10 11v5M14 11v5" /></>,
    document: <><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8Z" /><path d="M14 2v6h6M8 13h8M8 17h6" /></>,
    edit: <><path d="M12 20h9" /><path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L8 18l-4 1 1-4Z" /></>,
    folder: <path d="M3 6a2 2 0 0 1 2-2h5l2 2h7a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2Z" />,
    logout: <><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" /><path d="m16 17 5-5-5-5M21 12H9" /></>,
    more: <><circle cx="5" cy="12" r="1" fill="currentColor" stroke="none" /><circle cx="12" cy="12" r="1" fill="currentColor" stroke="none" /><circle cx="19" cy="12" r="1" fill="currentColor" stroke="none" /></>,
    plus: <path d="M12 5v14M5 12h14" />,
    search: <><circle cx="11" cy="11" r="7" /><path d="m20 20-4-4" /></>,
    send: <><path d="m22 2-7 20-4-9-9-4Z" /><path d="M22 2 11 13" /></>,
    source: <><path d="M16 3h5v5M8 21H3v-5M21 3l-7 7M3 21l7-7" /><path d="M14 3H8a5 5 0 0 0-5 5v4M10 21h6a5 5 0 0 0 5-5v-4" /></>,
    upload: <><path d="M12 16V3M7 8l5-5 5 5" /><path d="M20 15v5H4v-5" /></>,
    user: <><circle cx="12" cy="8" r="4" /><path d="M4 21a8 8 0 0 1 16 0" /></>,
};

export function Icon({ name, size = 18 }: { name: IconName; size?: number }) {
    return (
        <svg className="ui-icon" width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            {iconPaths[name]}
        </svg>
    );
}

interface ConfirmDialogProps {
    open: boolean;
    title: string;
    description: ReactNode;
    confirmLabel: string;
    busy?: boolean;
    destructive?: boolean;
    onCancel: () => void;
    onConfirm: () => void;
}

export function ConfirmDialog({ open, title, description, confirmLabel, busy, destructive = true, onCancel, onConfirm }: ConfirmDialogProps) {
    const cancelRef = useRef<HTMLButtonElement>(null);

    useEffect(() => {
        if (!open) return;
        const previous = document.activeElement as HTMLElement | null;
        cancelRef.current?.focus();
        const closeOnEscape = (event: KeyboardEvent) => {
            if (event.key === 'Escape' && !busy) onCancel();
        };
        document.addEventListener('keydown', closeOnEscape);
        return () => {
            document.removeEventListener('keydown', closeOnEscape);
            previous?.focus();
        };
    }, [busy, onCancel, open]);

    if (!open) return null;
    return (
        <div className="modal-backdrop" role="presentation" onMouseDown={(event) => {
            if (event.target === event.currentTarget && !busy) onCancel();
        }}>
            <section className="modal-card confirm-dialog" role="alertdialog" aria-modal="true" aria-labelledby="confirm-title" aria-describedby="confirm-description">
                <div className="confirm-icon" aria-hidden="true"><Icon name="delete" /></div>
                <h2 id="confirm-title">{title}</h2>
                <div id="confirm-description" className="confirm-description">{description}</div>
                <div className="modal-actions">
                    <button ref={cancelRef} className="secondary-button" type="button" onClick={onCancel} disabled={busy}>Cancel</button>
                    <button className={destructive ? 'danger-button danger-button-solid' : 'primary-button'} type="button" onClick={onConfirm} disabled={busy}>
                        {busy ? 'Working…' : confirmLabel}
                    </button>
                </div>
            </section>
        </div>
    );
}

interface ToastItem extends ToastOptions { id: number }

export function ToastProvider({ children }: { children: ReactNode }) {
    const [toasts, setToasts] = useState<ToastItem[]>([]);

    const showToast = useCallback((options: ToastOptions) => {
        const id = Date.now() + Math.random();
        setToasts((current) => [...current, { ...options, id }]);
        window.setTimeout(() => {
            setToasts((current) => current.filter((toast) => toast.id !== id));
        }, 3_200);
    }, []);

    return (
        <ToastContext.Provider value={showToast}>
            {children}
            <div className="toast-region" aria-live="polite" aria-label="Notifications">
                {toasts.map((toast) => (
                    <div className={`toast toast-${toast.tone ?? 'success'}`} role="status" key={toast.id}>
                        <span className="toast-icon"><Icon name="check" size={15} /></span>
                        <span>{toast.message}</span>
                    </div>
                ))}
            </div>
        </ToastContext.Provider>
    );
}

export function Skeleton({ className = '' }: { className?: string }) {
    return <span className={`skeleton ${className}`.trim()} aria-hidden="true" />;
}
