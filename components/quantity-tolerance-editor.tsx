'use client';

import { useCallback, useState, type Ref } from 'react';
import { z } from 'zod';
import { ApiError, errorMessage } from '@/lib/poc-api';
import { useDebouncedCalculation } from '@/lib/use-debounced-calculation';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';

export const toleranceSchema = z.object({ excess: z.string(), shortage: z.string(), upperQuantity: z.string(), lowerQuantity: z.string() });
type Tolerance = z.infer<typeof toleranceSchema>;
type Entry = { value: string; percent: string | null };

export function QuantityToleranceEditor({ quantity, excess, shortage, rowNumber, disabled, calculate, contentRef, paths, onApply, onClose, onDirty, active = true }: {
	quantity: string; excess: string; shortage: string; rowNumber: number; disabled: boolean;
	calculate: (body: string, signal: AbortSignal) => Promise<Tolerance>;
	contentRef?: Ref<HTMLDivElement>; paths?: { excess: string; shortage: string }; active?: boolean;
	onApply: (excess: string, shortage: string) => void; onClose: () => void; onDirty: () => void;
}) {
	const [excessEntry, setExcess] = useState<Entry>({ value: excess, percent: null });
	const [shortageEntry, setShortage] = useState<Entry>({ value: shortage, percent: null });
	const [calculated, setCalculated] = useState<{ signature: string; value: Tolerance } | null>(null);
	const [failure, setFailure] = useState<{ signature: string; message: string; errors: Record<string, string[]> } | null>(null);
	const signature = JSON.stringify({ quantity, excess: excessEntry.value, shortage: shortageEntry.value,
		excessPercent: excessEntry.percent, shortagePercent: shortageEntry.percent });
	const success = useCallback((value: Tolerance, signature: string) => {
		setCalculated({ signature, value }); setFailure(null);
	}, []);
	const fail = useCallback((error: unknown, signature: string) =>
		setFailure({ signature, message: errorMessage(error), errors: error instanceof ApiError ? error.errors : {} }), []);
	useDebouncedCalculation(active ? signature : null, calculate, success, fail);
	const result = calculated?.signature === signature ? calculated.value : null;
	const error = failure?.signature === signature ? failure : null;
	const entries = [
		{ label: 'Excess', entry: excessEntry, set: setExcess, quantity: result?.excess, net: result?.upperQuantity, path: paths?.excess },
		{ label: 'Shortage', entry: shortageEntry, set: setShortage, quantity: result?.shortage, net: result?.lowerQuantity, path: paths?.shortage },
	];
	return <Dialog open={active} onOpenChange={(open) => { if (!open) onClose(); }}>
		<DialogContent ref={contentRef} className="sm:max-w-lg">
			<DialogHeader><DialogTitle>Quantity tolerance · Row {rowNumber}</DialogTitle>
				<DialogDescription>Enter a percentage or quantity. Apply changes only this row; saving the document commits them.</DialogDescription></DialogHeader>
			<div className="flex items-center justify-between rounded-md bg-slate-50 px-3 py-2 text-xs">
				<span>Order quantity</span><strong className="tabular-nums">{quantity}</strong>
			</div>
			<div className="overflow-auto">
				<table className="w-full text-xs"><thead className="text-slate-500 text-right">
					<tr><th className="text-left py-2">Tolerance</th><th className="px-1">Percent</th><th className="px-1">Quantity</th><th className="px-1">Net quantity</th></tr>
				</thead><tbody>{entries.map(({ label, entry, set, quantity: calculatedQuantity, net, path }) => <tr key={label}>
					<th className="py-2 text-left font-medium">{label}</th>
					<td className="p-1"><Input aria-label={`${label} percent`} inputMode="decimal" className="h-8 min-w-16 w-20 text-right text-xs"
						disabled={disabled} value={entry.percent ?? ''}
						aria-invalid={Boolean(error?.errors[`tolerance.${label}Percent`])}
						onChange={(event) => { set({ ...entry, percent: event.target.value }); onDirty(); }} /></td>
					<td className="p-1"><Input aria-label={`${label} quantity`} inputMode="decimal" className="h-8 min-w-20 w-24 text-right text-xs"
						data-validation-path={path}
						disabled={disabled} value={entry.percent === null ? entry.value : calculatedQuantity ?? ''}
						aria-invalid={Boolean(error?.errors[`tolerance.${label}`])}
						onChange={(event) => { set({ value: event.target.value, percent: null }); onDirty(); }} /></td>
					<td className="px-1 text-right tabular-nums">{net ?? '—'}</td>
				</tr>)}</tbody></table>
			</div>
			{error ? <p role="alert" className="rounded border border-red-200 bg-red-50 p-2 text-xs text-red-700">{error.message}</p> :
				<p role="status" className="text-xs text-slate-500">{result ? 'Limits calculated. Percentage quantities are rounded to 3 decimal places.' : 'Calculating limits…'}</p>}
			<DialogFooter><Button variant="outline" onClick={onClose}>{disabled ? 'Close' : 'Cancel'}</Button>
				{!disabled && <Button disabled={!result || Boolean(error)} onClick={() => {
					if (result) onApply(result.excess, result.shortage);
				}}>Apply to row</Button>}</DialogFooter>
		</DialogContent>
	</Dialog>;
}
