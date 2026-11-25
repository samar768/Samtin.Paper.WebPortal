'use client';

import { useEffect, useId, useRef, useState, useSyncExternalStore, type KeyboardEvent } from 'react';
import dynamic from 'next/dynamic';
import { ChevronsUpDown, Plus, Search, X } from 'lucide-react';
import { api, errorMessage } from '@/lib/poc-api';
import { lookupSearchSchema, lookupResolveSchema, lookupResolver, type LookupDefinition, type LookupOption, type LookupSearch } from '@/lib/erp-lookups';
import { useSession } from '@/components/poc-session';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle, DialogTrigger } from '@/components/ui/dialog';

const ErpMasterDrawer = dynamic(() => import('@/components/erp-master-drawer').then(module => module.ErpMasterDrawer), { ssr: false });

function subscribeMobile(onChange: () => void) {
	const query = window.matchMedia('(max-width: 639px)');
	query.addEventListener('change', onChange);
	return () => query.removeEventListener('change', onChange);
}
const mobileSnapshot = () => window.matchMedia('(max-width: 639px)').matches;
const serverMobileSnapshot = () => false;

export function ErpLookup({ lookupId, id, label, value, onChange, disabled, required, validationError, validationPath }: {
	lookupId: string; id: string; label: string; value: string | null; onChange: (key: string | null, label?: string) => void;
	disabled: boolean; required: boolean; validationError?: string; validationPath?: string;
}) {
	const session = useSession();
	const scope = JSON.stringify({ username: session.username, contextVersion: session.contextVersion });
	const identity = JSON.stringify([scope, lookupId, value]);
	const popupId = useId();
	const mobile = useSyncExternalStore(subscribeMobile, mobileSnapshot, serverMobileSnapshot);
	const [open, setOpen] = useState(false);
	const [creator, setCreator] = useState<{ masterId: string; title: string } | null>(null);
	const masterApplied = useRef(false);
	const [search, setSearch] = useState('');
	const [page, setPage] = useState(0);
	const [attempt, setAttempt] = useState(0);
	const [definition, setDefinition] = useState<LookupDefinition | null>(null);
	const [selection, setSelection] = useState<{ identity: string; option: LookupOption | null; error: string } | null>(null);
	const [results, setResults] = useState<{ signature: string; data?: LookupSearch; error?: string } | null>(null);
	const searchRef = useRef<HTMLInputElement | null>(null);
	const triggerRef = useRef<HTMLButtonElement | null>(null);
	const rowsRef = useRef<HTMLTableSectionElement | null>(null);
	const lookupVersion = useSyncExternalStore(lookupResolver.subscribe, () => lookupResolver.version(scope, lookupId), () => 0);
	const signature = JSON.stringify([scope, lookupId, search, page, attempt, lookupVersion]);
	useEffect(() => {
		let disposed = false;
		(value ? lookupResolver.resolve(scope, lookupId, value) : Promise.resolve(null)).then(option => {
			if (disposed) return;
			setSelection({ identity, option, error: value && !option
				? 'This reference is unavailable in the current lookup scope. It has not been cleared or replaced.' : '' });
		}).catch((failure: unknown) => {
			if (!disposed) setSelection({ identity, option: null, error: errorMessage(failure) });
		});
		return () => { disposed = true; };
	}, [lookupId, value, scope, identity, attempt, lookupVersion]);
	useEffect(() => {
		if (!open) return;
		const controller = new AbortController();
		const timer = setTimeout(() => {
			api(`/value-help/${encodeURIComponent(lookupId)}/options?search=${encodeURIComponent(search)}&page=${page}`, lookupSearchSchema,
				{ signal: controller.signal, headers: { 'X-ERP-Context': session.contextVersion } })
				.then(data => {
					if (!controller.signal.aborted) {
						setDefinition(data.definition); setResults({ signature, data });
					}
				}).catch((failure: unknown) => {
					if (!controller.signal.aborted) setResults({ signature, error: errorMessage(failure) });
				});
		}, 200);
		return () => { clearTimeout(timer); controller.abort(); };
	}, [open, lookupId, search, page, signature, session.contextVersion]);
	const resolved = selection?.identity === identity ? selection : null;
	const current = results?.signature === signature ? results : null;
	const options = current?.data?.options ?? [];
	const loading = open && current === null;
	const tableWidth = definition?.columns.reduce((width, column) => width + column.width, 0);
	// Leave room for popup padding, both borders and a native vertical scrollbar.
	const popupWidth = tableWidth ? Math.max(280, tableWidth + 48) : undefined;
	function select(option: LookupOption) {
		onChange(option.key, option.label);
		setSelection({ identity: JSON.stringify([scope, lookupId, option.key]), option, error: '' });
		setOpen(false);
	}
	function changeOpen(next: boolean) {
		setOpen(next);
		if (next) { setSearch(''); setPage(0); }
	}
	function retry() { lookupResolver.clear(); setAttempt(value => value + 1); }
	function navigateRows(event: KeyboardEvent, index: number) {
		if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp' && event.key !== 'Home' && event.key !== 'End') return;
		event.preventDefault();
		const buttons = rowsRef.current?.querySelectorAll<HTMLButtonElement>('[data-lookup-row]');
		if (!buttons?.length) return;
		const next = event.key === 'Home' ? 0 : event.key === 'End' ? buttons.length - 1 :
			Math.max(0, Math.min(buttons.length - 1, index + (event.key === 'ArrowDown' ? 1 : -1)));
		buttons[next]?.focus();
	}
	const trigger = <Button ref={triggerRef} id={id} type="button" variant="outline" role="combobox"
		aria-label={label} aria-haspopup="dialog" aria-expanded={open} aria-controls={open ? popupId : undefined}
		aria-required={required} aria-invalid={Boolean(validationError || resolved?.error)} data-validation-path={validationPath}
		aria-describedby={validationError ? `${id}-validation` : resolved?.error ? `${id}-lookup-error` : undefined}
		disabled={disabled} title={resolved?.option ? `${resolved.option.label} (${value})` : value ?? label}
		className="h-8 w-full min-w-0 justify-between gap-1 rounded border-slate-200 px-2 text-left text-xs font-normal shadow-none disabled:bg-slate-50"
		onKeyDown={(event) => {
			if (event.key === 'F4' || event.key === 'ArrowDown') { event.preventDefault(); changeOpen(true); }
		}}>
		<span className={`w-0 flex-1 truncate ${value ? '' : 'text-slate-400'}`}>
			{resolved?.option?.label ?? (value ? resolved ? `Unavailable reference: ${value}` : 'Loading description…' : `Select ${label.toLowerCase()}…`)}
		</span><ChevronsUpDown className="size-3 shrink-0 text-slate-400" />
	</Button>;
	const content = <div className="flex min-h-0 min-w-0 flex-col gap-2">
		<div className="relative shrink-0"><Search className="absolute left-2 top-2.5 size-3.5 text-slate-400" />
			<Input ref={searchRef} aria-label={`Search ${label}`} placeholder="Search descriptions or codes…" value={search}
				className="h-9 pl-7 text-xs" onChange={(event) => { setSearch(event.target.value); setPage(0); }}
				onKeyDown={(event) => {
					if (event.key === 'ArrowDown' && options.length) { event.preventDefault(); rowsRef.current?.querySelector<HTMLButtonElement>('[data-lookup-row]')?.focus(); }
					if (event.key === 'Enter' && options.length === 1) { event.preventDefault(); select(options[0]); }
				}} />
		</div>
		<div className="max-h-[45vh] min-h-0 min-w-0 overflow-auto rounded border border-slate-200" aria-busy={loading}>
			<table className="table-fixed text-xs" style={{ width: tableWidth }} aria-label={`${label} lookup results`}>
				<colgroup>{definition?.columns.map(column => <col key={column.key} style={{ width: column.width }} />)}</colgroup>
				<thead className="sticky top-0 bg-slate-50 text-left text-[11px] text-slate-500"><tr>
					{definition?.columns.map(column => <th key={column.key} scope="col" className="border-b px-2 py-2 font-medium">{column.label}</th>)}
				</tr></thead>
				<tbody ref={rowsRef}>{options.map((option, index) => <tr key={option.key}
					className={`${option.key === value ? 'bg-blue-50' : index % 2 ? 'bg-slate-50/40' : 'bg-white'} hover:bg-blue-50`}
					onClick={() => select(option)}>
					{definition?.columns.map((column, columnIndex) => <td key={column.key} className="border-b border-slate-100 px-2 py-2 truncate"
						title={option.cells[column.key] ?? undefined}>
						{columnIndex === 0 ? <button type="button" data-lookup-row className="w-full truncate text-left outline-blue-500"
							aria-label={`Select ${option.label} (${option.key})`} aria-pressed={option.key === value}
							onKeyDown={(event) => navigateRows(event, index)}
							onClick={(event) => { event.stopPropagation(); select(option); }}>{option.cells[column.key] ?? '—'}</button> :
							option.cells[column.key] ?? '—'}
					</td>)}
				</tr>)}</tbody>
			</table>
			{loading && <p role="status" className="p-4 text-xs text-slate-500">Searching…</p>}
			{current?.data && !options.length && <p role="status" className="p-4 text-xs text-slate-500">No matching references.</p>}
		</div>
		{current?.error && <div role="alert" className="shrink-0 text-xs text-red-700">{current.error} <Button variant="link" className="h-7 px-1 text-xs" onClick={retry}>Retry</Button></div>}
		<div className="flex shrink-0 flex-wrap items-center justify-between gap-2 text-[11px] text-slate-500">
			<span>Page {page + 1} · Select a row to apply</span>
			<div className="flex gap-1"><Button variant="outline" className="h-7 px-2 text-xs" disabled={loading || page === 0}
				onClick={() => setPage(value => value - 1)}>Previous</Button>
				<Button variant="outline" className="h-7 px-2 text-xs" disabled={loading || !current?.data?.hasMore}
					onClick={() => setPage(value => value + 1)}>Next</Button></div>
		</div>
		<div className="flex shrink-0 flex-wrap justify-between gap-1">
			<div className="flex flex-wrap gap-1">
				<Button variant="ghost" className="h-7 px-2 text-xs" disabled={!value} onClick={() => { onChange(null); setOpen(false); }}><X className="size-3" />Clear selection</Button>
				{current?.data?.definition.create && !disabled && <Button variant="ghost" className="h-7 px-2 text-xs text-blue-700"
					onClick={() => { masterApplied.current = false; setCreator(current.data!.definition.create!); setOpen(false); }}>
					<Plus className="size-3" />Add new
				</Button>}
			</div>
			<Button variant="outline" className="h-7 px-2 text-xs" onClick={() => setOpen(false)}>Cancel</Button>
		</div>
	</div>;
	return <div className="min-w-0">
		{mobile ? <Dialog open={open} onOpenChange={changeOpen}>
				<DialogTrigger asChild>{trigger}</DialogTrigger>
				<DialogContent id={popupId} className="flex max-h-[calc(100dvh-2rem)] min-w-0 flex-col gap-3 p-4" onOpenAutoFocus={(event) => { event.preventDefault(); searchRef.current?.focus(); }}
					onCloseAutoFocus={(event) => { event.preventDefault(); triggerRef.current?.focus(); }}>
					<DialogHeader className="shrink-0"><DialogTitle className="text-sm">{definition?.title ?? label}</DialogTitle>
						<DialogDescription className="text-xs">Search and choose a reference. Only its code is saved.</DialogDescription></DialogHeader>
					{content}
				</DialogContent>
			</Dialog> :
			<Popover open={open} onOpenChange={changeOpen}><PopoverTrigger asChild>{trigger}</PopoverTrigger>
				<PopoverContent id={popupId} role="dialog" aria-label={definition?.title ?? `${label} lookup`} align="start" collisionPadding={8}
					className="flex max-h-[var(--radix-popover-content-available-height)] w-[min(680px,calc(100vw-2rem))] flex-col p-3"
					style={popupWidth ? { width: `min(${popupWidth}px, calc(100vw - 2rem))` } : undefined}
					onOpenAutoFocus={(event) => { event.preventDefault(); searchRef.current?.focus(); }}>
					<p className="mb-2 shrink-0 text-xs font-semibold text-slate-700">{definition?.title ?? label}</p>{content}
				</PopoverContent>
			</Popover>}
		{resolved?.error && <p id={`${id}-lookup-error`} role="alert" className="mt-1 text-[10px] text-red-700">
			{resolved.error} <button type="button" className="underline" onClick={retry}>Retry</button>
		</p>}
		{creator && <ErpMasterDrawer masterId={creator.masterId} mode="new"
			onSaved={async record => {
				lookupResolver.invalidate(scope, lookupId);
				const resolved = await api(`/value-help/${encodeURIComponent(lookupId)}/resolve`, lookupResolveSchema,
					{ method: 'POST', headers: { 'X-ERP-Context': session.contextVersion }, body: JSON.stringify({ keys: [record.id] }) });
				const option = resolved.options.find(option => option.key === record.id);
				if (!option) throw new Error('The saved master is not eligible for this lookup. Review its configuration before selecting it.');
				masterApplied.current = true; select(option);
			}}
			onClose={() => { setCreator(null); if (!masterApplied.current) setOpen(true); }}
			onCloseAutoFocus={event => {
				event.preventDefault();
				if (masterApplied.current) triggerRef.current?.focus();
				else requestAnimationFrame(() => { if (searchRef.current) searchRef.current.focus(); else triggerRef.current?.focus(); });
			}} />}
	</div>;
}
