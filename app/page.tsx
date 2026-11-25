'use client';

import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Eye, EyeOff, Layers3, ShieldCheck } from 'lucide-react';
import { api, errorMessage, authenticationSchema, setApiContext } from '@/lib/poc-api';

export default function LoginPage() {
	const [username, setUsername] = useState('');
	const [password, setPassword] = useState('');
	const [busy, setBusy] = useState(false);
	const [error, setError] = useState('');
	const [showPassword, setShowPassword] = useState(false);
	async function signIn(event: React.FormEvent) {
		event.preventDefault();
		setBusy(true); setError('');
		try {
			await api('/session', authenticationSchema, { method: 'POST', body: JSON.stringify({ username, password }) });
			setApiContext(null);
			setPassword('');
			window.location.replace('/select-company');
		} catch (failure) { setError(errorMessage(failure)); }
		finally { setBusy(false); }
	}
	return (
		<main className="min-h-dvh bg-white flex flex-col items-center justify-center px-5 py-10 text-slate-700">
			<div className="w-full max-w-[390px]">
				<div className="text-center mb-8">
					<div className="inline-flex items-center gap-2 text-2xl font-semibold tracking-tight text-slate-900"><span className="rounded-xl bg-blue-600 p-2 text-white"><Layers3 className="size-7" /></span>PaperSoft</div>
					<p className="mt-4 text-sm text-slate-500">Your business workspace, connected.</p>
				</div>
				<section className="rounded-xl border border-slate-200 bg-white p-7 sm:p-9 shadow-[0_8px_40px_-24px_rgba(15,23,42,0.2)]">
					<h1 className="text-2xl font-medium text-slate-900 mb-1">Sign in</h1>
					<p className="text-xs text-slate-400 mb-7">Continue with your existing ERP account.</p>
					<form onSubmit={signIn} className="space-y-5">
						<div className="space-y-2">
							<Label className="text-xs text-slate-500" htmlFor="username">Username</Label>
							<Input id="username" autoComplete="username" className="h-10 rounded-none border-0 border-b border-slate-200 px-0 shadow-none focus-visible:ring-0 focus-visible:border-blue-500" maxLength={10} required disabled={busy}
								value={username} onChange={(event) => setUsername(event.target.value)} />
						</div>
						<div className="space-y-2">
							<Label className="text-xs text-slate-500" htmlFor="password">Password</Label>
							<div className="relative"><Input id="password" type={showPassword ? 'text' : 'password'} autoComplete="current-password" maxLength={256} required
								className="h-10 rounded-none border-0 border-b border-slate-200 pl-0 pr-9 shadow-none focus-visible:ring-0 focus-visible:border-blue-500"
								disabled={busy} value={password} onChange={(event) => setPassword(event.target.value)} />
								<button type="button" className="absolute right-1 top-3 text-slate-400 hover:text-slate-700" disabled={busy}
									aria-label={showPassword ? 'Hide password' : 'Show password'} aria-pressed={showPassword} onClick={() => setShowPassword(!showPassword)}>
									{showPassword ? <EyeOff className="size-4" /> : <Eye className="size-4" />}
								</button></div>
						</div>
						{error && <p role="alert" className="text-xs text-red-700">{error}</p>}
						<Button type="submit" className="w-full h-10 bg-blue-600 hover:bg-blue-700" disabled={busy}>
							{busy ? 'Validating…' : 'Sign in'}
						</Button>
					</form>
					<p className="text-[11px] text-slate-400 text-center mt-6">Need access? Contact your ERP administrator.</p>
				</section>
				<p className="flex items-center justify-center gap-1.5 mt-5 text-[11px] text-slate-400"><ShieldCheck className="size-3.5" />Protected ERP workspace · Development POC</p>
			</div>
		</main>
	);
}
