import test from 'node:test';
import assert from 'node:assert/strict';
import { applyLegacySelection, parseLegacySelection, serializeLegacySelection, updateLegacySelection } from '../lib/legacy-selections.ts';

test('Windows pipe-wrapped codes round-trip with old comma spacing, not descriptions', () => {
	assert.deepEqual(parseLegacySelection('|HO1|, |HO2|').keys, ['HO1', 'HO2']);
	assert.equal(serializeLegacySelection(['HO1', 'HO2']), '|HO1|,|HO2|');
	assert.deepEqual(parseLegacySelection(null), { keys: [] });
	assert.equal(serializeLegacySelection([]), '');
});

test('invalid, duplicate, oversized or delimiter-containing selections are explicit failures', () => {
	assert.ok(parseLegacySelection('Accounts, Names').error);
	assert.ok(parseLegacySelection('|HO1|,|HO1|').error);
	assert.throws(() => serializeLegacySelection(['HO1', 'HO1']));
	assert.throws(() => serializeLegacySelection(['HO|1']));
	assert.throws(() => serializeLegacySelection(Array.from({ length: 101 }, (_, i) => String(i))));
});

test('checking and clearing a results page preserves other pages and never mutates the opening selection', () => {
	const original = parseLegacySelection('|OFFPAGE|, |B|').keys;
	const checked = updateLegacySelection(original, ['A', 'B', 'C'], true);
	assert.deepEqual(checked, ['OFFPAGE', 'B', 'A', 'C']);
	assert.deepEqual(updateLegacySelection(checked, ['A', 'B', 'C'], false), ['OFFPAGE']);
	assert.deepEqual(original, ['OFFPAGE', 'B']);
});

test('apply stores just pipe-wrapped codes and preserves the exact original for a no-op', () => {
	const original = '|OFFPAGE|, |B|';
	assert.equal(applyLegacySelection(original, ['B', 'OFFPAGE']), original);
	assert.equal(applyLegacySelection(original, ['OFFPAGE', 'A']), '|OFFPAGE|,|A|');
	assert.equal(applyLegacySelection(null, []), null);
	assert.equal(applyLegacySelection(original, []), '');
	assert.equal(applyLegacySelection('bad legacy value', ['A']), '|A|');
	assert.throws(() => applyLegacySelection(original, ['B', 'B']));
});
