'use client';

import { createContext, useContext, useEffect, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import { ApiError, api, authenticationSchema, errorMessage, setApiContext, type Session } from '@/lib/poc-api';
import { Button } from '@/components/ui/button';

const SessionContext = createContext<Session | null>(null);

export function useSession() {
	const session = useContext(SessionContext);
	if (!session) throw new Error('The dashboard session has not loaded.');
	return session;
}

export function SessionGate({ children }: { children: React.ReactNode }) {
	const router = useRouter();
	const [session, setSession] = useState<Session | null>(null);
	const [error, setError] = useState('');
	const [attempt, setAttempt] = useState(0);
	const [stale, setStale] = useState(false);
	const version = useRef<string | null>(null);
	useEffect(() => {
		const controller = new AbortController();
		const expired = () => { setApiContext(null); setSession(null); router.replace('/'); };
		const changed = () => setStale(true);
		const accept = (authentication: { context: Session | null }) => {
			if (!authentication.context) {
				if (version.current) { setStale(true); return; }
				setApiContext(null); router.replace('/select-company'); return;
			}
			if (version.current && version.current !== authentication.context.contextVersion) { setStale(true); return; }
			version.current = authentication.context.contextVersion;
			setApiContext(version.current); setSession(authentication.context); setError('');
		};
		const refresh = () => {
			api('/session', authenticationSchema, { signal: controller.signal }).then(accept).catch((failure: unknown) => {
				if (!controller.signal.aborted) setError(errorMessage(failure));
			});
		};
		window.addEventListener('poc:session-expired', expired);
		window.addEventListener('poc:grants-changed', refresh);
		window.addEventListener('poc:context-changed', changed);
		api('/session', authenticationSchema, { signal: controller.signal }).then(accept).catch((failure: unknown) => {
			if (controller.signal.aborted) return;
			if (failure instanceof ApiError && failure.status === 401) expired();
			else setError(errorMessage(failure));
		});
		return () => {
			controller.abort(); window.removeEventListener('poc:session-expired', expired);
			window.removeEventListener('poc:grants-changed', refresh);
			window.removeEventListener('poc:context-changed', changed);
		};
	}, [router, attempt]);
	if (!session) return (
		<main className="min-h-screen bg-slate-50 text-slate-700 grid place-content-center p-8">
			<p role={error ? 'alert' : 'status'}>{error || 'Checking your session…'}</p>
			{error && <Button onClick={() => { setError(''); setAttempt((value) => value + 1); }}>Retry</Button>}
			{error && <a className="text-blue-700 underline mt-3" href="/select-company">Choose company / financial year</a>}
		</main>
	);
	return <SessionContext.Provider value={session}>
		{(stale || error) && <div role="alert" className="border-b border-amber-200 bg-amber-50 px-4 py-2 text-xs text-amber-900">
			{stale ? 'Company selection changed in another tab. This page cannot submit requests under the new selection. Preserve any unsaved details before reopening your workspace.' : error}
			{' '}<a className="font-medium underline" href="/select-company">Choose company / financial year</a>
		</div>}
		{children}
	</SessionContext.Provider>;
}
