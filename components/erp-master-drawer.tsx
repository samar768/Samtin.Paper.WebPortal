'use client';

import { useEffect, useRef, useState } from 'react';
import { Check, Save, X } from 'lucide-react';
import { toast } from 'sonner';
import { ApiError, api, errorMessage, type DataRow } from '@/lib/poc-api';
import { masterFormSchema, masterRecordSchema, type MasterForm, type MasterRecord } from '@/lib/erp-masters';
import { masterSavePayload } from '@/lib/master-draft';
import { ErpDrawer } from '@/components/erp-drawer';
import { ErpField } from '@/components/order-field';
import { RecordActionBar } from '@/components/record-action-bar';
import { useSession } from '@/components/poc-session';
import { Button } from '@/components/ui/button';

export function ErpMasterDrawer({ masterId, recordId, mode, onClose, onSaved, onCloseAutoFocus }: {
	masterId: string; recordId?: string; mode: 'new' | 'view' | 'edit'; onClose: () => void;
	onSaved: (record: MasterRecord) => Promise<void> | void; onCloseAutoFocus?: (event: Event) => void;
}) {
	const session = useSession();
	const [form, setForm] = useState<MasterForm | null>(null);
	const [values, setValues] = useState<DataRow>({});
	const [error, setError] = useState('');
	const [busy, setBusy] = useState(false);
	const [attempt, setAttempt] = useState(0);
	const [saved, setSaved] = useState<MasterRecord | null>(null);
	const [uncertain, setUncertain] = useState(false);
	const command = useRef<string | null>(null);
	const pending = useRef<string | null>(null);
	const formRef = useRef<HTMLFormElement | null>(null);
	const dirty = !saved && Boolean(form && JSON.stringify(values) !== JSON.stringify(form.record?.values ?? form.definition.defaults));
	const editable = Boolean(form && mode !== 'view' && (mode === 'new' ? form.definition.capabilities.add : form.definition.capabilities.edit));
	useEffect(() => {
		const controller = new AbortController();
		api(`/masters/${encodeURIComponent(masterId)}/form${recordId ? `?recordId=${encodeURIComponent(recordId)}` : ''}`,
			masterFormSchema, { signal: controller.signal, headers: { 'X-ERP-Context': session.contextVersion } })
			.then(data => {
				if (controller.signal.aborted) return;
				setForm(data); setValues(data.record?.values ?? data.definition.defaults); setError('');
			}).catch((failure: unknown) => { if (!controller.signal.aborted) setError(errorMessage(failure)); });
		return () => controller.abort();
	}, [masterId, recordId, session.contextVersion, attempt]);
	useEffect(() => {
		if (!form || mode === 'view') return;
		const frame = requestAnimationFrame(() => formRef.current?.querySelector<HTMLElement>('input:not(:disabled), select:not(:disabled)')?.focus());
		return () => cancelAnimationFrame(frame);
	}, [form, mode]);
	useEffect(() => {
		const unload = (event: BeforeUnloadEvent) => { if (dirty || uncertain) event.preventDefault(); };
		const navigation = (event: Event) => {
			if ((dirty || uncertain) && !window.confirm(uncertain
				? 'The master save result is uncertain. Check the master list before creating another record. Leave this form?'
				: 'Discard unsaved master changes?')) event.preventDefault();
		};
		window.addEventListener('beforeunload', unload);
		window.addEventListener('poc:before-navigation', navigation);
		return () => { window.removeEventListener('beforeunload', unload); window.removeEventListener('poc:before-navigation', navigation); };
	}, [dirty, uncertain]);
	function close() {
		if (busy) return;
		if ((dirty || uncertain) && !window.confirm(uncertain
			? 'The master save result is uncertain. Check the master list before creating another record. Close this form?'
			: 'Discard unsaved master changes?')) return;
		onClose();
	}
	async function save() {
		if (!form || !editable) return;
		setBusy(true); setError('');
		let persisted = saved;
		try {
			if (!persisted) {
				command.current ??= crypto.randomUUID();
				const payload = masterSavePayload(form, values, command.current);
				const signature = JSON.stringify(payload);
				if (pending.current && pending.current !== signature)
					throw new Error('The previous master save has an uncertain result. Retry the unchanged request before editing its values.');
				pending.current = signature;
				persisted = await api(`/masters/${encodeURIComponent(masterId)}/records${recordId ? `/${encodeURIComponent(recordId)}` : ''}`,
					masterRecordSchema, { method: recordId ? 'PUT' : 'POST',
						headers: { 'X-ERP-Context': session.contextVersion }, body: JSON.stringify(payload) });
				pending.current = null; setUncertain(false); setSaved(persisted); setValues(persisted.values);
				toast.success(`Master ${persisted.id} saved`);
			}
			await onSaved(persisted); onClose();
		} catch (failure: unknown) {
			if (persisted) setError(`Master ${persisted.id} was saved, but the calling screen could not apply it. ${errorMessage(failure)} Retry below; do not create it again.`);
			else {
				if (failure instanceof ApiError && failure.status < 500) { pending.current = null; command.current = null; }
				setUncertain(pending.current !== null); setError(errorMessage(failure));
			}
		} finally { setBusy(false); }
	}
	const details = form?.record ? [
		{ label: 'Code', value: form.record.id }, { label: 'Created by', value: form.record.recordInfo.createdBy },
		{ label: 'Modified by', value: form.record.recordInfo.modifiedBy },
	] : [];
	return <ErpDrawer title={`${mode === 'new' ? 'Add' : mode === 'edit' ? 'Edit' : 'View'} ${form?.definition.title ?? 'master'}`}
		description="Shared ERP master form. Visibility follows the registered master's business scope."
		busy={busy} onClose={close} onCloseAutoFocus={onCloseAutoFocus}>
		<div className="min-h-0 flex-1 overflow-y-auto p-5">
			{!form && !error && <p role="status" className="text-sm text-slate-500">Loading master fields...</p>}
			{error && <div role="alert" className="mb-4 rounded border border-red-200 bg-red-50 p-3 text-xs text-red-700">
				{error}{!form && <Button variant="link" className="h-7 px-1 text-xs" onClick={() => { setError(''); setAttempt(value => value + 1); }}>Retry</Button>}
			</div>}
			{form && <form ref={formRef} className="space-y-4" onSubmit={event => { event.preventDefault(); void save(); }}>
				<div className="rounded-lg border bg-slate-50 p-3 text-xs text-slate-500">
					<p>Code: <strong className="font-mono text-slate-700">{saved?.id ?? form.record?.id ?? 'Generated when saved'}</strong></p>
					<p className="mt-1">Configuration revision {form.definition.revision} · {session.companyName}</p>
					<p className="mt-1">{form.definition.scope === 'site' ? `Current site: ${session.site}` : 'Global within the business database'}
						{' · '}Detailed audit {form.definition.captureAuditValues ? 'on' : 'off'}</p>
				</div>
				{form.definition.fields.map(field => <ErpField key={field.key} field={field} value={values[field.key] ?? null}
					disabled={!editable || busy || Boolean(saved) || uncertain}
					onChange={value => { setValues(previous => ({ ...previous, [field.key]: value })); setError(''); command.current = null; }} />)}
				{mode !== 'view' && !editable && <p role="alert" className="text-xs text-amber-800">You cannot change this master in the current workspace.</p>}
				{mode === 'new' && <p className="text-xs leading-relaxed text-slate-500">
					Saving creates the master immediately. Cancelling the Sales Order later will not delete it.
				</p>}
				<button type="submit" className="sr-only" tabIndex={-1} disabled={!editable || busy}>Save master</button>
			</form>}
		</div>
		<div className="shrink-0 border-t p-3">
			<RecordActionBar busy={busy} status={saved ? `Saved ${saved.id}` : uncertain ? 'Save result uncertain; retry unchanged' : dirty ? 'Unsaved master changes' : mode === 'view' ? 'View mode' : 'Ready'}
				details={details} actions={[{ id: 'close', label: mode === 'view' || saved ? 'Close' : 'Cancel', icon: X, onSelect: close }]}
				primary={editable ? { id: 'save', label: saved ? 'Retry applying saved master' : 'Save master', icon: saved ? Check : Save,
					onSelect: () => {
						formRef.current?.requestSubmit();
					} } : undefined} />
		</div>
	</ErpDrawer>;
}
