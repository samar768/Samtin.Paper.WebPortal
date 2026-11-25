import test from 'node:test';
import assert from 'node:assert/strict';
import { masterAuditChanges } from '../lib/master-audit.ts';

test('audit changes show renamed, added and removed values without unchanged fields', () => {
	assert.deepEqual(masterAuditChanges({ Name: 'Old', Hidden: 'Retained', Removed: 'X' },
		{ Name: 'New', Hidden: 'Retained', Added: 'Y' }), [
		{ key: 'Name', before: 'Old', after: 'New' },
		{ key: 'Removed', before: 'X', after: null },
		{ key: 'Added', before: null, after: 'Y' },
	]);
});

test('audit changes handle creation, deletion and events without captured values', () => {
	assert.deepEqual(masterAuditChanges(null, { Reason: 'Damaged' }), [{ key: 'Reason', before: null, after: 'Damaged' }]);
	assert.deepEqual(masterAuditChanges({ GodName: 'Stores' }, null), [{ key: 'GodName', before: 'Stores', after: null }]);
	assert.deepEqual(masterAuditChanges(null, null), []);
});
