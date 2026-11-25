'use client';

import { useEffect, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import { Eye, History, LogOut, Pencil, Plus, Printer, RefreshCw, Trash2 } from 'lucide-react';
import { toast } from 'sonner';
import { api, emptySchema, errorMessage } from '@/lib/poc-api';
import { masterListSchema, masterPrintSchema, type MasterList, type MasterPrint } from '@/lib/erp-masters';
import { lookupResolver } from '@/lib/erp-lookups';
import { ErpMasterDrawer } from '@/components/erp-master-drawer';
import { ErpMasterAudit } from '@/components/erp-master-audit';
import { RecordActionBar } from '@/components/record-action-bar';
import { useSession } from '@/components/poc-session';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog';

function PrintPreview({ data, onClose }: { data: MasterPrint; onClose: () => void }) {
	const session = useSession();
	const content = useRef<HTMLDivElement | null>(null);
	const frame = useRef<HTMLIFrameElement | null>(null);
	const [error, setError] = useState('');
	useEffect(() => () => { frame.current?.remove(); }, []);
	function print() {
		frame.current?.remove();
		const iframe = document.createElement('iframe');
		iframe.title = 'ERP master print document'; iframe.className = 'fixed h-0 w-0 border-0';
		document.body.appendChild(iframe); frame.current = iframe;
		const target = iframe.contentDocument;
		if (!target || !iframe.contentWindow || !content.current) { setError('The browser could not open the print document. Retry or check browser restrictions.'); return; }
		const style = target.createElement('style');
		style.textContent = 'body{font:12px Arial;color:#222;margin:24px}table{width:100%;border-collapse:collapse}th,td{border:1px solid #ddd;padding:7px;text-align:left}thead{display:table-header-group}tr{break-inside:avoid}';
		target.head.appendChild(style); target.title = data.definition.title;
		target.body.appendChild(content.current.cloneNode(true));
		iframe.contentWindow.focus(); iframe.contentWindow.print();
	}
	return <Dialog open onOpenChange={open => { if (!open) onClose(); }}><DialogContent className="sm:max-w-3xl">
		<DialogHeader><DialogTitle>Print preview</DialogTitle><DialogDescription>Authorized, filtered master list. Use your browser to print or save as PDF.</DialogDescription></DialogHeader>
		<div ref={content} className="max-h-[60vh] overflow-auto text-xs">
			<h2 className="mb-1 text-base font-semibold">{data.definition.title}</h2>
			<p className="mb-3">{session.companyName} · {session.financialYear} · {data.records.length} records</p>
			<table className="w-full border-collapse"><thead><tr><th className="border p-2 text-left">Code</th>
				{data.definition.fields.map(field => <th key={field.key} className="border p-2 text-left">{field.label}</th>)}</tr></thead>
				<tbody>{data.records.map(record => <tr key={record.id}><td className="border p-2">{record.id}</td>
					{data.definition.fields.map(field => <td key={field.key} className="border p-2">{record.values[field.key] ?? ''}</td>)}</tr>)}</tbody>
			</table>
		</div>
		{error && <p role="alert" className="text-xs text-red-700">{error}</p>}
		<Button onClick={print}><Printer className="size-4" />Print / Save PDF</Button>
	</DialogContent></Dialog>;
}

export function ErpMasterWorkspace({ masterId }: { masterId: string }) {
	const session = useSession();
	const router = useRouter();
	const [search, setSearch] = useState('');
	const [page, setPage] = useState(0);
	const [attempt, setAttempt] = useState(0);
	const [result, setResult] = useState<{ signature: string; data?: MasterList; error?: string } | null>(null);
	const [selected, setSelected] = useState<string | null>(null);
	const [drawer, setDrawer] = useState<{ mode: 'new' | 'view' | 'edit'; id?: string } | null>(null);
	const [busy, setBusy] = useState(false);
	const [error, setError] = useState('');
	const [preview, setPreview] = useState<MasterPrint | null>(null);
	const [audit, setAudit] = useState<{ recordId?: string } | null>(null);
	const signature = JSON.stringify([masterId, session.contextVersion, search, page, attempt]);
	const current = result?.signature === signature ? result : null;
	const data = current?.data;
	const record = data?.records.find(row => row.id === selected);
	useEffect(() => {
		const controller = new AbortController();
		const timer = setTimeout(() => {
			api(`/masters/${encodeURIComponent(masterId)}/records?search=${encodeURIComponent(search)}&page=${page}`,
				masterListSchema, { signal: controller.signal, headers: { 'X-ERP-Context': session.contextVersion } })
				.then(response => { if (!controller.signal.aborted) setResult({ signature, data: response }); })
				.catch((failure: unknown) => { if (!controller.signal.aborted) setResult({ signature, error: errorMessage(failure) }); });
		}, 200);
		return () => { clearTimeout(timer); controller.abort(); };
	}, [masterId, session.contextVersion, search, page, signature]);
	function invalidate() {
		const scope = JSON.stringify({ username: session.username, contextVersion: session.contextVersion });
		for (const id of data?.definition.lookupIds ?? []) lookupResolver.invalidate(scope, id);
		setAttempt(value => value + 1);
	}
	async function remove() {
		if (!record || !window.confirm(`Delete master ${record.id}? Referenced masters cannot be deleted.`)) return;
		setBusy(true); setError('');
		try {
			await api(`/masters/${encodeURIComponent(masterId)}/records/${encodeURIComponent(record.id)}`, emptySchema,
				{ method: 'DELETE', headers: { 'X-ERP-Context': session.contextVersion }, body: JSON.stringify({ version: record.version }) });
			toast.success(`Master ${record.id} deleted`);
			setSelected(null); invalidate();
		} catch (failure: unknown) { setError(errorMessage(failure)); }
		finally { setBusy(false); }
	}
	async function print() {
		setBusy(true); setError('');
		try {
			setPreview(await api(`/masters/${encodeURIComponent(masterId)}/print?search=${encodeURIComponent(search)}`,
				masterPrintSchema, { headers: { 'X-ERP-Context': session.contextVersion } }));
		} catch (failure: unknown) { setError(errorMessage(failure)); }
		finally { setBusy(false); }
	}
	return <section className="space-y-4">
		<div className="flex flex-wrap items-center justify-between gap-3">
			<div><h1 className="text-xl font-semibold text-slate-900">{data?.definition.title ?? 'Master workspace'}</h1>
				<p className="mt-1 text-xs text-slate-500">Shared, configuration-driven master maintenance
					{data && ` · ${data.definition.scope === 'site' ? `current site ${session.site}` : 'global within the business database'}`}</p></div>
			<Button variant="outline" size="sm" disabled={busy} onClick={() => setAttempt(value => value + 1)}><RefreshCw className="size-3.5" />Refresh</Button>
		</div>
		<Input aria-label="Search masters" placeholder="Search description or code..." className="max-w-sm bg-white"
			value={search} onChange={event => { setSearch(event.target.value); setPage(0); setSelected(null); }} />
		{(current?.error || error) && <p role="alert" className="rounded border border-red-200 bg-red-50 p-3 text-xs text-red-700">{current?.error || error}</p>}
		<div className="min-w-0 overflow-auto rounded-lg border bg-white" aria-busy={!current}>
			<table className="w-full text-left text-xs"><thead className="bg-slate-50 text-slate-500"><tr>
				<th className="w-28 border-b px-3 py-3 font-medium">Code</th>
				{data?.definition.fields.map(field => <th key={field.key} className="border-b px-3 py-3 font-medium">{field.label}</th>)}
				<th className="w-24 border-b px-3 py-3 font-medium">View</th>
			</tr></thead><tbody>{data?.records.map(row => <tr key={row.id} onClick={() => setSelected(row.id)}
				className={`cursor-pointer border-b last:border-b-0 ${row.id === selected ? 'bg-blue-50' : 'hover:bg-slate-50'}`}>
				<td className="px-3 py-2 font-mono">{row.id}</td>
				{data.definition.fields.map(field => <td key={field.key} className="px-3 py-2">{row.values[field.key] ?? '—'}</td>)}
				<td className="px-3 py-2"><Button variant="ghost" size="sm" aria-label={`View master ${row.id}`}
					onClick={event => { event.stopPropagation(); setSelected(row.id); setDrawer({ mode: 'view', id: row.id }); }}><Eye className="size-3.5" /></Button></td>
			</tr>)}</tbody></table>
			{!current && <p role="status" className="p-6 text-sm text-slate-500">Loading masters...</p>}
			{data && !data.records.length && <p role="status" className="p-6 text-sm text-slate-500">No matching masters.</p>}
		</div>
		<div className="flex items-center justify-between text-xs text-slate-500"><span>Page {page + 1}</span><div className="flex gap-2">
			<Button variant="outline" size="sm" disabled={!data || busy || page === 0} onClick={() => { setPage(value => value - 1); setSelected(null); }}>Previous</Button>
			<Button variant="outline" size="sm" disabled={!data?.hasMore || busy} onClick={() => { setPage(value => value + 1); setSelected(null); }}>Next</Button>
		</div></div>
		<RecordActionBar busy={busy} status={record ? `Selected ${record.id}` : 'Select a master to view, edit or delete'}
			details={record ? [{ label: 'Created by', value: record.recordInfo.createdBy }, { label: 'Modified by', value: record.recordInfo.modifiedBy }] : []}
			primary={data?.definition.capabilities.add ? { id: 'new', label: 'Add new', icon: Plus, onSelect: () => { setError(''); setDrawer({ mode: 'new' }); } } : undefined}
			actions={data ? [
				{ id: 'view', label: 'View', icon: Eye, disabled: !record, onSelect: () => { if (record) setDrawer({ mode: 'view', id: record.id }); } },
				...(data.definition.capabilities.edit ? [{ id: 'edit', label: 'Edit', icon: Pencil, disabled: !record, onSelect: () => { if (record) setDrawer({ mode: 'edit', id: record.id }); } }] : []),
				...(data.definition.capabilities.delete ? [{ id: 'delete', label: 'Delete', icon: Trash2, destructive: true, disabled: !record, onSelect: () => { void remove(); } }] : []),
				...(data.definition.capabilities.print ? [{ id: 'print', label: 'Print filtered list', group: 'output', icon: Printer, onSelect: () => { void print(); } }] : []),
				...(data.definition.capabilities.audit ? [{ id: 'audit', label: record ? 'Audit selected master' : 'Audit trail', group: 'output',
					icon: History, onSelect: () => setAudit({ recordId: record?.id }) }] : []),
				{ id: 'exit', label: 'Exit to dashboard', group: 'navigation', icon: LogOut, onSelect: () => {
					if (window.dispatchEvent(new Event('poc:before-navigation', { cancelable: true }))) router.push('/dashboard');
				} },
			] : []} />
		{drawer && <ErpMasterDrawer key={JSON.stringify([masterId, session.contextVersion, drawer])} masterId={masterId} recordId={drawer.id} mode={drawer.mode}
			onClose={() => setDrawer(null)} onSaved={saved => { setSelected(saved.id); invalidate(); }} />}
		{preview && <PrintPreview data={preview} onClose={() => setPreview(null)} />}
		{audit && <ErpMasterAudit key={JSON.stringify([masterId, session.contextVersion, audit])} masterId={masterId}
			recordId={audit.recordId} onClose={() => setAudit(null)} />}
	</section>;
}
