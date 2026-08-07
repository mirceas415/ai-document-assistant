import { createContext, useContext } from 'react';

export type ToastTone = 'success' | 'info';

export interface ToastOptions {
    message: string;
    tone?: ToastTone;
}

export const ToastContext = createContext<((options: ToastOptions) => void) | null>(null);

export function useToast() {
    const showToast = useContext(ToastContext);
    if (!showToast) throw new Error('useToast must be used inside ToastProvider.');
    return showToast;
}
