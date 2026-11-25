'use client';

import { useEffect, useId, useRef, useState, useSyncExternalStore } from 'react';
import { ChevronsUpDown } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle, DialogTrigger } from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { useSession } from '@/components/poc-session';
import { lookupResolver, lookupSearchSchema, type LookupSearch } from '@/lib/erp-lookups';
import { api, errorMessage } from '@/lib/poc-api';
import { applyLegacySelection, parseLegacySelection, serializeLegacySelection, updateLegacySelection } from '@/lib/legacy-selections';

export function ErpMultiLookup({ lookupId, id, label, value, disabled, required, maxLength, validationError, onChange }: {
	lookupId: string; id: string; label: string; value: string | null; disabled: boolean;
	required: boolean; maxLength: number; validationError?: string; onChange: (value: string | null) => void;
}) {
	const { username, contextVersion } = useSession();
	const scope = JSON.stringify({ username, contextVersion });
	const popupId = useId();
	const searchRef = useRef<HTMLInputElement>(null);
	const [open, setOpen] = useState(false);
	const [search, setSearch] = useState('');
	const [page, setPage] = useState(0);
	const [attempt, setAttempt] = useState(0);
	const [draft, setDraft] = useState<string[]>([]);
	const [limitError, setLimitError] = useState('');
	const [result, setResult] = useState<{ signature: string; data?: LookupSearch; error?: string }>();
	const [resolved, setResolved] = useState<{ signature: string; names?: Map<string, string | null>; error?: string }>();
	const version = useSyncExternalStore(lookupResolver.subscribe, () => lookupResolver.version(scope, lookupId), () => 0);
	const identity = JSON.stringify([scope, lookupId, value, version]);
	const signature = JSON.stringify([scope, lookupId, search, page, attempt, version]);
	const selection = parseLegacySelection(value);
	useEffect(() => {
		const { keys } = parseLegacySelection(value);
		if (!keys.length) return;
		let cancelled = false;
		Promise.all(keys.map(async key => {
			const option = await lookupResolver.resolve(scope, lookupId, key);
			return [key, option?.label ?? null] as const;
		})).then(entries => {
			if (!cancelled) setResolved({ signature: identity, names: new Map(entries) });
		}).catch((failure: unknown) => {
			if (!cancelled) setResolved({ signature: identity, error: errorMessage(failure) });
		});
		return () => { cancelled = true; };
	}, [scope, lookupId, value, identity]);
	useEffect(() => {
		if (!open) return;
		const controller = new AbortController();
		const timer = setTimeout(() => {
			api(`/value-help/${encodeURIComponent(lookupId)}/options?search=${encodeURIComponent(search)}&page=${page}`, lookupSearchSchema,
				{ signal: controller.signal, headers: { 'X-ERP-Context': contextVersion } })
				.then(data => { if (!controller.signal.aborted) setResult({ signature, data }); })
				.catch((failure: unknown) => {
					if (!controller.signal.aborted) setResult({ signature, error: errorMessage(failure) });
				});
		}, 200);
		return () => { clearTimeout(timer); controller.abort(); };
	}, [open, lookupId, search, page, signature, contextVersion]);
	const labels = resolved?.signature === identity ? resolved : undefined;
	const summary = selection.error ? 'Invalid stored selection' : !selection.keys.length ? 'No selection'
		: labels?.names ? selection.keys.map(key => labels.names?.get(key) ?? `Unavailable reference (${key})`).join(', ')
			: labels?.error ? 'Could not load descriptions' : 'Loading descriptions...';
	const current = result?.signature === signature ? result : undefined;
	const columns = current?.data?.definition.columns ?? [];
	const options = current?.data?.options ?? [];
	const pageKeys = options.map(option => option.key);
	const pageCount = pageKeys.filter(key => draft.includes(key)).length;
	const allOnPage = pageKeys.length > 0 && pageCount === pageKeys.length;
	const unavailable = draft.filter(key => labels?.names?.get(key) === null);
	const fieldError = validationError || selection.error || labels?.error;
	function changeOpen(next: boolean) {
		if (next) {
			if (disabled) return;
			setDraft(selection.keys);
			setSearch('');
			setPage(0);
			setAttempt(count => count + 1);
			setLimitError('');
		}
		setOpen(next);
	}
	function toggle(keys: string[], checked: boolean) {
		const next = updateLegacySelection(draft, keys, checked);
		if (checked) {
			if (next.length > 100) return setLimitError('Select at most 100 references.');
			if (keys.some(key => !key || /[|,]/.test(key))) return setLimitError('A reference code contains a reserved separator and cannot be selected.');
			if (maxLength > 0 && serializeLegacySelection(next).length > maxLength)
				return setLimitError(`This field allows up to ${maxLength} characters of stored reference codes.`);
		}
		setLimitError('');
		setDraft(next);
	}
	return <Dialog open={open && !disabled} onOpenChange={changeOpen}>
		<DialogTrigger asChild>
			<Button id={id} type="button" variant="outline" role="combobox" aria-label={label} aria-haspopup="dialog"
				aria-expanded={open && !disabled} aria-controls={open && !disabled ? popupId : undefined}
				aria-required={required} aria-invalid={Boolean(fieldError)}
				aria-describedby={`${id}-summary${fieldError ? ` ${id}-error` : ''}`}
				disabled={disabled} title={summary}
				className="h-auto min-h-8 w-full min-w-0 justify-between gap-2 whitespace-normal rounded border-slate-200 px-2 py-1.5 text-left text-xs font-normal shadow-none disabled:bg-slate-50"
				onKeyDown={event => {
					if (event.key === 'F4' || event.key === 'ArrowDown') { event.preventDefault(); changeOpen(true); }
				}}>
				<span id={`${id}-summary`} className="line-clamp-3 min-w-0 break-words">{summary}</span>
				<ChevronsUpDown className="size-3 shrink-0 text-slate-400" />
			</Button>
		</DialogTrigger>
		{fieldError && <p id={`${id}-error`} role="alert" className="mt-1 text-xs text-red-700">
			{fieldError}{labels?.error && !validationError && !selection.error && <> <button type="button" className="underline"
				onClick={() => lookupResolver.invalidate(scope, lookupId)}>Retry</button></>}
		</p>}
		<DialogContent id={popupId} className="flex max-h-[calc(100dvh-2rem)] flex-col gap-3 p-4 sm:max-w-[640px]"
			onOpenAutoFocus={event => { event.preventDefault(); searchRef.current?.focus(); }}
			onKeyDown={event => {
				if (event.key === 'Enter' && event.target instanceof HTMLInputElement) event.preventDefault();
			}}>
			<DialogHeader className="shrink-0 pr-8">
				<DialogTitle className="text-sm">{label}</DialogTitle>
				<DialogDescription className="text-xs">Check references across pages, then apply the selection.</DialogDescription>
			</DialogHeader>
			<Input ref={searchRef} value={search} aria-label={`Search ${label}`} placeholder="Search descriptions or codes..."
				className="h-9 shrink-0 text-xs" onChange={event => { setSearch(event.target.value); setPage(0); }} />
			<div className="min-h-0 overflow-auto rounded border border-slate-200" aria-busy={!current}>
				{columns.length > 0 && <table className="w-full table-fixed text-xs"
					style={{ minWidth: columns.reduce((width, column) => width + column.width, 42) }} aria-label={`${label} lookup results`}>
					<colgroup><col style={{ width: 42 }} />{columns.map((column, index) =>
						<col key={column.key} style={index < columns.length - 1 ? { width: column.width } : undefined} />)}</colgroup>
					<thead className="sticky top-0 bg-slate-50 text-left text-slate-600"><tr>
						<th scope="col" className="border-b py-2 text-center" aria-label="Tick">
							<input type="checkbox" className="size-4 align-middle" checked={allOnPage}
								ref={node => { if (node) node.indeterminate = pageCount > 0 && !allOnPage; }}
								aria-label="Select all on this page" title="Select all on this page"
								disabled={!pageKeys.length} onChange={event => toggle(pageKeys, event.target.checked)} />
						</th>
						{columns.map(column => <th key={column.key} scope="col" className="border-b px-2 py-2">{column.label}</th>)}
					</tr></thead>
					<tbody>{options.map((option, index) => {
						const checked = draft.includes(option.key);
						const checkboxId = `${popupId}-option-${index}`;
						return <tr key={option.key} className={checked ? 'bg-blue-50' : 'hover:bg-slate-50'}>
							<td className="border-b px-2 text-center">
								<input id={checkboxId} type="checkbox" className="size-4 align-middle" checked={checked}
									aria-label={`${option.label} (${option.key})`} onChange={event => toggle([option.key], event.target.checked)}
									onKeyDown={event => {
										if (event.key === 'Enter' && event.shiftKey) {
											event.preventDefault(); toggle([option.key], !checked);
										}
									}} />
							</td>
							{columns.map(column => <td key={column.key} className="border-b px-2">
								<label htmlFor={checkboxId} className="block cursor-pointer break-words py-2">{option.cells[column.key] ?? '—'}</label>
							</td>)}
						</tr>;
					})}</tbody>
				</table>}
				{!current && <p role="status" className="p-3 text-xs">Searching...</p>}
				{current?.data && !options.length && <p role="status" className="p-3 text-xs">No matching references.</p>}
			</div>
			{current?.error && <p role="alert" className="shrink-0 text-xs text-red-700">{current.error}{' '}
				<button type="button" className="underline" onClick={() => setAttempt(count => count + 1)}>Retry</button>
			</p>}
			{limitError && <p role="alert" className="shrink-0 text-xs text-red-700">{limitError}</p>}
			{unavailable.length > 0 && <details className="max-h-24 shrink-0 overflow-auto text-xs">
				<summary className="cursor-pointer">Unavailable selections ({unavailable.length})</summary>
				<ul>{unavailable.map(key => <li key={key} className="flex items-center justify-between gap-2">
					<span className="break-all">{key}</span>
					<Button type="button" variant="ghost" size="sm" aria-label={`Remove unavailable code ${key}`}
						onClick={() => toggle([key], false)}>Remove</Button>
				</li>)}</ul>
			</details>}
			<div className="flex shrink-0 flex-wrap items-center justify-between gap-2 text-xs">
				<span role="status">{draft.length} selected · Page {page + 1}</span>
				<div className="flex gap-2">
					<Button type="button" variant="outline" size="sm" disabled={page === 0 || !current} onClick={() => setPage(n => n - 1)}>Previous</Button>
					<Button type="button" variant="outline" size="sm" disabled={!current?.data?.hasMore} onClick={() => setPage(n => n + 1)}>Next</Button>
				</div>
			</div>
			<div className="flex shrink-0 flex-wrap justify-between gap-2">
				<Button type="button" variant="ghost" size="sm" disabled={!draft.length} onClick={() => { setDraft([]); setLimitError(''); }}>Clear selection</Button>
				<div className="flex gap-2">
					<Button type="button" size="sm" variant="outline" onClick={() => changeOpen(false)}>Cancel</Button>
					<Button type="button" size="sm" disabled={draft.length === 0 && (required || Boolean(selection.error))}
						onClick={() => {
							const next = applyLegacySelection(value, draft);
							if (next !== value) onChange(next);
							changeOpen(false);
						}}>Apply selection</Button>
				</div>
			</div>
		</DialogContent>
	</Dialog>;
}
