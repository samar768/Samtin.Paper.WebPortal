import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createListingPreferencesSchema, defaultListingPreferences, moveListingColumn, visibleListingColumns } from '../lib/listing-preferences.ts';

const columns = [
	{ id: 'reference', label: 'Reference', width: 140 },
	{ id: 'date', label: 'Date', width: 130 },
	{ id: 'buyer', label: 'Buyer', width: 300 },
	{ id: 'gross', label: 'Gross amount', width: 160 },
	{ id: 'state', label: 'State', width: 130 },
];
const definition = { key: 'sales-orders', columns, sortColumn: 'date' };
const schema = createListingPreferencesSchema(columns);

test('default listing preferences preserve initial columns, density, sort and paging', () => {
	const defaults = defaultListingPreferences(definition);
	assert.equal(defaults.pageSize, 25);
	assert.equal(defaults.density, 'compact');
	assert.equal(defaults.sortDirection, 'desc');
	assert.equal(defaults.sortColumn, 'date');
	assert.equal(schema.safeParse(defaults).success, true);
	assert.notEqual(defaults.columns, defaultListingPreferences(definition).columns);
});

test('column reorder and visibility are immutable and keep widths attached to identifiers', () => {
	const defaults = defaultListingPreferences(definition);
	const reordered = moveListingColumn(defaults, 'buyer', -1);
	assert.deepEqual(defaults.columns.map(c => c.id), ['reference', 'date', 'buyer', 'gross', 'state']);
	assert.deepEqual(reordered.columns.map(c => c.id), ['reference', 'buyer', 'date', 'gross', 'state']);
	assert.equal(reordered.columns[1].width, 300);
	const customized = { ...reordered, columns: reordered.columns.map(c => ({ ...c, visible: c.id !== 'date' })) };
	assert.deepEqual(visibleListingColumns(customized).map(c => c.id), ['reference', 'buyer', 'gross', 'state']);
	assert.equal(schema.safeParse(customized).success, true);
});

test('invalid saved settings are explicit failures, not silent default layouts', () => {
	const defaults = defaultListingPreferences(definition);
	for (const settings of [
		{ ...defaults, schemaVersion: 2 },
		{ ...defaults, columns: [] },
		{ ...defaults, columns: defaults.columns.map(() => defaults.columns[0]) },
		{ ...defaults, columns: defaults.columns.map(c => ({ ...c, visible: false })) },
		{ ...defaults, columns: defaults.columns.map(c => ({ ...c, width: 900 })) },
		{ ...defaults, sortColumn: 'secret' },
		{ ...defaults, pageSize: 10000 },
		{ ...defaults, density: 'unknown' },
	]) assert.equal(schema.safeParse(settings).success, false);
});
