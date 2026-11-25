'use client';

import { useEffect, useId, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import { History, LogOut, Pencil, RefreshCw, Save, X } from 'lucide-react';
import { toast } from 'sonner';
import { api, ApiError, errorMessage, type DataRow } from '@/lib/poc-api';
import { environmentFormSchema, environmentTabs, type EnvironmentForm } from '@/lib/environment-settings';
import { masterRecordSchema } from '@/lib/erp-masters';
import { masterSavePayload } from '@/lib/master-draft';
import { ErpField } from '@/components/order-field';
import { ErpMultiLookup } from '@/components/erp-multi-lookup';
import { ErpMasterAudit } from '@/components/erp-master-audit';
import { RecordActionBar } from '@/components/record-action-bar';
import { useSession } from '@/components/poc-session';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';

// Desktop control widths from the Windows Environment form; the grid can stack on narrow screens.
const environmentLookupWidths: Readonly<Record<string, number | undefined>> = {
	'env-unit': 102,
	'env-city': 229,
	'env-accounts': 282,
	'env-designations': 324,
	'env-open-rate-vouchers': 441,
	'env-rate-categories': 441,
	'env-groups': 566,
};

export function ErpEnvironmentWorkspace() {
	const session = useSession();
	const router = useRouter();
	const id = useId();
	const formRef = useRef<HTMLFormElement>(null);
	const command = useRef<string | null>(null);
	const pending = useRef<string | null>(null);
	const [form, setForm] = useState<EnvironmentForm | null>(null);
	const [values, setValues] = useState<DataRow>({});
	const [tab, setTab] = useState<(typeof environmentTabs)[number]>('Gate');
	const [editing, setEditing] = useState(false);
	const [attempt, setAttempt] = useState(0);
	const [busy, setBusy] = useState(false);
	const [uncertain, setUncertain] = useState(false);
	const [error, setError] = useState('');
	const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});
	const [audit, setAudit] = useState(false);
	const dirty = form !== null && JSON.stringify(values) !== JSON.stringify(form.record?.values ?? form.definition.defaults);
	useEffect(() => {
		const controller = new AbortController();
		api('/environment-settings/form', environmentFormSchema,
			{ signal: controller.signal, headers: { 'X-ERP-Context': session.contextVersion } })
			.then(data => { if (!controller.signal.aborted) {
				setForm(data); setValues(data.record?.values ?? data.definition.defaults);
				setEditing(data.record === null && data.definition.capabilities.add); setError('');
			} }).catch((failure: unknown) => { if (!controller.signal.aborted) setError(errorMessage(failure)); });
		return () => controller.abort();
	}, [session.contextVersion, attempt]);
	useEffect(() => {
		if (!editing) return;
		const frame = requestAnimationFrame(() => formRef.current?.querySelector<HTMLElement>(
			'input:not(:disabled),select:not(:disabled),button[role="combobox"]:not(:disabled)')?.focus());
		return () => cancelAnimationFrame(frame);
	}, [editing]);
	useEffect(() => {
		const unload = (event: BeforeUnloadEvent) => { if (dirty || uncertain) event.preventDefault(); };
		const navigation = (event: Event) => {
			if ((dirty || uncertain) && !window.confirm(uncertain ? 'The save result is uncertain. Leave this settings draft?'
				: 'Discard unsaved environment settings?')) event.preventDefault();
		};
		window.addEventListener('beforeunload', unload); window.addEventListener('poc:before-navigation', navigation);
		return () => { window.removeEventListener('beforeunload', unload); window.removeEventListener('poc:before-navigation', navigation); };
	}, [dirty, uncertain]);
	function discard() {
		if (busy || uncertain || dirty && !window.confirm('Discard unsaved environment settings?')) return false;
		if (form) setValues(form.record?.values ?? form.definition.defaults);
		setEditing(false); setError(''); setFieldErrors({}); command.current = null;
		return true;
	}
	async function save() {
		if (!form || !editing) return;
		setBusy(true); setError(''); setFieldErrors({});
		try {
			command.current ??= crypto.randomUUID();
			const payload = masterSavePayload(form, values, command.current);
			const signature = JSON.stringify(payload);
			if (pending.current && pending.current !== signature) throw new Error('Retry the unchanged uncertain request first.');
			pending.current = signature;
			const saved = await api('/environment-settings/', masterRecordSchema, {
				method: 'PUT', headers: { 'X-ERP-Context': session.contextVersion }, body: JSON.stringify(payload),
			});
			pending.current = null; command.current = null; setUncertain(false);
			setForm({ ...form, record: saved }); setValues(saved.values); setEditing(false);
			toast.success('Environment settings saved for this site');
		} catch (failure: unknown) {
			if (failure instanceof ApiError && failure.status < 500) {
				pending.current = null; command.current = null; setFieldErrors(failure.errors);
				const key = Object.keys(failure.errors)[0]?.replace(/^values\./, '');
				const invalid = form.layout.find(field => field.key === key);
				if (invalid) setTab(invalid.tab);
			}
			setUncertain(pending.current !== null); setError(errorMessage(failure));
		} finally { setBusy(false); }
	}
	function change(key: string, value: string | null) {
		setValues(previous => ({ ...previous, [key]: value })); setError(''); setFieldErrors({}); command.current = null;
	}
	const disabled = !editing || busy || uncertain;
	const layout = form?.layout.filter(field => field.tab === tab) ?? [];
	const groups = [...new Set(layout.map(field => field.group))];
	return <section className="min-w-0 space-y-4">
		<div className="flex flex-wrap items-center justify-between gap-3">
			<div><h1 className="text-xl font-semibold">Environment Setting</h1>
				<p className="mt-1 text-xs text-slate-500">Windows Master configuration · current site {session.site} · revision {form?.definition.revision ?? '…'}</p></div>
			<Button variant="outline" size="sm" disabled={busy || uncertain} onClick={() => {
				if (discard()) { setForm(null); setAttempt(value => value + 1); }
			}}><RefreshCw className="size-3.5" />Refresh</Button>
		</div>
		<p className="rounded-lg border bg-slate-50 p-3 text-xs text-slate-600">
			Settings affect this site&apos;s ERP operations. EXE/data/image paths configure Windows integrations; the portal does not upload or execute files.
			{' '}Required fields across all six tabs are validated before saving. Existing missing values are not silently defaulted.
		</p>
		{error && <p role="alert" className="rounded border border-red-200 bg-red-50 p-3 text-xs text-red-700">{error}</p>}
		{!form && !error && <p role="status">Loading environment settings...</p>}
		{form && <>
			<div role="tablist" aria-label="Environment modules" className="flex flex-wrap gap-1 rounded-lg border bg-slate-50 p-1">
				{environmentTabs.map(name => <button key={name} type="button" role="tab" id={`${id}-${name}`}
					aria-controls={`${id}-panel`} aria-selected={name === tab} className={`rounded px-4 py-2 text-xs font-medium ${name === tab ? 'bg-blue-600 text-white' : 'text-slate-600 hover:bg-white'}`}
					tabIndex={name === tab ? 0 : -1}
					onClick={() => setTab(name)} onKeyDown={event => {
						if (!['ArrowRight', 'ArrowLeft', 'Home', 'End'].includes(event.key)) return;
						event.preventDefault();
						const index = environmentTabs.indexOf(name);
						const next = event.key === 'Home' ? 0 : event.key === 'End' ? 5 :
							(index + (event.key === 'ArrowRight' ? 1 : 5)) % 6;
						setTab(environmentTabs[next]); event.currentTarget.parentElement?.querySelectorAll<HTMLButtonElement>('[role="tab"]')[next]?.focus();
					}}>
					{name}{form.layout.some(field => field.tab === name && fieldErrors[`values.${field.key}`]) && ' !'}
				</button>)}
			</div>
			<form ref={formRef} noValidate role="tabpanel" id={`${id}-panel`} aria-labelledby={`${id}-${tab}`}
				className="space-y-4" onSubmit={event => {
					event.preventDefault(); if (window.confirm('Save environment settings for the current site?')) void save();
				}}>
				{groups.map(group => {
					const settings = layout.filter(field => field.group === group);
					const lookupTrackWidth = Math.max(0, ...settings.map(field => field.lookupId
						? environmentLookupWidths[field.lookupId] ?? 282 : 0));
					return <fieldset key={group} className="min-w-0 rounded-lg border bg-white p-4">
					<legend className="px-2 text-xs font-semibold text-slate-700">{group}</legend>
					<div className={`grid min-w-0 gap-4 ${lookupTrackWidth ? '' : 'md:grid-cols-2 xl:grid-cols-3'}`}
						style={lookupTrackWidth ? { gridTemplateColumns: `repeat(auto-fit, minmax(min(100%, ${Math.max(282, lookupTrackWidth)}px), 1fr))` } : undefined}>
						{settings.map(setting => {
							const field = form.definition.fields.find(field => field.key === setting.key)!;
							const value = values[field.key] ?? null;
							const validation = fieldErrors[`values.${field.key}`]?.join(' ');
							const controlId = `${id}-${field.key}`;
							return <div key={field.key} className="w-full min-w-0"
								style={setting.lookupId ? { maxWidth: environmentLookupWidths[setting.lookupId] } : undefined}>
								{setting.multiple || setting.choices.length ? <>
									<label htmlFor={controlId} className="mb-1 block text-[11px] text-slate-500">{field.label}{field.required && ' *'}</label>
									{setting.multiple ? <ErpMultiLookup lookupId={setting.lookupId!} id={controlId} label={field.label} value={value}
										disabled={disabled || !field.writable} required={field.required} maxLength={field.length}
										validationError={validation} onChange={value => change(field.key, value)} /> :
										<select id={controlId} value={value ?? ''} disabled={disabled} required={field.required} aria-invalid={Boolean(validation)}
											className="h-8 w-full min-w-0 rounded border border-slate-200 bg-white px-2 text-xs disabled:bg-slate-50"
											onChange={event => change(field.key, event.target.value || null)}>
											<option value="">Not configured</option>
											{value && !setting.choices.some(choice => choice.code === value) && <option value={value}>Unsupported existing value: {value}</option>}
											{setting.choices.map(choice => <option key={choice.code} value={choice.code}
												disabled={!choice.selectable}>{choice.label}</option>)}
										</select>}
									{validation && !setting.multiple && <p className="mt-1 text-xs text-red-700">{validation}</p>}
								</> : <ErpField field={field} value={value} disabled={disabled} onChange={value => change(field.key, value)}
									validationError={validation} validationPath={`values.${field.key}`} />}
								{setting.maximum !== null && <p className="mt-1 text-[10px] text-slate-400">Supported range: 0–{setting.maximum}</p>}
							</div>;
						})}
					</div>
					{tab === 'Gate' && group === 'Weightment' && <div className="mt-3 max-w-sm space-y-1">
						<label htmlFor={`${id}-kanta-value`} className="text-[11px] text-slate-500">Weightment value (Windows display only)</label>
						<Input id={`${id}-kanta-value`} disabled value="Value" className="h-8 text-xs" />
						<p className="text-xs text-slate-500">Value {values.WeightExpression ?? '× or ÷'} conversion rate. No weighbridge reading is executed here.</p>
						<p className="text-[11px] text-slate-400">Windows example: if the Kanta returns KG and you maintain MT, use Value / 1000; select / and enter conversion rate 1000.</p>
					</div>}
					</fieldset>;
				})}
				<button type="submit" className="sr-only" tabIndex={-1} disabled={disabled}>Save settings</button>
			</form>
			<RecordActionBar busy={busy} status={uncertain ? 'Save result uncertain; retry unchanged' : dirty ? 'Unsaved settings' : editing ? 'Edit mode' : 'View mode'}
				details={form.record ? [{ label: 'Created by', value: form.record.recordInfo.createdBy }, { label: 'Modified by', value: form.record.recordInfo.modifiedBy }] : []}
				primary={editing ? { id: 'save', label: uncertain ? 'Retry save' : 'Save settings', icon: Save,
					onSelect: () => formRef.current?.requestSubmit() } :
					(form.record ? form.definition.capabilities.edit : form.definition.capabilities.add) ? {
						id: 'edit', label: form.record ? 'Edit settings' : 'Configure site', icon: Pencil, onSelect: () => setEditing(true),
					} : undefined}
				actions={[
					{ id: 'cancel', label: 'Cancel changes', icon: X, disabled: !editing || uncertain, onSelect: () => { discard(); } },
					...(form.definition.capabilities.audit ? [{ id: 'audit', label: 'Audit trail', icon: History, onSelect: () => setAudit(true) }] : []),
					{ id: 'exit', label: 'Exit to dashboard', icon: LogOut, onSelect: () => {
						if (window.dispatchEvent(new Event('poc:before-navigation', { cancelable: true }))) router.push('/dashboard');
					} },
				]} />
		</>}
		{audit && <ErpMasterAudit masterId="environment" apiPath="/environment-settings/audit" onClose={() => setAudit(false)} />}
	</section>;
}
