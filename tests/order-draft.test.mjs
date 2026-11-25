import { test } from 'node:test';
import assert from 'node:assert/strict';
import { orderDraft, orderPayload, applyItemExpenses, applyItemExpenseRatesToAll } from '../lib/order-draft.ts';
import { menuTree, filterMenu, menuCreateHref } from '../lib/navigation.ts';
import { editEntryRow, ensureEntryRow } from '../lib/entry-rows.ts';
import { bindFieldErrors, clearFieldError } from '../lib/field-validation.ts';
import { isVisibleOrderField, summarizeOrderItems } from '../lib/order-item-summary.ts';

test('item totals follow Windows running two-place rounding and ignore untouched entry rows', () => {
	const items = [
		{ key: 'one', values: { Reel: '2', Qty: '0.105' }, expenses: [] },
		{ key: 'two', values: { Reel: '3', Qty: '0.105' }, expenses: [] },
		{ key: 'entry', values: { Reel: '100', Qty: '100' }, expenses: [], entryOnly: true },
	];
	assert.deepEqual(summarizeOrderItems(items), { quantity: '5.00', weight: '0.22' });
	assert.deepEqual(summarizeOrderItems(items.slice(1)), { quantity: '3.00', weight: '0.11' });
	assert.deepEqual(summarizeOrderItems([]), { quantity: '0.00', weight: '0.00' });
	assert.equal(items[0].values.Qty, '0.105');
});

test('quantity totals preserve decimal precision, support SQL scientific values and expose invalid edits', () => {
	const item = values => ({ key: 'one', values, expenses: [] });
	assert.deepEqual(summarizeOrderItems([
		item({ Reel: '9007199254740993.10', Qty: '1e-2' }),
		item({ Reel: '.05', Qty: '1E-3' }),
	]), { quantity: '9007199254740993.15', weight: '0.01' });
	for (const value of ['-', '-1', 'bad', '1e99999']) {
		assert.deepEqual(summarizeOrderItems([item({ Reel: value, Qty: '2' })]),
			{ quantity: null, weight: '2.00' });
	}
	assert.deepEqual(summarizeOrderItems([item({ Reel: null, Qty: '' })]), { quantity: '0.00', weight: '0.00' });
});

test('hiding web demo fields affects only Sales rendering, never the saved payload', () => {
	const fields = [
		{ section: 'header', key: 'WebPocReference' },
		{ section: 'lines', key: 'WebPocMeasure' },
		{ section: 'header', key: 'PartyOrdNo' },
		{ section: 'lines', key: 'Qty' },
		{ section: 'master', key: 'WebPocReference' },
	];
	assert.deepEqual(fields.filter(isVisibleOrderField).map(field => field.key), ['PartyOrdNo', 'Qty', 'WebPocReference']);
	const draft = fixture();
	draft.header.WebPocReference = 'KEEP';
	draft.items[0].values.WebPocMeasure = '12.345';
	const payload = orderPayload(draft);
	assert.equal(payload.header.WebPocReference, 'KEEP');
	assert.equal(payload.lines[0].WebPocMeasure, '12.345');
});

function fixture() {
	return orderDraft({ id: null, version: null, metadataVersion: 'schema', header: {},
		lines: [{ ItemCode: 'ONE' }, { ItemCode: 'TWO' }],
		lineExpenses: [[{ SNo: '1', Per: '5' }], [{ SNo: '1', Per: '12' }]],
		headerExpenses: [], terms: [], locked: false, lockReason: null });
}

test('applying rates to all matches expense codes and keeps each other line’s amounts', () => {
	const source = { key: 'source', values: {}, expenses: [] };
	const receiver = { key: 'receiver', values: {}, expenses: [
		{ SNo: '4', ExpCode: 'Disc ', PerEditable: 'Y', Per: '3', Amount: '15' },
		{ SNo: '2', ExpCode: 'GST', PerEditable: 'N', Per: '7', Amount: '12' },
		{ SNo: '3', ExpCode: 'CESS', PerEditable: 'Y', Per: '4', Amount: '9' },
	] };
	const blank = { ...receiver, key: 'blank', entryOnly: true };
	const draft = { items: [source, receiver, blank] };
	const rows = [{ SNo: '2', ExpCode: 'DISC', Per: '0', Amount: '20' }, { SNo: '3', ExpCode: 'GST', Per: '10' }];
	const applied = applyItemExpenseRatesToAll(draft, 'source', rows);
	assert.deepEqual(applied.items[0].expenses, rows);
	assert.deepEqual(applied.items[1].expenses.map(({ Per, Amount }) => [Per, Amount]), [['0', '15'], ['7', '12'], ['4', '9']]);
	assert.equal(applied.items[2], blank);
	assert.equal(receiver.expenses[0].Per, '3');
});

test('expense drafts cannot change another item or mutate cancelled input', () => {
	const order = fixture();
	const draft = order.items[0].expenses.map(row => ({ ...row }));
	draft[0].Per = '18.125';
	assert.equal(order.items[0].expenses[0].Per, '5');
	const applied = applyItemExpenses(order, order.items[0].key, draft);
	assert.equal(applied.items[0].expenses[0].Per, '18.125');
	assert.equal(applied.items[1].expenses[0].Per, '12');
});

