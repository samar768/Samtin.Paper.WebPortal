import { test } from 'node:test';
import assert from 'node:assert/strict';
import { newSalesOrderHref, salesOrdersPath, salesOrderView } from '../lib/sales-workspace-route.ts';

function view(href) {
	const url = new URL(href, 'http://localhost');
	return salesOrderView(url.pathname, url.searchParams);
}

test('new and copied orders get independent identities, including repeated copies of one saved order', () => {
	const drafts = [newSalesOrderHref(), newSalesOrderHref(), newSalesOrderHref('SO 123'), newSalesOrderHref('SO 123')].map(view);
	assert.ok(drafts.every(draft => draft.kind === 'editor' && draft.id === undefined));
	assert.equal(new Set(drafts.map(draft => draft.key)).size, 4);
	assert.equal(drafts[3].copyFrom, 'SO 123');
	assert.equal(view(drafts[3].href).key, drafts[3].key);
});

test('saved order identity survives link encoding and unrelated query parameters', () => {
	const record = view(`${salesOrdersPath}/SO%20%20123`);
	assert.equal(record.id, 'SO  123');
	assert.equal(record.key, view(`${record.href}?ignored=1`).key);
	assert.notEqual(record.key, view(newSalesOrderHref(record.id)).key);
	assert.equal(view(`${salesOrdersPath}/new?copyFrom=A&copyFrom=B`).kind, 'invalid');
});
