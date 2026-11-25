'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { Building2, LogOut } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { api, ApiError, companySelectionSchema, emptySchema, errorMessage, sessionSchema, setApiContext } from '@/lib/poc-api';
import type { z } from 'zod';

type Selection = z.infer<typeof companySelectionSchema>;

export default function CompanySelectionPage() {
	const router = useRouter();
	const [data, setData] = useState<Selection | null>(null);
	const [company, setCompany] = useState('');
	const [site, setSite] = useState('');
	const [search, setSearch] = useState('');
	const [busy, setBusy] = useState(false);
	const [error, setError] = useState('');
	const [attempt, setAttempt] = useState(0);
	useEffect(() => {
		const controller = new AbortController();
		api('/companies', companySelectionSchema, { signal: controller.signal }).then(result => {
			setData(result); setError('');
		}).catch((failure: unknown) => {
			if (controller.signal.aborted) return;
			if (failure instanceof ApiError && failure.status === 401) router.replace('/');
			else setError(errorMessage(failure));
		});
		return () => controller.abort();
	}, [router, attempt]);
	const selectedCode = company || data?.company || data?.companies.find(option => option.available)?.company || '';
	const selected = data?.companies.find(option => option.company === selectedCode);
	const selectedSite = site || (selected?.sites.length === 1 ? selected.sites[0].code : '');
	const choices = data?.companies.filter(option => `${option.companyName} ${option.financialYear}`.toLowerCase().includes(search.toLowerCase())) ?? [];
	async function enter(event: React.FormEvent) {
		event.preventDefault();
		if (!selected?.available || !selectedSite || !data) return;
		setBusy(true); setError('');
		try {
			const context = await api('/session/context', sessionSchema, {
				method: 'PUT', body: JSON.stringify({ company: selected.company, site: selectedSite, expectedVersion: data.contextVersion }),
			});
			setApiContext(context.contextVersion); window.location.replace('/dashboard');
		} catch (failure: unknown) { setError(errorMessage(failure)); }
		finally { setBusy(false); }
	}
	return <main className="min-h-dvh bg-slate-50 p-4 text-slate-700 sm:p-8">
		<section className="mx-auto max-w-3xl space-y-5">
			<header className="flex items-center justify-between gap-3">
				<div className="flex items-center gap-2 font-semibold text-slate-900"><Building2 className="size-5 text-blue-600" />PaperSoft</div>
				<Button variant="ghost" disabled={busy} onClick={async () => {
					setBusy(true); setError('');
					try { await api('/session', emptySchema, { method: 'DELETE' }); setApiContext(null); window.location.replace('/'); }
					catch (failure: unknown) { setError(errorMessage(failure)); }
					finally { setBusy(false); }
				}}><LogOut className="size-4" />Sign out</Button>
			</header>
			<div><h1 className="text-xl font-semibold text-slate-900">Select company &amp; financial year</h1>
				<p className="mt-1 text-xs text-slate-500">{data ? `Signed in as ${data.username}. Choose your authorized workspace.` : 'Loading authorized companies…'}</p></div>
			{error && <div role="alert" className="rounded border border-red-200 bg-red-50 p-3 text-xs text-red-700">{error}
				<Button className="ml-2" variant="outline" disabled={busy} onClick={() => setAttempt(value => value + 1)}>Reload choices</Button></div>}
			{data && <form onSubmit={enter} className="space-y-4 rounded-xl border border-slate-200 bg-white p-4 sm:p-6">
				<Input aria-label="Find company or financial year" placeholder="Find company or financial year…" value={search} disabled={busy} onChange={event => setSearch(event.target.value)} />
				<fieldset disabled={busy} className="min-w-0">
					<legend className="mb-2 text-xs font-medium">Available companies and financial years</legend>
					<div className="max-h-[45dvh] space-y-2 overflow-y-auto p-0.5">
						{choices.map(option => <label key={option.company} className={`flex items-start gap-3 rounded-lg border p-3 text-xs ${selectedCode === option.company ? 'border-blue-300 bg-blue-50' : 'border-slate-200'} ${!option.available ? 'opacity-70' : ''}`}>
							<input className="mt-1" type="radio" name="company" value={option.company} checked={selectedCode === option.company} disabled={!option.available}
								onChange={() => { setCompany(option.company); setSite(''); }} />
							<span className="min-w-0 flex-1"><span className="block font-medium text-slate-900">{option.companyName}</span>
								<span className="mt-1 block text-slate-500">{option.startDate} – {option.endDate} · Company {option.company}</span>
								{option.unavailableReason && <span className="mt-1 block text-amber-800">{option.unavailableReason}</span>}</span>
							<span className="text-right"><span className="block font-medium text-blue-800">FY {option.financialYear}</span>
								{option.readOnly && <span className="mt-1 block text-[10px] text-slate-500">Read-only</span>}</span>
						</label>)}
						{!choices.length && <p className="p-3 text-xs text-slate-500">{data.companies.length ? 'No matching company/year.' : 'No companies are permitted for this account. Contact your ERP administrator.'}</p>}
					</div>
				</fieldset>
				{selected?.available && (selected.sites.length === 1
					? <p className="rounded bg-slate-50 p-3 text-xs">Site: <strong>{selected.sites[0].name} ({selected.sites[0].code})</strong> · selected automatically</p>
					: <label className="block space-y-1 text-xs">Site<select aria-label="Site" className="h-9 w-full rounded border border-slate-200 bg-white px-2" required value={selectedSite} disabled={busy} onChange={event => setSite(event.target.value)}>
						<option value="">Choose a site…</option>{selected.sites.map(option => <option key={option.code} value={option.code}>{option.name} ({option.code})</option>)}
					</select></label>)}
				{selected?.readOnly && <p className="text-xs text-amber-800">This financial year is read-only in the POC. Order creation, changes and deletion remain enabled only in the approved current context.</p>}
				<div className="flex flex-wrap items-center justify-end gap-3">
					{data.company && <Link className="text-xs text-slate-500 hover:underline" href="/dashboard">Return to workspace</Link>}
					<Button type="submit" className="bg-blue-600 hover:bg-blue-700" disabled={busy || !selected?.available || !selectedSite}>{busy ? 'Opening…' : 'Open workspace'}</Button>
				</div>
			</form>}
		</section>
	</main>;
}
