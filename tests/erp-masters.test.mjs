import { test } from 'node:test';
import assert from 'node:assert/strict';
import { masterSavePayload } from '../lib/master-draft.ts';

test('master projection supports approved new fields without sending generated keys or actor details', () => {
	const form = {
		definition: { version: 'revision-2', fields: [{ key: 'Name', writable: true }, { key: 'Note', writable: true }] },
		record: { id: 'HO42', version: 'row-version', recordInfo: { createdBy: 'SA' } },
	};
	const result = masterSavePayload(form, { Name: 'Display description', Note: 'extra', Code: 'forged', U_Name: 'forged' }, 'command');
	assert.deepEqual(result, {
		metadataVersion: 'revision-2', version: 'row-version', idempotencyKey: 'command',
		values: { Name: 'Display description', Note: 'extra' },
	});
});

test('new-master values preserve explicit nulls and lossless decimals and have no record version', () => {
	const result = masterSavePayload({
		definition: { version: 'schema', fields: [{ key: 'Name', writable: true }, { key: 'Measure', writable: true }, { key: 'Note', writable: true }] },
		record: null,
	}, { Name: 'New master', Measure: '123456789.12345678', Note: null }, 'command');
	assert.equal(result.version, null);
	assert.deepEqual(result.values, { Name: 'New master', Measure: '123456789.12345678', Note: null });
});
