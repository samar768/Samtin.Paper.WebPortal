import { test } from 'node:test';
import assert from 'node:assert/strict';
import { scheduleCalculation } from '../lib/debounced-calculation.ts';
import { applyCalculation, calculationSignature, itemExpenseCalculationSignature, retainCalculatedExpenseAmounts } from '../lib/order-calculation.ts';

function fixture() {
	return { id: null, version: null, metadataVersion: 'schema', header: { PartyOrdNo: 'KEEP' }, headerExpenses: [],
		items: [{ key: 'one', values: { Qty: '10', Rate: '2', ItemCode: 'ONE' }, expenses: [] },
			{ key: 'entry', values: { Qty: '0' }, expenses: [], entryOnly: true }],
		terms: [], locked: false, lockReason: null };
}
const metadata = { fields: ['Qty', 'Rate', 'ItemCode'].map(key => ({ key, section: 'lines', writable: true })) };
const result = { header: { GAmount: '20' }, headerExpenses: [], lines: [{ Qty: '10', Amount: '20' }], lineExpenses: [[]] };

test('tax dialog previews the selected entry without committing its changes to the draft', () => {
	const draft = fixture();
	const expenses = [{ SNo: '3', PerEditable: 'Y', Per: '12' }];
	const preview = JSON.parse(itemExpenseCalculationSignature(draft, metadata, 'entry', expenses));
	assert.deepEqual(preview.keys, ['one', 'entry']);
	assert.deepEqual(preview.request.lineExpenses, [[], [{ SNo: '3', Per: '12' }]]);
	assert.equal(draft.items[1].entryOnly, true);
	assert.deepEqual(draft.items[1].expenses, []);
	assert.equal(itemExpenseCalculationSignature(draft, metadata, 'removed', expenses), null);
});

test('clearing a percentage retains the last calculated amount without overriding a manual amount edit', () => {
	const row = { SNo: '3', Per: '10', Amount: '0', AmtEditable: 'Y' };
	const calculated = [{ ...row, Amount: '2' }];
	const cleared = retainCalculatedExpenseAmounts([row], [{ ...row, Per: '0' }], calculated);
	assert.equal(cleared[0].Amount, '2');
	const manual = retainCalculatedExpenseAmounts([row], [{ ...row, Amount: '3' }], calculated);
	assert.equal(manual[0].Amount, '3');
});

test('existing draft calculations send persisted line identities, not new line placeholders', () => {
	const draft = fixture();
	draft.id = 'existing';
	draft.items[0].values.V_SNo = '3';
	draft.items.push({ key: 'new', values: { Qty: '2', V_SNo: '0' }, expenses: [] });
	const { request } = JSON.parse(calculationSignature(draft, metadata));
	assert.equal(request.documentId, 'existing');
	assert.equal(request.lines[0].V_SNo, '3');
	assert.equal(Object.hasOwn(request.lines[1], 'V_SNo'), false);
});

test('switching a new draft voucher changes the server calculation request', () => {
	const draft = fixture();
	draft.header.V_Type = 'SORD';
	const prior = calculationSignature(draft, metadata);
	draft.header.V_Type = 'SODRL';
	const selected = calculationSignature(draft, metadata);
	assert.notEqual(selected, prior);
	assert.equal(JSON.parse(selected).request.voucherType, 'SODRL');
	assert.equal(applyCalculation(draft, metadata, prior, result), draft);
});

test('calculated merge preserves stable rows, focus keys, unrelated values and untouched entries', () => {
	const order = fixture();
	const calculated = applyCalculation(order, metadata, calculationSignature(order, metadata), result);
	assert.equal(calculated.header.PartyOrdNo, 'KEEP');
	assert.equal(calculated.items[0].key, 'one');
	assert.equal(calculated.items[0].values.ItemCode, 'ONE');
	assert.equal(calculated.items[0].values.Amount, '20');
	assert.equal(calculated.items[1], order.items[1]);
	assert.equal(order.items[0].values.Amount, undefined);
	assert.throws(() => applyCalculation(order, metadata, calculationSignature(order, metadata), { ...result, lines: [] }), /do not match/);
});

test('newer edits and row removal reject an older calculated response', () => {
	const order = fixture();
	const signature = calculationSignature(order, metadata);
	const edited = { ...order, items: order.items.map(item => item.key === 'one' ?
		{ ...item, values: { ...item.values, Rate: '3.125' } } : item) };
	assert.equal(applyCalculation(edited, metadata, signature, result), edited);
	const removed = { ...order, items: order.items.slice(1) };
	assert.equal(applyCalculation(removed, metadata, signature, result), removed);
});

test('cancelled requests cannot deliver results even if the transport ignores abort', async () => {
	let complete;
	let starts = 0;
	const received = [];
	const cancel = scheduleCalculation('old', () => {
		starts++; return new Promise(resolve => { complete = resolve; });
	}, value => received.push(value), error => { throw error; }, 0);
	await new Promise(resolve => setTimeout(resolve, 10));
	assert.equal(starts, 1);
	cancel();
	complete('old response');
	await new Promise(resolve => setTimeout(resolve, 10));
	assert.deepEqual(received, []);
	const cancelledBeforeStart = scheduleCalculation('new', async () => { starts++; },
		value => received.push(value), error => { throw error; }, 20);
	cancelledBeforeStart();
	await new Promise(resolve => setTimeout(resolve, 30));
	assert.equal(starts, 1);
});

test('calculation failures are surfaced instead of treated as calculated values', async () => {
	const errors = [];
	scheduleCalculation('bad', async () => { throw new Error('invalid number'); },
		() => assert.fail('failed calculation must not succeed'), (error, signature) => errors.push([error.message, signature]), 0);
	await new Promise(resolve => setTimeout(resolve, 10));
	assert.deepEqual(errors, [['invalid number', 'bad']]);
});
