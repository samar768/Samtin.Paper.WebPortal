'use client';

import { useState } from 'react';
import { ArrowDown, ArrowUp } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { createListingPreferencesSchema, moveListingColumn, type ListingDefinition, type ListingPreferences } from '@/lib/listing-preferences';
import { errorMessage } from '@/lib/poc-api';

const selectClass = 'h-8 w-full rounded-md border border-slate-200 bg-white px-2 text-xs text-slate-700';

export function ListingSettings({ definition, value, onSave, onReset, onClose }: {
	definition: ListingDefinition; value: ListingPreferences;
	onSave: (value: ListingPreferences) => Promise<void>; onReset: () => Promise<void>; onClose: () => void;
}) {
	const [draft, setDraft] = useState(() => ({ ...value, columns: value.columns.map(column => ({ ...column })) }));
	const [saving, setSaving] = useState(false);
	const [error, setError] = useState('');
	const labels = new Map(definition.columns.map(column => [column.id, column.label]));
	const validation = createListingPreferencesSchema(definition.columns).safeParse(draft);
	async function commit(reset: boolean) {
		if (!reset && !validation.success) { setError(validation.error.issues[0].message); return; }
		setSaving(true); setError('');
		try {
			if (reset) await onReset(); else await onSave(draft);
			onClose();
		} catch (failure: unknown) {
			setError(errorMessage(failure)); setSaving(false);
		}
	}
	return <Dialog open onOpenChange={open => { if (!open && !saving) onClose(); }}>
		<DialogContent className="max-h-[90dvh] overflow-y-auto p-4 sm:max-w-xl" showCloseButton={!saving}>
			<DialogHeader>
				<DialogTitle>Listing settings</DialogTitle>
				<DialogDescription className="text-xs">Your layout for this company, on every device. Apply saves it; Cancel leaves it unchanged.</DialogDescription>
			</DialogHeader>
			<fieldset disabled={saving} className="min-w-0 space-y-4">
				<legend className="sr-only">Personal listing layout</legend>
				<div className="space-y-2">
					<div className="flex items-center justify-between text-xs text-slate-500"><span>Columns · keep at least one visible</span><span>Width (px)</span></div>
					<ol className="space-y-1">
						{draft.columns.map((column, index) => <li key={column.id} className="flex items-center gap-1 rounded-md border border-slate-200 p-2 text-xs">
							<label className="flex min-w-0 flex-1 items-center gap-2">
								<input type="checkbox" checked={column.visible} onChange={event => {
									const visible = event.target.checked;
									setDraft(current => ({ ...current, columns: current.columns.map(c => c.id === column.id ? { ...c, visible } : c) }));
								}} /><span>{labels.get(column.id)}</span>
							</label>
							<Button variant="ghost" className="size-7 shrink-0" disabled={saving || index === 0} title={`Move ${labels.get(column.id)} up`}
								aria-label={`Move ${labels.get(column.id)} up`} onClick={() => setDraft(current => moveListingColumn(current, column.id, -1))}><ArrowUp className="size-3" /></Button>
							<Button variant="ghost" className="size-7 shrink-0" disabled={saving || index === draft.columns.length - 1} title={`Move ${labels.get(column.id)} down`}
								aria-label={`Move ${labels.get(column.id)} down`} onClick={() => setDraft(current => moveListingColumn(current, column.id, 1))}><ArrowDown className="size-3" /></Button>
							<Input className="h-7 w-16 shrink-0 text-xs md:text-xs" aria-label={`${labels.get(column.id)} width`} type="number" min={80} max={600} step={1} value={Number.isNaN(column.width) ? '' : column.width}
								aria-invalid={!Number.isInteger(column.width) || column.width < 80 || column.width > 600}
								onChange={event => {
									const width = event.target.valueAsNumber;
									setDraft(current => ({ ...current, columns: current.columns.map(c => c.id === column.id ? { ...c, width } : c) }));
								}} />
						</li>)}
					</ol>
					<p className="text-[11px] text-slate-500">80–600 px per column. Open stays available. Wide layouts scroll inside the table.</p>
				</div>
				<div className="grid grid-cols-2 gap-3 text-xs">
					<label className="space-y-1">Row spacing<select aria-label="Row spacing" className={selectClass} value={draft.density} onChange={event => setDraft(current => ({ ...current, density: event.target.value === 'comfortable' ? 'comfortable' : 'compact' }))}>
						<option value="compact">Compact</option><option value="comfortable">Comfortable</option>
					</select></label>
					<label className="space-y-1">Rows per page<select aria-label="Rows per page" className={selectClass} value={draft.pageSize} onChange={event => {
						const pageSize = Number(event.target.value);
						if (pageSize === 10 || pageSize === 25 || pageSize === 50 || pageSize === 100) setDraft(current => ({ ...current, pageSize }));
					}}>{[10, 25, 50, 100].map(size => <option key={size} value={size}>{size}</option>)}</select></label>
					<label className="space-y-1">Default sort<select aria-label="Default sort" className={selectClass} value={draft.sortColumn} onChange={event => setDraft(current => ({ ...current, sortColumn: event.target.value }))}>
						{definition.columns.map(column => <option key={column.id} value={column.id}>{column.label}</option>)}
					</select></label>
					<label className="space-y-1">Direction<select aria-label="Direction" className={selectClass} value={draft.sortDirection} onChange={event => setDraft(current => ({ ...current, sortDirection: event.target.value === 'asc' ? 'asc' : 'desc' }))}>
						<option value="asc">Ascending</option><option value="desc">Descending</option>
					</select></label>
				</div>
			</fieldset>
			{(!validation.success || error) && <p role="alert" className="text-xs text-red-700">{error || (!validation.success && validation.error.issues[0].message)}</p>}
			<DialogFooter className="gap-2">
				<Button variant="ghost" className="sm:mr-auto" disabled={saving} onClick={() => void commit(true)}>Reset defaults</Button>
				<Button variant="outline" disabled={saving} onClick={onClose}>Cancel</Button>
				<Button disabled={saving || !validation.success} onClick={() => void commit(false)}>{saving ? 'Saving…' : 'Apply'}</Button>
			</DialogFooter>
		</DialogContent>
	</Dialog>;
}
