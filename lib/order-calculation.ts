import type { DataRow, OrderMetadata } from './poc-api';
import type { OrderDraft } from './order-draft';

export type DraftCalculation = { header: DataRow; lines: DataRow[]; headerExpenses: DataRow[]; lineExpenses: DataRow[][] };

export function calculationSignature(draft: OrderDraft, metadata: OrderMetadata): string {
	const project = (row: DataRow, section: string) => Object.fromEntries(metadata.fields
		.filter(field => field.section === section && field.writable && field.key in row)
		.map(field => [field.key, row[field.key]]));
	const expenses = (rows: DataRow[]) => rows.map(row => ({
		SNo: row.SNo,
		...(row.PerEditable === 'Y' ? { Per: row.Per } : {}),
		...(row.AmtEditable === 'Y' ? { Amount: row.Amount } : {}),
	}));
	const items = draft.items.filter(item => !item.entryOnly);
	return JSON.stringify({ keys: items.map(item => item.key), request: {
		metadataVersion: draft.metadataVersion, ...(draft.id ? { documentId: draft.id } : {}),
		...(draft.header.V_Type ? { voucherType: draft.header.V_Type } : {}),
		lines: items.map(item => ({
			...project(item.values, 'lines'),
			...(draft.id && item.values.V_SNo && item.values.V_SNo !== '0' ? { V_SNo: item.values.V_SNo } : {}),
		})),
		headerExpenses: expenses(draft.headerExpenses), lineExpenses: items.map(item => expenses(item.expenses)),
	} });
}

export function itemExpenseCalculationSignature(draft: OrderDraft, metadata: OrderMetadata, key: string, expenses: DataRow[]): string | null {
	if (!draft.items.some(item => item.key === key)) return null;
	return calculationSignature({ ...draft, items: draft.items.map(item => item.key === key
		? { ...item, entryOnly: false, expenses } : item) }, metadata);
}

export function retainCalculatedExpenseAmounts(previous: DataRow[], edited: DataRow[], calculated?: DataRow[]): DataRow[] {
	if (!calculated) return edited;
	return edited.map(row => {
		const old = previous.find(candidate => candidate.SNo === row.SNo);
		const amount = calculated.find(candidate => candidate.SNo === row.SNo)?.Amount;
		return old && old.Per !== row.Per && old.Amount === row.Amount && row.AmtEditable === 'Y' && amount != null
			? { ...row, Amount: amount } : row;
	});
}

export function applyCalculation(draft: OrderDraft, metadata: OrderMetadata, signature: string, result: DraftCalculation): OrderDraft {
	if (calculationSignature(draft, metadata) !== signature) return draft;
	const active = draft.items.filter(item => !item.entryOnly);
	if (result.lines.length !== active.length || result.lineExpenses.length !== active.length)
		throw new Error('Calculated items and expenses do not match the draft. Reload or contact the administrator.');
	const positions = new Map(active.map((item, index) => [item.key, index]));
	return { ...draft, header: { ...draft.header, ...result.header }, headerExpenses: result.headerExpenses,
		items: draft.items.map(item => {
			const index = positions.get(item.key);
			return index === undefined ? item : { ...item, values: { ...item.values, ...result.lines[index] },
				expenses: result.lineExpenses[index] };
		}) };
}
