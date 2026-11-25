'use client';

import { useId } from 'react';
import { ErpLookup } from '@/components/erp-lookup';
import { Input } from '@/components/ui/input';
import type { DataRow } from '@/lib/poc-api';

export function VoucherTermsEditor({ terms, disabled, errors, onChange }: {
	terms: DataRow[]; disabled: boolean; errors: Record<string, string>;
	onChange: (code: string, values: DataRow, path: string) => void;
}) {
	const formId = useId();
	return <section className="rounded-lg border border-slate-200 bg-white p-3 space-y-2">
		<h2 className="font-semibold text-xs">Terms and conditions</h2>
		<div className="grid gap-2 sm:grid-cols-2">{terms.map((term, index) => {
			const code = term.Code;
			if (!code) return null;
			const id = `${formId}-${code}`;
			const label = term.Label ?? term.TCManualCode ?? 'Term';
			const required = term.Mandatory === 'Y';
			const path = `terms[${index}].Value`;
			const unavailable = term.Editor === 'unsupported' || term.Editor === 'lookup' && !term.LookupId;
			const error = errors[path] ?? (unavailable ? 'This term configuration requires administrator review.' : undefined);
			const date = term.Editor === 'date' && (!term.Value || /^\d{4}-\d{2}-\d{2}$/.test(term.Value));
			return <div key={code} className="min-w-0 space-y-1 text-[11px] text-slate-500">
				<label htmlFor={id}>{label}{required ? ' *' : ''}</label>
				{term.LookupId && !disabled ? <>
					<ErpLookup id={id} label={label} lookupId={term.LookupId} value={term.ValueCode || null}
						disabled={false} required={required} validationError={error} validationPath={path}
						onChange={(key, name) => onChange(code, { ValueCode: key, Value: name ?? null }, path)} />
					{!term.ValueCode && term.Value && <p>Existing value: {term.Value}. Select a lookup entry to use it on this order.</p>}
				</> : <>
					<Input id={id} className="h-8 text-xs md:text-xs shadow-none" value={term.Value ?? ''}
						disabled={disabled || unavailable} type={date && !disabled ? 'date' : 'text'} maxLength={250}
						inputMode={term.Editor === 'integer' ? 'numeric' : term.Editor === 'decimal' ? 'decimal' : undefined}
						aria-required={required} aria-invalid={Boolean(error)} data-validation-path={path}
						aria-describedby={error ? `${id}-error` : undefined}
						onChange={(event) => onChange(code, { Value: event.target.value }, path)} />
					{error && <span id={`${id}-error`} className="text-[10px] text-red-700">{error}</span>}
				</>}
			</div>;
		})}</div>
	</section>;
}
