import type { DataRow, Order } from './poc-api';

export type ItemDraft = { key: string; values: DataRow; expenses: DataRow[]; entryOnly?: boolean };
export type OrderDraft = Omit<Order, 'lines' | 'lineExpenses'> & { items: ItemDraft[] };

export function itemDraft(values: DataRow, expenses: DataRow[]): ItemDraft {
	return { key: crypto.randomUUID(), values: { ...values }, expenses: expenses.map((row) => ({ ...row })) };
}

export function orderDraft(order: Order): OrderDraft {
	if (order.lines.length !== order.lineExpenses.length) throw new Error('The order has mismatched line expenses. Reload or contact the administrator.');
	const { lines, lineExpenses, ...rest } = order;
	return { ...rest, items: lines.map((values, index) => itemDraft(values, lineExpenses[index])) };
}

export function orderPayload(draft: OrderDraft): Order {
	const { items, ...rest } = draft;
	const active = items.filter((item) => !item.entryOnly);
	return { ...rest, lines: active.map((item) => item.values), lineExpenses: active.map((item) => item.expenses) };
}

export function applyItemExpenses(draft: OrderDraft, key: string, expenses: DataRow[]): OrderDraft {
	if (!draft.items.some((item) => item.key === key)) throw new Error('This item row no longer exists. Reopen its expenses.');
	return { ...draft, items: draft.items.map((item) => item.key === key
		? { ...item, entryOnly: false, expenses: expenses.map((row) => ({ ...row })) } : item) };
}

export function applyItemExpenseRatesToAll(draft: OrderDraft, key: string, expenses: DataRow[]): OrderDraft {
	const selected = applyItemExpenses(draft, key, expenses);
	const expenseCode = (row: DataRow) => row.ExpCode?.trim().toUpperCase();
	const rates = new Map(expenses.flatMap(row => {
		const code = expenseCode(row);
		return code && row.Per != null ? [[code, row.Per] as const] : [];
	}));
	return { ...selected, items: selected.items.map(item => item.key === key || item.entryOnly ? item : {
		...item, expenses: item.expenses.map(row => {
			const code = expenseCode(row);
			const rate = code ? rates.get(code) : undefined;
			return rate !== undefined && row.PerEditable === 'Y' ? { ...row, Per: rate } : row;
		}),
	}) };
}
