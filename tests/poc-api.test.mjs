import { test } from 'node:test';
import assert from 'node:assert/strict';
import { api, ApiError, fetchOrderPdf, loadOrderVoucherTypes, savePayload, orderSchema, fieldSchema, setApiContext } from '../lib/poc-api.ts';

const metadata = {
	version: 'schema',
	fields: [
		{ key: 'PartyCode', section: 'header', writable: true },
		{ key: 'NewField', section: 'header', writable: true },
		{ key: 'NewMeasure', section: 'lines', writable: true },
	],
};
const order = {
	id: 'persisted', version: 'version', metadataVersion: 'schema',
	header: { PartyCode: 'buyer', NewField: null, ApprovedBy: 'forged' },
	lines: [{ V_SNo: '1', NewMeasure: '123456789.123', Closed: 'Y' }],
	headerExpenses: [{ SNo: '1', PerEditable: 'N', AmtEditable: 'N', Amount: '100' }],
	lineExpenses: [[{ SNo: '2', PerEditable: 'Y', AmtEditable: 'N', Per: '10', Amount: '50' }]],
	terms: [{ Code: '1', Value: 'term', TableName: 'forged' }],
};

test('Sales Order PDF uses the selected context and rejects non-PDF responses', async t => {
	setApiContext('print-context');
	try {
		const responses = [
			new Response('%PDF-test', { headers: { 'Content-Type': 'application/pdf' } }),
			new Response('HTML error', { headers: { 'Content-Type': 'text/html' } }),
		];
		const fetch = t.mock.method(globalThis, 'fetch', async () => responses.shift());
		assert.equal(await (await fetchOrderPdf('SO 123')).text(), '%PDF-test');
		const [path, init] = fetch.mock.calls[0].arguments;
		assert.equal(path, '/api/poc/orders/SO%20123/print.pdf');
		assert.equal(init.headers.get('X-ERP-Context'), 'print-context');
		await assert.rejects(fetchOrderPdf('SO 123'), /did not return a PDF/);
	} finally {
		setApiContext(null);
	}
});

test('a missing voucher endpoint does not reject the new-order loader, but server errors still do', async (t) => {
	const requests = [
		Response.json({ title: 'order_not_found', detail: 'Order not found in the selected context.' }, { status: 404 }),
		Response.json([{ code: 'SORD', label: 'Sales Order', canSave: true }]),
		Response.json({ detail: 'Voucher lookup failed.' }, { status: 500 }),
	];
	const fetch = t.mock.method(globalThis, 'fetch', async () => requests.shift());
	assert.equal(await loadOrderVoucherTypes(), null);
	assert.deepEqual(await loadOrderVoucherTypes(), [{ code: 'SORD', label: 'Sales Order', canSave: true }]);
	await assert.rejects(loadOrderVoucherTypes(), { status: 500, message: 'Voucher lookup failed.' });
	assert.equal(fetch.mock.calls[0].arguments[0], '/api/poc/orders/voucher-types');
});

test('one metadata projection handles new fields and preserves explicit null / decimal strings', () => {
	const payload = savePayload(order, metadata, 'request-key');
	assert.equal(payload.header.NewField, null);
	assert.equal(payload.lines[0].NewMeasure, '123456789.123');
	assert.equal(payload.lines[0].V_SNo, '1');
	assert.equal('ApprovedBy' in payload.header, false);
	assert.equal('Closed' in payload.lines[0], false);
});

test('only editable expense values and term values cross the command boundary', () => {
	const payload = savePayload(order, metadata, 'request-key');
	assert.deepEqual(payload.headerExpenses, [{ SNo: '1' }]);
	assert.deepEqual(payload.lineExpenses, [[{ SNo: '2', Per: '10' }]]);
	assert.deepEqual(payload.terms, [{ Code: '1', Value: 'term' }]);
});

test('lookup terms send the selected key without the description or configuration', () => {
	const terms = [{ Code: '45', LookupId: 'sales-order-distance', Value: 'Distance description', ValueCode: 'D10' },
		{ Code: '1', Value: 'Freight prepaid' }, { Code: '46', LookupId: 'another-lookup', Value: 'Unkeyed' }];
	assert.deepEqual(savePayload({ ...order, terms }, metadata, 'request-key').terms,
		[{ Code: '45', ValueCode: 'D10' }, { Code: '1', Value: 'Freight prepaid' }, { Code: '46', ValueCode: null }]);
});

test('preview-generated new-line identities never become persisted identities', () => {
	const payload = savePayload({ ...order, id: null }, metadata, 'request-key');
	assert.equal('V_SNo' in payload.lines[0], false);
});

test('automatic numbering is explicit and protected voucher numbers never become command fields', () => {
	const automatic = savePayload({ ...order, id: null, header: { ...order.header, V_No: '176' } }, metadata, 'request-key', true);
	assert.equal(automatic.automaticReference, true);
	assert.equal('V_No' in automatic.header, false);
	assert.equal(savePayload(order, metadata, 'request-key').automaticReference, false);
});

test('record actor details are response-only and never enter a save command', () => {
	const recordInfo = { createdBy: 'CREATOR', modifiedBy: 'EDITOR', approvedBy: 'APPROVER', changeBlockedReason: null };
	const response = orderSchema.parse({ ...order, locked: true, lockReason: 'Approved', recordInfo });
	assert.equal(response.recordInfo.approvedBy, 'APPROVER');
	assert.equal('recordInfo' in savePayload(response, metadata, 'request-key'), false);
});

test('API errors retain structured field locations and the diagnostic reference', async t => {
	t.mock.method(globalThis, 'fetch', async () => new Response(JSON.stringify({
		detail: 'Invalid conversion', traceId: 'trace', errors: { 'lines[1].QtySDU': ['Review conversion'] },
	}), { status: 422, headers: { 'Content-Type': 'application/json' } }));
	await assert.rejects(api('/orders/defaults', orderSchema), error => {
		assert.ok(error instanceof ApiError);
		assert.equal(error.traceId, 'trace');
		assert.deepEqual(error.errors, { 'lines[1].QtySDU': ['Review conversion'] });
		return true;
	});

});

test('value-help metadata does not change the persisted foreign-key payload', () => {
	const field = fieldSchema.parse({ key: 'PartyCode', section: 'header', label: 'Buyer', type: 'varchar',
		required: true, length: 10, precision: 0, scale: 0, lookup: 'parties', writable: true, valueHelp: 'parties' });
	const payload = savePayload({ ...order, header: { PartyCode: 'HO123' } }, { ...metadata, fields: [field] }, 'request-key');
	assert.deepEqual(payload.header, { PartyCode: 'HO123' });
	assert.equal('valueHelp' in payload.header, false);
	assert.throws(() => orderSchema.parse({ ...order, header: { PartyCode: { key: 'HO123', label: 'Party name' } },
		locked: false, lockReason: null }));
});

test('explicit lookup context is retained across queued resolution requests', async t => {
	let context;
	setApiContext('new-context');
	t.mock.method(globalThis, 'fetch', async (_url, init) => {
		context = new Headers(init.headers).get('X-ERP-Context');
		return new Response(JSON.stringify({ ...order, locked: false, lockReason: null }),
			{ headers: { 'Content-Type': 'application/json' } });
	});
	try {
		await api('/orders/defaults', orderSchema, { headers: { 'X-ERP-Context': 'captured-context' } });
		assert.equal(context, 'captured-context');
	} finally { setApiContext(null); }
});
