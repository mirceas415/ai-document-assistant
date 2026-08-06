import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import './App.css';

interface CurrentUser {
    id: string;
    email: string;
    displayName: string;
    createdAtUtc: string;
}

interface ApiError {
    message?: string;
    errors?: Record<string, string[]>;
}

type AuthView = 'login' | 'register';

const apiRequest = async <T,>(url: string, options?: RequestInit): Promise<T> => {
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
            // The fallback below keeps non-JSON failures readable.
        }

        const details = error.errors
            ? Object.values(error.errors).flat().join(' ')
            : '';

        throw new Error(
            [error.message, details].filter(Boolean).join(' ') ||
            'The request could not be completed.',
        );
    }

    return response.status === 204
        ? undefined as T
        : await response.json() as T;
};

function App() {
    const [user, setUser] = useState<CurrentUser | null>(null);
    const [view, setView] = useState<AuthView>('login');
    const [isCheckingSession, setIsCheckingSession] = useState(true);
    const [sessionError, setSessionError] = useState('');

    useEffect(() => {
        const checkSession = async () => {
            try {
                const response = await fetch('/api/auth/me', {
                    credentials: 'include',
                });

                if (response.ok) {
                    setUser(await response.json() as CurrentUser);
                } else if (response.status !== 401) {
                    setSessionError('The current session could not be checked.');
                }
            } catch {
                setSessionError('Unable to connect to the backend.');
            } finally {
                setIsCheckingSession(false);
            }
        };

        void checkSession();
    }, []);

    if (isCheckingSession) {
        return (
            <main className="shell" aria-live="polite">
                <section className="card loading-card">
                    <div className="spinner" aria-hidden="true" />
                    <p>Checking your session…</p>
                </section>
            </main>
        );
    }

    if (user) {
        return <Home user={user} onLogout={() => setUser(null)} />;
    }

    return (
        <main className="shell">
            <section className="card auth-card">
                <div className="brand-mark" aria-hidden="true">AI</div>
                <p className="eyebrow">AI Document Assistant</p>
                <h1>{view === 'login' ? 'Welcome back' : 'Create your account'}</h1>
                <p className="subtitle">
                    {view === 'login'
                        ? 'Sign in to continue to your workspace.'
                        : 'Start with a secure account for your documents.'}
                </p>

                {sessionError && <div className="alert" role="alert">{sessionError}</div>}

                {view === 'login'
                    ? <LoginForm onSuccess={setUser} />
                    : <RegistrationForm onSuccess={setUser} />}

                <p className="switch-view">
                    {view === 'login' ? 'New here?' : 'Already have an account?'}{' '}
                    <button
                        className="text-button"
                        type="button"
                        onClick={() => {
                            setSessionError('');
                            setView(view === 'login' ? 'register' : 'login');
                        }}
                    >
                        {view === 'login' ? 'Create an account' : 'Sign in'}
                    </button>
                </p>
            </section>
        </main>
    );
}

interface AuthFormProps {
    onSuccess: (user: CurrentUser) => void;
}

function LoginForm({ onSuccess }: AuthFormProps) {
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
            const currentUser = await apiRequest<CurrentUser>('/api/auth/login', {
                method: 'POST',
                body: JSON.stringify({ email, password, rememberMe }),
            });
            onSuccess(currentUser);
        } catch (requestError) {
            setError(getErrorMessage(requestError));
        } finally {
            setIsSubmitting(false);
        }
    };

    return (
        <form onSubmit={submit}>
            {error && <div className="alert" role="alert">{error}</div>}

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

            <button className="primary-button" type="submit" disabled={isSubmitting}>
                {isSubmitting ? 'Signing in…' : 'Sign in'}
            </button>
        </form>
    );
}

function RegistrationForm({ onSuccess }: AuthFormProps) {
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
            const currentUser = await apiRequest<CurrentUser>('/api/auth/register', {
                method: 'POST',
                body: JSON.stringify({ email, password, displayName }),
            });
            onSuccess(currentUser);
        } catch (requestError) {
            setError(getErrorMessage(requestError));
        } finally {
            setIsSubmitting(false);
        }
    };

    return (
        <form onSubmit={submit}>
            {error && <div className="alert" role="alert">{error}</div>}

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

            <button className="primary-button" type="submit" disabled={isSubmitting}>
                {isSubmitting ? 'Creating account…' : 'Create account'}
            </button>
        </form>
    );
}

interface HomeProps {
    user: CurrentUser;
    onLogout: () => void;
}

function Home({ user, onLogout }: HomeProps) {
    const [error, setError] = useState('');
    const [isLoggingOut, setIsLoggingOut] = useState(false);

    const logout = async () => {
        setError('');
        setIsLoggingOut(true);

        try {
            await apiRequest<void>('/api/auth/logout', { method: 'POST' });
            onLogout();
        } catch (requestError) {
            setError(getErrorMessage(requestError));
        } finally {
            setIsLoggingOut(false);
        }
    };

    return (
        <main className="shell">
            <section className="card home-card">
                <div className="status-badge"><span /> Signed in</div>
                <p className="eyebrow">Your workspace</p>
                <h1>AI Document Assistant</h1>
                <p className="success-message">Authentication is working.</p>

                <dl className="user-details">
                    <div>
                        <dt>Display name</dt>
                        <dd>{user.displayName}</dd>
                    </div>
                    <div>
                        <dt>Email</dt>
                        <dd>{user.email}</dd>
                    </div>
                </dl>

                {error && <div className="alert" role="alert">{error}</div>}

                <button
                    className="secondary-button"
                    type="button"
                    onClick={() => void logout()}
                    disabled={isLoggingOut}
                >
                    {isLoggingOut ? 'Signing out…' : 'Sign out'}
                </button>
            </section>
        </main>
    );
}

function getErrorMessage(error: unknown) {
    return error instanceof Error
        ? error.message
        : 'The request could not be completed.';
}

export default App;