test('removing an earlier row keeps the remaining item and its expenses together', () => {
	const order = fixture();
	const remaining = { ...order, items: order.items.slice(1) };
	const payload = orderPayload(remaining);
	assert.equal(payload.lines[0].ItemCode, 'TWO');
	assert.equal(payload.lineExpenses[0][0].Per, '12');
	assert.equal('key' in payload.lines[0], false);
	assert.throws(() => applyItemExpenses(remaining, order.items[0].key, []), /no longer exists/);
});

test('invalid API line/expense cardinality is not silently repaired', () => {
	const payload = orderPayload(fixture());
	assert.throws(() => orderDraft({ ...payload, lineExpenses: [] }), /mismatched/);
});

test('menu filtering preserves a matching leaf and its ancestors', () => {
	const items = [
		{ key: 'root', label: 'Order', parentKey: null, order: 1 },
		{ key: 'sale', label: 'Sale Order', parentKey: 'root', order: 2 },
		{ key: 'work', label: 'Work Order', parentKey: 'root', order: 3 },
	];
	const filtered = filterMenu(menuTree(items), 'sale');
	assert.equal(filtered[0].key, 'root');
	assert.deepEqual(filtered[0].children.map(node => node.key), ['sale']);
	assert.throws(() => menuTree([{ ...items[1], parentKey: 'absent' }]), /parent is missing/);
});

test('editing the trailing entry creates exactly one successor and never mutates another row', () => {
	let serial = 0;
	const create = () => ({ key: `${++serial}`, values: { Rate: '0' }, expenses: [{ SNo: '1', Per: '0' }] });
	let rows = ensureEntryRow([], create, 100);
	assert.equal(rows.length, 1);
	assert.equal(rows[0].entryOnly, true);
	const firstKey = rows[0].key;
	rows = editEntryRow(rows, firstKey, row => ({ ...row, values: { Rate: '2.125' } }), create, 100);
	const entryKey = rows[1].key;
	rows = editEntryRow(rows, firstKey, row => ({ ...row, values: { Rate: '3' } }), create, 100);
	assert.equal(rows.length, 2);
	assert.equal(rows[1].key, entryKey);
	assert.equal(rows.filter(row => row.entryOnly).length, 1);
	assert.equal(rows[1].values.Rate, '0');
	assert.throws(() => editEntryRow(rows, 'missing', row => row, create, 100), /no longer exists/);
});

test('untouched entries are excluded but partially edited items and their expenses remain in the payload', () => {
	const order = fixture();
	const entry = { key: 'entry', values: { ItemCode: null }, expenses: [{ SNo: '1', Per: '0' }], entryOnly: true };
	order.items.push(entry);
	assert.equal(orderPayload(order).lines.length, 2);
	const edited = applyItemExpenses(order, entry.key, [{ SNo: '1', Per: '18.125' }]);
	assert.equal(orderPayload(edited).lines.length, 3);
	assert.equal(orderPayload(edited).lineExpenses[2][0].Per, '18.125');
	assert.equal(orderPayload(edited).lines[2].ItemCode, null);
});

test('entry rows survive removal without moving expenses and stop at the document line limit', () => {
	const create = () => ({ key: 'blank', entryOnly: true });
	const full = [{ key: 'one' }, { key: 'two' }, { key: 'entry', entryOnly: true }];
	assert.deepEqual(ensureEntryRow(full, create, 2).map(row => row.key), ['one', 'two']);
	assert.deepEqual(ensureEntryRow(full.slice(1), create, 2).map(row => row.key), ['two', 'entry']);
});

test('server validation indices bind to stable active item keys, not untouched entry rows', () => {
	const errors = bindFieldErrors({
		'header.PartyOrdNo': ['Required'],
		'lines[1].ItemCode': ['Invalid conversion'],
		'lines[1].QtySDU': ['Invalid conversion'],
		'lineExpenses[1][0].Per': ['Enter a decimal'],
	}, ['first', 'second']);
	assert.equal(errors['items[second].ItemCode'], 'Invalid conversion');
	assert.equal(errors['items[second].expenses[0].Per'], 'Enter a decimal');
	const edited = clearFieldError(errors, 'items[second].ItemCode');
	assert.equal(edited['items[second].ItemCode'], undefined);
	assert.equal(edited['items[second].QtySDU'], 'Invalid conversion');
	assert.equal(errors['items[second].ItemCode'], 'Invalid conversion');
	assert.throws(() => bindFieldErrors({ 'lines[9].Qty': ['Unknown row'] }, ['first']), /unknown validation row/);
});

test('menu quick-add requires Add permission and an implemented transaction route', () => {
	const sale = { implemented: true, href: '/dashboard/sale-order' };
	assert.equal(menuCreateHref(sale, true), '/dashboard/sale-order/new');
	assert.equal(menuCreateHref(sale, false), null);
	assert.equal(menuCreateHref({ ...sale, implemented: false }, true), null);
	assert.equal(menuCreateHref({ ...sale, href: '/dashboard/modules/Purchase' }, true), null);
});
