import { test } from 'node:test';
import assert from 'node:assert/strict';
import { erpTaskSchema, taskInboxSchema, approvalStateSchema } from '../lib/erp-tasks.ts';

const task = {
	id: 'f726f57b-4671-42b9-b04a-c581134aa129', type: 'SORD_APPROVAL', category: 'Approval',
	title: 'Order approval', body: 'Review the persisted order.', state: 'Open',
	createdAt: '2026-11-20T12:00:00+02:00', createdBy: 'SA', read: false,
	href: '/dashboard/sale-order/DHO%20SORD%20202600000001',
};
test('task links allow only implemented local record targets, never arbitrary navigation', () => {
	assert.equal(erpTaskSchema.parse(task).href, task.href);
	for (const href of ['javascript:alert(1)', 'https://example.com', '//example.com', '/dashboard/modules/Sales', '/dashboard/sale-order/']) {
		assert.equal(erpTaskSchema.safeParse({ ...task, href }).success, false);
	}
	assert.equal(erpTaskSchema.parse({ ...task, category: 'Memo', href: null }).href, null);
});
test('inbox contracts enforce bounded pages and valid per-user unread counters', () => {
	assert.equal(taskInboxSchema.parse({ items: [task], unread: 2, hasMore: false }).items[0].read, false);
	assert.equal(taskInboxSchema.safeParse({ items: Array(26).fill(task), unread: 0, hasMore: true }).success, false);
	assert.equal(taskInboxSchema.safeParse({ items: [], unread: -1, hasMore: false }).success, false);
});
test('approval progress preserves distinct stages, actors, comments and concurrency revision', () => {
	const progress = {
		requestId: task.id, state: 'Pending', version: 'AAAAAAAABwE=', canAct: false,
		stages: [
			{ step: 1, name: 'Sales review', group: 'Sales', state: 'Approved', actor: 'SA', comment: 'Checked', actedAt: task.createdAt },
			{ step: 2, name: 'Finance review', group: 'Finance', state: 'Active', actor: null, comment: null, actedAt: null },
		],
	};
	const parsed = approvalStateSchema.parse(progress);
	assert.equal(parsed.stages[0].actor, 'SA'); assert.equal(parsed.stages[1].state, 'Active');
	assert.equal(parsed.canAct, false); assert.equal(parsed.version, progress.version);
	assert.equal(approvalStateSchema.safeParse({ ...progress, state: 'Unknown' }).success, false);
});
test('reversal is a separate explicit permission and preserves its recorded actor and reason', () => {
	const old = { requestId: null, state: 'NotRequested', version: null, canAct: false, stages: [] };
	assert.equal(approvalStateSchema.parse(old).canReverse, false);
	const reversed = approvalStateSchema.parse({ ...old, state: 'Reversed', reversedBy: 'SA', reversalReason: 'Correction required.' });
	assert.equal(reversed.reversedBy, 'SA'); assert.equal(reversed.reversalReason, 'Correction required.');
	assert.equal(reversed.canAct, false); assert.equal(reversed.canReverse, false);
	assert.equal(approvalStateSchema.parse({ ...old, state: 'Approved', canReverse: true }).canReverse, true);
});
