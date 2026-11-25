import { test } from 'node:test';
import assert from 'node:assert/strict';
import { api, rowsSchema, authenticationSchema, companySelectionSchema, sessionSchema, setApiContext } from '../lib/poc-api.ts';

test('authenticated users may be pending company selection but selected contexts require a revision', () => {
	assert.equal(authenticationSchema.safeParse({ username: 'SA', context: null }).success, true);
	assert.equal(sessionSchema.safeParse({ username: 'SA', company: '26', site: 'HO' }).success, false);
	assert.equal(companySelectionSchema.safeParse({
		username: 'SA', company: null, site: null, contextVersion: null, companies: [{
			company: '25', companyName: 'Example', financialYear: '2021-2022', startDate: '2021-04-01', endDate: '2022-03-31',
			sites: [{ code: 'HO', name: 'Head Office', headOffice: true }], available: true, unavailableReason: null, readOnly: true,
		}],
	}).success, true);
});

test('API requests carry only the explicitly established tab context and clear it for a new login', async () => {
	const originalFetch = globalThis.fetch;
	const requests = [];
	globalThis.fetch = async (path, init) => {
		requests.push({ path, revision: new Headers(init.headers).get('X-ERP-Context') });
		return new Response('[]', { status: 200, headers: { 'Content-Type': 'application/json' } });
	};
	try {
		setApiContext('a'.repeat(32));
		await api('/orders', rowsSchema);
		setApiContext('b'.repeat(32));
		await api('/orders', rowsSchema);
		setApiContext(null);
		await api('/orders', rowsSchema);
		assert.deepEqual(requests.map(request => request.revision), ['a'.repeat(32), 'b'.repeat(32), null]);
	} finally { globalThis.fetch = originalFetch; setApiContext(null); }
});
