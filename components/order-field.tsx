'use client';

import { useEffect, useId, useState } from 'react';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { ErpLookup } from '@/components/erp-lookup';
import { api, errorMessage, rowsSchema, type DataRow, type Field } from '@/lib/poc-api';

const selectClass = 'h-8 w-full rounded border border-slate-200 bg-white px-2 text-xs text-slate-700 disabled:bg-slate-50 disabled:opacity-75 focus:outline-blue-500';
const labels: Record<string, string> = {
	RecId: 'Order reference', V_Date: 'Order date', PartyCode: 'Buyer',
	PartyOrdNo: 'Buyer order number', PartyOrdDt: 'Buyer order date', DelAt: 'Delivery to',
	DelDate: 'Delivery date', FormCode: 'Against form', Exciseable: 'Excisable', OrderType: 'Order type',
	ItemCode: 'Item', ItemDesc: 'Description', BF: 'BF', GSM: 'GSM', SizeReel: 'Width',
	SizeHeight: 'Height', SizeUnit: 'Size unit', QtySDU: 'Secondary weight', Qty: 'Weight',
	Reel: 'Reels', Tl_ExcessQty: 'Excess tolerance', TL_ShortageQty: 'Shortage tolerance',
};

export function fieldLabel(field: Field) { return field.section === 'master' ? field.label : labels[field.key] ?? field.label; }

export function ErpField({ field, value, disabled, onChange, rowNumber, validationError, validationPath }: {
	field: Field; value: string | null; disabled: boolean; onChange: (value: string | null) => void; rowNumber?: number;
	validationError?: string; validationPath?: string;
}) {
	const id = useId();
	const [options, setOptions] = useState<DataRow[]>([]);
	const [error, setError] = useState('');
	const [search, setSearch] = useState('');
	const [focused, setFocused] = useState(false);
	useEffect(() => {
		if (field.valueHelp || !field.lookup || disabled || rowNumber !== undefined && !focused) return;
		const controller = new AbortController();
		const timer = setTimeout(() => {
			api(`/lookups/${field.lookup}?search=${encodeURIComponent(search)}`, rowsSchema, { signal: controller.signal })
				.then((rows) => { setOptions(rows); setError(''); })
				.catch((failure: unknown) => { if (!controller.signal.aborted) setError(errorMessage(failure)); });
		}, 150);
		return () => { clearTimeout(timer); controller.abort(); };
	}, [field.lookup, field.valueHelp, search, disabled, rowNumber, focused]);
	const label = fieldLabel(field);
	const accessibleLabel = rowNumber === undefined ? undefined : `Row ${rowNumber} ${label}`;
	const fixedLookup = ['orderTypes', 'yesNo', 'sizeUnits', 'grain'].includes(field.lookup ?? '');
	const isDate = ['date', 'smalldatetime', 'datetime', 'datetime2'].includes(field.type);
	const isText = ['varchar', 'char', 'nvarchar', 'nchar'].includes(field.type);
	const dateOnly = field.type === 'date' || ['V_Date', 'DelDate', 'PartyOrdDt'].includes(field.key);
	const integer = ['int', 'smallint', 'bigint', 'tinyint'].includes(field.type);
	const numeric = integer || ['decimal', 'numeric', 'float', 'real', 'money', 'smallmoney'].includes(field.type);
	const masterName = options.find((row) => row.code === value)?.label;
	return (
		<div className={rowNumber === undefined ? 'space-y-1 min-w-0' : 'min-w-0'}>
			{rowNumber === undefined && <Label htmlFor={id} className="text-slate-500 text-[11px]">{label}{field.required && ' *'}</Label>}
			{field.valueHelp ? <ErpLookup lookupId={field.valueHelp} id={id} label={accessibleLabel ?? label} value={value}
				disabled={disabled || !field.writable} required={field.required} onChange={onChange}
				validationError={validationError} validationPath={validationPath} /> : fixedLookup || field.type === 'bit'
				? <select id={id} aria-label={accessibleLabel} aria-invalid={Boolean(validationError)}
					aria-describedby={validationError ? `${id}-validation` : undefined} data-validation-path={validationPath}
					value={field.type === 'bit' ? value?.toLowerCase() ?? '' : value ?? ''}
					disabled={disabled || !field.writable} required={field.required}
					className={`${selectClass} ${validationError ? 'border-red-400 bg-red-50/30 focus:outline-red-400' : ''}`}
					onFocus={() => setFocused(true)} onBlur={() => setFocused(false)}
					onChange={(event) => onChange(event.target.value || null)}>
					<option value="">Select…</option>
					{field.type === 'bit'
						? <><option value="true">Yes</option><option value="false">No</option></>
						: <>{value && !options.some((row) => row.code === value) && <option value={value}>{value}</option>}
							{options.map((row) => <option key={row.code} value={row.code ?? ''}>{row.label}</option>)}</>}
				</select>
				: <Input id={id} aria-label={accessibleLabel} title={masterName ?? label}
					aria-invalid={Boolean(validationError)} aria-describedby={validationError ? `${id}-validation` : undefined}
					data-validation-path={validationPath}
					value={isDate && dateOnly ? (value ?? '').slice(0, 10) : value ?? ''}
					disabled={disabled || !field.writable} required={field.required} maxLength={field.length > 0 && isText ? field.length : undefined}
					inputMode={numeric ? integer ? 'numeric' : 'decimal' : undefined}
					onFocus={() => setFocused(true)} onBlur={() => setFocused(false)}
					className={`h-8 rounded border-slate-200 bg-white px-2 text-xs md:text-xs shadow-none disabled:bg-slate-50 ${numeric ? 'text-right tabular-nums' : ''}`}
					type={isDate ? dateOnly ? 'date' : 'datetime-local' : 'text'}
					step={isDate ? 'any' : undefined} list={field.lookup ? `${id}-choices` : undefined}
					onChange={(event) => {
						const next = event.target.value;
						setSearch(next);
						onChange(!next ? null : isDate && !dateOnly && next.length === 16 ? `${next}:00` : next);
					}} />}
			{field.lookup && !field.valueHelp && !fixedLookup && <datalist id={`${id}-choices`}>
				{options.map((row) => <option key={row.code} value={row.code ?? ''}>{row.label}</option>)}
			</datalist>}
			{field.lookup && !field.valueHelp && !fixedLookup && rowNumber === undefined && <p className="text-[10px] text-slate-400 truncate" title={masterName ?? undefined}>
				{masterName ?? (disabled ? 'ERP master code' : 'Search by name; choose a code.')}
			</p>}
			{validationError && <p id={`${id}-validation`} className="text-[10px] leading-tight text-red-700 max-w-48" title={validationError}>
				{rowNumber === undefined ? validationError : <>
					<span aria-hidden="true">Check this value - see validation details above.</span>
					<span className="sr-only">{validationError}</span>
				</>}
			</p>}
			{error && <p role="alert" className="text-[11px] text-red-700">{error}</p>}
		</div>
	);
}

export const OrderField = ErpField;
