import { useCallback, useEffect, useState } from 'react';
import { LoginPage, LoadingPage, RegistrationPage } from './AuthPages';
import { ProjectDetailsPage, ProjectsDashboard } from './Projects';
import { ProjectChatWorkspace } from './ChatWorkspace';
import type { CurrentUser } from './api';
import './App.css';

type Route =
    | { page: 'login' }
    | { page: 'register' }
    | { page: 'projects' }
    | { page: 'project-chat'; projectId: string; conversationId?: string }
    | { page: 'project-documents'; projectId: string }
    | { page: 'unknown' };

function readRoute(): Route {
    const path = window.location.pathname.replace(/\/+$/, '') || '/';

    if (path === '/login') return { page: 'login' };
    if (path === '/register') return { page: 'register' };
    if (path === '/projects') return { page: 'projects' };

    const conversationMatch = path.match(/^\/projects\/([^/]+)\/chats\/([^/]+)$/);
    if (conversationMatch) {
        return {
            page: 'project-chat',
            projectId: decodeURIComponent(conversationMatch[1]),
            conversationId: decodeURIComponent(conversationMatch[2]),
        };
    }

    const documentsMatch = path.match(/^\/projects\/([^/]+)\/documents$/);
    if (documentsMatch) {
        return { page: 'project-documents', projectId: decodeURIComponent(documentsMatch[1]) };
    }

    const projectMatch = path.match(/^\/projects\/([^/]+)$/);
    return projectMatch
        ? { page: 'project-chat', projectId: decodeURIComponent(projectMatch[1]) }
        : { page: 'unknown' };
}

function App() {
    const [route, setRoute] = useState<Route>(readRoute);
    const [user, setUser] = useState<CurrentUser | null>(null);
    const [isCheckingSession, setIsCheckingSession] = useState(true);
    const [sessionError, setSessionError] = useState('');

    const navigate = useCallback((path: string, replace = false) => {
        if (replace) {
            window.history.replaceState(null, '', path);
        } else {
            window.history.pushState(null, '', path);
        }
        setRoute(readRoute());
        window.scrollTo({ top: 0, behavior: 'smooth' });
    }, []);

    useEffect(() => {
        const handlePopState = () => {
            const nextRoute = readRoute();
            const isAuthRoute = nextRoute.page === 'login' || nextRoute.page === 'register';

            if (user && (isAuthRoute || nextRoute.page === 'unknown')) {
                window.history.replaceState(null, '', '/projects');
                setRoute({ page: 'projects' });
            } else if (!user && !isAuthRoute) {
                window.history.replaceState(null, '', '/login');
                setRoute({ page: 'login' });
            } else {
                setRoute(nextRoute);
            }
        };

        window.addEventListener('popstate', handlePopState);
        return () => window.removeEventListener('popstate', handlePopState);
    }, [user]);

    useEffect(() => {
        const checkSession = async () => {
            try {
                const response = await fetch('/api/auth/me', {
                    credentials: 'include',
                });

                if (response.ok) {
                    const currentUser = await response.json() as CurrentUser;
                    setUser(currentUser);

                    const currentRoute = readRoute();
                    if (currentRoute.page === 'login' ||
                        currentRoute.page === 'register' ||
                        currentRoute.page === 'unknown') {
                        navigate('/projects', true);
                    }
                } else if (response.status !== 401) {
                    setSessionError('The current session could not be checked.');
                }

                if (!response.ok) {
                    const currentRoute = readRoute();
                    if (currentRoute.page !== 'login' && currentRoute.page !== 'register') {
                        navigate('/login', true);
                    }
                }
            } catch {
                setSessionError('Unable to connect to the backend.');
                const currentRoute = readRoute();
                if (currentRoute.page !== 'login' && currentRoute.page !== 'register') {
                    navigate('/login', true);
                }
            } finally {
                setIsCheckingSession(false);
            }
        };

        void checkSession();
    }, [navigate]);

    if (isCheckingSession) {
        return <LoadingPage />;
    }

    const authenticated = (currentUser: CurrentUser) => {
        setUser(currentUser);
        setSessionError('');
        navigate('/projects', true);
    };

    const signedOut = () => {
        setUser(null);
        navigate('/login', true);
    };

    if (!user) {
        return route.page === 'register'
            ? <RegistrationPage sessionError={sessionError} onAuthenticated={authenticated} onNavigate={navigate} />
            : <LoginPage sessionError={sessionError} onAuthenticated={authenticated} onNavigate={navigate} />;
    }

    if (route.page === 'project-documents') {
        return (
            <ProjectDetailsPage
                user={user}
                projectId={route.projectId}
                onNavigate={navigate}
                onSignedOut={signedOut}
            />
        );
    }

    if (route.page === 'project-chat') {
        return (
            <ProjectChatWorkspace
                key={`${route.projectId}-${route.conversationId ?? 'root'}`}
                user={user}
                projectId={route.projectId}
                conversationId={route.conversationId}
                onNavigate={navigate}
                onSignedOut={signedOut}
            />
        );
    }

    return (
        <ProjectsDashboard
            user={user}
            onNavigate={navigate}
            onSignedOut={signedOut}
        />
    );
}

export default App;
