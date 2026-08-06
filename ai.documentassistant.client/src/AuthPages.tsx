import { useState } from 'react';
import type { FormEvent, ReactNode } from 'react';
import { apiRequest, getErrorMessage } from './api';
import type { CurrentUser } from './api';

interface AuthPageProps {
    sessionError?: string;
    onAuthenticated: (user: CurrentUser) => void;
    onNavigate: (path: string) => void;
}

export function LoadingPage() {
    return (
        <main className="shell" aria-live="polite">
            <section className="card loading-card">
                <div className="spinner" aria-hidden="true" />
                <p>Checking your session…</p>
            </section>
        </main>
    );
}

export function LoginPage({ sessionError, onAuthenticated, onNavigate }: AuthPageProps) {
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [rememberMe, setRememberMe] = useState(false);
    const [error, setError] = useState('');
    const [isSubmitting, setIsSubmitting] = useState(false);

    const submit = async (event: FormEvent<HTMLFormElement>) => {
        event.preventDefault();
        setError('');
        setIsSubmitting(true);

        try {
            const user = await apiRequest<CurrentUser>('/api/auth/login', {
                method: 'POST',
                body: JSON.stringify({ email, password, rememberMe }),
            });
            onAuthenticated(user);
        } catch (requestError) {
            setError(getErrorMessage(requestError));
        } finally {
            setIsSubmitting(false);
        }
    };

    return (
        <AuthCard
            title="Welcome back"
            subtitle="Sign in to continue to your projects."
            footer={<>New here?{' '}<button className="text-button" type="button" onClick={() => onNavigate('/register')}>Create an account</button></>}
        >
            {sessionError && <div className="alert" role="alert">{sessionError}</div>}
            {error && <div className="alert" role="alert">{error}</div>}

            <form onSubmit={submit}>
                <label htmlFor="login-email">Email</label>
                <input
                    id="login-email"
                    type="email"
                    autoComplete="email"
                    value={email}
                    onChange={(event) => setEmail(event.target.value)}
                    required
                    maxLength={320}
                />

                <label htmlFor="login-password">Password</label>
                <input
                    id="login-password"
                    type="password"
                    autoComplete="current-password"
                    value={password}
                    onChange={(event) => setPassword(event.target.value)}
                    required
                    maxLength={128}
                />

                <label className="checkbox-row">
                    <input
                        type="checkbox"
                        checked={rememberMe}
                        onChange={(event) => setRememberMe(event.target.checked)}
                    />
                    <span>Keep me signed in</span>
                </label>

                <button className="primary-button full-width" type="submit" disabled={isSubmitting}>
                    {isSubmitting ? 'Signing in…' : 'Sign in'}
                </button>
            </form>
        </AuthCard>
    );
}

export function RegistrationPage({ sessionError, onAuthenticated, onNavigate }: AuthPageProps) {
    const [displayName, setDisplayName] = useState('');
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [error, setError] = useState('');
    const [isSubmitting, setIsSubmitting] = useState(false);

    const submit = async (event: FormEvent<HTMLFormElement>) => {
        event.preventDefault();
        setError('');
        setIsSubmitting(true);

        try {
            const user = await apiRequest<CurrentUser>('/api/auth/register', {
                method: 'POST',
                body: JSON.stringify({ email, password, displayName }),
            });
            onAuthenticated(user);
        } catch (requestError) {
            setError(getErrorMessage(requestError));
        } finally {
            setIsSubmitting(false);
        }
    };

    return (
        <AuthCard
            title="Create your account"
            subtitle="Start with a secure workspace for your projects."
            footer={<>Already have an account?{' '}<button className="text-button" type="button" onClick={() => onNavigate('/login')}>Sign in</button></>}
        >
            {sessionError && <div className="alert" role="alert">{sessionError}</div>}
            {error && <div className="alert" role="alert">{error}</div>}

            <form onSubmit={submit}>
                <label htmlFor="register-name">Display name</label>
                <input
                    id="register-name"
                    type="text"
                    autoComplete="name"
                    value={displayName}
                    onChange={(event) => setDisplayName(event.target.value)}
                    required
                    maxLength={100}
                />

                <label htmlFor="register-email">Email</label>
                <input
                    id="register-email"
                    type="email"
                    autoComplete="email"
                    value={email}
                    onChange={(event) => setEmail(event.target.value)}
                    required
                    maxLength={320}
                />

                <label htmlFor="register-password">Password</label>
                <input
                    id="register-password"
                    type="password"
                    autoComplete="new-password"
                    value={password}
                    onChange={(event) => setPassword(event.target.value)}
                    required
                    minLength={8}
                    maxLength={128}
                    aria-describedby="password-help"
                />
                <p id="password-help" className="field-help">
                    At least 8 characters with uppercase, lowercase, and a number.
                </p>

                <button className="primary-button full-width" type="submit" disabled={isSubmitting}>
                    {isSubmitting ? 'Creating account…' : 'Create account'}
                </button>
            </form>
        </AuthCard>
    );
}

interface AuthCardProps {
    title: string;
    subtitle: string;
    footer: ReactNode;
    children: ReactNode;
}

function AuthCard({ title, subtitle, footer, children }: AuthCardProps) {
    return (
        <main className="shell">
            <section className="card auth-card">
                <div className="brand-mark" aria-hidden="true">AI</div>
                <p className="eyebrow">AI Document Assistant</p>
                <h1>{title}</h1>
                <p className="subtitle">{subtitle}</p>
                {children}
                <p className="switch-view">{footer}</p>
            </section>
        </main>
    );
}
