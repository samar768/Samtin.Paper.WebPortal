'use client';

import { useEffect, useState } from 'react';
import { api, errorMessage } from '@/lib/poc-api';
import { masterAuditSchema, type MasterAudit } from '@/lib/erp-masters';
import { masterAuditChanges } from '@/lib/master-audit';
import { useSession } from '@/components/poc-session';
import { ErpDrawer } from '@/components/erp-drawer';
import { Button } from '@/components/ui/button';

export function ErpMasterAudit({ masterId, recordId, apiPath, onClose }: {
	masterId: string; recordId?: string; apiPath?: string; onClose: () => void;
}) {
	const session = useSession();
	const [page, setPage] = useState(0);
	const [attempt, setAttempt] = useState(0);
	const [result, setResult] = useState<{ signature: string; data?: MasterAudit; error?: string } | null>(null);
	const signature = JSON.stringify([masterId, recordId, apiPath, session.contextVersion, page, attempt]);
	const current = result?.signature === signature ? result : null;
	const data = current?.data;
	useEffect(() => {
		const controller = new AbortController();
		const query = new URLSearchParams({ page: String(page) });
		if (recordId) query.set('recordId', recordId);
		api(`${apiPath ?? `/masters/${encodeURIComponent(masterId)}/audit`}?${query}`, masterAuditSchema,
			{ signal: controller.signal, headers: { 'X-ERP-Context': session.contextVersion } })
			.then(data => { if (!controller.signal.aborted) setResult({ signature, data }); })
			.catch((failure: unknown) => { if (!controller.signal.aborted) setResult({ signature, error: errorMessage(failure) }); });
		return () => controller.abort();
	}, [masterId, recordId, apiPath, session.contextVersion, page, signature]);
	return <ErpDrawer title={`${data?.definition.title ?? 'Master'} audit trail`}
		description={`Portal changes${recordId ? ` for ${recordId}` : ''}. Windows/direct SQL changes are not captured.`}
		busy={false} onClose={onClose}>
		<div className="min-h-0 flex-1 space-y-3 overflow-y-auto p-5 text-xs">
			{!current && <p role="status">Loading audit history...</p>}
			{current?.error && <div role="alert" className="rounded border border-red-200 bg-red-50 p-3 text-red-700">
				{current.error}<Button variant="link" size="sm" onClick={() => setAttempt(value => value + 1)}>Retry</Button>
			</div>}
			{data && <p className="rounded border bg-slate-50 p-3 text-slate-600">
				Detailed value logging is {data.definition.captureAuditValues ? 'enabled' : 'disabled'} for this master.
				Existing history is retained; changes while disabled retain actor/time only.
			</p>}
			{data?.entries.map(entry => {
				const changes = masterAuditChanges(entry.before, entry.after);
				return <article key={entry.id} className="space-y-2 rounded-lg border bg-white p-3">
					<div className="flex flex-wrap items-center justify-between gap-2">
						<strong className="font-mono">{entry.recordId}</strong><span className="capitalize">{entry.operation}</span>
					</div>
					<p className="text-slate-600">{entry.username} · {new Date(entry.occurredAt).toLocaleString()}</p>
					<p className="text-slate-500">Company {entry.companyCode ?? 'not recorded'} · Site {entry.siteCode ?? 'not recorded'} · Revision {entry.metadataRevision ?? 'not recorded'}</p>
					{!entry.valuesCaptured ? <p>Detailed values were not captured for this event.</p> :
						<details><summary className="cursor-pointer text-blue-700">Value changes ({changes.length})</summary>
							{changes.length ? <table className="mt-2 w-full table-fixed border-collapse text-left">
								<thead><tr><th className="border p-2">Field</th><th className="border p-2">Old value</th><th className="border p-2">New value</th></tr></thead>
								<tbody>{changes.map(change => <tr key={change.key}>
									<td className="break-words border p-2">{data.definition.fields.find(field => field.key === change.key)?.label ?? change.key}</td>
									<td className="break-words whitespace-pre-wrap border p-2">{change.before ?? '—'}</td>
									<td className="break-words whitespace-pre-wrap border p-2">{change.after ?? '—'}</td>
								</tr>)}</tbody>
							</table> : <p className="mt-2">No approved field value changed.</p>}
						</details>}
				</article>;
			})}
			{data && !data.entries.length && <p role="status">No portal audit events match this master and workspace.</p>}
		</div>
		<div className="flex shrink-0 items-center justify-between gap-2 border-t p-3 text-xs">
			<span>Page {page + 1}</span><div className="flex gap-2">
				<Button variant="outline" size="sm" disabled={!data || page === 0} onClick={() => setPage(value => value - 1)}>Previous</Button>
				<Button variant="outline" size="sm" disabled={!data?.hasMore} onClick={() => setPage(value => value + 1)}>Next</Button>
			</div>
		</div>
	</ErpDrawer>;
}
